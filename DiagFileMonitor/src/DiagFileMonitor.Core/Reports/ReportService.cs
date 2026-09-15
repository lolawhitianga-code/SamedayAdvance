using System.Text;
using DiagFileMonitor.Core.Services;

namespace DiagFileMonitor.Core.Reports;

public enum ReportKind
{
    /// <summary>Report 5 - every fault occurrence across the fleet, then the pattern.</summary>
    FaultBenchmarking,

    /// <summary>Report 6 - the case for a mechanical change, built on report 5's evidence.</summary>
    MechanicalChangeCase
}

public class ReportRequest
{
    public ReportKind Kind { get; init; } = ReportKind.FaultBenchmarking;

    /// <summary>What the report is about, e.g. "PlatePresentSwitch". Goes in the title.</summary>
    public string Subject { get; init; } = string.Empty;

    /// <summary>How the period should be described in the header, e.g. "Last 30 days".</summary>
    public string PeriodLabel { get; init; } = "Period";

    public FleetScanRequest Scope { get; init; } = new();

    /// <summary>Only read for a change case. Ignored otherwise.</summary>
    public ChangeCaseInputs ChangeCase { get; init; } = new();

    public ReportHtmlOptions Html { get; init; } = new();
}

public class GeneratedReport
{
    public ReportModel Model { get; init; } = new();
    public string Html { get; init; } = string.Empty;
    public FleetScanResult Scan { get; init; } = new();

    /// <summary>A sensible name to save it under.</summary>
    public string SuggestedFileName { get; init; } = "report.html";
}

/// <summary>
/// Builds a report end to end: scan the stored bundles, shape the findings, render the HTML.
/// <para>
/// Everything on the page comes from the analysis the app already runs. The renderer never opens
/// a log, so the report and the on-screen analysis cannot quietly disagree about what a bundle
/// said.
/// </para>
/// </summary>
public class ReportService
{
    private readonly FleetFaultScanner _scanner;

    public ReportService(DiagFileRepository repository, MachineInventory? inventory = null)
        : this(new FleetFaultScanner(repository, inventory))
    {
    }

    public ReportService(FleetFaultScanner scanner) => _scanner = scanner;

    public async Task<GeneratedReport> GenerateAsync(ReportRequest request, CancellationToken token = default)
    {
        var scan = await _scanner.ScanAsync(request.Scope, token);
        return Render(request, scan);
    }

    public GeneratedReport Render(ReportRequest request, FleetScanResult scan)
    {
        var period = PeriodFor(request, scan);

        var model = request.Kind switch
        {
            ReportKind.MechanicalChangeCase =>
                MechanicalChangeCaseReport.Build(scan, period, request.Subject, request.ChangeCase),
            _ => FaultBenchmarkReport.Build(scan, period, request.Subject)
        };

        return new GeneratedReport
        {
            Model = model,
            Scan = scan,
            Html = new ReportHtmlRenderer(request.Html).Render(model),
            SuggestedFileName = FileName(request, period)
        };
    }

    /// <summary>
    /// The window the figures actually cover. Where the scope did not pin one down, the dates of
    /// the bundles that were read are used - so the header never claims a month it did not read.
    /// </summary>
    private static ReportPeriod PeriodFor(ReportRequest request, FleetScanResult scan)
    {
        var from = request.Scope.FromUtc;
        var to = request.Scope.ToUtc;

        if (from is null && scan.Occurrences.Count > 0) from = scan.Occurrences.Min(o => o.WhenUtc).Date;
        if (to is null && scan.Occurrences.Count > 0) to = scan.Occurrences.Max(o => o.WhenUtc).Date.AddDays(1);

        return new ReportPeriod(from ?? DateTime.UtcNow.Date, to ?? DateTime.UtcNow.Date, request.PeriodLabel);
    }

    public static async Task SaveAsync(GeneratedReport report, string path, CancellationToken token = default)
    {
        var folder = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);

        // UTF-8 without a BOM: a BOM ahead of the doctype trips some older viewers.
        await File.WriteAllTextAsync(path, report.Html, new UTF8Encoding(false), token);
    }

    private static string FileName(ReportRequest request, ReportPeriod period)
    {
        var kind = request.Kind == ReportKind.MechanicalChangeCase ? "design-case" : "fault-benchmark";
        var subject = Slug(request.Subject);
        var stamp = period.ToUtc.ToString("yyyy-MM-dd");

        return subject.Length > 0 ? $"{kind}-{subject}-{stamp}.html" : $"{kind}-{stamp}.html";
    }

    private static string Slug(string value)
    {
        var chars = value.Trim().ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '-')
            .ToArray();

        return string.Join('-', new string(chars).Split('-', StringSplitOptions.RemoveEmptyEntries));
    }
}
