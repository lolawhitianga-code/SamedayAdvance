using DiagFileMonitor.Core.Models;
using DiagFileMonitor.Core.Services;

namespace DiagFileMonitor.Core.Tests;

public class DiagnosticFileFilterTests
{
    private static DiagnosticFileSummary Row(
        string serial = "SN-00123",
        string customer = "Wellington Roasters Ltd",
        string fileName = "diag_2026.zip",
        string machineType = "CoffeeRoaster-5000",
        string status = "Processed",
        DateTime? arrivedLocal = null) => new()
    {
        SerialNumber = serial,
        Customer = customer,
        OriginalFileName = fileName,
        MachineType = machineType,
        Status = status,
        ArrivedAtUtc = (arrivedLocal ?? new DateTime(2026, 9, 10, 12, 0, 0, DateTimeKind.Local)).ToUniversalTime()
    };

    [Fact]
    public void EmptyCriteriaMatchesEverything()
    {
        Assert.True(DiagnosticFileFilter.Matches(Row(), new FilterCriteria()));
        Assert.True(new FilterCriteria().IsEmpty);
    }

    [Theory]
    [InlineData("SN-00123")]
    [InlineData("sn-00123")]
    [InlineData("wellington")]
    [InlineData("diag_2026")]
    [InlineData("CoffeeRoaster")]
    public void SearchMatchesAcrossFields(string search)
    {
        Assert.True(DiagnosticFileFilter.Matches(Row(), new FilterCriteria { SearchText = search }));
    }

    [Fact]
    public void SearchRejectsNonMatch()
    {
        Assert.False(DiagnosticFileFilter.Matches(Row(), new FilterCriteria { SearchText = "auckland" }));
    }

    [Fact]
    public void AllSearchTermsMustMatch()
    {
        var row = Row();
        Assert.True(DiagnosticFileFilter.Matches(row, new FilterCriteria { SearchText = "SN-00123 wellington" }));
        Assert.False(DiagnosticFileFilter.Matches(row, new FilterCriteria { SearchText = "SN-00123 auckland" }));
    }

    [Fact]
    public void StatusFilterNarrowsToOneStatus()
    {
        var processed = Row(status: "Processed");
        var errored = Row(status: "Error");

        Assert.True(DiagnosticFileFilter.Matches(processed, new FilterCriteria { Status = "Processed" }));
        Assert.False(DiagnosticFileFilter.Matches(errored, new FilterCriteria { Status = "Processed" }));
        Assert.True(DiagnosticFileFilter.Matches(errored, new FilterCriteria { Status = DiagnosticFileFilter.AnyStatus }));
    }

    [Fact]
    public void DateRangeIsInclusiveOfBothEnds()
    {
        var row = Row(arrivedLocal: new DateTime(2026, 9, 10, 15, 30, 0, DateTimeKind.Local));

        Assert.True(DiagnosticFileFilter.Matches(row, new FilterCriteria { FromDate = new DateTime(2026, 9, 10) }));
        Assert.True(DiagnosticFileFilter.Matches(row, new FilterCriteria { ToDate = new DateTime(2026, 9, 10) }));
        Assert.True(DiagnosticFileFilter.Matches(row, new FilterCriteria
        {
            FromDate = new DateTime(2026, 9, 1),
            ToDate = new DateTime(2026, 9, 30)
        }));
    }

    [Fact]
    public void DateRangeExcludesOutsideDays()
    {
        var row = Row(arrivedLocal: new DateTime(2026, 9, 10, 15, 30, 0, DateTimeKind.Local));

        Assert.False(DiagnosticFileFilter.Matches(row, new FilterCriteria { FromDate = new DateTime(2026, 9, 11) }));
        Assert.False(DiagnosticFileFilter.Matches(row, new FilterCriteria { ToDate = new DateTime(2026, 9, 9) }));
    }

    [Fact]
    public void CombinesSearchStatusAndDate()
    {
        var row = Row(arrivedLocal: new DateTime(2026, 9, 10, 9, 0, 0, DateTimeKind.Local));
        var criteria = new FilterCriteria
        {
            SearchText = "wellington",
            Status = "Processed",
            FromDate = new DateTime(2026, 9, 1),
            ToDate = new DateTime(2026, 9, 30)
        };

        Assert.True(DiagnosticFileFilter.Matches(row, criteria));

        criteria.Status = "Error";
        Assert.False(DiagnosticFileFilter.Matches(row, criteria));
    }

    [Fact]
    public void SummaryFallsBackToUnknownForMissingFields()
    {
        var summary = DiagnosticFileSummary.FromEntity(new DiagnosticFile
        {
            OriginalFileName = "x.zip",
            SerialNumber = null,
            Customer = "   ",
            Status = ProcessingStatus.Error
        });

        Assert.Equal(DiagnosticFileSummary.Unknown, summary.SerialNumber);
        Assert.Equal(DiagnosticFileSummary.Unknown, summary.Customer);
        Assert.Equal("Error", summary.Status);
    }
}
