using System.Text;
using DiagFileMonitor.Core.SpidaLogs;

namespace DiagFileMonitor.Core.Knowledge;

/// <summary>
/// Writes the machine-specific part of the report: what the fault means on this machine, what is
/// already known to be wrong with it, and what is still unproven.
/// </summary>
public static class KnowledgeReportFormatter
{
    public static void Append(StringBuilder text, KnowledgeFindings findings)
    {
        if (findings.Knowledge is not { } knowledge)
        {
            text.AppendLine();
            text.AppendLine("WHAT WE KNOW ABOUT THIS MACHINE");
            text.AppendLine($"  Nothing written down yet for \"{findings.Model ?? "(unknown model)"}\".");
            text.AppendLine("  Everything above comes from the logs alone.");
            return;
        }

        text.AppendLine();
        text.AppendLine($"WHAT WE KNOW ABOUT THIS MACHINE - {knowledge.Model}");
        text.AppendLine($"  {Wrap(knowledge.Summary, 4)}");

        if (findings.SerialNumber is { Length: > 0 })
        {
            text.AppendLine(findings.SerialIsKnown
                ? $"  Serial {findings.SerialNumber} is one we already have history on."
                : $"  Serial {findings.SerialNumber} is not one we have history on yet.");
        }

        AppendHomeInterlock(text, findings);
        AppendMatchedFaults(text, findings);
        AppendIssues(text, findings);
        AppendPlatePresent(text, findings);
        AppendAxes(text, findings);
        AppendNoise(text, knowledge);
        AppendGaps(text, knowledge);
    }

