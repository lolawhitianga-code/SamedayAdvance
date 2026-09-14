# Diagnostic File Monitor — Handover

For the next developer picking this up. Written 2026-09-14.

---

## 1. What this is

A Windows desktop app for **one technical support person at Spida Machinery (NZ)**. Spida
build timber frame and truss machinery; when a machine misbehaves the operator exports a
`.szip` support bundle, which lands in a folder. This app watches those folders, unpacks each
bundle, reads the machine identity out of `Machine.xml`, stores it, and lists everything on a
dashboard. On top of that it analyses the logs inside a bundle and benchmarks one machine
against a known-good one.

**The user is not a developer.** Build instructions in `README.md` are deliberately
step-by-step. Keep that tone.

**Standing user preferences:** New Zealand based, metric units, plain language — avoid
elaborate vocabulary in both UI text and report output.

---

## 2. Current status

| | |
|---|---|
| State | Working and in use. Not packaged or signed; built locally by the user. |
| Tests | 278 passing, 0 failing, 0 skipped |
| Source | ~7,700 lines C# across 2 projects |
| Branch | `claude/windows-diag-file-monitor-szw857`, merged to `main` |
| Repo | `lolawhitianga-code/SamedayAdvance`, subfolder `DiagFileMonitor/` |

`PRODUCT.md` and `console/` at the repo root belong to an **unrelated** earlier project
(Sameday Advance). Ignore them.

Validated against two real support bundles from machine **M20716** (RakingWallExtruderV3DG at
Carters, Cambridge). Every parser in `SpidaLogs/` was rewritten at least once after meeting
real data — see §7.

---

## 3. Architecture

Two projects, split so the logic is testable on Linux:

```
DiagFileMonitor.Core   net8.0           All logic. No UI types. Runs anywhere.
DiagFileMonitor.App    net8.0-windows   WPF + WinForms interop. UI only.
```

**This split is load-bearing.** WPF cannot be compiled on Linux — Ubuntu's .NET SDK omits the
`Microsoft.NET.Sdk.WindowsDesktop` targets. Keeping every decision in Core is what makes the
test suite runnable off-Windows. Do not move logic into the App project.

### Dependency flow

```
App (WPF)  ──►  Core.Services  ──►  Core.Data (EF Core/SQLite)
                      │
                      ├──►  Core.SpidaLogs    (log formats, machine-agnostic)
                      │
                      └──►  Core.Knowledge    (per-machine facts, layered on top)
```

### The four Core folders

**`SpidaLogs/`** — reading Spida's log formats and reasoning about them *without knowing
which machine it is*. Unit boundaries come from a step counter dropping, which holds across
every machine family even though the step numbers mean different things on each.

**`Knowledge/`** — what is known about *specific* machine models, layered over the
machine-agnostic analysis. Currently one model (RakingWallExtruderV3DG). This is where "the
fault means X on this machine, check Y first" lives.

**`Services/`** — plumbing: folder watching, unpacking, the repository, settings, Zoho, email.

**`Models/`** — EF entities and flattened read models.

### Key design rule in `Knowledge/`

Every fact carries a `Confidence`: `Confirmed`, `Inferred`, or `Unconfirmed`, and the report
prints it. This exists because the support person **quotes these reports to customers**. A
guess must never be indistinguishable from something verified. `MachineKnowledge.OpenGaps`
prints what is known to be *unknown* for the same reason. Preserve this behaviour.

---

## 4. Folder structure

