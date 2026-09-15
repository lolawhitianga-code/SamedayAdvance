using System.Text.RegularExpressions;

namespace DiagFileMonitor.Core.Knowledge;

/// <summary>
/// What a reported SDN version actually means.
/// <para>
/// A version of all zeros - V0.0.0.0 - is a dev build that went out without its real version
/// stamped in. It is not a released version and it is not a parsing failure: the machine really
/// does report it that way. It matters because the field cannot then be trusted, and because two
/// machines both reporting V0.0.0.0 may be running completely different builds.
/// </para>
/// </summary>
public static class SoftwareVersion
{
    /// <summary>What these dev builds were believed to be around, and when that was last checked.</summary>
    private const string BelievedToBe = "V2.6.1";
    private const string AsAt = "September 2026";

    private static readonly Regex AllZeros = new(@"^\s*[vV]?\s*0+(\s*\.\s*0+)*\s*$", RegexOptions.Compiled);

    /// <summary>True where the version is all zeros, however many parts it has.</summary>
    public static bool IsUnstampedDevBuild(string? version) =>
        !string.IsNullOrWhiteSpace(version) && AllZeros.IsMatch(version);

    /// <summary>
    /// The warning to print beside the version, or null where the version looks like a real one.
    /// A whole sentence, so it reads properly wherever it is dropped in.
    /// </summary>
    public static string? Note(string? version) => IsUnstampedDevBuild(version)
        ? $"{version!.Trim()} is a dev build that never had its real version stamped in, not a "
          + $"released version. As at {AsAt} these were around {BelievedToBe}, but ask which build "
          + "it actually is rather than trusting this field."
        : null;

    /// <summary>
    /// The warning for a comparison, which has the extra trap that two machines both reporting
    /// all zeros may be running completely different builds.
    /// </summary>
    public static string? CompareNote(string? masterVersion, string? comparedVersion)
    {
        var master = IsUnstampedDevBuild(masterVersion);
        var compared = IsUnstampedDevBuild(comparedVersion);

        if (master && compared)
        {
            return $"Both files report {masterVersion!.Trim()} - dev builds that never had their "
                   + "real version stamped in. They are not necessarily the same build as each "
                   + $"other, so a difference here may just be two different versions. As at {AsAt} "
                   + $"these were around {BelievedToBe}; check which build each machine is on.";
        }

        if (master) return "The benchmark is on " + Note(masterVersion);
        if (compared) return "The compared machine is on " + Note(comparedVersion);

        return null;
    }
}
