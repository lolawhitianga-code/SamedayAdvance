using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using DiagFileMonitor.App.Services;

namespace DiagFileMonitor.App.ViewModels;

/// <summary>The splash shown while the database opens and the first load runs.</summary>
public partial class SplashViewModel : ObservableObject
{
    [ObservableProperty] private string _statusMessage = "Starting...";

    public ImageSource? LogoImage { get; }
    public bool HasLogo => LogoImage is not null;

    public string Version => AppVersion.Display;

    public SplashViewModel()
    {
        LogoImage = BrandLogo.Load();
    }
}
