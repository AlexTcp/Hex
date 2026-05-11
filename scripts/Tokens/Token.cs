// =============================================================================
// Token
// =============================================================================
// Purpose:
//   Abstract partial Node3D base class for all movable game pieces. Defines
//   the contract for token identity (Id), legal movement generation
//   (LegalMoves, which fills a caller-supplied buffer to avoid per-call
//   allocations), and visual appearance (CreateMesh / GetColor). Builds the
//   visual MeshInstance3D in _Ready using the subclass-supplied mesh and
//   color, and offers a Filter helper to drop the origin tile and any hex
//   outside the board radius. Display names and descriptions live in
//   TokenCatalog so the picker UI can read them without instantiating tokens.
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
    public abstract void LegalMoves(HexCoord from, int boardRadius, List<HexCoord> output);

    protected abstract Mesh GetSharedMesh();
    protected abstract StandardMaterial3D GetSharedMaterial();

    public override void _Ready()
    {
        var visual = new MeshInstance3D
        {
            Mesh = GetSharedMesh(),
            MaterialOverride = GetSharedMaterial(),
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

    protected static void Filter(List<HexCoord> moves, HexCoord from, int boardRadius)
    {
        int write = 0;
        for (int read = 0; read < moves.Count; read++)
        {
            var h = moves[read];
            if (h == from) continue;
            if (h.DistanceFromOrigin() > boardRadius) continue;
            moves[write++] = h;
        }
        if (write < moves.Count) moves.RemoveRange(write, moves.Count - write);
    }
}
