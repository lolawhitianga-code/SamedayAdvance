using System.Globalization;
using System.Text.RegularExpressions;

namespace DiagFileMonitor.Core.SpidaLogs;

public class ErrLogEntry
{
    public DateTime Timestamp { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
    public string Method { get; init; } = string.Empty;

    /// <summary>First few stack frames, which say where in the software it happened.</summary>
    public string TopOfStack { get; init; } = string.Empty;

    public string Text => Message.Length > 0 ? Message : Title;

    /// <summary>
    /// Message and method with numbers masked, so the same error raised repeatedly groups
    /// together even when the numbers in it differ.
    /// </summary>
    public string Signature => Regex.Replace($"{Text}|{Method}", @"\d+", "#").Trim();

    public string Display =>
        $"{Timestamp:yyyy-MM-dd HH:mm:ss}  {Text}"
        + (Source.Length > 0 ? $"  (in {Source})" : string.Empty);
}

/// <summary>
/// Reads ErrLog.txt, which SDN writes as blocks rather than one line per error:
/// <code>
/// Date/Time: 10/02/2025 8:25:04 pm
/// ==========================================
///
/// Title: SDN
/// Message: Sequence contains no elements
/// Source: First
/// Method: ...
/// StackTrace:    at ...
/// </code>
/// A block runs until the next <c>Date/Time:</c>. Dates are day-first with a lowercase am/pm.
/// </summary>
public static class ErrLogFile
{
    private static readonly Regex Field = new(@"^\s*(?<name>Date/Time|Title|Message|Source|Method|StackTrace)\s*:\s*(?<value>.*)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly string[] Formats =
    {
        "d/MM/yyyy h:mm:ss tt", "dd/MM/yyyy h:mm:ss tt", "d/M/yyyy h:mm:ss tt", "dd/MM/yyyy hh:mm:ss tt",
        "d/MM/yyyy H:mm:ss", "dd/MM/yyyy HH:mm:ss", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm:ss.fff"
    };

    public static IReadOnlyList<ErrLogEntry> Parse(IEnumerable<string> lines)
    {
        var entries = new List<ErrLogEntry>();

        DateTime? timestamp = null;
        string title = "", message = "", source = "", method = "";
        var stack = new List<string>();
        var inStack = false;

        void Flush()
        {
            if (timestamp is null) return;

            if (title.Trim().Length == 0 && message.Trim().Length == 0)
            {
                title = message = source = method = "";
                stack.Clear();
                inStack = false;
                return;
            }

            entries.Add(new ErrLogEntry
            {
                Timestamp = timestamp.Value,
                Title = title.Trim(),
                Message = message.Trim(),
                Source = source.Trim(),
                Method = method.Trim(),
                TopOfStack = string.Join(" / ", stack.Take(3))
            });

            title = message = source = method = "";
            stack.Clear();
            inStack = false;
        }

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd('\r');
            if (line.StartsWith("====")) continue;

            var match = Field.Match(line);
            if (match.Success)
            {
                var name = match.Groups["name"].Value.ToUpperInvariant();
                var value = match.Groups["value"].Value;
                inStack = false;

                switch (name)
                {
                    case "DATE/TIME":
                        Flush();
                        timestamp = TryParse(value.Trim(), out var parsed) ? parsed : null;
                        break;
                    case "TITLE": title = value; break;
                    case "MESSAGE": message = value; break;
                    case "SOURCE": source = value; break;
                    case "METHOD": method = value; break;
                    case "STACKTRACE":
                        inStack = true;
                        if (value.Trim().Length > 0) stack.Add(value.Trim());
                        break;
                }

                continue;
            }

            // Stack frames continue on their own indented lines.
            if (inStack && line.TrimStart().StartsWith("at ", StringComparison.OrdinalIgnoreCase))
            {
                stack.Add(line.Trim());
            }
        }

        Flush();
        return entries;
    }

    public static IReadOnlyList<ErrLogEntry> ParseFile(string path) =>
        File.Exists(path) ? Parse(File.ReadLines(path)) : Array.Empty<ErrLogEntry>();

    private static bool TryParse(string value, out DateTime timestamp)
    {
        // The designator is lowercase in real files; invariant parsing accepts it either way.
        return DateTime.TryParseExact(value, Formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out timestamp)
               || DateTime.TryParse(value, CultureInfo.GetCultureInfo("en-NZ"), DateTimeStyles.None, out timestamp);
    }
}
