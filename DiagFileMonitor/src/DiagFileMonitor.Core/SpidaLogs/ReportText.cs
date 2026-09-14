using System.Text;

namespace DiagFileMonitor.Core.SpidaLogs;

/// <summary>Shared layout helpers so every report section wraps to the same width.</summary>
public static class ReportText
{
    public const int Width = 78;

    /// <summary>Wraps to the report width, indenting every line after the first.</summary>
    public static string Wrap(string value, int indent, int width = Width)
    {
        var words = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var lines = new List<string>();
        var current = new StringBuilder();

        foreach (var word in words)
        {
            if (current.Length > 0 && current.Length + 1 + word.Length > width - indent)
            {
                lines.Add(current.ToString());
                current.Clear();
            }

            if (current.Length > 0) current.Append(' ');
            current.Append(word);
        }

        if (current.Length > 0) lines.Add(current.ToString());

        return string.Join("\n" + new string(' ', indent), lines);
    }
}
