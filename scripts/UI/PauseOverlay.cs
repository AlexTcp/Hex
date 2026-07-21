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
    private Control _actionRow;
    private Control _confirmRow;

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

        // Normal actions: resume, or begin abandoning (which asks to confirm first —
        // ABANDON sits directly under RESUME, so a single mis-tap must not discard a
        // whole run without a chance to back out).
        var actions = new VBoxContainer();
        actions.AddThemeConstantOverride("separation", 18);
        _actionRow = actions;
        v.AddChild(actions);

        var resume = UiTheme.PrimaryButton("RESUME");
        resume.Pressed += () => _onResume?.Invoke();
        actions.AddChild(resume);

        var quit = UiTheme.DangerButton("ABANDON RUN");
        quit.Pressed += ShowConfirm;
        actions.AddChild(quit);

        // Confirmation: only YES commits the abandon; CANCEL returns to the menu.
        var confirm = new VBoxContainer { Visible = false };
        confirm.AddThemeConstantOverride("separation", 12);
        _confirmRow = confirm;
        v.AddChild(confirm);

        var warn = UiTheme.MakeLabel("Abandon this run? Your progress is lost.",
            UiTheme.BodySmallSize, UiTheme.Danger, HorizontalAlignment.Center);
        warn.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        warn.CustomMinimumSize = new Vector2(400, 0);
        confirm.AddChild(warn);

        var yes = UiTheme.DangerButton("YES, ABANDON");
        yes.Pressed += () => _onAbandon?.Invoke();
        confirm.AddChild(yes);

        var cancel = UiTheme.PrimaryButton("KEEP PLAYING");
        cancel.Pressed += HideConfirm;
        confirm.AddChild(cancel);
    }

    // Two-step abandon: swap the resume/abandon row for a yes/cancel confirmation.
    private void ShowConfirm()
    {
        if (_actionRow != null) _actionRow.Visible = false;
        if (_confirmRow != null) _confirmRow.Visible = true;
    }

    private void HideConfirm()
    {
        if (_confirmRow != null) _confirmRow.Visible = false;
        if (_actionRow != null) _actionRow.Visible = true;
    }

    public void Refresh(RunState run)
    {
        HideConfirm();   // always reopen on the normal menu, never a stale confirm
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
