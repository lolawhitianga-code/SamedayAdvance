using System.Windows;

namespace DiagFileMonitor.App;

public partial class AnalysisWindow : Window
{
    public AnalysisWindow()
    {
        InitializeComponent();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
