// =============================================================================
// UiFlowDriver — headless UI-flow smoke test (dev tool)
// =============================================================================
// Purpose:
//   Boots the REAL game scene (res://scenes/game.tscn) and walks the whole
//   screen flow by pressing the actual buttons: Title → PLAY → New Run
//   (reroll + begin) → tutorial skip → HUD pause/resume → a full battle played
//   by BotBrain → Shop (buy + continue) or Game Over (title) → pause/abandon →
//   Title. Catches dead buttons, broken transitions, and signal-wiring
//   regressions the board-only autoplay harness cannot see.
//
// Run:
//   godot --headless --path . res://dev/uiflow.tscn
//   (Writes to the real user://hex.cfg — back it up around CI runs.)
//
// Exit codes: 0 = pass, 1 = flow failure, 2 = unhandled exception. DEBUG-only.
// =============================================================================

using Godot;
#if DEBUG
using System;
using System.Threading.Tasks;
using HexGame.Board;
using HexGame.Chess;
#endif

namespace HexGame.Dev;

public partial class UiFlowDriver : Node
{
#if DEBUG
    private const int WaitFrames = 600;          // per-expectation timeout
    private const int MaxActionsPerBattle = 500;

    private HexBoard _board;
    private GameSession _session;
    private readonly Random _rng = new();
    private bool _won, _lost;
    private int _fails;
    private string _shotDir;   // screenshots=<dir> user arg; requires a windowed run

    public override void _Ready() => _ = Run();

    private async Task Run()
    {
        try
        {
            await RunFlow();
        }
        catch (Exception e)
        {
            GD.PrintErr($"[UIFLOW] EXCEPTION: {e}");
            GetTree().Quit(2);
            return;
        }
        GD.Print(_fails == 0 ? "[UIFLOW] PASS" : $"[UIFLOW] FAILED with {_fails} failures");
        GetTree().Quit(_fails == 0 ? 0 : 1);
    }

    private async Task RunFlow()
    {
        foreach (var arg in OS.GetCmdlineUserArgs())
            if (arg.StartsWith("screenshots="))
                _shotDir = arg.Substring("screenshots=".Length).TrimEnd('/', '\\');

        var packed = GD.Load<PackedScene>("res://scenes/game.tscn");
        AddChild(packed.Instantiate());
        await Frames(3);

        _board = FindNode<HexBoard>(this);
        _session = GetNode<GameSession>("/root/GameSession");
        if (_board == null) { Fail("HexBoard not found in game scene"); return; }
        _board.BattleWon += () => _won = true;
        _board.BattleLost += () => _lost = true;

        // Title → New Run (reroll once, then begin).
        if (await ExpectButton("PLAY") == null) return;
        await Shot("01-title");
        if (!await PressButton("PLAY")) return;
        if (!await PressButton("REROLL ARMY")) return;
        await Shot("02-newrun");
        if (!await PressButton("BEGIN RUN")) return;

        // First-run tutorial (only if this save hasn't seen it).
        var skip = FindButton("SKIP");
        if (skip != null)
        {
            skip.EmitSignal(BaseButton.SignalName.Pressed);
            await Frames(2);
            GD.Print("[UIFLOW] tutorial skipped");
        }

        // HUD up? Pause and resume.
        if (await ExpectButton("II") == null) return;
        if (!await PressButton("II")) return;
        await Shot("03-pause");
        if (!await PressButton("RESUME")) return;
        if (await ExpectButton("II") == null) return;

        // Screenshot a selection (highlights + inspection chip) and an
        // enemy-reach inspection before playing the battle out. Select the
        // piece nearest an enemy so red death tiles have a chance to appear.
        if (_shotDir != null)
        {
            BattlePiece pick = null;
            int best = int.MaxValue;
            foreach (var p in _board.DebugPieces)
            {
                if (!p.Alive || p.Side != PieceSide.Player) continue;
                foreach (var e in _board.DebugPieces)
                {
                    if (!e.Alive || e.Side != PieceSide.Enemy) continue;
                    int d = p.Coord.Distance(e.Coord);
                    if (d < best) { best = d; pick = p; }
                }
            }
            if (pick != null)
            {
                _board.DebugTap(pick.Coord);
                await Shot("04-selection");
                _board.DebugTap(pick.Coord);   // toggle off
            }
            foreach (var p in _board.DebugPieces)
            {
                if (!p.Alive || p.Side != PieceSide.Enemy) continue;
                _board.DebugTap(p.Coord);
                await Shot("05-enemy-reach");
                _board.ClearSelection();
                break;
            }
        }

        // Play battle 1 to completion via the shared bot.
        if (!await PlayBattle()) return;

        if (_won)
        {
            GD.Print("[UIFLOW] battle 1 won → shop");
            if (await ExpectButton("NEXT BATTLE") == null) return;
            await Shot("06-shop");

            // Exercise a purchase if anything is affordable.
            var buy = FindButton("BUY", requireEnabled: true);
            if (buy != null)
            {
                buy.EmitSignal(BaseButton.SignalName.Pressed);
                await Frames(2);
                GD.Print("[UIFLOW] bought a shop offer");
            }

            if (!await PressButton("NEXT BATTLE")) return;
            if (await ExpectButton("II") == null) return;

            // Battle 2: pause → abandon → back to Title.
            if (!await PressButton("II")) return;
            if (!await PressButton("ABANDON RUN")) return;
            if (await ExpectButton("PLAY") == null) return;
            GD.Print("[UIFLOW] abandon → title ok");
        }
        else
        {
            GD.Print("[UIFLOW] battle 1 lost → game over");
            if (await ExpectButton("NEW RUN") == null) return;
            if (!await PressButton("TITLE")) return;
            if (await ExpectButton("PLAY") == null) return;
            GD.Print("[UIFLOW] game over → title ok");
        }

        // Phase 2: reach the defeat path deliberately so the Game Over screen's
        // buttons get exercised (a competent bot usually wins battle 1).
        if (!await PressButton("PLAY")) return;
        if (!await PressButton("BEGIN RUN")) return;
        if (await ExpectButton("II") == null) return;
        if (!await PlayBattle(suicidal: true)) return;
        if (_won)
        {
            GD.Print("[UIFLOW] suicidal bot somehow won — skipping game-over check");
            return;
        }
        GD.Print("[UIFLOW] deliberate defeat → game over");
        if (await ExpectButton("NEW RUN") == null) return;
        await Shot("07-gameover");
        if (!await PressButton("NEW RUN")) return;
        if (await ExpectButton("BEGIN RUN") == null) return;
        if (!await PressButton("‹")) return;   // NewRun back chevron
        if (await ExpectButton("PLAY") == null) return;
        GD.Print("[UIFLOW] game over → new run → back → title ok");
    }

