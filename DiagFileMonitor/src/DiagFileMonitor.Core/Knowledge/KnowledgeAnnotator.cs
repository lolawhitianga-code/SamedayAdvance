using DiagFileMonitor.Core.SpidaLogs;

namespace DiagFileMonitor.Core.Knowledge;

public class MatchedFault
{
    public KnownFault Known { get; init; } = new();

    /// <summary>The fault text as it actually appeared in the log.</summary>
    public string SeenAs { get; init; } = string.Empty;

    public int Occurrences { get; init; }
    public IReadOnlyList<int> CycleNumbers { get; init; } = Array.Empty<int>();

    /// <summary>True where it turned up on more than one attempt, which points at a real problem.</summary>
    public bool RepeatsAcrossAttempts => CycleNumbers.Count >= 2;
}

public class AxisSighting
{
    public string LogName { get; init; } = string.Empty;
    public AxisRole? Known { get; init; }
    public int Mentions { get; init; }

    public bool IsNew => Known is null;
}

public class KnowledgeFindings
{
    public MachineKnowledge? Knowledge { get; init; }

    /// <summary>The model the knowledge was looked up under, for the report header.</summary>
    public string? Model { get; init; }

    public string? SerialNumber { get; init; }

    /// <summary>True where this serial is one the knowledge base already lists.</summary>
    public bool SerialIsKnown { get; init; }

    public IReadOnlyList<MatchedFault> MatchedFaults { get; init; } = Array.Empty<MatchedFault>();

    /// <summary>Faults in the log that the knowledge base has nothing on.</summary>
    public IReadOnlyList<string> UnknownFaults { get; init; } = Array.Empty<string>();

    /// <summary>Known problems this log actually shows signs of.</summary>
    public IReadOnlyList<KnownIssue> IssuesSeenInThisLog { get; init; } = Array.Empty<KnownIssue>();

    /// <summary>
    /// Known problems reported on this serial before, with nothing in this log pointing at them.
    /// Background for the support person, not a lead.
    /// </summary>
    public IReadOnlyList<KnownIssue> IssueHistoryForSerial { get; init; } = Array.Empty<KnownIssue>();
    public IReadOnlyList<AxisSighting> Axes { get; init; } = Array.Empty<AxisSighting>();
    public IReadOnlyList<PlatePresentEvent> PlatePresentEvents { get; init; } = Array.Empty<PlatePresentEvent>();

    /// <summary>Whether anything was stopping the machine homing.</summary>
    public HomeInterlockFindings HomeInterlock { get; init; } = new();

    /// <summary>
    /// Drive fault codes found in the log. Read for every machine, not only ones we have notes
    /// for - the codes come from the CLX drive, not from the machine model.
    /// </summary>
    public IReadOnlyList<MotionControllerSighting> DriveFaults { get; init; } = Array.Empty<MotionControllerSighting>();

    /// <summary>Which electronics family each axis runs on, where the machine config was readable.</summary>
    public AxisHardwareMap AxisHardware { get; init; } = new();

    public bool HasAnything =>
        // Drive faults stand on their own: they come from the drive, not the machine model, so
        // there is something to report even for a machine we have no notes for.
        DriveFaults.Count > 0
        || (Knowledge is not null
            && (MatchedFaults.Count > 0 || IssuesSeenInThisLog.Count > 0 || IssueHistoryForSerial.Count > 0
                || Axes.Count > 0 || PlatePresentEvents.Count > 0 || UnknownFaults.Count > 0
                || HomeInterlock.Any));
}

/// <summary>
/// Lays what is known about a machine model over the machine-agnostic log analysis: matches the
/// faults found against fault messages this family is known to produce, pulls up issues already
/// reported on the serial, names the axes the log mentions, and runs the PlatePresentSwitch check.
/// </summary>
public static class KnowledgeAnnotator
{
    public static KnowledgeFindings Annotate(
        SpidaLogAnalysis analysis,
        IReadOnlyList<MachineLogEntry> machineLog,
        string? machineModel,
        string? serialNumber,
        string? machineConfigXmlPath = null)
    {
        var knowledge = MachineKnowledgeBase.Find(machineModel ?? analysis.MachineModelFromLog);

        // Drive faults are read whatever the machine is: an F-code comes from the CLX drive
        // rather than from anything model specific, and a machine we have no notes for still
        // raises them.
        var driveFaults = MotionControllerFaults.Find(machineLog);
        var axisHardware = AxisHardware.Read(machineConfigXmlPath ?? string.Empty);

        if (knowledge is null)
        {
            return new KnowledgeFindings
            {
                Model = machineModel ?? analysis.MachineModelFromLog,
                SerialNumber = serialNumber,
                DriveFaults = driveFaults,
                AxisHardware = axisHardware
            };
        }

        var faultsSeen = analysis.Cycles
            .SelectMany(cycle => cycle.Faults.Select(fault => (cycle.Number, fault.Text)))
            .ToList();

        var matched = new List<MatchedFault>();
        var accountedFor = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var known in knowledge.Faults)
        {
            var hits = faultsSeen
                .Where(f => f.Text.Contains(known.Match, StringComparison.OrdinalIgnoreCase))
                .ToList();

            // A fault we have written down is a fault whatever the generic wording filter made
            // of it. Falling back to the raw log means a machine whose messages do not read like
            // failures - the Tornado's "Board not expected length" - is still recognised.
            if (hits.Count == 0) hits = FindInRawLog(machineLog, analysis, known);

            if (hits.Count == 0) continue;

            foreach (var hit in hits) accountedFor.Add(hit.Text);

            matched.Add(new MatchedFault
            {
                Known = known,
                SeenAs = hits[0].Text,
                Occurrences = hits.Count,
                CycleNumbers = hits.Select(h => h.Number).Distinct().OrderBy(n => n).ToList()
            });
        }

