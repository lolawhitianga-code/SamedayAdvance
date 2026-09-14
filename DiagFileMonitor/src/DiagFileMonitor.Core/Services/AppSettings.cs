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

    public AlertSettings Alerts { get; set; } = new();
    public ZohoSettings Zoho { get; set; } = new();
    public EmailSettings Email { get; set; } = new();
}

/// <summary>When a machine sending several bundles in a row should raise an alert, and how it is analysed.</summary>
public class AlertSettings
{
    public bool Enabled { get; set; }

    /// <summary>How many bundles from one machine inside the window before alerting.</summary>
    public int BurstThreshold { get; set; } = BurstDetector.DefaultThreshold;

    public int BurstWindowHours { get; set; } = BurstDetector.DefaultWindowHours;

    /// <summary>A step this much slower than the baseline median is reported. 1.5 = 50% slower.</summary>
    public double SlowStepFactor { get; set; } = 1.5;

    /// <summary>Changelog entries this recent are flagged as a possible cause.</summary>
    public int RecentChangeDays { get; set; } = 30;

    /// <summary>
    /// Regex for a machinelog.txt line, with named groups timestamp, marker and step.
    /// Blank uses the built-in default, which has not yet been checked against a real machine log.
    /// </summary>
    public string MachineLogPattern { get; set; } = string.Empty;

    public AnalysisOptions ToAnalysisOptions() => new()
    {
        SlowStepFactor = SlowStepFactor,
        RecentChangeDays = RecentChangeDays,
        MachineLogPattern = string.IsNullOrWhiteSpace(MachineLogPattern) ? null : MachineLogPattern
    };
}
