// =============================================================================
// HexBoard — battle controller
// =============================================================================
// Purpose:
//   Partial Node3D that builds the radius-4 hex board and runs one hex-chess
//   battle at a time: the ActiveTiles mask (early battles fight on a smaller
//   area), both armies of BattlePieces, player selection/highlighting with
//   danger-tile marking, reserve deployment, the one-enemy-acts-per-turn
//   tactical AI, the crumble timer that cracks and collapses outer rings, boss
//   modifiers, tile-upgrade effects, Gambit effects, and win/loss detection.
//
//   Premium Slate look preserved: brushed-slate tiles, ivory/oxblood alloy
//   pieces, gold selection glow (emissive material — the only sanctioned
//   "glow"), pooled landing shockwave + CPU-particle capture burst. All GPU
//   resources are shared statics (Compatibility renderer; no bloom/GPU
//   particles).
//
// Fairness:
//   - A candidate destination is a DEATH tile (painted red) iff some non-
//     stunned enemy piece could capture it next turn, computed exactly by
//     re-running the enemy move generator against a hypothetical occupancy
//     (the mover relocated) — no RNG, no allocation.
//   - The crumble telegraphs: the doomed ring is painted "cracked" two player
//     actions before it collapses, and the inner radius-1 disc never crumbles.
//
// Interactions:
//   - PieceRules/IBattleQuery: move generation for selection, AI and danger.
//   - RunState: mutated live (money, reserve, gambit flags via Has()).
//   - ScreenManager: calls ShowPreview/StartBattle/BeginDeploy/ClearSelection
//     and listens to the signals to drive the HUD and screen flow.
// =============================================================================

using System.Collections.Generic;
using Godot;
using HexGame.Chess;
using HexGame.Hex;
using static HexGame.Board.TileVisuals;   // shared tile/highlight/FX GPU resources

namespace HexGame.Board;

public partial class HexBoard : Node3D, IBattleQuery
{
    [Export] public int Radius = 4;

    private const float PieceY = 0.35f;
    private const float RingY = 0.09f;
    private const int CrackGraceActions = 2;   // player actions between crack and collapse
    private const int StandoffActions = 16;    // capture-free terminal actions before adjudication

    // The enemy's answer resolves synchronously but its VISUALS lag so the
    // exchange reads as call-and-response instead of a simultaneous blur.
    private const float EnemyMoveDelay = 0.22f;
    private const float EnemyStrikeDelay = 0.32f;   // victim shrink + spark as the attacker lands

    [Signal] public delegate void MoneyChangedEventHandler(int money);
    [Signal] public delegate void ScoreChangedEventHandler(int score);
    [Signal] public delegate void EnemiesChangedEventHandler(int remaining);
    [Signal] public delegate void ArmyChangedEventHandler(int onBoard, int reserve);
    [Signal] public delegate void CrumbleChangedEventHandler(int turnsLeft, bool cracking);
    [Signal] public delegate void ThreatChangedEventHandler(bool inDanger);
    [Signal] public delegate void StatusNoteEventHandler(string note);
    [Signal] public delegate void InspectChangedEventHandler(string text);   // "" hides
    [Signal] public delegate void DeployModeChangedEventHandler(bool active);
    [Signal] public delegate void BattleWonEventHandler();
    [Signal] public delegate void BattleLostEventHandler();

    // ----- Board -----------------------------------------------------------
    private readonly Dictionary<HexCoord, Tile> _tiles = new();
    private readonly HashSet<HexCoord> _active = new();
    private readonly HashSet<HexCoord> _cracked = new();
    private readonly HashSet<HexCoord> _locked = new();

    // ----- Pieces ----------------------------------------------------------
    private readonly List<BattlePiece> _pieces = new();
    private readonly Dictionary<HexCoord, BattlePiece> _occupied = new();

    // ----- Battle state ----------------------------------------------------
    private RunState _run;
    private bool _running = false;
    private int _activeRadius = 4;
    private int _outerRadius = 4;            // current outermost playable ring
    private int _crumbleTimer = 0;           // player actions until the next crack
    private int _crackCountdown = 0;         // actions until the cracked ring collapses
    private BossModifier _boss = BossModifier.None;
    private int _lockTurnsLeft = 0;
    private bool _firstCaptureDone = false;  // Tax Collector
    private bool _mercyUsed = false;         // Mercy Charter
    private int _stagnantActions = 0;        // actions since the last piece death
    private bool _echoUsed = false;          // Bishop Echo
    private bool _royalGuardUsed = false;    // Royal Guard
    private readonly HashSet<HexCoord> _shieldConsumed = new();

    // ----- Selection / deploy ---------------------------------------------
    private BattlePiece _selPiece;
    private int _deployIndex = -1;           // >= 0: deploy mode, index into run.Reserve
    private readonly HashSet<HexCoord> _highlighted = new();
    private readonly List<HexCoord> _movesBuffer = new(64);
    private readonly List<bool> _dangerBuffer = new(64);
    // Selection memoization: recompute legal moves + danger only when the piece
    // or the board state stamp changed (hard rule 3, adapted to multi-piece).
    private int _stateStamp = 0;
    private BattlePiece _cachePiece;
    private int _cacheStamp = -1;

    // ----- AI / scratch (zero-alloc invariant: pooled, reused) -------------
    private readonly System.Random _rng = new();
    private readonly List<HexCoord> _aiMoves = new(64);
    private readonly List<HexCoord> _dangerScratch = new(64);
    private readonly List<HexCoord> _coordScratch = new(64);
    private readonly List<PieceKind> _enemyPlan = new(12);
    private HexCoord _hypoFrom;              // hypothetical occupancy overlay for
    private HexCoord _hypoTo;                // danger prediction (IBattleQuery)
    private bool _hypoActive = false;

