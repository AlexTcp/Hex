# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

**Hex** is a Godot 4.6 (mono, .NET 8) hex-grid movement **hunt** with a "Premium Slate" tournament-board look. The player chooses one of 14 token archetypes (Walker, Charger, Stepper, Skipper, Runner, Hopper, Jumper, Halo, Diamond, Glider, Knight, Camel, Spiral, Drifter) — each encodes a unique movement rule, and that rule *is* the character — then clears waves of **hunters** on a 61-tile board (radius 4) by moving onto them (capture). Destinations are generated relative to the token (`from + offset`); the old origin-anchored set (Mirror, Ringwalk, Orbit, Edge, Anchor, Echo, Pivot, Shrine) was removed.

**Stakes & meta:** Chase hunters can step onto the player → **game over** (`PlayerCaught`). The fail-state is kept fair: a candidate move tile is painted red ("death tile") iff a non-grace Chase hunter is adjacent to it (an exact, zero-alloc `Distance==1` test, since Chase minimises distance-to-player and the player tile is a strict distance-0 minimum). Wave 1 is all-RandomWalk (un-losable teaching); later waves add Chasers (≤70% cap); freshly spawned hunters get a one-turn grace; a mercy rule frees the player if *every* legal move is lethal. Scoring: capture = `100 × min(combo,5)`, wave-clear = `250 × wave`. Best wave / high score / per-token bests / tutorial-seen persist to `user://hex.cfg`. Flow: **Title → Character-Select → Run → Game-Over → retry**, all as Control overlays over a board that never unloads.

The codebase is still small and heavily optimized for low-spec mobile: **GL Compatibility renderer (no glow/SSAO/SSR/GPU-particles — see the design constraints)**, zero-alloc movement generation, shared static GPU resources, signal-based input gating. Preserve the zero-alloc invariants when touching gameplay code.

## Build & Run

- **Engine**: Godot 4.6.2 mono. Main scene is `res://scenes/game.tscn`. Open in editor and press F5.
- **Renderer**: GL Compatibility (`rendering/renderer/rendering_method = "gl_compatibility"`, both desktop and `.mobile`) — mobile-targeted. VRAM ETC2/ASTC compression on.
- **Headless typecheck**: `dotnet build Hex.csproj`. Project targets .NET 8 (`Godot.NET.Sdk/4.6.2`, `EnableDynamicLoading=true`, nullable disabled).
- **Android**: `./run_android.sh` builds, installs, and launches a debug APK. Env vars: `BUILD_MODE=release`, `DEVICE=<serial>`, `LOGCAT=1`, `VERBOSE=1`. APK output: `build/android/hex-{debug,release}.apk`. `export_presets.cfg` is checked in.

## Autoloads (`project.godot` `[autoload]`)

- **`DebugLog`** (`scripts/DebugLog.cs`) — singleton. Installs an `OS.AddLogger` capturing every Godot `print` / `push_error` into a 500-entry ring buffer. Owns the top-right gear button → `SettingsModal` (sliding drawer) → `DebugModal` (full-screen log viewer with copy/clear).
- **`GameSession`** (`scripts/GameSession.cs`) — single source of truth for cross-screen state. Live run: `SelectedTokenIndex`, `Score`, `Wave`. Persisted meta (via `ConfigFile` at `user://hex.cfg` `[progress]`): `BestWave`, `HighScore`, `TutorialSeen`, `PerTokenBestWave[14]`. Loads on `_Ready`; saves on `CommitRun` (game over), `MarkTutorialSeen`, and app-pause/close `_Notification`.

## Architecture

### Core Game Loop

