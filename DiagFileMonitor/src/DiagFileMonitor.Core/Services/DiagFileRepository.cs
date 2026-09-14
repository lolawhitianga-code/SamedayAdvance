using DiagFileMonitor.Core.Data;
using DiagFileMonitor.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace DiagFileMonitor.Core.Services;

/// <summary>
/// Talks to SQLite. Takes a context factory rather than a single DbContext instance so each
/// operation gets its own short-lived context - DbContext isn't thread-safe and the folder
/// watcher's background worker and the UI thread both need to read/write.
/// </summary>
public class DiagFileRepository
{
    private readonly Func<DiagDbContext> _contextFactory;

    public DiagFileRepository(Func<DiagDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<int> AddAsync(DiagnosticFile file)
    {
        await using var context = _contextFactory();
        context.DiagnosticFiles.Add(file);
        await context.SaveChangesAsync();
        return file.Id;
    }

    /// <summary>Saves support's case notes against a bundle. Returns false if the row is gone.</summary>
    public async Task<bool> UpdateNotesAsync(int id, string? ticketNumber, string? notes)
    {
        await using var context = _contextFactory();
        var file = await context.DiagnosticFiles.FirstOrDefaultAsync(f => f.Id == id);
        if (file is null) return false;

        file.TicketNumber = string.IsNullOrWhiteSpace(ticketNumber) ? null : ticketNumber.Trim();
        file.Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        await context.SaveChangesAsync();
        return true;
    }

    /// <summary>Marks (or unmarks) a bundle as a known-good reference for its machine type.</summary>
    public async Task<bool> SetBaselineAsync(int id, bool isBaseline)
    {
        await using var context = _contextFactory();
        var file = await context.DiagnosticFiles.FirstOrDefaultAsync(f => f.Id == id);
        if (file is null) return false;

        file.IsBaseline = isBaseline;
        await context.SaveChangesAsync();
        return true;
    }

    /// <summary>
    /// The ticket most recently raised for this machine, if it is still inside the reuse window.
    /// The local database is the record of which ticket belongs to which machine, so no custom
    /// field is needed in Zoho.
    /// </summary>
    public async Task<DiagnosticFile?> FindRecentTicketForSerialAsync(string serialNumber, DateTime cutoffUtc)
    {
        await using var context = _contextFactory();
        return await context.DiagnosticFiles
            .Where(f => f.SerialNumber == serialNumber
                        && f.ZohoTicketId != null
                        && f.ZohoTicketCreatedUtc != null
                        && f.ZohoTicketCreatedUtc >= cutoffUtc)
            .OrderByDescending(f => f.ZohoTicketCreatedUtc)
            .AsNoTracking()
            .FirstOrDefaultAsync();
    }

    public async Task<bool> SaveTicketLinkAsync(int id, string ticketId, string ticketNumber, DateTime createdUtc)
    {
        await using var context = _contextFactory();
        var file = await context.DiagnosticFiles.FirstOrDefaultAsync(f => f.Id == id);
        if (file is null) return false;

        file.ZohoTicketId = ticketId;
        file.ZohoTicketNumber = ticketNumber;
        file.ZohoTicketCreatedUtc = createdUtc;
        await context.SaveChangesAsync();
        return true;
    }

    public async Task MarkAlertSentAsync(int id, DateTime sentUtc)
    {
        await using var context = _contextFactory();
        var file = await context.DiagnosticFiles.FirstOrDefaultAsync(f => f.Id == id);
        if (file is null) return;

        file.AlertSentUtc = sentUtc;
        await context.SaveChangesAsync();
    }

    public async Task<List<DiagnosticFile>> GetBaselinesAsync()
    {
        await using var context = _contextFactory();
        return await context.DiagnosticFiles
            .Include(f => f.LogFiles)
            .Where(f => f.IsBaseline)
            .AsNoTracking()
            .ToListAsync();
    }

    public async Task<List<DiagnosticFile>> GetAllAsync()
    {
        await using var context = _contextFactory();
        return await context.DiagnosticFiles
            .Include(f => f.LogFiles)
            .OrderByDescending(f => f.ArrivedAtUtc)
            .AsNoTracking()
            .ToListAsync();
    }
}
