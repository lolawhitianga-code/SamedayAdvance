using System.Text.Json;

namespace DiagFileMonitor.Core.Services;

/// <summary>Loads/saves AppSettings as JSON under %AppData%\DiagFileMonitor, seeding sane defaults on first run.</summary>
public class SettingsService
{
    private readonly string _settingsFilePath;
    private readonly string _appDataDir;

    public SettingsService(string? settingsFilePath = null)
    {
        _appDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DiagFileMonitor");
        Directory.CreateDirectory(_appDataDir);

        _settingsFilePath = settingsFilePath ?? Path.Combine(_appDataDir, "settings.json");
    }

    public AppSettings Load()
    {
        if (!File.Exists(_settingsFilePath))
        {
            var defaults = CreateDefaults();
            Save(defaults);
            return defaults;
        }

        try
        {
            var json = File.ReadAllText(_settingsFilePath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json);
            return settings ?? CreateDefaults();
        }
        catch
        {
            return CreateDefaults();
        }
    }

    public void Save(AppSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_settingsFilePath, json);
    }

    private AppSettings CreateDefaults()
    {
        var watchFolder = Path.Combine(_appDataDir, "Incoming");
        var extractRoot = Path.Combine(_appDataDir, "Extracted");
        Directory.CreateDirectory(watchFolder);
        Directory.CreateDirectory(extractRoot);

        return new AppSettings
        {
            WatchFolderPath = watchFolder,
            ExtractRootPath = extractRoot,
            FileExtensions = new List<string> { ".zip" },
            DatabasePath = Path.Combine(_appDataDir, "diagfiles.db")
        };
    }
}