    // ----- Markers / threat (FX node pools live in HexBoard.Fx.cs) ---------
    private readonly Dictionary<HexCoord, MeshInstance3D> _upgradeMarkers = new();
    private bool _threat = false;

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

    private void BuildBoard()
    {
        var coords = new List<HexCoord>(1 + 3 * Radius * (Radius + 1));
        HexCoord.Within(Radius, coords);
        for (int i = 0; i < coords.Count; i++)
        {
            var h = coords[i];
            var tile = BuildTile(h);
            _tiles[h] = tile;
            _active.Add(h);
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

        // Kill tweens and reset shared materials we mutate — static resources
        // persist across scene reloads in the same process.
        _ringTween?.Kill();
        StopHighlightPulse();
        RingMaterialShared.AlbedoColor = RingGold;
    }

    // ----- IBattleQuery ----------------------------------------------------

    public bool IsPlayable(HexCoord c) =>
        _active.Contains(c) && !_locked.Contains(c);

    public PieceSide? OccupantSide(HexCoord c)
    {
        if (_hypoActive)
        {
            if (c == _hypoTo) return PieceSide.Player;      // mover relocated here
            if (c == _hypoFrom) return null;                // ...vacating its origin
        }
        return _occupied.TryGetValue(c, out var p) ? p.Side : null;
    }

    // ----- Battle lifecycle --------------------------------------------------

    // Decorative arrangement behind the Title screen: full board, a few pieces,
    // no run, no input response (_running stays false).
    public void ShowPreview()
    {
        _running = false;
        _run = null;
        ResetBattleState(Radius);
        SpawnPiece(PieceKind.King, PieceSide.Player, new HexCoord(0, 3));
        SpawnPiece(PieceKind.Rook, PieceSide.Player, new HexCoord(-2, 3));
        SpawnPiece(PieceKind.Knight, PieceSide.Player, new HexCoord(2, 1));
        SpawnPiece(PieceKind.Bishop, PieceSide.Player, new HexCoord(1, 2));
        SpawnPiece(PieceKind.Pawn, PieceSide.Enemy, new HexCoord(0, -2));
        SpawnPiece(PieceKind.Pawn, PieceSide.Enemy, new HexCoord(-1, -1));
        SpawnPiece(PieceKind.Queen, PieceSide.Enemy, new HexCoord(1, -3));
        SpawnPiece(PieceKind.Knight, PieceSide.Enemy, new HexCoord(-2, -1));
    }

    public void StartBattle(RunState run)
    {
        _run = run;
        _running = true;
        int battle = run.Battle;
        _boss = BattlePlanner.BossFor(battle);
        ResetBattleState(BattlePlanner.ActiveRadius(battle));
        _crumbleTimer = BattlePlanner.CrumbleTurns(battle, run, _boss);

        SpawnPlayerArmy();
        string theme = SpawnEnemyArmy(battle);
        if (_boss == BossModifier.Lockmaker) ApplyLockmaker();
        PlaceUpgradeMarkers();
        RefreshAllTileVisuals();

        EmitSignal(SignalName.MoneyChanged, run.Money);
        EmitSignal(SignalName.ScoreChanged, run.Score);
        EmitSignal(SignalName.CrumbleChanged, _crumbleTimer, false);
        EmitArmyCounts();
        EmitSignal(SignalName.EnemiesChanged, CountSide(PieceSide.Enemy));
        SetThreat(false);

        // Every battle announces itself; a boss also names its rule bend
        // (its rules would apply invisibly otherwise).
        if (_boss != BossModifier.None)
        {
            EmitSignal(SignalName.StatusNote,
                $"{BossCatalog.NameOf(_boss).ToUpperInvariant()}: {BossCatalog.EffectOf(_boss).ToUpperInvariant()}");
            Sfx.Play(SfxCue.Boss, -4f);
        }
        else
        {
            EmitSignal(SignalName.StatusNote,
                theme != null ? $"BATTLE {battle}: {theme}" : $"BATTLE {battle}");
        }

        // The finale's guaranteed Queen gets her own announcement (last note wins
        // the flourish; the HUD label still names the boss).
        if (battle == RunState.FinalBattle)
            EmitSignal(SignalName.StatusNote, "THE ENEMY CROWN TAKES THE FIELD");
    }

    private void ResetBattleState(int activeRadius)
    {
        EndSelect();
        CancelDeploy();
        ClearPieces();
        _cracked.Clear();
        _locked.Clear();
        _shieldConsumed.Clear();
        _activeRadius = activeRadius;
        _outerRadius = activeRadius;
        _crackCountdown = 0;
        _crumbleTimer = 0;
        _lockTurnsLeft = 0;
        _firstCaptureDone = false;
        _mercyUsed = false;
        _stagnantActions = 0;
        _shopPreviewCoord = null;
        _echoUsed = false;
        _royalGuardUsed = false;
        _stateStamp++;
        _cacheStamp = -1;

        _active.Clear();
        foreach (var kv in _tiles)
            if (kv.Key.DistanceFromOrigin() <= activeRadius) _active.Add(kv.Key);

        foreach (var kv in _upgradeMarkers)
            if (IsInstanceValid(kv.Value)) kv.Value.Visible = false;

        RefreshAllTileVisuals();
        SetThreat(false);
    }

    // Player army fills the southernmost active tiles (positive R — pawns push
    // toward the enemy at negative R). Overflow beyond the home rows moves to
    // the reserve so no piece is silently lost.
    private void SpawnPlayerArmy()
    {
        _coordScratch.Clear();
        foreach (var c in _active) _coordScratch.Add(c);
        _coordScratch.Sort((a, b) => b.R != a.R ? b.R.CompareTo(a.R) : System.Math.Abs(a.Q).CompareTo(System.Math.Abs(b.Q)));

        int placed = 0;
        for (int i = 0; i < _coordScratch.Count && placed < _run.Army.Count; i++)
        {
            var c = _coordScratch[i];
            if (c.R < 1) break;                       // home rows only
            if (_occupied.ContainsKey(c)) continue;
            SpawnPiece(_run.Army[placed], PieceSide.Player, c);
            placed++;
        }
        // Anything that didn't fit waits in the reserve.
        for (int i = _run.Army.Count - 1; i >= placed; i--)
        {
            _run.Reserve.Add(_run.Army[i]);
            _run.Army.RemoveAt(i);
        }
    }

    private string SpawnEnemyArmy(int battle)
    {
        string theme = BattlePlanner.FillEnemyArmy(battle, _rng, _enemyPlan);
        _coordScratch.Clear();
        foreach (var c in _active) _coordScratch.Add(c);
        _coordScratch.Sort((a, b) => a.R != b.R ? a.R.CompareTo(b.R) : System.Math.Abs(a.Q).CompareTo(System.Math.Abs(b.Q)));

        int placed = 0;
        for (int i = 0; i < _coordScratch.Count && placed < _enemyPlan.Count; i++)
        {
            var c = _coordScratch[i];
            if (c.R > -1) break;                      // enemy rows only
            if (_occupied.ContainsKey(c)) continue;
            SpawnPiece(_enemyPlan[placed], PieceSide.Enemy, c);
            placed++;
        }
        return theme;
    }

    private void ApplyLockmaker()
    {
        // The locks must never strand the whole army (e.g. a lone corner
        // bishop whose only two on-board diagonals both get locked): the
        // battle would start with no possible player action, and since the
        // lock timer only ticks on player actions it would never expire.
        for (int attempt = 0; attempt < 8; attempt++)
        {
            _locked.Clear();
            _coordScratch.Clear();
            foreach (var c in _active)
                if (!_occupied.ContainsKey(c)) _coordScratch.Add(c);
            for (int n = 0; n < 3 && _coordScratch.Count > 0; n++)
            {
                int idx = _rng.Next(_coordScratch.Count);
                _locked.Add(_coordScratch[idx]);
                _coordScratch[idx] = _coordScratch[_coordScratch.Count - 1];
                _coordScratch.RemoveAt(_coordScratch.Count - 1);
            }
            if (PlayerHasAnyAction())
            {
                _lockTurnsLeft = 2;
                return;
            }
        }
        _locked.Clear();
        _lockTurnsLeft = 0;
    }

    private void PlaceUpgradeMarkers()
    {
        if (_run == null) return;
        foreach (var kv in _run.TileUpgrades)
        {
            if (!_tiles.ContainsKey(kv.Key)) continue;
            if (!_upgradeMarkers.TryGetValue(kv.Key, out var node) || !IsInstanceValid(node))
            {
                node = new MeshInstance3D { Mesh = SharedMarkerMesh };
                node.Position = HexLayout.ToWorld(kv.Key, 0.085f);
                AddChild(node);
                _upgradeMarkers[kv.Key] = node;
            }
            node.MaterialOverride = MarkerMaterials[(int)kv.Value];
            node.Visible = _active.Contains(kv.Key);
        }
    }

    private void SpawnPiece(PieceKind kind, PieceSide side, HexCoord coord)
    {
        var mesh = new MeshInstance3D
        {
            Mesh = PieceVisuals.MeshFor(kind),
            MaterialOverride = PieceVisuals.MaterialFor(side),
            Position = HexLayout.ToWorld(coord, PieceY),
            Scale = Vector3.Zero,
        };
        AddChild(mesh);
        var piece = new BattlePiece { Kind = kind, Side = side, Coord = coord, Node = mesh };
        _pieces.Add(piece);
        _occupied[coord] = piece;

        var spawn = CreateTween();
        spawn.TweenProperty(mesh, "scale", Vector3.One, 0.18f)
            .SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
    }

    private void ClearPieces()
    {
        for (int i = 0; i < _pieces.Count; i++)
        {
            var node = _pieces[i].Node;
            if (node != null && IsInstanceValid(node)) node.QueueFree();
        }
        _pieces.Clear();
        _occupied.Clear();
        _selPiece = null;
    }

    private int CountSide(PieceSide side)
    {
        int n = 0;
        for (int i = 0; i < _pieces.Count; i++)
            if (_pieces[i].Alive && _pieces[i].Side == side) n++;
        return n;
    }

    private void EmitArmyCounts() =>
        EmitSignal(SignalName.ArmyChanged, CountSide(PieceSide.Player), _run?.Reserve.Count ?? 0);

    // ----- Input -------------------------------------------------------------

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
        if (!_running) return;

        if (_deployIndex >= 0)
        {
            if (_highlighted.Contains(coord)) ExecuteDeploy(coord);
            else CancelDeploy();
            return;
        }

        if (_occupied.TryGetValue(coord, out var piece) && piece.Side == PieceSide.Player)
        {
            if (_selPiece == piece) { EndSelect(); Haptics.Tap(8); }
            else Select(piece);
            return;
        }

        // Tapping an enemy piece shows its reach (read-only, steel blue) —
        // unless a move to its tile is currently offered (capture beats
        // inspection; the un-highlighted case falls through to EndSelect).
        if (piece != null && piece.Side == PieceSide.Enemy
            && !(_selPiece != null && _highlighted.Contains(coord)))
        {
            ShowEnemyReach(piece);
            return;
        }

        if (_selPiece != null && _highlighted.Contains(coord))
        {
            var mover = _selPiece;
            EndSelect();
            ExecuteMove(mover, coord);
        }
        else
        {
            EndSelect();
            // A bare tap on an upgraded tile explains its marker — the colour
            // code is otherwise taught only once, in the shop.
            if (_run != null && _active.Contains(coord)
                && _run.TileUpgrades.TryGetValue(coord, out var upKind))
            {
                var up = TileUpgradeCatalog.Info(upKind);
                EmitSignal(SignalName.InspectChanged, $"{up.Name.ToUpperInvariant()} — {up.Description}");
            }
        }
    }

