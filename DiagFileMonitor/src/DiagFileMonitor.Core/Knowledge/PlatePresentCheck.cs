using System.Text.RegularExpressions;
using DiagFileMonitor.Core.SpidaLogs;

namespace DiagFileMonitor.Core.Knowledge;

public enum PlatePresentVerdict
{
    /// <summary>Both sides dropped together with a clamp output change alongside - the clamp really released.</summary>
    NormalClampRelease,

    /// <summary>One side dropped on its own with nothing physically moving - a sensor glitch.</summary>
    SensorGlitch,

    /// <summary>Does not fit either pattern cleanly, so it needs a human to look at it.</summary>
    Unclear
}

public class PlatePresentEvent
{
    public TimeSpan Time { get; init; }

    /// <summary>"Fixed" or "Floating", from the input address.</summary>
    public string Side { get; init; } = string.Empty;

    public string Address { get; init; } = string.Empty;

    public bool BothSidesTogether { get; init; }
    public bool ClampOutputNearby { get; init; }

    /// <summary>How long until that side read 1 again, where the log shows it coming back.</summary>
    public TimeSpan? RecoveredAfter { get; init; }

    public PlatePresentVerdict Verdict { get; init; }

    public string Explanation => Verdict switch
    {
        PlatePresentVerdict.NormalClampRelease =>
            "Both sides dropped together and a plate clamp output changed alongside it. "
            + "That is the clamp releasing normally, not a fault.",
        PlatePresentVerdict.SensorGlitch =>
            $"Only the {Side.ToLowerInvariant()} side dropped, with no plate clamp output change nearby"
            + (RecoveredAfter is { } r ? $" and it came back after {r.TotalSeconds:0.#}s" : string.Empty)
            + ". Nothing physically moved, so this reads as a sensor glitch rather than lost product.",
        _ =>
            $"The {Side.ToLowerInvariant()} side dropped. "
            + (BothSidesTogether ? "Both sides changed together" : "Only this side changed")
            + (ClampOutputNearby ? ", and a clamp output changed nearby" : ", with no clamp output change nearby")
            + ". Does not match either the normal or the glitch pattern cleanly - worth a look."
    };
}

/// <summary>
/// Tells a real lost-product event apart from a PlatePresentSwitch glitch.
/// <para>
/// The test is the one from the machine write-up: on a genuine clamp release both plate sensors
/// drop at the same instant and a plate clamp output changes with them. A glitch shows one side
/// dropping on its own with nothing else moving, usually recovering within a second or two.
/// </para>
/// </summary>
public static class PlatePresentCheck
{
    public const string FixedSideAddress = "192.168.250.1-4.2";
    public const string FloatingSideAddress = "192.168.250.1-4.4";

    /// <summary>Both sides changing this close together count as the same event.</summary>
    private static readonly TimeSpan Together = TimeSpan.FromMilliseconds(50);

    /// <summary>How far either side of a drop to look for a plate clamp output change.</summary>
    private static readonly TimeSpan ClampWindow = TimeSpan.FromSeconds(2);

    private static readonly Regex InputChange = new(
        @"Input\s*\((?<address>[^)]+)\)\s*Changed\s*to\s*(?<value>\d+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static IReadOnlyList<PlatePresentEvent> Find(IReadOnlyList<MachineLogEntry> machineLog)
    {
        var changes = new List<(TimeSpan Time, string Address, int Value)>();

        foreach (var entry in machineLog)
        {
            if (entry.Category != MachineLogCategory.InputChange) continue;
            if (!entry.Tag.Contains("PlatePresent", StringComparison.OrdinalIgnoreCase)) continue;

            var match = InputChange.Match(entry.Description);
            if (!match.Success) continue;

            var address = match.Groups["address"].Value.Trim();
            if (address != FixedSideAddress && address != FloatingSideAddress) continue;

            changes.Add((entry.Time, address, int.Parse(match.Groups["value"].Value)));
        }

        var clampChanges = machineLog
            .Where(e => e.Category == MachineLogCategory.OutputChange
                        && e.Tag.Contains("PlateClamp", StringComparison.OrdinalIgnoreCase))
            .Select(e => e.Time)
            .ToList();

        var events = new List<PlatePresentEvent>();

        foreach (var (time, address, value) in changes.Where(c => c.Value == 0))
        {
            var other = address == FixedSideAddress ? FloatingSideAddress : FixedSideAddress;

            var bothTogether = changes.Any(c =>
                c.Address == other && c.Value == 0 && (c.Time - time).Duration() <= Together);

            var clampNearby = clampChanges.Any(t => (t - time).Duration() <= ClampWindow);

            var recovery = changes
                .Where(c => c.Address == address && c.Value == 1 && c.Time > time)
                .Select(c => (TimeSpan?)(c.Time - time))
                .FirstOrDefault();

            events.Add(new PlatePresentEvent
            {
                Time = time,
                Side = address == FixedSideAddress ? "Fixed" : "Floating",
                Address = address,
                BothSidesTogether = bothTogether,
                ClampOutputNearby = clampNearby,
                RecoveredAfter = recovery,
                Verdict = (bothTogether, clampNearby) switch
                {
                    (true, true) => PlatePresentVerdict.NormalClampRelease,
                    (false, false) => PlatePresentVerdict.SensorGlitch,
                    _ => PlatePresentVerdict.Unclear
                }
            });
        }

        return events;
    }
}
