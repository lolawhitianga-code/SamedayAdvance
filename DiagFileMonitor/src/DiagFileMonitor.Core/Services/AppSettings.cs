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

    /// <summary>Ignore bundles whose file name timestamp is older than this many days. 0 processes everything.</summary>
    public int MonitorMaxAgeDays { get; set; }

    /// <summary>Whether the timestamp in a support file name is UTC rather than the machine's local time.</summary>
    public bool FileNameTimesAreUtc { get; set; } = true;

    public AlertSettings Alerts { get; set; } = new();
    public ZohoSettings Zoho { get; set; } = new();
    public EmailSettings Email { get; set; } = new();
}

/// <summary>
/// When a machine sending several bundles in a row should raise an alert. How the bundle is
/// analysed is not configurable here - the alert runs the same analysis as the Analyse button.
/// </summary>
public class AlertSettings
{
    public bool Enabled { get; set; }

    /// <summary>How many bundles from one machine inside the window before alerting.</summary>
    public int BurstThreshold { get; set; } = BurstDetector.DefaultThreshold;

    public int BurstWindowHours { get; set; } = BurstDetector.DefaultWindowHours;

}
