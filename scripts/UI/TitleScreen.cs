// =============================================================================
// TitleScreen
// =============================================================================
// Purpose:
//   The front door: gold "HEX" wordmark, PLAY (primary) and HOW TO PLAY (ghost),
//   and bottom stat chips showing the persisted best wave + high score, all over
//   the live dimmed board. Refresh() re-reads GameSession so the chips are
//   current each time the title is shown.
//
// Interactions:
//   - ScreenManager: constructs with the play / how-to-play callbacks, calls
//     Refresh() before showing.
// =============================================================================

using System;
using Godot;

namespace HexGame.UI;

public partial class TitleScreen : Control
{
    private readonly GameSession _session;
    private readonly Action _onPlay;
    private readonly Action _onHowToPlay;
    private Label _bestWave;
    private Label _highScore;

    public TitleScreen(GameSession session, Action onPlay, Action onHowToPlay)
    {
        _session = session;
        _onPlay = onPlay;
        _onHowToPlay = onHowToPlay;
    }

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        Build();
        Refresh();
    }

    private void Build()
    {
        var center = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(center);

        var v = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        v.AddThemeConstantOverride("separation", 22);
        center.AddChild(v);

        var logo = UiTheme.MakeLabel("HEX", UiTheme.TitleSize, UiTheme.Accent, HorizontalAlignment.Center);
        logo.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.6f));
        logo.AddThemeConstantOverride("shadow_offset_y", 4);
        logo.AddThemeConstantOverride("shadow_offset_x", 0);
        v.AddChild(logo);

        v.AddChild(UiTheme.MakeLabel("a movement hunt", UiTheme.BodySize, UiTheme.TextMuted, HorizontalAlignment.Center));
        v.AddChild(new Control { CustomMinimumSize = new Vector2(0, 28) });

        var play = UiTheme.PrimaryButton("PLAY");
        play.CustomMinimumSize = new Vector2(360, 88);
        play.Pressed += () => _onPlay?.Invoke();
        v.AddChild(play);

        var how = UiTheme.GhostButton("HOW TO PLAY");
        how.CustomMinimumSize = new Vector2(360, 60);
        how.Pressed += () => _onHowToPlay?.Invoke();
        v.AddChild(how);

        // Bottom stat chips.
        var chips = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        chips.AddThemeConstantOverride("separation", 24);
        chips.SetAnchorsPreset(LayoutPreset.BottomWide);
        chips.OffsetTop = -96; chips.OffsetBottom = -40;
        chips.MouseFilter = MouseFilterEnum.Ignore;
        chips.AddChild(StatChip("BEST WAVE", out _bestWave));
        chips.AddChild(StatChip("HIGH SCORE", out _highScore));
        AddChild(chips);
    }

    private static PanelContainer StatChip(string caption, out Label value)
    {
        var panel = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        panel.AddThemeStyleboxOverride("panel", UiTheme.Box(UiTheme.PanelRaised, 10, 1, UiTheme.PanelBorder, 22, 12));
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 14);
        row.AddChild(UiTheme.MakeLabel(caption, UiTheme.ChipSize, UiTheme.TextMuted));
        value = UiTheme.MakeLabel("—", UiTheme.ChipSize, UiTheme.Accent);
        row.AddChild(value);
        panel.AddChild(row);
        return panel;
    }

    public void Refresh()
    {
        if (_bestWave != null) _bestWave.Text = _session.BestWave > 0 ? _session.BestWave.ToString() : "—";
        if (_highScore != null) _highScore.Text = _session.HighScore.ToString();
    }
}
