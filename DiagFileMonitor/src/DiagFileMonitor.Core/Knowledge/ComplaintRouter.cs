using DiagFileMonitor.Core.SpidaLogs;

namespace DiagFileMonitor.Core.Knowledge;

public class MatchedTopic
{
    public ComplaintTopic Topic { get; init; } = new();

    /// <summary>The operator's words that matched, so the reading can be checked.</summary>
    public IReadOnlyList<string> MatchedOn { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Settings changes that touch this topic, over the whole Change.log rather than just the
    /// day of the export - a gun height changed three weeks ago still explains a gun complaint.
    /// </summary>
    public IReadOnlyList<ChangeLogEntry> RelatedChanges { get; init; } = Array.Empty<ChangeLogEntry>();

    /// <summary>Lines from MachineLog.txt under the tags this topic cares about.</summary>
    public IReadOnlyList<MachineLogEntry> RelatedLogLines { get; init; } = Array.Empty<MachineLogEntry>();
}

public class ComplaintFindings
{
    public string Issue { get; init; } = string.Empty;
    public IReadOnlyList<MatchedTopic> Topics { get; init; } = Array.Empty<MatchedTopic>();

    public bool HasIssueText => Issue.Trim().Length > 0;
    public bool Any => Topics.Count > 0;
}

/// <summary>
/// Works out what the operator was actually complaining about and points the report at that.
/// <para>
/// The logs shout about whatever happens most, which is rarely the complaint. If the operator
/// says the nail gun is in the wrong position, gun heights are what matters, even though nothing
/// in MachineLog.txt will look unusual.
/// </para>
/// </summary>
public static class ComplaintRouter
{
    /// <summary>Changes older than this are not worth listing even when they match.</summary>
    private const int MaxChangesPerTopic = 12;

    public static ComplaintFindings Route(
        string? issue,
        IReadOnlyList<ChangeLogEntry> changeLog,
        IReadOnlyList<MachineLogEntry> machineLog,
        string? machineModel = null)
    {
        var text = (issue ?? string.Empty).Trim();
        if (text.Length == 0) return new ComplaintFindings();

        var matches = new List<MatchedTopic>();

        foreach (var topic in ComplaintTopics.All.Where(t => t.AppliesTo(machineModel)))
        {
            var hits = topic.Keywords
                .Where(k => text.Contains(k, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (hits.Count == 0) continue;

            matches.Add(new MatchedTopic
            {
                Topic = topic,
                MatchedOn = hits,
                RelatedChanges = FindChanges(changeLog, topic),
                RelatedLogLines = FindLogLines(machineLog, topic)
            });
        }

        return new ComplaintFindings
        {
            Issue = text,
            // The topic matching the most of the operator's words is the likeliest reading.
            Topics = matches.OrderByDescending(m => m.MatchedOn.Count).ToList()
        };
    }

    private static List<ChangeLogEntry> FindChanges(IReadOnlyList<ChangeLogEntry> changeLog, ComplaintTopic topic)
    {
        if (topic.SettingWords.Count == 0) return new List<ChangeLogEntry>();

        return changeLog
            .Where(c => topic.SettingWords.Any(w =>
                c.Setting.Contains(w, StringComparison.OrdinalIgnoreCase)
                || c.Category.Contains(w, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(c => c.Timestamp)
            .Take(MaxChangesPerTopic)
            .ToList();
    }

    private static List<MachineLogEntry> FindLogLines(IReadOnlyList<MachineLogEntry> machineLog, ComplaintTopic topic)
    {
        if (topic.LogTags.Count == 0) return new List<MachineLogEntry>();

        return machineLog
            .Where(e => topic.LogTags.Any(t => e.Tag.Contains(t, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }
}
