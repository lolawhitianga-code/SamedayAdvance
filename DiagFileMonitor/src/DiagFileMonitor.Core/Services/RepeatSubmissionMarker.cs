using DiagFileMonitor.Core.Models;

namespace DiagFileMonitor.Core.Services;

/// <summary>
/// Flags bundles that follow another from the same machine within a short window. A machine
/// sending twice in a week usually means the first fix did not hold, which is worth noticing
/// before treating the second bundle as a fresh case.
/// </summary>
public static class RepeatSubmissionMarker
{
    public const int DefaultWindowDays = 7;

    public static void Mark(IEnumerable<DiagnosticFileSummary> files, int windowDays = DefaultWindowDays)
    {
        foreach (var group in files.GroupBy(f => f.SerialNumber, StringComparer.OrdinalIgnoreCase))
        {
            var ordered = group.OrderBy(f => f.ArrivedAtUtc).ToList();

            for (var i = 0; i < ordered.Count; i++)
            {
                // An unknown serial is not a machine, so bundles under it are unrelated.
                if (ordered[i].SerialNumber == DiagnosticFileSummary.Unknown)
                {
                    ordered[i].IsRepeatSubmission = false;
                    continue;
                }

                ordered[i].IsRepeatSubmission = i > 0
                    && (ordered[i].ArrivedAtUtc - ordered[i - 1].ArrivedAtUtc).TotalDays <= windowDays;
            }
        }
    }
}
