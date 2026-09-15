using DiagFileMonitor.Core.Knowledge;
using DiagFileMonitor.Core.Models;
using DiagFileMonitor.Core.SpidaLogs;

namespace DiagFileMonitor.Core.Tests;

public class MachineLogFileTests
{
    [Fact]
    public void ReadsTheFourFieldFormat()
    {
        var entries = MachineLogFile.Parse([
            "09:15:22.1234567,  InputChange,  StartButton,  Pressed"
        ]);

        var entry = Assert.Single(entries);
        Assert.Equal(9, entry.Time.Hours);
        Assert.Equal(15, entry.Time.Minutes);
        Assert.Equal(22, entry.Time.Seconds);
        Assert.Equal(123, entry.Time.Milliseconds);
        Assert.Equal(MachineLogCategory.InputChange, entry.Category);
        Assert.Equal("StartButton", entry.Tag);
        Assert.Equal("Pressed", entry.Description);
    }

    [Fact]
    public void RecognisesEveryDocumentedCategory()
    {
        var entries = MachineLogFile.Parse([
            "09:00:00.0000000, InputChange, A, x",
            "09:00:01.0000000, OutputChange, B, x",
            "09:00:02.0000000, MotionEvent, C, x",
            "09:00:03.0000000, Other, D, x"
        ]);

        Assert.Equal(
            [MachineLogCategory.InputChange, MachineLogCategory.OutputChange,
             MachineLogCategory.MotionEvent, MachineLogCategory.Other],
            entries.Select(e => e.Category));
    }

    [Fact]
    public void HandlesTabSeparatedExports()
    {
        var entries = MachineLogFile.Parse(["09:15:22.1234567\tOther\tStep\tStep = 10"]);

        var entry = Assert.Single(entries);
        Assert.Equal(MachineLogCategory.Other, entry.Category);
        Assert.Equal("Step = 10", entry.Description);
    }

    [Fact]
    public void StripsWindowsLineEndings()
    {
        var entry = Assert.Single(MachineLogFile.Parse(["09:15:22.0000000, Other, Tag, Something happened\r"]));

        Assert.Equal("Something happened", entry.Description);
    }

    [Fact]
    public void KeepsCommasInsideTheDescription()
    {
        var entry = Assert.Single(MachineLogFile.Parse([
            "09:15:22.0000000, Other, Fault, Cannot Move Towards Operator, Check Panel And Restart"
        ]));

        Assert.Equal("Cannot Move Towards Operator, Check Panel And Restart", entry.Description);
    }

    [Fact]
    public void SkipsLinesWithNoTimestamp()
    {
        var entries = MachineLogFile.Parse(["some header text", "09:15:22.0000000, Other, T, ok"]);

        Assert.Single(entries);
    }

    [Fact]
    public void FindsTheMachineModelReportedAtPowerOn()
    {
        var entries = MachineLogFile.Parse([
            "09:00:00.0000000, Other, Startup, Machine Model = Apollo",
            "09:00:01.0000000, Other, Step, Step = 10"
        ]);

        Assert.Equal("Apollo", MachineLogFile.FindMachineModel(entries));
    }
}

public class ErrLogFileTests
{
    /// <summary>Builds a block in the exact shape SDN writes, including the trailing fields.</summary>
    internal static string[] Block(string dateTime, string message, string method = "Run()") =>
    [
        "Date/Time: " + dateTime,
        "===========================================================================================",
        "",
        "Title: SDN",
        "Message: " + message,
        "Source: First",
        "Method: " + method,
        "StackTrace:    at System.Linq.Enumerable.First[TSource](IEnumerable`1 source)",
        "   at SDN.ClsWallExtruderPLC.get_StartYHeight()",
        "Additional Info: ",
        "Errors missed since last Log: 0",
        "Last Excecute CMD : ",
        "Last Logged Mem Usage : 0"
    ];

    [Fact]
    public void ReadsTheBlockFormatSdnActuallyWrites()
    {
        var entry = Assert.Single(ErrLogFile.Parse(Block("10/02/2025 8:25:04 pm", "Sequence contains no elements")));

        Assert.Equal(new DateTime(2025, 2, 10, 20, 25, 4), entry.Timestamp);
        Assert.Equal("Sequence contains no elements", entry.Message);
        Assert.Equal("SDN", entry.Title);
        Assert.Equal("First", entry.Source);
    }

