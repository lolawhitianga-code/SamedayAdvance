using System.Text;
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

    /// <summary>The analysis that went out, exactly as Zoho and the email received it.</summary>
    public string? ReportText { get; init; }

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
/// The analysis comes from <see cref="DiagnosticAnalysisService"/> - the same one behind the
/// dashboard's Analyse button, which is the only analysis path checked against real Spida
/// exports. An earlier version used a separate analyser whose line pattern matched no real
/// machine log, so an automated alert would have carried an empty analysis to a customer ticket.
/// </para>
/// <para>
/// Every outbound step is best-effort. A Zoho outage or a bad mail password must not stop the
/// bundle being recorded, so failures are collected rather than thrown. The analysis itself is
/// not best-effort: without it there is nothing worth sending, so the alert is left unsent and
/// unmarked so it can be retried.
/// </para>
/// </summary>
public class BurstAlertService
{
    private readonly DiagFileRepository _repository;
    private readonly DiagnosticAnalysisService _analysisService;
    private readonly IZohoDeskClient? _zoho;
    private readonly IEmailAlertSender? _email;
    private readonly ZohoSettings _zohoSettings;
    private readonly AlertSettings _alertSettings;

    public BurstAlertService(
        DiagFileRepository repository,
        DiagnosticAnalysisService analysisService,
        ZohoSettings zohoSettings,
        AlertSettings alertSettings,
        IZohoDeskClient? zoho = null,
        IEmailAlertSender? email = null)
    {
        _repository = repository;
        _analysisService = analysisService;
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

        string reportText;
        try
        {
            reportText = await _analysisService.AnalyseAsync(new[] { bundle.Id }, token);
        }
        catch (Exception ex)
        {
            // Nothing worth sending without the analysis. Leaving AlertSentUtc unset means the
            // next bundle from this machine tries again rather than the burst going unnoticed.
            SimpleLogger.Error("Could not analyse the bundle for a burst alert", ex);

            var failed = new BurstAlertOutcome { Triggered = true, Burst = burst };
            failed.Problems.Add($"Analysis failed: {ex.Message}");
            return failed;
        }

        var body = FormatBody(burst, trigger, reportText);

        var outcome = new TicketOutcome();
        if (_zoho is not null && _zohoSettings.IsConfigured)
        {
            outcome = await PostToZohoAsync(bundle, TicketSubject(burst, trigger), body, token);
        }

        var emailSent = false;
        if (_email is not null)
        {
            try
            {
                var withTicket = outcome.TicketNumber is null
                    ? body
                    : body + Environment.NewLine + $"Zoho ticket: #{outcome.TicketNumber}" + Environment.NewLine;

                await _email.SendAsync(EmailSubject(burst, trigger), withTicket, token);
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
            ReportText = reportText,
            TicketNumber = outcome.TicketNumber,
            TicketWasCreated = outcome.Created,
            EmailSent = emailSent
        };

        result.Problems.AddRange(outcome.Problems);
        return result;
    }

    /// <summary>A serial with nothing in it reads as "(unknown)" rather than as a blank gap.</summary>
    private static string Display(string? value) =>
        string.IsNullOrWhiteSpace(value) ? DiagnosticFileSummary.Unknown : value;

    internal static string EmailSubject(BurstResult burst, DiagnosticFileSummary trigger) =>
        $"[Diag alert] {Display(burst.SerialNumber)} - {trigger.Customer} - "
        + $"{burst.BundleCount} files in {burst.WindowHours}h";

    internal static string TicketSubject(BurstResult burst, DiagnosticFileSummary trigger) =>
        $"Repeated diagnostics: {Display(burst.SerialNumber)} ({trigger.MachineType}) - {trigger.Customer}";

    /// <summary>
    /// The burst context, then the full analysis verbatim. The analysis is not trimmed or
    /// summarised: it is what a support person would have read had they pressed Analyse
    /// themselves, and the whole point of the alert is that nobody did.
    /// </summary>
    internal static string FormatBody(BurstResult burst, DiagnosticFileSummary trigger, string reportText)
    {
        var text = new StringBuilder();

        text.AppendLine(burst.Headline + ".");
        text.AppendLine();
        text.AppendLine($"Machine type: {trigger.MachineType}");
        text.AppendLine($"Serial:       {Display(burst.SerialNumber)}");
        text.AppendLine($"Customer:     {trigger.Customer}");
        text.AppendLine($"Latest file:  {trigger.OriginalFileName} ({trigger.ArrivedDisplay})");
        text.AppendLine();

        text.AppendLine("Files in this burst:");
        foreach (var file in burst.Bundles)
        {
            text.AppendLine($"  - {file.ArrivedDisplay}  {file.OriginalFileName}  [{file.Status}]");
        }

        text.AppendLine();
        text.AppendLine(reportText.TrimEnd());
        text.AppendLine();
        text.AppendLine("-- Raised automatically by Diagnostic File Monitor.");

        return text.ToString();
    }

    private sealed class TicketOutcome
    {
        public string? TicketNumber { get; set; }
        public bool Created { get; set; }
        public List<string> Problems { get; } = new();
    }

    private async Task<TicketOutcome> PostToZohoAsync(
        DiagnosticFile bundle, string ticketSubject, string body, CancellationToken token)
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
                var ticket = await _zoho!.CreateTicketAsync(ticketSubject, body, token);

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
