using System.Windows;

namespace DiagFileMonitor.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is ViewModels.MainViewModel previous)
        {
            previous.AnalysisReady -= OnAnalysisReady;
            previous.FeedbackRequested -= OnFeedbackRequested;
        }

        if (e.NewValue is ViewModels.MainViewModel current)
        {
            current.AnalysisReady += OnAnalysisReady;
            current.FeedbackRequested += OnFeedbackRequested;
        }
    }

    private void OnAnalysisReady(object? sender, ViewModels.MainViewModel.AnalysisResult result)
    {
        new AnalysisWindow
        {
            Owner = this,
            DataContext = new ViewModels.AnalysisViewModel(result.Heading, result.ReportText)
        }.Show();
    }

    private void OnFeedbackRequested(object? sender, ViewModels.MainViewModel.FeedbackRequest request)
    {
        if (DataContext is not ViewModels.MainViewModel viewModel) return;

        new FeedbackWindow
        {
            Owner = this,
            DataContext = new ViewModels.FeedbackViewModel(
                viewModel.FeedbackPackageService, request.File, request.ReportText, request.OutputFolder)
        }.Show();
    }

    /// <summary>WPF cannot bind SelectedItems, so the multi-selection is pushed to the ViewModel here.</summary>
    private void FilesGrid_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (DataContext is not ViewModels.MainViewModel viewModel) return;
        if (sender is not System.Windows.Controls.DataGrid grid) return;

        viewModel.SetSelectedFiles(grid.SelectedItems.OfType<Core.Models.DiagnosticFileSummary>());
    }

    private void OpenIntegrationSettings_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ViewModels.MainViewModel viewModel) return;

        new IntegrationSettingsWindow
        {
            Owner = this,
            DataContext = viewModel.CreateIntegrationSettingsViewModel()
        }.ShowDialog();
    }

    private void OpenLogSearch_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ViewModels.MainViewModel viewModel) return;

        new LogSearchWindow
        {
            Owner = this,
            DataContext = viewModel.CreateLogSearchViewModel()
        }.Show();
    }
}
