namespace DiagFileMonitor.Core.Reports;

/// <summary>
/// The colours a report is drawn in.
/// <para>
/// Two sets are kept because the written brand standard and the actual logo artwork disagree.
/// <see cref="FromLogo"/> holds the values sampled out of the logo the app ships - #00A5E3 covers
/// 97% of its pixels - and is what <c>App/Theme.xaml</c> uses, so a report matches the app that
/// produced it and the logo printed beside it. <see cref="FromBrandDocument"/> holds the values
/// written in the reporting standard. Switching is one line; see docs/report-generation-plan.md.
/// </para>
/// </summary>
public class ReportPalette
{
    public string Blue { get; init; } = "#00A5E3";
    public string BlueDark { get; init; } = "#0084B6";
    public string Navy { get; init; } = "#415968";

    public string Ink { get; init; } = "#1B242B";
    public string Paper { get; init; } = "#F3F6F7";
    public string Card { get; init; } = "#FFFFFF";
    public string Border { get; init; } = "#DCE4E8";
    public string Amber { get; init; } = "#C98A2E";

    /// <summary>
    /// Status steps for chart bars. These are a reserved status set, not a categorical palette:
    /// they mean a state, never "series 3". The three coloured steps sit inside the readable
    /// lightness band and clear the colour-vision separation floor against each other. The grey
    /// deliberately reads as grey - it means "in scope, nothing recorded", and a neutral is the
    /// right answer for absent data. Every bar carries its own value label and the same numbers
    /// appear in the table underneath, which is the relief the lower-contrast amber needs.
    /// </summary>
    public string BarNormal { get; init; } = "#0084B6";
    public string BarWarning { get; init; } = "#C98A2E";
    public string BarBad { get; init; } = "#B33A2A";
    public string BarAbsent { get; init; } = "#6E828C";

    public string Good { get; init; } = "#2E7D4F";

    public static ReportPalette FromLogo { get; } = new();

    /// <summary>The values written in the supplied reporting standard, kept for when the brand
    /// book is confirmed as the authority over the artwork.</summary>
    public static ReportPalette FromBrandDocument { get; } = new()
    {
        Blue = "#009CDE",
        BlueDark = "#0077A8",
        Navy = "#425563"
    };
}

internal static class ReportStyle
{
    /// <summary>
    /// The families the house style asks for, each with a local fallback. The samples pull these
    /// from the Google Fonts CDN, which resolves to nothing on a factory PC opened from file://
    /// with no internet - so the stack has to stand on its own, and the CDN link is opt-in.
    /// </summary>
    private const string HeadingFont = "'Zilla Slab', Georgia, 'Times New Roman', serif";
    private const string BodyFont = "'Nunito', 'Segoe UI', Roboto, Helvetica, Arial, sans-serif";
    private const string DataFont = "'IBM Plex Mono', Consolas, 'Courier New', monospace";

    public const string WebFontLink =
        "<link rel=\"preconnect\" href=\"https://fonts.googleapis.com\">\n"
        + "<link href=\"https://fonts.googleapis.com/css2?family=Zilla+Slab:wght@500;600;700"
        + "&amp;family=Nunito:wght@400;600;700&amp;family=IBM+Plex+Mono:wght@500;600&amp;display=swap\""
        + " rel=\"stylesheet\">";

