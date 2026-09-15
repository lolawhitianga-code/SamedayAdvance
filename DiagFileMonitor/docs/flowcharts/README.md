# Operation flowcharts

Self-contained HTML operation guides for six machines. Open any of them in a browser.

| File | Machine | Axes |
|---|---|---|
| `tornado_m500_spida.html` | Tornado M500 automated linear saw | 7 |
| `raked_wall_extruder_spida.html` | Raked Wall Extruder | 6 |
| `wall_extruder_spida.html` | Wall Extruder nailing machine | 2 |
| `m600_spida.html` | M600 timber saw | 2 |
| `rapidstop_spida.html` | Rapid Stop reference stock saw | single trolley |
| `kufo_snip_saw_spida.html` | Kufo Snip Saw | manual, no servos |

**These carry no step numbers.** The `WallExtruderStep`, `InfeedStep`, `TornadoStep` and similar
counters in `MachineLog.txt` are a separate numbering that has not been tied to these stages —
a real M20421 export runs `Tornado` steps 110, 200, 1000, 1001, 1010, 1012, 1018, 1020 and
`InfeedStep` 800, 900, 950, 1000, none of which line up with the guide's `STEP // 01`..`04`.
Do not read the two as the same thing.

## What has been taken into the app

From the Tornado guide, into `Knowledge/TornadoKnowledge.cs`:

- **The seven homed axes**, paired with the tags a real M20421 export actually uses. The
  geometry ones carry their letter in the tag — `SawRotation(R)`, `SawOffset(Y)`,
  `SawHeight(Z)` — which makes those solid. `InBelt(X1)` and `OutBelt(X2)` match the `XInAxis`
  and `XOutAxis` in the machine config. The **pusher** is the one still not tied to a tag.
- **The cabinet clear rule** and its troubleshooting note: nothing loads after Start Board is
  usually sawdust on a cabinet eye sensor giving a false detection. The infeed rollers run
  *backwards* to eject until it clears, which is why it looks like the machine is doing nothing.
- **The 520mm rule** — members below it drop to the waste conveyor below the cabinet rather than
  going out on the outfeed rollers.

From the raked extruder guide: it lists **Trolley Heart** as one of the six homed axes, which
supports reading the log's `TrolleyHeight` as the same axis.

The rest — the M600, Rapid Stop, Kufo Snip Saw and Wall Extruder guides — is reference only.
Nothing has been written into the knowledge base for those machines yet.
