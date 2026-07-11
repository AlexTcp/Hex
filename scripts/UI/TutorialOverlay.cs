// =============================================================================
// TutorialOverlay
// =============================================================================
// Purpose:
//   First-run, three-step coachmark teaching select -> move/capture -> the new
//   DANGER mechanic (hunters can catch you). Re-triggerable from the title's
//   HOW TO PLAY. Purely explanatory panels over a dimmed board (board input is
//   gated by ScreenManager while it's up), advanced with Next; a Skip dismisses
//   all. Calls onComplete when finished so ScreenManager can mark it seen and
//   hand back control.
//
// Interactions:
//   - ScreenManager: constructs with onComplete; calls Begin() to (re)start.
// =============================================================================

using System;
using Godot;

namespace HexGame.UI;

public partial class TutorialOverlay : Control
{
    private readonly Action _onComplete;

    private static readonly (string Title, string Body, bool Danger)[] Steps =
    {
        ("Command your army", "Tap one of your ivory pieces to see its legal moves. Land on a black piece to capture it — clear them all to win the battle.", false),
        ("Earn & spend", "Captures pay money — and pieces you lose are gone for good. Between battles, restock your army at the shop, and add rule-bending Gambits and tile upgrades.", false),
        ("Mind the board", "One enemy strikes back after each of your moves — RED tiles warn where you'd be captured. And the board crumbles from the outside in: don't linger on the rim.", true),
    };

    private ColorRect _scrim;
    private PanelContainer _panel;
    private Label _title;
    private Label _body;
    private Button _next;
    private int _step = 0;

    public TutorialOverlay(Action onComplete)
    {
        _onComplete = onComplete;
    }

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        Build();
    }

    private void Build()
    {
        _scrim = new ColorRect { Color = new Color(0, 0, 0, 0.45f), MouseFilter = MouseFilterEnum.Stop };
        _scrim.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_scrim);

        var center = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(center);

        _panel = UiTheme.ModalPanel();
        center.AddChild(_panel);

        var v = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        // Cap width to the viewport so the panel keeps a margin on narrow phones.
        v.CustomMinimumSize = new Vector2(Mathf.Min(560f, GetViewportRect().Size.X * 0.9f), 0);
        v.AddThemeConstantOverride("separation", 16);
        _panel.AddChild(v);

        _title = UiTheme.MakeLabel("", UiTheme.HeadingSize, UiTheme.Text, HorizontalAlignment.Center);
        v.AddChild(_title);

        _body = UiTheme.MakeLabel("", UiTheme.BodySize, UiTheme.Text, HorizontalAlignment.Center);
        _body.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _body.CustomMinimumSize = new Vector2(0, 140);   // room for the longest (danger) step to wrap
        v.AddChild(_body);
        v.AddChild(new Control { CustomMinimumSize = new Vector2(0, 8) });

        _next = UiTheme.PrimaryButton("NEXT");
        _next.Pressed += Advance;
        v.AddChild(_next);

        var skip = UiTheme.GhostButton("SKIP");
        skip.CustomMinimumSize = new Vector2(0, 56);
        skip.Pressed += Complete;
        v.AddChild(skip);
    }

    public void Begin()
    {
        _step = 0;
        ShowStep();
    }

    private void ShowStep()
    {
        var s = Steps[_step];
        _title.Text = s.Title;
        _title.AddThemeColorOverride("font_color", s.Danger ? UiTheme.Danger : UiTheme.Text);
        _body.Text = s.Body;
        _next.Text = _step == Steps.Length - 1 ? "GOT IT" : "NEXT";
    }

    private void Advance()
    {
        _step++;
        if (_step >= Steps.Length) { Complete(); return; }
        ShowStep();
    }

    private void Complete() => _onComplete?.Invoke();
}