    private static void AppendHomeInterlock(StringBuilder text, KnowledgeFindings findings)
    {
        var home = findings.HomeInterlock;
        if (!home.Any) return;

        text.AppendLine();
        text.AppendLine("  CAN IT HOME? - all four product sensors must read 0");

        if (home.SensorsSeen.Count == 0)
        {
            text.AppendLine("    No product sensor changes in this log, so all four were most likely already");
            text.AppendLine("    at 0. Nothing here says the interlock was the problem.");
        }

        foreach (var sensor in home.SensorsSeen)
        {
            var verdict = sensor.Value == 0
                ? "clear"
                : "STILL DETECTING SOMETHING - this alone stops the machine homing";

            text.AppendLine($"    {sensor.Display}  at {sensor.ChangedAt:hh\\:mm\\:ss}  {verdict}");
        }

        if (home.SensorsNotInLog.Count > 0)
        {
            text.AppendLine($"    Never changed in this log: {string.Join(", ", home.SensorsNotInLog)}. The log only");
            text.AppendLine("      records changes, so these were most likely sitting at 0 throughout.");
        }

        if (home.Attempts.Count == 0) return;

        text.AppendLine();
        foreach (var attempt in home.Attempts)
        {
            text.AppendLine($"    {attempt.Time:hh\\:mm\\:ss} {attempt.Command} - {attempt.Outcome}");

            foreach (var blocked in attempt.Blocking)
            {
                text.AppendLine($"        blocked by {blocked.Display}");
            }
        }

        var refusedWithCause = home.Refused.FirstOrDefault(a => a.Blocking.Count > 0);
        if (refusedWithCause is not null)
        {
            text.AppendLine();
            text.AppendLine($"    {Wrap($"The machine was told to home at {refusedWithCause.Time:hh\\:mm\\:ss} and "
                + $"{refusedWithCause.Outcome}, with {string.Join(" and ", refusedWithCause.Blocking.Select(b => b.Display))}. "
                + "That is the interlock doing its job - clear whatever that sensor is seeing, or check the "
                + "sensor itself if there is nothing there.", 4)}");
        }
    }

    private static void AppendMatchedFaults(StringBuilder text, KnowledgeFindings findings)
    {
        if (findings.MatchedFaults.Count == 0 && findings.UnknownFaults.Count == 0) return;

        text.AppendLine();
        text.AppendLine("  FAULTS IN THIS LOG WE RECOGNISE");

        if (findings.MatchedFaults.Count == 0)
        {
            text.AppendLine("    None of the faults in this log match anything written down for this machine.");
        }

        foreach (var match in findings.MatchedFaults)
        {
            var repeats = match.RepeatsAcrossAttempts
                ? $" - repeated across attempts {string.Join(", ", match.CycleNumbers)}, so treat it as real"
                : " - seen once";

            text.AppendLine();
            text.AppendLine($"    \"{match.SeenAs}\"");
            text.AppendLine($"      x{match.Occurrences}{repeats}.");
            text.AppendLine($"      [{match.Known.Confidence.Label()}] {Wrap(match.Known.Meaning, 6)}");

            if (match.Known.WhatToCheck.Count > 0)
            {
                text.AppendLine("      Check:");
                foreach (var check in match.Known.WhatToCheck)
                {
                    text.AppendLine($"        - {Wrap(check, 10)}");
                }
            }
        }

        if (findings.UnknownFaults.Count > 0)
        {
            text.AppendLine();
            text.AppendLine("    Faults here we have nothing written down for - worth adding once understood:");
            foreach (var fault in findings.UnknownFaults.Take(8))
            {
                text.AppendLine($"      - {fault}");
            }
        }
    }

    private static void AppendIssues(StringBuilder text, KnowledgeFindings findings)
    {
        if (findings.IssuesSeenInThisLog.Count > 0)
        {
            text.AppendLine();
            text.AppendLine("  KNOWN PROBLEMS THIS LOG POINTS AT");
            foreach (var issue in findings.IssuesSeenInThisLog) AppendIssue(text, issue);
        }

        if (findings.IssueHistoryForSerial.Count > 0)
        {
            text.AppendLine();
            text.AppendLine($"  HISTORY ON SERIAL {findings.SerialNumber} - background, nothing in this log points at these");
            foreach (var issue in findings.IssueHistoryForSerial) AppendIssue(text, issue);
        }
    }

    private static void AppendIssue(StringBuilder text, KnownIssue issue)
    {
        var serials = issue.Serials.Count > 0
            ? $" (reported on {string.Join(", ", issue.Serials)})"
            : " (whole family)";

        text.AppendLine();
        text.AppendLine($"    [{issue.Confidence.Label()}] {issue.Title}{serials}");
        text.AppendLine($"      {Wrap(issue.Detail, 6)}");
    }

    private static void AppendPlatePresent(StringBuilder text, KnowledgeFindings findings)
    {
        if (findings.PlatePresentEvents.Count == 0) return;

        text.AppendLine();
        text.AppendLine("  PLATE PRESENT SENSOR - REAL OR GLITCH?");

        foreach (var group in findings.PlatePresentEvents.GroupBy(e => e.Verdict))
        {
            text.AppendLine();
            text.AppendLine($"    {group.Count()} x {Describe(group.Key)}");
            text.AppendLine($"      {Wrap(group.First().Explanation, 6)}");

            foreach (var occurrence in group.Take(5))
            {
                text.AppendLine($"        {occurrence.Time:hh\\:mm\\:ss\\.fff}  {occurrence.Side} side ({occurrence.Address})");
            }
        }
    }

    private static string Describe(PlatePresentVerdict verdict) => verdict switch
    {
        PlatePresentVerdict.NormalClampRelease => "normal clamp release",
        PlatePresentVerdict.SensorGlitch => "sensor glitch, not lost product",
        _ => "does not match either pattern"
    };

    private static void AppendAxes(StringBuilder text, KnowledgeFindings findings)
    {
        if (findings.Axes.Count == 0) return;

        text.AppendLine();
        text.AppendLine("  AXES THIS LOG MENTIONS");

        foreach (var axis in findings.Axes)
        {
            if (axis.Known is { } known)
            {
                text.AppendLine($"    {axis.LogName,-22} {known.PlainName}");
                text.AppendLine($"      [{known.Confidence.Label()}] {Wrap(known.Role, 6)}");
            }
            else
            {
                text.AppendLine($"    {axis.LogName,-22} not in our notes - new axis tag, worth writing down");
            }
        }
    }

    private static void AppendNoise(StringBuilder text, MachineKnowledge knowledge)
    {
        if (knowledge.BackgroundNoise.Count == 0) return;

        text.AppendLine();
        text.AppendLine("  NORMAL FOR THIS MACHINE - DON'T CHASE ON ITS OWN");
        foreach (var noise in knowledge.BackgroundNoise)
        {
            text.AppendLine($"    - {Wrap(noise, 6)}");
        }
    }

    private static void AppendGaps(StringBuilder text, MachineKnowledge knowledge)
    {
        if (knowledge.OpenGaps.Count == 0) return;

        text.AppendLine();
        text.AppendLine("  STILL UNPROVEN - don't quote these to a customer as fact");
        foreach (var gap in knowledge.OpenGaps)
        {
            text.AppendLine($"    - {Wrap(gap, 6)}");
        }
    }

    private static string Wrap(string value, int indent) => ReportText.Wrap(value, indent);
}
