# Sending a case back to Claude

The analysis only gets better by meeting real files. Every improvement in it so far came from a
support person saying *"it should have spotted X, and here's how I knew"*. This turns that into a
button rather than a conversation.

## Using it

1. Select a bundle, then **Send to Claude** on the toolbar (or right-click → *Send feedback to
   Claude*). The app analyses it first, so the notes are written against the same report that
   goes in the package.
2. The window shows that report on the left and four fields on the right.
3. **Create package** writes a `.zip` to `%AppData%\DiagFileMonitor\Feedback\` and *Show me the
   file* opens it in Explorer.
4. Attach the zip to a Claude session. It needs no explanation — `README.md` says what it is and
   `PROMPT.md` says what to do.

## The four fields, and why they are separate

| Field | Why |
|---|---|
| **How did it do?** | A working case is worth keeping too — it becomes a regression test rather than a change. |
| **What was actually wrong** | The ground truth. Without it there is nothing to check a change against. |
| **How did you know?** | **The one that becomes code.** *"The output came on and the confirm input never did"* can be turned into a check. *"The saw was broken"* cannot. |
| **What should the report have done** | Optional. A suggestion, not a requirement — the code may want a different shape. |

A single free-text box would lose the third, which is the whole point.

## What is in the package

```
feedback-M20421-2026-09-15-1831-missed.zip
├── README.md              what this is, and a note that it carries customer data
├── PROMPT.md              the learning prompt - start here
├── feedback.md            the notes, unedited
├── report-produced.txt    exactly what the app said
├── context.json           machine, serial, customer, versions, dates, app version
└── bundle/                the diagnostic files the analysis actually read
```

The filename carries the serial, the date and the verdict, so a folder of them sorts sensibly and
you can see what each one is without opening it.

## Decisions worth knowing

- **The extracted files go in, not the original `.szip`.** A bundle can be re-processed; the
  extracted copy is what produced the report being complained about.
- **A single file over 25 MB is left out and named** — in the result *and* in the package's own
  README, so the person reading it knows what is missing. The cap is generous because a multi-
  megabyte `MachineLog.txt` is the whole point and compresses to very little; it exists to stop a
  stray video or database file going along for the ride.
- **A bundle whose files have been cleaned up still packages** — the report and the notes are
  worth having on their own, with a note explaining what is missing.
- **Notes with no ground truth are refused.** A verdict and a suggestion with nothing to check
  them against produce a package nobody can act on.
- **The package carries customer name and site.** That is deliberate — the machine's identity is
  context the analysis needs — and the README says so before it goes anywhere.
