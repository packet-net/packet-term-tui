using System.Reflection;
using System.Text;

namespace Packet.Term;

/// <summary>
/// Build / version banner constants. Read from the assembly's
/// informational version so the welcome message stays in sync with the
/// version the build is stamped with.
/// </summary>
internal static class AppInfo
{
    /// <summary>Human-readable version string, e.g. <c>"0.1.0"</c>.</summary>
    public static string Version { get; } =
        typeof(AppInfo).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? typeof(AppInfo).Assembly.GetName().Version?.ToString()
            ?? "0.0.0";

    /// <summary>
    /// Multi-line version report: Packet.Term's own version plus the AX.25
    /// SDL spec-table version and the Packet.NET runtime library versions,
    /// read live from the loaded assemblies (so they reflect what's actually
    /// running, not hard-coded constants).
    /// </summary>
    public static string VersionReport()
    {
        static string Line(string name, Type t) => $"  {name,-20}{AsmVersion(t)}";

        var sb = new StringBuilder();
        sb.AppendLine("Packet.Term " + Strip(Version));
        sb.AppendLine();
        sb.AppendLine("SDL spec tables:");
        sb.AppendLine(Line("Packet.Ax25.Sdl", typeof(Packet.Ax25.Sdl.TransitionSpec)));
        sb.AppendLine();
        sb.AppendLine("Runtime libraries:");
        sb.AppendLine(Line("Packet.Core", typeof(Packet.Core.Callsign)));
        sb.AppendLine(Line("Packet.Ax25", typeof(Packet.Ax25.Ax25Frame)));
        sb.AppendLine(Line("Packet.Kiss", typeof(Packet.Kiss.KissEncoder)));
        sb.AppendLine(Line("Packet.Kiss.Serial", typeof(Packet.Kiss.Serial.KissSerialModem)));
        return sb.ToString().TrimEnd();
    }

    // Read a referenced assembly's informational version (the NuGet package
    // version), trimming any +commit-hash build-metadata suffix SourceLink
    // appends.
    private static string AsmVersion(Type t)
    {
        var asm = t.Assembly;
        var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        return !string.IsNullOrEmpty(info)
            ? Strip(info)
            : asm.GetName().Version?.ToString() ?? "unknown";
    }

    private static string Strip(string version)
    {
        int plus = version.IndexOf('+', StringComparison.Ordinal);
        return plus >= 0 ? version[..plus] : version;
    }
}