1. **Init**: `GameScreen._Ready` (root bootstrap) builds the 3D stage *in code* — `WorldEnvironment` (slate `Color` bg, cool flat ambient, Filmic tonemap), warm key + cool fill + central soft `SpotLight3D`, and a tilted perspective `Camera3D` — then constructs `ScreenManager` and hands it the board, camera, session and UI root. `HexBoard._Ready` builds 61 tiles (`Area3D` + `MeshInstance3D` sharing one static `CylinderMesh` + `CylinderShape3D`). `ScreenManager` boots to **Title**.
2. **Select → run**: `CharacterSelect` previews a piece via `HexBoard.SetToken(idx)` (no hunters); **START** calls `HexBoard.StartRun(idx)` (zero score/wave/combo, spawn wave 1). `SetToken` (preview) and `StartRun` (commit) are distinct entry points.
3. **First tap** on the token's tile → `BeginSelect()` fills `_movesBuffer`, paints each legal tile gold (move), copper (capture — hunter present), or **red (death tile)**, and pulses them via one shared looped tween. Mercy + telegraph computed here.
4. **Second tap** on a highlighted tile → `MoveTokenTo(coord)`: commit pos → `TryCapture` (+score/combo, spark burst) → wave-clear short-circuit (bonus + ramp + grace respawn) → else hunters step (`AdvanceEnemies`) with a per-hunter caught check that emits `PlayerCaught` and stops on the first catcher. `ScreenManager` listens to `PlayerCaught`/`ScoreChanged`/`WaveChanged`/`EnemiesChanged`/`ComboChanged`/`BoardSolved`/`ThreatChanged` to drive the HUD and the Title→…→GameOver flow.

### Hex Coordinate System

- `scripts/Hex/HexCoord.cs` — readonly struct with axial coords `(Q, R)` plus derived `S = -Q-R`. Implements arithmetic ops, distance, hash code, and equality. Six static direction vectors (`E`, `NE`, `NW`, `W`, `SW`, `SE`).
- **Zero-alloc invariant**: `Within(radius, List<HexCoord> output)` and `Ring(radius, List<HexCoord> output)` are direct-write overloads (the enumerable forms also exist, but hot paths use the buffer overloads). Honor this when adding new range queries.
- `scripts/Hex/HexLayout.cs` — static `ToWorld(HexCoord, y)`. `TileSize = 0.55f`. Flat-top hex math (√3 × 1.5 spacing).

### Token System

- `scripts/Tokens/Token.cs` — abstract partial `Node3D` base. Required overrides per subclass: `Id`, `LegalMoves(from, radius, output)`, `GetSharedMesh()`, `GetSharedMaterial()`.
- `scripts/Tokens/Tokens.cs` — 14 sealed partial subclasses. Each defines:
  - A movement rule that fills the caller's `List<HexCoord>` buffer (no per-call allocation).
  - `static readonly` `SharedMesh` and `SharedMaterial` — every instance of a given token type renders with the same GPU resources.
- `scripts/Tokens/TokenCatalog.cs` — static `TokenInfo[]` of 14 entries (`Name`, `Description`, `Func<Token> Factory`). The picker UI iterates this without instantiating.
- `Token.Filter(moves, from, boardRadius)` — in-place two-pointer compaction that strips the origin tile and out-of-bounds entries. Use it in every subclass's `LegalMoves` before returning.

### Board (`scripts/Board/HexBoard.cs`)

- Stores tiles in `Dictionary<HexCoord, Tile>` (`Tile` is a nested class with `Coord`, `CheckerIndex` (precomputed `(Q-R)%3`), `Mesh`, `Area3D`, `InputHandler` Callable).
- 3-color tile palette (`TileMaterialA/B/C`, all `static readonly`). Highlighted tile uses the shared gold emissive material — `MaterialOverride` swap, not material instance.
- **Selection memoization**: `BeginSelect` returns early if `_lastSelectedToken == _token && _lastSelectedPos == _tokenPos` — tapping the same token twice doesn't rebuild the highlight set.
- **Per-tile InputEvent Callables** are stored on each `Tile` and explicitly disconnected in `_ExitTree`. New per-tile signal hookups must follow this pattern or they leak on scene exit.
- `_movesBuffer` is a single `List<HexCoord>` (capacity 64) reused across every selection. Don't allocate a fresh list inside `LegalMoves` callers.

