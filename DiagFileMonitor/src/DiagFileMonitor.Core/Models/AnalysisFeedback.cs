namespace DiagFileMonitor.Core.Models;

/// <summary>How well the report did, in the support person's judgement.</summary>
public enum FeedbackVerdict
{
    /// <summary>It found the real problem. Worth keeping - a working example is as useful as a failure.</summary>
    GotItRight,

    /// <summary>It found something real but missed the point, or buried it.</summary>
    PartlyRight,

    /// <summary>The real cause was in the files and the report did not mention it.</summary>
    MissedIt,

    /// <summary>It led somewhere wrong, which costs more time than saying nothing.</summary>
    SentMeTheWrongWay
}

/// <summary>
/// What a support person knows about a bundle that the report did not. This is the raw material
/// for improving the analysis: the report's output, the real answer, and - the part that actually
/// becomes code - how they knew.
/// </summary>
public class AnalysisFeedback
{
    public FeedbackVerdict Verdict { get; init; }

    /// <summary>The real fault, in their words.</summary>
    public string WhatWasActuallyWrong { get; init; } = string.Empty;

    /// <summary>
    /// Which lines, signals or checks told them. The most valuable field: "output came on but the
    /// confirm input never did" is what turns into a check, where "the saw was broken" is not.
    /// </summary>
    public string HowYouKnew { get; init; } = string.Empty;

    /// <summary>What the report should have said or done instead. Optional.</summary>
    public string WhatShouldChange { get; init; } = string.Empty;

    /// <summary>Who raised it, so a follow-up question has somewhere to go. Optional.</summary>
    public string RaisedBy { get; init; } = string.Empty;

    public DateTime RaisedUtc { get; init; } = DateTime.UtcNow;

    /// <summary>Nothing useful to learn from without at least the ground truth.</summary>
    public bool IsUsable => WhatWasActuallyWrong.Trim().Length > 0 || HowYouKnew.Trim().Length > 0;

    public string VerdictText => Verdict switch
    {
        FeedbackVerdict.GotItRight => "got it right",
        FeedbackVerdict.PartlyRight => "partly right - found something real but missed the point",
        FeedbackVerdict.MissedIt => "missed it - the cause was in the files and the report did not mention it",
        _ => "sent me the wrong way"
    };
}
