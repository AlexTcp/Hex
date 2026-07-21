// =============================================================================
// HexBoard.Fx — presentation-only half of the battle controller
// =============================================================================
// Purpose:
//   Everything in this partial paints or animates; nothing here mutates battle
//   state. Pooled FX nodes (landing ring, selection ring, capture spark burst,
//   floating money pop), the shared highlight-pulse tweens, and the tile/piece
//   material refreshers. Shared GPU resources live in TileVisuals/PieceVisuals;
//   this file only re-triggers them (hard rule 2).
//
// Interactions:
//   - HexBoard (main partial): battle resolution calls these hooks.
//   - ShopScreen (via ScreenManager): SetShopPreviewTile marks an offer's tile.
// =============================================================================

using Godot;
using HexGame.Chess;
using HexGame.Hex;
using static HexGame.Board.TileVisuals;

namespace HexGame.Board;

public partial class HexBoard
{
    // ----- FX (pooled) ------------------------------------------------------
    private Tween _pulseTween;
    private Tween _dangerPulseTween;
    private MeshInstance3D _ringNode;
    private Tween _ringTween;
    private MeshInstance3D _selectRingNode;
    // Small round-robin pools so multi-event actions render each event distinctly:
    // a capture spark + the ~0.32s-later counter-capture spark, and a capture pop +
    // the battle-clear centre-bonus pop (every battle clear) / Bishop-Echo double.
    private const int CaptureBurstPool = 4;
    private CpuParticles3D[] _captureBursts;
    private int _captureBurstNext;
    private const int MoneyPopPool = 5;
    private Label3D[] _moneyPops;
    private Tween[] _moneyPopTweens;
    private int _moneyPopNext;
    private HexCoord? _shopPreviewCoord;     // tile a shop offer would claim

    // ----- Highlight pulse ---------------------------------------------------

    // Two looped tweens drive the highlight materials at zero per-tile cost:
    // gold/copper breathe together; the danger red pulses at DOUBLE rate so the
    // death-tile warning is a rhythm cue, not only a colour (colour-blind safe).
    private void StartHighlightPulse()
    {
        _pulseTween?.Kill();
        var t = CreateTween().SetLoops();
        AddPulseLeg(t, 1.5f, 0.55f);
        AddPulseLeg(t, 0.7f, 0.55f);
        _pulseTween = t;

        _dangerPulseTween?.Kill();
        var d = CreateTween().SetLoops();
        d.TweenProperty(DangerHighlightMaterialShared, "emission_energy_multiplier", 1.6f, 0.275f)
            .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        d.TweenProperty(DangerHighlightMaterialShared, "emission_energy_multiplier", 0.6f, 0.275f)
            .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        _dangerPulseTween = d;
    }

    private void AddPulseLeg(Tween t, float energy, float dur)
    {
        t.TweenProperty(HighlightMaterialShared, "emission_energy_multiplier", energy, dur)
            .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        t.Parallel().TweenProperty(CaptureHighlightMaterialShared, "emission_energy_multiplier", energy, dur)
            .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
    }

    private void StopHighlightPulse()
    {
        if (_pulseTween != null) { _pulseTween.Kill(); _pulseTween = null; }
        if (_dangerPulseTween != null) { _dangerPulseTween.Kill(); _dangerPulseTween = null; }
        HighlightMaterialShared.EmissionEnergyMultiplier = 1f;
        CaptureHighlightMaterialShared.EmissionEnergyMultiplier = 1f;
        DangerHighlightMaterialShared.EmissionEnergyMultiplier = 1f;
    }

    // ----- Tile / piece visuals ----------------------------------------------

    // Shop hook: gold-mark the tile a tile-upgrade offer would claim, over the
    // frozen post-battle board, so the player sees exactly what they'd buy.
    public void SetShopPreviewTile(HexCoord? coord)
    {
        var prev = _shopPreviewCoord;
        _shopPreviewCoord = coord;
        if (prev.HasValue) RefreshTileVisual(prev.Value);
        if (coord.HasValue) RefreshTileVisual(coord.Value);
    }

    private void RefreshTileVisual(HexCoord coord)
    {
        if (!_tiles.TryGetValue(coord, out var tile)) return;
        if (_highlighted.Contains(coord)) return;     // selection paint wins
        if (_shopPreviewCoord == coord)
        {
            tile.Mesh.MaterialOverride = HighlightMaterialShared;
            return;
        }
        if (!_active.Contains(coord)) tile.Mesh.MaterialOverride = InactiveMaterial;
        else if (_locked.Contains(coord)) tile.Mesh.MaterialOverride = LockedMaterial;
        else if (_cracked.Contains(coord)) tile.Mesh.MaterialOverride = CrackedMaterial;
        else tile.Mesh.MaterialOverride = tile.BaseMaterial;
    }

