using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DiagFileMonitor.Core.Services;

public class ZohoTicket
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("ticketNumber")]
    public string TicketNumber { get; set; } = string.Empty;

    [JsonPropertyName("webUrl")]
    public string? WebUrl { get; set; }
}

public class ZohoException : Exception
{
    public HttpStatusCode? StatusCode { get; }

    public ZohoException(string message, HttpStatusCode? statusCode = null, Exception? inner = null)
        : base(message, inner)
    {
        StatusCode = statusCode;
    }
}

/// <summary>
/// Minimal Zoho Desk client: refreshes the OAuth access token, raises tickets and posts
/// comments. Only the few calls this app needs are implemented.
/// </summary>
public class ZohoDeskClient : IZohoDeskClient
{
    private readonly HttpClient _http;
    private readonly ZohoSettings _settings;

    private string? _accessToken;
    private DateTime _accessTokenExpiresUtc = DateTime.MinValue;

    public ZohoDeskClient(ZohoSettings settings, HttpClient? httpClient = null)
    {
        _settings = settings;
        _http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    }

    /// <summary>Exchanges the long-lived refresh token for an access token, reusing it until it nears expiry.</summary>
    public async Task<string> GetAccessTokenAsync(CancellationToken token = default)
    {
        if (_accessToken is not null && DateTime.UtcNow < _accessTokenExpiresUtc)
        {
            return _accessToken;
        }

        var url = $"{_settings.AccountsBaseUrl.TrimEnd('/')}/oauth/v2/token"
                  + $"?refresh_token={Uri.EscapeDataString(_settings.RefreshToken)}"
                  + $"&client_id={Uri.EscapeDataString(_settings.ClientId)}"
                  + $"&client_secret={Uri.EscapeDataString(_settings.ClientSecret)}"
                  + "&grant_type=refresh_token";

        HttpResponseMessage response;
        try
        {
            response = await _http.PostAsync(url, content: null, token);
        }
        catch (HttpRequestException ex)
        {
            // Usually the accounts hostname pointing at the wrong data centre.
            throw new ZohoException(
                $"Could not reach Zoho at '{_settings.AccountsBaseUrl}' to refresh the access token: {ex.Message}. "
                + "Check the accounts host matches your data centre (.com, .com.au, .eu ...).", null, ex);
        }

        using var _ = response;
        var body = await response.Content.ReadAsStringAsync(token);

        if (!response.IsSuccessStatusCode)
        {
            throw new ZohoException($"Zoho token refresh failed ({(int)response.StatusCode}): {Trim(body)}", response.StatusCode);
        }

        using var document = JsonDocument.Parse(body);

        // Zoho answers 200 with an "error" field when the refresh token is bad.
        if (document.RootElement.TryGetProperty("error", out var error))
        {
            throw new ZohoException($"Zoho token refresh rejected: {error}");
        }

        if (!document.RootElement.TryGetProperty("access_token", out var accessToken))
        {
            throw new ZohoException($"Zoho token response had no access_token: {Trim(body)}");
        }

        var expiresInSeconds = document.RootElement.TryGetProperty("expires_in", out var expires)
            ? expires.GetInt32()
            : 3600;

        _accessToken = accessToken.GetString();
        // Renew a minute early so a call cannot start with a token that expires mid-flight.
        _accessTokenExpiresUtc = DateTime.UtcNow.AddSeconds(expiresInSeconds - 60);

        return _accessToken!;
    }

    public async Task<ZohoTicket> CreateTicketAsync(string subject, string description, CancellationToken token = default)
    {
        var payload = new Dictionary<string, object>
        {
            ["subject"] = subject,
            ["description"] = description
        };

        if (!string.IsNullOrWhiteSpace(_settings.DepartmentId)) payload["departmentId"] = _settings.DepartmentId;
        if (!string.IsNullOrWhiteSpace(_settings.DefaultContactId)) payload["contactId"] = _settings.DefaultContactId;

        using var request = await BuildRequestAsync(HttpMethod.Post, "tickets", token);
        request.Content = JsonContent.Create(payload);

        var body = await SendAsync(request, "create ticket", token);

        return JsonSerializer.Deserialize<ZohoTicket>(body)
               ?? throw new ZohoException($"Zoho returned no ticket: {Trim(body)}");
    }

    public async Task AddCommentAsync(string ticketId, string comment, CancellationToken token = default)
    {
        var payload = new Dictionary<string, object>
        {
            ["content"] = comment,
            ["isPublic"] = !_settings.PostAsPrivateNote
        };

        using var request = await BuildRequestAsync(HttpMethod.Post, $"tickets/{ticketId}/comments", token);
        request.Content = JsonContent.Create(payload);

        await SendAsync(request, "add comment", token);
    }

    /// <summary>Cheapest call that proves the credentials, org id and data centre are all right.</summary>
    public async Task<string> TestConnectionAsync(CancellationToken token = default)
    {
        using var request = await BuildRequestAsync(HttpMethod.Get, "organizations", token);
        var body = await SendAsync(request, "read organizations", token);

        using var document = JsonDocument.Parse(body);
        if (document.RootElement.TryGetProperty("data", out var data) && data.GetArrayLength() > 0)
        {
            var first = data[0];
            var name = first.TryGetProperty("companyName", out var company) ? company.GetString() : null;
            return $"Connected to Zoho Desk{(name is null ? string.Empty : $" ({name})")}.";
        }

        return "Connected to Zoho Desk.";
    }

    private async Task<HttpRequestMessage> BuildRequestAsync(HttpMethod method, string path, CancellationToken token)
    {
        var accessToken = await GetAccessTokenAsync(token);

        var request = new HttpRequestMessage(method, $"{_settings.ApiBaseUrl.TrimEnd('/')}/{path}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Zoho-oauthtoken", accessToken);
        request.Headers.Add("orgId", _settings.OrgId);

        return request;
    }

    private async Task<string> SendAsync(HttpRequestMessage request, string what, CancellationToken token)
    {
        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, token);
        }
        catch (HttpRequestException ex)
        {
            throw new ZohoException($"Could not reach Zoho to {what}: {ex.Message}", null, ex);
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(token);

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                // Force a fresh token next time; a stale one is the usual cause.
                _accessToken = null;
                _accessTokenExpiresUtc = DateTime.MinValue;
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new ZohoException($"Zoho refused to {what} ({(int)response.StatusCode}): {Trim(body)}", response.StatusCode);
            }

            return body;
        }
    }

    private static string Trim(string body) =>
        body.Length <= 400 ? body : body[..400] + "...";
}
