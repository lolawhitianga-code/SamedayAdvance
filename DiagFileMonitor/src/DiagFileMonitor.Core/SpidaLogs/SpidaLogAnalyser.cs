using System.Text.RegularExpressions;

namespace DiagFileMonitor.Core.SpidaLogs;

public class MachineLogFault
{
    public TimeSpan Time { get; init; }
    public string Text { get; init; } = string.Empty;
    public int? StepAtFault { get; init; }
    public IReadOnlyList<MachineLogEntry> Context { get; init; } = Array.Empty<MachineLogEntry>();

    /// <summary>Fault text with numbers masked, so the same fault matches across attempts.</summary>
    public string Signature => Regex.Replace(Text, @"\d+", "#").Trim().ToUpperInvariant();
}

/// <summary>One unit (panel/board/assembly) the machine attempted.</summary>
public class MachineCycle
{
    public int Number { get; init; }
    public TimeSpan Start { get; init; }
    public TimeSpan End { get; init; }
    public IReadOnlyList<MachineLogFault> Faults { get; init; } = Array.Empty<MachineLogFault>();
    public bool Completed { get; init; }
    public int? HighestStep { get; init; }

    public TimeSpan Duration => End - Start;

    public string Outcome => Completed
        ? $"completed in {Describe(Duration)}"
        : Faults.Count > 0
            ? $"stopped after {Describe(Duration)} - {Faults[^1].Text}"
            : $"no completion logged, ran {Describe(Duration)}";

    internal static string Describe(TimeSpan span) =>
        span.TotalMinutes >= 1 ? $"{span.TotalMinutes:0.#} min" : $"{span.TotalSeconds:0.#} s";
}

public class RepeatedFault
{
    public string Text { get; init; } = string.Empty;
    public int Occurrences { get; init; }
    public IReadOnlyList<int> CycleNumbers { get; init; } = Array.Empty<int>();
    public int? StepAtFault { get; init; }
}

public class SpidaLogAnalysis
{
    public string? MachineModelFromLog { get; init; }
    public int MachineLogLines { get; init; }
    public TimeSpan? LogStart { get; init; }
    public TimeSpan? LogEnd { get; init; }

    public IReadOnlyList<MachineCycle> Cycles { get; init; } = Array.Empty<MachineCycle>();
    public MachineCycle? Last { get; init; }
    public MachineCycle? SecondToLast { get; init; }
    public IReadOnlyList<RepeatedFault> RepeatedFaults { get; init; } = Array.Empty<RepeatedFault>();

    public IReadOnlyList<ErrLogEntry> RealErrors { get; init; } = Array.Empty<ErrLogEntry>();
    public IReadOnlyList<ErrLogEntry> CosmeticErrors { get; init; } = Array.Empty<ErrLogEntry>();
    public IReadOnlyList<ErrLogEntry> ErrorsOutsideLogWindow { get; init; } = Array.Empty<ErrLogEntry>();
    public IReadOnlyList<string> RepeatingBackgroundErrors { get; init; } = Array.Empty<string>();

    public IReadOnlyList<ChangeLogEntry> RecentSettingChanges { get; init; } = Array.Empty<ChangeLogEntry>();
    public IReadOnlyList<string> Notes { get; init; } = Array.Empty<string>();
}

public class SpidaLogAnalyserOptions
{
    /// <summary>A completion logged this soon after the start is a false start, not a real unit.</summary>
    public TimeSpan MinimumRealCycle { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>Lines either side of a fault to quote, so the fault can be read in context.</summary>
    public int ContextLines { get; set; } = 8;

    /// <summary>Settings changed within this long of the session count as "around the session".</summary>
    public int RecentChangeDays { get; set; } = 1;

    /// <summary>An error seen at least this many times is background noise rather than this fault.</summary>
    public int RepeatingErrorThreshold { get; set; } = 5;
}

/// <summary>
/// Implements the reading method from the Spida log guide: slice MachineLog.txt into the units
/// the machine attempted, pull the fault text out of each, work out which faults repeat, then
/// cross-check ErrLog.txt against Change.log and the MachineLog time window.
/// <para>
/// Deliberately machine-agnostic. Step numbering differs per machine family, so a unit boundary
/// is taken from a step counter resetting rather than from any particular step map.
/// </para>
/// </summary>
public class SpidaLogAnalyser
{
    private readonly SpidaLogAnalyserOptions _options;

    public SpidaLogAnalyser(SpidaLogAnalyserOptions? options = null) => _options = options ?? new SpidaLogAnalyserOptions();

