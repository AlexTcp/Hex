# Performance Audit — Hex

**Engine:** Godot 4.6 (GL Compatibility renderer, C#)
**Audit Date:** 2026-05-12

---

## Executive Summary

**Health Score: 8.5 / 10**

The smallest project in your set, and architecturally one of the tightest. `HexCoord` is a `readonly struct` with proper equality/hashing; `Within` / `Ring` both expose buffer-filling overloads ([HexCoord.cs:62, 73](scripts/Hex/HexCoord.cs#L62)) alongside the enumerable versions; every Token subclass exposes `static readonly` shared `Mesh` and `StandardMaterial3D` ([Tokens.cs:43-46](scripts/Tokens/Tokens.cs#L43) and identically across all 18 tokens) so the rendering side already batches by piece type; `HexBoard` preallocates `_movesBuffer` with capacity 64 and reuses it ([HexBoard.cs:36](scripts/Board/HexBoard.cs#L36)); the BeginSelect path memoises `(token, pos)` and skips rebuild if the selection hasn't changed ([HexBoard.cs:234](scripts/Board/HexBoard.cs#L234)).

There's only one large category of remaining issues: diagnostic `GD.Print` calls on every input event. The `[DIAG-IN]`, `[DIAG-TILE]`, `[DIAG-TAP]` strings fire on every touch / mouse event, allocating formatted strings and writing to the logger. On Android these are visible in the frame profiler.

Beyond that, the per-tile `CylinderMesh`/`CylinderShape3D` allocation in `BuildTile` ([HexBoard.cs:151-168](scripts/Board/HexBoard.cs#L151)) creates a unique mesh per tile when one shared mesh would suffice — minor at radius 4 (61 tiles) but trivially avoidable.

---

## Identified Issues

### CPU Overhead

1. **[HexBoard.cs:90](scripts/Board/HexBoard.cs#L90), [98, 103](scripts/Board/HexBoard.cs#L98), [197, 204, 210](scripts/Board/HexBoard.cs#L197) — diagnostic `GD.Print` per input event.**
   `_Input` logs on every touch and mouse event regardless of pickup. `OnTileInput` and `OnTileTapped` log per tap. Each call allocates a formatted string (interpolation builds a `string.Format` payload) and writes to the engine logger. With multitouch this can fire dozens of times per second.

2. **[HexBoard.cs:175-177](scripts/Board/HexBoard.cs#L175) — `Callable.From` closure captures `coord` per tile.**
   Per tile, a delegate is allocated wrapping the captured `HexCoord` and connected to `Area3D.InputEvent`. At radius 4 that's 61 closures; at radius 6 it's 127. Acceptable since `BuildBoard` runs once, but the closure is unnecessary — `Area3D.InputEvent` provides the world-space hit point. You can extract the `coord` via `_tiles` reverse lookup from the hit Area3D, eliminating the per-tile closure.

3. **[HexBoard.cs:151-157](scripts/Board/HexBoard.cs#L151) `BuildTile` allocates a fresh `CylinderMesh` per tile.**
   Every tile shares identical mesh geometry. Promote to a `static readonly CylinderMesh SharedTileMesh = …;` and reference it from each `MeshInstance3D`. With shared mesh + shared material, GL Compatibility *should* batch tile draws into a single command (verify with `Visible Surface` debug).

4. **[HexBoard.cs:164-167](scripts/Board/HexBoard.cs#L164) `CylinderShape3D` allocated per tile.**
   Same argument — promote to a shared static `CylinderShape3D`. The physics broadphase already deduplicates shared shapes.

5. **`Token.LegalMoves` allocation pattern.**
   `Jumper.LegalMoves` ([Tokens.cs:80-86](scripts/Tokens/Tokens.cs#L80)) calls `HexCoord.Ring(2, output)` then walks the output adjusting in place — good. `Spiral` / `Drifter` use `HexCoord.Within` similarly — good. `Filter` (defined on `Token`, not shown) must avoid allocating; verify it uses `RemoveAt` or two-pointer compaction inside the same list.

6. **`MoveTokenTo`** creates a Tween per move ([HexBoard.cs:275](scripts/Board/HexBoard.cs#L275)) — per move, not per frame, so the cost is bounded by player tempo. Fine.

### Memory / GC

1. **`HexCoord.Within(int radius)` (`IEnumerable<HexCoord>`)** ([line 87](scripts/Hex/HexCoord.cs#L87)) allocates an enumerator state machine per use. The buffer-filling overload ([line 62](scripts/Hex/HexCoord.cs#L62)) is already used by `BuildBoard` and by `Spiral` / `Drifter` — good. Audit Token subclasses to ensure none call the enumerable version.

2. **No per-frame allocation hotspots** beyond the diagnostic prints.

### Rendering

1. **GL Compatibility renderer** is appropriate for this minimalist 3D look — no change. `etc2_astc` texture compression is on (mobile-ready).

2. **Per-tile `MeshInstance3D` with `MaterialOverride`** — the override is one of three checker materials ([HexBoard.cs:69-74](scripts/Board/HexBoard.cs#L69)), so draw calls collapse to three checker groups + one highlight group. Combined with the shared-mesh fix above, this gives you a 4-call board.

3. **`Tile.HighlightMaterial = HighlightMaterialShared`** ([line 149](scripts/Board/HexBoard.cs#L149)) — every tile points at the same highlight material. Toggling `MaterialOverride` between base and highlight is the correct approach; no need to clone.

4. **No environment / lights configured in `game.tscn`** that I could verify here. Default Godot 4 setup has one DirectionalLight3D in the scene template — fine.

5. **No shaders in this project.** All visuals via `StandardMaterial3D`. Good for GL Compatibility compatibility.

### Collision / Physics

1. **Per-tile `Area3D` + `CollisionShape3D` + `CylinderShape3D`** at radius 4 = 61 physics objects. Acceptable. At radius 6 = 127. Still acceptable.

2. **Picking via `Viewport.PhysicsObjectPicking`** is correct for touch-driven boards. Make sure the viewport flag is set in [scenes/game.tscn](scenes/game.tscn) — the diagnostic line in `_Input` already prints `vp.PhysicsObjectPicking`, suggesting it was investigated.

3. **No `_PhysicsProcess`** anywhere in the project. Good.

---

## Remediation Plan

### Priority 1 — Strip diagnostic prints

**P1.1 Gate `GD.Print` behind `[Conditional("DEBUG")]`.**
Add to `DebugLog` or a project-wide helper:
```csharp
[System.Diagnostics.Conditional("DEBUG")]
public static void Trace(string s) => GD.Print(s);
```
Replace `GD.Print(...)` in [HexBoard.cs:90, 98, 103, 197, 204, 210](scripts/Board/HexBoard.cs#L90) with `DebugLog.Trace(...)`. The JIT will erase the calls in release builds.

Alternatively, surround the `_Input` body with `#if DEBUG`:
```csharp
public override void _Input(InputEvent @event)
{
#if DEBUG
    if (@event is InputEventScreenTouch st) GD.Print(…);
    else if (@event is InputEventMouseButton mb) GD.Print(…);
#endif
}
```

### Priority 2 — Share tile mesh and shape

**P2.1 Share `CylinderMesh` and `CylinderShape3D` across all tiles.**
```csharp
private static readonly CylinderMesh SharedTileMesh = new()
{
    TopRadius = HexLayout.TileSize * 0.95f,
    BottomRadius = HexLayout.TileSize * 0.95f,
    Height = 0.15f,
    RadialSegments = 6,
};
private static readonly CylinderShape3D SharedTileShape = new()
{
    Radius = HexLayout.TileSize * 0.95f,
    Height = 0.15f,
};
```
In `BuildTile`, use these directly:
```csharp
tile.Mesh = new MeshInstance3D
{
    Mesh = SharedTileMesh,
    MaterialOverride = tile.BaseMaterial,
};
var collision = new CollisionShape3D { Shape = SharedTileShape };
```
Cuts per-board allocations from `61 × 2 = 122` resource objects down to 2. Also enables better GPU batching.

### Priority 3 — Optional: remove per-tile Callable closures

**P3.1 Replace per-tile InputEvent connection with a single Board-level pick handler.**
Connect to the board's `InputEvent` once, derive the tapped coord from the hit `Area3D` via a reverse `Dictionary<Area3D, HexCoord>`:
```csharp
private readonly Dictionary<Area3D, HexCoord> _areaToCoord = new();
// in BuildTile: _areaToCoord[tile.Area] = coord;
// in BuildBoard or _Ready: connect once at board level (or keep per-tile but with a non-capturing handler).
```
Skip if (P2.1) already fixed batching and per-tile closure cost is negligible (61 delegates is ~3 KB total).

### Priority 4 — Audit Token.Filter

**P4.1 Verify `Token.Filter` is allocation-free.**
The method is the post-pass that clamps moves to the board radius. Read [scripts/Tokens/Token.cs](scripts/Tokens/Token.cs). It should compact the supplied `List<HexCoord>` in place — typically a two-pointer pass. If it uses `RemoveAll(predicate)` with a lambda, that lambda allocates a delegate per call; convert to a hand-rolled `Where` pass or a `Predicate<HexCoord>` cached as a static field.

---

## Suggested order of operations

1. P1.1 (strip prints) — 30 minutes.
2. P2.1 (share tile mesh / shape) — 15 minutes.
3. P4.1 (audit Filter) — 15 minutes to read, 30 minutes to refactor if needed.
4. P3.1 — opportunistic; only matters if you scale board radius past ~8.
