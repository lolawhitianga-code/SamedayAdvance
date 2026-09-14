namespace DiagFileMonitor.Core.Models;

/// <summary>Fields parsed out of a machine.xml before they are attached to a DiagnosticFile.</summary>
public class MachineInfo
{
    public string? MachineType { get; set; }
    public string? SerialNumber { get; set; }
    public string? Customer { get; set; }
    public string? Version { get; set; }
}
