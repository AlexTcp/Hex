// =============================================================================
// ShopScreen
// =============================================================================
// Purpose:
//   The between-battles shop: money readout, four procedural offer cards (two
//   pieces, one unowned Gambit, one tile upgrade), a paid REROLL and CONTINUE.
//   Purchases mutate the RunState directly (pieces join the army — or the
//   reserve when the army is full; gambits register in run.Gambits; tile
//   upgrades claim a random un-upgraded coord near the board centre). Sold
//   cards grey out; unaffordable buy buttons disable on every money change.
//
// Interactions:
//   - ScreenManager: constructs with onContinue; calls Present(run, battle)
//     before showing.
//   - PieceCatalog / GambitCatalog / TileUpgradeCatalog: offer data.
// =============================================================================

using System;
using System.Collections.Generic;
using Godot;
using HexGame.Chess;
using HexGame.Hex;

namespace HexGame.UI;

public partial class ShopScreen : Control
{
    private const int RerollPrice = 2;

    private readonly Action _onContinue;
    private readonly Random _rng = new();

    private RunState _run;
    private Label _money;
    private Label _heading;
    private HBoxContainer _cardRow;
    private Button _reroll;

    private readonly List<(Button Buy, int Price)> _buyButtons = new();

    public ShopScreen(Action onContinue)
    {
        _onContinue = onContinue;
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
        col.AddThemeConstantOverride("separation", 16);
        margin.AddChild(col);

        _heading = UiTheme.MakeLabel("THE EXCHEQUER", UiTheme.HeadingSize, UiTheme.Text, HorizontalAlignment.Center);
        col.AddChild(_heading);

        _money = UiTheme.MakeLabel("", UiTheme.HudPrimarySize, UiTheme.Accent, HorizontalAlignment.Center);
        col.AddChild(_money);

        _cardRow = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        _cardRow.AddThemeConstantOverride("separation", 16);
        _cardRow.SizeFlagsVertical = SizeFlags.ExpandFill;
        col.AddChild(_cardRow);

        var footer = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        footer.AddThemeConstantOverride("separation", 20);
        _reroll = UiTheme.SecondaryButton($"REROLL  ${RerollPrice}");
        _reroll.CustomMinimumSize = new Vector2(240, 76);
        _reroll.Pressed += OnReroll;
        footer.AddChild(_reroll);
        var cont = UiTheme.PrimaryButton("NEXT BATTLE");
        cont.CustomMinimumSize = new Vector2(300, 80);
        cont.Pressed += () => _onContinue?.Invoke();
        footer.AddChild(cont);
        col.AddChild(footer);
    }

    public void Present(RunState run)
    {
        _run = run;
        bool bossNext = RunState.IsBossBattle(run.Battle);
        _heading.Text = bossNext ? "THE EXCHEQUER — A BOSS AWAITS" : "THE EXCHEQUER";
        BuildOffers();
        RefreshMoney();
    }

    private void OnReroll()
    {
        if (_run == null || _run.Money < RerollPrice) return;
        _run.Money -= RerollPrice;
        BuildOffers();
        RefreshMoney();
    }

    private void RefreshMoney()
    {
        if (_run == null) return;
        _money.Text = $"${_run.Money}   ·   army {_run.Army.Count}   reserve {_run.Reserve.Count}";
        for (int i = 0; i < _buyButtons.Count; i++)
        {
            var (buy, price) = _buyButtons[i];
            if (IsInstanceValid(buy) && buy.Text != "SOLD")
                buy.Disabled = _run.Money < price;
        }
        _reroll.Disabled = _run.Money < RerollPrice;
    }

    // ----- Offers ------------------------------------------------------------

