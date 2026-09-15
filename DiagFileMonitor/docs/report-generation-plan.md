# Report Generation — Plan

Status: **phases 0-2 built.** See `reports.md` for what shipped. The conflict analysis below is
what the build was decided on, and the questions in section 6 are still open. Written in response to the two uploaded
documents, `diagnostic-analyser-report-spec.md` and `spida-report-generation-context.md`.

The brief was to plan a report feature using those documents, and to check for
conflicting views rather than assume the documents' methods beat the ones already
working in this app. So this plan leads with the conflict check, then the scope,
then the design.

---

## 1. How I checked the documents

Everything below was tested against real files this project holds, not against memory:

- the two M20716 `.szip` bundles (Carters, RakingWallExtruderV3DG)
- the M21642-1 `Machine.xml` (Grandeur Housing Limited, Apollo)
- the M20421 TornadoM500 exports the analyser has already run on
- the 100,000 line Tornado `MachineLog.txt`
- `RakingWallExtruderV3DG.xml` (UTF-16 machine config)
- the code and the 388 passing tests

Where a document claim could be tested, it was. Where it couldn't, it is marked
unverified rather than accepted.

---

## 2. Conflicts found

### 2.1 Machine identity — keep the existing method

**Documents say:** identify machines from a hard-coded site/serial inventory table
(spec §6).

**This app does:** parses `<Title>` out of `Machine.xml`, reading from both ends so a
comma inside the customer name cannot shift the model field.

```
<Title>Spida SDN, V2.4.0.0, M20716, Carters, RakingWallExtruderV3DG</Title>
              ^version   ^serial  ^customer...  ^model (last field)
```

**Verdict: keep the `Machine.xml` parse. Do not adopt the table as the identity source.**

Reasons, in order of weight:

1. The parse reads what the machine itself reported. The table is a hand-kept list.
2. The table is already wrong or unresolved in several rows *by its own admission* —
   AOR1694 vs AOR1613 is flagged unresolved in the spec but asserted as settled in the
   context doc (§"Site & machine identities"). A source that contradicts itself cannot
   be the identity source.
3. The table is missing machines we hold real exports for:
   - **M20421**, TornadoM500 at **Engineered Truss Systems** — the spec lists M20421 only
     under "not yet mapped to confirmed sites", and lists Tornado M500 as M17311 at
     Carters Wellington Upper Hutt. Both can be true; the point is the table didn't know.
   - **M21642-1**, Apollo, at **Grandeur Housing Limited** — absent entirely.
   - M21461 (BFS Harrisburg) and M20616 (Independant Frame & Trusses) — absent.
4. The customer field proves the parse earns its keep: "Engineered Truss Systems" arrived
   with a trailing comma in the title. A naive split on commas would have mangled it.

**What the table is good for:** enriching what the log can't tell us. `Machine.xml` says
"Carters", not "Carters Cambridge". It has no contact name, no line number, no gun count,
no machine nickname. So:

> The inventory becomes a **lookup keyed on serial**, used only to add site detail,
> contact, and line/asset name to a report header. If the lookup and the log disagree on
> **model or customer**, the log wins and the report says so.

That last part matters — a silent disagreement is how a wrong serial gets quoted to a
customer. Print it.

### 2.2 Serial number conflicts to settle before the table ships

| Serial | Spec doc | Context doc | Our evidence | Action |
|---|---|---|---|---|
| M18121 | "**Saw, not a nailer**" | "Component Nailer M18121 (single-gun)" at Carters Auckland | none | Ask. Two uploaded docs disagree in the same sentence pair. |
| M21036 | Raked Wall Extruder, Waihi Mitre 10 | Raked Wall Extruder | Your earlier raked-extruder knowledge doc called M21036 a **Spida Saw** at Waihi Mitre 10 | Ask. Site agrees, machine type doesn't. |
| AOR1694 / AOR1613 | unresolved, "confirm against the machine's log header" | AOR1694 asserted as settled | none | Ask, or send me a `.szip` from that machine and the log header settles it. |
| M20421 | listed as unmapped | not listed | **TornadoM500, Engineered Truss Systems** (real export) | Use ours. |
| M21642-1 | absent | absent | **Apollo, Grandeur Housing Limited** (real `Machine.xml`) | Add. |
| M20716 | Carters Cambridge, 6.0M Raked Extruder | Carters Cambridge | **Carters, RakingWallExtruderV3DG** (real bundle) | Agrees. Use the table for "Cambridge" and "6.0M". |