    [Fact]
    public void ReadsDayFirstDatesWithALowercaseMeridiem()
    {
        // 21/04/2026 is 21 April, not an invalid month, and the file writes "pm" in lowercase.
        var entry = Assert.Single(ErrLogFile.Parse(Block("21/04/2026 2:16:13 pm", "Something failed")));

        Assert.Equal(new DateTime(2026, 4, 21, 14, 16, 13), entry.Timestamp);
    }

    [Fact]
    public void KeepsTheTopOfTheStackTrace()
    {
        var entry = Assert.Single(ErrLogFile.Parse(Block("10/02/2025 8:25:04 pm", "Sequence contains no elements")));

        Assert.Contains("get_StartYHeight", entry.TopOfStack);
    }

    [Fact]
    public void SeparatesConsecutiveBlocks()
    {
        var lines = Block("10/02/2025 8:25:04 pm", "First problem")
            .Concat(Block("10/02/2025 8:25:05 pm", "Second problem"))
            .ToArray();

        var entries = ErrLogFile.Parse(lines);

        Assert.Equal(2, entries.Count);
        Assert.Equal("First problem", entries[0].Message);
        Assert.Equal("Second problem", entries[1].Message);
    }

    [Fact]
    public void GroupsRepeatsByMaskingNumbers()
    {
        var first = ErrLogFile.Parse(Block("10/02/2025 8:25:04 pm", "Timeout after 30 ms"))[0];
        var second = ErrLogFile.Parse(Block("10/02/2025 9:00:00 pm", "Timeout after 45 ms"))[0];

        Assert.Equal(first.Signature, second.Signature);
    }

    [Fact]
    public void SkipsABlockWithNoTitleOrMessage()
    {
        var entries = ErrLogFile.Parse([
            "Date/Time: 10/02/2025 8:25:04 pm",
            "===========================================",
            "Last Logged Mem Usage : 0"
        ]);

        Assert.Empty(entries);
    }
}

public class ChangeLogFileTests
{
    [Fact]
    public void ReadsTheDayFirstFormatRealFilesUse()
    {
        var entry = Assert.Single(ChangeLogFile.Parse(
            ["21/04/2026 2:16:13 pm, Spida, WallExtruder, RWEPanelHeightGap, 2, 0"]));

        Assert.Equal(new DateTime(2026, 4, 21, 14, 16, 13), entry.Timestamp);
        Assert.Equal("Spida", entry.User);
        Assert.Equal("RWEPanelHeightGap", entry.Setting);
        Assert.Equal("2", entry.OldValue);
        Assert.Equal("0", entry.NewValue);
    }

    [Fact]
    public void ReadsTheSixFieldFormat()
    {
        var entries = ChangeLogFile.Parse(["2026-08-10 10:25:00, mark, Saw, BladeSpeed, 1200, 1400"]);

        var entry = Assert.Single(entries);
        Assert.Equal(new DateTime(2026, 8, 10, 10, 25, 0), entry.Timestamp);
        Assert.Equal("mark", entry.User);
        Assert.Equal("BladeSpeed", entry.Setting);
        Assert.Equal("1200", entry.OldValue);
        Assert.Equal("1400", entry.NewValue);
    }

    [Fact]
    public void SkipsLinesThatAreNotEntries()
    {
        var entries = ChangeLogFile.Parse(["--- start of log ---", "2026-08-10 10:25:00, m, C, S, 1, 2"]);

        Assert.Single(entries);
    }
}

public class SpidaLogAnalyserTests
{
    private static readonly DateTime Session = new(2026, 8, 10, 12, 0, 0, DateTimeKind.Utc);

    private static string Line(string time, string category, string tag, string description) =>
        $"{time}, {category}, {tag}, {description}";

    /// <summary>Two attempts: the first completes, the second faults. Step resets mark the boundary.</summary>
    private static IReadOnlyList<MachineLogEntry> TwoAttempts() => MachineLogFile.Parse([
        Line("09:00:00.0000000", "Other", "Step", "Step = 10"),
        Line("09:00:01.0000000", "InputChange", "StartButton", "Pressed"),
        Line("09:00:05.0000000", "Other", "Step", "Step = 40"),
        Line("09:00:30.0000000", "Other", "Status", "Panel Assembled"),

        Line("09:01:00.0000000", "Other", "Step", "Step = 10"),
        Line("09:01:02.0000000", "OutputChange", "Gripper", "Down"),
        Line("09:01:03.0000000", "Other", "Step", "Step = 40"),
        Line("09:01:20.0000000", "Other", "Fault",
            "Cannot Move Towards Operator While Grippers Are Down - Check Panel And Restart")
    ]);

