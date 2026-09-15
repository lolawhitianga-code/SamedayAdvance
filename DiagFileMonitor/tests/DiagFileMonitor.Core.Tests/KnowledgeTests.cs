using DiagFileMonitor.Core.Models;
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

public class HomeInterlockCheckTests
{
    private static IReadOnlyList<MachineLogEntry> Log(params string[] lines) => MachineLogFile.Parse(lines);

    [Fact]
    public void AProductSensorReadingOneStopsTheMachineHoming()
    {
        var findings = HomeInterlockCheck.Check(Log(
            "10:12:15.0000000,  InputChange, GripperProductSensor,  Input (192.168.250.1-4.6) Changed to 1",
            "10:12:16.0000000,  Other, ClsWallExtruder,  HomeServos",
            "10:12:54.0000000,  Other, FixedSidePuller,  Axis Start Home"));

        var attempt = Assert.Single(findings.Attempts);
        Assert.True(attempt.WasRefused);
        Assert.Equal("192.168.250.1-4.6", Assert.Single(attempt.Blocking).Address);
        Assert.Contains("nothing homed for 38s", attempt.Outcome);
    }

    [Fact]
    public void ASensorChangingOnTheSameTimestampAsTheCommandCounts()
    {
        // In the real M20716 export the sensor change and the HomeServos command share a
        // timestamp, with the sensor printed on a later line. They are the same PLC scan, so
        // the sensor was already reading 1 when the command was given.
        var findings = HomeInterlockCheck.Check(Log(
            "10:12:15.6590038,  Other, ClsWallExtruder,  HomeServos",
            "10:12:15.6590038,  InputChange, GripperProductSensor,  Input (192.168.250.1-4.6) Changed to 1"));

        Assert.Single(Assert.Single(findings.Attempts).Blocking);
    }

    [Fact]
    public void AClearMachineHomesWithNothingBlocking()
    {
        var findings = HomeInterlockCheck.Check(Log(
            "10:12:15.0000000,  InputChange, GripperProductSensor,  Input (192.168.250.1-4.6) Changed to 0",
            "10:12:16.0000000,  Other, ClsWallExtruder,  HomeServos",
            "10:12:16.5000000,  Other, FixedSidePuller,  Axis Start Home"));

        var attempt = Assert.Single(findings.Attempts);
        Assert.False(attempt.WasRefused);
        Assert.Empty(attempt.Blocking);
    }

    [Fact]
    public void ASensorThatClearedBeforeTheCommandDoesNotBlock()
    {
        var findings = HomeInterlockCheck.Check(Log(
            "10:12:10.0000000,  InputChange, GripperProductSensor,  Input (192.168.250.1-4.6) Changed to 1",
            "10:12:14.0000000,  InputChange, GripperProductSensor,  Input (192.168.250.1-4.6) Changed to 0",
            "10:12:16.0000000,  Other, ClsWallExtruder,  HomeServos",
            "10:12:16.2000000,  Other, FixedSidePuller,  Axis Start Home"));

        Assert.Empty(Assert.Single(findings.Attempts).Blocking);
    }

    [Fact]
    public void ThePlatePresentSwitchesBlockHomingToo()
    {
        var findings = HomeInterlockCheck.Check(Log(
            "10:12:15.0000000,  InputChange, PlatePresentSwitch,  Input (192.168.250.1-4.2) Changed to 1",
            "10:12:16.0000000,  Other, ClsWallExtruder,  HomeServos"));

        var attempt = Assert.Single(findings.Attempts);
        Assert.Equal("PlatePresentSwitch", Assert.Single(attempt.Blocking).Tag);

        // Nothing ever homed after it.
        Assert.True(attempt.WasRefused);
        Assert.Null(attempt.FirstAxisHomedAfter);
    }

    [Fact]
    public void BothGrippersAreTrackedSeparately()
    {
        var findings = HomeInterlockCheck.Check(Log(
            "10:12:10.0000000,  InputChange, GripperProductSensor,  Input (192.168.250.1-4.6) Changed to 1",
            "10:12:11.0000000,  InputChange, GripperProductSensor,  Input (192.168.250.1-4.7) Changed to 1",
            "10:12:12.0000000,  InputChange, GripperProductSensor,  Input (192.168.250.1-4.6) Changed to 0",
            "10:12:16.0000000,  Other, ClsWallExtruder,  HomeServos"));

        // Only the one still reading 1 blocks.
        var blocking = Assert.Single(Assert.Single(findings.Attempts).Blocking);
        Assert.Equal("192.168.250.1-4.7", blocking.Address);
        Assert.Equal(2, findings.SensorsSeen.Count);
    }

