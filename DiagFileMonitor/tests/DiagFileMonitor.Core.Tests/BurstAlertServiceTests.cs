using DiagFileMonitor.Core.Models;
using DiagFileMonitor.Core.Services;

namespace DiagFileMonitor.Core.Tests;

internal sealed class FakeZohoClient : IZohoDeskClient
{
    public List<(string Subject, string Body)> Created { get; } = new();
    public List<(string TicketId, string Comment)> Comments { get; } = new();
    public Exception? ThrowOnCreate { get; set; }

    private int _next = 500;

    public Task<ZohoTicket> CreateTicketAsync(string subject, string description, CancellationToken token = default)
    {
        if (ThrowOnCreate is not null) throw ThrowOnCreate;

        Created.Add((subject, description));
        var id = (++_next).ToString();
        return Task.FromResult(new ZohoTicket { Id = id, TicketNumber = $"T-{id}" });
    }

    public Task AddCommentAsync(string ticketId, string comment, CancellationToken token = default)
    {
        Comments.Add((ticketId, comment));
        return Task.CompletedTask;
    }
}

internal sealed class FakeEmailSender : IEmailAlertSender
{
    public List<(string Subject, string Body)> Sent { get; } = new();
    public Exception? ThrowOnSend { get; set; }

    public Task SendAsync(string subject, string body, CancellationToken token = default)
    {
        if (ThrowOnSend is not null) throw ThrowOnSend;

        Sent.Add((subject, body));
        return Task.CompletedTask;
    }
}

public class BurstAlertServiceTests
{
    private static AlertSettings Alerts() => new() { Enabled = true, BurstThreshold = 2, BurstWindowHours = 24 };

    private static ZohoSettings Zoho() => new()
    {
        Enabled = true,
        OrgId = "org",
        ClientId = "id",
        ClientSecret = "secret",
        RefreshToken = "refresh",
        ReuseTicketWithinHours = 24
    };

    private static BurstAlertService Service(TestEnvironment env, FakeZohoClient? zoho, FakeEmailSender? email,
        ZohoSettings? zohoSettings = null) =>
        new(env.Repository, new DiagnosticAnalysisService(env.Repository), zohoSettings ?? Zoho(), Alerts(), zoho, email);

    /// <summary>Processes a bundle and backdates its arrival, so a burst can be staged deterministically.</summary>
    private static async Task<DiagnosticFile> Arrive(TestEnvironment env, string zipName, string serial, double hoursAgo)
    {
        var bundle = await env.Processor.ProcessAsync(env.CreateZip(zipName, TestEnvironment.SampleBundle(serial: serial)));

        await using var context = env.CreateContext();
        var stored = context.DiagnosticFiles.Single(f => f.Id == bundle.Id);
        stored.ArrivedAtUtc = DateTime.UtcNow.AddHours(-hoursAgo);
        await context.SaveChangesAsync();

        bundle.ArrivedAtUtc = stored.ArrivedAtUtc;
        return bundle;
    }

    private static async Task<List<DiagnosticFileSummary>> Summaries(TestEnvironment env) =>
        (await env.Repository.GetAllAsync()).Select(DiagnosticFileSummary.FromEntity).ToList();

    [Fact]
    public async Task DoesNothingForASingleBundle()
    {
        using var env = new TestEnvironment();
        var zoho = new FakeZohoClient();
        var email = new FakeEmailSender();

        var bundle = await Arrive(env, "a.zip", "SN-1", 0);
        var outcome = await Service(env, zoho, email).HandleAsync(bundle, await Summaries(env));

        Assert.False(outcome.Triggered);
        Assert.Empty(zoho.Created);
        Assert.Empty(email.Sent);
    }

