// =============================================================================
// BattlePlanner
// =============================================================================
// Purpose:
//   Data-driven battle generation: how large the active board area is, how
//   long until the crumble starts, which black pieces a battle fields, and
//   which boss modifier applies. Bosses reuse the normal battle logic — a boss
//   IS a normal battle plus exactly one BossModifier consumed by HexBoard.
//
// Interactions:
//   - HexBoard.StartBattle: calls all of these to set up a battle.
//   - RunState: CrumbleDelay gambit extends the crumble timer.
// =============================================================================

using System;
using System.Collections.Generic;

namespace HexGame.Chess;

public enum BossModifier
{
    None,
    Lockmaker,      // 3 random active tiles are locked for the first 2 turns
    TaxCollector,   // the first capture each battle pays no money
    CrumbleCrown,   // crumble starts earlier, but cracked-tile captures pay bonus money
}

// Display copy for boss modifiers — the boss must never act silently: the HUD
// names it, the battle-start note explains it, and the shop warns about it.
public static class BossCatalog
{
    public static string NameOf(BossModifier boss) => boss switch
    {
        BossModifier.Lockmaker => "Lockmaker",
        BossModifier.TaxCollector => "Tax Collector",
        BossModifier.CrumbleCrown => "Crumble Crown",
        _ => "",
    };

    public static string EffectOf(BossModifier boss) => boss switch
    {
        BossModifier.Lockmaker => "3 tiles frozen for 2 turns",
        BossModifier.TaxCollector => "your first capture pays nothing",
        BossModifier.CrumbleCrown => "faster crumble, cracked captures +$2",
        _ => "",
    };
}

public static class BattlePlanner
{
    // Early battles use a small active area, expanding toward the full board.
    public static int ActiveRadius(int battle) => battle <= 2 ? 2 : battle <= 5 ? 3 : 4;

    // Player actions before the outer ring cracks. Tuned to pressure without
    // feeling unfair: enough turns to fight, not enough to stall forever.
    public static int CrumbleTurns(int battle, RunState run, BossModifier boss)
    {
        int turns = battle <= 3 ? 14 : 12;
        if (run.Has(GambitKind.CrumbleDelay)) turns += 2;
        if (boss == BossModifier.CrumbleCrown) turns -= 4;
        return Math.Max(4, turns);
    }

    public static BossModifier BossFor(int battle)
    {
        if (!RunState.IsBossBattle(battle)) return BossModifier.None;
        return (battle / 4) switch
        {
            1 => BossModifier.Lockmaker,
            2 => BossModifier.TaxCollector,
            _ => BossModifier.CrumbleCrown,
        };
    }

    // Spend a point budget on black pieces. Stronger kinds unlock as the run
    // progresses; the roster is capped so the small active area stays legible.
    private const int MaxEnemies = 8;

    public static void FillEnemyArmy(int battle, Random rng, List<PieceKind> output)
    {
        output.Clear();
        int budget = 3 + battle * 2 + (RunState.IsBossBattle(battle) ? 3 : 0);

        while (budget >= 2 && output.Count < MaxEnemies)
        {
            var pick = PickAffordable(battle, budget, rng);
            output.Add(pick);
            budget -= Cost(pick);
        }
        if (output.Count == 0) output.Add(PieceKind.Pawn);
    }

    private static int Cost(PieceKind kind) => kind switch
    {
        PieceKind.Pawn => 2,
        PieceKind.Knight => 3,
        PieceKind.Bishop => 3,
        PieceKind.King => 4,
        PieceKind.Rook => 4,
        _ => 6,   // Queen
    };

    private static PieceKind PickAffordable(int battle, int budget, Random rng)
    {
        // Roll a few times for something unlocked and affordable; fall back to pawn.
        for (int attempt = 0; attempt < 6; attempt++)
        {
            var kind = (PieceKind)rng.Next(6);
            if (kind == PieceKind.Queen && battle < 8) continue;
            if (kind == PieceKind.Rook && battle < 4) continue;
            if (kind == PieceKind.King && battle < 5) continue;
            if ((kind == PieceKind.Knight || kind == PieceKind.Bishop) && battle < 2) continue;
            if (Cost(kind) <= budget) return kind;
        }
        return PieceKind.Pawn;
    }
}
