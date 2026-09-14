using DiagFileMonitor.Core.Models;
using DiagFileMonitor.Core.Services;

namespace DiagFileMonitor.Core.Tests;

public class MachineLogParserTests
{
    [Fact]
    public void PairsStartAndEndIntoTimedSteps()
    {
        var steps = new MachineLogParser().Parse([
            "2026-09-01 08:00:00.000 START Preheat",
            "2026-09-01 08:00:45.000 END Preheat"
        ]);

        var step = Assert.Single(steps);
        Assert.Equal("Preheat", step.Name);
        Assert.Equal(45, step.Duration.TotalSeconds);
    }

    [Fact]
    public void HandlesSeveralStepsAndAcceptsBeginFinishWording()
    {
        var steps = new MachineLogParser().Parse([
            "2026-09-01 08:00:00 BEGIN Preheat",
            "2026-09-01 08:01:00 FINISH Preheat",
            "2026-09-01 08:01:00 START Roast",
            "2026-09-01 08:11:00 END Roast"
        ]);

        Assert.Equal(2, steps.Count);
        Assert.Equal(60, steps[0].Duration.TotalSeconds);
        Assert.Equal(600, steps[1].Duration.TotalSeconds);
    }

    [Fact]
    public void IgnoresLinesItCannotUnderstand()
    {
        var steps = new MachineLogParser().Parse([
            "some preamble",
            "2026-09-01 08:00:00 START Preheat",
            "garbage in the middle",
            "2026-09-01 08:00:30 END Preheat"
        ]);

        Assert.Single(steps);
    }

    [Fact]
    public void IgnoresAStepThatNeverEnds()
    {
        var steps = new MachineLogParser().Parse(["2026-09-01 08:00:00 START Preheat"]);

        Assert.Empty(steps);
    }

    [Fact]
    public void SupportsACustomLineFormat()
    {
        var parser = new MachineLogParser(
            @"^\[(?<timestamp>[^\]]+)\]\s+(?<marker>START|END)\s*:\s*(?<step>.+)$");

        var steps = parser.Parse([
            "[2026-09-01T08:00:00] START: Calibrate",
            "[2026-09-01T08:00:20] END: Calibrate"
        ]);

        Assert.Equal(20, Assert.Single(steps).Duration.TotalSeconds);
    }
}

public class DiagnosticAnalyserTests
{
    private static DiagnosticFile Bundle(string extractDir, string machineType = "Roaster-5000",
        string serial = "SN-1", bool isBaseline = false, int id = 1)
    {
        var bundle = new DiagnosticFile
        {
            Id = id,
            OriginalFileName = $"{serial}.zip",
            SerialNumber = serial,
            MachineType = machineType,
            Customer = "Cafe A",
            IsBaseline = isBaseline,
            ArrivedAtUtc = new DateTime(2026, 9, 14, 12, 0, 0, DateTimeKind.Utc),
            ExtractedPath = extractDir
        };

        foreach (var (name, kind) in new[]
        {
            ("machinelog.txt", LogFileKind.MachineLog),
            ("errorlog.txt", LogFileKind.ErrorLog),
            ("changelog.txt", LogFileKind.ChangeLog)
        })
        {
            var path = Path.Combine(extractDir, name);
            if (File.Exists(path))
            {
                bundle.LogFiles.Add(new ExtractedLogFile { FileName = name, FullPath = path, Kind = kind });
            }
        }

        return bundle;
    }

    private static string WriteBundleFiles(string root, string folder,
        string? machineLog = null, string? errorLog = null, string? changeLog = null)
    {
        var dir = Path.Combine(root, folder);
        Directory.CreateDirectory(dir);
        if (machineLog is not null) File.WriteAllText(Path.Combine(dir, "machinelog.txt"), machineLog);
        if (errorLog is not null) File.WriteAllText(Path.Combine(dir, "errorlog.txt"), errorLog);
        if (changeLog is not null) File.WriteAllText(Path.Combine(dir, "changelog.txt"), changeLog);
        return dir;
    }

