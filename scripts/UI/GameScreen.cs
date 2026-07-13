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
            AmbientLightEnergy = 0.25f,   // a touch lower so carved forms keep contrast
            TonemapMode = Godot.Environment.ToneMapper.Filmic,
            TonemapExposure = 1.0f,
        };
        AddChild(new WorldEnvironment { Environment = env });

        var key = new DirectionalLight3D
        {
            LightColor = new Color(1.0f, 0.953f, 0.866f),   // warm key
            LightEnergy = 1.30f,
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

        // Board plinth + stage floor: lift the board out of the void so it reads
        // as a tournament set on a pedestal in a pool of light. The hexagonal
        // plinth's top sits flush under the tiles (which cast their grounding
        // shadow onto it); the vast matte floor catches the top-down spot as a
        // soft circle and fades into the background under the vignette.
        var floor = new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = new Vector2(60f, 60f) },
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.052f, 0.060f, 0.080f),
                Roughness = 0.95f,
                Metallic = 0.0f,
            },
            Position = new Vector3(0f, -0.40f, 0f),
        };
        AddChild(floor);

        var plinth = new MeshInstance3D
        {
            Mesh = new CylinderMesh
            {
                TopRadius = 5.7f,
                BottomRadius = 6.05f,
                Height = 0.6f,
                RadialSegments = 6,
            },
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.105f, 0.120f, 0.152f),
                Metallic = 0.25f,
                MetallicSpecular = 0.45f,
                Roughness = 0.42f,
                RimEnabled = true,
                Rim = 0.2f,
            },
            Position = new Vector3(0f, -0.385f, 0f),   // top ≈ -0.085, just under the tiles
            RotationDegrees = new Vector3(0f, 30f, 0f),
        };
        AddChild(plinth);

        // A cinematic ~48° oblique (was near-top-down): the carved army is a set
        // of figurines, and figurines only read from a three-quarter view — a
        // top-down camera flattens them to specks. Pulled back to ~11.9u with
        // FOV 50 so the radius-4 board (x ±4.3, z ±3.8 incl. tile geometry) still
        // clears the frame with margin (vertical board span ~28° inside the 50°
        // FOV; the vignette frames the slack). Verified by render, not by eye.
        var camera = new Camera3D
        {
            Projection = Camera3D.ProjectionType.Perspective,
            Fov = 50f,
            Current = true,
        };
        camera.Position = new Vector3(0f, 8.8f, 8.0f);
        AddChild(camera);
        camera.LookAt(Vector3.Zero, Vector3.Up);   // after entering tree (uses global xform)
        return camera;
    }
}
