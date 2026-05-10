# PERFORMANCE_AUDIT — Hex

## Executive Summary

**Health Score: 6.5 / 10**

Hex is a Godot 4.6 / C# hex puzzle game on the GL Compatibility renderer (~1.5k LOC). The architecture is clean and small — `HexBoard` for game logic, `GameScreen` for UI, no shaders, no physics outside Area3D pickers. The bottlenecks are all GC-related: every `LegalMoves` query allocates a fresh `List<HexCoord>`, the token catalog full-instantiates 18 token trees just to read names, and viewport `PhysicsObjectPicking` runs globally even when modal menus are open.

**Profile**
- Godot 4.6, C# (11 scripts, 1,454 LOC)
- Renderer: `gl_compatibility`
- Scenes: 1 (`game.tscn`)
- Shaders: 0
- Autoloads (2): `GameSession`, `DebugLog`
- Viewport: 1280×720, `canvas_items` stretch
- Physics surfaces: ~37–61 Area3D nodes (board hex tiles)

**Top 3 Wins (impact-per-hour)**
1. Replace `List<HexCoord>` returns from `LegalMoves` with a yielded enumerator or a reusable buffer — kills the largest GC source.
2. Build a static metadata table (`TokenName`/`TokenDescription`) so the picker UI doesn't instantiate full Token trees just to read strings.
3. Toggle `PhysicsObjectPicking` off when modal menus are open — removes per-frame collision evaluation across all Area3D tiles.

---

## Identified Issues

### Memory / GC

**1. [HIGH] Per-call `List<HexCoord>` allocation in `LegalMoves`**
`scripts/Tokens/Tokens.cs:40-350` (across all 18 token types)
```csharp
public override IEnumerable<HexCoord> LegalMoves(HexCoord from, int boardRadius) {
    var moves = new List<HexCoord>();
    foreach (var h in HexCoord.Within(2)) moves.Add(from + h); // up to 19 entries
    return moves;
}
```
Worst offenders: Spiral (~19 entries), Drifter (~30+), Orbit (loop-driven). Called every time a tile is selected.

**2. [MEDIUM] Token catalog instantiates 18 full tokens just to read names**
`scripts/UI/GameScreen.cs:47-50`
```csharp
for (int i = 0; i < TokenCatalog.All.Count; i++) {
    var preview = TokenCatalog.All[i]();   // allocates full Token tree
    var description = preview.Description;  // only this string is needed
}
```
*Impact:* Startup stutter; 18 disposed `MeshInstance3D` + `StandardMaterial3D` allocations.

**3. [MEDIUM] Double instantiation on token swap**
`scripts/UI/GameScreen.cs:66-70` + `scripts/Board/HexBoard.cs:57-71`
GameScreen calls `TokenCatalog.All[index]()` (one allocation), then `HexBoard.SetToken()` causes `Token._Ready` to allocate another mesh.

### CPU — Hot Path Math

**4. [MEDIUM] Repeated `DistanceFromOrigin` inside tight loops**
`scripts/Tokens/Tokens.cs:61` (Runner — also Drifter)
```csharp
foreach (var d in HexCoord.Directions) {
    var pos = from + d;
    while (pos.DistanceFromOrigin() <= boardRadius) {  // recomputed each step
        moves.Add(pos);
        pos += d;
    }
}
```
`DistanceFromOrigin()` calls `Math.Abs(Q) + Math.Abs(R) + Math.Abs(S)` where `S` is derived (2 extra Math ops).

### Rendering / Physics

**5. [MEDIUM] Viewport physics picking always on**
`scripts/UI/GameScreen.cs:41`
```csharp
GetViewport().PhysicsObjectPicking = true;
```
Set in `_Ready` and never gated. Forces per-frame collision shape evaluation against all 37+ Area3D tiles even when menus or modals cover the board.

### State Management

**6. [MEDIUM] Tile highlight rebuild even when re-selecting same piece**
`scripts/Board/HexBoard.cs:171-176, 187-190`
Selecting the same token re-runs `ClearHighlights` + `BeginSelect`, allocating a new `_highlighted` HashSet contents and reassigning materials per tile.
```csharp
foreach (var h in _highlighted)
    if (_tiles.TryGetValue(h, out var t))
        t.Mesh.MaterialOverride = t.BaseMaterial;
```

### Lifecycle

**7. [LOW-MEDIUM] No signal disconnect on token change**
`scripts/Board/HexBoard.cs:59-61`
Old token `QueueFree`'d but Area3D `InputEvent` connections (line 130) are not explicitly disconnected. Cleanup happens at next frame; risk of ghost inputs during stutters.