```
DiagFileMonitor/
├── DiagFileMonitor.sln
├── build.bat                      One-click build for the user (see §9)
├── README.md                      End-user instructions, deliberately non-technical
├── docs/
│   ├── HANDOVER.md                This file
│   └── raked-wall-extruder-knowledge.md    Domain knowledge + what real logs confirmed
├── tools/                         Linux verification rig (see §8)
│   ├── check.sh                   Run before every commit
│   ├── check_xaml.py              Static XAML binding validator
│   ├── vmcheck/                   Type-checks ViewModels without WPF
│   └── dumpprops/                 Reflects over assemblies via MetadataLoadContext
├── samples/sample-diagnostic/     Synthetic bundle for manual testing
├── src/
│   ├── DiagFileMonitor.Core/
│   │   ├── Data/                  DiagDbContext, DatabaseInitializer
│   │   ├── Models/                EF entities + read models
│   │   ├── Services/              Monitoring, unpacking, repo, Zoho, email, settings
│   │   ├── SpidaLogs/             Log parsers, step timing, comparison
│   │   └── Knowledge/             Per-machine knowledge + checks
│   └── DiagFileMonitor.App/
│       ├── MainWindow.xaml        Dashboard
│       ├── AnalysisWindow.xaml    Analysis / compare output
│       ├── LogSearchWindow.xaml   Cross-bundle log search
│       ├── IntegrationSettingsWindow.xaml   Zoho/email/alert config
│       ├── Theme.xaml             Spida brand colours and control styles
│       ├── ViewModels/            MVVM via CommunityToolkit.Mvvm source generators
│       ├── Converters/
│       └── Services/              BrandLogo, TrayNotifier
└── tests/DiagFileMonitor.Core.Tests/    xunit, 278 tests
```

---

## 5. Database schema

SQLite via EF Core 8. Default location `%AppData%\DiagFileMonitor\diagfiles.db`.

### `DiagnosticFiles`

| Column | Type | Notes |
|---|---|---|
| `Id` | INTEGER PK | |
| `OriginalFileName` | TEXT | e.g. `_7_27_2026 10-15-10 PM . M20716SupportFile.szip` |
| `SourcePath` | TEXT | Where the zip was found |
| `FileSizeBytes` | INTEGER | |
| `ArrivedAtUtc` | TEXT | **Parsed from the file name, not the filesystem** — see §7 |
| `ProcessedAtUtc` | TEXT NULL | |
| `ExtractedPath` | TEXT NULL | Unpack folder; null once cleaned up |
| `MachineType` | TEXT NULL | From `Machine.xml` `<Title>`, e.g. `RakingWallExtruderV3DG` |
| `MachineName` | TEXT NULL | |
| `SerialNumber` | TEXT NULL | e.g. `M20716` |
| `Customer` | TEXT NULL | |
| `SiteLocation` | TEXT NULL | |
| `SoftwareName` | TEXT NULL | e.g. `Spida SDN` |
| `Version` | TEXT NULL | e.g. `V2.4.0.0` |
| `SupportPanel` | TEXT NULL | From `SupportInfo.txt` |
| `SupportMembers` | TEXT NULL | From `SupportInfo.txt` |
| `SupportIssue` | TEXT NULL | **The operator's own words. Drives the whole report — see §6.** |
| `Status` | TEXT | `Pending` / `Processed` / `Error`, stored as string |
| `ErrorMessage` | TEXT NULL | |
| `Notes` | TEXT NULL | Support's case notes |
| `TicketNumber` | TEXT NULL | Free-text ticket ref |
| `IsBaseline` | INTEGER | Marks a known-good bundle |
| `ZohoTicketId` | TEXT NULL | |
| `ZohoTicketNumber` | TEXT NULL | |
| `ZohoTicketCreatedUtc` | TEXT NULL | |
| `AlertSentUtc` | TEXT NULL | Stops a burst alert firing twice |

Indexes: `SerialNumber`, `MachineType`, `Customer`, `ArrivedAtUtc`.

### `ExtractedLogFiles`

| Column | Type | Notes |
|---|---|---|
| `Id` | INTEGER PK | |
| `DiagnosticFileId` | INTEGER FK | Cascade delete |
| `FileName` | TEXT | |
| `FullPath` | TEXT | |
| `SizeBytes` | INTEGER | |
| `Kind` | TEXT | `ChangeLog` / `MachineLog` / `ErrorLog` / `SupportInfo` / `Other` |

### Migrations — read this before changing the schema

**There are no EF migrations.** `DatabaseInitializer` calls `EnsureCreated()` then runs
idempotent `ALTER TABLE ADD COLUMN` statements from a hard-coded list.

