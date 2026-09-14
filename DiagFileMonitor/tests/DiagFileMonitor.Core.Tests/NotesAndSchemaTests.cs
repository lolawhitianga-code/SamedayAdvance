using DiagFileMonitor.Core.Data;
using DiagFileMonitor.Core.Models;
using DiagFileMonitor.Core.Services;
using Microsoft.EntityFrameworkCore;

namespace DiagFileMonitor.Core.Tests;

public class NotesAndSchemaTests
{
    [Fact]
    public async Task SavesTicketNumberAndNotes()
    {
        using var env = new TestEnvironment();
        var file = await env.Processor.ProcessAsync(env.CreateZip("a.zip", TestEnvironment.SampleBundle()));

        var saved = await env.Repository.UpdateNotesAsync(file.Id, "TICK-42", "Customer reports slow preheat.");

        Assert.True(saved);
        var reloaded = (await env.Repository.GetAllAsync()).Single();
        Assert.Equal("TICK-42", reloaded.TicketNumber);
        Assert.Equal("Customer reports slow preheat.", reloaded.Notes);
    }

    [Fact]
    public async Task BlankNotesAreStoredAsNullRatherThanWhitespace()
    {
        using var env = new TestEnvironment();
        var file = await env.Processor.ProcessAsync(env.CreateZip("a.zip", TestEnvironment.SampleBundle()));

        await env.Repository.UpdateNotesAsync(file.Id, "   ", "  ");

        var reloaded = (await env.Repository.GetAllAsync()).Single();
        Assert.Null(reloaded.TicketNumber);
        Assert.Null(reloaded.Notes);
    }

    [Fact]
    public async Task ReportsWhenTheRowIsGone()
    {
        using var env = new TestEnvironment();

        Assert.False(await env.Repository.UpdateNotesAsync(9999, "TICK-1", "notes"));
    }

    [Fact]
    public async Task NotesAndTicketAreSearchable()
    {
        using var env = new TestEnvironment();
        var file = await env.Processor.ProcessAsync(env.CreateZip("a.zip", TestEnvironment.SampleBundle()));
        await env.Repository.UpdateNotesAsync(file.Id, "TICK-42", "thermostat replaced");

        var summary = DiagnosticFileSummary.FromEntity((await env.Repository.GetAllAsync()).Single());

        Assert.True(DiagnosticFileFilter.Matches(summary, new FilterCriteria { SearchText = "TICK-42" }));
        Assert.True(DiagnosticFileFilter.Matches(summary, new FilterCriteria { SearchText = "thermostat" }));
    }

    [Fact]
    public void AddsMissingColumnsToAnOlderDatabase()
    {
        var dir = Path.Combine(Path.GetTempPath(), "diagschema", Guid.NewGuid().ToString("N"));
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

            // Stand up a database shaped like an older build: no Notes/TicketNumber columns.
            using (var context = Context())
            {
                context.Database.EnsureCreated();
                context.Database.ExecuteSqlRaw("ALTER TABLE \"DiagnosticFiles\" DROP COLUMN \"Notes\";");
                context.Database.ExecuteSqlRaw("ALTER TABLE \"DiagnosticFiles\" DROP COLUMN \"TicketNumber\";");
            }

            using (var context = Context())
            {
                DatabaseInitializer.Initialize(context);
            }

            // Upgraded database must accept writes to the new columns.
            using (var context = Context())
            {
                context.DiagnosticFiles.Add(new DiagnosticFile
                {
                    OriginalFileName = "x.zip",
                    SerialNumber = "SN-1",
                    Status = ProcessingStatus.Processed,
                    TicketNumber = "TICK-9",
                    Notes = "after upgrade"
                });
                context.SaveChanges();
            }

            using (var context = Context())
            {
                var stored = context.DiagnosticFiles.Single();
                Assert.Equal("TICK-9", stored.TicketNumber);
                Assert.Equal("after upgrade", stored.Notes);
            }
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void InitializeIsSafeToRunTwice()
    {
        using var env = new TestEnvironment();

        using var context = env.CreateContext();
        DatabaseInitializer.Initialize(context);
        DatabaseInitializer.Initialize(context);
    }
}
