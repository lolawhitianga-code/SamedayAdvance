using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiagFileMonitor.Core.Models;
using DiagFileMonitor.Core.Services;

namespace DiagFileMonitor.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly SettingsService _settingsService;
    private readonly DiagFileRepository _repository;
    private readonly FolderMonitorService _monitorService;

    public ObservableCollection<DiagnosticFileSummary> Files { get; } = new();
    public ICollectionView FilesView { get; }

    public List<string> GroupByOptions { get; } = new()
    {
        "Serial Number",
        "Machine Type",
        "Customer",
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

    [ObservableProperty]
    private string _watchFolderPath = string.Empty;

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

    [ObservableProperty]
    private DashboardStats _stats = DashboardStats.Empty;

    [ObservableProperty]
    private string _editTicketNumber = string.Empty;

    [ObservableProperty]
    private string _editNotes = string.Empty;

    partial void OnSelectedFileChanged(DiagnosticFileSummary? value)
    {
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

    public MainViewModel(SettingsService settingsService, DiagFileRepository repository, FolderMonitorService monitorService)
    {
        _settingsService = settingsService;
        _repository = repository;
        _monitorService = monitorService;

        FilesView = CollectionViewSource.GetDefaultView(Files);
        FilesView.Filter = o => o is DiagnosticFileSummary row && DiagnosticFileFilter.Matches(row, CurrentCriteria());
        ApplyGrouping();

        _monitorService.FileProcessed += OnFileProcessed;
        _monitorService.FileFailed += OnFileFailed;

        var settings = _settingsService.Load();
        WatchFolderPath = settings.WatchFolderPath;
        ExtensionsText = string.Join(", ", settings.FileExtensions);
    }

    private FilterCriteria CurrentCriteria() => new()
    {
        SearchText = SearchText,
        Status = SelectedStatusFilter,
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

    private void RecalculateStats() => Stats = DashboardStats.Calculate(Files, DateTime.Now);

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
            "Machine Type" => nameof(DiagnosticFileSummary.MachineType),
            "Customer" => nameof(DiagnosticFileSummary.Customer),
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

        RefreshFilter();
        RecalculateStats();
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
    private void ClearFilters()
    {
        SearchText = string.Empty;
        SelectedStatusFilter = DiagnosticFileFilter.AnyStatus;
        FromDate = null;
        ToDate = null;
    }

    [RelayCommand]
    private void StartMonitoring()
    {
        var extensions = ExtensionsText
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (extensions.Length == 0)
        {
            StatusMessage = "Enter at least one file extension (e.g. .zip).";
            return;
        }

        if (string.IsNullOrWhiteSpace(WatchFolderPath))
        {
            StatusMessage = "Choose a folder to monitor first.";
            return;
        }

        _monitorService.Start(WatchFolderPath, extensions);
        IsMonitoring = true;
        StatusMessage = $"Monitoring '{WatchFolderPath}' for {string.Join(", ", extensions)}";

        var settings = _settingsService.Load();
        settings.WatchFolderPath = WatchFolderPath;
        settings.FileExtensions = extensions.ToList();
        _settingsService.Save(settings);
    }

    [RelayCommand]
    private void StopMonitoring()
    {
        _monitorService.Stop();
        IsMonitoring = false;
        StatusMessage = "Not monitoring.";
    }

    [RelayCommand]
    private void BrowseFolder()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Choose folder to monitor for diagnostic files",
            InitialDirectory = string.IsNullOrWhiteSpace(WatchFolderPath) ? null : WatchFolderPath
        };

        if (dialog.ShowDialog() == true)
        {
            WatchFolderPath = dialog.FolderName;
        }
    }

    private void OnFileProcessed(object? sender, DiagnosticFile file)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            Files.Insert(0, DiagnosticFileSummary.FromEntity(file));
            StatusMessage = file.Status == ProcessingStatus.Processed
                ? $"Processed '{file.OriginalFileName}' (serial {file.SerialNumber})."
                : $"Processed '{file.OriginalFileName}' with issues: {file.ErrorMessage}";
            OnPropertyChanged(nameof(FilterSummary));
            RecalculateStats();
        });
    }

    private void OnFileFailed(object? sender, string path)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            StatusMessage = $"Failed to process '{path}'. See logs for details.";
        });
    }
}
