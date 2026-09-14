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
    public string MachineName { get; set; } = Unknown;
    public string SiteLocation { get; set; } = Unknown;
    public string SoftwareName { get; set; } = Unknown;
    public string Customer { get; set; } = Unknown;
    public string Version { get; set; } = Unknown;
    public DateTime ArrivedAtUtc { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
    public string? ExtractedPath { get; set; }
    public string? SupportPanel { get; set; }
    public string? SupportMembers { get; set; }
    public string? SupportIssue { get; set; }

    /// <summary>
    /// The Details column. A processing failure matters more than anything the operator typed,
    /// so it wins; otherwise this is what they reported when raising the bundle.
    /// </summary>
    public string Details
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(ErrorMessage)) return ErrorMessage;

            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(SupportPanel)) parts.Add($"Panel: {SupportPanel}");
            if (!string.IsNullOrWhiteSpace(SupportMembers)) parts.Add($"Members: {SupportMembers}");
            if (!string.IsNullOrWhiteSpace(SupportIssue)) parts.Add($"Issue: {SupportIssue}");

            return string.Join("  |  ", parts);
        }
    }

    public string? Notes { get; set; }
    public string? TicketNumber { get; set; }

    public bool IsBaseline { get; set; }
    public string? ZohoTicketNumber { get; set; }
    public string BaselineLabel => IsBaseline ? "Baseline" : string.Empty;

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
        MachineName = Display(file.MachineName),
        SiteLocation = Display(file.SiteLocation),
        SoftwareName = Display(file.SoftwareName),
        Customer = Display(file.Customer),
        Version = Display(file.Version),
        ArrivedAtUtc = file.ArrivedAtUtc,
        Status = file.Status.ToString(),
        ErrorMessage = file.ErrorMessage,
        SupportPanel = file.SupportPanel,
        SupportMembers = file.SupportMembers,
        SupportIssue = file.SupportIssue,
        ExtractedPath = file.ExtractedPath,
        Notes = file.Notes,
        TicketNumber = file.TicketNumber,
        IsBaseline = file.IsBaseline,
        ZohoTicketNumber = file.ZohoTicketNumber,
        ChangeLogPath = PathOf(file, LogFileKind.ChangeLog),
        MachineLogPath = PathOf(file, LogFileKind.MachineLog),
        ErrorLogPath = PathOf(file, LogFileKind.ErrorLog)
    };

    private static string? PathOf(DiagnosticFile file, LogFileKind kind) =>
        file.LogFiles.FirstOrDefault(log => log.Kind == kind)?.FullPath;

    private static string Display(string? value) => string.IsNullOrWhiteSpace(value) ? Unknown : value;
}
