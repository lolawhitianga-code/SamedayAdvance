using System.Globalization;
using System.Net;
using System.Text;

namespace DiagFileMonitor.Core.Reports;

public class ReportHtmlOptions
{
    public ReportPalette Palette { get; init; } = ReportPalette.FromLogo;

    /// <summary>
    /// Pull Zilla Slab, Nunito and IBM Plex Mono from the Google Fonts CDN. Off by default: a
    /// report saved to a factory PC and opened from file:// with no internet gets nothing from a
    /// CDN, and the local font stack is what it would fall back to anyway. Turn it on for a
    /// report that will only ever be read on a connected machine.
    /// </summary>
    public bool UseWebFonts { get; init; }

    public string Wordmark { get; init; } = "SPIDA";
    public string WordmarkTail { get; init; } = "MACHINERY";
}

/// <summary>
/// Turns a <see cref="ReportModel"/> into one self-contained HTML file.
/// <para>
/// Self-contained means it: no CDN, no external stylesheet, no fetch(). Charts are drawn as
/// inline SVG rather than with a charting library, because a library loaded over https renders
/// an empty box on an offline factory PC - which is precisely where these get opened.
/// </para>
/// </summary>
public class ReportHtmlRenderer
{
    private readonly ReportHtmlOptions _options;

    public ReportHtmlRenderer(ReportHtmlOptions? options = null) => _options = options ?? new ReportHtmlOptions();

