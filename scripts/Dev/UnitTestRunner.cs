// =============================================================================
// UnitTestRunner — fast deterministic checks for the pure game logic (dev tool)
// =============================================================================
// Purpose:
//   The other harnesses are integration/statistical; this one asserts exact
//   invariants of the Godot-free logic: HexCoord math, PieceRules move
//   generation (against a fake board), EnemyPlanner decisions, BattlePlanner
//   schedules/rosters, ShopOffers gating, RunState and Scoring rules.
//
// Run:
//   godot --headless --path . res://dev/tests.tscn
//
// Exit codes: 0 = all checks pass, 1 = failures, 2 = exception. DEBUG-only.
// =============================================================================

using Godot;
#if DEBUG
using System;
using System.Collections.Generic;
using HexGame.Chess;
using HexGame.Hex;
#endif

namespace HexGame.Dev;

public partial class UnitTestRunner : Node
{
#if DEBUG
    private int _checks;
    private int _failures;

    // Open hex board of a given radius with explicit per-coord occupancy.
    private sealed class FakeBoard : IBattleQuery
    {
        public int Radius = 4;
        public readonly Dictionary<HexCoord, PieceSide> Occ = new();
        public bool IsPlayable(HexCoord c) => c.DistanceFromOrigin() <= Radius;
        public PieceSide? OccupantSide(HexCoord c) => Occ.TryGetValue(c, out var s) ? s : null;
    }

