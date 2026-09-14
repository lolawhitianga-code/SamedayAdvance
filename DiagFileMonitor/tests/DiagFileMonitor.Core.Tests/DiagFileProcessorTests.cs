using DiagFileMonitor.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace DiagFileMonitor.Core.Tests;

public class DiagFileProcessorTests
{
    [Fact]
    public async Task ExtractsAndStoresMachineDetails()
    {
        using var env = new TestEnvironment();
        var zipPath = env.CreateZip("diag1.zip", TestEnvironment.SampleBundle());

        var result = await env.Processor.ProcessAsync(zipPath);

        Assert.Equal(ProcessingStatus.Processed, result.Status);
        Assert.Equal("SN-00123", result.SerialNumber);
        Assert.Equal("CoffeeRoaster-5000", result.MachineType);
        Assert.Equal("Wellington Roasters Ltd", result.Customer);
        Assert.Equal("2.4.1", result.Version);
        Assert.Equal("diag1.zip", result.OriginalFileName);
        Assert.NotNull(result.ProcessedAtUtc);
        Assert.True(Directory.Exists(result.ExtractedPath));
    }

    [Fact]
    public async Task PersistsToDatabase()
    {
        using var env = new TestEnvironment();
        var zipPath = env.CreateZip("diag1.zip", TestEnvironment.SampleBundle());

        await env.Processor.ProcessAsync(zipPath);

        var stored = await env.Repository.GetAllAsync();
        var only = Assert.Single(stored);
        Assert.Equal("SN-00123", only.SerialNumber);
        Assert.Equal(4, only.LogFiles.Count);
    }

    [Fact]
    public async Task TagsKnownLogFileKinds()
    {
        using var env = new TestEnvironment();
        var zipPath = env.CreateZip("diag1.zip", TestEnvironment.SampleBundle());

        var result = await env.Processor.ProcessAsync(zipPath);

        Assert.Equal(LogFileKind.ChangeLog, result.LogFiles.Single(f => f.FileName == "changelog.txt").Kind);
        Assert.Equal(LogFileKind.MachineLog, result.LogFiles.Single(f => f.FileName == "machinelog.txt").Kind);
        Assert.Equal(LogFileKind.ErrorLog, result.LogFiles.Single(f => f.FileName == "errorlog.txt").Kind);
        Assert.Equal(LogFileKind.Other, result.LogFiles.Single(f => f.FileName == "machine.xml").Kind);
    }

    [Fact]
    public async Task FindsMachineXmlInSubfolder()
    {
        using var env = new TestEnvironment();
        var files = new Dictionary<string, string>
        {
            [Path.Combine("data", "machine.xml")] = "<Machine><SerialNumber>SN-NESTED</SerialNumber></Machine>",
            ["errorlog.txt"] = "none"
        };
        var zipPath = env.CreateZip("nested.zip", files);

        var result = await env.Processor.ProcessAsync(zipPath);

        Assert.Equal(ProcessingStatus.Processed, result.Status);
        Assert.Equal("SN-NESTED", result.SerialNumber);
    }

    [Fact]
    public async Task FlagsBundleWithNoMachineXml()
    {
        using var env = new TestEnvironment();
        var zipPath = env.CreateZip("nomachine.zip", new Dictionary<string, string> { ["errorlog.txt"] = "boom" });

        var result = await env.Processor.ProcessAsync(zipPath);

        Assert.Equal(ProcessingStatus.Error, result.Status);
        Assert.NotNull(result.ErrorMessage);
        Assert.Contains("serial number", result.ErrorMessage);
    }

    [Fact]
    public async Task FlagsCorruptZipWithoutThrowing()
    {
        using var env = new TestEnvironment();
        var zipPath = Path.Combine(env.IncomingPath, "corrupt.zip");
        await File.WriteAllTextAsync(zipPath, "this is definitely not a zip file");

        var result = await env.Processor.ProcessAsync(zipPath);

        Assert.Equal(ProcessingStatus.Error, result.Status);
        Assert.NotNull(result.ErrorMessage);

        // A failed bundle must still be recorded, so support can see it arrived and failed.
        var stored = await env.Repository.GetAllAsync();
        Assert.Single(stored);
    }

    [Fact]
    public async Task GivesEachBundleItsOwnExtractFolder()
    {
        using var env = new TestEnvironment();

        // Same machine sending the same filename twice must not clobber the earlier extract.
        var zipPath = env.CreateZip("a.zip", TestEnvironment.SampleBundle());
        var first = await env.Processor.ProcessAsync(zipPath);
        var second = await env.Processor.ProcessAsync(zipPath);

        Assert.NotEqual(first.ExtractedPath, second.ExtractedPath);
        Assert.True(Directory.Exists(first.ExtractedPath));
        Assert.True(Directory.Exists(second.ExtractedPath));
    }
}
