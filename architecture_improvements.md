# Architecture Improvements — Worklog

_Started: 2026-07-10 (post-playability-loop; behavior-preserving refactors only)_

Verification battery per change: `dotnet build Hex.csproj` +
`godot --headless --path . res://dev/autoplay.tscn -- runs=60` (0 failures required) +
`godot --headless --path . res://dev/uiflow.tscn` (PASS required, save backed up/restored) +
headless boot. Reference bot win rate ~20–30% (statistical, not exact).

## Round 1

- [x] **Kill shop-logic duplication** — `Chess/ShopOffers.cs` (RollPieceOffer with the
      Queen gate, RollUpgradeCoord over the central disc) + `RunState.AddPiece`
      (army-or-reserve). ShopScreen and AutoPlayDriver both retargeted; their private
      copies and "mirrors ShopScreen" comments deleted.
- [x] **Extract the enemy AI decision** — `Chess/EnemyPlanner.ChooseAction` is now the pure
      capture>approach>reservoir choice (verbatim logic, same RNG call order); HexBoard's
      EnemyAct shrank to stun bookkeeping + execution handoff.
- [x] **Single tuning surface for the economy** — `Chess/Scoring.cs` (capture/clear/survivor
      score, clear pay + Quartermaster bonus); HexBoard call sites converted 1:1.
- [x] **Move tile GPU resources out of HexBoard** — `Board/TileVisuals.cs` holds all shared
      tile/highlight/FX materials and meshes (PieceVisuals pattern); HexBoard consumes via
      `using static` so resolution code is textually unchanged. HexBoard: 1,437 → ~1,190
      lines.
- [x] **Pure-logic unit tests** — `dev/tests.tscn` + `UnitTestRunner.cs`: 937 deterministic
      checks over HexCoord (disc/ring counts, uniqueness, invariants), PieceRules (movegen
      counts, pawn blocking/capture, slider blocking on a fake board), EnemyPlanner
      (capture choice, value priority, stun, approach), BattlePlanner (boss schedule,
      crumble turns, roster determinism, finale queen, themed single-kind), ShopOffers
      (queen gate, claimed-disc null), RunState/Scoring. One test authoring error found and
      fixed during bring-up (target pawn accidentally on the rook's file). 0 failures.
- [x] **Verified** — build clean; 937/937 unit checks; 80 autoplay runs 0 failures (27
      wins); UI-flow PASS; headless boot clean.

## Round 2

- [x] **Split HexBoard's FX/visual plumbing into a partial** — `Board/HexBoard.Fx.cs` now
      holds the pooled FX nodes, highlight-pulse tweens, tile/piece material refreshers and
      the shop preview hook (verbatim move). Main file ~940 lines, resolution-focused.
      (A bulk PowerShell find/replace corrupted em-dashes in five files mid-round —
      reverted those files to HEAD and replayed every change through the Edit tool.)
- [x] **Namespace the debug overlay** — DebugLog/DebugModal/SettingsModal moved into
      `namespace HexGame`; autoload bindings unaffected.
- [x] **BotBrain mode enum** — `BotMode { Normal, Suicidal, Stall }` replaces the two
      mutually-exclusive bools; UiFlowDriver call sites converted.
- [x] **Typo-proof audio cues** — `SfxCue` enum (array-indexed streams, name-derived wav
      paths); all 15 call sites converted; a mistyped cue is now a compile error instead of
      a silent no-op.
- [x] **Honest Hud API** — `SetArmy(onBoard, reserve)` (which ignored both args) is now
      `RefreshReserve()`; ScreenManager's signal handler updated; class-header doc synced.
- [x] **Verified** — build clean; 937/937 unit checks; 80 autoplay runs 0 failures (24
      wins); UI-flow PASS; no mojibake anywhere in scripts/.

## Round 3

- [x] **UI-flow harness backs up the save itself** — UiFlowDriver stashes `user://hex.cfg`
      bytes at start and restores (or deletes) on every exit path (pass/fail/exception);
      `GameSession.SavePath` made public const so the path has one source. Verified: run
      with NO external wrapper leaves the save hash byte-identical.
- [x] **Split UiFlowDriver.RunFlow** — now a ~25-line orchestrator over BootGame /
      PhaseTitleToFirstBattle / PhaseInspectionShots / PhaseWinPath / PhaseBoardStateShots /
      PhaseDeliberateDefeat (behavior identical; early-return semantics preserved).
- [x] **Testable stranded-pawn rule** — `PieceRules.PawnStranded(side, from, activeTiles)`
      extracted; HexBoard.PromoteStrandedPawns consumes it; 4 new unit checks (both sides,
      both edges). Suite now 941 checks.
- [x] **CLAUDE.md sync** — harness docs (self-backing uiflow, tests.tscn), Chess/ additions,
      HexBoard partial layout, TileVisuals row.
- [x] **Verified** — build clean; 941/941 unit checks; UI-flow PASS with save hash
      preserved sans wrapper; 80 autoplay runs 0 failures (25 wins).

## Round 4

- [x] **Prune dead code (grep-verified zero readers)** — removed `Tile.CheckerIndex`, the
      `HexCoord.Ring(int)` IEnumerable overload, `UiTheme.Heading`/`Body` factories, and
      `DebugModal.Instance` (with its now-empty `_ExitTree`).
- [x] **Extract CameraDirector from ScreenManager** — `UI/CameraDirector.cs` plain class
      (Drift/StopDrift/Restore/Shake/Cleanup, tweens bound to the camera node); the
      state machine delegates. ScreenManager loses ~55 lines and one responsibility.
- [x] **Verified** — build clean; 941/941 unit checks; 80 autoplay runs 0 failures;
      UI-flow PASS; boot clean; no mojibake.

## Round 5

- [x] **Audio mix in one table** — `Sfx.CueVolumeDb` holds per-cue defaults; `Play(cue)`
      overload added; 13 call sites dropped their dB literals (the board-select −10 and
      enemy-reach −14 variants intentionally keep overrides). Identical mix values.
- [x] **Reproducible harness runs** — `seed=` user arg seeds the bot RNG and the board's
      battle RNG (DEBUG-only `DebugSeedRng`; the field lost `readonly`). Verified: two
      `runs=5 seed=42` sweeps produced bit-identical outcomes. Documented in CLAUDE.md.
- [x] **Verified** — build clean; 941/941 unit checks; 80 autoplay runs 0 failures;
      UI-flow PASS.

## Round 6

- [x] **Pure win/loss decision** — `Chess/BattleReferee.Decide(players, reserve, enemies)`
      now owns the loss-before-win ordering; CheckBattleEnd consumes. Unit-locked incl.
      the mutual-wipe-is-a-loss case (the historical zombie-army bug's invariant).
- [x] **Pure standoff adjudication** — `BattleReferee.PlayerWinsStandoff` (reserve counts,
      dead pieces excluded, ties to the player); ResolveStandoff consumes; unit-locked.
- [x] **Crack grace joins the pacing module** — `BattlePlanner.CrackGrace(run)` next to
      CrumbleTurns; HexBoard delegates; unit-locked (base 2, Stonemason 3, null-safe).
- [x] **Verified** — build clean; 954/954 unit checks; 80 autoplay runs 0 failures;
      UI-flow PASS.
