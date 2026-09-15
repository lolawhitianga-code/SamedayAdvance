using System.Text.RegularExpressions;

namespace DiagFileMonitor.Core.Knowledge;

/// <summary>One 1S-series drive alarm.</summary>
public class OmronAlarm
{
    public string Code { get; init; } = string.Empty;
    public string Meaning { get; init; } = string.Empty;
    public string FirstChecks { get; init; } = string.Empty;

    /// <summary>True for a family like 83.xx where the subcode narrows it further.</summary>
    public bool IsGroup { get; init; }

    /// <summary>True where repeated resetting can make the damage worse.</summary>
    public bool Urgent { get; init; }

    public string Display => IsGroup ? $"{Code}.xx" : Code;
}

public class OmronDrive
{
    public string Model { get; init; } = string.Empty;

    /// <summary>Rated output in watts, decoded from the capacity code.</summary>
    public int Watts { get; init; }

    /// <summary>Supply voltage class: 100, 200 or 400 VAC.</summary>
    public int Volts { get; init; }

    /// <summary>
    /// How long to wait after cutting the main circuit power before wiring or inspecting.
    /// Straight out of the instruction manual - this one is a shock hazard, not a guideline.
    /// </summary>
    public int DischargeWaitMinutes { get; init; }

    public string Description =>
        $"{Model}: {(Watts >= 1000 ? $"{Watts / 1000.0:0.##} kW" : $"{Watts} W")}, {Volts} V, EtherCAT";
}

/// <summary>
/// Omron 1S-series servo drives, <c>R88D-1SN□□-ECT</c>, driving <c>R88M-1L</c> / <c>R88M-1M</c>
/// motors over EtherCAT. From the 1S-series instruction manual (no. 2884903-0E, 2021) and a
/// photographed R88D-1SN15F-ECT.
/// <para>
/// The alarm table below is the <b>1S</b> family only, because that is what Spida fits. It came
/// from a third-party summary rather than from Omron, so every entry is a starting point to be
/// confirmed against Omron's own 1S User's Manual (I586) before it is quoted to a customer.
/// </para>
/// </summary>
public static class OmronServoDrives
{
    /// <summary>Where the alarm meanings came from, printed with them so the reader can weigh them.</summary>
    public const string AlarmSource =
        "from a third-party summary, not Omron - confirm against Omron's 1S User's Manual (I586)";

