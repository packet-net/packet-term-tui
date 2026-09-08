using System.Globalization;
using System.Text;
using Packet.Ax25;
using Packet.Ax25.Session;
using Packet.Ax25.Transport;
using Packet.Core;
using Terminal.Gui.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Packet.Term.Tui;

/// <summary>
/// The Terminal.Gui v2 main window — a full-screen Turbo-Vision-style
/// shell with a top <see cref="MenuBar"/>, three stacked sub-views
/// (frame monitor → conversation → input), and a bottom
/// <see cref="StatusBar"/>. Owns the <see cref="SessionRunner"/> for the
/// lifetime of the TUI.
/// </summary>
/// <remarks>
/// All cross-thread updates from <see cref="SessionRunner"/> callbacks
/// (chat lines, link-state, frame trace) must marshal back onto the
/// Terminal.Gui main thread before touching any view. The
/// <see cref="IApplication.Invoke(Action)"/> shim is the documented v2
/// path for this — it posts to the iteration loop.
///
/// Input policy: the bottom <see cref="TextField"/> is the focus target
/// while connected. Disabling (CanFocus=false + ReadOnly=true) when
/// disconnected stops typing from being silently swallowed; the menu
/// remains keyboard-driven via its hotkeys regardless.
/// </remarks>
internal sealed class MainWindow : Window
{
    private const int FrameLogCapacity = 200;
    private const int ChatLogCapacity = 200;
    private const int InputHistoryCapacity = 100;

    private readonly IApplication app;
    // myCall / endpoint / modem / runner are mutable so the Settings dialog
    // can hot-swap them at runtime — see ReconfigureAsync.
    private Callsign myCall;
    private ModemEndpoint endpoint;
    private IAx25Transport modem;
    private SessionRunner runner;
    // Not `readonly`: the View → "Clear ..." menu commands swap in a fresh
    // buffer rather than touching RingBuffer's internals (kept as-is per
    // the brief).
    private RingBuffer frameLog = new(FrameLogCapacity);
    private RingBuffer chatLog = new(ChatLogCapacity);

    private readonly TextView monitorView;
    private readonly TextView chatView;
    private readonly TextField inputField;
    private readonly Shortcut statusIdentity;
    private readonly Shortcut statusPort;
    private readonly Shortcut statusLink;
    private readonly MenuItem acceptIncomingMenuItem;

    // Sent-line history for the input field, oldest first. historyIndex ==
    // inputHistory.Count means "the line currently being typed", which is
    // where the cursor sits until Up walks back into the history;
    // historyDraft parks that half-typed line so Down can return to it.
    private readonly List<string> inputHistory = [];
    private int historyIndex;
    private string historyDraft = string.Empty;

    private LinkState linkState = LinkState.Disconnected;
    private Callsign? remote;
    private CancellationTokenSource? runnerCts;
    private Callsign? pendingAutoConnect;
    private bool acceptIncoming = true;
    private bool disposed;

    public MainWindow(IApplication app, Callsign myCall, ModemEndpoint endpoint, IAx25Transport modem)
    {
        this.app = app ?? throw new ArgumentNullException(nameof(app));
        this.myCall = myCall;
        this.endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
        this.modem = modem ?? throw new ArgumentNullException(nameof(modem));

        Title = $"Packet.Term {AppInfo.Version}  —  MYCALL {FormatCallsign(myCall)}  {endpoint.Description}";
        BorderStyle = LineStyle.None;

        // ─── MenuBar ──────────────────────────────────────────────────
        var menuBar = BuildMenuBar(out acceptIncomingMenuItem);

        // ─── Frame monitor (top ~40%) ─────────────────────────────────
        var monitorFrame = new FrameView
        {
            Title = "Frame monitor",
            X = 0,
            Y = Pos.Bottom(menuBar),
            Width = Dim.Fill(),
            Height = Dim.Percent(40),
        };
        monitorView = new TextView
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            ReadOnly = true,
            Multiline = true,
            // Wrapped, not clipped: neither pane can be focused, so there
            // is no way to scroll sideways to reach whatever a clipped
            // line hid. Trace headers are short enough that only long
            // payload rows wrap.
            WordWrap = true,
            CanFocus = false,
        };
        monitorView.SchemeName = TuiSchemes.Monitor;
        monitorFrame.Add(monitorView);

