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
    [Export] public int Radius = 4;

    private readonly Dictionary<HexCoord, Tile> _tiles = new();
    private Token _token;
    private HexCoord _tokenPos = HexCoord.Zero;
    private bool _selected = false;
    private readonly HashSet<HexCoord> _highlighted = new();
    private readonly List<HexCoord> _movesBuffer = new(64);
    private Token _lastSelectedToken;
    private HexCoord _lastSelectedPos;
    private bool _selectionValid = false;

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

        if (index < 0 || index >= TokenCatalog.All.Length) return;
        _token = TokenCatalog.All[index].Factory();
        AddChild(_token);
        _token.Position = HexLayout.ToWorld(_tokenPos, 0.35f);
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
        var target = HexLayout.ToWorld(coord, 0.35f);
        var tween = CreateTween();
        tween.TweenProperty(_token, "position", target, 0.18f)
            .SetTrans(Tween.TransitionType.Sine)
            .SetEase(Tween.EaseType.Out);
    }
}
