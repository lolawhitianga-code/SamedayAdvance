using System.Globalization;
using System.Text.RegularExpressions;

namespace DiagFileMonitor.Core.Services;

/// <summary>
/// Reads the timestamp Spida puts at the front of a support file name, e.g.
/// <c>_7_27_2026 9-53-10 PM . M21461SupportFile.szip</c>.
/// <para>
/// The date is month_day_year (confirmed by names like <c>_10_24_2025</c>, where 24 cannot be a
/// month), and neither the month, day nor hour is zero padded. This is a far better "arrived"
/// date than the file's creation time, which only records when the file was copied about.
/// </para>
/// </summary>
public static class DiagFileNameDate
{
    private static readonly Regex Pattern = new(
        @"(?<month>\d{1,2})_(?<day>\d{1,2})_(?<year>\d{4})[ _]+(?<hour>\d{1,2})-(?<minute>\d{2})-(?<second>\d{2})\s*(?<meridiem>AM|PM)?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Returns the timestamp in UTC, or null when the name carries no usable date.
    /// <paramref name="fileNameTimesAreUtc"/> says whether the machine wrote the time in UTC
    /// or in its own local time.
    /// </summary>
    public static DateTime? TryParseUtc(string fileName, bool fileNameTimesAreUtc)
    {
        var match = Pattern.Match(Path.GetFileName(fileName));
        if (!match.Success) return null;

        var hour = int.Parse(match.Groups["hour"].Value, CultureInfo.InvariantCulture);
        var meridiem = match.Groups["meridiem"].Value;

        if (meridiem.Length > 0)
        {
            if (hour is < 1 or > 12) return null;

            var isPm = meridiem.Equals("PM", StringComparison.OrdinalIgnoreCase);
            hour = hour switch
            {
                12 when !isPm => 0,     // 12:xx AM is midnight
                12 => 12,               // 12:xx PM is noon
                _ when isPm => hour + 12,
                _ => hour
            };
        }

        try
        {
            var local = new DateTime(
                int.Parse(match.Groups["year"].Value, CultureInfo.InvariantCulture),
                int.Parse(match.Groups["month"].Value, CultureInfo.InvariantCulture),
                int.Parse(match.Groups["day"].Value, CultureInfo.InvariantCulture),
                hour,
                int.Parse(match.Groups["minute"].Value, CultureInfo.InvariantCulture),
                int.Parse(match.Groups["second"].Value, CultureInfo.InvariantCulture),
                fileNameTimesAreUtc ? DateTimeKind.Utc : DateTimeKind.Local);

            return fileNameTimesAreUtc ? local : local.ToUniversalTime();
        }
        catch (ArgumentOutOfRangeException)
        {
            // Month 13, day 32 and similar: the name only looked like a date.
            return null;
        }
    }

    /// <summary>The name's timestamp where there is one, otherwise when the file landed on disk.</summary>
    public static DateTime ArrivedUtc(string path, bool fileNameTimesAreUtc)
    {
        if (TryParseUtc(path, fileNameTimesAreUtc) is { } fromName) return fromName;

        try
        {
            return new FileInfo(path).CreationTimeUtc;
        }
        catch (IOException)
        {
            return DateTime.UtcNow;
        }
    }
}
