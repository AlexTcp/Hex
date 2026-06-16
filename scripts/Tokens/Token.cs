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

    private MeshInstance3D _visual;

    public override void _Ready()
    {
        _visual = new MeshInstance3D
        {
            Mesh = GetSharedMesh(),
            MaterialOverride = GetSharedMaterial(),
        };
        AddChild(_visual);
    }

    // Swap the piece's surface to a transient material (e.g. the gold "selected"
    // look the board applies while a move is being chosen) without disturbing the
    // shared identity material.
    public void SetActiveMaterial(StandardMaterial3D mat)
    {
        if (_visual != null) _visual.MaterialOverride = mat;
    }

    public void RestoreMaterial()
    {
        if (_visual != null) _visual.MaterialOverride = GetSharedMaterial();
    }

    // Premium Slate: tokens read as polished, tinted metal (no self-glow — the
    // emissive look fights the tournament-grade aesthetic; the shine is earned
    // from the light rig instead). Each identity colour is lerped toward a neutral
    // steel so pieces read as tinted alloy rather than painted plastic. Highlight
    // comes only from direct light (no SSR/probes in the Compatibility renderer),
    // so the rim term defines the silhouette edge.
    private static readonly Color MetalBase = new(0.78f, 0.80f, 0.82f);

    protected static StandardMaterial3D MakeMaterial(Color color)
    {
        return new StandardMaterial3D
        {
            AlbedoColor = color.Lerp(MetalBase, 0.45f),
            Metallic = 0.85f,
            MetallicSpecular = 0.6f,
            Roughness = 0.30f,
            RimEnabled = true,
            Rim = 0.25f,
            RimTint = 0.5f,
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
