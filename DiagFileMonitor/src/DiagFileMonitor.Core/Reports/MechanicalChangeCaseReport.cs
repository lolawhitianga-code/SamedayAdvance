namespace DiagFileMonitor.Core.Reports;

/// <summary>
/// The figures a human has to supply, because the logs do not hold them.
/// <para>
/// Downtime cost is the obvious one. A machine log says a fault happened; it does not say what
/// the revert-and-retry cost in minutes, and inventing a number here would put a made-up figure
/// in front of the design team in the same typeface as the measured ones.
/// </para>
/// </summary>
public class ChangeCaseInputs
{
    /// <summary>What one occurrence costs in lost running time. Null means it is not known.</summary>
    public TimeSpan? TimePerEvent { get; init; }

    /// <summary>Who said so, e.g. "timed on M21036, 12 Sep". Printed beside the estimate.</summary>
    public string TimePerEventSource { get; init; } = string.Empty;

    /// <summary>The change being proposed, in order.</summary>
    public IReadOnlyList<string> ProposedSteps { get; init; } = Array.Empty<string>();

    /// <summary>Photos, video and records still to be gathered.</summary>
    public string StillNeeded { get; init; } = string.Empty;

    /// <summary>Anything real but unmeasurable - operator confidence, nuisance stops.</summary>
    public string SoftCost { get; init; } = string.Empty;
}

/// <summary>
/// Report 6 - the mechanical change case. Built on the same occurrences as report 5, turned into
/// an argument the design team can act on: what is happening, what it costs, what to change, and
/// how the change will be judged.
/// </summary>
public static class MechanicalChangeCaseReport
{
    public static ReportModel Build(
        FleetScanResult scan, ReportPeriod period, string subject, ChangeCaseInputs inputs)
    {
        var report = new ReportModel
        {
            Title = string.IsNullOrWhiteSpace(subject) ? "Design Review" : $"Design Review - {subject}",
            Subtitle = Scope(scan),
            InternalUseOnly = true,
            Period = period,
            Footer = "Spida Machinery - internal design review. Fault counts are measured from "
                     + "diagnostic bundles; anything marked an estimate is not."
        };

        report.Sections.Add(WhatsHappening(scan, period));
        report.Sections.Add(Cost(scan, period, inputs));

        if (inputs.ProposedSteps.Count > 0)
        {
            var proposed = new ReportSection { Title = "Proposed change" };
            proposed.Blocks.Add(new StepsBlock { Steps = inputs.ProposedSteps.ToList() });
            proposed.Blocks.Add(new NoteBlock
            {
                Text = "Validation: re-run this report over the same number of days after the change "
                       + "and compare against the counts above. The period is in the header so the "
                       + "two are measured the same way."
            });
            report.Sections.Add(proposed);
        }

        if (!string.IsNullOrWhiteSpace(inputs.StillNeeded))
        {
            var evidence = new ReportSection { Title = "Still to gather" };
            evidence.Blocks.Add(new PendingBlock { Lead = "Reference material needed:", Text = inputs.StillNeeded });
            report.Sections.Add(evidence);
        }

        return report;
    }

    private static string Scope(FleetScanResult scan)
    {
        var types = scan.Machines
            .Select(m => m.MachineType)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return types.Count == 1 ? types[0] : $"{scan.Machines.Count} machines";
    }

