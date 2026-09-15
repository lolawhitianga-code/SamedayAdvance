namespace DiagFileMonitor.Core.Reports;

/// <summary>What the inventory can add on top of what the machine reported about itself.</summary>
public class InventoryEntry
{
    public string SerialNumber { get; init; } = string.Empty;

    /// <summary>The full site name, e.g. "Carters Cambridge". Machine.xml carries only "Carters".</summary>
    public string Site { get; init; } = string.Empty;

    /// <summary>The name people at the site use, e.g. "4.8M Raked Extruder (Raking Line 1)".</summary>
    public string AssetName { get; init; } = string.Empty;

    public string Contact { get; init; } = string.Empty;

    /// <summary>The machine type the inventory believes this is, used only to cross-check the log.</summary>
    public string ExpectedType { get; init; } = string.Empty;

    /// <summary>Set where the supplied documents disagree with each other about this machine.</summary>
    public string Disputed { get; init; } = string.Empty;
}

/// <summary>
/// Site and contact detail keyed on serial number.
/// <para>
/// This is an <b>enrichment layer, not the identity source.</b> Machine identity comes from
/// <c>Machine.xml</c>'s Title, which is what the machine itself reported and is read from both
/// ends so a comma in the customer name cannot shift the model field. The inventory only adds
/// what the log has no way of knowing - the town, the line number, who to ring.
/// </para>
/// <para>
/// Where the inventory and the log disagree on model or customer, the log wins and the report
/// prints the disagreement. A silently resolved conflict is how a wrong serial ends up quoted to
/// a customer. Entries the supplied documents contradict each other on carry
/// <see cref="InventoryEntry.Disputed"/> and are never used to override anything.
/// </para>
/// </summary>
public class MachineInventory
{
    private readonly Dictionary<string, InventoryEntry> _exact;
    private readonly Dictionary<string, InventoryEntry> _byDigits;

