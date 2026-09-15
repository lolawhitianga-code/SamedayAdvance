using DiagFileMonitor.Core.Knowledge;

namespace DiagFileMonitor.Core.Reports;

/// <summary>
/// Report 5 - fault benchmarking. Every occurrence in full, most recent first, then the pattern.
/// <para>
/// The order matters and is not a style choice: a count tells a reader a pattern exists, an
/// occurrence list lets them check whether the reading behind it is right. Summarising first
/// invites the summary to be taken on trust.
/// </para>
/// </summary>
public static class FaultBenchmarkReport
{
    public static ReportModel Build(FleetScanResult scan, ReportPeriod period, string subject = "")
    {
        var title = string.IsNullOrWhiteSpace(subject)
            ? "Fault Benchmarking"
            : $"Fault Benchmarking - {subject}";

        var machineTypes = scan.Machines
            .Select(m => m.MachineType)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var subtitle = machineTypes.Count == 1
            ? $"{machineTypes[0]} fleet"
            : $"{scan.Machines.Count} machines";

        var report = new ReportModel
        {
            Title = title,
            Subtitle = subtitle,
            InternalUseOnly = true,
            Period = period,
            Footer = Footer(scan)
        };

        report.Sections.Add(OccurrenceSection(scan, period));
        report.Sections.Add(PatternSection(scan));

        if (DataQualitySection(scan) is { } quality) report.Sections.Add(quality);

        return report;
    }

    private static ReportSection OccurrenceSection(FleetScanResult scan, ReportPeriod period)
    {
        var table = new TableBlock
        {
            Scroll = scan.Occurrences.Count > 12,
            EmptyText = $"No occurrences recorded in the {period.Days} days covered.",
            // Six columns is what fits on a page without the right-hand end falling off it.
            // The machine carries its site underneath, and recovery and step share one column.
            Columns =
            {
                new ReportColumn("When", ColumnStyle.Timestamp, 12),
                new ReportColumn("Machine", ColumnStyle.Data, 12),
                new ReportColumn("Signal", ColumnStyle.Data, 24),
                new ReportColumn("What it means", ColumnStyle.Text, 30),
                new ReportColumn("Where", ColumnStyle.Data, 11),
                new ReportColumn("Confidence", ColumnStyle.Text, 11)
            }
        };

        foreach (var o in scan.Occurrences)
        {
            table.Rows.Add(new ReportRow
            {
                Cells =
                {
                    o.TimeCell,
                    string.IsNullOrWhiteSpace(o.Site) ? o.SerialNumber : $"{o.SerialNumber}\n{o.Site}",
                    o.Signal,
                    o.Detail,
                    Context(o),
                    Label(o.Confidence)
                }
            });
        }

        var section = new ReportSection
        {
            Title = "Fault log",
            Subtitle = "Every occurrence, most recent first. Nothing is grouped away - the pattern "
                       + "comes after the list, not instead of it."
        };

        section.Blocks.Add(table);

        // The confidence column is the point of this block, so it gets explained rather than
        // left as three words a reader has to guess at.
        if (scan.Occurrences.Count > 0)
        {
            section.Blocks.Add(new NoteBlock
            {
                Text = "Confidence: Confirmed means the signature is documented and the log matches "
                       + "it. Inferred means the reading follows from the log but has not been "
                       + "verified on a machine. Unconfirmed means it needs someone to look. "
                       + "Timestamps come from the machine log; a row marked \"date only\" had no "
                       + "time of day in the log and carries the day the bundle arrived."
            });
        }

        return section;
    }