    [Fact]
    public void SplitsTheLogWhereTheStepCounterResets()
    {
        var analysis = new SpidaLogAnalyser().Analyse(TwoAttempts(), [], [], Session);

        Assert.Equal(2, analysis.Cycles.Count);
        Assert.Equal(1, analysis.SecondToLast!.Number);
        Assert.Equal(2, analysis.Last!.Number);
    }

    [Fact]
    public void TellsACompletedAttemptFromAFaultedOne()
    {
        var analysis = new SpidaLogAnalyser().Analyse(TwoAttempts(), [], [], Session);

        Assert.True(analysis.SecondToLast!.Completed);
        Assert.False(analysis.Last!.Completed);
        Assert.Contains("Cannot Move Towards Operator", analysis.Last.Outcome);
    }

    [Fact]
    public void PullsTheFaultTextAndTheStepItHappenedAt()
    {
        var analysis = new SpidaLogAnalyser().Analyse(TwoAttempts(), [], [], Session);

        var fault = Assert.Single(analysis.Last!.Faults);
        Assert.Contains("Grippers Are Down", fault.Text);
        Assert.Equal(40, fault.StepAtFault);
        Assert.NotEmpty(fault.Context);
    }

    [Fact]
    public void DoesNotTreatAStepCounterAsAFault()
    {
        var analysis = new SpidaLogAnalyser().Analyse(TwoAttempts(), [], [], Session);

        Assert.DoesNotContain(analysis.Cycles.SelectMany(c => c.Faults), f => f.Text.Contains("Step ="));
    }

    [Fact]
    public void FlagsAFaultThatRepeatsAcrossAttempts()
    {
        var log = MachineLogFile.Parse([
            Line("09:00:00.0000000", "Other", "Step", "Step = 10"),
            Line("09:00:10.0000000", "Other", "Step", "Step = 40"),
            Line("09:00:20.0000000", "Other", "Fault", "Lost product, revert and try again"),
            Line("09:01:00.0000000", "Other", "Step", "Step = 10"),
            Line("09:01:10.0000000", "Other", "Step", "Step = 40"),
            Line("09:01:20.0000000", "Other", "Fault", "Lost product, revert and try again")
        ]);

        var repeat = Assert.Single(new SpidaLogAnalyser().Analyse(log, [], [], Session).RepeatedFaults);

        Assert.Equal(2, repeat.Occurrences);
        Assert.Equal([1, 2], repeat.CycleNumbers);
    }

    [Fact]
    public void AOneOffFaultIsNotReportedAsRepeating()
    {
        var log = MachineLogFile.Parse([
            Line("09:00:00.0000000", "Other", "Step", "Step = 10"),
            Line("09:00:10.0000000", "Other", "Step", "Step = 40"),
            Line("09:00:20.0000000", "Other", "Fault", "Lost product, revert and try again"),
            Line("09:01:00.0000000", "Other", "Step", "Step = 10"),
            Line("09:01:30.0000000", "Other", "Status", "Panel Assembled")
        ]);

        Assert.Empty(new SpidaLogAnalyser().Analyse(log, [], [], Session).RepeatedFaults);
    }

    [Fact]
    public void IgnoresTheBackgroundNoiseTheGuideListsAsNotWorthReporting()
    {
        var log = MachineLogFile.Parse([
            Line("09:00:00.0000000", "Other", "Step", "Step = 10"),
            Line("09:00:01.0000000", "Other", "Comms", "Comms timeout on driver 3, retrying"),
            Line("09:00:02.0000000", "Other", "Air", "Air supply pressure is low"),
            Line("09:00:03.0000000", "Other", "Upload", "Background upload task failed, will retry")
        ]);

        Assert.Empty(new SpidaLogAnalyser().Analyse(log, [], [], Session).Cycles.SelectMany(c => c.Faults));
    }

    [Fact]
    public void ARepeatedStepValueDoesNotStartANewUnit()
    {
        // A step logged twice at the same value is one unit, not two. Only a drop is a reset.
        var log = MachineLogFile.Parse([
            Line("09:00:00.0000000", "Other", "Step", "Step = 10"),
            Line("09:00:05.0000000", "Other", "Step", "Step = 10"),
            Line("09:00:10.0000000", "Other", "Step", "Step = 20")
        ]);

        Assert.Single(new SpidaLogAnalyser().Analyse(log, [], [], Session).Cycles);
    }

