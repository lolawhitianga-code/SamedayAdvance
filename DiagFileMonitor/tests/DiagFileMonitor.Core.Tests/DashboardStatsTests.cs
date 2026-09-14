using DiagFileMonitor.Core.Models;

namespace DiagFileMonitor.Core.Tests;

public class DashboardStatsTests
{
    private static readonly DateTime Now = new(2026, 9, 14, 10, 0, 0, DateTimeKind.Local);

    private static DiagnosticFileSummary Row(int daysAgo, string serial = "SN-1", string customer = "Cafe A", string status = "Processed") => new()
    {
        SerialNumber = serial,
        Customer = customer,
        Status = status,
        ArrivedAtUtc = Now.AddDays(-daysAgo).ToUniversalTime()
    };

    [Fact]
    public void EmptyListProducesZeroes()
    {
        var stats = DashboardStats.Calculate([], Now);

        Assert.Equal(0, stats.Total);
        Assert.Equal(0, stats.ArrivedToday);
        Assert.Equal(0, stats.ErrorCount);
        Assert.Equal(0, stats.DistinctMachines);
        Assert.Equal(DashboardStats.None, stats.TopCustomerLast7Days);
    }

    [Fact]
    public void CountsTodayAndTotal()
    {
        var stats = DashboardStats.Calculate([Row(0), Row(0), Row(3), Row(30)], Now);

        Assert.Equal(4, stats.Total);
        Assert.Equal(2, stats.ArrivedToday);
    }

    [Fact]
    public void SevenDayWindowIncludesTodayAndSixDaysBack()
    {
        var stats = DashboardStats.Calculate([Row(0), Row(6), Row(7)], Now);

        Assert.Equal(2, stats.ArrivedLast7Days);
    }

    [Fact]
    public void CountsErrors()
    {
        var stats = DashboardStats.Calculate([Row(0, status: "Error"), Row(0, status: "Processed"), Row(1, status: "Error")], Now);

        Assert.Equal(2, stats.ErrorCount);
    }

    [Fact]
    public void CountsDistinctMachinesIgnoringUnknown()
    {
        var rows = new[]
        {
            Row(0, serial: "SN-1"),
            Row(0, serial: "SN-1"),
            Row(0, serial: "SN-2"),
            Row(0, serial: DiagnosticFileSummary.Unknown)
        };

        Assert.Equal(2, DashboardStats.Calculate(rows, Now).DistinctMachines);
    }

    [Fact]
    public void TopCustomerUsesLastSevenDaysOnly()
    {
        var rows = new[]
        {
            Row(0, customer: "Cafe A"),
            Row(1, customer: "Cafe B"),
            Row(2, customer: "Cafe B"),
            // Older burst that should not win despite being the largest overall.
            Row(20, customer: "Cafe C"),
            Row(21, customer: "Cafe C"),
            Row(22, customer: "Cafe C"),
            Row(23, customer: "Cafe C")
        };

        Assert.Equal("Cafe B (2)", DashboardStats.Calculate(rows, Now).TopCustomerLast7Days);
    }

    [Fact]
    public void TopCustomerIgnoresUnknownCustomers()
    {
        var rows = new[]
        {
            Row(0, customer: DiagnosticFileSummary.Unknown),
            Row(0, customer: DiagnosticFileSummary.Unknown),
            Row(1, customer: "Cafe A")
        };

        Assert.Equal("Cafe A (1)", DashboardStats.Calculate(rows, Now).TopCustomerLast7Days);
    }
}
