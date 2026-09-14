# Diagnostic File Monitor

A Windows desktop app (C#, .NET 8, WPF) for technical support. It watches folders
for incoming diagnostic zip files, unpacks them, reads `machine.xml`, and stores
everything in a local database. The dashboard lists what has arrived, grouped by
serial number by default, with search, case notes and log searching on top.

## Projects

- `src/DiagFileMonitor.Core` — folder watching, zip extraction, XML parsing, the
  SQLite database, and all the logic that can be tested without a UI.
- `src/DiagFileMonitor.App` — the WPF dashboard.
- `tests/DiagFileMonitor.Core.Tests` — xunit tests for Core (92 at last count).

## What it does

**Taking files in**
- Watches one or more folders (email drop, SFTP drop, etc.) with a
  `FileSystemWatcher`, filtered to the extensions you configure (defaults to
  `.zip`). Waits for a file's size to settle before touching it, so a zip that
  is still copying in is not picked up half-written.
- Unpacks each zip into its own folder under `%AppData%\DiagFileMonitor\Extracted`,
  finds `machine.xml` anywhere inside, and pulls out machine type, serial number,
  customer and version. Parsing is tolerant of common tag-name variants (e.g.
  `Serial` or `SerialNumber`) — adjust `MachineXmlParser.cs` once you have a real
  sample file.
- Records every extracted file, tagging `changelog.txt`, `machinelog.txt` and
  `errorlog.txt` so they can be opened (and searched) directly.
- A bundle that fails to unpack, or has no serial number, is still recorded and
  shown in red — an arrival that failed is exactly what support needs to see.

**Finding things**
- Group the grid by serial number (default), machine type, customer, arrival date
  or status.
- Filter bar: free-text search across serial, customer, file name, machine type,
  version, ticket number and notes (all words must match), plus a status filter
  and an arrival date range.
- Right-click a row to see that machine's full history — every bundle that serial
  has sent, with the date range, the versions in order, and how many failed.
- **Search in logs...** greps the unpacked logs of every stored bundle, for
  "have we seen this error code before, on any machine?"

**Working a case**
- Ticket number and free-text notes against each bundle, editable in the side
  panel and searchable.
- **Copy summary for ticket** puts the machine details, arrival time, status and
  notes on the clipboard as plain text.
- Right-click to open the extracted folder, or `errorlog.txt` / `machinelog.txt` /
  `changelog.txt` straight in the default viewer.
- Bundles arriving within 7 days of the same machine's last one are flagged as a
  **Repeat** — usually a sign the first fix did not hold.

**Alerting, analysis and Zoho**
- When one serial sends several bundles inside a rolling window (default 2 in 24 hours), the
  app analyses the latest bundle automatically, posts the report to a Zoho Desk ticket and
  emails the alert.
- Ticket handling matches how support actually works: a new Zoho ticket is raised per burst,
  unless a ticket was already raised for that same machine recently (default 24 hours), in
  which case the new report is added to it as a note. The local database is the record of
  which ticket belongs to which machine, so no Zoho custom field is needed.
- The analysis compares the bundle against bundles marked as known-good baselines of the same
  machine type: per-step timings from machinelog.txt against the baseline median, steps that
  never completed, error lines the baselines do not have, and changes made shortly beforehand.
- Configure all of it under **Alerts and Zoho...**, which includes a Test Zoho button and a
  send-test-email button.

**Keeping on top of it**
- Headline counts above the grid: arrivals today, arrivals over 7 days, failures,
  distinct machines, total stored, busiest customer.
- A notification-area balloon as each bundle lands (toggleable), warning instead
  of informing when a bundle failed or is a repeat.
- Mark a bundle from a healthy machine as a **known-good baseline**, and filter to
  baselines only.
- Retention: optionally delete unpacked files older than N days. The history,
  notes and ticket references are kept; only the files on disk go. Baselines are
  never removed, and cleanup refuses to touch anything outside the extract folder.

## Settings

Stored as JSON at `%AppData%\DiagFileMonitor\settings.json`. The dashboard edits
the common ones; the rest can be edited by hand:

| Setting | Meaning |
| --- | --- |
| `WatchFolders` | Folders to monitor |
| `FileExtensions` | Which extensions count as diagnostic bundles |
| `ExtractRootPath` | Where bundles are unpacked |
| `DatabasePath` | SQLite database location |
| `RepeatWindowDays` | How close together two bundles count as a repeat (default 7) |
| `NotifyOnArrival` | Show a notification-area balloon per arrival |
| `ExtractRetentionDays` | Delete unpacked files older than this; `0` keeps everything |
| `Alerts` | Burst threshold and window, slow-step factor, machinelog line pattern |
| `Zoho` | Data-centre hosts, org id, OAuth client id/secret/refresh token, department and contact ids |
| `Email` | SMTP host, port, SSL, credentials, from and recipient addresses |

Upgrading is safe: `DatabaseInitializer` adds any columns a newer build expects,
so an existing database keeps its history rather than having to be deleted.

## Building and running

Needs Windows and the .NET 8 SDK:

```
cd DiagFileMonitor
dotnet build
dotnet run --project src\DiagFileMonitor.App
dotnet test
```

## Setting up Zoho Desk

1. In the Zoho API console create a **Self Client** and note the client id and secret.
2. Generate a code with the ticket scopes (`Desk.tickets.CREATE`, `Desk.tickets.UPDATE`,
   `Desk.tickets.READ`, `Desk.basic.READ`) and exchange it for a **refresh token**.
3. Find your **org id** in Zoho Desk under Setup.
4. Put those in **Alerts and Zoho...**, set both hostnames to match your data centre
   (`.com`, `.com.au`, `.eu`, ...), and press **Test Zoho**.
5. Zoho normally requires a contact on a new ticket. Set a default contact id if ticket
   creation is rejected.

Credentials are stored in `settings.json` in plain text under your AppData folder. Anyone who
can read your Windows profile can read them. If that is not acceptable, keep the app's
Zoho account scoped to only what it needs.

**Note on verification.** The Core library and its 92 tests were built and run
during development, and the ViewModels were type-checked against the real WPF and
WinForms reference assemblies. The WPF app itself (XAML compilation, and the app
actually running) has **not** been built or launched, because that requires
Windows. Give it a build and a run before relying on it — expect to shake out the
odd layout detail that only shows up on screen.

Two further gaps worth knowing about:

- **The Zoho calls have never run against a real Zoho account.** They are written to the
  documented Desk API shape and tested against a fake HTTP layer (URLs, headers, OAuth
  refresh, token expiry, error handling), but the first real call may still need adjusting.
  Use **Test Zoho** before trusting it.
- **The machinelog parser has not seen a real machine log.** The default line pattern expects
  `2026-09-01 08:00:00 START Preheat`. If your machines write something else, the analysis
  will report no timed steps until `MachineLogPattern` is set to match.

## Trying it out

`samples/sample-diagnostic/` has a `machine.xml` plus placeholder
`changelog.txt`, `machinelog.txt` and `errorlog.txt`. Zip that folder's
*contents* (not the folder itself) and drop the zip into a watched folder:

```powershell
Compress-Archive -Path samples\sample-diagnostic\* -DestinationPath test-diagnostic.zip
```

## Roadmap: the "analyse the diag file" feature

Not built yet, but the groundwork is in place:

- Each bundle's `changelog.txt`, `machinelog.txt` and `errorlog.txt` are already
  indexed by full path, so an analysis pass can go straight to them.
- Bundles can already be marked as known-good **baselines** — that is the
  reference set to compare a faulty machine against.
- For machinelog step timings: parse each line's timestamp and step name, compute
  per-step durations, and store them (e.g. a `MachineLogStep` table: diagnostic
  file id, step name, start/end/duration). Benchmarking is then "compare this
  bundle's per-step durations against the median/range from baseline bundles of
  the same machine type", flagging steps that are outliers.
- `errorlog.txt` / `changelog.txt` comparison against normal is likely a line diff
  or pattern match against the baseline set, surfaced as a ranked list of
  anomalies rather than a raw diff.
