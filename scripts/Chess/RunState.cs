// =============================================================================
// RunState
// =============================================================================
// Purpose:
//   The single mutable record of one roguelike run: money, score, battle
//   number, the army (pieces auto-deployed at battle start), the reserve
//   (off-board pieces deployable mid-battle for an action), owned Gambits and
//   the permanent per-coordinate tile upgrades. Owned by GameSession; handed to
//   HexBoard (which mutates it during battles), ShopScreen (purchases) and the
//   HUD (display).
//
// Interactions:
//   - GameSession: creates via NewRun, commits Battle/Score into persisted
//     records at run end.
//   - HexBoard / ShopScreen / Hud / NewRunScreen: read + mutate live state.
// =============================================================================

using System;
using System.Collections.Generic;
using HexGame.Hex;

namespace HexGame.Chess;

public sealed class RunState
{
    public const int FinalBattle = 12;      // three boss cycles; winning battle 12 wins the run
    public const int StartingMoney = 4;
    public const int ArmyCap = 8;

    public int Money;
    public int Score;
    public int Battle = 1;                  // 1-based; the battle about to be (or being) fought

    // Run-lifetime tallies for the end screen (not persisted).
    public int CapturesMade;
    public int PiecesLost;
    public int MoneyEarned;

    public readonly List<PieceKind> Army = new();
    public readonly List<PieceKind> Reserve = new();
    public readonly HashSet<GambitKind> Gambits = new();
    public readonly Dictionary<HexCoord, TileUpgradeKind> TileUpgrades = new();

    public bool Has(GambitKind g) => Gambits.Contains(g);

    // A bought piece joins the army, overflowing to the reserve at the cap.
    public void AddPiece(PieceKind kind)
    {
        if (Army.Count < ArmyCap) Army.Add(kind);
        else Reserve.Add(kind);
    }

    // ----- Purchases: the run owns its own economy mutations so a shop debit and
    // its effect are one atomic, testable step (the UI only gates + presents).

    // Debit `amount` iff affordable; returns whether the purchase went through.
    public bool TrySpend(int amount)
    {
        if (Money < amount) return false;
        Money -= amount;
        return true;
    }

    public void AddGambit(GambitKind kind) => Gambits.Add(kind);
    public void SetTileUpgrade(HexCoord coord, TileUpgradeKind kind) => TileUpgrades[coord] = kind;

    public static bool IsBossBattle(int battle) => battle % 4 == 0;

    // The run is won once the final battle is cleared: WinBattle post-increments
    // Battle, so it lands past FinalBattle. The single home for this rule (the Board,
    // the screen flow, and the harness all asked the same question independently).
    public bool RunWon => Battle > FinalBattle;

    // Starter pool: everything but the Queen (she's a late-run shop prize).
    private static readonly PieceKind[] StarterPool =
    {
        PieceKind.Pawn, PieceKind.Pawn, PieceKind.Knight, PieceKind.Bishop,
        PieceKind.Rook, PieceKind.King,
    };

    public static RunState NewRun(Random rng)
    {
        var run = new RunState { Money = StartingMoney };
        for (int i = 0; i < 3; i++)
            run.Army.Add(StarterPool[rng.Next(StarterPool.Length)]);
        return run;
    }
}