    [Fact]
    public async Task RaisesATicketAndEmailsOnTheSecondBundle()
    {
        using var env = new TestEnvironment();
        var zoho = new FakeZohoClient();
        var email = new FakeEmailSender();

        await Arrive(env, "a.zip", "SN-1", 3);
        var second = await Arrive(env, "b.zip", "SN-1", 0);

        var outcome = await Service(env, zoho, email).HandleAsync(second, await Summaries(env));

        Assert.True(outcome.Triggered);
        Assert.True(outcome.TicketWasCreated);
        Assert.Equal("T-501", outcome.TicketNumber);

        var created = Assert.Single(zoho.Created);
        Assert.Equal("Repeated diagnostics: SN-1 (CoffeeRoaster-5000) - Wellington Roasters Ltd", created.Subject);
        Assert.Contains("2 diagnostic files", created.Body);

        var sent = Assert.Single(email.Sent);
        Assert.Equal("[Diag alert] SN-1 - Wellington Roasters Ltd - 2 files in 24h", sent.Subject);
        Assert.Contains("Zoho ticket: #T-501", sent.Body);
    }

    [Fact]
    public async Task ReusesATicketRaisedForTheSameMachineToday()
    {
        using var env = new TestEnvironment();
        var zoho = new FakeZohoClient();
        var service = Service(env, zoho, new FakeEmailSender());

        await Arrive(env, "a.zip", "SN-1", 5);
        var second = await Arrive(env, "b.zip", "SN-1", 2);
        await service.HandleAsync(second, await Summaries(env));

        var third = await Arrive(env, "c.zip", "SN-1", 0);
        var outcome = await service.HandleAsync(third, await Summaries(env));

        // One ticket, with the later report added as a comment rather than a second ticket.
        Assert.Single(zoho.Created);
        Assert.False(outcome.TicketWasCreated);
        Assert.Equal("T-501", outcome.TicketNumber);
        var comment = Assert.Single(zoho.Comments);
        Assert.Equal("501", comment.TicketId);
        Assert.Contains("3 diagnostic files", comment.Comment);
    }

    [Fact]
    public async Task RaisesAFreshTicketWhenTheEarlierOneIsOutsideTheReuseWindow()
    {
        using var env = new TestEnvironment();
        var zoho = new FakeZohoClient();

        var settings = Zoho();
        settings.ReuseTicketWithinHours = 1;
        var service = Service(env, zoho, new FakeEmailSender(), settings);

        await Arrive(env, "a.zip", "SN-1", 5);
        var second = await Arrive(env, "b.zip", "SN-1", 4);
        await service.HandleAsync(second, await Summaries(env));

        // Age the recorded ticket so it falls outside the one-hour reuse window.
        await using (var context = env.CreateContext())
        {
            foreach (var row in context.DiagnosticFiles.Where(f => f.ZohoTicketCreatedUtc != null))
            {
                row.ZohoTicketCreatedUtc = DateTime.UtcNow.AddHours(-10);
            }
            await context.SaveChangesAsync();
        }

        var third = await Arrive(env, "c.zip", "SN-1", 0);
        var outcome = await service.HandleAsync(third, await Summaries(env));

        Assert.Equal(2, zoho.Created.Count);
        Assert.True(outcome.TicketWasCreated);
    }

    [Fact]
    public async Task DifferentMachinesGetTheirOwnTickets()
    {
        using var env = new TestEnvironment();
        var zoho = new FakeZohoClient();
        var service = Service(env, zoho, new FakeEmailSender());

        await Arrive(env, "a1.zip", "SN-1", 3);
        var firstMachine = await Arrive(env, "a2.zip", "SN-1", 0);
        await service.HandleAsync(firstMachine, await Summaries(env));

        await Arrive(env, "b1.zip", "SN-2", 3);
        var secondMachine = await Arrive(env, "b2.zip", "SN-2", 0);
        var outcome = await service.HandleAsync(secondMachine, await Summaries(env));

        Assert.Equal(2, zoho.Created.Count);
        Assert.True(outcome.TicketWasCreated);
    }

