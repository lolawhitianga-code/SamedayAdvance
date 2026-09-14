using System.IO.Compression;
using DiagFileMonitor.Core.Data;
using DiagFileMonitor.Core.Services;
using Microsoft.EntityFrameworkCore;

namespace DiagFileMonitor.Core.Tests;

/// <summary>A throwaway temp folder with a SQLite database, wired up the same way the app wires itself.</summary>
public sealed class TestEnvironment : IDisposable
{
    public string RootPath { get; }
    public string IncomingPath { get; }
    public string ExtractPath { get; }
    public DiagFileRepository Repository { get; }
    public DiagFileProcessor Processor { get; }

    private readonly string _databasePath;

    public TestEnvironment()
    {
        RootPath = Path.Combine(Path.GetTempPath(), "diagtests", Guid.NewGuid().ToString("N"));
        IncomingPath = Path.Combine(RootPath, "Incoming");
        ExtractPath = Path.Combine(RootPath, "Extracted");
        Directory.CreateDirectory(IncomingPath);
        Directory.CreateDirectory(ExtractPath);

        _databasePath = Path.Combine(RootPath, "test.db");
        using (var context = CreateContext())
        {
            DatabaseInitializer.Initialize(context);
        }

        Repository = new DiagFileRepository(CreateContext);
        Processor = new DiagFileProcessor(ExtractPath, Repository);
    }

    public DiagDbContext CreateContext()
    {
        var builder = new DbContextOptionsBuilder<DiagDbContext>();
        builder.UseSqlite($"Data Source={_databasePath}");
        return new DiagDbContext(builder.Options);
    }

    /// <summary>Writes the given files into a folder and zips it into the incoming folder.</summary>
    public string CreateZip(string zipName, Dictionary<string, string> files)
    {
        var stagingPath = Path.Combine(RootPath, "staging", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stagingPath);

        foreach (var (relativePath, content) in files)
        {
            var fullPath = Path.Combine(stagingPath, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, content);
        }

        var zipPath = Path.Combine(IncomingPath, zipName);
        ZipFile.CreateFromDirectory(stagingPath, zipPath);
        return zipPath;
    }

    public static Dictionary<string, string> SampleBundle(
        string serial = "SN-00123",
        string machineType = "CoffeeRoaster-5000",
        string customer = "Wellington Roasters Ltd",
        string version = "2.4.1") => new()
    {
        ["machine.xml"] = $"""
            <?xml version="1.0" encoding="utf-8"?>
            <Machine>
              <MachineType>{machineType}</MachineType>
              <SerialNumber>{serial}</SerialNumber>
              <Customer>{customer}</Customer>
              <Version>{version}</Version>
            </Machine>
            """,
        ["changelog.txt"] = "2026-08-15 Firmware updated to 2.4.1\n",
        ["machinelog.txt"] = "2026-09-01 08:00:00.000 START Preheat\n2026-09-01 08:00:45.250 END Preheat\n",
        ["errorlog.txt"] = "No errors recorded.\n"
    };

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(RootPath)) Directory.Delete(RootPath, recursive: true);
        }
        catch
        {
            // Temp cleanup is best-effort.
        }
    }
}
