using DiagFileMonitor.Core.Models;
using DiagFileMonitor.Core.Services;
using Microsoft.EntityFrameworkCore;

namespace DiagFileMonitor.Core.Tests;

public class ExtractCleanupServiceTests
{
    private static readonly DateTime Now = new(2026, 9, 14, 10, 0, 0, DateTimeKind.Utc);

    private static ExtractCleanupService ServiceFor(TestEnvironment env) =>
        new(env.CreateContext, env.ExtractPath);

    private static async Task<DiagnosticFile> AgeBundle(TestEnvironment env, DiagnosticFile file, int daysOld)
    {
        await using var context = env.CreateContext();
        var stored = context.DiagnosticFiles.Single(f => f.Id == file.Id);
        stored.ArrivedAtUtc = Now.AddDays(-daysOld);
        await context.SaveChangesAsync();
        return stored;
    }

    [Fact]
    public async Task DoesNothingWhenRetentionIsZero()
    {
        using var env = new TestEnvironment();
        var file = await env.Processor.ProcessAsync(env.CreateZip("a.zip", TestEnvironment.SampleBundle()));
        await AgeBundle(env, file, 500);

        var result = await ServiceFor(env).CleanupAsync(0, Now);

        Assert.True(result.WasDisabled);
        Assert.Equal(0, result.FoldersDeleted);
        Assert.True(Directory.Exists(file.ExtractedPath));
    }

    [Fact]
    public async Task DeletesExtractsOlderThanRetention()
    {
        using var env = new TestEnvironment();
        var file = await env.Processor.ProcessAsync(env.CreateZip("a.zip", TestEnvironment.SampleBundle()));
        await AgeBundle(env, file, 100);

        var result = await ServiceFor(env).CleanupAsync(30, Now);

        Assert.Equal(1, result.FoldersDeleted);
        Assert.False(Directory.Exists(file.ExtractedPath));
    }

    [Fact]
    public async Task KeepsRecentExtracts()
    {
        using var env = new TestEnvironment();
        var file = await env.Processor.ProcessAsync(env.CreateZip("a.zip", TestEnvironment.SampleBundle()));
        await AgeBundle(env, file, 5);

        var result = await ServiceFor(env).CleanupAsync(30, Now);

        Assert.Equal(0, result.FoldersDeleted);
        Assert.True(Directory.Exists(file.ExtractedPath));
    }

    [Fact]
    public async Task NeverDeletesBaselines()
    {
        using var env = new TestEnvironment();
        var file = await env.Processor.ProcessAsync(env.CreateZip("a.zip", TestEnvironment.SampleBundle()));
        await AgeBundle(env, file, 500);
        await env.Repository.SetBaselineAsync(file.Id, true);

        var result = await ServiceFor(env).CleanupAsync(30, Now);

        Assert.Equal(0, result.FoldersDeleted);
        Assert.True(Directory.Exists(file.ExtractedPath));
    }

    [Fact]
    public async Task KeepsTheHistoryRowAfterDeletingTheFiles()
    {
        using var env = new TestEnvironment();
        var file = await env.Processor.ProcessAsync(env.CreateZip("a.zip", TestEnvironment.SampleBundle()));
        await env.Repository.UpdateNotesAsync(file.Id, "TICK-9", "replaced thermostat");
        await AgeBundle(env, file, 100);

        await ServiceFor(env).CleanupAsync(30, Now);

        var stored = Assert.Single(await env.Repository.GetAllAsync());
        Assert.Equal("SN-00123", stored.SerialNumber);
        Assert.Equal("TICK-9", stored.TicketNumber);
        Assert.Equal("replaced thermostat", stored.Notes);
        Assert.Null(stored.ExtractedPath);
        Assert.Empty(stored.LogFiles);
    }

    [Fact]
    public async Task RefusesToDeleteAnythingOutsideTheExtractFolder()
    {
        using var env = new TestEnvironment();
        var outsideDir = Path.Combine(env.RootPath, "not-the-extract-folder");
        Directory.CreateDirectory(outsideDir);
        await File.WriteAllTextAsync(Path.Combine(outsideDir, "important.txt"), "do not delete me");

        var file = await env.Processor.ProcessAsync(env.CreateZip("a.zip", TestEnvironment.SampleBundle()));
        await using (var context = env.CreateContext())
        {
            var stored = context.DiagnosticFiles.Single(f => f.Id == file.Id);
            stored.ArrivedAtUtc = Now.AddDays(-100);
            stored.ExtractedPath = outsideDir;
            await context.SaveChangesAsync();
        }

        var result = await ServiceFor(env).CleanupAsync(30, Now);

        Assert.Equal(0, result.FoldersDeleted);
        Assert.True(Directory.Exists(outsideDir));
        Assert.True(File.Exists(Path.Combine(outsideDir, "important.txt")));
    }

    [Fact]
    public async Task SurvivesAnExtractFolderThatIsAlreadyGone()
    {
        using var env = new TestEnvironment();
        var file = await env.Processor.ProcessAsync(env.CreateZip("a.zip", TestEnvironment.SampleBundle()));
        await AgeBundle(env, file, 100);
        Directory.Delete(file.ExtractedPath!, recursive: true);

        var result = await ServiceFor(env).CleanupAsync(30, Now);

        Assert.Equal(0, result.Failures);
        Assert.Null((await env.Repository.GetAllAsync()).Single().ExtractedPath);
    }
}
