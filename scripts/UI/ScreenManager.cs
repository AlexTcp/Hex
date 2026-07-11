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

    private Transform3D _camRest;
    private Tween _driftTween;
    private Tween _shakeTween;
    private readonly System.Collections.Generic.List<Tween> _transitionTweens = new();
    private Control[] _allScreens;

    public ScreenManager(HexBoard board, Camera3D camera, GameSession session, Control root)
    {
        _board = board;
        _camera = camera;
        _session = session;
        _root = root;
    }

    public override void _Ready()
    {
        _camRest = _camera.Transform;
        _root.Theme = UiTheme.Build();

        BuildOverlays();
        WireBoard();

        DebugLog.GameplayActiveChanged += OnDebugModalToggled;

        GoTitle();
    }

    public override void _ExitTree()
    {
        DebugLog.GameplayActiveChanged -= OnDebugModalToggled;
        _dangerTween?.Kill();
        _driftTween?.Kill();
        _shakeTween?.Kill();
        foreach (var t in _transitionTweens) t?.Kill();
        _transitionTweens.Clear();
        if (_board != null)
        {
            _board.MoneyChanged -= OnMoneyChanged;
            _board.ScoreChanged -= OnScoreChanged;
            _board.EnemiesChanged -= OnEnemiesChanged;
            _board.ArmyChanged -= OnArmyChanged;
            _board.CrumbleChanged -= OnCrumbleChanged;
            _board.ThreatChanged -= OnThreatChanged;
            _board.StatusNote -= OnStatusNote;
            _board.InspectChanged -= OnInspectChanged;
            _board.DeployModeChanged -= OnDeployModeChanged;
            _board.BattleWon -= OnBattleWon;
            _board.BattleLost -= OnBattleLost;
        }
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

    private void WireBoard()
    {
        _board.MoneyChanged += OnMoneyChanged;
        _board.ScoreChanged += OnScoreChanged;
        _board.EnemiesChanged += OnEnemiesChanged;
        _board.ArmyChanged += OnArmyChanged;
        _board.CrumbleChanged += OnCrumbleChanged;
        _board.ThreatChanged += OnThreatChanged;
        _board.StatusNote += OnStatusNote;
        _board.InspectChanged += OnInspectChanged;
        _board.DeployModeChanged += OnDeployModeChanged;
        _board.BattleWon += OnBattleWon;
        _board.BattleLost += OnBattleLost;
    }

    // ----- Board signal handlers -----------------------------------------

    private void OnMoneyChanged(int money) => _hud.SetMoney(money);
    private void OnScoreChanged(int score) => _hud.SetScore(score);
    private void OnEnemiesChanged(int n) => _hud.SetEnemies(n);
    private void OnArmyChanged(int onBoard, int reserve) => _hud.SetArmy(onBoard, reserve);
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

        if (run.Battle > RunState.FinalBattle)
        {
            // The crown is won. Commit records and present the victory.
            bool newBest = _session.CommitRun(wonRun: true);
            _gameOver.Present(victory: true, battle: RunState.FinalBattle, score: run.Score, newBest: newBest, run: run);
            GoState(AppState.GameOver, _gameOver, 0.6f, 0.4f);
            return;
        }

        _shop.Present(run);
        StartDrift();
        GoState(AppState.Shop, _shop, 0.55f, 0.4f);
    }

    private void OnBattleLost()
    {
        SetDangerVignette(false);
        var run = _session.CurrentRun;
        bool newBest = _session.CommitRun();
        _gameOver.Present(victory: false, battle: run.Battle, score: run.Score, newBest: newBest, run: run);
        Shake();
        GoState(AppState.GameOver, _gameOver, 0.6f, 0.3f);
    }

    private void OnDebugModalToggled(bool _) => UpdatePicking();

    // ----- State transitions ---------------------------------------------

    public void GoTitle()
    {
        _board.ShowPreview();
        _title.Refresh();
        StartDrift();
        GoState(AppState.Title, _title, 0.55f);
    }

    private void GoNewRun()
    {
        var run = _session.StartNewRun();
        _newRun.Present(run);
        _board.ShowPreview();
        StartDrift();
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
        StopDrift();
        RestoreCamera();
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
        FadeAlpha(_tutorial, "modulate:a", 1f, 0.22f);
    }

    private void OnTutorialComplete()
    {
        _session.MarkTutorialSeen();
        _tutorialActive = false;
        FadeAlpha(_tutorial, "modulate:a", 0f, 0.22f, () => _tutorial.Visible = false);
        UpdatePicking();
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

    // ----- Camera attract drift + defeat shake ----------------------------

    private void StartDrift()
    {
        _driftTween?.Kill();
        var origin = _camRest.Origin;
        _driftTween = CreateTween().SetLoops();
        _driftTween.TweenProperty(_camera, "position", origin + new Vector3(0.35f, 0f, 0.2f), 7.0f)
            .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        _driftTween.TweenProperty(_camera, "position", origin + new Vector3(-0.35f, 0f, -0.1f), 7.0f)
            .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
    }

    private void StopDrift()
    {
        _driftTween?.Kill();
        _driftTween = null;
    }

    private void RestoreCamera()
    {
        _shakeTween?.Kill();
        _camera.Transform = _camRest;
    }

    // Restrained defeat shake: a short decaying positional jitter, then snap
    // back to the cached rest transform.
    private void Shake()
    {
        _shakeTween?.Kill();
        var o = _camRest.Origin;
        _shakeTween = CreateTween();
        Vector3[] offs =
        {
            new(0.08f, -0.05f, 0f), new(-0.06f, 0.04f, 0f),
            new(0.04f, 0.03f, 0f), new(-0.02f, -0.02f, 0f),
        };
        foreach (var off in offs)
            _shakeTween.TweenProperty(_camera, "position", o + off, 0.06f).SetTrans(Tween.TransitionType.Sine);
        _shakeTween.TweenProperty(_camera, "position", o, 0.06f).SetTrans(Tween.TransitionType.Sine);
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
