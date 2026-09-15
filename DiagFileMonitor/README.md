# Diagnostic File Monitor

A Windows desktop app (C#, .NET 8, WPF) for technical support. It watches folders
for incoming diagnostic zip files, unpacks them, reads `machine.xml`, and stores
everything in a local database. The dashboard lists what has arrived, grouped by
serial number by default, with search, case notes and log searching on top.

## Getting it running on your PC (start here)

You need to do this once. After that, the app is just an icon you double-click.

**1. Install .NET 8** (the toolkit that turns the code into a program)

Go to <https://dotnet.microsoft.com/download/dotnet/8.0>, and under **.NET 8.0** find
the **SDK** column. Download the **Windows x64** installer and run it. Click through
the defaults.

**2. Download this code**

On the GitHub page for this repository, click the green **Code** button, then
**Download ZIP**. Save it somewhere sensible like your Documents folder.

**3. Unzip it**

Right-click the downloaded ZIP, choose **Extract All**, and let it finish. You will
end up with a folder containing a `DiagFileMonitor` folder.

**4. Build it**

Open the `DiagFileMonitor` folder and double-click **`build.bat`**.

A black window appears and prints a lot of text. That is normal. The first build
downloads packages and can take several minutes. When it finishes it tells you where
your program is.

If Windows warns about running the file, choose **More info** then **Run anyway** -
that warning appears for any script that was downloaded.

**5. Run it**

Inside `DiagFileMonitor` there is now a `publish` folder. Double-click
**`DiagFileMonitor.exe`** inside it. That is the app.

To make it easy to get back to: right-click `DiagFileMonitor.exe`, choose
**Show more options** then **Send to > Desktop (create shortcut)**, or pin it to your
taskbar.

**When I change the code later**, download the ZIP again, unzip, and double-click
`build.bat` again. Your history, notes and settings are kept separately in your
AppData folder, so they survive a rebuild.

**If the build fails**, scroll up in the black window to the first line with the word
`error` in it, and send me that line.

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

## Building from the command line

`build.bat` wraps this. If you would rather type it yourself, on Windows with the
.NET 8 SDK installed:

```
cd DiagFileMonitor
dotnet build                                  :: compile everything
dotnet test                                   :: run the test suite
dotnet run --project src\DiagFileMonitor.App   :: run without publishing
```

To produce the standalone folder that `build.bat` creates:

```
dotnet publish src\DiagFileMonitor.App\DiagFileMonitor.App.csproj ^
    -c Release -r win-x64 --self-contained true -o publish
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

- **The version number** shows on the splash screen and beside the logo, so a screenshot says
  which build produced it. Bump `<Version>` in `src/DiagFileMonitor.App/DiagFileMonitor.App.csproj`
  when you cut a build for the floor.
- **The logo ships inside the program.** To use a different one, put a `logo.png` next to
  `DiagFileMonitor.exe` and restart - a file on disk always wins over the built-in one.
- **The Zoho calls have never run against a real Zoho account.** They are written to the
  documented Desk API shape and tested against a fake HTTP layer (URLs, headers, OAuth
  refresh, token expiry, error handling), but the first real call may still need adjusting.
  Use **Test Zoho** before trusting it.
- **Burst alerts carry the same analysis as the Analyse button.** There is no separate
  setting for how an alert analyses a bundle, and nothing to configure.

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