    public MachineInventory(IEnumerable<InventoryEntry> entries)
    {
        var list = entries.ToList();

        _exact = list
            .GroupBy(e => Exact(e.SerialNumber), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        _byDigits = list
            .GroupBy(e => Digits(e.SerialNumber), StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Key.Length > 0)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
    }

    public InventoryEntry? Find(string? serialNumber)
    {
        if (string.IsNullOrWhiteSpace(serialNumber)) return null;

        if (_exact.TryGetValue(Exact(serialNumber), out var exact)) return exact;
        return _byDigits.TryGetValue(Digits(serialNumber), out var entry) ? entry : null;
    }

    /// <summary>
    /// The site to print. The inventory's fuller name is preferred, falling back to whatever the
    /// bundle carried.
    /// </summary>
    public string SiteFor(string? serialNumber, string? customerFromLog)
    {
        var entry = Find(serialNumber);
        if (entry is not null && !string.IsNullOrWhiteSpace(entry.Site)) return entry.Site;
        return (customerFromLog ?? string.Empty).Trim();
    }

    /// <summary>
    /// Any disagreement worth printing: the inventory expecting a different machine type from the
    /// one the log reported, or a serial the supplied documents argue over.
    /// </summary>
    public string? Disagreement(string? serialNumber, string? typeFromLog)
    {
        var entry = Find(serialNumber);
        if (entry is null) return null;

        if (!string.IsNullOrWhiteSpace(entry.Disputed))
            return $"{entry.SerialNumber}: {entry.Disputed} The log is what this report uses.";

        if (!string.IsNullOrWhiteSpace(entry.ExpectedType)
            && !string.IsNullOrWhiteSpace(typeFromLog)
            && !entry.ExpectedType.Equals(typeFromLog, StringComparison.OrdinalIgnoreCase))
        {
            return $"{entry.SerialNumber}: the inventory lists a {entry.ExpectedType}, the machine "
                   + $"reported a {typeFromLog}. The machine is believed over the inventory.";
        }

        return null;
    }

    /// <summary>The serial with separators and case removed - the strictest match.</summary>
    private static string Exact(string serial) =>
        new string(serial.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();

    /// <summary>
    /// The first run of digits. Serials are written with and without a prefix and with a line
    /// suffix - M21642-1 is the same machine as M21642 - so the leading digits are what identifies
    /// it. Only the first run is taken, or the -1 would make it a different machine.
    /// </summary>
    private static string Digits(string serial)
    {
        var start = serial.IndexOfAny("0123456789".ToCharArray());
        if (start < 0) return string.Empty;

        var end = start;
        while (end < serial.Length && char.IsDigit(serial[end])) end++;

        return serial[start..end];
    }

    /// <summary>
    /// The sites and machines from the supplied reporting documents. Rows the two documents
    /// contradict each other on are marked disputed rather than picked between - see
    /// docs/report-generation-plan.md section 2.2.
    /// </summary>
    public static MachineInventory FromSupportRecords() => new(new[]
    {
        new InventoryEntry { SerialNumber = "M21737", Site = "PlaceMakers Auckland Frame & Truss", AssetName = "4.8M Raked Extruder (Raking Line 1)", Contact = "Sierk Bosman", ExpectedType = "RakingWallExtruder" },
        new InventoryEntry { SerialNumber = "M21844", Site = "PlaceMakers Auckland Frame & Truss", AssetName = "3.6M Raked Extruder (Raking Line 2)", Contact = "Sierk Bosman", ExpectedType = "RakingWallExtruder" },
        new InventoryEntry { SerialNumber = "M21778", Site = "PlaceMakers Auckland Frame & Truss", AssetName = "Component Nailer, dual-gun", Contact = "Sierk Bosman" },
        new InventoryEntry { SerialNumber = "M21817", Site = "PlaceMakers Auckland Frame & Truss", AssetName = "Component Nailer V2 3.6M", Contact = "Sierk Bosman" },
        new InventoryEntry { SerialNumber = "DGM20771", Site = "TrussTech 2019 Ltd", AssetName = "Raked Wall Extruder V3", Contact = "Hayden Todd", ExpectedType = "RakingWallExtruderV3DG" },
        new InventoryEntry { SerialNumber = "M18644", Site = "Akarana Hamilton", AssetName = "Component Nailer, single-gun", Disputed = "the inventory marks this machine's data provisional." },
        new InventoryEntry { SerialNumber = "M20921", Site = "ITM Nelson", AssetName = "Component Nailer, dual-gun" },
        new InventoryEntry { SerialNumber = "M20716", Site = "Carters Cambridge", AssetName = "6.0M Raked Extruder", ExpectedType = "RakingWallExtruderV3DG" },
        new InventoryEntry { SerialNumber = "M17311", Site = "Carters Wellington Upper Hutt", AssetName = "Tornado M500 automated saw", ExpectedType = "TornadoM500" },

        // From real exports this app has read, which the supplied inventory did not list.
        new InventoryEntry { SerialNumber = "M20421", Site = "Engineered Truss Systems", AssetName = "Spida Saw", ExpectedType = "TornadoM500" },
        new InventoryEntry { SerialNumber = "M21642", Site = "Grandeur Housing Limited", AssetName = "Apollo", ExpectedType = "Apollo" },

        // Unresolved. Both are recorded so a report never silently picks one.
        new InventoryEntry { SerialNumber = "M18121", Site = "Carters Auckland", Disputed = "one document calls this a saw, the other a single-gun Component Nailer." },
        new InventoryEntry { SerialNumber = "M21036", Site = "Waihi Mitre 10", Disputed = "the reporting documents call this a Raked Wall Extruder, the earlier machine notes call it a Spida Saw." },
        new InventoryEntry { SerialNumber = "AOR1694", Site = "Carters Auckland Line 3", AssetName = "Raked Wall Extruder", Disputed = "AOR1694 and AOR1613 have both been used for this machine; the log header decides it." }
    });
}
