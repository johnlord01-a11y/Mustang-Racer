# COCKPIT-REPORT — Mustang GT cockpit pass (OpenWheelRacer)

Hermes, 2026-11-13. Task: `/home/agent-exchange/for-hermes/2026-11-13-0040-from-muse-cockpit-fix-task.md`.
Workbench: `/home/agent-exchange/projects/OpenWheelRacer` (canonical project untouched).

## Commits

- `cc50380` — Mustang interior: full GT dashboard, twin gauge pods, three vent
  rounds, touchscreen, manual shifter, red seat inserts (`MustangMeshBuilder.cs`)
- `74d1953` — CockpitView: seat the driver in the left-hand Mustang seat, round
  wheel centred (`CockpitView.cs`)

Both authored `Hermes <hermes@p620.local>`.

## What was added

`Assets/Scripts/MustangMeshBuilder.cs` — `BuildInterior()` rebuilt from the F1
donor interior into the S550 layout (all names verified present in harness):

- **Dashboard**: full-width charcoal `Dash` shelf at the base of the windshield
  (top at y 0.74, front edge z 0.37 — behind the glass line), `DashKick` knee
  panel, chrome `DashTrim` band across the front.
- **Twin gauge pods** (driver side, x < 0): `GaugePod_L/R` round pods,
  `GaugeFace_L/R` black faces, red `GaugeNeedle_L/R`, `ClusterScreen` between.
- **Three center rounds** (the S550 signature): `CentreVent1..3` + black
  `CentreVentFace1..3` across the dash top center.
- **Center stack + touchscreen**: `CentreStack`, tilted `TouchScreen` with
  lit `TouchUI` face.
- **Console + manual shifter**: `CentreConsole` tunnel running back between the
  seats; `ShifterBoot`, `ShifterLever`, round `ShifterKnob` + chrome
  `ShifterCap` (knob top y 0.86 — below the sight line). Manual lever only,
  no automatic selector.
- **Seats** (optional item, done): pan/back/headrest both sides with **red
  leather inserts** (`SeatInsert_L/R`, `SeatBackInsert_L/R`) on charcoal —
  John's red/black combo.
- New geometry helpers `AddCylinderZ`/`AddDiskZ` for vertical round parts.
  They follow the twin-winding rule: every doubled triangle goes on **separate
  vertices**, so nothing collapses to black.

`CockpitView.cs` — whole file rewritten:

- **Round 3-spoke wheel** replacing the rectangular F1 wheel: 14-segment dark
  rim (`Wheel_Rim0..13`, 0.37 m diameter), silver spokes L/R/bottom, dark hub
  inside a silver `Wheel_HubRing`, and a **silver-ringed round badge**
  (`Wheel_BadgeRing` + `Wheel_BadgePlate`) — the brief's minimum; no
  galloping-horse silhouette.
- Wheel **rotates with steering input** (`-WheelAngleFor(_car.CurrentSteerAngle)`
  applied as roll, plus its natural dash angle), hands and column follow.
- The old "N 0" readout is gone: the plate now carries a **gear + km/h
  readout** derived from the car's actual speed (`VirtualGear`, 1–8, R/N).
- Wheel/column/hands re-centred to x = -0.35, over the **left** seat.

**Hood and windshield frame**: already part of the Mustang body — they were
invisible because the F1 cockpit camera sat at (0, 1.16, −0.20), centerline and
above a believable eye, inside a donor interior. Now that the driver sits in
the left seat the hood runs away ahead below the sight line (harness: all body
ahead of z 1.2 tops out well below eye height), and `Pillar_L/R` + `Header`
frame the view. The car's own glass stays hidden in cockpit view (b1fa5f9
behavior preserved — harness still finds exactly 1 own-glass).

## Camera change (as required, stated exactly)

- Eye `Vector3(0, 1.16, -0.20)` → `Vector3(-0.22, 0.99, -0.24)` — left seat,
  real seated eye height, back from the seat front.
- Added `EyePitchDown = 8f`: cockpit camera pitches 8° down so dash, wheel
  and shifter compose naturally.
- Added `CockpitFieldOfView = 80f` applied on entering cockpit view and
  restored to the camera's default on exit (chase frames unaffected).
- Chase camera positions: unchanged.

## Verification so far

- Local harness (`/home/hermes/cockpit-verify`, compiles the real builder
  against Unity-semantics stubs — same rig as the black-body task): **all
  checks pass** — 90 mesh parts, 17,808 verts with **zero collapsed normals**
  (no black-part regression), all 34 named interior parts present, sight line
  from the new eye clear of body geometry except the glass you look through,
  dash/hood/gauge/shifter placement as intended.
- Existing `mustang-verify` suite (64 old body parts): still passes.

## Capture

- Label: **`cockpit-1`**, request file
  `/home/agent-exchange/capture-requests/cockpit-1.req` (brief format, line 1
  = workbench path, line 2 = `cockpit-1`).
- **Status: not yet built.** The request has sat unconsumed for 40+ minutes
  although `owr-capture-watcher.sh` (p620-side, pid 516479) is alive — it
  stopped consuming requests around 01:36 (last activity). Format and
  cleanliness were each re-tested (`smoke-baseline` consumed fine from this
  directory at 00:09). A note went to
  `/home/agent-exchange/for-openclaw/2026-11-13-0510-from-hermes-watcher-idle.md`
  asking the p620 side to restart the watcher; the queued request should then
  build by itself. Once `captures/cockpit-1/RESULT.md` shows
  `[OWR-BUILD] Succeeded, errors=0` I will inspect `frame_01_cockpit` against
  the reference photos and iterate if needed.

## Deliberately left undone

- Galloping-horse badge silhouette — used the brief's accepted fallback
  (silver-ringed round badge).
- Door top rails at dash height (brief optional) — not built.
- Pedals (visible in ref-4 but not on the build list) — not built.
- Nothing else outstanding from the brief, pending capture verification.
