using System.IO;
using System.Windows;
using DiagFileMonitor.App.ViewModels;
using DiagFileMonitor.Core.Data;
using DiagFileMonitor.Core.Services;
using Microsoft.EntityFrameworkCore;

namespace DiagFileMonitor.App;

public partial class App : System.Windows.Application
{
    private FolderMonitorService? _monitorService;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var settingsService = new SettingsService();
        var settings = settingsService.Load();

        var logPath = Path.Combine(
            Path.GetDirectoryName(settings.DatabasePath) ?? AppContext.BaseDirectory,
            "logs",
            "diagfilemonitor.log");
        SimpleLogger.Initialize(logPath);

        DbContextOptions<DiagDbContext> BuildOptions()
        {
            var builder = new DbContextOptionsBuilder<DiagDbContext>();
            builder.UseSqlite($"Data Source={settings.DatabasePath}");
            return builder.Options;
        }

        using (var context = new DiagDbContext(BuildOptions()))
        {
            DatabaseInitializer.Initialize(context);
        }

        var repository = new DiagFileRepository(() => new DiagDbContext(BuildOptions()));
        var processor = new DiagFileProcessor(settings.ExtractRootPath, repository);
        _monitorService = new FolderMonitorService(processor);

        var viewModel = new MainViewModel(settingsService, repository, _monitorService);

        var mainWindow = new MainWindow { DataContext = viewModel };
        mainWindow.Show();

        _ = viewModel.LoadCommand.ExecuteAsync(null);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _monitorService?.Dispose();
        base.OnExit(e);
    }
}
