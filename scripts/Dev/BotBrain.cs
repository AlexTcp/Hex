// =============================================================================
// BotBrain — shared autoplay decision logic (dev tool)
// =============================================================================
// Purpose:
//   One player action per call against a live HexBoard, used by both headless
//   harnesses (AutoPlayDriver plays whole runs; UiFlowDriver plays battles
//   inside the real screen flow). Plays like a competent human: safe captures
//   over unsafe ones (by victim value), then safe approach moves, then
//   desperate ones; occasionally deploys from the reserve. All taps go through
//   the real OnTileTapped input path via the DEBUG hooks.
// =============================================================================

#if DEBUG
using System;
using System.Collections.Generic;
using HexGame.Board;
using HexGame.Chess;
using HexGame.Hex;

namespace HexGame.Dev;

public enum BotActionResult
{
    Moved,          // a piece was selected and moved
    Deployed,       // a reserve piece was placed
    NoAction,       // nothing legal to do (a bug if the battle is running)
    Inconsistent,   // a legal move was not highlighted after selecting (a bug)
}

public enum BotMode
{
    Normal,     // play to win: safe captures > captures > safe approach
    Suicidal,   // court danger, refuse captures — reach defeat deterministically
    Stall,      // safe, capture-averse keep-away — let the crumble arrive on camera
}

public static class BotBrain
{
    private static readonly List<HexCoord> Moves = new(64);
    private static readonly List<BattlePiece> Mine = new(16);

    public static BotActionResult TakeOneAction(HexBoard board, RunState run, Random rng,
        BotMode mode = BotMode.Normal)
    {
        Mine.Clear();
        foreach (var p in board.DebugPieces)
            if (p.Alive && p.Side == PieceSide.Player) Mine.Add(p);
        Shuffle(Mine, rng);

        // Reinforce eagerly when the board presence is thin; otherwise trickle
        // the reserve in occasionally.
        if (mode == BotMode.Normal && run.Reserve.Count > 0
            && (Mine.Count < 4 || rng.Next(6) == 0)
            && TryDeploy(board, run, rng))
            return BotActionResult.Deployed;

        // Score every legal (piece, destination) pair the way a human reads the
        // board: safe captures > unsafe captures (by victim value), then safe
        // approach moves, then desperate ones. A dash of noise for coverage.
        BattlePiece movePiece = null;
        HexCoord moveDest = default;
        int bestRank = int.MinValue;
        foreach (var p in Mine)
        {
            PieceRules.LegalMoves(p.Kind, PieceSide.Player, p.Coord, board, Moves);
            for (int m = 0; m < Moves.Count; m++)
            {
                var dest = Moves[m];
                bool capture = board.OccupantSide(dest) == PieceSide.Enemy;
                bool safe = !board.DebugIsDeathTile(p, dest);
                int rank;
                if (mode == BotMode.Stall)
                    rank = (safe ? 3000 : 0) + (capture ? -2000 : 0)
                        + DistanceToNearestEnemy(board, dest) * 10;
                else if (mode == BotMode.Suicidal)
                    rank = (capture ? 0 : 1000) + (safe ? 0 : 2000)
                        - DistanceToNearestEnemy(board, dest) * 10;
                else if (capture)
                    rank = (safe ? 4000 : 3000) + VictimValueAt(board, dest) * 10;
                else
                    rank = (safe ? 2000 : 1000) - DistanceToNearestEnemy(board, dest) * 10;
                rank += rng.Next(10);
                if (rank > bestRank)
                {
                    bestRank = rank;
                    movePiece = p;
                    moveDest = dest;
                }
            }
        }
        if (movePiece == null)
            return run.Reserve.Count > 0 && TryDeploy(board, run, rng)
                ? BotActionResult.Deployed
                : BotActionResult.NoAction;

        board.DebugTap(movePiece.Coord);
        if (!board.DebugIsHighlighted(moveDest)) return BotActionResult.Inconsistent;
        board.DebugTap(moveDest);
        return BotActionResult.Moved;
    }

    private static bool TryDeploy(HexBoard board, RunState run, Random rng)
    {
        board.BeginDeploy(rng.Next(run.Reserve.Count));
        var targets = new List<HexCoord>(board.DebugHighlighted);
        if (targets.Count == 0) return false;   // board disarmed itself: no room
        board.DebugTap(targets[rng.Next(targets.Count)]);
        return true;
    }

    private static int VictimValueAt(HexBoard board, HexCoord dest)
    {
        foreach (var p in board.DebugPieces)
            if (p.Alive && p.Side == PieceSide.Enemy && p.Coord == dest)
                return PieceCatalog.ValueOf(p.Kind);
        return 0;
    }

    private static int DistanceToNearestEnemy(HexBoard board, HexCoord from)
    {
        int best = int.MaxValue;
        foreach (var p in board.DebugPieces)
        {
            if (!p.Alive || p.Side != PieceSide.Enemy) continue;
            int d = p.Coord.Distance(from);
            if (d < best) best = d;
        }
        return best;
    }

    private static void Shuffle(List<BattlePiece> list, Random rng)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
#endif
