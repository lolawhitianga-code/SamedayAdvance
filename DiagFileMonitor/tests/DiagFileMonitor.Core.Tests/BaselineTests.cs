using DiagFileMonitor.Core.Data;
using DiagFileMonitor.Core.Models;
using DiagFileMonitor.Core.Services;
using Microsoft.EntityFrameworkCore;

namespace DiagFileMonitor.Core.Tests;

public class BaselineTests
{
    [Fact]
    public async Task MarksAndUnmarksABaseline()
    {
        using var env = new TestEnvironment();
        var file = await env.Processor.ProcessAsync(env.CreateZip("a.zip", TestEnvironment.SampleBundle()));

        Assert.True(await env.Repository.SetBaselineAsync(file.Id, true));
        Assert.True((await env.Repository.GetAllAsync()).Single().IsBaseline);

        Assert.True(await env.Repository.SetBaselineAsync(file.Id, false));
        Assert.False((await env.Repository.GetAllAsync()).Single().IsBaseline);
    }

    [Fact]
    public async Task BundlesAreNotBaselinesByDefault()
    {
        using var env = new TestEnvironment();
        await env.Processor.ProcessAsync(env.CreateZip("a.zip", TestEnvironment.SampleBundle()));

        Assert.False((await env.Repository.GetAllAsync()).Single().IsBaseline);
    }

    [Fact]
    public void BaselinesOnlyFilterNarrowsTheList()
    {
        var baseline = new DiagnosticFileSummary { SerialNumber = "SN-1", Status = "Processed", IsBaseline = true };
        var ordinary = new DiagnosticFileSummary { SerialNumber = "SN-2", Status = "Processed" };

        var criteria = new FilterCriteria { BaselinesOnly = true };

        Assert.True(DiagnosticFileFilter.Matches(baseline, criteria));
        Assert.False(DiagnosticFileFilter.Matches(ordinary, criteria));
        Assert.False(criteria.IsEmpty);
    }

    [Fact]
    public void BaselineLabelReflectsTheFlag()
    {
        var row = new DiagnosticFileSummary();

        Assert.Equal(string.Empty, row.BaselineLabel);
        row.IsBaseline = true;
        Assert.Equal("Baseline", row.BaselineLabel);
    }

    [Fact]
    public void NotNullBooleanColumnIsAddedToAnOlderDatabase()
    {
        var dir = Path.Combine(Path.GetTempPath(), "diagbaseline", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var dbPath = Path.Combine(dir, "old.db");

        try
        {
            DiagDbContext Context()
            {
                var builder = new DbContextOptionsBuilder<DiagDbContext>();
                builder.UseSqlite($"Data Source={dbPath}");
                return new DiagDbContext(builder.Options);
            }

            using (var context = Context())
            {
                context.Database.EnsureCreated();
                context.Database.ExecuteSqlRaw("ALTER TABLE \"DiagnosticFiles\" DROP COLUMN \"IsBaseline\";");

                // A row written before the upgrade must survive it.
                context.Database.ExecuteSqlRaw(
                    "INSERT INTO \"DiagnosticFiles\" (\"OriginalFileName\", \"SourcePath\", \"FileSizeBytes\", \"ArrivedAtUtc\", \"Status\") " +
                    "VALUES ('old.zip', 'C:\\old.zip', 10, '2026-09-01 00:00:00', 'Processed');");
            }

            using (var context = Context())
            {
                DatabaseInitializer.Initialize(context);
            }

            using (var context = Context())
            {
                var stored = context.DiagnosticFiles.Single();
                Assert.Equal("old.zip", stored.OriginalFileName);
                Assert.False(stored.IsBaseline);
            }
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
