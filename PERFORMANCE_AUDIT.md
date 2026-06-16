# Performance Audit — Hex

## Provenance

- **Profiler data provided:** None.
- **Build verified:** Yes — `dotnet build Hex.csproj` compiles cleanly in both Debug and Release (0 warnings, 0 errors) after the patches below.
- **Runtime verified:** No (static analysis only). No frame timing, engine monitors, or on-device measurements were taken.
- **Score qualification:** The health score below reflects *static-analysis confidence* that the code follows performant patterns. It is **not** a measured performance figure. Every "issue" is a hypothesis until confirmed with a profiler on a target device.

## Health Score: 9/10

Hex is a small, mature, and unusually well-optimized codebase. It is entirely event-driven: there are **no `_Process` / `_PhysicsProcess` hot paths anywhere**, so there is no per-frame CPU or allocation pressure to hunt. Movement generation is zero-alloc (caller-supplied buffers), GPU resources (meshes, materials, collision shapes) are `static readonly` and shared across all tiles/tokens/enemies, selection is memoized, telegraph/ring nodes are pooled, and per-tile input Callables are explicitly disconnected in `_ExitTree`. The only real findings were diagnostic `GD.Print` calls left unconditionally compiled, which the project's own CLAUDE.md rule 5 says should be `#if DEBUG`-gated — now fixed. One point withheld only because the project targets low-spec mobile and has not been profiled on device.

## Fixes Applied

- **`scripts/UI/GameScreen.cs:80`** — wrapped the `_Ready` diagnostic `GD.Print` (with two `GetViewport()` calls and string interpolation) in `#if DEBUG`. The DebugLog overlay captures prints via `OS.AddLogger` regardless, so debug behavior is unchanged; release builds no longer execute it.
- **`scripts/UI/GameScreen.cs:188`** — wrapped the `[DIAG-GATE]` print in `SetGameplayActive` in `#if DEBUG`. This method runs on **every modal open/close** (0↔1 transitions), so it was the most repeatedly-executed unconditional print. Behavior (the `PhysicsObjectPicking` toggle) is untouched.
- **`scripts/DebugLog.cs:61, 153, 159`** — wrapped three diagnostic prints (`[DebugLog] ready`, gear `Pressed`, gear `ButtonDown`) in `#if DEBUG`. Infrequent, but they violated the same convention and are pure diagnostics.

Backups: `GameScreen.cs.bak` already existed from a prior audit and was left untouched; `DebugLog.cs.bak` was created before editing. Build re-verified clean in Debug and Release after all edits.

## Identified Issues

### CPU
- **[3/10]** `scripts/UI/GameScreen.cs:80` — *(fixed)* `_Ready` diagnostic print with two `GetViewport()` calls + interpolation, formerly unconditional. One-shot, so low impact; gated for cleanliness/release.
- **[4/10]** `scripts/UI/GameScreen.cs:188` — *(fixed)* `[DIAG-GATE]` print ran on every modal toggle, doing string interpolation + a boxed null-conditional viewport read each time. Now `#if DEBUG`.
- **[2/10]** `scripts/Board/HexBoard.cs:626` `PredictEnemyStep` is called once per enemy both to apply a move and (separately) by `ShowTelegraph`; for deterministic behaviors the same prediction is computed twice per selection. Enemy count is tiny (~3–10), so this is negligible — hypothesis only, **not** fixed.

### Memory
- No per-frame allocations exist (no `_Process`). Movement rules fill caller buffers; BFS, spawn, and neighbor scratch lists are all pooled fields. `string.Join` in `DebugLog.Snapshot()` allocates, but only when the log modal is opened. No action needed.

### Rendering
- **[2/10]** `scenes/game.tscn` uses a 3D scene (`Node3D` + `Camera3D` + `DirectionalLight3D`) for what is effectively a flat board. Shadows are not explicitly enabled on the light (Godot default is off in GL Compatibility), and there are no high-vertex meshes (hex tiles are 6-segment cylinders). Shared meshes/materials keep draw-call dedup healthy. No change warranted.

### Physics
- Board tiles are `Area3D` + `CylinderShape3D` used purely for tap picking via `InputEvent` (not simulated bodies). `PhysicsObjectPicking` is gated off while modals are open. `physics/common/physics_ticks_per_second` is left at the engine default (60) in `project.godot`; for a turn-based, tap-only game with no `_PhysicsProcess`, this could in principle be lowered to reduce idle physics ticks — **hypothesis only**, not changed (behavior/feel risk, unmeasured).

### Architecture
- Input is fully signal-driven (per-tile `InputEvent` Callables); no polling that should be a signal. Callables are disconnected in `_ExitTree` (no leak). Token swap frees the old token and instantiates the new — this is per-user-action (picker tap), not churn, so no pool is warranted. Static mutated materials (highlight/ring emission) are reset in `_ExitTree` to survive same-process scene reloads. Clean.

### Scenes & Resources
- `scenes/game.tscn` is small and hand-tuned. No `local_to_scene` materials, no `GPUParticles`, no high-vertex `Polygon2D`. The picker UI is built procedurally (no extra `.tscn` to parse). No issues.

### Shaders
- No `.gdshader` files in the project. All visuals use `StandardMaterial3D`. N/A.

### Project Settings
- `renderer/rendering_method = "gl_compatibility"` (desktop and `.mobile`) — correct for the low-spec mobile target.
- `textures/vram_compression/import_etc2_astc = true` — correct for mobile.
- `display/window/handheld/orientation = "landscape"` set.
- MSAA / `msaa_3d` not set (defaults to disabled) — appropriate for mobile.
- See Physics note re: `physics_ticks_per_second` default 60 (unverified opportunity only).

### Mobile-Specific
- `Haptics.cs` guards `Input.VibrateHandheld` behind `OS.HasFeature("mobile")` — safe no-op on desktop, correct.
- ETC2/ASTC compression enabled; GL Compatibility renderer; landscape lock. The diagnostic prints removed in this pass mattered slightly more on mobile (slower string formatting / log I/O), which is the main reason they were gated.

## What this audit did NOT cover

- **No runtime profiling.** No FPS, frame-time, draw-call, or memory measurements were taken on any device or in the editor. All findings are static hypotheses.
- **No on-device testing** (the `android/` export path was excluded from scope and not exercised).
- **No gameplay/logic review** — this was a performance pass only; correctness of movement rules, enemy AI, and wave progression was not audited.
- **No assessment of the GDScript test scaffolding** under `android/build/` (excluded by scope).
- The `physics_ticks_per_second` and double-`PredictEnemyStep` items are flagged as hypotheses and were intentionally left unchanged pending measurement.

## Recommended next step

Run the project in the Godot editor with the **Profiler** and **Monitors** tabs open (or the `improve-perf-live` flow) on a representative mobile device or at a phone resolution, drive several waves of captures, and confirm frame time stays within budget. If idle CPU is a concern on low-end hardware, *measure* before/after lowering `physics_ticks_per_second` — do not change it blind. Absent any measured hot path, the codebase needs no further structural optimization.
