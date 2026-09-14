namespace DiagFileMonitor.Core.SpidaLogs;

public record StepSample(int Step, TimeSpan Duration, int Cycle);

/// <summary>
/// How a machine actually runs: which steps it goes through, in what order, and how long it
/// spends in each. Built from every unit in the log so one slow run cannot skew the picture.
/// </summary>
public class StepProfile
{
    public IReadOnlyList<StepSample> Samples { get; init; } = Array.Empty<StepSample>();
    public IReadOnlyList<int> Sequence { get; init; } = Array.Empty<int>();
    public IReadOnlyDictionary<int, TimeSpan> MedianDuration { get; init; } = new Dictionary<int, TimeSpan>();
    public IReadOnlyDictionary<int, int> Occurrences { get; init; } = new Dictionary<int, int>();
    public int CycleCount { get; init; }
    public TimeSpan MedianCycleDuration { get; init; }

    public bool IsEmpty => Samples.Count == 0;

    public static StepProfile From(IReadOnlyList<MachineLogEntry> entries)
    {
        var boundaries = MachineCycles.FindBoundaries(entries);
        if (entries.Count == 0 || boundaries.Count == 0) return new StepProfile();

        var samples = new List<StepSample>();
        var sequences = new List<List<int>>();
        var cycleDurations = new List<TimeSpan>();

        for (var b = 0; b < boundaries.Count; b++)
        {
            var from = boundaries[b];
            var to = b + 1 < boundaries.Count ? boundaries[b + 1] : entries.Count;

            var readings = MachineCycles.ReadSteps(entries)
                .Where(r => r.EntryIndex >= from && r.EntryIndex < to)
                .ToList();

            if (readings.Count == 0) continue;

            var order = new List<int>();

            for (var i = 0; i < readings.Count; i++)
            {
                // A step lasts until the next step is logged; the last one runs to the end of the unit.
                var endTime = i + 1 < readings.Count ? readings[i + 1].Time : entries[to - 1].Time;
                var duration = endTime - readings[i].Time;
                if (duration < TimeSpan.Zero) continue;

                samples.Add(new StepSample(readings[i].Step, duration, b + 1));

                // Steps can be logged repeatedly; the sequence records transitions, not repeats.
                if (order.Count == 0 || order[^1] != readings[i].Step) order.Add(readings[i].Step);
            }

            sequences.Add(order);
            cycleDurations.Add(entries[to - 1].Time - readings[0].Time);
        }

        return new StepProfile
        {
            Samples = samples,
            // The order most units follow, so an odd run does not define "normal".
            Sequence = sequences
                .GroupBy(s => string.Join(",", s))
                .OrderByDescending(g => g.Count())
                .Select(g => g.First())
                .FirstOrDefault() ?? new List<int>(),
            MedianDuration = samples
                .GroupBy(s => s.Step)
                .ToDictionary(g => g.Key, g => Median(g.Select(s => s.Duration).ToList())),
            Occurrences = samples.GroupBy(s => s.Step).ToDictionary(g => g.Key, g => g.Count()),
            CycleCount = sequences.Count,
            MedianCycleDuration = cycleDurations.Count == 0 ? TimeSpan.Zero : Median(cycleDurations)
        };
    }

    internal static TimeSpan Median(List<TimeSpan> values)
    {
        if (values.Count == 0) return TimeSpan.Zero;

        var ordered = values.OrderBy(v => v).ToList();
        var middle = ordered.Count / 2;

        return ordered.Count % 2 == 1
            ? ordered[middle]
            : TimeSpan.FromTicks((ordered[middle - 1].Ticks + ordered[middle].Ticks) / 2);
    }
}
