// =============================================================================
// HexBoard
// =============================================================================
// Purpose:
//   Partial Node3D that builds and runs the hexagonal game board: the radius-4
//   tile grid, the single player Token, and the wave-based hunter system that
//   gives the game its stakes. Handles tile pick/tap input, computes legal-move
//   highlights (with per-tile DANGER marking that forewarns the player of any
//   move that would get them caught), tweens the token, resolves captures,
//   advances hunters, and detects the lose condition.
//
//   Premium Slate look: brushed-slate tiles, polished-metal/oxblood enemies,
//   a gold "this is you" base ring + gold selection material on the player
//   piece, a recoloured landing shockwave, and a pooled CPU-particle capture
//   burst — all using shared static GPU resources (Compatibility-renderer safe;
//   no glow/bloom, no GPU particles).
//
// Fail-state & fairness (see the design spec):
//   - Chase hunters MAY step onto the player's tile -> PlayerCaught -> game over.
//   - Flee / RandomWalk hunters never target the player (non-lethal flavour).
//   - A candidate move tile is a DEATH tile iff a non-grace Chase hunter is
//     adjacent to it (Chase minimises distance-to-player; the player's tile is
//     distance 0, a strict minimum that no other hunter can block). This is an
//     exact, zero-alloc Distance==1 test — no RNG, no per-candidate cloning.
//   - Freshly spawned hunters get a one-turn grace (SpawnedThisWave) so a wave
//     can never spawn-kill, and wave 1 is all-RandomWalk (un-losable teaching).
//   - Mercy rule: if EVERY legal move is a death tile, Chase steps are
//     suppressed for that one resolution so the player always has an out.
//
// Interactions:
//   - HexCoord / HexLayout: tile coords + world placement.
//   - Token: the active piece; queried via LegalMoves; gold material swap.
//   - The screen controller listens to PlayerCaught / ScoreChanged / WaveChanged
//     / EnemiesChanged / ComboChanged / BoardSolved / ThreatChanged to drive the
//     HUD and Game-Over flow; it calls StartRun (commit) and SetToken (preview).
// =============================================================================

using System.Collections.Generic;
using Godot;
using HexGame.Hex;
using HexGame.Tokens;

namespace HexGame.Board;

public partial class HexBoard : Node3D
{
    public enum EnemyBehavior { RandomWalk, Flee, Chase }

    [Export] public int Radius = 4;

    // Spawn ramp: wave 1 = BaseSpawn, +1 per wave, hard-capped so 61 tiles never choke.
    private const int BaseSpawn = 3;
    private const int MaxSpawn = 10;

    private const float TokenY = 0.35f;
    private const float RingY = 0.09f;

    [Signal] public delegate void EnemiesChangedEventHandler(int remaining);
    [Signal] public delegate void BoardSolvedEventHandler();
    [Signal] public delegate void WaveChangedEventHandler(int wave);
    [Signal] public delegate void ComboChangedEventHandler(int combo);
    [Signal] public delegate void ScoreChangedEventHandler(int score);
    [Signal] public delegate void PlayerCaughtEventHandler();
    [Signal] public delegate void ThreatChangedEventHandler(bool inDanger);

    private readonly Dictionary<HexCoord, Tile> _tiles = new();
    private Token _token;
    private HexCoord _tokenPos = HexCoord.Zero;
    private bool _selected = false;
    private bool _running = false;            // true only during an active run (not preview)
    private readonly HashSet<HexCoord> _highlighted = new();
    private readonly List<HexCoord> _movesBuffer = new(64);
    private Token _lastSelectedToken;
    private HexCoord _lastSelectedPos;
    private bool _selectionValid = false;

    // Run state.
    private int _wave = 1;
    private int _comboCount = 0;             // consecutive capturing moves
    private int _score = 0;
    private bool _mercyThisTurn = false;     // set in BeginSelect, consumed in AdvanceEnemies
    private bool _inDanger = false;          // last-emitted threat state (ambient HUD cue)

    // Selection juice: one looped tween pulses every shared highlight material at once.
    private Tween _pulseTween;

    // Telegraph: pooled ghost markers previewing deterministic (Chase/Flee) hunter steps.
    private readonly List<MeshInstance3D> _telegraphNodes = new();

    // Landing shockwave ring + gold "this is you" base ring + capture spark burst:
    // each a single pooled node, re-triggered/repositioned rather than re-allocated.
    private MeshInstance3D _ringNode;
    private Tween _ringTween;
    private MeshInstance3D _activeRingNode;
    private CpuParticles3D _captureParticles;

    // Reachable-reach overlay: optional faint marking of every BFS-reachable tile.
    private bool _showReach = false;
    private readonly HashSet<HexCoord> _reachMarked = new();

