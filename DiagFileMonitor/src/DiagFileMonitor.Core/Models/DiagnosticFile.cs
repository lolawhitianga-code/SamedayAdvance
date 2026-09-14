namespace DiagFileMonitor.Core.Models;

public enum ProcessingStatus
{
    Pending,
    Processed,
    Error
}

/// <summary>One incoming diagnostic zip, its unpack result, and the machine.xml fields extracted from it.</summary>
public class DiagnosticFile
{
    public int Id { get; set; }

    public string OriginalFileName { get; set; } = string.Empty;
    public string SourcePath { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }

    public DateTime ArrivedAtUtc { get; set; }
    public DateTime? ProcessedAtUtc { get; set; }

    public string? ExtractedPath { get; set; }

    public string? MachineType { get; set; }
    public string? SerialNumber { get; set; }
    public string? Customer { get; set; }
    public string? Version { get; set; }

    public ProcessingStatus Status { get; set; } = ProcessingStatus.Pending;
    public string? ErrorMessage { get; set; }

    /// <summary>Support's own case notes against this bundle.</summary>
    public string? Notes { get; set; }

    /// <summary>Reference into whatever ticketing system is in use.</summary>
    public string? TicketNumber { get; set; }

    public List<ExtractedLogFile> LogFiles { get; set; } = new();
}
