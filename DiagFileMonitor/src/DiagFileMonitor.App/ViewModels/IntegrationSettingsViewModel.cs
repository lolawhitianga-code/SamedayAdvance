using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiagFileMonitor.Core.Services;

namespace DiagFileMonitor.App.ViewModels;

public partial class IntegrationSettingsViewModel : ObservableObject
{
    private readonly SettingsService _settingsService;

    [ObservableProperty] private bool _alertsEnabled;
    [ObservableProperty] private int _burstThreshold = BurstDetector.DefaultThreshold;
    [ObservableProperty] private int _burstWindowHours = BurstDetector.DefaultWindowHours;
    [ObservableProperty] private double _slowStepFactor = 1.5;
    [ObservableProperty] private string _machineLogPattern = string.Empty;

    [ObservableProperty] private bool _zohoEnabled;
    [ObservableProperty] private string _zohoApiBaseUrl = "https://desk.zoho.com/api/v1";
    [ObservableProperty] private string _zohoAccountsBaseUrl = "https://accounts.zoho.com";
    [ObservableProperty] private string _zohoOrgId = string.Empty;
    [ObservableProperty] private string _zohoClientId = string.Empty;
    [ObservableProperty] private string _zohoClientSecret = string.Empty;
    [ObservableProperty] private string _zohoRefreshToken = string.Empty;
    [ObservableProperty] private string _zohoDepartmentId = string.Empty;
    [ObservableProperty] private string _zohoContactId = string.Empty;
    [ObservableProperty] private bool _zohoPostAsPrivateNote = true;
    [ObservableProperty] private int _zohoReuseTicketWithinHours = 24;

    [ObservableProperty] private bool _emailEnabled;
    [ObservableProperty] private string _smtpHost = string.Empty;
    [ObservableProperty] private int _smtpPort = 587;
    [ObservableProperty] private bool _smtpUseSsl = true;
    [ObservableProperty] private string _smtpUsername = string.Empty;
    [ObservableProperty] private string _smtpPassword = string.Empty;
    [ObservableProperty] private string _emailFrom = string.Empty;
    [ObservableProperty] private string _emailTo = string.Empty;

    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private bool _isBusy;

    public IntegrationSettingsViewModel(SettingsService settingsService)
    {
        _settingsService = settingsService;
        Load();
    }

    private void Load()
    {
        var settings = _settingsService.Load();

        AlertsEnabled = settings.Alerts.Enabled;
        BurstThreshold = settings.Alerts.BurstThreshold;
        BurstWindowHours = settings.Alerts.BurstWindowHours;
        SlowStepFactor = settings.Alerts.SlowStepFactor;
        MachineLogPattern = settings.Alerts.MachineLogPattern;

        ZohoEnabled = settings.Zoho.Enabled;
        ZohoApiBaseUrl = settings.Zoho.ApiBaseUrl;
        ZohoAccountsBaseUrl = settings.Zoho.AccountsBaseUrl;
        ZohoOrgId = settings.Zoho.OrgId;
        ZohoClientId = settings.Zoho.ClientId;
        ZohoClientSecret = settings.Zoho.ClientSecret;
        ZohoRefreshToken = settings.Zoho.RefreshToken;
        ZohoDepartmentId = settings.Zoho.DepartmentId;
        ZohoContactId = settings.Zoho.DefaultContactId;
        ZohoPostAsPrivateNote = settings.Zoho.PostAsPrivateNote;
        ZohoReuseTicketWithinHours = settings.Zoho.ReuseTicketWithinHours;

        EmailEnabled = settings.Email.Enabled;
        SmtpHost = settings.Email.Host;
        SmtpPort = settings.Email.Port;
        SmtpUseSsl = settings.Email.UseSsl;
        SmtpUsername = settings.Email.Username;
        SmtpPassword = settings.Email.Password;
        EmailFrom = settings.Email.FromAddress;
        EmailTo = string.Join(", ", settings.Email.ToAddresses);
    }

    private AppSettings Apply()
    {
        var settings = _settingsService.Load();

        settings.Alerts.Enabled = AlertsEnabled;
        settings.Alerts.BurstThreshold = Math.Max(2, BurstThreshold);
        settings.Alerts.BurstWindowHours = Math.Max(1, BurstWindowHours);
        settings.Alerts.SlowStepFactor = SlowStepFactor <= 1 ? 1.5 : SlowStepFactor;
        settings.Alerts.MachineLogPattern = MachineLogPattern.Trim();

        settings.Zoho.Enabled = ZohoEnabled;
        settings.Zoho.ApiBaseUrl = ZohoApiBaseUrl.Trim();
        settings.Zoho.AccountsBaseUrl = ZohoAccountsBaseUrl.Trim();
        settings.Zoho.OrgId = ZohoOrgId.Trim();
        settings.Zoho.ClientId = ZohoClientId.Trim();
        settings.Zoho.ClientSecret = ZohoClientSecret.Trim();
        settings.Zoho.RefreshToken = ZohoRefreshToken.Trim();
        settings.Zoho.DepartmentId = ZohoDepartmentId.Trim();
        settings.Zoho.DefaultContactId = ZohoContactId.Trim();
        settings.Zoho.PostAsPrivateNote = ZohoPostAsPrivateNote;
        settings.Zoho.ReuseTicketWithinHours = Math.Max(1, ZohoReuseTicketWithinHours);

        settings.Email.Enabled = EmailEnabled;
        settings.Email.Host = SmtpHost.Trim();
        settings.Email.Port = SmtpPort;
        settings.Email.UseSsl = SmtpUseSsl;
        settings.Email.Username = SmtpUsername.Trim();
        settings.Email.Password = SmtpPassword;
        settings.Email.FromAddress = EmailFrom.Trim();
        settings.Email.ToAddresses = EmailTo
            .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        return settings;
    }

    [RelayCommand]
    private void Save()
    {
        _settingsService.Save(Apply());
        StatusMessage = "Saved. Restart monitoring for changes to take effect.";
    }

    [RelayCommand]
    private async Task TestZohoAsync()
    {
        IsBusy = true;
        StatusMessage = "Contacting Zoho...";

        try
        {
            var settings = Apply();
            _settingsService.Save(settings);

            if (!settings.Zoho.IsConfigured)
            {
                StatusMessage = "Fill in org id, client id, client secret and refresh token, and tick Enabled.";
                return;
            }

            StatusMessage = await new ZohoDeskClient(settings.Zoho).TestConnectionAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Zoho test failed: {ex.Message}";
            SimpleLogger.Error("Zoho test connection failed", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task TestEmailAsync()
    {
        IsBusy = true;
        StatusMessage = "Sending test email...";

        try
        {
            var settings = Apply();
            _settingsService.Save(settings);

            if (!settings.Email.IsConfigured)
            {
                StatusMessage = "Fill in the SMTP host, from address and at least one recipient, and tick Enabled.";
                return;
            }

            await new SmtpEmailAlertSender(settings.Email).SendAsync(
                "Diagnostic File Monitor test",
                "This is a test message from Diagnostic File Monitor. If you can read it, alerting is set up correctly.");

            StatusMessage = $"Test email sent to {string.Join(", ", settings.Email.ToAddresses)}.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Email test failed: {ex.Message}";
            SimpleLogger.Error("Email test failed", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