    // ----- Selection ---------------------------------------------------------

    private void Select(BattlePiece piece)
    {
        EndSelect();
        _selPiece = piece;

        // Memoized: reuse the last computed move/danger set when neither the
        // piece nor the board state stamp changed.
        if (_cachePiece != piece || _cacheStamp != _stateStamp)
        {
            PieceRules.LegalMoves(piece.Kind, PieceSide.Player, piece.Coord, this, _movesBuffer);
            _dangerBuffer.Clear();
            for (int i = 0; i < _movesBuffer.Count; i++)
                _dangerBuffer.Add(IsDeathTile(piece, _movesBuffer[i]));
            _cachePiece = piece;
            _cacheStamp = _stateStamp;
        }

        for (int i = 0; i < _movesBuffer.Count; i++)
        {
            var dest = _movesBuffer[i];
            if (!_tiles.TryGetValue(dest, out var tile)) continue;
            _highlighted.Add(dest);

            // Priority: DANGER (you lose this piece here) > CAPTURE > move —
            // the fairness cue is never hidden behind a capture colour.
            if (_dangerBuffer[i])
                tile.Mesh.MaterialOverride = DangerHighlightMaterialShared;
            else if (_occupied.TryGetValue(dest, out var occ) && occ.Side == PieceSide.Enemy)
                tile.Mesh.MaterialOverride = CaptureHighlightMaterialShared;
            else
                tile.Mesh.MaterialOverride = HighlightMaterialShared;
        }

        piece.Node.MaterialOverride = PieceVisuals.SelectedMaterial;
        PositionSelectRing(piece.Coord);
        StartHighlightPulse();
        Sfx.Play(SfxCue.Select, -10f);

        var info = PieceCatalog.Info(piece.Kind);
        EmitSignal(SignalName.InspectChanged, $"{info.Name.ToUpperInvariant()} — {info.Description}");

        // A blocked piece still selects (gold glow), but with no lit tiles the
        // tap reads as dead — say why.
        if (_movesBuffer.Count == 0)
            EmitSignal(SignalName.StatusNote, "NO LEGAL MOVES");
    }

