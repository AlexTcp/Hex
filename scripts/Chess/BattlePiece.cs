// =============================================================================
// BattlePiece & PieceVisuals
// =============================================================================
// Purpose:
//   BattlePiece is the plain data record for one piece on the battle board:
//   kind, side, coordinate, stun counter and the MeshInstance3D visual the
//   board creates for it. It is not a Node — HexBoard owns the lifecycle,
//   mirroring the old hunter Enemy class.
//
//   PieceVisuals holds the shared GPU resources: exactly one static mesh per
//   PieceKind and one static material per PieceSide (plus the gold "selected"
//   material). Every piece of a given kind renders with the same mesh object;
//   every piece of a side shares one material — the shared-resource hard rule.
//
// Interactions:
//   - HexBoard: creates/destroys the MeshInstance3D, moves it with tweens,
//     swaps MaterialOverride for the selected look.
// =============================================================================

using Godot;
using HexGame.Hex;

namespace HexGame.Chess;

public sealed class BattlePiece
{
    public PieceKind Kind;
    public PieceSide Side;
    public HexCoord Coord;
    public MeshInstance3D Node;
    public int StunTurns;          // > 0: skips enemy-turn selection (Snare / Knight Fork)
    public bool Alive = true;
}

public static class PieceVisuals
{
    // Premium Slate: pieces read as polished tinted alloy — no self-glow, the
    // shine is earned from the light rig (Compatibility renderer: no probes).
    private static readonly Color MetalBase = new(0.78f, 0.80f, 0.82f);

    private static StandardMaterial3D Metal(Color color) => new()
    {
        AlbedoColor = color.Lerp(MetalBase, 0.45f),
        Metallic = 0.85f,
        MetallicSpecular = 0.6f,
        Roughness = 0.30f,
        RimEnabled = true,
        Rim = 0.25f,
        RimTint = 0.5f,
    };

    // Player = warm ivory-silver; enemy = the established oxblood. A stunned
    // enemy shifts to snare-purple so the skipped turn is visible on the board.
    public static readonly StandardMaterial3D PlayerMaterial = Metal(new Color(0.95f, 0.93f, 0.86f));
    public static readonly StandardMaterial3D EnemyMaterial = Metal(new Color(0.42f, 0.10f, 0.10f));
    public static readonly StandardMaterial3D StunnedMaterial = Metal(new Color(0.40f, 0.26f, 0.50f));

    // Gold glow while the piece is selected (emissive is the sanctioned "glow").
    private static readonly Color GoldColor = new(0.890f, 0.698f, 0.235f);
    public static readonly StandardMaterial3D SelectedMaterial = new()
    {
        AlbedoColor = GoldColor,
        Metallic = 0.9f,
        MetallicSpecular = 0.65f,
        Roughness = 0.22f,
        RimEnabled = true,
        Rim = 0.35f,
        RimTint = 0.6f,
        Emission = GoldColor * 0.3f,
        EmissionEnabled = true,
    };

    // One shared mesh per kind. Distinct silhouettes at a glance from the
    // tilted camera: squat sphere pawn, tall tapered queen, hexagonal rook, etc.
    // Sized so pieces read clearly from the play camera (screenshot-checked:
    // at the old ~0.15–0.25 radii the rook was mistakable for a loose tile).
    private static readonly Mesh PawnMesh = new SphereMesh { Radius = 0.20f, Height = 0.40f };
    private static readonly Mesh KingMesh = new CylinderMesh { TopRadius = 0.30f, BottomRadius = 0.22f, Height = 0.72f };
    private static readonly Mesh RookMesh = new CylinderMesh { TopRadius = 0.25f, BottomRadius = 0.25f, Height = 0.56f, RadialSegments = 6 };
    private static readonly Mesh BishopMesh = new CylinderMesh { TopRadius = 0.05f, BottomRadius = 0.26f, Height = 0.70f };
    private static readonly Mesh KnightMesh = new PrismMesh { Size = new Vector3(0.42f, 0.58f, 0.38f) };
    private static readonly Mesh QueenMesh = new CylinderMesh { TopRadius = 0.11f, BottomRadius = 0.31f, Height = 0.90f };

    public static Mesh MeshFor(PieceKind kind) => kind switch
    {
        PieceKind.Pawn => PawnMesh,
        PieceKind.King => KingMesh,
        PieceKind.Rook => RookMesh,
        PieceKind.Bishop => BishopMesh,
        PieceKind.Knight => KnightMesh,
        _ => QueenMesh,
    };

    public static StandardMaterial3D MaterialFor(PieceSide side) =>
        side == PieceSide.Player ? PlayerMaterial : EnemyMaterial;
}
