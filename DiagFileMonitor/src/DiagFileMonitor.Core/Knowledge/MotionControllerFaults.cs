using System.Text.RegularExpressions;
using DiagFileMonitor.Core.SpidaLogs;

namespace DiagFileMonitor.Core.Knowledge;

/// <summary>A code the CyberLogix MC2 drive flashes on its status LED display.</summary>
public class MotionControllerCode
{
    public string Code { get; init; } = string.Empty;
    public string Meaning { get; init; } = string.Empty;
    public string WhatToCheck { get; init; } = string.Empty;

    /// <summary>False for the limit-switch codes, which are a state rather than a failure.</summary>
    public bool IsFault { get; init; } = true;

    /// <summary>True where the manual says to call CyberLogix rather than chase it on site.</summary>
    public bool CallCyberLogix { get; init; }

    /// <summary>
    /// The headline half of the meaning, without the trailing stop, for lines that read
    /// "F02 Encoder wiring fault on Axis-FixedSidePusher".
    /// </summary>
    public string ShortMeaning
    {
        get
        {
            var cut = Meaning.IndexOf(" - ", StringComparison.Ordinal);
            var head = cut > 0 ? Meaning[..cut] : Meaning;

            return head.TrimEnd('.', ' ');
        }
    }
}

public class MotionControllerSighting
{
    public MotionControllerCode Code { get; init; } = new();
    public int Occurrences { get; init; }

    /// <summary>The axes the code was raised on, e.g. Axis-FixedSidePusher.</summary>
    public IReadOnlyList<string> Axes { get; init; } = Array.Empty<string>();

    public TimeSpan? FirstSeen { get; init; }
    public TimeSpan? LastSeen { get; init; }

    /// <summary>The log lines it appeared on, so the reading can be checked.</summary>
    public IReadOnlyList<string> Lines { get; init; } = Array.Empty<string>();

    public string Where => Axes.Count == 0
        ? string.Empty
        : Axes.Count == 1 ? $" on {Axes[0]}" : $" on {string.Join(" and ", Axes)}";
}

/// <summary>
/// The fault and status codes from the CyberLogix MC2 motion controller documentation
/// (Version 12, revised 11/04/16). The drive flashes a three-character code on its status LED:
/// the display scrolls in a circle when all is well.
/// <para>
/// These are CLX codes. An axis running on Omron hardware (CIPNet) does not produce them, so
/// <see cref="AxisHardware"/> is worth reading alongside this on a machine that mixes the two.
/// </para>
/// </summary>
public static class MotionControllerFaults
{
    public static readonly IReadOnlyList<MotionControllerCode> All = new MotionControllerCode[]
    {
        new()
        {
            Code = "F01",
            Meaning = "Invalid hall state on the hall inputs.",
            WhatToCheck = "Hall wiring, the motor's hall sensors, and that the controller is set to the "
                          + "right brush or brushless mode."
        },
        new()
        {
            Code = "F02",
            Meaning = "Encoder wiring fault.",
            WhatToCheck = "The encoder wiring, and the encoder on the motor itself."
        },
        new()
        {
            Code = "F03",
            Meaning = "Encoder power fault - the internal auto-reset fuse has tripped from over current "
                      + "on the encoder supply.",
            WhatToCheck = "The encoder wiring and the encoder on the motor. The fuse resets itself, so it "
                          + "can trip repeatedly without leaving anything obviously blown."
        },
        new()
        {
            Code = "F04",
            Meaning = "Position error limit exceeded.",
            WhatToCheck = "A jam on the machine and whether the motor turns freely; whether it is being "
                          + "driven too fast; under voltage on the motor; a faulty motor or encoder; and "
                          + "the current and output limiting set in the drive."
        },
        new()
        {
            Code = "F05",
            Meaning = "Motor over current fault - the peak current limit on the servo drive was reached.",
            WhatToCheck = "Faulty wiring, a faulty motor, or the axis being overloaded."
        },
        new()
        {
            Code = "F06",
            Meaning = "Motor power fault - the motor supply voltage is either too high or too low.",
            WhatToCheck = "The motor supply voltage at the drive."
        },
        new()
        {
            Code = "F07",
            Meaning = "Temperature fault - the drive is overheating.",
            WhatToCheck = "Ventilation around the drive and whether the axis is overloaded. Forced cooling "
                          + "may be needed."
        },
        new()
        {
            Code = "F08",
            Meaning = "Amp disabled - a massive over current was detected by the drive amp.",
            WhatToCheck = "A short circuit on the motor or its wiring. It can also happen when the motor "
                          + "output is hard stopped very suddenly."
        },
        new()
        {
            Code = "F09",
            Meaning = "Enable lost - the drive lost its enable (emergency stop) input while it was enabled "
                      + "and holding position or moving.",
            WhatToCheck = "The E-Stop chain and the enable input wiring. Worth lining up against when the "
                          + "operator says an E-Stop was pressed."
        },
        new()
        {
            Code = "F10",
            Meaning = "Motor stalled - the motor is not moving with full permissible power applied.",
            WhatToCheck = "The same as F04: a jam, whether the motor turns freely, supply voltage, and the "
                          + "drive's current limiting. Added in firmware 12."
        },
        new() { Code = "F11", Meaning = "Internal fault.", WhatToCheck = "Call CyberLogix.", CallCyberLogix = true },
        new() { Code = "F12", Meaning = "Internal fault.", WhatToCheck = "Call CyberLogix.", CallCyberLogix = true },
        new() { Code = "F13", Meaning = "Internal fault.", WhatToCheck = "Call CyberLogix.", CallCyberLogix = true },
        new()
        {
            Code = "F14",
            Meaning = "Comms fail - host device communications timed out. The drive expects host comms "
                      + "every 3 seconds by default.",
            WhatToCheck = "The network to the drive, and the comms timeout set in software."
        },
        new()
        {
            Code = "F15",
            Meaning = "Drive not set up - no setup message has been sent to the drive.",
            WhatToCheck = "Normally cleared by the reset button in the software."
        },
        new()
        {
            Code = "F16",
            Meaning = "No address - the drive has not been configured by the host device.",
            WhatToCheck = "The communication cables and the host device."
        },
        new()
        {
            Code = "F99",
            Meaning = "CPU not running - the unit is in flash update mode or the CPU has failed.",
            WhatToCheck = "Call CyberLogix.",
            CallCyberLogix = true
        },
        new()
        {
            Code = "SLL",
            Meaning = "Software low limit - the drive is at a software limit and will only respond to "
                      + "higher position setpoints.",
            WhatToCheck = "Not a failure. Check the software limits if the axis will not move one way.",
            IsFault = false
        },
        new()
        {
            Code = "SHL",
            Meaning = "Software high limit - the drive is at a software limit and will only respond to "
                      + "lower position setpoints.",
            WhatToCheck = "Not a failure. Check the software limits if the axis will not move one way.",
            IsFault = false
        },
        new()
        {
            Code = "HLL",
            Meaning = "Hardware low limit - the low limit switch is off, so the drive will only respond "
                      + "to forward motion.",
            WhatToCheck = "Not a failure in itself. Check the low limit switch if the axis will not reverse.",
            IsFault = false
        },
        new()
        {
            Code = "HHL",
            Meaning = "Hardware high limit - the high limit switch is off, so the drive will only respond "
                      + "to reverse motion.",
            WhatToCheck = "Not a failure in itself. Check the high limit switch if the axis will not advance.",
            IsFault = false
        }
    };

