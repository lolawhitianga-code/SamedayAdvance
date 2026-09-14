namespace DiagFileMonitor.Core.Services;

/// <summary>Persisted user settings: where to watch, which extensions count as diagnostic bundles, and where things get stored.</summary>
public class AppSettings
{
    public string WatchFolderPath { get; set; } = string.Empty;
    public string ExtractRootPath { get; set; } = string.Empty;
    public List<string> FileExtensions { get; set; } = new() { ".zip" };
    public string DatabasePath { get; set; } = string.Empty;

    /// <summary>How close together two bundles from one machine have to be to count as a repeat.</summary>
    public int RepeatWindowDays { get; set; } = RepeatSubmissionMarker.DefaultWindowDays;
}