    private void BuildOffers()
    {
        foreach (Node child in _cardRow.GetChildren())
            child.QueueFree();
        _buyButtons.Clear();

        // Two pieces (stronger kinds appear as the run progresses).
        for (int i = 0; i < 2; i++)
        {
            var kind = RollPieceOffer();
            var info = PieceCatalog.Info(kind);
            AddOffer("PIECE", info.Name, info.Description, info.Price, () =>
            {
                if (_run.Army.Count < RunState.ArmyCap) _run.Army.Add(kind);
                else _run.Reserve.Add(kind);
            });
        }

        // One unowned gambit (if any remain).
        var gambit = RollGambitOffer();
        if (gambit.HasValue)
        {
            var g = gambit.Value;
            AddOffer("GAMBIT", g.Name, g.Description, g.Price, () => _run.Gambits.Add(g.Kind));
        }

        // One tile upgrade, assigned a random un-upgraded coord near the centre.
        var up = TileUpgradeCatalog.All[_rng.Next(TileUpgradeCatalog.All.Length)];
        var coord = RollUpgradeCoord();
        if (coord.HasValue)
        {
            var c = coord.Value;
            AddOffer("TILE", up.Name, $"{up.Description}\nClaims hex ({c.Q},{c.R}).", up.Price,
                () => _run.TileUpgrades[c] = up.Kind);
        }
    }

    private PieceKind RollPieceOffer()
    {
        int battle = _run.Battle;
        for (int attempt = 0; attempt < 8; attempt++)
        {
            var kind = (PieceKind)_rng.Next(6);
            if (kind == PieceKind.Queen && battle < 6) continue;
            return kind;
        }
        return PieceKind.Pawn;
    }

    private GambitInfo? RollGambitOffer()
    {
        // Collect unowned gambits; pick one at random.
        int unowned = 0;
        foreach (var g in GambitCatalog.All)
            if (!_run.Gambits.Contains(g.Kind)) unowned++;
        if (unowned == 0) return null;
        int pick = _rng.Next(unowned);
        foreach (var g in GambitCatalog.All)
        {
            if (_run.Gambits.Contains(g.Kind)) continue;
            if (pick-- == 0) return g;
        }
        return null;
    }

    private HexCoord? RollUpgradeCoord()
    {
        // Any un-upgraded hex within radius 2 — central enough to matter in
        // every battle size, and safe from all but the deepest crumble.
        var candidates = new List<HexCoord>();
        HexCoord.Within(2, candidates);
        candidates.RemoveAll(c => _run.TileUpgrades.ContainsKey(c));
        if (candidates.Count == 0) return null;
        return candidates[_rng.Next(candidates.Count)];
    }

    private void AddOffer(string tag, string name, string desc, int price, Action apply)
    {
        var card = new PanelContainer();
        card.CustomMinimumSize = new Vector2(250, 300);
        card.AddThemeStyleboxOverride("panel",
            UiTheme.Box(UiTheme.PanelRaised, 14, 1, UiTheme.PanelBorder, 16, 16));

        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", 8);
        card.AddChild(v);

        v.AddChild(UiTheme.MakeLabel(tag, UiTheme.ChipSize, UiTheme.TextMuted, HorizontalAlignment.Center));
        v.AddChild(UiTheme.MakeLabel(name, UiTheme.BodySize, UiTheme.Accent, HorizontalAlignment.Center));

        var d = UiTheme.MakeLabel(desc, 17, UiTheme.Text, HorizontalAlignment.Center);
        d.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        d.CustomMinimumSize = new Vector2(210, 0);
        d.SizeFlagsVertical = SizeFlags.ExpandFill;
        v.AddChild(d);

        var buy = UiTheme.PrimaryButton($"BUY  ${price}");
        buy.CustomMinimumSize = new Vector2(0, 64);
        buy.Pressed += () =>
        {
            if (_run.Money < price || buy.Disabled) return;
            _run.Money -= price;
            apply();
            buy.Text = "SOLD";
            buy.Disabled = true;
            RefreshMoney();
        };
        _buyButtons.Add((buy, price));
        v.AddChild(buy);

        _cardRow.AddChild(card);
    }
}
