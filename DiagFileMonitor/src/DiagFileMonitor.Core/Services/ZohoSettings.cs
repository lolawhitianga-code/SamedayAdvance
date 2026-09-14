namespace DiagFileMonitor.Core.Services;

/// <summary>
/// Zoho Desk connection details. Create a Self Client in the Zoho API console, grant it the
/// ticket scopes, and paste the resulting client id / secret / refresh token here.
/// <para>
/// Hostnames differ per data centre: .com, .com.au, .eu, .in and so on. Both the API host and
/// the accounts host must match the data centre your Zoho org lives in.
/// </para>
/// </summary>
public class ZohoSettings
{
    public bool Enabled { get; set; }

    public string ApiBaseUrl { get; set; } = "https://desk.zoho.com/api/v1";
    public string AccountsBaseUrl { get; set; } = "https://accounts.zoho.com";

    public string OrgId { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;

    /// <summary>Department new tickets are raised in. Optional - Zoho uses the default if blank.</summary>
    public string DepartmentId { get; set; } = string.Empty;

    /// <summary>Contact the ticket is raised against. Zoho requires either this or contact details.</summary>
    public string DefaultContactId { get; set; } = string.Empty;

    /// <summary>Post the analysis as a private note rather than a customer-visible reply.</summary>
    public bool PostAsPrivateNote { get; set; } = true;

    /// <summary>Reuse a ticket raised for the same machine within this many hours instead of creating another.</summary>
    public int ReuseTicketWithinHours { get; set; } = 24;

    public bool IsConfigured =>
        Enabled
        && !string.IsNullOrWhiteSpace(OrgId)
        && !string.IsNullOrWhiteSpace(ClientId)
        && !string.IsNullOrWhiteSpace(ClientSecret)
        && !string.IsNullOrWhiteSpace(RefreshToken);
}
