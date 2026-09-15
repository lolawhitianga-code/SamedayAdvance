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

    /// <summary>The normal run-up, for telling how far a machine got before it stopped.</summary>
    public static readonly IReadOnlyList<string> StartupSequence = new[]
    {
        "Power on.",
        "Press Home Machine - all 7 servo axes reset.",
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

        KnownSerials = Array.Empty<string>(),
        Axes = Array.Empty<AxisRole>(),

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
            "Whether the M500 axis names in MachineLog.txt match the M450, whose config lists "
                + "XInAxis, XOutAxis, YAxis, ZAxis and RAxis - five, where the M500 is described as "
                + "having seven servo axes. No M500 log has been read yet.",
            "Whether a \"Board not expected size\" error ever comes from the clamp sensor drifting "
                + "rather than from genuinely undersized timber."
        }
    };
}