    private async Task<bool> PlayBattle(bool suicidal = false)
    {
        _won = _lost = false;
        bool popShot = false;
        for (int actions = 0; actions < MaxActionsPerBattle && !_won && !_lost; actions++)
        {
            int moneyBefore = _session.CurrentRun.Money;
            var result = BotBrain.TakeOneAction(_board, _session.CurrentRun, _rng, suicidal);
            // Catch the first capture's floating +$N while it's still rising.
            if (_shotDir != null && !popShot && !_won && !_lost
                && _session.CurrentRun.Money > moneyBefore)
            {
                popShot = true;
                await Shot("08-money-pop", waitFrames: 4);
            }
            if (result == BotActionResult.NoAction)
            {
                Fail("no player action available in a running battle");
                return false;
            }
            if (result == BotActionResult.Inconsistent)
            {
                Fail("legal move not highlighted after select");
                return false;
            }
            await Frames(1);
        }
        if (!_won && !_lost)
        {
            Fail($"battle did not finish within {MaxActionsPerBattle} actions");
            return false;
        }
        await Frames(3);   // let the transition tween start
        return true;
    }

    // ----- Helpers ---------------------------------------------------------

    // Capture the rendered viewport (windowed runs only — headless renders
    // nothing). Waits out the screen cross-fade first by default.
    private async Task Shot(string name, int waitFrames = 25)
    {
        if (_shotDir == null) return;
        await Frames(waitFrames);
        var img = GetViewport().GetTexture().GetImage();
        var err = img.SavePng($"{_shotDir}/{name}.png");
        GD.Print(err == Error.Ok ? $"[UIFLOW] shot {name}" : $"[UIFLOW] shot {name} FAILED: {err}");
    }

    private async Task Frames(int n)
    {
        for (int i = 0; i < n; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private async Task<Button> ExpectButton(string prefix)
    {
        for (int i = 0; i < WaitFrames; i++)
        {
            var b = FindButton(prefix);
            if (b != null) return b;
            await Frames(1);
        }
        Fail($"button '{prefix}' never became visible");
        return null;
    }

    private async Task<bool> PressButton(string prefix)
    {
        var b = await ExpectButton(prefix);
        if (b == null) return false;
        b.EmitSignal(BaseButton.SignalName.Pressed);
        await Frames(2);
        return true;
    }

    private Button FindButton(string prefix, bool requireEnabled = false) =>
        FindButtonIn(this, prefix, requireEnabled);

    private static Button FindButtonIn(Node node, string prefix, bool requireEnabled)
    {
        if (node is Button b && b.Text.StartsWith(prefix) && b.IsVisibleInTree()
            && (!requireEnabled || !b.Disabled))
            return b;
        foreach (Node c in node.GetChildren())
        {
            var r = FindButtonIn(c, prefix, requireEnabled);
            if (r != null) return r;
        }
        return null;
    }

    private static T FindNode<T>(Node node) where T : class
    {
        if (node is T t) return t;
        foreach (Node c in node.GetChildren())
        {
            var r = FindNode<T>(c);
            if (r != null) return r;
        }
        return null;
    }

    private void Fail(string msg)
    {
        _fails++;
        GD.PrintErr($"[UIFLOW] FAIL: {msg}");
    }
#else
    public override void _Ready() => GetTree().Quit();
#endif
}
