// =============================================================================
// ScreenManager
// =============================================================================
// Purpose:
//   The app state machine and UI orchestrator for the roguelike loop. Owns the
//   overlay screens + tutorial + HUD + scrim + vignettes as Control children of
//   the persistent UI root (the 3D board never unloads behind them). Wires
//   HexBoard's battle signals to the HUD and GameSession, drives screen
//   transitions (cross-fades), gates board input per-state (cooperating with
//   the DebugLog modal), pulses the danger vignette while the board is
//   cracking, and runs the attract camera drift + the defeat shake.
//
// States: Title -> NewRun -> Playing -> (Paused) -> Shop -> ... -> GameOver.
//   BattleWon routes to Shop, or to the victory presentation of GameOver after
//   the final battle; BattleLost routes to the defeat presentation.
//
// Interactions:
//   - GameScreen: builds the 3D stage, then constructs this with the board,
//     camera, session and UI root, and calls GoTitle().
//   - HexBoard: StartBattle / ShowPreview / BeginDeploy; listens to signals.
//   - GameSession: owns the RunState; commits records at run end.
// =============================================================================

using System;
using Godot;
using HexGame.Board;
using HexGame.Chess;

namespace HexGame.UI;

public partial class ScreenManager : Node
{
    public enum AppState { Title, NewRun, Playing, Paused, Shop, GameOver }

    private readonly HexBoard _board;
    private readonly Camera3D _camera;
    private readonly GameSession _session;
    private readonly Control _root;

    private AppState _state = AppState.Title;
    private bool _tutorialActive = false;

    private ColorRect _scrim;
    private ColorRect _dangerVignette;
    private Tween _dangerTween;

    private TitleScreen _title;
    private NewRunScreen _newRun;
    private ShopScreen _shop;
    private Hud _hud;
    private PauseOverlay _pause;
    private GameOverScreen _gameOver;
    private TutorialOverlay _tutorial;
    private Control _currentScreen;

    private CameraDirector _cameraDirector;
    private readonly System.Collections.Generic.List<Tween> _transitionTweens = new();
    private Control[] _allScreens;
    private Tween _tutorialTween;

    public ScreenManager(HexBoard board, Camera3D camera, GameSession session, Control root)
    {
        _board = board;
        _camera = camera;
        _session = session;
        _root = root;
    }

    public override void _Ready()
    {
        _cameraDirector = new CameraDirector(_camera);
        _root.Theme = UiTheme.Build();

        BuildOverlays();
        WireBoard(true);

        DebugLog.GameplayActiveChanged += OnDebugModalToggled;

        GoTitle();
    }

    public override void _ExitTree()
    {
        DebugLog.GameplayActiveChanged -= OnDebugModalToggled;
        _dangerTween?.Kill();
        _cameraDirector?.Cleanup();
        _tutorialTween?.Kill();
        foreach (var t in _transitionTweens) t?.Kill();
        _transitionTweens.Clear();
        WireBoard(false);
    }

    // ----- Build ---------------------------------------------------------

    private void BuildOverlays()
    {
        // Behind everything: a static dark vignette frames the board; the danger
        // vignette pulses red while the board crumbles; the scrim dims for menus.
        AddRoot(UiTheme.Vignette(new Color(0, 0, 0), 0.78f));
        _dangerVignette = UiTheme.Vignette(UiTheme.Danger, 0.5f);
        _dangerVignette.Modulate = new Color(1, 1, 1, 0);
        AddRoot(_dangerVignette);

        _scrim = new ColorRect { Color = new Color(0, 0, 0, 0), MouseFilter = Control.MouseFilterEnum.Ignore };
        _scrim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddRoot(_scrim);

        _hud = new Hud(GoPause, OnDeployRequested);
        _title = new TitleScreen(_session, GoNewRun, ShowTutorial);
        _newRun = new NewRunScreen(_session.RerollRun, BeginRun, GoTitle);
        _shop = new ShopScreen(NextBattle, _board.SetShopPreviewTile);
        _pause = new PauseOverlay(ResumeFromPause, AbandonRun);
        _gameOver = new GameOverScreen(GoNewRun, GoTitle);
        _tutorial = new TutorialOverlay(OnTutorialComplete);

        _allScreens = new Control[] { _hud, _title, _newRun, _shop, _pause, _gameOver };
        foreach (Control c in new Control[] { _hud, _title, _newRun, _shop, _pause, _gameOver, _tutorial })
        {
            c.Visible = false;
            c.Modulate = new Color(1, 1, 1, 0);
            AddRoot(c);
        }
    }

    private void AddRoot(Control c)
    {
        c.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _root.AddChild(c);
    }

