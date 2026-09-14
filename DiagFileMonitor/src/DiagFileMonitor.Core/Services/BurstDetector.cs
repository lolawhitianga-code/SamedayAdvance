using DiagFileMonitor.Core.Models;

namespace DiagFileMonitor.Core.Services;

public class BurstResult
{
    public bool IsBurst { get; init; }
    public string SerialNumber { get; init; } = string.Empty;
    public int BundleCount { get; init; }
    public int WindowHours { get; init; }
    public IReadOnlyList<DiagnosticFileSummary> Bundles { get; init; } = Array.Empty<DiagnosticFileSummary>();

    public string Headline =>
        $"{SerialNumber} has sent {BundleCount} diagnostic files in the last {WindowHours} hours";
}

/// <summary>
/// Spots a machine sending several bundles in quick succession, which is the signal that
/// something is actively going wrong rather than a one-off report.
/// </summary>
public static class BurstDetector
{
    public const int DefaultThreshold = 2;
    public const int DefaultWindowHours = 24;

    /// <summary>
    /// Looks at the machine that just sent <paramref name="trigger"/> and decides whether its
    /// recent activity crosses the alert threshold.
    /// </summary>
    public static BurstResult Evaluate(
        IEnumerable<DiagnosticFileSummary> allBundles,
        DiagnosticFileSummary trigger,
        int threshold = DefaultThreshold,
        int windowHours = DefaultWindowHours)
    {
        // An unknown serial is not a machine, so its bundles are unrelated to each other.
        if (trigger.SerialNumber == DiagnosticFileSummary.Unknown)
        {
            return new BurstResult { SerialNumber = trigger.SerialNumber, WindowHours = windowHours };
        }

        var windowStart = trigger.ArrivedAtUtc.AddHours(-windowHours);

        var inWindow = allBundles
            .Where(b => string.Equals(b.SerialNumber, trigger.SerialNumber, StringComparison.OrdinalIgnoreCase))
            .Where(b => b.ArrivedAtUtc > windowStart && b.ArrivedAtUtc <= trigger.ArrivedAtUtc)
            .OrderBy(b => b.ArrivedAtUtc)
            .ToList();

        return new BurstResult
        {
            IsBurst = inWindow.Count >= threshold,
            SerialNumber = trigger.SerialNumber,
            BundleCount = inWindow.Count,
            WindowHours = windowHours,
            Bundles = inWindow
        };
    }
}