    // Hunters. Separate mesh nodes (not tile swaps) so they tween/pop freely.
    private readonly List<Enemy> _enemies = new();
    private readonly System.Random _rng = new();
    private readonly HashSet<HexCoord> _reachable = new();
    private readonly Queue<HexCoord> _bfsQueue = new();
    private readonly List<HexCoord> _bfsMoves = new(64);
    private readonly List<HexCoord> _spawnScratch = new(64);
    private readonly List<HexCoord> _neighborScratch = new(6);
    private readonly List<EnemyBehavior> _behaviorScratch = new(MaxSpawn);

    // ----- Premium Slate palette (Compatibility-safe: no glow; emissive only) -----
    private static readonly Color TileColorA = new(0.165f, 0.180f, 0.212f);   // slate dark
    private static readonly Color TileColorB = new(0.212f, 0.231f, 0.271f);   // slate mid
    private static readonly Color TileColorC = new(0.263f, 0.286f, 0.337f);   // slate light
    private static readonly Color GoldColor = new(0.890f, 0.698f, 0.235f);    // accent gold
    private static readonly Color GoldRimColor = new(0.957f, 0.824f, 0.478f);
    private static readonly Color CopperColor = new(0.851f, 0.384f, 0.180f);  // capture
    private static readonly Color DangerColor = new(0.900f, 0.150f, 0.150f);  // death tile
    private static readonly Color OxbloodColor = new(0.620f, 0.169f, 0.169f); // hunter
    private static readonly Color ReachColor = new(0.30f, 0.45f, 0.52f);

    private static StandardMaterial3D Slate(Color albedo, float roughness) => new()
    {
        AlbedoColor = albedo,
        Metallic = 0.10f,
        MetallicSpecular = 0.5f,
        Roughness = roughness,
        RimEnabled = true,
        Rim = 0.18f,
        RimTint = 0.4f,
    };

    private static readonly StandardMaterial3D TileMaterialA = Slate(TileColorA, 0.62f);
    private static readonly StandardMaterial3D TileMaterialB = Slate(TileColorB, 0.58f);
    private static readonly StandardMaterial3D TileMaterialC = Slate(TileColorC, 0.55f);

    private static StandardMaterial3D Emissive(Color albedo, float emit) => new()
    {
        AlbedoColor = albedo,
        Emission = albedo * emit,
        EmissionEnabled = true,
        Roughness = 0.5f,
        Metallic = 0.2f,
    };

    private static readonly StandardMaterial3D HighlightMaterialShared = Emissive(GoldColor, 0.55f);
    private static readonly StandardMaterial3D CaptureHighlightMaterialShared = Emissive(CopperColor, 0.7f);
    private static readonly StandardMaterial3D DangerHighlightMaterialShared = Emissive(DangerColor, 0.7f);

    private static readonly StandardMaterial3D ReachMaterialShared = new()
    {
        AlbedoColor = ReachColor,
        Emission = ReachColor * 0.22f,
        EmissionEnabled = true,
        Roughness = 0.7f,
    };

    // The player piece glows gold while a move is being chosen (swapped onto the
    // token's mesh; restored to its identity metal on deselect).
    private static readonly StandardMaterial3D SelectedTokenGoldMaterial = new()
    {
        AlbedoColor = GoldColor,
        Metallic = 0.9f,
        MetallicSpecular = 0.65f,
        Roughness = 0.22f,
        RimEnabled = true,
        Rim = 0.35f,
        RimTint = 0.6f,
        Emission = GoldColor * 0.3f,
        EmissionEnabled = true,
    };

    // Telegraph marker: small unshaded translucent disc shared by every ghost node.
    private static readonly Mesh SharedTelegraphMesh = new CylinderMesh
    {
        TopRadius = HexLayout.TileSize * 0.4f,
        BottomRadius = HexLayout.TileSize * 0.4f,
        Height = 0.04f,
        RadialSegments = 6,
    };
    private static readonly StandardMaterial3D TelegraphMaterialShared = new()
    {
        AlbedoColor = new Color(0.85f, 0.3f, 0.25f, 0.4f),
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
    };

    // Landing shockwave ring: one thin flat torus, scaled up + faded on each move.
    private static readonly Color RingGold = new(0.890f, 0.698f, 0.235f, 0.8f);
    private static readonly Mesh SharedRingMesh = new TorusMesh
    {
        InnerRadius = HexLayout.TileSize * 0.5f,
        OuterRadius = HexLayout.TileSize * 0.6f,
        RingSegments = 6,
    };
    private static readonly StandardMaterial3D RingMaterialShared = new()
    {
        AlbedoColor = RingGold,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
    };

    // Gold "this is you" base ring under the active piece (always present in a run).
    private static readonly Mesh SharedActiveRingMesh = new TorusMesh
    {
        InnerRadius = HexLayout.TileSize * 0.42f,
        OuterRadius = HexLayout.TileSize * 0.5f,
        RingSegments = 6,
    };
    private static readonly StandardMaterial3D ActiveRingMaterialShared = new()
    {
        AlbedoColor = new Color(0.890f, 0.698f, 0.235f, 0.9f),
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
    };

