using System.Collections.Concurrent;
using DiagFileMonitor.Core.Models;
using DiagFileMonitor.Core.Services;

namespace DiagFileMonitor.Core.Tests;

public class FolderMonitorServiceTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    /// <summary>Waits until the expected number of bundles have been processed, or gives up.</summary>
    private static async Task<List<DiagnosticFile>> WaitForBundles(
        FolderMonitorService monitor, int expected, Func<Task> trigger)
    {
        var processed = new ConcurrentBag<DiagnosticFile>();
        var done = new TaskCompletionSource();

        monitor.FileProcessed += (_, file) =>
        {
            processed.Add(file);
            if (processed.Count >= expected) done.TrySetResult();
        };

        await trigger();
        await Task.WhenAny(done.Task, Task.Delay(Timeout));

        return processed.ToList();
    }

    [Fact]
    public async Task ProcessesAFileDroppedIntoAWatchedFolder()
    {
        using var env = new TestEnvironment();
        using var monitor = new FolderMonitorService(env.Processor);

        var bundles = await WaitForBundles(monitor, 1, () =>
        {
            monitor.Start([env.IncomingPath], [".zip"]);
            env.CreateZip("dropped.zip", TestEnvironment.SampleBundle());
            return Task.CompletedTask;
        });

        var bundle = Assert.Single(bundles);
        Assert.Equal("dropped.zip", bundle.OriginalFileName);
        Assert.Equal("SN-00123", bundle.SerialNumber);
    }

    [Fact]
    public async Task PicksUpFilesThatWereAlreadyThereBeforeStarting()
    {
        using var env = new TestEnvironment();
        using var monitor = new FolderMonitorService(env.Processor);
        env.CreateZip("waiting.zip", TestEnvironment.SampleBundle());

        var bundles = await WaitForBundles(monitor, 1, () =>
        {
            monitor.Start([env.IncomingPath], [".zip"]);
            return Task.CompletedTask;
        });

        Assert.Equal("waiting.zip", Assert.Single(bundles).OriginalFileName);
    }

    [Fact]
    public async Task WatchesSeveralFoldersAtOnce()
    {
        using var env = new TestEnvironment();
        using var monitor = new FolderMonitorService(env.Processor);

        var secondFolder = Path.Combine(env.RootPath, "SftpDrop");
        Directory.CreateDirectory(secondFolder);

        var bundles = await WaitForBundles(monitor, 2, async () =>
        {
            monitor.Start([env.IncomingPath, secondFolder], [".zip"]);

            env.CreateZip("from-email.zip", TestEnvironment.SampleBundle(serial: "SN-EMAIL"));

            var staged = env.CreateZip("from-sftp.zip", TestEnvironment.SampleBundle(serial: "SN-SFTP"));
            File.Move(staged, Path.Combine(secondFolder, "from-sftp.zip"));

            await Task.CompletedTask;
        });

        Assert.Equal(2, bundles.Count);
        Assert.Contains(bundles, b => b.SerialNumber == "SN-EMAIL");
        Assert.Contains(bundles, b => b.SerialNumber == "SN-SFTP");
    }

    [Fact]
    public async Task IgnoresFilesOutsideTheExtensionFilter()
    {
        using var env = new TestEnvironment();
        using var monitor = new FolderMonitorService(env.Processor);

        var bundles = await WaitForBundles(monitor, 1, async () =>
        {
            monitor.Start([env.IncomingPath], [".zip"]);

            await File.WriteAllTextAsync(Path.Combine(env.IncomingPath, "notes.txt"), "not a bundle");
            await File.WriteAllTextAsync(Path.Combine(env.IncomingPath, "archive.7z"), "not a bundle either");
            env.CreateZip("real.zip", TestEnvironment.SampleBundle());
        });

        Assert.Equal("real.zip", Assert.Single(bundles).OriginalFileName);
    }

    [Fact]
    public async Task HonoursACustomExtensionFilter()
    {
        using var env = new TestEnvironment();
        using var monitor = new FolderMonitorService(env.Processor);

        var bundles = await WaitForBundles(monitor, 1, async () =>
        {
            // Extensions given without a leading dot must still work.
            monitor.Start([env.IncomingPath], ["diag"]);

            var staged = env.CreateZip("bundle.zip", TestEnvironment.SampleBundle());
            File.Move(staged, Path.Combine(env.IncomingPath, "bundle.diag"));
            await Task.CompletedTask;
        });

        Assert.Equal("bundle.diag", Assert.Single(bundles).OriginalFileName);
    }

    [Fact]
    public void StopLeavesTheServiceIdle()
    {
        using var env = new TestEnvironment();
        using var monitor = new FolderMonitorService(env.Processor);

        monitor.Start([env.IncomingPath], [".zip"]);
        Assert.True(monitor.IsRunning);

        monitor.Stop();
        Assert.False(monitor.IsRunning);
    }

    [Fact]
    public void RestartingSwapsTheWatchedFolders()
    {
        using var env = new TestEnvironment();
        using var monitor = new FolderMonitorService(env.Processor);

        var other = Path.Combine(env.RootPath, "Other");
        monitor.Start([env.IncomingPath], [".zip"]);
        monitor.Start([other], [".zip"]);

        Assert.Equal([other], monitor.WatchFolders);
    }

    [Fact]
    public void DuplicateFoldersAreCollapsed()
    {
        using var env = new TestEnvironment();
        using var monitor = new FolderMonitorService(env.Processor);

        monitor.Start([env.IncomingPath, env.IncomingPath, "  "], [".zip"]);

        Assert.Single(monitor.WatchFolders);
    }
}