    [Fact]
    public void SensorsThatNeverChangeAreReportedAsAbsentRatherThanBlocking()
    {
        // The log records changes only, so a sensor sitting at 0 the whole session never appears.
        var findings = HomeInterlockCheck.Check(Log(
            "10:12:16.0000000,  Other, ClsWallExtruder,  HomeServos",
            "10:12:16.2000000,  Other, FixedSidePuller,  Axis Start Home"));

        Assert.Empty(Assert.Single(findings.Attempts).Blocking);
        Assert.Equal(2, findings.SensorsNotInLog.Count);
        Assert.Contains("GripperProductSensor", findings.SensorsNotInLog);
        Assert.Contains("PlatePresentSwitch", findings.SensorsNotInLog);
    }

    [Fact]
    public void OtherInputsAreNotTreatedAsProductSensors()
    {
        var findings = HomeInterlockCheck.Check(Log(
            "10:12:15.0000000,  InputChange, TrolleyTopClampOpen,  Input (192.168.250.1-0.8) Changed to 1",
            "10:12:15.1000000,  InputChange, LowerNailSensor,  Input (192.168.250.1-0.23) Changed to 1",
            "10:12:16.0000000,  Other, ClsWallExtruder,  HomeServos"));

        Assert.Empty(findings.SensorsSeen);
        Assert.Empty(Assert.Single(findings.Attempts).Blocking);
    }

    [Fact]
    public void ALogWithNoHomeCommandReportsNoAttempts()
    {
        var findings = HomeInterlockCheck.Check(Log(
            "10:12:15.0000000,  InputChange, GripperProductSensor,  Input (192.168.250.1-4.6) Changed to 1"));

        Assert.Empty(findings.Attempts);
        Assert.Single(findings.SensorsSeen);
        Assert.True(findings.Any);
    }
}

public class ComplaintRouterTests
{
    private static ChangeLogEntry Change(string date, string setting, string from, string to) => new()
    {
        Timestamp = DateTime.Parse(date), User = "Spida", Category = "Settings",
        Setting = setting, OldValue = from, NewValue = to
    };

    private static readonly IReadOnlyList<ChangeLogEntry> Changes = new[]
    {
        Change("2026-05-05 13:09:03", "NailDistFromEdgeOfTimber", "22", "20"),
        Change("2026-04-20 14:03:18", "GunFireTime", "150", "170"),
        Change("2026-04-21 14:26:51", "RWEPanelHeightGap", "0", "2"),
        Change("2026-01-20 11:38:30", "NailsPastSensor", "10", "20")
    };

    private static ComplaintFindings Route(string issue) =>
        ComplaintRouter.Route(issue, Changes, Array.Empty<MachineLogEntry>());

    [Fact]
    public void ANailGunComplaintGoesToGunAndNailSettingsFirst()
    {
        var findings = Route("Nail gun in incorrect position");

        var topic = findings.Topics[0];
        Assert.Equal("Nail gun position or height", topic.Topic.Name);
        Assert.Contains("NailDistFromEdgeOfTimber", topic.RelatedChanges.Select(c => c.Setting));
        Assert.Contains("GunFireTime", topic.RelatedChanges.Select(c => c.Setting));
    }

    [Fact]
    public void APanelHeightSettingIsNotDraggedIntoAGunComplaint()
    {
        // "height" as a search word pulls in RWEPanelHeightGap, which is nothing to do with
        // where the gun fires.
        var topic = Route("Nail gun in incorrect position").Topics[0];

        Assert.DoesNotContain("RWEPanelHeightGap", topic.RelatedChanges.Select(c => c.Setting));
    }

    [Fact]
    public void RelatedChangesAreNewestFirstAndNotLimitedToTheDayOfTheExport()
    {
        // A gun setting changed weeks ago still explains a gun complaint.
        var changes = Route("nail gun wrong position").Topics[0].RelatedChanges;

        Assert.Equal("NailDistFromEdgeOfTimber", changes[0].Setting);
        Assert.True(changes[0].Timestamp > changes[^1].Timestamp);
    }

