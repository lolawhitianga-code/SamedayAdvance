namespace DiagFileMonitor.Core.Knowledge;

/// <summary>
/// What is known about the Tornado M500 saw, from an on-site infeed troubleshooting session.
/// The faults here were captured as they happened rather than reconstructed afterwards, so the
/// symptoms and the verbatim error text are reliable; the permanent fix for the laser reflection
/// is not settled and is marked accordingly.
/// </summary>
public static class TornadoKnowledge
{
    /// <summary>
    /// The printer has no software height setting, so this is the whole adjustment. Ordered
    /// thickest timber first, which is also highest printer position.
    /// </summary>
    public static readonly IReadOnlyList<(string Timber, string Thickness, string Printer)> PrinterHeights = new[]
    {
        ("New Zealand", "thicker", "higher"),
        ("Australian", "standard", "mid"),
        ("American", "thinner", "lower"),
        ("Sterling", "thinnest", "lowest")
    };

    /// <summary>
    /// Members shorter than this drop to the waste conveyor below the saw cabinet rather than
    /// going out on the outfeed rollers. The conveyor runs right for waste, left for a usable
    /// offcut, and a short support flap rises before the cut so the piece cannot fall inside.
    /// </summary>
    public const int ShortMemberMillimetres = 520;

    /// <summary>The normal run-up, for telling how far a machine got before it stopped.</summary>
    public static readonly IReadOnlyList<string> StartupSequence = new[]
    {
        "Power on.",
        "Press Home Machine - all 7 servo axes reset: pusher, saw blade angle, saw blade offset, "
            + "saw up/down, top printer up/down, outfeed rollers, infeed rollers.",
        "Open the job file and confirm the members show in the Members tab.",
        "Check the Infeed tab: board created, members assigned, stock board listed in the stock table.",
        "Review board selection - the machine suggests a stack height of 1, 2 or 3 high from the job.",
        "If there is spare material on the board, the operator can process the waste into blocks "
            + "from the standard offcut table.",
        "Press OK, then load boards onto the infeed chain conveyor.",
        "Press Start Board to begin the automated run."
    };

