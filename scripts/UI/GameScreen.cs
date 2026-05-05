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
//     SetToken whenever the player picks a different piece.
//   - TokenCatalog: enumerated to build the token list and re-instantiate
//     the chosen token for the board.
//   - Token: factory output from TokenCatalog; reads DisplayName and
//     Description for the UI button and label.
//   - GameSession: looked up at /root/GameSession to persist the player's
//     SelectedTokenIndex for the session.
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
        GetViewport().PhysicsObjectPicking = true;

        _board = GetNode<HexBoard>(BoardPath);
        _description = GetNode<Label>(DescriptionPath);
        var list = GetNode<VBoxContainer>(TokenListPath);

        for (int i = 0; i < TokenCatalog.All.Count; i++)
        {
            var preview = TokenCatalog.All[i]();
            var index = i;
            var description = preview.Description;
            var button = new Button
            {
                Text = preview.DisplayName,
                CustomMinimumSize = new Vector2(0, 40),
                ToggleMode = true,
                Alignment = HorizontalAlignment.Left,
            };
            button.Pressed += () => OnPickToken(index, button, description);
            list.AddChild(button);
        }

        GD.Print($"[GameScreen] _Ready done, camera={GetViewport().GetCamera3D()?.Name}, viewport={GetViewport().GetVisibleRect().Size}");
    }

    private void OnPickToken(int index, Button button, string description)
    {
        var session = GetNode<GameSession>("/root/GameSession");
        session.SelectedTokenIndex = index;
        _board.SetToken(TokenCatalog.All[index]());
        _description.Text = description;

        if (_selectedButton != null && _selectedButton != button)
            _selectedButton.ButtonPressed = false;
        button.ButtonPressed = true;
        _selectedButton = button;
    }
}
