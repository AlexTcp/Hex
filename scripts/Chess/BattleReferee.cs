// =============================================================================
// BattleReferee
// =============================================================================
// Purpose:
//   The pure end-of-battle rulings, extracted so the two subtlest invariants
//   in the game are deterministic-testable:
//   - Decide: the loss condition is checked BEFORE the win, so a collapse
//     that wipes both armies at once is a defeat — a "win" there would hand
//     the shop an empty army and the next battle would start unplayable (the
//     historical zombie-army bug).
//   - PlayerWinsStandoff: when the crumble is spent and nothing can capture,
//     the battle is adjudicated by remaining force; the reserve counts and
//     the player wins ties.
//
// Interactions:
//   - HexBoard: CheckBattleEnd and ResolveStandoff consume these.
// =============================================================================

using System.Collections.Generic;

namespace HexGame.Chess;

public enum BattleOutcome { Continue, Loss, Win }

public static class BattleReferee
{
    // Loss first: a mutual wipe must never read as a win.
    public static BattleOutcome Decide(int playerPieces, int reservePieces, int enemyPieces)
    {
        if (playerPieces == 0 && reservePieces == 0) return BattleOutcome.Loss;
        if (enemyPieces == 0) return BattleOutcome.Win;
        return BattleOutcome.Continue;
    }

    // Standoff adjudication by remaining piece value; alive pieces on both
    // sides plus the player's reserve. Ties go to the player.
    public static bool PlayerWinsStandoff(List<BattlePiece> pieces, List<PieceKind> reserve)
    {
        int player = 0, enemy = 0;
        for (int i = 0; i < pieces.Count; i++)
        {
            var p = pieces[i];
            if (!p.Alive) continue;
            if (p.Side == PieceSide.Player) player += PieceCatalog.ValueOf(p.Kind);
            else enemy += PieceCatalog.ValueOf(p.Kind);
        }
        if (reserve != null)
            for (int i = 0; i < reserve.Count; i++)
                player += PieceCatalog.ValueOf(reserve[i]);
        return player >= enemy;
    }
}