This exists because the app's whole purpose is accumulating history — a migration that forces
the user to delete the database defeats the point, and the user cannot run CLI migration
tools. **To add a column: add the property to the entity AND add a row to
`DatabaseInitializer.ExpectedColumns`.** Forgetting the second step means the app works on a
fresh database and crashes on an existing one.

Column drops and type changes are not supported by this mechanism. If you need one, write a
real migration path and test it against a populated database.

---

## 6. What the app does

### Ingestion
`FolderMonitorService` watches N folders with `FileSystemWatcher`, polls for file stability
(a bundle is still being written when the event fires), and feeds a single-consumer queue
(`ConcurrentQueue` + `SemaphoreSlim`) so two bundles never unpack at once.

Skips: already-stored duplicates, and bundles older than `MonitorMaxAgeDays`.

### Analysis (the Analyse button)
`DiagnosticAnalysisService` → report, in this order:

1. **`START HERE — WHAT THE OPERATOR DESCRIBED`** — the operator's `SupportIssue` text is
   matched against `ComplaintTopics` (8 topics) and each match says what to check *in order*,
   plus every matching setting from `Change.log` **across the whole file**, not just the export
   day. This is first by design: the logs are loudest about whatever happens most often, which
   is rarely the complaint. A gun height changed three weeks ago still explains a gun complaint.
2. Units attempted, faults, repeats
3. `ErrLog.txt` classified into real / cosmetic / out-of-window / background noise
4. Settings changed around the session
5. **`CAN IT HOME?`** — the homing interlock check
6. Recognised faults, known problems, serial history, axes, noise, open gaps
7. Where to start looking, questions for the customer

### Compare (benchmark)
Mark one bundle as **master** (known good), select another, Compare. Produces overall cycle
time, step sequence, per-step median timings with a verdict, and a `Machine.xml` settings diff.

### Two machine-specific checks in `Knowledge/`

**`HomeInterlockCheck`** — both `GripperProductSensor` inputs and both `PlatePresentSwitch`
inputs must read 0 or the machine will not home, with nothing on screen to say why. Reads the
last known state of each sensor at each `HomeServos` command. Three subtleties, each with a
test:
- Sensor addresses are **discovered from the log by tag**, not hard-coded — only one address
  was ever observed.
- Entries sharing a timestamp are one PLC scan, so a sensor change printed on a *later line*
  than the command was still true when the command was given.
- `MachineLog.txt` records **changes only**, so a sensor at 0 all session never appears. Those
  are reported as absent, never assumed.

**`PlatePresentCheck`** — tells a real lost-product event from a sensor glitch. Both sides
dropping together *with* a clamp output change = normal release. One side alone with nothing
moving = glitch. Implemented and tested, but has only ever been observed on a Spida Saw, not
on this machine.

---

## 7. Real-data lessons (do not undo these)

Every one of these was a wrong guess corrected by a real file. They are the most expensive
knowledge in the repo.

| Area | What was wrong | What is true |
|---|---|---|
| `Machine.xml` | Assumed tidy element names | Real file uses `<MachineModel>`, `<SiteName>`, `<MachineName>`, `<SiteLocation>`; **Version exists only inside `<Title>`**. ~40 nested `<Name>` elements mean direct children must be read before descendants. |
| `<Title>` parsing | Split on commas left-to-right | Customer names contain commas. `TitleLine.Parse` reads from **both ends** inward. |
| `ErrLog.txt` | Assumed one line per error | It is a **block format**. The line-based parser found 0 entries in a real file; the block parser finds 2,190. |
| `Arrived` date | Used filesystem timestamps | Comes from the **file name** (`_7_27_2026 10-15-10 PM`, month_day_year, non-padded, 12-hour), treated as UTC by default. |
| Duplicate imports | No dedup existed | Every "Start Monitoring" re-imported the folder — 58 records became 616. `IsAlreadyStoredAsync` fixes it. |
| Step timings | Last step measured to the last log line | The step a log **ends in never finished**. A trailing `Upload succeeded` line made one export read as *421% slower* than an identical one. Open steps are excluded from timings but still count as a step the machine reached. |
| Issue signals | Matched against all log text | `FixedEjectServo` contains "eject", so the ejection bug flagged on **every** export. Signals match **fault text only**. |
| Extract folders | Named by timestamp | Two bundles in the same millisecond collided and one overwrote the other. Counter suffix added. |

