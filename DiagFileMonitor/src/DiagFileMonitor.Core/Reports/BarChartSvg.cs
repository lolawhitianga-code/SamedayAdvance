using System.Globalization;
using System.Text;

namespace DiagFileMonitor.Core.Reports;

/// <summary>
/// Draws a bar chart as inline SVG. No script, no CDN, no canvas - it renders in an email
/// preview, in a print-to-PDF, and on a factory PC with the network unplugged.
/// <para>
/// Conventions: bars are anchored to the baseline with rounded tops, separated by a surface gap
/// so adjacent fills never touch, and every bar carries its own value so identity never rests on
/// colour alone. Grid and axis are recessive. Bar colour encodes a state - worst, elevated, none
/// recorded - not a series, and the same numbers appear in the table beside the chart.
/// </para>
/// </summary>
internal static class BarChartSvg
{
    private const int Width = 800;
    private const int PlotHeight = 170;
    private const int TopPad = 22;      // room for the value label above the tallest bar
    private const int LabelHeight = 40; // two lines of x-axis label
    private const int LeftPad = 44;     // room for y-axis numbers
    private const int RightPad = 8;
    private const int BarGap = 2;       // the surface gap between adjacent fills
    private const int MaxBarWidth = 56;

    public static string Render(BarChartBlock chart, ReportPalette palette)
    {
        var bars = chart.Bars;
        var height = TopPad + PlotHeight + LabelHeight;
        var plotWidth = Width - LeftPad - RightPad;
        var baseline = TopPad + PlotHeight;

        var axisTop = AxisTop(bars.Select(b => b.Value).ToList(), chart.ScaleToPercentile);
        var ticks = Ticks(axisTop);

        var slot = plotWidth / (double)bars.Count;
        var barWidth = Math.Min(MaxBarWidth, Math.Max(6, slot - BarGap * 2));

        var sb = new StringBuilder();
        sb.AppendLine($"      <svg viewBox=\"0 0 {Width} {height}\" role=\"img\" "
                      + $"aria-label=\"{ReportHtmlRenderer.E(Describe(chart))}\">");

        // Grid first, so the bars sit over it.
        foreach (var tick in ticks)
        {
            var y = baseline - tick / axisTop * PlotHeight;
            sb.AppendLine($"        <line class=\"grid\" x1=\"{LeftPad}\" y1=\"{F(y)}\" "
                          + $"x2=\"{Width - RightPad}\" y2=\"{F(y)}\" />");
            sb.AppendLine($"        <text x=\"{LeftPad - 8}\" y=\"{F(y + 4)}\" text-anchor=\"end\" "
                          + $"font-size=\"11\">{ReportHtmlRenderer.N(tick)}</text>");
        }

        sb.AppendLine($"        <line class=\"axis\" x1=\"{LeftPad}\" y1=\"{baseline}\" "
                      + $"x2=\"{Width - RightPad}\" y2=\"{baseline}\" />");

        for (var i = 0; i < bars.Count; i++)
        {
            var bar = bars[i];
            var centre = LeftPad + slot * (i + 0.5);
            var x = centre - barWidth / 2;

            // A bar past the axis top is drawn full height rather than clipped off the chart.
            // Its label still carries the real number, so the value is never misread.
            var fraction = axisTop <= 0 ? 0 : Math.Min(1, bar.Value / axisTop);
            var barHeight = fraction * PlotHeight;
            var fill = Colour(bar.Tone, palette);
            var label = ReportHtmlRenderer.N(bar.Value) + chart.ValueSuffix;

            if (barHeight >= 1)
            {
                sb.AppendLine($"        <path d=\"{RoundedTopBar(x, baseline - barHeight, barWidth, barHeight)}\" "
                              + $"fill=\"{fill}\"><title>{ReportHtmlRenderer.E($"{bar.Label}: {label}")}</title></path>");
            }
            else
            {
                // Nothing recorded still gets a mark, so the machine reads as in scope with zero
                // rather than as missing from the chart.
                sb.AppendLine($"        <rect x=\"{F(x)}\" y=\"{F(baseline - 2)}\" width=\"{F(barWidth)}\" "
                              + $"height=\"2\" fill=\"{palette.BarAbsent}\"><title>"
                              + $"{ReportHtmlRenderer.E($"{bar.Label}: {label}")}</title></rect>");
            }

            sb.AppendLine($"        <text class=\"val\" x=\"{F(centre)}\" y=\"{F(baseline - barHeight - 7)}\" "
                          + $"text-anchor=\"middle\" font-size=\"12\">{ReportHtmlRenderer.E(label)}</text>");

            sb.AppendLine($"        <text x=\"{F(centre)}\" y=\"{baseline + 17}\" text-anchor=\"middle\" "
                          + $"font-size=\"12\" font-weight=\"600\">{ReportHtmlRenderer.E(bar.Label)}</text>");

            if (!string.IsNullOrWhiteSpace(bar.SubLabel))
                sb.AppendLine($"        <text x=\"{F(centre)}\" y=\"{baseline + 32}\" text-anchor=\"middle\" "
                              + $"font-size=\"11\" opacity=\"0.8\">{ReportHtmlRenderer.E(bar.SubLabel)}</text>");
        }

        sb.AppendLine("      </svg>");
        return sb.ToString();
    }

    /// <summary>
    /// Scale to the 97th percentile rather than the maximum, so one burst does not flatten every
    /// other bar. With only a handful of bars the percentile lands on the top value anyway, which
    /// is the right answer there - the rule earns its keep on a long series.
    /// </summary>
    private static double AxisTop(List<double> values, bool usePercentile)
    {
        if (values.Count == 0) return 1;

        var top = values.Max();

        if (usePercentile && values.Count >= 8)
        {
            var sorted = values.OrderBy(v => v).ToList();
            var rank = 0.97 * (sorted.Count - 1);
            var low = (int)Math.Floor(rank);
            var high = (int)Math.Ceiling(rank);
            top = low == high ? sorted[low] : sorted[low] + (sorted[high] - sorted[low]) * (rank - low);
        }

        return top <= 0 ? 1 : NiceCeiling(top);
    }

    /// <summary>Round the axis top up to something a reader can divide in their head.</summary>
    private static double NiceCeiling(double value)
    {
        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(value)));
        var normalised = value / magnitude;
        var step = normalised <= 1 ? 1 : normalised <= 2 ? 2 : normalised <= 5 ? 5 : 10;
        return step * magnitude;
    }

    private static List<double> Ticks(double top)
    {
        var ticks = new List<double>();
        for (var i = 1; i <= 4; i++) ticks.Add(top * i / 4);
        return ticks;
    }

    private static string RoundedTopBar(double x, double y, double width, double height)
    {
        var radius = Math.Min(4, Math.Min(width / 2, height));
        return $"M {F(x)} {F(y + height)} L {F(x)} {F(y + radius)} Q {F(x)} {F(y)} {F(x + radius)} {F(y)} "
               + $"L {F(x + width - radius)} {F(y)} Q {F(x + width)} {F(y)} {F(x + width)} {F(y + radius)} "
               + $"L {F(x + width)} {F(y + height)} Z";
    }

    private static string Colour(BarTone tone, ReportPalette palette) => tone switch
    {
        BarTone.Bad => palette.BarBad,
        BarTone.Warning => palette.BarWarning,
        BarTone.Absent => palette.BarAbsent,
        _ => palette.BarNormal
    };

    private static string Describe(BarChartBlock chart) =>
        string.Join("; ", chart.Bars.Select(b => $"{b.Label} {ReportHtmlRenderer.N(b.Value)}{chart.ValueSuffix}"));

    private static string F(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
