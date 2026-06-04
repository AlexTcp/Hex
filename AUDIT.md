# AUDIT.md — Hex

- **Run:** `/audit --fix` (all-projects sweep), 2026-06-02
- **Scope:** all `scripts/**/*.cs` (12 files, ~2.2k LOC). Excluded `obj/`, `bin/`, `.godot/`, `*.g.cs`, `android/`.
- **Min-confidence:** 80. **--fix:** applied. **Result: 0 findings at/above cutoff — clean pass.**
- **Method:** the entire gameplay core was read directly and verified: `HexCoord` (axial math, `Within`/`Ring`/`Distance`), all 14 token movement rules + `Token.Filter`, and `HexBoard` (input → select → move, the enemy/wave/combo/capture system, tween + signal cleanup).

## Findings kept (≥80)
**None.** This is a small, perf-audited codebase and the gameplay logic held up under direct review.

## Verified-clean (explicitly checked)
- **`HexCoord`** — canonical Red-Blob ring walk (start `Directions[4]*radius`, 6 sides), correct axial `Within` range, `(|q|+|r|+|s|)/2` distance, `Q,R`-based equality/hash (S derived). Correct.
- **All 14 token `LegalMoves`** (Walker/Runner/Jumper/Halo/Knight/Camel/Stepper/Hopper/Spiral/Charger/Diamond/Glider/Skipper/Drifter) — offsets correct, all route through `Filter` (drops origin tile + out-of-bounds via `DistanceFromOrigin`); slides bound by `boardRadius`; zero-alloc buffer reuse honored.
- **`HexBoard`** — selection memoization `(token,pos)` short-circuit; per-tile `InputEvent` Callables disconnected in `_ExitTree`; both `_ringTween` and `_pulseTween` killed on exit (`StopHighlightPulse`); diagnostic `GD.Print`s all `#if DEBUG`-gated (CLAUDE.md perf rule honored).
- **Enemy system (undocumented new code)** — sequential `AdvanceEnemies` with immediate position update + `IsEnemyAt(n, index)` guard guarantees no two enemies share a tile and never step onto the player; `ComputeReachable` BFS over the token's own move graph guarantees enemies only spawn on solvable tiles (e.g. avoids parity-locked Jumper dead spawns); `TryCapture` removes exactly one enemy per tile (tiles hold ≤1 enemy by construction).

## Sub-threshold / informational (NOT a finding)
- **Telegraph vs. actual enemy resolution can diverge.** `PredictEnemyStep` is called independently per enemy (from current positions) for the move preview, while `AdvanceEnemies` resolves sequentially (updating positions as it goes). When two enemies' moves would block each other, the previewed step can differ from the executed one. Preview-accuracy imperfection only — game state stays correct. Conf ~50, below cutoff; left as-is.

## Test gaps (no test suite)
- **Token `LegalMoves` correctness** — a table-driven test (token × from-position → expected destination set, incl. board-edge clamping) would lock the puzzle rules.
- **`ComputeReachable` solvability invariant** — assert every spawned enemy tile is in the token's reachable set for each of the 14 tokens (the property the design depends on).
- **`AdvanceEnemies` no-overlap / no-onto-player invariant** under dense enemy counts.

## Newly-confirmed finding keys
_(none)_
