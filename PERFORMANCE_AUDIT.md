# PERFORMANCE_AUDIT — Hex

## Executive Summary

**Health Score: 7.5 / 10**

Hex is a small, well-shaped Godot 4.6 / C# hex-puzzle project on the GL Compatibility renderer (~1.5k LOC across 11 scripts). The prior audit's three HIGH findings — per-call `List<HexCoord>` allocations in `LegalMoves`, 18-token catalog instantiation, and always-on viewport picking — have all been resolved: tokens now write into a caller-supplied `_movesBuffer`, `TokenCatalog.All` is a static `TokenInfo[]` with name/description strings and lazy factories, and `GameScreen` toggles `Viewport.PhysicsObjectPicking` off via the `DebugLog.GameplayActiveChanged` event when any modal pushes. What remains is a tail of low-impact GC sources (iterator-allocating `HexCoord.Within`/`Ring`, a slightly undersized `_movesBuffer` that resizes on `Drifter`) and a few minor lifecycle items.

**Profile**
- Godot 4.6, C# — 11 scripts, 1,484 LOC
- Renderer: `gl_compatibility` (mobile + desktop)
- Scenes: 1 (`scenes/game.tscn`)
- Shaders: 0 (all `StandardMaterial3D`)
- Autoloads (2): `DebugLog`, `GameSession`
- Viewport: 1280x720, `canvas_items` stretch, expand aspect
- Main scene: 1 `DirectionalLight3D` (no shadow flag set), 1 `WorldEnvironment` (ambient only), 1 `Camera3D`, 1 `CanvasLayer` UI tree
- Physics surfaces: 61 `Area3D` nodes at `Radius = 4` (1 + 6*(1+2+3+4))
- No `_Process`, `_PhysicsProcess`, `_Draw`, `_Input`, `_UnhandledInput`, `IntersectRay`, `GetChildren`, or `EmitSignal` calls anywhere in `scripts/`

**Top 3 Wins (impact-per-hour)**
1. Bump `_movesBuffer` initial capacity from 32 to 64 and convert `HexCoord.Within`/`Ring` from `yield return IEnumerable` to a custom `struct` enumerator — the only remaining per-selection heap traffic.
2. Cache `Token.CreateMesh()` outputs and the highlight `StandardMaterial3D` as static fields so swapping tokens / building the board doesn't re-allocate identical resources.
3. Disconnect the `Area3D.InputEvent` lambdas in `_ExitTree` (or capture the `Callable` once and disconnect by reference) — pure correctness, not throughput, but prevents dangling closures during scene swaps.

---

## Identified Issues

### Memory / GC

**1. [LOW-MEDIUM] `HexCoord.Within` / `HexCoord.Ring` allocate an enumerator object per call**
`scripts/Hex/HexCoord.cs:62-85`
```csharp
public static IEnumerable<HexCoord> Within(int radius) { ... yield return ...; }
public static IEnumerable<HexCoord> Ring(int radius)   { ... yield return ...; }
```
Both are compiler-generated state machines returned as `IEnumerable<HexCoord>`. Every call (board build, plus `Spiral`/`Drifter`/`Jumper`/`Ringwalk`/`Orbit`/`Edge`/`Anchor` `LegalMoves`) allocates one enumerator on the heap. Per tile-tap this is one allocation, so impact is small, but it is the only remaining GC source on the hot path.

**2. [LOW] `_movesBuffer` initial capacity (32) is below worst-case (Drifter ~37)**
`scripts/Board/HexBoard.cs:36`
```csharp
private readonly List<HexCoord> _movesBuffer = new(32);
```
`Drifter.LegalMoves` writes `HexCoord.Within(3)` = 37 entries, forcing a one-time `List<T>` doubling to 64 on first selection. Trivial to fix: `new(64)`.

**3. [LOW] Each `Token` re-allocates an identical `Mesh` and `StandardMaterial3D` on instantiate**
`scripts/Tokens/Token.cs:37-56`, plus every concrete `CreateMesh()` in `scripts/Tokens/Tokens.cs`
```csharp
public override void _Ready() {
    var visual = new MeshInstance3D {
        Mesh = CreateMesh(),
        MaterialOverride = MakeMaterial(GetColor()),
    };
    AddChild(visual);
}
```
Each `CreateMesh()` instantiates a fresh `CylinderMesh`/`BoxMesh`/etc. and `MakeMaterial` builds a fresh `StandardMaterial3D`. Since meshes and materials are pure resources, they can be cached once per `Token` subclass (static field) and reused across instances. Not in a per-frame path, so this is a startup/swap optimization only.

