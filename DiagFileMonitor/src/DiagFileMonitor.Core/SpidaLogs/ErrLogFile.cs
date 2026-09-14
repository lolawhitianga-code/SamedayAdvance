using System.Globalization;
using System.Text.RegularExpressions;

namespace DiagFileMonitor.Core.SpidaLogs;

public class ErrLogEntry
{
    public DateTime Timestamp { get; init; }
    public string Text { get; init; } = string.Empty;
    public string RawLine { get; init; } = string.Empty;

    /// <summary>The error text with numbers and quoted values removed, for grouping repeats.</summary>
    public string Signature => Regex.Replace(Text, @"\d+", "#").Trim();
}

/// <summary>
/// Reads ErrLog.txt: .NET style error entries, each starting with a timestamp. Lines that
/// continue an entry (stack traces and the like) are folded into the entry above.
/// </summary>
public static class ErrLogFile
{
    private static readonly Regex LeadingTimestamp = new(
        @"^\s*(?<stamp>\d{4}[-/]\d{2}[-/]\d{2}[ T]\d{1,2}:\d{2}:\d{2}(?:\.\d+)?)\s*[,\-:|\]]?\s*(?<text>.*)$",
        RegexOptions.Compiled);

    private static readonly string[] Formats =
    {
        "yyyy-MM-dd HH:mm:ss.fff", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-ddTHH:mm:ss.fff", "yyyy-MM-ddTHH:mm:ss",
        "yyyy/MM/dd HH:mm:ss.fff", "yyyy/MM/dd HH:mm:ss"
    };

    public static IReadOnlyList<ErrLogEntry> Parse(IEnumerable<string> lines)
    {
        var entries = new List<ErrLogEntry>();
        ErrLogEntry? current = null;
        var continuation = new List<string>();

        void Flush()
        {
            if (current is null) return;

            entries.Add(continuation.Count == 0
                ? current
                : new ErrLogEntry
                {
                    Timestamp = current.Timestamp,
                    Text = (current.Text + " " + string.Join(" ", continuation)).Trim(),
                    RawLine = current.RawLine
                });

            continuation.Clear();
        }

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd('\r');
            var match = LeadingTimestamp.Match(line);

            if (match.Success && TryParse(match.Groups["stamp"].Value, out var timestamp))
            {
                Flush();
                current = new ErrLogEntry
                {
                    Timestamp = timestamp,
                    Text = match.Groups["text"].Value.Trim(),
                    RawLine = line
                };
                continue;
            }

            if (current is not null && line.Trim().Length > 0)
            {
                continuation.Add(line.Trim());
            }
        }

        Flush();
        return entries;
    }

    public static IReadOnlyList<ErrLogEntry> ParseFile(string path) =>
        File.Exists(path) ? Parse(File.ReadLines(path)) : Array.Empty<ErrLogEntry>();

    private static bool TryParse(string value, out DateTime timestamp) =>
        DateTime.TryParseExact(value.Replace('T', ' '), Formats, CultureInfo.InvariantCulture,
            DateTimeStyles.None, out timestamp)
        || DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out timestamp);
}