    // Capture spark burst (CPU particles — GL Compatibility safe).
    private static readonly Mesh SharedSparkMesh = new SphereMesh { Radius = 0.045f, Height = 0.09f };
    private static readonly StandardMaterial3D SparkMaterialShared = new()
    {
        AlbedoColor = CopperColor,
        Emission = CopperColor * 0.6f,
        EmissionEnabled = true,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
    };

    private static readonly StandardMaterial3D[] TileMaterialsByChecker =
    {
        TileMaterialA,
        TileMaterialB,
        TileMaterialC,
    };

    private static readonly CylinderMesh SharedTileMesh = new()
    {
        TopRadius = HexLayout.TileSize * 0.95f,
        BottomRadius = HexLayout.TileSize * 0.95f,
        Height = 0.15f,
        RadialSegments = 6,          // KEEP 6 — this is a hex grid, not a dodecagon
    };
    private static readonly CylinderShape3D SharedTileShape = new()
    {
        Radius = HexLayout.TileSize * 0.95f,
        Height = 0.15f,
    };

    // Every hunter shares one mesh + one oxblood polished-metal material.
    private static readonly Mesh SharedEnemyMesh = new SphereMesh { Radius = 0.2f, Height = 0.4f };
    private static readonly StandardMaterial3D EnemyMaterial = new()
    {
        AlbedoColor = OxbloodColor,
        Metallic = 0.7f,
        MetallicSpecular = 0.5f,
        Roughness = 0.38f,
        RimEnabled = true,
        Rim = 0.3f,
        RimTint = 0.4f,
    };

    private sealed class Enemy
    {
        public HexCoord Coord;
        public MeshInstance3D Node;
        public EnemyBehavior Behavior;
        public bool SpawnedThisWave;
    }

    private sealed class Tile
    {
        public HexCoord Coord;
        public int CheckerIndex;
        public MeshInstance3D Mesh;
        public StandardMaterial3D BaseMaterial;
        public Area3D Area;
        public Callable InputHandler;
    }

    public override void _Ready()
    {
        BuildBoard();
#if DEBUG
        GD.Print($"[HexBoard] _Ready done, tiles={_tiles.Count}");
#endif
    }

    public override void _Input(InputEvent @event)
    {
#if DEBUG
        if (@event is InputEventScreenTouch st)
        {
            var vp = GetViewport();
            GD.Print($"[DIAG-IN] touch pressed={st.Pressed} idx={st.Index} pos={st.Position} picking={vp?.PhysicsObjectPicking} tiles={_tiles.Count}");
        }
        else if (@event is InputEventMouseButton mb)
        {
            var vp = GetViewport();
            GD.Print($"[DIAG-IN] mouse pressed={mb.Pressed} btn={mb.ButtonIndex} pos={mb.Position} picking={vp?.PhysicsObjectPicking}");
        }
#endif
    }

    // ----- Run lifecycle -------------------------------------------------

    // PREVIEW: show a piece on the board with no hunters and no active run
    // (used behind the Character-Select screen). Distinct from StartRun.
    public void SetToken(int index)
    {
        _running = false;
        ClearHighlights();
        ClearEnemies();
        _selected = false;
        _selectionValid = false;
        _lastSelectedToken = null;
        _tokenPos = HexCoord.Zero;
        EquipToken(index);
        UpdateThreat();
        EmitSignal(SignalName.EnemiesChanged, 0);
    }

    // COMMIT: begin a fresh run with the chosen piece — zero score/wave/combo,
    // spawn wave 1 (all-RandomWalk, grace), and emit the starting HUD values.
    public void StartRun(int index)
    {
        _running = true;
        ClearHighlights();
        ClearEnemies();
        _selected = false;
        _selectionValid = false;
        _lastSelectedToken = null;
        _tokenPos = HexCoord.Zero;
        _wave = 1;
        _comboCount = 0;
        _score = 0;
        _mercyThisTurn = false;
        EquipToken(index);
        EmitSignal(SignalName.WaveChanged, _wave);
        EmitSignal(SignalName.ScoreChanged, _score);
        SpawnWave(_wave);
    }

    private void EquipToken(int index)
    {
        if (_token != null)
        {
            _token.SetProcessInput(false);
            _token.SetProcessUnhandledInput(false);
            _token.ProcessMode = ProcessModeEnum.Disabled;
            _token.QueueFree();
            _token = null;
        }

        if (index < 0 || index >= TokenCatalog.All.Length)
        {
            HideActiveRing();
            return;
        }

        _token = TokenCatalog.All[index].Factory();
        AddChild(_token);
        _token.Position = HexLayout.ToWorld(_tokenPos, TokenY);
        PositionActiveRing(_tokenPos);
    }

    private void BuildBoard()
    {
        var coords = new List<HexCoord>(1 + 3 * Radius * (Radius + 1));
        HexCoord.Within(Radius, coords);
        for (int i = 0; i < coords.Count; i++)
        {
            var h = coords[i];
            var tile = BuildTile(h);
            _tiles[h] = tile;
            AddChild(tile.Area);
        }
    }

