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

**Note on verification.** The Core library and its 92 tests were built and run
during development, and the ViewModels were type-checked against the real WPF and
WinForms reference assemblies. The WPF app itself (XAML compilation, and the app
actually running) has **not** been built or launched, because that requires
Windows. Give it a build and a run before relying on it — expect to shake out the
odd layout detail that only shows up on screen.

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
