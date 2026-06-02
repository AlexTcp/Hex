// =============================================================================
// GameScreen
// =============================================================================
// Purpose:
//   Top-level Node3D for the in-game scene. Wires together the 3D HexBoard
//   with a side panel of token-pick buttons: on _Ready it iterates
//   TokenCatalog to populate a VBoxContainer with one toggle Button per
//   token, displaying its name. When the player picks a token, GameScreen
//   updates the GameSession's SelectedTokenIndex, swaps the board's active
//   piece, and shows that token's description in the UI Label.
//
// Interactions:
//   - HexBoard: fetched via GetNode<HexBoard>(BoardPath) and updated with
//     SetToken(int index) whenever the player picks a different piece.
//   - TokenCatalog: TokenInfo array iterated to build the picker UI without
//     instantiating tokens.
//   - GameSession: looked up at /root/GameSession to persist the player's
//     SelectedTokenIndex for the session.
//   - DebugLog: subscribes to GameplayActiveChanged to gate viewport
//     PhysicsObjectPicking on/off as modals open and close.
// =============================================================================

using Godot;
using HexGame.Board;
using HexGame.Tokens;

namespace HexGame.UI;

public partial class GameScreen : Node3D
{
    [Export] public NodePath BoardPath;
    [Export] public NodePath TokenListPath;
    [Export] public NodePath DescriptionPath;

    private HexBoard _board;
    private Label _description;
    private Button _selectedButton;
    private GameSession _session;
    private Label _hudLabel;
    private int _remaining;
    private int _wave = 1;
    private ulong _flashUntilMs;

    public override void _Ready()
    {
        SetGameplayActive(!DebugLog.IsAnyModalOpen);
        DebugLog.GameplayActiveChanged += SetGameplayActive;

        _board = GetNode<HexBoard>(BoardPath);
        _description = GetNode<Label>(DescriptionPath);
        _session = GetNode<GameSession>("/root/GameSession");
        var list = GetNode<VBoxContainer>(TokenListPath);
        var scroll = list.GetParent<ScrollContainer>();
        scroll.GetVScrollBar().CustomMinimumSize = new Vector2(24, 0);

        for (int i = 0; i < TokenCatalog.All.Length; i++)
        {
            var info = TokenCatalog.All[i];
            var index = i;
            var description = info.Description;
            var button = new Button
            {
                Text = info.Name,
                CustomMinimumSize = new Vector2(0, 80),
                ToggleMode = true,
                Alignment = HorizontalAlignment.Left,
            };
            button.AddThemeFontSizeOverride("font_size", 32);
            button.Pressed += () => OnPickToken(index, button, description);
            list.AddChild(button);
        }

        BuildHud();
        BuildReachToggle();
        _board.EnemiesChanged += OnEnemiesChanged;
        _board.BoardSolved += OnBoardSolved;
        _board.WaveChanged += OnWaveChanged;
        _board.ComboChanged += OnComboChanged;

        GD.Print($"[GameScreen] _Ready done, camera={GetViewport().GetCamera3D()?.Name}, viewport={GetViewport().GetVisibleRect().Size}");
    }

    private void BuildHud()
    {
        var uiRoot = GetNode<Control>("UI/Root");
        _hudLabel = new Label
        {
            Text = "",
            MouseFilter = Control.MouseFilterEnum.Ignore,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        _hudLabel.SetAnchorsPreset(Control.LayoutPreset.TopWide);
        _hudLabel.OffsetTop = 24;
        _hudLabel.OffsetBottom = 88;
        _hudLabel.AddThemeFontSizeOverride("font_size", 36);
        _hudLabel.AddThemeColorOverride("font_color", new Color(1f, 0.95f, 0.8f));
        _hudLabel.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.85f));
        _hudLabel.AddThemeConstantOverride("shadow_outline_size", 6);
        uiRoot.AddChild(_hudLabel);
    }

    private string HudText() => _remaining > 0 ? $"Wave {_wave}   Enemies: {_remaining}" : "Cleared!";

    private void OnEnemiesChanged(int remaining)
    {
        _remaining = remaining;
        if (Time.GetTicksMsec() < _flashUntilMs) return;
        _hudLabel.Text = HudText();
    }

    private void OnWaveChanged(int wave)
    {
        _wave = wave;
        if (Time.GetTicksMsec() < _flashUntilMs) return;
        _hudLabel.Text = HudText();
    }

    private void OnComboChanged(int combo)
    {
        _hudLabel.Text = $"Combo x{combo}!";
        _flashUntilMs = Time.GetTicksMsec() + 700;
        FlashPop(1.3f, 0.12f);

        var timer = GetTree().CreateTimer(0.7);
        timer.Timeout += () =>
        {
            if (!IsInstanceValid(_hudLabel)) return;
            if (Time.GetTicksMsec() < _flashUntilMs) return;   // a newer flash superseded this one
            _flashUntilMs = 0;
            _hudLabel.Text = HudText();
        };
    }

    private void OnBoardSolved()
    {
        _hudLabel.Text = "Cleared!";
        _flashUntilMs = Time.GetTicksMsec() + 900;
        FlashPop(1.35f, 0.14f);

        var timer = GetTree().CreateTimer(0.9);
        timer.Timeout += () =>
        {
            if (!IsInstanceValid(_hudLabel)) return;
            if (Time.GetTicksMsec() < _flashUntilMs) return;   // a newer flash superseded this one
            _flashUntilMs = 0;
            _hudLabel.Text = HudText();
        };
    }

    private void FlashPop(float scale, float upDuration)
    {
        _hudLabel.PivotOffset = _hudLabel.Size / 2f;
        var pop = CreateTween();
        pop.TweenProperty(_hudLabel, "scale", new Vector2(scale, scale), upDuration)
            .SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        pop.TweenProperty(_hudLabel, "scale", Vector2.One, 0.22f)
            .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
    }

    private void BuildReachToggle()
    {
        var uiRoot = GetNode<Control>("UI/Root");
        var btn = new CheckButton { Text = "Reach" };
        btn.SetAnchorsPreset(Control.LayoutPreset.CenterRight);
        btn.GrowHorizontal = Control.GrowDirection.Begin;
        btn.OffsetRight = -24;
        btn.AddThemeFontSizeOverride("font_size", 26);
        btn.Toggled += on => _board.SetShowReach(on);
        uiRoot.AddChild(btn);
    }

    public override void _ExitTree()
    {
        DebugLog.GameplayActiveChanged -= SetGameplayActive;
        if (_board != null)
        {
            _board.EnemiesChanged -= OnEnemiesChanged;
            _board.BoardSolved -= OnBoardSolved;
            _board.WaveChanged -= OnWaveChanged;
            _board.ComboChanged -= OnComboChanged;
        }
    }

    public void SetGameplayActive(bool active)
    {
        var viewport = GetViewport();
        if (viewport != null) viewport.PhysicsObjectPicking = active;
        GD.Print($"[DIAG-GATE] active={active} anyModalOpen={DebugLog.IsAnyModalOpen} picking={viewport?.PhysicsObjectPicking}");
    }

    private void OnPickToken(int index, Button button, string description)
    {
        _session.SelectedTokenIndex = index;
        _board.SetToken(index);
        _description.Text = description;

        if (_selectedButton != null && _selectedButton != button)
            _selectedButton.ButtonPressed = false;
        button.ButtonPressed = true;
        _selectedButton = button;
    }
}
