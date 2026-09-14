using System.Globalization;
using System.Text.RegularExpressions;

namespace DiagFileMonitor.Core.SpidaLogs;

public enum MachineLogCategory
{
    InputChange,
    OutputChange,
    MotionEvent,
    Other,
    Unknown
}

public class MachineLogEntry
{
    public int LineNumber { get; init; }
    public TimeSpan Time { get; init; }
    public MachineLogCategory Category { get; init; }
    public string Tag { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string RawLine { get; init; } = string.Empty;

    public string Display => $"{Time:hh\\:mm\\:ss\\.fff}  {Category,-13} {Tag}  {Description}".TrimEnd();
}

/// <summary>
/// Reads MachineLog.txt, whose lines are
/// <c>HH:MM:SS.fffffff,  Category,  Tag,  Description</c>.
/// <para>
/// The delimiter is sniffed per file because some exports are tab separated rather than comma
/// separated, and Windows line endings are stripped. Only the four documented categories are
/// recognised; anything else is Unknown so it can be reported rather than silently dropped.
/// </para>
/// </summary>
public static class MachineLogFile
{
    private static readonly Regex TimePattern = new(@"^\s*(?<h>\d{1,2}):(?<m>\d{2}):(?<s>\d{2})(?:\.(?<frac>\d+))?\s*$",
        RegexOptions.Compiled);

    public static IReadOnlyList<MachineLogEntry> Parse(IEnumerable<string> lines)
    {
        var entries = new List<MachineLogEntry>();
        var lineNumber = 0;
        char? delimiter = null;

        foreach (var rawLine in lines)
        {
            lineNumber++;
            var line = rawLine.TrimEnd('\r');
            if (line.Trim().Length == 0) continue;

            delimiter ??= SniffDelimiter(line);

            var parts = line.Split(delimiter.Value);
            if (parts.Length < 2) continue;

            if (!TryParseTime(parts[0], out var time)) continue;

            entries.Add(new MachineLogEntry
            {
                LineNumber = lineNumber,
                Time = time,
                Category = ParseCategory(Field(parts, 1)),
                Tag = Field(parts, 2),
                // The description can itself contain the delimiter, so keep everything that is left.
                Description = parts.Length > 3
                    ? string.Join(delimiter.Value, parts.Skip(3)).Trim()
                    : string.Empty,
                RawLine = line
            });
        }

        return entries;
    }

    public static IReadOnlyList<MachineLogEntry> ParseFile(string path) =>
        File.Exists(path) ? Parse(File.ReadLines(path)) : Array.Empty<MachineLogEntry>();

    /// <summary>The machine model the PLC reports once at power-on, used to cross-check Machine.xml.</summary>
    public static string? FindMachineModel(IEnumerable<MachineLogEntry> entries)
    {
        foreach (var entry in entries)
        {
            var text = $"{entry.Tag} {entry.Description}";
            var index = text.IndexOf("Machine Model", StringComparison.OrdinalIgnoreCase);
            if (index < 0) continue;

            var after = text[(index + "Machine Model".Length)..].Trim().TrimStart('=', ':', ',').Trim();
            if (after.Length > 0) return after;
        }

        return null;
    }

    private static char SniffDelimiter(string line) => line.Count(c => c == '\t') >= 2 ? '\t' : ',';

    private static string Field(string[] parts, int index) =>
        index < parts.Length ? parts[index].Trim() : string.Empty;

    private static bool TryParseTime(string value, out TimeSpan time)
    {
        time = default;

        var match = TimePattern.Match(value);
        if (!match.Success) return false;

        var fraction = match.Groups["frac"].Success
            // .fffffff is 100ns ticks; keep only what TimeSpan can hold.
            ? double.Parse("0." + match.Groups["frac"].Value, CultureInfo.InvariantCulture)
            : 0;

        time = new TimeSpan(
            int.Parse(match.Groups["h"].Value, CultureInfo.InvariantCulture),
            int.Parse(match.Groups["m"].Value, CultureInfo.InvariantCulture),
            int.Parse(match.Groups["s"].Value, CultureInfo.InvariantCulture))
            + TimeSpan.FromSeconds(fraction);

        return true;
    }

    private static MachineLogCategory ParseCategory(string value) => value.ToUpperInvariant() switch
    {
        "INPUTCHANGE" => MachineLogCategory.InputChange,
        "OUTPUTCHANGE" => MachineLogCategory.OutputChange,
        "MOTIONEVENT" => MachineLogCategory.MotionEvent,
        "OTHER" => MachineLogCategory.Other,
        _ => MachineLogCategory.Unknown
    };
}
