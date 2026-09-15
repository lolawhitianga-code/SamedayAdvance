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
