using System.Drawing;
using System.Windows.Forms;

namespace DiagFileMonitor.App.Services;

/// <summary>
/// Notification-area balloon shown when a bundle lands, so support does not have to sit
/// watching the dashboard. Uses the WinForms tray icon, which works without the packaged-app
/// identity that Windows toast notifications require.
/// </summary>
public sealed class TrayNotifier : IDisposable
{
    private readonly NotifyIcon _icon;

    public bool Enabled { get; set; } = true;

    public TrayNotifier()
    {
        _icon = new NotifyIcon
        {
            Icon = SystemIcons.Information,
            Visible = true,
            Text = "Diagnostic File Monitor"
        };
    }

    public void Notify(string title, string message, bool isProblem = false)
    {
        if (!Enabled) return;

        try
        {
            _icon.ShowBalloonTip(5000, title, message, isProblem ? ToolTipIcon.Warning : ToolTipIcon.Info);
        }
        catch (Exception ex)
        {
            // A balloon failing must never take down the processing pipeline.
            Core.Services.SimpleLogger.Error("Could not show tray notification", ex);
        }
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
