using DiagFileMonitor.Core.Knowledge;
using DiagFileMonitor.Core.Models;
using DiagFileMonitor.Core.SpidaLogs;

namespace DiagFileMonitor.Core.Services;

/// <summary>
/// Runs the Spida log reading method over stored bundles, on demand from the dashboard.
/// Reads the three log files back off disk, so a bundle whose extracted files have been
/// cleaned up reports that plainly rather than producing an empty analysis.
/// </summary>
public class DiagnosticAnalysisService
{
    private readonly DiagFileRepository _repository;
    private readonly SpidaLogAnalyser _analyser;

    public DiagnosticAnalysisService(DiagFileRepository repository, SpidaLogAnalyserOptions? options = null)
    {
        _repository = repository;
        _analyser = new SpidaLogAnalyser(options);
    }

    /// <summary>
    /// Virtual so a test can stand in a failing analysis. Burst alerting depends on this
    /// succeeding, and the behaviour when it does not is worth pinning down.
    /// </summary>
    public virtual async Task<string> AnalyseAsync(IEnumerable<int> diagnosticFileIds, CancellationToken token = default)
    {
        var bundles = await _repository.GetByIdsAsync(diagnosticFileIds);

        if (bundles.Count == 0) return "Nothing to analyse.";

        return await Task.Run(() =>
        {
            var reports = bundles.Select(AnalyseOne).ToList();

            if (reports.Count == 1) return reports[0];

            var header = $"Analysed {reports.Count} diagnostic files.\n\n";
            return header + string.Join("\n\n" + new string('=', 78) + "\n\n", reports);
        }, token);
    }

    private string AnalyseOne(DiagnosticFile bundle)
    {
        var summary = DiagnosticFileSummary.FromEntity(bundle);

        if (bundle.ExtractedPath is null || !Directory.Exists(bundle.ExtractedPath))
        {
            return $"DIAGNOSTIC ANALYSIS - {bundle.OriginalFileName}\n"
                   + new string('-', 78) + "\n"
                   + "The unpacked files for this bundle are no longer on disk, so the logs cannot be\n"
                   + "read. Clear the database and re-process the original .szip to analyse it.\n";
        }

        var machineLog = MachineLogFile.ParseFile(PathOf(bundle, LogFileKind.MachineLog));
        var errLog = ErrLogFile.ParseFile(PathOf(bundle, LogFileKind.ErrorLog));
        var changeLog = ChangeLogFile.ParseFile(PathOf(bundle, LogFileKind.ChangeLog));

        var analysis = _analyser.Analyse(machineLog, errLog, changeLog, bundle.ArrivedAtUtc);

        // Machine.xml is the better source for the model; fall back to what the PLC reported.
        var knowledge = KnowledgeAnnotator.Annotate(
            analysis, machineLog, bundle.MachineType, bundle.SerialNumber, MachineConfigPath(bundle));

        // What the operator wrote in SupportInfo.txt decides where the report points first.
        var complaint = ComplaintRouter.Route(bundle.SupportIssue, changeLog, machineLog, bundle.MachineType);

        return SpidaReportFormatter.Format(summary, analysis, knowledge, complaint);
    }

    private static string PathOf(DiagnosticFile bundle, LogFileKind kind) =>
        bundle.LogFiles.FirstOrDefault(l => l.Kind == kind)?.FullPath ?? string.Empty;

    /// <summary>
    /// The machine's own configuration file, named after the model - RakingWallExtruderV3DG.xml,
    /// TornadoM450.xml and so on - rather than the generic Machine.xml. It says which electronics
    /// each axis runs on. A bundle can carry several, including an empty one beside a .xmlTmp, so
    /// the model's own name is matched first and the largest readable file wins.
    /// </summary>
    private static string MachineConfigPath(DiagnosticFile bundle)
    {
        if (string.IsNullOrWhiteSpace(bundle.MachineType)) return string.Empty;

        return bundle.LogFiles
            .Where(l => l.SizeBytes > 0)
            .Where(l => l.FileName.StartsWith(bundle.MachineType, StringComparison.OrdinalIgnoreCase)
                        && l.FileName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(l => l.SizeBytes)
            .FirstOrDefault()?.FullPath ?? string.Empty;
    }
}
