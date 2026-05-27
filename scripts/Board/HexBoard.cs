// =============================================================================
// HexBoard
// =============================================================================
// Purpose:
//   Partial Node3D class that constructs and manages a hexagonal game board.
//   Builds the grid of tiles within a configurable Radius, hosts a single
//   Token instance, handles tile pick/tap input, computes legal-move
//   highlights via the active Token, and tweens the token between tiles
//   when the player commits to a move.
//
// Interactions:
//   - HexCoord: used as the tile coordinate key, for iterating Within(Radius),
//     and for the token's logical position.
//   - HexLayout: converts HexCoord values to world-space positions and
//     supplies TileSize for mesh/collision sizing.
//   - Token: held as the active piece on the board; queried via LegalMoves
//     to compute highlighted destinations and reparented when set/moved.
// =============================================================================

using System.Collections.Generic;
using Godot;
using HexGame.Hex;
using HexGame.Tokens;

namespace HexGame.Board;

public partial class HexBoard : Node3D
{
    public enum EnemyBehavior { RandomWalk, Flee, Chase }

    [Export] public int Radius = 4;
    [Export] public int EnemyCount = 3;
    [Export] public EnemyBehavior Behavior = EnemyBehavior.RandomWalk;

    [Signal] public delegate void EnemiesChangedEventHandler(int remaining);
    [Signal] public delegate void BoardSolvedEventHandler();

    private readonly Dictionary<HexCoord, Tile> _tiles = new();
    private Token _token;
    private HexCoord _tokenPos = HexCoord.Zero;
    private bool _selected = false;
    private readonly HashSet<HexCoord> _highlighted = new();
    private readonly List<HexCoord> _movesBuffer = new(64);
    private Token _lastSelectedToken;
    private HexCoord _lastSelectedPos;
    private bool _selectionValid = false;

    // Enemy state. Enemies are separate mesh nodes (not tile material swaps) so
    // they can coexist with highlight materials and be tweened/popped freely.
    private readonly List<Enemy> _enemies = new();
    private readonly System.Random _rng = new();
    private readonly HashSet<HexCoord> _reachable = new();
    private readonly Queue<HexCoord> _bfsQueue = new();
    private readonly List<HexCoord> _bfsMoves = new(64);
    private readonly List<HexCoord> _spawnScratch = new(64);
    private readonly List<HexCoord> _neighborScratch = new(6);

    private static readonly Color TileColorA = new(0.32f, 0.34f, 0.40f);
    private static readonly Color TileColorB = new(0.42f, 0.44f, 0.50f);
    private static readonly Color TileColorC = new(0.52f, 0.54f, 0.60f);
    private static readonly Color HighlightColor = new(0.95f, 0.85f, 0.30f);

    private static readonly StandardMaterial3D TileMaterialA = new()
    {
        AlbedoColor = TileColorA,
        Roughness = 0.7f,
    };
    private static readonly StandardMaterial3D TileMaterialB = new()
    {
        AlbedoColor = TileColorB,
        Roughness = 0.7f,
    };
    private static readonly StandardMaterial3D TileMaterialC = new()
    {
        AlbedoColor = TileColorC,
        Roughness = 0.7f,
    };
    private static readonly StandardMaterial3D HighlightMaterialShared = new()
    {
        AlbedoColor = HighlightColor,
        Emission = HighlightColor * 0.6f,
        EmissionEnabled = true,
        Roughness = 0.5f,
    };

    private static readonly StandardMaterial3D[] TileMaterialsByChecker =
    {
        TileMaterialA,
        TileMaterialB,
        TileMaterialC,
    };

    // Shared across every tile — geometry is identical, so a single Mesh + Shape3D
    // resource keeps Godot's renderer/physics dedup happy and avoids 61 redundant
    // resource allocations per board build.
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

    // Every enemy shares one mesh + one emissive-red material, mirroring the
    // shared-GPU-resource convention used for tiles and tokens.
    private static readonly Mesh SharedEnemyMesh = new SphereMesh { Radius = 0.2f, Height = 0.4f };
    private static readonly StandardMaterial3D EnemyMaterial = new()
    {
        AlbedoColor = new Color(0.95f, 0.2f, 0.2f),
        Emission = new Color(0.95f, 0.2f, 0.2f) * 0.5f,
        EmissionEnabled = true,
        Roughness = 0.45f,
    };