    [Fact]
    public async Task StillEmailsWhenZohoFails()
    {
        using var env = new TestEnvironment();
        var zoho = new FakeZohoClient { ThrowOnCreate = new ZohoException("Zoho refused to create ticket (401)") };
        var email = new FakeEmailSender();

        await Arrive(env, "a.zip", "SN-1", 3);
        var second = await Arrive(env, "b.zip", "SN-1", 0);

        var outcome = await Service(env, zoho, email).HandleAsync(second, await Summaries(env));

        Assert.True(outcome.Triggered);
        Assert.Null(outcome.TicketNumber);
        Assert.True(outcome.EmailSent);
        Assert.Contains(outcome.Problems, p => p.Contains("Zoho failed"));
    }

    [Fact]
    public async Task StillRaisesTheTicketWhenEmailFails()
    {
        using var env = new TestEnvironment();
        var zoho = new FakeZohoClient();
        var email = new FakeEmailSender { ThrowOnSend = new InvalidOperationException("bad mail password") };

        await Arrive(env, "a.zip", "SN-1", 3);
        var second = await Arrive(env, "b.zip", "SN-1", 0);

        var outcome = await Service(env, zoho, email).HandleAsync(second, await Summaries(env));

        Assert.True(outcome.TicketWasCreated);
        Assert.False(outcome.EmailSent);
        Assert.Contains(outcome.Problems, p => p.Contains("Email failed"));
    }

    [Fact]
    public async Task WorksWithZohoTurnedOff()
    {
        using var env = new TestEnvironment();
        var email = new FakeEmailSender();

        var settings = Zoho();
        settings.Enabled = false;

        await Arrive(env, "a.zip", "SN-1", 3);
        var second = await Arrive(env, "b.zip", "SN-1", 0);

        var outcome = await Service(env, new FakeZohoClient(), email, settings).HandleAsync(second, await Summaries(env));

        Assert.True(outcome.Triggered);
        Assert.Null(outcome.TicketNumber);
        Assert.True(outcome.EmailSent);
        Assert.Empty(outcome.Problems);
    }

    [Fact]
    public async Task RecordsThatTheAlertWentOut()
    {
        using var env = new TestEnvironment();

        await Arrive(env, "a.zip", "SN-1", 3);
        var second = await Arrive(env, "b.zip", "SN-1", 0);
        await Service(env, new FakeZohoClient(), new FakeEmailSender()).HandleAsync(second, await Summaries(env));

        await using var context = env.CreateContext();
        Assert.NotNull(context.DiagnosticFiles.Single(f => f.Id == second.Id).AlertSentUtc);
    }

    [Fact]
    public async Task TheAlertCarriesTheBurstContextAndEveryFileInIt()
    {
        using var env = new TestEnvironment();
        var zoho = new FakeZohoClient();

        await Arrive(env, "first.zip", "SN-1", 3);
        var second = await Arrive(env, "second.zip", "SN-1", 0);

        await Service(env, zoho, new FakeEmailSender()).HandleAsync(second, await Summaries(env));

        var body = Assert.Single(zoho.Created).Body;
        Assert.Contains("first.zip", body);
        Assert.Contains("second.zip", body);
        Assert.Contains("CoffeeRoaster-5000", body);
        Assert.Contains("Wellington Roasters Ltd", body);
        Assert.Contains("-- Raised automatically by Diagnostic File Monitor.", body);
    }

    [Fact]
    public async Task NoAnalysisRunsWhenThereIsNoBurst()
    {
        using var env = new TestEnvironment();

        var bundle = await Arrive(env, "a.zip", "SN-1", 0);

        // An analyser that throws if it is called at all, so reaching it would fail the test
        // rather than quietly wasting the work on every single bundle that arrives.
        var service = new BurstAlertService(
            env.Repository, new ThrowingAnalysisService(env), Zoho(), Alerts(),
            new FakeZohoClient(), new FakeEmailSender());

        var outcome = await service.HandleAsync(bundle, await Summaries(env));

        Assert.False(outcome.Triggered);
        Assert.Null(outcome.ReportText);
        Assert.Empty(outcome.Problems);
    }

