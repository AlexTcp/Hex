// =============================================================================
// GameOverScreen
// =============================================================================
// Purpose:
//   Run summary shown when a hunter catches the player: "CAUGHT", an optional
//   "NEW BEST!" chip, the piece played, the wave reached and final score, and
//   Retry (same piece) / Change Piece / Title.
//
// Interactions:
//   - ScreenManager: constructs with the three callbacks; calls
//     Present(wave, score, newBest, pieceName) before showing.
// =============================================================================

using System;
using Godot;

namespace HexGame.UI;

public partial class GameOverScreen : Control
{
    private readonly Action _onRetry;
    private readonly Action _onChangePiece;
    private readonly Action _onTitle;

    private Label _title;
    private Label _pieceName;
    private Label _waveValue;
    private Label _scoreValue;
    private Control _newBestChip;
    private Tween _titleTween;

    public GameOverScreen(Action onRetry, Action onChangePiece, Action onTitle)
    {
        _onRetry = onRetry;
        _onChangePiece = onChangePiece;
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

        _title = UiTheme.MakeLabel("CAUGHT", UiTheme.HeadingSize, UiTheme.Danger, HorizontalAlignment.Center);
        v.AddChild(_title);

        _pieceName = UiTheme.MakeLabel("", UiTheme.BodySize, UiTheme.TextMuted, HorizontalAlignment.Center);
        v.AddChild(_pieceName);
        v.AddChild(new Control { CustomMinimumSize = new Vector2(0, 4) });

        v.AddChild(ResultRow("WAVE REACHED", out _waveValue));
        v.AddChild(ResultRow("SCORE", out _scoreValue));
        v.AddChild(new Control { CustomMinimumSize = new Vector2(0, 10) });

        var retry = UiTheme.PrimaryButton("RETRY");
        retry.Pressed += () => _onRetry?.Invoke();
        v.AddChild(retry);

        var change = UiTheme.SecondaryButton("CHANGE PIECE");
        change.Pressed += () => _onChangePiece?.Invoke();
        v.AddChild(change);

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

    public void Present(int wave, int score, bool newBest, string pieceName)
    {
        if (_waveValue != null) _waveValue.Text = wave.ToString();
        if (_scoreValue != null) _scoreValue.Text = score.ToString();
        if (_pieceName != null) _pieceName.Text = pieceName;
        if (_newBestChip != null) _newBestChip.Visible = newBest;

        // One-time title fade/scale (no bounce — a loss should feel weighty).
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
