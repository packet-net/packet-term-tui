using System.IO.Ports;
using CommandLine;
using Packet.Ax25.Transport;
using Packet.Core;
using Packet.Term.Tui;

namespace Packet.Term;

/// <summary>
/// Entry point. Parses CLI, resolves MYCALL + modem endpoint — serial port
/// or KISS-over-TCP — (CLI override → settings → interactive prompt), opens
/// the modem, hands off to the Terminal.Gui v2 app shell in
/// <see cref="PacketTermApp"/>.
/// </summary>
/// <remarks>
/// All boot-time prompts happen here via plain
/// <see cref="Console.ReadLine"/> — before the TUI takes over the screen.
/// Once the modem is open, control transfers to <see cref="PacketTermApp.Run"/>,
/// which owns the entire screen until the user exits.
/// </remarks>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Contains("--version") || args.Contains("-V"))
        {
            Console.WriteLine(AppInfo.VersionReport());
            return 0;
        }

        var parsed = Parser.Default.ParseArguments<CommandLineOptions>(args);
        if (parsed is not CommandLine.Parsed<CommandLineOptions> ok)
        {
            return 1;
        }
        var opts = ok.Value;

        if (!string.IsNullOrWhiteSpace(opts.Port) && !string.IsNullOrWhiteSpace(opts.Tcp))
        {
            Console.Error.WriteLine("--port and --tcp are mutually exclusive — the modem is on one or the other.");
            return 2;
        }

        AppContext.Load();

        // When MYCALL and a modem endpoint are both supplied via CLI, treat
        // this run as ephemeral — disable settings persistence so two
        // parallel instances started with their own --mycall and
        // --port/--tcp don't race on the shared settings.json. The
        // Connect-target history and any Settings-dialog changes during
        // this run won't survive to disk, which is the right trade-off when
        // the CLI is the source of truth for identity + modem.
        var endpointFromCli = !string.IsNullOrWhiteSpace(opts.Port) || !string.IsNullOrWhiteSpace(opts.Tcp);
        if (!string.IsNullOrWhiteSpace(opts.MyCall) && endpointFromCli)
        {
            AppContext.PersistenceEnabled = false;
            Console.WriteLine("Packet.Term: --mycall + --port/--tcp both provided, settings persistence disabled for this run.");
        }

        // Resolve MYCALL: --mycall > settings > prompt.
        var myCallStr = opts.MyCall ?? AppContext.Settings.MyCall;
        if (string.IsNullOrWhiteSpace(myCallStr))
        {
            Console.Write("MYCALL (your callsign + SSID, e.g. M0LTE-1): ");
            myCallStr = Console.ReadLine()?.Trim();
            if (string.IsNullOrWhiteSpace(myCallStr))
            {
                Console.Error.WriteLine("MYCALL is required.");
                return 2;
            }
            AppContext.Settings.MyCall = myCallStr;
            AppContext.SaveSettings();
        }
        if (!Callsign.TryParse(myCallStr, out var myCall))
        {
            Console.Error.WriteLine($"Invalid MYCALL: {myCallStr}");
            return 2;
        }

        // Resolve the modem endpoint: --tcp / --port > settings > prompt.
        ModemEndpoint? endpoint;
        if (!string.IsNullOrWhiteSpace(opts.Tcp))
        {
            if (!ModemEndpoint.TryParseTcp(opts.Tcp, out endpoint, out var tcpError))
            {
                Console.Error.WriteLine($"Invalid --tcp endpoint: {tcpError}");
                return 2;
            }
        }
        else if (!string.IsNullOrWhiteSpace(opts.Port))
        {
            endpoint = ModemEndpoint.ForSerial(opts.Port);
        }
        else
        {
            endpoint = EndpointFromSettings(AppContext.Settings);
        }

        if (endpoint is null)
        {
            endpoint = ChooseEndpoint();
            if (endpoint is null)
            {
                Console.Error.WriteLine(
                    "No modem chosen. Re-run with --port /path/to/port (serial) or --tcp host:port (KISS over TCP).");
                return 3;
            }
            ApplyEndpointToSettings(AppContext.Settings, endpoint);
            AppContext.SaveSettings();
        }

        // --connect: validate up-front and bail out fast on a typo, before
        // we open the modem.
        Callsign? autoConnect = null;
        if (!string.IsNullOrWhiteSpace(opts.Connect))
        {
            if (!Callsign.TryParse(opts.Connect, out var ac))
            {
                Console.Error.WriteLine($"Invalid --connect callsign: {opts.Connect}");
                return 4;
            }
            autoConnect = ac;
        }

        IAx25Transport modem;
        try
        {
            modem = await endpoint.OpenAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to open {endpoint.Description}: {ex.Message}");
            return 5;
        }

        Console.WriteLine($"Packet.Term {AppInfo.Version}  MYCALL={myCall}  {endpoint.Description}");
        Console.WriteLine("Starting TUI...");

        try
        {
            // MainWindow takes ownership of the modem now — it may swap
            // to a different endpoint at runtime via the Settings dialog.
            // Don't wrap with `using` here; MainWindow.Dispose handles it.
            PacketTermApp.Run(myCall, endpoint, modem, autoConnect);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Packet.Term aborted: {ex.Message}");
            return 6;
        }

        Console.WriteLine("Goodbye.");
        return 0;
    }

    /// <summary>
    /// The saved modem endpoint, or <c>null</c> when the settings file
    /// doesn't name one yet (first run, or a TCP-mode file whose endpoint
    /// was cleared by hand).
    /// </summary>
    internal static ModemEndpoint? EndpointFromSettings(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (settings.Transport == TransportKind.Tcp)
        {
            // A saved endpoint is trusted to be well-formed; if it isn't
            // (hand-edited file), fall through to the prompt rather than
            // failing the boot with a parse error.
            return ModemEndpoint.TryParseTcp(settings.TcpEndpoint, out var tcp, out _) ? tcp : null;
        }

        return string.IsNullOrWhiteSpace(settings.SerialPort)
            ? null
            : ModemEndpoint.ForSerial(settings.SerialPort);
    }

    /// <summary>
    /// Write <paramref name="endpoint"/> into <paramref name="settings"/>.
    /// The other transport's value is left alone — switching to TCP and back
    /// keeps the serial port you last used.
    /// </summary>
    internal static void ApplyEndpointToSettings(AppSettings settings, ModemEndpoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(endpoint);

        settings.Transport = endpoint.Kind;
        if (endpoint.Kind == TransportKind.Tcp)
        {
            settings.TcpEndpoint = endpoint.Value;
        }
        else
        {
            settings.SerialPort = endpoint.Value;
        }
    }

    private static ModemEndpoint? ChooseEndpoint()
    {
        var ports = SerialPort.GetPortNames();
        Array.Sort(ports, StringComparer.Ordinal);

        if (ports.Length == 0)
        {
            Console.WriteLine("No serial ports found.");
            Console.Write("Type a KISS-over-TCP endpoint (host:port), or a serial port path: ");
        }
        else
        {
            Console.WriteLine("Available serial ports:");
            for (int i = 0; i < ports.Length; i++)
            {
                Console.WriteLine($"  [{i + 1}] {ports[i]}");
            }
            Console.Write($"Pick one [1-{ports.Length}], a serial path, or host:port for KISS over TCP: ");
        }

        var raw = Console.ReadLine()?.Trim();
        if (string.IsNullOrEmpty(raw)) return null;
        if (int.TryParse(raw, out var idx) && idx >= 1 && idx <= ports.Length)
        {
            return ModemEndpoint.ForSerial(ports[idx - 1]);
        }
        // "host:port" is unambiguous here — no serial port name (COM5,
        // /dev/ttyUSB0) ends in a colon plus a port number.
        if (ModemEndpoint.LooksLikeTcp(raw) && ModemEndpoint.TryParseTcp(raw, out var tcp, out _))
        {
            return tcp;
        }
        return ModemEndpoint.ForSerial(raw);
    }
}
