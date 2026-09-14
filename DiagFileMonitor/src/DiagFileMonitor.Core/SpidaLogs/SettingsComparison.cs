using System.Xml.Linq;

namespace DiagFileMonitor.Core.SpidaLogs;

public record SettingDifference(string Setting, string MasterValue, string ComparedValue);

public class SettingsComparison
{
    public IReadOnlyList<SettingDifference> Differences { get; init; } = Array.Empty<SettingDifference>();
    public IReadOnlyList<string> OnlyInMaster { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> OnlyInCompared { get; init; } = Array.Empty<string>();
    public int SettingsCompared { get; init; }

    /// <summary>
    /// Flattens both Machine.xml files to path/value pairs and reports every value that differs.
    /// Repeated elements are keyed by their Role or Name child where there is one, so a list
    /// reordering does not read as hundreds of changes.
    /// </summary>
    public static SettingsComparison Compare(string masterXmlPath, string comparedXmlPath)
    {
        var master = Flatten(masterXmlPath);
        var compared = Flatten(comparedXmlPath);

        var shared = master.Keys.Intersect(compared.Keys).ToList();

        return new SettingsComparison
        {
            Differences = shared
                .Where(key => !string.Equals(master[key], compared[key], StringComparison.Ordinal))
                .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
                .Select(key => new SettingDifference(key, master[key], compared[key]))
                .ToList(),
            OnlyInMaster = master.Keys.Except(compared.Keys).OrderBy(k => k).ToList(),
            OnlyInCompared = compared.Keys.Except(master.Keys).OrderBy(k => k).ToList(),
            SettingsCompared = shared.Count
        };
    }

    public static Dictionary<string, string> Flatten(string xmlPath)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(xmlPath)) return values;

        try
        {
            var root = XDocument.Load(xmlPath).Root;
            if (root is not null) Walk(root, root.Name.LocalName, values);
        }
        catch (Exception)
        {
            // A malformed file compares as empty rather than bringing the comparison down.
        }

        return values;
    }

    private static void Walk(XElement element, string path, Dictionary<string, string> values)
    {
        var children = element.Elements().ToList();

        if (children.Count == 0)
        {
            values[path] = element.Value.Trim();
            return;
        }

        var duplicateNames = children
            .GroupBy(c => c.Name.LocalName)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var child in children)
        {
            var name = child.Name.LocalName;
            var key = name;

            if (duplicateNames.Contains(name))
            {
                // Prefer a stable identity over position so reordering is not reported as change.
                var identity = child.Elements()
                    .FirstOrDefault(e => e.Name.LocalName is "Role" or "Name" or "Id")?.Value.Trim();

                if (!string.IsNullOrEmpty(identity))
                {
                    key = $"{name}[{identity}]";
                }
                else
                {
                    seen.TryGetValue(name, out var index);
                    seen[name] = index + 1;
                    key = $"{name}[{index}]";
                }
            }

            Walk(child, $"{path}/{key}", values);
        }
    }
}
