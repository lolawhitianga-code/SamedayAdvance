using DiagFileMonitor.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace DiagFileMonitor.Core.Data;

public class DiagDbContext : DbContext
{
    public DbSet<DiagnosticFile> DiagnosticFiles => Set<DiagnosticFile>();
    public DbSet<ExtractedLogFile> ExtractedLogFiles => Set<ExtractedLogFile>();

    public DiagDbContext(DbContextOptions<DiagDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var diagnosticFile = modelBuilder.Entity<DiagnosticFile>();
        diagnosticFile.HasKey(d => d.Id);
        diagnosticFile.Property(d => d.Status).HasConversion<string>();
        diagnosticFile.HasIndex(d => d.SerialNumber);
        diagnosticFile.HasIndex(d => d.MachineType);
        diagnosticFile.HasIndex(d => d.Customer);
        diagnosticFile.HasIndex(d => d.ArrivedAtUtc);
        diagnosticFile.HasMany(d => d.LogFiles)
            .WithOne(l => l.DiagnosticFile)
            .HasForeignKey(l => l.DiagnosticFileId)
            .OnDelete(DeleteBehavior.Cascade);

        var logFile = modelBuilder.Entity<ExtractedLogFile>();
        logFile.HasKey(l => l.Id);
        logFile.Property(l => l.Kind).HasConversion<string>();
    }
}