        var unknown = faultsSeen
            .Select(f => f.Text)
            .Where(text => !accountedFor.Contains(text))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var signalled = FindSignalledIssues(knowledge, analysis, faultsSeen.Select(f => f.Text).ToList());

        return new KnowledgeFindings
        {
            Knowledge = knowledge,
            Model = machineModel ?? analysis.MachineModelFromLog,
            SerialNumber = serialNumber,
            SerialIsKnown = serialNumber is not null
                            && knowledge.KnownSerials.Any(s => s.Equals(serialNumber, StringComparison.OrdinalIgnoreCase)),
            MatchedFaults = matched.OrderByDescending(m => m.Occurrences).ToList(),
            UnknownFaults = unknown,
            IssuesSeenInThisLog = signalled,
            IssueHistoryForSerial = knowledge.Issues
                .Where(i => !signalled.Contains(i))
                .Where(i => serialNumber is not null
                            && i.Serials.Any(s => s.Equals(serialNumber, StringComparison.OrdinalIgnoreCase)))
                .ToList(),
            Axes = FindAxes(knowledge, machineLog),
            PlatePresentEvents = PlatePresentCheck.Find(machineLog),
            HomeInterlock = HomeInterlockCheck.Check(machineLog),
            DriveFaults = driveFaults,
            AxisHardware = axisHardware
        };
    }

    /// <summary>
    /// Looks for a known fault's text anywhere in the log, attributing each hit to the unit it
    /// falls in so a fault repeating across attempts still reads as repeating.
    /// </summary>
    private static List<(int Number, string Text)> FindInRawLog(
        IReadOnlyList<MachineLogEntry> machineLog, SpidaLogAnalysis analysis, KnownFault known)
    {
        return machineLog
            .Where(e => e.Category == MachineLogCategory.Other)
            .Where(e => e.Description.Contains(known.Match, StringComparison.OrdinalIgnoreCase))
            .Select(e => (
                Number: analysis.Cycles.FirstOrDefault(c => e.Time >= c.Start && e.Time <= c.End)?.Number ?? 0,
                Text: e.Description.Trim()))
            .ToList();
    }

    /// <summary>
    /// An issue counts as seen here only where its signal text turns up in a fault or a real
    /// ErrLog entry. Matching against every log line instead would flag the ejection bug on every
    /// export, because the eject axes are named in the log the moment the machine homes.
    /// </summary>
    private static List<KnownIssue> FindSignalledIssues(
        MachineKnowledge knowledge,
        SpidaLogAnalysis analysis,
        IReadOnlyList<string> faultTexts)
    {
        var haystack = faultTexts
            .Concat(analysis.RealErrors.Select(e => e.Text))
            .ToList();

        return knowledge.Issues
            .Where(issue => issue.Signals.Any(signal =>
                haystack.Any(text => text.Contains(signal, StringComparison.OrdinalIgnoreCase))))
            .ToList();
    }

    /// <summary>
    /// Counts the axis tags the log mentions. Anything the log names that the knowledge base does
    /// not have is reported rather than dropped, so a new axis turns up on its own.
    /// </summary>
    private static List<AxisSighting> FindAxes(MachineKnowledge knowledge, IReadOnlyList<MachineLogEntry> machineLog)
    {
        var axisLines = machineLog
            .Where(e => e.Category is MachineLogCategory.MotionEvent or MachineLogCategory.Other)
            .Where(e => e.Description.Contains("Axis ", StringComparison.OrdinalIgnoreCase))
            .GroupBy(e => e.Tag, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return axisLines
            .Select(group => new AxisSighting
            {
                LogName = group.Key,
                Known = knowledge.Axes.FirstOrDefault(a =>
                    a.LogName.Equals(group.Key, StringComparison.OrdinalIgnoreCase)),
                Mentions = group.Count()
            })
            .OrderBy(a => a.IsNew ? 0 : 1)
            .ThenBy(a => a.LogName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
