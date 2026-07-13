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
using HexGame.Chess;

namespace HexGame.UI;

public partial class GameOverScreen : Control
{
    private readonly Action _onNewRun;
    private readonly Action _onTitle;

    private Label _title;
    private Label _subtitle;
    private Label _battleValue;
    private Label _scoreValue;
    private Label _capturesValue;
    private Label _lostValue;
    private Label _earnedValue;
    private Control _newBestChip;
    private Tween _titleTween;
    private CpuParticles2D _confetti;

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
        // Gold confetti for the victory presentation only (CpuParticles2D —
        // GL Compatibility safe; the texture is a code-made white square).
        var img = Image.CreateEmpty(6, 6, false, Image.Format.Rgba8);
        img.Fill(Colors.White);
        _confetti = new CpuParticles2D
        {
            Texture = ImageTexture.CreateFromImage(img),
            Emitting = false,
            Amount = 48,
            Lifetime = 3.2,
            Preprocess = 1.2,               // already falling when the screen fades in
            EmissionShape = CpuParticles2D.EmissionShapeEnum.Rectangle,
            EmissionRectExtents = new Vector2(430, 6),
            Direction = new Vector2(0, 1),
            Spread = 25f,
            Gravity = new Vector2(0, 260),
            InitialVelocityMin = 40f,
            InitialVelocityMax = 120f,
            AngularVelocityMin = -220f,
            AngularVelocityMax = 220f,
            ScaleAmountMin = 0.7f,
            ScaleAmountMax = 1.4f,
            Color = UiTheme.Accent,
        };
        _confetti.VisibilityChanged += () =>
        {
            if (!IsVisibleInTree()) _confetti.Emitting = false;
        };
        AddChild(_confetti);

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

        _title = UiTheme.Heading("ARMY FALLEN", UiTheme.HeadingSize, UiTheme.Danger, HorizontalAlignment.Center);
        v.AddChild(_title);

        _subtitle = UiTheme.MakeLabel("", UiTheme.BodySize, UiTheme.TextMuted, HorizontalAlignment.Center);
        v.AddChild(_subtitle);
        v.AddChild(new Control { CustomMinimumSize = new Vector2(0, 4) });

        v.AddChild(ResultRow("BATTLE REACHED", out _battleValue));
        v.AddChild(ResultRow("SCORE", out _scoreValue));
        v.AddChild(ResultRow("CAPTURES", out _capturesValue));
        v.AddChild(ResultRow("PIECES LOST", out _lostValue));
        v.AddChild(ResultRow("MONEY EARNED", out _earnedValue));
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

    public void Present(bool victory, int battle, int score, bool newBest, RunState run = null)
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
        if (_capturesValue != null) _capturesValue.Text = run?.CapturesMade.ToString() ?? "—";
        if (_lostValue != null) _lostValue.Text = run?.PiecesLost.ToString() ?? "—";
        if (_earnedValue != null) _earnedValue.Text = run != null ? $"${run.MoneyEarned}" : "—";
        if (_newBestChip != null) _newBestChip.Visible = newBest;

        if (_confetti != null)
        {
            _confetti.Position = new Vector2(GetViewportRect().Size.X / 2f, -12f);
            _confetti.Emitting = victory;
            if (victory) _confetti.Restart();
        }

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
