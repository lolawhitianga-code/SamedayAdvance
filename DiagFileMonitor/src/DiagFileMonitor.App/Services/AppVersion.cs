using System.Reflection;

namespace DiagFileMonitor.App.Services;

/// <summary>
/// The build's version, for the splash and the header. Read from the assembly rather than held
/// as a constant, so the number in the csproj is the only place it is written down.
/// </summary>
public static class AppVersion
{
    /// <summary>Three parts - 1.0.0 - since the fourth is always zero and adds nothing.</summary>
    public static string Number
    {
        get
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            return version is null ? "unknown" : $"{version.Major}.{version.Minor}.{version.Build}";
        }
    }

    public static string Display => $"v{Number}";

    public static string LongDisplay => $"Diagnostic File Monitor {Display}";
}
