namespace DiagFileMonitor.Core.Models;

public enum FindingSeverity
{
    Info,
    Warning,
    Problem
}

public class AnalysisFinding
{
    public FindingSeverity Severity { get; init; }
    public string Area { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
}

public class StepComparison
{
    public string StepName { get; init; } = string.Empty;
    public TimeSpan Duration { get; init; }
    public TimeSpan? BaselineMedian { get; init; }
    public double? PercentOfBaseline { get; init; }
    public bool IsSlow { get; init; }
    public bool IsMissing { get; init; }
}

/// <summary>What the automatic analysis found for one bundle.</summary>
public class AnalysisReport
{
    public string SerialNumber { get; init; } = string.Empty;
    public string MachineType { get; init; } = string.Empty;
    public string Customer { get; init; } = string.Empty;
    public string BundleName { get; init; } = string.Empty;
    public DateTime ArrivedAtUtc { get; init; }

    public int BaselinesUsed { get; init; }
    public IReadOnlyList<AnalysisFinding> Findings { get; init; } = Array.Empty<AnalysisFinding>();
    public IReadOnlyList<StepComparison> Steps { get; init; } = Array.Empty<StepComparison>();
    public IReadOnlyList<string> NewErrorLines { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> RecentChanges { get; init; } = Array.Empty<string>();

    public bool HasProblems => Findings.Any(f => f.Severity == FindingSeverity.Problem);

    public string Headline
    {
        get
        {
            var problems = Findings.Count(f => f.Severity == FindingSeverity.Problem);
            var warnings = Findings.Count(f => f.Severity == FindingSeverity.Warning);

            if (problems == 0 && warnings == 0) return "Nothing obviously wrong found.";

            var parts = new List<string>();
            if (problems > 0) parts.Add($"{problems} problem(s)");
            if (warnings > 0) parts.Add($"{warnings} warning(s)");
            return string.Join(", ", parts) + " found.";
        }
    }
}