    private Tile BuildTile(HexCoord coord)
    {
        var checker = ((coord.Q - coord.R) % 3 + 3) % 3;
        var tile = new Tile { Coord = coord, CheckerIndex = checker };

        tile.BaseMaterial = TileMaterialsByChecker[checker];

        tile.Mesh = new MeshInstance3D
        {
            Mesh = SharedTileMesh,
            MaterialOverride = tile.BaseMaterial,
        };

        var collision = new CollisionShape3D { Shape = SharedTileShape };

        tile.Area = new Area3D { Position = HexLayout.ToWorld(coord) };
        tile.Area.AddChild(tile.Mesh);
        tile.Area.AddChild(collision);
        var captured = coord;
        tile.InputHandler = Callable.From(
            (Node camera, InputEvent e, Vector3 pos, Vector3 normal, long idx) => OnTileInput(captured, e));
        tile.Area.Connect(Area3D.SignalName.InputEvent, tile.InputHandler);

        return tile;
    }

    public override void _ExitTree()
    {
        foreach (var kv in _tiles)
        {
            var tile = kv.Value;
            if (tile.Area != null && IsInstanceValid(tile.Area))
                tile.Area.Disconnect(Area3D.SignalName.InputEvent, tile.InputHandler);
        }

        // Kill animation tweens and reset the shared materials we mutate — static
        // resources persist across scene reloads in the same process.
        _ringTween?.Kill();
        StopHighlightPulse();
        RingMaterialShared.AlbedoColor = RingGold;
    }

