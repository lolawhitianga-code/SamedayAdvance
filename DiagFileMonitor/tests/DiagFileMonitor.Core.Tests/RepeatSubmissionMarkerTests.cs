using DiagFileMonitor.Core.Models;
using DiagFileMonitor.Core.Services;

namespace DiagFileMonitor.Core.Tests;

public class RepeatSubmissionMarkerTests
{
    private static DiagnosticFileSummary Row(string serial, DateTime arrivedLocal) => new()
    {
        SerialNumber = serial,
        Status = "Processed",
        ArrivedAtUtc = arrivedLocal.ToUniversalTime()
    };

    private static DateTime Day(int day) => new(2026, 9, day, 9, 0, 0, DateTimeKind.Local);

    [Fact]
    public void FirstBundleFromAMachineIsNeverARepeat()
    {
        var rows = new[] { Row("SN-1", Day(1)) };

        RepeatSubmissionMarker.Mark(rows);

        Assert.False(rows[0].IsRepeatSubmission);
    }

    [Fact]
    public void SecondBundleInsideTheWindowIsARepeat()
    {
        var first = Row("SN-1", Day(1));
        var second = Row("SN-1", Day(4));

        RepeatSubmissionMarker.Mark([first, second]);

        Assert.False(first.IsRepeatSubmission);
        Assert.True(second.IsRepeatSubmission);
    }

    [Fact]
    public void SecondBundleOutsideTheWindowIsNotARepeat()
    {
        var first = Row("SN-1", Day(1));
        var second = Row("SN-1", Day(20));

        RepeatSubmissionMarker.Mark([first, second]);

        Assert.False(second.IsRepeatSubmission);
    }

    [Fact]
    public void WindowIsMeasuredAgainstThePrecedingBundleNotTheFirst()
    {
        // Each step is inside the window, so a slow drip still reads as repeats.
        var rows = new[] { Row("SN-1", Day(1)), Row("SN-1", Day(7)), Row("SN-1", Day(13)) };

        RepeatSubmissionMarker.Mark(rows);

        Assert.False(rows[0].IsRepeatSubmission);
        Assert.True(rows[1].IsRepeatSubmission);
        Assert.True(rows[2].IsRepeatSubmission);
    }

    [Fact]
    public void DifferentMachinesDoNotMakeEachOtherRepeats()
    {
        var a = Row("SN-1", Day(1));
        var b = Row("SN-2", Day(2));

        RepeatSubmissionMarker.Mark([a, b]);

        Assert.False(a.IsRepeatSubmission);
        Assert.False(b.IsRepeatSubmission);
    }

    [Fact]
    public void UnknownSerialsAreNeverTreatedAsTheSameMachine()
    {
        var a = Row(DiagnosticFileSummary.Unknown, Day(1));
        var b = Row(DiagnosticFileSummary.Unknown, Day(2));

        RepeatSubmissionMarker.Mark([a, b]);

        Assert.False(a.IsRepeatSubmission);
        Assert.False(b.IsRepeatSubmission);
    }

    [Fact]
    public void HonoursACustomWindow()
    {
        var first = Row("SN-1", Day(1));
        var second = Row("SN-1", Day(20));

        RepeatSubmissionMarker.Mark([first, second], windowDays: 30);

        Assert.True(second.IsRepeatSubmission);
    }

    [Fact]
    public void MarkingIsIdempotentAndClearsStaleFlags()
    {
        var first = Row("SN-1", Day(1));
        var second = Row("SN-1", Day(20));
        second.IsRepeatSubmission = true; // stale flag from an earlier, wider window

        RepeatSubmissionMarker.Mark([first, second]);

        Assert.False(second.IsRepeatSubmission);
    }

    [Fact]
    public void RepeatLabelIsBlankWhenNotARepeat()
    {
        var row = Row("SN-1", Day(1));

        Assert.Equal(string.Empty, row.RepeatLabel);
        row.IsRepeatSubmission = true;
        Assert.Equal("Repeat", row.RepeatLabel);
    }
}
