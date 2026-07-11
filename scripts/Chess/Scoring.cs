// =============================================================================
// Scoring
// =============================================================================
// Purpose:
//   Every score/money payout formula in one place. These numbers shape the
//   whole run economy (measured difficulty curve: ~25% bot win rate) and were
//   previously magic literals scattered through HexBoard's resolution code —
//   tune them here, nowhere else.
//
// Interactions:
//   - HexBoard: capture score, battle-clear score/pay, survivors bonus.
//   - Capture MONEY is data-driven separately (PieceCatalog values plus
//     Gold-tile / gambit / boss bonuses resolved in TryCapturePlayerMove).
// =============================================================================

namespace HexGame.Chess;

public static class Scoring
{
    public const int CaptureScorePerValue = 100;   // score = piece value × this
    public const int ClearScorePerBattle = 250;    // score = battle × this
    public const int SurvivorScoreEach = 200;      // final-win bonus per army piece
    public const int ClearPayBase = 4;             // money = base + battle
    public const int QuartermasterClearBonus = 3;

    public static int CaptureScore(PieceKind kind) =>
        PieceCatalog.ValueOf(kind) * CaptureScorePerValue;

    public static int ClearScore(int battle) => ClearScorePerBattle * battle;

    public static int ClearPay(int battle, bool quartermaster) =>
        ClearPayBase + battle + (quartermaster ? QuartermasterClearBonus : 0);

    public static int SurvivorsBonus(int survivors) => SurvivorScoreEach * survivors;
}
