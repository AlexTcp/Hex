// =============================================================================
// PauseOverlay
// =============================================================================
// Purpose:
//   Centred pause panel: current battle/score line plus Resume / Abandon Run.
//   The board is already frozen (no enemy acts without a committed player
//   action, and picking is gated) so this is purely a menu.
//
// Interactions:
//   - ScreenManager: constructs with the two callbacks; calls Refresh(battle,
//     score) before showing.
// =============================================================================

using System;
using Godot;

namespace HexGame.UI;

public partial class PauseOverlay : Control
{
    private readonly Action _onResume;
    private readonly Action _onAbandon;
    private Label _stats;

    public PauseOverlay(Action onResume, Action onAbandon)
    {
        _onResume = onResume;
        _onAbandon = onAbandon;
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

        var quit = UiTheme.DangerButton("ABANDON RUN");
        quit.Pressed += () => _onAbandon?.Invoke();
        v.AddChild(quit);
    }

    public void Refresh(int battle, int score)
    {
        if (_stats != null) _stats.Text = $"Battle {battle}    Score {score}";
    }
}