    [Fact]
    public async Task TheAlertUsesWhatDiagnosticAnalysisServiceProduced()
    {
        using var env = new TestEnvironment();

        await Arrive(env, "a.zip", "SN-1", 3);
        var second = await Arrive(env, "b.zip", "SN-1", 0);

        var outcome = await Service(env, new FakeZohoClient(), new FakeEmailSender())
            .HandleAsync(second, await Summaries(env));

        // Byte for byte what pressing Analyse on that bundle would have produced.
        var expected = await new DiagnosticAnalysisService(env.Repository).AnalyseAsync(new[] { second.Id });

        Assert.Equal(expected, outcome.ReportText);
        Assert.Contains("DIAGNOSTIC ANALYSIS", outcome.ReportText);
    }

    [Fact]
    public async Task TheWholeReportReachesTheEmailBody()
    {
        using var env = new TestEnvironment();
        var email = new FakeEmailSender();

        await Arrive(env, "a.zip", "SN-1", 3);
        var second = await Arrive(env, "b.zip", "SN-1", 0);

        var outcome = await Service(env, new FakeZohoClient(), email).HandleAsync(second, await Summaries(env));

        Assert.Contains(outcome.ReportText!.TrimEnd(), Assert.Single(email.Sent).Body);
    }

    [Fact]
    public async Task TheWholeReportReachesANewZohoTicketDescription()
    {
        using var env = new TestEnvironment();
        var zoho = new FakeZohoClient();

        await Arrive(env, "a.zip", "SN-1", 3);
        var second = await Arrive(env, "b.zip", "SN-1", 0);

        var outcome = await Service(env, zoho, new FakeEmailSender()).HandleAsync(second, await Summaries(env));

        Assert.True(outcome.TicketWasCreated);
        Assert.Contains(outcome.ReportText!.TrimEnd(), Assert.Single(zoho.Created).Body);
    }

    [Fact]
    public async Task TheWholeReportReachesAReusedTicketAsAComment()
    {
        using var env = new TestEnvironment();
        var zoho = new FakeZohoClient();
        var service = Service(env, zoho, new FakeEmailSender());

        await Arrive(env, "a.zip", "SN-1", 5);
        var second = await Arrive(env, "b.zip", "SN-1", 2);
        await service.HandleAsync(second, await Summaries(env));

        var third = await Arrive(env, "c.zip", "SN-1", 0);
        var outcome = await service.HandleAsync(third, await Summaries(env));

        Assert.False(outcome.TicketWasCreated);
        Assert.Contains(outcome.ReportText!.TrimEnd(), Assert.Single(zoho.Comments).Comment);
    }

    [Fact]
    public async Task AFailedAnalysisLeavesTheAlertUnsentAndUnmarked()
    {
        using var env = new TestEnvironment();
        var zoho = new FakeZohoClient();
        var email = new FakeEmailSender();

        await Arrive(env, "a.zip", "SN-1", 3);
        var second = await Arrive(env, "b.zip", "SN-1", 0);

        // A repository that throws stands in for a database or disk failure mid-analysis.
        var service = new BurstAlertService(
            env.Repository, new ThrowingAnalysisService(env), Zoho(), Alerts(), zoho, email);

        var outcome = await service.HandleAsync(second, await Summaries(env));

        Assert.True(outcome.Triggered);
        Assert.Null(outcome.ReportText);
        Assert.Contains(outcome.Problems, p => p.Contains("Analysis failed"));
        Assert.Empty(zoho.Created);
        Assert.Empty(email.Sent);

        // Left unmarked so the next bundle from this machine tries again.
        await using var context = env.CreateContext();
        Assert.Null(context.DiagnosticFiles.Single(f => f.Id == second.Id).AlertSentUtc);
    }

    /// <summary>Stands in for an analysis that cannot complete - a missing extract folder, a locked file.</summary>
    private sealed class ThrowingAnalysisService : DiagnosticAnalysisService
    {
        public ThrowingAnalysisService(TestEnvironment env) : base(env.Repository)
        {
        }

        public override Task<string> AnalyseAsync(IEnumerable<int> ids, CancellationToken token = default) =>
            throw new IOException("the unpacked files have gone");
    }
}