**4. [LOW] 61 tiles each allocate their own `BaseMaterial` + `HighlightMaterial`**
`scripts/Board/HexBoard.cs:94-129`
There are only three checker colours and one highlight colour, but the loop builds 122 `StandardMaterial3D` instances. Three shared base materials and one shared highlight material would suffice. One-time cost (board build), so LOW.

### CPU

**5. [LOW] `Filter` re-derives `DistanceFromOrigin` per candidate**
`scripts/Tokens/Token.cs:59-70`
With `S` now stored in the struct (`HexCoord.cs:26-28`) this is already fast (three `Math.Abs` + a divide). Acceptable; called once per selection on a buffer of <= ~37 entries. No action recommended.

**6. [LOW] `BeginSelect` `foreach`-iterates a `List<T>` instead of indexing**
`scripts/Board/HexBoard.cs:190-197`
Already uses `for (int i = 0; i < _movesBuffer.Count; i++)` — actually correct. No issue; flagged here only to note that the prior audit's selection re-entry concern is addressed by the `_selectionValid` / `_lastSelectedToken` guard at `HexBoard.cs:182-186`.

### Rendering

**7. [INFO] Light + shadows**
`scenes/game.tscn:26-28` — one `DirectionalLight3D` with no `shadow_enabled` flag set (defaults to false on GL Compatibility). Ambient-only world environment. Nothing to optimise.

**8. [INFO] No shaders, no `QueueRedraw`, no `_Draw` overrides**
Searched all of `scripts/` — clean.

### Collision / Physics

**9. [RESOLVED — verified] `Viewport.PhysicsObjectPicking` is now gated on modal state**
`scripts/UI/GameScreen.cs:41-42, 72-76` plus `scripts/DebugLog.cs:30-44` (`PushModal`/`PopModal` drive `GameplayActiveChanged`). Modals (`SettingsModal.Open`/`Close` at `SettingsModal.cs:113, 135`; `DebugModal.Open`/`Close` at `DebugModal.cs:153, 162`) push/pop correctly. Counter logic is sound — bottoms-out at zero (`PopModal` early-returns if depth is already 0).

**10. [LOW] `Area3D.InputEvent` lambdas are not explicitly disconnected**
`scripts/Board/HexBoard.cs:141`
```csharp
tile.Area.InputEvent += (camera, e, pos, normal, idx) => OnTileInput(coord, e);
```
Lambdas are captured by value at subscription. Because tiles live for the lifetime of `HexBoard`, this is fine in practice — but on a scene-change/`QueueFree` path the closure objects are only collected once Godot tears the nodes down. No leak, no urgency.

### Lifecycle

**11. [INFO] Token swap path correctly disables old token before free**
`scripts/Board/HexBoard.cs:69-76` — sets input flags off, sets `ProcessMode = Disabled`, then `QueueFree()`. Defensive and correct.

**12. [INFO] `DebugLog._ExitTree` removes its `Logger` and nulls the static singleton**
`scripts/DebugLog.cs:65-73` — clean.

---

## Remediation Plan

**Step 1 — Resize `_movesBuffer` to fit worst case**
- File: `scripts/Board/HexBoard.cs:36`
- Change: `new(32)` -> `new(64)`
- Expected impact: Eliminates the one-time `List<T>` capacity-doubling allocation that fires the first time a `Drifter` is selected.
- Verification: Mono profiler — first `Drifter` selection should no longer show a `T[]` resize event for `_movesBuffer`.

**Step 2 — Replace `Within` / `Ring` `IEnumerable` with struct enumerators**
- File: `scripts/Hex/HexCoord.cs:62-85`
- Change: Either expose `public struct WithinEnumerator { public bool MoveNext(); public HexCoord Current; }` returned by a `WithinEnumerable Within(int radius)`, or add direct-write overloads `public static void Within(int radius, List<HexCoord> output)` / `Ring(int radius, List<HexCoord> output)` and call them from the affected token rules. Direct-write is simpler and avoids any iterator state machine.
- Affected callers (verified): `HexBoard.BuildBoard` (`HexBoard.cs:86`), `Spiral.LegalMoves`, `Drifter.LegalMoves`, `Jumper.LegalMoves`, `Ringwalk.LegalMoves`, `Orbit.LegalMoves`, `Edge.LegalMoves`, `Anchor.LegalMoves` (all in `scripts/Tokens/Tokens.cs`).
- Expected impact: Removes the last per-selection heap allocation. Selection becomes zero-alloc except for the eventual tween.
- Verification: Mono profiler shows 0 Gen0 collections during 100 rapid token selections.

