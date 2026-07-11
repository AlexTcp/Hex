// =============================================================================
// EnemyPlanner
// =============================================================================
// Purpose:
//   The pure decision half of the enemy turn: exactly one enemy piece acts per
//   player action — take the highest-value capture if any exists, otherwise
//   the move that most closes on the nearest player piece, otherwise a
//   uniformly random legal move (reservoir-sampled). No node access, no side
//   effects: HexBoard executes whatever this chooses (and handles stun
//   recovery and blocking effects like Royal Guard / Shield tiles itself).
//
// Zero-alloc invariant:
//   Iterates the caller's piece list and fills the caller's scratch buffer via
//   PieceRules.LegalMoves. Never allocates.
//
// Interactions:
//   - HexBoard.EnemyAct: sole production caller.
//   - Dev harness tests: deterministic given a seeded Random.
// =============================================================================

using System;
using System.Collections.Generic;
using HexGame.Hex;

namespace HexGame.Chess;

public static class EnemyPlanner
{
    // Returns false when no non-stunned enemy has a legal move.
    public static bool ChooseAction(List<BattlePiece> pieces, IBattleQuery board,
        Dictionary<HexCoord, BattlePiece> occupied, Random rng, List<HexCoord> scratch,
        out BattlePiece piece, out HexCoord dest, out bool capture)
    {
        BattlePiece bestCapPiece = null; HexCoord bestCapDest = default; int bestCapValue = -1;
        BattlePiece bestAppPiece = null; HexCoord bestAppDest = default; int bestAppDist = int.MaxValue;
        BattlePiece anyPiece = null; HexCoord anyDest = default; int anyCount = 0;

        for (int i = 0; i < pieces.Count; i++)
        {
            var e = pieces[i];
            if (!e.Alive || e.Side != PieceSide.Enemy) continue;
            if (e.StunTurns > 0) continue;

            PieceRules.LegalMoves(e.Kind, PieceSide.Enemy, e.Coord, board, scratch);
            for (int m = 0; m < scratch.Count; m++)
            {
                var d = scratch[m];

                if (occupied.TryGetValue(d, out var victim) && victim.Side == PieceSide.Player)
                {
                    int v = PieceCatalog.ValueOf(victim.Kind);
                    if (v > bestCapValue) { bestCapValue = v; bestCapPiece = e; bestCapDest = d; }
                    continue;
                }

                int dist = DistanceToNearestPlayer(pieces, d);
                if (dist < bestAppDist) { bestAppDist = dist; bestAppPiece = e; bestAppDest = d; }

                // Reservoir sample one uniformly random legal move as fallback.
                anyCount++;
                if (rng.Next(anyCount) == 0) { anyPiece = e; anyDest = d; }
            }
        }

        if (bestCapPiece != null)
        {
            piece = bestCapPiece; dest = bestCapDest; capture = true;
            return true;
        }
        if (bestAppPiece != null && bestAppDist != int.MaxValue)
        {
            piece = bestAppPiece; dest = bestAppDest; capture = false;
            return true;
        }
        if (anyPiece != null)
        {
            piece = anyPiece; dest = anyDest; capture = false;
            return true;
        }

        piece = null; dest = default; capture = false;
        return false;
    }

    private static int DistanceToNearestPlayer(List<BattlePiece> pieces, HexCoord from)
    {
        int best = int.MaxValue;
        for (int i = 0; i < pieces.Count; i++)
        {
            var p = pieces[i];
            if (!p.Alive || p.Side != PieceSide.Player) continue;
            int d = p.Coord.Distance(from);
            if (d < best) best = d;
        }
        return best;
    }
}