    [Fact]
    public void AFalseStartIsNotCountedAsACompletion()
    {
        // "Complete" one second after the start is a false start, per the guide.
        var log = MachineLogFile.Parse([
            Line("09:00:00.0000000", "Other", "Step", "Step = 10"),
            Line("09:00:01.0000000", "Other", "Status", "Complete")
        ]);

        Assert.False(new SpidaLogAnalyser().Analyse(log, [], [], Session).Last!.Completed);
    }

    [Fact]
    public void AnErrorWithAMatchingSettingsChangeIsTreatedAsCosmetic()
    {
        var errors = ErrLogFile.Parse(ErrLogFileTests.Block("10/08/2026 9:00:30 am", "Object reference not set"));
        var changes = ChangeLogFile.Parse(["2026-08-10 09:00:30, mark, Saw, BladeSpeed, 1200, 1400"]);

        var analysis = new SpidaLogAnalyser().Analyse(TwoAttempts(), errors, changes, Session);

        Assert.Single(analysis.CosmeticErrors);
        Assert.Empty(analysis.RealErrors);
    }

    [Fact]
    public void AnErrorOutsideTheMachineLogWindowIsSeparatedOut()
    {
        // The log runs 09:00 to 09:01; this error is hours later.
        var errors = ErrLogFile.Parse(ErrLogFileTests.Block("10/08/2026 6:00:00 pm", "Something failed"));

        var analysis = new SpidaLogAnalyser().Analyse(TwoAttempts(), errors, [], Session);

        Assert.Single(analysis.ErrorsOutsideLogWindow);
        Assert.Empty(analysis.RealErrors);
    }

    [Fact]
    public void AnErrorInsideTheWindowWithNoMatchingChangeIsReal()
    {
        var errors = ErrLogFile.Parse(ErrLogFileTests.Block("10/08/2026 9:01:10 am", "Servo Not Setup"));

        var analysis = new SpidaLogAnalyser().Analyse(TwoAttempts(), errors, [], Session);

        Assert.Single(analysis.RealErrors);
        Assert.Empty(analysis.CosmeticErrors);
    }

    [Fact]
    public void AnErrorSeenEverySessionIsTreatedAsBackground()
    {
        var lines = Enumerable.Range(0, 8)
            .SelectMany(i => ErrLogFileTests.Block($"10/08/2026 9:00:{i:00} am", $"Licence check failed after {i} tries"))
            .ToList();

        var analysis = new SpidaLogAnalyser().Analyse(TwoAttempts(), ErrLogFile.Parse(lines), [], Session);

        Assert.NotEmpty(analysis.RepeatingBackgroundErrors);
        Assert.Empty(analysis.RealErrors);
    }

    [Fact]
    public void ListsSettingsChangedAroundTheSession()
    {
        var changes = ChangeLogFile.Parse([
            "2019-01-01 08:00:00, old, C, AncientSetting, 1, 2",
            "2026-08-10 08:30:00, mark, Saw, BladeSpeed, 1200, 1400"
        ]);

        var recent = new SpidaLogAnalyser().Analyse(TwoAttempts(), [], changes, Session).RecentSettingChanges;

        Assert.Equal("BladeSpeed", Assert.Single(recent).Setting);
    }

    [Fact]
    public void SaysSoWhenTheMachineLogIsEmpty()
    {
        var analysis = new SpidaLogAnalyser().Analyse([], [], [], Session);

        Assert.Empty(analysis.Cycles);
        Assert.Contains(analysis.Notes, n => n.Contains("MachineLog.txt was empty"));
    }
}

public class StepProfileOpenStepTests
{
    private static IReadOnlyList<MachineLogEntry> Log(params string[] lines) => MachineLogFile.Parse(lines);

