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
