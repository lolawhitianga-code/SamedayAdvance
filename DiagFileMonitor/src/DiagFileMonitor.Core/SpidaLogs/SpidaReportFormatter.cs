using System.Text;
using DiagFileMonitor.Core.Knowledge;
using DiagFileMonitor.Core.Models;

namespace DiagFileMonitor.Core.SpidaLogs;

/// <summary>Writes the analysis up in the order the guide asks for.</summary>
public static class SpidaReportFormatter
{
    public static string Format(
        DiagnosticFileSummary file,
        SpidaLogAnalysis analysis,
        KnowledgeFindings? knowledge = null)
    {
        var text = new StringBuilder();

        text.AppendLine($"DIAGNOSTIC ANALYSIS - {file.OriginalFileName}");
        text.AppendLine(new string('-', 78));
        text.AppendLine($"Machine:   {file.MachineType} ({file.MachineName})   serial {file.SerialNumber}");
        text.AppendLine($"Customer:  {file.Customer}, {file.SiteLocation}");
        text.AppendLine($"Software:  {file.SoftwareName} {file.Version}");
        text.AppendLine($"Arrived:   {file.ArrivedDisplay}");

        if (analysis.MachineModelFromLog is { } fromLog)
        {
            var agrees = fromLog.Contains(file.MachineType, StringComparison.OrdinalIgnoreCase);
            text.AppendLine($"MachineLog reports model: {fromLog}{(agrees ? string.Empty : "   <-- does not match Machine.xml")}");
        }

        AppendOperatorText(text, file);
        AppendUnits(text, analysis);
        AppendRepeats(text, analysis);
        AppendErrors(text, analysis);
        AppendChanges(text, analysis);
        if (knowledge is not null) KnowledgeReportFormatter.Append(text, knowledge);
        AppendWhereToLook(text, file, analysis, knowledge);
        AppendQuestions(text, analysis, knowledge);

        if (analysis.Notes.Count > 0)
        {
            text.AppendLine();
            text.AppendLine("NOTES ON THIS ANALYSIS");
            foreach (var note in analysis.Notes) text.AppendLine($"  - {note}");
        }

        return text.ToString();
    }

    private static void AppendOperatorText(StringBuilder text, DiagnosticFileSummary file)
    {
        if (string.IsNullOrWhiteSpace(file.SupportIssue)
            && string.IsNullOrWhiteSpace(file.SupportPanel)
            && string.IsNullOrWhiteSpace(file.SupportMembers)) return;

        text.AppendLine();
        text.AppendLine("WHAT THE OPERATOR REPORTED");
        if (!string.IsNullOrWhiteSpace(file.SupportPanel)) text.AppendLine($"  Panel:   {file.SupportPanel}");
        if (!string.IsNullOrWhiteSpace(file.SupportMembers)) text.AppendLine($"  Members: {file.SupportMembers}");
        if (!string.IsNullOrWhiteSpace(file.SupportIssue)) text.AppendLine($"  Issue:   \"{file.SupportIssue}\"");
    }

    private static void AppendUnits(StringBuilder text, SpidaLogAnalysis analysis)
    {
        text.AppendLine();
        text.AppendLine("UNITS ATTEMPTED");

        if (analysis.Cycles.Count == 0)
        {
            text.AppendLine("  No unit boundaries could be found in MachineLog.txt.");
            return;
        }

        text.AppendLine($"  {analysis.Cycles.Count} attempt(s) found across {analysis.MachineLogLines} log lines.");
        text.AppendLine();

        foreach (var (cycle, label) in new[] { (analysis.Last, "Last attempt"), (analysis.SecondToLast, "Previous attempt") })
        {
            if (cycle is null) continue;

            text.AppendLine($"  {label} (#{cycle.Number}, {cycle.Start:hh\\:mm\\:ss} to {cycle.End:hh\\:mm\\:ss}): {cycle.Outcome}");

            foreach (var fault in cycle.Faults.TakeLast(3))
            {
                var atStep = fault.StepAtFault is { } step ? $" at step {step}" : string.Empty;
                text.AppendLine($"      {fault.Time:hh\\:mm\\:ss}{atStep}: {fault.Text}");
            }

            // The guide asks for quoted context so the reading can be checked against the file.
            var lastFault = cycle.Faults.LastOrDefault();
            if (lastFault is not null && lastFault.Context.Count > 0)
            {
                text.AppendLine();
                text.AppendLine($"      What the machine was doing around that fault:");
                foreach (var line in lastFault.Context)
                {
                    text.AppendLine($"        {line.Display}");
                }
            }

            text.AppendLine();
        }
    }

