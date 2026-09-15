using DiagFileMonitor.Core.Knowledge;
using DiagFileMonitor.Core.Models;
using DiagFileMonitor.Core.Services;
using DiagFileMonitor.Core.SpidaLogs;

namespace DiagFileMonitor.Core.Reports;

/// <summary>A machine that is in scope for a report, whether or not it turned anything up.</summary>
public record ScannedMachine
{
    public string SerialNumber { get; init; } = string.Empty;
    public string MachineType { get; init; } = string.Empty;
    public string Site { get; init; } = string.Empty;
    public string AssetName { get; init; } = string.Empty;

    /// <summary>How many bundles were read for this machine inside the period.</summary>
    public int BundlesRead { get; init; }

    /// <summary>Set where the inventory and the machine's own log do not agree.</summary>
    public string? Disagreement { get; init; }
}

public class FleetScanResult
{
    public IReadOnlyList<FaultOccurrence> Occurrences { get; init; } = Array.Empty<FaultOccurrence>();

    /// <summary>Every machine read, including ones with nothing to report - they still appear in
    /// the tables and charts, because a comparison that quietly drops a machine is not one.</summary>
    public IReadOnlyList<ScannedMachine> Machines { get; init; } = Array.Empty<ScannedMachine>();

    /// <summary>Bundles that could not be read, with the reason.</summary>
    public IReadOnlyList<string> Skipped { get; init; } = Array.Empty<string>();

