using System.Text;
using DiagFileMonitor.Core.Models;

namespace DiagFileMonitor.Core.Services;

/// <summary>
/// Turns a support person's notes into something a Claude session can act on without being
/// told anything else. The package has to stand on its own: whoever opens it may not have been
/// part of the conversation that produced the report.
/// </summary>
public static class FeedbackPromptFormatter
{
    /// <summary>The learning prompt. This is the file the human pastes or attaches.</summary>
    public static string Prompt(DiagnosticFileSummary file, AnalysisFeedback feedback, string reportText)
    {
        var text = new StringBuilder();

        text.AppendLine("# Improve the diagnostic analysis from a real case");
        text.AppendLine();
        text.AppendLine("A support person ran this bundle through Diagnostic File Monitor and has told us what");
        text.AppendLine("the report got wrong. Everything needed to check it is in this package.");
        text.AppendLine();

        text.AppendLine("## The machine");
        text.AppendLine();
        text.AppendLine($"- **Model:** {file.MachineType}");
        text.AppendLine($"- **Serial:** {file.SerialNumber}");
        text.AppendLine($"- **Customer:** {file.Customer}, {file.SiteLocation}");
        text.AppendLine($"- **Software:** {file.SoftwareName} {file.Version}");
        text.AppendLine($"- **Bundle:** `{file.OriginalFileName}`, arrived {file.ArrivedDisplay}");
        text.AppendLine();

        if (!string.IsNullOrWhiteSpace(file.SupportIssue))
        {
            text.AppendLine($"The operator wrote: **\"{file.SupportIssue.Trim()}\"**");
            text.AppendLine();
        }

        text.AppendLine("## How the report did");
        text.AppendLine();
        text.AppendLine($"**{feedback.VerdictText}.**");
        text.AppendLine();

        Section(text, "What was actually wrong", feedback.WhatWasActuallyWrong);
        Section(text, "How they knew", feedback.HowYouKnew);
        Section(text, "What the report should have done", feedback.WhatShouldChange);

        text.AppendLine("## What to do with this");
        text.AppendLine();

        if (feedback.Verdict == FeedbackVerdict.GotItRight)
        {
            text.AppendLine("This one worked. Add a regression test built from the real files in `bundle/` so it");
            text.AppendLine("keeps working, rather than changing behaviour.");
        }
        else
        {
            text.AppendLine("1. **Check the claim against the files** in `bundle/` before changing anything. The");
            text.AppendLine("   note above is a support person's reading, and it is worth confirming the log");
            text.AppendLine("   really says what they believe it says.");
            text.AppendLine("2. **Work out whether this generalises.** A check that only ever fires on this one");
            text.AppendLine("   serial is worth less than one built on a naming convention or a signal other");
            text.AppendLine("   machines share.");
            text.AppendLine("3. **Make the change, with a test built from these real lines** - not from an invented");
            text.AppendLine("   fixture. Every parser in this repo was wrong on its first attempt and only became");
            text.AppendLine("   correct after meeting a real file.");
            text.AppendLine("4. **Re-run this bundle** and confirm the report now says what it should.");
        }

        text.AppendLine();
        text.AppendLine("## Where the code lives");
        text.AppendLine();
        text.AppendLine("Repository `lolawhitianga-code/SamedayAdvance`, folder `DiagFileMonitor/`.");
        text.AppendLine();
        text.AppendLine("| Folder | What is in it |");
        text.AppendLine("|---|---|");
        text.AppendLine("| `src/DiagFileMonitor.Core/SpidaLogs/` | Log parsing and machine-agnostic analysis |");
        text.AppendLine("| `src/DiagFileMonitor.Core/Knowledge/` | Per-machine facts and the specific checks |");
        text.AppendLine("| `tests/DiagFileMonitor.Core.Tests/` | xunit, runs on any platform |");
        text.AppendLine("| `docs/HANDOVER.md` | Architecture, schema, conventions, known bugs |");
        text.AppendLine();
        text.AppendLine("Conventions that matter here:");
        text.AppendLine();
        text.AppendLine("- **Run `./tools/check.sh` before committing.** WPF will not build on Linux, so that");
        text.AppendLine("  script is what stands in for it. Treat MSB3243 as a failure.");
        text.AppendLine("- **Every fact in `Knowledge/` carries a confidence** - confirmed, inferred or");
        text.AppendLine("  unconfirmed - and the report prints it, because these get quoted to customers.");
        text.AppendLine("  A new fact from one case is *inferred* at best until it is seen twice.");
        text.AppendLine("- **Do not widen a check until it stops crying wolf.** When one motor fails the");
        text.AppendLine("  machine aborts the step and drops every output, and reporting each of those as its");
        text.AppendLine("  own fault is how a useful section turns into noise.");
        text.AppendLine();

        text.AppendLine("## What the report said");
        text.AppendLine();
        text.AppendLine("In full in `report-produced.txt`. Note the version that produced it - if the code has");
        text.AppendLine("moved on, re-run the bundle before assuming the output below is still current.");

        return text.ToString();
    }

