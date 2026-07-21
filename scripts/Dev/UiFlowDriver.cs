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
//   The real user://hex.cfg is stashed at start and restored on every exit
//   path, so runs never disturb the player's records.
//
// Exit codes: 0 = pass, 1 = flow failure, 2 = unhandled exception. DEBUG-only.
// =============================================================================

using Godot;
#if DEBUG
using System;
using System.Threading.Tasks;
using HexGame.Board;
using HexGame.Chess;
using HexGame.Hex;
using HexGame.UI;
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
    private byte[] _savedCfg;  // stashed user://hex.cfg bytes (null = absent at start)
    private bool _hadCfg;

    public override void _Ready() => _ = Run();

    private async Task Run()
    {
        BackupSave();
        int code;
        try
        {
            await RunFlow();
            code = _fails == 0 ? 0 : 1;
            GD.Print(code == 0 ? "[UIFLOW] PASS" : $"[UIFLOW] FAILED with {_fails} failures");
        }
        catch (Exception e)
        {
            GD.PrintErr($"[UIFLOW] EXCEPTION: {e}");
            code = 2;
        }
        RestoreSave();
        GetTree().Quit(code);
    }

    // The flow writes to the real save (records, tutorial-seen); stash the
    // file's bytes and put them back on every exit path so harness runs never
    // disturb the player's progress.
    private void BackupSave()
    {
        _hadCfg = FileAccess.FileExists(GameSession.SavePath);
        if (!_hadCfg) return;
        using var f = FileAccess.Open(GameSession.SavePath, FileAccess.ModeFlags.Read);
        _savedCfg = f.GetBuffer((long)f.GetLength());
    }

    private void RestoreSave()
    {
        if (_hadCfg)
        {
            using var f = FileAccess.Open(GameSession.SavePath, FileAccess.ModeFlags.Write);
            f.StoreBuffer(_savedCfg);
        }
        else if (FileAccess.FileExists(GameSession.SavePath))
        {
            DirAccess.RemoveAbsolute(GameSession.SavePath);
        }
    }

    private async Task RunFlow()
    {
        foreach (var arg in OS.GetCmdlineUserArgs())
            if (arg.StartsWith("screenshots="))
                _shotDir = arg.Substring("screenshots=".Length).TrimEnd('/', '\\');

        if (!await BootGame()) return;
        if (!await PhaseTitleToFirstBattle()) return;
        PhaseProtectionChecks();
        await PhaseInspectionShots();

        // Play battle 1 to completion via the shared bot, then branch.
        if (!await PlayBattle()) return;
        if (_won)
        {
            if (!await PhaseWinPath()) return;
        }
        else
        {
            GD.Print("[UIFLOW] battle 1 lost → game over");
            if (await ExpectButton("NEW RUN") == null) return;
            if (!await PressButton("TITLE")) return;
            if (await ExpectButton("PLAY") == null) return;
            GD.Print("[UIFLOW] game over → title ok");
        }

        await PhaseDeliberateDefeat();
    }

    private async Task<bool> BootGame()
    {
        var packed = GD.Load<PackedScene>("res://scenes/game.tscn");
        AddChild(packed.Instantiate());
        await Frames(3);

        _board = FindNode<HexBoard>(this);
        _session = GetNode<GameSession>("/root/GameSession");
        if (_board == null) { Fail("HexBoard not found in game scene"); return false; }
        _board.BattleWon += () => _won = true;
        _board.BattleLost += () => _lost = true;
        return true;
    }

    // Title → New Run (reroll once, begin), optional tutorial skip, then a
    // pause/resume round-trip to prove the HUD is live.
    private async Task<bool> PhaseTitleToFirstBattle()
    {
        if (await ExpectButton("PLAY") == null) return false;
        await Shot("01-title");
        if (!await PressButton("PLAY")) return false;
        if (!await PressButton("REROLL ARMY")) return false;
        await Shot("02-newrun");
        if (!await PressButton("BEGIN RUN")) return false;

        // First-run tutorial (only if this save hasn't seen it).
        var skip = FindButton("SKIP");
        if (skip != null)
        {
            skip.EmitSignal(BaseButton.SignalName.Pressed);
            await Frames(2);
            GD.Print("[UIFLOW] tutorial skipped");
        }

        if (await ExpectButton("II") == null) return false;
        if (!await PressButton("II")) return false;
        await Shot("03-pause");
        if (!await PressButton("RESUME")) return false;
        return await ExpectButton("II") != null;
    }

    // Round 36: verify the death-tile telegraph honours the capture protections
    // (Royal Guard for the King, Shield tiles for anyone) so the resolver and the
    // red paint agree. Mutates + restores the live run, leaving it untouched.
    private void PhaseProtectionChecks()
    {
        var run = _session.CurrentRun;
        HexCoord c = default;
        foreach (var p in _board.DebugPieces)
            if (p.Alive && p.Side == PieceSide.Player) { c = p.Coord; break; }

        // Royal Guard: blocks a King capture only while owned + unused.
        if (_board.DebugCaptureBlockedAt(c, PieceKind.King)) Fail("RG block active before owning Royal Guard");
        run.Gambits.Add(GambitKind.RoyalGuard);
        if (!_board.DebugCaptureBlockedAt(c, PieceKind.King)) Fail("Royal Guard did not block a King capture");
        if (_board.DebugCaptureBlockedAt(c, PieceKind.Pawn)) Fail("Royal Guard wrongly blocked a non-King");
        run.Gambits.Remove(GambitKind.RoyalGuard);

        // Shield tile: blocks any friendly piece while the tile has an unconsumed Shield.
        if (_board.DebugCaptureBlockedAt(c, PieceKind.Pawn)) Fail("shield block active on an un-upgraded tile");
        run.TileUpgrades[c] = TileUpgradeKind.Shield;
        if (!_board.DebugCaptureBlockedAt(c, PieceKind.Pawn)) Fail("Shield tile did not block a capture");
        run.TileUpgrades.Remove(c);

        if (_fails == 0) GD.Print("[UIFLOW] protection death-tile predicate ok");
    }

    // Screenshot a selection (highlights + inspection chip) and an enemy-reach
    // inspection before playing the battle out. Prefer a piece with a
    // death-tile move so the red warning appears in frame; fall back to the
    // piece nearest an enemy.
    private async Task PhaseInspectionShots()
    {
        if (_shotDir == null) return;

        var moves = new System.Collections.Generic.List<HexCoord>(64);
        BattlePiece pick = null;
        int best = int.MaxValue;
        bool pickHasDanger = false;
        foreach (var p in _board.DebugPieces)
        {
            if (!p.Alive || p.Side != PieceSide.Player) continue;
            PieceRules.LegalMoves(p.Kind, PieceSide.Player, p.Coord, _board, moves);
            bool hasDanger = false;
            foreach (var m in moves)
                if (_board.DebugIsDeathTile(p, m)) { hasDanger = true; break; }
            int dist = int.MaxValue;
            foreach (var e in _board.DebugPieces)
            {
                if (!e.Alive || e.Side != PieceSide.Enemy) continue;
                int d = p.Coord.Distance(e.Coord);
                if (d < dist) dist = d;
            }
            if ((hasDanger && !pickHasDanger)
                || (hasDanger == pickHasDanger && dist < best))
            {
                pick = p;
                best = dist;
                pickHasDanger = hasDanger;
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

        // Deploy-mode highlights (round 32/35): inject a throwaway reserve piece,
        // enter deploy mode, and shoot the lit deploy tiles (gold, and danger-red
        // for any an enemy could capture next turn). Cleaned up so the real run is
        // untouched. Verifies the deploy telegraph the headless harnesses can't see.
        var reserve = _session.CurrentRun.Reserve;
        reserve.Add(PieceKind.Rook);
        _board.BeginDeploy(reserve.Count - 1);
        await Shot("12-deploy");
        _board.CancelDeploy();
        if (reserve.Count > 0) reserve.RemoveAt(reserve.Count - 1);

        // Settings drawer (player-facing Sound toggle + volume slider) — verify it
        // renders, then close it via its own Resume so the flow can continue.
        var settings = FindNode<SettingsModal>(GetTree().Root);
        if (settings != null)
        {
            settings.Open();
            await Shot("13-settings");
            var resume = FindButton("Resume");   // drawer button (not the all-caps pause RESUME)
            if (resume != null) resume.EmitSignal(BaseButton.SignalName.Pressed);
            await Frames(25);                     // let the drawer slide shut
        }
    }

    // Battle 1 won: shop (shot + a purchase), then with screenshots on push
    // through to battle 4 for the cracked-board and Lockmaker shots, and
    // finally pause → abandon back to the title.
    private async Task<bool> PhaseWinPath()
    {
        GD.Print("[UIFLOW] battle 1 won → shop");

        // Regression guard (round 30): a pause tap during the ~0.4s post-battle HUD
        // fade-out must NOT hijack the transition. The pause button ("II") is still
        // visible mid-fade; press it. The guarded GoPause ignores taps outside
        // Playing, so the pause overlay must NOT open (RESUME must not appear) and the
        // shop must still arrive. If the softlock returns, GoPause enters Paused here,
        // RESUME shows, and — after any resume — the board is left with _running==false
        // (dead), so NEXT BATTLE never appears and the run is stranded.
        var pauseBtn = FindButton("II");
        if (pauseBtn != null)
        {
            pauseBtn.EmitSignal(BaseButton.SignalName.Pressed);
            await Frames(2);
            if (FindButton("RESUME") != null)
            {
                Fail("pause hijacked the post-battle transition (softlock regression)");
                return false;
            }
            GD.Print("[UIFLOW] pause during post-battle fade ignored ok");
        }

        if (await ExpectButton("NEXT BATTLE") == null) return false;
        await Shot("06-shop");

        var buy = FindButton("BUY", requireEnabled: true);
        if (buy != null)
        {
            buy.EmitSignal(BaseButton.SignalName.Pressed);
            await Frames(2);
            GD.Print("[UIFLOW] bought a shop offer");
        }

        if (!await PressButton("NEXT BATTLE")) return false;
        if (await ExpectButton("II") == null) return false;

        if (_shotDir != null && !await PhaseBoardStateShots()) return false;

        // Pause → abandon (confirm) → back to Title (if still in a battle).
        if (FindButton("II") != null)
        {
            if (!await PressButton("II")) return false;
            if (!await PressButton("ABANDON RUN")) return false;
            // Confirm-before-abandon guard (round 33): ABANDON must NOT drop the run
            // straight to Title — it must ask first. If the confirm regresses, PLAY
            // appears here and YES, ABANDON is missing.
            if (FindButton("PLAY") != null) { Fail("abandon skipped its confirmation"); return false; }
            if (await ExpectButton("YES, ABANDON") == null) return false;
            GD.Print("[UIFLOW] abandon confirm shown ok");
            if (!await PressButton("YES, ABANDON")) return false;
        }
        if (await ExpectButton("PLAY") == null) return false;
        GD.Print("[UIFLOW] abandon → title ok");
        return true;
    }

    // Push through real shops to battle 4 (buying each visit): stall battle 3
    // until the ring cracks for that shot, then capture Lockmaker's locked
    // tiles at battle 4's start. Losing on the way is tolerated.
    private async Task<bool> PhaseBoardStateShots()
    {
        while (_session.CurrentRun.Battle < 4)
        {
            if (_session.CurrentRun.Battle == 3)
            {
                _won = _lost = false;
                for (int a = 0; a < MaxActionsPerBattle && !_won && !_lost
                    && _board.DebugCrackedCount == 0; a++)
                {
                    if (BotBrain.TakeOneAction(_board, _session.CurrentRun, _rng, BotMode.Stall)
                        == BotActionResult.NoAction) break;
                    await Frames(1);
                }
                if (_board.DebugCrackedCount > 0) await Shot("11-cracked");
                else GD.Print("[UIFLOW] battle 3 ended before cracking");
                if (!_won && !_lost && !await FinishBattle()) return false;
            }
            else if (!await PlayBattle()) return false;

            if (_lost) break;
            if (await ExpectButton("NEXT BATTLE") == null) return false;
            for (int b = 0; b < 2; b++)
            {
                var offer = FindButton("BUY", requireEnabled: true);
                if (offer == null) break;
                offer.EmitSignal(BaseButton.SignalName.Pressed);
                await Frames(2);
            }
            if (!await PressButton("NEXT BATTLE")) return false;
            if (await ExpectButton("II") == null) return false;
        }

        if (!_lost && _session.CurrentRun.Battle == 4)
        {
            if (_board.DebugLockedCount > 0) await Shot("10-lockmaker");
            else GD.Print("[UIFLOW] battle 4 had no locked tiles");
        }
        if (_lost)
        {
            GD.Print("[UIFLOW] lost before battle 4 — tolerated, heading to title");
            if (await ExpectButton("NEW RUN") == null) return false;
            if (!await PressButton("TITLE")) return false;
        }
        return true;
    }

    // Reach the defeat path deliberately so the Game Over screen's buttons get
    // exercised (a competent bot usually wins battle 1), including the forced
    // victory presentation for its screenshot.
    private async Task PhaseDeliberateDefeat()
    {
        if (!await PressButton("PLAY")) return;
        if (!await PressButton("BEGIN RUN")) return;
        if (await ExpectButton("II") == null) return;
        if (!await PlayBattle(BotMode.Suicidal)) return;
        if (_won)
        {
            GD.Print("[UIFLOW] suicidal bot somehow won — skipping game-over check");
            return;
        }
        GD.Print("[UIFLOW] deliberate defeat → game over");
        if (await ExpectButton("NEW RUN") == null) return;
        await Shot("07-gameover");

        // Force the victory presentation for a shot (a real one needs 12 wins),
        // then restore the defeat state before continuing the flow.
        if (_shotDir != null)
        {
            var go = FindNode<GameOverScreen>(this);
            if (go != null)
            {
                go.Present(victory: true, battle: RunState.FinalBattle, score: 99999,
                    newBest: true, run: _session.CurrentRun);
                await Shot("09-victory");
                go.Present(victory: false, battle: _session.CurrentRun.Battle,
                    score: _session.CurrentRun.Score, newBest: false, run: _session.CurrentRun);
            }
        }

        if (!await PressButton("NEW RUN")) return;
        if (await ExpectButton("BEGIN RUN") == null) return;
        if (!await PressButton("‹")) return;   // NewRun back chevron
        if (await ExpectButton("PLAY") == null) return;
        GD.Print("[UIFLOW] game over → new run → back → title ok");
    }

    // Continue the current battle to completion WITHOUT resetting the outcome
    // flags (used after a stall segment already advanced the battle).
    private async Task<bool> FinishBattle()
    {
        for (int actions = 0; actions < MaxActionsPerBattle && !_won && !_lost; actions++)
        {
            var result = BotBrain.TakeOneAction(_board, _session.CurrentRun, _rng);
            if (result == BotActionResult.NoAction || result == BotActionResult.Inconsistent)
            {
                Fail($"battle continuation failed: {result}");
                return false;
            }
            await Frames(1);
        }
        if (!_won && !_lost)
        {
            Fail($"battle did not finish within {MaxActionsPerBattle} actions");
            return false;
        }
        await Frames(3);
        return true;
    }

    private async Task<bool> PlayBattle(BotMode mode = BotMode.Normal)
    {
        _won = _lost = false;
        bool popShot = false;
        for (int actions = 0; actions < MaxActionsPerBattle && !_won && !_lost; actions++)
        {
            int moneyBefore = _session.CurrentRun.Money;
            var result = BotBrain.TakeOneAction(_board, _session.CurrentRun, _rng, mode);
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
