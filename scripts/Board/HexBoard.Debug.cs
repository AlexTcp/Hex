// =============================================================================
// HexBoard.Debug — DEBUG-only inspection/drive hooks
// =============================================================================
// Purpose:
//   Test surface for the headless autoplay harness (scripts/Dev/
//   AutoPlayDriver.cs): read-only views of the battle state plus a tap
//   simulator that enters through the same OnTileTapped path as real input.
//   Compiled only in DEBUG builds — release exports ship HexBoard untouched.
// =============================================================================

#if DEBUG
using System.Collections.Generic;
using HexGame.Chess;
using HexGame.Hex;

namespace HexGame.Board;

public partial class HexBoard
{
    public bool DebugRunning => _running;
    public IReadOnlyList<BattlePiece> DebugPieces => _pieces;
    public IEnumerable<HexCoord> DebugHighlighted => _highlighted;
    public bool DebugIsHighlighted(HexCoord c) => _highlighted.Contains(c);
    public void DebugTap(HexCoord c) => OnTileTapped(c);
    public bool DebugIsDeathTile(BattlePiece piece, HexCoord dest) => IsDeathTile(piece, dest);
    public bool DebugCaptureBlockedAt(HexCoord dest, PieceKind kind) => CaptureBlockedAt(dest, kind);
    public int DebugCrackedCount => _cracked.Count;
    public int DebugLockedCount => _locked.Count;

    // Reseed the board's battle RNG so a harness failure replays exactly.
    public void DebugSeedRng(int seed) => _rng = new System.Random(seed);
}
#endif
