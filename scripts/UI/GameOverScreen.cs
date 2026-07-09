// =============================================================================
// GameOverScreen
// =============================================================================
// Purpose:
//   Run summary shown at the end of a run — defeat ("ARMY FALLEN") when the
//   last piece is lost, or victory ("CROWN CLAIMED") after the final battle.
//   Shows an optional "NEW BEST!" chip, the battle reached and final score,
//   and New Run / Title buttons.
//
// Interactions:
//   - ScreenManager: constructs with the two callbacks; calls
//     Present(victory, battle, score, newBest) before showing.
// =============================================================================

using System;
using Godot;

namespace HexGame.UI;

public partial class GameOverScreen : Control
{
    private readonly Action _onNewRun;
    private readonly Action _onTitle;

    private Label _title;
    private Label _subtitle;
    private Label _battleValue;
    private Label _scoreValue;
    private Control _newBestChip;
    private Tween _titleTween;

    public GameOverScreen(Action onNewRun, Action onTitle)
    {
        _onNewRun = onNewRun;
        _onTitle = onTitle;
    }

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        Build();
    }

    private void Build()
    {
        var center = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(center);

        var panel = UiTheme.ModalPanel();
        center.AddChild(panel);

        var v = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        v.CustomMinimumSize = new Vector2(480, 0);
        v.AddThemeConstantOverride("separation", 14);
        panel.AddChild(v);

        _newBestChip = UiTheme.Chip("NEW BEST!", UiTheme.ChipSize, UiTheme.Accent);
        _newBestChip.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
        _newBestChip.Visible = false;
        v.AddChild(_newBestChip);

        _title = UiTheme.MakeLabel("ARMY FALLEN", UiTheme.HeadingSize, UiTheme.Danger, HorizontalAlignment.Center);
        v.AddChild(_title);

        _subtitle = UiTheme.MakeLabel("", UiTheme.BodySize, UiTheme.TextMuted, HorizontalAlignment.Center);
        v.AddChild(_subtitle);
        v.AddChild(new Control { CustomMinimumSize = new Vector2(0, 4) });

        v.AddChild(ResultRow("BATTLE REACHED", out _battleValue));
        v.AddChild(ResultRow("SCORE", out _scoreValue));
        v.AddChild(new Control { CustomMinimumSize = new Vector2(0, 10) });

        var retry = UiTheme.PrimaryButton("NEW RUN");
        retry.Pressed += () => _onNewRun?.Invoke();
        v.AddChild(retry);

        var title = UiTheme.GhostButton("TITLE");
        title.Pressed += () => _onTitle?.Invoke();
        v.AddChild(title);
    }

    private static HBoxContainer ResultRow(string caption, out Label value)
    {
        var row = new HBoxContainer();
        var c = UiTheme.MakeLabel(caption, UiTheme.BodySize, UiTheme.TextMuted);
        c.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        row.AddChild(c);
        value = UiTheme.MakeLabel("0", UiTheme.BodySize, UiTheme.Accent, HorizontalAlignment.Right);
        row.AddChild(value);
        return row;
    }

    public void Present(bool victory, int battle, int score, bool newBest)
    {
        if (_title != null)
        {
            _title.Text = victory ? "CROWN CLAIMED" : "ARMY FALLEN";
            _title.AddThemeColorOverride("font_color", victory ? UiTheme.Accent : UiTheme.Danger);
        }
        if (_subtitle != null)
            _subtitle.Text = victory ? "Every battle won. The board is yours." : "The last piece has been taken.";
        if (_battleValue != null) _battleValue.Text = battle.ToString();
        if (_scoreValue != null) _scoreValue.Text = score.ToString();
        if (_newBestChip != null) _newBestChip.Visible = newBest;

        // One-time title fade/scale (no bounce — the moment should feel weighty).
        if (_title != null)
        {
            _title.PivotOffset = _title.Size / 2f;
            _title.Scale = new Vector2(0.9f, 0.9f);
            _titleTween?.Kill();
            _titleTween = CreateTween();
            _titleTween.TweenProperty(_title, "scale", Vector2.One, 0.25f).SetTrans(Tween.TransitionType.Sine);
        }
    }

    public override void _ExitTree()
    {
        _titleTween?.Kill();
    }
}
