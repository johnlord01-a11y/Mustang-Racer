I could not read `Packages/manifest.json` (tool approval was declined, and you asked me not to run commands), so package availability is listed as an explicit verify-first step rather than an assumption.

# OpenWheelRacer — Development Plan (Single-Player Prototype)

**Scope:** one fictional open-wheel car on placeholder geometry, one primitive closed track, chase camera, keyboard drive, checkpoints + lap timer, reset. No AI until Stage 6.

**Engine:** Unity 6000.6.6f1. **Two setup gotchas to confirm in Stage 0:** (1) `com.unity.inputsystem` must be installed **and** *Project Settings → Player → Active Input Handling* set to **Input System Package (new)** — installing the package alone does not switch it; (2) Unity Test Framework ships with the editor in Unity 6 but test assemblies need `.asmdef` files with the EditMode/PlayMode test refs.

**Core physics decision:** `WheelCollider` (4 wheels, one per corner) over raycast-vehicle or `ArticulationBody`. It is built in, documented, tunable, and matches an open-wheel car well. Supporting choices: Rigidbody mass ~700 kg, custom center-of-mass via an empty child lowered ~0.15 m; `WheelCollider.ConfigureVehicleSubsteps(5f, 12, 15)`; fixed timestep 1/100 s; front axles steer, rear axles drive via an open-differential approximation (torque split 50/50, no diff component); visual wheel meshes as children assigned to `wheelGraphics`, spun/steered from the collider in `LateUpdate`.

## Numbered Stages

**Stage 0 — Foundation.** *Editor tasks:* create `Assets/Scenes/{Boot, Garage, Track_Proto, Sandbox}`; folders `Scripts`, `Prefabs`, `Physics`, `Input`, `Tests`; add packages `com.unity.inputsystem`, `com.unity.test-framework` if missing; create `OpenWheelRacer.InputActions.asset`; create `.gitignore` (`Library/`, `Logs/`, `UserSettings/`) and commit baseline. *Scripts:* `OpenWheelRacer.Runtime.asmdef` + test asmdefs.

**Stage 1 — Drivetrain & steering feel.** *Editor tasks:* build `Prefabs/Car_Primitive`: Cube chassis (2.9×0.4×1.4 m, y-offset so ride height ≈ 3 cm), 4 Cylinder wheels (Ø 0.68 m rear / 0.55 m front, rotated 90° on Z, 8–12 sides for perf), 4 empty wheel anchors; add Rigidbody + 4 `WheelCollider`s; `PhysicMaterial` "Road" (static/dynamic friction 1.6, bounce 0) on track, "OffRoad" (0.5) on run-off; Sandbox scene with a large ground plane. *Scripts:* `VehicleInput`, `VehicleController`, `VehicleTuning` (ScriptableObject), `WheelVisuals`.
**Gate:** wheel spin/steer visually correct, no jitter at rest, tunable from the ScriptableObject.

**Stage 2 — Chase camera.** *Editor tasks:* Main Camera → child-free rig following a `CameraTarget` empty behind the chassis; `Camera.sfieldOfView` 60 base. *Scripts:* `ChaseCamera` (exponential smoothing, target-offset, speed-based FOV kick, optional wall-avoid raycast), `CameraTarget`. Use plain C# rather than Cinemachine — one script, no package-version ambiguity. **Gate:** camera readable at 40 m/s; no roll contamination.

**Stage 3 — Track, checkpoints, lap timer, reset → FIRST PLAYABLE.** *Editor tasks:* build `Track_Proto` from primitives — a rounded-oval: 2 long road Boxes + 2 half-torus/quadrant Boxes on a 60 m radius, road width 12 m, each segment `MeshCollider` (convex) tagged `Road`; low Cube walls; darker Cube run-off with `OffRoad` material; 4 `TrackCheckpoint` trigger Boxes spanning the road (finish line = index 0) plus one OutOfBounds trigger volume above the infield; `SpawnPoint` empty on the grid; Canvas HUD with three `Text` fields. *Scripts:* `TrackCheckpoint`, `LapManager`, `PlayerCarReset`, `SpawnPoint`, `GameManager`, `LapTimerUI`.
**Milestone M1 — "First Playable" acceptance criteria** (all pass = M1 done):
1. From a cold start the car is drivable within 2 s; keyboard gamepad-free.
2. W/↑ accelerates to ≥ 30 m/s on flat road; S/↓ reverses after stop; release coasts with drag.
3. A/D or ←/→ steer left/right with speed-sensitive reduction; car does not lift wheels or spin out from full lock at 5 m/s.
4. Space holds the rear axle and produces a controllable slide at 20 m/s.
5. Crossing checkpoints 1→2→3→finish increments lap exactly once; skipping one does not; driving backwards does not.
6. Lap timer shows mm:ss.fff, updates live, and reports a correct best lap across 3 laps (±0.05 s vs a phone stopwatch).
7. R resets to the last valid checkpoint within 1 frame, zeroes velocity; off-track for 3 s auto-resets.
8. Stable 60 fps at 1080p; no NaN position, no tunneling through walls at 40 m/s.

