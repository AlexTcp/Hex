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
using System.Collections.Generic;
using Godot;
using HexGame.Chess;

namespace HexGame.UI;

public partial class PauseOverlay : Control
{
    private readonly Action _onResume;
    private readonly Action _onAbandon;
    private Label _stats;
    private Label _gambits;

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

        v.AddChild(UiTheme.Heading("PAUSED", UiTheme.HeadingSize, UiTheme.Text, HorizontalAlignment.Center));
        _stats = UiTheme.MakeLabel("", UiTheme.BodySmallSize, UiTheme.TextMuted, HorizontalAlignment.Center);
        v.AddChild(_stats);

        // Owned gambits — nowhere else in a run reminds the player what rule
        // bends they've bought.
        _gambits = UiTheme.MakeLabel("", UiTheme.BodySmallSize, UiTheme.Accent, HorizontalAlignment.Center);
        _gambits.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _gambits.CustomMinimumSize = new Vector2(400, 0);
        _gambits.Visible = false;
        v.AddChild(_gambits);
        v.AddChild(new Control { CustomMinimumSize = new Vector2(0, 6) });

        var resume = UiTheme.PrimaryButton("RESUME");
        resume.Pressed += () => _onResume?.Invoke();
        v.AddChild(resume);

        var quit = UiTheme.DangerButton("ABANDON RUN");
        quit.Pressed += () => _onAbandon?.Invoke();
        v.AddChild(quit);
    }

    public void Refresh(RunState run)
    {
        if (_stats != null) _stats.Text = $"Battle {run?.Battle ?? 1}    Score {run?.Score ?? 0}";
        if (_gambits == null) return;
        if (run == null || run.Gambits.Count == 0)
        {
            _gambits.Visible = false;
            return;
        }
        var names = new List<string>();
        foreach (var g in run.Gambits) names.Add(GambitCatalog.Info(g).Name);
        _gambits.Text = "Gambits: " + string.Join("  ·  ", names);
        _gambits.Visible = true;
    }
}
