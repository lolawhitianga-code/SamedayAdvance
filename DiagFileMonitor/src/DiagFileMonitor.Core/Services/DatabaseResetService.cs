using DiagFileMonitor.Core.Data;
using Microsoft.EntityFrameworkCore;

namespace DiagFileMonitor.Core.Services;

public class ResetResult
{
    public int RecordsDeleted { get; set; }
    public int FoldersDeleted { get; set; }
    public int Failures { get; set; }

    public string Summary =>
        RecordsDeleted == 0
            ? "The database was already empty."
            : $"Cleared {RecordsDeleted} record(s) and removed {FoldersDeleted} extracted folder(s)."
              + (Failures > 0 ? $" {Failures} folder(s) could not be deleted." : string.Empty);
}

/// <summary>
/// Empties the database so a folder can be processed again from scratch. Deliberately separate
/// from retention cleanup: that keeps the history and drops the files, this drops everything.
/// </summary>
public class DatabaseResetService
{
    private readonly Func<DiagDbContext> _contextFactory;
    private readonly string _extractRootPath;

    public DatabaseResetService(Func<DiagDbContext> contextFactory, string extractRootPath)
    {
        _contextFactory = contextFactory;
        _extractRootPath = Path.GetFullPath(extractRootPath);
    }

    public async Task<ResetResult> ResetAsync(bool deleteExtractedFiles = true)
    {
        var result = new ResetResult();

        await using var context = _contextFactory();
        var bundles = await context.DiagnosticFiles.ToListAsync();
        result.RecordsDeleted = bundles.Count;

        if (deleteExtractedFiles)
        {
            foreach (var path in bundles.Select(b => b.ExtractedPath).Where(p => p is not null))
            {
                if (!IsInsideExtractRoot(path!) || !Directory.Exists(path)) continue;

                try
                {
                    Directory.Delete(path!, recursive: true);
                    result.FoldersDeleted++;
                }
                catch (Exception ex)
                {
                    result.Failures++;
                    SimpleLogger.Error($"Could not delete extracted folder '{path}'", ex);
                }
            }
        }

        // Log file rows go with their bundle through the cascade.
        context.DiagnosticFiles.RemoveRange(bundles);
        await context.SaveChangesAsync();

        return result;
    }

    /// <summary>Never deletes outside the configured extract folder, whatever the database says.</summary>
    private bool IsInsideExtractRoot(string path)
    {
        var full = Path.GetFullPath(path);
        var root = _extractRootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return full.Length > root.Length
            && full.StartsWith(root, StringComparison.OrdinalIgnoreCase)
            && (full[root.Length] == Path.DirectorySeparatorChar || full[root.Length] == Path.AltDirectorySeparatorChar);
    }
}
