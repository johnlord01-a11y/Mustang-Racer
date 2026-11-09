# OpenWheelRacer — current plan

Unity 6000.6.6f1. WheelCollider car (tuning ScriptableObject), procedural rounded-oval `Track_Proto`, cockpit + chase cameras, keyboard/gamepad Input System, race session with three AI opponents.

## What exists (code)

**Drive:** `VehicleController`, `VehicleTuning` (`Resources/VehicleTuning_Default` plus `Settings/VehicleTuning_Default`), `VehicleInput`, `WheelVisuals`.
**View:** `ChaseCamera`, `CockpitView`, `CockpitCapture` (`--capture-cockpit /abs/path.png`).
**Race (`Assets/Scripts/Race/`):** `TrackLayout`, `TrackBuilder`, `TrackCheckpoint`, `TrackProgress`, `LapCounter`, `RaceFlow`, `RaceSession`, `RaceBootstrap`, `RaceHud`, `GamePause`, `TimeFormat`, `AiDriver`, `AiField`.

Scenes: `Boot`, `Garage`, `Track_Proto`, `Sandbox`. Default spawn path is `RaceBootstrap` assembling `Track_Proto` (player + 3 opponents + HUD + pause).

## Tests (verified 2026-11-09 at 9b1ccbf: EditMode 82/82, PlayMode 56/56, headless, exit 0)

- **EditMode (82):** layout/progress/laps/flow/time format/cockpit-capture parse, minimap, best-lap store, AI profiles, settings, plus earlier vehicle/input checks.
- **PlayMode (56):** handling, race bootstrap/flow, AI opponents (spawn, a lap, independent input, stuck recover), cockpit, track continuity, wall recovery (head-on, 30°, **10° glance at ~35 m/s**), car body/visibility, car contact. `Ctl3_BoxIntoCar_Default` fixed and passing — the box mover now slides friction-free so it arrives at a valid impact speed. Free-rolling car-into-car contact asserts 15% of impact speed (including Ctl4). No ignores remain.

## Done relative to the old staged plan

Stage 0–1 (foundation, drivetrain) were committed first. Stages 2–6 work landed in the snapshot commit: chase + cockpit, track/laps/HUD/pause, and AI opponents (ahead of the original “no AI until written Stage 6 approval” gate). Parked-car WheelCollider hit response and obsolete Unity 6 find-API warnings in the player build are fixed.

## Still open

1. **Content (old Stage 5 remainder)** — more checkpoints, elevation, road decals, editor track builder. (Placeholder body and minimap shipped.)
2. **Traffic rules** — AI is still pure pursuit; named `TrafficRules` (yielding, corner etiquette) never built.
3. **Play feel** — signed-off M1 checklist (phone-stopwatch laps, 60 fps at 1080p, gamepad unplug, etc.) is still a manual pass, not a test suite.

Done since the finish-plan pass: Ctl3 honest fixture (frictionless box mover, passing), CS0618 sweep (zero `FindObjectsOfType` left), best-lap persistence, `AIDriverProfile` SOs, quality/VSync pause settings, minimap, F1-style body with halo, closed cockpit (windshield below the sight line, cowl, shoulders, headrest; halo and glass exempt from team-colour paint).