None of these block the feature. They block the *table*, which is why the table is an
enrichment layer and not the identity source.

### 2.3 "Raked Extruder log entries are double-written" — not true of MachineLog.txt

**Documents say:** deduplicate or raw counts read 2× actual (spec §7).

**Tested:**

| Log | Lines | Distinct | Consecutive duplicates |
|---|---|---|---|
| M20716 `MachineLog.txt` | 111 | 111 | 0 |
| Tornado `MachineLog.txt` | 100,000 | 100,000 | 0 |

**Verdict: does not apply to `MachineLog.txt`.** It is very likely true of ProdLogV2,
which this app does not read. If a blanket dedupe were applied to `MachineLog.txt` it
would destroy real data — the PLC legitimately writes the same tag/value twice within one
scan, and `HomeInterlockCheck` depends on same-timestamp entries being treated as one
scan, not thrown away.

**Action: do not dedupe `MachineLog.txt`.** If ProdLogV2 support is ever added, apply the
rule there, with the `MemberCut` exception the docs note.

### 2.4 Brand colours don't match the actual logo

| | Documents | Sampled from `logo.png` |
|---|---|---|
| Spida blue | `#009CDE` | **`#00A5E3`** (97% of logo pixels) |
| Grey / navy | `#425563` | **`#415968`** |
| Dark blue | `#0077A8` | `#0084B6` (derived) |

Also, the spec calls `#425563` "grey" in the palette line and "Dark Blue" in the note
three lines later.

**Verdict: keep the sampled values** (already in `App/Theme.xaml`). Reports generated by
the app must match the app they came out of, and both must match the logo sitting next to
them on the page. A report in `#009CDE` beside a logo in `#00A5E3` looks like a mistake.

Happy to switch the whole app to `#009CDE` if that's the official brand-book value — but
then the logo artwork is off-brand, and that's worth knowing either way. **Question for you.**

### 2.5 Self-contained HTML vs CDN fonts and charts

The spec asks for both:

- "self-contained single-file HTML preferred for portability" (§4)
- Zilla Slab / Nunito / IBM Plex Mono via Google Fonts CDN, Chart.js via CDN (§2, §4)

On a factory PC opened from `file://` with no internet — which is the whole point of
self-contained — **none of the CDN assets load.** Fonts silently fall back, charts render
as nothing.

**Verdict: genuinely self-contained.**

- Fonts: embed as base64 WOFF2 subsets, or use a system stack. A three-family webfont set
  is roughly 150–250 kB base64'd, which is acceptable once per report.
- Charts: inline the Chart.js UMD build (~200 kB), or draw with inline SVG. For the two
  reports in scope (see §3) inline SVG is enough and adds nothing to load.
- The spec's `<script src>`-not-`fetch()` note is correct and stays — but with a relative
  local path, not a CDN one.

### 2.6 Toolchain — Python/Node vs this app

The documents describe Python parsers, a Node.js validation harness, and a personal SQLite
file on a laptop. That's a separate, working pipeline.

**Verdict: don't port it in.** This app is C#/.NET 8 with its own SQLite store, its own
parsers, and 388 tests covering them. Report generation here should be a C# formatter that
consumes `DiagnosticAnalysisService` output — the same objects the on-screen report uses.
Two parsers for one log format is how the two of them drift apart.

The Node harness has no equivalent here and doesn't need one: the xunit suite already does
that job, and `tools/check.sh` runs it.

If the goal is for both pipelines to produce the same numbers, the sane path is the reverse
of what the docs suggest — the app exports, the Python side consumes the export.

### 2.7 "If a template is uploaded, its field mappings are authoritative"

Context doc, §Validation discipline.

**Verdict: accept for layout, reject for parsing.** A template's *look* is authoritative —
headings, order, wording. Its *field mappings* are not, when they contradict a parser that
has been checked against real files. This is exactly the rule that would have made us adopt
the double-write dedupe in §2.3 and corrupt the log.

### 2.8 The stale `MachineLog.txt` — real risk, not present in our bundles

The spec warns the real log is under `Support Files\...\Logs\MachineLog.txt`, **not** the
root `SDN\Logs\MachineLog.txt` placeholder.

**Checked:** neither M20716 bundle contains a root-level duplicate. Only `./Logs/MachineLog.txt`.