    // Paint an enemy's legal destinations steel blue. Purely informational —
    // nothing is selected, so the next tap anywhere clears it. Uses the AI
    // scratch buffer so the memoized selection cache stays intact.
    private void ShowEnemyReach(BattlePiece enemy)
    {
        EndSelect();
        PieceRules.LegalMoves(enemy.Kind, PieceSide.Enemy, enemy.Coord, this, _aiMoves);
        for (int i = 0; i < _aiMoves.Count; i++)
        {
            var dest = _aiMoves[i];
            if (!_tiles.TryGetValue(dest, out var tile)) continue;
            _highlighted.Add(dest);
            tile.Mesh.MaterialOverride = EnemyReachMaterialShared;
        }
        var info = PieceCatalog.Info(enemy.Kind);
        EmitSignal(SignalName.InspectChanged, $"ENEMY {info.Name.ToUpperInvariant()} — {info.Description}");
        Sfx.Play(SfxCue.Select, -14f);
        Haptics.Tap(8);
    }

    private void EndSelect()
    {
        if (_selPiece != null && IsInstanceValid(_selPiece.Node))
            _selPiece.Node.MaterialOverride = PieceVisuals.MaterialFor(_selPiece.Side);
        _selPiece = null;
        HideSelectRing();
        ClearHighlights();
        EmitSignal(SignalName.InspectChanged, "");
    }

    // Public hook for the screen flow (e.g. Pause) so highlights/pulse don't
    // persist behind a menu.
    public void ClearSelection()
    {
        EndSelect();
        CancelDeploy();
    }

    private void ClearHighlights()
    {
        StopHighlightPulse();
        // Empty the set BEFORE repainting: RefreshTileVisual skips any coord
        // still in _highlighted ("selection paint wins"), so refreshing first
        // left every highlight stuck on the board forever.
        _coordScratch.Clear();
        foreach (var h in _highlighted) _coordScratch.Add(h);
        _highlighted.Clear();
        for (int i = 0; i < _coordScratch.Count; i++) RefreshTileVisual(_coordScratch[i]);
    }

    // Exact danger test: could any non-stunned enemy capture `dest` next turn,
    // with `piece` hypothetically relocated there? Zero-alloc (pooled scratch),
    // no RNG — the AI always takes an available capture.
    private bool IsDeathTile(BattlePiece piece, HexCoord dest)
    {
        _hypoFrom = piece.Coord;
        _hypoTo = dest;
        _hypoActive = true;
        bool danger = false;
        for (int i = 0; i < _pieces.Count && !danger; i++)
        {
            var e = _pieces[i];
            if (!e.Alive || e.Side != PieceSide.Enemy) continue;
            if (e.StunTurns > 0) continue;             // can't act next turn
            if (e.Coord == dest) continue;             // captured on landing
            PieceRules.LegalMoves(e.Kind, PieceSide.Enemy, e.Coord, this, _dangerScratch);
            for (int m = 0; m < _dangerScratch.Count; m++)
                if (_dangerScratch[m] == dest) { danger = true; break; }
        }
        _hypoActive = false;
        return danger;
    }

    // ----- Deploy ------------------------------------------------------------

