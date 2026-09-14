namespace DiagFileMonitor.Core.Services;

/// <summary>Persisted user settings: where to watch, which extensions count as diagnostic bundles, and where things get stored.</summary>
public class AppSettings
{
    public List<string> WatchFolders { get; set; } = new();

    /// <summary>Single-folder setting from earlier builds. Migrated into WatchFolders on load.</summary>
    public string? WatchFolderPath { get; set; }
    public string ExtractRootPath { get; set; } = string.Empty;
    public List<string> FileExtensions { get; set; } = new() { ".zip" };
    public string DatabasePath { get; set; } = string.Empty;

    /// <summary>How close together two bundles from one machine have to be to count as a repeat.</summary>
    public int RepeatWindowDays { get; set; } = RepeatSubmissionMarker.DefaultWindowDays;

    /// <summary>Show a notification-area balloon as each bundle lands.</summary>
    public bool NotifyOnArrival { get; set; } = true;

    /// <summary>Delete unpacked files older than this many days. 0 keeps everything.</summary>
    public int ExtractRetentionDays { get; set; }
}
