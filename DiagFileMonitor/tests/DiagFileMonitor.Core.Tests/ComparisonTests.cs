using DiagFileMonitor.Core.Models;
using DiagFileMonitor.Core.SpidaLogs;

namespace DiagFileMonitor.Core.Tests;

public class StepProfileTests
{
    private static string Line(string time, string tag, string description) =>
        $"{time},  Other, {tag},  {description}";

    /// <summary>One unit stepping 10 -> 20 -> 30, with known gaps between each.</summary>
    private static IReadOnlyList<MachineLogEntry> Cycle(string startSecond, int step10Seconds, int step20Seconds) =>
        MachineLogFile.Parse([
            Line($"10:{startSecond}:00.0000000", "WallExtruderStep", "Step = 10"),
            Line($"10:{startSecond}:{step10Seconds:00}.0000000", "WallExtruderStep", "Step = 20"),
            Line($"10:{startSecond}:{step10Seconds + step20Seconds:00}.0000000", "WallExtruderStep", "Step = 30"),
            Line($"10:{startSecond}:{step10Seconds + step20Seconds + 5:00}.0000000", "Status", "Panel Assembled")
        ]);

    [Fact]
    public void MeasuresHowLongEachStepTakes()
    {
        var profile = StepProfile.From(Cycle("00", 4, 6));

        Assert.Equal(4, profile.MedianDuration[10].TotalSeconds);
        Assert.Equal(6, profile.MedianDuration[20].TotalSeconds);
        Assert.Equal([10, 20, 30], profile.Sequence);
    }

    [Fact]
    public void UsesTheMedianAcrossUnitsSoOneSlowRunDoesNotSkewIt()
    {
        var log = Cycle("00", 4, 6).Concat(Cycle("30", 40, 6)).ToList();

        // Two units, step 10 taking 4s and 40s: the median of the two is 22s.
        var profile = StepProfile.From(log);

        Assert.Equal(2, profile.CycleCount);
        Assert.Equal(22, profile.MedianDuration[10].TotalSeconds);
    }

    [Fact]
    public void AnEmptyLogProducesAnEmptyProfile()
    {
        Assert.True(StepProfile.From([]).IsEmpty);
    }
}

public class StepTimingComparisonTests
{
    private static StepProfile Profile(params (int Step, double Seconds)[] steps) => new()
    {
        Samples = steps.Select(s => new StepSample(s.Step, TimeSpan.FromSeconds(s.Seconds), 1)).ToList(),
        Sequence = steps.Select(s => s.Step).ToList(),
        MedianDuration = steps.ToDictionary(s => s.Step, s => TimeSpan.FromSeconds(s.Seconds)),
        Occurrences = steps.ToDictionary(s => s.Step, _ => 1),
        CycleCount = 1,
        MedianCycleDuration = TimeSpan.FromSeconds(steps.Sum(s => s.Seconds))
    };

    [Fact]
    public void MatchingMachinesReportNoDifferences()
    {
        var comparison = StepTimingComparison.Compare(Profile((10, 4), (20, 6)), Profile((10, 4), (20, 6)));

        Assert.True(comparison.SequenceMatches);
        Assert.Empty(comparison.Noteworthy);
    }

    [Theory]
    [InlineData(4.5, StepVerdict.SlightlySlower)]   // 112%
    [InlineData(5.2, StepVerdict.Slower)]           // 130%
    [InlineData(8.0, StepVerdict.MuchSlower)]       // 200%
    [InlineData(3.0, StepVerdict.Faster)]           // 75%
    [InlineData(4.2, StepVerdict.Same)]             // 105%, inside the noise band
    public void GradesHowFarAStepHasDrifted(double comparedSeconds, StepVerdict expected)
    {
        var comparison = StepTimingComparison.Compare(Profile((10, 4)), Profile((10, comparedSeconds)));

        Assert.Equal(expected, Assert.Single(comparison.Differences).Verdict);
    }

    [Fact]
    public void CatchesASubtleSlowdownTheEyeWouldMiss()
    {
        // 15% slower on one step is exactly the drift a benchmark is meant to surface.
        var comparison = StepTimingComparison.Compare(Profile((10, 20)), Profile((10, 23)));

        var difference = Assert.Single(comparison.Differences);
        Assert.Equal(StepVerdict.SlightlySlower, difference.Verdict);
        Assert.Equal(115, difference.PercentOfMaster);
    }

