namespace DiagFileMonitor.Core.Models;

/// <summary>Fields parsed out of a machine.xml before they are attached to a DiagnosticFile.</summary>
public class MachineInfo
{
    /// <summary>Machine model, e.g. "Apollo".</summary>
    public string? MachineType { get; set; }

    /// <summary>Machine name, e.g. "Spida Saw".</summary>
    public string? MachineName { get; set; }

    public string? SerialNumber { get; set; }

    /// <summary>Site the machine belongs to, used as the customer.</summary>
    public string? Customer { get; set; }

    /// <summary>Where the site is, e.g. "Winkler".</summary>
    public string? SiteLocation { get; set; }

    /// <summary>Software product running the machine, e.g. "Spida SDN".</summary>
    public string? SoftwareName { get; set; }

    public string? Version { get; set; }
}
