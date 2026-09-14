using System.Collections.ObjectModel;
using System.ComponentModel;
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

    public ObservableCollection<DiagnosticFileRow> Files { get; } = new();
    public ICollectionView FilesView { get; }

    public List<string> GroupByOptions { get; } = new()
    {
        "Serial Number",
        "Machine Type",
        "Customer",
        "Arrived Date",
        "Status"
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

    public MainViewModel(SettingsService settingsService, DiagFileRepository repository, FolderMonitorService monitorService)
    {
        _settingsService = settingsService;
        _repository = repository;
        _monitorService = monitorService;

        FilesView = CollectionViewSource.GetDefaultView(Files);
        ApplyGrouping();

        _monitorService.FileProcessed += OnFileProcessed;
        _monitorService.FileFailed += OnFileFailed;

        var settings = _settingsService.Load();
        WatchFolderPath = settings.WatchFolderPath;
        ExtensionsText = string.Join(", ", settings.FileExtensions);
    }

    partial void OnSelectedGroupByChanged(string value) => ApplyGrouping();

    private void ApplyGrouping()
    {
        var propertyName = SelectedGroupBy switch
        {
            "Serial Number" => nameof(DiagnosticFileRow.SerialNumber),
            "Machine Type" => nameof(DiagnosticFileRow.MachineType),
            "Customer" => nameof(DiagnosticFileRow.Customer),
            "Arrived Date" => nameof(DiagnosticFileRow.ArrivedDate),
            "Status" => nameof(DiagnosticFileRow.Status),
            _ => nameof(DiagnosticFileRow.SerialNumber)
        };

        FilesView.GroupDescriptions.Clear();
        FilesView.GroupDescriptions.Add(new PropertyGroupDescription(propertyName));

        FilesView.SortDescriptions.Clear();
        FilesView.SortDescriptions.Add(new SortDescription(propertyName, ListSortDirection.Ascending));
        FilesView.SortDescriptions.Add(new SortDescription(nameof(DiagnosticFileRow.ArrivedAtUtc), ListSortDirection.Descending));
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        var all = await _repository.GetAllAsync();
        Files.Clear();
        foreach (var f in all)
        {
            Files.Add(ToRow(f));
        }
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
            Files.Insert(0, ToRow(file));
            StatusMessage = file.Status == ProcessingStatus.Processed
                ? $"Processed '{file.OriginalFileName}' (serial {file.SerialNumber})."
                : $"Processed '{file.OriginalFileName}' with issues: {file.ErrorMessage}";
        });
    }

    private void OnFileFailed(object? sender, string path)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            StatusMessage = $"Failed to process '{path}'. See logs for details.";
        });
    }

    private static DiagnosticFileRow ToRow(DiagnosticFile f) => new()
    {
        Id = f.Id,
        OriginalFileName = f.OriginalFileName,
        SerialNumber = string.IsNullOrWhiteSpace(f.SerialNumber) ? "(unknown)" : f.SerialNumber,
        MachineType = string.IsNullOrWhiteSpace(f.MachineType) ? "(unknown)" : f.MachineType,
        Customer = string.IsNullOrWhiteSpace(f.Customer) ? "(unknown)" : f.Customer,
        Version = string.IsNullOrWhiteSpace(f.Version) ? "(unknown)" : f.Version,
        ArrivedAtUtc = f.ArrivedAtUtc,
        Status = f.Status.ToString(),
        ErrorMessage = f.ErrorMessage,
        ExtractedPath = f.ExtractedPath
    };
}
