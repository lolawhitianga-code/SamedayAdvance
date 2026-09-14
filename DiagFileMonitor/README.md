# Diagnostic File Monitor

A Windows desktop app (C#, .NET 8, WPF) that watches a folder for incoming
diagnostic zip files, unpacks them, reads `machine.xml`, and stores everything
in a local database. A dashboard lists what's arrived, grouped by serial
number by default (switchable in the UI).

## Projects

- `src/DiagFileMonitor.Core` — folder watching, zip extraction, XML parsing,
  and the SQLite database. No UI dependency, so it's reusable and testable
  on its own.
- `src/DiagFileMonitor.App` — the WPF dashboard.

## How it works

1. `FolderMonitorService` watches the chosen folder with a `FileSystemWatcher`,
   filtered to the extensions you configure (defaults to `.zip`). It waits for
   a file's size to stop changing before touching it, so a zip that's still
   copying in doesn't get picked up half-written.
2. `DiagFileProcessor` unpacks the zip into a per-file folder under
   `%AppData%\DiagFileMonitor\Extracted`, finds `machine.xml` anywhere inside
   it, and pulls out machine type, serial number, customer and version.
   Parsing is tolerant of a few common tag-name variants (e.g. `Serial` or
   `SerialNumber`) since the exact schema wasn't specified — adjust
   `MachineXmlParser.cs` once you have a real sample file.
3. Every extracted file is recorded too (`ExtractedLogFile`), tagged as
   `ChangeLog` / `MachineLog` / `ErrorLog` / `Other` by filename
   (`changelog.txt`, `machinelog.txt`, `errorlog.txt`). This is groundwork for
   the analysis feature below — it means "find me the machinelog.txt for
   diagnostic #123" is already a solved problem.
4. Everything is saved to SQLite (`%AppData%\DiagFileMonitor\diagfiles.db`)
   with the arrival date, via `DiagFileRepository`.
5. The dashboard reads from the same database and groups the grid by
   whichever column you pick (serial number, machine type, customer, arrival
   date, or status).

## Settings

Stored as JSON at `%AppData%\DiagFileMonitor\settings.json` (watch folder,
extension filter, database path). Created with sensible defaults on first
run; edited from the dashboard toolbar after that.

## Building and running

Needs Windows + the .NET 8 SDK (this app wasn't built or run in this session
— there's no Windows/.NET environment available here — so give it a build
before relying on it):

```
cd DiagFileMonitor
dotnet build
dotnet run --project src\DiagFileMonitor.App
```

## Trying it out

`samples/sample-diagnostic/` has a `machine.xml` plus placeholder
`changelog.txt`, `machinelog.txt` and `errorlog.txt`. Zip that folder's
*contents* (not the folder itself) and drop the zip into the watch folder
to see a row appear on the dashboard:

```powershell
Compress-Archive -Path samples\sample-diagnostic\* -DestinationPath test-diagnostic.zip
```

## Roadmap: the "analyse the diag file" feature

Not built yet, but the schema is laid out for it:

- `ExtractedLogFile` already tags each bundle's `changelog.txt`,
  `machinelog.txt` and `errorlog.txt` by full path, so an analysis pass can
  go straight to them without re-extracting anything.
- A natural next step is a `Baseline` concept — one or more diagnostic
  bundles (or hand-picked log files) marked as "known normal" per machine
  type, to diff/compare a reported problem against.
- For machinelog step timings: parse each line's timestamp + step name from
  `machinelog.txt`, compute per-step durations, and store them (e.g. a
  `MachineLogStep` table: diagnostic file id, step name, start/end/duration).
  Benchmarking is then "compare this bundle's per-step durations to the
  median/range from baseline bundles of the same machine type" — flag steps
  that are outliers.
- `errorlog.txt` / `changelog.txt` comparison against "normal" is likely a
  line-diff or keyword/pattern match against the baseline set, surfaced as a
  ranked list of anomalies rather than a raw diff.

None of this is implemented — it's noted here so the data model doesn't need
reshaping when you get to it.
