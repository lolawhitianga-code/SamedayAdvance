using System.Text.RegularExpressions;
using DiagFileMonitor.Core.SpidaLogs;

namespace DiagFileMonitor.Core.Knowledge;

/// <summary>One time a motor was told to run and did not report back that it was running.</summary>
public class MotorConfirmFailure
{
    /// <summary>The motor's plain name, e.g. SawMotor.</summary>
    public string Motor { get; init; } = string.Empty;

    public string OutputTag { get; init; } = string.Empty;
    public string ConfirmTag { get; init; } = string.Empty;

    public TimeSpan CommandedOn { get; init; }

    /// <summary>When the confirmation was last seen reading 0, where it was seen at all.</summary>
    public TimeSpan? ConfirmDroppedAt { get; init; }

    /// <summary>When the command was dropped again - the machine giving up.</summary>
    public TimeSpan? OutputDroppedAt { get; init; }

    /// <summary>A step condition the machine logged while it waited, in its own words.</summary>
    public string? MachineWaitedFor { get; init; }

    public TimeSpan? GaveUpAfter => OutputDroppedAt is { } off ? off - CommandedOn : null;

    public string Summary
    {
        get
        {
            var text = $"{Motor} was told to run at {CommandedOn:hh\\:mm\\:ss} and {ConfirmTag} never "
                       + "came on to say it was running";

            if (ConfirmDroppedAt is { } dropped) text += $" - it read 0 from {dropped:hh\\:mm\\:ss}";
            if (GaveUpAfter is { } gave) text += $", and the command was dropped again {gave.TotalSeconds:0.#}s later";

            return text + ".";
        }
    }
}

public class MotorConfirmFindings
{
    public IReadOnlyList<MotorConfirmFailure> Failures { get; init; } = Array.Empty<MotorConfirmFailure>();

    /// <summary>Motors that have a confirmation input, whether or not they misbehaved.</summary>
    public IReadOnlyList<string> MotorsChecked { get; init; } = Array.Empty<string>();

    /// <summary>Motors whose confirmation followed the command every time, for comparison.</summary>
    public IReadOnlyList<string> Healthy { get; init; } = Array.Empty<string>();

    public bool Any => Failures.Count > 0;
}

/// <summary>
/// Checks that a motor told to run actually reported back that it was running.
/// <para>
/// Spida names these by convention: an output <c>IO-SawMotor</c> is answered by an input
/// <c>SawMotorConfirm</c>. The pairs are discovered from the log rather than listed here, so a
/// machine with motors we have never seen is still checked.
/// </para>
/// <para>
/// This is the difference between "the machine was asked to cut" and "the blade was turning".
/// On M20421 the saw output came on, the confirmation sat at 0, the machine logged "Waiting for
/// Saw Blade Running" and dropped the command 5.5 seconds later - which is exactly what the
/// operator wrote in the support file.
/// </para>
/// </summary>
public static class MotorConfirmCheck
{
    /// <summary>How long to give a motor to report itself running before calling it a failure.</summary>
    private static readonly TimeSpan ConfirmWindow = TimeSpan.FromSeconds(10);

