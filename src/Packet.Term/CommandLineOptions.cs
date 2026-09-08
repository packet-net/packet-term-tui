using CommandLine;

namespace Packet.Term;

/// <summary>
/// Command-line surface for <c>Packet.Term</c>. Each option overrides the
/// corresponding persisted setting for one run; nothing is written back to
/// the settings file as a side-effect of a flag being present.
/// </summary>
public sealed class CommandLineOptions
{
    [Option("mycall", Required = false, HelpText = "Override the saved MYCALL for this run only.")]
    public string? MyCall { get; set; }

    [Option("port", Required = false, HelpText = "Serial port to open (overrides saved value).")]
    public string? Port { get; set; }

    [Option("tcp", Required = false, HelpText = "KISS-over-TCP endpoint, host:port (e.g. localhost:8001). Mutually exclusive with --port.")]
    public string? Tcp { get; set; }

    [Option("connect", Required = false, HelpText = "Skip the disconnected state — connect to this callsign at startup.")]
    public string? Connect { get; set; }
}
