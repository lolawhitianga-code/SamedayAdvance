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

    /// <summary>
    /// True where the log ended part way through a step. That step's length is unknown, so it is
    /// left out of the timings rather than measured against whatever line happened to be written
    /// last - a housekeeping line minutes later would otherwise read as a very slow step.
    /// </summary>
    public bool FinalStepWasStillRunning { get; init; }

    public bool IsEmpty => Samples.Count == 0;

    public static StepProfile From(IReadOnlyList<MachineLogEntry> entries)
    {
        var boundaries = MachineCycles.FindBoundaries(entries);
        if (entries.Count == 0 || boundaries.Count == 0) return new StepProfile();

        var allReadings = MachineCycles.ReadSteps(entries);
        if (allReadings.Count == 0) return new StepProfile();

        // The step the machine was in when the export was taken never got an end, so its length
        // is unknown. Measuring it to the last line in the file makes it as long as whatever
        // housekeeping happened to be logged afterwards.
        var openReading = allReadings[^1];
        var finalStepOpen = false;

        var samples = new List<StepSample>();
        var sequences = new List<List<int>>();
        var cycleDurations = new List<TimeSpan>();

        for (var b = 0; b < boundaries.Count; b++)
        {
            var from = boundaries[b];
            var to = b + 1 < boundaries.Count ? boundaries[b + 1] : entries.Count;

            var readings = allReadings
                .Where(r => r.EntryIndex >= from && r.EntryIndex < to)
                .ToList();

            if (readings.Count == 0) continue;

            var closed = readings.Count;
            if (readings[^1].EntryIndex == openReading.EntryIndex)
            {
                closed--;
                finalStepOpen = true;
            }

            var order = new List<int>();

            // The machine did reach the step the log ends in, so it belongs in the sequence.
            // Only its duration is unknown.
            foreach (var reading in readings)
            {
                if (order.Count == 0 || order[^1] != reading.Step) order.Add(reading.Step);
            }

            for (var i = 0; i < closed; i++)
            {
                // A step lasts until the next step is logged; the last closed one runs to the
                // end of the unit.
                var endTime = i + 1 < readings.Count ? readings[i + 1].Time : entries[to - 1].Time;
                var duration = endTime - readings[i].Time;
                if (duration < TimeSpan.Zero) continue;

                samples.Add(new StepSample(readings[i].Step, duration, b + 1));
            }

            // A unit whose only step was the open one tells us nothing about timing.
            if (closed == 0) continue;

            sequences.Add(order);

            // The unit ran until its last closed step finished, which is where the open step
            // began if there is one.
            var cycleEnd = closed < readings.Count ? readings[closed].Time : entries[to - 1].Time;
            cycleDurations.Add(cycleEnd - readings[0].Time);
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
            MedianCycleDuration = cycleDurations.Count == 0 ? TimeSpan.Zero : Median(cycleDurations),
            FinalStepWasStillRunning = finalStepOpen
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
