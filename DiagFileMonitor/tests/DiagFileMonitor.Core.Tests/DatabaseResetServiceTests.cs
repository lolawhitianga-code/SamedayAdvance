using DiagFileMonitor.Core.Services;

namespace DiagFileMonitor.Core.Tests;

public class DatabaseResetServiceTests
{
    private static DatabaseResetService ServiceFor(TestEnvironment env) =>
        new(env.CreateContext, env.ExtractPath);

    [Fact]
    public async Task RemovesEveryRecordAndItsUnpackedFiles()
    {
        using var env = new TestEnvironment();
        var first = await env.Processor.ProcessAsync(env.CreateZip("a.zip", TestEnvironment.SampleBundle(serial: "SN-1")));
        var second = await env.Processor.ProcessAsync(env.CreateZip("b.zip", TestEnvironment.SampleBundle(serial: "SN-2")));

        var result = await ServiceFor(env).ResetAsync();

        Assert.Equal(2, result.RecordsDeleted);
        Assert.Equal(2, result.FoldersDeleted);
        Assert.Empty(await env.Repository.GetAllAsync());
        Assert.False(Directory.Exists(first.ExtractedPath));
        Assert.False(Directory.Exists(second.ExtractedPath));
    }

    [Fact]
    public async Task LeavesTheOriginalZipsAloneSoTheyCanBeProcessedAgain()
    {
        using var env = new TestEnvironment();
        var zipPath = env.CreateZip("a.zip", TestEnvironment.SampleBundle());
        await env.Processor.ProcessAsync(zipPath);

        await ServiceFor(env).ResetAsync();

        Assert.True(File.Exists(zipPath));

        // And the same file can go through again from scratch.
        var reprocessed = await env.Processor.ProcessAsync(zipPath);
        Assert.Equal("SN-00123", reprocessed.SerialNumber);
        Assert.Single(await env.Repository.GetAllAsync());
    }

    [Fact]
    public async Task CanKeepTheUnpackedFiles()
    {
        using var env = new TestEnvironment();
        var bundle = await env.Processor.ProcessAsync(env.CreateZip("a.zip", TestEnvironment.SampleBundle()));

        var result = await ServiceFor(env).ResetAsync(deleteExtractedFiles: false);

        Assert.Equal(1, result.RecordsDeleted);
        Assert.Equal(0, result.FoldersDeleted);
        Assert.True(Directory.Exists(bundle.ExtractedPath));
    }

    [Fact]
    public async Task RefusesToDeleteOutsideTheExtractFolder()
    {
        using var env = new TestEnvironment();
        var outsideDir = Path.Combine(env.RootPath, "somewhere-else");
        Directory.CreateDirectory(outsideDir);
        await File.WriteAllTextAsync(Path.Combine(outsideDir, "important.txt"), "keep me");

        var bundle = await env.Processor.ProcessAsync(env.CreateZip("a.zip", TestEnvironment.SampleBundle()));
        await using (var context = env.CreateContext())
        {
            context.DiagnosticFiles.Single(f => f.Id == bundle.Id).ExtractedPath = outsideDir;
            await context.SaveChangesAsync();
        }

        var result = await ServiceFor(env).ResetAsync();

        Assert.Equal(1, result.RecordsDeleted);
        Assert.Equal(0, result.FoldersDeleted);
        Assert.True(File.Exists(Path.Combine(outsideDir, "important.txt")));
    }

    [Fact]
    public async Task IsSafeOnAnEmptyDatabase()
    {
        using var env = new TestEnvironment();

        var result = await ServiceFor(env).ResetAsync();

        Assert.Equal(0, result.RecordsDeleted);
        Assert.Contains("already empty", result.Summary);
    }
}
