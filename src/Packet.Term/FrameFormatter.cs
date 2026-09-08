using System.Globalization;
using System.Text;
using Packet.Ax25;
using Packet.Core;

namespace Packet.Term;

/// <summary>
/// Direction tag for a frame as it crosses the modem boundary.
/// </summary>
public enum FrameDirection
{
    /// <summary>The frame is being transmitted by us.</summary>
    Transmit,

    /// <summary>The frame is being received from the air.</summary>
    Receive,
}

/// <summary>
/// Format <see cref="Ax25Frame"/>s for the BPQ-monitor-style frame log
/// the TUI's top pane renders. Format mirrors the web packet-terminal's
/// <c>fmtFrame</c> sans VT100 colour codes — colouring is the rendering
/// layer's job (Terminal.Gui colour schemes at render time).
/// </summary>
public static class FrameFormatter
{
    /// <summary>
    /// Render <paramref name="bytes"/> as one BPQ-style monitor line. If
    /// the bytes don't parse as an AX.25 frame, emit a single
    /// <c>&lt;undecodable Nb&gt;</c> placeholder so frame-log readers still
    /// see something on the timeline rather than nothing.
    /// </summary>
    public static string Format(FrameDirection direction, ReadOnlySpan<byte> bytes, DateTimeOffset timestamp)
    {
        if (Ax25Frame.TryParse(bytes, out var frame))
        {
            return Format(direction, frame, timestamp);
        }

        var dirCh = direction == FrameDirection.Transmit ? 'T' : 'R';
        return $"{FormatTime(timestamp)} {dirCh} <undecodable {bytes.Length}B>";
    }

    /// <summary>
    /// Render <paramref name="frame"/> as one BPQ-style monitor line.
    /// </summary>
    public static string Format(FrameDirection direction, Ax25Frame frame, DateTimeOffset timestamp)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var sb = new StringBuilder(96);
        sb.Append(FormatTime(timestamp));
        sb.Append(' ');
        sb.Append(direction == FrameDirection.Transmit ? 'T' : 'R');
        sb.Append(' ');
        sb.Append(FormatCallsign(frame.Source.Callsign));
        sb.Append('>');
        sb.Append(FormatCallsign(frame.Destination.Callsign));
        if (frame.Digipeaters.Count > 0)
        {
            foreach (var d in frame.Digipeaters)
            {
                sb.Append(',');
                sb.Append(FormatCallsign(d.Callsign));
            }
        }

        sb.Append(" <");
        sb.Append(FormatBody(frame));
        sb.Append('>');

        // I-frames + UI frames carry an info field; indent it on the next
        // line. An info field of nothing but line terminators renders as
        // nothing, so don't leave a dangling indent behind it.
        var kind = ClassifyKind(frame);
        if ((kind == "I" || kind == "UI") && frame.Info.Length > 0)
        {
            var info = FormatInfo(frame.Info.Span);
            if (info.Length > 0)
            {
                sb.Append('\n');
                sb.Append("    ");
                sb.Append(info);
            }
        }

        return sb.ToString();
    }

    private static string FormatTime(DateTimeOffset ts)
        => ts.LocalDateTime.ToString("HH:mm:ss", CultureInfo.InvariantCulture);

    private static string FormatCallsign(Callsign c)
    {
        // Match BPQ's habit of hiding SSID-0 (so "M0LTE" rather than "M0LTE-0").
        if (c.Ssid == 0) return c.Base;
        return c.ToString();
    }

    private static string FormatBody(Ax25Frame frame)
    {
        var kind = ClassifyKind(frame);
        var cr = frame.IsCommand ? "C" : (frame.IsResponse ? "R" : "?");
        // Per the web demo: P on commands, F on responses, suppressed when P/F=0.
        string pf = string.Empty;
        if (frame.PollFinal)
        {
            pf = frame.IsCommand ? " P" : " F";
        }

        var ctrl = frame.Control;
        switch (kind)
        {
            case "I":
                {
                    int nr = (ctrl >> 5) & 0x07;
                    int ns = (ctrl >> 1) & 0x07;
                    return $"I {cr} R{nr.ToString(CultureInfo.InvariantCulture)} S{ns.ToString(CultureInfo.InvariantCulture)}{pf}";
                }
            case "RR":
            case "RNR":
            case "REJ":
            case "SREJ":
                {
                    int nr = (ctrl >> 5) & 0x07;
                    return $"{kind} {cr} R{nr.ToString(CultureInfo.InvariantCulture)}{pf}";
                }
            default:
                return $"{kind} {cr}{pf}";
        }
    }

    /// <summary>
    /// Map a frame's control byte to its short kind tag — same vocabulary
    /// as <see cref="Packet.Ax25.Session.Ax25FrameClassifier"/> but as a
    /// plain string for display, plus the UI case.
    /// </summary>
    public static string ClassifyKind(Ax25Frame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        byte ctrl = frame.Control;
        if ((ctrl & 0x01) == 0) return "I";

        if ((ctrl & 0x03) == 0x01)
        {
            return (ctrl & 0x0C) switch
            {
                0x00 => "RR",
                0x04 => "RNR",
                0x08 => "REJ",
                0x0C => "SREJ",
                _ => "?",
            };
        }

        byte uBase = (byte)(ctrl & 0xEF);
        return uBase switch
        {
            0x2F => "SABM",
            0x6F => "SABME",
            0x43 => "DISC",
            0x63 => "UA",
            0x0F => "DM",
            0x87 => "FRMR",
            0xAF => "XID",
            0xE3 => "TEST",
            0x03 => "UI",
            _ => "?",
        };
    }

    // The info field's own line breaks are kept as breaks, each
    // continuation carrying the same indent as the first — the monitor
    // pane splits the rendered line on '\n' into its scroll-back, so a
    // multi-line payload lands as multiple indented rows rather than one
    // run of text with dots where the CRs were.
    private static string FormatInfo(ReadOnlySpan<byte> info)
        => string.Join("\n    ", ReceivedText.ToLines(info));
}
