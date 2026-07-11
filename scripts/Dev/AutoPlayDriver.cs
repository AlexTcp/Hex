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
    private readonly List<HexCoord> _moves = new(64);   // DumpState scratch
    private bool _won, _lost;
    private int _fails;

    // Difficulty-curve stats, indexed by battle number (1..FinalBattle).
    private readonly int[] _reached = new int[RunState.FinalBattle + 2];
    private readonly int[] _cleared = new int[RunState.FinalBattle + 2];
    private readonly int[] _armySum = new int[RunState.FinalBattle + 2];

    public override void _Ready() => _ = Run();

    private async System.Threading.Tasks.Task Run()
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
            {
                if (PlayRun(i)) victories++; else defeats++;
                // Let QueueFree flush so the leak canary below is meaningful.
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                // Leak canary: node/orphan counts must stay flat across runs.
                if ((i + 1) % 50 == 0)
                    GD.Print($"[AUTOPLAY] after run {i + 1}: nodes={Performance.GetMonitor(Performance.Monitor.ObjectNodeCount)}, " +
                        $"orphans={Performance.GetMonitor(Performance.Monitor.ObjectOrphanNodeCount)}, " +
                        $"objects={Performance.GetMonitor(Performance.Monitor.ObjectCount)}");
            }
        }
        catch (Exception e)
        {
            GD.PrintErr($"[AUTOPLAY] EXCEPTION: {e}");
            GetTree().Quit(2);
            return;
        }

        GD.Print($"[AUTOPLAY] done: {runs} runs, {victories} victories, {defeats} defeats, {_fails} failures");
        GD.Print("[AUTOPLAY] battle | reached | cleared | clear% | avg army");
        for (int b = 1; b <= RunState.FinalBattle; b++)
        {
            if (_reached[b] == 0) continue;
            GD.Print($"[AUTOPLAY]   {b,2}   |  {_reached[b],4}   |  {_cleared[b],4}   |  {100 * _cleared[b] / _reached[b],3}%  |  {(float)_armySum[b] / _reached[b]:F1}");
        }
        GetTree().Quit(_fails == 0 ? 0 : 1);
    }

    // Plays one full run; returns true on victory, false on defeat/abort.
    private bool PlayRun(int index)
    {
        var run = RunState.NewRun(_rng);
        while (true)
        {
            int battleNo = Math.Min(run.Battle, RunState.FinalBattle + 1);
            _reached[battleNo]++;
            _armySum[battleNo] += run.Army.Count;

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
            _cleared[battleNo]++;
            if (run.Battle > RunState.FinalBattle)
            {
                GD.Print($"[AUTOPLAY] run {index}: VICTORY (score {run.Score}, ${run.Money})");
                return true;
            }
            SimulateShop(run);
        }
    }

    // One player action via the shared bot brain; false = playability failure.
    private bool TakeOneAction(RunState run)
    {
        var result = BotBrain.TakeOneAction(_board, run, _rng);
        if (result == BotActionResult.Inconsistent)
        {
            Fail("legal move not highlighted after select");
            return false;
        }
        return result != BotActionResult.NoAction;
    }

    // Mirrors the real shop's offer structure (two rolled piece offers per
    // visit, $2 reroll, one gambit, one tile) but shops like a survival-minded
    // player: grow the army first, spend leftovers on gambits/tiles.
    private void SimulateShop(RunState run)
    {
        const int rerollPrice = 2;
        for (int visit = 0; visit < 4; visit++)
        {
            for (int i = 0; i < 2; i++)
            {
                var kind = ShopOffers.RollPieceOffer(run.Battle, _rng);
                int price = PieceCatalog.Info(kind).Price;
                if (run.Army.Count + run.Reserve.Count < 7 && run.Money >= price)
                {
                    run.Money -= price;
                    run.AddPiece(kind);
                }
            }
            // Reroll only while flush and still short on pieces.
            if (run.Army.Count + run.Reserve.Count >= 6 || run.Money < rerollPrice + 5) break;
            run.Money -= rerollPrice;
        }

        if (_rng.Next(5) < 2)
        {
            foreach (var g in GambitCatalog.All)
            {
                if (run.Gambits.Contains(g.Kind) || run.Money < g.Price) continue;
                run.Money -= g.Price;
                run.Gambits.Add(g.Kind);
                break;
            }
        }
        if (_rng.Next(5) < 2)
        {
            var up = TileUpgradeCatalog.All[_rng.Next(TileUpgradeCatalog.All.Length)];
            var coord = ShopOffers.RollUpgradeCoord(run, _rng);
            if (coord.HasValue && run.Money >= up.Price)
            {
                run.Money -= up.Price;
                run.TileUpgrades[coord.Value] = up.Kind;
            }
        }
    }

    private void Fail(string msg)
    {
        _fails++;
        GD.PrintErr($"[AUTOPLAY] FAIL: {msg}");
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
