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
        KnowledgeFindings? knowledge = null,
        ComplaintFindings? complaint = null)
    {
        var text = new StringBuilder();

        text.AppendLine($"DIAGNOSTIC ANALYSIS - {file.OriginalFileName}");
        text.AppendLine(new string('-', 78));
        text.AppendLine($"Machine:   {file.MachineType} ({file.MachineName})   serial {file.SerialNumber}");
        text.AppendLine($"Customer:  {file.Customer}, {file.SiteLocation}");
        text.AppendLine($"Software:  {file.SoftwareName} {file.Version}");

        if (SoftwareVersion.Note(file.Version) is { } versionNote)
        {
            text.AppendLine($"           !! {ReportText.Wrap(versionNote, 14)}");
        }
        text.AppendLine($"Arrived:   {file.ArrivedDisplay}");

        if (analysis.MachineModelFromLog is { } fromLog)
        {
            var agrees = fromLog.Contains(file.MachineType, StringComparison.OrdinalIgnoreCase);
            text.AppendLine($"MachineLog reports model: {fromLog}{(agrees ? string.Empty : "   <-- does not match Machine.xml")}");
        }

        AppendOperatorText(text, file);
        if (complaint is not null) AppendComplaint(text, complaint);
        AppendHowItEnded(text, analysis);
        if (knowledge is not null) AppendMotorConfirm(text, knowledge);
        if (knowledge is not null) AppendDriveFaults(text, knowledge);
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

    /// <summary>
    /// What the operator said, turned into somewhere to look. This goes first because the logs
    /// are loudest about whatever happens most often, which is rarely the complaint.
    /// </summary>
    private static void AppendComplaint(StringBuilder text, ComplaintFindings complaint)
    {
        if (!complaint.HasIssueText) return;

        text.AppendLine();
        text.AppendLine("START HERE - WHAT THE OPERATOR DESCRIBED");

        if (!complaint.Any)
        {
            text.AppendLine($"  \"{complaint.Issue}\"");
            text.AppendLine("  Nothing in that matches a complaint we have a routine for, so the rest of this");
            text.AppendLine("  report works from the logs alone. Read the operator's words first anyway.");
            return;
        }

        foreach (var match in complaint.Topics)
        {
            text.AppendLine();
            text.AppendLine($"  {match.Topic.Name.ToUpperInvariant()}");
            text.AppendLine($"    (from \"{string.Join("\", \"", match.MatchedOn)}\" in what the operator wrote)");

            foreach (var step in match.Topic.LookAt)
            {
                text.AppendLine($"      - {ReportText.Wrap(step, 8)}");
            }

            AppendRelatedChanges(text, match);
        }
    }

    private static void AppendRelatedChanges(StringBuilder text, MatchedTopic match)
    {
        if (match.Topic.SettingWords.Count == 0) return;

        text.AppendLine();

        if (match.RelatedChanges.Count == 0)
        {
            text.AppendLine($"      Change.log has no {string.Join("/", match.Topic.SettingWords)} settings changed at all,");
            text.AppendLine("      so this is not a setting somebody moved.");
            return;
        }

        text.AppendLine($"      {match.RelatedChanges.Count} matching setting change(s), newest first:");
        foreach (var change in match.RelatedChanges)
        {
            text.AppendLine($"        {change.Display}");
        }
    }

    /// <summary>
    /// The end of the log, first. A bundle is normally exported within a few minutes of the
    /// problem, so the last thing the machine did is usually the thing being reported - and a
    /// long quiet tail says it stopped and sat there rather than carrying on.
    /// </summary>
    private static void AppendHowItEnded(StringBuilder text, SpidaLogAnalysis analysis)
    {
        if (analysis.FinalEntries.Count == 0) return;

        text.AppendLine();
        text.AppendLine("HOW IT ENDED - read this first");
        text.AppendLine($"  The file is normally exported within minutes of the problem, so the end of");
        text.AppendLine($"  MachineLog.txt is usually the problem itself.");
        text.AppendLine();

        if (analysis.LogEnd is { } end)
        {
            text.AppendLine($"  The log ends at {end:hh\\:mm\\:ss}.");
        }

        if (analysis.LastNotableEvent is { } last && analysis.SilenceBeforeEnd is { } silence)
        {
            text.AppendLine($"  The last thing the machine actually did was at {last.Time:hh\\:mm\\:ss}:");
            text.AppendLine($"      {last.Display}");

            if (silence >= TimeSpan.FromSeconds(30))
            {
                text.AppendLine();
                text.AppendLine($"  {ReportText.Wrap($"Nothing of note happened for the last {MachineCycle.Describe(silence)}. "
                    + "The machine was sitting there when the file was taken, so whatever stopped it is "
                    + "above, not below.", 2)}");
            }
        }

        text.AppendLine();
        var skipped = analysis.ChatterSkipped > 0
            ? $" (heartbeat lines left out - {analysis.ChatterSkipped} of them repeat too often to mean anything)"
            : string.Empty;

        text.AppendLine($"  The last {analysis.FinalEntries.Count} lines that say something{skipped}:");
        foreach (var entry in analysis.FinalEntries)
        {
            text.AppendLine($"      {entry.Display}");
        }
    }

    /// <summary>
    /// What each motor's run command and its confirmation input did. This is the difference
    /// between "the machine was asked to cut" and "the blade was turning", and it is usually the
    /// whole answer when an operator writes that something is not running.
    /// <para>
    /// It says when the confirmation last read 1 and last read 0 whatever the verdict, because
    /// "it never appears in this file" is an answer to that question too - a short export taken
    /// after the motor stopped carries no change at all, and staying silent there reads as
    /// nothing being wrong.
    /// </para>
    /// </summary>
    private static void AppendMotorConfirm(StringBuilder text, KnowledgeFindings knowledge)
    {
        var findings = knowledge.MotorConfirm;
        if (!findings.Any) return;

        var failures = findings.Failures.ToList();

        text.AppendLine();
        text.AppendLine(failures.Count > 0
            ? "*** A MOTOR WAS TOLD TO RUN AND DID NOT REPORT BACK ***"
            : "MOTORS TOLD TO RUN - DID THEY REPORT BACK?");

        foreach (var status in findings.Statuses.Take(6))
        {
            text.AppendLine();
            text.AppendLine($"  {ReportText.Wrap(status.Summary, 2)}");
            text.AppendLine($"      {ReportText.Wrap(status.ConfirmSentence, 6)}");

            if (status.MachineWaitedFor is { } waited)
            {
                text.AppendLine($"      the machine's own words: \"{waited}\"");
            }
        }

        if (failures.Count == 0) return;

        text.AppendLine();
        text.AppendLine("  The command went out and the confirmation did not come back, so the motor was");
        text.AppendLine("  not turning. Check the contactor, its auxiliary contact, the overload and the");
        text.AppendLine("  confirmation wiring before anything further down the process.");

        var healthy = findings.Healthy.Select(h => h.Motor).ToList();
        if (healthy.Count > 0)
        {
            text.AppendLine($"  For comparison, {string.Join(" and ", healthy)} confirmed normally in this "
                            + "same log.");
        }
    }

    /// <summary>
    /// Drive fault codes, high up and on their own. The drive says exactly what is wrong and on
    /// which axis - "F02 Encoder Wiring Fault on Axis-FixedSidePusher" - but buried in a block of
    /// quoted context it reads as just more log noise, and the report ends up advising a generic
    /// sensor check when the machine has already named the part.
    /// </summary>
    private static void AppendDriveFaults(StringBuilder text, KnowledgeFindings knowledge)
    {
        if (knowledge.DriveFaults.Count == 0) return;

        var faults = knowledge.DriveFaults.Where(f => f.Code.IsFault).ToList();
        var states = knowledge.DriveFaults.Where(f => !f.Code.IsFault).ToList();

        text.AppendLine();
        text.AppendLine("*** DRIVE FAULT CODES - THE MACHINE HAS NAMED THE PROBLEM ***");

        foreach (var sighting in faults)
        {
            text.AppendLine();
            text.AppendLine($"  {sighting.Code.Code}  {ReportText.Wrap(sighting.Code.Meaning, 7)}");
            text.AppendLine($"      x{sighting.Occurrences}{sighting.Where}"
                            + Window(sighting) + ".");
            text.AppendLine($"      Check: {ReportText.Wrap(sighting.Code.WhatToCheck, 13)}");

            if (sighting.Code.CallCyberLogix)
            {
                text.AppendLine("      This one is not fixable on site - CyberLogix need to see it.");
            }
        }

        foreach (var sighting in states)
        {
            text.AppendLine();
            text.AppendLine($"  {sighting.Code.Code}  {ReportText.Wrap(sighting.Code.Meaning, 6)}");
            text.AppendLine($"      x{sighting.Occurrences}{sighting.Where}{Window(sighting)}. "
                            + "Not a failure in itself.");
        }

        AppendElectronics(text, knowledge);
    }

    private static string Window(MotionControllerSighting sighting)
    {
        if (sighting.FirstSeen is not { } first || sighting.LastSeen is not { } last) return string.Empty;

        return first == last
            ? $", at {first:hh\\:mm\\:ss}"
            : $", from {first:hh\\:mm\\:ss} to {last:hh\\:mm\\:ss}";
    }

    /// <summary>
    /// Which electronics the axes run on. F-codes come from the CLX drives, so on a machine that
    /// mixes CLX and Omron it is worth knowing which family the faulting axis belongs to.
    /// </summary>
    private static void AppendElectronics(StringBuilder text, KnowledgeFindings knowledge)
    {
        var hardware = knowledge.AxisHardware;

        text.AppendLine();
        text.AppendLine("  These are CyberLogix CLX drive codes, read off the drive's status display.");

        if (!hardware.Any) return;

        text.AppendLine($"  This machine's axes: {hardware.Summary}.");

        if (hardware.IsMixed)
        {
            text.AppendLine("  It runs both families, so check the faulting axis is a CLX one:");
            foreach (var axis in hardware.InUse.OrderBy(a => a.Family).ThenBy(a => a.DisplayName))
            {
                text.AppendLine($"    {axis.DisplayName,-46} {AxisHardwareMap.Describe(axis.Family)}");
            }
        }

        if (hardware.HasOmron) AppendOmronNote(text);
    }

    /// <summary>
    /// What to do when the axis in question is an Omron one. The CLX codes above do not apply to
    /// it, so the drive has to be read directly - it shows its own alarm as "Er" and two bytes.
    /// </summary>
    private static void AppendOmronNote(StringBuilder text)
    {
        text.AppendLine();
        text.AppendLine("  The Omron axes run 1S-series drives (R88D-1SN..-ECT) on EtherCAT. The CLX codes");
        text.AppendLine("  above do not apply to them. An Omron drive shows its own alarm as \"Er\" and two");
        text.AppendLine("  bytes - Er 16 00 is Overload - so ask site to read the display and the LEDs:");

        foreach (var (name, meaning) in OmronServoDrives.Indicators.Take(4))
        {
            text.AppendLine($"    {name,-9} {ReportText.Wrap(meaning, 14)}");
        }

        text.AppendLine();
        text.AppendLine("  Safety: CHARGE stays lit after power off. Wait the drive's discharge time before");
        text.AppendLine("  touching anything - 10 minutes on the 400 V models, 15 to 20 on 100 and 200 V.");
        text.AppendLine("  A dark display is not proof the bus is discharged.");
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
            text.AppendLine("  Nothing was changed around this session.");
        }
        else
        {
            foreach (var change in analysis.RecentSettingChanges.Take(15))
            {
                text.AppendLine($"  {change.Display}");
            }

            text.AppendLine();
            text.AppendLine("  Listed as context. Ask the customer to confirm whether any of these are related");
            text.AppendLine("  rather than assuming they are.");
        }

        AppendLatestChanges(text, analysis);
    }

    /// <summary>
    /// The last few changes whatever their date. A machine can run for months on a setting
    /// somebody changed once, so the most recent change is worth seeing even when it is old -
    /// otherwise this section reads "none" on a machine whose settings were quietly altered.
    /// </summary>
    private static void AppendLatestChanges(StringBuilder text, SpidaLogAnalysis analysis)
    {
        if (analysis.LatestSettingChanges.Count == 0) return;

        var alreadyListed = analysis.RecentSettingChanges.Take(15).ToHashSet();
        var latest = analysis.LatestSettingChanges.Where(c => !alreadyListed.Contains(c)).ToList();

        if (latest.Count == 0) return;

        text.AppendLine();
        text.AppendLine($"  Last {analysis.LatestSettingChanges.Count} change(s) on this machine, whenever they happened:");

        var sessionLocal = analysis.SessionDateUtc.ToLocalTime();
        foreach (var change in latest)
        {
            text.AppendLine($"    {change.Display}{Age(change.Timestamp, sessionLocal)}");
        }
    }

    /// <summary>How long before this bundle a change was made.</summary>
    private static string Age(DateTime changedAt, DateTime sessionLocal)
    {
        var days = (sessionLocal.Date - changedAt.Date).Days;

        return days switch
        {
            < 0 => "   after this file",
            0 => "   same day",
            1 => "   1 day before",
            < 31 => $"   {days} days before",
            < 365 => $"   about {days / 30} month(s) before",
            _ => $"   about {days / 365} year(s) before"
        };
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

        // How long before the end of the log something happened, since the file is taken within
        // minutes of the problem and the end of the log is where the problem is.
        string Age(TimeSpan? when) =>
            when is { } t && analysis.LogEnd is { } end && end > t
                ? $", {MachineCycle.Describe(end - t)} before the log ends"
                : string.Empty;

        // The last thing the machine did comes first, whatever else is in the log.
        if (analysis.LastNotableEvent is { } lastEvent)
        {
            leads.Add($"The machine's last act was {lastEvent.Tag} \"{lastEvent.Description}\" at "
                      + $"{lastEvent.Time:hh\\:mm\\:ss}{Age(lastEvent.Time)}. Start at the end of the log "
                      + "and work back.");
        }

        // A motor that never reported itself running is as concrete as it gets, and it is
        // normally the exact thing the operator wrote down.
        foreach (var failure in knowledge?.MotorConfirm.Failures.Take(2)
                                ?? Enumerable.Empty<MotorConfirmStatus>())
        {
            leads.Add($"{failure.Motor} was commanded on at {failure.LastCommandedOn:hh\\:mm\\:ss}"
                      + $"{Age(failure.LastCommandedOn)} and {failure.ConfirmTag} did not come on. "
                      + $"{failure.ConfirmSentence} Check the contactor, its auxiliary contact, the "
                      + "overload and the confirmation wiring.");
        }

        // A drive fault outranks everything else: the machine has named the failed part, so a
        // generic "check the sensor and cable" is worse than useless beside it. Most recent first.
        foreach (var fault in knowledge?.DriveFaults.Where(f => f.Code.IsFault).Take(2)
                              ?? Enumerable.Empty<MotionControllerSighting>())
        {
            leads.Add($"{fault.Code.Code} {fault.Code.ShortMeaning}{fault.Where} (x{fault.Occurrences}"
                      + $"{Age(fault.LastSeen)}) - {fault.Code.WhatToCheck}");
        }

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
            leads.Add($"A plate present sensor glitched at {glitch.Time:hh\\:mm\\:ss}{Age(glitch.Time)} on the "
                      + $"{glitch.Side.ToLowerInvariant()} side with nothing physically moving - check that "
                      + "sensor and its cable.");
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
