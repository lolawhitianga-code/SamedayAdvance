using DiagFileMonitor.Core.Models;

namespace DiagFileMonitor.Core.Reports;

/// <summary>
/// What a report covers. Scope is stated rather than assumed - a report defaults to what it was
/// asked for, not to every bundle in the database.
/// </summary>
public class FleetScanRequest
{
    /// <summary>Only bundles that arrived on or after this. Null means no lower bound.</summary>
    public DateTime? FromUtc { get; init; }

    /// <summary>Only bundles that arrived before this. Null means no upper bound.</summary>
    public DateTime? ToUtc { get; init; }

    /// <summary>Serial numbers to include. Empty means every machine that has a bundle.</summary>
    public IReadOnlyCollection<string> Serials { get; init; } = Array.Empty<string>();

    /// <summary>Machine types to include, e.g. RakingWallExtruderV3DG. Empty means all.</summary>
    public IReadOnlyCollection<string> MachineTypes { get; init; } = Array.Empty<string>();

    /// <summary>Fault kinds to collect. Empty means all of them.</summary>
    public IReadOnlyCollection<FaultKind> Kinds { get; init; } = Array.Empty<FaultKind>();

    public bool Wants(FaultKind kind) => Kinds.Count == 0 || Kinds.Contains(kind);

    public bool Includes(DiagnosticFile bundle)
    {
        if (bundle.Status != ProcessingStatus.Processed) return false;
        if (FromUtc is { } from && bundle.ArrivedAtUtc < from) return false;
        if (ToUtc is { } to && bundle.ArrivedAtUtc >= to) return false;

        if (Serials.Count > 0
            && !Serials.Contains((bundle.SerialNumber ?? string.Empty).Trim(), StringComparer.OrdinalIgnoreCase))
            return false;

        if (MachineTypes.Count > 0
            && !MachineTypes.Contains((bundle.MachineType ?? string.Empty).Trim(), StringComparer.OrdinalIgnoreCase))
            return false;

        return true;
    }

    /// <summary>The period as it should read in a report header.</summary>
    public ReportPeriod PeriodFor(IReadOnlyList<DiagnosticFile> scanned, string label)
    {
        var from = FromUtc ?? (scanned.Count > 0 ? scanned.Min(b => b.ArrivedAtUtc) : DateTime.UtcNow);
        var to = ToUtc ?? (scanned.Count > 0 ? scanned.Max(b => b.ArrivedAtUtc) : DateTime.UtcNow);
        return new ReportPeriod(from, to, label);
    }

    /// <summary>The last <paramref name="days"/> days up to now.</summary>
    public static FleetScanRequest LastDays(int days, params FaultKind[] kinds) => new()
    {
        FromUtc = DateTime.UtcNow.Date.AddDays(-days),
        Kinds = kinds
    };
}
