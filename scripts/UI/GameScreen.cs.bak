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

    public override void _Ready()
    {
        SetGameplayActive(!DebugLog.IsAnyModalOpen);
        DebugLog.GameplayActiveChanged += SetGameplayActive;

        _board = GetNode<HexBoard>(BoardPath);
        _description = GetNode<Label>(DescriptionPath);
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

        GD.Print($"[GameScreen] _Ready done, camera={GetViewport().GetCamera3D()?.Name}, viewport={GetViewport().GetVisibleRect().Size}");
    }

    public override void _ExitTree()
    {
        DebugLog.GameplayActiveChanged -= SetGameplayActive;
    }

    public void SetGameplayActive(bool active)
    {
        var viewport = GetViewport();
        if (viewport != null) viewport.PhysicsObjectPicking = active;
        GD.Print($"[DIAG-GATE] active={active} anyModalOpen={DebugLog.IsAnyModalOpen} picking={viewport?.PhysicsObjectPicking}");
    }

    private void OnPickToken(int index, Button button, string description)
    {
        var session = GetNode<GameSession>("/root/GameSession");
        session.SelectedTokenIndex = index;
        _board.SetToken(index);
        _description.Text = description;

        if (_selectedButton != null && _selectedButton != button)
            _selectedButton.ButtonPressed = false;
        button.ButtonPressed = true;
        _selectedButton = button;
    }
}
