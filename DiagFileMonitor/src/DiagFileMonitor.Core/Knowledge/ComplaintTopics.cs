namespace DiagFileMonitor.Core.Knowledge;

/// <summary>
/// One kind of thing an operator complains about, and where to look first when they do.
/// </summary>
public class ComplaintTopic
{
    public string Name { get; init; } = string.Empty;

    /// <summary>Words in the operator's own text that point at this topic.</summary>
    public IReadOnlyList<string> Keywords { get; init; } = Array.Empty<string>();

    /// <summary>MachineLog tags worth pulling up for this complaint.</summary>
    public IReadOnlyList<string> LogTags { get; init; } = Array.Empty<string>();

    /// <summary>Words to search Change.log settings for, over the whole file rather than one day.</summary>
    public IReadOnlyList<string> SettingWords { get; init; } = Array.Empty<string>();

    /// <summary>What to check, in the order to check it.</summary>
    public IReadOnlyList<string> LookAt { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Machine models this applies to, matched as a prefix. Empty means every machine. It keeps
    /// a saw's infeed topics off a wall extruder report, where the same words mean something else.
    /// </summary>
    public IReadOnlyList<string> Models { get; init; } = Array.Empty<string>();

    /// <summary>
    /// An unknown model matches everything rather than nothing. A bundle whose machine.xml did
    /// not parse should still get the full set of suggestions - showing too many beats showing
    /// none at all.
    /// </summary>
    public bool AppliesTo(string? model) =>
        Models.Count == 0
        || string.IsNullOrWhiteSpace(model)
        || Models.Any(m => model.StartsWith(m, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// The complaints that come in, and what each one means you should
/// go and look at. The operator's own words are the best steer available: "nail gun in the wrong
/// position" means start at gun heights, not at whatever the log happens to shout loudest about.
/// </summary>
public static class ComplaintTopics
{
    public static readonly IReadOnlyList<ComplaintTopic> All = new ComplaintTopic[]
    {
        new()
        {
            Name = "Nail gun position or height",
            Models = new[] { "WallExtruder", "RakingWallExtruder", "RakedWallExtruder", "FastFramer" },
            Keywords = new[]
            {
                "nail gun", "nailgun", "nail-gun", "gun position", "gun height", "gun is", "guns are",
                "nail position", "nails are", "nailing", "misfire", "miss fire", "wrong position",
                "incorrect position", "nail in the wrong", "gun not"
            },
            LogTags = new[] { "GunFire", "DualGuns", "TopStudClamp", "SideClamp", "RackLock" },
            // "height" is deliberately not here - it drags in RWEPanelHeightGap, which is a panel
            // height setting, not a gun one. "gun" and "nail" match the real setting names exactly.
            SettingWords = new[] { "gun", "nail" },
            LookAt = new[]
            {
                "Gun and nail settings first - every one that has ever been changed is listed below "
                    + "with its date. For position specifically, NailDistFromEdgeOfTimber and "
                    + "MinNailDistFromEdgeOfTimber set how far in from the edge it nails, and GunType "
                    + "and DualGuns say what is fitted. A change just before the complaint started is "
                    + "the likeliest cause.",
                "Then the nail count for the timber width: 90mm takes 2, 140mm takes 3, 190mm takes 4, "
                    + "and the top gun steps up for the 3 and 4 nail patterns.",
                "Then whether the plate clamp was detecting timber at the moment of firing. A clamp "
                    + "that misses aborts only its own side's guns - the panel carries on regardless, "
                    + "so the result looks like a gun that fired in the wrong place."
            }
        },
        new()
        {
            Name = "Machine will not move or start",
            Keywords = new[]
            {
                "not moving", "wont move", "won't move", "not move", "nothing happens", "no movement",
                "doesn't start", "does not start", "wont start", "won't start", "start panel",
                "stuck", "sitting there", "does nothing", "not going", "trolleys not"
            },
            LogTags = new[] { "WallExtruderPLC", "ClsWallExtruder" },
            SettingWords = Array.Empty<string>(),
            LookAt = new[]
            {
                "The homing interlock first - see CAN IT HOME? below. All four product sensors have "
                    + "to read 0, and a single one reading 1 stops everything with nothing on screen.",
                "Then the floating side safety bar, which blocks all dangerous movement until an "
                    + "E-Stop reset.",
                "Then \"Servo Not Setup\", which clears in under 0.2s without faulting properly and "
                    + "can leave the machine quietly waiting."
            }
        },
        new()
        {
            Name = "Homing",
            Keywords = new[] { "home", "homing", "wont home", "won't home", "reference" },
            LogTags = new[] { "GripperProductSensor", "PlatePresentSwitch" },
            SettingWords = Array.Empty<string>(),
            LookAt = new[]
            {
                "Both GripperProductSensor inputs and both PlatePresentSwitch inputs must read 0 "
                    + "before the machine will home - see CAN IT HOME? below."
            }
        },
        new()
        {
            Name = "Ejection",
            Models = new[] { "WallExtruder", "RakingWallExtruder", "RakedWallExtruder", "FastFramer" },
            Keywords = new[]
            {
                "eject", "ejection", "ejector", "wont come out", "won't come out", "panel stuck",
                "outfeed", "not coming out"
            },
            LogTags = new[] { "FixedEjectServo", "FloatingEjectServo", "TrolleyHeight" },
            SettingWords = new[] { "eject" },
            LookAt = new[]
            {
                "The known ejection bug first: the faulting floating-side node does not get its "
                    + "disable command mid-eject. That is filed and confirmed - rule it in or out "
                    + "before looking for a sensor or wiring cause.",
                "Then TrolleyHeight, which has to move higher to let the panel out. How far depends "
                    + "on panel height, so check it against the panel that failed."
            }
        },
        new()
        {
            Name = "Clamps and plate detection",
            Models = new[] { "WallExtruder", "RakingWallExtruder", "RakedWallExtruder", "FastFramer" },
            Keywords = new[]
            {
                "clamp", "plate", "lost product", "not detected", "not detecting", "timber",
                "wont clamp", "won't clamp", "slipping"
            },
            LogTags = new[] { "PlateClamp", "PlatePresent", "HorizStudClamp", "PlateSupport" },
            SettingWords = new[] { "clamp", "plate" },
            LookAt = new[]
            {
                "Whether a plate present sensor really lost the timber or just glitched - see the "
                    + "PLATE PRESENT SENSOR section below if there is one.",
                "Both sides dropping together with a clamp output change is a normal release. One "
                    + "side on its own with nothing moving is a sensor glitch."
            }
        },
        new()
        {
            Name = "Studs skipped or in the wrong place",
            Models = new[] { "WallExtruder", "RakingWallExtruder", "RakedWallExtruder", "FastFramer" },
            Keywords = new[]
            {
                "stud", "studs", "spacing", "missing", "skipped", "not nailed", "no nails",
                "wrong place", "out of position"
            },
            LogTags = new[] { "StudPin", "HorizStudClamp", "FileReader" },
            SettingWords = new[] { "stud", "assembly", "inclusion", "dwassemblies" },
            LookAt = new[]
            {
                "Nail mark generation before anything mechanical. A stud only gets nailed if the "
                    + "member has Process = TRUE and Nail To = TRUE.",
                "Then UseDWAssemblies. With no major sub and UseDWAssemblies = FALSE, nail marks are "
                    + "not generated for members above or below a Header or Sill, plus however many "
                    + "adjacent studs \"Assembly Stud Inclusion Count\" is set to.",
                "Only once those are ruled out is it worth looking at stud pins and clamps."
            }
        },
        new()
        {
            Name = "Trolley or floating head height",
            Models = new[] { "WallExtruder", "RakingWallExtruder", "RakedWallExtruder", "FastFramer" },
            Keywords = new[]
            {
                "trolley", "floating head", "head height", "rake", "raked", "angle", "too high",
                "too low", "wrong height"
            },
            LogTags = new[] { "TrolleyHeight", "FloatingSideHeight", "Floating Head" },
            SettingWords = new[] { "height", "rake", "trolley" },
            LookAt = new[]
            {
                "TrolleyHeight sets the floating side trolley to the panel's starting height and "
                    + "holds it for the whole panel, moving again only to let the panel eject. One "
                    + "move at the start and one at the eject is normal - a move part way through a "
                    + "panel is not.",
                "FloatingSideHeight is the one that repositions per stud, following the rake "
                    + "progression. Wrong heights stud to stud point here rather than at TrolleyHeight.",
                "The floating head laser scans the travel path before moving. A blockage gives an "
                    + "on-screen warning and needs the THNTD buttons tapped again to retry."
            }
        },
        new()
        {
            Name = "Print position on the timber",
            Models = new[] { "Tornado" },
            Keywords = new[]
            {
                "print", "printer", "printing", "label", "ink", "off the edge", "off edge",
                "not printing", "wrong place"
            },
            LogTags = new[] { "Printer" },
            SettingWords = new[] { "print" },
            LookAt = new[]
            {
                "The top printer height, which is a physical adjustment - there is no software "
                    + "setting for it. Print riding off the top edge means lower it; print sitting "
                    + "too low on the face means raise it.",
                "What timber is being run. Thickness goes New Zealand (thickest, printer highest), "
                    + "Australian, American, then Sterling (thinnest, printer lowest), so ask whether "
                    + "the timber type changed just before this started.",
                "Whether anyone checked the printer height at the last timber changeover - that is "
                    + "the step most often missed."
            }
        },
        new()
        {
            Name = "Board length or size rejected at infeed",
            Models = new[] { "Tornado" },
            Keywords = new[]
            {
                "not expected length", "not expected size", "wrong length", "wrong size",
                "board length", "board size", "undersize", "over length", "overlength",
                "rejects the board", "wont accept", "won't accept", "measuring"
            },
            LogTags = new[] { "Laser", "Clamp", "Gripper", "Infeed" },
            SettingWords = new[] { "tolerance", "length", "board" },
            LookAt = new[]
            {
                "Which of the two errors it is. \"Board not expected length\" is the post laser and "
                    + "is usually reflection off the polished deck; \"Board not expected size\" is the "
                    + "analog clamp sensor and is usually genuinely undersized timber.",
                "For a length error: does the reported figure look random rather than merely wrong? "
                    + "A wildly long reading on a short board is the deck reflection, not the sensor. "
                    + "The gripper eyes and entry sensor reading correctly does not rule it out.",
                "For a size error: measure the timber. Outside about 10% is a genuine reject; only "
                    + "slightly out can be run through with Continue.",
                "Ask whether the infeed deck has been cleaned, polished or had tape removed."
            }
        },
        new()
        {
            Name = "Safety bar or E-Stop",
            Keywords = new[] { "safety bar", "estop", "e-stop", "e stop", "emergency", "reset" },
            LogTags = new[] { "EstopReset", "WallExtruderPLC" },
            SettingWords = Array.Empty<string>(),
            LookAt = new[]
            {
                "Which circuits keep or lose power and air on an E-Stop has never been pinned down "
                    + "on this machine, so check what actually lost air or power rather than assuming.",
                "\"Clamps Air supply pressure is low\" right after an E-Stop reset is normal and not "
                    + "worth chasing on its own."
            }
        }
    };
}