    private void OnTileInput(HexCoord coord, InputEvent e)
    {
        bool clicked = false;
        if (e is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
            clicked = true;
        else if (e is InputEventScreenTouch st && st.Pressed)
            clicked = true;
        if (!clicked) return;
        OnTileTapped(coord);
    }

    private void OnTileTapped(HexCoord coord)
    {
        if (_token == null || !_running) return;

        if (!_selected)
        {
            if (coord == _tokenPos) BeginSelect();
            return;
        }

        // Re-tapping the token's own tile cancels the selection cleanly.
        if (coord == _tokenPos)
        {
            EndSelect();
            Haptics.Tap(8);
            return;
        }

        if (_highlighted.Contains(coord))
        {
            MoveTokenTo(coord);
            EndSelect();
        }
        else
        {
            EndSelect();
        }
    }

    // ----- Selection: highlights + death-tile marking + mercy ------------

    private void BeginSelect()
    {
        if (_selectionValid && _lastSelectedToken == _token && _lastSelectedPos == _tokenPos)
        {
            _selected = true;
            _token.SetActiveMaterial(SelectedTokenGoldMaterial);
            StartHighlightPulse();
            ShowTelegraph();
            return;
        }

        _selected = true;
        _highlighted.Clear();
        _token.LegalMoves(_tokenPos, Radius, _movesBuffer);

        int danger = 0;
        for (int i = 0; i < _movesBuffer.Count; i++)
        {
            var dest = _movesBuffer[i];
            if (!_tiles.TryGetValue(dest, out var tile)) continue;
            _highlighted.Add(dest);

            // Priority: DANGER (you die here) > CAPTURE (enemy here) > normal move.
            // Danger wins so the fairness cue is never hidden behind a capture colour.
            if (IsDeathTile(dest))
            {
                tile.Mesh.MaterialOverride = DangerHighlightMaterialShared;
                danger++;
            }
            else if (HasEnemyAt(dest))
            {
                tile.Mesh.MaterialOverride = CaptureHighlightMaterialShared;
            }
            else
            {
                tile.Mesh.MaterialOverride = HighlightMaterialShared;
            }
        }

        // Mercy: if every legal move would get the player caught, suppress Chase
        // steps for the upcoming resolution so there is always a survivable turn.
        _mercyThisTurn = _highlighted.Count > 0 && danger == _highlighted.Count;

        _lastSelectedToken = _token;
        _lastSelectedPos = _tokenPos;
        _selectionValid = true;

        _token.SetActiveMaterial(SelectedTokenGoldMaterial);
        StartHighlightPulse();
        ShowTelegraph();
    }

    private void EndSelect()
    {
        ClearHighlights();
        _selected = false;
        _token?.RestoreMaterial();
    }

    // Public hook for the screen flow (e.g. Pause) to drop any in-progress
    // selection so its highlights / pulse / telegraph don't persist behind a menu.
    public void ClearSelection()
    {
        if (_selected) EndSelect();
    }

    private void ClearHighlights()
    {
        StopHighlightPulse();
        HideTelegraph();
        foreach (var h in _highlighted)
            if (_tiles.TryGetValue(h, out var t))
                t.Mesh.MaterialOverride = t.BaseMaterial;
        _highlighted.Clear();
        _selectionValid = false;
        RefreshReachOverlay();
    }

    private bool HasEnemyAt(HexCoord coord)
    {
        for (int i = 0; i < _enemies.Count; i++)
            if (_enemies[i].Coord == coord) return true;
        return false;
    }

    // A tile is lethal iff a non-grace Chase hunter sits adjacent to it: Chase
    // minimises distance to the player, and a step onto the player (the tile,
    // hypothetically occupied by the player) is distance 0 — a strict minimum no
    // other hunter can block. Exact, zero-alloc, no RNG. The hunter currently ON
    // the tile is excluded (it gets captured on landing).
    private bool IsDeathTile(HexCoord cand)
    {
        for (int i = 0; i < _enemies.Count; i++)
        {
            var e = _enemies[i];
            if (e.Behavior != EnemyBehavior.Chase) continue;
            if (e.SpawnedThisWave) continue;        // grace turn: won't step next move
            if (e.Coord == cand) continue;          // captured on landing — not a threat
            if (e.Coord.Distance(cand) == 1) return true;
        }
        return false;
    }

    // One looped tween drives the emission of all three highlight materials, so
    // every highlighted tile pulses in sync at zero per-tile cost.
    private void StartHighlightPulse()
    {
        _pulseTween?.Kill();
        var t = CreateTween().SetLoops();
        AddPulseLeg(t, 1.5f);
        AddPulseLeg(t, 0.7f);
        _pulseTween = t;
    }

    private void AddPulseLeg(Tween t, float energy)
    {
        t.TweenProperty(HighlightMaterialShared, "emission_energy_multiplier", energy, 0.55f)
            .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        t.Parallel().TweenProperty(CaptureHighlightMaterialShared, "emission_energy_multiplier", energy, 0.55f)
            .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        t.Parallel().TweenProperty(DangerHighlightMaterialShared, "emission_energy_multiplier", energy, 0.55f)
            .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
    }

    private void StopHighlightPulse()
    {
        if (_pulseTween != null) { _pulseTween.Kill(); _pulseTween = null; }
        HighlightMaterialShared.EmissionEnergyMultiplier = 1f;
        CaptureHighlightMaterialShared.EmissionEnergyMultiplier = 1f;
        DangerHighlightMaterialShared.EmissionEnergyMultiplier = 1f;
    }

    private MeshInstance3D GetTelegraphNode(int index)
    {
        while (index >= _telegraphNodes.Count)
        {
            var node = new MeshInstance3D
            {
                Mesh = SharedTelegraphMesh,
                MaterialOverride = TelegraphMaterialShared,
                Visible = false,
            };
            AddChild(node);
            _telegraphNodes.Add(node);
        }
        return _telegraphNodes[index];
    }

    // Ghost markers preview where each DETERMINISTIC (Chase/Flee) hunter intends
    // to step from its current tile. RandomWalk hunters are skipped (showing them
    // would both mislead and consume the RNG that drives the real step).
    private void ShowTelegraph()
    {
        HideTelegraph();
        int used = 0;
        for (int i = 0; i < _enemies.Count; i++)
        {
            var e = _enemies[i];
            if (e.Behavior == EnemyBehavior.RandomWalk) continue;
            if (e.SpawnedThisWave) continue;       // won't move next turn
            var step = PredictEnemyStep(i, _tokenPos, mercy: false);
            if (step == e.Coord) continue;         // boxed in: nothing to show
            var node = GetTelegraphNode(used++);
            node.Position = HexLayout.ToWorld(step, 0.12f);
            node.Visible = true;
        }
    }

    private void HideTelegraph()
    {
        for (int i = 0; i < _telegraphNodes.Count; i++)
            if (IsInstanceValid(_telegraphNodes[i]))
                _telegraphNodes[i].Visible = false;
    }

    public void SetShowReach(bool show)
    {
        _showReach = show;
        RefreshReachOverlay();
    }

    private void RefreshReachOverlay()
    {
        foreach (var c in _reachMarked)
            if (!_highlighted.Contains(c) && _tiles.TryGetValue(c, out var t))
                t.Mesh.MaterialOverride = t.BaseMaterial;
        _reachMarked.Clear();

        if (!_showReach || _token == null) return;

        ComputeReachable(_tokenPos);
        foreach (var c in _reachable)
        {
            if (_highlighted.Contains(c)) continue;
            if (!_tiles.TryGetValue(c, out var t)) continue;
            t.Mesh.MaterialOverride = ReachMaterialShared;
            _reachMarked.Add(c);
        }
    }

    // ----- Turn resolution ----------------------------------------------

    // Ordered: commit pos -> capture+score+combo -> wave-clear short-circuit
    // (always safe) -> hunter step with per-hunter caught check (first catcher
    // ends the run). Mercy + spawn-grace are honoured inside AdvanceEnemies.
    private void MoveTokenTo(HexCoord coord)
    {
        _tokenPos = coord;
        _selectionValid = false;
        TweenTokenTo(coord);
        PositionActiveRing(coord);

        bool captured = TryCapture(coord);
        PlayLandingRing(coord, captured);

        if (captured)
        {
            _comboCount++;
            int mult = Mathf.Min(_comboCount, 5);
            AddScore(100 * mult);
            if (_comboCount >= 2) EmitSignal(SignalName.ComboChanged, _comboCount);
        }
        else
        {
            _comboCount = 0;
        }
        int strength = captured ? Mathf.Min(35 + (_comboCount - 1) * 12, 90) : 15;
        Haptics.Tap(strength);

        // Wave cleared: award the clear bonus, ramp, and respawn (with grace).
        if (_enemies.Count == 0)
        {
            AddScore(250 * _wave);
            EmitSignal(SignalName.BoardSolved);
            _wave++;
            EmitSignal(SignalName.WaveChanged, _wave);
            SpawnWave(_wave);
            _mercyThisTurn = false;
            UpdateThreat();
            return;
        }

        bool caught = AdvanceEnemies(coord);
        _mercyThisTurn = false;
        if (caught)
        {
            _running = false;          // gate any further board taps until Retry
            return;                    // PlayerCaught already emitted
        }
        UpdateThreat();
    }

    private void AddScore(int delta)
    {
        _score += delta;
        EmitSignal(SignalName.ScoreChanged, _score);
    }

    // Quick expanding shockwave ring at the landing tile (gold normally, copper
    // on a capturing move). One pooled node, re-triggered each move.
    private void PlayLandingRing(HexCoord coord, bool capture)
    {
        if (_ringNode == null)
        {
            _ringNode = new MeshInstance3D
            {
                Mesh = SharedRingMesh,
                MaterialOverride = RingMaterialShared,
                Visible = false,
            };
            AddChild(_ringNode);
        }

        _ringTween?.Kill();
        _ringNode.Position = HexLayout.ToWorld(coord, 0.1f);
        _ringNode.Scale = new Vector3(0.35f, 1f, 0.35f);
        _ringNode.Visible = true;
        var ringColor = capture
            ? new Color(CopperColor.R, CopperColor.G, CopperColor.B, 0.85f)
            : RingGold;
        RingMaterialShared.AlbedoColor = ringColor;

        _ringTween = CreateTween();
        _ringTween.TweenProperty(_ringNode, "scale", new Vector3(1.5f, 1f, 1.5f), 0.28f)
            .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
        // Tween the whole albedo_color to alpha-0 (rather than the ":a" sub-path) so the
        // fade is unambiguous across renderer/value-type quirks.
        _ringTween.Parallel().TweenProperty(RingMaterialShared, "albedo_color",
                new Color(ringColor.R, ringColor.G, ringColor.B, 0f), 0.28f)
            .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
        _ringTween.TweenCallback(Callable.From(() =>
        {
            if (IsInstanceValid(_ringNode)) _ringNode.Visible = false;
        }));
    }

    private void TweenTokenTo(HexCoord coord)
    {
        var target = HexLayout.ToWorld(coord, TokenY);

        var move = CreateTween();
        move.TweenProperty(_token, "position", target, 0.18f)
            .SetTrans(Tween.TransitionType.Sine)
            .SetEase(Tween.EaseType.Out);

        // Parallel squash-and-stretch: compress on launch, snap back on landing.
        var squash = CreateTween();
        squash.TweenProperty(_token, "scale", new Vector3(1.12f, 0.85f, 1.12f), 0.09f)
            .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
        squash.TweenProperty(_token, "scale", Vector3.One, 0.09f)
            .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.In);
    }

