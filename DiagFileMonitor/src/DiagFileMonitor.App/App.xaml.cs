using System.IO;
using System.Windows;
using DiagFileMonitor.App.Services;
using DiagFileMonitor.App.ViewModels;
using DiagFileMonitor.Core.Data;
using DiagFileMonitor.Core.Services;
using Microsoft.EntityFrameworkCore;

namespace DiagFileMonitor.App;

public partial class App : System.Windows.Application
{
    private FolderMonitorService? _monitorService;
    private TrayNotifier? _notifier;

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
        var processor = new DiagFileProcessor(settings.ExtractRootPath, repository, settings.FileNameTimesAreUtc);
        _monitorService = new FolderMonitorService(processor);
        _notifier = new TrayNotifier();

        var cleanupService = new ExtractCleanupService(() => new DiagDbContext(BuildOptions()), settings.ExtractRootPath);
        var alertService = BuildAlertService(settings, repository);

        var resetService = new DatabaseResetService(() => new DiagDbContext(BuildOptions()), settings.ExtractRootPath);

        var viewModel = new MainViewModel(settingsService, repository, _monitorService, _notifier,
            cleanupService, resetService, alertService);

        var mainWindow = new MainWindow { DataContext = viewModel };
        mainWindow.Show();

        _ = InitialiseAsync(viewModel);
    }

    /// <summary>Only wires up alerting when it is switched on, so an unconfigured app does no outbound work.</summary>
    private static BurstAlertService? BuildAlertService(AppSettings settings, DiagFileRepository repository)
    {
        if (!settings.Alerts.Enabled) return null;

        var zoho = settings.Zoho.IsConfigured ? new ZohoDeskClient(settings.Zoho) : null;
        var email = settings.Email.IsConfigured ? new SmtpEmailAlertSender(settings.Email) : null;

        return new BurstAlertService(
            repository,
            new DiagnosticAnalyser(settings.Alerts.ToAnalysisOptions()),
            settings.Zoho,
            settings.Alerts,
            zoho,
            email);
    }

    private static async Task InitialiseAsync(MainViewModel viewModel)
    {
        await viewModel.LoadCommand.ExecuteAsync(null);
        await viewModel.RunCleanupAsync(announceWhenNothingToDo: false);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _monitorService?.Dispose();
        _notifier?.Dispose();
        base.OnExit(e);
    }
}
