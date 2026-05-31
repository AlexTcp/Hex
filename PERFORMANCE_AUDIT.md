# Performance Audit — Hex

## Provenance

**Profiler data provided:** None
**Build verified:** Yes (`dotnet build` clean, 0 warnings, 0 errors)
**Runtime verified:** No (static analysis only)
**Score qualification:** Reflects static-analysis confidence, not measured performance.

## Health Score: 8.5/10

Hex is a small turn-based hex puzzle game with a tight, well-factored codebase. The per-frame attack surface is essentially zero — there are no `_Process` or `_PhysicsProcess` overrides in any project script, and the only `_Input` handler is guarded by `#if DEBUG`. Token meshes and materials are already cached as shared static fields, tile mesh/shape resources are shared across all 61 tiles, and the move-buffer list is pre-sized and reused. The remaining findings are minor mobile-tuning hypotheses (physics tick rate, autoload count) rather than concrete inefficiencies.

## Fixes Applied

_No automatic patches were applied._

The conservative-fix criteria (per skill) did not match any site in this codebase: no `GetNode`/`Load`/LINQ/string-build in confirmed hot paths, no print calls in non-`#if DEBUG` per-frame methods, no obvious shadow/MSAA misconfiguration. Pre-existing `.bak` files (e.g. `scripts/UI/GameScreen.cs.bak`) were left untouched. Re-audit (2026-05-30): 5 changed files (`HexBoard.cs`, `GameScreen.cs`, `TokenCatalog.cs`, `Tokens.cs`, `Haptics.cs`) scanned — no new performance issues found.

## Identified Issues (hypotheses; verify with profiler before further work)

### CPU
- **[2/10]** `scripts/Board/HexBoard.cs:111-125` — `_Input` does `GetViewport()` plus string interpolation inside `GD.Print` on every touch/mouse event. **Guarded by `#if DEBUG`**, so it is not a release hot path. No action needed unless DEBUG builds are profiled and these logs dominate.
- **[2/10]** `scripts/Board/HexBoard.cs:202-217` — `OnTileInput` does the same DEBUG-only logging on every tile pick event. Again `#if DEBUG`, not in release.
- **[2/10]** `scripts/UI/GameScreen.cs:81` — `GD.Print` in `SetGameplayActive` runs on every modal open/close. Not per-frame; user-driven. Low cost. Could be removed for production cleanliness, but behavior-affecting (people may rely on the diagnostic) so left as-is per the conservative rule.

### Memory
- **[2/10]** `scripts/UI/GameScreen.cs:65` — `button.Pressed += () => OnPickToken(index, button, description);` allocates 18 capturing closures during `_Ready`. One-time cost at scene load, not per-frame. Not worth refactoring.
- **[3/10]** `scripts/Board/HexBoard.cs:183-185` — `Callable.From(...)` with a captured `coord` is allocated per tile during `BuildBoard` (~61 tiles at `Radius=4`). One-time at scene load.

### Rendering
- No issues identified. `gl_compatibility` renderer + single `DirectionalLight3D` (no shadow_enabled) + a single shared environment is mobile-appropriate. No MSAA configured (defaults off on `gl_compatibility`).

### Physics
- **[3/10]** `project.godot` does not set `physics/common/physics_ticks_per_second`, so the engine defaults to 60. This is a turn-based picker game with no rigid bodies — physics ticks are only used by `Area3D` input picking. Lowering to 30 Hz would save CPU on mobile. **Hypothesis only**; not patched because behavior-preservation isn't certain (`Area3D` input picking responsiveness depends on tick rate).

### Architecture
- **[3/10]** Two autoloads (`DebugLog`, `GameSession`). Both are looked up only once in `_Ready` (`GameScreen.cs:47`), so there is no per-frame `/root/...` resolution churn. No issue.
- **[2/10]** `DebugLog.CapturingLogger._LogMessage` allocates strings via `string.TrimEnd()` and the bracketed prefix in `Append`. Triggered on every `GD.Print` (including the DEBUG-only ones). Acceptable for a debug subsystem.
- **[1/10]** Signal `DebugLog.GameplayActiveChanged` is correctly subscribed and unsubscribed in `GameScreen._Ready` / `_ExitTree`. No leak.

