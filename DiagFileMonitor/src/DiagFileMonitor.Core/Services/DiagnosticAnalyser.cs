using DiagFileMonitor.Core.Models;

namespace DiagFileMonitor.Core.Services;

public class AnalysisOptions
{
    /// <summary>A step this much slower than the baseline median is called out. 1.5 = 50% slower.</summary>
    public double SlowStepFactor { get; set; } = 1.5;

    /// <summary>Steps shorter than this are ignored, so millisecond noise does not raise findings.</summary>
    public TimeSpan IgnoreStepsShorterThan { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Changelog entries this recent are worth mentioning as a possible cause.</summary>
    public int RecentChangeDays { get; set; } = 30;

    public string? MachineLogPattern { get; set; }
}

/// <summary>
/// Compares one bundle against the bundles marked as known-good baselines for the same machine
/// type: step timings from machinelog.txt, error lines that baselines do not have, and recent
/// changelog entries that might explain a new fault.
/// </summary>
public class DiagnosticAnalyser
{
    private readonly AnalysisOptions _options;
    private readonly MachineLogParser _parser;

    public DiagnosticAnalyser(AnalysisOptions? options = null)
    {
        _options = options ?? new AnalysisOptions();
        _parser = new MachineLogParser(_options.MachineLogPattern);
    }

