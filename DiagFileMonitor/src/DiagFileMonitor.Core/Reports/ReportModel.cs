namespace DiagFileMonitor.Core.Reports;

/// <summary>
/// What a report is, before anything decides how it looks. Builders fill this in from stored
/// bundles; renderers turn it into HTML or plain text.
/// <para>
/// Nothing here knows about HTML, and nothing in the renderer reads a log. If a number is not
/// on this model it does not reach the page, which keeps the two sides from drifting apart.
/// </para>
/// </summary>
public class ReportModel
{
    /// <summary>Report title, e.g. "Fault Benchmarking - PlatePresentSwitch".</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>The line under the title: machines, site, period.</summary>
    public string Subtitle { get; init; } = string.Empty;

    /// <summary>Marked on every internal report so it is not forwarded to a customer by accident.</summary>
    public bool InternalUseOnly { get; init; } = true;

    public DateTime PreparedUtc { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// The period the figures cover, spelled out. A period-filtered report must never present an
    /// all-time total as if it were the period, so the header states it and the builders set it.
    /// </summary>
    public ReportPeriod? Period { get; init; }

    public List<ReportSection> Sections { get; init; } = new();

    /// <summary>Small print at the bottom - data sources, caveats, what is still sample data.</summary>
    public string Footer { get; init; } = string.Empty;
}

/// <summary>The window a report covers, for the header line and the "not an all-time total" rule.</summary>
public record ReportPeriod(DateTime FromUtc, DateTime ToUtc, string Label)
{
    public int Days => Math.Max(1, (int)Math.Round((ToUtc - FromUtc).TotalDays));

    /// <summary>
    /// The window in words. The year is repeated on the start date when the period crosses one,
    /// so "28 Feb - 29 Jul 2026" can never be read as five months when it was really sixteen.
    /// </summary>
    public string Describe()
    {
        var from = FromUtc.Year == ToUtc.Year ? $"{FromUtc:d MMM}" : $"{FromUtc:d MMM yyyy}";
        return $"{from} - {ToUtc:d MMM yyyy} ({Days} days)";
    }
}

public class ReportSection
{
    public string Title { get; init; } = string.Empty;

    /// <summary>Optional line under the section heading explaining what the reader is looking at.</summary>
    public string Subtitle { get; init; } = string.Empty;

    public List<ReportBlock> Blocks { get; init; } = new();
}
