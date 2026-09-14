using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DiagFileMonitor.Core.Services;

namespace DiagFileMonitor.App.Services;

/// <summary>
/// Finds the company logo at runtime rather than baking it into the build, so it can be
/// dropped in or swapped without recompiling. Looks next to the exe first, then in the
/// app data folder. Falls back to a text wordmark when no file is found.
/// </summary>
public static class BrandLogo
{
    private static readonly string[] FileNames = { "logo.png", "spida-logo.png", "logo.jpg", "logo.bmp" };

    public static ImageSource? Load()
    {
        foreach (var path in CandidatePaths())
        {
            if (!File.Exists(path)) continue;

            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                // OnLoad so the file is not left locked while the app runs.
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(path);
                bitmap.EndInit();
                bitmap.Freeze();

                return bitmap;
            }
            catch (Exception ex)
            {
                SimpleLogger.Error($"Could not load the logo from '{path}'", ex);
            }
        }

        return null;
    }

    public static IEnumerable<string> CandidatePaths()
    {
        var appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DiagFileMonitor");

        foreach (var name in FileNames)
        {
            yield return Path.Combine(AppContext.BaseDirectory, name);
        }

        foreach (var name in FileNames)
        {
            yield return Path.Combine(appData, name);
        }
    }
}