    // Connect (true) or disconnect (false) every board signal. Each event is named
    // exactly ONCE (with an inline branch) so the subscribe and unsubscribe sets can
    // never fall out of sync — a forgotten -= would leak the handler and keep this
    // dead ScreenManager reachable, because the board node never unloads (hard rule 4).
    private void WireBoard(bool connect)
    {
        if (_board == null) return;
        if (connect) _board.MoneyChanged += OnMoneyChanged; else _board.MoneyChanged -= OnMoneyChanged;
        if (connect) _board.ScoreChanged += OnScoreChanged; else _board.ScoreChanged -= OnScoreChanged;
        if (connect) _board.EnemiesChanged += OnEnemiesChanged; else _board.EnemiesChanged -= OnEnemiesChanged;
        if (connect) _board.ArmyChanged += OnArmyChanged; else _board.ArmyChanged -= OnArmyChanged;
        if (connect) _board.CrumbleChanged += OnCrumbleChanged; else _board.CrumbleChanged -= OnCrumbleChanged;
        if (connect) _board.ThreatChanged += OnThreatChanged; else _board.ThreatChanged -= OnThreatChanged;
        if (connect) _board.StatusNote += OnStatusNote; else _board.StatusNote -= OnStatusNote;
        if (connect) _board.InspectChanged += OnInspectChanged; else _board.InspectChanged -= OnInspectChanged;
        if (connect) _board.DeployModeChanged += OnDeployModeChanged; else _board.DeployModeChanged -= OnDeployModeChanged;
        if (connect) _board.BattleWon += OnBattleWon; else _board.BattleWon -= OnBattleWon;
        if (connect) _board.BattleLost += OnBattleLost; else _board.BattleLost -= OnBattleLost;
    }

    // ----- Board signal handlers -----------------------------------------

    private void OnMoneyChanged(int money) => _hud.SetMoney(money);
    private void OnScoreChanged(int score) => _hud.SetScore(score);
    private void OnEnemiesChanged(int n) => _hud.SetEnemies(n);
    private void OnArmyChanged() => _hud.RefreshReserve();
    private void OnCrumbleChanged(int turnsLeft, bool cracking) => _hud.SetCrumble(turnsLeft, cracking);
    private void OnStatusNote(string note) => _hud.ShowNote(note);
    private void OnInspectChanged(string text) => _hud.SetInspect(text);
    private void OnDeployModeChanged(bool active) => _hud.SetDeployArmed(active);

    private void OnThreatChanged(bool inDanger)
    {
        SetDangerVignette(inDanger);
        Sfx.SetThreatBed(inDanger);
    }

    private void OnBattleWon()
    {
        var run = _session.CurrentRun;
        _hud.ShowCleared();

        if (run.RunWon)
        {
            // The crown is won. Commit records and present the victory.
            bool newBest = _session.CommitRun(wonRun: true);
            _gameOver.Present(victory: true, battle: RunState.FinalBattle, score: run.Score, newBest: newBest, run: run);
            GoState(AppState.GameOver, _gameOver, 0.6f, 0.4f);
            return;
        }

        _shop.Present(run);
        _cameraDirector.Drift();
        GoState(AppState.Shop, _shop, 0.55f, 0.4f);
    }

    private void OnBattleLost()
    {
        SetDangerVignette(false);
        var run = _session.CurrentRun;
        bool newBest = _session.CommitRun();
        _gameOver.Present(victory: false, battle: run.Battle, score: run.Score, newBest: newBest, run: run);
        _cameraDirector.Shake();
        GoState(AppState.GameOver, _gameOver, 0.6f, 0.3f);
    }

    private void OnDebugModalToggled(bool _) => UpdatePicking();

    // ----- State transitions ---------------------------------------------

    public void GoTitle()
    {
        _board.ShowPreview();
        _title.Refresh();
        _cameraDirector.Drift();
        GoState(AppState.Title, _title, 0.55f);
    }

    private void GoNewRun()
    {
        var run = _session.StartNewRun();
        _newRun.Present(run);
        _board.ShowPreview();
        _cameraDirector.Drift();
        GoState(AppState.NewRun, _newRun, 0.45f);
    }

    private void BeginRun()
    {
        NextBattle();
        if (!_session.TutorialSeen) ShowTutorial();
    }

    // Entry point for every battle (first, post-shop, boss…).
    private void NextBattle()
    {
        var run = _session.CurrentRun;
        _cameraDirector.StopDrift();
        _cameraDirector.Restore();
        _hud.BindRun(run);
        _hud.SetBattle(run.Battle, BattlePlanner.BossFor(run.Battle));
        _board.StartBattle(run);
        GoState(AppState.Playing, _hud, 0f);
    }

    private void OnDeployRequested(int reserveIndex)
    {
        if (reserveIndex < 0) _board.CancelDeploy();
        else _board.BeginDeploy(reserveIndex);
    }

