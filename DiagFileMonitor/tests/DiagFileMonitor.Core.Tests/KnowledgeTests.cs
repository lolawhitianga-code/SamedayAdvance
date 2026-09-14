using DiagFileMonitor.Core.Knowledge;
using DiagFileMonitor.Core.SpidaLogs;
using Xunit;

namespace DiagFileMonitor.Core.Tests;

public class KnowledgeBaseTests
{
    [Theory]
    [InlineData("RakingWallExtruderV3DG")]
    [InlineData("rakingwallextruderv3dg")]
    [InlineData("RakingWallExtruderV4")]
    public void FindsTheRakedExtruder(string model)
    {
        Assert.Equal("RakingWallExtruderV3DG", MachineKnowledgeBase.Find(model)?.Model);
    }

    [Theory]
    [InlineData("SpidaSaw")]
    [InlineData("")]
    [InlineData(null)]
    public void ReturnsNothingForAMachineWeHaveNotWrittenUp(string? model)
    {
        Assert.Null(MachineKnowledgeBase.Find(model));
    }

    [Fact]
    public void EveryFactCarriesItsConfidence()
    {
        // The report quotes these to customers, so a guess must never be indistinguishable
        // from something confirmed.
        foreach (var machine in MachineKnowledgeBase.All)
        {
            Assert.All(machine.Faults, f => Assert.False(string.IsNullOrWhiteSpace(f.Meaning)));
            Assert.All(machine.Issues, i => Assert.False(string.IsNullOrWhiteSpace(i.Detail)));
            Assert.NotEmpty(machine.OpenGaps);
        }
    }
}

public class KnowledgeAnnotatorTests
{
    private static IReadOnlyList<MachineLogEntry> Log(params string[] lines) => MachineLogFile.Parse(lines);

    private static KnowledgeFindings Annotate(IReadOnlyList<MachineLogEntry> log, string serial = "M20716")
    {
        var analysis = new SpidaLogAnalyser().Analyse(
            log, Array.Empty<ErrLogEntry>(), Array.Empty<ChangeLogEntry>(), new DateTime(2026, 7, 27));

        return KnowledgeAnnotator.Annotate(analysis, log, "RakingWallExtruderV3DG", serial);
    }

    [Fact]
    public void RecognisesTheSafetyBarFault()
    {
        var findings = Annotate(Log(
            "10:12:13.0000000,  Other, WallExtruderStep,  Step = 0",
            "10:12:13.8782691,  Other, WallExtruderPLC,  Floating Side Safety Bar Pressed - Press Estop Reset to Continue",
            "10:12:20.0000000,  Other, WallExtruderStep,  Step = 10"));

        var match = Assert.Single(findings.MatchedFaults);
        Assert.Contains("safety bar", match.Known.Meaning, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(Confidence.Confirmed, match.Known.Confidence);
    }

    [Fact]
    public void ReportsFaultsWeHaveNothingWrittenDownFor()
    {
        var findings = Annotate(Log(
            "10:12:13.0000000,  Other, WallExtruderStep,  Step = 0",
            "10:12:14.0000000,  Other, WallExtruderPLC,  Something nobody has written down failed badly"));

        Assert.Empty(findings.MatchedFaults);
        Assert.Contains(findings.UnknownFaults, f => f.Contains("nobody has written down"));
    }

    [Fact]
    public void TheEjectionBugIsNotRaisedJustBecauseTheEjectAxesAreNamed()
    {
        // Every export names the eject servos as soon as the machine homes. Matching issue
        // signals against all log text rather than fault text flagged the ejection bug on
        // every single file, which makes the section worthless.
        var findings = Annotate(Log(
            "10:12:25.0000000,  Other, FixedEjectServo,  Axis Enable",
            "10:12:25.5000000,  Other, FloatingEjectServo,  Axis Start Home",
            "10:12:26.0000000,  MotionEvent, FloatingEjectServo,  Axis Enabled"));

        Assert.DoesNotContain(findings.IssuesSeenInThisLog, i => i.Title.Contains("Ejection"));
    }

    [Fact]
    public void TheSafetyBarFaultIsNotReadAsAnOvercurrent()
    {
        // The safety bar text contains "Estop", which is not evidence of an overcurrent fault.
        var findings = Annotate(Log(
            "10:12:13.0000000,  Other, WallExtruderStep,  Step = 0",
            "10:12:13.8782691,  Other, WallExtruderPLC,  Floating Side Safety Bar Pressed - Press Estop Reset to Continue"));

        Assert.DoesNotContain(findings.IssuesSeenInThisLog, i => i.Title.Contains("Overcurrent"));
    }

    [Fact]
    public void SerialHistoryIsSeparatedFromWhatThisLogShows()
    {
        var findings = Annotate(Log("10:12:13.0000000,  Other, WallExtruderStep,  Step = 0"));

        Assert.True(findings.SerialIsKnown);
        Assert.NotEmpty(findings.IssueHistoryForSerial);
        Assert.Empty(findings.IssuesSeenInThisLog);
    }

    [Fact]
    public void AnUnknownSerialGetsNoHistory()
    {
        var findings = Annotate(Log("10:12:13.0000000,  Other, WallExtruderStep,  Step = 0"), serial: "M99999");

        Assert.False(findings.SerialIsKnown);
        Assert.Empty(findings.IssueHistoryForSerial);
    }

    [Fact]
    public void NamesTheAxesTheLogMentionsAndFlagsOnesWeHaveNotSeenBefore()
    {
        var findings = Annotate(Log(
            "10:12:25.0000000,  Other, FixedSidePuller,  Axis Enable",
            "10:12:26.0000000,  Other, TrolleyHeight,  Axis Start Home",
            "10:12:27.0000000,  Other, SomeBrandNewAxis,  Axis Enable"));

        Assert.Equal("Fixed side gripper", findings.Axes.Single(a => a.LogName == "FixedSidePuller").Known?.PlainName);
        Assert.True(findings.Axes.Single(a => a.LogName == "SomeBrandNewAxis").IsNew);
        Assert.False(findings.Axes.Single(a => a.LogName == "TrolleyHeight").IsNew);
    }

    [Fact]
    public void AMachineWeHaveNotWrittenUpStillProducesFindingsWithoutKnowledge()
    {
        var log = Log("10:12:13.0000000,  Other, SomeStep,  Step = 0");
        var analysis = new SpidaLogAnalyser().Analyse(
            log, Array.Empty<ErrLogEntry>(), Array.Empty<ChangeLogEntry>(), new DateTime(2026, 7, 27));

        var findings = KnowledgeAnnotator.Annotate(analysis, log, "TornadoM450", "M12345");

        Assert.Null(findings.Knowledge);
        Assert.False(findings.HasAnything);
        Assert.Equal("TornadoM450", findings.Model);
    }
}

public class PlatePresentCheckTests
{
    private static IReadOnlyList<MachineLogEntry> Log(params string[] lines) => MachineLogFile.Parse(lines);

