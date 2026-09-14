# Raked Wall Extruder (RakingWallExtruderV3DG) — Everything Known So Far

This is a working knowledge dump for diagnosing this machine from `.szip` support exports. It covers how the machine physically works, how that shows up in the logs, and every known issue/fault signature confirmed so far. Some parts are marked **unconfirmed** — treat those as things to verify against a real log, not settled fact.

---

## 1. Identity

- **Model:** RakingWallExtruderV3DG
- **Known serials in the field:** M19820, M20822, M20716
- Shares a lot of its control logic with the standard (non-raked) Wall Extruder — the tag/step-number scheme in the logs (`WallExtruderStep`, `ClsWallExtruderPLC`, etc.) is the same family, with the raked-specific parts layered on top (floating head laser check, rake-angle positioning).

---

## 2. Physical layout — 6 axes

| Axis | Role |
|---|---|
| Fixed Side Gripper | Holds fixed-side plate, stays put |
| Floating Side Gripper | Holds floating-side plate, moves across for panel width |
| Floating Side Head (a.k.a. "trolley head") | Positions vertically for each stud's height, follows the rake angle progression |
| Fixed Side Ejector | Pushes finished panel out, fixed side |
| Floating Side Ejector | Pushes finished panel out, floating side |
| Trolley Heart (`TrolleyHeight` in the log) | Moves the floating side trolley/gripper across to the panel's starting height, holds it there for the whole panel, then moves higher to let the panel eject. Eject movement varies with panel height. **Confirmed.** One move at the start and one at the eject is normal — a move part way through a panel is not. |

Also: 4 nail guns, dual ejectors, two THNTD (two-hand no-tie-down) safety buttons that must both be pressed/held for any dangerous movement.

**Node mapping used in MachineLog.txt** (from the existing diagnostic playbook, same family): Node0 = FixedSidePuller, Node1 = FloatingSidePuller, Node2/Node3 = the two eject servos. **This mapping was confirmed on the standard Wall Extruder — worth double-checking it lines up exactly on a raked unit, since the raked machine's op-guide names the axes slightly differently (see table above).**

---

## 3. Full operational sequence

### Startup
1. **Home all 6 servos** — operator commands home, all 6 axes reference, controller confirms all ready.

### Job setup
2. Operator opens job file, selects panel. Program reads stud positions, heights, rake progression, nail requirements.
3. Operator presses **Start Panel** — both grippers move to 400mm and wait (not yet ready for plates — floating head positions first).

### Floating head setup (raked-specific)
4. Operator taps both THNTD buttons — **floating head laser sensor scans the travel path for obstructions** before moving.
   - **Laser clear** → floating head auto-moves to position for the first stud height (based on rake angle + stud height data).
   - **Blockage detected** → on-screen warning, operator must clear the obstruction and tap THNTD again to retry.
5. Operator holds both THNTD — grippers pull in from 400mm to zero position (only now ready to receive plates).

### Plate loading
6. Screen prompts "Load Plates" — operator loads top and bottom plates onto open gripper jaws.
7. Operator holds THNTD — gripper jaws clamp top and bottom plates simultaneously.
   - **Both gripper sensors detect timber** → locked, won't release until ejection.
   - **One or both miss** → that gripper re-opens, operator must hold THNTD again to retry.

### Phase 1 — studs within first 400mm (gripper zone)
*Horizontal clamp only — no plate clamps, no top stud clamps.*
- Nail-mark check at each stud position (see section 5 below) — nail if marked, auto-skip if not.
8. Screen: Load Stud — grippers auto-advance, floating head repositions, stud pins rise if clear.
9. Operator holds THNTD ≥75ms — horizontal clamp engages.
10. Operator adjusts stud position, nails any nogs by hand.
11. Operator taps THNTD — side clamp engages, nail guns fire (count depends on timber size). Horizontal clamp back-down sensor must confirm before grippers can advance.
12. All clamps release, stud pins retract, grippers auto-advance to next stud, floating head repositions.
- Loops back to step 8 while still <400mm.

### Phase 2 transition — first stud past 400mm
13. Operator holds THNTD — plate clamps (top + bottom) descend. **Dangerous movement — hands must be clear.**
    - **Detect timber** → lock down, stud pins rise, full Phase 2 mode active (plate clamps + top stud clamps + horizontal clamps + stud pins).
    - **Not detected** → that clamp lifts back up, operator holds THNTD again to retry.

