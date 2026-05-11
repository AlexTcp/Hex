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

    private sealed class Tile
    {
        public HexCoord Coord;
        public MeshInstance3D Mesh;
        public StandardMaterial3D BaseMaterial;
        public StandardMaterial3D HighlightMaterial;
        public Area3D Area;
    }

    public override void _Ready()
    {
        BuildBoard();
        GD.Print($"[HexBoard] _Ready done, tiles={_tiles.Count}");
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
        var tile = new Tile { Coord = coord };

        var checker = ((coord.Q - coord.R) % 3 + 3) % 3;
        tile.BaseMaterial = checker switch
        {
            0 => TileMaterialA,
            1 => TileMaterialB,
            _ => TileMaterialC,
        };
        tile.HighlightMaterial = HighlightMaterialShared;

        var hexMesh = new CylinderMesh
        {
            TopRadius = HexLayout.TileSize * 0.95f,
            BottomRadius = HexLayout.TileSize * 0.95f,
            Height = 0.15f,
            RadialSegments = 6,
        };
        tile.Mesh = new MeshInstance3D
        {
            Mesh = hexMesh,
            MaterialOverride = tile.BaseMaterial,
        };

        var shape = new CylinderShape3D
        {
            Radius = HexLayout.TileSize * 0.95f,
            Height = 0.15f,
        };
        var collision = new CollisionShape3D { Shape = shape };

        tile.Area = new Area3D { Position = HexLayout.ToWorld(coord) };
        tile.Area.AddChild(tile.Mesh);
        tile.Area.AddChild(collision);
        tile.Area.InputEvent += (camera, e, pos, normal, idx) => OnTileInput(coord, e);

        return tile;
    }

    private void OnTileInput(HexCoord coord, InputEvent e)
    {
        bool clicked = false;
        if (e is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
            clicked = true;
        else if (e is InputEventScreenTouch st && st.Pressed)
            clicked = true;
        if (!clicked) return;
        OnTileTapped(coord);
    }

    private void OnTileTapped(HexCoord coord)
    {
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
