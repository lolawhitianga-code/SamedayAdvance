using DiagFileMonitor.Core.Data;
using DiagFileMonitor.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace DiagFileMonitor.Core.Services;

public class CleanupResult
{
    public int FoldersDeleted { get; set; }
    public long BytesFreed { get; set; }
    public int Failures { get; set; }
    public bool WasDisabled { get; set; }

    public string Summary => WasDisabled
        ? "Cleanup is switched off (retention set to 0 days)."
        : FoldersDeleted == 0
            ? "Nothing old enough to clean up."
            : $"Removed {FoldersDeleted} extracted folder(s), freeing {BytesFreed / 1024 / 1024} MB."
              + (Failures > 0 ? $" {Failures} could not be deleted." : string.Empty);
}

/// <summary>
/// Deletes the unpacked contents of bundles past their retention age. The database rows stay,
/// so the arrival history, notes and ticket references survive - only the files on disk go.
/// Baselines are always kept, since they are the reference set for comparisons.
/// </summary>
public class ExtractCleanupService
{
    private readonly Func<DiagDbContext> _contextFactory;
    private readonly string _extractRootPath;

    public ExtractCleanupService(Func<DiagDbContext> contextFactory, string extractRootPath)
    {
        _contextFactory = contextFactory;
        _extractRootPath = Path.GetFullPath(extractRootPath);
    }

    public async Task<CleanupResult> CleanupAsync(int retentionDays, DateTime nowUtc)
    {
        if (retentionDays <= 0)
        {
            return new CleanupResult { WasDisabled = true };
        }

        var result = new CleanupResult();
        var cutoff = nowUtc.AddDays(-retentionDays);

        await using var context = _contextFactory();
        var stale = await context.DiagnosticFiles
            .Include(f => f.LogFiles)
            .Where(f => !f.IsBaseline && f.ArrivedAtUtc < cutoff && f.ExtractedPath != null)
            .ToListAsync();

        foreach (var bundle in stale)
        {
            var path = bundle.ExtractedPath!;

            if (!IsInsideExtractRoot(path) || !Directory.Exists(path))
            {
                // Either already gone, or somewhere we have no business deleting from.
                bundle.ExtractedPath = null;
                bundle.LogFiles.Clear();
                continue;
            }

            try
            {
                result.BytesFreed += DirectorySize(path);
                Directory.Delete(path, recursive: true);
                result.FoldersDeleted++;

                bundle.ExtractedPath = null;
                bundle.LogFiles.Clear();
            }
            catch (Exception ex)
            {
                result.Failures++;
                SimpleLogger.Error($"Could not delete extracted folder '{path}'", ex);
            }
        }

        await context.SaveChangesAsync();
        return result;
    }

    /// <summary>Refuses to touch anything outside the configured extract folder.</summary>
    private bool IsInsideExtractRoot(string path)
    {
        var full = Path.GetFullPath(path);
        var root = _extractRootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return full.Length > root.Length
            && full.StartsWith(root, StringComparison.OrdinalIgnoreCase)
            && (full[root.Length] == Path.DirectorySeparatorChar || full[root.Length] == Path.AltDirectorySeparatorChar);
    }

    private static long DirectorySize(string path)
    {
        try
        {
            return Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
                .Sum(file => new FileInfo(file).Length);
        }
        catch (IOException)
        {
            return 0;
        }
    }
}