    private static void AppendRepeats(StringBuilder text, SpidaLogAnalysis analysis)
    {
        text.AppendLine("DOES IT REPEAT?");

        if (analysis.RepeatedFaults.Count == 0)
        {
            text.AppendLine("  No fault repeated across attempts. Treat what you see as a one-off unless the");
            text.AppendLine("  customer says otherwise.");
            return;
        }

        foreach (var repeat in analysis.RepeatedFaults)
        {
            var atStep = repeat.StepAtFault is { } step ? $", at step {step}" : string.Empty;
            text.AppendLine($"  x{repeat.Occurrences} across attempts {string.Join(", ", repeat.CycleNumbers)}{atStep}:");
            text.AppendLine($"      {repeat.Text}");
        }

        text.AppendLine();
        text.AppendLine("  A fault repeating at the same step across attempts points at real hardware or a");
        text.AppendLine("  sensor, not a glitch.");
    }

    private static void AppendErrors(StringBuilder text, SpidaLogAnalysis analysis)
    {
        text.AppendLine();
        text.AppendLine("ERRLOG.TXT");

        if (analysis.RealErrors.Count == 0)
        {
            text.AppendLine("  Nothing left after filtering cosmetic, out-of-window and background entries.");
        }
        else
        {
            text.AppendLine($"  {analysis.RealErrors.Count} entr(ies) worth a look:");
            foreach (var error in analysis.RealErrors.Take(10))
            {
                text.AppendLine($"      {error.Timestamp:yyyy-MM-dd HH:mm:ss}  {error.Text}");
            }
        }

        if (analysis.CosmeticErrors.Count > 0)
        {
            text.AppendLine($"  {analysis.CosmeticErrors.Count} ignored as cosmetic (a matching Change.log entry at the");
            text.AppendLine("      same second shows the save actually worked).");
        }

        if (analysis.ErrorsOutsideLogWindow.Count > 0)
        {
            text.AppendLine($"  {analysis.ErrorsOutsideLogWindow.Count} fall outside the MachineLog window, so there is no");
            text.AppendLine("      machine context for them.");
        }

        if (analysis.RepeatingBackgroundErrors.Count > 0)
        {
            text.AppendLine("  Background errors seen every session (not this complaint):");
            foreach (var noise in analysis.RepeatingBackgroundErrors.Take(5))
            {
                text.AppendLine($"      {noise}");
            }
        }
    }

    private static void AppendChanges(StringBuilder text, SpidaLogAnalysis analysis)
    {
        text.AppendLine();
        text.AppendLine("SETTINGS CHANGED AROUND THIS SESSION");

        if (analysis.RecentSettingChanges.Count == 0)
        {
            text.AppendLine("  None.");
            return;
        }

        foreach (var change in analysis.RecentSettingChanges.Take(15))
        {
            text.AppendLine($"  {change.Display}");
        }

        text.AppendLine();
        text.AppendLine("  Listed as context. Ask the customer to confirm whether any of these are related");
        text.AppendLine("  rather than assuming they are.");
    }

