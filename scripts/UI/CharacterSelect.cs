// =============================================================================
// CharacterSelect
// =============================================================================
// Purpose:
//   The "choose your piece" screen that replaces the old 640px toggle-button
//   panel: a 2x7 grid of token cards (gold monogram + name + per-token best
//   wave), a footer detail bar showing the selected piece's full move rule and
//   a START RUN button. Tapping a card previews the piece on the live board
//   behind; START commits the run. Monograms and copy come straight from
//   TokenCatalog so this never drifts from the catalog.
//
// Interactions:
//   - ScreenManager: constructs with onPreview(index) / onStart(index) /
//     onBack callbacks; calls Refresh() before showing.
//   - TokenCatalog: source of names + descriptions.
//   - GameSession: per-token best waves for the card pills.
// =============================================================================

using System;
using Godot;
using HexGame.Tokens;

namespace HexGame.UI;

public partial class CharacterSelect : Control
{
    private readonly GameSession _session;
    private readonly Action<int> _onPreview;
    private readonly Action<int> _onStart;
    private readonly Action _onBack;

    private readonly PanelContainer[] _cards = new PanelContainer[TokenCatalog.All.Length];
    private readonly Label[] _cardPills = new Label[TokenCatalog.All.Length];
    private int _selected = -1;

    private Label _detailName;
    private Label _detailDesc;
    private Label _detailBest;

    public CharacterSelect(GameSession session, Action<int> onPreview, Action<int> onStart, Action onBack)
    {
        _session = session;
        _onPreview = onPreview;
        _onStart = onStart;
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

        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 16);
        margin.AddChild(col);

