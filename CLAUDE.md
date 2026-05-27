# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

**Hex** is a minimalist Godot 4.6 (mono, .NET 8) hex-grid movement puzzle. The player picks one of 14 token types (Walker, Charger, Stepper, Skipper, Runner, Hopper, Jumper, Halo, Diamond, Glider, Knight, Camel, Spiral, Drifter) and taps tiles to move it across a 61-tile board (radius 4). Each token type encodes a different movement rule — that rule *is* the puzzle. All current rules generate destinations relative to the token's position (`from + offset`); the previous origin-anchored set (Mirror, Ringwalk, Orbit, Edge, Anchor, Echo, Pivot, Shrine) has been removed.

The codebase is small (11 C# files) and heavily optimized for low-spec mobile: GL Compatibility renderer, zero-alloc movement generation, shared GPU resources, signal-based input gating. Most recent commits are perf audit follow-ups — preserve the zero-alloc invariants when touching gameplay code.

## Build & Run

- **Engine**: Godot 4.6.2 mono. Main scene is `res://scenes/game.tscn`. Open in editor and press F5.
- **Renderer**: GL Compatibility (`rendering/renderer/rendering_method = "gl_compatibility"`, both desktop and `.mobile`) — mobile-targeted. VRAM ETC2/ASTC compression on.
- **Headless typecheck**: `dotnet build Hex.csproj`. Project targets .NET 8 (`Godot.NET.Sdk/4.6.2`, `EnableDynamicLoading=true`, nullable disabled).
- **Android**: `./run_android.sh` builds, installs, and launches a debug APK. Env vars: `BUILD_MODE=release`, `DEVICE=<serial>`, `LOGCAT=1`, `VERBOSE=1`. APK output: `build/android/hex-{debug,release}.apk`. `export_presets.cfg` is checked in.

## Autoloads (`project.godot` `[autoload]`)

- **`DebugLog`** (`scripts/DebugLog.cs`) — singleton. Installs an `OS.AddLogger` capturing every Godot `print` / `push_error` into a 500-entry ring buffer. Owns the top-right gear button → `SettingsModal` (sliding drawer) → `DebugModal` (full-screen log viewer with copy/clear).
- **`GameSession`** (`scripts/GameSession.cs`) — lightweight state holder. Currently just `SelectedTokenIndex` (int), persisted across scene transitions.

## Architecture

### Core Game Loop

1. **Init**: `HexBoard._Ready` enumerates `HexCoord.Within(radius=4)` to build 61 tiles. Each tile is an `Area3D` + `MeshInstance3D` sharing one static `CylinderMesh` + `CylinderShape3D`. `GameScreen._Ready` builds the 14-button picker from `TokenCatalog.All`; pre-selects Walker (index 0).
2. **First tap** on the current token's tile → `BeginSelect()` calls `Token.LegalMoves(from, radius, _movesBuffer)`, swaps the `MaterialOverride` of each legal-move tile to the shared gold emissive material.
3. **Second tap** on a highlighted tile → `MoveTokenTo(coord)` tweens the token (0.18s Sine-out) to the new world position, then `EndSelect()` restores base materials.
4. **Picker** → `OnPickToken(idx)` updates `GameSession.SelectedTokenIndex`, calls `HexBoard.SetToken(idx)` (frees the old token, instantiates the new via `TokenCatalog.All[idx].Factory()`).

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
| `scenes/game.tscn` | Single game scene — `GameScreen` (Node3D) + `HexBoard` (Node3D) + CanvasLayer for UI |
| `scripts/Board/` | `HexBoard.cs` |
| `scripts/Hex/` | `HexCoord.cs`, `HexLayout.cs` |
| `scripts/Tokens/` | `Token.cs`, `Tokens.cs`, `TokenCatalog.cs` |
| `scripts/UI/` | `GameScreen.cs` |
| `scripts/` (root) | `DebugLog.cs`, `GameSession.cs`, `DebugModal.cs`, `SettingsModal.cs` |
| `textures/` | `gear.png` only |
| `android/` | Godot Android export template (do not hand-edit) |
