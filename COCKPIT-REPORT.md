# COCKPIT-REPORT — Mustang GT cockpit pass (OpenWheelRacer)

Hermes, 2026-11-13. Latest task: round 3 brief
`/home/agent-exchange/for-hermes/2026-11-13-1915-from-claude-cockpit-round3.md`.
Workbench only: `/home/agent-exchange/projects/OpenWheelRacer` (canonical
project untouched). History: round 1 built the interior and first cockpit
(`cc50380`, `74d1953`); round 2 fixed the torus wheel, exterior silhouette,
hood and chase framing (`d132ba6`, Muse checkpoint `834dcc1`). The round-1
`cockpit-1` capture story in older revisions of this file is superseded —
the capture watcher works; rounds 2–3 produced 20+ built sets.

## Round-3 commits (in order, one per capture pass)

| Commit | What | Capture-pass rule |
|---|---|---|
| `6431740` | round-3 impl: readout removed, new eye/pitch/FOV, spread dash set, slim pillars, pop-up screen | made after r3-1 review |
| `43a2a0d` | round-3b: flush well screen, contrast-scaled pods/vents, stacked shifter, eye 1.02→1.06, pitch 24→26 | made after r3-2 review |
| `024d099` | round-3c: dash set grown for readability, wheel rim tube 22→29 mm + lightened | made after r3-2 review, before r3-3 |
| `c0f57d7` | round-3d: self-lit instruments (lit UI panel, blue LCD cluster, lit red needles) | made after r3-3 review, before r3-4 |
| `0db3eb0` | r3-4 capture artifacts | commit after r3-4 pass |

All four builds: `[OWR-BUILD] Succeeded scenes=2 errors=0`.

## What changed in round 3

**Fault 1 — the "N 0" plaque: removed entirely.**
`CockpitView.cs` rewritten with no readout of any kind: no `VirtualGear`,
`GearLabel`, `DashText`, no projected plate. Grep-verified zero references
across scripts and tests. Tests (`CockpitDashPlayTests`,
`CockpitViewPlayTests`) updated to assert absence.

**Fault 2 — the dash is now in frame.**
- Camera: eye `(-0.10, 1.06, -0.55)` (was `(-0.10, 0.99, -0.55)`), pitch down
  **26°** (was 24° at r3 start, 8° at round-2 end), hFOV **90°** (was 80°).
  Horizon lands at ~26% frame height; sky, road, hood, dash cap, console and
  shifter stack in natural order.
- Wheel: centre dropped to y 0.68 so the eye looks *through the rim opening*
  at the pods/screen instead of over an arc that barred them; hub slimmed to
  8.8 cm; rim tube thickened 22→29 mm and lightened (0.15→0.22 grey) so the
  lower arc reads against the dark dash front.
- Dash set spread for the new sight lines: pods x −0.32/+0.12, cluster LCD
  x −0.13, vents x −0.04/0.09/0.22, screen centred x 0.02.
- Touchscreen: a tilted pop-up proved geometrically impossible under the
  0.641 deck rule at this eye (the cap front edge hides anything below ~0.73
  at 0.85 m); it is now a **flush black well with a lit UI plate floating
  ~3 mm proud** (top y 0.6378, inside the rule).
- A-pillars slimmed from 50 mm slabs to 32×38 mm posts.
- Contrast/scale (r3-3) then self-lit materials (r3-4): the sky rig leaves
  the cabin in shade — black dials on the grey cap measured a 0.94 luminance
  ratio at the 60° graze — so powered instruments carry their own light:
  `LitPanelMat` (touchscreen UI, pale blue-white), `LcdMat` (cluster, blue),
  `NeedleMat` (lit red needles). Chrome bezels/rings/vent bars stay
  emissive-free.
- Shifter: boot + 84 mm black knob + chrome cap dot, topping y 0.6405, inside
  the deck rule.

## Per-criterion self-verdict (from r3-4 `frame_01_cockpit`, pixels + crops)

| Criterion (brief) | Verdict | Evidence |
|---|---|---|
| Dash top/face visible | PASS | cap band renders at predicted rows (luminance profile) |
| Twin gauge pods visible | PASS | black discs + chrome rings at predicted px (column dips at 557/723), red needles seen as bright red marks in 2.5× crop review |
| Three centre rounds visible | PASS with caveat | dark discs + chrome cross bars (bars sampled 255,255,255); at the 60° graze they read as recesses with bright bars, not textbook round vents |
| Touchscreen visible | PASS | black well + lit pale UI plate; hot-pixel map shows a bright block at the screen's predicted position |
| Shifter visible | PASS | knob ring + chrome cap at lower centre (~74% frame height) |
| No plaque readout | PASS | readout code deleted; vision pass found no text plate |
| Hood sloped, not a slab | PASS (round-2 geometry kept) | road and horizon visible ahead of the cap; no hood changes were needed |
| Pillars realistic width | PASS | 32 mm posts; earlier r3 vision: "thin posts" |
| Torus rim + black hub kept | PASS | rim torus whole (two vision passes mis-read its dark lower arc as "open"; pixel profile shows the closed ring) |
| Nothing interior above body line | PASS | all interior tops ≤ 0.6405 < 0.641; deck test asserts < 0.641 |
| Chase framing unregressed | PASS | frames 02–04 composition identical across r3-1…r3-4; opponent color clusters (blue `28509A`, red `D44E47`) unchanged first-to-last set |
| Separate-vertices rule / no collapsed normals | PASS | no winding code touched; four clean Unity builds; chase shading correct |
| Opponents render in color | PASS | saturated blue/red clusters present in r3-4 chase pack |

## Honest limitations

- **Unity test suite was not run.** No Unity Editor/assemblies are reachable
  from this account. Gate: Roslyn syntax compile of `Assets/Scripts` +
  `Assets/Tests/PlayMode` (clean apart from pre-existing reference-assembly
  noise in `RacePhoto.cs`) plus the p620 watcher's real Unity **build**
  (Succeeded, errors=0, all four sets). Run the PlayMode tests in the Editor
  before merging.
- The vision model repeatedly mis-described small parts (called the closed
  rim "open", missed parts that pixel sampling found on-model). Every verdict
  above is backed by pixel samples, column-luminance profiles and hot-pixel
  maps at modelled positions, with vision prose used as a secondary read.
- The vent rounds are the weakest item at this camera angle. If Muse wants
  textbook-visible rounds, the lever is a lower/less-pitched eye — which
  re-breaks the shifter/hood composition. Left as-is per the STOP rule.
- Iterations used: 4 of 4 (r3-1…r3-4). No fifth set was made.

## Deliberately left undone

- A readout rendered on the touchscreen surface (allowed, not required —
  "no readout visible at all" was the brief's simplest acceptable outcome and
  is what shipped).
- Anything round 2 already fixed: silhouette, hood, chase framing, wheel.
