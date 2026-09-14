using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiagFileMonitor.App.Services;
using DiagFileMonitor.Core.Models;
using DiagFileMonitor.Core.Services;

namespace DiagFileMonitor.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly SettingsService _settingsService;
    private readonly DiagFileRepository _repository;
    private readonly FolderMonitorService _monitorService;
    private readonly TrayNotifier _notifier;
    private readonly ExtractCleanupService _cleanupService;
    private readonly BurstAlertService? _alertService;
    private readonly DatabaseResetService _resetService;
    private readonly DiagnosticAnalysisService _analysisService;
    private readonly DiagnosticComparisonService _comparisonService;
    private readonly int _repeatWindowDays;

    /// <summary>Company logo, if one was dropped next to the exe. Null shows the text wordmark instead.</summary>
    public System.Windows.Media.ImageSource? LogoImage { get; } = BrandLogo.Load();

    public bool HasLogo => LogoImage is not null;

    public ObservableCollection<DiagnosticFileSummary> Files { get; } = new();
    public ICollectionView FilesView { get; }

    public List<string> GroupByOptions { get; } = new()
    {
        "Serial Number",
        "Model",
        "Customer",
        "Site Location",
        "Software Version",
        "Arrived Date",
        "Status"
    };

    public List<string> StatusFilterOptions { get; } = new()
    {
        DiagnosticFileFilter.AnyStatus,
        nameof(ProcessingStatus.Processed),
        nameof(ProcessingStatus.Error)
    };

    [ObservableProperty]
    private string _selectedGroupBy = "Serial Number";

    public ObservableCollection<string> WatchFolders { get; } = new();

    [ObservableProperty]
    private string? _selectedWatchFolder;

    [ObservableProperty]
    private string _extensionsText = ".zip";

    [ObservableProperty]
    private bool _isMonitoring;

    [ObservableProperty]
    private string _statusMessage = "Not monitoring.";

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _selectedStatusFilter = DiagnosticFileFilter.AnyStatus;

    [ObservableProperty]
    private DateTime? _fromDate;

    [ObservableProperty]
    private DateTime? _toDate;

    [ObservableProperty]
    private DiagnosticFileSummary? _selectedFile;

    /// <summary>Every row highlighted in the grid. WPF cannot bind SelectedItems, so the view pushes it here.</summary>
    public List<DiagnosticFileSummary> SelectedFiles { get; private set; } = new();

    public void SetSelectedFiles(IEnumerable<DiagnosticFileSummary> files)
    {
        SelectedFiles = files.ToList();
        OnPropertyChanged(nameof(SelectionLabel));
        AnalyseSelectedCommand.NotifyCanExecuteChanged();
        CompareWithMasterCommand.NotifyCanExecuteChanged();
    }

    public string SelectionLabel => SelectedFiles.Count > 1
        ? $"{SelectedFiles.Count} files selected"
        : SelectedFile?.OriginalFileName ?? "No file selected";

    [ObservableProperty]
    private bool _isAnalysing;

    private bool CanAnalyse() => !IsAnalysing && (SelectedFiles.Count > 0 || SelectedFile is not null);

    /// <summary>Analyses every highlighted row, or just the current one. Result opens in its own window.</summary>
    [RelayCommand(CanExecute = nameof(CanAnalyse))]
    private async Task AnalyseSelectedAsync()
    {
        var ids = SelectedFiles.Count > 0
            ? SelectedFiles.Select(f => f.Id).ToList()
            : SelectedFile is null ? new List<int>() : new List<int> { SelectedFile.Id };

        if (ids.Count == 0)
        {
            StatusMessage = "Select one or more files to analyse.";
            return;
        }

        IsAnalysing = true;
        AnalyseSelectedCommand.NotifyCanExecuteChanged();
        StatusMessage = ids.Count == 1 ? "Analysing..." : $"Analysing {ids.Count} files...";

        try
        {
            var report = await _analysisService.AnalyseAsync(ids);

            var heading = ids.Count == 1
                ? $"Analysis of {SelectedFiles.FirstOrDefault()?.OriginalFileName ?? SelectedFile?.OriginalFileName}"
                : $"Analysis of {ids.Count} diagnostic files";

            AnalysisReady?.Invoke(this, new AnalysisResult(heading, report));
            StatusMessage = ids.Count == 1 ? "Analysis complete." : $"Analysed {ids.Count} files.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Analysis failed: {ex.Message}";
            SimpleLogger.Error("Analysis failed", ex);
        }
        finally
        {
            IsAnalysing = false;
            AnalyseSelectedCommand.NotifyCanExecuteChanged();
        }
    }

    /// <summary>The bundle held up as the known-good benchmark to measure others against.</summary>
    [ObservableProperty]
    private DiagnosticFileSummary? _masterFile;

    public bool HasMaster => MasterFile is not null;

    public string MasterLabel => MasterFile is null
        ? "No benchmark set"
        : $"Benchmark: {MasterFile.OriginalFileName}  ({MasterFile.MachineType}, serial {MasterFile.SerialNumber})";

    partial void OnMasterFileChanged(DiagnosticFileSummary? value)
    {
        OnPropertyChanged(nameof(HasMaster));
        OnPropertyChanged(nameof(MasterLabel));
        CompareWithMasterCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void SetMaster(DiagnosticFileSummary? row)
    {
        var target = row ?? SelectedFile;
        if (target is null)
        {
            StatusMessage = "Select a file to use as the benchmark.";
            return;
        }

        MasterFile = target;
        StatusMessage = $"'{target.OriginalFileName}' is now the benchmark to compare against.";
    }

    [RelayCommand]
    private void ClearMaster()
    {
        MasterFile = null;
        StatusMessage = "Benchmark cleared.";
    }

    private bool CanCompare() => !IsAnalysing && MasterFile is not null && SelectedFile is not null
                                 && SelectedFile.Id != MasterFile.Id;

    /// <summary>Measures the selected bundle against the benchmark: step process, speed and settings.</summary>
    [RelayCommand(CanExecute = nameof(CanCompare))]
    private async Task CompareWithMasterAsync()
    {
        if (MasterFile is null || SelectedFile is null) return;

        IsAnalysing = true;
        CompareWithMasterCommand.NotifyCanExecuteChanged();
        StatusMessage = $"Comparing '{SelectedFile.OriginalFileName}' against the benchmark...";

        try
        {
            var report = await _comparisonService.CompareAsync(MasterFile.Id, SelectedFile.Id);

            AnalysisReady?.Invoke(this, new AnalysisResult(
                $"{SelectedFile.OriginalFileName} vs benchmark {MasterFile.OriginalFileName}", report));

            StatusMessage = "Comparison complete.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Comparison failed: {ex.Message}";
            SimpleLogger.Error("Comparison failed", ex);
        }
        finally
        {
            IsAnalysing = false;
            CompareWithMasterCommand.NotifyCanExecuteChanged();
        }
    }

    /// <summary>Raised when a report is ready; the view opens the window so the ViewModel stays free of it.</summary>
    public event EventHandler<AnalysisResult>? AnalysisReady;

    public record AnalysisResult(string Heading, string ReportText);

    [ObservableProperty]
    private DashboardStats _stats = DashboardStats.Empty;

    [ObservableProperty]
    private string _editTicketNumber = string.Empty;

    [ObservableProperty]
    private string _editNotes = string.Empty;

    [ObservableProperty]
    private bool _notifyOnArrival = true;

    partial void OnNotifyOnArrivalChanged(bool value)
    {
        _notifier.Enabled = value;

        var settings = _settingsService.Load();
        settings.NotifyOnArrival = value;
        _settingsService.Save(settings);
    }

    [ObservableProperty]
    private int _maxAgeDays;

    partial void OnMaxAgeDaysChanged(int value)
    {
        _monitorService.MaxAgeDays = value;

        var settings = _settingsService.Load();
        settings.MonitorMaxAgeDays = value;
        _settingsService.Save(settings);
    }

    [RelayCommand]
    private async Task ClearDatabaseAsync()
    {
        var confirm = System.Windows.MessageBox.Show(
            $"Delete all {Files.Count} stored diagnostic record(s) and their unpacked files?\n\n"
            + "This also removes case notes, ticket numbers and baseline marks. It cannot be undone.\n\n"
            + "The original .szip files in your watch folders are not touched, so they can be processed again.",
            "Clear the database",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning,
            System.Windows.MessageBoxResult.No);

        if (confirm != System.Windows.MessageBoxResult.Yes) return;

        var result = await _resetService.ResetAsync();
        await LoadAsync();
        StatusMessage = result.Summary + " Start monitoring to process the folders again.";
    }

    [ObservableProperty]
    private int _retentionDays;

    partial void OnRetentionDaysChanged(int value)
    {
        var settings = _settingsService.Load();
        settings.ExtractRetentionDays = value;
        _settingsService.Save(settings);
    }

    /// <summary>Runs the retention policy. Called on startup, and from the Clean up now button.</summary>
    public async Task RunCleanupAsync(bool announceWhenNothingToDo)
    {
        var result = await _cleanupService.CleanupAsync(RetentionDays, DateTime.UtcNow);

        if (result.FoldersDeleted > 0 || result.Failures > 0 || announceWhenNothingToDo)
        {
            StatusMessage = result.Summary;
        }

        if (result.FoldersDeleted > 0)
        {
            await LoadAsync();
        }
    }

    [RelayCommand]
    private async Task CleanUpNowAsync()
    {
        if (RetentionDays <= 0)
        {
            StatusMessage = "Set a retention period above 0 days first.";
            return;
        }

        var confirm = System.Windows.MessageBox.Show(
            $"Delete the unpacked files of every non-baseline bundle older than {RetentionDays} days?\n\n"
            + "The dashboard history, notes and ticket references are kept - only the extracted files on disk are removed.",
            "Clean up old extracts",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);

        if (confirm != System.Windows.MessageBoxResult.Yes) return;

        await RunCleanupAsync(announceWhenNothingToDo: true);
    }

    [ObservableProperty]
    private bool _baselinesOnly;

    partial void OnBaselinesOnlyChanged(bool value) => RefreshFilter();

    [RelayCommand]
    private async Task ToggleBaselineAsync(DiagnosticFileSummary? row)
    {
        var target = row ?? SelectedFile;
        if (target is null)
        {
            StatusMessage = "Select a file to mark as a baseline.";
            return;
        }

        var newValue = !target.IsBaseline;
        if (!await _repository.SetBaselineAsync(target.Id, newValue))
        {
            StatusMessage = "That file is no longer in the database.";
            return;
        }

        target.IsBaseline = newValue;
        RefreshFilter();
        StatusMessage = newValue
            ? $"Marked '{target.OriginalFileName}' as a known-good baseline for {target.MachineType}."
            : $"'{target.OriginalFileName}' is no longer a baseline.";
    }

    [ObservableProperty]
    private string? _machineFilterSerial;

    [ObservableProperty]
    private MachineHistory? _machineHistory;

    public bool IsMachineFilterActive => !string.IsNullOrEmpty(MachineFilterSerial);

    partial void OnMachineFilterSerialChanged(string? value)
    {
        MachineHistory = string.IsNullOrEmpty(value) ? null : MachineHistory.For(Files, value);
        OnPropertyChanged(nameof(IsMachineFilterActive));
        RefreshFilter();
    }

    [RelayCommand]
    private void ShowMachineHistory(DiagnosticFileSummary? row)
    {
        var target = row ?? SelectedFile;
        if (target is null)
        {
            StatusMessage = "Select a file to see that machine's history.";
            return;
        }

        MachineFilterSerial = target.SerialNumber;
        StatusMessage = $"Showing every bundle from {target.SerialNumber}.";
    }

    [RelayCommand]
    private void ClearMachineFilter() => MachineFilterSerial = null;

    partial void OnSelectedFileChanged(DiagnosticFileSummary? value)
    {
        OnPropertyChanged(nameof(SelectionLabel));
        AnalyseSelectedCommand.NotifyCanExecuteChanged();
        CompareWithMasterCommand.NotifyCanExecuteChanged();
        EditTicketNumber = value?.TicketNumber ?? string.Empty;
        EditNotes = value?.Notes ?? string.Empty;
    }

    [RelayCommand]
    private async Task SaveNotesAsync()
    {
        if (SelectedFile is null)
        {
            StatusMessage = "Select a file before saving notes.";
            return;
        }

        var saved = await _repository.UpdateNotesAsync(SelectedFile.Id, EditTicketNumber, EditNotes);
        if (!saved)
        {
            StatusMessage = "That file is no longer in the database.";
            return;
        }

        SelectedFile.TicketNumber = string.IsNullOrWhiteSpace(EditTicketNumber) ? null : EditTicketNumber.Trim();
        SelectedFile.Notes = string.IsNullOrWhiteSpace(EditNotes) ? null : EditNotes.Trim();

        // The grid binds to a plain DTO, so nudge the view to redraw the ticket column.
        FilesView.Refresh();
        StatusMessage = $"Saved notes for '{SelectedFile.OriginalFileName}'.";
    }

    public MainViewModel(SettingsService settingsService, DiagFileRepository repository,
        FolderMonitorService monitorService, TrayNotifier notifier, ExtractCleanupService cleanupService,
        DatabaseResetService resetService, DiagnosticAnalysisService analysisService,
        DiagnosticComparisonService comparisonService, BurstAlertService? alertService = null)
    {
        _settingsService = settingsService;
        _repository = repository;
        _monitorService = monitorService;
        _notifier = notifier;
        _cleanupService = cleanupService;
        _alertService = alertService;
        _resetService = resetService;
        _analysisService = analysisService;
        _comparisonService = comparisonService;

        FilesView = CollectionViewSource.GetDefaultView(Files);
        FilesView.Filter = o => o is DiagnosticFileSummary row && DiagnosticFileFilter.Matches(row, CurrentCriteria());
        ApplyGrouping();

        _monitorService.FileProcessed += OnFileProcessed;
        _monitorService.FileFailed += OnFileFailed;
        _monitorService.FileSkippedAsTooOld += OnFileSkippedAsTooOld;
        _monitorService.FileSkippedAsDuplicate += OnFileSkippedAsDuplicate;

        var settings = _settingsService.Load();
        _repeatWindowDays = settings.RepeatWindowDays;
        _notifyOnArrival = settings.NotifyOnArrival;
        _retentionDays = settings.ExtractRetentionDays;
        _maxAgeDays = settings.MonitorMaxAgeDays;
        _monitorService.MaxAgeDays = settings.MonitorMaxAgeDays;
        _monitorService.FileNameTimesAreUtc = settings.FileNameTimesAreUtc;
        _notifier.Enabled = settings.NotifyOnArrival;
        foreach (var folder in settings.WatchFolders)
        {
            WatchFolders.Add(folder);
        }

        ExtensionsText = string.Join(", ", settings.FileExtensions);
    }

    public LogSearchViewModel CreateLogSearchViewModel() => new(_repository);

    public IntegrationSettingsViewModel CreateIntegrationSettingsViewModel() => new(_settingsService);

    private FilterCriteria CurrentCriteria() => new()
    {
        SearchText = SearchText,
        Status = SelectedStatusFilter,
        SerialNumber = MachineFilterSerial,
        BaselinesOnly = BaselinesOnly,
        FromDate = FromDate,
        ToDate = ToDate
    };

    partial void OnSelectedGroupByChanged(string value) => ApplyGrouping();

    partial void OnSearchTextChanged(string value) => RefreshFilter();

    partial void OnSelectedStatusFilterChanged(string value) => RefreshFilter();

    partial void OnFromDateChanged(DateTime? value) => RefreshFilter();

    partial void OnToDateChanged(DateTime? value) => RefreshFilter();

    private void RefreshFilter()
    {
        FilesView.Refresh();
        OnPropertyChanged(nameof(FilterSummary));
    }

    /// <summary>
    /// Recomputes everything derived from the file list, then redraws. Marking has to happen
    /// before the refresh: the grid binds to plain DTOs that raise no change notifications.
    /// </summary>
    private void RefreshDerivedState()
    {
        RepeatSubmissionMarker.Mark(Files, _repeatWindowDays);
        Stats = DashboardStats.Calculate(Files, DateTime.Now);

        if (MachineFilterSerial is { } serial)
        {
            MachineHistory = MachineHistory.For(Files, serial);
        }

        RefreshFilter();
    }

    public string FilterSummary
    {
        get
        {
            var shown = FilesView.Cast<object>().Count();
            return shown == Files.Count
                ? $"{Files.Count} file(s)"
                : $"{shown} of {Files.Count} file(s)";
        }
    }

    private void ApplyGrouping()
    {
        var propertyName = SelectedGroupBy switch
        {
            "Serial Number" => nameof(DiagnosticFileSummary.SerialNumber),
            "Model" => nameof(DiagnosticFileSummary.MachineType),
            "Customer" => nameof(DiagnosticFileSummary.Customer),
            "Site Location" => nameof(DiagnosticFileSummary.SiteLocation),
            "Software Version" => nameof(DiagnosticFileSummary.Version),
            "Arrived Date" => nameof(DiagnosticFileSummary.ArrivedDate),
            "Status" => nameof(DiagnosticFileSummary.Status),
            _ => nameof(DiagnosticFileSummary.SerialNumber)
        };

        FilesView.GroupDescriptions.Clear();
        FilesView.GroupDescriptions.Add(new PropertyGroupDescription(propertyName));

        FilesView.SortDescriptions.Clear();
        FilesView.SortDescriptions.Add(new SortDescription(propertyName, ListSortDirection.Ascending));
        FilesView.SortDescriptions.Add(new SortDescription(nameof(DiagnosticFileSummary.ArrivedAtUtc), ListSortDirection.Descending));
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        var all = await _repository.GetAllAsync();
        Files.Clear();
        foreach (var file in all)
        {
            Files.Add(DiagnosticFileSummary.FromEntity(file));
        }

        RefreshDerivedState();
    }

    [RelayCommand]
    private void OpenExtractedFolder(DiagnosticFileSummary? row)
    {
        var target = row ?? SelectedFile;
        if (target?.ExtractedPath is null || !Directory.Exists(target.ExtractedPath))
        {
            StatusMessage = "That bundle has no extracted folder on disk.";
            return;
        }

        Launch(target.ExtractedPath, isFolder: true);
    }

    [RelayCommand]
    private void OpenChangeLog(DiagnosticFileSummary? row) => OpenLog((row ?? SelectedFile)?.ChangeLogPath, "changelog.txt");

    [RelayCommand]
    private void OpenMachineLog(DiagnosticFileSummary? row) => OpenLog((row ?? SelectedFile)?.MachineLogPath, "machinelog.txt");

    [RelayCommand]
    private void OpenErrorLog(DiagnosticFileSummary? row) => OpenLog((row ?? SelectedFile)?.ErrorLogPath, "errorlog.txt");

    private void OpenLog(string? path, string label)
    {
        if (path is null || !File.Exists(path))
        {
            StatusMessage = $"This bundle has no {label}.";
            return;
        }

        Launch(path, isFolder: false);
    }

    private void Launch(string path, bool isFolder)
    {
        try
        {
            if (isFolder)
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
            }
            else
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not open '{path}': {ex.Message}";
            SimpleLogger.Error($"Could not open '{path}'", ex);
        }
    }

    [RelayCommand]
    private void CopySummary(DiagnosticFileSummary? row)
    {
        var target = row ?? SelectedFile;
        if (target is null)
        {
            StatusMessage = "Select a file to copy its summary.";
            return;
        }

        try
        {
            System.Windows.Clipboard.SetText(CaseSummaryFormatter.Format(target));
            StatusMessage = $"Copied summary for '{target.OriginalFileName}' to the clipboard.";
        }
        catch (Exception ex)
        {
            // The clipboard can be locked by another process.
            StatusMessage = $"Could not copy to clipboard: {ex.Message}";
            SimpleLogger.Error("Clipboard copy failed", ex);
        }
    }

    [RelayCommand]
    private void ClearFilters()
    {
        SearchText = string.Empty;
        SelectedStatusFilter = DiagnosticFileFilter.AnyStatus;
        FromDate = null;
        ToDate = null;
        MachineFilterSerial = null;
        BaselinesOnly = false;
    }

    [RelayCommand]
    private void StartMonitoring()
    {
        var extensions = CurrentExtensions();

        if (extensions.Length == 0)
        {
            StatusMessage = "Enter at least one file extension (e.g. .zip).";
            return;
        }

        if (WatchFolders.Count == 0)
        {
            StatusMessage = "Add at least one folder to monitor first.";
            return;
        }

        _monitorService.Start(WatchFolders, extensions);
        IsMonitoring = true;
        StatusMessage = WatchFolders.Count == 1
            ? $"Monitoring '{WatchFolders[0]}' for {string.Join(", ", extensions)}"
            : $"Monitoring {WatchFolders.Count} folders for {string.Join(", ", extensions)}";

        SaveFolderSettings(extensions.ToList());
    }

    [RelayCommand]
    private void StopMonitoring()
    {
        _monitorService.Stop();
        IsMonitoring = false;
        StatusMessage = "Not monitoring.";
    }

    [RelayCommand]
    private void AddFolder()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Choose a folder to monitor for diagnostic files",
            InitialDirectory = WatchFolders.Count > 0 ? WatchFolders[0] : null
        };

        if (dialog.ShowDialog() != true) return;

        if (WatchFolders.Contains(dialog.FolderName, StringComparer.OrdinalIgnoreCase))
        {
            StatusMessage = "That folder is already being watched.";
            return;
        }

        WatchFolders.Add(dialog.FolderName);
        SaveFolderSettings();
        RestartWatchersIfRunning();
        StatusMessage = $"Added '{dialog.FolderName}'.";
    }

    [RelayCommand]
    private void RemoveFolder()
    {
        if (SelectedWatchFolder is null)
        {
            StatusMessage = "Select a folder to remove.";
            return;
        }

        var removed = SelectedWatchFolder;
        WatchFolders.Remove(removed);
        SaveFolderSettings();
        RestartWatchersIfRunning();
        StatusMessage = $"Removed '{removed}'. No longer watching it.";
    }

    /// <summary>
    /// Points the watchers at the current list straight away, so a folder removed from the list
    /// stops being watched without needing Stop and Start. Safe to re-scan: bundles already in
    /// the database are skipped as duplicates.
    /// </summary>
    private void RestartWatchersIfRunning()
    {
        if (!IsMonitoring) return;

        if (WatchFolders.Count == 0)
        {
            _monitorService.Stop();
            IsMonitoring = false;
            return;
        }

        _monitorService.Start(WatchFolders, CurrentExtensions());
    }

    private string[] CurrentExtensions() =>
        ExtensionsText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private void SaveFolderSettings(List<string>? extensions = null)
    {
        var settings = _settingsService.Load();
        settings.WatchFolders = WatchFolders.ToList();
        if (extensions is not null) settings.FileExtensions = extensions;
        _settingsService.Save(settings);
    }

    private void OnFileProcessed(object? sender, DiagnosticFile file)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            OnFileProcessedOnUiThread(file);
        });

        _ = RaiseAlertIfBurstAsync(file);
    }

    private void OnFileProcessedOnUiThread(DiagnosticFile file)
    {
        Files.Insert(0, DiagnosticFileSummary.FromEntity(file));
        RefreshDerivedState();

        var row = Files[0];
        NotifyArrival(file, row);
        StatusMessage = file.Status == ProcessingStatus.Processed
            ? $"Processed '{file.OriginalFileName}' (serial {file.SerialNumber}).{(row.IsRepeatSubmission ? " Repeat from this machine." : string.Empty)}"
            : $"Processed '{file.OriginalFileName}' with issues: {file.ErrorMessage}";
    }

    /// <summary>
    /// Runs the burst check and everything it triggers, off the UI thread. Nothing here may
    /// throw: a Zoho outage or a bad mail password must not disturb a bundle that was recorded
    /// successfully.
    /// </summary>
    private async Task RaiseAlertIfBurstAsync(DiagnosticFile file)
    {
        if (_alertService is null) return;

        try
        {
            var snapshot = (await _repository.GetAllAsync())
                .Select(DiagnosticFileSummary.FromEntity)
                .ToList();

            var outcome = await _alertService.HandleAsync(file, snapshot);
            if (!outcome.Triggered) return;

            _notifier.Notify("Repeated diagnostics", outcome.Summary, isProblem: true);

            await System.Windows.Application.Current.Dispatcher.InvokeAsync(async () =>
            {
                StatusMessage = outcome.Summary;
                await LoadAsync();
            });
        }
        catch (Exception ex)
        {
            SimpleLogger.Error("Burst alert handling failed", ex);
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(
                () => StatusMessage = $"Burst alert failed: {ex.Message}");
        }
    }

    private void NotifyArrival(DiagnosticFile file, DiagnosticFileSummary row)
    {
        if (file.Status == ProcessingStatus.Error)
        {
            _notifier.Notify("Diagnostic file failed", $"{file.OriginalFileName} could not be read: {file.ErrorMessage}", isProblem: true);
            return;
        }

        var detail = $"{row.SerialNumber} - {row.Customer}";
        if (row.IsRepeatSubmission)
        {
            _notifier.Notify("Repeat diagnostic file", $"{detail} has sent another bundle within {_repeatWindowDays} days.", isProblem: true);
            return;
        }

        _notifier.Notify("Diagnostic file received", detail);
    }

    private int _skippedAsTooOld;

    private void OnFileSkippedAsTooOld(object? sender, string path)
    {
        var total = Interlocked.Increment(ref _skippedAsTooOld);

        System.Windows.Application.Current.Dispatcher.Invoke(() =>
            StatusMessage = $"Skipped {total} file(s) older than {MaxAgeDays} days.");
    }

    private int _skippedAsDuplicate;

    private void OnFileSkippedAsDuplicate(object? sender, string path)
    {
        var total = Interlocked.Increment(ref _skippedAsDuplicate);

        System.Windows.Application.Current.Dispatcher.Invoke(() =>
            StatusMessage = $"Skipped {total} file(s) already in the database.");
    }

    private void OnFileFailed(object? sender, string path)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            StatusMessage = $"Failed to process '{path}'. See logs for details.";
        });
    }
}
