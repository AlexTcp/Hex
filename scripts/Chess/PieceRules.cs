// =============================================================================
// PieceRules
// =============================================================================
// Purpose:
//   The hex-chess movement core: PieceKind / PieceSide enums, the direction
//   tables (6 rook edge directions, 6 bishop diagonals, 12 knight leaps), and
//   the single zero-alloc LegalMoves generator every caller uses. Movement is
//   occupancy-aware (sliders stop on blockers, captures only land on enemy
//   pieces) via the IBattleQuery interface the battle controller implements.
//
//   Pawns are simplified hex pawns: player pawns advance/capture on the two
//   "forward" directions NE/NW (toward negative R, the enemy side); enemy
//   pawns use SE/SW. A forward cell is enterable whether empty (move) or
//   enemy-occupied (capture).
//
// Zero-alloc invariant:
//   LegalMoves fills the caller's List<HexCoord> buffer. Never allocate inside
//   a movement rule.
//
// Interactions:
//   - HexCoord: all offsets/arithmetic.
//   - HexBoard: implements IBattleQuery and calls LegalMoves for selection,
//     enemy AI and danger-tile marking.
//   - PieceCatalog: display names / values / prices per kind.
// =============================================================================

using System.Collections.Generic;
using HexGame.Hex;

namespace HexGame.Chess;

public enum PieceKind { Pawn, King, Rook, Bishop, Knight, Queen }

public enum PieceSide { Player, Enemy }

// Minimal board view the move generator needs. Implemented by HexBoard, which
// may substitute a hypothetical occupancy (for danger-tile prediction).
public interface IBattleQuery
{
    bool IsPlayable(HexCoord c);            // active, not collapsed, not locked
    // Side of the piece on c, or null when empty. Must honour any hypothetical
    // overlay the implementer has active.
    PieceSide? OccupantSide(HexCoord c);
}

public static class PieceRules
{
    // Rook: the six hex edge directions.
    public static readonly HexCoord[] RookDirs = HexCoord.Directions;

    // Bishop: the six hex "diagonals" (between adjacent edge directions).
    public static readonly HexCoord[] BishopDirs =
    {
        new(1, 1), new(2, -1), new(1, -2), new(-1, -1), new(-2, 1), new(-1, 2),
    };

    // Knight: 12 fixed symmetric leaps — two steps in one edge direction plus
    // one step in an adjacent edge direction. All lie at hex distance 3 and on
    // neither a rook line nor a bishop diagonal (validated below).
    public static readonly HexCoord[] KnightLeaps =
    {
        new(3, -1), new(2, 1),     // around E
        new(3, -2), new(2, -3),    // around NE
        new(1, -3), new(-1, -2),   // around NW
        new(-2, -1), new(-3, 1),   // around W
        new(-3, 2), new(-2, 3),    // around SW
        new(-1, 3), new(1, 2),     // around SE
    };

    // Pawn forward directions per side. Player pawns push toward the enemy
    // rows (negative R); enemy pawns push toward the player rows (positive R).
    public static readonly HexCoord[] PlayerPawnDirs = { new(1, -1), new(0, -1) };  // NE, NW
    public static readonly HexCoord[] EnemyPawnDirs = { new(0, 1), new(-1, 1) };    // SE, SW

    static PieceRules()
    {
#if DEBUG
        // Validate the knight table: 12 unique offsets, all at distance 3,
        // none colinear with a rook direction or bishop diagonal.
        var seen = new HashSet<HexCoord>();
        foreach (var leap in KnightLeaps)
        {
            if (!seen.Add(leap))
                throw new System.InvalidOperationException($"Duplicate knight leap {leap}");
            if (leap.DistanceFromOrigin() != 3)
                throw new System.InvalidOperationException($"Knight leap {leap} not at distance 3");
            foreach (var d in RookDirs)
                if (leap == d * 3)
                    throw new System.InvalidOperationException($"Knight leap {leap} lies on a rook line");
            foreach (var d in BishopDirs)
                if (leap == d)
                    throw new System.InvalidOperationException($"Knight leap {leap} lies on a bishop diagonal");
        }
#endif
    }

