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

        // Up before anything slow happens, so the first thing seen is the brand rather than a
        // blank taskbar entry while the database opens.
        var splashModel = new SplashViewModel();
        var splash = new SplashWindow { DataContext = splashModel };
        splash.Show();

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
        var resetService = new DatabaseResetService(() => new DiagDbContext(BuildOptions()), settings.ExtractRootPath);

        var analysisService = new DiagnosticAnalysisService(repository);
        var comparisonService = new DiagnosticComparisonService(repository);

        // The alert posts the same analysis the Analyse button produces, so it shares the service.
        var alertService = BuildAlertService(settings, repository, analysisService);

        var viewModel = new MainViewModel(settingsService, repository, _monitorService, _notifier,
            cleanupService, resetService, analysisService, comparisonService, alertService);

        var mainWindow = new MainWindow { DataContext = viewModel };

        _ = InitialiseAsync(viewModel, splashModel, splash, mainWindow);
    }

    /// <summary>Only wires up alerting when it is switched on, so an unconfigured app does no outbound work.</summary>
    private static BurstAlertService? BuildAlertService(
        AppSettings settings, DiagFileRepository repository, DiagnosticAnalysisService analysisService)
    {
        if (!settings.Alerts.Enabled) return null;

        var zoho = settings.Zoho.IsConfigured ? new ZohoDeskClient(settings.Zoho) : null;
        var email = settings.Email.IsConfigured ? new SmtpEmailAlertSender(settings.Email) : null;

        return new BurstAlertService(
            repository,
            analysisService,
            settings.Zoho,
            settings.Alerts,
            zoho,
            email);
    }

    /// <summary>
    /// Loads behind the splash, then swaps to the dashboard. The splash closes in a finally so a
    /// failure during startup cannot leave it on screen with no way to dismiss it.
    /// </summary>
    private static async Task InitialiseAsync(
        MainViewModel viewModel, SplashViewModel splashModel, Window splash, Window mainWindow)
    {
        try
        {
            splashModel.StatusMessage = "Reading stored diagnostics...";
            await viewModel.LoadCommand.ExecuteAsync(null);

            splashModel.StatusMessage = "Tidying up unpacked files...";
            await viewModel.RunCleanupAsync(announceWhenNothingToDo: false);
        }
        catch (Exception ex)
        {
            SimpleLogger.Error("Something failed while starting up", ex);
        }
        finally
        {
            mainWindow.Show();
            splash.Close();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _monitorService?.Dispose();
        _notifier?.Dispose();
        base.OnExit(e);
    }
}
