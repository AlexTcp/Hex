// =============================================================================
// AutoPlayDriver — headless autoplay bot (dev tool)
// =============================================================================
// Purpose:
//   Plays complete roguelike runs against a real HexBoard, synchronously, with
//   no rendering or UI: battles via simulated taps (same OnTileTapped path as
//   real input), reserve deploys, and shop purchases mirroring ShopScreen's
//   effects. Asserts the game never crashes, every legal move is actually
//   tappable, and a running battle always leaves the player an action.
//
// Run:
//   godot --headless --path . res://dev/autoplay.tscn -- runs=40
//
// Exit codes: 0 = all runs completed clean, 1 = playability failure,
//   2 = unhandled exception. Only functional in DEBUG builds (the release
//   stub quits immediately so exports stay valid).
// =============================================================================

using Godot;
#if DEBUG
using System;
using System.Collections.Generic;
using HexGame.Board;
using HexGame.Chess;
using HexGame.Hex;
#endif

namespace HexGame.Dev;

public partial class AutoPlayDriver : Node
{
#if DEBUG
    private const int DefaultRuns = 30;
    private const int MaxActionsPerBattle = 500;

    private HexBoard _board;
    private readonly Random _rng = new();
    private readonly List<HexCoord> _moves = new(64);
    private readonly List<BattlePiece> _mine = new(16);
    private bool _won, _lost;
    private int _fails;

    public override void _Ready()
    {
        _board = new HexBoard();
        AddChild(_board);
        _board.BattleWon += () => _won = true;
        _board.BattleLost += () => _lost = true;

        int runs = DefaultRuns;
        foreach (var arg in OS.GetCmdlineUserArgs())
            if (arg.StartsWith("runs=")) runs = int.Parse(arg.Substring(5));

        int victories = 0, defeats = 0;
        try
        {
            for (int i = 0; i < runs; i++)
                if (PlayRun(i)) victories++; else defeats++;
        }
        catch (Exception e)
        {
            GD.PrintErr($"[AUTOPLAY] EXCEPTION: {e}");
            GetTree().Quit(2);
            return;
        }

        GD.Print($"[AUTOPLAY] done: {runs} runs, {victories} victories, {defeats} defeats, {_fails} failures");
        GetTree().Quit(_fails == 0 ? 0 : 1);
    }

    // Plays one full run; returns true on victory, false on defeat/abort.
    private bool PlayRun(int index)
    {
        var run = RunState.NewRun(_rng);
        while (true)
        {
            _won = _lost = false;
            _board.StartBattle(run);
            int actions = 0;
            while (!_won && !_lost)
            {
                if (++actions > MaxActionsPerBattle)
                {
                    Fail($"run {index} battle {run.Battle}: exceeded {MaxActionsPerBattle} actions (stuck?)");
                    DumpState(run);
                    return false;
                }
                if (!TakeOneAction(run))
                {
                    Fail($"run {index} battle {run.Battle}: no player action available while battle running");
                    DumpState(run);
                    return false;
                }
            }
            if (_lost)
            {
                GD.Print($"[AUTOPLAY] run {index}: defeated in battle {run.Battle} (score {run.Score})");
                return false;
            }
            if (run.Battle > RunState.FinalBattle)
            {
                GD.Print($"[AUTOPLAY] run {index}: VICTORY (score {run.Score}, ${run.Money})");
                return true;
            }
            SimulateShop(run);
        }
    }

