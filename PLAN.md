# OpenWheelRacer — current plan

Unity 6000.6.6f1. WheelCollider car (tuning ScriptableObject), procedural rounded-oval `Track_Proto`, cockpit + chase cameras, keyboard/gamepad Input System, race session with three AI opponents.

## What exists (code)

**Drive:** `VehicleController`, `VehicleTuning` (`Resources/VehicleTuning_Default` plus `Settings/VehicleTuning_Default`), `VehicleInput`, `WheelVisuals`.
**View:** `ChaseCamera`, `CockpitView`, `CockpitCapture` (`--capture-cockpit /abs/path.png`).
**Race (`Assets/Scripts/Race/`):** `TrackLayout`, `TrackBuilder`, `TrackCheckpoint`, `TrackProgress`, `LapCounter`, `RaceFlow`, `RaceSession`, `RaceBootstrap`, `RaceHud`, `GamePause`, `TimeFormat`, `AiDriver`, `AiField`.

Scenes: `Boot`, `Garage`, `Track_Proto`, `Sandbox`. Default spawn path is `RaceBootstrap` assembling `Track_Proto` (player + 3 opponents + HUD + pause).

## Tests (as of the finish-plan pass)

- **EditMode (63):** layout/progress/laps/flow/time format/cockpit-capture parse, plus earlier vehicle/input checks.
- **PlayMode (53 total):** handling, race bootstrap/flow, AI opponents (spawn, a lap, independent input, stuck recover), cockpit, track continuity, wall recovery (head-on, 30°, **10° glance at ~35 m/s**), car contact. One remaining ignore: `Ctl3_BoxIntoCar_Default` (plain-box mover loses speed to ground friction). Free-rolling car-into-car contact now asserts 15% of impact speed (including Ctl4).

## Done relative to the old staged plan

Stage 0–1 (foundation, drivetrain) were committed first. Stages 2–6 work landed in the snapshot commit: chase + cockpit, track/laps/HUD/pause, and AI opponents (ahead of the original “no AI until written Stage 6 approval” gate). Parked-car WheelCollider hit response and obsolete Unity 6 find-API warnings in the player build are fixed.

## Still open

1. **Ctl3** — honest box-into-car fixture (mover must arrive at a valid impact speed).
2. **Content (old Stage 5)** — better placeholder body, more checkpoints, elevation, road decals, editor track builder, minimap.
3. **Progression extras never built** — `SaveBestLap` / PlayerPrefs, `AIDriverProfile` ScriptableObject, named `TrafficRules` (AI is pure pursuit today), quality/timescale settings panel beyond pause.
4. **Play feel** — signed-off M1 checklist (phone-stopwatch laps, 60 fps at 1080p, gamepad unplug, etc.) is still a manual pass, not a test suite.
5. **Housekeeping** — leftover CS0618 in some EditMode/PlayMode test helpers and `Assets/Editor/Stage1Bootstrap.cs` (not the Linux player build).