    /// <summary>The note that explains the package itself, for whoever opens the zip.</summary>
    public static string Readme(DiagnosticFileSummary file, AnalysisFeedback feedback, IReadOnlyList<string> notes)
    {
        var text = new StringBuilder();

        text.AppendLine("# Diagnostic analysis feedback");
        text.AppendLine();
        text.AppendLine($"From Diagnostic File Monitor, {feedback.RaisedUtc.ToLocalTime():yyyy-MM-dd HH:mm}"
                        + (feedback.RaisedBy.Trim().Length > 0 ? $", raised by {feedback.RaisedBy.Trim()}" : string.Empty)
                        + ".");
        text.AppendLine();
        text.AppendLine($"A real case from **{file.MachineType} serial {file.SerialNumber}** where the report");
        text.AppendLine($"**{feedback.VerdictText}**.");
        text.AppendLine();
        text.AppendLine("## Start here");
        text.AppendLine();
        text.AppendLine("Open **`PROMPT.md`** and give it to Claude along with this folder. It says what the");
        text.AppendLine("report produced, what was really wrong, and what to do about it.");
        text.AppendLine();
        text.AppendLine("## What is in here");
        text.AppendLine();
        text.AppendLine("| File | What it is |");
        text.AppendLine("|---|---|");
        text.AppendLine("| `PROMPT.md` | The learning prompt - start here |");
        text.AppendLine("| `feedback.md` | The support person's notes, unedited |");
        text.AppendLine("| `report-produced.txt` | Exactly what the app said about this bundle |");
        text.AppendLine("| `context.json` | Machine, serial, customer, versions and dates |");
        text.AppendLine("| `bundle/` | The diagnostic files the analysis actually read |");
        text.AppendLine();
        text.AppendLine("## Before sharing this");
        text.AppendLine();
        text.AppendLine("The bundle carries the **customer name and site**, and the logs may carry job and");
        text.AppendLine("member names. That is deliberate - the machine's identity is context the analysis");
        text.AppendLine("needs - but it is worth knowing before the package goes anywhere.");

        if (notes.Count > 0)
        {
            text.AppendLine();
            text.AppendLine("## Notes on building this package");
            text.AppendLine();
            foreach (var note in notes) text.AppendLine($"- {note}");
        }

        return text.ToString();
    }

    /// <summary>The notes on their own, so they survive without the prompt's framing.</summary>
    public static string Notes(DiagnosticFileSummary file, AnalysisFeedback feedback)
    {
        var text = new StringBuilder();

        text.AppendLine($"# Feedback on {file.OriginalFileName}");
        text.AppendLine();
        text.AppendLine($"{file.MachineType}, serial {file.SerialNumber}, {file.Customer}.");
        text.AppendLine($"Raised {feedback.RaisedUtc.ToLocalTime():yyyy-MM-dd HH:mm}"
                        + (feedback.RaisedBy.Trim().Length > 0 ? $" by {feedback.RaisedBy.Trim()}" : string.Empty) + ".");
        text.AppendLine();

        if (!string.IsNullOrWhiteSpace(file.SupportIssue))
        {
            text.AppendLine($"Operator's words: \"{file.SupportIssue.Trim()}\"");
            text.AppendLine();
        }

        text.AppendLine($"Verdict: {feedback.VerdictText}.");
        text.AppendLine();

        Section(text, "What was actually wrong", feedback.WhatWasActuallyWrong);
        Section(text, "How I knew", feedback.HowYouKnew);
        Section(text, "What the report should have done", feedback.WhatShouldChange);

        return text.ToString();
    }

    private static void Section(StringBuilder text, string heading, string body)
    {
        if (body.Trim().Length == 0) return;

        text.AppendLine($"### {heading}");
        text.AppendLine();
        text.AppendLine(body.Trim());
        text.AppendLine();
    }
}
