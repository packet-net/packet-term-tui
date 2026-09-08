using Packet.Term;

namespace Packet.Term.Tests;

/// <summary>
/// Round-trip the JSON settings file. Path is a temp file so the tests
/// don't trample the user's real settings.
/// </summary>
public class AppSettingsTests
{
    [Fact]
    public void Empty_Settings_Round_Trip_Through_Disk()
    {
        var path = Path.Combine(Path.GetTempPath(), $"packet-term-test-{Guid.NewGuid():N}.json");
        try
        {
            var original = new AppSettings();
            original.Save(path);
            var loaded = AppSettings.Load(path);
            loaded.MyCall.Should().BeNull();
            loaded.SerialPort.Should().BeNull();
            loaded.LastConnectTarget.Should().BeNull();
            loaded.TcpEndpoint.Should().BeNull();
            loaded.Transport.Should().Be(TransportKind.Serial);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Populated_Settings_Round_Trip_Through_Disk()
    {
        var path = Path.Combine(Path.GetTempPath(), $"packet-term-test-{Guid.NewGuid():N}.json");
        try
        {
            var original = new AppSettings
            {
                MyCall = "M0LTE-1",
                SerialPort = "/dev/ttyUSB0",
                LastConnectTarget = "G1AAA",
            };
            original.Save(path);
            var loaded = AppSettings.Load(path);
            loaded.MyCall.Should().Be("M0LTE-1");
            loaded.SerialPort.Should().Be("/dev/ttyUSB0");
            loaded.LastConnectTarget.Should().Be("G1AAA");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Tcp_Transport_Round_Trips_As_A_Readable_String()
    {
        var path = Path.Combine(Path.GetTempPath(), $"packet-term-test-{Guid.NewGuid():N}.json");
        try
        {
            new AppSettings
            {
                MyCall = "M0LTE-1",
                Transport = TransportKind.Tcp,
                TcpEndpoint = "localhost:8001",
            }.Save(path);

            // settings.json is hand-editable — the enum must not land as 0/1.
            File.ReadAllText(path).Should().Contain("\"Tcp\"");

            var loaded = AppSettings.Load(path);
            loaded.Transport.Should().Be(TransportKind.Tcp);
            loaded.TcpEndpoint.Should().Be("localhost:8001");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Pre_Tcp_Settings_File_Still_Loads_As_Serial()
    {
        var path = Path.Combine(Path.GetTempPath(), $"packet-term-legacy-{Guid.NewGuid():N}.json");
        try
        {
            // Exactly what a settings.json written before KISS-over-TCP looks like.
            File.WriteAllText(path, """
                {
                  "MyCall": "M0LTE-1",
                  "SerialPort": "/dev/ttyUSB0",
                  "LastConnectTarget": "G1AAA"
                }
                """);

            var loaded = AppSettings.Load(path);
            loaded.Transport.Should().Be(TransportKind.Serial);
            loaded.SerialPort.Should().Be("/dev/ttyUSB0");
            loaded.TcpEndpoint.Should().BeNull();
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Missing_File_Returns_Blank_Defaults()
    {
        var nonexistent = Path.Combine(Path.GetTempPath(), $"packet-term-missing-{Guid.NewGuid():N}.json");
        var loaded = AppSettings.Load(nonexistent);
        loaded.MyCall.Should().BeNull();
        loaded.SerialPort.Should().BeNull();
    }

    [Fact]
    public void Corrupt_File_Returns_Blank_Defaults_Without_Throwing()
    {
        var path = Path.Combine(Path.GetTempPath(), $"packet-term-corrupt-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, "{not valid json}");
            var loaded = AppSettings.Load(path);
            loaded.MyCall.Should().BeNull();
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
