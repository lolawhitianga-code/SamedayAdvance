using System.Text.RegularExpressions;

namespace DiagFileMonitor.Core.SpidaLogs;

public record StepReading(int EntryIndex, TimeSpan Time, int Step);

/// <summary>
/// Shared step-counter reading. A new unit starts where the counter drops back, which holds
/// across machine families even though the step numbers themselves mean different things.
/// </summary>
public static class MachineCycles
{
    private static readonly Regex StepPattern = new(@"(?<name>[A-Za-z]*Step)\s*=\s*(?<value>\d+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static int? ReadStep(MachineLogEntry entry)
    {
        var match = StepPattern.Match($"{entry.Tag} {entry.Description}");
        return match.Success && int.TryParse(match.Groups["value"].Value, out var value) ? value : null;
    }

    public static List<StepReading> ReadSteps(IReadOnlyList<MachineLogEntry> entries)
    {
        var readings = new List<StepReading>();

        for (var i = 0; i < entries.Count; i++)
        {
            if (entries[i].Category != MachineLogCategory.Other) continue;
            if (ReadStep(entries[i]) is { } step) readings.Add(new StepReading(i, entries[i].Time, step));
        }

        return readings;
    }

    /// <summary>Entry indices where a new unit begins.</summary>
    public static List<int> FindBoundaries(IReadOnlyList<MachineLogEntry> entries)
    {
        var boundaries = new List<int>();
        var previous = int.MinValue;

        foreach (var reading in ReadSteps(entries))
        {
            if (previous != int.MinValue && reading.Step < previous) boundaries.Add(reading.EntryIndex);
            previous = reading.Step;
        }

        if (entries.Count == 0) return boundaries;
        if (boundaries.Count == 0 || boundaries[0] != 0) boundaries.Insert(0, 0);

        return boundaries;
    }
}
