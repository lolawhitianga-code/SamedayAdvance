using DiagFileMonitor.Core.Models;
using DiagFileMonitor.Core.Services;

namespace DiagFileMonitor.Core.Tests;

public class MachineHistoryTests
{
    private static DiagnosticFileSummary Row(string serial, DateTime arrivedLocal, string version = "2.4.1",
        string customer = "Cafe A", string status = "Processed") => new()
    {
        SerialNumber = serial,
        Version = version,
        Customer = customer,
        Status = status,
        ArrivedAtUtc = arrivedLocal.ToUniversalTime()
    };

    [Fact]
    public void GathersOnlyTheRequestedMachine()
    {
        var rows = new[]
        {
            Row("SN-1", new DateTime(2026, 9, 1, 9, 0, 0, DateTimeKind.Local)),
            Row("SN-1", new DateTime(2026, 9, 10, 9, 0, 0, DateTimeKind.Local)),
            Row("SN-2", new DateTime(2026, 9, 5, 9, 0, 0, DateTimeKind.Local))
        };

        var history = MachineHistory.For(rows, "SN-1");

        Assert.Equal(2, history.BundleCount);
        Assert.Equal(new DateTime(2026, 9, 1), history.FirstSeenLocal!.Value.Date);
        Assert.Equal(new DateTime(2026, 9, 10), history.LastSeenLocal!.Value.Date);
    }

    [Fact]
    public void MatchesSerialCaseInsensitively()
    {
        var rows = new[] { Row("sn-1", new DateTime(2026, 9, 1, 9, 0, 0, DateTimeKind.Local)) };

        Assert.Equal(1, MachineHistory.For(rows, "SN-1").BundleCount);
    }

    [Fact]
    public void TracksVersionsInTheOrderTheyArrived()
    {
        var rows = new[]
        {
            Row("SN-1", new DateTime(2026, 7, 1, 9, 0, 0, DateTimeKind.Local), version: "2.3.0"),
            Row("SN-1", new DateTime(2026, 8, 1, 9, 0, 0, DateTimeKind.Local), version: "2.4.0"),
            Row("SN-1", new DateTime(2026, 9, 1, 9, 0, 0, DateTimeKind.Local), version: "2.4.0")
        };

        var history = MachineHistory.For(rows, "SN-1");

        Assert.Equal(new[] { "2.3.0", "2.4.0" }, history.Versions);
        Assert.Contains("2.3.0 -> 2.4.0", history.Headline);
    }

    [Fact]
    public void CountsFailuresForTheMachine()
    {
        var rows = new[]
        {
            Row("SN-1", new DateTime(2026, 9, 1, 9, 0, 0, DateTimeKind.Local), status: "Error"),
            Row("SN-1", new DateTime(2026, 9, 2, 9, 0, 0, DateTimeKind.Local), status: "Processed")
        };

        var history = MachineHistory.For(rows, "SN-1");

        Assert.Equal(1, history.ErrorCount);
        Assert.Contains("1 failed", history.Headline);
    }

    [Fact]
    public void HandlesAMachineWithNoHistory()
    {
        var history = MachineHistory.For([], "SN-NOPE");

        Assert.Equal(0, history.BundleCount);
        Assert.Null(history.FirstSeenLocal);
        Assert.Contains("No history", history.Headline);
    }

    [Fact]
    public void HeadlineCollapsesASingleDay()
    {
        var rows = new[]
        {
            Row("SN-1", new DateTime(2026, 9, 1, 9, 0, 0, DateTimeKind.Local)),
            Row("SN-1", new DateTime(2026, 9, 1, 14, 0, 0, DateTimeKind.Local))
        };

        Assert.Contains("all on 2026-09-01", MachineHistory.For(rows, "SN-1").Headline);
    }

    [Fact]
    public void ExactSerialFilterDoesNotMatchPartialSerials()
    {
        var row = new DiagnosticFileSummary { SerialNumber = "SN-100", Status = "Processed" };

        Assert.True(DiagnosticFileFilter.Matches(row, new FilterCriteria { SerialNumber = "SN-100" }));
        Assert.False(DiagnosticFileFilter.Matches(row, new FilterCriteria { SerialNumber = "SN-10" }));
    }
}
