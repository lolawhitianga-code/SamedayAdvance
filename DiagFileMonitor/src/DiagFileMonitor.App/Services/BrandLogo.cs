using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DiagFileMonitor.Core.Services;

namespace DiagFileMonitor.App.Services;

/// <summary>
/// Finds the company logo. The artwork ships inside the exe, so a fresh build looks right with
/// nothing to copy, but a file on disk still wins - drop a logo.png beside the exe or in the app
/// data folder to change it without rebuilding.
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

        return LoadEmbedded();
    }

    /// <summary>The artwork built into the exe, used when no file on disk overrides it.</summary>
    private static ImageSource? LoadEmbedded()
    {
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri("pack://application:,,,/Assets/logo.png", UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();

            return bitmap;
        }
        catch (Exception ex)
        {
            // The text wordmark in MainWindow covers this, so a missing resource is not fatal.
            SimpleLogger.Error("Could not load the built-in logo", ex);
            return null;
        }
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
