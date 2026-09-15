using System.IO.Compression;
using DiagFileMonitor.Core.Models;
using DiagFileMonitor.Core.Services;

namespace DiagFileMonitor.Core.Tests;

public class FeedbackPackageTests
{
    private static AnalysisFeedback Feedback(
        FeedbackVerdict verdict = FeedbackVerdict.MissedIt,
        string wrong = "The saw motor was commanded on but never actually turned.",
        string how = "IO-SawMotor went on and SawMotorConfirm never came on. The machine logged "
                     + "\"Waiting for Saw Blade Running\" then dropped the command 5.5s later.") => new()
    {
        Verdict = verdict,
        WhatWasActuallyWrong = wrong,
        HowYouKnew = how,
        WhatShouldChange = "Check every motor output against its confirmation input.",
        RaisedBy = "Lola",
        RaisedUtc = new DateTime(2026, 9, 15, 4, 30, 0, DateTimeKind.Utc)
    };

    private static async Task<(TestEnvironment Env, FeedbackPackageService Service, DiagnosticFile Bundle)> Arrange()
    {
        var env = new TestEnvironment();
        var bundle = await env.Processor.ProcessAsync(
            env.CreateZip("a.zip", TestEnvironment.SampleBundle(serial: "M20421")));

        return (env, new FeedbackPackageService(env.Repository, new DiagnosticAnalysisService(env.Repository)), bundle);
    }

    [Fact]
    public async Task ThePackageCarriesEverythingNeededToActOnItAlone()
    {
        var (env, service, bundle) = await Arrange();
        using var _ = env;

        var package = await service.CreateAsync(bundle.Id, Feedback(), env.RootPath);

        Assert.True(package.Created, package.Problem);

        using var zip = ZipFile.OpenRead(package.ZipPath);
        var names = zip.Entries.Select(e => e.FullName).ToList();

        Assert.Contains("README.md", names);
        Assert.Contains("PROMPT.md", names);
        Assert.Contains("feedback.md", names);
        Assert.Contains("report-produced.txt", names);
        Assert.Contains("context.json", names);

        // The diagnostic files the analysis actually read.
        Assert.Contains(names, n => n.StartsWith("bundle/") && n.EndsWith("machine.xml"));
        Assert.Contains(names, n => n.StartsWith("bundle/") && n.EndsWith("machinelog.txt"));
    }

    [Fact]
    public async Task ThePromptCarriesTheMachineTheGroundTruthAndHowTheyKnew()
    {
        var (env, service, bundle) = await Arrange();
        using var _ = env;

        var package = await service.CreateAsync(bundle.Id, Feedback(), env.RootPath);

        using var zip = ZipFile.OpenRead(package.ZipPath);
        using var reader = new StreamReader(zip.GetEntry("PROMPT.md")!.Open());
        var prompt = await reader.ReadToEndAsync();

        Assert.Contains("M20421", prompt);
        Assert.Contains("CoffeeRoaster-5000", prompt);
        Assert.Contains("never actually turned", prompt);
        Assert.Contains("SawMotorConfirm never came on", prompt);

        // It has to stand on its own for a session that was not part of this conversation.
        Assert.Contains("src/DiagFileMonitor.Core/Knowledge/", prompt);
        Assert.Contains("./tools/check.sh", prompt);
        Assert.Contains("confidence", prompt);
    }

    [Fact]
    public async Task AWorkingCaseAsksForARegressionTestRatherThanAChange()
    {
        var (env, service, bundle) = await Arrange();
        using var _ = env;

        var package = await service.CreateAsync(
            bundle.Id, Feedback(FeedbackVerdict.GotItRight, "It found the real fault.", "It named the right axis."),
            env.RootPath);

        using var zip = ZipFile.OpenRead(package.ZipPath);
        using var reader = new StreamReader(zip.GetEntry("PROMPT.md")!.Open());
        var prompt = await reader.ReadToEndAsync();

        Assert.Contains("This one worked", prompt);
        Assert.Contains("regression test", prompt);
        Assert.DoesNotContain("Check the claim against the files", prompt);
    }

    [Fact]
    public async Task TheReportInThePackageIsTheOneTheAppProduced()
    {
        var (env, service, bundle) = await Arrange();
        using var _ = env;

        var package = await service.CreateAsync(bundle.Id, Feedback(), env.RootPath);
        var expected = await new DiagnosticAnalysisService(env.Repository).AnalyseAsync(new[] { bundle.Id });

        using var zip = ZipFile.OpenRead(package.ZipPath);
        using var reader = new StreamReader(zip.GetEntry("report-produced.txt")!.Open());

        Assert.Equal(expected, await reader.ReadToEndAsync());
    }

    [Fact]
    public async Task NotesWithNothingToLearnFromAreRefused()
    {
        var (env, service, bundle) = await Arrange();
        using var _ = env;

        var package = await service.CreateAsync(
            bundle.Id,
            new AnalysisFeedback { Verdict = FeedbackVerdict.MissedIt, WhatShouldChange = "do better" },
            env.RootPath);

        Assert.False(package.Created);
        Assert.Contains("nothing to learn from", package.Problem);
    }

    [Fact]
    public async Task AnOversizedFileIsLeftOutAndSaidSo()
    {
        var (env, service, bundle) = await Arrange();
        using var _ = env;

        await File.WriteAllBytesAsync(
            Path.Combine(bundle.ExtractedPath!, "huge.bin"), new byte[2048]);
        service.MaximumFileBytes = 1024;

        var package = await service.CreateAsync(bundle.Id, Feedback(), env.RootPath);

        Assert.True(package.Created);
        Assert.Contains(package.Notes, n => n.Contains("huge.bin") && n.Contains("left out"));

        // And the reader of the package is told, not just the person who made it.
        using var zip = ZipFile.OpenRead(package.ZipPath);
        using var reader = new StreamReader(zip.GetEntry("README.md")!.Open());
        Assert.Contains("huge.bin", await reader.ReadToEndAsync());
    }

    [Fact]
    public async Task ABundleWhoseFilesHaveBeenCleanedUpStillPackagesTheReportAndNotes()
    {
        var (env, service, bundle) = await Arrange();
        using var _ = env;

        Directory.Delete(bundle.ExtractedPath!, recursive: true);

        var package = await service.CreateAsync(bundle.Id, Feedback(), env.RootPath);

        Assert.True(package.Created);
        Assert.Contains(package.Notes, n => n.Contains("no longer on disk"));
    }

    [Fact]
    public void TheFileNameSaysWhatItIsWithoutBeingOpened()
    {
        var file = new DiagnosticFileSummary { SerialNumber = "M20421" };

        Assert.Equal("feedback-M20421-2026-09-15-1630-missed.zip",
            FeedbackPackageService.FileName(file, new AnalysisFeedback
            {
                Verdict = FeedbackVerdict.MissedIt,
                RaisedUtc = new DateTime(2026, 9, 15, 16, 30, 0, DateTimeKind.Unspecified)
            }));
    }

    [Fact]
    public void ASerialWithAwkwardCharactersStillMakesAValidFileName()
    {
        var file = new DiagnosticFileSummary { SerialNumber = "M204/21 (spare)" };
        var name = FeedbackPackageService.FileName(file, new AnalysisFeedback());

        Assert.DoesNotContain('/', name);
        Assert.DoesNotContain('(', name);
    }
}
