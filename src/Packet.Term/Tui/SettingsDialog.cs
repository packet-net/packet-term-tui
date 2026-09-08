using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Packet.Term.Tui;

/// <summary>
/// Modal "Settings" dialog — edit MYCALL and the modem endpoint (serial
/// port or KISS-over-TCP host:port). The dialog is purely a form; the
/// caller (MainWindow.PromptSettings) validates the values, persists them
/// via <see cref="AppContext.SaveSettings"/> and applies them at runtime
/// via MainWindow's reconfigure flow — the listener is disposed and
/// rebuilt, the modem is reopened if the endpoint changes, and any active
/// session is dropped.
/// </summary>
/// <remarks>
/// Both transports' fields stay on screen whichever is selected: the
/// dialog is pre-filled with the live endpoint plus the saved value for
/// the other transport, so flipping between serial and TCP doesn't mean
/// retyping the one you flipped away from.
/// </remarks>
internal sealed class SettingsDialog : Dialog
{
    private readonly TextField myCallField;
    private readonly TextField portField;
    private readonly TextField tcpField;
    private readonly OptionSelector transportSelector;

    /// <summary>Whether the user cancelled the dialog. (Hides the
    /// inherited <see cref="Dialog.Canceled"/>; we drive it from our
    /// own OK / Cancel handlers.)</summary>
    public new bool Canceled { get; private set; } = true;

    /// <summary>The MYCALL value the user submitted (or <c>null</c> if cancelled).</summary>
    public string? MyCallResult { get; private set; }

    /// <summary>Which transport the user selected.</summary>
    public TransportKind TransportResult { get; private set; }

    /// <summary>The serial port value the user submitted (or <c>null</c> if cancelled).</summary>
    public string? PortResult { get; private set; }

    /// <summary>The KISS-over-TCP <c>host:port</c> the user submitted (or <c>null</c> if cancelled).</summary>
    public string? TcpResult { get; private set; }

    public SettingsDialog(string initialMyCall, TransportKind initialTransport, string initialPort, string initialTcp)
    {
        Title = "Settings";
        Width = 60;
        Height = 18;

        var myCallLabel = new Label
        {
            X = 1,
            Y = 1,
            Text = "MYCALL (callsign + SSID, e.g. M0LTE-1):",
        };
        myCallField = new TextField
        {
            X = 1,
            Y = 2,
            Width = Dim.Fill(2),
            Text = initialMyCall ?? string.Empty,
        };

        var transportLabel = new Label
        {
            X = 1,
            Y = 4,
            Text = "Modem transport:",
        };
        transportSelector = new OptionSelector
        {
            X = 1,
            Y = 5,
            Orientation = Orientation.Horizontal,
            Labels = new[] { "_Serial", "KISS over T_CP" },
            Value = initialTransport == TransportKind.Tcp ? 1 : 0,
        };

        var portLabel = new Label
        {
            X = 1,
            Y = 7,
            Text = "Serial port (e.g. /dev/ttyUSB0 or COM5):",
        };
        portField = new TextField
        {
            X = 1,
            Y = 8,
            Width = Dim.Fill(2),
            Text = initialPort ?? string.Empty,
        };

        var tcpLabel = new Label
        {
            X = 1,
            Y = 10,
            Text = "KISS over TCP (host:port, e.g. localhost:8001):",
        };
        tcpField = new TextField
        {
            X = 1,
            Y = 11,
            Width = Dim.Fill(2),
            Text = initialTcp ?? string.Empty,
        };

        var note = new Label
        {
            X = 1,
            Y = 13,
            Text = "Changes apply immediately. Active session will be dropped.",
        };

        var ok = new Button
        {
            Text = "_OK",
            IsDefault = true,
            X = Pos.Center() - 8,
            Y = Pos.AnchorEnd(2),
        };
        ok.Accepting += (_, e) =>
        {
            e.Handled = true;
            Canceled = false;
            MyCallResult = myCallField.Text;
            TransportResult = transportSelector.Value == 1 ? TransportKind.Tcp : TransportKind.Serial;
            PortResult = portField.Text;
            TcpResult = tcpField.Text;
            App?.RequestStop(this);
        };

        var cancel = new Button
        {
            Text = "_Cancel",
            X = Pos.Center() + 2,
            Y = Pos.AnchorEnd(2),
        };
        cancel.Accepting += (_, e) =>
        {
            e.Handled = true;
            Canceled = true;
            App?.RequestStop(this);
        };

        Add(myCallLabel, myCallField, transportLabel, transportSelector,
            portLabel, portField, tcpLabel, tcpField, note, ok, cancel);

        KeyDown += (_, e) =>
        {
            if (e == Key.Esc)
            {
                Canceled = true;
                App?.RequestStop(this);
                e.Handled = true;
            }
        };
    }
}
