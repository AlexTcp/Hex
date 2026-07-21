// =============================================================================
// Hud
// =============================================================================
// Purpose:
//   The in-battle heads-up display: a battle/enemies/money chip (top-left),
//   the live score (top-centre), a crumble-countdown chip (top-left, second
//   row), a transient status flourish (centre), the pause button (top-right)
//   and the reserve bar (bottom-centre) — one button per reserve piece that
//   arms deploy mode on the board. Everything else ignores input so taps reach
//   the 3D board. Driven entirely by ScreenManager via the Set*/Show* methods
//   (which mirror HexBoard's signals).
//
// Interactions:
//   - ScreenManager: constructs it with the pause + deploy callbacks; calls
//     SetBattle/SetEnemies/SetScore/SetMoney/SetCrumble/RefreshReserve/
//     ShowNote/SetDeployArmed.
//   - RunState: bound via BindRun so the reserve bar can list piece names.
// =============================================================================

using System;
using Godot;
using HexGame.Chess;

namespace HexGame.UI;

public partial class Hud : Control
{
    private readonly Action _onPause;
    private readonly Action<int> _onDeploy;     // reserve index; -1 cancels

    private RunState _run;

    private Label _battleLabel;
    private Label _enemiesLabel;
    private Label _moneyLabel;
    private Label _scoreValue;
    private Tween _scoreTween;      // rolling count-up of the displayed number
    private Tween _scorePopTween;   // scale pop
    private int _scoreShown;        // the number currently displayed (for count-up)
    private Label _crumbleLabel;
    private PanelContainer _crumbleChip;
    private Label _note;
    private Tween _noteTween;
    private VBoxContainer _reserveBar;
    private PanelContainer _inspectChip;
    private Label _inspectLabel;
    private int _armedIndex = -1;