    [Fact]
    public void TheRealComplaintFromM20716PointsAtTheInterlockFirst()
    {
        var findings = Route("trolleys not moving when i hit start panel");

        Assert.Equal("Machine will not move or start", findings.Topics[0].Topic.Name);
        Assert.Contains("CAN IT HOME?", findings.Topics[0].Topic.LookAt[0]);
    }

    [Fact]
    public void TheTopicMatchingMostOfTheOperatorsWordsComesFirst()
    {
        // "trolleys not moving when i hit start panel" hits the movement topic three times and
        // the trolley topic once, so movement leads.
        var findings = Route("trolleys not moving when i hit start panel");

        Assert.Equal(2, findings.Topics.Count);
        Assert.True(findings.Topics[0].MatchedOn.Count > findings.Topics[1].MatchedOn.Count);
        Assert.Equal("Trolley or floating head height", findings.Topics[1].Topic.Name);
    }

    [Fact]
    public void AComplaintWeHaveNoRoutineForStillKeepsTheOperatorsWords()
    {
        var findings = Route("the screen colours look odd");

        Assert.True(findings.HasIssueText);
        Assert.False(findings.Any);
        Assert.Equal("the screen colours look odd", findings.Issue);
    }

    [Fact]
    public void NoIssueTextMeansNoRouting()
    {
        Assert.False(ComplaintRouter.Route("", Changes, Array.Empty<MachineLogEntry>()).HasIssueText);
        Assert.False(ComplaintRouter.Route(null, Changes, Array.Empty<MachineLogEntry>()).HasIssueText);
        Assert.False(ComplaintRouter.Route("   ", Changes, Array.Empty<MachineLogEntry>()).HasIssueText);
    }

    [Fact]
    public void AnEjectionComplaintRaisesTheKnownBugAndTheTrolleyLift()
    {
        var topic = Route("panel wont come out at eject").Topics[0];

        Assert.Equal("Ejection", topic.Topic.Name);
        Assert.Contains("ejection bug", topic.Topic.LookAt[0]);
        Assert.Contains("TrolleyHeight", topic.Topic.LookAt[1]);
    }

    [Fact]
    public void AStudComplaintChecksNailMarkGenerationBeforeAnythingMechanical()
    {
        var topic = Route("studs are being skipped").Topics[0];

        Assert.Equal("Studs skipped or in the wrong place", topic.Topic.Name);
        Assert.Contains("Nail mark generation", topic.Topic.LookAt[0]);
    }

    [Fact]
    public void RelatedLogLinesArePulledByTag()
    {
        var log = MachineLogFile.Parse(new[]
        {
            "10:12:15.0000000,  Other, TrolleyHeight,  Axis Start Home",
            "10:12:16.0000000,  Other, FixedSidePuller,  Axis Start Home"
        });

        var topic = ComplaintRouter.Route("trolley is at the wrong height", Changes, log).Topics[0];

        Assert.Equal("TrolleyHeight", Assert.Single(topic.RelatedLogLines).Tag);
    }
}

public class SoftwareVersionTests
{
    [Theory]
    [InlineData("V0.0.0.0")]
    [InlineData("v0.0.0.0")]
    [InlineData("0.0.0.0")]
    [InlineData("0.0.0")]
    [InlineData("0")]
    [InlineData(" V0.0.0.0 ")]
    public void AllZeroVersionsAreUnstampedDevBuilds(string version)
    {
        Assert.True(SoftwareVersion.IsUnstampedDevBuild(version));
        Assert.NotNull(SoftwareVersion.Note(version));
    }

    [Theory]
    [InlineData("V2.4.0.0")]
    [InlineData("V2.6.1")]
    [InlineData("V0.1.0.0")]
    [InlineData("V1.0.0.0")]
    [InlineData("V2.0.0.0")]
    [InlineData("(unknown)")]
    [InlineData("")]
    [InlineData(null)]
    public void RealVersionsAreLeftAlone(string? version)
    {
        // V0.1.0.0 and V2.0.0.0 contain zeros but are real versions - only all-zero is the dev build.
        Assert.False(SoftwareVersion.IsUnstampedDevBuild(version));
        Assert.Null(SoftwareVersion.Note(version));
    }

    [Fact]
    public void TheNoteSaysNotToTrustTheFieldRatherThanGuessingTheBuild()
    {
        var note = SoftwareVersion.Note("V0.0.0.0")!;

        Assert.StartsWith("V0.0.0.0 is a dev build", note);
        Assert.Contains("V2.6.1", note);
        Assert.Contains("rather than trusting this field", note);
    }

