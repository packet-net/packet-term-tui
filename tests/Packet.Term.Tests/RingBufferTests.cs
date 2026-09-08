using Packet.Term;

namespace Packet.Term.Tests;

/// <summary>
/// The per-pane scroll-back buffer, including the continuation path the
/// conversation pane uses to rejoin a line that arrived split across
/// frames.
/// </summary>
public class RingBufferTests
{
    [Fact]
    public void Keeps_The_Newest_Entries_Up_To_Capacity()
    {
        var buffer = new RingBuffer(3);
        foreach (var line in new[] { "a", "b", "c", "d" }) buffer.Add(line);
        buffer.Snapshot().Should().Equal("b", "c", "d");
    }

    [Fact]
    public void AppendToLast_Joins_The_Newest_Entry()
    {
        var buffer = new RingBuffer(10);
        buffer.Add("GB7RDG: a line that ran");
        buffer.AppendToLast(" out of paclen mid-sentence");

        buffer.Snapshot().Should().ContainSingle()
            .Which.Should().Be("GB7RDG: a line that ran out of paclen mid-sentence");
    }

    [Fact]
    public void AppendToLast_On_An_Empty_Buffer_Starts_A_Line()
    {
        var buffer = new RingBuffer(10);
        buffer.AppendToLast("orphan tail");
        buffer.Snapshot().Should().Equal("orphan tail");
    }

    [Fact]
    public void AppendToLast_Leaves_Older_Entries_Alone()
    {
        var buffer = new RingBuffer(10);
        buffer.Add("first");
        buffer.Add("second");
        buffer.AppendToLast("-tail");
        buffer.Snapshot().Should().Equal("first", "second-tail");
    }

    [Fact]
    public void AppendToLast_Does_Not_Disturb_Capacity_Or_Order()
    {
        var buffer = new RingBuffer(2);
        buffer.Add("a");
        buffer.Add("b");
        buffer.AppendToLast("!");
        buffer.Add("c");
        buffer.Snapshot().Should().Equal("b!", "c");
    }
}
