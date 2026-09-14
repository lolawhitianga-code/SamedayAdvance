namespace DiagFileMonitor.Core.Knowledge;

/// <summary>
/// How well a piece of knowledge is established. Written into the report next to each item so a
/// guess is never read as a fact when it is quoted back to a customer.
/// </summary>
public enum Confidence
{
    /// <summary>Seen in a real log or a filed bug report.</summary>
    Confirmed,

    /// <summary>Follows from something confirmed, but has not been observed directly.</summary>
    Inferred,

    /// <summary>Written down from experience and still to be checked against a real log.</summary>
    Unconfirmed
}

public static class ConfidenceText
{
    public static string Label(this Confidence confidence) => confidence switch
    {
        Confidence.Confirmed => "confirmed",
        Confidence.Inferred => "inferred",
        _ => "UNCONFIRMED"
    };
}

/// <summary>One of the machine's axes, as it is named in MachineLog.txt.</summary>
public class AxisRole
{
    /// <summary>The tag the log uses, for example <c>FloatingSideHeight</c>.</summary>
    public string LogName { get; init; } = string.Empty;

    /// <summary>What a support person would call it.</summary>
    public string PlainName { get; init; } = string.Empty;

    public string Role { get; init; } = string.Empty;

    /// <summary>How sure the plain name and role are, as opposed to the log tag itself.</summary>
    public Confidence Confidence { get; init; } = Confidence.Unconfirmed;
}

/// <summary>A fault message this machine family is known to produce, and what to do about it.</summary>
public class KnownFault
{
    /// <summary>Matched against the fault text, case insensitively, as a substring.</summary>
    public string Match { get; init; } = string.Empty;

    public string Meaning { get; init; } = string.Empty;

    public IReadOnlyList<string> WhatToCheck { get; init; } = Array.Empty<string>();

    public Confidence Confidence { get; init; } = Confidence.Unconfirmed;
}

/// <summary>A problem already reported on this machine family, so it is not chased again from scratch.</summary>
public class KnownIssue
{
    public string Title { get; init; } = string.Empty;

    /// <summary>Serials it has been reported on. Empty means the whole family.</summary>
    public IReadOnlyList<string> Serials { get; init; } = Array.Empty<string>();

    public string Detail { get; init; } = string.Empty;

    public Confidence Confidence { get; init; } = Confidence.Unconfirmed;

    /// <summary>Log text that suggests this issue is the one in play, matched case insensitively.</summary>
    public IReadOnlyList<string> Signals { get; init; } = Array.Empty<string>();
}

/// <summary>Everything known about one machine model.</summary>
public class MachineKnowledge
{
    public string Model { get; init; } = string.Empty;

    /// <summary>Other model names this knowledge also covers.</summary>
    public IReadOnlyList<string> AlsoMatches { get; init; } = Array.Empty<string>();

    public string Summary { get; init; } = string.Empty;

    public IReadOnlyList<string> KnownSerials { get; init; } = Array.Empty<string>();
    public IReadOnlyList<AxisRole> Axes { get; init; } = Array.Empty<AxisRole>();
    public IReadOnlyList<KnownFault> Faults { get; init; } = Array.Empty<KnownFault>();
    public IReadOnlyList<KnownIssue> Issues { get; init; } = Array.Empty<KnownIssue>();

    /// <summary>Things that turn up every session and are not worth chasing on their own.</summary>
    public IReadOnlyList<string> BackgroundNoise { get; init; } = Array.Empty<string>();

    /// <summary>Worth asking the customer whatever the logs show.</summary>
    public IReadOnlyList<string> CustomerQuestions { get; init; } = Array.Empty<string>();

    /// <summary>Known to be unknown. Printed so the gaps stay visible instead of being forgotten.</summary>
    public IReadOnlyList<string> OpenGaps { get; init; } = Array.Empty<string>();

    public bool Matches(string? model)
    {
        if (string.IsNullOrWhiteSpace(model)) return false;

        return Model.Equals(model, StringComparison.OrdinalIgnoreCase)
               || AlsoMatches.Any(m => m.Equals(model, StringComparison.OrdinalIgnoreCase))
               || AlsoMatches.Any(m => model.Contains(m, StringComparison.OrdinalIgnoreCase));
    }
}