    private static ReportSection PatternSection(FleetScanResult scan)
    {
        var byMachine = scan.Machines
            .Select(m => new
            {
                Machine = m,
                Events = scan.Occurrences.Count(o =>
                    o.SerialNumber.Equals(m.SerialNumber, StringComparison.OrdinalIgnoreCase)),
                Recoveries = scan.Occurrences
                    .Where(o => o.SerialNumber.Equals(m.SerialNumber, StringComparison.OrdinalIgnoreCase))
                    .Select(o => o.RecoveredAfter)
                    .Where(r => r is not null)
                    .Select(r => r!.Value.TotalSeconds)
                    .ToList()
            })
            .OrderByDescending(x => x.Events)
            .ThenBy(x => x.Machine.SerialNumber, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var worst = byMachine.FirstOrDefault()?.Events ?? 0;

        var chart = new BarChartBlock { ValueSuffix = string.Empty };
        var table = new TableBlock
        {
            EmptyText = "No machines in scope.",
            Columns =
            {
                new ReportColumn("Machine", ColumnStyle.Timestamp),
                new ReportColumn("Site"),
                new ReportColumn("Bundles read", ColumnStyle.Number),
                new ReportColumn("Events", ColumnStyle.Number),
                new ReportColumn("Avg recovery", ColumnStyle.Number)
            }
        };

        foreach (var row in byMachine)
        {
            // Every machine in scope appears in both the chart and the table, including the ones
            // with nothing recorded. A zero is a finding; a missing bar is a gap in the report.
            var tone = row.Events == 0 ? BarTone.Absent
                : worst > 0 && row.Events >= worst ? BarTone.Bad
                : BarTone.Warning;

            chart.Bars.Add(new BarChartBar(row.Machine.SerialNumber, row.Machine.Site, row.Events, tone));

            table.Rows.Add(new ReportRow
            {
                Muted = row.Machine.BundlesRead == 0,
                Cells =
                {
                    row.Machine.SerialNumber,
                    string.IsNullOrWhiteSpace(row.Machine.Site) ? "-" : row.Machine.Site,
                    row.Machine.BundlesRead.ToString(),
                    row.Machine.BundlesRead == 0 ? "pending" : row.Events.ToString(),
                    row.Recoveries.Count > 0 ? $"{row.Recoveries.Average():0.#}s" : "-"
                }
            });
        }

        var section = new ReportSection
        {
            Title = "Pattern analysis",
            Subtitle = "Counts are per machine over the period in the header, not all time."
        };

        // One bar is not a comparison - the table says it better and in less space.
        if (chart.Bars.Count > 1) section.Blocks.Add(chart);
        section.Blocks.Add(table);

        if (Reading(byMachine.Select(x => (x.Machine, x.Events)).ToList()) is { } reading)
            section.Blocks.Add(reading);

        return section;
    }

    /// <summary>
    /// The one interpretation the numbers do not make on their own: whether this is one machine's
    /// problem or the fleet's. It is deliberately hedged, because a count alone cannot settle it.
    /// </summary>
    private static CalloutBlock? Reading(List<(ScannedMachine Machine, int Events)> rows)
    {
        var withEvents = rows.Where(r => r.Events > 0).ToList();
        if (withEvents.Count == 0) return null;

        var top = withEvents[0];

        if (withEvents.Count == 1)
        {
            return new CalloutBlock
            {
                Lead = "Reading:",
                Text = $"Only {top.Machine.SerialNumber} shows this in the period covered. On one "
                       + "machine it reads as that machine's own problem - cabling, mounting, "
                       + "install - rather than a design issue, until a second one turns up."
            };
        }

        var second = withEvents[1];
        var ratio = second.Events > 0 ? top.Events / (double)second.Events : 0;

        var spread = $"Seen on {withEvents.Count} of {rows.Count} machines in scope. ";

        return new CalloutBlock
        {
            Lead = "Reading:",
            Text = ratio >= 2
                ? spread + $"{top.Machine.SerialNumber} runs at roughly {ratio:0.#}x the rate of the "
                  + $"next machine ({second.Machine.SerialNumber}). That gap points at something "
                  + "specific to that machine or site as well as the shared pattern - worth "
                  + "checking on-site before treating the whole thing as a design issue."
                : spread + "The rates are close enough across machines that this reads as a shared "
                  + "pattern rather than one bad unit. That is the case for looking at the design "
                  + "rather than the individual install."
        };
    }

    /// <summary>
    /// Anything that would make a reader mistrust the numbers, said out loud rather than buried:
    /// bundles that could not be read, ambiguous files inside a bundle, and machines whose
    /// identity the inventory argues with.
    /// </summary>
    private static ReportSection? DataQualitySection(FleetScanResult scan)
    {
        var disagreements = scan.Machines
            .Where(m => !string.IsNullOrWhiteSpace(m.Disagreement))
            .Select(m => m.Disagreement!)
            .ToList();

        if (disagreements.Count == 0 && scan.Skipped.Count == 0 && scan.SelectionNotes.Count == 0)
            return null;

        var section = new ReportSection
        {
            Title = "About this data",
            Subtitle = "What the figures above do and do not cover."
        };

        if (disagreements.Count > 0)
        {
            section.Blocks.Add(new TextBlock
            {
                Text = "Machine identity comes from each bundle's own Machine.xml, not from the site "
                       + "inventory. Where the two disagree the machine is believed, and the "
                       + "disagreement is printed rather than resolved quietly:"
            });
            section.Blocks.Add(new BulletsBlock { Items = disagreements });
        }

        if (scan.SelectionNotes.Count > 0)
            section.Blocks.Add(new BulletsBlock { Items = scan.SelectionNotes.ToList() });

        if (scan.Skipped.Count > 0)
        {
            section.Blocks.Add(new CalloutBlock
            {
                Tone = CalloutTone.Caution,
                Lead = "Not counted:",
                Text = $"{scan.Skipped.Count} bundle(s) in the period could not be read, so their "
                       + "faults are missing from these totals. " + string.Join(" ", scan.Skipped)
            });
        }

        return section;
    }

    private static string Footer(FleetScanResult scan)
    {
        var bundles = scan.Machines.Sum(m => m.BundlesRead);
        return $"Spida Machinery - internal diagnostics. Built from {bundles} diagnostic bundle(s) "
               + $"across {scan.Machines.Count} machine(s). Figures cover the period in the header only.";
    }

    /// <summary>Recovery time and machine step in one column, since most rows carry neither.</summary>
    private static string Context(FaultOccurrence o)
    {
        var parts = new List<string>();
        if (o.RecoveredAfter is { } r) parts.Add($"back after {r.TotalSeconds:0.#}s");
        if (o.Step is { } step) parts.Add($"step {step}");

        return parts.Count == 0 ? "-" : string.Join("\n", parts);
    }

    private static string Label(Confidence confidence) => confidence switch
    {
        Confidence.Confirmed => "Confirmed",
        Confidence.Inferred => "Inferred",
        _ => "Unconfirmed"
    };
}
