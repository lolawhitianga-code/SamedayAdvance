using System.Xml.Linq;
using DiagFileMonitor.Core.Models;

namespace DiagFileMonitor.Core.Services;

/// <summary>
/// Tolerant machine.xml reader. The exact schema varies between machine models, so instead of
/// binding to one fixed structure this looks for elements/attributes by name (case-insensitive,
/// trying a few common aliases) anywhere in the document.
/// </summary>
public static class MachineXmlParser
{
    public static MachineInfo Parse(string xmlPath)
    {
        var doc = XDocument.Load(xmlPath);
        var root = doc.Root ?? throw new InvalidOperationException("machine.xml has no root element.");

        return new MachineInfo
        {
            MachineType = FindValue(root, "MachineType", "Type", "Model"),
            SerialNumber = FindValue(root, "SerialNumber", "Serial", "SerialNo", "SN"),
            Customer = FindValue(root, "Customer", "CustomerName", "Client"),
            Version = FindValue(root, "Version", "SoftwareVersion", "FirmwareVersion")
        };
    }

    private static string? FindValue(XElement root, params string[] candidateNames)
    {
        foreach (var name in candidateNames)
        {
            var element = root.DescendantsAndSelf()
                .FirstOrDefault(e => string.Equals(e.Name.LocalName, name, StringComparison.OrdinalIgnoreCase));

            if (element is not null && !string.IsNullOrWhiteSpace(element.Value))
            {
                return element.Value.Trim();
            }

            var attribute = root.DescendantsAndSelf()
                .SelectMany(e => e.Attributes())
                .FirstOrDefault(a => string.Equals(a.Name.LocalName, name, StringComparison.OrdinalIgnoreCase));

            if (attribute is not null && !string.IsNullOrWhiteSpace(attribute.Value))
            {
                return attribute.Value.Trim();
            }
        }

        return null;
    }
}
