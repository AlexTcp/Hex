// =============================================================================
// NewRunScreen
// =============================================================================
// Purpose:
//   The "muster your army" screen that replaces the old per-token character
//   select: shows the run's three random starter pieces as cards (monogram +
//   name + move rule from PieceCatalog), the starting purse, a free REROLL
//   ARMY, and BEGIN RUN. The live board sits behind it showing the preview
//   arrangement.
//
// Interactions:
//   - ScreenManager: constructs with onReroll (returns the fresh RunState to
//     display), onBegin, onBack; calls Present(run) before showing.
//   - PieceCatalog: names/descriptions for the starter cards.
// =============================================================================

using System;
using Godot;
using HexGame.Chess;

namespace HexGame.UI;

public partial class NewRunScreen : Control
{
    private readonly Func<RunState> _onReroll;
    private readonly Action _onBegin;
    private readonly Action _onBack;

    private HBoxContainer _cardRow;
    private Label _purse;

    public NewRunScreen(Func<RunState> onReroll, Action onBegin, Action onBack)
    {
        _onReroll = onReroll;
        _onBegin = onBegin;
        _onBack = onBack;
    }

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        Build();
    }

    private void Build()
    {
        var margin = new MarginContainer { MouseFilter = MouseFilterEnum.Ignore };
        margin.SetAnchorsPreset(LayoutPreset.FullRect);
        margin.AddThemeConstantOverride("margin_left", 24);
        margin.AddThemeConstantOverride("margin_right", 24);
        margin.AddThemeConstantOverride("margin_top", 20);
        margin.AddThemeConstantOverride("margin_bottom", 20);
        AddChild(margin);

        var col = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        col.AddThemeConstantOverride("separation", 18);
        margin.AddChild(col);

        // --- Header ---
        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 12);
        var back = UiTheme.GhostButton("‹");
        back.CustomMinimumSize = new Vector2(64, 56);
        back.Pressed += () => _onBack?.Invoke();
        header.AddChild(back);
        var title = UiTheme.MakeLabel("MUSTER YOUR ARMY", UiTheme.HeadingSize, UiTheme.Text, HorizontalAlignment.Center);
        title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        header.AddChild(title);
        header.AddChild(new Control { CustomMinimumSize = new Vector2(64, 0) });
        col.AddChild(header);

        col.AddChild(UiTheme.Muted("Three pieces answer the call. Win battles, earn money, grow the army.", UiTheme.BodySmallSize));

        // --- Starter cards ---
        _cardRow = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        _cardRow.AddThemeConstantOverride("separation", 18);
        _cardRow.SizeFlagsVertical = SizeFlags.ExpandFill;
        col.AddChild(_cardRow);

        _purse = UiTheme.MakeLabel("", UiTheme.BodySize, UiTheme.Accent, HorizontalAlignment.Center);
        col.AddChild(_purse);

        // --- Footer buttons ---
        var footer = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        footer.AddThemeConstantOverride("separation", 20);
        var reroll = UiTheme.SecondaryButton("REROLL ARMY");
        reroll.CustomMinimumSize = new Vector2(260, 76);
        reroll.Pressed += () => { var run = _onReroll?.Invoke(); if (run != null) Present(run); };
        footer.AddChild(reroll);
        var begin = UiTheme.PrimaryButton("BEGIN RUN");
        begin.CustomMinimumSize = new Vector2(300, 80);
        begin.Pressed += () => _onBegin?.Invoke();
        footer.AddChild(begin);
        col.AddChild(footer);
    }

    public void Present(RunState run)
    {
        if (_cardRow == null) return;
        foreach (Node child in _cardRow.GetChildren())
            child.QueueFree();

        for (int i = 0; i < run.Army.Count; i++)
            _cardRow.AddChild(BuildCard(run.Army[i]));

        _purse.Text = $"Starting purse: ${run.Money}";
    }

    private static PanelContainer BuildCard(PieceKind kind)
    {
        var info = PieceCatalog.Info(kind);
        var card = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore };
        card.CustomMinimumSize = new Vector2(250, 220);
        card.AddThemeStyleboxOverride("panel",
            UiTheme.Box(UiTheme.PanelRaised, 14, 1, UiTheme.PanelBorder, 18, 18));

        var v = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, MouseFilter = MouseFilterEnum.Ignore };
        v.AddThemeConstantOverride("separation", 10);
        card.AddChild(v);

        var circle = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ShrinkCenter };
        circle.CustomMinimumSize = new Vector2(58, 58);
        circle.AddThemeStyleboxOverride("panel", UiTheme.Box(UiTheme.Panel, 29, 1, UiTheme.Border));
        var mono = UiTheme.MakeLabel(info.Monogram.ToUpperInvariant(), 24, UiTheme.Accent, HorizontalAlignment.Center);
        mono.VerticalAlignment = VerticalAlignment.Center;
        circle.AddChild(mono);
        v.AddChild(circle);

        v.AddChild(UiTheme.MakeLabel(info.Name, UiTheme.BodySize, UiTheme.Text, HorizontalAlignment.Center));

        var desc = UiTheme.MakeLabel(info.Description, 17, UiTheme.TextMuted, HorizontalAlignment.Center);
        desc.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        desc.CustomMinimumSize = new Vector2(210, 0);
        v.AddChild(desc);

        return card;
    }
}
