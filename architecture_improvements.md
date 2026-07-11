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
