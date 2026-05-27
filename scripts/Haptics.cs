// =============================================================================
// Haptics
// =============================================================================
// Purpose:
//   Tiny static helper for short haptic feedback on touch devices. Wraps
//   Input.VibrateHandheld behind an OS.HasFeature("mobile") guard so calls are
//   safe no-ops on desktop. Used by HexBoard to give a tactile tick on moves
//   and a stronger one on captures.
//
// Interactions:
//   - HexBoard: calls Haptics.Tap on every committed move / capture.
// =============================================================================

using Godot;

namespace HexGame;

public static class Haptics
{
    public static void Tap(int milliseconds)
    {
        if (OS.HasFeature("mobile"))
            Input.VibrateHandheld(milliseconds);
    }
}