### Phase 2 — studs past 400mm (full clamp mode, "same as Wall Extruder")
- Nail-mark check again at each position.
14. Screen: Load Stud (plate clamps locked, stud pins up, floating head still repositions per stud).
15. Operator holds THNTD ≥75ms — top stud clamp + horizontal clamp engage, plate clamps re-descend (detect → lock; not detected → lift 6mm and continue anyway).
16. Operator adjusts stud, nails nogs by hand.
17. Operator taps THNTD — plate clamps descend one final time (detect → fire associated guns; not detected → **only that side's guns abort, the rest of the process always continues**). Nail count by timber width: 90mm→2, 140mm→3, 190mm→4 nails; top gun steps up for 3–4.
18. All clamps release, plate clamps lift 6mm only (not fully open), grippers stay locked throughout.
- Loops back to step 14 if more studs, else panel done.

### Ejection (auto, after last stud fired)
19. Plate clamps open fully (not just the 6mm lift used between studs).
20. Grippers (still holding the plates) pull the completed nailed frame to the far end.
21. Floating side (trolley head) gripper releases, moves sideways 50mm clear.
22. Fixed side gripper releases, moves sideways 50mm clear.
23. Both ejectors advance from the back, touch the last stud nailed, push together — panel ejected.
24. Both grippers return to 400mm, ready for next panel.
25. **Start Next Panel = TRUE** → auto-loops back to step 3. **= FALSE** → waits for operator to select next panel and press Start Panel.

---

## 4. Nail mark generation logic

This determines whether a given stud position gets nailed or auto-skipped (steps 8 and 14 above depend on this):

1. **Qualifying members:** at job load, any member touching either plate where `Process = TRUE` and `Nail To = TRUE`.
2. **UseDWAssemblies filter:**
   - Machine has a major sub → all qualifying members get nail marks (stop & nail); no mark → auto skip.
   - No major sub, `UseDWAssemblies = TRUE` → same as above.
   - No major sub, `UseDWAssemblies = FALSE` → nail marks are **not** generated for all members above/below a Header or Sill, plus however many adjacent studs are set in **"Assembly Stud Inclusion Count"**.

If a panel looks like it's missing studs that should have been nailed, check this setting and the member's `Process`/`Nail To` flags before assuming it's a fault.

---

## 5. Key rules (documented on the machine's own op-guide)

- **Gripper movement rule:** grippers only advance when ALL conditions are met.
  - Phase 1 (<400mm): stud pins DOWN sensor + horizontal clamp back-down sensor.
  - Phase 2 (>400mm): stud pins DOWN sensor + top stud clamp released sensor + horizontal clamp back-down sensor.
  - **Any condition dropping out mid-travel stops the grippers immediately** — if a panel stalls mid-move, check these three/two sensors first.
- **Firing safety rule:** each plate clamp sensor only gates its own side's guns. If a clamp isn't detecting timber at the moment of firing, only that side's guns abort — the process always continues regardless (it doesn't halt the whole panel).
- **E-Stop — unconfirmed, needs checking:** air drops to most of the machine on E-Stop, grippers may release. **Exactly which circuits keep or lose power/air on E-Stop has not been confirmed yet.** Don't assume anything here when a fault happens right after an E-Stop — check what actually lost air/power before concluding cause.

---

## 6. Sensors

### The four product-present sensors
Same architecture as the standard Wall Extruder / Spida Saw family:

| Tag | Location | Side |
|---|---|---|
| `GripperProductSensor` | one per gripper | Fixed / Floating |
| `PlatePresentSwitch` | one per plate clamp (legacy name — it's a timber/product sensor, not just a switch) | Fixed = `192.168.250.1-4.2`, Floating = `192.168.250.1-4.4` |

- `PlatePresentSwitch` only goes active once plate support is raised and grippers have moved back more than ~300mm.
- **Rule:** all 4 sensors must read 0 (off) before the machine will home or start setup.

### Normal PlatePresentSwitch behaviour
Both 4.2 and 4.4 drop to 0 **together**, tied to a real output change (`IO-PlateClampLift10mm` going off, logged with `Other, Clamping, Waiting For Plate Clamp Down`). They return to 1 together once the clamp physically re-engages.

### PlatePresentSwitch fault signature
- Only **one** side drops (seen so far: fixed side, 4.2) — the other stays steady.
- No matching clamp output change nearby — nothing physically moved.
- Self-recovers in ~1–2 seconds.
- Triggers `Other, PlatePresent, Lost product, revert and try again...`, usually during the fire step rather than the normal clamp-release window.

**How to tell real vs glitch, fast:** do both sides change at the exact same timestamp, and is there a clamp output change nearby? Both yes = normal. Either no = sensor glitch, not a real lost/moved product.

**Note on the one confirmed occurrence of this exact fault pattern:** it was seen on M21036, a **Spida Saw** at Waihi Mitre 10 (10 Aug 2026, panel I-6, ~10:25:38) — the same sensor architecture, not the Raked Wall Extruder itself. It's included here because the same sensor design and fault pattern applies to this machine family; if it turns up on a RakingWallExtruder log, it'll look the same.

**Caveat:** a `PlatePresentSwitch` transition back to 0 can occasionally just not get logged (a known logging gap). Don't call a sensor "stuck" from a missing log line alone — only when there's also a real symptom (jam, refusal to proceed, an actual fault message).

### Background noise — not worth flagging on their own
- CIP driver comms timeouts.
- "Clamps Air supply pressure is low" right at startup or after an E-Stop reset (only flag if it reappears well after that).
- Node0/Node1 arriving ~15 seconds ahead of Node2/Node3 during presetup — normal sequencing.

---

## 7. Known issues log

- **Overcurrent / E-Stop faults on synchronised axis groups** — reported on M19820, M20822, M20716. Cause not yet pinned down; worth checking servo current logs and whether it clusters around a particular axis pairing.
- **Product sensor interlock failures preventing homing** — same three serials. Ties back to the "all 4 product sensors must read 0 before homing" rule above; if homing won't start, check all four sensor states first.
- **Ejection system software bug (confirmed, bug report filed):** the faulting floating-side node does not receive its disable command mid-eject. This is a genuine software gap in the ejection safety sequence, not a sensor or wiring issue — flag any ejection-stage fault against this known bug before looking elsewhere.
- **Follow-up open:** further investigation into ejection timing, and an outfeed conveyor proposal is on the table as a result of the ejection issue.

---

## 8. Fault messages seen in this machine family (verbatim, from `Other` category)

- `"Cannot Move Towards Operator While Grippers Are Down - Check Panel And Restart"`
- `"Floating Side Safety Bar Pressed - Press Estop Reset to Continue"`
- `"Lost product, revert and try again..."`
- `"Servo Not Setup"` (resolves in under 0.2s without properly faulting — can leave the machine silently waiting with no operator-visible sign)

A fault that repeats identically across 2+ restart attempts at the same step number = genuine hardware/sensor problem. A one-off = more likely a transient, don't over-call it.

---

## 9. Questions worth asking the customer, given what's known

- Has this fault started recently, or has it always been there?
- Does it happen on every panel, or only some — and if only some, is there a pattern (panel size, rake angle, timber size)?
- Has anyone been near the sensor/cable/axis in question recently (cleaning, maintenance, a knock)?
- Did the fault happen right after an E-Stop? (Given the unconfirmed E-Stop circuit behaviour, this is worth nailing down every time.)
- For an ejection-stage fault specifically: does it match the known floating-side node disable bug, or does it look different (worth flagging as a new issue if so)?

---

## Still to confirm / gaps

- Whether the Node0–Node3 mapping from the standard Wall Extruder playbook lines up exactly with this machine's own axis names, or whether raked units log them differently.
- Which circuits actually keep/lose power and air on E-Stop.
- Whether the overcurrent/E-Stop faults on synchronised axis groups have a common root cause across the three known serials, or are independent per-machine issues.

---

## Checked against a real export (M20716, 27 Jul 2026)

Two support files from M20716 were read through the analyser. What that settled:

- **The six axes are named in MachineLog.txt**, not just numbered:
  `FixedSidePuller`, `FloatingSidePuller`, `FloatingSideHeight`, `TrolleyHeight`,
  `FixedEjectServo`, `FloatingEjectServo`.
- **"Trolley Heart" is `TrolleyHeight`** — confirmed, along with its role: it moves the floating
  side trolley/gripper to the panel's starting height, holds there for the whole panel, then
  moves higher for ejection (distance varying with panel height). It is a separate axis from
  `FloatingSideHeight`, which repositions per stud following the rake progression.
- **The Node0–Node5 mapping is still not tied to the axis names.** The log lists
  `Node0 Status` … `Node5 Status` as one block at power-on and names the axes separately, so
  nothing in this export connects the two. Still open.
- **`WallExtruderStep` numbers do not match the 1–25 operator sequence above.** The real export
  ran `0 → 10 → 300 → 302 → 0`. The two numberings must not be read as the same thing.
- **The homing interlock rule is confirmed and now checked automatically.** Both
  `GripperProductSensor` inputs and both `PlatePresentSwitch` inputs must read 0 before the
  machine will home. On this export `GripperProductSensor` at `192.168.250.1-4.6` went to 1 at
  the same moment as the first `HomeServos` command, two home commands did nothing, and no axis
  moved for 39 seconds — matching the operator's "trolleys not moving when i hit start panel".
  The report now has a **CAN IT HOME?** section that reads the last known state of every product
  sensor at each home command and names whichever one is blocking. Note that MachineLog.txt
  records *changes* only, so a sensor sitting at 0 all session never appears — the check reports
  those as absent rather than assuming a state.
- **The `PlatePresentSwitch` fault pattern did not appear** in either export — only
  `IO-PlatePresentBypass`. The glitch-vs-real check is implemented and tested, but has still
  only ever been seen on a Spida Saw, not on this machine.