    private void PositionActiveRing(HexCoord coord)
    {
        if (_token == null) { HideActiveRing(); return; }
        if (_activeRingNode == null)
        {
            _activeRingNode = new MeshInstance3D
            {
                Mesh = SharedActiveRingMesh,
                MaterialOverride = ActiveRingMaterialShared,
            };
            AddChild(_activeRingNode);
        }
        _activeRingNode.Position = HexLayout.ToWorld(coord, RingY);
        _activeRingNode.Visible = true;
    }

    private void HideActiveRing()
    {
        if (_activeRingNode != null && IsInstanceValid(_activeRingNode))
            _activeRingNode.Visible = false;
    }

    // ----- Hunters -------------------------------------------------------

    // Each surviving hunter steps once. Freshly spawned hunters skip their first
    // step (grace). Returns true (and emits PlayerCaught) the instant a hunter
    // lands on the player — the first catcher wins and resolution stops.
    private bool AdvanceEnemies(HexCoord playerPos)
    {
        for (int i = 0; i < _enemies.Count; i++)
        {
            var e = _enemies[i];
            if (e.SpawnedThisWave) { e.SpawnedThisWave = false; continue; }

            var chosen = PredictEnemyStep(i, playerPos, _mercyThisTurn);
            if (chosen == e.Coord) continue;       // boxed in, holds position
            e.Coord = chosen;
            MoveEnemyVisual(e, chosen);

            if (chosen == playerPos)
            {
                EmitSignal(SignalName.PlayerCaught);
                return true;
            }
        }
        return false;
    }