---

## 8. Development environment

The work so far was done on **Linux**, where WPF will not compile. `tools/check.sh` is the
substitute — **run it before every commit**:

```
./tools/check.sh
```

1. Build Core
2. Type-check ViewModels against real WPF reference assemblies (`vmcheck`)
3. Statically validate every XAML binding and `StaticResource` (`check_xaml.py`)
4. Run the 278 Core tests

Step 2 is subtle. `vmcheck.csproj` uses `FrameworkReference Microsoft.WindowsDesktop.App` with
`EnableWindowsTargeting` and **must not** set `UseWPF` or `UseWindowsForms` — either one
imports SDK targets Ubuntu does not have. An earlier version resolved two `WindowsBase`
versions and MSBuild picked one "arbitrarily" (MSB3243), silently hiding a real compile
failure. If you see MSB3243, treat it as a failure, not a warning.

Step 3 exists because step 2 cannot see XAML at all. A typo in a binding path compiles fine and
fails silently at runtime; `check_xaml.py` reflects over the built assemblies with
`MetadataLoadContext` and checks each `{Binding}` against real properties.

**On Windows none of this is needed** — `dotnet build` on the solution covers steps 1–3
properly. The rig is only for developing off-Windows.

---

## 9. Deployment

There is no CI, no installer, no code signing. The user builds it themselves:

1. Install the .NET 8 **SDK** (x64) from Microsoft.
2. Download the repo as a ZIP from GitHub and extract it.
3. Double-click **`build.bat`** in `DiagFileMonitor/`.
4. Run `publish\DiagFileMonitor.exe`.

`build.bat` runs `dotnet publish -c Release -r win-x64 --self-contained true -o publish`, and
prints friendly guidance if .NET is missing or the build fails. Self-contained means the
target PC needs no runtime installed.

### Runtime data locations

