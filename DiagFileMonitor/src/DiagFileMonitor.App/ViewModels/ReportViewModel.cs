using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiagFileMonitor.Core.Reports;
using DiagFileMonitor.Core.Services;

namespace DiagFileMonitor.App.ViewModels;

/// <summary>
/// Builds an HTML report out of the bundles already stored, and saves it somewhere the user can
/// send it on.
/// <para>
/// The scope is chosen here rather than assumed: a report covers the machines and the period that
/// were asked for. Everything on the page comes from the analysis the app already runs.
/// </para>
/// </summary>
public partial class ReportViewModel : ObservableObject
{
    private readonly ReportService _reportService;
    private readonly string _outputFolder;

    [ObservableProperty] private int _kindIndex;
    [ObservableProperty] private int _periodIndex = 1;

    [ObservableProperty] private string _subject = string.Empty;
    [ObservableProperty] private string _serials = string.Empty;
    [ObservableProperty] private string _machineTypes = string.Empty;

    [ObservableProperty] private bool _includePlateSensor = true;
    [ObservableProperty] private bool _includeDriveFaults = true;
    [ObservableProperty] private bool _includeMotorConfirm = true;
    [ObservableProperty] private bool _includeKnownFaults = true;
    [ObservableProperty] private bool _includeSoftwareErrors;

    [ObservableProperty] private string _secondsPerEvent = string.Empty;
    [ObservableProperty] private string _secondsPerEventSource = string.Empty;
    [ObservableProperty] private string _proposedChange = string.Empty;
    [ObservableProperty] private string _stillNeeded = string.Empty;
    [ObservableProperty] private string _softCost = string.Empty;

    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _createdPath = string.Empty;

    public List<string> KindOptions { get; } = new()
    {
        "Fault benchmarking - every occurrence across the fleet",
        "Mechanical change case - the argument for a design change"
    };

    public List<string> PeriodOptions { get; } = new()
    {
        "Last 30 days",
        "Last 90 days",
        "Last 12 months",
        "Every stored bundle"
    };

    public bool IsChangeCase => KindIndex == 1;
    public bool HasCreatedReport => CreatedPath.Length > 0;

    public ReportViewModel(ReportService reportService, string outputFolder)
    {
        _reportService = reportService;
        _outputFolder = outputFolder;
    }

    partial void OnKindIndexChanged(int value) => OnPropertyChanged(nameof(IsChangeCase));

    partial void OnCreatedPathChanged(string value)
    {
        OnPropertyChanged(nameof(HasCreatedReport));
        OpenReportCommand.NotifyCanExecuteChanged();
        ShowFolderCommand.NotifyCanExecuteChanged();
    }

    private bool CanBuild() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanBuild))]
    private async Task BuildAsync()
    {
        IsBusy = true;
        BuildCommand.NotifyCanExecuteChanged();
        StatusMessage = "Reading the stored bundles...";
        CreatedPath = string.Empty;

        try
        {
            var generated = await _reportService.GenerateAsync(BuildRequest());
            var path = Path.Combine(_outputFolder, generated.SuggestedFileName);

            await ReportService.SaveAsync(generated, path);
            CreatedPath = path;

            StatusMessage = $"{generated.Scan.Occurrences.Count} occurrence(s) across "
                            + $"{generated.Scan.Machines.Count} machine(s). Saved as {generated.SuggestedFileName}.";

            if (generated.Scan.Skipped.Count > 0)
            {
                StatusMessage += Environment.NewLine
                                 + $"{generated.Scan.Skipped.Count} bundle(s) could not be read - "
                                 + "see the report's \"About this data\" section.";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not build the report: {ex.Message}";
            SimpleLogger.Error("Could not build the report", ex);
        }
        finally
        {
            IsBusy = false;
            BuildCommand.NotifyCanExecuteChanged();
        }
    }

    [RelayCommand(CanExecute = nameof(HasCreatedReport))]
    private void OpenReport() => Launch(new ProcessStartInfo(CreatedPath) { UseShellExecute = true });

    [RelayCommand(CanExecute = nameof(HasCreatedReport))]
    private void ShowFolder() =>
        Launch(new ProcessStartInfo("explorer.exe", $"/select,\"{CreatedPath}\"") { UseShellExecute = true });

    private void Launch(ProcessStartInfo info)
    {
        try
        {
            Process.Start(info);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not open it: {ex.Message}";
            SimpleLogger.Error("Could not open the report", ex);
        }
    }

    internal ReportRequest BuildRequest()
    {
        var (days, label) = PeriodIndex switch
        {
            0 => (30, "Last 30 days"),
            1 => (90, "Last 90 days"),
            2 => (365, "Last 12 months"),
            _ => (0, "Every stored bundle")
        };

        return new ReportRequest
        {
            Kind = IsChangeCase ? ReportKind.MechanicalChangeCase : ReportKind.FaultBenchmarking,
            Subject = Subject.Trim(),
            PeriodLabel = label,
            Scope = new FleetScanRequest
            {
                FromUtc = days > 0 ? DateTime.UtcNow.Date.AddDays(-days) : null,
                Serials = Split(Serials),
                MachineTypes = Split(MachineTypes),
                Kinds = SelectedKinds()
            },
            ChangeCase = new ChangeCaseInputs
            {
                TimePerEvent = ParseSeconds(SecondsPerEvent),
                TimePerEventSource = SecondsPerEventSource.Trim(),
                ProposedSteps = Lines(ProposedChange),
                StillNeeded = StillNeeded.Trim(),
                SoftCost = SoftCost.Trim()
            }
        };
    }

    private List<FaultKind> SelectedKinds()
    {
        var kinds = new List<FaultKind>();

        if (IncludePlateSensor)
        {
            kinds.Add(FaultKind.PlateSensorGlitch);
            kinds.Add(FaultKind.PlateSensorUnclear);
        }

        if (IncludeDriveFaults) kinds.Add(FaultKind.DriveFault);
        if (IncludeMotorConfirm) kinds.Add(FaultKind.MotorNotConfirmed);
        if (IncludeKnownFaults) kinds.Add(FaultKind.KnownMachineFault);
        if (IncludeSoftwareErrors) kinds.Add(FaultKind.SoftwareError);

        // An empty list means everything, which is not what an all-boxes-clear means here.
        return kinds.Count == 0 ? new List<FaultKind> { FaultKind.PlateSensorGlitch } : kinds;
    }

    /// <summary>Only a real number counts. A blank or a typo leaves the downtime figure unstated
    /// rather than quietly turning into zero minutes a month.</summary>
    private static TimeSpan? ParseSeconds(string value) =>
        double.TryParse(value.Trim(), out var seconds) && seconds > 0
            ? TimeSpan.FromSeconds(seconds)
            : null;

    private static string[] Split(string value) =>
        value.Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .ToArray();

    private static string[] Lines(string value) =>
        value.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .ToArray();
}
