namespace DiagFileMonitor.Core.Models;

/// <summary>
/// What one machine has sent over time. Answers the question support actually asks when a
/// customer rings twice: has this unit been here before, and what changed since?
/// </summary>
public class MachineHistory
{
    public string SerialNumber { get; init; } = string.Empty;
    public int BundleCount { get; init; }
    public int ErrorCount { get; init; }
    public DateTime? FirstSeenLocal { get; init; }
    public DateTime? LastSeenLocal { get; init; }
    public IReadOnlyList<string> Versions { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Customers { get; init; } = Array.Empty<string>();

    public static MachineHistory For(IEnumerable<DiagnosticFileSummary> files, string serialNumber)
    {
        var forMachine = files
            .Where(f => string.Equals(f.SerialNumber, serialNumber, StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f.ArrivedAtUtc)
            .ToList();

        return new MachineHistory
        {
            SerialNumber = serialNumber,
            BundleCount = forMachine.Count,
            ErrorCount = forMachine.Count(f => string.Equals(f.Status, nameof(ProcessingStatus.Error), StringComparison.OrdinalIgnoreCase)),
            FirstSeenLocal = forMachine.Count == 0 ? null : forMachine[0].ArrivedAtLocal,
            LastSeenLocal = forMachine.Count == 0 ? null : forMachine[^1].ArrivedAtLocal,
            Versions = forMachine
                .Select(f => f.Version)
                .Where(v => v != DiagnosticFileSummary.Unknown)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            Customers = forMachine
                .Select(f => f.Customer)
                .Where(c => c != DiagnosticFileSummary.Unknown)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList()
        };
    }

    public string Headline
    {
        get
        {
            if (BundleCount == 0) return $"No history for {SerialNumber}.";

            var parts = new List<string>
            {
                $"{SerialNumber}: {BundleCount} bundle(s)"
            };

            if (FirstSeenLocal is { } first && LastSeenLocal is { } last)
            {
                parts.Add(first.Date == last.Date
                    ? $"all on {first:yyyy-MM-dd}"
                    : $"{first:yyyy-MM-dd} to {last:yyyy-MM-dd}");
            }

            if (Versions.Count > 0)
            {
                parts.Add(Versions.Count == 1
                    ? $"version {Versions[0]}"
                    : $"versions {string.Join(" -> ", Versions)}");
            }

            if (ErrorCount > 0) parts.Add($"{ErrorCount} failed");
            if (Customers.Count > 1) parts.Add($"{Customers.Count} customers");

            return string.Join(" | ", parts);
        }
    }
}
