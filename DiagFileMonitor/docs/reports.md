# Reports

Builds a self-contained HTML report from the bundles already stored. **Report** on the main
toolbar opens the builder; the file is written to `Reports\` beside the database.

Two reports are built, matching the house style of the supplied samples
(`5-diagnostics-benchmarking.html`, `6-design-team-case.html`):

| Report | What it is | Sourced from |
|---|---|---|
| Fault benchmarking | Every fault occurrence across the fleet, most recent first, then the pattern | Stored `.szip` bundles |
| Mechanical change case | The argument for a design change, built on the same occurrences | The same, plus figures you supply |

The four customer-facing reports (machine snapshot, live dashboard, historic rollup, fleet
comparison) need ProdLogV2 production data this app does not read. See
`report-generation-plan.md` section 3.

## How it is put together

```
stored bundles → FleetFaultScanner → FleetScanResult
                                          │
                     FaultBenchmarkReport / MechanicalChangeCaseReport
                                          │
                                     ReportModel
                                          │
                                 ReportHtmlRenderer → one .html file
```

The scanner runs the same analysis the on-screen report does - the same parsers, the same checks,
the same confidence labels. The renderer never opens a log. If a number is not on the
`ReportModel` it does not reach the page, so the report and the analysis cannot drift apart.

| File | Job |
|---|---|
| `Reports/FleetScanRequest.cs` | What a report covers: period, serials, machine types, fault kinds |
| `Reports/FleetFaultScanner.cs` | Walks stored bundles, pulls out occurrences, deduplicates |
| `Reports/FaultOccurrence.cs` | One fault, one moment, one machine |
| `Reports/FaultBenchmarkReport.cs` | Builds report 5 |
| `Reports/MechanicalChangeCaseReport.cs` | Builds report 6 |
| `Reports/ReportModel.cs`, `ReportBlocks.cs` | The report, before anything decides how it looks |
| `Reports/ReportHtmlRenderer.cs` | Model to HTML |
| `Reports/BarChartSvg.cs` | The chart, as inline SVG |
| `Reports/ReportStyle.cs` | Palette and CSS |
| `Reports/MachineInventory.cs` | Site and contact detail, keyed on serial |
| `Reports/ReportService.cs` | Ties it together and saves the file |
| `App/ReportWindow.xaml`, `App/ViewModels/ReportViewModel.cs` | Choosing the scope |

## Where this differs from the supplied samples, and why

**Genuinely self-contained.** The samples ask for a single portable file *and* pull Zilla Slab,
Nunito and IBM Plex Mono from the Google Fonts CDN and Chart.js from cdnjs. On a factory PC opened
from `file://` with no internet, none of that loads: the fonts silently fall back and the charts
render as an empty box. So there is no CDN here - the font stack degrades on its own, and charts
are drawn as inline SVG. `ReportHtmlOptions.UseWebFonts` turns the CDN link back on for a report
that will only ever be read on a connected machine. A test asserts the output contains no `http`.

**Colours from the logo artwork.** `#00A5E3` / `#415968`, sampled from the logo the app ships
(97% of its pixels), rather than the `#009CDE` / `#425563` written in the standard. A report has
to match the app that produced it and the logo printed beside it. `ReportPalette.FromBrandDocument`
holds the written values; switching is one line.

**Machine identity comes from `Machine.xml`, not the inventory table.** The table adds the site
detail the log has no way of knowing - "Carters Cambridge" rather than "Carters", the line number,
the contact, and which machine feeds which. Where the two disagree on model or customer, the
machine is believed and the report prints the disagreement in its "About this data" section.
Reasoning in `report-generation-plan.md` section 2.1.

Serials are matched on the whole serial first, then with a line suffix dropped (M21642-1 finds
M21642), then on the digits alone. Digits shared by two machines identify neither, so the lookup
returns nothing rather than guessing. `IsRetiredSerialStyle` recognises the AOR style, dropped
around 2021 - a pre-2021 build runs older electronics, so a fault code table from a current
machine may not apply to it.

**Faults are deduplicated across overlapping bundles.** Every export carries the machine's recent
history, not just the moment it was raised. Two bundles taken seconds apart carry the same ErrLog
entries; counting both doubles every figure. Matching is on machine, kind, signal and moment. This
is not the samples' "Raked Extruder entries are double-written" rule, which does not hold for
`MachineLog.txt` and is not applied - see the plan, section 2.3.

## Rules taken straight from the samples

- Real serials everywhere, never Machine A/B/C
- Period-filtered headers state the period; an all-time total is never dressed up as a week
- Every machine in scope appears in every chart and column; missing data shows as pending, not dropped
- Occurrence-level detail first, pattern analysis second - never a count standing in for the list
- Log lines quoted with context, not line numbers
- Callout boxes for anything needing interpretation beyond the numbers
- 97th percentile for chart scaling, not the maximum
- Internal reports marked "Internal use only" by default

Plus one of ours the samples do not have: **every interpretation carries its confidence label.**
Confirmed / Inferred / Unconfirmed is already printed in the text report because these get quoted
to customers. An HTML report that drops the label is more dangerous than a plain text one, because
it looks settled.

## The change case needs a number from you

The logs record that a fault happened. They do not record what it cost in lost running time. Leave
"seconds lost each time" blank and the report says the downtime is not known and how to find out;
it does not invent one. Where you do supply it, the report says who supplied it and that the
monthly total is only as good as that number.

Monthly figures are scaled from the real period, not from however many days happen to be stored.
