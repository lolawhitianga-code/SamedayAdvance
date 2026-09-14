namespace DiagFileMonitor.Core.Models;

/// <summary>
/// Flattened read model for the dashboard list. Kept separate from the DiagnosticFile
/// entity so the UI binds to a detached object rather than a tracked EF one.
/// </summary>
public class DiagnosticFileSummary
{
    public const string Unknown = "(unknown)";

    public int Id { get; set; }
    public string OriginalFileName { get; set; } = string.Empty;
    public string SerialNumber { get; set; } = Unknown;
    public string MachineType { get; set; } = Unknown;
    public string Customer { get; set; } = Unknown;
    public string Version { get; set; } = Unknown;
    public DateTime ArrivedAtUtc { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
    public string? ExtractedPath { get; set; }

    public DateTime ArrivedAtLocal => ArrivedAtUtc.ToLocalTime();
    public string ArrivedDate => ArrivedAtLocal.ToString("yyyy-MM-dd");
    public string ArrivedDisplay => ArrivedAtLocal.ToString("yyyy-MM-dd HH:mm");

    public static DiagnosticFileSummary FromEntity(DiagnosticFile file) => new()
    {
        Id = file.Id,
        OriginalFileName = file.OriginalFileName,
        SerialNumber = Display(file.SerialNumber),
        MachineType = Display(file.MachineType),
        Customer = Display(file.Customer),
        Version = Display(file.Version),
        ArrivedAtUtc = file.ArrivedAtUtc,
        Status = file.Status.ToString(),
        ErrorMessage = file.ErrorMessage,
        ExtractedPath = file.ExtractedPath
    };

    private static string Display(string? value) => string.IsNullOrWhiteSpace(value) ? Unknown : value;
}