    [Fact]
    public void TheStepTheLogEndsInIsLeftOutOfTheTimings()
    {
        // Two exports of the same session differed only by a trailing SharePoint upload line
        // written 1.3 minutes after the machine stopped. Measuring the last step to the end of
        // the file made one export read as 421% slower than the other, which is nonsense.
        var withoutTrailingLine = StepProfile.From(Log(
            "10:00:00.0000000,  Other, WallExtruderStep,  Step = 10",
            "10:00:01.0000000,  Other, WallExtruderStep,  Step = 20",
            "10:00:05.0000000,  Other, WallExtruderStep,  Step = 30",
            "10:00:05.0300000,  MotionEvent, FixedSidePuller,  Axis Disabled"));

        var withTrailingLine = StepProfile.From(Log(
            "10:00:00.0000000,  Other, WallExtruderStep,  Step = 10",
            "10:00:01.0000000,  Other, WallExtruderStep,  Step = 20",
            "10:00:05.0000000,  Other, WallExtruderStep,  Step = 30",
            "10:00:05.0300000,  MotionEvent, FixedSidePuller,  Axis Disabled",
            "10:01:20.0000000,  Other, SharepointReporting,  Upload succeeded"));

        Assert.True(withoutTrailingLine.FinalStepWasStillRunning);
        Assert.True(withTrailingLine.FinalStepWasStillRunning);

        // Step 30 never finished, so neither export reports a duration for it at all.
        Assert.False(withoutTrailingLine.MedianDuration.ContainsKey(30));
        Assert.False(withTrailingLine.MedianDuration.ContainsKey(30));

        // And the housekeeping line makes no difference to anything.
        Assert.Equal(withoutTrailingLine.MedianCycleDuration, withTrailingLine.MedianCycleDuration);
        Assert.Equal(withoutTrailingLine.MedianDuration[10], withTrailingLine.MedianDuration[10]);
        Assert.Equal(withoutTrailingLine.MedianDuration[20], withTrailingLine.MedianDuration[20]);
    }

    [Fact]
    public void StepsThatDidFinishAreStillTimed()
    {
        var profile = StepProfile.From(Log(
            "10:00:00.0000000,  Other, WallExtruderStep,  Step = 10",
            "10:00:01.0000000,  Other, WallExtruderStep,  Step = 20",
            "10:00:05.0000000,  Other, WallExtruderStep,  Step = 30"));

        Assert.Equal(TimeSpan.FromSeconds(1), profile.MedianDuration[10]);
        Assert.Equal(TimeSpan.FromSeconds(4), profile.MedianDuration[20]);
        Assert.Equal(TimeSpan.FromSeconds(5), profile.MedianCycleDuration);
    }

    [Fact]
    public void AUnitWhoseOnlyStepWasTheOpenOneIsNotCountedAsAUnit()
    {
        // A trailing step drop leaves a final unit holding nothing but the open step. It has no
        // timing in it, so counting it drags the median cycle time towards zero.
        var profile = StepProfile.From(Log(
            "10:00:00.0000000,  Other, WallExtruderStep,  Step = 10",
            "10:00:01.0000000,  Other, WallExtruderStep,  Step = 20",
            "10:00:05.0000000,  Other, WallExtruderStep,  Step = 30",
            "10:00:08.0000000,  InputChange, PlateClampUp,  Input (192.168.250.1-0.10) Changed to 1",
            "10:00:09.0000000,  Other, WallExtruderStep,  Step = 0",
            "10:02:00.0000000,  Other, SharepointReporting,  Upload succeeded"));

        Assert.Equal(1, profile.CycleCount);
        Assert.Equal(TimeSpan.FromSeconds(8), profile.MedianCycleDuration);
        Assert.False(profile.MedianDuration.ContainsKey(0));
        Assert.Equal(TimeSpan.FromSeconds(3), profile.MedianDuration[30]);
    }

    [Fact]
    public void TheStepTheLogEndsInStillCountsAsAStepTheMachineReached()
    {
        var profile = StepProfile.From(Log(
            "10:00:00.0000000,  Other, WallExtruderStep,  Step = 10",
            "10:00:01.0000000,  Other, WallExtruderStep,  Step = 20",
            "10:00:05.0000000,  Other, WallExtruderStep,  Step = 30"));

        // It ran step 30, we just do not know for how long.
        Assert.Equal([10, 20, 30], profile.Sequence);
        Assert.False(profile.MedianDuration.ContainsKey(30));
    }