    public AnalysisReport Analyse(DiagnosticFile bundle, IEnumerable<DiagnosticFile> baselines)
    {
        var relevantBaselines = baselines
            .Where(b => b.IsBaseline && b.Id != bundle.Id)
            .Where(b => string.Equals(b.MachineType, bundle.MachineType, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var findings = new List<AnalysisFinding>();
        var steps = CompareSteps(bundle, relevantBaselines, findings);
        var newErrors = CompareErrors(bundle, relevantBaselines, findings);
        var recentChanges = FindRecentChanges(bundle, findings);

        if (relevantBaselines.Count == 0)
        {
            findings.Add(new AnalysisFinding
            {
                Severity = FindingSeverity.Info,
                Area = "Baselines",
                Detail = $"No known-good baseline is marked for machine type '{Display(bundle.MachineType)}', "
                         + "so timings could not be compared. Mark a healthy machine's bundle as a baseline to enable this."
            });
        }

        return new AnalysisReport
        {
            SerialNumber = Display(bundle.SerialNumber),
            MachineType = Display(bundle.MachineType),
            Customer = Display(bundle.Customer),
            BundleName = bundle.OriginalFileName,
            ArrivedAtUtc = bundle.ArrivedAtUtc,
            BaselinesUsed = relevantBaselines.Count,
            Findings = findings,
            Steps = steps,
            NewErrorLines = newErrors,
            RecentChanges = recentChanges
        };
    }

    private IReadOnlyList<StepComparison> CompareSteps(
        DiagnosticFile bundle, List<DiagnosticFile> baselines, List<AnalysisFinding> findings)
    {
        var steps = ParseSteps(bundle);
        if (steps.Count == 0)
        {
            findings.Add(new AnalysisFinding
            {
                Severity = FindingSeverity.Info,
                Area = "Machine log",
                Detail = "No timed steps could be read from machinelog.txt. If the machine writes a different "
                         + "line format, set MachineLogPattern in settings.json to match it."
            });
            return Array.Empty<StepComparison>();
        }

        // Median per step across baselines, so one unusually slow reference run cannot skew things.
        var baselineDurations = new Dictionary<string, List<TimeSpan>>(StringComparer.OrdinalIgnoreCase);
        foreach (var baseline in baselines)
        {
            foreach (var step in ParseSteps(baseline))
            {
                if (!baselineDurations.TryGetValue(step.Name, out var list))
                {
                    baselineDurations[step.Name] = list = new List<TimeSpan>();
                }
                list.Add(step.Duration);
            }
        }

        var comparisons = new List<StepComparison>();
        foreach (var step in steps)
        {
            TimeSpan? median = baselineDurations.TryGetValue(step.Name, out var durations) && durations.Count > 0
                ? Median(durations)
                : null;

            var slow = median is { } m
                       && m > TimeSpan.Zero
                       && step.Duration >= _options.IgnoreStepsShorterThan
                       && step.Duration.TotalMilliseconds >= m.TotalMilliseconds * _options.SlowStepFactor;

            comparisons.Add(new StepComparison
            {
                StepName = step.Name,
                Duration = step.Duration,
                BaselineMedian = median,
                PercentOfBaseline = median is { } b && b > TimeSpan.Zero
                    ? Math.Round(step.Duration.TotalMilliseconds / b.TotalMilliseconds * 100, 0)
                    : null,
                IsSlow = slow
            });

            if (slow)
            {
                findings.Add(new AnalysisFinding
                {
                    Severity = FindingSeverity.Problem,
                    Area = "Step timing",
                    Detail = $"Step '{step.Name}' took {Describe(step.Duration)} against a baseline of "
                             + $"{Describe(median!.Value)} ({comparisons[^1].PercentOfBaseline}% of normal)."
                });
            }
        }

        // A step the baselines run but this machine never reached is worth knowing about.
        foreach (var missing in baselineDurations.Keys.Except(steps.Select(s => s.Name), StringComparer.OrdinalIgnoreCase))
        {
            comparisons.Add(new StepComparison { StepName = missing, IsMissing = true, BaselineMedian = Median(baselineDurations[missing]) });
            findings.Add(new AnalysisFinding
            {
                Severity = FindingSeverity.Problem,
                Area = "Step missing",
                Detail = $"Step '{missing}' runs on known-good machines but did not complete here."
            });
        }

        return comparisons;
    }

    private IReadOnlyList<string> CompareErrors(
        DiagnosticFile bundle, List<DiagnosticFile> baselines, List<AnalysisFinding> findings)
    {
        var errors = ReadLines(bundle, LogFileKind.ErrorLog);
        if (errors.Count == 0) return Array.Empty<string>();

        var baselineErrors = baselines
            .SelectMany(b => ReadLines(b, LogFileKind.ErrorLog))
            .Select(Normalise)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var newLines = errors
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Where(line => !baselineErrors.Contains(Normalise(line)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (newLines.Count > 0)
        {
            findings.Add(new AnalysisFinding
            {
                Severity = FindingSeverity.Warning,
                Area = "Error log",
                Detail = $"{newLines.Count} error line(s) appear here that known-good bundles do not have."
            });
        }

        return newLines;
    }

    private IReadOnlyList<string> FindRecentChanges(DiagnosticFile bundle, List<AnalysisFinding> findings)
    {
        var lines = ReadLines(bundle, LogFileKind.ChangeLog);
        if (lines.Count == 0) return Array.Empty<string>();

        var cutoff = bundle.ArrivedAtUtc.AddDays(-_options.RecentChangeDays);

        var recent = lines
            .Where(line => TryReadLeadingDate(line, out var date) && date >= cutoff)
            .ToList();

        if (recent.Count > 0)
        {
            findings.Add(new AnalysisFinding
            {
                Severity = FindingSeverity.Warning,
                Area = "Recent change",
                Detail = $"{recent.Count} change(s) were made in the {_options.RecentChangeDays} days before this "
                         + "bundle arrived, which may be related."
            });
        }

        return recent;
    }

    private IReadOnlyList<MachineLogStep> ParseSteps(DiagnosticFile bundle)
    {
        var path = bundle.LogFiles.FirstOrDefault(l => l.Kind == LogFileKind.MachineLog)?.FullPath;
        return path is null ? Array.Empty<MachineLogStep>() : _parser.ParseFile(path);
    }

    private static List<string> ReadLines(DiagnosticFile bundle, LogFileKind kind)
    {
        var path = bundle.LogFiles.FirstOrDefault(l => l.Kind == kind)?.FullPath;
        if (path is null || !File.Exists(path)) return new List<string>();

        try
        {
            return File.ReadAllLines(path).ToList();
        }
        catch (IOException ex)
        {
            SimpleLogger.Error($"Could not read '{path}' for analysis", ex);
            return new List<string>();
        }
    }

    /// <summary>Strips timestamps and digits so the same fault logged at a different time still matches.</summary>
    private static string Normalise(string line) =>
        string.Concat(line.Trim().Where(c => !char.IsDigit(c))).Replace("  ", " ");

    private static bool TryReadLeadingDate(string line, out DateTime date)
    {
        var trimmed = line.TrimStart();
        var candidate = trimmed.Length >= 10 ? trimmed[..10] : trimmed;
        return DateTime.TryParse(candidate, out date);
    }

    private static TimeSpan Median(List<TimeSpan> values)
    {
        var ordered = values.OrderBy(v => v).ToList();
        var middle = ordered.Count / 2;

        return ordered.Count % 2 == 1
            ? ordered[middle]
            : TimeSpan.FromTicks((ordered[middle - 1].Ticks + ordered[middle].Ticks) / 2);
    }

    private static string Describe(TimeSpan span) =>
        span.TotalMinutes >= 1
            ? $"{span.TotalMinutes:0.#} min"
            : $"{span.TotalSeconds:0.#} s";

    private static string Display(string? value) =>
        string.IsNullOrWhiteSpace(value) ? DiagnosticFileSummary.Unknown : value;
}
