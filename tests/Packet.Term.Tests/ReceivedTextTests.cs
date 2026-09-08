using System.Text;
using Packet.Term;

namespace Packet.Term.Tests;

/// <summary>
/// A node's menu and help text arrives with its line breaks *inside* one
/// information field. These cover that the breaks survive as breaks —
/// mapping them to a placeholder is what turned a BBS menu into a single
/// unreadable run.
/// </summary>
public class ReceivedTextTests
{
    private static IReadOnlyList<string> Lines(string ascii)
        => ReceivedText.ToLines(Encoding.ASCII.GetBytes(ascii));

    [Fact]
    public void Single_Line_With_Trailing_Cr_Has_No_Empty_Tail()
        => Lines("hello\r").Should().Equal("hello");

    [Fact]
    public void Embedded_Cr_Splits_Into_Lines()
        => Lines("GB7RDG BBS\rB)ye  L)ist  R)ead\rEnter command:\r")
            .Should().Equal("GB7RDG BBS", "B)ye  L)ist  R)ead", "Enter command:");

    [Fact]
    public void Crlf_Is_One_Break_Not_Two()
        => Lines("one\r\ntwo\r\nthree\r\n").Should().Equal("one", "two", "three");

    [Fact]
    public void Bare_Lf_Also_Breaks()
        => Lines("one\ntwo").Should().Equal("one", "two");

    [Fact]
    public void Blank_Lines_Inside_The_Payload_Are_Kept()
        => Lines("header\r\rbody\r").Should().Equal("header", "", "body");

    [Fact]
    public void Text_Without_A_Terminator_Still_Yields_Its_Line()
        => Lines("no terminator").Should().Equal("no terminator");

    [Fact]
    public void Nothing_But_Terminators_Yields_No_Lines()
    {
        Lines("\r").Should().BeEmpty();
        Lines("\r\n\r\n").Should().BeEmpty();
        ReceivedText.ToLines(ReadOnlySpan<byte>.Empty).Should().BeEmpty();
    }

    [Fact]
    public void Non_Printable_Bytes_Become_Dots_Without_Breaking_The_Line()
    {
        var line = ReceivedText.ToLines(new byte[] { 0x41, 0x00, 0x1B, 0xFF, 0x42 }).Should().ContainSingle().Subject;
        line.Should().Be("A...B");
    }

    [Fact]
    public void Tab_Is_Not_Printable_Ascii_So_It_Becomes_A_Dot()
        => Lines("a\tb").Should().Equal("a.b");

    // A line longer than PACLEN is segmented across frames. The chunk that
    // ends mid-line has to say so, or the tail renders as its own row.
    [Fact]
    public void A_Field_Ending_Mid_Line_Is_Flagged_Incomplete()
    {
        var chunk = ReceivedText.Split(Encoding.ASCII.GetBytes("a very long line that ran out of pac"));
        chunk.Incomplete.Should().BeTrue();
        chunk.Lines.Should().Equal("a very long line that ran out of pac");
    }

    [Fact]
    public void A_Field_Ending_On_A_Terminator_Is_Complete()
    {
        ReceivedText.Split(Encoding.ASCII.GetBytes("done\r")).Incomplete.Should().BeFalse();
        ReceivedText.Split(Encoding.ASCII.GetBytes("done\r\n")).Incomplete.Should().BeFalse();
        ReceivedText.Split(Encoding.ASCII.GetBytes("a\rb\r")).Incomplete.Should().BeFalse();
    }

    [Fact]
    public void A_Field_Whose_Last_Line_Is_Open_Still_Reports_Its_Earlier_Lines()
    {
        var chunk = ReceivedText.Split(Encoding.ASCII.GetBytes("first\rsecond\rthird-open"));
        chunk.Lines.Should().Equal("first", "second", "third-open");
        chunk.Incomplete.Should().BeTrue();
    }

    [Fact]
    public void An_Empty_Field_Is_Not_Incomplete()
        => ReceivedText.Split(ReadOnlySpan<byte>.Empty).Incomplete.Should().BeFalse();
}