    [Fact]
    public void TheLastStepOfAUnitRunsToTheEndOfThatUnit()
    {
        var profile = StepProfile.From(Log(
            "10:00:00.0000000,  Other, WallExtruderStep,  Step = 10",
            "10:00:02.0000000,  Other, WallExtruderStep,  Step = 20",
            "10:00:09.0000000,  InputChange, PlateClampUp,  Input (192.168.250.1-0.10) Changed to 1",
            "10:00:10.0000000,  Other, WallExtruderStep,  Step = 10",
            "10:00:12.0000000,  Other, WallExtruderStep,  Step = 20",
            "10:00:20.0000000,  Other, WallExtruderStep,  Step = 30"));

        Assert.Equal(2, profile.CycleCount);
        Assert.Equal(TimeSpan.FromSeconds(2), profile.MedianDuration[10]);

        // Unit 1's step 20 ran to the end of unit 1 (7s), unit 2's to its step 30 (8s).
        // The idle time between units is not charged to either.
        Assert.Equal(TimeSpan.FromSeconds(7.5), profile.MedianDuration[20]);
    }
}

public class LatestSettingChangeTests
{
    private static readonly DateTime Session = new(2026, 7, 27, 22, 15, 0, DateTimeKind.Utc);

    private static ChangeLogEntry Change(string date, string setting) => new()
    {
        Timestamp = DateTime.Parse(date), User = "Spida", Category = "Settings",
        Setting = setting, OldValue = "1", NewValue = "2"
    };

    private static SpidaLogAnalysis Analyse(params ChangeLogEntry[] changes) =>
        new SpidaLogAnalyser().Analyse(
            Array.Empty<MachineLogEntry>(), Array.Empty<ErrLogEntry>(), changes, Session);

    [Fact]
    public void TheLastFourChangesAreKeptHoweverOldTheyAre()
    {
        // The real M20716 file had nothing changed near the session but plenty changed months
        // earlier, and the report said only "None." - which reads as "nothing was ever changed".
        var analysis = Analyse(
            Change("2025-02-28 12:16:19", "Oldest"),
            Change("2026-01-20 14:12:54", "Older"),
            Change("2026-04-21 14:26:51", "Old"),
            Change("2026-05-05 13:09:03", "Newer"),
            Change("2026-05-05 13:09:04", "Newest"));

        Assert.Empty(analysis.RecentSettingChanges);

        Assert.Equal(
            new[] { "Newest", "Newer", "Old", "Older" },
            analysis.LatestSettingChanges.Select(c => c.Setting));
    }

    [Fact]
    public void FewerThanFourChangesGivesWhateverThereIs()
    {
        var analysis = Analyse(Change("2025-02-28 12:16:19", "Only"));

        Assert.Equal("Only", Assert.Single(analysis.LatestSettingChanges).Setting);
    }

    [Fact]
    public void AnEmptyChangeLogGivesNoLatestChanges()
    {
        Assert.Empty(Analyse().LatestSettingChanges);
    }

    [Fact]
    public void ChangesAroundTheSessionStillShowSeparately()
    {
        var analysis = Analyse(
            Change("2026-02-01 09:00:00", "Old"),
            Change("2026-07-27 09:00:00", "OnTheDay"));

        Assert.Equal("OnTheDay", Assert.Single(analysis.RecentSettingChanges).Setting);
        Assert.Equal(new[] { "OnTheDay", "Old" }, analysis.LatestSettingChanges.Select(c => c.Setting));
    }

    [Fact]
    public void TheReportShowsOldChangesRatherThanSayingNone()
    {
        var analysis = Analyse(Change("2026-05-05 13:09:03", "NailDistFromEdgeOfTimber"));
        var text = SpidaReportFormatter.Format(new DiagnosticFileSummary { ArrivedAtUtc = Session }, analysis);

        Assert.Contains("Nothing was changed around this session.", text);
        Assert.Contains("Last 1 change(s) on this machine, whenever they happened:", text);
        Assert.Contains("NailDistFromEdgeOfTimber", text);
        Assert.Contains("month(s) before", text);
    }

    [Fact]
    public void AChangeAlreadyListedAroundTheSessionIsNotRepeated()
    {
        var analysis = Analyse(Change("2026-07-27 09:00:00", "OnTheDay"));
        var text = SpidaReportFormatter.Format(new DiagnosticFileSummary { ArrivedAtUtc = Session }, analysis);

        Assert.Single(analysis.RecentSettingChanges);
        Assert.DoesNotContain("whenever they happened", text);
        Assert.Equal(1, text.Split("OnTheDay").Length - 1);
    }
}

