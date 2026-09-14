using System.Text.RegularExpressions;
using DiagFileMonitor.Core.SpidaLogs;

namespace DiagFileMonitor.Core.Knowledge;

/// <summary>The last known state of one product sensor, read off the input changes in the log.</summary>
public class ProductSensorState
{
    public string Tag { get; init; } = string.Empty;
    public string Address { get; init; } = string.Empty;
    public int Value { get; init; }
    public TimeSpan ChangedAt { get; init; }

    public string Display => $"{Tag} ({Address}) = {Value}";
}

/// <summary>One attempt to home the machine, and whether anything was in the way of it.</summary>
public class HomeAttempt
{
    public TimeSpan Time { get; init; }
    public string Command { get; init; } = string.Empty;

    /// <summary>Product sensors reading 1 when the command was given. Any one of these blocks homing.</summary>
    public IReadOnlyList<ProductSensorState> Blocking { get; init; } = Array.Empty<ProductSensorState>();

    /// <summary>How long until an axis actually started homing, where one did.</summary>
    public TimeSpan? FirstAxisHomedAfter { get; init; }

    public bool WasRefused { get; init; }

    public string Outcome => FirstAxisHomedAfter is { } delay
        ? WasRefused
            ? $"nothing homed for {delay.TotalSeconds:0.#}s"
            : $"an axis started homing {delay.TotalSeconds:0.#}s later"
        : "no axis ever started homing after this";
}

public class HomeInterlockFindings
{
    public IReadOnlyList<HomeAttempt> Attempts { get; init; } = Array.Empty<HomeAttempt>();

    /// <summary>Every product sensor the log mentions, with the state it was last left in.</summary>
    public IReadOnlyList<ProductSensorState> SensorsSeen { get; init; } = Array.Empty<ProductSensorState>();

    /// <summary>Sensor tags that must read 0 but never appear in this log at all.</summary>
    public IReadOnlyList<string> SensorsNotInLog { get; init; } = Array.Empty<string>();

    public IReadOnlyList<HomeAttempt> Refused => Attempts.Where(a => a.WasRefused).ToList();

    public bool Any => Attempts.Count > 0 || SensorsSeen.Count > 0;
}

/// <summary>
/// Checks the homing interlock: on a raked extruder both GripperProductSensor inputs and both
/// PlatePresentSwitch inputs must read 0 before the machine will home. Any one of them reading 1
/// means something is still being detected, and the home command is refused.
/// <para>
/// MachineLog.txt only records changes, so a sensor that has sat at 0 the whole time never
/// appears. A sensor is only ever reported as blocking on the strength of a logged change to 1
/// with no logged change back to 0.
/// </para>
/// </summary>
public static class HomeInterlockCheck
{
    /// <summary>The tags whose inputs all have to read 0. Addresses are read from the log.</summary>
    public static readonly string[] ProductSensorTags = { "GripperProductSensor", "PlatePresentSwitch" };

    /// <summary>A home command that takes longer than this to move anything was not accepted.</summary>
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

    private static readonly Regex InputChange = new(
        @"Input\s*\((?<address>[^)]+)\)\s*Changed\s*to\s*(?<value>\d+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static HomeInterlockFindings Check(IReadOnlyList<MachineLogEntry> machineLog)
    {
        var changes = ReadSensorChanges(machineLog);

        var homeCommands = machineLog
            .Where(e => e.Description.Contains("HomeServos", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var axisHomes = machineLog
            .Where(e => e.Description.Contains("Axis Start Home", StringComparison.OrdinalIgnoreCase))
            .Select(e => e.Time)
            .ToList();

        var attempts = new List<HomeAttempt>();

        foreach (var command in homeCommands)
        {
            // Entries sharing a timestamp come from the same PLC scan, so a sensor that changed
            // on the same timestamp was already reading that way when the command was given -
            // even where the log happens to print it on a later line.
            var stateAtCommand = changes
                .Where(c => c.ChangedAt <= command.Time)
                .GroupBy(c => c.Address)
                .Select(g => g.OrderBy(c => c.ChangedAt).Last())
                .ToList();

            var firstHomeAfter = axisHomes
                .Where(t => t >= command.Time)
                .Select(t => (TimeSpan?)(t - command.Time))
                .FirstOrDefault();

            var blocking = stateAtCommand.Where(c => c.Value != 0).OrderBy(c => c.Address).ToList();

            attempts.Add(new HomeAttempt
            {
                Time = command.Time,
                Command = command.Description.Trim(),
                Blocking = blocking,
                FirstAxisHomedAfter = firstHomeAfter,
                WasRefused = firstHomeAfter is null || firstHomeAfter > Patience
            });
        }

        var lastStates = changes
            .GroupBy(c => c.Address)
            .Select(g => g.OrderBy(c => c.ChangedAt).Last())
            .OrderBy(c => c.Tag, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.Address, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new HomeInterlockFindings
        {
            Attempts = attempts,
            SensorsSeen = lastStates,
            SensorsNotInLog = ProductSensorTags
                .Where(tag => !lastStates.Any(s => s.Tag.Equals(tag, StringComparison.OrdinalIgnoreCase)))
                .ToList()
        };
    }

    private static List<ProductSensorState> ReadSensorChanges(IReadOnlyList<MachineLogEntry> machineLog)
    {
        var changes = new List<ProductSensorState>();

        foreach (var entry in machineLog)
        {
            if (entry.Category != MachineLogCategory.InputChange) continue;

            var tag = ProductSensorTags.FirstOrDefault(t =>
                entry.Tag.Equals(t, StringComparison.OrdinalIgnoreCase));

            if (tag is null) continue;

            var match = InputChange.Match(entry.Description);
            if (!match.Success) continue;

            changes.Add(new ProductSensorState
            {
                Tag = tag,
                Address = match.Groups["address"].Value.Trim(),
                Value = int.Parse(match.Groups["value"].Value),
                ChangedAt = entry.Time
            });
        }

        return changes;
    }
}
