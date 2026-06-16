// =============================================================================
// GameScreen
// =============================================================================
// Purpose:
//   Top-level Node3D bootstrap for the single persistent scene. Builds the
//   Premium Slate 3D stage in code (WorldEnvironment, warm key + cool fill +
//   central soft spotlight, and a slightly-tilted perspective camera), then
//   constructs the ScreenManager and hands it the board, camera, session and
//   UI root. All gameplay UI is owned by ScreenManager from there.
//
//   Lighting/camera are built in code (not the .tscn) so the exact C# values
//   are typechecked; framing may want a small on-device tweak in the editor.
//
// Interactions:
//   - HexBoard: fetched via BoardPath; driven by ScreenManager.
//   - ScreenManager: created here and given the stage refs.
//   - GameSession: the /root autoload, passed through to ScreenManager.
// =============================================================================

using Godot;
using HexGame.Board;

namespace HexGame.UI;

public partial class GameScreen : Node3D
{
    [Export] public NodePath BoardPath;

    private HexBoard _board;
    private ScreenManager _screens;

    public override void _Ready()
    {
        _board = GetNode<HexBoard>(BoardPath);
        var root = GetNode<Control>("UI/Root");
        var session = GetNode<GameSession>("/root/GameSession");

        var camera = BuildStage();

        _screens = new ScreenManager(_board, camera, session, root);
        AddChild(_screens);

#if DEBUG
        GD.Print($"[GameScreen] _Ready done, camera={camera.Name}, viewport={GetViewport().GetVisibleRect().Size}");
#endif
    }

    // Premium Slate stage, all Compatibility-renderer safe (no glow/SSAO/SSR):
    // dark slate background + cool flat ambient + Filmic tonemap; a warm key
    // light that casts the grounding shadow, a cool fill that lifts shadowed
    // faces, and a soft central spotlight that focuses the board and pairs with
    // the vignette.
    private Camera3D BuildStage()
    {
        var env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = new Color(0.055f, 0.063f, 0.082f),
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color(0.165f, 0.196f, 0.259f),
            AmbientLightEnergy = 0.30f,
            TonemapMode = Godot.Environment.ToneMapper.Filmic,
            TonemapExposure = 1.0f,
        };
        AddChild(new WorldEnvironment { Environment = env });

        var key = new DirectionalLight3D
        {
            LightColor = new Color(1.0f, 0.957f, 0.878f),   // warm key
            LightEnergy = 1.15f,
            ShadowEnabled = true,
        };
        key.RotationDegrees = new Vector3(-58f, 28f, 0f);
        AddChild(key);

        var fill = new DirectionalLight3D
        {
            LightColor = new Color(0.725f, 0.776f, 0.878f), // cool fill
            LightEnergy = 0.35f,
            ShadowEnabled = false,
        };
        fill.RotationDegrees = new Vector3(-22f, -140f, 0f);
        AddChild(fill);

        var spot = new SpotLight3D
        {
            LightColor = new Color(1f, 1f, 1f),
            LightEnergy = 1.4f,
            SpotRange = 13f,
            SpotAngle = 34f,
            SpotAngleAttenuation = 1.8f,
            ShadowEnabled = false,
        };
        spot.Position = new Vector3(0f, 7f, 0f);
        spot.RotationDegrees = new Vector3(-90f, 0f, 0f);  // straight down at board centre
        AddChild(spot);

        // Framing is biased for SAFE MARGIN (the one thing not verifiable without a
        // render): pulled back to ~10.9u with FOV 46 so the radius-4 board's near
        // edge (~z 3.8 incl. tile geometry, ~21° off-axis) clears the ~23° half-FOV
        // with room to spare; the vignette frames the slack. Nudge live in-editor for
        // a tighter fit if desired.
        var camera = new Camera3D
        {
            Projection = Camera3D.ProjectionType.Perspective,
            Fov = 46f,
            Current = true,
        };
        camera.Position = new Vector3(0f, 10.5f, 3.0f);
        AddChild(camera);
        camera.LookAt(Vector3.Zero, Vector3.Up);   // after entering tree (uses global xform)
        return camera;
    }
}
