// =============================================================================
// TileUpgrade
// =============================================================================
// Purpose:
//   Permanent per-tile upgrades bought in the shop. Pure data: an enum of the
//   four upgrade kinds plus a catalog with display copy and prices. Effects are
//   resolved data-driven in HexBoard (capture bonus, capture block, stun on
//   landing, deploy bonus) from RunState.TileUpgrades — never as one-off hacks.
//
// Interactions:
//   - RunState: Dictionary<HexCoord, TileUpgradeKind> maps upgraded coords.
//   - HexBoard: renders a marker disc per upgraded tile and applies effects.
//   - ShopScreen: sells one upgrade per offer, assigning it a random coord.
// =============================================================================

namespace HexGame.Chess;

public enum TileUpgradeKind { Gold, Shield, Snare, Blessed }

public readonly record struct TileUpgradeInfo(TileUpgradeKind Kind, string Name, string Description, int Price);

public static class TileUpgradeCatalog
{
    public static readonly TileUpgradeInfo[] All =
    {
        new(TileUpgradeKind.Gold, "Gold Tile",
            "Captures on this tile pay +2 money.", 5),
        new(TileUpgradeKind.Shield, "Shield Tile",
            "The first friendly piece on this tile ignores one capture each battle.", 6),
        new(TileUpgradeKind.Snare, "Snare Tile",
            "An enemy landing on this tile skips its next turn.", 5),
        new(TileUpgradeKind.Blessed, "Blessed Tile",
            "Deploying a reserve piece onto this tile grants +1 money.", 4),
    };

    public static TileUpgradeInfo Info(TileUpgradeKind kind) => All[(int)kind];
}