### Scenes & Resources
- `scenes/game.tscn` — clean. No `shadow_enabled=true`, no `local_to_scene=true` materials, no `GPUParticles*`. WorldEnvironment uses a flat clear-color background with cheap ambient. Tree depth is shallow.
- `HexBoard` tiles share `SharedTileMesh` and `SharedTileShape`. Per-checker `StandardMaterial3D` (only 3 of them) shared across all tiles. Highlight uses one shared `StandardMaterial3D`. Good resource hygiene.
- Token subclasses share their `Mesh` and `StandardMaterial3D` via `private static readonly` fields. Good.

### Shaders
- No custom `.gdshader` files in the project. Tokens and tiles use `StandardMaterial3D` only. Nothing to audit.

### Project Settings
- `renderer/rendering_method=gl_compatibility` (mobile-appropriate).
- `textures/vram_compression/import_etc2_astc=true` (correct for modern Android).
- No `physics_ticks_per_second` override — see Physics section above.
- No `rendering/anti_aliasing/quality/msaa_*` overrides — defaults to off, which is the right choice for `gl_compatibility` on mobile.
- `window/stretch/mode="canvas_items"` with landscape — appropriate.

### Mobile-Specific
- Android export targets only `arm64-v8a` — single architecture keeps APK small.
- `gradle_build/compress_native_libraries=false` — fine for size/startup tradeoff.
- `screen/immersive_mode=true` — good for full-screen game.
- Single `DirectionalLight3D` with no shadow — mobile-friendly.
- Draw call count is approximately: 61 tile meshes + 1 token mesh + UI canvas = well under the <100 mobile guideline. (Note: each tile is its own `Area3D + MeshInstance3D`; Godot may not batch these. Could be flagged if a future profile shows draw-call pressure, but conservative analysis can't confirm.)

## What this audit did NOT cover

- **No profiling.** Every "hot path" is inferred from syntax. The engine may behave very differently from what static analysis predicts.
- **No runtime measurement.** No FPS, frame-time, draw-call, or memory data was captured.
- **No editor-driven scene playback.** Did not run the project to count actual draw calls, observe `Area3D` picking cost, or inspect VRAM.
- **No GPU/shader inspection.** No custom shaders exist, but `StandardMaterial3D` compile/parse cost on first frame was not measured.
- **No mobile thermals / battery measurement.** Cannot confirm whether `gl_compatibility` + 60 Hz physics is a problem on low-end Android.

## Recommended next step

Profile the game on the worst-case target device (lowest-end target Android phone) with the Godot Monitor + frame-time graph visible, recording a 30-second session of token-picking + tile-tapping. Specifically watch:

1. **Process / Physics Process time** — to confirm there really is no per-frame CPU work (the static analysis predicts ~zero).
2. **Draw calls** — to confirm the 61 individual tile `MeshInstance3D`s aren't a draw-call issue. If they are, the fix is to switch to a `MultiMeshInstance3D` for tiles.
3. **`Area3D` picking cost** under continuous touch input.
4. **Physics tick CPU** — if it's a meaningful slice, lower `physics_ticks_per_second` to 30.

Re-run this audit with the profile as input; that's when concrete prioritization becomes possible.

---

## Re-audit (2026-05-31, static analysis, no profiler)

Re-pass, **no new patches**. Battery clean (`Enum.GetValues`=0, `.Call(`=0, no hot-path
`_Process`/`_Draw`/`_PhysicsProcess`, no uncached `=> GetNodeOrNull` accessors). `gl_compatibility`
explicit for both desktop and mobile; 3D Area3D-picker board with **no shadow-enabled lights**. The
only open item — `physics_ticks_per_second` default 60 — stays a hypothesis (lowering it risks
`Area3D` input-picking responsiveness; behavior preservation not certain). Health **8.5/10**.
Next step: if mobile CPU is tight, A/B test 30 Hz physics on device and watch tap latency.
