using System.Xml.Linq;
using DiagFileMonitor.Core.Models;

namespace DiagFileMonitor.Core.Services;

/// <summary>
/// Reads the identifying fields out of machine.xml.
/// <para>
/// Spida machines put these near the end of the file as direct children of the root:
/// SerialNumber, MachineModel, MachineName, SiteName, SiteLocation, plus a Title line that
/// packs everything together as
/// <c>Spida SDN, V2.5.0.0, M21642-1, Grandeur Housing Limited, Apollo</c>.
/// </para>
/// <para>
/// Dedicated elements win because they are unambiguous; Title fills any gaps, and is the only
/// place the software version appears. Element names are matched case-insensitively against
/// direct children first, then anywhere in the document, so a nested element with a common name
/// (the file is full of RoleAction Name elements) cannot be mistaken for a machine field.
/// </para>
/// </summary>
public static class MachineXmlParser
{
    public static MachineInfo Parse(string xmlPath)
    {
        var doc = XDocument.Load(xmlPath);
        var root = doc.Root ?? throw new InvalidOperationException("machine.xml has no root element.");

        var title = TitleLine.Parse(FindValue(root, "Title"));

        return new MachineInfo
        {
            SerialNumber = FindValue(root, "SerialNumber", "Serial", "SerialNo", "SN") ?? title.SerialNumber,
            Customer = FindValue(root, "SiteName", "Customer", "CustomerName", "Client") ?? title.Customer,
            MachineType = FindValue(root, "MachineModel", "MachineType", "Model") ?? title.MachineType,
            Version = FindValue(root, "SoftwareVersion", "FirmwareVersion", "Version") ?? title.Version,
            MachineName = FindValue(root, "MachineName"),
            SiteLocation = FindValue(root, "SiteLocation", "Location", "Site"),
            SoftwareName = FindValue(root, "SoftwareName", "Product") ?? title.SoftwareName
        };
    }

    private static string? FindValue(XElement root, params string[] candidateNames)
    {
        foreach (var name in candidateNames)
        {
            var value = Value(root.Elements().FirstOrDefault(e => Matches(e.Name.LocalName, name)))
                        ?? Value(root.Descendants().FirstOrDefault(e => Matches(e.Name.LocalName, name)));

            if (value is not null) return value;

            var attribute = root.DescendantsAndSelf()
                .SelectMany(e => e.Attributes())
                .FirstOrDefault(a => Matches(a.Name.LocalName, name));

            if (attribute is not null && !string.IsNullOrWhiteSpace(attribute.Value))
            {
                return attribute.Value.Trim();
            }
        }

        return null;
    }

    private static bool Matches(string actual, string expected) =>
        string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);

    private static string? Value(XElement? element) =>
        element is null || string.IsNullOrWhiteSpace(element.Value) ? null : element.Value.Trim();
}

/// <summary>
/// The comma-separated Title line: software, version, serial, customer, model.
/// Read from both ends so a customer name containing a comma does not shift the model out of
/// place - the model is always last, and everything between the serial and the model is customer.
/// </summary>
public class TitleLine
{
    public string? SoftwareName { get; private init; }
    public string? Version { get; private init; }
    public string? SerialNumber { get; private init; }
    public string? Customer { get; private init; }
    public string? MachineType { get; private init; }

    public static TitleLine Parse(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return new TitleLine();

        var parts = title
            .Split(',', StringSplitOptions.TrimEntries)
            .Where(p => p.Length > 0)
            .ToList();

        return parts.Count switch
        {
            >= 5 => new TitleLine
            {
                SoftwareName = parts[0],
                Version = parts[1],
                SerialNumber = parts[2],
                Customer = string.Join(", ", parts.Skip(3).Take(parts.Count - 4)),
                MachineType = parts[^1]
            },
            4 => new TitleLine
            {
                SoftwareName = parts[0],
                Version = parts[1],
                SerialNumber = parts[2],
                Customer = parts[3]
            },
            3 => new TitleLine { SoftwareName = parts[0], Version = parts[1], SerialNumber = parts[2] },
            2 => new TitleLine { SoftwareName = parts[0], Version = parts[1] },
            _ => new TitleLine { SoftwareName = parts[0] }
        };
    }
}
