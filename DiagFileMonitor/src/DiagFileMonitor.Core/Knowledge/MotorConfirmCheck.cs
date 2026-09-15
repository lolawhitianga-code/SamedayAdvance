using System.Text.RegularExpressions;
using DiagFileMonitor.Core.SpidaLogs;

namespace DiagFileMonitor.Core.Knowledge;

public enum MotorConfirmVerdict
{
    /// <summary>The motor reported itself running after it was told to.</summary>
    Confirmed,

    /// <summary>It was told to run and the confirmation did not come.</summary>
    NotConfirmed,

    /// <summary>The command was withdrawn before the motor had a chance - an aborted step.</summary>
    AbortedBeforeItCould,

    /// <summary>Nothing in this file says either way.</summary>
    Unknown
}

/// <summary>What a motor's run command and its confirmation input did in this log.</summary>
public class MotorConfirmStatus
{
    public string Motor { get; init; } = string.Empty;
    public string OutputTag { get; init; } = string.Empty;
    public string ConfirmTag { get; init; } = string.Empty;

    public TimeSpan? LastCommandedOn { get; init; }
    public TimeSpan? LastCommandedOff { get; init; }

    /// <summary>The last time the confirmation was seen reading 1 - the motor running.</summary>
    public TimeSpan? ConfirmLastOn { get; init; }

    /// <summary>The last time the confirmation was seen reading 0 - the motor not running.</summary>
    public TimeSpan? ConfirmLastOff { get; init; }

    /// <summary>False where the confirmation never changed in this file, so its state is unknown.</summary>
    public bool ConfirmSeenInLog { get; init; }

    /// <summary>A step condition the machine logged while it waited, in its own words.</summary>
    public string? MachineWaitedFor { get; init; }

    public MotorConfirmVerdict Verdict { get; init; }

    public TimeSpan? GaveUpAfter => LastCommandedOn is { } on && LastCommandedOff is { } off && off > on
        ? off - on
        : null;

    /// <summary>The plain answer to "did the confirmation come on?".</summary>
    public string ConfirmSentence
    {
        get
        {
            if (!ConfirmSeenInLog)
            {
                return $"{ConfirmTag} does not appear anywhere in this file. The log records changes "
                       + "only, so either it never moved or it was never made in the first place.";
            }

            var parts = new List<string>();

            parts.Add(ConfirmLastOn is { } on
                ? $"{ConfirmTag} last read 1 - running - at {on:hh\\:mm\\:ss}"
                : $"{ConfirmTag} was never seen reading 1 in this file");

            parts.Add(ConfirmLastOff is { } off
                ? $"and last read 0 - not running - at {off:hh\\:mm\\:ss}"
                : "and was never seen reading 0");

            return string.Join(" ", parts) + ".";
        }
    }

    public string Summary => Verdict switch
    {
        MotorConfirmVerdict.NotConfirmed =>
            $"{Motor} was told to run at {LastCommandedOn:hh\\:mm\\:ss} and did not report back that "
            + "it was running"
            + (GaveUpAfter is { } gave ? $" - the command was dropped again {gave.TotalSeconds:0.#}s later" : string.Empty)
            + ".",
        MotorConfirmVerdict.Confirmed =>
            $"{Motor} was told to run at {LastCommandedOn:hh\\:mm\\:ss} and confirmed running.",
        MotorConfirmVerdict.AbortedBeforeItCould =>
            $"{Motor} was told to run at {LastCommandedOn:hh\\:mm\\:ss} and the command was withdrawn "
            + $"{GaveUpAfter?.TotalSeconds ?? 0:0.#}s later, before it had a chance to report back. "
            + "That is the step being aborted, not the motor failing.",
        _ =>
            $"{Motor} was told to run at {LastCommandedOn:hh\\:mm\\:ss} and this file does not say "
            + "whether it ran."
    };
}

public class MotorConfirmFindings
{
    /// <summary>Every motor that was commanded on, whatever its confirmation did.</summary>
    public IReadOnlyList<MotorConfirmStatus> Statuses { get; init; } = Array.Empty<MotorConfirmStatus>();

    public IEnumerable<MotorConfirmStatus> Failures =>
        Statuses.Where(s => s.Verdict == MotorConfirmVerdict.NotConfirmed);

