using System.Windows;

namespace DiagFileMonitor.App;

public partial class ReportWindow : Window
{
    public ReportWindow()
    {
        InitializeComponent();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