    private sealed class Enemy
    {
        public HexCoord Coord;
        public MeshInstance3D Node;
    }

    private sealed class Tile
    {
        public HexCoord Coord;
        public int CheckerIndex;
        public MeshInstance3D Mesh;
        public StandardMaterial3D BaseMaterial;
        public StandardMaterial3D HighlightMaterial;
        public Area3D Area;
        public Callable InputHandler;
    }

    public override void _Ready()
    {
        BuildBoard();
#if DEBUG
        GD.Print($"[HexBoard] _Ready done, tiles={_tiles.Count}");
#endif
    }

    public override void _Input(InputEvent @event)
    {
#if DEBUG
        if (@event is InputEventScreenTouch st)
        {
            var vp = GetViewport();
            GD.Print($"[DIAG-IN] touch pressed={st.Pressed} idx={st.Index} pos={st.Position} picking={vp?.PhysicsObjectPicking} tiles={_tiles.Count}");
        }
        else if (@event is InputEventMouseButton mb)
        {
            var vp = GetViewport();
            GD.Print($"[DIAG-IN] mouse pressed={mb.Pressed} btn={mb.ButtonIndex} pos={mb.Position} picking={vp?.PhysicsObjectPicking}");
        }
#endif
    }

    public void SetToken(int index)
    {
        ClearHighlights();
        ClearEnemies();
        _selected = false;
        _selectionValid = false;
        _lastSelectedToken = null;
        _tokenPos = HexCoord.Zero;

        if (_token != null)
        {
            _token.SetProcessInput(false);
            _token.SetProcessUnhandledInput(false);
            _token.ProcessMode = ProcessModeEnum.Disabled;
            _token.QueueFree();
            _token = null;
        }

        if (index < 0 || index >= TokenCatalog.All.Length)
        {
            EmitSignal(SignalName.EnemiesChanged, 0);
            return;
        }
        _token = TokenCatalog.All[index].Factory();
        AddChild(_token);
        _token.Position = HexLayout.ToWorld(_tokenPos, 0.35f);

        SpawnEnemies();
    }

    private void BuildBoard()
    {
        var coords = new List<HexCoord>(1 + 3 * Radius * (Radius + 1));
        HexCoord.Within(Radius, coords);
        for (int i = 0; i < coords.Count; i++)
        {
            var h = coords[i];
            var tile = BuildTile(h);
            _tiles[h] = tile;
            AddChild(tile.Area);
        }
    }

    private Tile BuildTile(HexCoord coord)
    {
        var checker = ((coord.Q - coord.R) % 3 + 3) % 3;
        var tile = new Tile { Coord = coord, CheckerIndex = checker };

        tile.BaseMaterial = TileMaterialsByChecker[checker];
        tile.HighlightMaterial = HighlightMaterialShared;

        tile.Mesh = new MeshInstance3D
        {
            Mesh = SharedTileMesh,
            MaterialOverride = tile.BaseMaterial,
        };

        var collision = new CollisionShape3D { Shape = SharedTileShape };

        tile.Area = new Area3D { Position = HexLayout.ToWorld(coord) };
        tile.Area.AddChild(tile.Mesh);
        tile.Area.AddChild(collision);
        var captured = coord;
        tile.InputHandler = Callable.From(
            (Node camera, InputEvent e, Vector3 pos, Vector3 normal, long idx) => OnTileInput(captured, e));
        tile.Area.Connect(Area3D.SignalName.InputEvent, tile.InputHandler);

        return tile;
    }

    public override void _ExitTree()
    {
        foreach (var kv in _tiles)
        {
            var tile = kv.Value;
            if (tile.Area != null && IsInstanceValid(tile.Area))
            {
                tile.Area.Disconnect(Area3D.SignalName.InputEvent, tile.InputHandler);
            }
        }
    }

