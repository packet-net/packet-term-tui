using Packet.Term;

namespace Packet.Term.Tests;

/// <summary>
/// The serial-or-TCP endpoint value object: parsing, display, and the
/// value equality MainWindow's reconfigure path leans on.
/// </summary>
public class ModemEndpointTests
{
    [Theory]
    [InlineData("localhost:8001")]
    [InlineData("192.168.1.5:8001")]
    [InlineData("bpq.example.com:65535")]
    [InlineData("[::1]:8001")]
    public void Valid_Host_Port_Parses_As_Tcp(string raw)
    {
        ModemEndpoint.TryParseTcp(raw, out var endpoint, out var error).Should().BeTrue();
        endpoint!.Kind.Should().Be(TransportKind.Tcp);
        endpoint.Value.Should().Be(raw);
        error.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("localhost")]           // no port
    [InlineData("localhost:")]          // empty port
    [InlineData(":8001")]               // empty host
    [InlineData("localhost:0")]         // out of range
    [InlineData("localhost:65536")]     // out of range
    [InlineData("localhost:-1")]        // not an unsigned number
    [InlineData("localhost:80a1")]      // not a number
    [InlineData("/dev/ttyUSB0")]        // a serial path, not an endpoint
    [InlineData("COM5")]
    public void Invalid_Endpoint_Fails_With_A_Message(string raw)
    {
        ModemEndpoint.TryParseTcp(raw, out var endpoint, out var error).Should().BeFalse();
        endpoint.Should().BeNull();
        error.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Surrounding_Whitespace_Is_Trimmed()
    {
        ModemEndpoint.TryParseTcp("  localhost:8001  ", out var endpoint, out _).Should().BeTrue();
        endpoint!.Value.Should().Be("localhost:8001");
    }

    [Theory]
    [InlineData("localhost:8001", true)]
    [InlineData("[::1]:8001", true)]
    [InlineData("/dev/ttyUSB0", false)]
    [InlineData("COM5", false)]
    [InlineData("", false)]
    public void LooksLikeTcp_Separates_Endpoints_From_Serial_Paths(string raw, bool expected)
        => ModemEndpoint.LooksLikeTcp(raw).Should().Be(expected);

    [Fact]
    public void Serial_Endpoint_Keeps_Its_Port_Name()
    {
        var endpoint = ModemEndpoint.ForSerial("  /dev/ttyUSB0 ");
        endpoint.Kind.Should().Be(TransportKind.Serial);
        endpoint.Value.Should().Be("/dev/ttyUSB0");
        endpoint.StatusLabel.Should().Be("/dev/ttyUSB0");
        endpoint.Description.Should().StartWith("port /dev/ttyUSB0 @ ");
    }

    [Fact]
    public void Tcp_Endpoint_Labels_Itself_As_Tcp()
    {
        ModemEndpoint.TryParseTcp("localhost:8001", out var endpoint, out _).Should().BeTrue();
        endpoint!.StatusLabel.Should().Be("tcp localhost:8001");
        endpoint.Description.Should().Be("KISS/TCP localhost:8001");
    }

    [Fact]
    public void Endpoints_Compare_By_Value()
    {
        ModemEndpoint.ForSerial("COM5").Should().Be(ModemEndpoint.ForSerial("COM5"));
        ModemEndpoint.ForSerial("COM5").Should().NotBe(ModemEndpoint.ForSerial("COM6"));

        ModemEndpoint.TryParseTcp("localhost:8001", out var a, out _);
        ModemEndpoint.TryParseTcp("localhost:8001", out var b, out _);
        ModemEndpoint.TryParseTcp("localhost:8002", out var c, out _);
        a.Should().Be(b);
        a.Should().NotBe(c);
    }

    [Fact]
    public void A_Serial_Port_Never_Equals_A_Tcp_Endpoint_With_The_Same_Text()
    {
        ModemEndpoint.TryParseTcp("localhost:8001", out var tcp, out _);
        ModemEndpoint.ForSerial("localhost:8001").Should().NotBe(tcp);
    }
}
