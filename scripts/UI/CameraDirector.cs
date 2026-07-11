// =============================================================================
// CameraDirector
// =============================================================================
// Purpose:
//   The stage camera's choreography, factored out of the screen state machine:
//   the slow attract drift behind menu screens, the restrained defeat shake,
//   and restoring the cached rest transform when a battle starts. Owns its
//   tweens (created on — and therefore bound to — the camera node) so killing
//   and cleanup live in one place.
//
// Interactions:
//   - ScreenManager: Drift() on Title/NewRun/Shop, Restore() on NextBattle,
//     Shake() on defeat, Cleanup() on exit.
// =============================================================================

using Godot;

namespace HexGame.UI;

public sealed class CameraDirector
{
    private readonly Camera3D _camera;
    private readonly Transform3D _rest;
    private Tween _driftTween;
    private Tween _shakeTween;

    public CameraDirector(Camera3D camera)
    {
        _camera = camera;
        _rest = camera.Transform;
    }

    // Slow attract sway behind the menu screens.
    public void Drift()
    {
        _driftTween?.Kill();
        var origin = _rest.Origin;
        _driftTween = _camera.CreateTween().SetLoops();
        _driftTween.TweenProperty(_camera, "position", origin + new Vector3(0.35f, 0f, 0.2f), 7.0f)
            .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        _driftTween.TweenProperty(_camera, "position", origin + new Vector3(-0.35f, 0f, -0.1f), 7.0f)
            .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
    }

    public void StopDrift()
    {
        _driftTween?.Kill();
        _driftTween = null;
    }

    // Snap back to the cached rest transform (battle framing).
    public void Restore()
    {
        _shakeTween?.Kill();
        _camera.Transform = _rest;
    }

    // Restrained defeat shake: a short decaying positional jitter, then snap
    // back to the rest position.
    public void Shake()
    {
        _shakeTween?.Kill();
        var o = _rest.Origin;
        _shakeTween = _camera.CreateTween();
        Vector3[] offs =
        {
            new(0.08f, -0.05f, 0f), new(-0.06f, 0.04f, 0f),
            new(0.04f, 0.03f, 0f), new(-0.02f, -0.02f, 0f),
        };
        foreach (var off in offs)
            _shakeTween.TweenProperty(_camera, "position", o + off, 0.06f).SetTrans(Tween.TransitionType.Sine);
        _shakeTween.TweenProperty(_camera, "position", o, 0.06f).SetTrans(Tween.TransitionType.Sine);
    }

    public void Cleanup()
    {
        _driftTween?.Kill();
        _shakeTween?.Kill();
    }
}