    [Fact]
    public void TwoDevBuildsGetOneWarningSayingTheyMayNotMatchEachOther()
    {
        var note = SoftwareVersion.CompareNote("V0.0.0.0", "V0.0.0.0")!;

        Assert.Contains("Both files report V0.0.0.0", note);
        Assert.Contains("not necessarily the same build as each other", note);
    }

    [Fact]
    public void OnlyTheSideThatIsADevBuildIsNamed()
    {
        Assert.StartsWith("The benchmark is on", SoftwareVersion.CompareNote("V0.0.0.0", "V2.4.0.0"));
        Assert.StartsWith("The compared machine is on", SoftwareVersion.CompareNote("V2.4.0.0", "V0.0.0.0"));
        Assert.Null(SoftwareVersion.CompareNote("V2.4.0.0", "V2.6.1"));
    }

    [Fact]
    public void TheAnalysisReportFlagsADevBuildBesideTheVersion()
    {
        var analysis = new SpidaLogAnalyser().Analyse(
            Array.Empty<MachineLogEntry>(), Array.Empty<ErrLogEntry>(), Array.Empty<ChangeLogEntry>(),
            new DateTime(2026, 7, 27));

        var devBuild = SpidaReportFormatter.Format(
            new DiagnosticFileSummary { SoftwareName = "Spida SDN", Version = "V0.0.0.0" }, analysis);

        var released = SpidaReportFormatter.Format(
            new DiagnosticFileSummary { SoftwareName = "Spida SDN", Version = "V2.4.0.0" }, analysis);

        Assert.Contains("dev build", devBuild);
        Assert.DoesNotContain("dev build", released);
    }

    [Fact]
    public void TheCompareReportWarnsOnceWhenBothSidesAreDevBuilds()
    {
        var both = new DiagnosticFileSummary { OriginalFileName = "x.szip", Version = "V0.0.0.0" };

        var text = CompareReportFormatter.Format(both, both,
            StepTimingComparison.Compare(new StepProfile(), new StepProfile()),
            new SettingsComparison(), Array.Empty<string>());

        Assert.Equal(1, text.Split("dev build").Length - 1);
        Assert.Contains("Both files report V0.0.0.0", text);
    }

    [Fact]
    public void TheCompareReportFlagsWhicheverSideIsADevBuild()
    {
        var text = CompareReportFormatter.Format(
            new DiagnosticFileSummary { OriginalFileName = "m.szip", Version = "V2.4.0.0" },
            new DiagnosticFileSummary { OriginalFileName = "c.szip", Version = "V0.0.0.0" },
            StepTimingComparison.Compare(new StepProfile(), new StepProfile()),
            new SettingsComparison(), Array.Empty<string>());

        Assert.Contains("The compared machine is on V0.0.0.0", text);
        Assert.DoesNotContain("The benchmark is on", text);
    }
}

public class MotionControllerFaultTests
{
    /// <summary>The real lines from the M20616 WallExtruderDG export.</summary>
    private static IReadOnlyList<MachineLogEntry> RealLog() => MachineLogFile.Parse(new[]
    {
        "13:12:07.2470000,  MotionEvent, Axis-FixedSidePusher,  F02 Encoder Wiring Fault",
        "13:12:07.3270000,  MotionEvent, Control,  Fixed Side Trolley Tripped and stopped FloatingSide Trolley",
        "13:16:58.2950000,  MotionEvent, Axis-FixedSidePusher,  F02 Encoder Wiring Fault",
        "13:30:04.0600000,  MotionEvent, Axis-FloatingSidePusher,  Node Not Found on Network",
        "13:30:04.3400000,  MotionEvent, Axis-FloatingSidePusher,  F14 Comms Fail",
        "13:30:04.3400000,  MotionEvent, Axis-FixedSidePusher,  F14 Comms Fail",
        "13:30:04.4890000,  MotionEvent, Axis-FixedSidePusher,  F02 Encoder Wiring Fault"
    });

