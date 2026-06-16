// =============================================================================
// ScreenManager
// =============================================================================
// Purpose:
//   The app state machine and UI orchestrator. Owns the five overlay screens +
//   the tutorial + the HUD + the scrim + the two vignettes, all as Control
//   children of the persistent UI root (the 3D board never unloads behind
//   them). Wires HexBoard's signals to the HUD and GameSession, drives screen
//   transitions (cross-fades), gates board input per-state (cooperating with
//   the DebugLog modal), pulses the danger vignette, and runs the attract
//   camera drift + the game-over shake.
//
// States: Title -> Select -> Playing -> (Paused) -> GameOver -> retry/back.
//
// Interactions:
//   - GameScreen: builds the 3D stage (camera/lights/env), then constructs this
//     with the board, camera, session and UI root, and calls GoTitle().
//   - HexBoard: StartRun (commit) / SetToken (preview); listens to its signals.
//   - GameSession: mirrors live score/wave, commits records on PlayerCaught.
// =============================================================================

using System;
using Godot;
using HexGame.Board;
using HexGame.Tokens;

namespace HexGame.UI;

public partial class ScreenManager : Node
{
    public enum AppState { Title, Select, Playing, Paused, GameOver }

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
    private CharacterSelect _select;
    private Hud _hud;
    private PauseOverlay _pause;
    private GameOverScreen _gameOver;
    private TutorialOverlay _tutorial;
    private Control _currentScreen;

    private Transform3D _camRest;
    private Tween _driftTween;
    private Tween _shakeTween;

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
        if (_board != null)
        {
            _board.ScoreChanged -= OnScoreChanged;
            _board.WaveChanged -= OnWaveChanged;
            _board.EnemiesChanged -= OnEnemiesChanged;
            _board.ComboChanged -= OnComboChanged;
            _board.BoardSolved -= OnBoardSolved;
            _board.ThreatChanged -= OnThreatChanged;
            _board.PlayerCaught -= OnPlayerCaught;
        }
    }

    // ----- Build ---------------------------------------------------------

    private void BuildOverlays()
    {
        // Behind everything: a static dark vignette frames the board; the danger
        // vignette pulses red over it; the scrim dims for menus. All ignore input
        // except the scrim when a menu is up.
        AddRoot(UiTheme.Vignette(new Color(0, 0, 0), 0.78f));
        _dangerVignette = UiTheme.Vignette(UiTheme.Danger, 0.5f);
        _dangerVignette.Modulate = new Color(1, 1, 1, 0);
        AddRoot(_dangerVignette);

        _scrim = new ColorRect { Color = new Color(0, 0, 0, 0), MouseFilter = Control.MouseFilterEnum.Ignore };
        _scrim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddRoot(_scrim);

        _hud = new Hud(GoPause, on => _board.SetShowReach(on));
        _title = new TitleScreen(_session, () => GoSelect(), () => ShowTutorial());
        _select = new CharacterSelect(_session, i => _board.SetToken(i), StartRun, GoTitle);
        _pause = new PauseOverlay(ResumeFromPause, GoSelect, GoTitle);
        _gameOver = new GameOverScreen(RetrySamePiece, GoSelect, GoTitle);
        _tutorial = new TutorialOverlay(OnTutorialComplete);

        foreach (Control c in new Control[] { _hud, _title, _select, _pause, _gameOver, _tutorial })
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
        _board.ScoreChanged += OnScoreChanged;
        _board.WaveChanged += OnWaveChanged;
        _board.EnemiesChanged += OnEnemiesChanged;
        _board.ComboChanged += OnComboChanged;
        _board.BoardSolved += OnBoardSolved;
        _board.ThreatChanged += OnThreatChanged;
        _board.PlayerCaught += OnPlayerCaught;
    }

    // ----- Board signal handlers -----------------------------------------

    private void OnScoreChanged(int score) { _session.Score = score; _hud.SetScore(score); }
    private void OnWaveChanged(int wave) { _session.Wave = wave; _hud.SetWave(wave); }
    private void OnEnemiesChanged(int n) => _hud.SetEnemies(n);
    private void OnComboChanged(int combo) => _hud.ShowCombo(combo);
    private void OnBoardSolved() => _hud.ShowCleared();

    private void OnThreatChanged(bool inDanger)
    {
        _hud.SetThreat(inDanger);
        SetDangerVignette(inDanger);
    }

    private void OnPlayerCaught()
    {
        SetDangerVignette(false);
        bool newBest = _session.CommitRun();
        string piece = TokenCatalog.All[Mathf.Clamp(_session.SelectedTokenIndex, 0, TokenCatalog.All.Length - 1)].Name;
        _gameOver.Present(_session.Wave, _session.Score, newBest, piece);
        Shake();
        GoState(AppState.GameOver, _gameOver, 0.6f, 0.3f);
    }

    private void OnDebugModalToggled(bool _) => UpdatePicking();

    // ----- State transitions ---------------------------------------------

    public void GoTitle()
    {
        _board.SetToken(_session.SelectedTokenIndex);   // preview piece behind the title
        _title.Refresh();
        StartDrift();
        GoState(AppState.Title, _title, 0.55f);
    }

    public void GoSelect()
    {
        _select.Refresh();   // selects the current piece, which previews it on the board
        StartDrift();
        GoState(AppState.Select, _select, 0.35f);
    }

    public void StartRun(int index)
    {
        _session.ResetRun(index);
        StopDrift();
        RestoreCamera();
        _board.StartRun(index);
        GoState(AppState.Playing, _hud, 0f);

        if (!_session.TutorialSeen) ShowTutorial();
    }

    private void RetrySamePiece() => StartRun(_session.SelectedTokenIndex);

    private void GoPause()
    {
        _board.ClearSelection();   // don't leave highlights/pulse/telegraph behind the menu
        _pause.Refresh(_session.Wave, _session.Score);
        GoState(AppState.Paused, _pause, 0.5f, 0.18f);
    }

    private void ResumeFromPause() => GoState(AppState.Playing, _hud, 0f, 0.16f);

    // Core transition: set state, fade scrim to its alpha, cross-fade screens.
    private void GoState(AppState state, Control screen, float scrimAlpha, float dur = 0.22f)
    {
        _state = state;
        UpdatePicking();

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

    // ----- Camera attract drift + game-over shake ------------------------

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

    // Restrained game-over shake: a short decaying positional jitter, then snap
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
    }
}
