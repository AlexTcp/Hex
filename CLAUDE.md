# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

**Hex** is a Godot 4.6 (mono, .NET 8) **hex-chess roguelike** with a "Premium Slate" tournament-board look. The player commands a small army of ivory hex-chess pieces (`PieceKind`: Pawn, King, Rook, Bishop, Knight, Queen) on a 61-tile board (radius 4), clearing run after run of battles against black pieces. Battles are fought on an **ActiveTiles** subset that grows through the run (radius 2 → 3 → 4); the objective is to capture every enemy piece before the board **crumbles** from the outside in or the army is wiped. Between battles a procedural **shop** sells new pieces, rule-bending **Gambits**, and permanent per-coordinate **tile upgrades**. Every 4th battle is a **boss** — a normal battle plus exactly one `BossModifier` (Lockmaker / Tax Collector / Crumble Crown). Winning battle 12 wins the run.

**Run loop:** Title → New Run (3 random starter pieces, $4) → Battle → Shop → … → Boss every 4th → Victory/Game Over. The run's mutable state lives in one `RunState` (money, score, battle number, `Army`, `Reserve`, `Gambits`, `TileUpgrades`, plus run tallies `CapturesMade`/`PiecesLost`/`MoneyEarned` for the end screen). Army pieces auto-deploy at battle start on the home rows (positive R); reserve pieces can be deployed mid-battle for the player's action. A captured player piece is gone (Mercy Charter gambit can save the first each battle). Captures pay money (piece value + Gold-tile / gambit / boss bonuses, shown as a floating `+$N` Label3D) and score (`value × 100`); a battle clear pays `250 × battle` score and `4 + battle` money (+3 with Quartermaster). **Battle 12 always fields a guaranteed enemy Queen** ("the crown takes the field", paid from the normal enemy budget), and the final win adds +200 score per surviving army piece. ~25% of non-boss battles from battle 3 field a **themed roster** (PAWN HORDE / CAVALRY / BISHOP COURT), announced in the battle-start flourish. Best battle / high score / crowns (runs won) / tutorial-seen persist to `user://hex.cfg`; the sound toggle persists separately to `user://settings.cfg` (owned by `Sfx` — GameSession rewrites its file whole). All screens are Control overlays over a 3D board that never unloads.

**Fairness & turn resolution:** exactly one enemy piece acts per player action (capture-if-possible → approach nearest player piece → random). A candidate destination is painted red ("death tile") iff some non-stunned enemy could capture it next turn — computed exactly by re-running the enemy move generator against a hypothetical occupancy (zero-alloc, no RNG). Tapping an enemy piece paints its reach steel blue (read-only inspection); a HUD inspection chip names whatever was tapped (piece rule / enemy rule / tile-upgrade effect). Stunned enemies (Snare, Knight Fork) wear a snare-purple material until they recover. The enemy's answer resolves synchronously but its *visuals* lag (~0.22s move, ~0.32s strike) so exchanges read as call-and-response. The crumble telegraphs: the doomed ring is painted "cracked" two player actions before it collapses (three with Stonemason), and the inner radius-1 disc never crumbles. Three termination guarantees keep battles resolvable: (1) **auto-pass** — if the player has no legal move and no possible deploy, the turn passes (enemy acts, crumble ticks), with a 64-pass paralysis guard ending in a stalemate loss; (2) **stranded-pawn promotion** — a pawn whose every forward hex has left the board promotes on the spot (both sides, preferring kinds that can move from that tile); (3) **standoff adjudication** — once the crumble is spent, 16 actions with no piece death resolve the battle by remaining force (reserve counts, player wins ties). In `CheckBattleEnd` the loss condition is checked *before* the win so a mutual wipe never "wins" with an empty army.

The codebase is heavily optimized for low-spec mobile: **GL Compatibility renderer (no glow/SSAO/SSR/GPU-particles — see the design constraints)**, zero-alloc movement generation, shared static GPU resources, signal-based input gating. Preserve the zero-alloc invariants when touching gameplay code.

