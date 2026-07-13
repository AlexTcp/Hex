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
//   The pieces are a *carved army*: real low-poly figurine OBJs (an adventuring
//   war-council — rogue / cleric / wizard / warrior — plus a horse statue and a
//   hexagonal keep) baked at load into board-ready meshes and rendered in a
//   single tinted stone material per side, exactly like a themed chess set.
//   Baking = normalise each source mesh to base-at-origin, centred in X/Z,
//   uniform-scaled to a per-kind height, merged to one surface, so HexBoard's
//   positioning / squash / promotion swap all keep working unchanged. The
//   "carved marble" read is earned from the light rig (Compatibility: no probes).
//
// Interactions:
//   - HexBoard: creates/destroys the MeshInstance3D, moves it with tweens,
//     swaps MaterialOverride for the selected look, faces it per side.
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
    // ----- Carved-stone materials -----------------------------------------
    // Not polished alloy: ivory & oxblood *statuary*. Low metallic + mid-high
    // roughness so form reads from diffuse shading; a warm rim picks out the
    // silhouette edge against the slate. The player army is warm carved ivory,
    // the enemy the established oxblood; a snared enemy shifts to snare-purple.
    private static StandardMaterial3D Stone(Color color, float rough = 0.62f) => new()
    {
        AlbedoColor = color,
        Metallic = 0.0f,
        MetallicSpecular = 0.35f,
        Roughness = rough,
        RimEnabled = true,
        Rim = 0.55f,
        RimTint = 0.35f,
    };

    public static readonly StandardMaterial3D PlayerMaterial = Stone(new Color(0.93f, 0.90f, 0.82f));
    public static readonly StandardMaterial3D EnemyMaterial = Stone(new Color(0.46f, 0.12f, 0.12f));
    public static readonly StandardMaterial3D StunnedMaterial = Stone(new Color(0.42f, 0.28f, 0.52f));

    // Gold glow while the piece is selected (emissive is the sanctioned "glow").
    private static readonly Color GoldColor = new(0.890f, 0.698f, 0.235f);
    public static readonly StandardMaterial3D SelectedMaterial = new()
    {
        AlbedoColor = GoldColor,
        Metallic = 0.55f,
        MetallicSpecular = 0.6f,
        Roughness = 0.30f,
        RimEnabled = true,
        Rim = 0.5f,
        RimTint = 0.5f,
        Emission = GoldColor * 0.35f,
        EmissionEnabled = true,
    };

    // ----- Baked figurine meshes ------------------------------------------
    // One shared, board-normalised mesh per kind. Source OBJs live in res://models.
    // Per-kind target height (world units) sets the tournament-set hierarchy —
    // the King stands tallest, the pawn shortest — and doubles as instant
    // silhouette separation. yawDeg orients each model's "front" toward the
    // far side of the board (HexBoard rotates the enemy 180° to face back).
    private const string ModelDir = "res://models/";

    private static readonly Mesh PawnMesh   = Bake("Rogue.obj",              0.62f, 180f);
    private static readonly Mesh KingMesh    = Bake("Warrior.obj",            1.02f, 180f);
    private static readonly Mesh RookMesh    = Bake("tower-hexagon-base.obj", 0.82f, 0f);
    private static readonly Mesh BishopMesh  = Bake("Cleric.obj",             0.86f, 180f);
    private static readonly Mesh KnightMesh  = Bake("Statue_Horse.obj",       0.80f, 90f);
    private static readonly Mesh QueenMesh   = Bake("Wizard.obj",             0.94f, 180f);

    // Normalise a source mesh into a board-ready piece: centre X/Z, drop the
    // base to y=0 (feet plant on the tile so squash & pop-in read from the
    // ground), uniform-scale to targetHeight, apply a facing yaw, and merge
    // every source surface into one so a single MaterialOverride tints it all.
    private static Mesh Bake(string file, float targetHeight, float yawDeg)
    {
        var raw = ResourceLoader.Load<Mesh>(ModelDir + file);
        if (raw == null || raw.GetSurfaceCount() == 0)
            return new SphereMesh { Radius = 0.2f, Height = 0.4f };   // safe fallback

        var aabb = raw.GetAabb();
        float srcH = Mathf.Max(aabb.Size.Y, 0.0001f);
        float s = targetHeight / srcH;
        float cx = aabb.Position.X + aabb.Size.X * 0.5f;
        float cz = aabb.Position.Z + aabb.Size.Z * 0.5f;
        float minY = aabb.Position.Y;

        // xform = yaw * scale * centre  (applied right-to-left to each vertex)
        var centre = new Transform3D(Basis.Identity, new Vector3(-cx, -minY, -cz));
        var scale = new Transform3D(Basis.Identity.Scaled(new Vector3(s, s, s)), Vector3.Zero);
        var yaw = new Transform3D(new Basis(Vector3.Up, Mathf.DegToRad(yawDeg)), Vector3.Zero);
        var xform = yaw * scale * centre;

        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        for (int i = 0; i < raw.GetSurfaceCount(); i++)
            st.AppendFrom(raw, i, xform);
        st.Index();
        return st.Commit();
    }

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
