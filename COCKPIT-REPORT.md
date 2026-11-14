# COCKPIT-REPORT — Mustang GT cockpit pass (OpenWheelRacer)

Hermes, 2026-11-14. Latest task: round 4 brief
`/home/agent-exchange/for-hermes/2026-11-13-2012-from-claude-cockpit-round4.md`.
Workbench only: `/home/agent-exchange/projects/OpenWheelRacer` (canonical
project untouched). History: round 1 built the interior and first cockpit
(`cc50380`, `74d1953`); round 2 fixed the torus wheel, exterior silhouette,
hood and chase framing (`d132ba6`, `834dcc1`); round 3 removed the "N 0"
plaque and moved the camera into the dash (see Round-3 section below).

## Round 4 (this pass) — "make the cockpit read"

Brief demand: the dashboard must be readable at a glance — twin pods with
visible needles, three separate centre vent rounds, a clearly lit
touchscreen with UI, a shifter that reads 3D, wheel rim clear of the
cluster, hood nose sloping downhill, A-pillars visible — max 4 capture sets.

### Round-4 commits (in order; every capture pass committed)

| Commit | What |
|---|---|
| `30afa61` | round-4 dash set: white dial faces + lit needles, chrome-ringed vents, big UI plate, shifter gate plate; camera up/back (`EyeLocal (-0.10, 1.09, -0.57)`); hood nose slope (front crowns 0.398→0.380, 0.452→0.438, 0.520→0.512) |
| `7736ee2` | **capture set r4-1** (build OK, 0 errors) |
| `c41c149` | r4-2 prep: `LightTrimMat` (bright matte silver) replaces chrome — full-metal chrome renders near-black with no reflection probe; pods into the rim opening; shifter raised behind cap lid cut back to z −0.26 |
| `42b6093` | **capture set r4-2** (build OK, 0 errors) |
| `bf14fe0` | r4-3 prep: pods beside the rim (x −0.44/+0.225, whole dials, hand clear, ticks + 12-o'clock pip); vents spread right of pod (x 0.378/0.481/0.584); shifter wedding-cake stack |
| `84c143c` | **capture set r4-3** (build OK, 0 errors) |
| `39f6c95` | r4-4 prep: **needle zero-volume bug fixed** (see below); shifter alternating-contrast stack (dark gate, bright boot, dark stem/knob, eccentric bright cap) |
| `2d7f3ca` | **capture set r4-4** (build OK, 0 errors) |

All four builds: `[OWR-BUILD] Succeeded scenes=2 sizeMB=90 errors=0`.

### The bug round 4 was actually waiting on

r4-1..r4-3 renders kept failing "needles visible" and "shifter reads 3D".
Audit of every `AddPartBox` call found the needle call passed its
arguments in `(pos, euler, size)` order — the real signature is
`(pos, size, euler)` — so the needle's "size" was `(0, 33, 0)`: zero
volume. **The needles had never rendered in any round.** Fixed in
`39f6c95`; only `GaugeNeedle` was affected (scripted audit of all calls).

The shifter was a different fault: `Round()` builds an **uncapped** tube,
and the cap disc was the same material as the plate under it, so the
stack rendered as one flat bright plate with a black ring. Cure: alternate
light/dark at every layer with an eccentric 17 mm cap dot; deck-rule
height (≤ 0.641) means the 3D read comes from contrast steps and the
parallax crescent between layers, not from height alone.

### Materials added

- `OwrDialFace` — emissive warm-white dial faces (sky rig leaves the cabin
  in shade; unlit dials measured a 0.94 luminance ratio against the cap).
- `OwrUiSlot` — near-black matte recesses (screen well, shifter gate).
- `OwrLightTrim` — bright matte silver with slight emission; reads as
  metal in the shaded cabin where chrome (`_metallic`=1) goes black.
- Screen `OwrLcd` blue-glow + dark UI slot tiles; `OwrNeedle` lit red.

### Final self-verdict — r4-4, 9-item acceptance list, all PASS

Vision pass on `captures/r4-4/frame_01_cockpit.png` (overall verdict:
**PASS, no failing items**): full-frame; wheel rim visible with cluster
readable beside it; hood downhill to its far edge; **two white dials each
with a red needle from a black hub** (crop: needles at ~10–11 and 1–2
o'clock, tick dots visible); three separate silver vent rings; lit white
touchscreen with dark UI blocks; shifter "raised knob with visible height
and side walls — bright base, dark knob, bright top dot" (crop-verified);
A-pillars as two vertical posts; pods/rounds/screen instantly distinct.

Crop-level caveat kept honest: each dial's inner-lower arc is grazed by
the wheel's 3/9-o'clock grip at straight-ahead steering. The rim tube
itself clears both dials and needles are unmistakable; swinging the wheel
moves the grips, so at full lock the inner arcs can be clipped. Left as-is
(capture budget spent; brief judged at the capture pose).

### Round-4 limitations

- Unity PlayMode tests still not run from this account; gate is Roslyn
  compile + four clean p620 watcher builds (errors=0). Run the suite in
  the Editor before merging.
- Iterations used: **4 of 4** (r4-1…r4-4). STOP rule respected.
- `captures/r4-*/crop_*.png` / `small.png` are my analysis aids, not
  deliverables.

## Round-3 archive (condensed from previous revision)

- "N 0" plaque readout deleted outright (`CockpitView.cs` rewritten; tests
  assert absence).
- Camera into the dash: eye y 0.99→1.06, pitch 8→26°, hFOV 80→90°; wheel
  centre y 0.68 so sight lines pass through the rim opening; flush screen
  well (tilted pop-up impossible under the 0.641 deck rule); pillars
  50→32 mm.
- Self-lit instrument materials introduced (`LitPanelMat`, `LcdMat`,
  `NeedleMat`) because the sky rig shades the cabin.
- All interior tops ≤ 0.6405 < 0.641; separate-vertices rule untouched;
  chase frames 02–04 unregressed; opponents render in color.
- Round-3 commits: `6431740`, `43a2a0d`, `024d099`, `c0f57d7`, `0db3eb0`;
  four builds, errors=0; STOP at 4/4 sets.

## Deliberately left undone

- A readout rendered on the touchscreen surface (allowed, not required).
- Anything round 2 already fixed: silhouette, chase framing.
- Grip-arc dial occlusion at full steering lock (see caveat; needs a
  wheel/hand geometry round, not a dash round).