    // Pure decision: where would hunter `index` step, without mutating state.
    // Chase MAY land on the player (the lethal catch) unless mercy suppresses it
    // this turn; Flee/RandomWalk always exclude the player tile. The IsEnemyAt
    // anti-stack guard applies to every behaviour.
    private HexCoord PredictEnemyStep(int index, HexCoord playerPos, bool mercy)
    {
        var e = _enemies[index];
        var dirs = HexCoord.Directions;
        bool lethal = e.Behavior == EnemyBehavior.Chase && !mercy;

        _neighborScratch.Clear();
        for (int d = 0; d < 6; d++)
        {
            var n = e.Coord + dirs[d];
            if (!_tiles.ContainsKey(n)) continue;       // off-board
            if (IsEnemyAt(n, index)) continue;          // never stack
            if (n == playerPos && !lethal) continue;    // non-lethal behaviours avoid player
            _neighborScratch.Add(n);
        }
        if (_neighborScratch.Count == 0) return e.Coord;

        return e.Behavior switch
        {
            EnemyBehavior.Flee => PickByDistance(_neighborScratch, playerPos, maximize: true),
            EnemyBehavior.Chase => PickByDistance(_neighborScratch, playerPos, maximize: false),
            _ => _neighborScratch[_rng.Next(_neighborScratch.Count)],
        };
    }

    private bool IsEnemyAt(HexCoord coord, int except)
    {
        for (int j = 0; j < _enemies.Count; j++)
        {
            if (j == except) continue;
            if (_enemies[j].Coord == coord) return true;
        }
        return false;
    }

    private static HexCoord PickByDistance(List<HexCoord> options, HexCoord target, bool maximize)
    {
        var best = options[0];
        int bestD = best.Distance(target);
        for (int i = 1; i < options.Count; i++)
        {
            int d = options[i].Distance(target);
            if (maximize ? d > bestD : d < bestD) { best = options[i]; bestD = d; }
        }
        return best;
    }

    private void MoveEnemyVisual(Enemy e, HexCoord coord)
    {
        if (e.Node == null || !IsInstanceValid(e.Node)) return;
        var target = HexLayout.ToWorld(coord, TokenY);
        var tween = CreateTween();
        tween.TweenProperty(e.Node, "position", target, 0.16f)
            .SetTrans(Tween.TransitionType.Sine)
            .SetEase(Tween.EaseType.Out);
    }

    // Spawn the wave's hunters: count ramps with the wave (capped), behaviours
    // follow the per-wave mix, and tiles are drawn from the token's BFS-reachable
    // set minus the radius-1 disc around the player (never spawn adjacent).
    private void SpawnWave(int wave)
    {
        ClearEnemies();
        if (_token == null)
        {
            EmitSignal(SignalName.EnemiesChanged, 0);
            RefreshReachOverlay();
            return;
        }

        int count = Mathf.Min(BaseSpawn + (wave - 1), MaxSpawn);

        ComputeReachable(_tokenPos);
        _spawnScratch.Clear();
        foreach (var c in _reachable)
            if (c.Distance(_tokenPos) >= 2) _spawnScratch.Add(c);
        // Fallback for tiny reachable sets: allow any reachable tile (still never
        // the player's own tile — ComputeReachable removes the start).
        if (_spawnScratch.Count < count)
        {
            _spawnScratch.Clear();
            foreach (var c in _reachable) _spawnScratch.Add(c);
        }

        int target = Mathf.Min(count, _spawnScratch.Count);
        AssignWaveBehaviors(wave, target);

        for (int n = 0; n < target; n++)
        {
            int idx = _rng.Next(_spawnScratch.Count);
            var coord = _spawnScratch[idx];
            _spawnScratch[idx] = _spawnScratch[_spawnScratch.Count - 1];
            _spawnScratch.RemoveAt(_spawnScratch.Count - 1);
            AddEnemy(coord, _behaviorScratch[n]);
        }
        EmitSignal(SignalName.EnemiesChanged, _enemies.Count);
        RefreshReachOverlay();
        UpdateThreat();
    }

    // Difficulty ramp: wave 1 all-RandomWalk (un-losable teaching), then add
    // Chasers (the only lethal class), keeping a non-Chase majority so the board
    // stays legible. Tile assignment is already randomised by the spawn loop.
    private void AssignWaveBehaviors(int wave, int count)
    {
        _behaviorScratch.Clear();
        int chasers, fleers;
        if (wave <= 1) { chasers = 0; fleers = 0; }
        else if (wave == 2) { chasers = 1; fleers = 0; }
        else if (wave == 3) { chasers = 1; fleers = 1; }
        else
        {
            float pct = wave <= 5 ? 0.40f : 0.60f;     // <= the 70% anti-frustration cap
            chasers = Mathf.Max(1, Mathf.FloorToInt(count * pct));
            fleers = wave <= 5 ? 1 : Mathf.Max(1, count / 5);
        }
        chasers = Mathf.Min(chasers, count);
        fleers = Mathf.Min(fleers, count - chasers);
        int randoms = count - chasers - fleers;

        for (int i = 0; i < chasers; i++) _behaviorScratch.Add(EnemyBehavior.Chase);
        for (int i = 0; i < fleers; i++) _behaviorScratch.Add(EnemyBehavior.Flee);
        for (int i = 0; i < randoms; i++) _behaviorScratch.Add(EnemyBehavior.RandomWalk);
    }

