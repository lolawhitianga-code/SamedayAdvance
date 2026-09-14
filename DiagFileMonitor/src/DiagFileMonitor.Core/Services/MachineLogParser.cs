using System.Globalization;
using System.Text.RegularExpressions;

namespace DiagFileMonitor.Core.Services;

public class MachineLogStep
{
    public string Name { get; init; } = string.Empty;
    public DateTime Start { get; init; }
    public DateTime End { get; init; }
    public TimeSpan Duration => End - Start;
}

/// <summary>
/// Turns machinelog.txt into timed steps.
/// <para>
/// The line format is configurable because it varies by machine. The default expects a
/// timestamp, then START or END, then a step name, e.g.
/// <c>2026-09-01 08:00:00.000 START Preheat</c>. Point <see cref="LinePattern"/> at your own
/// shape if it differs - it needs named groups: timestamp, marker, step.
/// </para>
/// </summary>
public class MachineLogParser
{
    public const string DefaultLinePattern =
        @"^(?<timestamp>\d{4}-\d{2}-\d{2}[ T]\d{2}:\d{2}:\d{2}(?:[.,]\d+)?)\s+(?<marker>START|BEGIN|END|FINISH|DONE)\s+(?<step>.+?)\s*$";

    private static readonly string[] TimestampFormats =
    {
        "yyyy-MM-dd HH:mm:ss.fff", "yyyy-MM-dd HH:mm:ss,fff", "yyyy-MM-dd HH:mm:ss",
        "yyyy-MM-ddTHH:mm:ss.fff", "yyyy-MM-ddTHH:mm:ss,fff", "yyyy-MM-ddTHH:mm:ss"
    };

    private static readonly string[] StartMarkers = { "START", "BEGIN" };

    private readonly Regex _pattern;

    public string LinePattern { get; }

    public MachineLogParser(string? linePattern = null)
    {
        LinePattern = string.IsNullOrWhiteSpace(linePattern) ? DefaultLinePattern : linePattern;
        _pattern = new Regex(LinePattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
    }

    public IReadOnlyList<MachineLogStep> Parse(IEnumerable<string> lines)
    {
        var steps = new List<MachineLogStep>();
        var open = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in lines)
        {
            var match = _pattern.Match(line);
            if (!match.Success) continue;
            if (!TryParseTimestamp(match.Groups["timestamp"].Value, out var timestamp)) continue;

            var step = match.Groups["step"].Value.Trim();
            var isStart = StartMarkers.Contains(match.Groups["marker"].Value, StringComparer.OrdinalIgnoreCase);

            if (isStart)
            {
                open[step] = timestamp;
            }
            else if (open.Remove(step, out var start) && timestamp >= start)
            {
                steps.Add(new MachineLogStep { Name = step, Start = start, End = timestamp });
            }
        }

        return steps;
    }

    public IReadOnlyList<MachineLogStep> ParseFile(string path) =>
        File.Exists(path) ? Parse(File.ReadLines(path)) : Array.Empty<MachineLogStep>();

    private static bool TryParseTimestamp(string value, out DateTime timestamp) =>
        DateTime.TryParseExact(value, TimestampFormats, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeLocal | DateTimeStyles.AdjustToUniversal, out timestamp);
}
