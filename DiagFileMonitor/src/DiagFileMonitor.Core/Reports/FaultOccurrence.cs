using DiagFileMonitor.Core.Knowledge;

namespace DiagFileMonitor.Core.Reports;

public enum FaultKind
{
    /// <summary>One side of the plate sensor dropped on its own - a sensor glitch, not lost product.</summary>
    PlateSensorGlitch,

    /// <summary>A plate sensor event that fits neither the normal nor the glitch pattern.</summary>
    PlateSensorUnclear,

    /// <summary>A code raised by the motion controller or servo drive.</summary>
    DriveFault,

    /// <summary>A motor told to run that never reported back that it was running.</summary>
    MotorNotConfirmed,

    /// <summary>A fault the machine knowledge base recognises.</summary>
    KnownMachineFault,

    /// <summary>A software error out of ErrLog.txt.</summary>
    SoftwareError
}

/// <summary>
/// One fault, at one moment, on one machine. The benchmarking report lists these in full rather
/// than grouping them - a count tells you a pattern exists, an occurrence list lets someone check
/// whether the reading is right.
/// </summary>
public record FaultOccurrence
{
    public FaultKind Kind { get; init; }

    /// <summary>Date the bundle arrived, plus the time of day from the log.</summary>
    public DateTime WhenUtc { get; init; }

    /// <summary>True where only the bundle's arrival date is known, not the moment in the log.</summary>
    public bool TimeIsApproximate { get; init; }

    public string SerialNumber { get; init; } = string.Empty;
    public string MachineType { get; init; } = string.Empty;
    public string Site { get; init; } = string.Empty;

    /// <summary>Short name for the fault - the code, the sensor, the motor.</summary>
    public string Signal { get; init; } = string.Empty;

    /// <summary>What it means, in a sentence.</summary>
    public string Detail { get; init; } = string.Empty;

    /// <summary>How long until it recovered, where the log shows it coming back.</summary>
    public TimeSpan? RecoveredAfter { get; init; }

    /// <summary>The machine step it happened in, where the log carries one.</summary>
    public int? Step { get; init; }

    /// <summary>
    /// How sure we are of the reading. Printed on every row - an interpretation that loses its
    /// confidence label is more dangerous in a tidy HTML table than in a plain text report,
    /// because it looks settled.
    /// </summary>
    public Confidence Confidence { get; init; } = Confidence.Inferred;

    /// <summary>Which bundle it came out of, so the row can be traced back.</summary>
    public int DiagnosticFileId { get; init; }
    public string BundleFileName { get; init; } = string.Empty;

    /// <summary>What the operator typed into SupportInfo.txt when they raised the bundle.</summary>
    public string OperatorReported { get; init; } = string.Empty;

    public string TimeText => TimeIsApproximate
        ? WhenUtc.ToString("yyyy-MM-dd") + " (date only)"
        : WhenUtc.ToString("yyyy-MM-dd HH:mm:ss");

    /// <summary>
    /// The same moment over two lines, date then time. A timestamp is nineteen characters and
    /// will not share a table row with five other columns on one line.
    /// </summary>
    public string TimeCell => WhenUtc.ToString("yyyy-MM-dd") + "\n"
                              + (TimeIsApproximate ? "date only" : WhenUtc.ToString("HH:mm:ss"));
}
