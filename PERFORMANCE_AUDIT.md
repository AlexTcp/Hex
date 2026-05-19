# Performance Audit — Hex

## Provenance

**Profiler data provided:** None
**Build verified:** Yes (`dotnet build` clean, 0 warnings, 0 errors)
**Runtime verified:** No (static analysis only)
**Score qualification:** Reflects static-analysis confidence, not measured performance. Target device class is BOTH desktop and mobile (mobile-tier constraints applied).

## Health Score: 8.5/10

Codebase is unusually clean for an audit pass: all meshes/materials are shared `static readonly` resources, allocation hot paths use reusable buffers (`_movesBuffer`, `Within(int, List<HexCoord>)`, `Ring(int, List<HexCoord>)`), input handlers are guarded behind `#if DEBUG`, and per-frame `_Process`/`_PhysicsProcess` overrides are absent entirely. Game is turn-based (input-driven only), so static analysis finds little to optimize. Most remaining items are configuration-tier hypotheses for mobile rather than CPU/allocation issues.

## Fixes Applied

- `scripts/UI/GameScreen.cs` — cached `GameSession` autoload as a private field in `_Ready` instead of resolving `/root/GameSession` on every token-button press. [.bak preserved]

## Identified Issues (hypotheses; verify with profiler before further work)

### CPU
- **[3/10]** `scripts/UI/GameScreen.cs:84` (pre-fix) — `GetNode<GameSession>("/root/GameSession")` was re-resolved on every `OnPickToken` call. User-triggered, not per-frame, so impact is small.
  - *Fix applied:* cached as `_session` field, resolved once in `_Ready`.

- **[2/10]** `scripts/UI/GameScreen.cs:67` — `_Ready` final `GD.Print` interpolates camera name and viewport size; each Variant-boxed arg. One-shot, no concern, but noisy logging at startup. Not patched (no clear behavior-preserving rule on whether the log is intentional diagnostics).

- **[2/10]** `scripts/Board/HexBoard.cs:111-125, 202-217, 220-244` — multiple `GD.Print` calls in `_Input`, `OnTileInput`, `OnTileTapped`. All correctly wrapped in `#if DEBUG`, so release builds are clean. No fix needed; flagging for awareness.

### Memory
- **[2/10]** `scripts/Board/HexBoard.cs:182-185` — `Callable.From(...)` lambda captures `coord` per tile inside `BuildTile`. Allocates one closure per tile (61 at radius 4). One-shot at board build; trivial cost. Could be refactored to a single dispatch by `coord` lookup keyed off the `Area3D` instance, but the change is not mechanical (would require equating sender to coord through a dictionary), so left as-is.

### Rendering
- **[N/A]** Scene `scenes/game.tscn` is well-formed: a single `DirectionalLight3D` with `shadow_enabled` unset (defaults to false — good for mobile). No MSAA enabled. No particles. `WorldEnvironment` uses simple ambient — minimal cost.

- **[5/10]** *Hypothesis (mobile):* `DirectionalLight3D` with `light_energy=1.1` is fine, but the renderer is `gl_compatibility`, which does not support `DirectionalLight3D` shadows in the same way as Forward+. If the visual depends on lighting subtlety, verify on target device. Not patched.

### Physics
- **[4/10]** `project.godot` — no `physics/common/physics_ticks_per_second` override; defaults to 60 Hz. The game is turn-based with no `_PhysicsProcess` overrides in any script, so 60 Hz of physics is wasted. Lowering to 30 Hz (or even 20 Hz) would save battery on mobile.
  - *Fix not applied:* changing physics tick rate is a project-wide setting; even though no code uses physics directly, the `Area3D` input picking uses physics queries. Tap responsiveness could degrade subtly. Recommend manual change + on-device verification.

### Architecture
- **[2/10]** `scripts/DebugLog.cs:75-83` — `Append` does `string.Format`-style interpolation, then `AddLast`, under lock. If the engine generates a high-volume log burst (e.g. shader compilation errors), this serializes through the lock. Acceptable for normal operation; flagged for awareness.

- **[3/10]** `scripts/DebugLog.cs:172-193` — `_LogError` builds two interpolated strings (`$"{file}:{line} {function}"` and `$"{rationale} ({code})"`) per engine error. Allocation per error. Not patched — error logging is intentional and low-frequency.

### Scenes & Resources
- **[N/A]** `scenes/game.tscn` (90 lines) — small, no `local_to_scene=true` materials, no deep node chains, no over-amount particles. Clean.

### Shaders
- **[N/A]** Project ships no `.gdshader` files. Nothing to audit.

### Project Settings
- **[4/10]** `project.godot` — `renderer/rendering_method="gl_compatibility"` is set for both desktop and mobile, which is the right choice for low-end mobile reach but limits features (no shadows from spot/point lights, no SSR, no SSIL). Intentional, do not change without consent.

- **[3/10]** `project.godot` — `textures/vram_compression/import_etc2_astc=true` is set. Good for mobile; pairs with the Android export preset.

- **[5/10]** `project.godot` — missing explicit `rendering/anti_aliasing/quality/msaa_3d` setting. Default is `Disabled`, which is correct for low-end mobile. Confirm on target device that no MSAA is desired.

### Mobile-Specific
- **[6/10]** `export_presets.cfg` — `gradle_build/compress_native_libraries=false`. Set to `true` for smaller APK and faster initial install, at the cost of slightly slower startup (libraries decompressed on launch). For a small game like Hex, `true` is usually the better trade-off. Not patched (user preference).

- **[4/10]** `export_presets.cfg` — `architectures/armeabi-v7a=false`, only `arm64-v8a=true`. Correct for modern devices; excludes pre-2017 hardware. Already mobile-optimal.

- **[4/10]** *Hypothesis:* draw call count for 61 tiles + 1 token + UI is well under the <100 mobile target. Static budget looks safe.

- **[3/10]** *Hypothesis:* `Area3D` input picking on all 61 tiles every frame the viewport is processing input. Currently gated by `Viewport.PhysicsObjectPicking` toggled when modals open/close (`GameScreen.SetGameplayActive`) — good. Confirm that physics-object picking is the actual cost driver before changing.

## What this audit did NOT cover

- No profiling — every "hot path" is inferred from syntax, not measured. The game has no per-frame `_Process` or `_PhysicsProcess` overrides at all, so most static-analysis "hot path" categories simply do not apply here.
- No runtime measurement — patch not verified to improve perf.
- No editor-driven scene playback or draw-call inspection.
- No GPU profiling — shader compilation, texture upload, and fillrate not assessed.
- No on-device thermal/battery measurement.
- No analysis of `addons/`, `android/build/`, `build/`, `.godot/`, `obj/`, `bin/` (excluded by audit methodology).
- No tween/animation profiling — `CreateTween` is used for token movement and gear flash, both short-lived and infrequent.

## Recommended next step

Profile a real session on the target mobile device: open the Godot remote profiler, watch CPU frame time during board build, token swap, and tile selection, and measure GPU frame time during steady-state idle. If frame time is acceptable, no further audit work is warranted. If a hot spot appears, re-run this audit with the profile as input. Independently of profiling, consider:

1. Setting `physics/common/physics_ticks_per_second` to 30 Hz (turn-based game, no physics simulation).
2. Setting `gradle_build/compress_native_libraries=true` in `export_presets.cfg` for smaller APK.
3. Confirming `Viewport.PhysicsObjectPicking` gating actually correlates with idle CPU savings on device.
