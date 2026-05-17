# Packet.Term

A full-window AX.25 terminal application for connected-mode sessions over a KISS-over-USB-serial modem. Turbo Vision / DOS Edit aesthetic, built on **Terminal.Gui v2**.

```text
╔═══════════════════════════════════════════════════════════════════════════╗
║ File  Session  View  Help                                                 ║
╠═══════════════════════════════════════════════════════════════════════════╣
║ ┌─ Frame monitor ───────────────────────────────────────────────────────┐ ║
║ │ 21:13:45 T M0LTE-1>GB7CIP <SABM C P>                                  │ ║
║ │ 21:13:46 R GB7CIP>M0LTE-1 <UA R F>                                    │ ║
║ │ 21:13:50 T M0LTE-1>GB7CIP <I C R0 S0 P> "list"                        │ ║
║ └───────────────────────────────────────────────────────────────────────┘ ║
║ ┌─ Conversation ────────────────────────────────────────────────────────┐ ║
║ │ *** Connected to GB7CIP                                               │ ║
║ │ GB7CIP> Welcome to the BBS                                            │ ║
║ │ me: list                                                              │ ║
║ └───────────────────────────────────────────────────────────────────────┘ ║
║ ┌─ Input ───────────────────────────────────────────────────────────────┐ ║
║ │ > _                                                                   │ ║
║ └───────────────────────────────────────────────────────────────────────┘ ║
╠═══════════════════════════════════════════════════════════════════════════╣
║ M0LTE-1 │ /dev/ttyUSB0 │ CONNECTED to GB7CIP        F2=Conn F3=Disc Esc=Q ║
╚═══════════════════════════════════════════════════════════════════════════╝
```

## Run it

```sh
dotnet run --project src/Packet.Term -- --mycall M0LTE-1 --port /dev/ttyUSB0
```

CLI flags:

| Flag | Purpose |
| --- | --- |
| `--mycall <CALL-SSID>` | Your callsign + SSID. Required (prompted if omitted). |
| `--port <path>` | Serial port path (e.g. `/dev/ttyUSB0`, `COM5`). Prompted if omitted. |
| `--connect <CALL>` | Auto-connect to this callsign once the TUI is up. Optional. |

If `--mycall` AND `--port` are both supplied, the run is treated as ephemeral — settings aren't persisted, so two parallel instances driven by their own flags can run side-by-side without racing on the shared settings file. Useful for connecting two modems on the same host to each other.

## Keyboard

| Key | Action |
| --- | --- |
| `F2` | Connect... (modal callsign prompt) |
| `F3` | Disconnect |
| `Esc` | Quit (also `Ctrl-Q` or `File → Exit`) |
| `F10` | Open menu bar |
| `Ctrl-S` | Settings... (hot-swap MYCALL / port) |

While connected, typing in the input line and pressing Enter sends one I-frame.

## What it's built on

Downstream consumer of the [Packet.NET libraries](https://github.com/m0lte/packet.net):

- `Packet.Core` — shared primitives (Callsign, Ax25Address, KissFrame).
- `Packet.Ax25` — AX.25 v2.2 frame codec + connected-mode session machine + `Ax25Listener` (inbound + outbound). Pulls `Packet.Ax25.Sdl` transitively from [m0lte/ax25sdl](https://github.com/m0lte/ax25sdl).
- `Packet.Kiss` — KISS framing + ACKMODE + TCP transport.
- `Terminal.Gui` v2 — Turbo Vision-style TUI framework.

This repo was extracted from `m0lte/packet.net` on 2026-05-17 with history preserved via `git filter-repo`.

## License

[MIT](LICENSE).
