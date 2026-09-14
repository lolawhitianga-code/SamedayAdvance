namespace DiagFileMonitor.Core.Models;

/// <summary>Headline counts shown above the dashboard grid.</summary>
public class DashboardStats
{
    public const string None = "-";

    public int Total { get; init; }
    public int ArrivedToday { get; init; }
    public int ArrivedLast7Days { get; init; }
    public int ErrorCount { get; init; }
    public int DistinctMachines { get; init; }
    public string TopCustomerLast7Days { get; init; } = None;

    public static DashboardStats Empty { get; } = new();

    public static DashboardStats Calculate(IEnumerable<DiagnosticFileSummary> files, DateTime nowLocal)
    {
        var all = files.ToList();
        var today = nowLocal.Date;
        var weekStart = today.AddDays(-6); // today plus the six days before it

        var lastWeek = all.Where(f => f.ArrivedAtLocal.Date >= weekStart && f.ArrivedAtLocal.Date <= today).ToList();

        var topCustomer = lastWeek
            .Where(f => f.Customer != DiagnosticFileSummary.Unknown)
            .GroupBy(f => f.Customer)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        return new DashboardStats
        {
            Total = all.Count,
            ArrivedToday = all.Count(f => f.ArrivedAtLocal.Date == today),
            ArrivedLast7Days = lastWeek.Count,
            ErrorCount = all.Count(f => string.Equals(f.Status, nameof(ProcessingStatus.Error), StringComparison.OrdinalIgnoreCase)),
            DistinctMachines = all
                .Where(f => f.SerialNumber != DiagnosticFileSummary.Unknown)
                .Select(f => f.SerialNumber)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count(),
            TopCustomerLast7Days = topCustomer is null ? None : $"{topCustomer.Key} ({topCustomer.Count()})"
        };
    }
}