    private sealed class Workspace : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "diaganalyse", Guid.NewGuid().ToString("N"));
        public Workspace() => Directory.CreateDirectory(Root);
        public void Dispose() { if (Directory.Exists(Root)) Directory.Delete(Root, true); }
    }

    private const string HealthyLog = """
        2026-09-01 08:00:00 START Preheat
        2026-09-01 08:01:00 END Preheat
        2026-09-01 08:01:00 START Roast
        2026-09-01 08:11:00 END Roast
        """;

    [Fact]
    public void FlagsAStepThatIsMuchSlowerThanBaseline()
    {
        using var workspace = new Workspace();
        var baselineDir = WriteBundleFiles(workspace.Root, "baseline", HealthyLog);
        var faultyDir = WriteBundleFiles(workspace.Root, "faulty", """
            2026-09-01 08:00:00 START Preheat
            2026-09-01 08:05:00 END Preheat
            2026-09-01 08:05:00 START Roast
            2026-09-01 08:15:00 END Roast
            """);

        var report = new DiagnosticAnalyser().Analyse(
            Bundle(faultyDir, id: 2),
            [Bundle(baselineDir, isBaseline: true, id: 1)]);

        var slow = Assert.Single(report.Steps, s => s.IsSlow);
        Assert.Equal("Preheat", slow.StepName);
        Assert.Equal(500, slow.PercentOfBaseline);
        Assert.True(report.HasProblems);
        Assert.Contains(report.Findings, f => f.Area == "Step timing" && f.Detail.Contains("Preheat"));
    }

    [Fact]
    public void DoesNotFlagStepsRunningAtNormalSpeed()
    {
        using var workspace = new Workspace();
        var baselineDir = WriteBundleFiles(workspace.Root, "baseline", HealthyLog);
        var normalDir = WriteBundleFiles(workspace.Root, "normal", HealthyLog);

        var report = new DiagnosticAnalyser().Analyse(
            Bundle(normalDir, id: 2),
            [Bundle(baselineDir, isBaseline: true, id: 1)]);

        Assert.DoesNotContain(report.Steps, s => s.IsSlow);
        Assert.False(report.HasProblems);
    }

    [Fact]
    public void FlagsAStepThatNeverCompleted()
    {
        using var workspace = new Workspace();
        var baselineDir = WriteBundleFiles(workspace.Root, "baseline", HealthyLog);
        var faultyDir = WriteBundleFiles(workspace.Root, "faulty", """
            2026-09-01 08:00:00 START Preheat
            2026-09-01 08:01:00 END Preheat
            2026-09-01 08:01:00 START Roast
            """);

        var report = new DiagnosticAnalyser().Analyse(
            Bundle(faultyDir, id: 2),
            [Bundle(baselineDir, isBaseline: true, id: 1)]);

        Assert.Contains(report.Findings, f => f.Area == "Step missing" && f.Detail.Contains("Roast"));
        Assert.Contains(report.Steps, s => s.IsMissing && s.StepName == "Roast");
    }

    [Fact]
    public void UsesTheMedianAcrossSeveralBaselines()
    {
        using var workspace = new Workspace();

        // Two baselines at 60s and one outlier at 600s: the median stays 60s.
        var b1 = WriteBundleFiles(workspace.Root, "b1", HealthyLog);
        var b2 = WriteBundleFiles(workspace.Root, "b2", HealthyLog);
        var b3 = WriteBundleFiles(workspace.Root, "b3", """
            2026-09-01 08:00:00 START Preheat
            2026-09-01 08:10:00 END Preheat
            """);
        var faultyDir = WriteBundleFiles(workspace.Root, "faulty", """
            2026-09-01 08:00:00 START Preheat
            2026-09-01 08:03:00 END Preheat
            """);

        var report = new DiagnosticAnalyser().Analyse(
            Bundle(faultyDir, id: 4),
            [Bundle(b1, isBaseline: true, id: 1), Bundle(b2, isBaseline: true, id: 2), Bundle(b3, isBaseline: true, id: 3)]);

        Assert.Equal(3, report.BaselinesUsed);
        Assert.True(Assert.Single(report.Steps, s => s.StepName == "Preheat").IsSlow);
    }

    [Fact]
    public void OnlyComparesAgainstTheSameMachineType()
    {
        using var workspace = new Workspace();
        var otherTypeDir = WriteBundleFiles(workspace.Root, "other", HealthyLog);
        var faultyDir = WriteBundleFiles(workspace.Root, "faulty", HealthyLog);

        var report = new DiagnosticAnalyser().Analyse(
            Bundle(faultyDir, machineType: "Roaster-5000", id: 2),
            [Bundle(otherTypeDir, machineType: "Grinder-200", isBaseline: true, id: 1)]);

        Assert.Equal(0, report.BaselinesUsed);
        Assert.Contains(report.Findings, f => f.Area == "Baselines");
    }

    [Fact]
    public void IgnoresBundlesNotMarkedAsBaselines()
    {
        using var workspace = new Workspace();
        var notBaseline = WriteBundleFiles(workspace.Root, "other", HealthyLog);
        var faultyDir = WriteBundleFiles(workspace.Root, "faulty", HealthyLog);

        var report = new DiagnosticAnalyser().Analyse(
            Bundle(faultyDir, id: 2),
            [Bundle(notBaseline, isBaseline: false, id: 1)]);

        Assert.Equal(0, report.BaselinesUsed);
    }

    [Fact]
    public void ListsErrorLinesThatBaselinesDoNotHave()
    {
        using var workspace = new Workspace();
        var baselineDir = WriteBundleFiles(workspace.Root, "baseline", HealthyLog,
            errorLog: "W-0001 routine warning");
        var faultyDir = WriteBundleFiles(workspace.Root, "faulty", HealthyLog,
            errorLog: "W-0001 routine warning\nE-4021 thermostat fault");

        var report = new DiagnosticAnalyser().Analyse(
            Bundle(faultyDir, id: 2),
            [Bundle(baselineDir, isBaseline: true, id: 1)]);

        Assert.Equal("E-4021 thermostat fault", Assert.Single(report.NewErrorLines));
        Assert.Contains(report.Findings, f => f.Area == "Error log");
    }

    [Fact]
    public void TreatsTheSameErrorAtADifferentTimeAsAlreadyKnown()
    {
        using var workspace = new Workspace();
        var baselineDir = WriteBundleFiles(workspace.Root, "baseline", HealthyLog,
            errorLog: "2026-01-01 08:00:00 W-0001 routine warning");
        var faultyDir = WriteBundleFiles(workspace.Root, "faulty", HealthyLog,
            errorLog: "2026-09-14 11:22:33 W-0001 routine warning");

        var report = new DiagnosticAnalyser().Analyse(
            Bundle(faultyDir, id: 2),
            [Bundle(baselineDir, isBaseline: true, id: 1)]);

        Assert.Empty(report.NewErrorLines);
    }

    [Fact]
    public void CallsOutChangesMadeShortlyBeforeTheBundleArrived()
    {
        using var workspace = new Workspace();
        var faultyDir = WriteBundleFiles(workspace.Root, "faulty", HealthyLog,
            changeLog: "2026-09-10 Firmware updated to 2.4.1\n2024-01-05 Installed");

        var report = new DiagnosticAnalyser().Analyse(Bundle(faultyDir, id: 2), []);

        Assert.Contains("Firmware updated to 2.4.1", Assert.Single(report.RecentChanges));
        Assert.Contains(report.Findings, f => f.Area == "Recent change");
    }

    [Fact]
    public void SaysSoWhenTheMachineLogCannotBeRead()
    {
        using var workspace = new Workspace();
        var dir = WriteBundleFiles(workspace.Root, "odd", "this log is in a format we do not know");

        var report = new DiagnosticAnalyser().Analyse(Bundle(dir, id: 2), []);

        Assert.Contains(report.Findings, f => f.Area == "Machine log" && f.Detail.Contains("MachineLogPattern"));
    }

    [Fact]
    public void ReportsCleanlyWhenThereIsNothingWrong()
    {
        using var workspace = new Workspace();
        var baselineDir = WriteBundleFiles(workspace.Root, "baseline", HealthyLog, errorLog: "none");
        var goodDir = WriteBundleFiles(workspace.Root, "good", HealthyLog, errorLog: "none");

        var report = new DiagnosticAnalyser().Analyse(
            Bundle(goodDir, id: 2),
            [Bundle(baselineDir, isBaseline: true, id: 1)]);

        Assert.False(report.HasProblems);
        Assert.Equal("Nothing obviously wrong found.", report.Headline);
    }
}
