using System.Text.Json;
using DiagFileMonitor.Core.Services;

namespace DiagFileMonitor.Core.Tests;

public class SettingsServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "diagsettings", Guid.NewGuid().ToString("N"));

    private string SettingsPath
    {
        get
        {
            Directory.CreateDirectory(_dir);
            return Path.Combine(_dir, "settings.json");
        }
    }

    [Fact]
    public void MigratesTheOldSingleFolderSetting()
    {
        var path = SettingsPath;
        File.WriteAllText(path, """
            {
              "WatchFolderPath": "C:\\DiagDrop",
              "ExtractRootPath": "C:\\Extract",
              "FileExtensions": [".zip"],
              "DatabasePath": "C:\\diag.db"
            }
            """);

        var settings = new SettingsService(path).Load();

        Assert.Equal(["C:\\DiagDrop"], settings.WatchFolders);
        Assert.Null(settings.WatchFolderPath);
    }

    [Fact]
    public void LeavesAnExistingFolderListAlone()
    {
        var path = SettingsPath;
        File.WriteAllText(path, """
            {
              "WatchFolders": ["C:\\A", "C:\\B"],
              "WatchFolderPath": "C:\\Old",
              "FileExtensions": [".zip"]
            }
            """);

        var settings = new SettingsService(path).Load();

        Assert.Equal(["C:\\A", "C:\\B"], settings.WatchFolders);
    }

    [Fact]
    public void RoundTripsSettings()
    {
        var path = SettingsPath;
        var service = new SettingsService(path);

        var settings = service.Load();
        settings.WatchFolders = ["C:\\One", "C:\\Two"];
        settings.FileExtensions = [".zip", ".7z"];
        settings.RepeatWindowDays = 14;
        settings.ExtractRetentionDays = 90;
        settings.NotifyOnArrival = false;
        service.Save(settings);

        var reloaded = new SettingsService(path).Load();

        Assert.Equal(["C:\\One", "C:\\Two"], reloaded.WatchFolders);
        Assert.Equal([".zip", ".7z"], reloaded.FileExtensions);
        Assert.Equal(14, reloaded.RepeatWindowDays);
        Assert.Equal(90, reloaded.ExtractRetentionDays);
        Assert.False(reloaded.NotifyOnArrival);
    }

    [Fact]
    public void FallsBackToDefaultsWhenTheFileIsCorrupt()
    {
        var path = SettingsPath;
        File.WriteAllText(path, "{ this is not json");

        var settings = new SettingsService(path).Load();

        Assert.NotEmpty(settings.FileExtensions);
        Assert.Equal(RepeatSubmissionMarker.DefaultWindowDays, settings.RepeatWindowDays);
    }

    [Fact]
    public void DefaultsKeepEverythingUntilRetentionIsSet()
    {
        var settings = new SettingsService(SettingsPath).Load();

        Assert.Equal(0, settings.ExtractRetentionDays);
        Assert.Contains(".zip", settings.FileExtensions);
    }

    [Fact]
    public void SavedFileIsReadableJson()
    {
        var path = SettingsPath;
        new SettingsService(path).Save(new AppSettings { WatchFolders = ["C:\\X"] });

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        Assert.Equal("C:\\X", document.RootElement.GetProperty("WatchFolders")[0].GetString());
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }
}