    public static MachineKnowledge Build() => new()
    {
        Model = "TornadoM500",
        AlsoMatches = new[] { "Tornado M500", "TornadoM500" },

        KnownSerials = new[] { "M20421" },

        // The seven axes from the op guide flowchart, paired with the tags a real M20421 export
        // uses. The geometry ones carry their letter in the log tag, which makes those solid; the
        // pusher is the one that has not been tied to a tag.
        Axes = new AxisRole[]
        {
            new()
            {
                LogName = "SawRotation(R)", PlainName = "Saw blade angle",
                Role = "Swings the blade to the cut angle. The R axis in the machine config.",
                Confidence = Confidence.Confirmed
            },
            new()
            {
                LogName = "SawOffset(Y)", PlainName = "Saw blade offset",
                Role = "Moves the blade forward and back. The Y axis.",
                Confidence = Confidence.Confirmed
            },
            new()
            {
                LogName = "SawHeight(Z)", PlainName = "Saw up/down",
                Role = "Raises and lowers the blade. The Z axis.",
                Confidence = Confidence.Confirmed
            },
            new()
            {
                LogName = "InBelt(X1)", PlainName = "Infeed rollers",
                Role = "Drives timber in. XInAxis in the machine config.",
                Confidence = Confidence.Inferred
            },
            new()
            {
                LogName = "OutBelt(X2)", PlainName = "Outfeed rollers",
                Role = "Drives cut members out. XOutAxis in the machine config.",
                Confidence = Confidence.Inferred
            },
            new()
            {
                LogName = "TopTimPrinterServo", PlainName = "Top printer up/down",
                Role = "Sets printer height. This is the one with no software setting for its "
                       + "physical position - see the print fault below.",
                Confidence = Confidence.Inferred
            },
            new()
            {
                LogName = "(not yet identified)", PlainName = "Pusher",
                Role = "The op guide lists a pusher as one of the seven homed axes. No tag in a "
                       + "real export has been tied to it yet.",
                Confidence = Confidence.Unconfirmed
            }
        },

        Faults = new KnownFault[]
        {
            new()
            {
                Match = "Board not expected length",
                Meaning =
                    "The post laser is reading the board far longer than it is - 4,719mm reported "
                    + "for a board of about 2,000mm in the case seen. The laser reflects off the "
                    + "polished metal infeed deck and picks up a false return. The gripper eyes and "
                    + "entry sensor all read the timber correctly, so the sensors are not the problem.",
                WhatToCheck = new[]
                {
                    "If the reported length does not match the board and looks random, suspect the "
                        + "laser reflecting off the deck before assuming the wrong board was loaded.",
                    "Black tape on the deck reduces the reflection but is not reliable - it can still "
                        + "happen intermittently.",
                    "A permanent fix is not settled. Worth trying: matte paint or coating on the deck, "
                        + "rubber or matte matting, or adjusting the laser angle or sensitivity if that "
                        + "can be configured. Ask Spida whether there is a recommended fix."
                },
                Confidence = Confidence.Confirmed
            },
            new()
            {
                Match = "Board not expected size",
                Meaning =
                    "The analog clamp sensor measured the board outside the roughly 10% tolerance. In "
                    + "the case seen the timber really was undersized - 90x45mm expected against "
                    + "89x39.2mm measured, about 13% under on width. The board loads and advances "
                    + "normally and every sensor triggers correctly first.",
                WhatToCheck = new[]
                {
                    "Look at the timber. If it is clearly the wrong size, reload the correct stock.",
                    "If it looks acceptable and is only slightly out, press Continue - the machine "
                        + "runs the full cut sequence normally. That is what happened in the case seen.",
                    "Repeated size errors on timber that measures correctly would point at the clamp "
                        + "sensor instead, but that has not been seen."
                },
                Confidence = Confidence.Confirmed
            }
        },

        Issues = new KnownIssue[]
        {
            new()
            {
                Title = "Nothing loads after pressing Start Board",
                Serials = Array.Empty<string>(),
                Detail =
                    "Before any infeed loading all four cabinet clamps must read OPEN on their reed "
                    + "switches - two horizontal, two vertical - both cabinet eye sensors must be "
                    + "clear of timber, and the top printer must be up above the stack. If an eye "
                    + "sensor still sees timber when Start Board is pressed, the infeed does not "
                    + "load and the infeed rollers run backwards to eject until it clears. The "
                    + "usual cause is sawdust on the cabinet eye sensors giving a false detection: "
                    + "clean them and retry.",
                Confidence = Confidence.Confirmed,
                Signals = new[] { "cabinet", "eye sensor", "will not load", "does not load" }
            },
            new()
            {
                Title = "Print running off the edge of the timber",
                Serials = Array.Empty<string>(),
                Detail =
                    "The top printer servo is set too high for the timber thickness. There is no "
                    + "software setting - the top printer has to be moved physically. Print riding "
                    + "off the top edge means lower it; print sitting too low on the face means raise "
                    + "it. Timber thickness runs New Zealand (thickest, printer highest), Australian, "
                    + "American, then Sterling (thinnest, printer lowest), so the height wants "
                    + "checking whenever the timber type changes.",
                Confidence = Confidence.Confirmed,
                Signals = new[] { "print", "printer", "label", "ink" }
            }
        },

        BackgroundNoise = Array.Empty<string>(),

        CustomerQuestions = new[]
        {
            "Does the reported length or size match the board that was actually loaded?",
            "Has the timber type changed recently - a different country's stock runs a different thickness?",
            "Is it every board, or only some? If only some, is there a pattern by board length or timber type?",
            "Has anything been done to the infeed deck surface - cleaning, polishing, tape removed?"
        },

        OpenGaps = new[]
        {
            "A permanent fix for the post laser reflecting off the infeed deck. Black tape is only a "
                + "partial measure and Spida have not been asked yet whether this is a known issue.",
            "Which log tag is the pusher. The op guide names seven homed axes and six are now "
                + "matched to tags in a real M20421 export; the pusher is not.",
            "Whether a \"Board not expected size\" error ever comes from the clamp sensor drifting "
                + "rather than from genuinely undersized timber."
        }
    };
}
