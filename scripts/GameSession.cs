// =============================================================================
// GameSession
// =============================================================================
// Purpose:
//   Lightweight autoload Node that holds per-session game state shared
//   across scene transitions. Currently tracks which token the player has
//   chosen (SelectedTokenIndex) so screens like GameScreen can read the
//   selection when constructing the board.
//
// Interactions:
//   - (none — self-contained; consumed by other scripts that read the
//     selected index, but does not itself reference other project classes)
// =============================================================================

using Godot;

namespace HexGame;

public partial class GameSession : Node
{
    public int SelectedTokenIndex { get; set; } = 0;
}