    private static readonly Regex StepPattern = new(@"(?<name>[A-Za-z]*Step)\s*=\s*(?<value>\d+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex CompletionPattern = new(
        @"\b(Assembled|Complete[d]?|Ejected|Finished|Unloaded)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Lines that appear constantly in every export and are not this fault.</summary>
    private static readonly Regex[] NoisePatterns =
    {
        new(@"\bcomms?\b.*\btimeout\b", RegexOptions.IgnoreCase),
        new(@"\bdriver\b.*\btimeout\b", RegexOptions.IgnoreCase),
        new(@"air (supply )?pressure is low", RegexOptions.IgnoreCase),
        new(@"\b(upload|sync)\b.*\b(task|queue|background)\b", RegexOptions.IgnoreCase)
    };

    /// <summary>Descriptive Other-category text that reads like a fault rather than a counter.</summary>
    private static readonly Regex FaultWording = new(
        @"\b(cannot|can't|unable|not set ?up|failed|failure|fault|error|stopped|stop\b|lost|jam|check|press|estop|e-stop|revert|retry|try again|timeout|missing|invalid)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public SpidaLogAnalysis Analyse(
        IReadOnlyList<MachineLogEntry> machineLog,
        IReadOnlyList<ErrLogEntry> errLog,
        IReadOnlyList<ChangeLogEntry> changeLog,
        DateTime sessionDateUtc)
    {
        var notes = new List<string>();

        if (machineLog.Count == 0)
        {
            notes.Add("MachineLog.txt was empty or could not be read, so no machine behaviour could be examined.");
        }

        var cycles = SliceIntoCycles(machineLog);
        var repeated = FindRepeatedFaults(cycles);

        var logStart = machineLog.Count > 0 ? machineLog[0].Time : (TimeSpan?)null;
        var logEnd = machineLog.Count > 0 ? machineLog[^1].Time : (TimeSpan?)null;

        var (real, cosmetic, outside, backgroundNoise) = ClassifyErrors(errLog, changeLog, logStart, logEnd, sessionDateUtc, notes);

        return new SpidaLogAnalysis
        {
            MachineModelFromLog = MachineLogFile.FindMachineModel(machineLog),
            MachineLogLines = machineLog.Count,
            LogStart = logStart,
            LogEnd = logEnd,
            Cycles = cycles,
            Last = cycles.Count > 0 ? cycles[^1] : null,
            SecondToLast = cycles.Count > 1 ? cycles[^2] : null,
            RepeatedFaults = repeated,
            RealErrors = real,
            CosmeticErrors = cosmetic,
            ErrorsOutsideLogWindow = outside,
            RepeatingBackgroundErrors = backgroundNoise,
            RecentSettingChanges = RecentChanges(changeLog, sessionDateUtc),
            Notes = notes
        };
    }

    /// <summary>
    /// Cuts the log where a step counter jumps back down, which marks a new unit on every machine
    /// family even where the step numbers themselves mean different things.
    /// </summary>
    private List<MachineCycle> SliceIntoCycles(IReadOnlyList<MachineLogEntry> entries)
    {
        var boundaries = new List<int>();
        var previousStep = int.MinValue;

        for (var i = 0; i < entries.Count; i++)
        {
            if (entries[i].Category != MachineLogCategory.Other) continue;

            var step = ReadStep(entries[i]);
            if (step is null) continue;

            if (previousStep != int.MinValue && step < previousStep)
            {
                boundaries.Add(i);
            }

            previousStep = step.Value;
        }

        if (entries.Count == 0) return new List<MachineCycle>();
        if (boundaries.Count == 0) boundaries.Add(0);
        if (boundaries[0] != 0) boundaries.Insert(0, 0);

        var cycles = new List<MachineCycle>();
        for (var b = 0; b < boundaries.Count; b++)
        {
            var from = boundaries[b];
            var to = b + 1 < boundaries.Count ? boundaries[b + 1] : entries.Count;
            var slice = entries.Skip(from).Take(to - from).ToList();
            if (slice.Count == 0) continue;

            var duration = slice[^1].Time - slice[0].Time;
            var completed = slice.Any(e => CompletionPattern.IsMatch(e.Description))
                            && duration >= _options.MinimumRealCycle;

            cycles.Add(new MachineCycle
            {
                Number = cycles.Count + 1,   // renumbered below once slivers are dropped
                Start = slice[0].Time,
                End = slice[^1].Time,
                Completed = completed,
                HighestStep = slice.Select(ReadStep).Where(s => s is not null).Select(s => s!.Value).DefaultIfEmpty().Max(),
                Faults = FindFaults(entries, from, to)
            });
        }

        // A trailing step drop can leave a slice of a fraction of a second with nothing in it.
        // That is the tail of the log, not an attempt, so drop it rather than report it as one.
        var real = cycles
            .Where(c => c.Duration >= _options.MinimumRealCycle || c.Faults.Count > 0 || c.Completed)
            .ToList();

        if (real.Count == 0) real = cycles;

        return real
            .Select((c, index) => new MachineCycle
            {
                Number = index + 1,
                Start = c.Start,
                End = c.End,
                Completed = c.Completed,
                HighestStep = c.HighestStep,
                Faults = c.Faults
            })
            .ToList();
    }

    private List<MachineLogFault> FindFaults(IReadOnlyList<MachineLogEntry> all, int from, int to)
    {
        var faults = new List<MachineLogFault>();
        var currentStep = (int?)null;

        for (var i = from; i < to; i++)
        {
            var entry = all[i];
            if (entry.Category != MachineLogCategory.Other) continue;

            var step = ReadStep(entry);
            if (step is not null)
            {
                currentStep = step;
                continue;   // a step counter is not a fault
            }

            var text = entry.Description.Trim();
            if (text.Length < 8) continue;
            if (!FaultWording.IsMatch(text)) continue;
            if (NoisePatterns.Any(p => p.IsMatch(text))) continue;

            faults.Add(new MachineLogFault
            {
                Time = entry.Time,
                Text = text,
                StepAtFault = currentStep,
                Context = all
                    .Skip(Math.Max(0, i - _options.ContextLines))
                    .Take(_options.ContextLines * 2 + 1)
                    .ToList()
            });
        }

        return faults;
    }

    /// <summary>
    /// A fault at the same step across two or more attempts is a real hardware or sensor problem;
    /// one that appears once is more likely a transient.
    /// </summary>
    private static List<RepeatedFault> FindRepeatedFaults(List<MachineCycle> cycles)
    {
        return cycles
            .SelectMany(cycle => cycle.Faults.Select(fault => (cycle, fault)))
            .GroupBy(pair => pair.fault.Signature)
            .Where(group => group.Select(p => p.cycle.Number).Distinct().Count() >= 2)
            .Select(group => new RepeatedFault
            {
                Text = group.First().fault.Text,
                Occurrences = group.Count(),
                CycleNumbers = group.Select(p => p.cycle.Number).Distinct().OrderBy(n => n).ToList(),
                StepAtFault = group.Select(p => p.fault.StepAtFault).FirstOrDefault(s => s is not null)
            })
            .OrderByDescending(f => f.Occurrences)
            .ToList();
    }

    private (List<ErrLogEntry> Real, List<ErrLogEntry> Cosmetic, List<ErrLogEntry> Outside, List<string> Background)
        ClassifyErrors(
            IReadOnlyList<ErrLogEntry> errors,
            IReadOnlyList<ChangeLogEntry> changes,
            TimeSpan? logStart,
            TimeSpan? logEnd,
            DateTime sessionDateUtc,
            List<string> notes)
    {
        var real = new List<ErrLogEntry>();
        var cosmetic = new List<ErrLogEntry>();
        var outside = new List<ErrLogEntry>();

        // An error repeating this often fires every session and is not this complaint.
        var background = errors
            .GroupBy(e => e.Signature)
            .Where(g => g.Count() >= _options.RepeatingErrorThreshold)
            .Select(g => $"{g.First().Text} (x{g.Count()})")
            .ToList();

        var backgroundSignatures = errors
            .GroupBy(e => e.Signature)
            .Where(g => g.Count() >= _options.RepeatingErrorThreshold)
            .Select(g => g.Key)
            .ToHashSet();

        var changeTimes = changes.Select(c => Truncate(c.Timestamp)).ToHashSet();

        if (logStart is null || logEnd is null)
        {
            notes.Add("MachineLog.txt has no timestamps, so errors could not be placed against machine behaviour.");
        }
        else
        {
            notes.Add($"MachineLog.txt covers {logStart:hh\\:mm\\:ss} to {logEnd:hh\\:mm\\:ss}; "
                      + $"errors are matched against that window on {sessionDateUtc.ToLocalTime():yyyy-MM-dd}, "
                      + "which is taken from the file name rather than the log itself.");
        }

        foreach (var error in errors)
        {
            if (backgroundSignatures.Contains(error.Signature)) continue;

            // A settings save that really happened makes the UI error that follows it cosmetic.
            if (changeTimes.Contains(Truncate(error.Timestamp)))
            {
                cosmetic.Add(error);
                continue;
            }

            if (logStart is { } start && logEnd is { } end)
            {
                var timeOfDay = error.Timestamp.TimeOfDay;
                var sameDay = error.Timestamp.Date == sessionDateUtc.ToLocalTime().Date;

                if (!sameDay || timeOfDay < start || timeOfDay > end)
                {
                    outside.Add(error);
                    continue;
                }
            }

            real.Add(error);
        }

        return (real, cosmetic, outside, background);
    }

    private List<ChangeLogEntry> RecentChanges(IReadOnlyList<ChangeLogEntry> changes, DateTime sessionDateUtc)
    {
        var sessionLocal = sessionDateUtc.ToLocalTime();
        var from = sessionLocal.Date.AddDays(-_options.RecentChangeDays);

        return changes
            .Where(c => c.Timestamp >= from && c.Timestamp <= sessionLocal.Date.AddDays(1))
            .OrderBy(c => c.Timestamp)
            .ToList();
    }

    private static int? ReadStep(MachineLogEntry entry)
    {
        var match = StepPattern.Match($"{entry.Tag} {entry.Description}");
        return match.Success && int.TryParse(match.Groups["value"].Value, out var value) ? value : null;
    }

    private static DateTime Truncate(DateTime value) =>
        new(value.Year, value.Month, value.Day, value.Hour, value.Minute, value.Second);
}
