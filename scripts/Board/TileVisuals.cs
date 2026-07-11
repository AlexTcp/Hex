// =============================================================================
// TileVisuals
// =============================================================================
// Purpose:
//   The board's shared GPU resources — Premium Slate tile palette, highlight /
//   inactive / cracked / locked materials, landing-ring and selection-ring
//   meshes, capture-spark material, tile-upgrade marker discs, and the tile
//   mesh + collision shape. Mirrors the PieceVisuals pattern: every resource
//   here is a static shared object (hard rule 2 — never instantiate per tile).
//
//   HexBoard consumes these via `using static` so the resolution code reads
//   unchanged. GL Compatibility constraints apply: emissive materials are the
//   only sanctioned "glow"; keep tile RadialSegments at 6.
//
// Interactions:
//   - HexBoard: sole consumer (tiles, highlights, FX, markers).
// =============================================================================

using Godot;
using HexGame.Hex;

namespace HexGame.Board;

public static class TileVisuals
{
    // ----- Premium Slate palette (Compatibility-safe: emissive only) -------
    public static readonly Color TileColorA = new(0.165f, 0.180f, 0.212f);
    public static readonly Color TileColorB = new(0.212f, 0.231f, 0.271f);
    public static readonly Color TileColorC = new(0.263f, 0.286f, 0.337f);
    public static readonly Color GoldColor = new(0.890f, 0.698f, 0.235f);
    public static readonly Color CopperColor = new(0.851f, 0.384f, 0.180f);
    public static readonly Color DangerColor = new(0.900f, 0.150f, 0.150f);

    private static StandardMaterial3D Slate(Color albedo, float roughness) => new()
    {
        AlbedoColor = albedo,
        Metallic = 0.10f,
        MetallicSpecular = 0.5f,
        Roughness = roughness,
        RimEnabled = true,
        Rim = 0.18f,
        RimTint = 0.4f,
    };

    public static readonly StandardMaterial3D TileMaterialA = Slate(TileColorA, 0.62f);
    public static readonly StandardMaterial3D TileMaterialB = Slate(TileColorB, 0.58f);
    public static readonly StandardMaterial3D TileMaterialC = Slate(TileColorC, 0.55f);

    // Out-of-battle-area tiles recede into the stage; cracked tiles smoulder;
    // locked tiles read as cold iron.
    public static readonly StandardMaterial3D InactiveMaterial = Slate(new Color(0.075f, 0.082f, 0.098f), 0.8f);
    public static readonly StandardMaterial3D CrackedMaterial = new()
    {
        AlbedoColor = new Color(0.32f, 0.14f, 0.11f),
        Emission = new Color(0.55f, 0.16f, 0.08f) * 0.35f,
        EmissionEnabled = true,
        Roughness = 0.7f,
    };
    public static readonly StandardMaterial3D LockedMaterial = Slate(new Color(0.15f, 0.19f, 0.30f), 0.35f);

    private static StandardMaterial3D Emissive(Color albedo, float emit) => new()
    {
        AlbedoColor = albedo,
        Emission = albedo * emit,
        EmissionEnabled = true,
        Roughness = 0.5f,
        Metallic = 0.2f,
    };

    public static readonly StandardMaterial3D HighlightMaterialShared = Emissive(GoldColor, 0.55f);
    public static readonly StandardMaterial3D CaptureHighlightMaterialShared = Emissive(CopperColor, 0.7f);
    public static readonly StandardMaterial3D DangerHighlightMaterialShared = Emissive(DangerColor, 0.7f);
    // Read-only "where could this enemy strike" paint — cold steel blue.
    public static readonly StandardMaterial3D EnemyReachMaterialShared = Emissive(new Color(0.36f, 0.56f, 0.85f), 0.5f);

    // Landing shockwave ring.
    public static readonly Color RingGold = new(0.890f, 0.698f, 0.235f, 0.8f);
    public static readonly Mesh SharedRingMesh = new TorusMesh
    {
        InnerRadius = HexLayout.TileSize * 0.5f,
        OuterRadius = HexLayout.TileSize * 0.6f,
        RingSegments = 6,
    };
    public static readonly StandardMaterial3D RingMaterialShared = new()
    {
        AlbedoColor = RingGold,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
    };

    // Gold ring under the currently selected piece.
    public static readonly Mesh SharedSelectRingMesh = new TorusMesh
    {
        InnerRadius = HexLayout.TileSize * 0.42f,
        OuterRadius = HexLayout.TileSize * 0.5f,
        RingSegments = 6,
    };
    public static readonly StandardMaterial3D SelectRingMaterialShared = new()
    {
        AlbedoColor = new Color(0.890f, 0.698f, 0.235f, 0.9f),
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
    };

    // Capture spark burst (CPU particles — GL Compatibility safe).
    public static readonly Mesh SharedSparkMesh = new SphereMesh { Radius = 0.045f, Height = 0.09f };
    public static readonly StandardMaterial3D SparkMaterialShared = new()
    {
        AlbedoColor = CopperColor,
        Emission = CopperColor * 0.6f,
        EmissionEnabled = true,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
    };

    // Tile-upgrade marker discs: one shared mesh, one shared material per kind.
    public static readonly Mesh SharedMarkerMesh = new CylinderMesh
    {
        TopRadius = HexLayout.TileSize * 0.32f,
        BottomRadius = HexLayout.TileSize * 0.32f,
        Height = 0.03f,
        RadialSegments = 6,
    };
    public static readonly StandardMaterial3D[] MarkerMaterials =
    {
        Emissive(new Color(0.890f, 0.698f, 0.235f), 0.35f),   // Gold
        Emissive(new Color(0.35f, 0.55f, 0.85f), 0.35f),      // Shield
        Emissive(new Color(0.62f, 0.35f, 0.75f), 0.35f),      // Snare
        Emissive(new Color(0.31f, 0.70f, 0.53f), 0.35f),      // Blessed
    };

    public static readonly StandardMaterial3D[] TileMaterialsByChecker =
    {
        TileMaterialA,
        TileMaterialB,
        TileMaterialC,
    };

    public static readonly CylinderMesh SharedTileMesh = new()
    {
        TopRadius = HexLayout.TileSize * 0.95f,
        BottomRadius = HexLayout.TileSize * 0.95f,
        Height = 0.15f,
        RadialSegments = 6,          // KEEP 6 — this is a hex grid, not a dodecagon
    };
    public static readonly CylinderShape3D SharedTileShape = new()
    {
        Radius = HexLayout.TileSize * 0.95f,
        Height = 0.15f,
    };
}
