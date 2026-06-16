// =============================================================================
// PauseOverlay
// =============================================================================
// Purpose:
//   Centred pause panel: current wave/score line plus Resume / Change Piece /
//   Quit to Title. The board is already frozen (no enemy stepping happens
//   without a committed move, and picking is gated) so this is purely a menu.
//
// Interactions:
//   - ScreenManager: constructs with the three callbacks; calls Refresh(wave,
//     score) before showing.
// =============================================================================

using System;
using Godot;

namespace HexGame.UI;

public partial class PauseOverlay : Control
{
    private readonly Action _onResume;
    private readonly Action _onChangePiece;
    private readonly Action _onQuitTitle;
    private Label _stats;

    public PauseOverlay(Action onResume, Action onChangePiece, Action onQuitTitle)
    {
        _onResume = onResume;
        _onChangePiece = onChangePiece;
        _onQuitTitle = onQuitTitle;
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
        v.CustomMinimumSize = new Vector2(440, 0);
        v.AddThemeConstantOverride("separation", 18);
        panel.AddChild(v);

        v.AddChild(UiTheme.MakeLabel("PAUSED", UiTheme.HeadingSize, UiTheme.Text, HorizontalAlignment.Center));
        _stats = UiTheme.MakeLabel("", UiTheme.BodySmallSize, UiTheme.TextMuted, HorizontalAlignment.Center);
        v.AddChild(_stats);
        v.AddChild(new Control { CustomMinimumSize = new Vector2(0, 6) });

        var resume = UiTheme.PrimaryButton("RESUME");
        resume.Pressed += () => _onResume?.Invoke();
        v.AddChild(resume);

        var change = UiTheme.SecondaryButton("CHANGE PIECE");
        change.Pressed += () => _onChangePiece?.Invoke();
        v.AddChild(change);

        var quit = UiTheme.DangerButton("QUIT TO TITLE");
        quit.Pressed += () => _onQuitTitle?.Invoke();
        v.AddChild(quit);
    }

    public void Refresh(int wave, int score)
    {
        if (_stats != null) _stats.Text = $"Wave {wave}    Score {score}";
    }
}