        // --- Header ---
        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 12);
        var back = UiTheme.GhostButton("‹");
        back.CustomMinimumSize = new Vector2(64, 56);
        back.Pressed += () => _onBack?.Invoke();
        header.AddChild(back);
        var title = UiTheme.MakeLabel("CHOOSE YOUR PIECE", UiTheme.HeadingSize, UiTheme.Text, HorizontalAlignment.Center);
        title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        header.AddChild(title);
        header.AddChild(new Control { CustomMinimumSize = new Vector2(64, 0) }); // balance the back button
        col.AddChild(header);

        // --- Card grid (horizontally scrollable on narrow screens) ---
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Auto };
        scroll.VerticalScrollMode = ScrollContainer.ScrollMode.Disabled;
        scroll.SizeFlagsVertical = SizeFlags.ExpandFill;
        col.AddChild(scroll);

        var grid = new GridContainer { Columns = 7 };
        grid.AddThemeConstantOverride("h_separation", 16);
        grid.AddThemeConstantOverride("v_separation", 16);
        scroll.AddChild(grid);

        for (int i = 0; i < TokenCatalog.All.Length; i++)
            grid.AddChild(BuildCard(i));

        // --- Footer detail + START ---
        var footer = new PanelContainer();
        footer.AddThemeStyleboxOverride("panel", UiTheme.Box(UiTheme.Panel, 14, 1, UiTheme.PanelBorder, 24, 16));
        var footerRow = new HBoxContainer();
        footerRow.AddThemeConstantOverride("separation", 20);
        footer.AddChild(footerRow);

        var detail = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        detail.AddThemeConstantOverride("separation", 4);
        _detailName = UiTheme.MakeLabel("", UiTheme.HeadingSize, UiTheme.Accent);
        _detailDesc = UiTheme.MakeLabel("", UiTheme.BodySize, UiTheme.Text);
        _detailDesc.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _detailDesc.CustomMinimumSize = new Vector2(0, 88);   // room for 2-3 wrapped lines on narrow screens
        _detailBest = UiTheme.MakeLabel("", UiTheme.ChipSize, UiTheme.TextMuted);
        detail.AddChild(_detailName);
        detail.AddChild(_detailDesc);
        detail.AddChild(_detailBest);
        footerRow.AddChild(detail);

        var start = UiTheme.PrimaryButton("START RUN");
        start.CustomMinimumSize = new Vector2(280, 80);
        start.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        start.Pressed += () => { if (_selected >= 0) _onStart?.Invoke(_selected); };
        footerRow.AddChild(start);

        col.AddChild(footer);
    }

    private PanelContainer BuildCard(int index)
    {
        var info = TokenCatalog.All[index];
        var card = new PanelContainer { MouseFilter = MouseFilterEnum.Stop };
        card.CustomMinimumSize = new Vector2(150, 124);
        card.AddThemeStyleboxOverride("panel", CardStyle(false));

        var v = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        v.AddThemeConstantOverride("separation", 8);
        v.MouseFilter = MouseFilterEnum.Ignore;
        card.AddChild(v);

        // Monogram circle (first two letters of the name — stays in sync w/ catalog).
        var circle = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ShrinkCenter };
        circle.CustomMinimumSize = new Vector2(54, 54);
        circle.AddThemeStyleboxOverride("panel", UiTheme.Box(UiTheme.Panel, 27, 1, UiTheme.Border));
        circle.MouseFilter = MouseFilterEnum.Ignore;
        var mono = UiTheme.MakeLabel(Monogram(info.Name), 24, UiTheme.Accent, HorizontalAlignment.Center);
        mono.VerticalAlignment = VerticalAlignment.Center;
        mono.MouseFilter = MouseFilterEnum.Ignore;   // let taps fall through to the card's GuiInput
        circle.AddChild(mono);
        v.AddChild(circle);

        var name = UiTheme.MakeLabel(info.Name, UiTheme.BodySmallSize, UiTheme.Text, HorizontalAlignment.Center);
        name.MouseFilter = MouseFilterEnum.Ignore;
        v.AddChild(name);

        var pill = UiTheme.MakeLabel("—", 16, UiTheme.TextMuted, HorizontalAlignment.Center);
        pill.MouseFilter = MouseFilterEnum.Ignore;
        _cardPills[index] = pill;
        v.AddChild(pill);

        card.GuiInput += e => OnCardInput(index, e);
        _cards[index] = card;
        return card;
    }

    private static string Monogram(string name) =>
        name.Length >= 2 ? name.Substring(0, 2).ToUpperInvariant() : name.ToUpperInvariant();

    private void OnCardInput(int index, InputEvent e)
    {
        bool tapped = (e is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
                   || (e is InputEventScreenTouch st && st.Pressed);
        if (!tapped) return;
        Select(index);
        AcceptEvent();
    }

    private void Select(int index)
    {
        if (index < 0 || index >= _cards.Length) return;
        if (_selected >= 0 && _cards[_selected] != null)
            _cards[_selected].AddThemeStyleboxOverride("panel", CardStyle(false));
        _selected = index;
        var card = _cards[index];
        card.AddThemeStyleboxOverride("panel", CardStyle(true));

        // Card pop for tactile feedback.
        card.PivotOffset = card.Size / 2f;
        var pop = CreateTween();
        pop.TweenProperty(card, "scale", new Vector2(1.06f, 1.06f), 0.10f)
            .SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        pop.TweenProperty(card, "scale", Vector2.One, 0.10f)
            .SetTrans(Tween.TransitionType.Sine);

        var info = TokenCatalog.All[index];
        _detailName.Text = info.Name;
        _detailDesc.Text = info.Description;
        int best = _session.PerTokenBestWave[index];
        _detailBest.Text = best > 0 ? $"Best: Wave {best}" : "Best: —";

        _onPreview?.Invoke(index);
    }

    private static StyleBoxFlat CardStyle(bool selected) => selected
        ? UiTheme.Box(new Color(0.137f, 0.173f, 0.239f), 14, 2, UiTheme.Accent, 12, 12)
        : UiTheme.Box(UiTheme.PanelRaised, 14, 1, UiTheme.PanelBorder, 12, 12);

    // Update pills + select the session's current piece, scrolling it into view.
    public void Refresh()
    {
        for (int i = 0; i < _cardPills.Length; i++)
        {
            int best = _session.PerTokenBestWave[i];
            if (_cardPills[i] != null) _cardPills[i].Text = best > 0 ? $"W{best}" : "—";
        }
        Select(Mathf.Clamp(_session.SelectedTokenIndex, 0, _cards.Length - 1));
    }
}