### UI (`scripts/UI/GameScreen.cs`)

- Root `Node3D` for the game scene. `_Ready` builds a vertical `VBoxContainer` of 14 toggle buttons from `TokenCatalog`. Subscribes to `DebugLog.GameplayActiveChanged` and toggles `Viewport.PhysicsObjectPicking` so modal overlays don't pass taps through to the board.
- All UI is procedural — there's no `.tscn` for the picker.

### Debug Overlay

- `DebugModal` (full-screen) and `SettingsModal` (320px right-edge drawer, 0.22s Cubic tween) are both procedurally built.
- `DebugLog.PushModal()` / `PopModal()` track nesting depth; the `GameplayActiveChanged` event fires only on the 0↔1 transition. Use these wrappers when adding new modals so input gating works automatically.

## Performance Conventions (Hard Rules)

These are the patterns the recent perf audit established. Don't regress them:

1. **Zero-alloc movement**: `LegalMoves` fills the caller's buffer. Never `new List<HexCoord>()` inside a movement rule.
2. **Shared GPU resources**: tile mesh, tile collision shape, highlight material, and every token's mesh + material are all `static readonly`. Don't instantiate per-tile / per-token.
3. **Memoized selection**: if you change the selection flow, keep the `(token, pos)` short-circuit in `BeginSelect`.
4. **Signal cleanup**: per-node Callables get disconnected in `_ExitTree`. Add new per-tile signal hookups to the existing pattern, not as anonymous lambdas.
5. **Diagnostic prints**: wrap `GD.Print` calls in `#if DEBUG` unless they're behind the `DebugLog` overlay (which captures them anyway).

## Existing Documentation

- `overview.html` — comprehensive HTML reference with file map, token table, hex math notes. Open in a browser; treat as the human-facing readme. (May still reference the removed origin-anchored tokens until refreshed.)
- `PERFORMANCE_AUDIT.md` — most recent audit; identifies remaining minor opportunities. Scope covers C# gameplay and the GDScript test scaffolding under `android/build/`.

## Scene & Project Layout

| Path | Contents |
|------|----------|
| `scenes/game.tscn` | Single persistent scene — minimal: `GameScreen` (Node3D) + `HexBoard` (Node3D) + `UI` CanvasLayer + `Root` Control. Camera/lights/environment are built in code by `GameScreen`. |
| `scripts/Board/` | `HexBoard.cs` (tiles, hunters, turn resolution, fail-state, scoring, FX) |
| `scripts/Hex/` | `HexCoord.cs`, `HexLayout.cs` |
| `scripts/Tokens/` | `Token.cs` (base + gold-swap), `Tokens.cs` (14 rules + meshes), `TokenCatalog.cs` |
| `scripts/UI/` | `GameScreen.cs` (stage bootstrap), `ScreenManager.cs` (state machine), `UiTheme.cs` (palette/Theme/factories/vignette), `Hud.cs`, `TitleScreen.cs`, `CharacterSelect.cs`, `PauseOverlay.cs`, `GameOverScreen.cs`, `TutorialOverlay.cs` |
| `scripts/` (root) | `DebugLog.cs`, `GameSession.cs` (state + persistence), `Haptics.cs`, `DebugModal.cs`, `SettingsModal.cs` |
| `textures/ui/` | `gear.png` (vignette is a code shader, not an asset) |
| `android/` | Godot Android export template (do not hand-edit) |

**Premium Slate / Compatibility constraints (hard rules for visual work):** the GL Compatibility renderer has NO glow/bloom, SSAO, SSR, ReflectionProbe, SDFGI, volumetric fog, DOF, FXAA/TAA, or reliable GPU particles. The "glow" of the active piece is an **emissive material**; the vignette is a **`canvas_item` shader** ColorRect; capture sparks use **`CpuParticles3D`**; AA is **`msaa_3d`** only (cosmetic — never make readability depend on it). Keep tiles at `RadialSegments = 6` (a hex grid, not a dodecagon).
