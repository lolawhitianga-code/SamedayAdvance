using System.Text;
using DiagFileMonitor.Core.Models;

namespace DiagFileMonitor.Core.SpidaLogs;

public static class CompareReportFormatter
{
    public static string Format(
        DiagnosticFileSummary master, DiagnosticFileSummary compared,
        StepTimingComparison steps, SettingsComparison settings, IReadOnlyList<string> notes)
    {
        var text = new StringBuilder();

        text.AppendLine("BENCHMARK COMPARISON");
        text.AppendLine(new string('=', 78));
        text.AppendLine($"Master (known good): {master.OriginalFileName}");
        text.AppendLine($"                     {master.MachineType}  serial {master.SerialNumber}  {master.Customer}");
        text.AppendLine($"                     {master.SoftwareName} {master.Version}   {master.ArrivedDisplay}");
        text.AppendLine();
        text.AppendLine($"Compared:            {compared.OriginalFileName}");
        text.AppendLine($"                     {compared.MachineType}  serial {compared.SerialNumber}  {compared.Customer}");
        text.AppendLine($"                     {compared.SoftwareName} {compared.Version}   {compared.ArrivedDisplay}");

        if (!string.Equals(master.MachineType, compared.MachineType, StringComparison.OrdinalIgnoreCase))
        {
            text.AppendLine();
            text.AppendLine("  !! These are different machine models. Expect a lot of differences that are");
            text.AppendLine("     normal rather than faults.");
        }

        if (!string.Equals(master.Version, compared.Version, StringComparison.OrdinalIgnoreCase))
        {
            text.AppendLine();
            text.AppendLine($"  Note: software versions differ ({master.Version} vs {compared.Version}), which can");
            text.AppendLine("  explain both timing and settings differences.");
        }

        AppendCycle(text, steps);
        AppendSequence(text, steps);
        AppendStepTable(text, steps);
        AppendSettings(text, settings);

        if (notes.Count > 0)
        {
            text.AppendLine();
            text.AppendLine("NOTES");
            foreach (var note in notes) text.AppendLine($"  - {note}");
        }

        return text.ToString();
    }

    private static void AppendCycle(StringBuilder text, StepTimingComparison steps)
    {
        text.AppendLine();
        text.AppendLine("OVERALL CYCLE TIME");

        if (steps.MasterCycle == TimeSpan.Zero || steps.ComparedCycle == TimeSpan.Zero)
        {
            text.AppendLine("  Not enough complete units in one of the files to compare cycle time.");
            return;
        }

        var percent = steps.CyclePercentOfMaster ?? 0;
        var verdict = percent >= 125 ? "  <-- slower than the benchmark"
            : percent <= 80 ? "  faster than the benchmark"
            : "  in line with the benchmark";

        text.AppendLine($"  master   {StepDifference.Describe(steps.MasterCycle)}");
        text.AppendLine($"  compared {StepDifference.Describe(steps.ComparedCycle)}  ({percent:0}% of master){verdict}");
    }

    private static void AppendSequence(StringBuilder text, StepTimingComparison steps)
    {
        text.AppendLine();
        text.AppendLine("STEP SEQUENCE");

        if (steps.MasterSequence.Count == 0 || steps.ComparedSequence.Count == 0)
        {
            text.AppendLine("  One of the files has no step counters logged, so the process order could not");
            text.AppendLine("  be compared.");
            return;
        }

        text.AppendLine($"  master   {string.Join(" -> ", steps.MasterSequence)}");
        text.AppendLine($"  compared {string.Join(" -> ", steps.ComparedSequence)}");
        text.AppendLine();

        if (steps.SequenceMatches)
        {
            text.AppendLine("  Same steps in the same order.");
            return;
        }

        var missing = steps.MasterSequence.Except(steps.ComparedSequence).ToList();
        var extra = steps.ComparedSequence.Except(steps.MasterSequence).ToList();

        if (missing.Count > 0) text.AppendLine($"  Steps the benchmark runs but this machine never reached: {string.Join(", ", missing)}");
        if (extra.Count > 0) text.AppendLine($"  Steps this machine ran that the benchmark does not: {string.Join(", ", extra)}");
        if (missing.Count == 0 && extra.Count == 0) text.AppendLine("  Same steps, but in a different order.");
    }

    private static void AppendStepTable(StringBuilder text, StepTimingComparison steps)
    {
        text.AppendLine();
        text.AppendLine("TIME IN EACH STEP (median across units)");

        if (steps.Differences.Count == 0)
        {
            text.AppendLine("  No step timings could be measured.");
            return;
        }

        foreach (var difference in steps.Differences)
        {
            text.AppendLine(difference.Display);
        }

        var noteworthy = steps.Noteworthy.Count();
        text.AppendLine();
        text.AppendLine(noteworthy == 0
            ? "  Every step is within 10% of the benchmark."
            : $"  {noteworthy} step(s) differ from the benchmark by more than 10%.");
    }

    private static void AppendSettings(StringBuilder text, SettingsComparison settings)
    {
        text.AppendLine();
        text.AppendLine("SETTINGS DIFFERENCES (Machine.xml)");

        if (settings.SettingsCompared == 0)
        {
            text.AppendLine("  Machine.xml was missing from one of the bundles.");
            return;
        }

        if (settings.Differences.Count == 0)
        {
            text.AppendLine($"  None. All {settings.SettingsCompared} shared settings match.");
        }
        else
        {
            text.AppendLine($"  {settings.Differences.Count} of {settings.SettingsCompared} settings differ:");
            text.AppendLine();

            foreach (var difference in settings.Differences.Take(60))
            {
                text.AppendLine($"  {Shorten(difference.Setting)}");
                text.AppendLine($"      master   {difference.MasterValue}");
                text.AppendLine($"      compared {difference.ComparedValue}");
            }

            if (settings.Differences.Count > 60)
            {
                text.AppendLine($"  ... and {settings.Differences.Count - 60} more.");
            }
        }

        if (settings.OnlyInMaster.Count > 0 || settings.OnlyInCompared.Count > 0)
        {
            text.AppendLine();
            text.AppendLine($"  {settings.OnlyInMaster.Count} setting(s) exist only in the master and "
                            + $"{settings.OnlyInCompared.Count} only in the compared machine, which usually means");
            text.AppendLine("  a different software version rather than a misconfiguration.");
        }
    }

    /// <summary>Drops the common root prefix so a setting path stays readable.</summary>
    private static string Shorten(string path) =>
        path.StartsWith("Machine/", StringComparison.OrdinalIgnoreCase) ? path["Machine/".Length..] : path;
}
