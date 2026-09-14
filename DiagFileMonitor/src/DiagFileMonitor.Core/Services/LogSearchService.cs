using DiagFileMonitor.Core.Models;

namespace DiagFileMonitor.Core.Services;

public class LogSearchHit
{
    public int DiagnosticFileId { get; init; }
    public string SerialNumber { get; init; } = string.Empty;
    public string Customer { get; init; } = string.Empty;
    public string BundleName { get; init; } = string.Empty;
    public DateTime ArrivedAtUtc { get; init; }
    public string LogFileName { get; init; } = string.Empty;
    public string LogFilePath { get; init; } = string.Empty;
    public int LineNumber { get; init; }
    public string Line { get; init; } = string.Empty;

    public string ArrivedDisplay => ArrivedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
}

public class LogSearchResult
{
    public IReadOnlyList<LogSearchHit> Hits { get; init; } = Array.Empty<LogSearchHit>();
    public int BundlesSearched { get; init; }
    public int BundlesMatched { get; init; }
    public bool TruncatedAtLimit { get; init; }
    public int MissingFiles { get; init; }
}

/// <summary>
/// Greps the unpacked logs of every stored bundle. Lets support ask "have we seen this
/// error code before, on any machine?" rather than opening bundles one at a time.
/// </summary>
public class LogSearchService
{
    private static readonly string[] SearchableExtensions = { ".txt", ".log", ".xml", ".csv", ".ini", ".cfg" };

    public const int DefaultHitsPerBundle = 5;
    public const int DefaultTotalHitLimit = 300;

    public Task<LogSearchResult> SearchAsync(
        IEnumerable<DiagnosticFile> bundles,
        string term,
        int hitsPerBundle = DefaultHitsPerBundle,
        int totalHitLimit = DefaultTotalHitLimit,
        CancellationToken token = default)
        => Task.Run(() => Search(bundles, term, hitsPerBundle, totalHitLimit, token), token);

    private static LogSearchResult Search(
        IEnumerable<DiagnosticFile> bundles, string term, int hitsPerBundle, int totalHitLimit, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(term))
        {
            return new LogSearchResult();
        }

        var hits = new List<LogSearchHit>();
        var searched = 0;
        var matched = 0;
        var missing = 0;
        var truncated = false;

        foreach (var bundle in bundles)
        {
            token.ThrowIfCancellationRequested();
            searched++;
            var hitsForBundle = 0;

            foreach (var log in bundle.LogFiles)
            {
                if (hitsForBundle >= hitsPerBundle || truncated) break;
                if (!SearchableExtensions.Contains(Path.GetExtension(log.FileName), StringComparer.OrdinalIgnoreCase)) continue;

                if (!File.Exists(log.FullPath))
                {
                    missing++;
                    continue;
                }

                var lineNumber = 0;
                try
                {
                    foreach (var line in File.ReadLines(log.FullPath))
                    {
                        token.ThrowIfCancellationRequested();
                        lineNumber++;

                        if (!line.Contains(term, StringComparison.OrdinalIgnoreCase)) continue;

                        hits.Add(new LogSearchHit
                        {
                            DiagnosticFileId = bundle.Id,
                            SerialNumber = string.IsNullOrWhiteSpace(bundle.SerialNumber) ? DiagnosticFileSummary.Unknown : bundle.SerialNumber,
                            Customer = string.IsNullOrWhiteSpace(bundle.Customer) ? DiagnosticFileSummary.Unknown : bundle.Customer,
                            BundleName = bundle.OriginalFileName,
                            ArrivedAtUtc = bundle.ArrivedAtUtc,
                            LogFileName = log.FileName,
                            LogFilePath = log.FullPath,
                            LineNumber = lineNumber,
                            Line = line.Trim()
                        });

                        hitsForBundle++;
                        if (hits.Count >= totalHitLimit) { truncated = true; break; }
                        if (hitsForBundle >= hitsPerBundle) break;
                    }
                }
                catch (IOException ex)
                {
                    SimpleLogger.Error($"Could not read '{log.FullPath}' while searching", ex);
                }
            }

            if (hitsForBundle > 0) matched++;
            if (truncated) break;
        }

        return new LogSearchResult
        {
            Hits = hits,
            BundlesSearched = searched,
            BundlesMatched = matched,
            TruncatedAtLimit = truncated,
            MissingFiles = missing
        };
    }
}
