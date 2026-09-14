using System.Net;
using System.Net.Mail;

namespace DiagFileMonitor.Core.Services;

public class EmailSettings
{
    public bool Enabled { get; set; }
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 587;
    public bool UseSsl { get; set; } = true;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string FromAddress { get; set; } = string.Empty;
    public List<string> ToAddresses { get; set; } = new();

    public bool IsConfigured =>
        Enabled
        && !string.IsNullOrWhiteSpace(Host)
        && !string.IsNullOrWhiteSpace(FromAddress)
        && ToAddresses.Count > 0;
}

public interface IEmailAlertSender
{
    Task SendAsync(string subject, string body, CancellationToken token = default);
}

/// <summary>Sends the burst alert over SMTP.</summary>
public class SmtpEmailAlertSender : IEmailAlertSender
{
    private readonly EmailSettings _settings;

    public SmtpEmailAlertSender(EmailSettings settings)
    {
        _settings = settings;
    }

    public async Task SendAsync(string subject, string body, CancellationToken token = default)
    {
        if (!_settings.IsConfigured)
        {
            throw new InvalidOperationException("Email is not configured: set Host, FromAddress and at least one recipient.");
        }

        using var message = new MailMessage
        {
            From = new MailAddress(_settings.FromAddress),
            Subject = subject,
            Body = body,
            IsBodyHtml = false
        };

        foreach (var recipient in _settings.ToAddresses)
        {
            message.To.Add(recipient);
        }

        using var client = new SmtpClient(_settings.Host, _settings.Port)
        {
            EnableSsl = _settings.UseSsl,
            DeliveryMethod = SmtpDeliveryMethod.Network
        };

        if (!string.IsNullOrWhiteSpace(_settings.Username))
        {
            client.Credentials = new NetworkCredential(_settings.Username, _settings.Password);
        }

        await client.SendMailAsync(message, token);
    }
}