    [Fact]
    public void FlagsAStepTheComparedMachineNeverReached()
    {
        var comparison = StepTimingComparison.Compare(Profile((10, 4), (20, 6)), Profile((10, 4)));

        Assert.Equal(StepVerdict.MissingFromCompared,
            comparison.Differences.Single(d => d.Step == 20).Verdict);
        Assert.False(comparison.SequenceMatches);
    }

    [Fact]
    public void FlagsAStepThatIsNotInTheBenchmark()
    {
        var comparison = StepTimingComparison.Compare(Profile((10, 4)), Profile((10, 4), (99, 1)));

        Assert.Equal(StepVerdict.NotInMaster, comparison.Differences.Single(d => d.Step == 99).Verdict);
    }

    [Fact]
    public void ComparesOverallCycleTime()
    {
        var comparison = StepTimingComparison.Compare(Profile((10, 10)), Profile((10, 15)));

        Assert.Equal(150, comparison.CyclePercentOfMaster);
    }

    [Fact]
    public void NoticesTheSameStepsRunInADifferentOrder()
    {
        var master = Profile((10, 4), (20, 4));
        var compared = new StepProfile
        {
            Sequence = [20, 10],
            MedianDuration = master.MedianDuration,
            Occurrences = master.Occurrences,
            CycleCount = 1,
            MedianCycleDuration = master.MedianCycleDuration,
            Samples = master.Samples
        };

        Assert.False(StepTimingComparison.Compare(master, compared).SequenceMatches);
    }
}

public class SettingsComparisonTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "settingscmp", Guid.NewGuid().ToString("N"));

    private string Write(string name, string xml)
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, xml);
        return path;
    }

    [Fact]
    public void ReportsValuesThatDiffer()
    {
        var master = Write("a.xml", "<Machine><JobSettings><RoundTo90>0</RoundTo90><CanUseSSF>true</CanUseSSF></JobSettings></Machine>");
        var compared = Write("b.xml", "<Machine><JobSettings><RoundTo90>5</RoundTo90><CanUseSSF>true</CanUseSSF></JobSettings></Machine>");

        var comparison = SettingsComparison.Compare(master, compared);

        var difference = Assert.Single(comparison.Differences);
        Assert.Contains("RoundTo90", difference.Setting);
        Assert.Equal("0", difference.MasterValue);
        Assert.Equal("5", difference.ComparedValue);
    }

    [Fact]
    public void IdenticalFilesReportNothing()
    {
        const string xml = "<Machine><JobSettings><RoundTo90>0</RoundTo90></JobSettings></Machine>";

        var comparison = SettingsComparison.Compare(Write("a.xml", xml), Write("b.xml", xml));

        Assert.Empty(comparison.Differences);
        Assert.Equal(1, comparison.SettingsCompared);
    }

    [Fact]
    public void KeysRepeatedElementsByIdentitySoReorderingIsNotAChange()
    {
        var master = Write("a.xml", """
            <Machine><Roles>
              <RoleActions><Role>Stud</Role><MarkString>S</MarkString></RoleActions>
              <RoleActions><Role>Nog</Role><MarkString>N</MarkString></RoleActions>
            </Roles></Machine>
            """);

        // Same content, opposite order.
        var compared = Write("b.xml", """
            <Machine><Roles>
              <RoleActions><Role>Nog</Role><MarkString>N</MarkString></RoleActions>
              <RoleActions><Role>Stud</Role><MarkString>S</MarkString></RoleActions>
            </Roles></Machine>
            """);

        Assert.Empty(SettingsComparison.Compare(master, compared).Differences);
    }

    [Fact]
    public void ReportsSettingsPresentInOnlyOneFile()
    {
        var master = Write("a.xml", "<Machine><A>1</A><OnlyHere>x</OnlyHere></Machine>");
        var compared = Write("b.xml", "<Machine><A>1</A></Machine>");

        var comparison = SettingsComparison.Compare(master, compared);

        Assert.Contains(comparison.OnlyInMaster, k => k.Contains("OnlyHere"));
        Assert.Empty(comparison.OnlyInCompared);
    }

    [Fact]
    public void AMissingFileComparesAsEmptyRatherThanThrowing()
    {
        var comparison = SettingsComparison.Compare(
            Path.Combine(_dir, "nope.xml"), Path.Combine(_dir, "also-nope.xml"));

        Assert.Equal(0, comparison.SettingsCompared);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }
}

