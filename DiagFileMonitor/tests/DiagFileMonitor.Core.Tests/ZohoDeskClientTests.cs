using System.Net;
using System.Text;
using DiagFileMonitor.Core.Services;

namespace DiagFileMonitor.Core.Tests;

/// <summary>Captures outgoing requests and replies with canned responses, so the client can be
/// exercised without a Zoho account.</summary>
internal sealed class FakeHandler : HttpMessageHandler
{
    private readonly Queue<(HttpStatusCode Status, string Body)> _responses = new();

    public List<HttpRequestMessage> Requests { get; } = new();
    public List<string> RequestBodies { get; } = new();

    public FakeHandler Respond(HttpStatusCode status, string body)
    {
        _responses.Enqueue((status, body));
        return this;
    }

    public FakeHandler RespondWithToken(string token = "tok-1", int expiresIn = 3600) =>
        Respond(HttpStatusCode.OK, $$"""{"access_token":"{{token}}","expires_in":{{expiresIn}},"api_domain":"https://desk.zoho.com"}""");

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        RequestBodies.Add(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));

        var (status, body) = _responses.Count > 0 ? _responses.Dequeue() : (HttpStatusCode.OK, "{}");

        return new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
    }
}

public class ZohoDeskClientTests
{
    private static ZohoSettings Settings() => new()
    {
        Enabled = true,
        ApiBaseUrl = "https://desk.zoho.com/api/v1",
        AccountsBaseUrl = "https://accounts.zoho.com",
        OrgId = "org-123",
        ClientId = "client-1",
        ClientSecret = "secret-1",
        RefreshToken = "refresh-1",
        DepartmentId = "dept-9",
        DefaultContactId = "contact-7"
    };

    [Fact]
    public async Task RefreshesTheAccessTokenAgainstTheConfiguredAccountsHost()
    {
        var handler = new FakeHandler().RespondWithToken("abc123");
        var client = new ZohoDeskClient(Settings(), new HttpClient(handler));

        var token = await client.GetAccessTokenAsync();

        Assert.Equal("abc123", token);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.StartsWith("https://accounts.zoho.com/oauth/v2/token", request.RequestUri!.ToString());
        Assert.Contains("grant_type=refresh_token", request.RequestUri.Query);
        Assert.Contains("refresh_token=refresh-1", request.RequestUri.Query);
    }