    public static string Css(ReportPalette p) => $@"
  :root{{
    --blue:{p.Blue}; --blue-dark:{p.BlueDark}; --navy:{p.Navy};
    --ink:{p.Ink}; --paper:{p.Paper}; --card:{p.Card}; --border:{p.Border};
    --amber:{p.Amber}; --good:{p.Good};
  }}
  *{{box-sizing:border-box;}}
  body{{margin:0; background:var(--paper); color:var(--ink);
       font-family:{BodyFont}; line-height:1.5;}}
  .wrap{{max-width:860px; margin:0 auto; padding:0 20px 40px;}}
  header{{background:var(--navy); color:#fff; padding:24px 20px 20px;}}
  .header-inner{{max-width:860px; margin:0 auto; display:flex; justify-content:space-between;
                align-items:flex-start; flex-wrap:wrap; gap:10px;}}
  .wordmark{{font-family:{HeadingFont}; font-weight:700; font-size:1.05rem; letter-spacing:0.02em;}}
  .wordmark span{{color:#C9D3D8;}}
  h1{{font-family:{HeadingFont}; font-weight:600; font-size:1.35rem; margin:8px 0 2px;}}
  .meta{{font-size:0.86rem; color:#D3DBDE;}}
  .tag{{font-size:0.72rem; font-weight:700; letter-spacing:0.04em;
       background:rgba(255,255,255,0.16); padding:5px 10px; border-radius:4px;
       align-self:flex-start; white-space:nowrap;}}
  section{{background:var(--card); border:1px solid var(--border); border-radius:6px;
          padding:20px 22px; margin-top:16px;}}
  h2{{font-family:{HeadingFont}; font-size:1.02rem; margin:0 0 6px;}}
  .sub{{font-size:0.84rem; color:var(--navy); margin-bottom:12px;}}
  p{{margin:0 0 10px;}}
  p:last-child{{margin-bottom:0;}}
  ul{{margin:0; padding-left:20px;}}
  li{{margin-bottom:8px;}}
  li:last-child{{margin-bottom:0;}}

  table{{width:100%; border-collapse:collapse; font-size:0.84rem;}}
  table.fixed{{table-layout:fixed;}}
  th{{text-align:left; font-size:0.72rem; text-transform:uppercase; letter-spacing:0.03em;
     color:var(--navy); padding:0 10px 6px 0; border-bottom:2px solid var(--border);
     position:sticky; top:0; background:var(--card);}}
  td{{padding:7px 10px 7px 0; border-bottom:1px solid var(--border); vertical-align:top;}}
  th:last-child, td:last-child{{padding-right:0;}}
  tr:last-child td{{border-bottom:none;}}
  td.data, th.data{{font-family:{DataFont}; overflow-wrap:anywhere;}}
  td.stamp, th.stamp{{white-space:nowrap;}}
  .sub-cell{{font-family:{BodyFont}; font-size:0.9em; opacity:0.72; margin-top:2px; white-space:normal;}}
  td.num, th.num{{font-family:{DataFont}; text-align:right; font-weight:600;}}
  th.num{{font-weight:700;}}
  tr.muted td{{color:var(--navy); font-style:italic;}}
  .scroll{{max-height:430px; overflow-y:auto; overflow-x:auto; border:1px solid var(--border);
          border-radius:4px; padding:0 10px;}}
  .empty{{font-size:0.86rem; color:var(--navy); font-style:italic;}}

  .hero{{display:flex; align-items:baseline; gap:14px; flex-wrap:wrap;}}
  .hero .num{{font-family:{HeadingFont}; font-weight:700; font-size:2.6rem;
             color:var(--blue-dark); line-height:1;}}
  .hero .label{{font-size:0.95rem; color:var(--navy); max-width:470px;}}
  .hero-sub{{margin-top:10px; font-size:0.96rem;}}

  .callout{{margin-top:14px; border-left:4px solid var(--blue); background:#EAF7FD;
           padding:14px 18px; border-radius:0 6px 6px 0; font-size:0.9rem;}}
  .callout b{{color:var(--blue-dark);}}
  .callout.caution{{border-left-color:var(--amber); background:#FBF3E6; color:#6B4E1E;}}
  .callout.caution b{{color:#6B4E1E;}}
  .note{{margin-top:12px; font-size:0.83rem; color:var(--navy);}}
  .pending{{margin-top:14px; border:1px dashed var(--border); border-radius:6px;
           padding:14px 18px; font-size:0.86rem; color:var(--navy); background:var(--paper);}}

  .step{{display:flex; gap:12px; margin-bottom:12px;}}
  .step:last-child{{margin-bottom:0;}}
  .step .n{{font-family:{DataFont}; font-weight:600; color:var(--blue-dark); flex-shrink:0;}}

  .extract{{margin-top:12px; background:#F7FAFB; border:1px solid var(--border);
           border-radius:4px; padding:12px 14px; font-family:{DataFont};
           font-size:0.76rem; line-height:1.7; overflow-x:auto; white-space:pre;}}
  .extract .hit{{background:#FFF1CC; font-weight:600; display:inline-block; width:100%;}}
  .extract-cap{{font-size:0.83rem; color:var(--navy); margin-top:12px;}}

  .chart{{margin-top:4px;}}
  .chart svg{{width:100%; height:auto; display:block;}}
  .chart .axis{{stroke:var(--border); stroke-width:1;}}
  .chart .grid{{stroke:#E9EEF0; stroke-width:1;}}
  .chart text{{font-family:{BodyFont}; fill:var(--navy);}}
  .chart .val{{font-family:{DataFont}; font-weight:600; fill:var(--ink);}}

  footer{{text-align:center; color:var(--navy); font-size:0.78rem; margin-top:22px;}}

  @media print{{
    body{{background:#fff;}}
    section{{break-inside:avoid; box-shadow:none;}}
    .scroll{{max-height:none; overflow:visible;}}
    .sub-cell{{opacity:1;}}
  }}
  @media (max-width:600px){{
    .hero .num{{font-size:2rem;}}
    table{{font-size:0.78rem;}}
  }}
";
}