    private static readonly Regex ConfirmTag = new(@"^(?<motor>.+)Confirm$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex InputValue = new(@"Changed\s*to\s*(?<value>\d+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static MotorConfirmFindings Check(IReadOnlyList<MachineLogEntry> machineLog)
    {
        var confirms = machineLog
            .Where(e => e.Category == MachineLogCategory.InputChange)
            .Select(e => (Entry: e, Match: ConfirmTag.Match(e.Tag)))
            .Where(pair => pair.Match.Success)
            .GroupBy(pair => pair.Match.Groups["motor"].Value, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (confirms.Count == 0) return new MotorConfirmFindings();

        var failures = new List<MotorConfirmFailure>();
        var checkedMotors = new List<string>();
        var healthy = new List<string>();

        foreach (var motor in confirms)
        {
            var confirmTag = motor.First().Entry.Tag;
            var changes = motor
                .Select(p => (p.Entry.Time, Value: ReadValue(p.Entry.Description)))
                .Where(c => c.Value is not null)
                .Select(c => (c.Time, Value: c.Value!.Value))
                .OrderBy(c => c.Time)
                .ToList();

            var commands = FindCommands(machineLog, motor.Key).ToList();
            if (commands.Count == 0) continue;

            checkedMotors.Add(motor.Key);
            var motorFailures = new List<MotorConfirmFailure>();

            foreach (var command in commands.Where(c => c.On))
            {
                var time = command.Time;
                if (Confirmed(changes, time)) continue;

                var droppedAt = changes
                    .Where(c => c.Value == 0 && c.Time >= time && c.Time <= time + ConfirmWindow)
                    .Select(c => (TimeSpan?)c.Time)
                    .FirstOrDefault();

                var outputDropped = commands
                    .Where(c => !c.On && c.Time > time)
                    .Select(c => (TimeSpan?)c.Time)
                    .FirstOrDefault();

                var waited = FindWaitLine(machineLog, motor.Key, time);

                // Positive evidence only. When one motor fails the machine aborts the whole step
                // and withdraws every output at once, so its companions look unconfirmed too -
                // the nog conveyor on M20421 was dropped 5.5s after being asked, never having had
                // a chance to spin up. That is the abort, not a second broken motor.
                var withdrawnEarly = outputDropped is { } off && off - time < ConfirmWindow;
                var positiveEvidence = droppedAt is not null || waited is not null || !withdrawnEarly;

                if (!positiveEvidence) continue;

                motorFailures.Add(new MotorConfirmFailure
                {
                    Motor = motor.Key,
                    OutputTag = command.Tag,
                    ConfirmTag = confirmTag,
                    CommandedOn = time,
                    ConfirmDroppedAt = droppedAt,
                    OutputDroppedAt = outputDropped,
                    MachineWaitedFor = waited
                });
            }

            if (motorFailures.Count == 0) healthy.Add(motor.Key);
            else failures.AddRange(motorFailures);
        }

        return new MotorConfirmFindings
        {
            // Most recent first: the file is taken minutes after the problem.
            Failures = failures.OrderByDescending(f => f.CommandedOn).ToList(),
            MotorsChecked = checkedMotors,
            Healthy = healthy
        };
    }

    /// <summary>
    /// Output changes for a motor. The output carries an IO- prefix and can be more specific than
    /// the confirmation - WasteMotorConfirm answers IO-WasteMotorFwd and IO-WasteMotorRev.
    /// </summary>
    private static IEnumerable<(TimeSpan Time, string Tag, bool On)> FindCommands(
        IReadOnlyList<MachineLogEntry> machineLog, string motor)
    {
        return machineLog
            .Where(e => e.Category == MachineLogCategory.OutputChange)
            .Where(e => e.Tag.Equals(motor, StringComparison.OrdinalIgnoreCase)
                        || e.Tag.StartsWith($"IO-{motor}", StringComparison.OrdinalIgnoreCase))
            .Select(e => (
                e.Time,
                e.Tag,
                On: e.Description.Contains("Set On", StringComparison.OrdinalIgnoreCase)))
            .OrderBy(c => c.Time);
    }

    /// <summary>
    /// Whether the motor was reporting itself running at any point in the window. The log records
    /// changes only, so a confirmation already reading 1 and not changing counts as confirmed -
    /// which is why the last state before the command matters as much as what follows it.
    /// </summary>
    private static bool Confirmed(IReadOnlyList<(TimeSpan Time, int Value)> changes, TimeSpan commandedOn)
    {
        var before = changes.LastOrDefault(c => c.Time <= commandedOn);
        var hadBefore = changes.Any(c => c.Time <= commandedOn);

        var within = changes
            .Where(c => c.Time > commandedOn && c.Time <= commandedOn + ConfirmWindow)
            .ToList();

        // It came on inside the window.
        if (within.Any(c => c.Value == 1)) return true;

        // It was already on and nothing dropped it.
        if (hadBefore && before.Value == 1 && within.All(c => c.Value != 0)) return true;

        // Never logged either side of the command: no evidence of a problem, so say nothing.
        if (!hadBefore && within.Count == 0) return true;

        return false;
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