    private void GoPause()
    {
        // The HUD pause button sits above the scrim and stays clickable through the
        // ~0.4s post-battle HUD fade-out. Without this guard a tap in that window
        // would hijack the in-flight Shop/GameOver transition into Paused; RESUME then
        // lands on a board with _running == false where every tap/deploy no-ops,
        // stranding the run (only ABANDON escapes). Pause only from live play.
        if (_state != AppState.Playing || _tutorialActive) return;
        _board.ClearSelection();   // don't leave highlights/pulse behind the menu
        _pause.Refresh(_session.CurrentRun);
        GoState(AppState.Paused, _pause, 0.5f, 0.18f);
    }

    private void ResumeFromPause() => GoState(AppState.Playing, _hud, 0f, 0.16f);

    private void AbandonRun()
    {
        _session.CommitRun();
        SetDangerVignette(false);
        GoTitle();
    }

    // Core transition: set state, fade scrim to its alpha, cross-fade screens.
    private void GoState(AppState state, Control screen, float scrimAlpha, float dur = 0.22f)
    {
        _state = state;
        UpdatePicking();

        // Kill any in-flight transition first: a slower outgoing fade (e.g. the
        // shop's 0.4s) would otherwise keep writing after a quick next
        // transition finished, leaving the scrim dark and the HUD transparent
        // for a whole battle when the player taps NEXT BATTLE fast.
        foreach (var t in _transitionTweens) t?.Kill();
        _transitionTweens.Clear();

        // A killed outgoing fade never runs its hide callback — make sure no
        // third screen stays ghosted at partial alpha behind this transition.
        if (_allScreens != null)
            foreach (var s in _allScreens)
                if (s != screen && s != _currentScreen && s.Visible)
                    s.Visible = false;

        _scrim.MouseFilter = screen == _hud ? Control.MouseFilterEnum.Ignore : Control.MouseFilterEnum.Stop;
        FadeAlpha(_scrim, "color:a", scrimAlpha, dur);

        if (_currentScreen != null && _currentScreen != screen)
        {
            var prev = _currentScreen;
            FadeAlpha(prev, "modulate:a", 0f, dur, () =>
            {
                if (prev != _currentScreen) prev.Visible = false;
            });
        }

        screen.Visible = true;
        FadeAlpha(screen, "modulate:a", 1f, dur);
        _currentScreen = screen;
    }

    // ----- Tutorial ------------------------------------------------------

    private void ShowTutorial()
    {
        _tutorialActive = true;
        UpdatePicking();
        _tutorial.Visible = true;
        _tutorial.Begin();
        FadeTutorial(1f);
    }

    private void OnTutorialComplete()
    {
        _session.MarkTutorialSeen();
        _tutorialActive = false;
        FadeTutorial(0f, () => _tutorial.Visible = false);
        UpdatePicking();
    }

    // The tutorial overlays screen states, so its fade must NOT ride the
    // transition-tween list — a GoState during the fade-out would kill it
    // before the hide callback, leaving a ghost scrim that eats all UI input.
    private void FadeTutorial(float to, Action onDone = null)
    {
        _tutorialTween?.Kill();
        _tutorialTween = CreateTween();
        _tutorialTween.TweenProperty(_tutorial, "modulate:a", to, 0.22f)
            .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        if (onDone != null) _tutorialTween.TweenCallback(Callable.From(onDone));
    }

    // ----- Input gating --------------------------------------------------

    private void UpdatePicking()
    {
        var vp = GetViewport();
        if (vp == null) return;
        vp.PhysicsObjectPicking = _state == AppState.Playing && !_tutorialActive && !DebugLog.IsAnyModalOpen;
    }

    // ----- Vignette ------------------------------------------------------

    private void SetDangerVignette(bool on)
    {
        _dangerTween?.Kill();
        if (on)
        {
            // Restrained pulse — a clear cue, not a strobe (clarity over spectacle).
            _dangerTween = CreateTween().SetLoops();
            _dangerTween.TweenProperty(_dangerVignette, "modulate:a", 0.6f, 0.6f)
                .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
            _dangerTween.TweenProperty(_dangerVignette, "modulate:a", 0.3f, 0.6f)
                .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        }
        else
        {
            _dangerTween = CreateTween();
            _dangerTween.TweenProperty(_dangerVignette, "modulate:a", 0f, 0.25f).SetTrans(Tween.TransitionType.Sine);
        }
    }

    // ----- Helpers -------------------------------------------------------

    private void FadeAlpha(CanvasItem c, string prop, float to, float dur, Action onDone = null)
    {
        var t = CreateTween();
        t.TweenProperty(c, prop, to, dur).SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        if (onDone != null) t.TweenCallback(Callable.From(onDone));
        _transitionTweens.Add(t);
    }
}
