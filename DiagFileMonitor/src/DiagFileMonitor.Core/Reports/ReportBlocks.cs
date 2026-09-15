namespace DiagFileMonitor.Core.Reports;

/// <summary>One piece of a report section. The set is deliberately small - it covers the house
/// style the sample reports are built in, and nothing more.</summary>
public abstract class ReportBlock
{
}

/// <summary>A paragraph or two of plain prose.</summary>
public class TextBlock : ReportBlock
{
    public string Text { get; init; } = string.Empty;
}

/// <summary>Bulleted findings.</summary>
public class BulletsBlock : ReportBlock
{
    public List<string> Items { get; init; } = new();
}

/// <summary>Numbered steps, as used for a proposed change.</summary>
public class StepsBlock : ReportBlock
{
    public List<string> Steps { get; init; } = new();
}

/// <summary>A single large figure with a label beside it, and optionally a line underneath.</summary>
public class HeroBlock : ReportBlock
{
    public string Figure { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public string Subline { get; init; } = string.Empty;
}

public enum CalloutTone
{
    /// <summary>Blue. A reading of the data that needs saying out loud.</summary>
    Reading,

    /// <summary>Amber. Something the reader should treat with care - an estimate, a gap, a caveat.</summary>
    Caution
}

/// <summary>A boxed note for a finding that needs interpretation beyond the raw numbers.</summary>
public class CalloutBlock : ReportBlock
{
    public CalloutTone Tone { get; init; } = CalloutTone.Reading;

    /// <summary>Lead-in shown in bold, e.g. "Reading:" or "Harder to quantify:".</summary>
    public string Lead { get; init; } = string.Empty;

    public string Text { get; init; } = string.Empty;
}

/// <summary>Small grey print under a block - a methodology aside or a pending-data note.</summary>
public class NoteBlock : ReportBlock
{
    public string Text { get; init; } = string.Empty;
}

public enum ColumnStyle
{
    /// <summary>Ordinary prose column.</summary>
    Text,

    /// <summary>Monospaced, for serials and log text that should line up. Wraps.</summary>
    Data,

    /// <summary>Monospaced and never wrapped - a timestamp broken over three lines is unreadable.</summary>
    Timestamp,

    /// <summary>Monospaced and right-aligned, for counts and durations.</summary>
    Number
}

/// <summary>
/// A table column. <paramref name="WidthPercent"/> is worth setting on any table with more than
/// about four columns: without it the browser hands the width to whichever column has the longest
/// unbroken word, and the rest are squeezed to a letter apiece.
/// </summary>
public record ReportColumn(string Heading, ColumnStyle Style = ColumnStyle.Text, int WidthPercent = 0);

/// <summary>One table row. <see cref="Muted"/> marks a row with no data yet, shown as pending
/// rather than dropped - a machine in scope must appear even when its figures are missing.</summary>
public class ReportRow
{
    public List<string> Cells { get; init; } = new();
    public bool Muted { get; init; }
}

public class TableBlock : ReportBlock
{
    public List<ReportColumn> Columns { get; init; } = new();
    public List<ReportRow> Rows { get; init; } = new();

    /// <summary>
    /// Put a long table in a fixed-height scroller. Occurrence lists are shown in full rather
    /// than truncated, so a 200-row fault log needs somewhere to go.
    /// </summary>
    public bool Scroll { get; init; }

    /// <summary>Shown in place of the table when there are no rows.</summary>
    public string EmptyText { get; init; } = "Nothing recorded.";
}

public record BarChartBar(string Label, string SubLabel, double Value, BarTone Tone = BarTone.Normal);

public enum BarTone
{
    Normal,
    Warning,
    Bad,

    /// <summary>Greyed out - a machine in scope with nothing recorded.</summary>
    Absent
}

/// <summary>
/// A bar chart, drawn as inline SVG rather than a charting library. The sample reports load
/// Chart.js from a CDN, which renders nothing on a factory PC opened from file:// with no
/// internet - and offline portability is the whole point of a single-file report.
/// </summary>
public class BarChartBlock : ReportBlock
{
    public List<BarChartBar> Bars { get; init; } = new();
    public string ValueSuffix { get; init; } = string.Empty;

    /// <summary>
    /// Scale the axis to the 97th percentile of the values rather than the maximum, so one
    /// outlier does not flatten everything else. Bars above the top are drawn full height and
    /// keep their real label.
    /// </summary>
    public bool ScaleToPercentile { get; init; } = true;
}

/// <summary>
/// Quoted log lines with their surrounding context. Line numbers are deliberately not shown -
/// they come from filtered excerpts and do not match positions in the real file.
/// </summary>
public class LogExtractBlock : ReportBlock
{
    public string Caption { get; init; } = string.Empty;
    public List<string> Lines { get; init; } = new();

    /// <summary>Index into <see cref="Lines"/> of the line the extract is about.</summary>
    public int HighlightIndex { get; init; } = -1;
}

/// <summary>A dashed box listing material that has to be gathered before the report is complete.</summary>
public class PendingBlock : ReportBlock
{
    public string Lead { get; init; } = "Still needed:";
    public string Text { get; init; } = string.Empty;
}