    public Hud(Action onPause, Action<int> onDeploy)
    {
        _onPause = onPause;
        _onDeploy = onDeploy;
    }

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        Build();
    }

    private void Build()
    {
        // --- Top-left: battle + enemies + money ---
        var leftChip = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore };
        leftChip.AddThemeStyleboxOverride("panel",
            UiTheme.Box(UiTheme.PanelRaised, 10, 1, UiTheme.PanelBorder, 18, 10));
        leftChip.SetAnchorsPreset(LayoutPreset.TopLeft);
        leftChip.OffsetLeft = 24; leftChip.OffsetTop = 24;
        var leftV = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        leftV.AddThemeConstantOverride("separation", 2);
        _battleLabel = UiTheme.MakeLabel("BATTLE 1", UiTheme.HudSecondarySize, UiTheme.Text);
        _enemiesLabel = UiTheme.MakeLabel("Enemies 0", UiTheme.HudSecondarySize, UiTheme.TextMuted);
        _moneyLabel = UiTheme.MakeLabel("$0", UiTheme.HudSecondarySize, UiTheme.Accent);
        leftV.AddChild(_battleLabel);
        leftV.AddChild(_enemiesLabel);
        leftV.AddChild(_moneyLabel);
        leftChip.AddChild(leftV);
        AddChild(leftChip);

        // --- Below it: crumble countdown ---
        _crumbleChip = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore };
        _crumbleChip.AddThemeStyleboxOverride("panel",
            UiTheme.Box(UiTheme.PanelRaised, 10, 1, UiTheme.PanelBorder, 18, 8));
        _crumbleChip.SetAnchorsPreset(LayoutPreset.TopLeft);
        _crumbleChip.OffsetLeft = 24; _crumbleChip.OffsetTop = 172;
        _crumbleLabel = UiTheme.MakeLabel("", UiTheme.HudSecondarySize, UiTheme.TextMuted);
        _crumbleChip.AddChild(_crumbleLabel);
        AddChild(_crumbleChip);

        // --- Top-centre: score ---
        var scoreBox = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        scoreBox.SetAnchorsPreset(LayoutPreset.TopWide);
        scoreBox.OffsetTop = 16; scoreBox.OffsetBottom = 100;
        scoreBox.AddThemeConstantOverride("separation", 0);
        var scoreCaption = UiTheme.MakeLabel("SCORE", UiTheme.SectionSize, UiTheme.TextMuted, HorizontalAlignment.Center);
        _scoreValue = UiTheme.MakeLabel("0", UiTheme.HudPrimarySize, UiTheme.Text, HorizontalAlignment.Center);
        _scoreValue.PivotOffset = Vector2.Zero;
        scoreBox.AddChild(scoreCaption);
        scoreBox.AddChild(_scoreValue);
        AddChild(scoreBox);

        // --- Centre: status flourish (promotions, shields, cracks…) ---
        // Full-rect with centred alignment: anchoring the label itself to the
        // centre broke when Flourish reset Position (notes drew from the
        // screen's top-left corner), and long boss text needs to wrap.
        _note = UiTheme.Heading("", UiTheme.ComboSize, UiTheme.Accent, HorizontalAlignment.Center);
        _note.SetAnchorsPreset(LayoutPreset.FullRect);
        _note.VerticalAlignment = VerticalAlignment.Center;
        _note.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _note.MouseFilter = MouseFilterEnum.Ignore;
        _note.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.85f));
        _note.AddThemeConstantOverride("shadow_outline_size", 6);
        _note.Modulate = new Color(1, 1, 1, 0);
        AddChild(_note);

        // --- Top-right: pause (left of the DebugLog gear) ---
        var pause = new Button { Text = "II", ThemeTypeVariation = "ButtonSecondary" };
        pause.CustomMinimumSize = new Vector2(64, 64);
        pause.SetAnchorsPreset(LayoutPreset.TopRight);
        pause.OffsetRight = -104; pause.OffsetLeft = -168;
        pause.OffsetTop = 8; pause.OffsetBottom = 72;
        pause.Pressed += () =>
        {
            Sfx.Play(SfxCue.Select);
            _onPause?.Invoke();
        };
        AddChild(pause);

        // --- Bottom-left: inspection chip (selected piece / enemy reach / tile) ---
        // Fixed-size and bottom-anchored: anchored containers don't reliably
        // re-grow when autowrapped text changes while hidden, so the chip is
        // sized for the longest description and the label centres within it.
        _inspectChip = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore };
        _inspectChip.AddThemeStyleboxOverride("panel",
            UiTheme.Box(UiTheme.PanelRaised, 10, 1, UiTheme.PanelBorder, 16, 10));
        _inspectChip.SetAnchorsPreset(LayoutPreset.BottomLeft);
        _inspectChip.OffsetLeft = 24; _inspectChip.OffsetRight = 494;
        _inspectChip.OffsetTop = -164; _inspectChip.OffsetBottom = -24;
        _inspectChip.Visible = false;
        _inspectLabel = UiTheme.MakeLabel("", UiTheme.BodySmallSize, UiTheme.Text);
        _inspectLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _inspectLabel.VerticalAlignment = VerticalAlignment.Center;
        _inspectChip.AddChild(_inspectLabel);
        AddChild(_inspectChip);

        // --- Bottom-centre: reserve bar (wraps into rows of 6 — a hoarded
        // reserve used to clip off both screen edges and become untappable) ---
        _reserveBar = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        _reserveBar.AddThemeConstantOverride("separation", 8);
        _reserveBar.SetAnchorsPreset(LayoutPreset.BottomWide);
        _reserveBar.OffsetTop = -96; _reserveBar.OffsetBottom = -24;
        _reserveBar.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(_reserveBar);
    }

    public override void _ExitTree()
    {
        _noteTween?.Kill();
        _noteTween = null;
    }

    // ----- Setters (mirror HexBoard signals) --------------------------------

    public void BindRun(RunState run)
    {
        _run = run;
        RebuildReserveBar();
    }

    public void SetBattle(int battle, BossModifier boss)
    {
        if (_battleLabel == null) return;
        _battleLabel.Text = boss != BossModifier.None
            ? $"BATTLE {battle} — {BossCatalog.NameOf(boss).ToUpperInvariant()}"
            : $"BATTLE {battle}";
        _battleLabel.AddThemeColorOverride("font_color",
            boss != BossModifier.None ? UiTheme.Danger : UiTheme.Text);
    }

    public void SetEnemies(int remaining)
    {
        if (_enemiesLabel == null) return;
        _enemiesLabel.Text = $"Enemies {remaining}";
        _enemiesLabel.AddThemeColorOverride("font_color",
            remaining > 0 && remaining <= 2 ? UiTheme.Accent : UiTheme.TextMuted);
    }

    public void SetMoney(int money)
    {
        if (_moneyLabel == null) return;
        _moneyLabel.Text = $"${money}";
    }

    public void SetCrumble(int turnsLeft, bool cracking)
    {
        if (_crumbleLabel == null) return;
        if (cracking)
        {
            _crumbleLabel.Text = turnsLeft > 0 ? $"COLLAPSE IN {turnsLeft}" : "CRUMBLING";
            _crumbleLabel.AddThemeColorOverride("font_color", UiTheme.Danger);
        }
        else if (turnsLeft <= 0)
        {
            // The final ring has collapsed; the board holds from here on.
            _crumbleLabel.Text = "CRUMBLED";
            _crumbleLabel.AddThemeColorOverride("font_color", UiTheme.TextMuted);
        }
        else
        {
            _crumbleLabel.Text = $"CRUMBLE IN {turnsLeft}";
            _crumbleLabel.AddThemeColorOverride("font_color",
                turnsLeft <= 3 ? UiTheme.Danger : UiTheme.TextMuted);
        }
    }

    public void SetScore(int score)
    {
        if (_scoreValue == null) return;
        _scoreValue.PivotOffset = _scoreValue.Size / 2f;

        // Roll the displayed number up to the new total — big capture / clear payouts
        // feel earned when they count up instead of snapping. Only count UP; a lower
        // value means a run reset, so snap to it. Kill any in-flight count so rapid
        // back-to-back updates (capture + clear bonus, Bishop Echo) don't fight.
        _scoreTween?.Kill();
        int from = _scoreShown;
        _scoreShown = score;
        if (score > from)
        {
            _scoreTween = CreateTween();
            _scoreTween.TweenMethod(Callable.From<int>(SetScoreText), from, score, 0.35f)
                .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        }
        else
        {
            _scoreValue.Text = score.ToString();
        }

        // Scale pop (its own tween; reset first so an interrupted pop can't stick scaled).
        _scorePopTween?.Kill();
        _scoreValue.Scale = Vector2.One;
        _scorePopTween = CreateTween();
        _scorePopTween.TweenProperty(_scoreValue, "scale", new Vector2(1.15f, 1.15f), 0.10f)
            .SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        _scorePopTween.TweenProperty(_scoreValue, "scale", Vector2.One, 0.12f)
            .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
    }

    private void SetScoreText(int v)
    {
        if (_scoreValue != null) _scoreValue.Text = v.ToString();
    }

    // The reserve bar reads straight from the bound RunState; the army-count
    // signal is just the refresh trigger.
    public void RefreshReserve() => RebuildReserveBar();

    // Board confirmed/cancelled deploy mode: clear the armed button state.
    public void SetDeployArmed(bool active)
    {
        if (!active) _armedIndex = -1;
        RebuildReserveBar();
    }

    private void RebuildReserveBar()
    {
        const int PerRow = 6;
        if (_reserveBar == null) return;
        foreach (Node child in _reserveBar.GetChildren())
            child.QueueFree();
        if (_run == null || _run.Reserve.Count == 0) return;

        int rows = (_run.Reserve.Count + PerRow - 1) / PerRow;
        _reserveBar.OffsetTop = -(24 + rows * 72);

        HBoxContainer row = null;
        for (int i = 0; i < _run.Reserve.Count; i++)
        {
            if (i % PerRow == 0)
            {
                row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
                row.AddThemeConstantOverride("separation", 12);
                row.MouseFilter = MouseFilterEnum.Ignore;
                _reserveBar.AddChild(row);
            }

            int idx = i;
            var info = PieceCatalog.Info(_run.Reserve[i]);
            var b = new Button
            {
                Text = _armedIndex == i ? $"» {info.Name} «" : info.Name,
                ThemeTypeVariation = _armedIndex == i ? "ButtonPrimary" : "ButtonSecondary",
            };
            b.CustomMinimumSize = new Vector2(0, 60);
            b.Pressed += () =>
            {
                Sfx.Play(SfxCue.Select);
                if (_armedIndex == idx)
                {
                    _armedIndex = -1;
                    _onDeploy?.Invoke(-1);
                }
                else
                {
                    _armedIndex = idx;
                    _onDeploy?.Invoke(idx);
                }
                RebuildReserveBar();
            };
            row.AddChild(b);
        }
    }

    public void SetInspect(string text)
    {
        if (_inspectChip == null) return;
        _inspectChip.Visible = !string.IsNullOrEmpty(text);
        if (_inspectLabel != null) _inspectLabel.Text = text;
    }

    public void ShowNote(string text) => Flourish(text, UiTheme.Accent);
    public void ShowCleared() => Flourish("VICTORY", UiTheme.Success);

    private void Flourish(string text, Color color)
    {
        if (_note == null) return;
        _note.Text = text;
        _note.AddThemeColorOverride("font_color", color);
        _note.PivotOffset = _note.Size / 2f;

        _noteTween?.Kill();
        _note.Scale = new Vector2(0.6f, 0.6f);
        _note.Modulate = new Color(1, 1, 1, 0);
        _note.Position = Vector2.Zero;

        _noteTween = CreateTween();
        _noteTween.SetParallel(true);
        _noteTween.TweenProperty(_note, "modulate:a", 1f, 0.18f).SetTrans(Tween.TransitionType.Sine);
        _noteTween.TweenProperty(_note, "scale", new Vector2(1.25f, 1.25f), 0.18f)
            .SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        _noteTween.SetParallel(false);
        _noteTween.TweenProperty(_note, "scale", Vector2.One, 0.16f)
            .SetTrans(Tween.TransitionType.Sine);
        _noteTween.TweenInterval(0.5);
        _noteTween.SetParallel(true);
        _noteTween.TweenProperty(_note, "modulate:a", 0f, 0.3f).SetTrans(Tween.TransitionType.Sine);
        _noteTween.TweenProperty(_note, "position:y", -40f, 0.3f).SetTrans(Tween.TransitionType.Sine);
    }
}