    private static void AppendWhereToLook(
        StringBuilder text,
        DiagnosticFileSummary file,
        SpidaLogAnalysis analysis,
        KnowledgeFindings? knowledge)
    {
        text.AppendLine();
        text.AppendLine("WHERE TO START LOOKING");

        var leads = new List<string>();

        // Something physically stopping the machine homing outranks everything else - nothing
        // else can happen until it is cleared.
        if (knowledge?.HomeInterlock.Refused.FirstOrDefault(a => a.Blocking.Count > 0) is { } refused)
        {
            leads.Add($"The machine was told to home at {refused.Time:hh\\:mm\\:ss} and {refused.Outcome}, "
                      + $"with {string.Join(" and ", refused.Blocking.Select(b => b.Display))}. All four product "
                      + "sensors have to read 0 before it will home - start by clearing that one.");
        }

        // A fault we already understand beats anything worked out from the log shape alone.
        foreach (var match in knowledge?.MatchedFaults.Take(2) ?? Enumerable.Empty<MatchedFault>())
        {
            leads.Add($"\"{match.SeenAs}\" - {match.Known.WhatToCheck.FirstOrDefault() ?? match.Known.Meaning}");
        }

        foreach (var issue in knowledge?.IssuesSeenInThisLog.Take(1)
                              ?? Enumerable.Empty<KnownIssue>())
        {
            leads.Add($"Known problem on this machine, and this log shows signs of it: {issue.Title}. "
                      + "Rule it in or out before looking elsewhere.");
        }

        foreach (var glitch in knowledge?.PlatePresentEvents.Where(e => e.Verdict == PlatePresentVerdict.SensorGlitch).Take(1)
                               ?? Enumerable.Empty<PlatePresentEvent>())
        {
            leads.Add($"A plate present sensor glitched at {glitch.Time:hh\\:mm\\:ss} on the {glitch.Side.ToLowerInvariant()} "
                      + "side with nothing physically moving - check that sensor and its cable.");
        }

        foreach (var repeat in analysis.RepeatedFaults.Take(2))
        {
            leads.Add($"The repeating fault \"{repeat.Text}\" - check the sensor, cable and connector for that mechanism.");
        }

        if (analysis.Last?.Faults.LastOrDefault() is { } lastFault && analysis.RepeatedFaults.Count == 0)
        {
            leads.Add($"The last attempt stopped on \"{lastFault.Text}\" - start there.");
        }

        if (!string.IsNullOrWhiteSpace(file.SupportIssue))
        {
            leads.Add($"The operator's own words: \"{file.SupportIssue}\".");
        }

        if (analysis.RecentSettingChanges.Count > 0)
        {
            leads.Add($"{analysis.RecentSettingChanges.Count} setting(s) changed around this session - rule them in or out.");
        }

        if (leads.Count == 0)
        {
            text.AppendLine("  Nothing in the logs points anywhere specific. Confirm the complaint with the");
            text.AppendLine("  customer before going further.");
            return;
        }

        foreach (var lead in leads) text.AppendLine($"  - {ReportText.Wrap(lead, 4)}");
    }

    private static void AppendQuestions(StringBuilder text, SpidaLogAnalysis analysis, KnowledgeFindings? knowledge)
    {
        text.AppendLine();
        text.AppendLine("QUESTIONS FOR THE CUSTOMER");

        var questions = new List<string>();

        // Questions that matter on this particular machine go first.
        if (knowledge?.Knowledge is { } machine)
        {
            questions.AddRange(machine.CustomerQuestions);
        }

        if (analysis.RepeatedFaults.Count > 0)
        {
            questions.Add("Does this happen on every panel, or only some?");
            questions.Add("When did it start - was there a day it began?");
            questions.Add("Has anyone been working near that sensor, cable or guard recently?");
        }
        else
        {
            questions.Add("Has this happened more than once, or was it a one-off?");
            questions.Add("What was the operator doing at the time?");
        }

        if (analysis.RecentSettingChanges.Count > 0)
        {
            questions.Add("Was anything adjusted on the machine that day, and by whom?");
        }

        questions.Add("Does the machine recover if it is restarted, and for how long?");

        foreach (var question in questions.Distinct().Take(6))
        {
            text.AppendLine($"  - {ReportText.Wrap(question, 4)}");
        }
    }
}