    private void OnTileInput(HexCoord coord, InputEvent e)
    {
#if DEBUG
        if (e is InputEventMouseButton || e is InputEventScreenTouch)
            GD.Print($"[DIAG-TILE] coord=({coord.Q},{coord.R}) type={e.GetType().Name}");
#endif
        bool clicked = false;
        if (e is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
            clicked = true;
        else if (e is InputEventScreenTouch st && st.Pressed)
            clicked = true;
        if (!clicked) return;
#if DEBUG
        GD.Print($"[DIAG-TILE] -> tapped coord=({coord.Q},{coord.R})");
#endif
        OnTileTapped(coord);
    }

    private void OnTileTapped(HexCoord coord)
    {
#if DEBUG
        GD.Print($"[DIAG-TAP] coord=({coord.Q},{coord.R}) tokenNull={_token == null} selected={_selected} tokenPos=({_tokenPos.Q},{_tokenPos.R})");
#endif
        if (_token == null) return;

        if (!_selected)
        {
            if (coord == _tokenPos) BeginSelect();
            return;
        }

        if (coord == _tokenPos) return;

        if (_highlighted.Contains(coord))
        {
            MoveTokenTo(coord);
            EndSelect();
        }
        else
        {
            EndSelect();
        }
    }

    private void BeginSelect()
    {
        if (_selectionValid && _lastSelectedToken == _token && _lastSelectedPos == _tokenPos)
        {
            _selected = true;
            return;
        }

        _selected = true;
        _highlighted.Clear();
        _token.LegalMoves(_tokenPos, Radius, _movesBuffer);
        for (int i = 0; i < _movesBuffer.Count; i++)
        {
            var dest = _movesBuffer[i];
            if (!_tiles.TryGetValue(dest, out var tile)) continue;
            _highlighted.Add(dest);
            tile.Mesh.MaterialOverride = tile.HighlightMaterial;
        }
        _lastSelectedToken = _token;
        _lastSelectedPos = _tokenPos;
        _selectionValid = true;
    }

    private void EndSelect()
    {
        ClearHighlights();
        _selected = false;
    }

    private void ClearHighlights()
    {
        foreach (var h in _highlighted)
            if (_tiles.TryGetValue(h, out var t))
                t.Mesh.MaterialOverride = t.BaseMaterial;
        _highlighted.Clear();
        _selectionValid = false;
    }

    private void MoveTokenTo(HexCoord coord)
    {
        _tokenPos = coord;
        _selectionValid = false;
        TweenTokenTo(coord);

        bool captured = TryCapture(coord);
        Haptics.Tap(captured ? 35 : 15);

        if (_enemies.Count == 0)
        {
            EmitSignal(SignalName.BoardSolved);
            SpawnEnemies();
            return;
        }
        AdvanceEnemies(coord);
    }

    private void TweenTokenTo(HexCoord coord)
    {
        var target = HexLayout.ToWorld(coord, 0.35f);

        var move = CreateTween();
        move.TweenProperty(_token, "position", target, 0.18f)
            .SetTrans(Tween.TransitionType.Sine)
            .SetEase(Tween.EaseType.Out);

        // Parallel squash-and-stretch: compress on launch, snap back on landing.
        // A separate tween so the unsquash starts mid-move (at ~0.09s) rather
        // than waiting for the position step to finish.
        var squash = CreateTween();
        squash.TweenProperty(_token, "scale", new Vector3(1.12f, 0.85f, 1.12f), 0.09f)
            .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
        squash.TweenProperty(_token, "scale", Vector3.One, 0.09f)
            .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.In);
    }

    // ----- Enemies -------------------------------------------------------

    // Each surviving enemy takes one step after the player commits a move.
    // Resolution is sequential, so an enemy never steps onto another enemy's
    // current tile and the order prevents overlaps. Enemies never step onto the
    // player. A fully boxed-in enemy holds position.
    private void AdvanceEnemies(HexCoord playerPos)
    {
        var dirs = HexCoord.Directions;
        for (int i = 0; i < _enemies.Count; i++)
        {
            var e = _enemies[i];
            _neighborScratch.Clear();
            for (int d = 0; d < 6; d++)
            {
                var n = e.Coord + dirs[d];
                if (!_tiles.ContainsKey(n)) continue;   // off-board
                if (n == playerPos) continue;           // never onto player
                if (IsEnemyAt(n, i)) continue;          // never onto another enemy
                _neighborScratch.Add(n);
            }
            if (_neighborScratch.Count == 0) continue;

            HexCoord chosen = Behavior switch
            {
                EnemyBehavior.Flee => PickByDistance(_neighborScratch, playerPos, maximize: true),
                EnemyBehavior.Chase => PickByDistance(_neighborScratch, playerPos, maximize: false),
                _ => _neighborScratch[_rng.Next(_neighborScratch.Count)],
            };
            e.Coord = chosen;
            MoveEnemyVisual(e, chosen);
        }
    }

