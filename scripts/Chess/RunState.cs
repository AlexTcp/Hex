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

    public static bool IsBossBattle(int battle) => battle % 4 == 0;

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