    public string Render(ReportModel report)
    {
        var sb = new StringBuilder();

        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"en-NZ\">");
        sb.AppendLine("<head>");
        sb.AppendLine("<meta charset=\"UTF-8\">");
        sb.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">");
        sb.AppendLine($"<title>{E(report.Title)}</title>");
        if (_options.UseWebFonts) sb.AppendLine(ReportStyle.WebFontLink);
        sb.AppendLine("<style>");
        sb.Append(ReportStyle.Css(_options.Palette));
        sb.AppendLine("</style>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");

        RenderHeader(sb, report);

        sb.AppendLine("<div class=\"wrap\">");
        foreach (var section in report.Sections) RenderSection(sb, section);

        if (!string.IsNullOrWhiteSpace(report.Footer))
            sb.AppendLine($"  <footer>{E(report.Footer)}</footer>");

        sb.AppendLine("</div>");
        sb.AppendLine("</body>");
        sb.AppendLine("</html>");

        return sb.ToString();
    }

    private void RenderHeader(StringBuilder sb, ReportModel report)
    {
        // The period goes in the header so a week's figures can never be read as an all-time total.
        var meta = report.Subtitle;
        if (report.Period is { } period)
        {
            var describe = $"{period.Label} · {period.Describe()}";
            meta = string.IsNullOrWhiteSpace(meta) ? describe : $"{meta} · {describe}";
        }

        meta = string.IsNullOrWhiteSpace(meta)
            ? $"Prepared {report.PreparedUtc:d MMM yyyy}"
            : $"{meta} · Prepared {report.PreparedUtc:d MMM yyyy}";

        sb.AppendLine("<header>");
        sb.AppendLine("  <div class=\"header-inner\">");
        sb.AppendLine("    <div>");
        sb.AppendLine($"      <div class=\"wordmark\">{E(_options.Wordmark)} <span>{E(_options.WordmarkTail)}</span></div>");
        sb.AppendLine($"      <h1>{E(report.Title)}</h1>");
        sb.AppendLine($"      <div class=\"meta\">{E(meta)}</div>");
        sb.AppendLine("    </div>");
        if (report.InternalUseOnly) sb.AppendLine("    <div class=\"tag\">INTERNAL USE ONLY</div>");
        sb.AppendLine("  </div>");
        sb.AppendLine("</header>");
    }

    private void RenderSection(StringBuilder sb, ReportSection section)
    {
        sb.AppendLine("  <section>");
        if (!string.IsNullOrWhiteSpace(section.Title)) sb.AppendLine($"    <h2>{E(section.Title)}</h2>");
        if (!string.IsNullOrWhiteSpace(section.Subtitle)) sb.AppendLine($"    <div class=\"sub\">{E(section.Subtitle)}</div>");
        foreach (var block in section.Blocks) RenderBlock(sb, block);
        sb.AppendLine("  </section>");
    }

    private void RenderBlock(StringBuilder sb, ReportBlock block)
    {
        switch (block)
        {
            case TextBlock t:
                foreach (var para in Paragraphs(t.Text)) sb.AppendLine($"    <p>{E(para)}</p>");
                break;

            case BulletsBlock b when b.Items.Count > 0:
                sb.AppendLine("    <ul>");
                foreach (var item in b.Items) sb.AppendLine($"      <li>{E(item)}</li>");
                sb.AppendLine("    </ul>");
                break;

            case StepsBlock s:
                for (var i = 0; i < s.Steps.Count; i++)
                    sb.AppendLine($"    <div class=\"step\"><div class=\"n\">{i + 1}</div><div>{E(s.Steps[i])}</div></div>");
                break;

            case HeroBlock h:
                sb.AppendLine("    <div class=\"hero\">");
                sb.AppendLine($"      <div class=\"num\">{E(h.Figure)}</div>");
                sb.AppendLine($"      <div class=\"label\">{E(h.Label)}</div>");
                sb.AppendLine("    </div>");
                if (!string.IsNullOrWhiteSpace(h.Subline))
                    sb.AppendLine($"    <div class=\"hero-sub\">{E(h.Subline)}</div>");
                break;

            case CalloutBlock c:
                var tone = c.Tone == CalloutTone.Caution ? " caution" : string.Empty;
                var lead = string.IsNullOrWhiteSpace(c.Lead) ? string.Empty : $"<b>{E(c.Lead)}</b> ";
                sb.AppendLine($"    <div class=\"callout{tone}\">{lead}{E(c.Text)}</div>");
                break;

            case NoteBlock n:
                sb.AppendLine($"    <div class=\"note\">{E(n.Text)}</div>");
                break;

            case PendingBlock p:
                sb.AppendLine($"    <div class=\"pending\"><b>{E(p.Lead)}</b> {E(p.Text)}</div>");
                break;

            case TableBlock tb:
                RenderTable(sb, tb);
                break;

            case BarChartBlock chart when chart.Bars.Count > 0:
                sb.AppendLine("    <div class=\"chart\">");
                sb.Append(BarChartSvg.Render(chart, _options.Palette));
                sb.AppendLine("    </div>");
                break;

            case LogExtractBlock x when x.Lines.Count > 0:
                if (!string.IsNullOrWhiteSpace(x.Caption))
                    sb.AppendLine($"    <div class=\"extract-cap\">{E(x.Caption)}</div>");
                sb.AppendLine("    <div class=\"extract\">");
                for (var i = 0; i < x.Lines.Count; i++)
                {
                    var hit = i == x.HighlightIndex ? " class=\"hit\"" : string.Empty;
                    sb.AppendLine($"<span{hit}>{E(x.Lines[i])}</span>");
                }
                sb.AppendLine("    </div>");
                break;
        }
    }

    private void RenderTable(StringBuilder sb, TableBlock table)
    {
        if (table.Rows.Count == 0)
        {
            sb.AppendLine($"    <div class=\"empty\">{E(table.EmptyText)}</div>");
            return;
        }

        if (table.Scroll) sb.AppendLine("    <div class=\"scroll\">");

        // Fixed layout wherever widths are given, so one long error message cannot starve every
        // other column of space.
        var fixedWidths = table.Columns.Any(c => c.WidthPercent > 0);
        sb.AppendLine(fixedWidths ? "    <table class=\"fixed\">" : "    <table>");

        if (fixedWidths)
        {
            sb.Append("      <colgroup>");
            foreach (var column in table.Columns)
            {
                sb.Append(column.WidthPercent > 0 ? $"<col style=\"width:{column.WidthPercent}%\">" : "<col>");
            }
            sb.AppendLine("</colgroup>");
        }

        if (table.Columns.Count > 0)
        {
            sb.Append("      <tr>");
            foreach (var column in table.Columns)
                sb.Append($"<th class=\"{CellClass(column.Style)}\">{E(column.Heading)}</th>");
            sb.AppendLine("</tr>");
        }

        foreach (var row in table.Rows)
        {
            sb.Append(row.Muted ? "      <tr class=\"muted\">" : "      <tr>");
            for (var i = 0; i < row.Cells.Count; i++)
            {
                var style = i < table.Columns.Count ? table.Columns[i].Style : ColumnStyle.Text;
                sb.Append($"<td class=\"{CellClass(style)}\">{Cell(row.Cells[i])}</td>");
            }
            sb.AppendLine("</tr>");
        }

        sb.AppendLine("    </table>");
        if (table.Scroll) sb.AppendLine("    </div>");
    }

    /// <summary>
    /// A cell's second and later lines are shown small underneath the first, so a machine and its
    /// site can share one column instead of costing two. The text is still escaped - only the
    /// line break is markup.
    /// </summary>
    private static string Cell(string value)
    {
        var lines = value.Replace("\r\n", "\n").Split('\n');
        if (lines.Length == 1) return E(value);

        return E(lines[0])
               + $"<div class=\"sub-cell\">{string.Join("<br>", lines.Skip(1).Select(E))}</div>";
    }

    private static string CellClass(ColumnStyle style) => style switch
    {
        ColumnStyle.Data => "data",
        ColumnStyle.Timestamp => "data stamp",
        ColumnStyle.Number => "num",
        _ => "text"
    };

    private static IEnumerable<string> Paragraphs(string text) =>
        text.Replace("\r\n", "\n").Split("\n\n", StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Trim())
            .Where(p => p.Length > 0);

    internal static string E(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);

    internal static string N(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
