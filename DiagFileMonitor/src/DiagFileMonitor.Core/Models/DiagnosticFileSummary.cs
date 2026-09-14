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
    public string? Notes { get; set; }
    public string? TicketNumber { get; set; }

    /// <summary>Set by RepeatSubmissionMarker, not stored: this machine sent another bundle just before.</summary>
    public bool IsRepeatSubmission { get; set; }
    public string RepeatLabel => IsRepeatSubmission ? "Repeat" : string.Empty;

    public string? ChangeLogPath { get; set; }
    public string? MachineLogPath { get; set; }
    public string? ErrorLogPath { get; set; }

    public DateTime ArrivedAtLocal => ArrivedAtUtc.ToLocalTime();
    public string ArrivedDate => ArrivedAtLocal.ToString("yyyy-MM-dd");
    public string ArrivedDisplay => ArrivedAtLocal.ToString("yyyy-MM-dd HH:mm");

    public bool HasExtractedFolder => !string.IsNullOrWhiteSpace(ExtractedPath);
    public bool HasChangeLog => !string.IsNullOrWhiteSpace(ChangeLogPath);
    public bool HasMachineLog => !string.IsNullOrWhiteSpace(MachineLogPath);
    public bool HasErrorLog => !string.IsNullOrWhiteSpace(ErrorLogPath);

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
        ExtractedPath = file.ExtractedPath,
        Notes = file.Notes,
        TicketNumber = file.TicketNumber,
        ChangeLogPath = PathOf(file, LogFileKind.ChangeLog),
        MachineLogPath = PathOf(file, LogFileKind.MachineLog),
        ErrorLogPath = PathOf(file, LogFileKind.ErrorLog)
    };

    private static string? PathOf(DiagnosticFile file, LogFileKind kind) =>
        file.LogFiles.FirstOrDefault(log => log.Kind == kind)?.FullPath;

    private static string Display(string? value) => string.IsNullOrWhiteSpace(value) ? Unknown : value;
}
