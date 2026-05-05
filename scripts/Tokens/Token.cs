// =============================================================================
// Token
// =============================================================================
// Purpose:
//   Abstract partial Node3D base class for all movable game pieces. Defines
//   the contract for token identity (Id, DisplayName, Description), legal
//   movement generation (LegalMoves), and visual appearance (CreateMesh /
//   GetColor). Builds the visual MeshInstance3D in _Ready using the
//   subclass-supplied mesh and color, and offers a Filter helper to drop
//   the origin tile and any hex outside the board radius.
//
// Interactions:
//   - HexCoord: used as the input/output coordinate type for LegalMoves and
//     Filter; reads DistanceFromOrigin() for board-bounds checks.
//   - HexBoard: instantiates a Token via SetToken and queries LegalMoves to
//     compute movement highlights.
//   - Subclasses (Tokens.cs concrete pieces): inherit from Token and supply
//     concrete movement, mesh, and color implementations.
// =============================================================================

using System.Collections.Generic;
using Godot;
using HexGame.Hex;

namespace HexGame.Tokens;

public abstract partial class Token : Node3D
{
    public abstract string Id { get; }
    public abstract string DisplayName { get; }
    public abstract string Description { get; }
    public abstract IEnumerable<HexCoord> LegalMoves(HexCoord from, int boardRadius);

    protected abstract Mesh CreateMesh();
    protected abstract Color GetColor();

    public override void _Ready()
    {
        var visual = new MeshInstance3D
        {
            Mesh = CreateMesh(),
            MaterialOverride = MakeMaterial(GetColor()),
        };
        AddChild(visual);
    }

    protected static StandardMaterial3D MakeMaterial(Color color)
    {
        return new StandardMaterial3D
        {
            AlbedoColor = color,
            Roughness = 0.4f,
            Metallic = 0.1f,
            EmissionEnabled = true,
            Emission = color * 0.25f,
        };
    }

    protected static IEnumerable<HexCoord> Filter(IEnumerable<HexCoord> hexes, HexCoord from, int boardRadius)
    {
        foreach (var h in hexes)
        {
            if (h == from) continue;
            if (h.DistanceFromOrigin() > boardRadius) continue;
            yield return h;
        }
    }
}