    private static ReportSection WhatsHappening(FleetScanResult scan, ReportPeriod period)
    {
        var affected = scan.Machines
            .Where(m => scan.Occurrences.Any(o =>
                o.SerialNumber.Equals(m.SerialNumber, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        var clear = scan.Machines.Except(affected).ToList();

        var section = new ReportSection { Title = "What's happening" };

        var lead = scan.Occurrences.FirstOrDefault();
        if (lead is not null)
            section.Blocks.Add(new TextBlock { Text = lead.Detail });

        var bullets = new List<string>();

        if (affected.Count > 0)
        {
            bullets.Add($"Seen on {affected.Count} of {scan.Machines.Count} machines read over "
                        + $"{period.Days} days: {string.Join(", ", affected.Select(Describe))}.");
        }

        if (clear.Count > 0)
        {
            bullets.Add($"Not seen in the same window on {string.Join(", ", clear.Select(Describe))}. "
                        + "A machine reading zero is evidence too - it is what a fix should look like.");
        }

        bullets.Add(affected.Count > 1
            ? "Present on more than one machine, which points at a design or wiring pattern rather "
              + "than a single bad unit."
            : "Present on one machine so far. Until a second turns up this is that machine's "
              + "install, not the design.");

        var confirmed = scan.Occurrences.Count(o => o.Confidence == Knowledge.Confidence.Confirmed);
        if (confirmed < scan.Occurrences.Count)
        {
            bullets.Add($"{confirmed} of {scan.Occurrences.Count} occurrences match a documented "
                        + "signature; the rest are readings that still need someone to check them.");
        }

        section.Blocks.Add(new BulletsBlock { Items = bullets });
        return section;
    }

    private static ReportSection Cost(FleetScanResult scan, ReportPeriod period, ChangeCaseInputs inputs)
    {
        var section = new ReportSection { Title = "Cost of the fault" };
        var events = scan.Occurrences.Count;

        // Per month, from the actual period, rather than quietly calling however many days we
        // happen to hold "a month".
        var perMonth = period.Days > 0 ? events * 30.0 / period.Days : events;

        if (inputs.TimePerEvent is { } per)
        {
            var monthly = TimeSpan.FromSeconds(perMonth * per.TotalSeconds);

            section.Blocks.Add(new HeroBlock
            {
                Figure = Round(monthly),
                Label = "estimated direct downtime per month across the machines in scope",
                Subline = $"{events} occurrence(s) over {period.Days} days, at "
                          + $"{per.TotalSeconds:0.#}s each."
            });

            section.Blocks.Add(new TableBlock
            {
                Columns = { new ReportColumn("Basis"), new ReportColumn("Figure", ColumnStyle.Number) },
                Rows =
                {
                    new ReportRow { Cells = { $"Occurrences measured over {period.Days} days", events.ToString() } },
                    new ReportRow { Cells = { "Scaled to 30 days", $"{perMonth:0.#}" } },
                    new ReportRow { Cells = { "Time per occurrence (supplied)", $"{per.TotalSeconds:0.#}s" } },
                    new ReportRow { Cells = { "Direct downtime per month", Round(monthly) } }
                }
            });

            section.Blocks.Add(new CalloutBlock
            {
                Tone = CalloutTone.Caution,
                Lead = "How solid is this:",
                Text = "The occurrence count is measured from the logs. The time per occurrence is "
                       + (string.IsNullOrWhiteSpace(inputs.TimePerEventSource)
                           ? "a figure supplied by hand, not measured from the logs"
                           : $"supplied by hand - {inputs.TimePerEventSource}")
                       + ". The monthly total is only as good as that number."
            });
        }
        else
        {
            section.Blocks.Add(new HeroBlock
            {
                Figure = events.ToString(),
                Label = $"occurrences over {period.Days} days across the machines in scope",
                Subline = $"About {perMonth:0.#} a month at this rate."
            });

            section.Blocks.Add(new CalloutBlock
            {
                Tone = CalloutTone.Caution,
                Lead = "No downtime figure:",
                Text = "The logs record that the fault happened, not what it cost in lost running "
                       + "time. Time one revert-and-retry on site and the monthly cost falls out of "
                       + "the count above. No estimate is shown rather than an invented one."
            });
        }

        if (!string.IsNullOrWhiteSpace(inputs.SoftCost))
        {
            section.Blocks.Add(new CalloutBlock
            {
                Tone = CalloutTone.Caution,
                Lead = "Harder to quantify:",
                Text = inputs.SoftCost
            });
        }

        return section;
    }

    private static string Describe(ScannedMachine machine) =>
        string.IsNullOrWhiteSpace(machine.Site)
            ? machine.SerialNumber
            : $"{machine.SerialNumber} ({machine.Site})";

    private static string Round(TimeSpan span) =>
        span.TotalMinutes < 1 ? $"{span.TotalSeconds:0}s"
        : span.TotalMinutes < 90 ? $"{span.TotalMinutes:0} min"
        : $"{span.TotalHours:0.#} hr";
}