    public override void _Ready()
    {
        try
        {
            TestHexCoord();
            TestPieceRules();
            TestEnemyPlanner();
            TestBattlePlanner();
            TestShopOffers();
            TestRunStateAndScoring();
        }
        catch (Exception e)
        {
            GD.PrintErr($"[TESTS] EXCEPTION: {e}");
            GetTree().Quit(2);
            return;
        }
        GD.Print($"[TESTS] {_checks} checks, {_failures} failures");
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    private void Check(bool cond, string what)
    {
        _checks++;
        if (!cond)
        {
            _failures++;
            GD.PrintErr($"[TESTS] FAIL: {what}");
        }
    }

    // ----- HexCoord ---------------------------------------------------------

    private void TestHexCoord()
    {
        for (int r = 0; r <= 4; r++)
        {
            var buf = new List<HexCoord>();
            HexCoord.Within(r, buf);
            Check(buf.Count == 1 + 3 * r * (r + 1), $"Within({r}) count = {buf.Count}");
            var set = new HashSet<HexCoord>(buf);
            Check(set.Count == buf.Count, $"Within({r}) unique");
            foreach (var c in buf)
            {
                Check(c.DistanceFromOrigin() <= r, $"Within({r}) contains {c} beyond radius");
                Check(c.S == -c.Q - c.R, $"S invariant at {c}");
            }
        }

        for (int r = 1; r <= 4; r++)
        {
            var ring = new List<HexCoord>();
            HexCoord.Ring(r, ring);
            Check(ring.Count == 6 * r, $"Ring({r}) count = {ring.Count}");
            foreach (var c in ring)
                Check(c.DistanceFromOrigin() == r, $"Ring({r}) coord {c} at wrong distance");
        }
        var ring0 = new List<HexCoord>();
        HexCoord.Ring(0, ring0);
        Check(ring0.Count == 1 && ring0[0] == HexCoord.Zero, "Ring(0) is the origin");

        var a = new HexCoord(2, -1);
        var b = new HexCoord(-1, 3);
        Check(a.Distance(b) == b.Distance(a), "distance symmetry");
        Check(a.Distance(a) == 0, "distance to self");
    }

    // ----- PieceRules ---------------------------------------------------------

    private void TestPieceRules()
    {
        var board = new FakeBoard();
        var moves = new List<HexCoord>(64);

        int Count(PieceKind kind, PieceSide side, HexCoord from)
        {
            PieceRules.LegalMoves(kind, side, from, board, moves);
            return moves.Count;
        }

        // Open radius-4 board from the centre.
        Check(Count(PieceKind.Knight, PieceSide.Player, HexCoord.Zero) == 12, "knight centre = 12");
        Check(Count(PieceKind.Rook, PieceSide.Player, HexCoord.Zero) == 24, "rook centre = 24");
        Check(Count(PieceKind.Bishop, PieceSide.Player, HexCoord.Zero) == 12, "bishop centre = 12");
        Check(Count(PieceKind.Queen, PieceSide.Player, HexCoord.Zero) == 36, "queen centre = 36");
        Check(Count(PieceKind.King, PieceSide.Player, HexCoord.Zero) == 12, "king centre = 12");

        // Pawns: two forward hexes; blocked by friendlies; capture enemies.
        var pawnAt = new HexCoord(0, 1);
        Check(Count(PieceKind.Pawn, PieceSide.Player, pawnAt) == 2, "pawn open = 2");
        board.Occ[new HexCoord(1, 0)] = PieceSide.Player;
        Check(Count(PieceKind.Pawn, PieceSide.Player, pawnAt) == 1, "pawn friendly-blocked = 1");
        board.Occ[new HexCoord(1, 0)] = PieceSide.Enemy;
        Check(Count(PieceKind.Pawn, PieceSide.Player, pawnAt) == 2, "pawn can capture = 2");
        board.Occ.Clear();

        // Sliders stop on blockers; capture squares are included for enemies only.
        board.Occ[new HexCoord(2, 0)] = PieceSide.Player;
        Check(Count(PieceKind.Rook, PieceSide.Player, HexCoord.Zero) == 21, "rook friendly block = 21");
        board.Occ[new HexCoord(2, 0)] = PieceSide.Enemy;
        Check(Count(PieceKind.Rook, PieceSide.Player, HexCoord.Zero) == 22, "rook enemy capture = 22");
        board.Occ.Clear();

        // Stranded pawns: every forward hex off the active set, per side.
        var active = new HashSet<HexCoord>();
        foreach (var c in HexCoord.Within(2)) active.Add(c);
        Check(PieceRules.PawnStranded(PieceSide.Player, new HexCoord(0, -2), active),
            "player pawn stranded at the north edge");
        Check(!PieceRules.PawnStranded(PieceSide.Player, new HexCoord(0, 2), active),
            "player pawn mobile at the south edge");
        Check(PieceRules.PawnStranded(PieceSide.Enemy, new HexCoord(0, 2), active),
            "enemy pawn stranded at the south edge");
        Check(!PieceRules.PawnStranded(PieceSide.Enemy, new HexCoord(0, -2), active),
            "enemy pawn mobile at the north edge");
    }

    // ----- EnemyPlanner --------------------------------------------------------

    private void TestEnemyPlanner()
    {
        var board = new FakeBoard();
        var scratch = new List<HexCoord>(64);
        var rng = new Random(7);

        List<BattlePiece> Pieces(params BattlePiece[] ps) => new(ps);
        Dictionary<HexCoord, BattlePiece> Occupy(List<BattlePiece> ps)
        {
            var d = new Dictionary<HexCoord, BattlePiece>();
            board.Occ.Clear();
            foreach (var p in ps)
            {
                d[p.Coord] = p;
                board.Occ[p.Coord] = p.Side;
            }
            return d;
        }

        // A rook with a clear line takes the capture.
        var rook = new BattlePiece { Kind = PieceKind.Rook, Side = PieceSide.Enemy, Coord = new HexCoord(0, -2) };
        var pawn = new BattlePiece { Kind = PieceKind.Pawn, Side = PieceSide.Player, Coord = new HexCoord(0, 2) };
        var pieces = Pieces(rook, pawn);
        bool acted = EnemyPlanner.ChooseAction(pieces, board, Occupy(pieces), rng, scratch,
            out var chosen, out var dest, out var capture);
        Check(acted && capture && chosen == rook && dest == pawn.Coord, "planner takes the open capture");

        // Higher-value victim wins.
        var queen = new BattlePiece { Kind = PieceKind.Queen, Side = PieceSide.Player, Coord = new HexCoord(2, -2) };
        pieces = Pieces(rook, pawn, queen);
        acted = EnemyPlanner.ChooseAction(pieces, board, Occupy(pieces), rng, scratch,
            out chosen, out dest, out capture);
        Check(acted && capture && dest == queen.Coord, "planner prefers the queen");

        // Stunned enemies sit out; with no other enemy nothing acts.
        rook.StunTurns = 1;
        pieces = Pieces(rook, pawn);
        acted = EnemyPlanner.ChooseAction(pieces, board, Occupy(pieces), rng, scratch,
            out chosen, out dest, out capture);
        Check(!acted, "stunned enemy does not act");
        rook.StunTurns = 0;

        // No capture available: (1,2) sits on none of the rook's three lines
        // through (0,-2), so the chosen move must close on the nearest player.
        var far = new BattlePiece { Kind = PieceKind.Pawn, Side = PieceSide.Player, Coord = new HexCoord(1, 2) };
        pieces = Pieces(rook, far);
        acted = EnemyPlanner.ChooseAction(pieces, board, Occupy(pieces), rng, scratch,
            out chosen, out dest, out capture);
        Check(acted && !capture && dest.Distance(far.Coord) < rook.Coord.Distance(far.Coord),
            "planner approaches when no capture exists");
    }

    // ----- BattlePlanner --------------------------------------------------------

    private void TestBattlePlanner()
    {
        Check(BattlePlanner.BossFor(4) == BossModifier.Lockmaker, "boss 4 = Lockmaker");
        Check(BattlePlanner.BossFor(8) == BossModifier.TaxCollector, "boss 8 = Tax Collector");
        Check(BattlePlanner.BossFor(12) == BossModifier.CrumbleCrown, "boss 12 = Crumble Crown");
        for (int b = 1; b <= 11; b++)
            if (b % 4 != 0)
                Check(BattlePlanner.BossFor(b) == BossModifier.None, $"battle {b} has no boss");

        var run = new RunState();
        Check(BattlePlanner.CrumbleTurns(1, run, BossModifier.None) == 14, "crumble turns early");
        Check(BattlePlanner.CrumbleTurns(5, run, BossModifier.None) == 12, "crumble turns late");
        Check(BattlePlanner.CrumbleTurns(4, run, BossModifier.CrumbleCrown) == 8, "crumble crown -4");
        run.Gambits.Add(GambitKind.CrumbleDelay);
        Check(BattlePlanner.CrumbleTurns(5, run, BossModifier.None) == 14, "crumble delay +2");

        // Same seed → same roster (determinism the resume-safe harnesses rely on).
        var armyA = new List<PieceKind>();
        var armyB = new List<PieceKind>();
        BattlePlanner.FillEnemyArmy(7, new Random(123), armyA);
        BattlePlanner.FillEnemyArmy(7, new Random(123), armyB);
        Check(armyA.Count == armyB.Count, "roster deterministic (count)");
        for (int i = 0; i < Math.Min(armyA.Count, armyB.Count); i++)
            Check(armyA[i] == armyB[i], $"roster deterministic (slot {i})");

        // The finale always fields the crown; themed rosters are single-kind.
        for (int seed = 0; seed < 50; seed++)
        {
            var army = new List<PieceKind>();
            BattlePlanner.FillEnemyArmy(12, new Random(seed), army);
            Check(army.Count > 0 && army[0] == PieceKind.Queen, $"finale queen (seed {seed})");

            var mid = new List<PieceKind>();
            string theme = BattlePlanner.FillEnemyArmy(6, new Random(seed), mid);
            Check(mid.Count > 0, $"battle 6 roster non-empty (seed {seed})");
            if (theme != null)
                for (int i = 1; i < mid.Count; i++)
                    Check(mid[i] == mid[0], $"themed roster single-kind (seed {seed})");
        }
    }

    // ----- ShopOffers ------------------------------------------------------------

    private void TestShopOffers()
    {
        var rng = new Random(99);
        for (int i = 0; i < 400; i++)
            Check(ShopOffers.RollPieceOffer(3, rng) != PieceKind.Queen, "no early queen offers");

        var run = new RunState();
        var coord = ShopOffers.RollUpgradeCoord(run, rng);
        Check(coord.HasValue && coord.Value.DistanceFromOrigin() <= ShopOffers.UpgradeRadius,
            "upgrade coord central");

        foreach (var c in HexCoord.Within(ShopOffers.UpgradeRadius))
            run.TileUpgrades[c] = TileUpgradeKind.Gold;
        Check(ShopOffers.RollUpgradeCoord(run, rng) == null, "no coord when disc claimed");
    }

    // ----- RunState & Scoring -------------------------------------------------------

    private void TestRunStateAndScoring()
    {
        var run = RunState.NewRun(new Random(5));
        Check(run.Army.Count == 3, "new run: 3 starters");
        Check(run.Money == RunState.StartingMoney, "new run: starting purse");
        Check(run.Battle == 1, "new run: battle 1");

        var stock = new RunState();
        for (int i = 0; i < RunState.ArmyCap + 2; i++) stock.AddPiece(PieceKind.Pawn);
        Check(stock.Army.Count == RunState.ArmyCap, "AddPiece fills to the cap");
        Check(stock.Reserve.Count == 2, "AddPiece overflows to the reserve");

        Check(Scoring.CaptureScore(PieceKind.Queen) == 600, "queen capture score");
        Check(Scoring.ClearScore(3) == 750, "clear score");
        Check(Scoring.ClearPay(5, false) == 9, "clear pay");
        Check(Scoring.ClearPay(5, true) == 12, "clear pay + quartermaster");
        Check(Scoring.SurvivorsBonus(4) == 800, "survivors bonus");
    }
#else
    public override void _Ready() => GetTree().Quit();
#endif
}