    // Enter deploy mode for run.Reserve[reserveIndex]: highlight the legal
    // deploy tiles (empty playable home-half tiles; any empty tile as a
    // fallback). Deploying costs the player's action.
    public void BeginDeploy(int reserveIndex)
    {
        if (!_running || _run == null) return;
        if (reserveIndex < 0 || reserveIndex >= _run.Reserve.Count) return;
        EndSelect();
        CancelDeploy();
        _deployIndex = reserveIndex;

        int found = 0;
        for (int pass = 0; pass < 2 && found == 0; pass++)
        {
            foreach (var c in _active)
            {
                if (!IsPlayable(c) || _occupied.ContainsKey(c)) continue;
                if (pass == 0 && c.R < 1) continue;    // prefer the home half
                _highlighted.Add(c);
                if (_tiles.TryGetValue(c, out var tile))
                    tile.Mesh.MaterialOverride = HighlightMaterialShared;
                found++;
            }
        }
        if (found == 0)
        {
            // Nowhere to deploy: report the mode ended so the HUD disarms.
            _deployIndex = -1;
            EmitSignal(SignalName.DeployModeChanged, false);
            EmitSignal(SignalName.StatusNote, "NO ROOM TO DEPLOY");
            return;
        }
        StartHighlightPulse();
        EmitSignal(SignalName.DeployModeChanged, true);
        EmitSignal(SignalName.StatusNote, "TAP A LIT TILE TO DEPLOY");
    }

    public void CancelDeploy()
    {
        if (_deployIndex < 0) return;
        _deployIndex = -1;
        ClearHighlights();
        EmitSignal(SignalName.DeployModeChanged, false);
    }

    private void ExecuteDeploy(HexCoord coord)
    {
        int idx = _deployIndex;
        CancelDeploy();
        if (idx < 0 || idx >= _run.Reserve.Count) return;

        var kind = _run.Reserve[idx];
        _run.Reserve.RemoveAt(idx);
        SpawnPiece(kind, PieceSide.Player, coord);
        PlayLandingRing(coord, false);
        Sfx.Play(SfxCue.Move, -7f);
        Haptics.Tap(15);
        PromoteStrandedPawns();

        if (_run.TileUpgrades.TryGetValue(coord, out var up) && up == TileUpgradeKind.Blessed)
        {
            AddMoney(1);
            ShowMoneyPop(coord, 1);
            EmitSignal(SignalName.StatusNote, "BLESSED +1");
        }

        EmitArmyCounts();
        AfterPlayerAction();
    }

    // ----- Player move resolution ---------------------------------------------

    private void ExecuteMove(BattlePiece mover, HexCoord dest)
    {
        var from = mover.Coord;
        MovePieceTo(mover, dest, tween: true);
        _cacheStamp = -1;

        bool captured = TryCapturePlayerMove(mover, from, dest);
        PlayLandingRing(dest, captured);
        Haptics.Tap(captured ? 40 : 15);
        PromoteStrandedPawns();

        if (CheckBattleEnd()) return;
        AfterPlayerAction();
    }

    private void MovePieceTo(BattlePiece piece, HexCoord dest, bool tween, float delay = 0f)
    {
        _occupied.Remove(piece.Coord);
        piece.Coord = dest;
        _occupied[dest] = piece;
        if (piece.Node == null || !IsInstanceValid(piece.Node)) return;

        var target = HexLayout.ToWorld(dest, PieceY);
        if (!tween) { piece.Node.Position = target; return; }

        var move = CreateTween();
        if (delay > 0f) move.TweenInterval(delay);
        // The thud rides the (possibly delayed) move animation, not the logic.
        move.TweenCallback(Callable.From(() => Sfx.Play(SfxCue.Move, -7f)));
        move.TweenProperty(piece.Node, "position", target, 0.18f)
            .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);