**But the warning is still worth acting on.** `DiagFileProcessor` matches by filename only,
across all directories, and `DiagnosticAnalysisService` takes `FirstOrDefault`. If a bundle
ever arrives with two `MachineLog.txt` files, which one gets analysed is down to enumeration
order. That's a real latent bug and cheap to fix:

- prefer the path containing `Support Files`
- then prefer the deeper path
- then prefer the larger file
- and if more than one candidate exists, say so in the report

There is already a `M-STALE` test fixture in `SpidaMachineXmlTests.cs`, so the idea has been
part-handled for `Machine.xml`. Extend it to the logs. **This is worth doing regardless of
whether the report feature goes ahead.**

---

## 3. What we can actually build from `.szip` bundles

The spec lists six report types. Sorted by whether this app holds the data:

| # | Report | Needs | Buildable now? |
|---|---|---|---|
| 1 | Machine snapshot (sales) | Speed Score, uptime %, panels/day | **No** — needs ProdLogV2 |
| 2 | Live factory dashboard | live status, cycle time, panel count | **No** — needs a live feed; `.szip` is a point-in-time snapshot |
| 3 | Historic weekly/monthly rollup | daily panel counts, downtime by cause, timber yield | **No** — needs ProdLogV2 / ShiftLog |
| 4 | Fleet comparison (sales) | Speed Score across machines | **No** — needs ProdLogV2 |
| 5 | **Fault benchmarking** (Spida diagnostics) | occurrence-level fault log across machines, newest first | **Yes** — this is what the app already stores |
| 6 | **Mechanical change case** (design team) | fault pattern evidence, downtime estimate, proposed fix | **Mostly** — evidence yes, downtime cost needs an input |

So: **build #5 and #6. Mark #1–#4 as blocked on ProdLogV2 ingest**, which is a separate and
much bigger piece of work (it's a different file format, a different cadence, and it's the
production log rather than the PLC log).

That's not a downgrade of the brief. #5 and #6 are the two marked "Internal use only" —
they're the Spida-facing ones, they're the ones this app's data actually supports, and
they're the ones that feed the design team. The four customer-facing ones need the
production data the docs' Python pipeline already has.

Worth saying plainly: if the aim is the customer-facing four, the fastest route is the
existing Python pipeline, not this app.

### What #5 looks like with our data

Every occurrence across every stored bundle, newest first, per the spec's rule ("list every
individual occurrence in full — never summarise or group by pattern alone"):

- timestamp, serial, site, machine type
- fault source: MC2 F-code, Omron 1S alarm, ErrLog entry, or machine-log fault wording
- the code's meaning and its **confidence label** (Confirmed / Inferred / Unconfirmed)
- ~5 lines of surrounding log context, quoted — not line numbers (their rule, and it's right;
  our excerpts are already filtered so line numbers would be meaningless)
- what the operator wrote in `SupportInfo.txt`
- motor confirm state, drive faults, home interlock — what the analyser already produces

Then pattern analysis after the table, not instead of it.

### What #6 looks like

One machine, one fault pattern, built from #5's occurrences:
evidence table → frequency over time → affected axes/hardware (we have `AxisHardware`,
CLX vs Omron) → the open gaps from `MachineKnowledge` → proposed fix → validation plan.
Downtime cost is a number you type in; we shouldn't invent it.

---

## 4. Design

### 4.1 Shape

```
DiagnosticAnalysisService  ──▶  ReportModel (new, plain data)
                                     │
                    ┌────────────────┴────────────────┐
                    ▼                                 ▼
          SpidaReportFormatter              HtmlReportRenderer (new)
          (existing, plain text)            (self-contained .html)
```

One analysis, two renderers. The HTML renderer never re-parses a log — if a number isn't in
`ReportModel`, it doesn't go on the page.

### 4.2 New files

| File | Job |
|---|---|
| `Core/Reports/ReportModel.cs` | plain data: header, occurrences, patterns, knowledge gaps |
| `Core/Reports/FaultOccurrence.cs` | one row of the #5 table, with confidence label |
| `Core/Reports/FaultBenchmarkBuilder.cs` | query the store, build `ReportModel` for #5 |
| `Core/Reports/ChangeCaseBuilder.cs` | build `ReportModel` for #6 |
| `Core/Reports/HtmlReportRenderer.cs` | `ReportModel` → single self-contained HTML string |
| `Core/Reports/ReportAssets.cs` | embedded CSS, base64 logo, base64 font subsets |
| `Core/Reports/MachineInventory.cs` | serial → site/contact/line, **enrichment only** (§2.1) |
| `App/ReportWindow.xaml` + `ViewModels/ReportViewModel.cs` | pick type, scope, period; save to disk |

