namespace DiagFileMonitor.Core.Models;

/// <summary>
/// Identifies the well-known log files inside a diagnostic bundle so the future
/// "analyse the diag file" feature can find changelog.txt / machinelog.txt / errorlog.txt
/// without re-scanning the extracted folder.
/// </summary>
public enum LogFileKind
{
    ChangeLog,
    MachineLog,
    ErrorLog,
    Other
}

/// <summary>One file extracted from a diagnostic zip, linked back to its parent DiagnosticFile.</summary>
public class ExtractedLogFile
{
    public int Id { get; set; }

    public int DiagnosticFileId { get; set; }
    public DiagnosticFile? DiagnosticFile { get; set; }

    public string FileName { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public LogFileKind Kind { get; set; } = LogFileKind.Other;
}