    private bool IsEnemyAt(HexCoord coord, int except)
    {
        for (int j = 0; j < _enemies.Count; j++)
        {
            if (j == except) continue;
            if (_enemies[j].Coord == coord) return true;
        }
        return false;
    }

    private static HexCoord PickByDistance(List<HexCoord> options, HexCoord target, bool maximize)
    {
        var best = options[0];
        int bestD = best.Distance(target);
        for (int i = 1; i < options.Count; i++)
        {
            int d = options[i].Distance(target);
            if (maximize ? d > bestD : d < bestD) { best = options[i]; bestD = d; }
        }
        return best;
    }

    private void MoveEnemyVisual(Enemy e, HexCoord coord)
    {
        if (e.Node == null || !IsInstanceValid(e.Node)) return;
        var target = HexLayout.ToWorld(coord, 0.35f);
        var tween = CreateTween();
        tween.TweenProperty(e.Node, "position", target, 0.16f)
            .SetTrans(Tween.TransitionType.Sine)
            .SetEase(Tween.EaseType.Out);
    }

    private void SpawnEnemies()
    {
        ClearEnemies();
        if (_token == null)
        {
            EmitSignal(SignalName.EnemiesChanged, 0);
            return;
        }

        ComputeReachable(_tokenPos);
        _spawnScratch.Clear();
        foreach (var c in _reachable) _spawnScratch.Add(c);

        int target = Mathf.Min(EnemyCount, _spawnScratch.Count);
        for (int n = 0; n < target; n++)
        {
            int idx = _rng.Next(_spawnScratch.Count);
            var coord = _spawnScratch[idx];
            _spawnScratch[idx] = _spawnScratch[_spawnScratch.Count - 1];
            _spawnScratch.RemoveAt(_spawnScratch.Count - 1);
            AddEnemy(coord);
        }
        EmitSignal(SignalName.EnemiesChanged, _enemies.Count);
    }

    private void AddEnemy(HexCoord coord)
    {
        var mesh = new MeshInstance3D
        {
            Mesh = SharedEnemyMesh,
            MaterialOverride = EnemyMaterial,
            Position = HexLayout.ToWorld(coord, 0.35f),
        };
        AddChild(mesh);
        _enemies.Add(new Enemy { Coord = coord, Node = mesh });
    }

    private void ClearEnemies()
    {
        for (int i = 0; i < _enemies.Count; i++)
        {
            var node = _enemies[i].Node;
            if (node != null && IsInstanceValid(node)) node.QueueFree();
        }
        _enemies.Clear();
    }

    // BFS over the active token's movement graph: every tile the token can
    // eventually reach from `start`. Enemies only spawn on reachable tiles so a
    // board is never unsolvable (e.g. a parity-locked Jumper that can only land
    // on same-parity hexes). Reuses pooled buffers to stay alloc-free.
    private void ComputeReachable(HexCoord start)
    {
        _reachable.Clear();
        _bfsQueue.Clear();
        _reachable.Add(start);
        _bfsQueue.Enqueue(start);
        while (_bfsQueue.Count > 0)
        {
            var cur = _bfsQueue.Dequeue();
            _token.LegalMoves(cur, Radius, _bfsMoves);
            for (int i = 0; i < _bfsMoves.Count; i++)
            {
                var n = _bfsMoves[i];
                if (_tiles.ContainsKey(n) && _reachable.Add(n))
                    _bfsQueue.Enqueue(n);
            }
        }
        _reachable.Remove(start);
    }

    private bool TryCapture(HexCoord coord)
    {
        for (int i = 0; i < _enemies.Count; i++)
        {
            if (_enemies[i].Coord != coord) continue;
            var node = _enemies[i].Node;
            _enemies.RemoveAt(i);
            CaptureVisual(node);
            EmitSignal(SignalName.EnemiesChanged, _enemies.Count);
            return true;
        }
        return false;
    }

    private void CaptureVisual(MeshInstance3D node)
    {
        if (node == null || !IsInstanceValid(node)) return;
        var tween = CreateTween();
        tween.TweenProperty(node, "scale", Vector3.Zero, 0.13f)
            .SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.In);
        tween.TweenCallback(Callable.From(() =>
        {
            if (IsInstanceValid(node)) node.QueueFree();
        }));
    }
}