        var squash = CreateTween();
        if (delay > 0f) squash.TweenInterval(delay);
        squash.TweenProperty(piece.Node, "scale", new Vector3(1.12f, 0.85f, 1.12f), 0.09f)
            .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
        squash.TweenProperty(piece.Node, "scale", Vector3.One, 0.09f)
            .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.In);
    }

    // Note: the mover has already been placed on dest, so the captured piece is
    // looked up from the pieces list, not the occupancy map.
    private bool TryCapturePlayerMove(BattlePiece mover, HexCoord from, HexCoord dest)
    {
        BattlePiece target = null;
        for (int i = 0; i < _pieces.Count; i++)
        {
            var p = _pieces[i];
            if (p.Alive && p != mover && p.Side == PieceSide.Enemy && p.Coord == dest) { target = p; break; }
        }
        if (target == null) return false;

        // Money: base value, Tax Collector's first-capture levy, tile bonuses,
        // Gambit dividends — all data-driven from the run.
        int money = PieceCatalog.ValueOf(target.Kind);
        if (_boss == BossModifier.TaxCollector && !_firstCaptureDone) money = 0;
        _firstCaptureDone = true;
        if (_run.TileUpgrades.TryGetValue(dest, out var up) && up == TileUpgradeKind.Gold)
        {
            money += 2;
            if (_run.Has(GambitKind.GoldenHarvest)) money += 2;
        }
        if (_boss == BossModifier.CrumbleCrown && _cracked.Contains(dest)) money += 2;
        if (mover.Kind == PieceKind.Rook && _run.Has(GambitKind.RookDividend)) money += 2;

        AddMoney(money);
        AddScore(Scoring.CaptureScore(target.Kind));
        _run.CapturesMade++;
        ShowMoneyPop(dest, money);
        KillPiece(target, playerLossCounts: false);
        PlayCaptureBurst(dest);

        // Knight Fork: stun every enemy adjacent to the landing hex.
        if (mover.Kind == PieceKind.Knight && _run.Has(GambitKind.KnightFork))
        {
            int stunned = 0;
            for (int i = 0; i < _pieces.Count; i++)
            {
                var p = _pieces[i];
                if (p.Alive && p.Side == PieceSide.Enemy && p.Coord.Distance(dest) == 1)
                {
                    p.StunTurns = 1;
                    RefreshPieceVisual(p);
                    stunned++;
                }
            }
            if (stunned > 0) EmitSignal(SignalName.StatusNote, "FORKED");
        }

        // Pawn promotion: capturing on the outer active ring upgrades the pawn.
        if (mover.Kind == PieceKind.Pawn && dest.DistanceFromOrigin() == _outerRadius)
            PromotePawn(mover);

        // Bishop Echo: once per battle, keep sliding one step past the capture.
        if (mover.Kind == PieceKind.Bishop && _run.Has(GambitKind.BishopEcho) && !_echoUsed)
            TryBishopEcho(mover, from, dest);

        EmitSignal(SignalName.EnemiesChanged, CountSide(PieceSide.Enemy));
        return true;
    }

    private void PromotePawn(BattlePiece pawn)
    {
        pawn.Kind = PickPromotionKind(pawn);
        if (IsInstanceValid(pawn.Node)) pawn.Node.Mesh = PieceVisuals.MeshFor(pawn.Kind);
        bool player = pawn.Side == PieceSide.Player;
        EmitSignal(SignalName.StatusNote, player
            ? $"PROMOTED: {PieceCatalog.NameOf(pawn.Kind).ToUpperInvariant()}"
            : $"ENEMY PROMOTED: {PieceCatalog.NameOf(pawn.Kind).ToUpperInvariant()}");
        if (player && _run != null && _run.Has(GambitKind.PawnAmbition))
        {
            AddMoney(3);
            ShowMoneyPop(pawn.Coord, 3);
        }
    }

    // Prefer a promotion kind that can actually move from this tile — a knight
    // on a collapsed radius-1 board has no legal leaps, and promoting into an
    // immobile piece would re-strand it.
    private PieceKind PickPromotionKind(BattlePiece pawn)
    {
        PieceKind[] options = { PieceKind.Knight, PieceKind.Rook, PieceKind.Bishop };
        var pick = options[_rng.Next(options.Length)];
        PieceRules.LegalMoves(pick, pawn.Side, pawn.Coord, this, _aiMoves);
        if (_aiMoves.Count > 0) return pick;
        for (int i = 0; i < options.Length; i++)
        {
            PieceRules.LegalMoves(options[i], pawn.Side, pawn.Coord, this, _aiMoves);
            if (_aiMoves.Count > 0) return options[i];
        }
        return PieceKind.Rook;
    }

    // A pawn whose every forward hex has left the board can never move again.
    // Promote it on the spot (standard chess semantics, both sides) — without
    // this, stuck pawns litter collapsed endgames and can make the last enemy
    // uncapturable, leaving the battle unresolvable.
    private void PromoteStrandedPawns()
    {
        for (int i = 0; i < _pieces.Count; i++)
        {
            var p = _pieces[i];
            if (!p.Alive || p.Kind != PieceKind.Pawn) continue;
            if (PieceRules.PawnStranded(p.Side, p.Coord, _active)) PromotePawn(p);
        }
    }

    private void TryBishopEcho(BattlePiece bishop, HexCoord from, HexCoord dest)
    {
        var delta = dest - from;
        foreach (var d in PieceRules.BishopDirs)
        {
            // delta must be a positive multiple of exactly one bishop diagonal.
            if (d.Q * delta.R != d.R * delta.Q) continue;
            bool positive = (d.Q != 0) ? (delta.Q / d.Q) > 0 : (delta.R / d.R) > 0;
            if (!positive) continue;

            var next = dest + d;
            if (!IsPlayable(next) || _occupied.ContainsKey(next)) return;
            _echoUsed = true;
            MovePieceTo(bishop, next, tween: true);
            EmitSignal(SignalName.StatusNote, "ECHO STEP");
            return;
        }
    }

    // ----- Turn advance: enemy action + crumble + lock timers ----------------

    private void AfterPlayerAction()
    {
        // The player can be left with no legal move and no possible deploy
        // (e.g. a lone pawn whose forward hexes left the active area). The
        // crumble only ticks on player actions, so without a pass rule the
        // battle would freeze forever. Auto-pass until an action opens up
        // (enemy movement or a collapse), with a paralysis guard so two
        // mutually stuck sides still resolve instead of looping.
        for (int pass = 0; ; pass++)
        {
            _stateStamp++;
            _cacheStamp = -1;

            if (_lockTurnsLeft > 0 && --_lockTurnsLeft == 0)
            {
                _locked.Clear();
                RefreshAllTileVisuals();
            }

            EnemyAct();
            if (CheckBattleEnd()) return;

            TickCrumble();
            if (CheckBattleEnd()) return;

            // Enemy arrivals and collapses can strand pawns; promote them now
            // so the next selection's danger marking sees their real threat.
            PromoteStrandedPawns();

            EmitArmyCounts();

            // Once the crumble is spent, a capture-free stretch means the
            // position can (or will) never resolve — adjudicate it.
            if (_outerRadius <= 1 && _cracked.Count == 0 && _crumbleTimer == 0
                && ++_stagnantActions >= StandoffActions)
            {
                ResolveStandoff();
                return;
            }

            if (PlayerHasAnyAction()) return;
            if (pass >= 64)
            {
                _running = false;
                EndSelect();
                SetThreat(false);
                EmitSignal(SignalName.StatusNote, "STALEMATE");
                EmitSignal(SignalName.BattleLost);
                return;
            }
            EmitSignal(SignalName.StatusNote, "NO MOVES — TURN PASSES");
        }
    }

    // True when the player can do something this turn: any piece with a legal
    // move, or a reserve piece with an empty playable tile to deploy onto.
    private bool PlayerHasAnyAction()
    {
        for (int i = 0; i < _pieces.Count; i++)
        {
            var p = _pieces[i];
            if (!p.Alive || p.Side != PieceSide.Player) continue;
            PieceRules.LegalMoves(p.Kind, PieceSide.Player, p.Coord, this, _aiMoves);
            if (_aiMoves.Count > 0) return true;
        }
        if (_run != null && _run.Reserve.Count > 0)
        {
            foreach (var c in _active)
                if (IsPlayable(c) && !_occupied.ContainsKey(c)) return true;
        }
        return false;
    }

    // Exactly one enemy piece acts per player action (chess-like alternation).
    // The choice itself is pure logic in EnemyPlanner; this method owns the
    // stun bookkeeping and hands the chosen action to the executor.
    private void EnemyAct()
    {
        bool acted = EnemyPlanner.ChooseAction(_pieces, this, _occupied, _rng, _aiMoves,
            out var piece, out var dest, out var capture);

        // Recover stunned enemies: they sat this turn out.
        for (int i = 0; i < _pieces.Count; i++)
        {
            var e = _pieces[i];
            if (e.Alive && e.Side == PieceSide.Enemy && e.StunTurns > 0)
            {
                e.StunTurns--;
                if (e.StunTurns == 0) RefreshPieceVisual(e);
            }
        }

        if (acted) ExecuteEnemyAction(piece, dest, capture);
    }

    private void ExecuteEnemyAction(BattlePiece enemy, HexCoord dest, bool capture)
    {
        if (capture && _occupied.TryGetValue(dest, out var victim) && victim.Side == PieceSide.Player)
        {
            // Royal Guard: the first attempt on your King each battle is blocked.
            if (victim.Kind == PieceKind.King && _run.Has(GambitKind.RoyalGuard) && !_royalGuardUsed)
            {
                _royalGuardUsed = true;
                EmitSignal(SignalName.StatusNote, "ROYAL GUARD");
                return;                                // attacker's turn is spent
            }
            // Shield tile: the first friendly piece on it ignores one capture.
            if (_run.TileUpgrades.TryGetValue(dest, out var up) && up == TileUpgradeKind.Shield
                && !_shieldConsumed.Contains(dest))
            {
                _shieldConsumed.Add(dest);
                EmitSignal(SignalName.StatusNote, "SHIELDED");
                return;
            }

            KillPiece(victim, playerLossCounts: true, fxDelay: EnemyStrikeDelay);
            PlayCaptureBurst(dest, EnemyStrikeDelay);
        }

        MovePieceTo(enemy, dest, tween: true, delay: EnemyMoveDelay);

        // Snare tile: an enemy landing here skips its next turn.
        if (_run.TileUpgrades.TryGetValue(dest, out var landUp) && landUp == TileUpgradeKind.Snare)
        {
            enemy.StunTurns = 1;
            RefreshPieceVisual(enemy);
            EmitSignal(SignalName.StatusNote, "SNARED");
        }
    }

    // Remove a piece from play. A player piece lost to the enemy or the crumble
    // may be saved once per battle by the Mercy Charter (returns to reserve).
    // fxDelay defers only the shrink visual (used when the attacker's own move
    // animation is delayed, so the victim vanishes as the attacker arrives).
    private void KillPiece(BattlePiece piece, bool playerLossCounts, float fxDelay = 0f)
    {
        _stagnantActions = 0;
        piece.Alive = false;
        _pieces.Remove(piece);
        // Identity check: on a player capture the mover has already claimed the
        // victim's coord in the map — don't evict the newly landed piece.
        if (_occupied.TryGetValue(piece.Coord, out var occ) && occ == piece)
            _occupied.Remove(piece.Coord);
        if (_selPiece == piece) EndSelect();

        if (playerLossCounts && piece.Side == PieceSide.Player && _run != null)
        {
            if (_run.Has(GambitKind.MercyCharter) && !_mercyUsed)
            {
                _mercyUsed = true;
                _run.Reserve.Add(piece.Kind);
                EmitSignal(SignalName.StatusNote, "MERCY: TO RESERVE");
            }
            else
            {
                _run.PiecesLost++;
            }
        }

        var node = piece.Node;
        if (node != null && IsInstanceValid(node))
        {
            var tween = CreateTween();
            if (fxDelay > 0f) tween.TweenInterval(fxDelay);
            tween.TweenProperty(node, "scale", Vector3.Zero, 0.13f)
                .SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.In);
            tween.TweenCallback(Callable.From(() =>
            {
                if (IsInstanceValid(node)) node.QueueFree();
            }));
        }
    }

    // ----- Crumble -------------------------------------------------------------

    // Each player action ticks the timer. At zero the outermost ring cracks
    // (telegraphed, still playable); CrackGraceActions later it collapses —
    // pieces on it die — and the next ring cracks. Radius <= 1 never crumbles.
    private void TickCrumble()
    {
        // While a ring is cracked, turnsLeft carries the actions until it
        // collapses so the HUD can show COLLAPSE IN N (the grace period was
        // previously invisible).
        if (_crumbleTimer > 0)
        {
            _crumbleTimer--;
            if (_crumbleTimer == 0 && _outerRadius >= 2)
            {
                CrackRing(_outerRadius);
                _crackCountdown = CrackGrace();
                EmitSignal(SignalName.CrumbleChanged, _crackCountdown, true);
                return;
            }
            EmitSignal(SignalName.CrumbleChanged, _crumbleTimer, _cracked.Count > 0);
            return;
        }

        if (_cracked.Count == 0) return;
        _crackCountdown--;
        if (_crackCountdown > 0)
        {
            EmitSignal(SignalName.CrumbleChanged, _crackCountdown, true);
            return;
        }

        CollapseCrackedRing();
        if (_outerRadius >= 2)
        {
            CrackRing(_outerRadius);
            _crackCountdown = CrackGrace();
            EmitSignal(SignalName.CrumbleChanged, _crackCountdown, true);
            return;
        }
        EmitSignal(SignalName.CrumbleChanged, 0, false);
    }

    // Actions a cracked ring holds before collapsing (Stonemason buys one more).
    private int CrackGrace() =>
        CrackGraceActions + (_run != null && _run.Has(GambitKind.Stonemason) ? 1 : 0);

    private void CrackRing(int radius)
    {
        _coordScratch.Clear();
        HexCoord.Ring(radius, _coordScratch);
        for (int i = 0; i < _coordScratch.Count; i++)
            if (_active.Contains(_coordScratch[i])) _cracked.Add(_coordScratch[i]);
        RefreshAllTileVisuals();
        SetThreat(true);
        Sfx.Play(SfxCue.Crack, -5f);
        EmitSignal(SignalName.StatusNote, "THE BOARD CRACKS");
    }

    private void CollapseCrackedRing()
    {
        foreach (var c in _cracked)
        {
            _active.Remove(c);
            if (_occupied.TryGetValue(c, out var piece))
                KillPiece(piece, playerLossCounts: true);
            if (_upgradeMarkers.TryGetValue(c, out var marker) && IsInstanceValid(marker))
                marker.Visible = false;
        }
        _cracked.Clear();
        _outerRadius--;
        _stateStamp++;
        _cacheStamp = -1;
        Sfx.Play(SfxCue.Collapse, -3f);
        RefreshAllTileVisuals();
        SetThreat(false);
        EmitSignal(SignalName.EnemiesChanged, CountSide(PieceSide.Enemy));
        EmitArmyCounts();
    }

    // ----- Win / loss ----------------------------------------------------------

    private bool CheckBattleEnd()
    {
        if (!_running) return true;

        // Loss is checked first: when one collapse wipes both armies at once,
        // a "win" here would hand the shop an empty army and the next battle
        // would start with nothing to move and hang forever.
        if (CountSide(PieceSide.Player) == 0 && (_run == null || _run.Reserve.Count == 0))
        {
            LoseBattle();
            return true;
        }

        if (CountSide(PieceSide.Enemy) == 0)
        {
            WinBattle();
            return true;
        }

        return false;
    }

    private void WinBattle()
    {
        _running = false;
        EndSelect();
        AddScore(Scoring.ClearScore(_run.Battle));
        int clearPay = Scoring.ClearPay(_run.Battle, _run.Has(GambitKind.Quartermaster));
        AddMoney(clearPay);
        ShowMoneyPop(HexCoord.Zero, clearPay);

        // The army going forward is whoever survived, in board order.
        _run.Army.Clear();
        for (int i = 0; i < _pieces.Count; i++)
            if (_pieces[i].Alive && _pieces[i].Side == PieceSide.Player)
                _run.Army.Add(_pieces[i].Kind);

        _run.Battle++;

        // Crossing the finish line rewards the army you kept alive.
        if (_run.Battle > RunState.FinalBattle && _run.Army.Count > 0)
        {
            int bonus = Scoring.SurvivorsBonus(_run.Army.Count);
            AddScore(bonus);
            EmitSignal(SignalName.StatusNote, $"SURVIVORS +{bonus}");
        }

        SetThreat(false);
        Sfx.Play(SfxCue.Win, -5f);
        EmitSignal(SignalName.BattleWon);
    }

    private void LoseBattle()
    {
        _running = false;
        EndSelect();
        SetThreat(false);
        Sfx.Play(SfxCue.Lose, -4f);
        EmitSignal(SignalName.BattleLost);
    }

    // Terminal-board standoff: the crumble is spent and no piece has been
    // captured for a long stretch — some endgames can never resolve by play
    // (a bishop can never attack an adjacent hex, so bishop-vs-bishop on the
    // collapsed 7-tile board is mutually uncapturable). Adjudicate by
    // remaining force, reserve included; the player wins ties.
    private void ResolveStandoff()
    {
        int player = 0, enemy = 0;
        for (int i = 0; i < _pieces.Count; i++)
        {
            var p = _pieces[i];
            if (!p.Alive) continue;
            if (p.Side == PieceSide.Player) player += PieceCatalog.ValueOf(p.Kind);
            else enemy += PieceCatalog.ValueOf(p.Kind);
        }
        if (_run != null)
            for (int i = 0; i < _run.Reserve.Count; i++)
                player += PieceCatalog.ValueOf(_run.Reserve[i]);

        EmitSignal(SignalName.StatusNote, "STANDOFF");
        if (player >= enemy) WinBattle();
        else LoseBattle();
    }

    // ----- Scoring / money -------------------------------------------------------

    private void AddScore(int delta)
    {
        if (_run == null) return;
        _run.Score += delta;
        EmitSignal(SignalName.ScoreChanged, _run.Score);
    }

    private void AddMoney(int delta)
    {
        if (_run == null || delta == 0) return;
        _run.Money += delta;
        if (delta > 0) _run.MoneyEarned += delta;
        EmitSignal(SignalName.MoneyChanged, _run.Money);
    }

    private void SetThreat(bool threat)
    {
        if (threat == _threat) return;
        _threat = threat;
        EmitSignal(SignalName.ThreatChanged, threat);
    }

    // Tile visuals and pooled FX live in HexBoard.Fx.cs.
}
