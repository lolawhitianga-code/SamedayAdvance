namespace DiagFileMonitor.Core.SpidaLogs;

public enum StepVerdict
{
    Same,
    SlightlySlower,
    Slower,
    MuchSlower,
    Faster,
    MissingFromCompared,
    NotInMaster
}

public class StepDifference
{
    public int Step { get; init; }
    public TimeSpan? MasterMedian { get; init; }
    public TimeSpan? ComparedMedian { get; init; }
    public double? PercentOfMaster { get; init; }
    public StepVerdict Verdict { get; init; }

    public bool IsNoteworthy => Verdict != StepVerdict.Same;

    /// <summary>
    /// How much faster or slower the compared machine is, as a multiple.
    /// <para>
    /// This used to print <see cref="PercentOfMaster"/> - the compared time as a percentage OF
    /// the master's - directly beside the words "faster" or "slower", so a step taking 17% of
    /// the benchmark's time read as "17% faster" when it is nearly six times faster. A multiple
    /// is how people say it out loud, and it cannot be read the wrong way round.
    /// </para>
    /// </summary>
    public string? Multiple
    {
        get
        {
            // From the durations, not from PercentOfMaster: that is rounded to a whole percent,
            // and at 9% a single percent is worth about half a turn of the multiple.
            if (MasterMedian is not { } master || ComparedMedian is not { } compared) return null;
            if (master <= TimeSpan.Zero || compared <= TimeSpan.Zero) return null;

            return Describe(master.TotalMilliseconds, compared.TotalMilliseconds);
        }
    }

    public string Display
    {
        get
        {
            var master = MasterMedian is { } m ? Describe(m) : "-";
            var compared = ComparedMedian is { } c ? Describe(c) : "-";

            var change = Verdict switch
            {
                StepVerdict.MissingFromCompared => "never reached",
                StepVerdict.NotInMaster => "not in master",
                _ => Multiple ?? "-"
            };

            var note = Verdict switch
            {
                StepVerdict.MuchSlower => "  <-- much slower",
                StepVerdict.Slower => "  <-- slower",
                StepVerdict.SlightlySlower => "  <-- slightly slower",
                _ => string.Empty
            };

            // "1.0x slower" reads oddly for a step that simply matches the benchmark.
            if (Verdict == StepVerdict.Same && Multiple is not null) change = "same";

            return $"  step {Step,-6} master {master,-9} compared {compared,-9} {change,-13}{note}".TrimEnd();
        }
    }

    /// <summary>How many times slower or faster the compared time is than the master time.</summary>
    internal static string Describe(double masterMs, double comparedMs)
    {
        var ratio = comparedMs / masterMs;

        return ratio >= 1 ? $"{ratio:0.0}x slower" : $"{1 / ratio:0.0}x faster";
    }

    internal static string Describe(TimeSpan span) =>
        span.TotalMinutes >= 1 ? $"{span.TotalMinutes:0.##} min"
        : span.TotalSeconds >= 1 ? $"{span.TotalSeconds:0.##} s"
        : $"{span.TotalMilliseconds:0} ms";
}

public class StepTimingComparison
{
    public IReadOnlyList<StepDifference> Differences { get; init; } = Array.Empty<StepDifference>();
    public IReadOnlyList<int> MasterSequence { get; init; } = Array.Empty<int>();
    public IReadOnlyList<int> ComparedSequence { get; init; } = Array.Empty<int>();
    public bool SequenceMatches { get; init; }
    public TimeSpan MasterCycle { get; init; }
    public TimeSpan ComparedCycle { get; init; }
    public double? CyclePercentOfMaster { get; init; }

    public IEnumerable<StepDifference> Noteworthy => Differences.Where(d => d.IsNoteworthy);

    /// <summary>
    /// Thresholds are deliberately tight, because the point of a benchmark is to catch a machine
    /// drifting before it fails outright.
    /// </summary>
    public static StepTimingComparison Compare(StepProfile master, StepProfile compared,
        double slightly = 1.10, double slower = 1.25, double much = 1.50, double faster = 0.80)
    {
        var steps = master.MedianDuration.Keys.Union(compared.MedianDuration.Keys).OrderBy(s => s).ToList();
        var differences = new List<StepDifference>();

        foreach (var step in steps)
        {
            master.MedianDuration.TryGetValue(step, out var masterTime);
            compared.MedianDuration.TryGetValue(step, out var comparedTime);

            var inMaster = master.MedianDuration.ContainsKey(step);
            var inCompared = compared.MedianDuration.ContainsKey(step);

            double? percent = inMaster && inCompared && masterTime > TimeSpan.Zero
                ? Math.Round(comparedTime.TotalMilliseconds / masterTime.TotalMilliseconds * 100, 0)
                : null;

            var verdict = (inMaster, inCompared) switch
            {
                (true, false) => StepVerdict.MissingFromCompared,
                (false, true) => StepVerdict.NotInMaster,
                _ when percent is null => StepVerdict.Same,
                _ when percent >= much * 100 => StepVerdict.MuchSlower,
                _ when percent >= slower * 100 => StepVerdict.Slower,
                _ when percent >= slightly * 100 => StepVerdict.SlightlySlower,
                _ when percent <= faster * 100 => StepVerdict.Faster,
                _ => StepVerdict.Same
            };

            differences.Add(new StepDifference
            {
                Step = step,
                MasterMedian = inMaster ? masterTime : null,
                ComparedMedian = inCompared ? comparedTime : null,
                PercentOfMaster = percent,
                Verdict = verdict
            });
        }

        return new StepTimingComparison
        {
            Differences = differences,
            MasterSequence = master.Sequence,
            ComparedSequence = compared.Sequence,
            SequenceMatches = master.Sequence.SequenceEqual(compared.Sequence),
            MasterCycle = master.MedianCycleDuration,
            ComparedCycle = compared.MedianCycleDuration,
            CyclePercentOfMaster = master.MedianCycleDuration > TimeSpan.Zero
                ? Math.Round(compared.MedianCycleDuration.TotalMilliseconds / master.MedianCycleDuration.TotalMilliseconds * 100, 0)
                : null
        };
    }
}
