using DiagFileMonitor.Core.Services;

namespace DiagFileMonitor.Core.Tests;

public class SupportInfoParserTests
{
    /// <summary>Byte-for-byte shape of a real SupportInfo.txt, including the leading spaces.</summary>
    private static readonly string[] RealFile =
    {
        " Panel: 448121m3",
        " Member: all",
        " Issue: trolleys not moving when i hit start panel",
        " ",
        "Latitude: 0",
        "Longitude: 0",
        "FixVer:  1.0.0.23"
    };

    [Fact]
    public void ReadsTheOperatorsThreeFields()
    {
        var info = SupportInfoParser.Parse(RealFile);

        Assert.Equal("448121m3", info.Panel);
        Assert.Equal("all", info.Members);
        Assert.Equal("trolleys not moving when i hit start panel", info.Issue);
    }

    [Fact]
    public void DoesNotLetTheTrailingFieldsRunIntoTheIssue()
    {
        var info = SupportInfoParser.Parse(RealFile);

        Assert.DoesNotContain("Latitude", info.Issue);
        Assert.DoesNotContain("FixVer", info.Issue);
        Assert.DoesNotContain("1.0.0.23", info.Issue);
    }

    [Fact]
    public void StopsAtAForeignFieldEvenWithNoBlankLineBetween()
    {
        var info = SupportInfoParser.Parse([
            " Issue: trolleys not moving",
            "Latitude: 0",
            "FixVer: 1.0.0.23"
        ]);

        Assert.Equal("trolleys not moving", info.Issue);
    }

    [Fact]
    public void BuildsTheOneLineSummary()
    {
        var info = SupportInfoParser.Parse(RealFile);

        Assert.Equal("Panel: 448121m3 | Members: all | Issue: trolleys not moving when i hit start panel", info.Summary);
        Assert.True(info.HasAnything);
    }

    [Fact]
    public void AcceptsSingularAndPluralMember()
    {
        Assert.Equal("all", SupportInfoParser.Parse([" Member: all"]).Members);
        Assert.Equal("3 and 4", SupportInfoParser.Parse([" Members: 3 and 4"]).Members);
    }

    [Fact]
    public void IsNotFussyAboutCase()
    {
        var info = SupportInfoParser.Parse(["PANEL: A1", "issue: broken"]);

        Assert.Equal("A1", info.Panel);
        Assert.Equal("broken", info.Issue);
    }

    [Fact]
    public void KeepsAnIssueThatRunsOverSeveralLines()
    {
        var info = SupportInfoParser.Parse([
            " Panel: A1",
            " Issue: trolleys not moving",
            "   it only happens after a restart",
            "",
            "Latitude: 0"
        ]);

        Assert.Equal("trolleys not moving it only happens after a restart", info.Issue);
    }

    [Fact]
    public void HandlesEmptyValuesAndMissingFields()
    {
        var info = SupportInfoParser.Parse([" Panel: ", " Issue: something wrong"]);

        Assert.Null(info.Panel);
        Assert.Null(info.Members);
        Assert.Equal("something wrong", info.Issue);
    }

    [Fact]
    public void ReportsNothingForAFileWithNoOperatorText()
    {
        var info = SupportInfoParser.Parse(["Latitude: 0", "Longitude: 0", "FixVer: 1.0.0.23"]);

        Assert.False(info.HasAnything);
        Assert.Equal(string.Empty, info.Summary);
    }

    [Fact]
    public void MissingFileGivesAnEmptyResultRatherThanThrowing()
    {
        var info = SupportInfoParser.ParseFile(Path.Combine(Path.GetTempPath(), $"nope-{Guid.NewGuid():N}.txt"));

        Assert.False(info.HasAnything);
    }
}
