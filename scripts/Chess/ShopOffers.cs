// =============================================================================
// ShopOffers
// =============================================================================
// Purpose:
//   The single source of truth for how shop offers are rolled: which piece
//   kinds may appear (Queen is withheld early) and which coordinates a tile
//   upgrade may claim. Used by the real ShopScreen and by the autoplay
//   harness's shop simulation — previously each carried its own copy, which
//   was a drift bug waiting to happen.
//
// Interactions:
//   - ShopScreen: rolls its two piece offers and the tile-upgrade coord here.
//   - AutoPlayDriver.SimulateShop: same rolls for headless runs.
//   - RunState.AddPiece: where a bought piece actually goes.
// =============================================================================

using System;
using System.Collections.Generic;
using HexGame.Hex;

namespace HexGame.Chess;

public static class ShopOffers
{
    public const int QueenUnlockBattle = 6;   // no Queen offers before this battle
    public const int UpgradeRadius = 2;       // tile upgrades claim central hexes only
    public const int RerollPrice = 2;         // cost to re-roll the shop offers

    // Uniform over kinds with the Queen gated early; falls back to a pawn.
    public static PieceKind RollPieceOffer(int battle, Random rng)
    {
        for (int attempt = 0; attempt < 8; attempt++)
        {
            var kind = (PieceKind)rng.Next(6);
            if (kind == PieceKind.Queen && battle < QueenUnlockBattle) continue;
            return kind;
        }
        return PieceKind.Pawn;
    }

    // Any un-upgraded hex within the central disc — central enough to matter in
    // every battle size, safe from all but the deepest crumble. Null when the
    // whole disc is claimed.
    public static HexCoord? RollUpgradeCoord(RunState run, Random rng)
    {
        var candidates = new List<HexCoord>();
        HexCoord.Within(UpgradeRadius, candidates);
        candidates.RemoveAll(c => run.TileUpgrades.ContainsKey(c));
        if (candidates.Count == 0) return null;
        return candidates[rng.Next(candidates.Count)];
    }
}
