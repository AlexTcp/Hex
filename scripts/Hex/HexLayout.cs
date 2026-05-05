// =============================================================================
// HexLayout
// =============================================================================
// Purpose:
//   Static helper that defines the visual layout of the hex grid. Provides
//   the shared TileSize constant and ToWorld(HexCoord) which converts axial
//   hex coordinates into 3D world-space positions using flat-top hex math
//   (sqrt(3) and 1.5 scaling factors).
//
// Interactions:
//   - HexCoord: input to ToWorld; reads its Q and R fields to compute the
//     x/z world position.
// =============================================================================

using Godot;

namespace HexGame.Hex;

public static class HexLayout
{
    public const float TileSize = 0.55f;

    private static readonly float Sqrt3 = Mathf.Sqrt(3f);

    public static Vector3 ToWorld(HexCoord h, float y = 0f)
    {
        float x = TileSize * Sqrt3 * (h.Q + h.R / 2f);
        float z = TileSize * 1.5f * h.R;
        return new Vector3(x, y, z);
    }
}
