// =============================================================================
// Gambit
// =============================================================================
// Purpose:
//   The run-modifier ("Gambit") system: an enum of every rule-bending upgrade a
//   player can own, plus a static catalog of display name / description / shop
//   price per kind. Gambits are pure data here — their effects are implemented
//   at the single point in HexBoard's battle resolution where each rule bends.
//
// Interactions:
//   - RunState: owned gambits are a HashSet<GambitKind>.
//   - HexBoard: consults run.Has(kind) during capture/loss/crumble resolution.
//   - ShopScreen: offers unowned gambits from GambitCatalog.All.
// =============================================================================

namespace HexGame.Chess;

public enum GambitKind
{
    RookDividend,
    KnightFork,
    GoldenHarvest,
    MercyCharter,
    CrumbleDelay,
    BishopEcho,
    PawnAmbition,
    RoyalGuard,
    Quartermaster,
    Stonemason,
}

public readonly record struct GambitInfo(GambitKind Kind, string Name, string Description, int Price);

public static class GambitCatalog
{
    public static readonly GambitInfo[] All =
    {
        new(GambitKind.RookDividend, "Rook Dividend",
            "Rook captures pay +2 money.", 8),
        new(GambitKind.KnightFork, "Knight Fork",
            "Knight captures stun every adjacent enemy for 1 turn.", 8),
        new(GambitKind.GoldenHarvest, "Golden Harvest",
            "Captures on a Gold tile pay +2 extra money.", 7),
        new(GambitKind.MercyCharter, "Mercy Charter",
            "The first piece you lose each battle returns to your reserve instead of dying.", 9),
        new(GambitKind.CrumbleDelay, "Crumble Delay",
            "The board holds together 2 turns longer each battle.", 7),
        new(GambitKind.BishopEcho, "Bishop Echo",
            "Once per battle, a bishop slides one extra step after a capture if the next hex is empty.", 8),
        new(GambitKind.PawnAmbition, "Pawn Ambition",
            "Pawn promotions also grant +3 money.", 6),
        new(GambitKind.RoyalGuard, "Royal Guard",
            "The first capture against your King each battle is blocked.", 9),
        new(GambitKind.Quartermaster, "Quartermaster",
            "Clearing a battle pays +3 extra money.", 6),
        new(GambitKind.Stonemason, "Stonemason",
            "Cracked tiles hold together one extra turn before collapsing.", 7),
    };

    public static GambitInfo Info(GambitKind kind)
    {
        for (int i = 0; i < All.Length; i++)
            if (All[i].Kind == kind) return All[i];
        return All[0];
    }
}