    private void RefreshAllTileVisuals()
    {
        foreach (var kv in _tiles) RefreshTileVisual(kv.Key);
    }

    // Base material by state: stunned enemies read snare-purple so the player
    // can see which piece is skipping its turn (the selected glow still wins).
    private void RefreshPieceVisual(BattlePiece p)
    {
        if (p == _selPiece || p.Node == null || !IsInstanceValid(p.Node)) return;
        p.Node.MaterialOverride = p.Side == PieceSide.Enemy && p.StunTurns > 0
            ? PieceVisuals.StunnedMaterial
            : PieceVisuals.MaterialFor(p.Side);
    }

    // ----- FX (pooled nodes, re-triggered) ---------------------------------------

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
        _ringTween.Parallel().TweenProperty(RingMaterialShared, "albedo_color",
                new Color(ringColor.R, ringColor.G, ringColor.B, 0f), 0.28f)
            .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
        _ringTween.TweenCallback(Callable.From(() =>
        {
            if (IsInstanceValid(_ringNode)) _ringNode.Visible = false;
        }));
    }

    private void PositionSelectRing(HexCoord coord)
    {
        if (_selectRingNode == null)
        {
            _selectRingNode = new MeshInstance3D
            {
                Mesh = SharedSelectRingMesh,
                MaterialOverride = SelectRingMaterialShared,
            };
            AddChild(_selectRingNode);
        }
        _selectRingNode.Position = HexLayout.ToWorld(coord, RingY);
        _selectRingNode.Visible = true;
    }

    private void HideSelectRing()
    {
        if (_selectRingNode != null && IsInstanceValid(_selectRingNode))
            _selectRingNode.Visible = false;
    }

    // Floating "+$N" over the hex that earned it — payouts vary (piece value,
    // Gold tiles, gambits, boss bonuses) and the corner chip alone hides that.
    private void ShowMoneyPop(HexCoord coord, int amount)
    {
        if (amount <= 0) return;
        if (_moneyPops == null)
        {
            _moneyPops = new Label3D[MoneyPopPool];
            _moneyPopTweens = new Tween[MoneyPopPool];
            for (int i = 0; i < MoneyPopPool; i++)
            {
                var lbl = new Label3D
                {
                    Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                    FontSize = 96,
                    PixelSize = 0.005f,
                    OutlineSize = 24,
                    OutlineModulate = new Color(0, 0, 0, 0.85f),
                    NoDepthTest = true,
                    Visible = false,
                };
                AddChild(lbl);
                _moneyPops[i] = lbl;
            }
        }

        // Round-robin a free slot so concurrent payouts (capture + clear bonus,
        // Bishop Echo) each rise from their own hex instead of collapsing to one.
        int slot = _moneyPopNext;
        _moneyPopNext = (_moneyPopNext + 1) % MoneyPopPool;
        var pop = _moneyPops[slot];
        _moneyPopTweens[slot]?.Kill();

        Sfx.Play(SfxCue.Coin);
        pop.Text = $"+${amount}";
        pop.Position = HexLayout.ToWorld(coord, 0.6f);
        pop.Modulate = new Color(GoldColor.R, GoldColor.G, GoldColor.B, 1f);
        pop.Visible = true;

        var tween = CreateTween();
        tween.TweenProperty(pop, "position:y", 1.4f, 0.7f)
            .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
        tween.Parallel().TweenProperty(pop, "modulate:a", 0f, 0.7f)
            .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.In);
        tween.TweenCallback(Callable.From(() =>
        {
            if (IsInstanceValid(pop)) pop.Visible = false;
        }));
        _moneyPopTweens[slot] = tween;
    }

    private void PlayCaptureBurst(HexCoord coord, float delay = 0f)
    {
        if (delay > 0f)
        {
            var t = CreateTween();
            t.TweenInterval(delay);
            t.TweenCallback(Callable.From(() => PlayCaptureBurst(coord)));
            return;
        }
        Sfx.Play(SfxCue.Capture);
        if (_captureBursts == null)
        {
            _captureBursts = new CpuParticles3D[CaptureBurstPool];
            for (int i = 0; i < CaptureBurstPool; i++)
            {
                var ps = new CpuParticles3D
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
                AddChild(ps);
                _captureBursts[i] = ps;
            }
        }

        // Round-robin so a capture and its ~0.32s-later counter-capture don't
        // reposition + Restart() the same system mid-flight (spark teleport).
        var burst = _captureBursts[_captureBurstNext];
        _captureBurstNext = (_captureBurstNext + 1) % CaptureBurstPool;
        burst.Position = HexLayout.ToWorld(coord, PieceY + 0.3f);
        burst.Restart();
        burst.Emitting = true;
    }
}
