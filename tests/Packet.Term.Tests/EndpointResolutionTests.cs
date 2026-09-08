using Packet.Term;

namespace Packet.Term.Tests;

/// <summary>
/// The settings ⇄ endpoint mapping the boot flow and the Settings dialog
/// share.
/// </summary>
public class EndpointResolutionTests
{
    [Fact]
    public void Serial_Settings_Resolve_To_The_Saved_Port()
    {
        var settings = new AppSettings { SerialPort = "/dev/ttyUSB0" };
        Program.EndpointFromSettings(settings).Should().Be(ModemEndpoint.ForSerial("/dev/ttyUSB0"));
    }

    [Fact]
    public void Tcp_Settings_Resolve_To_The_Saved_Endpoint()
    {
        var settings = new AppSettings
        {
            Transport = TransportKind.Tcp,
            TcpEndpoint = "localhost:8001",
            // A stale serial port must not win once the transport is TCP.
            SerialPort = "/dev/ttyUSB0",
        };

        var endpoint = Program.EndpointFromSettings(settings);
        endpoint.Should().NotBeNull();
        endpoint!.Kind.Should().Be(TransportKind.Tcp);
        endpoint.Value.Should().Be("localhost:8001");
    }

    [Fact]
    public void Blank_Settings_Resolve_To_Nothing_So_The_Prompt_Runs()
        => Program.EndpointFromSettings(new AppSettings()).Should().BeNull();

    [Fact]
    public void Tcp_Mode_With_An_Unparseable_Endpoint_Falls_Through_To_The_Prompt()
    {
        var settings = new AppSettings { Transport = TransportKind.Tcp, TcpEndpoint = "not-an-endpoint" };
        Program.EndpointFromSettings(settings).Should().BeNull();
    }

    [Fact]
    public void Applying_A_Tcp_Endpoint_Leaves_The_Saved_Serial_Port_Alone()
    {
        var settings = new AppSettings { SerialPort = "/dev/ttyUSB0" };
        ModemEndpoint.TryParseTcp("localhost:8001", out var tcp, out _).Should().BeTrue();

        Program.ApplyEndpointToSettings(settings, tcp!);

        settings.Transport.Should().Be(TransportKind.Tcp);
        settings.TcpEndpoint.Should().Be("localhost:8001");
        settings.SerialPort.Should().Be("/dev/ttyUSB0");
    }

    [Fact]
    public void Applying_A_Serial_Endpoint_Leaves_The_Saved_Tcp_Endpoint_Alone()
    {
        var settings = new AppSettings { Transport = TransportKind.Tcp, TcpEndpoint = "localhost:8001" };

        Program.ApplyEndpointToSettings(settings, ModemEndpoint.ForSerial("COM5"));

        settings.Transport.Should().Be(TransportKind.Serial);
        settings.SerialPort.Should().Be("COM5");
        settings.TcpEndpoint.Should().Be("localhost:8001");
    }

    [Fact]
    public void Round_Tripping_Through_Settings_Preserves_The_Endpoint()
    {
        var settings = new AppSettings();
        ModemEndpoint.TryParseTcp("192.168.1.5:8001", out var tcp, out _).Should().BeTrue();

        Program.ApplyEndpointToSettings(settings, tcp!);

        Program.EndpointFromSettings(settings).Should().Be(tcp);
    }
}