        // ─── Conversation (middle, fills above the input + status) ────
        var chatFrame = new FrameView
        {
            Title = "Conversation",
            X = 0,
            Y = Pos.Bottom(monitorFrame),
            Width = Dim.Fill(),
            // 1 for input row + 1 for status bar — Dim.Fill takes the
            // remaining lines minus that fixed footer.
            Height = Dim.Fill(2),
        };
        chatView = new TextView
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            ReadOnly = true,
            Multiline = true,
            WordWrap = true,
            CanFocus = false,
        };
        chatView.SchemeName = TuiSchemes.Chat;
        chatFrame.Add(chatView);

        // ─── Input line (one row above status) ────────────────────────
        inputField = new TextField
        {
            X = 0,
            Y = Pos.AnchorEnd(2),
            Width = Dim.Fill(),
            Height = 1,
            ReadOnly = true,
            CanFocus = false,
        };
        inputField.SchemeName = TuiSchemes.Input;
        inputField.Accepting += OnInputAccepting;
        inputField.KeyDown += OnInputKeyDown;

        // ─── StatusBar (bottom row) ───────────────────────────────────
        // Identity + port are stored as fields so the Settings-dialog
        // hot-swap path can mutate their Title without rebuilding the bar.
        // F10 is intentionally NOT bound here for Quit — Terminal.Gui v2
        // hardcodes F10 as the MenuBar activator (the Turbo Vision idiom),
        // so a status-bar Shortcut on F10 is shadowed by the framework
        // and never fires. Esc reaches the user reliably from any focus.
        statusIdentity = new Shortcut(Key.Empty, FormatCallsign(myCall), null);
        statusPort = new Shortcut(Key.Empty, endpoint.StatusLabel, null);
        statusLink = new Shortcut(Key.Empty, "DISCONNECTED", null);
        var statusConnect = new Shortcut(Key.F2, "Conn", () => PromptConnect());
        var statusDisconnect = new Shortcut(Key.F3, "Disc", () => InitiateDisconnect());
        var statusQuit = new Shortcut(Key.Esc, "Quit", () => app.RequestStop());

        var statusBar = new StatusBar(new[]
        {
            statusIdentity, statusPort, statusLink,
            statusConnect, statusDisconnect, statusQuit,
        })
        {
            X = 0,
            Y = Pos.AnchorEnd(1),
            Width = Dim.Fill(),
            Height = 1,
        };
        statusBar.SchemeName = TuiSchemes.Status;

        Add(menuBar, monitorFrame, chatFrame, inputField, statusBar);

        // ─── SessionRunner wiring ─────────────────────────────────────
        runner = new SessionRunner(modem, myCall, OnRunnerChatLine, OnRunnerChatText, OnRunnerLinkStateChanged);
        runner.FrameTraced += OnRunnerFrameTraced;

        // Defer the pump start + autoconnect until the view is initialised,
        // so any chat lines emitted before the first paint don't race the
        // first draw.
        Initialized += OnInitialized;
    }

    private MenuBar BuildMenuBar(out MenuItem acceptIncomingItem)
    {
        // The MenuItem "Accept incoming" entry needs to be reachable from
        // its click handler to flip its own title between "[on]" and
        // "[off]" — captured by reference and exposed to the caller.
        var settingsItem = new MenuItem("_Settings...", "", PromptSettings)
        {
            Key = Key.S.WithCtrl,
        };
        var exitItem = new MenuItem("E_xit", "", () => app.RequestStop())
        {
            Key = Key.Q.WithCtrl,
        };

        var connectItem = new MenuItem("_Connect...", "", PromptConnect)
        {
            Key = Key.F2,
        };
        var disconnectItem = new MenuItem("_Disconnect", "", InitiateDisconnect)
        {
            Key = Key.F3,
        };
        acceptIncomingItem = new MenuItem("_Accept incoming  [on]", "", ToggleAcceptIncoming);

        var clearMonitorItem = new MenuItem("Clear _frame monitor", "", () =>
        {
            frameLog = new RingBuffer(FrameLogCapacity);
            monitorView.Text = string.Empty;
        });
        var clearChatItem = new MenuItem("Clear _conversation", "", () =>
        {
            chatLog = new RingBuffer(ChatLogCapacity);
            chatView.Text = string.Empty;
        });

        var aboutItem = new MenuItem("_About...", "", ShowAbout);

        return new MenuBar
        {
            Menus = new[]
            {
                new MenuBarItem("_File", new[] { settingsItem, exitItem }),
                new MenuBarItem("_Session", new[] { connectItem, disconnectItem, acceptIncomingItem }),
                new MenuBarItem("_View", new[] { clearMonitorItem, clearChatItem }),
                new MenuBarItem("_Help", new[] { aboutItem }),
            },
        };
    }

    /// <summary>
    /// Park an auto-connect target. The connect is kicked off once the
    /// view is initialised and the listener pump is running — both
    /// happen during <see cref="OnInitialized"/>.
    /// </summary>
    public void AttachAutoConnect(Callsign? target) => pendingAutoConnect = target;

    // ─── Lifecycle ────────────────────────────────────────────────────

    private void OnInitialized(object? sender, EventArgs e)
    {
        // Start the listener pump in the background. The pump's writes to
        // chatLog/frameLog land on its own task; OnRunner* handlers
        // marshal back via app.Invoke before touching the view.
        runnerCts = new CancellationTokenSource();
        _ = runner.Start(runnerCts.Token);

        if (pendingAutoConnect is { } target)
        {
            _ = Task.Run(async () =>
            {
                // Tiny settle so the first paint completes before we add
                // chat noise; matches the old Spectre behaviour.
                await Task.Delay(200).ConfigureAwait(false);
                await DoConnectAsync(target).ConfigureAwait(false);
            });
        }
    }

    // ─── Menu / status handlers ───────────────────────────────────────

    private void PromptConnect()
    {
        if (linkState != LinkState.Disconnected)
        {
            MessageBox.ErrorQuery(app, "Already connected",
                "Disconnect the active session before opening another one.", "OK");
            return;
        }

        var dialog = new ConnectDialog(AppContext.LastConnectTarget ?? string.Empty);
        app.Run(dialog);
        if (dialog.Canceled || dialog.Result is null)
        {
            return;
        }

        var raw = dialog.Result;
        if (!Callsign.TryParse(raw, out var target))
        {
            MessageBox.ErrorQuery(app, "Invalid callsign",
                $"\"{raw}\" doesn't parse as an AX.25 callsign.\nUse e.g. M0LTE-1 or G1AAA-0.", "OK");
            return;
        }

        AppContext.LastConnectTarget = target.ToString();
        AppContext.SaveSettings();

        _ = Task.Run(() => DoConnectAsync(target));
    }

    private async Task DoConnectAsync(Callsign target)
    {
        AppendChat($"*** Connecting to {FormatCallsign(target)}...");
        var outcome = await runner.ConnectAsync(target, TimeSpan.FromSeconds(30), CancellationToken.None)
            .ConfigureAwait(false);
        if (outcome is null)
        {
            AppendChat("*** Connect timed out");
        }
        else if (outcome.Name == "DL_CONNECT_confirm")
        {
            AppendChat($"*** Connected to {FormatCallsign(target)}");
        }
        else
        {
            AppendChat("*** Connect refused / link torn down before UA arrived");
        }
    }

    private void InitiateDisconnect()
    {
        if (linkState != LinkState.Connected && linkState != LinkState.Connecting)
        {
            return;
        }
        _ = Task.Run(() => runner.DisconnectAsync(TimeSpan.FromSeconds(15), CancellationToken.None));
    }

    private void ToggleAcceptIncoming()
    {
        acceptIncoming = !acceptIncoming;
        acceptIncomingMenuItem.Title = acceptIncoming
            ? "_Accept incoming  [on]"
            : "_Accept incoming  [off]";
        // SessionRunner sets AcceptIncoming=false while a session is
        // active; this toggle is the user-facing default for idle state.
        if (linkState == LinkState.Disconnected)
        {
            runner.AcceptIncoming = acceptIncoming;
        }
    }

    private void PromptSettings()
    {
        // Pre-fill with the LIVE config (mutable myCall / endpoint) rather
        // than the saved settings — the user expects "edit what's currently
        // running", not "edit what's persisted". The transport the user
        // isn't on is pre-filled from the saved settings, so switching to
        // TCP and back doesn't mean retyping the port.
        var dialog = new SettingsDialog(
            FormatCallsign(myCall),
            endpoint.Kind,
            endpoint.Kind == TransportKind.Serial ? endpoint.Value : AppContext.Settings.SerialPort ?? string.Empty,
            endpoint.Kind == TransportKind.Tcp ? endpoint.Value : AppContext.Settings.TcpEndpoint ?? string.Empty);
        app.Run(dialog);
        if (dialog.Canceled)
        {
            return;
        }

        var newCallStr = dialog.MyCallResult ?? string.Empty;
        if (string.IsNullOrWhiteSpace(newCallStr))
        {
            MessageBox.ErrorQuery(app, "Settings incomplete", "MYCALL must be set.", "OK");
            return;
        }
        if (!Callsign.TryParse(newCallStr, out var newCall))
        {
            MessageBox.ErrorQuery(app, "Invalid callsign",
                $"\"{newCallStr}\" doesn't parse. Settings unchanged.", "OK");
            return;
        }

        ModemEndpoint newEndpoint;
        if (dialog.TransportResult == TransportKind.Tcp)
        {
            if (!ModemEndpoint.TryParseTcp(dialog.TcpResult, out var tcp, out var tcpError))
            {
                MessageBox.ErrorQuery(app, "Invalid TCP endpoint",
                    $"{tcpError}\n\nSettings unchanged.", "OK");
                return;
            }
            newEndpoint = tcp;
        }
        else
        {
            var newPortStr = (dialog.PortResult ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(newPortStr))
            {
                MessageBox.ErrorQuery(app, "Settings incomplete",
                    "A serial port must be set when the transport is Serial.", "OK");
                return;
            }
            newEndpoint = ModemEndpoint.ForSerial(newPortStr);
        }

        // Update the persisted settings (no-op if PersistenceEnabled=false,
        // i.e. CLI-driven instances).
        AppContext.Settings.MyCall = newCallStr;
        Program.ApplyEndpointToSettings(AppContext.Settings, newEndpoint);
        AppContext.SaveSettings();

        // Hot-swap. Runs on a background task so the UI thread stays
        // responsive; ReconfigureAsync marshals UI updates back via
        // app.Invoke.
        _ = Task.Run(() => ReconfigureAsync(newEndpoint, newCall));
    }

    /// <summary>
    /// Apply a live MYCALL / modem change without restarting the process.
    /// Disconnects any active session, disposes the runner (and the modem
    /// if the endpoint is changing), reopens the modem on the new endpoint
    /// if needed, builds a fresh runner with the new MYCALL, and resumes
    /// pumping. UI status bar + window title refresh once the new pump
    /// is live.
    /// </summary>
    private async Task ReconfigureAsync(ModemEndpoint newEndpoint, Callsign newMyCall)
    {
        var portChanged = newEndpoint != endpoint;
        var callChanged = !newMyCall.Equals(myCall);
        if (!portChanged && !callChanged)
        {
            app.Invoke(() => AppendChat("*** Settings unchanged."));
            return;
        }

        app.Invoke(() => AppendChat(
            $"*** Reconfiguring: MYCALL={FormatCallsign(newMyCall)} {newEndpoint.Description} ..."));

        // 1. Disconnect active session (best-effort; we proceed regardless).
        if (linkState != LinkState.Disconnected)
        {
            try
            {
                await runner.DisconnectAsync(TimeSpan.FromSeconds(5), CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch
            {
                // proceed anyway — the runner is about to be disposed
            }
        }

        // 2. Open the new modem FIRST (if the endpoint is changing). Doing
        //    this before disposing the current modem means a failed open
        //    leaves us with a working configuration to fall back to.
        IAx25Transport newModem = modem;
        if (portChanged)
        {
            try
            {
                newModem = await newEndpoint.OpenAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                app.Invoke(() => MessageBox.ErrorQuery(app, "Modem open failed",
                    $"Couldn't open {newEndpoint.Description}: {ex.Message}\n\nKeeping current configuration.",
                    "OK"));
                return;
            }
        }

        // 3. Stop + dispose old runner. (Always — the listener is bound
        //    to a specific MyCall at construction, so a call change alone
        //    still requires a rebuild.)
        try { runnerCts?.Cancel(); } catch { /* swallow */ }
        try { runner.Dispose(); } catch { /* swallow */ }
        try { runnerCts?.Dispose(); } catch { /* swallow */ }
        runnerCts = null;

        // 4. Swap modem (if the endpoint changed).
        if (portChanged)
        {
            modem.CloseQuietly();
            modem = newModem;
        }

        myCall = newMyCall;
        endpoint = newEndpoint;

        // 5. Build the new runner + restart the pump.
        runner = new SessionRunner(modem, myCall, OnRunnerChatLine, OnRunnerChatText, OnRunnerLinkStateChanged);
        runner.FrameTraced += OnRunnerFrameTraced;
        runnerCts = new CancellationTokenSource();
        _ = runner.Start(runnerCts.Token);

        // 6. Refresh UI bits the user notices.
        app.Invoke(() =>
        {
            Title = $"Packet.Term {AppInfo.Version}  —  MYCALL {FormatCallsign(myCall)}  {endpoint.Description}";
            statusIdentity.Title = FormatCallsign(myCall);
            statusPort.Title = endpoint.StatusLabel;
            AppendChat($"*** Reconfigured: MYCALL={FormatCallsign(myCall)} {endpoint.Description}");
            // statusbar layout may need a kick if Title widths changed.
            SetNeedsLayout();
        });
    }

    private void ShowAbout()
    {
        var body =
            $"Packet.Term v{AppInfo.Version}\n" +
            "\n" +
            "An AX.25 terminal application for connected-mode sessions\n" +
            "over a KISS modem — USB serial or KISS over TCP.\n" +
            "\n" +
            "Built on @packet-net/ax25 and Terminal.Gui v2.\n" +
            "MIT licence.\n" +
            "\n" +
            "https://github.com/m0lte/packet.net";
        MessageBox.Query(app, "About Packet.Term", body, "OK");
    }

    // ─── SessionRunner callbacks (called on background threads) ───────

    private void OnRunnerChatLine(string line)
    {
        AppendChat(line);
    }

    private void OnRunnerChatText(string text, bool continuesPreviousLine)
    {
        if (continuesPreviousLine)
        {
            AppendChatContinuation(text);
        }
        else
        {
            AppendChat(text);
        }
    }

    private void OnRunnerLinkStateChanged(LinkState state, Callsign? peer)
    {
        app.Invoke(() =>
        {
            linkState = state;
            remote = peer;
            UpdateStatusBar();
            UpdateInputEnabled();
        });
    }

    private void OnRunnerFrameTraced(object? sender, Ax25FrameEventArgs e)
    {
        var direction = e.Direction == Packet.Ax25.Session.FrameDirection.Transmitted
            ? FrameDirection.Transmit
            : FrameDirection.Receive;
        var line = FrameFormatter.Format(direction, e.Frame, DateTimeOffset.Now);
        AppendFrameLine(line);
    }

    // ─── View updaters ────────────────────────────────────────────────

    private void AppendChat(string line)
    {
        var stamped = $"[{DateTimeOffset.Now.LocalDateTime.ToString("HH:mm:ss", CultureInfo.InvariantCulture)}] {line}";
        chatLog.Add(stamped);
        app.Invoke(() =>
        {
            chatView.Text = string.Join("\n", chatLog.Snapshot());
            chatView.MoveEnd();
        });
    }

    // A peer line that was segmented across frames: join the tail onto the
    // row already on screen instead of starting a new one (and don't stamp
    // it again — the timestamp belongs to when the line started).
    private void AppendChatContinuation(string text)
    {
        chatLog.AppendToLast(text);
        app.Invoke(() =>
        {
            chatView.Text = string.Join("\n", chatLog.Snapshot());
            chatView.MoveEnd();
        });
    }

    private void AppendFrameLine(string line)
    {
        foreach (var sub in line.Split('\n'))
        {
            frameLog.Add(sub);
        }
        app.Invoke(() =>
        {
            monitorView.Text = string.Join("\n", frameLog.Snapshot());
            monitorView.MoveEnd();
        });
    }

    private void UpdateStatusBar()
    {
        statusLink.Title = linkState switch
        {
            LinkState.Connected => $"CONNECTED to {FormatCallsign(remote ?? new Callsign("UNK"))}",
            LinkState.Connecting => $"CONNECTING to {FormatCallsign(remote ?? new Callsign("UNK"))}",
            LinkState.Disconnecting => "DISCONNECTING",
            _ => "DISCONNECTED",
        };
    }

    private void UpdateInputEnabled()
    {
        if (linkState == LinkState.Connected)
        {
            inputField.ReadOnly = false;
            inputField.CanFocus = true;
            inputField.SetFocus();
        }
        else
        {
            inputField.ReadOnly = true;
            inputField.CanFocus = false;
            inputField.Text = string.Empty;
        }
    }

    private void OnInputAccepting(object? sender, CommandEventArgs e)
    {
        // 'Accepting' fires when Enter is hit. Consume the event so
        // Terminal.Gui doesn't try to apply its default activate
        // semantics (which would also fire on the menu/status bar).
        e.Handled = true;
        if (linkState != LinkState.Connected) return;

        var text = inputField.Text ?? string.Empty;
        inputField.Text = string.Empty;
        if (string.IsNullOrEmpty(text)) return;

        RememberInput(text);
        AppendChat($"me: {text}");
        var bytes = Encoding.ASCII.GetBytes(text + "\r");
        runner.SendData(bytes);
    }

    // ─── Input history (Up / Down) ────────────────────────────────────

    private void RememberInput(string line)
    {
        // Skip a straight repeat of the previous line — re-sending the
        // same command shouldn't cost two presses of Up to get past.
        if (inputHistory.Count == 0 || !string.Equals(inputHistory[^1], line, StringComparison.Ordinal))
        {
            inputHistory.Add(line);
            if (inputHistory.Count > InputHistoryCapacity)
            {
                inputHistory.RemoveAt(0);
            }
        }
        historyIndex = inputHistory.Count;
        historyDraft = string.Empty;
    }

    private void OnInputKeyDown(object? sender, Key e)
    {
        // Only while the field is live — when disconnected it's read-only
        // and there's nothing to recall into.
        if (inputField.ReadOnly) return;

        if (e == Key.CursorUp)
        {
            RecallHistory(-1);
            e.Handled = true;
        }
        else if (e == Key.CursorDown)
        {
            RecallHistory(+1);
            e.Handled = true;
        }
    }

    private void RecallHistory(int delta)
    {
        if (inputHistory.Count == 0) return;

        // Stepping off the line being typed: keep it, so Down comes back
        // to what the user had half-written.
        if (historyIndex == inputHistory.Count)
        {
            historyDraft = inputField.Text ?? string.Empty;
        }

        var next = Math.Clamp(historyIndex + delta, 0, inputHistory.Count);
        if (next == historyIndex) return;

        historyIndex = next;
        inputField.Text = historyIndex == inputHistory.Count ? historyDraft : inputHistory[historyIndex];
        inputField.MoveEnd();
    }

    // ─── Disposal ─────────────────────────────────────────────────────

    protected override void Dispose(bool disposing)
    {
        if (!disposed && disposing)
        {
            disposed = true;

            // Hang up before tearing anything down. Without a DISC on the
            // wire the peer holds the link open until its own timers give
            // up, and the next Packet.Term run dials into a session the
            // node still believes is live. Bounded, and best-effort: this
            // runs on the way out, after the message loop has stopped, so
            // a peer that won't answer costs a few seconds and no more.
            // Ordering matters — the listener pump has to still be running
            // to collect the UA, so this goes before the cancel below.
            try
            {
                if (runner.State is not LinkState.Disconnected)
                {
                    runner.DisconnectAsync(TimeSpan.FromSeconds(5), CancellationToken.None)
                        .Wait(TimeSpan.FromSeconds(6), CancellationToken.None);
                }
            }
            catch { /* swallowed — we're leaving either way */ }

            try { runnerCts?.Cancel(); } catch { /* swallowed */ }
            try { runner.Dispose(); } catch { /* swallowed */ }
            try { runnerCts?.Dispose(); } catch { /* swallowed */ }
            // MainWindow owns the modem (Program transferred it at
            // construction); dispose it here so the serial port / socket
            // closes.
            modem.CloseQuietly();
        }
        base.Dispose(disposing);
    }

    // ─── Helpers ──────────────────────────────────────────────────────

    private static string FormatCallsign(Callsign c) => c.Ssid == 0 ? c.Base : c.ToString();
}
