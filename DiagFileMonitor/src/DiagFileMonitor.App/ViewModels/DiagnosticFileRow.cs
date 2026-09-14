namespace DiagFileMonitor.App.ViewModels;

/// <summary>Flattened, UI-friendly view of a DiagnosticFile row for the dashboard grid.</summary>
public class DiagnosticFileRow
{
    public int Id { get; set; }
    public string OriginalFileName { get; set; } = string.Empty;
    public string SerialNumber { get; set; } = "(unknown)";
    public string MachineType { get; set; } = "(unknown)";
    public string Customer { get; set; } = "(unknown)";
    public string Version { get; set; } = "(unknown)";
    public DateTime ArrivedAtUtc { get; set; }
    public string ArrivedDate => ArrivedAtUtc.ToLocalTime().ToString("yyyy-MM-dd");
    public string Status { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
    public string? ExtractedPath { get; set; }
}