    // One player action: sometimes a deploy, otherwise a move (captures first).
    private bool TakeOneAction(RunState run)
    {
        if (run.Reserve.Count > 0 && _rng.Next(4) == 0 && TryDeploy(run)) return true;

        _mine.Clear();
        foreach (var p in _board.DebugPieces)
            if (p.Alive && p.Side == PieceSide.Player) _mine.Add(p);
        Shuffle(_mine);

        // Score every legal (piece, destination) pair the way a human reads the
        // board: safe captures > unsafe captures (by victim value), then safe
        // approach moves, then desperate ones. A dash of noise for coverage.
        BattlePiece movePiece = null;
        HexCoord moveDest = default;
        int bestRank = int.MinValue;
        foreach (var p in _mine)
        {
            PieceRules.LegalMoves(p.Kind, PieceSide.Player, p.Coord, _board, _moves);
            for (int m = 0; m < _moves.Count; m++)
            {
                var dest = _moves[m];
                bool capture = _board.OccupantSide(dest) == PieceSide.Enemy;
                bool safe = !_board.DebugIsDeathTile(p, dest);
                int rank;
                if (capture)
                    rank = (safe ? 4000 : 3000) + VictimValueAt(dest) * 10;
                else
                    rank = (safe ? 2000 : 1000) - DistanceToNearestEnemy(dest) * 10;
                rank += _rng.Next(10);
                if (rank > bestRank)
                {
                    bestRank = rank;
                    movePiece = p;
                    moveDest = dest;
                }
            }
        }
        if (movePiece == null) return run.Reserve.Count > 0 && TryDeploy(run);

        _board.DebugTap(movePiece.Coord);
        if (!_board.DebugIsHighlighted(moveDest))
        {
            Fail($"legal move {movePiece.Kind} {movePiece.Coord}->{moveDest} not highlighted after select");
            return false;
        }
        _board.DebugTap(moveDest);
        return true;
    }

    private bool TryDeploy(RunState run)
    {
        _board.BeginDeploy(_rng.Next(run.Reserve.Count));
        var targets = new List<HexCoord>(_board.DebugHighlighted);
        if (targets.Count == 0) return false;   // board disarmed itself: no room
        _board.DebugTap(targets[_rng.Next(targets.Count)]);
        return true;
    }

    // Mirrors ShopScreen's purchase effects so gambit / tile-upgrade / army
    // growth paths all get exercised across runs.
    private void SimulateShop(RunState run)
    {
        if (_rng.Next(2) == 0)
        {
            var kind = (PieceKind)_rng.Next(6);
            int price = PieceCatalog.Info(kind).Price;
            if (run.Money >= price)
            {
                run.Money -= price;
                if (run.Army.Count < RunState.ArmyCap) run.Army.Add(kind);
                else run.Reserve.Add(kind);
            }
        }
        if (_rng.Next(2) == 0)
        {
            foreach (var g in GambitCatalog.All)
            {
                if (run.Gambits.Contains(g.Kind) || run.Money < g.Price) continue;
                run.Money -= g.Price;
                run.Gambits.Add(g.Kind);
                break;
            }
        }
        if (_rng.Next(2) == 0)
        {
            var up = TileUpgradeCatalog.All[_rng.Next(TileUpgradeCatalog.All.Length)];
            var candidates = new List<HexCoord>();
            HexCoord.Within(2, candidates);
            candidates.RemoveAll(c => run.TileUpgrades.ContainsKey(c));
            if (candidates.Count > 0 && run.Money >= up.Price)
            {
                run.Money -= up.Price;
                run.TileUpgrades[candidates[_rng.Next(candidates.Count)]] = up.Kind;
            }
        }
    }

    private void Shuffle(List<BattlePiece> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = _rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    private void Fail(string msg)
    {
        _fails++;
        GD.PrintErr($"[AUTOPLAY] FAIL: {msg}");
    }

    private int VictimValueAt(HexCoord dest)
    {
        foreach (var p in _board.DebugPieces)
            if (p.Alive && p.Side == PieceSide.Enemy && p.Coord == dest)
                return PieceCatalog.ValueOf(p.Kind);
        return 0;
    }

    private int DistanceToNearestEnemy(HexCoord from)
    {
        int best = int.MaxValue;
        foreach (var p in _board.DebugPieces)
        {
            if (!p.Alive || p.Side != PieceSide.Enemy) continue;
            int d = p.Coord.Distance(from);
            if (d < best) best = d;
        }
        return best;
    }

    private void DumpState(RunState run)
    {
        GD.PrintErr($"[AUTOPLAY] state: battle {run.Battle}, running={_board.DebugRunning}, " +
            $"reserve={run.Reserve.Count}, money={run.Money}, won={_won}, lost={_lost}");
        foreach (var p in _board.DebugPieces)
        {
            PieceRules.LegalMoves(p.Kind, p.Side, p.Coord, _board, _moves);
            GD.PrintErr($"  {p.Side} {p.Kind} @ {p.Coord} alive={p.Alive} stun={p.StunTurns} moves={_moves.Count}");
        }
    }
#else
    public override void _Ready() => GetTree().Quit();
#endif
}