    /// <summary>
    /// Matches only the codes the manual actually defines, on a word boundary. Matching any
    /// F-and-two-digits would pick up version numbers and part numbers as drive faults.
    /// </summary>
    /// <para>
    /// The trailing guard stops a version or part number like F02.1 being read as fault F02:
    /// a dot is a word boundary, so \b alone would match it.
    /// </para>
    private static readonly Regex CodePattern = new(
        @"\b(" + string.Join("|", All.Select(c => c.Code)) + @")\b(?![.\d])", RegexOptions.Compiled);

    public static MotionControllerCode? Lookup(string code) =>
        All.FirstOrDefault(c => c.Code.Equals(code, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Every defined code in the machine log, with the axis it was raised on. The drive writes
    /// both the code and its meaning - "F02 Encoder Wiring Fault" - under an Axis-... tag, so the
    /// axis is what makes the code actionable.
    /// </summary>
    public static IReadOnlyList<MotionControllerSighting> Find(IReadOnlyList<MachineLogEntry> machineLog)
    {
        var hits = new Dictionary<string, List<MachineLogEntry>>(StringComparer.Ordinal);

        foreach (var entry in machineLog)
        {
            foreach (Match match in CodePattern.Matches(entry.Description))
            {
                if (!hits.TryGetValue(match.Value, out var seen))
                {
                    seen = new List<MachineLogEntry>();
                    hits[match.Value] = seen;
                }

                seen.Add(entry);
            }
        }

        return hits
            .Select(pair => new MotionControllerSighting
            {
                Code = Lookup(pair.Key)!,
                Occurrences = pair.Value.Count,
                Axes = pair.Value
                    .Select(e => e.Tag)
                    .Where(t => t.Length > 0)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                FirstSeen = pair.Value.Min(e => e.Time),
                LastSeen = pair.Value.Max(e => e.Time),
                Lines = pair.Value.Take(5).Select(e => e.Display).ToList()
            })
            // Most recent first within the faults. The bundle is exported within minutes of the
            // problem, so the code raised last is the one being reported - not the one raised most
            // often earlier in the shift.
            .OrderByDescending(s => s.Code.IsFault)
            .ThenByDescending(s => s.LastSeen)
            .ToList();
    }

    /// <summary>The same scan over plain text, for ErrLog entries which carry no axis tag.</summary>
    public static IReadOnlyList<string> FindCodes(IEnumerable<string> lines) =>
        lines.Where(l => !string.IsNullOrWhiteSpace(l))
            .SelectMany(l => CodePattern.Matches(l).Select(m => m.Value))
            .Distinct(StringComparer.Ordinal)
            .ToList();
}
