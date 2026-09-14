using DiagFileMonitor.Core.Models;
using DiagFileMonitor.Core.Services;

namespace DiagFileMonitor.Core.Tests;

public class BurstDetectorTests
{
    private static readonly DateTime Now = new(2026, 9, 14, 12, 0, 0, DateTimeKind.Utc);

    private static DiagnosticFileSummary Row(string serial, double hoursAgo) => new()
    {
        SerialNumber = serial,
        Status = "Processed",
        ArrivedAtUtc = Now.AddHours(-hoursAgo)
    };

    [Fact]
    public void OneBundleIsNotABurst()
    {
        var trigger = Row("SN-1", 0);

        var result = BurstDetector.Evaluate([trigger], trigger);

        Assert.False(result.IsBurst);
        Assert.Equal(1, result.BundleCount);
    }

    [Fact]
    public void SecondBundleInsideTwentyFourHoursIsABurst()
    {
        var earlier = Row("SN-1", 5);
        var trigger = Row("SN-1", 0);

        var result = BurstDetector.Evaluate([earlier, trigger], trigger);

        Assert.True(result.IsBurst);
        Assert.Equal(2, result.BundleCount);
        Assert.Equal("SN-1", result.SerialNumber);
        Assert.Contains("2 diagnostic files", result.Headline);
    }

    [Fact]
    public void BundlesOlderThanTheWindowDoNotCount()
    {
        var old = Row("SN-1", 30);
        var trigger = Row("SN-1", 0);

        var result = BurstDetector.Evaluate([old, trigger], trigger);

        Assert.False(result.IsBurst);
        Assert.Equal(1, result.BundleCount);
    }

    [Fact]
    public void WindowIsRollingFromTheTriggerNotFromMidnight()
    {
        // 23 hours earlier is still inside a rolling 24 hours even if it was "yesterday".
        var earlier = Row("SN-1", 23);
        var trigger = Row("SN-1", 0);

        Assert.True(BurstDetector.Evaluate([earlier, trigger], trigger).IsBurst);
    }

    [Fact]
    public void OtherMachinesDoNotContribute()
    {
        var other = Row("SN-2", 1);
        var trigger = Row("SN-1", 0);

        var result = BurstDetector.Evaluate([other, trigger], trigger);

        Assert.False(result.IsBurst);
        Assert.Equal(1, result.BundleCount);
    }

    [Fact]
    public void UnknownSerialsAreNeverABurst()
    {
        var first = Row(DiagnosticFileSummary.Unknown, 1);
        var trigger = Row(DiagnosticFileSummary.Unknown, 0);

        Assert.False(BurstDetector.Evaluate([first, trigger], trigger).IsBurst);
    }

    [Fact]
    public void HonoursAHigherThreshold()
    {
        var rows = new[] { Row("SN-1", 5), Row("SN-1", 2), Row("SN-1", 0) };

        Assert.False(BurstDetector.Evaluate(rows, rows[1], threshold: 3).IsBurst);
        Assert.True(BurstDetector.Evaluate(rows, rows[2], threshold: 3).IsBurst);
    }

    [Fact]
    public void IgnoresBundlesThatArrivedAfterTheTrigger()
    {
        // Re-evaluating an older bundle must not count ones that came later.
        var trigger = Row("SN-1", 10);
        var later = Row("SN-1", 0);

        var result = BurstDetector.Evaluate([trigger, later], trigger);

        Assert.False(result.IsBurst);
        Assert.Equal(1, result.BundleCount);
    }

    [Fact]
    public void ReturnsTheBundlesInTheWindowOldestFirst()
    {
        var first = Row("SN-1", 8);
        var second = Row("SN-1", 3);
        var trigger = Row("SN-1", 0);

        var result = BurstDetector.Evaluate([trigger, second, first], trigger);

        Assert.Equal([first.ArrivedAtUtc, second.ArrivedAtUtc, trigger.ArrivedAtUtc],
            result.Bundles.Select(b => b.ArrivedAtUtc));
    }
}