    [Fact]
    public void BothSidesDroppingWithAClampChangeIsANormalRelease()
    {
        var events = PlatePresentCheck.Find(Log(
            "10:25:38.0000000,  OutputChange, IO-PlateClampLift10mm,  Output (192.168.250.1-1.1) Set Off",
            "10:25:38.0100000,  InputChange, PlatePresentSwitch,  Input (192.168.250.1-4.2) Changed to 0",
            "10:25:38.0100000,  InputChange, PlatePresentSwitch,  Input (192.168.250.1-4.4) Changed to 0"));

        Assert.Equal(2, events.Count);
        Assert.All(events, e => Assert.Equal(PlatePresentVerdict.NormalClampRelease, e.Verdict));
    }

    [Fact]
    public void OneSideDroppingWithNothingMovingIsAGlitch()
    {
        var events = PlatePresentCheck.Find(Log(
            "10:25:38.0000000,  InputChange, PlatePresentSwitch,  Input (192.168.250.1-4.2) Changed to 0",
            "10:25:39.2000000,  InputChange, PlatePresentSwitch,  Input (192.168.250.1-4.2) Changed to 1"));

        var glitch = Assert.Single(events);
        Assert.Equal(PlatePresentVerdict.SensorGlitch, glitch.Verdict);
        Assert.Equal("Fixed", glitch.Side);
        Assert.Equal(1.2, glitch.RecoveredAfter!.Value.TotalSeconds, 1);
        Assert.Contains("sensor glitch", glitch.Explanation);
    }

    [Fact]
    public void OneSideDroppingWithAClampChangeIsNotCalledEitherWay()
    {
        var events = PlatePresentCheck.Find(Log(
            "10:25:38.0000000,  OutputChange, IO-PlateClampLift10mm,  Output (192.168.250.1-1.1) Set Off",
            "10:25:38.0100000,  InputChange, PlatePresentSwitch,  Input (192.168.250.1-4.2) Changed to 0"));

        Assert.Equal(PlatePresentVerdict.Unclear, Assert.Single(events).Verdict);
    }

    [Fact]
    public void SensorsComingBackOnAreNotTreatedAsEvents()
    {
        var events = PlatePresentCheck.Find(Log(
            "10:25:40.0000000,  InputChange, PlatePresentSwitch,  Input (192.168.250.1-4.2) Changed to 1",
            "10:25:40.0000000,  InputChange, PlatePresentSwitch,  Input (192.168.250.1-4.4) Changed to 1"));

        Assert.Empty(events);
    }

    [Fact]
    public void OtherSensorsOnOtherAddressesAreIgnored()
    {
        var events = PlatePresentCheck.Find(Log(
            "10:25:38.0000000,  InputChange, GripperProductSensor,  Input (192.168.250.1-4.6) Changed to 0",
            "10:25:38.0000000,  InputChange, PlatePresentBypass,  Input (192.168.250.1-0.0) Changed to 0"));

        Assert.Empty(events);
    }
}
