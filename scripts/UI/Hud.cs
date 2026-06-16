// =============================================================================
// Hud
// =============================================================================
// Purpose:
//   The in-run heads-up display: a wave/enemies chip (top-left), the live score
//   (top-centre), a transient combo flourish (centre), and the only two input
//   controls on the play field — a pause button (top-right, left of the gear)
//   and a small reach-overlay toggle (bottom-left). Everything else ignores
//   input so taps reach the 3D board. Driven entirely by ScreenManager via the
//   Set*/Show* methods (which mirror HexBoard's signals).
//
// Interactions:
//   - ScreenManager: constructs it, calls SetWave/SetEnemies/SetScore/
//     ShowCombo/ShowCleared/SetThreat, and supplies the pause + reach callbacks.
// =============================================================================

using System;
using Godot;

namespace HexGame.UI;

public partial class Hud : Control
{
    private readonly Action _onPause;
    private readonly Action<bool> _onReach;

    private Label _waveLabel;
    private Label _enemiesLabel;
    private Label _scoreValue;
    private Label _combo;
    private Tween _comboTween;

    private int _wave = 1;
    private int _enemies = 0;
    private bool _threat = false;

    public Hud(Action onPause, Action<bool> onReach)
    {
        _onPause = onPause;
        _onReach = onReach;
    }

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        Build();
    }

    private void Build()
    {
        // --- Top-left: wave + enemies ---
        var leftChip = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore };
        leftChip.AddThemeStyleboxOverride("panel",
            UiTheme.Box(UiTheme.PanelRaised, 10, 1, UiTheme.PanelBorder, 18, 10));
        leftChip.SetAnchorsPreset(LayoutPreset.TopLeft);
        leftChip.OffsetLeft = 24; leftChip.OffsetTop = 24;
        var leftV = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        leftV.AddThemeConstantOverride("separation", 2);
        _waveLabel = UiTheme.MakeLabel("WAVE 1", UiTheme.HudSecondarySize, UiTheme.Text);
        _enemiesLabel = UiTheme.MakeLabel("Enemies 0", UiTheme.HudSecondarySize, UiTheme.TextMuted);
        leftV.AddChild(_waveLabel);
        leftV.AddChild(_enemiesLabel);
        leftChip.AddChild(leftV);
        AddChild(leftChip);

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

        // --- Centre: combo flourish ---
        _combo = UiTheme.MakeLabel("", UiTheme.ComboSize, UiTheme.Accent, HorizontalAlignment.Center);
        _combo.SetAnchorsPreset(LayoutPreset.Center);
        _combo.GrowHorizontal = GrowDirection.Both;
        _combo.GrowVertical = GrowDirection.Both;
        _combo.MouseFilter = MouseFilterEnum.Ignore;
        _combo.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.85f));
        _combo.AddThemeConstantOverride("shadow_outline_size", 6);
        _combo.Modulate = new Color(1, 1, 1, 0);
        AddChild(_combo);

        // --- Top-right: pause (24px gap left of the DebugLog gear, top-aligned with it) ---
        var pause = new Button { Text = "II", ThemeTypeVariation = "ButtonSecondary" };
        pause.CustomMinimumSize = new Vector2(64, 64);
        pause.SetAnchorsPreset(LayoutPreset.TopRight);
        pause.OffsetRight = -104; pause.OffsetLeft = -168;
        pause.OffsetTop = 8; pause.OffsetBottom = 72;
        pause.Pressed += () => _onPause?.Invoke();
        AddChild(pause);

        // --- Bottom-left: reach toggle (low-stakes play aid) ---
        var reach = new Button { Text = "R", ToggleMode = true, ThemeTypeVariation = "ButtonGhost" };
        reach.CustomMinimumSize = new Vector2(48, 48);
        reach.SetAnchorsPreset(LayoutPreset.BottomLeft);
        reach.OffsetLeft = 24; reach.OffsetRight = 72;
        reach.OffsetTop = -72; reach.OffsetBottom = -24;
        reach.Toggled += on => _onReach?.Invoke(on);
        AddChild(reach);
    }

    public override void _ExitTree()
    {
        _comboTween?.Kill();
        _comboTween = null;
    }

    public void SetWave(int wave)
    {
        _wave = wave;
        if (_waveLabel != null) _waveLabel.Text = $"WAVE {wave}";
    }

    public void SetEnemies(int remaining)
    {
        _enemies = remaining;
        if (_enemiesLabel == null) return;
        _enemiesLabel.Text = $"Enemies {remaining}";
        // Gold when almost-clear, danger when a hunter is on top of you, else muted.
        _enemiesLabel.AddThemeColorOverride("font_color",
            _threat ? UiTheme.Danger : (remaining > 0 && remaining <= 2 ? UiTheme.Accent : UiTheme.TextMuted));
    }

    public void SetScore(int score)
    {
        if (_scoreValue == null) return;
        _scoreValue.Text = score.ToString();
        // Tabular pop on each change.
        _scoreValue.PivotOffset = _scoreValue.Size / 2f;
        var pop = CreateTween();
        pop.TweenProperty(_scoreValue, "scale", new Vector2(1.15f, 1.15f), 0.10f)
            .SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        pop.TweenProperty(_scoreValue, "scale", Vector2.One, 0.12f)
            .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
    }

    public void ShowCombo(int combo)
    {
        if (_combo == null) return;
        Flourish($"COMBO x{combo}", UiTheme.Accent);
    }

    public void ShowCleared()
    {
        if (_combo == null) return;
        Flourish("CLEARED", UiTheme.Success);
    }

    private void Flourish(string text, Color color)
    {
        _combo.Text = text;
        _combo.AddThemeColorOverride("font_color", color);
        _combo.PivotOffset = _combo.Size / 2f;

        _comboTween?.Kill();
        _combo.Scale = new Vector2(0.6f, 0.6f);
        _combo.Modulate = new Color(1, 1, 1, 0);
        _combo.Position = Vector2.Zero;

        _comboTween = CreateTween();
        _comboTween.SetParallel(true);
        _comboTween.TweenProperty(_combo, "modulate:a", 1f, 0.18f).SetTrans(Tween.TransitionType.Sine);
        _comboTween.TweenProperty(_combo, "scale", new Vector2(1.25f, 1.25f), 0.18f)
            .SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        _comboTween.SetParallel(false);
        _comboTween.TweenProperty(_combo, "scale", Vector2.One, 0.16f)
            .SetTrans(Tween.TransitionType.Sine);
        _comboTween.TweenInterval(0.4);
        _comboTween.SetParallel(true);
        _comboTween.TweenProperty(_combo, "modulate:a", 0f, 0.3f).SetTrans(Tween.TransitionType.Sine);
        _comboTween.TweenProperty(_combo, "position:y", -40f, 0.3f).SetTrans(Tween.TransitionType.Sine);
    }

    public void SetThreat(bool threat)
    {
        _threat = threat;
        SetEnemies(_enemies);   // re-apply enemy-label colour
    }
}
