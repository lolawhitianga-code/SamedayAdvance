using System.Globalization;

namespace DiagFileMonitor.Core.SpidaLogs;

public class ChangeLogEntry
{
    public DateTime Timestamp { get; init; }
    public string User { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string Setting { get; init; } = string.Empty;
    public string OldValue { get; init; } = string.Empty;
    public string NewValue { get; init; } = string.Empty;
    public string RawLine { get; init; } = string.Empty;

    public string Display =>
        $"{Timestamp:yyyy-MM-dd HH:mm:ss}  {Setting}: {OldValue} -> {NewValue}"
        + (User.Length > 0 ? $"  (by {User})" : string.Empty);
}

/// <summary>
/// Reads Change.log: <c>date time, User, Category, Setting, OldValue, NewValue</c>.
/// These files go back years, so callers should work from the most recent entries.
/// </summary>
public static class ChangeLogFile
{
    private static readonly string[] Formats =
    {
        "yyyy-MM-dd HH:mm:ss", "yyyy/MM/dd HH:mm:ss", "dd/MM/yyyy HH:mm:ss", "MM/dd/yyyy HH:mm:ss",
        "yyyy-MM-dd HH:mm:ss.fff", "d/MM/yyyy h:mm:ss tt", "M/d/yyyy h:mm:ss tt"
    };

    public static IReadOnlyList<ChangeLogEntry> Parse(IEnumerable<string> lines)
    {
        var entries = new List<ChangeLogEntry>();

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd('\r');
            if (line.Trim().Length == 0) continue;

            var parts = line.Split(',');
            if (parts.Length < 2) continue;
            if (!TryParse(parts[0].Trim(), out var timestamp)) continue;

            entries.Add(new ChangeLogEntry
            {
                Timestamp = timestamp,
                User = Field(parts, 1),
                Category = Field(parts, 2),
                Setting = Field(parts, 3),
                OldValue = Field(parts, 4),
                // A value can contain commas, so the last field takes the remainder.
                NewValue = parts.Length > 5 ? string.Join(",", parts.Skip(5)).Trim() : string.Empty,
                RawLine = line
            });
        }

        return entries;
    }

    public static IReadOnlyList<ChangeLogEntry> ParseFile(string path) =>
        File.Exists(path) ? Parse(File.ReadLines(path)) : Array.Empty<ChangeLogEntry>();

    private static string Field(string[] parts, int index) =>
        index < parts.Length ? parts[index].Trim() : string.Empty;

    private static bool TryParse(string value, out DateTime timestamp) =>
        DateTime.TryParseExact(value, Formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out timestamp)
        || DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out timestamp);
}
