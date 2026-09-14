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
    [Fact]
    public void ReadsTimestampedErrors()
    {
        var entries = ErrLogFile.Parse(["2026-08-10 10:25:00, Object reference not set to an instance of an object."]);

        var entry = Assert.Single(entries);
        Assert.Equal(new DateTime(2026, 8, 10, 10, 25, 0), entry.Timestamp);
        Assert.Contains("Object reference", entry.Text);
    }

    [Fact]
    public void FoldsStackTraceLinesIntoTheEntryAbove()
    {
        var entries = ErrLogFile.Parse([
            "2026-08-10 10:25:00, Object reference not set",
            "   at Spida.Ui.SaveSettings()",
            "   at Spida.Ui.Button_Click()"
        ]);

        var entry = Assert.Single(entries);
        Assert.Contains("SaveSettings", entry.Text);
    }

    [Fact]
    public void GroupsRepeatsByMaskingNumbers()
    {
        var first = ErrLogFile.Parse(["2026-08-10 10:25:00, Timeout after 30 ms"])[0];
        var second = ErrLogFile.Parse(["2026-08-10 11:00:00, Timeout after 45 ms"])[0];

        Assert.Equal(first.Signature, second.Signature);
    }
}

public class ChangeLogFileTests
{
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
        var errors = ErrLogFile.Parse(["2026-08-10 09:00:30, Object reference not set to an instance of an object"]);
        var changes = ChangeLogFile.Parse(["2026-08-10 09:00:30, mark, Saw, BladeSpeed, 1200, 1400"]);

        var analysis = new SpidaLogAnalyser().Analyse(TwoAttempts(), errors, changes, Session);

        Assert.Single(analysis.CosmeticErrors);
        Assert.Empty(analysis.RealErrors);
    }

    [Fact]
    public void AnErrorOutsideTheMachineLogWindowIsSeparatedOut()
    {
        // The log runs 09:00 to 09:01; this error is hours later.
        var errors = ErrLogFile.Parse(["2026-08-10 18:00:00, Something failed"]);

        var analysis = new SpidaLogAnalyser().Analyse(TwoAttempts(), errors, [], Session);

        Assert.Single(analysis.ErrorsOutsideLogWindow);
        Assert.Empty(analysis.RealErrors);
    }

    [Fact]
    public void AnErrorInsideTheWindowWithNoMatchingChangeIsReal()
    {
        var errors = ErrLogFile.Parse(["2026-08-10 09:01:10, Servo Not Setup"]);

        var analysis = new SpidaLogAnalyser().Analyse(TwoAttempts(), errors, [], Session);

        Assert.Single(analysis.RealErrors);
        Assert.Empty(analysis.CosmeticErrors);
    }

    [Fact]
    public void AnErrorSeenEverySessionIsTreatedAsBackground()
    {
        var lines = Enumerable.Range(0, 8)
            .Select(i => $"2026-08-10 09:00:{i:00}, Licence check failed after {i} tries")
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