    [Fact]
    public void FindsTheCodesAndTheAxisEachWasRaisedOn()
    {
        var found = MotionControllerFaults.Find(RealLog());

        var f02 = found.Single(f => f.Code.Code == "F02");
        Assert.Equal(3, f02.Occurrences);
        Assert.Equal("Axis-FixedSidePusher", Assert.Single(f02.Axes));
        Assert.Equal("Encoder wiring fault", f02.Code.ShortMeaning);
        Assert.Contains("encoder wiring", f02.Code.WhatToCheck, StringComparison.OrdinalIgnoreCase);

        var f14 = found.Single(f => f.Code.Code == "F14");
        Assert.Equal(2, f14.Occurrences);
        Assert.Equal(2, f14.Axes.Count);
    }

    [Fact]
    public void RecordsWhenTheCodeFirstAndLastAppeared()
    {
        var f02 = MotionControllerFaults.Find(RealLog()).Single(f => f.Code.Code == "F02");

        Assert.Equal(new TimeSpan(0, 13, 12, 7, 247), f02.FirstSeen);
        Assert.Equal(new TimeSpan(0, 13, 30, 4, 489), f02.LastSeen);
        Assert.Contains("on Axis-FixedSidePusher", f02.Where);
    }

    [Fact]
    public void OnlyCodesTheManualDefinesAreMatched()
    {
        // F17 and F42 are not MC2 codes; a bare "F02" inside a word is not one either.
        var log = MachineLogFile.Parse(new[]
        {
            "10:00:00.0000000,  MotionEvent, Axis-A,  F17 something invented",
            "10:00:01.0000000,  MotionEvent, Axis-A,  F42 also invented",
            "10:00:02.0000000,  Other, FileReader,  Opening XF02Y.MPS"
        });

        Assert.Empty(MotionControllerFaults.Find(log));
    }

    [Fact]
    public void AVersionNumberIsNotReadAsADriveFault()
    {
        var log = MachineLogFile.Parse(new[]
        {
            "10:00:00.0000000,  Other, Machine Model,  WallExtruderDG F02.1 build"
        });

        // F02.1 is not the code F02 on a word boundary followed by nothing.
        Assert.Empty(MotionControllerFaults.Find(log));
    }

    [Fact]
    public void LimitStatesAreReportedButNotAsFaults()
    {
        var log = MachineLogFile.Parse(new[]
        {
            "10:00:00.0000000,  MotionEvent, Axis-A,  SLL Software Low Limit"
        });

        var found = Assert.Single(MotionControllerFaults.Find(log));
        Assert.False(found.Code.IsFault);
    }

    [Fact]
    public void TheInternalFaultsSayToCallCyberLogix()
    {
        foreach (var code in new[] { "F11", "F12", "F13", "F99" })
        {
            Assert.True(MotionControllerFaults.Lookup(code)!.CallCyberLogix, code);
        }

        Assert.False(MotionControllerFaults.Lookup("F02")!.CallCyberLogix);
    }

    [Fact]
    public void DriveFaultsAreFoundOnAMachineWeHaveNoNotesFor()
    {
        // WallExtruderDG has no knowledge entry, but the codes come from the drive not the model.
        var log = RealLog();
        var analysis = new SpidaLogAnalyser().Analyse(
            log, Array.Empty<ErrLogEntry>(), Array.Empty<ChangeLogEntry>(), new DateTime(2026, 9, 15));

        var findings = KnowledgeAnnotator.Annotate(analysis, log, "WallExtruderDG", "M20616");

        Assert.Null(findings.Knowledge);
        Assert.Equal(2, findings.DriveFaults.Count);
        Assert.True(findings.HasAnything);
    }

    [Fact]
    public void TheReportLeadsWithTheCodeRatherThanAGenericSensorCheck()
    {
        var log = RealLog();
        var analysis = new SpidaLogAnalyser().Analyse(
            log, Array.Empty<ErrLogEntry>(), Array.Empty<ChangeLogEntry>(), new DateTime(2026, 9, 15));

        var text = SpidaReportFormatter.Format(
            new DiagnosticFileSummary { MachineType = "WallExtruderDG", SerialNumber = "M20616" },
            analysis,
            KnowledgeAnnotator.Annotate(analysis, log, "WallExtruderDG", "M20616"));

        Assert.Contains("DRIVE FAULT CODES", text);

        // The code must come before the units, not be buried in quoted context below them.
        Assert.True(text.IndexOf("F02", StringComparison.Ordinal) < text.IndexOf("UNITS ATTEMPTED", StringComparison.Ordinal));

        var leads = text[text.IndexOf("WHERE TO START LOOKING", StringComparison.Ordinal)..];
        Assert.Contains("F02 Encoder wiring fault on Axis-FixedSidePusher", leads);
    }
}

