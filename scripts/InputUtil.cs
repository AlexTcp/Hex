// =============================================================================
// InputUtil
// =============================================================================
// Purpose:
//   Tiny shared input helpers. IsPrimaryPress recognises the "dismiss" gesture
//   (a left mouse-button press or a screen touch-down) that the scrim/dim
//   overlays of the settings drawer and the log modal both react to — previously
//   the same detection was hand-copied byte-for-byte into each modal.
//
// Interactions:
//   - SettingsModal.OnScrimInput, DebugModal.OnDimInput.
// =============================================================================

using Godot;

namespace HexGame;

public static class InputUtil
{
    // True for a primary press-down: the left mouse button, or a screen touch.
    public static bool IsPrimaryPress(InputEvent ev) =>
        (ev is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
        || (ev is InputEventScreenTouch st && st.Pressed);
}
