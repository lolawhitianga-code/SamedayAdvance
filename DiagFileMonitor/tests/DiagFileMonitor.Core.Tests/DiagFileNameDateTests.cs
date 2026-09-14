using DiagFileMonitor.Core.Services;

namespace DiagFileMonitor.Core.Tests;

public class DiagFileNameDateTests
{
    [Theory]
    // Real names taken from the support folder.
    [InlineData("_7_27_2026 9-53-10 PM . M21461SupportFile.szip", 2026, 7, 27, 21, 53, 10)]
    [InlineData("_7_27_2026 10-15-10 PM . M20716SupportFile.szip", 2026, 7, 27, 22, 15, 10)]
    [InlineData("_10_6_2025 11-01-44 PM . M21036SupportFile.szip", 2025, 10, 6, 23, 1, 44)]
    [InlineData("_1_28_2026 1-00-58 AM . AOR00000SupportFile.szip", 2026, 1, 28, 1, 0, 58)]
    [InlineData("_12_18_2025 11-56-05 AM . AORSupportFile.szip", 2025, 12, 18, 11, 56, 5)]
    [InlineData("_3_13_2025 7-44-35 PM . 20114-6SupportFile.szip", 2025, 3, 13, 19, 44, 35)]
    public void ReadsTheTimestampFromTheName(string name, int year, int month, int day, int hour, int minute, int second)
    {
        var parsed = DiagFileNameDate.TryParseUtc(name, fileNameTimesAreUtc: true);

        Assert.Equal(new DateTime(year, month, day, hour, minute, second, DateTimeKind.Utc), parsed);
    }

    [Fact]
    public void TreatsTheDateAsMonthDayYear()
    {
        // 24 cannot be a month, so these names prove the order.
        var parsed = DiagFileNameDate.TryParseUtc("_10_24_2025 7-13-53 PM . AOR2728SupportFile.szip", true);

        Assert.Equal(10, parsed!.Value.Month);
        Assert.Equal(24, parsed.Value.Day);
    }

    [Theory]
    [InlineData("_1_1_2026 12-00-00 AM . x.szip", 0)]   // midnight
    [InlineData("_1_1_2026 12-30-00 PM . x.szip", 12)]  // noon
    [InlineData("_1_1_2026 11-59-59 PM . x.szip", 23)]
    public void HandlesMiddayAndMidnightCorrectly(string name, int expectedHour)
    {
        Assert.Equal(expectedHour, DiagFileNameDate.TryParseUtc(name, true)!.Value.Hour);
    }

    [Fact]
    public void ConvertsFromLocalTimeWhenTheNameIsNotUtc()
    {
        var asUtc = DiagFileNameDate.TryParseUtc("_7_27_2026 9-53-10 PM . x.szip", fileNameTimesAreUtc: true);
        var asLocal = DiagFileNameDate.TryParseUtc("_7_27_2026 9-53-10 PM . x.szip", fileNameTimesAreUtc: false);

        Assert.Equal(DateTimeKind.Utc, asLocal!.Value.Kind);

        var expected = new DateTime(2026, 7, 27, 21, 53, 10, DateTimeKind.Local).ToUniversalTime();
        Assert.Equal(expected, asLocal);

        // Only identical where the machine runs on UTC.
        if (TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 7, 27)) != TimeSpan.Zero)
        {
            Assert.NotEqual(asUtc, asLocal);
        }
    }

    [Fact]
    public void WorksOnAFullPathNotJustAName()
    {
        var parsed = DiagFileNameDate.TryParseUtc(@"C:\drop\_7_27_2026 9-53-10 PM . M21461SupportFile.szip", true);

        Assert.Equal(new DateTime(2026, 7, 27, 21, 53, 10, DateTimeKind.Utc), parsed);
    }

    [Theory]
    [InlineData("SupportFile.szip")]
    [InlineData("no date here.szip")]
    [InlineData("_13_45_2026 9-53-10 PM . x.szip")]   // month 13, day 45
    [InlineData("_2_30_2026 9-53-10 AM . x.szip")]    // 30 February
    public void ReturnsNothingWhenThereIsNoUsableDate(string name)
    {
        Assert.Null(DiagFileNameDate.TryParseUtc(name, true));
    }

    [Fact]
    public void AcceptsATwentyFourHourNameWithNoAmPm()
    {
        var parsed = DiagFileNameDate.TryParseUtc("_7_27_2026 21-53-10 . x.szip", true);

        Assert.Equal(21, parsed!.Value.Hour);
    }

    [Fact]
    public void FallsBackToTheFileDateWhenTheNameHasNone()
    {
        var dir = Path.Combine(Path.GetTempPath(), "diagname", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        try
        {
            var path = Path.Combine(dir, "nodate.szip");
            File.WriteAllText(path, "x");

            var arrived = DiagFileNameDate.ArrivedUtc(path, true);

            Assert.True((DateTime.UtcNow - arrived).Duration() < TimeSpan.FromMinutes(5));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void PrefersTheNameOverTheFileDate()
    {
        var dir = Path.Combine(Path.GetTempPath(), "diagname", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        try
        {
            var path = Path.Combine(dir, "_7_27_2026 9-53-10 PM . M1SupportFile.szip");
            File.WriteAllText(path, "x");

            Assert.Equal(new DateTime(2026, 7, 27, 21, 53, 10, DateTimeKind.Utc),
                DiagFileNameDate.ArrivedUtc(path, true));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
