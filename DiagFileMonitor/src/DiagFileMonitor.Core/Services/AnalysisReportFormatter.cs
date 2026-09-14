using System.Text;
using DiagFileMonitor.Core.Models;

namespace DiagFileMonitor.Core.Services;

/// <summary>Renders an analysis report for a Zoho ticket comment and for the alert email.</summary>
public static class AnalysisReportFormatter
{
    private const int MaxListedLines = 15;

    public static string Subject(BurstResult burst, AnalysisReport report) =>
        $"[Diag alert] {burst.SerialNumber} - {report.Customer} - {burst.BundleCount} files in {burst.WindowHours}h";

    public static string TicketSubject(BurstResult burst, AnalysisReport report) =>
        $"Repeated diagnostics: {burst.SerialNumber} ({report.MachineType}) - {report.Customer}";

    public static string Format(BurstResult burst, AnalysisReport report)
    {
        var text = new StringBuilder();

        text.AppendLine(burst.Headline + ".");
        text.AppendLine();
        text.AppendLine($"Machine type: {report.MachineType}");
        text.AppendLine($"Serial:       {report.SerialNumber}");
        text.AppendLine($"Customer:     {report.Customer}");
        text.AppendLine($"Latest file:  {report.BundleName} ({report.ArrivedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm})");
        text.AppendLine();

        text.AppendLine("Files in this burst:");
        foreach (var bundle in burst.Bundles)
        {
            text.AppendLine($"  - {bundle.ArrivedDisplay}  {bundle.OriginalFileName}  [{bundle.Status}]");
        }
        text.AppendLine();

        text.AppendLine($"Automatic analysis: {report.Headline}");
        if (report.BaselinesUsed > 0)
        {
            text.AppendLine($"Compared against {report.BaselinesUsed} known-good baseline(s) for this machine type.");
        }
        text.AppendLine();

        AppendFindings(text, report);
        AppendStepTimings(text, report);
        AppendList(text, "Error lines not seen in known-good bundles", report.NewErrorLines);
        AppendList(text, "Changes made shortly before this bundle", report.RecentChanges);

        text.AppendLine();
        text.AppendLine("-- Raised automatically by Diagnostic File Monitor.");

        return text.ToString();
    }

    private static void AppendFindings(StringBuilder text, AnalysisReport report)
    {
        if (report.Findings.Count == 0) return;

        text.AppendLine("Findings");
        text.AppendLine("--------");

        foreach (var finding in report.Findings.OrderByDescending(f => f.Severity))
        {
            text.AppendLine($"  [{finding.Severity.ToString().ToUpperInvariant()}] {finding.Area}: {finding.Detail}");
        }

        text.AppendLine();
    }

    private static void AppendStepTimings(StringBuilder text, AnalysisReport report)
    {
        if (report.Steps.Count == 0) return;

        text.AppendLine("Step timings vs baseline");
        text.AppendLine("------------------------");

        foreach (var step in report.Steps)
        {
            if (step.IsMissing)
            {
                text.AppendLine($"  {step.StepName,-28} did not complete (baseline {Describe(step.BaselineMedian)})");
                continue;
            }

            var comparison = step.PercentOfBaseline is { } percent
                ? $"{Describe(step.BaselineMedian)} baseline, {percent}%"
                : "no baseline";

            var marker = step.IsSlow ? "  <-- slow" : string.Empty;
            text.AppendLine($"  {step.StepName,-28} {Describe(step.Duration),-10} ({comparison}){marker}");
        }

        text.AppendLine();
    }

    private static void AppendList(StringBuilder text, string heading, IReadOnlyList<string> lines)
    {
        if (lines.Count == 0) return;

        text.AppendLine(heading);
        text.AppendLine(new string('-', heading.Length));

        foreach (var line in lines.Take(MaxListedLines))
        {
            text.AppendLine($"  {line.Trim()}");
        }

        if (lines.Count > MaxListedLines)
        {
            text.AppendLine($"  ... and {lines.Count - MaxListedLines} more.");
        }

        text.AppendLine();
    }

    private static string Describe(TimeSpan? span) =>
        span is not { } value
            ? "-"
            : value.TotalMinutes >= 1
                ? $"{value.TotalMinutes:0.#} min"
                : $"{value.TotalSeconds:0.#} s";
}
