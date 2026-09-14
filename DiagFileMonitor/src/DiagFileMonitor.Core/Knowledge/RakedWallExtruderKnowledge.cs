namespace DiagFileMonitor.Core.Knowledge;

/// <summary>
/// What is known about the Raked Wall Extruder (RakingWallExtruderV3DG).
/// <para>
/// Taken from the working knowledge write-up for the machine, with the axis tags corrected
/// against a real M20716 export. Anything the write-up flagged as unsure is carried through as
/// <see cref="Confidence.Unconfirmed"/> rather than quietly promoted to fact.
/// </para>
/// </summary>
public static class RakedWallExtruderKnowledge
{
    public static MachineKnowledge Build() => new()
    {
        Model = "RakingWallExtruderV3DG",
        AlsoMatches = new[] { "RakingWallExtruder", "RakedWallExtruder" },
        Summary =
            "Six-axis raked wall extruder. Shares its control logic and step tag scheme "
            + "(WallExtruderStep, ClsWallExtruderPLC) with the standard Wall Extruder, with the "
            + "raked parts - floating head laser check and rake-angle positioning - layered on top.",

        KnownSerials = new[] { "M19820", "M20822", "M20716" },

        Axes = new AxisRole[]
        {
            new()
            {
                LogName = "FixedSidePuller",
                PlainName = "Fixed side gripper",
                Role = "Holds the fixed-side plate. Stays put.",
                Confidence = Confidence.Inferred
            },
            new()
            {
                LogName = "FloatingSidePuller",
                PlainName = "Floating side gripper",
                Role = "Holds the floating-side plate, moves across to set panel width.",
                Confidence = Confidence.Inferred
            },
            new()
            {
                LogName = "FloatingSideHeight",
                PlainName = "Floating side head (trolley head)",
                Role = "Positions vertically for each stud height, following the rake progression.",
                Confidence = Confidence.Inferred
            },
            new()
            {
                LogName = "TrolleyHeight",
                PlainName = "Trolley height - most likely the axis written down as \"Trolley Heart\"",
                Role =
                    "Second vertical axis, separate from FloatingSideHeight. The write-up listed a "
                    + "sixth axis called \"Trolley Heart\" with its role unknown; the real log has no "
                    + "such tag but does have TrolleyHeight, which looks like the same axis written "
                    + "down by ear. Its exact job is still not established.",
                Confidence = Confidence.Inferred
            },
            new()
            {
                LogName = "FixedEjectServo",
                PlainName = "Fixed side ejector",
                Role = "Pushes the finished panel out, fixed side.",
                Confidence = Confidence.Inferred
            },
            new()
            {
                LogName = "FloatingEjectServo",
                PlainName = "Floating side ejector",
                Role = "Pushes the finished panel out, floating side.",
                Confidence = Confidence.Inferred
            }
        },

        Faults = new KnownFault[]
        {
            new()
            {
                Match = "Floating Side Safety Bar Pressed",
                Meaning =
                    "The floating side safety bar is reading as pressed, so the machine will not "
                    + "make any dangerous movement until an E-Stop reset clears it. This is the "
                    + "usual reason for \"nothing moves when I press start\".",
                WhatToCheck = new[]
                {
                    "Is the bar actually being leaned on, or is something resting against it?",
                    "Does the fault clear on an E-Stop reset, and does it come straight back?",
                    "If it comes straight back with the bar clear, suspect the bar switch or its wiring."
                },
                Confidence = Confidence.Confirmed
            },
            new()
            {
                Match = "Cannot Move Towards Operator While Grippers Are Down",
                Meaning =
                    "Movement towards the operator is blocked because the grippers are still down. "
                    + "The panel needs checking and the job restarting.",
                WhatToCheck = new[]
                {
                    "Check the stud pins DOWN sensor and the horizontal clamp back-down sensor.",
                    "Past 400mm, also check the top stud clamp released sensor.",
                    "Grippers only advance with all of those made - any one dropping out stops them mid-travel."
                },
                Confidence = Confidence.Unconfirmed
            },
            new()
            {
                Match = "Lost product, revert and try again",
                Meaning =
                    "A product sensor stopped seeing timber. Can be a real lost or moved plate, or a "
                    + "PlatePresentSwitch glitch - the two look different in the log.",
                WhatToCheck = new[]
                {
                    "Did both plate sensors (fixed 4.2 and floating 4.4) change at the same timestamp?",
                    "Was there a plate clamp output change nearby? Both yes = a real clamp release, not a glitch.",
                    "One side only, with no clamp output change and a 1-2 second self-recovery = sensor glitch."
                },
                Confidence = Confidence.Confirmed
            },
            new()
            {
                Match = "Servo Not Setup",
                Meaning =
                    "A servo reported itself not set up. It normally clears in under 0.2s without "
                    + "raising a proper fault, which can leave the machine quietly waiting with "
                    + "nothing on screen to tell the operator why.",
                WhatToCheck = new[]
                {
                    "If the operator reports the machine just sitting there doing nothing, this is a candidate.",
                    "Check which axis it was and whether it repeats at the same step."
                },
                Confidence = Confidence.Unconfirmed
            }
        },

        Issues = new KnownIssue[]
        {
            new()
            {
                Title = "Overcurrent / E-Stop faults on synchronised axis groups",
                Serials = new[] { "M19820", "M20822", "M20716" },
                Detail =
                    "Reported on all three known serials, root cause not pinned down. Worth checking "
                    + "the servo current logs and whether it clusters on a particular axis pairing.",
                Confidence = Confidence.Unconfirmed,
                Signals = new[] { "overcurrent", "over current", "axis fault", "drive fault" }
            },
            new()
            {
                Title = "Product sensor interlock stopping the machine homing",
                Serials = new[] { "M19820", "M20822", "M20716" },
                Detail =
                    "Both GripperProductSensor inputs and both PlatePresentSwitch inputs must read 0 "
                    + "before the machine will home. Any one of them reading 1 means something is "
                    + "still being detected and the home command is refused, with nothing on screen "
                    + "to say why. Seen on M20716 on 27 Jul 2026: GripperProductSensor 4.6 went to 1 "
                    + "at the same moment as the first HomeServos command, two home commands did "
                    + "nothing, and no axis moved for 39 seconds. The report checks this on every "
                    + "file - see \"CAN IT HOME?\".",
                Confidence = Confidence.Confirmed,
                Signals = new[] { "interlock", "will not home", "cannot home", "home failed" }
            },
            new()
            {
                Title = "Ejection software bug - floating side node not disabled mid-eject",
                Serials = Array.Empty<string>(),
                Detail =
                    "Confirmed and a bug report is filed: the faulting floating-side node does not "
                    + "receive its disable command part way through the eject. Any ejection-stage "
                    + "fault should be checked against this before looking for a sensor or wiring "
                    + "cause. Follow-up work on ejection timing and an outfeed conveyor is open.",
                Confidence = Confidence.Confirmed,
                Signals = new[] { "eject", "ejector" }
            }
        },

        BackgroundNoise = new[]
        {
            "CIP driver comms timeouts.",
            "\"Clamps Air supply pressure is low\" right at startup or just after an E-Stop reset - "
                + "only worth flagging if it comes back well after that.",
            "Node0 and Node1 arriving about 15 seconds ahead of Node2 and Node3 during presetup."
        },

        CustomerQuestions = new[]
        {
            "Has this fault started recently, or has it always done this?",
            "Every panel, or only some? If only some - is there a pattern by panel size, rake angle or timber size?",
            "Has anyone been near that sensor, cable or axis lately - cleaning, maintenance, or a knock?",
            "Did it happen straight after an E-Stop?",
            "For an ejection fault: does it look like the known floating-side node disable bug, or different?"
        },

        OpenGaps = new[]
        {
            "What the TrolleyHeight axis actually does, and whether it is the \"Trolley Heart\" from the write-up.",
            "Whether the Node0-Node3 mapping from the standard Wall Extruder playbook holds on a raked unit. "
                + "The raked log lists Node0 to Node5 as status lines and names the axes separately, so the "
                + "two have not been tied together yet.",
            "Which circuits actually keep or lose power and air on an E-Stop.",
            "Whether the overcurrent faults on the three serials share a root cause or are separate problems.",
            "What the WallExtruderStep numbers mean. A real M20716 export ran 0, 10, 300, 302 then back "
                + "to 0, which does not line up with the 1-25 operator sequence in the write-up, so the "
                + "two must not be read as the same numbering."
        }
    };
}