    private static readonly Regex ModelPattern = new(
        @"\bR88D-1SN(?<capacity>\d{2,3})(?<voltage>[LHF])-ECT\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Minutes to wait after power off, by capacity+voltage code, verbatim from the manual.
    /// </summary>
    private static readonly Dictionary<string, int> DischargeWait = new(StringComparer.OrdinalIgnoreCase)
    {
        ["06F"] = 10, ["10F"] = 10, ["15F"] = 10, ["20F"] = 10,
        ["30F"] = 10, ["55F"] = 10, ["75F"] = 10, ["150F"] = 10,

        ["01L"] = 15, ["02L"] = 15, ["01H"] = 15, ["02H"] = 15, ["04H"] = 15,

        ["04L"] = 20, ["08H"] = 20, ["10H"] = 20, ["15H"] = 20, ["20H"] = 20,
        ["30H"] = 20, ["55H"] = 20, ["75H"] = 20, ["150H"] = 20
    };

    /// <summary>
    /// Reads a drive part number. The capacity code is the output in hundreds of watts and the
    /// letter is the supply: L 100 V, H 200 V, F 400 V. Checked against a real R88D-1SN15F-ECT,
    /// whose own label reads 400 V 3PH 1.5 kW.
    /// </summary>
    public static OmronDrive? Describe(string? model)
    {
        if (string.IsNullOrWhiteSpace(model)) return null;

        var match = ModelPattern.Match(model);
        if (!match.Success) return null;

        var capacity = match.Groups["capacity"].Value;
        var voltage = match.Groups["voltage"].Value.ToUpperInvariant();

        return new OmronDrive
        {
            Model = match.Value.ToUpperInvariant(),
            Watts = int.Parse(capacity) * 100,
            Volts = voltage switch { "L" => 100, "H" => 200, _ => 400 },
            DischargeWaitMinutes = DischargeWait.GetValueOrDefault(capacity + voltage)
        };
    }

    /// <summary>
    /// 1S alarms. The drive shows <c>Er</c> then a hex main code and subcode - <c>Er 16 00</c> is
    /// Overload, <c>Er 83 03</c> a communications synchronisation error.
    /// <para>
    /// G5 drives share some main codes but not the subcodes, so a G5 remedy must not be applied
    /// here: G5 13.1 is an AC supply interruption while 1S 13.01 is main-circuit phase loss. The
    /// 1S encoder is batteryless, so the G5 40.0 battery remedy does not apply at all.
    /// </para>
    /// </summary>
    public static readonly IReadOnlyList<OmronAlarm> Alarms = new OmronAlarm[]
    {
        new() { Code = "12.0", Meaning = "DC bus overvoltage",
                FirstChecks = "Incoming voltage, the deceleration profile, and the regeneration circuit. "
                              + "An open regeneration resistor turns a normal deceleration into a trip." },
        new() { Code = "13.0", Meaning = "Main power supply undervoltage",
                FirstChecks = "Mains voltage measured while it tries to start rather than at idle, the "
                              + "contactor, fuses, cable and supply capacity." },
        new() { Code = "13.01", Meaning = "Main circuit power supply phase loss",
                FirstChecks = "Phase wiring and supply dips. Check whether the drive is a three-phase-only "
                              + "model before touching the phase-loss detection setting." },
        new() { Code = "14.0", Meaning = "Overcurrent",
                FirstChecks = "U/V/W phase-to-phase short, a fault to ground, the motor cable at flex "
                              + "points, and the motor windings. Do not keep resetting it - repeated "
                              + "energising can turn a repairable cable fault into a failed power module.",
                Urgent = true },
        new() { Code = "14.01", Meaning = "IPM or power module error",
                FirstChecks = "The motor circuit and regeneration wiring, then the power module itself.",
                Urgent = true },
        new() { Code = "15.0", Meaning = "Servo drive overheat",
                FirstChecks = "Cabinet filters, fans, spacing, ambient temperature, and whether something "
                              + "else is exhausting onto the drive." },
        new() { Code = "15.01", Meaning = "Motor overheat",
                FirstChecks = "Motor ambient temperature, load ratio and the condition of the motor." },
        new() { Code = "16.00", Meaning = "Overload",
                FirstChecks = "Mechanical load, whether the brake is releasing, acceleration, tuning and "
                              + "whether the axis is sized for the job." },
        new() { Code = "18.00", Meaning = "Regeneration overload",
                FirstChecks = "The motion profile first - inertia, short decelerations, repeated indexing, "
                              + "descending vertical loads. The resistor may be perfectly healthy." },
        new() { Code = "18.01", Meaning = "Regeneration circuit error",
                FirstChecks = "Resistor wiring, its resistance value and rating, and a possible short." },
        new() { Code = "21.00", Meaning = "Encoder communications disconnected",
                FirstChecks = "Both connectors fully engaged, cable continuity, and the cable at drag-chain "
                              + "exits and cabinet entries." },
        new() { Code = "21.01", Meaning = "Encoder communications error",
                FirstChecks = "Noise before hardware: shield and earth, connector contact, and keeping the "
                              + "encoder cable away from U/V/W and braking resistor wiring. If it only "
                              + "happens when something else switches, replacing the encoder will not fix it." },
        new() { Code = "24.00", Meaning = "Excessive position deviation",
                FirstChecks = "Whether the axis is physically blocked, the brake is releasing, and the "
                              + "commanded acceleration is realistic. If torque is saturated while the error "
                              + "grows, it is trying to move and cannot." },
        new() { Code = "24.01", Meaning = "Excessive speed deviation",
                FirstChecks = "Tuning, load restriction and the configured detection level." },
        new() { Code = "26.00", Meaning = "Excessive speed",
                FirstChecks = "Speed command, electronic gear ratio, overshoot, and whether an external "
                              + "load is driving the motor." },
        new() { Code = "34.01", Meaning = "Software position limit exceeded",
                FirstChecks = "The software limits and the commanded position. Do not bypass a limit to "
                              + "clear production." },
        new() { Code = "36.00", Meaning = "Non-volatile memory data error",
                FirstChecks = "Reload only a verified parameter file for this exact axis and hardware." },
        new() { Code = "37.00", Meaning = "Non-volatile memory hardware error",
                FirstChecks = "Reload verified data; replace the drive if it comes back." },
        new() { Code = "38.00", Meaning = "Drive prohibition input error",
                FirstChecks = "Positive and negative limit input assignments, NO/NC logic, field wiring "
                              + "and the actual limit switches." },
        new() { Code = "83", Meaning = "EtherCAT state or synchronisation error",
                FirstChecks = "The controller event log and the exact subcode before touching the motor. "
                              + "Node address, IN/OUT cable orientation, link indicators, distributed clock "
                              + "and cycle settings. Reaching Operational then dropping only on movement "
                              + "points at cable routing, strain or noise.", IsGroup = true },
        new() { Code = "87.00", Meaning = "Error stop input active",
                FirstChecks = "The emergency stop chain and the assigned input. Do not jumper it." },
        new() { Code = "90", Meaning = "EtherCAT configuration error",
                FirstChecks = "PDO mapping against the current ESI file, Sync Manager, distributed clocks "
                              + "and watchdog settings. If it never reaches the requested state after a "
                              + "project change, start with configuration rather than hardware.", IsGroup = true },
        new() { Code = "95", Meaning = "Motor non-conformity",
                FirstChecks = "Compare the complete motor and drive model numbers, not just kilowatts - "
                              + "voltage class, capacity, encoder type and brake option all matter.",
                IsGroup = true },
        new() { Code = "99.99", Meaning = "Misalignment alert",
                FirstChecks = "A normal reset is deliberately not enough: the encoder Communications Error "
                              + "Count must be cleared in Sysmac Studio first, and the machine reference "
                              + "verified before automatic operation." },
        new() { Code = "C0.00", Meaning = "STO detected (information, not an error by default)",
                FirstChecks = "The STO circuit. Do not jumper it." }
    };

    /// <summary>The alarm for a code, or null where it is not one we have written down.</summary>
    public static OmronAlarm? Lookup(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;

        var trimmed = code.Trim();

        return Alarms.FirstOrDefault(a => a.Code.Equals(trimmed, StringComparison.OrdinalIgnoreCase))
               ?? Alarms.FirstOrDefault(a => a.IsGroup
                                             && trimmed.StartsWith(a.Code + ".", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Alarms written the way the 1S drive shows them - "Er 16 00". Deliberately narrow: matching
    /// a bare "16.00" anywhere in a log would turn timestamps and version numbers into alarms.
    /// No Spida log has been seen carrying these yet, so this may never fire.
    /// </summary>
    private static readonly Regex AlarmPattern = new(
        @"\bEr\s*(?<main>[0-9A-F]{2})\s*\.?\s*(?<sub>[0-9A-F]{2})\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static IReadOnlyList<OmronAlarm> Find(IEnumerable<string> lines)
    {
        var found = new List<OmronAlarm>();

        foreach (var line in lines.Where(l => !string.IsNullOrWhiteSpace(l)))
        {
            foreach (Match match in AlarmPattern.Matches(line))
            {
                var main = match.Groups["main"].Value.TrimStart('0');
                if (main.Length == 0) main = "0";

                var alarm = Lookup($"{main}.{match.Groups["sub"].Value}")
                            ?? Lookup($"{main}.{match.Groups["sub"].Value.TrimStart('0')}")
                            ?? Lookup(main);

                if (alarm is not null && !found.Contains(alarm)) found.Add(alarm);
            }
        }

        return found;
    }

    /// <summary>What the lights on the front of the drive mean, for a phone call to site.</summary>
    public static readonly IReadOnlyList<(string Name, string Meaning)> Indicators = new[]
    {
        ("PWR", "Control power is on."),
        ("ERR", "Drive error. The ERR output is a normally closed contact that opens on error, "
                + "and is wired to cut the main circuit power."),
        ("ECAT RUN", "EtherCAT state - the drive is running on the network."),
        ("ECAT ERR", "EtherCAT error."),
        ("L/A IN", "Link and activity on CN10, the EtherCAT IN port."),
        ("L/A OUT", "Link and activity on CN11, the EtherCAT OUT port."),
        ("FS", "Safety function status."),
        ("CHARGE", "The internal bus is still charged. It stays lit after power off - see the "
                   + "discharge wait time before touching anything.")
    };

    public static readonly IReadOnlyList<(string Connector, string Purpose)> Connectors = new[]
    {
        ("CN1", "Control I/O."),
        ("CN2", "Encoder."),
        ("CN7", "USB, for the tuning software."),
        ("CN10", "EtherCAT IN."),
        ("CN11", "EtherCAT OUT."),
        ("CN12", "Safety I/O.")
    };
}
