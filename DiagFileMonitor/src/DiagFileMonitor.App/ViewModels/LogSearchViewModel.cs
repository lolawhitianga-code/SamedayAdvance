using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiagFileMonitor.Core.Services;

namespace DiagFileMonitor.App.ViewModels;

public partial class LogSearchViewModel : ObservableObject
{
    private readonly DiagFileRepository _repository;
    private readonly LogSearchService _searchService = new();

    public ObservableCollection<LogSearchHit> Hits { get; } = new();

    [ObservableProperty]
    private string _searchTerm = string.Empty;

    [ObservableProperty]
    private string _resultSummary = "Enter a term to search every stored bundle's logs.";

    [ObservableProperty]
    private bool _isSearching;

    [ObservableProperty]
    private LogSearchHit? _selectedHit;

    public LogSearchViewModel(DiagFileRepository repository)
    {
        _repository = repository;
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        if (string.IsNullOrWhiteSpace(SearchTerm))
        {
            ResultSummary = "Enter a term to search for.";
            return;
        }

        IsSearching = true;
        Hits.Clear();
        ResultSummary = "Searching...";

        try
        {
            var bundles = await _repository.GetAllAsync();
            var result = await _searchService.SearchAsync(bundles, SearchTerm);

            foreach (var hit in result.Hits)
            {
                Hits.Add(hit);
            }

            ResultSummary = result.Hits.Count == 0
                ? $"No matches for '{SearchTerm}' in {result.BundlesSearched} bundle(s)."
                : $"{result.Hits.Count} match(es) across {result.BundlesMatched} of {result.BundlesSearched} bundle(s)"
                  + (result.TruncatedAtLimit ? " - stopped at the result limit, narrow the term." : ".");

            if (result.MissingFiles > 0)
            {
                ResultSummary += $" {result.MissingFiles} log file(s) are recorded but no longer on disk.";
            }
        }
        catch (Exception ex)
        {
            ResultSummary = $"Search failed: {ex.Message}";
            SimpleLogger.Error("Log search failed", ex);
        }
        finally
        {
            IsSearching = false;
        }
    }

    [RelayCommand]
    private void OpenHit(LogSearchHit? hit)
    {
        var target = hit ?? SelectedHit;
        if (target is null || !File.Exists(target.LogFilePath))
        {
            ResultSummary = "That log file is no longer on disk.";
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(target.LogFilePath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ResultSummary = $"Could not open the log: {ex.Message}";
            SimpleLogger.Error($"Could not open '{target.LogFilePath}'", ex);
        }
    }
}