| What | Where |
|---|---|
| Settings | `%AppData%\DiagFileMonitor\settings.json` |
| Database | `%AppData%\DiagFileMonitor\diagfiles.db` |
| Unpacked bundles | `%AppData%\DiagFileMonitor\Extracted\` |
| Default watch folder | `%AppData%\DiagFileMonitor\Incoming\` |
| Log file | `%AppData%\DiagFileMonitor\logs\diagfilemonitor.log` |
| Logo | `logo.png` next to the exe, or in `%AppData%\DiagFileMonitor\` |

---

## 10. Known bugs and gaps

### B1 — Burst alerting uses a parser that cannot read real Spida logs *(high)*

There are **two analysis paths**, and only one has met real data:

| Path | Used by | State |
|---|---|---|
| `SpidaLogs/` + `Knowledge/` | Analyse button, Compare | Validated against real files |
| `DiagnosticAnalyser` + `MachineLogParser` + `AnalysisReportFormatter` | `BurstAlertService` only | **Never validated** |

`MachineLogParser.DefaultLinePattern` expects `2026-09-01 08:00:00.000 START Preheat`. Real
Spida lines are `10:12:15.6590038,  Other, ClsWallExtruder,  HomeServos` — no date, comma
delimited. **It will parse zero steps from a real machine log**, so any automated burst alert
would carry a near-empty analysis.

Fix: point `BurstAlertService` at `DiagnosticAnalysisService`/`SpidaLogAnalyser` and delete
the legacy trio (`DiagnosticAnalyser.cs`, `MachineLogParser.cs`, `AnalysisReportFormatter.cs`)
plus `DiagnosticAnalyserTests.cs` and the `MachineLogPattern` setting. ~16 tests cover the
legacy path and will go with it.

### B2 — Zoho has never run against a real account *(medium)*
`ZohoDeskClient` is tested only against a fake `HttpMessageHandler`. OAuth refresh flow,
`orgId` header and the data-centre domain are unverified. Credentials are not configured.

### B3 — Logo is not in the repo *(low)*
Images could not be transferred into the dev environment. `BrandLogo` loads `logo.png` at
runtime from beside the exe or `%AppData%`. Committing the real PNG would remove the workaround.

### B4 — `MachineName` reads oddly on real data *(low, may be correct)*
The M20716 bundle reports `MachineType = RakingWallExtruderV3DG` but `MachineName = Spida Saw`.
That is what is in the customer's `Machine.xml`, so it may be their misconfiguration rather
than a parser bug. Worth confirming with the user before "fixing".

### B5 — Old records keep stale `Machine.xml` fields *(low)*
Bundles imported before the parser was corrected still hold `(unknown)` values. There is no
re-read button; the only remedy is clearing the database, which loses notes.

### Open domain questions (also printed in every report)
- Node0–Node5 have never been tied to the named axes. The log lists node statuses in one
  block at power-on and names axes separately; nothing connects them.
- Which circuits keep or lose power and air on an E-Stop.
- Whether the overcurrent faults on M19820 / M20822 / M20716 share a root cause.
- What `WallExtruderStep` numbers mean. A real export ran `0 → 10 → 300 → 302 → 0`, which does
  **not** line up with the 1–25 operator sequence in the domain doc. Keep the two apart.

---

## 11. Pending / unstarted

- **`CloudLog/` is completely unread.** Every bundle carries ~49 files of daily history —
  `msg_YYYY-MM-DD.log` (IO counters, state changes, production stats as `type!timestamp!json`),
  `err_YYYY-MM-DD.log`, and `maint_data.json` (per-output on/off counts and run times). That is
  **weeks** of data against the 111 lines in `Logs/MachineLog.txt`. Biggest single opportunity
  in the codebase.
  - Note: `err_*.log` is close to `ErrLog.txt` but uses `Info:`/`Trace:` where `ErrLog.txt`
    uses `Title:`/`StackTrace:`. The current parser would silently drop the `Info` line.
  - The `msg_` timestamps (e.g. `5250893043399779821`) are **not** .NET ticks and have not
    been decoded.
- Re-read `Machine.xml` for stored bundles without losing notes (fixes B5).
- More complaint topics — `ComplaintTopics.cs` is a plain list; adding one is a small edit.
- Knowledge for other machine models. `MachineKnowledgeBase.All` takes a new entry; only the
  raked extruder exists today.

---

## 12. Recommended next steps

In order.

1. **Fix B1.** It is the only *wrong* behaviour in the app — everything else is missing rather
   than broken. If burst alerting is ever switched on it will post useless analyses to real
   customer tickets. Deleting the legacy path also removes the confusion of two analysers.
2. **Read `CloudLog/`.** Start with `maint_data.json` and the `err_*.log` files (the formats are
   already nearly understood) before tackling the `msg_` timestamp encoding. This turns the tool
   from single-snapshot to trend analysis: "this output's on-count has doubled since July".
3. **Get a real Zoho sandbox and exercise B2** before relying on ticket posting.
4. **Ask the user for more complaint types.** The complaint router is the feature most directly
   aimed at their daily work and the cheapest to extend. Each new topic needs only: keywords,
   what to check in order, and which settings to search.
5. Ask for a bundle from a **different machine model** — every parser fix so far came from real
   files, and the knowledge layer has only ever been exercised against one model.

### Working style that has held up

- **Never guess a file format.** Ask for a real example. Every parser in `SpidaLogs/` was wrong
  on the first attempt and only became correct after meeting real data.
- **Run `./tools/check.sh` before every commit.** No exceptions.
- **Keep confidence labels honest.** If something is inferred, say inferred. The user quotes
  these reports to paying customers.
- When a test fails, work out whether the *test* or the *code* is wrong before changing either.
  Two fixtures in this repo encoded impossible log sequences and the code was right.
