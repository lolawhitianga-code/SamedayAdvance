using System.IO.Compression;
using System.Text.Json;
using DiagFileMonitor.Core.Models;

namespace DiagFileMonitor.Core.Services;

public class FeedbackPackage
{
    public bool Created { get; init; }
    public string ZipPath { get; init; } = string.Empty;
    public long SizeBytes { get; init; }
    public int FilesIncluded { get; init; }

    /// <summary>Anything worth telling the user, such as files left out for being too large.</summary>
    public IReadOnlyList<string> Notes { get; init; } = Array.Empty<string>();

    public string? Problem { get; init; }

    public string Summary => Created
        ? $"Feedback package written to {ZipPath} ({SizeBytes / 1024:N0} KB, {FilesIncluded} file(s))."
        : $"Could not create the feedback package: {Problem}";
}

/// <summary>
/// Packages a real case up for a Claude session: the diagnostic files, the report the app
/// produced, and the support person's account of what was really wrong.
/// <para>
/// The point is that the package stands on its own. Whoever opens it was probably not part of
/// the conversation that produced the report, so it carries the machine's identity, the version
/// that produced the output, and instructions for what to do with it.
/// </para>
/// </summary>
public class FeedbackPackageService
{
    private readonly DiagFileRepository _repository;
    private readonly DiagnosticAnalysisService _analysisService;

    /// <summary>
    /// A single file larger than this is left out and noted. A MachineLog.txt of a few megabytes
    /// is the whole point of the package and compresses to very little, so this is deliberately
    /// generous - it exists to stop a stray video or database file going along for the ride.
    /// </summary>
    public long MaximumFileBytes { get; set; } = 25 * 1024 * 1024;

    public FeedbackPackageService(DiagFileRepository repository, DiagnosticAnalysisService analysisService)
    {
        _repository = repository;
        _analysisService = analysisService;
    }

    public async Task<FeedbackPackage> CreateAsync(
        int diagnosticFileId,
        AnalysisFeedback feedback,
        string outputFolder,
        CancellationToken token = default)
    {
        if (!feedback.IsUsable)
        {
            return new FeedbackPackage
            {
                Problem = "there is nothing to learn from without either the real fault or how you knew"
            };
        }

        var bundles = await _repository.GetByIdsAsync(new[] { diagnosticFileId });
        var bundle = bundles.FirstOrDefault();

        if (bundle is null)
        {
            return new FeedbackPackage { Problem = "that bundle is no longer in the database" };
        }

        var summary = DiagnosticFileSummary.FromEntity(bundle);
        var reportText = await _analysisService.AnalyseAsync(new[] { diagnosticFileId }, token);

        var notes = new List<string>();
        var zipPath = Path.Combine(outputFolder, FileName(summary, feedback));

        try
        {
            Directory.CreateDirectory(outputFolder);

            var included = 0;
            using (var stream = new FileStream(zipPath, FileMode.Create, FileAccess.Write))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                Write(zip, "PROMPT.md", FeedbackPromptFormatter.Prompt(summary, feedback, reportText));
                Write(zip, "feedback.md", FeedbackPromptFormatter.Notes(summary, feedback));
                Write(zip, "report-produced.txt", reportText);
                Write(zip, "context.json", Context(summary, feedback));
                included += 4;

                included += AddBundle(zip, bundle, notes);

                // Written last so it can report on what actually went in.
                Write(zip, "README.md", FeedbackPromptFormatter.Readme(summary, feedback, notes));
                included++;
            }

            return new FeedbackPackage
            {
                Created = true,
                ZipPath = zipPath,
                SizeBytes = new FileInfo(zipPath).Length,
                FilesIncluded = included,
                Notes = notes
            };
        }
        catch (Exception ex)
        {
            SimpleLogger.Error($"Could not write the feedback package to '{zipPath}'", ex);
            return new FeedbackPackage { Problem = ex.Message };
        }
    }

    /// <summary>
    /// The unpacked diagnostic files, which are what the analysis actually read. The original
    /// .szip is not used even when it is still on disk, because a bundle can be re-processed and
    /// the extracted copy is what produced the report being complained about.
    /// </summary>
    private int AddBundle(ZipArchive zip, DiagnosticFile bundle, List<string> notes)
    {
        if (bundle.ExtractedPath is null || !Directory.Exists(bundle.ExtractedPath))
        {
            notes.Add("The unpacked files for this bundle are no longer on disk, so only the report "
                      + "and the notes are included. Re-process the original .szip to include them.");
            return 0;
        }

        var added = 0;

        foreach (var path in Directory.EnumerateFiles(bundle.ExtractedPath, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(bundle.ExtractedPath, path).Replace('\\', '/');

            try
            {
                var size = new FileInfo(path).Length;
                if (size > MaximumFileBytes)
                {
                    notes.Add($"`{relative}` left out - {size / 1024 / 1024} MB, over the size limit.");
                    continue;
                }

                zip.CreateEntryFromFile(path, $"bundle/{relative}");
                added++;
            }
            catch (Exception ex)
            {
                notes.Add($"`{relative}` could not be read: {ex.Message}");
            }
        }

        return added;
    }

    private static string Context(DiagnosticFileSummary file, AnalysisFeedback feedback) =>
        JsonSerializer.Serialize(new
        {
            bundle = new
            {
                file.OriginalFileName,
                file.ArrivedAtUtc,
                file.Status
            },
            machine = new
            {
                model = file.MachineType,
                name = file.MachineName,
                serial = file.SerialNumber,
                file.Customer,
                site = file.SiteLocation,
                software = file.SoftwareName,
                file.Version
            },
            operatorReported = new
            {
                panel = file.SupportPanel,
                members = file.SupportMembers,
                issue = file.SupportIssue
            },
            feedback = new
            {
                verdict = feedback.Verdict.ToString(),
                verdictText = feedback.VerdictText,
                feedback.WhatWasActuallyWrong,
                feedback.HowYouKnew,
                feedback.WhatShouldChange,
                feedback.RaisedBy,
                feedback.RaisedUtc
            },
            producedBy = new
            {
                app = "Diagnostic File Monitor",
                version = typeof(FeedbackPackageService).Assembly.GetName().Version?.ToString() ?? "unknown"
            }
        }, new JsonSerializerOptions { WriteIndented = true });

    /// <summary>
    /// Named so a folder of these sorts by machine then date, and so the file says what it is
    /// without being opened.
    /// </summary>
    public static string FileName(DiagnosticFileSummary file, AnalysisFeedback feedback)
    {
        var serial = Safe(file.SerialNumber);
        var verdict = feedback.Verdict switch
        {
            FeedbackVerdict.GotItRight => "worked",
            FeedbackVerdict.PartlyRight => "partly",
            FeedbackVerdict.MissedIt => "missed",
            _ => "wrong-way"
        };

        return $"feedback-{serial}-{feedback.RaisedUtc.ToLocalTime():yyyy-MM-dd-HHmm}-{verdict}.zip";
    }

    private static string Safe(string value)
    {
        var cleaned = new string(value.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '-').ToArray());
        return cleaned.Trim('-').Length == 0 ? "unknown" : cleaned.Trim('-');
    }

    private static void Write(ZipArchive zip, string name, string content)
    {
        var entry = zip.CreateEntry(name);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(content);
    }
}