public class AxisHardwareTests
{
    [Theory]
    [InlineData("CIPNet", ElectronicsFamily.Omron)]
    [InlineData("CLXMCNet", ElectronicsFamily.Clx)]
    [InlineData("CLXMCNetAxis", ElectronicsFamily.Clx)]
    [InlineData("CLXMCNetInput", ElectronicsFamily.Clx)]
    [InlineData("Simulation", ElectronicsFamily.Simulated)]
    [InlineData("SomethingNew", ElectronicsFamily.Unknown)]
    [InlineData("", ElectronicsFamily.Unknown)]
    public void ClassifiesTheHardwareTypesTheConfigUses(string raw, ElectronicsFamily expected)
    {
        Assert.Equal(expected, AxisHardware.Classify(raw));
    }

    [Fact]
    public void ReadsAxesFromAUtf16MachineConfig()
    {
        // The real machine config files are UTF-16 with a byte order mark.
        var path = Path.Combine(Path.GetTempPath(), $"axes-{Guid.NewGuid():N}.xml");
        File.WriteAllText(path, """
            <?xml version="1.0" encoding="utf-16"?>
            <WallExtruder>
              <FixedSide>
                <Trolley><InUse>true</InUse><AxisHardwareType>CIPNet</AxisHardwareType></Trolley>
                <ServoGuns><ServoGun><InUse>true</InUse><AxisHardwareType>CLXMCNet</AxisHardwareType></ServoGun></ServoGuns>
                <YAxis><InUse>false</InUse><AxisHardwareType>CLXMCNet</AxisHardwareType></YAxis>
              </FixedSide>
            </WallExtruder>
            """, System.Text.Encoding.Unicode);

        try
        {
            var map = AxisHardware.Read(path);

            Assert.Equal(3, map.Axes.Count);
            Assert.Equal(2, map.InUse.Count());
            Assert.True(map.IsMixed);
            Assert.Equal("WallExtruder/FixedSide/ServoGuns/ServoGun",
                map.Axes.Single(a => a.Name == "ServoGun").Path);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void AMissingOrUnreadableConfigIsReportedAsNothingKnown()
    {
        Assert.False(AxisHardware.Read("").Any);
        Assert.False(AxisHardware.Read("/no/such/file.xml").Any);
    }
}

public class OmronServoDriveTests
{
    [Fact]
    public void DecodesTheDriveInThePhotoTheSameWayItsOwnLabelReads()
    {
        // The photographed drive is labelled 400V 3PH 1.5kW.
        var drive = OmronServoDrives.Describe("R88D-1SN15F-ECT")!;

        Assert.Equal(1500, drive.Watts);
        Assert.Equal(400, drive.Volts);
        Assert.Equal(10, drive.DischargeWaitMinutes);
        Assert.Contains("1.5 kW", drive.Description);
    }

    [Theory]
    [InlineData("R88D-1SN01L-ECT", 100, 100, 15)]
    [InlineData("R88D-1SN04H-ECT", 400, 200, 15)]
    [InlineData("R88D-1SN55H-ECT", 5500, 200, 20)]
    [InlineData("R88D-1SN150H-ECT", 15000, 200, 20)]
    [InlineData("R88D-1SN150F-ECT", 15000, 400, 10)]
    public void DecodesCapacityVoltageAndDischargeTime(string model, int watts, int volts, int minutes)
    {
        var drive = OmronServoDrives.Describe(model)!;

        Assert.Equal(watts, drive.Watts);
        Assert.Equal(volts, drive.Volts);
        Assert.Equal(minutes, drive.DischargeWaitMinutes);
    }

    [Theory]
    [InlineData("R88D-KN10H-ECT")]   // G5, a different family with a different alarm table
    [InlineData("not a model")]
    [InlineData("")]
    [InlineData(null)]
    public void OnlyOneSDriveModelsAreDecoded(string? model)
    {
        Assert.Null(OmronServoDrives.Describe(model));
    }

    [Fact]
    public void EveryDischargeTimeIsOneTheManualGives()
    {
        // These are a shock hazard, not a guideline - a made-up value here could hurt somebody.
        foreach (var alarm in new[] { "01L", "02L", "04L", "01H", "02H", "04H", "08H", "10H", "15H",
                                      "20H", "30H", "55H", "75H", "150H", "06F", "10F", "15F", "20F",
                                      "30F", "55F", "75F", "150F" })
        {
            var drive = OmronServoDrives.Describe($"R88D-1SN{alarm}-ECT")!;
            Assert.Contains(drive.DischargeWaitMinutes, new[] { 10, 15, 20 });
        }
    }

    [Fact]
    public void LooksUpTheAlarmsSeenMostInTheField()
    {
        Assert.Equal("Overload", OmronServoDrives.Lookup("16.00")!.Meaning);
        Assert.Equal("Overcurrent", OmronServoDrives.Lookup("14.0")!.Meaning);
        Assert.Contains("phase loss", OmronServoDrives.Lookup("13.01")!.Meaning);
    }

    [Fact]
    public void AGroupCodeMatchesAnySubcodeUnderIt()
    {
        // 83.xx and 90.xx are families - the subcode narrows them further.
        Assert.Contains("EtherCAT", OmronServoDrives.Lookup("83.03")!.Meaning);
        Assert.Contains("EtherCAT", OmronServoDrives.Lookup("83.21")!.Meaning);
        Assert.Contains("configuration", OmronServoDrives.Lookup("90.05")!.Meaning);
    }

    [Fact]
    public void AnOvercurrentIsMarkedAsNotSomethingToKeepResetting()
    {
        Assert.True(OmronServoDrives.Lookup("14.0")!.Urgent);
        Assert.Contains("Do not keep resetting", OmronServoDrives.Lookup("14.0")!.FirstChecks);
        Assert.False(OmronServoDrives.Lookup("16.00")!.Urgent);
    }

    [Fact]
    public void ReadsAlarmsWrittenTheWayTheDriveShowsThem()
    {
        var found = OmronServoDrives.Find(new[]
        {
            "drive reported Er 16 00 on the saw rotation axis",
            "and then Er 83 03"
        });

        Assert.Equal(2, found.Count);
        Assert.Contains(found, a => a.Meaning == "Overload");
        Assert.Contains(found, a => a.Code == "83");
    }

    [Fact]
    public void ABareNumberIsNotReadAsAnOmronAlarm()
    {
        // Without the Er prefix, "16.00" is a timestamp or a version far more often than an alarm.
        Assert.Empty(OmronServoDrives.Find(new[]
        {
            "16.00 something happened",
            "version 14.01 installed",
            "13:16:58.295 MotionEvent"
        }));
    }

    [Fact]
    public void TheSourceOfTheAlarmMeaningsIsStated()
    {
        // These get quoted to customers, and they did not come from Omron.
        Assert.Contains("not Omron", OmronServoDrives.AlarmSource);
        Assert.Contains("I586", OmronServoDrives.AlarmSource);
    }

    [Fact]
    public void TheReportTellsYouToReadTheOmronDriveWhenTheMachineHasOmronAxes()
    {
        var log = MachineLogFile.Parse(new[]
        {
            "13:12:07.2470000,  MotionEvent, Axis-FixedSidePusher,  F02 Encoder Wiring Fault"
        });

        var config = Path.Combine(Path.GetTempPath(), $"mix-{Guid.NewGuid():N}.xml");
        File.WriteAllText(config, """
            <?xml version="1.0" encoding="utf-16"?>
            <WallExtruder>
              <Trolley><InUse>true</InUse><AxisHardwareType>CIPNet</AxisHardwareType></Trolley>
              <ServoGun><InUse>true</InUse><AxisHardwareType>CLXMCNet</AxisHardwareType></ServoGun>
            </WallExtruder>
            """, System.Text.Encoding.Unicode);

        try
        {
            var analysis = new SpidaLogAnalyser().Analyse(
                log, Array.Empty<ErrLogEntry>(), Array.Empty<ChangeLogEntry>(), new DateTime(2026, 9, 15));

            var text = SpidaReportFormatter.Format(
                new DiagnosticFileSummary(), analysis,
                KnowledgeAnnotator.Annotate(analysis, log, "WallExtruderDG", "M20616", config));

            Assert.Contains("runs both families", text);
            Assert.Contains("R88D-1SN", text);
            Assert.Contains("Er 16 00", text);
            Assert.Contains("CHARGE stays lit", text);
        }
        finally
        {
            File.Delete(config);
        }
    }
}
