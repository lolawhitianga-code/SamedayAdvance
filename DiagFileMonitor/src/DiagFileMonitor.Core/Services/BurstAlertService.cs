using DiagFileMonitor.Core.Models;

namespace DiagFileMonitor.Core.Services;

public interface IZohoDeskClient
{
    Task<ZohoTicket> CreateTicketAsync(string subject, string description, CancellationToken token = default);
    Task AddCommentAsync(string ticketId, string comment, CancellationToken token = default);
}

public class BurstAlertOutcome
{
    public bool Triggered { get; init; }
    public BurstResult? Burst { get; init; }
    public AnalysisReport? Report { get; init; }
    public string? TicketNumber { get; init; }
    public bool TicketWasCreated { get; init; }
    public bool EmailSent { get; init; }
    public List<string> Problems { get; } = new();

    public string Summary
    {
        get
        {
            if (!Triggered) return "No burst.";

            var parts = new List<string> { Burst!.Headline };

            if (TicketNumber is not null)
            {
                parts.Add(TicketWasCreated ? $"raised ticket #{TicketNumber}" : $"added to ticket #{TicketNumber}");
            }

            if (EmailSent) parts.Add("alert emailed");
            if (Problems.Count > 0) parts.Add($"{Problems.Count} step(s) failed - see logs");

            return string.Join("; ", parts) + ".";
        }
    }
}

/// <summary>
/// Reacts to a machine sending several bundles in a short window: analyses the latest one,
/// posts the report to a Zoho ticket (reusing a ticket raised recently for the same machine),
/// and emails the alert.
/// <para>
/// Every outbound step is best-effort. A Zoho outage or a bad mail password must not stop the
/// bundle being recorded, so failures are collected rather than thrown.
/// </para>
/// </summary>
public class BurstAlertService
{
    private readonly DiagFileRepository _repository;
    private readonly DiagnosticAnalyser _analyser;
    private readonly IZohoDeskClient? _zoho;
    private readonly IEmailAlertSender? _email;
    private readonly ZohoSettings _zohoSettings;
    private readonly AlertSettings _alertSettings;

    public BurstAlertService(
        DiagFileRepository repository,
        DiagnosticAnalyser analyser,
        ZohoSettings zohoSettings,
        AlertSettings alertSettings,
        IZohoDeskClient? zoho = null,
        IEmailAlertSender? email = null)
    {
        _repository = repository;
        _analyser = analyser;
        _zohoSettings = zohoSettings;
        _alertSettings = alertSettings;
        _zoho = zoho;
        _email = email;
    }

    public async Task<BurstAlertOutcome> HandleAsync(
        DiagnosticFile bundle,
        IEnumerable<DiagnosticFileSummary> allBundles,
        CancellationToken token = default)
    {
        var trigger = DiagnosticFileSummary.FromEntity(bundle);
        var burst = BurstDetector.Evaluate(allBundles, trigger, _alertSettings.BurstThreshold, _alertSettings.BurstWindowHours);

        if (!burst.IsBurst)
        {
            return new BurstAlertOutcome { Triggered = false, Burst = burst };
        }

        var baselines = await _repository.GetBaselinesAsync();
        var report = _analyser.Analyse(bundle, baselines);
        var body = AnalysisReportFormatter.Format(burst, report);

        var outcome = new TicketOutcome();
        if (_zoho is not null && _zohoSettings.IsConfigured)
        {
            outcome = await PostToZohoAsync(bundle, burst, report, body, token);
        }

        var emailSent = false;
        if (_email is not null)
        {
            try
            {
                var subject = AnalysisReportFormatter.Subject(burst, report);
                var withTicket = outcome.TicketNumber is null
                    ? body
                    : body + Environment.NewLine + $"Zoho ticket: #{outcome.TicketNumber}" + Environment.NewLine;

                await _email.SendAsync(subject, withTicket, token);
                emailSent = true;
            }
            catch (Exception ex)
            {
                SimpleLogger.Error("Could not send the burst alert email", ex);
                outcome.Problems.Add($"Email failed: {ex.Message}");
            }
        }

        await _repository.MarkAlertSentAsync(bundle.Id, DateTime.UtcNow);

        var result = new BurstAlertOutcome
        {
            Triggered = true,
            Burst = burst,
            Report = report,
            TicketNumber = outcome.TicketNumber,
            TicketWasCreated = outcome.Created,
            EmailSent = emailSent
        };

        result.Problems.AddRange(outcome.Problems);
        return result;
    }

    private sealed class TicketOutcome
    {
        public string? TicketNumber { get; set; }
        public bool Created { get; set; }
        public List<string> Problems { get; } = new();
    }

    private async Task<TicketOutcome> PostToZohoAsync(
        DiagnosticFile bundle, BurstResult burst, AnalysisReport report, string body, CancellationToken token)
    {
        var outcome = new TicketOutcome();

        try
        {
            var cutoff = DateTime.UtcNow.AddHours(-_zohoSettings.ReuseTicketWithinHours);
            var existing = await _repository.FindRecentTicketForSerialAsync(bundle.SerialNumber ?? string.Empty, cutoff);

            string ticketId;
            if (existing?.ZohoTicketId is { } reusableId)
            {
                ticketId = reusableId;
                outcome.TicketNumber = existing.ZohoTicketNumber;
                await _repository.SaveTicketLinkAsync(bundle.Id, ticketId, existing.ZohoTicketNumber ?? string.Empty,
                    existing.ZohoTicketCreatedUtc ?? DateTime.UtcNow);
            }
            else
            {
                var ticket = await _zoho!.CreateTicketAsync(
                    AnalysisReportFormatter.TicketSubject(burst, report), body, token);

                ticketId = ticket.Id;
                outcome.TicketNumber = ticket.TicketNumber;
                outcome.Created = true;
                await _repository.SaveTicketLinkAsync(bundle.Id, ticket.Id, ticket.TicketNumber, DateTime.UtcNow);
            }

            // A new ticket already carries the report as its description; a reused one needs it as a note.
            if (!outcome.Created)
            {
                await _zoho!.AddCommentAsync(ticketId, body, token);
            }
        }
        catch (Exception ex)
        {
            SimpleLogger.Error("Could not post the analysis to Zoho", ex);
            outcome.Problems.Add($"Zoho failed: {ex.Message}");
        }

        return outcome;
    }
}