    private void AddEnemy(HexCoord coord, EnemyBehavior behavior)
    {
        var mesh = new MeshInstance3D
        {
            Mesh = SharedEnemyMesh,
            MaterialOverride = EnemyMaterial,
            Position = HexLayout.ToWorld(coord, TokenY),
            Scale = Vector3.Zero,
        };
        AddChild(mesh);
        _enemies.Add(new Enemy { Coord = coord, Node = mesh, Behavior = behavior, SpawnedThisWave = true });

        var spawn = CreateTween();
        spawn.TweenProperty(mesh, "scale", Vector3.One, 0.18f)
            .SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
    }

    private void ClearEnemies()
    {
        for (int i = 0; i < _enemies.Count; i++)
        {
            var node = _enemies[i].Node;
            if (node != null && IsInstanceValid(node)) node.QueueFree();
        }
        _enemies.Clear();
    }

    // BFS over the active token's movement graph: every tile the token can
    // eventually reach from `start`. Hunters only spawn on reachable tiles so a
    // board is never unsolvable. Reuses pooled buffers to stay alloc-free.
    private void ComputeReachable(HexCoord start)
    {
        _reachable.Clear();
        _bfsQueue.Clear();
        _reachable.Add(start);
        _bfsQueue.Enqueue(start);
        while (_bfsQueue.Count > 0)
        {
            var cur = _bfsQueue.Dequeue();
            _token.LegalMoves(cur, Radius, _bfsMoves);
            for (int i = 0; i < _bfsMoves.Count; i++)
            {
                var n = _bfsMoves[i];
                if (_tiles.ContainsKey(n) && _reachable.Add(n))
                    _bfsQueue.Enqueue(n);
            }
        }
        _reachable.Remove(start);
    }

    private bool TryCapture(HexCoord coord)
    {
        for (int i = 0; i < _enemies.Count; i++)
        {
            if (_enemies[i].Coord != coord) continue;
            var node = _enemies[i].Node;
            _enemies.RemoveAt(i);
            CaptureVisual(node);
            PlayCaptureBurst(coord);
            EmitSignal(SignalName.EnemiesChanged, _enemies.Count);
            return true;
        }
        return false;
    }

    private void CaptureVisual(MeshInstance3D node)
    {
        if (node == null || !IsInstanceValid(node)) return;
        var tween = CreateTween();
        tween.TweenProperty(node, "scale", Vector3.Zero, 0.13f)
            .SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.In);
        tween.TweenCallback(Callable.From(() =>
        {
            if (IsInstanceValid(node)) node.QueueFree();
        }));
    }

    // Brief copper spark puff on capture. One pooled CPU emitter, repositioned
    // and Restart()ed per capture (GL Compatibility safe; no GPU particles).
    private void PlayCaptureBurst(HexCoord coord)
    {
        if (_captureParticles == null)
        {
            _captureParticles = new CpuParticles3D
            {
                Mesh = SharedSparkMesh,
                MaterialOverride = SparkMaterialShared,
                Emitting = false,
                OneShot = true,
                Amount = 14,
                Lifetime = 0.5,
                Explosiveness = 1.0f,
                Direction = Vector3.Up,
                Spread = 35f,
                InitialVelocityMin = 1.2f,
                InitialVelocityMax = 2.4f,
                Gravity = new Vector3(0, -3f, 0),
                ScaleAmountMin = 0.6f,
                ScaleAmountMax = 1.0f,
            };
            AddChild(_captureParticles);
        }
        _captureParticles.Position = HexLayout.ToWorld(coord, TokenY);
        _captureParticles.Restart();
        _captureParticles.Emitting = true;
    }

    // Ambient "you might lose" cue: true while any non-grace Chase hunter sits
    // adjacent to the player. Emits ThreatChanged only on a flip.
    private void UpdateThreat()
    {
        bool danger = false;
        if (_running && _token != null)
        {
            for (int i = 0; i < _enemies.Count; i++)
            {
                var e = _enemies[i];
                if (e.Behavior != EnemyBehavior.Chase) continue;
                if (e.SpawnedThisWave) continue;
                if (e.Coord.Distance(_tokenPos) == 1) { danger = true; break; }
            }
        }
        if (danger != _inDanger)
        {
            _inDanger = danger;
            EmitSignal(SignalName.ThreatChanged, danger);
        }
    }
}