**Stage 4 — Player-loop hardening.** *Editor tasks:* pause/quit panel, quality + timescale settings, `Boot` scene as default with `DontDestroyOnLoad`. *Scripts:* `RaceConfig`, `GamePause`, `TimeFormat` (static helper), `SaveBestLap` (PlayerPrefs).
**Gate:** 10 clean laps, all input paths covered, no editor warnings.

**Stage 5 — Content pass.** *Editor tasks:* better placeholder body, 6–8 checkpoints, elevation via rotated segments, road-decal contrast for reference speed. *Scripts:* `TrackBuilderEditor` (Editor-window utility that duplicates/rotates segments along a hand-placed point list), `MinimapLabelStub`.

**Stage 6 — AI opponents (gated, only after M1 + Stage 4 sign-off).** *Editor tasks:* duplicate car prefab with AI tuning; waypoint empties down the centre line. *Scripts:* `WaypointPath`, `AIVehicleController`, `AIDriverProfile` (ScriptableObject: corner speed table, look-ahead, throttle curve), `TrafficRules` (no collision shove). **This is the first point at which any player-mechanic code may change shape — do it after mechanics are frozen.**

## Script Index (names + responsibilities, written in the stage noted)
`VehicleInput` (S1) reads Input Actions, exposes normalized axes, zero physics knowledge. `VehicleController` (S1) maps axes → `WheelCollider` steer/torque/handbrake. `VehicleTuning` (S1) ScriptableObject of all drive constants. `WheelVisuals` (S1) syncs placeholder meshes to colliders. `ChaseCamera`/`CameraTarget` (S2). `TrackCheckpoint` (S3) ordered-trigger validation. `LapManager` (S3) progress, lap count, splits, best lap. `PlayerCarReset` (S3) pose/velocity restore + cooldown. `SpawnPoint` (S3). `GameManager` (S3) restart/reset orchestration. `LapTimerUI` (S3) HUD binding. `RaceConfig`, `GamePause`, `SaveBestLap` (S4). `AI*` (S6).

## Tests
**EditMode** (fast logic, injected clock instead of `Time`): checkpoint ordering (valid loop, skipped, reversed, double-cross); best/split timing math; `VehicleTuning` range validation; Input Actions asset contains `Throttle/Brake/Steer/Handbrake/Reset` with keyboard+gamepad bindings.
**PlayMode** (dedicated tiny scenes): throttle produces > 2 m/s forward within 1.5 s; handbrake cuts rear rotation; lap registers only when triggers pass in order; reset restores pose within 1e-3 m and velocity under 1e-2 m/s; car never leaves the physics scene (no NaN).
**Manual checklist per stage:** key-repeat/hold behaviour, both arrow and WASD schemes, alt-tab and focus loss, controller unplug mid-session, wall/kerb collisions at speed, timer vs external stopwatch, restart 20× in a row, restart after pause, editor-enter/exit with no console errors.

## Explicit Approval Gates
No stage begins without your go-ahead: **A0** after Stage 0 (package/settings diff); **A1** after Stage 1 (drive-feel demo — approve torque/steer numbers before camera work); **A2** after Stage 2 (camera feel); **A3** after Stage 3 (M1 review — the biggest decision point); **A4** after Stage 4 (mechanics freeze); **A5** after Stage 5; **A6** only on written approval to open AI work.

**Suggested order of work after A0:** Stage 1 ≈ 1 session, Stage 2 ≈ half, Stage 3 ≈ 1–2 sessions to M1.
