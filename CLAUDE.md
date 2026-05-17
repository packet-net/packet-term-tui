# CLAUDE.md

Operating notes for Claude Code (and other agents) working in `m0lte/packet-term-tui`.

## What this repo is

`Packet.Term` — the .NET Terminal.Gui v2 TUI for AX.25 connected-mode sessions over a USB KISS modem. Single-purpose desktop app, talks to one modem, drives one session at a time. Built on the [Packet.NET libraries](https://github.com/m0lte/packet.net) (`Packet.Core`, `Packet.Ax25`, `Packet.Kiss`) consumed as NuGet packages.

Extracted from `m0lte/packet.net` on 2026-05-17 with `git filter-repo --path src/Packet.Term/ --path tests/Packet.Term.Tests/ --path LICENSE --path Directory.Build.props --path Directory.Packages.props --path global.json --path .gitignore` so it can have its own release cadence + issue tracker.

## Common commands

```sh
# Build everything
dotnet build

# Run unit tests
dotnet test

# Run the TUI locally (needs a real / fake serial port)
dotnet run --project src/Packet.Term -- --mycall M0LTE-1 --port /dev/ttyUSB0
```

## Layout

```
src/Packet.Term/                    app source
  Program.cs                        entry point — CLI parse + boot + handoff to PacketTermApp
  AppContext.cs / AppSettings.cs    process-wide settings (JSON-persisted)
  AppInfo.cs                        version constants
  CommandLineOptions.cs             CLI parse types
  FrameFormatter.cs                 BPQ-style frame trace line builder
  KissSerialModem.cs                KISS-over-USB-serial transport
  RingBuffer.cs                     per-pane scroll-back
  SessionRunner.cs                  Ax25Listener wrapper (one-session-at-a-time policy)
  Tui/
    PacketTermApp.cs                Terminal.Gui v2 lifecycle shim
    MainWindow.cs                   menu / monitor / chat / input / status bar
    ConnectDialog.cs                modal connect form
    SettingsDialog.cs               modal MYCALL / port form (hot-swap)
    TuiSchemes.cs                   colour scheme registration

tests/Packet.Term.Tests/            library-agnostic unit tests
```

## Hard rules

### Library boundary

`SessionRunner.cs` / `FrameFormatter.cs` / `KissSerialModem.cs` consume the published Packet.NET libraries. Don't reimplement what those libraries already provide — `Ax25Listener`, the SDL session machine, KISS framing, the AX.25 frame codec, the per-peer session cache. If you find yourself wanting to, the right move is to upstream a change to `m0lte/packet.net` instead.

### Self-hosted runners

Every workflow job MUST target `[self-hosted, Linux, X64]`. No GitHub-hosted-runner budget. Same rule as the other repos in this constellation. The runner registered against this repo runs jobs for it; if CI sits queued for >5 minutes, check that the runner is online before assuming anything else is wrong.

### Repo is private

`m0lte/packet-term-tui` is private (not because the code is sensitive — it's MIT-licensed, runs against an MIT-licensed library stack — but because self-hosted runners + public repo = fork-PR attack surface, and we haven't decided on the long-term runner story yet). Don't flip to public without checking with Tom.

## Things to avoid

- Don't add a direct dependency on `Packet.Ax25.Sdl` — it's pulled transitively via `Packet.Ax25` (NuGet from `m0lte/ax25sdl`). The TUI doesn't `using` any Sdl types; the library hides them.
- Don't extend `SessionRunner` with multi-session support. The TUI is single-session by design. The library *supports* multi-session via `Ax25Listener.SessionAccepted`; a future per-session-tabs version of the TUI would build on that, but not in this codebase yet.
- Don't break the boot flow's `Console.ReadLine` prompts. They run BEFORE `Terminal.Gui` takes the screen — flipping them to TUI dialogs would mean the user can't see modem-open errors at startup (TUI is already swallowing the screen by then).
- Don't add features that need network / web servers / databases. This is a single-binary desktop app talking to a modem. The packet *node* (which does those things) lives elsewhere in `m0lte/packet.net`.

## When in doubt

Ask Tom. For library-shaped concerns, the right place to raise them is `m0lte/packet.net` (the libraries' source repo), not here.