**Step 3 — Cache `Mesh` and `StandardMaterial3D` resources**
- File: `scripts/Tokens/Token.cs:37-56` and each `CreateMesh` / `GetColor` in `scripts/Tokens/Tokens.cs`
- Change: Convert `protected abstract Mesh CreateMesh()` to a static lazy resource. Pattern:
  ```csharp
  private static readonly Mesh SharedMesh = new CylinderMesh { ... };
  private static readonly StandardMaterial3D SharedMaterial = MakeMaterial(...);
  ```
  Each subclass exposes its `SharedMesh`/`SharedMaterial`; `Token._Ready` reads them.
- Expected impact: Token swap stops allocating a new mesh + material every time; removes ~36 transient resource allocations across a typical play session.
- Verification: Instrument `MakeMaterial` with a counter; should stay at 18 after touching every token.

**Step 4 — Share the four `StandardMaterial3D` resources across tiles**
- File: `scripts/Board/HexBoard.cs:94-129`
- Change: Hoist `TileMaterialA/B/C` and `HighlightMaterialShared` to `static readonly` fields built once. Tiles store references instead of unique instances.
- Expected impact: 122 -> 4 `StandardMaterial3D` instances; faster board build, smaller per-board memory footprint.
- Verification: GPU resource panel — material count drops from 122 to 4.

**Step 5 — Optional: disconnect `Area3D.InputEvent` lambdas on shutdown**
- File: `scripts/Board/HexBoard.cs:141`
- Change: Store the `Callable` once per tile so it can be passed to `tile.Area.InputEvent -= callable` in a future `_ExitTree`.
- Expected impact: Correctness on scene reload; no measurable runtime gain.
- Verification: Add an `_ExitTree` log; reload the scene 10 times — heap should not grow.

**Step 6 — Optional: precompute checker index for tile colour selection**
- File: `scripts/Board/HexBoard.cs:98`
- Change: `(coord.Q - coord.R) % 3 + 3) % 3` runs once per tile at build, so this is mostly a readability improvement. Leave as-is unless `BuildBoard` is later called per-level on lower-spec mobile.

### Project Settings to Review
- `project.godot` — `rendering/textures/vram_compression/import_etc2_astc=true` is correct for the `gl_compatibility` mobile path.
- `display/window/stretch/mode="canvas_items"` is the right choice for a portrait-of-content UI overlay against a 3D viewport. No change needed.
- Confirm `application/run/main_scene` is the only scene loaded — verified (`scenes/` contains only `game.tscn`).

---

## Verification Checklist

1. **Selection GC** — In the Mono profiler, select tokens 100x in a row. After Steps 1-2, Gen0 collections during selection should be 0 (down from a small but non-zero count today).
2. **Board build** — Log allocation count of `StandardMaterial3D` before/after Step 4: expect 122 -> 4.
3. **Token swap** — Step through `Token._Ready` 18 times; after Step 3 the `MakeMaterial` counter should stay at 18 total (one per subclass) instead of 18 * N swaps.
4. **Modal picking gate** — Open the settings drawer mid-game and watch `Performance/Time/Physics Process`: should drop to ~0 while the drawer is open (already true; verifies the implemented fix did not regress).
5. **Scene reload** — Reload `game.tscn` 10x; heap should be flat after Step 5.

---

## Execution Log — 2026-05-10 PM

- Step 1 — Resize `_movesBuffer` to fit worst case: ✓ applied
- Step 2 — Replace `Within` / `Ring` `IEnumerable` with direct-write overloads: ✓ applied
- Step 3 — Cache `Mesh` and `StandardMaterial3D` resources per token subclass: ✓ applied
- Step 4 — Share the four `StandardMaterial3D` resources across tiles: ✓ applied
- Step 5 — Disconnect `Area3D.InputEvent` lambdas on shutdown: ⊘ skipped optional
- Step 6 — Precompute checker index for tile colour selection: ⊘ skipped optional

Final build: green

Follow-up pass (2026-05-10 evening):
- Step 5 — Disconnect `Area3D.InputEvent` lambdas on shutdown: ✓ applied
- Step 6 — Precompute checker index for tile colour selection: ✓ applied
