using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiagFileMonitor.Core.Models;
using DiagFileMonitor.Core.Services;

namespace DiagFileMonitor.App.ViewModels;

/// <summary>
/// Collects what a support person knows about a bundle that the report did not, and packages it
/// up for a Claude session. The fields are separate on purpose: "the saw was broken" cannot be
/// turned into a check, where "the output came on and the confirm input never did" can.
/// </summary>
public partial class FeedbackViewModel : ObservableObject
{
    private readonly FeedbackPackageService _packageService;
    private readonly DiagnosticFileSummary _file;
    private readonly string _outputFolder;

    [ObservableProperty] private string _heading = string.Empty;
    [ObservableProperty] private string _reportText = string.Empty;

    [ObservableProperty] private int _verdictIndex = 2;
    [ObservableProperty] private string _whatWasActuallyWrong = string.Empty;
    [ObservableProperty] private string _howYouKnew = string.Empty;
    [ObservableProperty] private string _whatShouldChange = string.Empty;
    [ObservableProperty] private string _raisedBy = string.Empty;

    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _createdPath = string.Empty;

    public List<string> VerdictOptions { get; } = new()
    {
        "It got it right",
        "Partly right - found something real but missed the point",
        "It missed it - the cause was in the files",
        "It sent me the wrong way"
    };

    public bool HasCreatedPackage => CreatedPath.Length > 0;

    public FeedbackViewModel(
        FeedbackPackageService packageService,
        DiagnosticFileSummary file,
        string reportText,
        string outputFolder)
    {
        _packageService = packageService;
        _file = file;
        _outputFolder = outputFolder;

        Heading = $"Feedback on {file.OriginalFileName}";
        ReportText = reportText;
    }

    partial void OnCreatedPathChanged(string value)
    {
        OnPropertyChanged(nameof(HasCreatedPackage));
        OpenFolderCommand.NotifyCanExecuteChanged();
    }

    partial void OnWhatWasActuallyWrongChanged(string value) => CreatePackageCommand.NotifyCanExecuteChanged();
    partial void OnHowYouKnewChanged(string value) => CreatePackageCommand.NotifyCanExecuteChanged();

    private AnalysisFeedback BuildFeedback() => new()
    {
        Verdict = VerdictIndex switch
        {
            0 => FeedbackVerdict.GotItRight,
            1 => FeedbackVerdict.PartlyRight,
            2 => FeedbackVerdict.MissedIt,
            _ => FeedbackVerdict.SentMeTheWrongWay
        },
        WhatWasActuallyWrong = WhatWasActuallyWrong,
        HowYouKnew = HowYouKnew,
        WhatShouldChange = WhatShouldChange,
        RaisedBy = RaisedBy
    };

    private bool CanCreate() => !IsBusy && BuildFeedback().IsUsable;

    [RelayCommand(CanExecute = nameof(CanCreate))]
    private async Task CreatePackageAsync()
    {
        IsBusy = true;
        CreatePackageCommand.NotifyCanExecuteChanged();
        StatusMessage = "Building the package...";

        try
        {
            var package = await _packageService.CreateAsync(_file.Id, BuildFeedback(), _outputFolder);

            StatusMessage = package.Summary;
            CreatedPath = package.Created ? package.ZipPath : string.Empty;

            foreach (var note in package.Notes)
            {
                StatusMessage += Environment.NewLine + note;
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not create the package: {ex.Message}";
            SimpleLogger.Error("Could not create the feedback package", ex);
        }
        finally
        {
            IsBusy = false;
            CreatePackageCommand.NotifyCanExecuteChanged();
        }
    }

    [RelayCommand(CanExecute = nameof(HasCreatedPackage))]
    private void OpenFolder()
    {
        try
        {
            var folder = Path.GetDirectoryName(CreatedPath);
            if (folder is null) return;

            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{CreatedPath}\"")
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not open the folder: {ex.Message}";
            SimpleLogger.Error("Could not open the feedback folder", ex);
        }
    }
}
