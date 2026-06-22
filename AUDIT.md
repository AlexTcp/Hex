# Hex — Code Review Audit

**Scope:** `G:\Hex\scripts\**\*.cs` (gameplay + UI). Excluded `\addons\`, `\.godot\`, `\obj\`, `\bin\`, `\android\build\`, generated `*.g.cs`/`*.Designer.cs`. No `.audit-ignore` present. The `.gd` files all live under `android\build\` (excluded).
**Files reviewed:** 21 C# source files (~3.4k lines).
**Min confidence kept:** 80. **`--fix` ran:** yes (1 mechanical fix applied; rest report-only).

This is a small, unusually well-engineered project: zero-alloc movement, shared static GPU resources, disciplined signal cleanup in `_ExitTree`, bounded scores/waves, non-negative resource invariants. The fail-state math (`IsDeathTile`, mercy, spawn-grace) is correct and matches the spec. Very few findings cleared the bar; most candidates were intentional, documented approximations.

---

## Reuse / Dead code

### 1. `DebugLog.OpenModal` is unused — LOW · effort S · confidence 90 · FIXED
[scripts/DebugLog.cs:152](scripts/DebugLog.cs#L152)
`private void OpenModal() => _modal?.Open();` is never called. The gear button routes through `OpenSettings`, and the Settings drawer opens the log via the `() => _modal?.Open()` callback passed to `SettingsModal`. Dead private method.
**Reason:** mechanical, behavior-preserving deletion. Applied (`.bak` written first).

---

## Report-only (below 80, or behavior-changing — left unmodified)

These were examined and deliberately **not** fixed. Listed for transparency.

- **Telegraph ignores mercy** — [scripts/Board/HexBoard.cs:622](scripts/Board/HexBoard.cs#L622). `ShowTelegraph` predicts Chase steps with `mercy: false`; if `_mercyThisTurn` is active for the upcoming resolution, the ghost marker shows a step onto the player that the hunter won't actually take. Cosmetic only; mercy is a rare edge. Confidence ~70. Behavior-affecting — report-only.
- **`_inDanger` not reset on run start** — [scripts/Board/HexBoard.cs:82](scripts/Board/HexBoard.cs#L82). `StartRun`/`SetToken` don't reset the cached `_inDanger`; a run that ends while in danger leaves it `true`. Self-correcting: the next `SpawnWave`→`UpdateThreat` re-emits on the real flip (wave 1 has no Chasers, so danger=false is emitted correctly). No observed user-facing defect. Confidence ~60.
- **`Filter` board-bounds via `DistanceFromOrigin()`** — [scripts/Tokens/Token.cs:91](scripts/Tokens/Token.cs#L91). Correct only because the board is a radius-`Radius` hexagon centered at `(0,0)`. Callers also re-validate with `_tiles.ContainsKey`, so it's safe and consistent today; flagged as an implicit coupling to "board is centered at origin," not a bug.
- **`Spiral`/`Drifter` are flood discs, not spirals/drifts** — [scripts/Tokens/Tokens.cs:201](scripts/Tokens/Tokens.cs#L201). Movement rules (`Within(2)` / `Within(3)`) match their catalog descriptions exactly; only the flavor names imply otherwise. Design choice, not a defect.

---

## Lenses with no qualifying findings

- **Correctness / game-rule invariants:** score, combo (`min(combo,5)`), wave count, enemy count all bounded and non-negative; capture/spawn/wave-clear ordering in `MoveTokenTo` is correct; mercy + spawn-grace honored.
- **CLAUDE.md compliance:** zero-alloc movement preserved, shared `static readonly` GPU resources, selection memoization intact, per-tile Callables disconnected in `_ExitTree`, diagnostic `GD.Print` wrapped in `#if DEBUG`. No violations.
- **Godot correctness / lifecycle:** signal subscribe/unsubscribe balanced in `ScreenManager`; tweens killed in `_ExitTree`; `IsInstanceValid` guards before touching pooled nodes; `ConfigFile` load tolerates first-run (no throw).
- **Resource/lifecycle leaks:** none found — enemies, telegraph nodes, rings, particles are all pooled or `QueueFree`d.
- **Signal & call-graph wiring:** all 7 `HexBoard` signals are wired and torn down.

## Test gaps (named, not blocking)
No unit tests exist for the pure, highly-testable logic. Highest-value untested unit: **`HexCoord.Distance` / `Within` / `Ring`** (foundational hex math) and **`Token.Filter`** (two-pointer compaction + bounds). These are deterministic and alloc-sensitive — ideal regression targets.
