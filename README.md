# Packet.Term

A full-window AX.25 terminal application for connected-mode sessions over a KISS modem — USB serial, or KISS over TCP. Turbo Vision / DOS Edit aesthetic, built on **Terminal.Gui v2**.

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
# KISS over USB serial
dotnet run --project src/Packet.Term -- --mycall M0LTE-1 --port /dev/ttyUSB0

# KISS over TCP — a TNC or node exposing a KISS listener (LinBPQ, Direwolf, net-sim)
dotnet run --project src/Packet.Term -- --mycall M0LTE-1 --tcp localhost:8001
```

| Flag | Purpose |
| --- | --- |
| `--mycall <CALL-SSID>` | Your callsign + SSID. Prompted if omitted. |
| `--port <path>` | Serial port (e.g. `/dev/ttyUSB0`, `COM5`). |
| `--tcp <host:port>` | KISS-over-TCP endpoint (e.g. `localhost:8001`). Mutually exclusive with `--port`. |
| `--connect <CALL>` | Auto-connect once the TUI is up. Optional. |

With neither `--port` nor `--tcp`, the modem comes from the settings file; failing that you're prompted, and the prompt takes a serial port *or* a `host:port` endpoint. Both are editable at runtime from the Settings dialog (`Ctrl-S`), which is also where you switch between the two transports.

If `--mycall` AND one of `--port` / `--tcp` are supplied, the run is treated as ephemeral — settings aren't persisted, so two parallel instances driven by their own flags can run side-by-side without racing on the shared settings file. Useful for connecting two modems on the same host to each other.

## Keyboard

| Key | Action |
| --- | --- |
| `F2` | Connect... (modal callsign prompt) |
| `F3` | Disconnect |
| `Esc` (or `Ctrl-Q`) | Quit |
| `↑` / `↓` | Recall previously sent lines (input line) |
| `F10` | Open menu bar |
| `Ctrl-S` | Settings... (hot-swap MYCALL / transport / port / TCP endpoint) |

While connected, typing in the input line and pressing Enter sends one I-frame. `↑` walks back through what you've sent this run (a half-typed line is kept, so `↓` returns to it).

## Built on

Downstream consumer of the [Packet.NET libraries](https://github.com/m0lte/packet.net), all pulled from NuGet:

- [`Packet.Core`](https://www.nuget.org/packages/Packet.Core) — shared primitives.
- [`Packet.Ax25`](https://www.nuget.org/packages/Packet.Ax25) — AX.25 v2.2 codec + session machine + `Ax25Listener`. Transitively pulls [`Packet.Ax25.Sdl`](https://www.nuget.org/packages/Packet.Ax25.Sdl) from [`m0lte/ax25sdl`](https://github.com/m0lte/ax25sdl).
- [`Packet.Kiss`](https://www.nuget.org/packages/Packet.Kiss) — KISS framing + ACKMODE + transports.
- `Terminal.Gui` v2 — Turbo Vision-style TUI framework.

## Provenance

Extracted from `m0lte/packet.net` on 2026-05-17 (history preserved via `git filter-repo`) — the TUI used to live at `src/Packet.Term/` in that monorepo. Now an independent .NET application that consumes the Packet.* libraries from NuGet rather than living alongside them.

## Sibling repos

| Repo | What it is |
| --- | --- |
| **`m0lte/packet-term-tui`** *(here)* | C# Terminal.Gui v2 TUI |
| [`m0lte/packet-term-web`](https://github.com/m0lte/packet-term-web) | Browser TNC2 emulator — same idea on the desktop, at https://packet-term.m0lte.uk |
| [`m0lte/packet.net`](https://github.com/m0lte/packet.net) | .NET libraries (`Packet.Core` / `Packet.Ax25` / `Packet.Kiss`) — published to NuGet, consumed here |
| [`m0lte/ax25sdl`](https://github.com/m0lte/ax25sdl) | SDL transcriptions + codegen — transitively consumed via `Packet.Ax25` → `Packet.Ax25.Sdl` |
| [`m0lte/ax25-ts`](https://github.com/m0lte/ax25-ts) | TypeScript counterpart to `Packet.Ax25` — irrelevant to this app but part of the family |

## License

[MIT](LICENSE).
