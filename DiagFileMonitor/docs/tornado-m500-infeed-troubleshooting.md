# Tornado M500 — Infeed Troubleshooting Session

---

## ✅ Normal Startup Sequence (Baseline)

1. Power on machine
2. Press **Home Machine** — all 7 servo axes reset
3. Open job file — confirm members visible in Members tab
4. Check Infeed tab — board created, members assigned, stock board listed in stock table
5. Review board selection — machine will suggest stack height (1, 2, or 3 high) based on job
6. If spare material on board — operator can choose to process waste into blocks (pulled from standard offcut table)
7. Press **OK** → load boards onto infeed chain conveyor
8. Press **Start Board** — automated run begins

---

## 🔴 Fault #1 — "Board Not Expected Length" (Post Laser False Read)

### Symptoms
- Gripper eye sensors and entry sensor all detect timber correctly
- Grippers engage and pull timber to fence
- Error: *"Board not expected length"* — reported length is wrong (e.g. 4,719mm when actual board is ~2,000mm)

### Root Cause
Post laser reflecting off the **shiny polished metal infeed deck surface**, picking up a false return and reporting an incorrect board length.

### Temporary Fix
Black tape applied to deck surface to reduce reflection — partially effective but not fully reliable, reflection can still occur intermittently.

### Permanent Fix — TBD
Options to explore:
- Non-reflective paint or matte coating on deck surface
- Rubber or matte matting over deck
- Adjusting post laser angle or sensitivity if configurable
- Check with Spida Machinery — may be a known issue with a recommended fix

### Diagnostic Tip
If reported length doesn't match loaded board and seems random — suspect laser reflection off deck before assuming wrong board was loaded.

---

## 🔴 Fault #2 — "Board Not Expected Size" (Analog Clamp Sensor Out of Tolerance)

### Symptoms
- Board loads and advances normally
- Gripper and entry sensors all trigger correctly
- Pusher adjusts for slight overlength if needed
- First horizontal and vertical clamps engage
- Error: *"Board not expected size"* — measured dimensions outside ~10% tolerance
- Example: Expected 90x45mm, measured 89x39.2mm

### Root Cause
Timber is genuinely undersized (in this case ~13% under on width — just outside tolerance).

### Resolution
- Visually inspect the timber
- If timber is clearly the wrong size → reload correct stock
- If timber looks acceptable and is only slightly out → press **Continue** — machine will process normally
- In this case operator pressed Continue and machine ran full cut sequence without issue ✅

---

## 🔴 Fault #3 — Print Running Off Edge of Timber

### Symptoms
- Cut members come out with ink printing falling off the top edge of the timber face
- Print label partially or fully off the timber surface

### Root Cause
Top printer servo height set too high for the timber thickness being run.

### Resolution
Top printer must be **manually physically adjusted** in height. No software setting — physical adjustment only.

### Printer Height Guide

| Timber Type | Thickness | Printer Position |
|---|---|---|
| New Zealand Timber | Thicker | Higher |
| Australian Timber | Standard | Mid |
| American Timber | Thinner | Lower |
| Sterling Timber | Thinnest | Lowest |

### Key Note for Operators
When switching timber types, always check printer height before running.
- Print riding off top edge → lower the printer
- Print too low on face → raise the printer

---

*Session focus: Infeed issues. Faults captured as they occurred on-site. Troubleshooting guide format TBD.*

---

## Now in the app

`Knowledge/TornadoKnowledge.cs` carries all three faults, the printer height guide and the
startup sequence. Two complaint topics route a Tornado operator's own words — a print complaint
goes to printer height, a board-reject complaint separates the laser error from the clamp error.

Complaint topics are now gated by machine model, so these do not fire on a wall extruder (where
"wrong size" means something else) and the wall extruder's stud and nail gun topics do not fire
on a Tornado. A bundle whose model did not parse still gets every topic — showing too many beats
showing none.

**One bug this uncovered.** The analyser decides a line is a fault by looking for failure
wording — `error`, `fault`, `failed`, `stopped` and so on. *"Board not expected length"* contains
none of them, so the Tornado's real errors were being read as ordinary chatter and dropped
entirely. `not expected`, `unexpected`, `out of tolerance` and `reject` are now recognised, and
separately any fault text written into the knowledge base is matched against the raw log
regardless of wording.

**Still open** (printed in every Tornado report):

- A permanent fix for the deck reflection. Spida have not been asked yet whether it is known.
- Whether the M500's axis names match the M450, whose config lists five (`XInAxis`, `XOutAxis`,
  `YAxis`, `ZAxis`, `RAxis`) where the M500 is described as having seven. No M500 log read yet.
- Whether a "Board not expected size" error ever comes from the clamp sensor drifting rather
  than from genuinely undersized timber.

---

## Fault #4 — Saw motor commanded on but never running (M20421, 10 Sep 2026)

**Operator wrote:** *"saw motor not running"*

**What the log shows**, at the very end of a 100,000-line file:

```
13:08:19.259  OutputChange  IO-SawMotor      Set On                 ← commanded
13:08:19.715  InputChange   SawMotorConfirm  Changed to 0           ← confirm not made
13:08:23.261  Other  ControlYZRPLC  Step Condition, Waiting for Saw Blade Running
13:08:24.797  OutputChange  IO-SawMotor      Set Off                ← gave up after 5.5 s
```

Then nothing for 50 seconds until the file was exported. *"Waiting for Saw Blade Running"* appears
**once** in the whole log — right there.

**Root cause:** the command went out and the motor did not turn. Check the contactor, its
auxiliary contact, the thermal overload, and the confirmation wiring back to the input card.

**What made it certain:** `WasteMotorConfirm` (73 changes) and `NogConveyorConfirm` (19) toggled
normally all session. `SawMotorConfirm` changed **once** in 100,000 lines — to 0, and never back.

### Now checked automatically

`Knowledge/MotorConfirmCheck.cs` pairs every `XConfirm` input with its `IO-X` output by naming
convention — discovered from the log, so a machine with motors we have never seen is still
checked — and reports any command that went out without a confirmation coming back.

Two things keep it honest:

- **Positive evidence only.** When one motor fails the machine aborts the step and drops every
  output at once, so its companions look unconfirmed too. The nog conveyor was withdrawn 5.5 s
  after being asked, never having had a chance to start — that is the abort, not a second broken
  motor. A failure is only reported where the confirmation was seen reading 0, the machine logged
  a matching wait, or the command genuinely stayed on for the full window.
- **A wait line must name its own motor.** *"Waiting for Saw Blade Running"* belongs to the saw,
  not to whatever else switched on in the same millisecond.
