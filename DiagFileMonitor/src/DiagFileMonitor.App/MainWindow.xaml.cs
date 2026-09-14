using System.Windows;

namespace DiagFileMonitor.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
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
