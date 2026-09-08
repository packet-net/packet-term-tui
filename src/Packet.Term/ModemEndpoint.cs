using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Packet.Ax25.Transport;
using Packet.Kiss;
using Packet.Kiss.Serial;

namespace Packet.Term;

/// <summary>How Packet.Term reaches its KISS modem.</summary>
public enum TransportKind
{
    /// <summary>KISS over a local (USB) serial port.</summary>
    Serial,

    /// <summary>KISS over a TCP socket — a TNC or node (LinBPQ, Direwolf, net-sim) exposing a KISS listener.</summary>
    Tcp,
}

/// <summary>
/// Where the modem lives: either a serial port name or a
/// <c>host:port</c> KISS-over-TCP endpoint. One value object covers both,
/// so the boot flow, the settings file, the status bar and the
/// hot-swap path all pass a single thing around instead of a port string
/// plus a flag.
/// </summary>
/// <remarks>
/// <see cref="Value"/> is the round-trip form — the serial port name
/// (<c>/dev/ttyUSB0</c>, <c>COM5</c>) or the endpoint as typed
/// (<c>localhost:8001</c>). Value equality (record semantics) is what
/// MainWindow's reconfigure path uses to decide whether the modem needs
/// reopening.
/// </remarks>
public sealed record ModemEndpoint
{
    private ModemEndpoint(TransportKind kind, string value)
    {
        Kind = kind;
        Value = value;
    }

    /// <summary>Which transport this endpoint names.</summary>
    public TransportKind Kind { get; }

    /// <summary>Serial port name, or <c>host:port</c> for <see cref="TransportKind.Tcp"/>.</summary>
    public string Value { get; }

    /// <summary>A serial-port endpoint. No validation — the OS decides at open time.</summary>
    public static ModemEndpoint ForSerial(string portName)
    {
        ArgumentNullException.ThrowIfNull(portName);
        return new ModemEndpoint(TransportKind.Serial, portName.Trim());
    }

    /// <summary>
    /// Parse a <c>host:port</c> KISS-over-TCP endpoint. Returns
    /// <c>false</c> with a user-facing <paramref name="error"/> rather
    /// than throwing — every caller (CLI, boot prompt, settings dialog)
    /// wants to show the message, not catch.
    /// </summary>
    public static bool TryParseTcp(
        string? raw,
        [NotNullWhen(true)] out ModemEndpoint? endpoint,
        [NotNullWhen(false)] out string? error)
    {
        endpoint = null;
        var s = (raw ?? string.Empty).Trim();
        if (s.Length == 0)
        {
            error = "empty endpoint — expected host:port, e.g. localhost:8001";
            return false;
        }
        if (!TrySplitHostPort(s, out _, out _))
        {
            error = $"expected host:port with a port in 1..65535, got \"{s}\"";
            return false;
        }

        endpoint = new ModemEndpoint(TransportKind.Tcp, s);
        error = null;
        return true;
    }

    /// <summary>
    /// Whether <paramref name="raw"/> reads as a <c>host:port</c> pair.
    /// Used by the boot prompt to tell "typed an endpoint" from "typed a
    /// serial path" — neither <c>/dev/ttyUSB0</c> nor <c>COM5</c> has a
    /// colon followed by a port number.
    /// </summary>
    public static bool LooksLikeTcp(string? raw)
        => TrySplitHostPort((raw ?? string.Empty).Trim(), out _, out _);

    /// <summary>Long form for the window title and boot banner.</summary>
    public string Description => Kind == TransportKind.Tcp
        ? $"KISS/TCP {Value}"
        : FormattableString.Invariant($"port {Value} @ {KissSerialModem.DefaultBaudRate}");

    /// <summary>Short form for the status bar.</summary>
    public string StatusLabel => Kind == TransportKind.Tcp ? $"tcp {Value}" : Value;

    /// <summary>
    /// Open the modem. Serial opens synchronously (the library's own API);
    /// TCP dials the listener and is cancellable.
    /// </summary>
    public async Task<IAx25Transport> OpenAsync(CancellationToken cancellationToken)
    {
        if (Kind == TransportKind.Tcp)
        {
            if (!TrySplitHostPort(Value, out var host, out var port))
            {
                throw new InvalidOperationException($"invalid KISS-over-TCP endpoint: {Value}");
            }
            return await KissTcpClient.ConnectAsync(host, port, cancellationToken).ConfigureAwait(false);
        }

        return KissSerialModem.Open(Value);
    }

    // "host:port", splitting on the LAST colon so a bracketed IPv6 literal
    // ("[::1]:8001") keeps its address intact. The brackets are stripped
    // from the host here — Socket/Dns want "::1", not "[::1]".
    private static bool TrySplitHostPort(string s, [NotNullWhen(true)] out string? host, out int port)
    {
        host = null;
        port = 0;

        int colon = s.LastIndexOf(':');
        if (colon <= 0 || colon == s.Length - 1) return false;

        var h = s[..colon];
        if (h.Length >= 2 && h[0] == '[' && h[^1] == ']') h = h[1..^1];
        if (h.Length == 0) return false;

        if (!int.TryParse(s[(colon + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out port)
            || port is < 1 or > 65535)
        {
            return false;
        }

        host = h;
        return true;
    }
}

/// <summary>
/// Disposal helper for the neutral transport seam.
/// <see cref="IAx25Transport"/> itself is neither
/// <see cref="IDisposable"/> nor <see cref="IAsyncDisposable"/>, but every
/// concrete modem is one or both — so closing one means pattern-matching,
/// which we'd otherwise repeat at each of the three teardown sites.
/// </summary>
internal static class Ax25TransportExtensions
{
    /// <summary>
    /// Close the transport, preferring the synchronous path. Best-effort:
    /// this runs on teardown paths where a throwing close must not take
    /// the app (or the reconfigure) down with it.
    /// </summary>
    public static void CloseQuietly(this IAx25Transport transport)
    {
        try
        {
            switch (transport)
            {
                case IDisposable d:
                    d.Dispose();
                    break;
                case IAsyncDisposable ad:
                    ad.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(2), CancellationToken.None);
                    break;
            }
        }
        catch
        {
            // swallowed — teardown is best-effort
        }
    }
}