    /// <summary>Notes about ambiguous log selection inside a bundle.</summary>
    public IReadOnlyList<string> SelectionNotes { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Walks stored bundles and pulls out every fault occurrence, so a report can list them rather
/// than only count them.
/// <para>
/// It reuses the analysis the app already runs - the same parsers, the same checks, the same
/// confidence labels. Nothing here re-reads a log format for itself.
/// </para>
/// </summary>
public class FleetFaultScanner
{
    private readonly DiagFileRepository _repository;
    private readonly MachineInventory _inventory;

    public FleetFaultScanner(DiagFileRepository repository, MachineInventory? inventory = null)
    {
        _repository = repository;
        _inventory = inventory ?? MachineInventory.FromSupportRecords();
    }

    public async Task<FleetScanResult> ScanAsync(FleetScanRequest request, CancellationToken token = default)
    {
        var all = await _repository.GetAllAsync();
        return await Task.Run(() => Scan(all, request), token);
    }

    public FleetScanResult Scan(IEnumerable<DiagnosticFile> bundles, FleetScanRequest request)
    {
        var occurrences = new List<FaultOccurrence>();
        var skipped = new List<string>();
        var selectionNotes = new List<string>();
        var machines = new Dictionary<string, (ScannedMachine Machine, int Count)>(StringComparer.OrdinalIgnoreCase);

        foreach (var bundle in bundles.Where(request.Includes).OrderBy(b => b.ArrivedAtUtc))
        {
            var serial = (bundle.SerialNumber ?? string.Empty).Trim();
            if (serial.Length == 0) continue;

            var site = _inventory.SiteFor(serial, bundle.Customer);

            if (!machines.TryGetValue(serial, out var known))
            {
                var entry = _inventory.Find(serial);
                known = (new ScannedMachine
                {
                    SerialNumber = serial,
                    MachineType = bundle.MachineType ?? string.Empty,
                    Site = site,
                    AssetName = entry?.AssetName ?? bundle.MachineName ?? string.Empty,
                    Disagreement = _inventory.Disagreement(serial, bundle.MachineType)
                }, 0);
            }

            if (bundle.ExtractedPath is null || !Directory.Exists(bundle.ExtractedPath))
            {
                skipped.Add($"{bundle.OriginalFileName}: unpacked files are no longer on disk.");
                machines[serial] = known;
                continue;
            }

            var logs = BundleLogs.Read(bundle);
            foreach (var note in logs.SelectionNotes) selectionNotes.Add($"{bundle.OriginalFileName}: {note}");

            occurrences.AddRange(ReadBundle(bundle, logs, site, request));

            machines[serial] = (known.Machine with { }, known.Count + 1);
        }

        return new FleetScanResult
        {
            Occurrences = Deduplicate(occurrences),
            Machines = machines.Values
                .Select(m => new ScannedMachine
                {
                    SerialNumber = m.Machine.SerialNumber,
                    MachineType = m.Machine.MachineType,
                    Site = m.Machine.Site,
                    AssetName = m.Machine.AssetName,
                    Disagreement = m.Machine.Disagreement,
                    BundlesRead = m.Count
                })
                .OrderBy(m => m.SerialNumber, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            Skipped = skipped,
            SelectionNotes = selectionNotes
        };
    }

    private IEnumerable<FaultOccurrence> ReadBundle(
        DiagnosticFile bundle, BundleLogSet logs, string site, FleetScanRequest request)
    {
        var analyser = new SpidaLogAnalyser();
        var analysis = analyser.Analyse(logs.MachineLog, logs.ErrLog, logs.ChangeLog, bundle.ArrivedAtUtc);
        var knowledge = KnowledgeAnnotator.Annotate(
            analysis, logs.MachineLog, bundle.MachineType, bundle.SerialNumber, logs.MachineConfigPath);

        var steps = MachineCycles.ReadSteps(logs.MachineLog);
        var sessionDate = analysis.SessionDateUtc == default ? bundle.ArrivedAtUtc.Date : analysis.SessionDateUtc;
        var operatorReported = (bundle.SupportIssue ?? string.Empty).Trim();

        FaultOccurrence Base(FaultKind kind, TimeSpan? time) => new()
        {
            Kind = kind,
            WhenUtc = time is { } t ? sessionDate + t : bundle.ArrivedAtUtc,
            TimeIsApproximate = time is null,
            SerialNumber = (bundle.SerialNumber ?? string.Empty).Trim(),
            MachineType = bundle.MachineType ?? string.Empty,
            Site = site,
            Step = time is { } s ? StepAt(steps, s) : null,
            DiagnosticFileId = bundle.Id,
            BundleFileName = bundle.OriginalFileName,
            OperatorReported = operatorReported
        };

        foreach (var plate in knowledge.PlatePresentEvents)
        {
            if (plate.Verdict == PlatePresentVerdict.NormalClampRelease) continue;

            var kind = plate.Verdict == PlatePresentVerdict.SensorGlitch
                ? FaultKind.PlateSensorGlitch
                : FaultKind.PlateSensorUnclear;

            if (!request.Wants(kind)) continue;

            yield return Base(kind, plate.Time) with
            {
                Signal = $"PlatePresentSwitch {plate.Address} ({plate.Side.ToLowerInvariant()})",
                Detail = plate.Explanation,
                RecoveredAfter = plate.RecoveredAfter,
                // A one-sided drop with no output change is the documented glitch signature.
                // Anything that fits neither pattern is only ever a guess until someone looks.
                Confidence = plate.Verdict == PlatePresentVerdict.SensorGlitch
                    ? Confidence.Confirmed
                    : Confidence.Unconfirmed
            };
        }

        if (request.Wants(FaultKind.DriveFault))
        {
            foreach (var drive in knowledge.DriveFaults)
            {
                yield return Base(FaultKind.DriveFault, drive.LastSeen) with
                {
                    Signal = drive.Code.Code + drive.Where,
                    Detail = drive.Code.Meaning,
                    Confidence = Confidence.Confirmed
                };
            }
        }

        if (request.Wants(FaultKind.MotorNotConfirmed))
        {
            foreach (var motor in knowledge.MotorConfirm.Failures)
            {
                yield return Base(FaultKind.MotorNotConfirmed, motor.LastCommandedOn) with
                {
                    Signal = motor.Motor,
                    Detail = motor.ConfirmSentence,
                    RecoveredAfter = motor.GaveUpAfter,
                    Confidence = Confidence.Confirmed
                };
            }
        }

        if (request.Wants(FaultKind.KnownMachineFault))
        {
            foreach (var fault in knowledge.MatchedFaults)
            {
                yield return Base(FaultKind.KnownMachineFault, null) with
                {
                    Signal = fault.SeenAs.Length > 0 ? fault.SeenAs : fault.Known.Match,
                    Detail = fault.Known.Meaning,
                    Confidence = fault.Known.Confidence
                };
            }
        }

        if (request.Wants(FaultKind.SoftwareError))
        {
            foreach (var group in logs.ErrLog.GroupBy(e => e.Signature))
            {
                var latest = group.OrderByDescending(e => e.Timestamp).First();
                var raised = group.Count();

                yield return new FaultOccurrence
                {
                    Kind = FaultKind.SoftwareError,
                    WhenUtc = latest.Timestamp,
                    SerialNumber = (bundle.SerialNumber ?? string.Empty).Trim(),
                    MachineType = bundle.MachineType ?? string.Empty,
                    Site = site,
                    // Title is the product name on every entry, so it says nothing. The message
                    // is the signal, and the first frame inside SDN says where it came from.
                    Signal = latest.Text,
                    Detail = Where(latest) + (raised > 1 ? $" Raised {raised} times in this export." : string.Empty),
                    Confidence = Confidence.Confirmed,
                    DiagnosticFileId = bundle.Id,
                    BundleFileName = bundle.OriginalFileName,
                    OperatorReported = operatorReported
                };
            }
        }
    }

    /// <summary>Where in the software an error came from - the first frame that is Spida's own.</summary>
    private static string Where(ErrLogEntry entry)
    {
        var frame = entry.TopOfStack
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .FirstOrDefault(l => l.Contains("SDN.", StringComparison.Ordinal));

        if (frame is not null)
        {
            var at = frame.StartsWith("at ", StringComparison.Ordinal) ? frame[3..] : frame;
            return $"Raised in {at}.";
        }

        return string.IsNullOrWhiteSpace(entry.Method) ? string.Empty : $"Raised in {entry.Method}.";
    }

    /// <summary>
    /// Collapses the same fault reported by more than one bundle.
    /// <para>
    /// Every export carries the machine's recent history, not just the moment it was raised, so
    /// two bundles taken minutes apart carry the same ErrLog entries and the same recent faults.
    /// Counting both would double every figure in the report. Matching is on the machine, the
    /// kind, the signal and the moment it happened - the same event, described twice, not two
    /// events. Where a date-only occurrence would also match one with a real time, the timed one
    /// is kept, because it is the better record.
    /// </para>
    /// </summary>
    private static List<FaultOccurrence> Deduplicate(IEnumerable<FaultOccurrence> occurrences)
    {
        var kept = new Dictionary<string, FaultOccurrence>();

        foreach (var o in occurrences)
        {
            var key = string.Join('|',
                o.SerialNumber.ToUpperInvariant(),
                o.Kind,
                o.Signal,
                o.WhenUtc.ToString("yyyy-MM-dd"),
                o.TimeIsApproximate ? string.Empty : o.WhenUtc.ToString("HH:mm:ss"));

            var dateOnlyKey = string.Join('|',
                o.SerialNumber.ToUpperInvariant(), o.Kind, o.Signal, o.WhenUtc.ToString("yyyy-MM-dd"), string.Empty);

            if (o.TimeIsApproximate)
            {
                // A date-only row adds nothing next to one that knows the time.
                if (kept.Keys.Any(k => k.StartsWith(dateOnlyKey[..^1], StringComparison.Ordinal))) continue;
            }
            else
            {
                kept.Remove(dateOnlyKey);
            }

            kept.TryAdd(key, o);
        }

        return kept.Values.OrderByDescending(o => o.WhenUtc).ThenBy(o => o.Signal, StringComparer.Ordinal).ToList();
    }

    /// <summary>The step the machine was in at a given moment - the last one logged at or before it.</summary>
    private static int? StepAt(IReadOnlyList<StepReading> steps, TimeSpan time)
    {
        int? step = null;
        foreach (var reading in steps)
        {
            if (reading.Time > time) break;
            step = reading.Step;
        }
        return step;
    }
}
