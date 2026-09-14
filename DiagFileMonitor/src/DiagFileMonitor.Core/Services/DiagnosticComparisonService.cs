using DiagFileMonitor.Core.Models;
using DiagFileMonitor.Core.SpidaLogs;

namespace DiagFileMonitor.Core.Services;

/// <summary>
/// Measures one machine against a bundle marked as the known-good benchmark: how long it spends
/// in each step, whether it follows the same process, and which settings differ.
/// </summary>
public class DiagnosticComparisonService
{
    private readonly DiagFileRepository _repository;

    public DiagnosticComparisonService(DiagFileRepository repository) => _repository = repository;

    public async Task<string> CompareAsync(int masterId, int comparedId, CancellationToken token = default)
    {
        var bundles = await _repository.GetByIdsAsync([masterId, comparedId]);

        var master = bundles.FirstOrDefault(b => b.Id == masterId);
        var compared = bundles.FirstOrDefault(b => b.Id == comparedId);

        if (master is null || compared is null) return "One of the two files is no longer in the database.";

        return await Task.Run(() =>
        {
            var notes = new List<string>();

            var masterLog = ReadMachineLog(master, "master", notes);
            var comparedLog = ReadMachineLog(compared, "compared machine", notes);

            var masterProfile = StepProfile.From(masterLog);
            var comparedProfile = StepProfile.From(comparedLog);

            foreach (var (profile, role) in new[] { (masterProfile, "master"), (comparedProfile, "compared machine") })
            {
                if (profile.FinalStepWasStillRunning)
                {
                    notes.Add($"The {role} log ends part way through a step, so that step is left out of "
                              + "the timings - its length is unknown rather than measured.");
                }
            }

            var steps = StepTimingComparison.Compare(masterProfile, comparedProfile);
            var settings = SettingsComparison.Compare(MachineXmlPath(master), MachineXmlPath(compared));

            return CompareReportFormatter.Format(
                DiagnosticFileSummary.FromEntity(master),
                DiagnosticFileSummary.FromEntity(compared),
                steps, settings, notes);
        }, token);
    }

    private static IReadOnlyList<MachineLogEntry> ReadMachineLog(DiagnosticFile bundle, string role, List<string> notes)
    {
        var path = bundle.LogFiles.FirstOrDefault(l => l.Kind == LogFileKind.MachineLog)?.FullPath;

        if (path is null || !File.Exists(path))
        {
            notes.Add($"The {role} bundle has no MachineLog.txt on disk, so its timings could not be read.");
            return Array.Empty<MachineLogEntry>();
        }

        var entries = MachineLogFile.ParseFile(path);
        if (entries.Count == 0) notes.Add($"The {role} bundle's MachineLog.txt had no readable entries.");

        return entries;
    }

    /// <summary>Machine.xml is indexed as an ordinary extracted file, so find it by name.</summary>
    private static string MachineXmlPath(DiagnosticFile bundle) =>
        bundle.LogFiles.FirstOrDefault(l =>
            string.Equals(l.FileName, "machine.xml", StringComparison.OrdinalIgnoreCase))?.FullPath ?? string.Empty;
}