Tests go in `tests/DiagFileMonitor.Core.Tests/Reports/`.

### 4.3 Rules from the docs that we adopt as-is

These are good and go straight in:

- real serials everywhere, never Machine A/B/C
- period-filtered headers state the period, never an all-time total dressed up as a week
- every machine in scope appears in every chart and column; missing data shows as "pending",
  not dropped
- separate genuine faults from normal workflow before quoting a fault rate
- weighting choices documented inside the report
- callout boxes for anything needing interpretation
- 97th percentile for axis scaling, not the max
- technician-triggered IO overrides excluded from fault stats
- occurrence-level detail first, pattern analysis second
- quoted log lines with context, not line numbers
- internal reports marked "Internal use only" by default
- confirm the named scope before building; default to what's named

Plus one of ours the docs don't have, which matters more here than any of them:

- **every interpretation carries its confidence label.** Confirmed / Inferred / Unconfirmed
  is already printed in the text report because these get quoted to customers. An HTML
  report that drops the label is more dangerous than the text one, because it looks official.

### 4.4 Things the docs taught us that aren't in the code yet

Worth implementing whether or not the report feature proceeds:

1. **"Homing complete" fires before all axes confirm OK** — Node4/5 can lag 48–52 s and
   should always be flagged. Complements `HomeInterlockCheck`, which currently checks the
   four product sensors but not the post-"complete" axis lag.
2. **Phase 1 vs Phase 2 clamping** — authoritative signal is `PlateSupportUp=1` after
   `IO-PlateSupport` is commanded. Not currently read.
3. **Ejection timing** — last nail fired → Node0 and Node1 both OK at 400 mm.
4. **Plate clamps first used on the 4th stud** of a panel (internal detail, not customer-facing).
5. **Technician IO override exclusion** — needs a way to spot a forced output.

And these confirm things already shipped, which is a good sign for both sides:
PlatePresentSwitch `4.2`/`4.4`; all four product sensors at 0 before homing; Sharepoint
lines irrelevant; Node0/1 ~15 s ahead of Node2/3 is normal; "Servo Not Setup" under 0.2 s is
a silent stall; "Clamps Air supply pressure is low" only at startup/E-stop reset.

---

## 5. Order of work

**Phase 0 — fix first (small, independent of reports)** — done
- log file selection when a bundle has duplicates (§2.8) → `Services/BundleLogs.cs`, which prefers
  the support folder, then the deeper path, then the larger file, and prints the choice it made
- mixed-case filename matching, found while running the real bundles through on Linux: the export
  writes `Machine.xml` and `SupportInfo.txt`, and a filename glob matches case-insensitively on
  Windows but not elsewhere
- a duplicate `x:Key` in `Theme.xaml` that would have thrown on startup, found the same way;
  `tools/check_xaml.py` now catches it

**Phase 1 — report #5, fault benchmarking** — done

**Phase 2 — report #6, mechanical change case** — done

**Phase 3 — inventory enrichment** — done as an enrichment lookup only, with disputed rows
marked rather than picked between. The conflicts in §2.2 are still open questions.

**Still to do**
- "Homing complete" axis lag flag (§4.4.1) and the other three findings in §4.4
- **#1–#4.** They need ProdLogV2 ingest. Separate piece of work, separate plan.

---

## 6. What I need from you

1. **Brand blue** — `#009CDE` (docs) or `#00A5E3` (the actual logo)? Currently the app uses
   the logo's.
2. **M18121** — saw or Component Nailer? The two docs disagree.
3. **M21036** — Raked Wall Extruder (both docs) or Spida Saw (your earlier knowledge doc)?
4. **AOR1694 or AOR1613?** A `.szip` from that machine settles it without anyone guessing.
5. **The six sample HTML builds** (`1-sales-snapshot.html` … `6-design-team-case.html`) and
   `data-collection-checklist.md` are referenced in the spec but weren't uploaded. Send #5
   and #6 and I'll match the layout exactly.
6. **Confirm #5 and #6 is the right scope**, or tell me the customer-facing ones matter more —
   in which case the honest answer is that they belong in the Python pipeline until this app
   reads ProdLogV2.
