using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiagFileMonitor.Core.Services;

namespace DiagFileMonitor.App.ViewModels;

public partial class AnalysisViewModel : ObservableObject
{
    [ObservableProperty]
    private string _heading = string.Empty;

    [ObservableProperty]
    private string _reportText = string.Empty;

    public AnalysisViewModel(string heading, string reportText)
    {
        Heading = heading;
        ReportText = reportText;
    }

    [RelayCommand]
    private void Copy()
    {
        try
        {
            System.Windows.Clipboard.SetText(ReportText);
            Heading = "Copied to the clipboard.";
        }
        catch (Exception ex)
        {
            Heading = $"Could not copy: {ex.Message}";
            SimpleLogger.Error("Could not copy the analysis to the clipboard", ex);
        }
    }
}