## Build & Run

- **Engine**: Godot 4.6.2 mono. Main scene is `res://scenes/game.tscn`. Open in editor and press F5.
- **Renderer**: GL Compatibility (`rendering/renderer/rendering_method = "gl_compatibility"`, both desktop and `.mobile`) — mobile-targeted. VRAM ETC2/ASTC compression on.
- **Headless typecheck**: `dotnet build Hex.csproj`. Project targets .NET 8 (`Godot.NET.Sdk/4.6.2`, `EnableDynamicLoading=true`, nullable disabled).
- **Headless playtest (run before committing gameplay changes)**:
  - `godot --headless --path . res://dev/autoplay.tscn -- runs=100` — the autoplay bot plays N complete runs through the real tap-input path (DEBUG-only hooks in `HexBoard.Debug.cs`, shared brain in `scripts/Dev/BotBrain.cs`); prints a per-battle difficulty table; exits non-zero on crash/softlock/inconsistency. Reference curve: ~25% bot win rate, battle-12 clear ~65%. Add `seed=<n>` to reproduce a failing run exactly (seeds both the bot and the board RNG).
  - `godot --headless --path . res://dev/uiflow.tscn` — boots the real `game.tscn` and presses the actual buttons through title/new-run/tutorial/pause/battle/shop/game-over. It stashes and restores `user://hex.cfg` itself on every exit path, so no external save backup is needed.
  - `godot --headless --path . res://dev/tests.tscn` — ~940 fast deterministic unit checks over the Godot-free logic (HexCoord, PieceRules incl. `PawnStranded`, EnemyPlanner, BattlePlanner, ShopOffers, RunState, Scoring). Run this first; it's instant.
  - `godot --path . res://dev/uiflow.tscn -- screenshots=<dir>` (windowed, NOT headless) — same flow, capturing PNGs at nine key screens (incl. a forced victory presentation and the first capture's +$N pop). Review the images after UI/visual changes — screenshot diffing has caught real bugs the headless harnesses can't see (stale highlight paint, clipped chips, hidden shop preview).
  - After adding assets run `godot --headless --path . --import` once.
- **Audio assets**: `audio/*.wav` are synthesized by `python dev/gen_sfx.py` (deterministic; regenerating leaves untouched sounds byte-identical). Don't hand-edit the WAVs.
- **Android**: `./run_android.sh` builds, installs, and launches a debug APK. Env vars: `BUILD_MODE=release`, `DEVICE=<serial>`, `LOGCAT=1`, `VERBOSE=1`. APK output: `build/android/hex-{debug,release}.apk`. `export_presets.cfg` is checked in.

## Autoloads (`project.godot` `[autoload]`)

- **`DebugLog`** (`scripts/DebugLog.cs`) — singleton. Installs an `OS.AddLogger` capturing every Godot `print` / `push_error` into a 500-entry ring buffer. Owns the top-right gear button → `SettingsModal` (sliding drawer) → `DebugModal` (full-screen log viewer with copy/clear).
- **`GameSession`** (`scripts/GameSession.cs`) — single source of truth for cross-screen state. Owns the live `RunState` (`CurrentRun`, created by `StartNewRun`/`RerollRun`). Persisted meta (via `ConfigFile` at `user://hex.cfg` `[progress]`): `BestBattle`, `HighScore`, `Crowns`, `TutorialSeen`. Loads on `_Ready`; saves on `CommitRun` (run end; `wonRun: true` increments Crowns), `MarkTutorialSeen`, and app-pause/close `_Notification`.
- **`Sfx`** (`scripts/Sfx.cs`) — sound service: preloads `audio/*.wav`, plays through a 6-player round-robin pool (`Sfx.Play(name, volumeDb)`, static like `Haptics`), loops the ambient pad on a dedicated player, and owns the persisted sound toggle (`user://settings.cfg`, exposed in `SettingsModal`). Safe no-op headless or with missing streams.

## Architecture

### Core Game Loop

1. **Init**: `GameScreen._Ready` (root bootstrap) builds the 3D stage *in code* — `WorldEnvironment` (slate `Color` bg, cool flat ambient, Filmic tonemap), warm key + cool fill + central soft `SpotLight3D`, a hexagonal **plinth** + matte stage floor the board rests on (grounding it out of the void and catching the spot as a pool of light), and a cinematic **~48° oblique** `Camera3D` (three-quarter view so the carved figurine pieces read) — then constructs `ScreenManager` and hands it the board, camera, session and UI root. `HexBoard._Ready` builds 61 tiles (`Area3D` + `MeshInstance3D` sharing one static `CylinderMesh` + `CylinderShape3D`). `ScreenManager` boots to **Title** (board shows a decorative `ShowPreview()` arrangement).
2. **New Run → battle**: `NewRunScreen` shows the starter army (reroll = `GameSession.RerollRun`); **BEGIN RUN** → `ScreenManager.NextBattle()` → `HexBoard.StartBattle(run)` — sets the active-tile mask from `BattlePlanner.ActiveRadius(battle)`, spawns both armies (player south / enemy north), applies the boss modifier, places tile-upgrade markers, arms the crumble timer.
3. **Player turn**: tapping a player piece selects it and paints legal moves gold (move), copper (capture) or **red (death tile)**, pulsed by one shared looped tween; the piece glows gold (emissive material). Tapping a highlighted tile moves; tapping a reserve button in the HUD arms deploy mode instead.
4. **Resolution** (`ExecuteMove` → `AfterPlayerAction`): commit move → capture (money/score/gambit effects/promotion/Bishop Echo) → win check → **one** enemy action (capture > approach > random; Royal Guard / Shield tiles can block) → crumble tick (crack → collapse outer ring) → loss check. `ScreenManager` listens to `MoneyChanged`/`ScoreChanged`/`EnemiesChanged`/`ArmyChanged`/`CrumbleChanged`/`ThreatChanged`/`StatusNote`/`DeployModeChanged`/`BattleWon`/`BattleLost` to drive the HUD and the Title→…→GameOver flow.
5. **Between battles**: `BattleWon` → `ShopScreen.Present(run)` (2 piece offers, 1 unowned gambit, 1 tile upgrade, paid reroll) → NEXT BATTLE. After battle 12: victory presentation of `GameOverScreen`.

### Hex Coordinate System

- `scripts/Hex/HexCoord.cs` — readonly struct with axial coords `(Q, R)` plus derived `S = -Q-R`. Implements arithmetic ops, distance, hash code, and equality. Six static direction vectors (`E`, `NE`, `NW`, `W`, `SW`, `SE`).
- **Zero-alloc invariant**: `Within(radius, List<HexCoord> output)` and `Ring(radius, List<HexCoord> output)` are direct-write overloads (the enumerable forms also exist, but hot paths use the buffer overloads). Honor this when adding new range queries.
- `scripts/Hex/HexLayout.cs` — static `ToWorld(HexCoord, y)`. `TileSize = 0.55f`. Flat-top hex math (√3 × 1.5 spacing).

### Chess Core (`scripts/Chess/`)

- `PieceRules.cs` — `PieceKind`/`PieceSide`, the `IBattleQuery` interface, the direction tables (6 rook edge dirs, 6 bishop diagonals, 12 knight leaps — validated in a `#if DEBUG` static ctor), the single zero-alloc `LegalMoves(kind, side, from, board, output)` generator, and `PawnStranded` (the stranded-promotion predicate). `PieceCatalog` holds names/monograms/descriptions/values/prices.
- `EnemyPlanner.cs` — the pure enemy decision (capture > approach > reservoir-random); HexBoard executes what it chooses. `Scoring.cs` — every score/pay formula (the single tuning surface). `ShopOffers.cs` — offer rolls shared by ShopScreen and the autoplay harness.
- `BattlePiece.cs` — plain data class per on-board piece (not a Node; `HexBoard` owns the `MeshInstance3D`). `PieceVisuals`: the **carved army** — real low-poly figurine OBJs from `res://models` (Quaternius rogue/cleric/wizard/warrior + a horse statue for the Knight + a Kenney hexagonal keep for the Rook) **baked once at load** (each source mesh normalised base-at-origin, centred, uniform-scaled to a per-kind height, merged to a single surface) into one `static readonly` mesh per kind, rendered in one tinted **carved-stone** material per side (ivory player / oxblood enemy) + snare-purple stunned + gold selected — shared GPU resources. The board faces the two armies at each other (enemy node yawed 180°); baking preserves the one-mesh-per-kind / one-material-per-side invariants.
- `Gambit.cs` / `TileUpgrade.cs` — enums + static catalogs (pure data; effects live at the single resolution point in `HexBoard` and are driven by `RunState`).
- `RunState.cs` — the run record (see overview). `BattlePlanner.cs` — active radius / crumble turns / enemy-army budget / `BossModifier` per battle.

### Board (`scripts/Board/HexBoard.cs`) — battle controller

- Stores tiles in `Dictionary<HexCoord, Tile>`; `HashSet<HexCoord>` masks for `_active`, `_cracked`, `_locked`; pieces in `List<BattlePiece>` + `Dictionary<HexCoord, BattlePiece> _occupied` (kill honors an identity check because a capturing mover claims the victim's coord first).
- Implements `IBattleQuery`; `IsDeathTile` swaps in a hypothetical occupancy (`_hypoFrom`/`_hypoTo`) rather than cloning anything.
- 3-color tile palette + inactive/cracked/locked materials, all `static readonly`; highlights are `MaterialOverride` swaps, never material instances.
- **Selection memoization**: legal moves + danger flags are recomputed only when the selected piece or the board `_stateStamp` changed.
- **Per-tile InputEvent Callables** are stored on each `Tile` and explicitly disconnected in `_ExitTree`. New per-tile signal hookups must follow this pattern or they leak on scene exit.
- `_movesBuffer` / AI + danger scratch lists are single reused buffers. Don't allocate inside movement/AI loops.

### UI (`scripts/UI/`)

- `GameScreen.cs` — root `Node3D` stage bootstrap (camera/lights/environment in code); constructs `ScreenManager`.
- `ScreenManager.cs` — state machine: Title / NewRun / Playing / Paused / Shop / GameOver. Gates `Viewport.PhysicsObjectPicking` per state (cooperating with the DebugLog modal), owns scrim/vignettes/camera drift/defeat shake. The danger vignette pulses while the board is cracking (`ThreatChanged`).
- `Hud.cs` — battle chip (battle/enemies/money), score, crumble countdown chip, centre status flourish, pause button, bottom reserve bar (deploy buttons). `NewRunScreen.cs`, `ShopScreen.cs`, `TitleScreen.cs`, `PauseOverlay.cs`, `GameOverScreen.cs` (defeat + victory presentations), `TutorialOverlay.cs`.
- All UI is procedural (no .tscn) using `UiTheme.cs` palette/Theme/factories/vignette.

### Debug Overlay

- `DebugModal` (full-screen) and `SettingsModal` (320px right-edge drawer, 0.22s Cubic tween) are both procedurally built.
- `DebugLog.PushModal()` / `PopModal()` track nesting depth; the `GameplayActiveChanged` event fires only on the 0↔1 transition. Use these wrappers when adding new modals so input gating works automatically.

## Performance Conventions (Hard Rules)

1. **Zero-alloc movement**: `PieceRules.LegalMoves` fills the caller's buffer. Never `new List<HexCoord>()` inside movement, AI, or danger-check code.
2. **Shared GPU resources**: tile mesh, tile collision shape, highlight/inactive/cracked/locked materials, upgrade-marker mesh+materials, and every piece mesh + side material are all `static readonly`. Don't instantiate per-tile / per-piece.
3. **Memoized selection**: keep the (piece, stateStamp) short-circuit in `Select` if you change the selection flow.
4. **Signal cleanup**: per-node Callables get disconnected in `_ExitTree`. Add new per-tile signal hookups to the existing pattern, not as anonymous lambdas.
5. **Diagnostic prints**: wrap `GD.Print` calls in `#if DEBUG` unless they're behind the `DebugLog` overlay (which captures them anyway).

## Existing Documentation

- `overview.html` — HTML reference with file map and hex math notes. **Stale**: still documents the pre-roguelike token hunt; refresh before trusting it.
- `PERFORMANCE_AUDIT.md` / `AUDIT.md` — audits of the previous (token hunt) codebase; the conventions they established still apply.

## Scene & Project Layout

| Path | Contents |
|------|----------|
| `scenes/game.tscn` | Single persistent scene — minimal: `GameScreen` (Node3D) + `HexBoard` (Node3D) + `UI` CanvasLayer + `Root` Control. Camera/lights/environment are built in code by `GameScreen`. |
| `scripts/Board/` | `HexBoard.cs` (battle controller: lifecycle, input/selection, deploy, resolution, crumble, win/loss), `HexBoard.Fx.cs` (presentation partial: pooled FX, pulse tweens, tile/piece repaint), `HexBoard.Debug.cs` (DEBUG-only harness hooks), `TileVisuals.cs` (shared tile/highlight/FX GPU resources) |
| `scripts/Hex/` | `HexCoord.cs`, `HexLayout.cs` |
| `scripts/Chess/` | `PieceRules.cs`, `BattlePiece.cs`, `Gambit.cs`, `TileUpgrade.cs`, `RunState.cs`, `BattlePlanner.cs` |
| `scripts/UI/` | `GameScreen.cs`, `ScreenManager.cs`, `UiTheme.cs`, `Hud.cs`, `TitleScreen.cs`, `NewRunScreen.cs`, `ShopScreen.cs`, `PauseOverlay.cs`, `GameOverScreen.cs`, `TutorialOverlay.cs` |
| `scripts/` (root) | `DebugLog.cs`, `GameSession.cs` (RunState owner + persistence), `Sfx.cs` (sound service), `Haptics.cs`, `DebugModal.cs`, `SettingsModal.cs` |
| `scripts/Dev/` | `BotBrain.cs` (shared bot decision logic), `AutoPlayDriver.cs` (full-run harness), `UiFlowDriver.cs` (screen-flow harness) — all DEBUG-only with release stubs |
| `dev/` | `autoplay.tscn`, `uiflow.tscn` (harness scenes), `gen_sfx.py` (WAV synthesis recipe) |
| `audio/` | Synthesized SFX + ambient pad WAVs (regenerate via `dev/gen_sfx.py`, never hand-edit) |
| `textures/ui/` | `gear.png` (vignette is a code shader, not an asset) |
| `models/` | Carved-army figurine OBJs (import as shared `Mesh`; baked to piece meshes at load by `PieceVisuals`). Sourced from `../Game_Assets` (Quaternius RPG Character Pack, Modular Dungeons horse statue, Kenney Castle Kit hex tower). Override the material — never rely on the bundled `.mtl`/textures. |
| `fonts/` | `Kurale-Regular.ttf` — the serif display face (logo / screen headings / card titles / monograms / centre flourish) via `UiTheme.Heading`; body & HUD numerals stay on the default sans |
| `android/` | Godot Android export template (do not hand-edit) |
| `playability_improvements.md` | Round-by-round worklog of the autonomous improvement loop (resume point for future sessions) |

**Premium Slate / Compatibility constraints (hard rules for visual work):** the GL Compatibility renderer has NO glow/bloom, SSAO, SSR, ReflectionProbe, SDFGI, volumetric fog, DOF, FXAA/TAA, or reliable GPU particles. The "glow" of the selected piece is an **emissive material**; the vignette is a **`canvas_item` shader** ColorRect; capture sparks use **`CpuParticles3D`**; AA is **`msaa_3d`** only (cosmetic — never make readability depend on it). Keep tiles at `RadialSegments = 6` (a hex grid, not a dodecagon).