public class EndOfLogTests
{
    private static SpidaLogAnalysis Analyse(params string[] lines)
    {
        var log = MachineLogFile.Parse(lines);
        return new SpidaLogAnalyser().Analyse(
            log, Array.Empty<ErrLogEntry>(), Array.Empty<ChangeLogEntry>(), new DateTime(2026, 9, 15));
    }

    [Fact]
    public void TheLastNotableEventIgnoresTheIoChatterThatTicksOnAfterwards()
    {
        // The machine stopped at 13:30:04 and a hand button was pressed 10 minutes later. The
        // button is not what the operator is reporting.
        var analysis = Analyse(
            "13:30:04.4890000,  MotionEvent, Axis-FixedSidePusher,  F02 Encoder Wiring Fault",
            "13:40:57.0000000,  InputChange, THNTD,  Input (TCP192.168.50.2-1.12) Changed to 0");

        Assert.Equal("Axis-FixedSidePusher", analysis.LastNotableEvent!.Tag);
        Assert.Contains("F02", analysis.LastNotableEvent.Description);
    }

    [Fact]
    public void ALongQuietTailIsMeasuredSoItCanBeReported()
    {
        var analysis = Analyse(
            "13:30:04.0000000,  MotionEvent, Axis-A,  F14 Comms Fail",
            "13:40:04.0000000,  InputChange, THNTD,  Input (x) Changed to 0");

        Assert.Equal(TimeSpan.FromMinutes(10), analysis.SilenceBeforeEnd);
    }

    [Fact]
    public void AMachineStillRunningAtTheEndHasNoQuietTail()
    {
        var analysis = Analyse(
            "13:30:00.0000000,  MotionEvent, Axis-A,  Axis Enabled",
            "13:30:01.0000000,  Other, WallExtruderStep,  Step = 10");

        Assert.Equal(TimeSpan.FromSeconds(0), analysis.SilenceBeforeEnd);
    }

    [Fact]
    public void TheTailOfTheLogIsKeptForQuoting()
    {
        var lines = Enumerable.Range(0, 30)
            .Select(i => $"10:00:{i:00}.0000000,  Other, Step,  Step = {i}")
            .ToArray();

        var analysis = Analyse(lines);

        Assert.Equal(12, analysis.FinalEntries.Count);
        Assert.Equal("Step = 29", analysis.FinalEntries[^1].Description);
    }

    [Fact]
    public void TheReportLeadsWithHowItEndedBeforeTheUnits()
    {
        var analysis = Analyse(
            "13:12:07.0000000,  MotionEvent, Axis-A,  F02 Encoder Wiring Fault",
            "13:30:04.0000000,  MotionEvent, Axis-B,  F14 Comms Fail",
            "13:40:57.0000000,  InputChange, THNTD,  Input (x) Changed to 0");

        var text = SpidaReportFormatter.Format(new DiagnosticFileSummary(), analysis);

        Assert.True(text.IndexOf("HOW IT ENDED", StringComparison.Ordinal)
                    < text.IndexOf("UNITS ATTEMPTED", StringComparison.Ordinal));
        Assert.Contains("The machine was sitting", text);
        Assert.Contains("10.9 min", text);
    }

    [Fact]
    public void TheMostRecentDriveCodeIsReportedBeforeAnOlderOneRaisedMoreOften()
    {
        // F02 three times early, F14 once at the end. The file was taken minutes after the
        // problem, so F14 is the one being reported however often F02 fired earlier.
        var log = MachineLogFile.Parse(new[]
        {
            "13:00:00.0000000,  MotionEvent, Axis-A,  F02 Encoder Wiring Fault",
            "13:01:00.0000000,  MotionEvent, Axis-A,  F02 Encoder Wiring Fault",
            "13:02:00.0000000,  MotionEvent, Axis-A,  F02 Encoder Wiring Fault",
            "13:30:00.0000000,  MotionEvent, Axis-B,  F14 Comms Fail"
        });

        var found = MotionControllerFaults.Find(log);

        Assert.Equal("F14", found[0].Code.Code);
        Assert.Equal("F02", found[1].Code.Code);
    }

    [Fact]
    public void AnEmptyLogProducesNoEndSection()
    {
        var analysis = Analyse();

        Assert.Empty(analysis.FinalEntries);
        Assert.Null(analysis.LastNotableEvent);
        Assert.Null(analysis.SilenceBeforeEnd);
        Assert.DoesNotContain("HOW IT ENDED", SpidaReportFormatter.Format(new DiagnosticFileSummary(), analysis));
    }
}
