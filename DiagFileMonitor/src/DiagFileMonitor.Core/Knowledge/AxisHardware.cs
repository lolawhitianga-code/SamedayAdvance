using System.Xml.Linq;

namespace DiagFileMonitor.Core.Knowledge;

public enum ElectronicsFamily
{
    /// <summary>CyberLogix CLX, talking CLX MCNet. These are the drives that flash F-codes.</summary>
    Clx,

    /// <summary>Omron, talking CIPNet.</summary>
    Omron,

    /// <summary>Configured as simulated rather than real hardware.</summary>
    Simulated,

    Unknown
}

public class AxisHardwareEntry
{
    /// <summary>The element the axis sits under, e.g. FixedSide/Trolley.</summary>
    public string Path { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;
    public ElectronicsFamily Family { get; init; }

    /// <summary>The raw value from the file, kept so an unrecognised one can still be reported.</summary>
    public string RawType { get; init; } = string.Empty;

    public bool InUse { get; init; }

    /// <summary>
    /// The path without the root element, so the four ServoGun entries on a wall extruder can be
    /// told apart - FixedSide/ServoGuns/ServoGun against FloatingSide/DualServoGuns/... - rather
    /// than listing the same bare name four times.
    /// </summary>
    public string DisplayName
    {
        get
        {
            var cut = Path.IndexOf('/');
            return cut >= 0 && cut + 1 < Path.Length ? Path[(cut + 1)..] : Path;
        }
    }
}

public class AxisHardwareMap
{
    public IReadOnlyList<AxisHardwareEntry> Axes { get; init; } = Array.Empty<AxisHardwareEntry>();

    public IEnumerable<AxisHardwareEntry> InUse => Axes.Where(a => a.InUse);

    public bool HasClx => InUse.Any(a => a.Family == ElectronicsFamily.Clx);
    public bool HasOmron => InUse.Any(a => a.Family == ElectronicsFamily.Omron);

    /// <summary>True where one machine runs both families, so which drive faulted decides the code set.</summary>
    public bool IsMixed => HasClx && HasOmron;

    public bool Any => Axes.Count > 0;

    public string Summary
    {
        get
        {
            var counts = InUse
                .GroupBy(a => a.Family)
                .OrderByDescending(g => g.Count())
                .Select(g => $"{g.Count()} {Describe(g.Key)}");

            return string.Join(", ", counts);
        }
    }

    public static string Describe(ElectronicsFamily family) => family switch
    {
        ElectronicsFamily.Clx => "CLX (CLX MCNet)",
        ElectronicsFamily.Omron => "Omron (CIPNet)",
        ElectronicsFamily.Simulated => "simulated",
        _ => "unrecognised"
    };
}

/// <summary>
/// Reads which electronics each axis runs on, from the machine's own configuration file -
/// WallExtruder.xml, RakingWallExtruderV3DG.xml, TornadoM450.xml, SprintM600.xml and so on,
/// named after the machine model rather than the generic Machine.xml.
/// <para>
/// Each axis carries an AxisHardwareType, and each IO point a HardwareType: CIPNet for Omron,
/// CLXMCNet (or CLXMCNetAxis) for CLX. It matters because the MC2 F-codes are CLX codes, and a
/// machine can run both families at once - the raked wall extruder drives its trolleys and
/// ejectors on Omron while the gun and height servos are CLX.
/// </para>
/// <para>
/// These files are UTF-16, which XDocument handles from the byte order mark.
/// </para>
/// </summary>
public static class AxisHardware
{
    public static AxisHardwareMap Read(string configXmlPath)
    {
        if (string.IsNullOrWhiteSpace(configXmlPath) || !File.Exists(configXmlPath))
        {
            return new AxisHardwareMap();
        }

        try
        {
            var root = XDocument.Load(configXmlPath).Root;
            if (root is null) return new AxisHardwareMap();

            var axes = new List<AxisHardwareEntry>();
            Walk(root, root.Name.LocalName, axes);

            return new AxisHardwareMap { Axes = axes };
        }
        catch (Exception)
        {
            // A machine config we cannot read is reported as nothing known, not as a crash.
            return new AxisHardwareMap();
        }
    }

    private static void Walk(XElement element, string path, List<AxisHardwareEntry> axes)
    {
        foreach (var child in element.Elements())
        {
            var childPath = $"{path}/{child.Name.LocalName}";

            if (child.Element("AxisHardwareType") is { } type)
            {
                axes.Add(new AxisHardwareEntry
                {
                    Path = childPath,
                    Name = child.Name.LocalName,
                    RawType = type.Value.Trim(),
                    Family = Classify(type.Value),
                    InUse = string.Equals(child.Element("InUse")?.Value.Trim(), "true",
                        StringComparison.OrdinalIgnoreCase)
                });
            }

            Walk(child, childPath, axes);
        }
    }

    public static ElectronicsFamily Classify(string? hardwareType)
    {
        if (string.IsNullOrWhiteSpace(hardwareType)) return ElectronicsFamily.Unknown;

        var value = hardwareType.Trim();

        if (value.StartsWith("CLXMCNet", StringComparison.OrdinalIgnoreCase)) return ElectronicsFamily.Clx;
        if (value.Equals("CIPNet", StringComparison.OrdinalIgnoreCase)) return ElectronicsFamily.Omron;
        if (value.StartsWith("Simulat", StringComparison.OrdinalIgnoreCase)) return ElectronicsFamily.Simulated;

        return ElectronicsFamily.Unknown;
    }
}