    [Fact]
    public async Task ReusesAValidAccessTokenRatherThanRefreshingEveryCall()
    {
        var handler = new FakeHandler().RespondWithToken();
        var client = new ZohoDeskClient(Settings(), new HttpClient(handler));

        await client.GetAccessTokenAsync();
        await client.GetAccessTokenAsync();

        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task RefreshesAgainWhenTheTokenIsAboutToExpire()
    {
        // 30s lifetime is inside the one-minute safety margin, so it must not be reused.
        var handler = new FakeHandler().RespondWithToken("first", expiresIn: 30).RespondWithToken("second", expiresIn: 30);
        var client = new ZohoDeskClient(Settings(), new HttpClient(handler));

        Assert.Equal("first", await client.GetAccessTokenAsync());
        Assert.Equal("second", await client.GetAccessTokenAsync());
    }

    [Fact]
    public async Task ReportsABadRefreshTokenClearly()
    {
        // Zoho answers 200 with an error field rather than an HTTP error status.
        var handler = new FakeHandler().Respond(HttpStatusCode.OK, """{"error":"invalid_code"}""");
        var client = new ZohoDeskClient(Settings(), new HttpClient(handler));

        var ex = await Assert.ThrowsAsync<ZohoException>(() => client.GetAccessTokenAsync());

        Assert.Contains("invalid_code", ex.Message);
    }

    [Fact]
    public async Task CreatesATicketWithTheRightHeadersAndBody()
    {
        var handler = new FakeHandler()
            .RespondWithToken()
            .Respond(HttpStatusCode.OK, """{"id":"55","ticketNumber":"101","webUrl":"https://desk.zoho.com/agent/t/55"}""");

        var client = new ZohoDeskClient(Settings(), new HttpClient(handler));

        var ticket = await client.CreateTicketAsync("Repeated diagnostics: SN-1", "the report body");

        Assert.Equal("55", ticket.Id);
        Assert.Equal("101", ticket.TicketNumber);

        var request = handler.Requests[1];
        Assert.Equal("https://desk.zoho.com/api/v1/tickets", request.RequestUri!.ToString());
        Assert.Equal("Zoho-oauthtoken", request.Headers.Authorization!.Scheme);
        Assert.Equal("tok-1", request.Headers.Authorization.Parameter);
        Assert.Equal("org-123", Assert.Single(request.Headers.GetValues("orgId")));

        var body = handler.RequestBodies[1];
        Assert.Contains("\"subject\":\"Repeated diagnostics: SN-1\"", body);
        Assert.Contains("\"departmentId\":\"dept-9\"", body);
        Assert.Contains("\"contactId\":\"contact-7\"", body);
    }

    [Fact]
    public async Task OmitsOptionalIdsWhenTheyAreNotConfigured()
    {
        var settings = Settings();
        settings.DepartmentId = string.Empty;
        settings.DefaultContactId = string.Empty;

        var handler = new FakeHandler().RespondWithToken().Respond(HttpStatusCode.OK, """{"id":"1","ticketNumber":"2"}""");
        var client = new ZohoDeskClient(settings, new HttpClient(handler));

        await client.CreateTicketAsync("subject", "body");

        Assert.DoesNotContain("departmentId", handler.RequestBodies[1]);
        Assert.DoesNotContain("contactId", handler.RequestBodies[1]);
    }

    [Fact]
    public async Task PostsCommentsAsPrivateNotesByDefault()
    {
        var handler = new FakeHandler().RespondWithToken().Respond(HttpStatusCode.OK, "{}");
        var client = new ZohoDeskClient(Settings(), new HttpClient(handler));

        await client.AddCommentAsync("55", "analysis text");

        Assert.Equal("https://desk.zoho.com/api/v1/tickets/55/comments", handler.Requests[1].RequestUri!.ToString());
        Assert.Contains("\"isPublic\":false", handler.RequestBodies[1]);
        Assert.Contains("analysis text", handler.RequestBodies[1]);
    }

    [Fact]
    public async Task CanPostCommentsPublicly()
    {
        var settings = Settings();
        settings.PostAsPrivateNote = false;

        var handler = new FakeHandler().RespondWithToken().Respond(HttpStatusCode.OK, "{}");
        var client = new ZohoDeskClient(settings, new HttpClient(handler));

        await client.AddCommentAsync("55", "visible to the customer");

        Assert.Contains("\"isPublic\":true", handler.RequestBodies[1]);
    }

    [Fact]
    public async Task SurfacesZohoErrorsWithStatusAndBody()
    {
        var handler = new FakeHandler()
            .RespondWithToken()
            .Respond(HttpStatusCode.UnprocessableEntity, """{"errorCode":"INVALID_DATA","message":"contactId required"}""");

        var client = new ZohoDeskClient(Settings(), new HttpClient(handler));

        var ex = await Assert.ThrowsAsync<ZohoException>(() => client.CreateTicketAsync("s", "b"));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, ex.StatusCode);
        Assert.Contains("contactId required", ex.Message);
    }

    [Fact]
    public async Task ThrowsAwayTheCachedTokenAfterA401()
    {
        var handler = new FakeHandler()
            .RespondWithToken("stale")
            .Respond(HttpStatusCode.Unauthorized, """{"message":"invalid oauth token"}""")
            .RespondWithToken("fresh")
            .Respond(HttpStatusCode.OK, "{}");

        var client = new ZohoDeskClient(Settings(), new HttpClient(handler));

        await Assert.ThrowsAsync<ZohoException>(() => client.AddCommentAsync("55", "first go"));
        await client.AddCommentAsync("55", "second go");

        // Second attempt must have fetched a new token rather than reusing the rejected one.
        Assert.Equal("fresh", handler.Requests[3].Headers.Authorization!.Parameter);
    }

    [Fact]
    public async Task ReportsNetworkFailuresAsZohoExceptions()
    {
        var client = new ZohoDeskClient(Settings(), new HttpClient(new ThrowingHandler()));

        var ex = await Assert.ThrowsAsync<ZohoException>(() => client.GetAccessTokenAsync());

        Assert.Contains("Could not reach Zoho", ex.Message);
    }

    [Fact]
    public async Task TestConnectionNamesTheOrganisation()
    {
        var handler = new FakeHandler()
            .RespondWithToken()
            .Respond(HttpStatusCode.OK, """{"data":[{"id":"org-123","companyName":"Acme Service"}]}""");

        var client = new ZohoDeskClient(Settings(), new HttpClient(handler));

        Assert.Contains("Acme Service", await client.TestConnectionAsync());
    }

    [Fact]
    public void SettingsAreOnlyUsableOnceEveryCredentialIsPresent()
    {
        var settings = Settings();
        Assert.True(settings.IsConfigured);

        settings.RefreshToken = string.Empty;
        Assert.False(settings.IsConfigured);

        settings.RefreshToken = "refresh-1";
        settings.Enabled = false;
        Assert.False(settings.IsConfigured);
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("no such host");
    }
}
