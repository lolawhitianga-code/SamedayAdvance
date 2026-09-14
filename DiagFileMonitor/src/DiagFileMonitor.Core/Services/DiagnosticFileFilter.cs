using DiagFileMonitor.Core.Models;

namespace DiagFileMonitor.Core.Services;

public class FilterCriteria
{
    public string SearchText { get; set; } = string.Empty;
    public string Status { get; set; } = DiagnosticFileFilter.AnyStatus;

    /// <summary>Exact serial match, set when drilling into one machine's history.</summary>
    public string? SerialNumber { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public bool BaselinesOnly { get; set; }

    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(SearchText)
        && (string.IsNullOrEmpty(Status) || Status == DiagnosticFileFilter.AnyStatus)
        && FromDate is null
        && ToDate is null
        && string.IsNullOrEmpty(SerialNumber)
        && !BaselinesOnly;
}

/// <summary>Decides whether a dashboard row survives the current search box / status / date filters.</summary>
public static class DiagnosticFileFilter
{
    public const string AnyStatus = "All";

    public static bool Matches(DiagnosticFileSummary row, FilterCriteria criteria)
    {
        if (!string.IsNullOrEmpty(criteria.SerialNumber)
            && !string.Equals(row.SerialNumber, criteria.SerialNumber, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (criteria.BaselinesOnly && !row.IsBaseline) return false;

        if (!string.IsNullOrEmpty(criteria.Status) && criteria.Status != AnyStatus
            && !string.Equals(row.Status, criteria.Status, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var arrivedDate = row.ArrivedAtLocal.Date;
        if (criteria.FromDate is { } from && arrivedDate < from.Date) return false;
        if (criteria.ToDate is { } to && arrivedDate > to.Date) return false;

        if (string.IsNullOrWhiteSpace(criteria.SearchText)) return true;

        // Every whitespace-separated term must appear somewhere, so "SN-001 wellington" narrows rather than widens.
        var haystack = $"{row.SerialNumber} {row.Customer} {row.OriginalFileName} {row.MachineType} {row.MachineName} "
                       + $"{row.SiteLocation} {row.SoftwareName} {row.Version} {row.TicketNumber} {row.Notes} "
                       + $"{row.SupportPanel} {row.SupportMembers} {row.SupportIssue}";
        var terms = criteria.SearchText.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return terms.All(term => haystack.Contains(term, StringComparison.OrdinalIgnoreCase));
    }
}