    public IEnumerable<MotorConfirmStatus> Healthy =>
        Statuses.Where(s => s.Verdict == MotorConfirmVerdict.Confirmed);

    public bool Any => Statuses.Count > 0;
    public bool AnyFailed => Failures.Any();
}

/// <summary>
/// Checks that a motor told to run actually reported back that it was running, and says what its
/// confirmation input did either way.
/// <para>
/// Spida names these by convention: an output <c>IO-SawMotor</c> is answered by an input
/// <c>SawMotorConfirm</c>. Motors are found from the outputs, so one whose confirmation never
/// changes in a short export is still reported - "it does not appear in this file" is the answer
/// to the question, not a reason to say nothing.
/// </para>
/// </summary>
public static class MotorConfirmCheck
{
    /// <summary>How long to give a motor to report itself running before calling it a failure.</summary>
    private static readonly TimeSpan ConfirmWindow = TimeSpan.FromSeconds(10);

    private static readonly Regex MotorOutput = new(@"^IO-(?<motor>.*Motor.*|.*Conveyor.*)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ConfirmInput = new(@"^(?<motor>.+)Confirm$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex InputValue = new(@"Changed\s*to\s*(?<value>\d+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static MotorConfirmFindings Check(IReadOnlyList<MachineLogEntry> machineLog)
    {
        var motors = FindMotors(machineLog);
        if (motors.Count == 0) return new MotorConfirmFindings();

        var statuses = new List<MotorConfirmStatus>();

        foreach (var motor in motors)
        {
            var commands = machineLog
                .Where(e => e.Category == MachineLogCategory.OutputChange)
                .Where(e => e.Tag.Equals(motor, StringComparison.OrdinalIgnoreCase)
                            || e.Tag.StartsWith($"IO-{motor}", StringComparison.OrdinalIgnoreCase))
                .Select(e => (e.Time, e.Tag, On: e.Description.Contains("Set On", StringComparison.OrdinalIgnoreCase)))
                .OrderBy(c => c.Time)
                .ToList();

            var lastOn = commands.LastOrDefault(c => c.On);
            if (!commands.Any(c => c.On)) continue;

            var changes = machineLog
                .Where(e => e.Category == MachineLogCategory.InputChange)
                .Where(e => e.Tag.Equals($"{motor}Confirm", StringComparison.OrdinalIgnoreCase))
                .Select(e => (e.Time, Value: ReadValue(e.Description)))
                .Where(c => c.Value is not null)
                .Select(c => (c.Time, Value: c.Value!.Value))
                .OrderBy(c => c.Time)
                .ToList();

            var waited = FindWaitLine(machineLog, motor, lastOn.Time);

            // Without a confirmation input or the machine saying it is waiting, there is nothing
            // to report either way - an extractor fan with no feedback wired is not a finding.
            if (changes.Count == 0 && waited is null) continue;
            var droppedAfter = commands
                .Where(c => !c.On && c.Time > lastOn.Time)
                .Select(c => (TimeSpan?)c.Time)
                .FirstOrDefault();

            statuses.Add(new MotorConfirmStatus
            {
                Motor = motor,
                OutputTag = lastOn.Tag,
                ConfirmTag = $"{motor}Confirm",
                LastCommandedOn = lastOn.Time,
                LastCommandedOff = droppedAfter,
                ConfirmSeenInLog = changes.Count > 0,
                ConfirmLastOn = changes.LastOrDefault(c => c.Value == 1) is { Value: 1 } on ? on.Time : null,
                ConfirmLastOff = changes.LastOrDefault(c => c.Value == 0) is { Value: 0 } off ? off.Time : null,
                MachineWaitedFor = waited,
                Verdict = Judge(changes, lastOn.Time, droppedAfter, waited)
            });
        }

        return new MotorConfirmFindings
        {
            // Most recent command first: the file is taken minutes after the problem.
            Statuses = statuses.OrderByDescending(s => s.Verdict == MotorConfirmVerdict.NotConfirmed)
                .ThenByDescending(s => s.LastCommandedOn)
                .ToList()
        };
    }

    /// <summary>
    /// Motors are found from their outputs rather than from their confirmations, so a motor whose
    /// confirmation never changes in a short export is still checked. A confirmation input with no
    /// matching output is picked up too, in case the output is named differently.
    /// </summary>
    private static List<string> FindMotors(IReadOnlyList<MachineLogEntry> machineLog)
    {
        var fromOutputs = machineLog
            .Where(e => e.Category == MachineLogCategory.OutputChange)
            .Select(e => MotorOutput.Match(e.Tag))
            .Where(m => m.Success)
            .Select(m => m.Groups["motor"].Value);

        var fromConfirms = machineLog
            .Where(e => e.Category == MachineLogCategory.InputChange)
            .Select(e => ConfirmInput.Match(e.Tag))
            .Where(m => m.Success)
            .Select(m => m.Groups["motor"].Value);

        var confirmed = fromConfirms.ToHashSet(StringComparer.OrdinalIgnoreCase);

        return fromOutputs
            .Concat(confirmed)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            // IO-WasteMotorFwd and IO-WasteMotorRev are one motor with a direction, answered by a
            // single WasteMotorConfirm. Without this the same motor is reported three times, twice
            // of them looking for a confirmation that was never going to exist.
            .Where(motor => !confirmed.Any(c =>
                c.Length < motor.Length && motor.StartsWith(c, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(m => m, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// The log records changes only, so a confirmation already reading 1 and not changing counts
    /// as confirmed - the state before the command matters as much as what follows it.
    /// </summary>
    private static MotorConfirmVerdict Judge(
        IReadOnlyList<(TimeSpan Time, int Value)> changes,
        TimeSpan commandedOn,
        TimeSpan? droppedAfter,
        string? waited)
    {
        var within = changes.Where(c => c.Time > commandedOn && c.Time <= commandedOn + ConfirmWindow).ToList();
        var before = changes.Where(c => c.Time <= commandedOn).ToList();

        if (within.Any(c => c.Value == 1)) return MotorConfirmVerdict.Confirmed;
        if (before.Count > 0 && before[^1].Value == 1 && within.All(c => c.Value != 0))
        {
            return MotorConfirmVerdict.Confirmed;
        }

        // The machine saying it is waiting for this motor to run settles it on its own.
        if (waited is not null) return MotorConfirmVerdict.NotConfirmed;
        if (within.Any(c => c.Value == 0)) return MotorConfirmVerdict.NotConfirmed;
        if (before.Count > 0 && before[^1].Value == 0 && droppedAfter is null)
        {
            return MotorConfirmVerdict.NotConfirmed;
        }

        // When one motor fails the machine aborts the step and drops every output at once, so its
        // companions look unconfirmed too. That is the abort, not a second broken motor.
        if (droppedAfter is { } off && off - commandedOn < ConfirmWindow)
        {
            return MotorConfirmVerdict.AbortedBeforeItCould;
        }

        return changes.Count == 0 ? MotorConfirmVerdict.Unknown : MotorConfirmVerdict.NotConfirmed;
    }

    /// <summary>
    /// A step condition logged while the machine waited, quoted back in its own words. It has to
    /// name this motor: "Waiting for Saw Blade Running" belongs to the saw, not to whatever else
    /// was switched on in the same instant.
    /// </summary>
    private static string? FindWaitLine(
        IReadOnlyList<MachineLogEntry> machineLog, string motor, TimeSpan commandedOn)
    {
        var stems = Regex.Matches(motor, @"[A-Z][a-z]+|[A-Z]+(?![a-z])")
            .Select(m => m.Value)
            .Where(word => word.Length > 2)
            .ToList();

        if (stems.Count == 0) stems.Add(motor);

        return machineLog
            .Where(e => e.Category == MachineLogCategory.Other)
            .Where(e => e.Time >= commandedOn && e.Time <= commandedOn + ConfirmWindow)
            .Where(e => e.Description.Contains("Waiting for", StringComparison.OrdinalIgnoreCase))
            .Where(e => e.Description.Contains("Running", StringComparison.OrdinalIgnoreCase))
            .Where(e => stems.Any(stem => e.Description.Contains(stem, StringComparison.OrdinalIgnoreCase)))
            .Select(e => e.Description.Trim())
            .FirstOrDefault();
    }

    private static int? ReadValue(string description)
    {
        var match = InputValue.Match(description);
        return match.Success ? int.Parse(match.Groups["value"].Value) : null;
    }
}