public class StepDifferenceWordingTests
{
    private static StepDifference Difference(double masterMs, double comparedMs) =>
        StepTimingComparison.Compare(
            Profile(masterMs), Profile(comparedMs)).Differences.Single();

    private static StepProfile Profile(double milliseconds) => new()
    {
        Samples = new[] { new StepSample(0, TimeSpan.FromMilliseconds(milliseconds), 1) },
        Sequence = new[] { 0 },
        MedianDuration = new Dictionary<int, TimeSpan> { [0] = TimeSpan.FromMilliseconds(milliseconds) },
        Occurrences = new Dictionary<int, int> { [0] = 1 },
        CycleCount = 1,
        MedianCycleDuration = TimeSpan.FromMilliseconds(milliseconds)
    };

    [Fact]
    public void AStepTakingAFractionOfTheBenchmarkReadsAsTimesFasterNotPercentFaster()
    {
        // Reported from a real comparison: master 404 ms, compared 69 ms showed "17% faster".
        // 69ms is 17% OF 404ms, which is close to six times faster - the opposite of a 17% gain.
        var difference = Difference(masterMs: 404, comparedMs: 69);

        Assert.Equal(17, difference.PercentOfMaster);
        Assert.Equal("5.9x faster", difference.Multiple);

        Assert.Contains("5.9x faster", difference.Display);
        Assert.DoesNotContain("17%", difference.Display);
    }

    [Fact]
    public void ASlowerStepReadsAsTimesSlower()
    {
        // Also from that comparison: master 38 ms, compared 1.04 s showed "2749%".
        var difference = Difference(masterMs: 38, comparedMs: 1040);

        Assert.Equal("27.4x slower", difference.Multiple);
        Assert.Contains("27.4x slower", difference.Display);
        Assert.Contains("much slower", difference.Display);
    }

    [Theory]
    [InlineData(100, 50, "2.0x faster")]
    [InlineData(100, 200, "2.0x slower")]
    [InlineData(100, 151, "1.5x slower")]
    [InlineData(404, 69, "5.9x faster")]
    public void TheMultipleIsTheRatioEitherWayRound(double master, double compared, string expected)
    {
        Assert.Equal(expected, Difference(master, compared).Multiple);
    }

    [Fact]
    public void AStepMatchingTheBenchmarkSaysSameRatherThanOneTimesSlower()
    {
        var difference = Difference(masterMs: 100, comparedMs: 100);

        Assert.Equal(StepVerdict.Same, difference.Verdict);
        Assert.Contains("same", difference.Display);
        Assert.DoesNotContain("1.0x", difference.Display);
    }

    [Fact]
    public void StepsPresentInOnlyOneFileSaySoInsteadOfAMultiple()
    {
        var masterOnly = StepTimingComparison.Compare(Profile(100), new StepProfile()).Differences.Single();
        var comparedOnly = StepTimingComparison.Compare(new StepProfile(), Profile(100)).Differences.Single();

        Assert.Contains("never reached", masterOnly.Display);
        Assert.Null(masterOnly.Multiple);

        Assert.Contains("not in master", comparedOnly.Display);
        Assert.Null(comparedOnly.Multiple);
    }

    [Fact]
    public void TheOverallCycleLineAlsoReadsAsAMultiple()
    {
        var steps = StepTimingComparison.Compare(Profile(12720), Profile(1110));
        var text = CompareReportFormatter.Format(
            new DiagnosticFileSummary { OriginalFileName = "master.szip" },
            new DiagnosticFileSummary { OriginalFileName = "compared.szip" },
            steps, new SettingsComparison(), Array.Empty<string>());

        // 1.11 s against 12.72 s was printed as "(9% of master) faster than the benchmark".
        Assert.Contains("11.5x faster", text);
        Assert.DoesNotContain("9% of master", text);
    }
}
