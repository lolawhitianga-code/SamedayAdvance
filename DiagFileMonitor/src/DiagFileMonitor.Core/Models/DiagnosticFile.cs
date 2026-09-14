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
    public string? MachineName { get; set; }
    public string? SerialNumber { get; set; }
    public string? Customer { get; set; }
    public string? SiteLocation { get; set; }
    public string? SoftwareName { get; set; }
    public string? Version { get; set; }

    public ProcessingStatus Status { get; set; } = ProcessingStatus.Pending;
    public string? ErrorMessage { get; set; }

    /// <summary>Support's own case notes against this bundle.</summary>
    public string? Notes { get; set; }

    /// <summary>Reference into whatever ticketing system is in use.</summary>
    public string? TicketNumber { get; set; }

    /// <summary>Marked as a known-good bundle to compare faulty machines of this type against.</summary>
    public bool IsBaseline { get; set; }

    /// <summary>Zoho Desk ticket this bundle's analysis was posted to, if any.</summary>
    public string? ZohoTicketId { get; set; }
    public string? ZohoTicketNumber { get; set; }
    public DateTime? ZohoTicketCreatedUtc { get; set; }

    /// <summary>When a burst alert went out for this bundle, so it is not raised twice.</summary>
    public DateTime? AlertSentUtc { get; set; }

    public List<ExtractedLogFile> LogFiles { get; set; } = new();
}
