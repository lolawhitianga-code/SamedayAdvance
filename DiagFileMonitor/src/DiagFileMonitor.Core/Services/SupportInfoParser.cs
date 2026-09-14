using System.Text.RegularExpressions;

namespace DiagFileMonitor.Core.Services;

public class SupportInfo
{
    public string? Panel { get; set; }
    public string? Members { get; set; }
    public string? Issue { get; set; }

    public bool HasAnything =>
        !string.IsNullOrWhiteSpace(Panel)
        || !string.IsNullOrWhiteSpace(Members)
        || !string.IsNullOrWhiteSpace(Issue);

    /// <summary>One line for the grid's Details column.</summary>
    public string Summary
    {
        get
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(Panel)) parts.Add($"Panel: {Panel}");
            if (!string.IsNullOrWhiteSpace(Members)) parts.Add($"Members: {Members}");
            if (!string.IsNullOrWhiteSpace(Issue)) parts.Add($"Issue: {Issue}");

            return string.Join(" | ", parts);
        }
    }
}

/// <summary>
/// Reads what the operator typed into supportinfo.txt when raising the bundle: which panel,
/// which members, and what the problem is.
/// <para>
/// Written to be forgiving because the exact layout has not been confirmed against a real
/// file: labels are matched case-insensitively, a colon, equals or tab may separate label
/// from value, and a value may run over several lines until the next label or a blank line
/// (the issue description is usually free text).
/// </para>
/// </summary>
public static class SupportInfoParser
{
    private static readonly Regex LabelLine = new(
        @"^\s*(?<label>Panel|Panels|Member|Members|Issue|Issues|Problem|Fault|Description)\s*[:=\t]\s*(?<value>.*)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Any other "Key: value" line, e.g. the Latitude/Longitude/FixVer that follow the operator's
    /// text. These end whatever field was being collected so they cannot run into the issue text.
    /// </summary>
    private static readonly Regex ForeignLabelLine = new(
        @"^\s*[A-Za-z][A-Za-z0-9_]{0,20}\s*[:=]",
        RegexOptions.Compiled);

    public static SupportInfo Parse(IEnumerable<string> lines)
    {
        var values = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        string? current = null;

        foreach (var line in lines)
        {
            var match = LabelLine.Match(line);

            if (match.Success)
            {
                current = Canonical(match.Groups["label"].Value);
                if (!values.ContainsKey(current)) values[current] = new List<string>();

                var value = match.Groups["value"].Value.Trim();
                if (value.Length > 0) values[current].Add(value);
                continue;
            }

            // A blank line, or a field belonging to something else, ends a run-on value.
            if (string.IsNullOrWhiteSpace(line) || ForeignLabelLine.IsMatch(line))
            {
                current = null;
                continue;
            }

            if (current is not null)
            {
                values[current].Add(line.Trim());
            }
        }

        return new SupportInfo
        {
            Panel = Join(values, "Panel"),
            Members = Join(values, "Members"),
            Issue = Join(values, "Issue")
        };
    }

    public static SupportInfo ParseFile(string path)
    {
        try
        {
            return File.Exists(path) ? Parse(File.ReadAllLines(path)) : new SupportInfo();
        }
        catch (IOException ex)
        {
            SimpleLogger.Error($"Could not read '{path}'", ex);
            return new SupportInfo();
        }
    }

    private static string Canonical(string label) => label.ToUpperInvariant() switch
    {
        "PANEL" or "PANELS" => "Panel",
        "MEMBER" or "MEMBERS" => "Members",
        _ => "Issue"
    };

    private static string? Join(Dictionary<string, List<string>> values, string key) =>
        values.TryGetValue(key, out var lines) && lines.Count > 0
            ? string.Join(" ", lines)
            : null;
}