    // Fill `output` with every legal destination for a piece of `kind`/`side`
    // standing on `from`. Destinations are playable tiles that are empty or
    // hold an opposing piece (which would be captured). Zero-alloc: writes into
    // the caller's buffer only.
    public static void LegalMoves(PieceKind kind, PieceSide side, HexCoord from,
        IBattleQuery board, List<HexCoord> output)
    {
        output.Clear();
        switch (kind)
        {
            case PieceKind.Rook:
                Slide(from, side, RookDirs, board, output);
                break;
            case PieceKind.Bishop:
                Slide(from, side, BishopDirs, board, output);
                break;
            case PieceKind.Queen:
                Slide(from, side, RookDirs, board, output);
                Slide(from, side, BishopDirs, board, output);
                break;
            case PieceKind.King:
                Step(from, side, RookDirs, board, output);
                Step(from, side, BishopDirs, board, output);
                break;
            case PieceKind.Knight:
                Step(from, side, KnightLeaps, board, output);
                break;
            case PieceKind.Pawn:
                Step(from, side, side == PieceSide.Player ? PlayerPawnDirs : EnemyPawnDirs, board, output);
                break;
        }
    }

    // True when a pawn on `from` can never move again: every forward hex has
    // left the given active-tile set. Occupancy is deliberately ignored —
    // blockers are temporary, missing tiles are not. Drives the stranded-pawn
    // promotion that keeps collapsed endgames resolvable.
    public static bool PawnStranded(PieceSide side, HexCoord from, HashSet<HexCoord> activeTiles)
    {
        var dirs = side == PieceSide.Player ? PlayerPawnDirs : EnemyPawnDirs;
        for (int d = 0; d < dirs.Length; d++)
            if (activeTiles.Contains(from + dirs[d])) return false;
        return true;
    }

    private static void Slide(HexCoord from, PieceSide side, HexCoord[] dirs,
        IBattleQuery board, List<HexCoord> output)
    {
        for (int d = 0; d < dirs.Length; d++)
        {
            var pos = from + dirs[d];
            while (board.IsPlayable(pos))
            {
                var occ = board.OccupantSide(pos);
                if (occ == null) { output.Add(pos); pos += dirs[d]; continue; }
                if (occ != side) output.Add(pos);      // capture, then blocked
                break;                                  // friendly or captured: stop
            }
        }
    }

    private static void Step(HexCoord from, PieceSide side, HexCoord[] offsets,
        IBattleQuery board, List<HexCoord> output)
    {
        for (int i = 0; i < offsets.Length; i++)
        {
            var pos = from + offsets[i];
            if (!board.IsPlayable(pos)) continue;
            var occ = board.OccupantSide(pos);
            if (occ == null || occ != side) output.Add(pos);
        }
    }
}

// Display names, capture values (money), and shop prices per kind, indexed by
// (int)PieceKind. Read by the shop/HUD without instantiating anything.
public static class PieceCatalog
{
    public readonly record struct PieceInfo(string Name, string Monogram, string Description, int CaptureValue, int Price);

    public static readonly PieceInfo[] All =
    {
        new("Pawn",   "Pa", "Advances toward the enemy on the two forward hexes; captures the same way. Promotes when it captures on the outer ring.", 2, 3),
        new("King",   "Ki", "Steps one hex in any of the twelve directions. Sturdy and flexible.", 5, 6),
        new("Rook",   "Ro", "Slides any distance along the six edge directions.", 4, 7),
        new("Bishop", "Bi", "Slides any distance along the six hex diagonals.", 3, 5),
        new("Knight", "Kn", "Leaps to one of twelve fixed hexes, ignoring blockers.", 3, 5),
        new("Queen",  "Qu", "Slides along all twelve edge and diagonal lines.", 6, 12),
    };

    public static PieceInfo Info(PieceKind kind) => All[(int)kind];
    public static string NameOf(PieceKind kind) => All[(int)kind].Name;
    public static int ValueOf(PieceKind kind) => All[(int)kind].CaptureValue;
}