### Misc

**8. [LOW] `DistanceFromOrigin` derives S each call**
`scripts/Hex/HexCoord.cs:53` — could be precomputed.

---

## Remediation Plan

### Quick Wins (≤ 2 hours)

**Fix #5 — Gate physics picking on UI state**
```csharp
public void SetGameplayActive(bool active) {
    GetViewport().PhysicsObjectPicking = active;
}
// Call SetGameplayActive(false) when opening modal/menu, true on close.
```
Wire to whichever signal already drives modal visibility.

**Fix #4 — Cache distance, increment instead of recompute**
For Runner-style direction walks, increment a cumulative distance instead of recomputing:
```csharp
foreach (var d in HexCoord.Directions) {
    var pos = from + d;
    int dist = 1;
    while (dist <= boardRadius) {
        moves.Add(pos);
        pos += d;
        dist++;
    }
}
```
For tokens that genuinely need `DistanceFromOrigin`, store it on `HexCoord` once via constructor or have `HexCoord` expose a precomputed `S` field rather than `-Q-R` on each call.

**Fix #6 — Idempotent selection guard**
```csharp
public void BeginSelect() {
    if (_lastSelectedToken == _token && _selectionValid) return;
    ClearHighlights();
    // ... compute legal moves ...
    _lastSelectedToken = _token;
    _selectionValid = true;
}
```

**Fix #7 — Explicit disconnect on token swap**
In `HexBoard.SetToken`, before `QueueFree`:
```csharp
if (_token != null) {
    foreach (var t in _tiles.Values) {
        if (t.InputEvent.IsConnected(callable))
            t.InputEvent -= callable;
    }
    _token.QueueFree();
}
```

### Medium (4–8 hours)

**Fix #1 — Pooled buffer or yielded enumerator**

Option A — caller-supplied buffer (lowest GC):
```csharp
public abstract void LegalMoves(HexCoord from, int boardRadius, List<HexCoord> output);

// In Spiral:
public override void LegalMoves(HexCoord from, int boardRadius, List<HexCoord> output) {
    output.Clear();
    foreach (var h in HexCoord.Within(2)) output.Add(from + h);
}
```
`HexBoard` keeps a single `private List<HexCoord> _movesBuffer = new(32);`, passes it in, reads it, never frees it.

Option B — `IEnumerable<HexCoord>` via `yield return`:
```csharp
public override IEnumerable<HexCoord> LegalMoves(HexCoord from, int boardRadius) {
    foreach (var h in HexCoord.Within(2))
        yield return from + h;
}
```
Still allocates an enumerator object but eliminates the list backing array. Option A is preferred for the deepest hot paths.

**Fix #2 — Static token metadata table**
```csharp
public readonly record struct TokenInfo(string Name, string Description, Func<Token> Factory);

public static class TokenCatalog {
    public static readonly TokenInfo[] All = new[] {
        new TokenInfo("Runner", "Moves N spaces in a direction", () => new Runner()),
        new TokenInfo("Spiral", "Moves within 2 hexes",          () => new Spiral()),
        // ...
    };
}
```
Picker UI iterates `All` reading `Name`/`Description` only; calls `Factory()` only when the player commits a selection.

**Fix #3 — Single token allocation path**
Remove the GameScreen-side `TokenCatalog.All[index]()` allocation; pass the index to `HexBoard.SetToken(int index)`, which is the sole creator.

### Project Settings to Review

`project.godot`:
- Confirm `rendering/textures/canvas_textures/default_texture_filter` matches the visual target.
- `physics/3d/default_gravity` — irrelevant if no physics bodies; leave default.

---

## Verification

1. **Selection GC**: in Mono profiler, select tokens 50× rapidly. After Fix #1, Gen0 collections should drop by ~80%.
2. **Startup time**: log `OS.GetTicksMsec()` in `GameScreen._Ready` before/after Fix #2 — expect a measurable drop (no 18 token instantiations).
3. **Picking overhead**: open the menu modal and watch `Time/Physics Process` in Monitor; after Fix #5, physics process cost should drop to near zero while menu is open.
4. **Re-selection**: tap the same piece 5 times; with Fix #6, only the first tap should trigger highlight rebuild (instrument with a static counter).
5. **Token swap stutter**: cycle tokens 30 times; Fix #3 should remove visible stutter (count `MeshInstance3D` allocations in profiler).
