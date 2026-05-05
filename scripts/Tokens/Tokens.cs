// =============================================================================
// Tokens.cs — Concrete Token Subclasses
// =============================================================================
// Purpose:
//   Defines all 18 concrete Token pieces (Walker, Runner, Jumper, Knight,
//   Stepper, Spiral, Mirror, Ringwalk, Charger, Diamond, Orbit, Edge,
//   Anchor, Echo, Pivot, Skipper, Drifter, Shrine). Each sealed partial
//   class supplies its Id, DisplayName, Description, a LegalMoves rule
//   that produces destination HexCoords from a starting hex within the
//   board radius, and a CreateMesh / GetColor pair that gives the piece
//   its 3D appearance.
//
// Interactions:
//   - Token: every class here inherits from Token and implements its
//     abstract members (LegalMoves, CreateMesh, GetColor) plus uses the
//     inherited Filter helper to clamp moves to the board.
//   - HexCoord: used throughout for movement math — HexCoord.Directions,
//     HexCoord.Ring(n), HexCoord.Within(n), HexCoord.Zero, plus arithmetic
//     and DistanceFromOrigin().
//   - TokenCatalog: registers factory delegates for each subclass declared
//     in this file.
//   - HexBoard: hosts whichever Token subclass is currently active and
//     consumes its LegalMoves output to highlight tiles.
// =============================================================================

using System.Collections.Generic;
using Godot;
using HexGame.Hex;

namespace HexGame.Tokens;

public sealed partial class Walker : Token
{
    public override string Id => "walker";
    public override string DisplayName => "Walker";
    public override string Description => "Steps to any adjacent hex.";

    public override IEnumerable<HexCoord> LegalMoves(HexCoord from, int boardRadius)
    {
        var moves = new List<HexCoord>();
        foreach (var d in HexCoord.Directions) moves.Add(from + d);
        return Filter(moves, from, boardRadius);
    }

    protected override Mesh CreateMesh() => new CylinderMesh { TopRadius = 0.22f, BottomRadius = 0.28f, Height = 0.45f };
    protected override Color GetColor() => Colors.White;
}

public sealed partial class Runner : Token
{
    public override string Id => "runner";
    public override string DisplayName => "Runner";
    public override string Description => "Slides any number of hexes in one of six directions.";

    public override IEnumerable<HexCoord> LegalMoves(HexCoord from, int boardRadius)
    {
        var moves = new List<HexCoord>();
        foreach (var d in HexCoord.Directions)
        {
            var pos = from + d;
            while (pos.DistanceFromOrigin() <= boardRadius)
            {
                moves.Add(pos);
                pos += d;
            }
        }
        return Filter(moves, from, boardRadius);
    }

    protected override Mesh CreateMesh() => new CylinderMesh { TopRadius = 0.18f, BottomRadius = 0.22f, Height = 0.85f };
    protected override Color GetColor() => new Color(0.2f, 0.55f, 1f);
}

public sealed partial class Jumper : Token
{
    public override string Id => "jumper";
    public override string DisplayName => "Jumper";
    public override string Description => "Teleports to any hex exactly 2 away.";

    public override IEnumerable<HexCoord> LegalMoves(HexCoord from, int boardRadius)
    {
        var moves = new List<HexCoord>();
        foreach (var h in HexCoord.Ring(2)) moves.Add(from + h);
        return Filter(moves, from, boardRadius);
    }

    protected override Mesh CreateMesh() => new SphereMesh { Radius = 0.28f, Height = 0.56f };
    protected override Color GetColor() => new Color(1f, 0.25f, 0.25f);
}

public sealed partial class Knight : Token
{
    public override string Id => "knight";
    public override string DisplayName => "Knight";
    public override string Description => "Hex-L: 2 in one direction, 1 in an adjacent direction.";

    public override IEnumerable<HexCoord> LegalMoves(HexCoord from, int boardRadius)
    {
        var moves = new List<HexCoord>();
        var dirs = HexCoord.Directions;
        for (int i = 0; i < 6; i++)
        {
            var d1 = dirs[i];
            var d2a = dirs[(i + 1) % 6];
            var d2b = dirs[(i + 5) % 6];
            moves.Add(from + d1 * 2 + d2a);
            moves.Add(from + d1 * 2 + d2b);
        }
        return Filter(moves, from, boardRadius);
    }

    protected override Mesh CreateMesh() => new BoxMesh { Size = new Vector3(0.45f, 0.45f, 0.45f) };
    protected override Color GetColor() => new Color(1f, 0.85f, 0.1f);
}

public sealed partial class Stepper : Token
{
    public override string Id => "stepper";
    public override string DisplayName => "Stepper";
    public override string Description => "Lands exactly 3 hexes away in a straight line.";

    public override IEnumerable<HexCoord> LegalMoves(HexCoord from, int boardRadius)
    {
        var moves = new List<HexCoord>();
        foreach (var d in HexCoord.Directions) moves.Add(from + d * 3);
        return Filter(moves, from, boardRadius);
    }

    protected override Mesh CreateMesh() => new CylinderMesh { TopRadius = 0.0f, BottomRadius = 0.3f, Height = 0.6f };
    protected override Color GetColor() => new Color(0.3f, 0.85f, 0.35f);
}

public sealed partial class Spiral : Token
{
    public override string Id => "spiral";
    public override string DisplayName => "Spiral";
    public override string Description => "Any hex within distance 2.";

    public override IEnumerable<HexCoord> LegalMoves(HexCoord from, int boardRadius)
    {
        var moves = new List<HexCoord>();
        foreach (var h in HexCoord.Within(2)) moves.Add(from + h);
        return Filter(moves, from, boardRadius);
    }

    protected override Mesh CreateMesh() => new TorusMesh { InnerRadius = 0.15f, OuterRadius = 0.32f };
    protected override Color GetColor() => new Color(1f, 0.2f, 0.85f);
}

public sealed partial class Mirror : Token
{
    public override string Id => "mirror";
    public override string DisplayName => "Mirror";
    public override string Description => "Reflects through the board centre.";

    public override IEnumerable<HexCoord> LegalMoves(HexCoord from, int boardRadius)
    {
        var moves = new List<HexCoord> { -from };
        return Filter(moves, from, boardRadius);
    }

    protected override Mesh CreateMesh() => new CapsuleMesh { Radius = 0.2f, Height = 0.7f };
    protected override Color GetColor() => new Color(0.2f, 0.95f, 0.95f);
}

public sealed partial class Ringwalk : Token
{
    public override string Id => "ringwalk";
    public override string DisplayName => "Ringwalk";
    public override string Description => "Any hex on the same ring distance from centre.";

    public override IEnumerable<HexCoord> LegalMoves(HexCoord from, int boardRadius)
    {
        var moves = new List<HexCoord>();
        int ring = from.DistanceFromOrigin();
        if (ring == 0) return moves;
        foreach (var h in HexCoord.Ring(ring)) moves.Add(h);
        return Filter(moves, from, boardRadius);
    }

    protected override Mesh CreateMesh() => new BoxMesh { Size = new Vector3(0.28f, 0.75f, 0.28f) };
    protected override Color GetColor() => new Color(1f, 0.55f, 0.1f);
}

public sealed partial class Charger : Token
{
    public override string Id => "charger";
    public override string DisplayName => "Charger";
    public override string Description => "Charges exactly 2 hexes in a straight line.";

    public override IEnumerable<HexCoord> LegalMoves(HexCoord from, int boardRadius)
    {
        var moves = new List<HexCoord>();
        foreach (var d in HexCoord.Directions) moves.Add(from + d * 2);
        return Filter(moves, from, boardRadius);
    }

    protected override Mesh CreateMesh() => new PrismMesh { Size = new Vector3(0.5f, 0.6f, 0.4f) };
    protected override Color GetColor() => new Color(0.95f, 0.4f, 0.1f);
}

public sealed partial class Diamond : Token
{
    public override string Id => "diamond";
    public override string DisplayName => "Diamond";
    public override string Description => "Slips to one of six diagonal hexes between directions.";

    public override IEnumerable<HexCoord> LegalMoves(HexCoord from, int boardRadius)
    {
        var moves = new List<HexCoord>();
        var dirs = HexCoord.Directions;
        for (int i = 0; i < 6; i++)
            moves.Add(from + dirs[i] + dirs[(i + 1) % 6]);
        return Filter(moves, from, boardRadius);
    }

    protected override Mesh CreateMesh() => new BoxMesh { Size = new Vector3(0.32f, 0.55f, 0.32f) };
    protected override Color GetColor() => new Color(0.4f, 0.75f, 1f);
}

public sealed partial class Orbit : Token
{
    public override string Id => "orbit";
    public override string DisplayName => "Orbit";
    public override string Description => "Steps to any hex one ring closer or farther from the centre.";

    public override IEnumerable<HexCoord> LegalMoves(HexCoord from, int boardRadius)
    {
        var moves = new List<HexCoord>();
        int ring = from.DistanceFromOrigin();
        if (ring - 1 >= 0)
            foreach (var h in HexCoord.Ring(ring - 1)) moves.Add(h);
        if (ring + 1 <= boardRadius)
            foreach (var h in HexCoord.Ring(ring + 1)) moves.Add(h);
        return Filter(moves, from, boardRadius);
    }

    protected override Mesh CreateMesh() => new TorusMesh { InnerRadius = 0.22f, OuterRadius = 0.4f };
    protected override Color GetColor() => new Color(0.7f, 0.3f, 0.95f);
}

public sealed partial class Edge : Token
{
    public override string Id => "edge";
    public override string DisplayName => "Edge";
    public override string Description => "Teleports to any hex on the outer boundary.";

    public override IEnumerable<HexCoord> LegalMoves(HexCoord from, int boardRadius)
    {
        var moves = new List<HexCoord>();
        if (boardRadius > 0)
            foreach (var h in HexCoord.Ring(boardRadius)) moves.Add(h);
        return Filter(moves, from, boardRadius);
    }

    protected override Mesh CreateMesh() => new BoxMesh { Size = new Vector3(0.6f, 0.18f, 0.6f) };
    protected override Color GetColor() => new Color(0.7f, 0.78f, 0.85f);
}

public sealed partial class Anchor : Token
{
    public override string Id => "anchor";
    public override string DisplayName => "Anchor";
    public override string Description => "Pulls to any hex adjacent to the centre.";

    public override IEnumerable<HexCoord> LegalMoves(HexCoord from, int boardRadius)
    {
        var moves = new List<HexCoord>();
        foreach (var h in HexCoord.Ring(1)) moves.Add(h);
        return Filter(moves, from, boardRadius);
    }

    protected override Mesh CreateMesh() => new CapsuleMesh { Radius = 0.28f, Height = 0.5f };
    protected override Color GetColor() => new Color(0.15f, 0.25f, 0.65f);
}

public sealed partial class Echo : Token
{
    public override string Id => "echo";
    public override string DisplayName => "Echo";
    public override string Description => "Reflects across one of the three hex axes.";

    public override IEnumerable<HexCoord> LegalMoves(HexCoord from, int boardRadius)
    {
        var moves = new List<HexCoord>
        {
            new(from.Q, -from.Q - from.R),
            new(-from.Q - from.R, from.R),
            new(from.R, from.Q),
        };
        return Filter(moves, from, boardRadius);
    }

    protected override Mesh CreateMesh() => new PrismMesh { Size = new Vector3(0.5f, 0.35f, 0.5f) };
    protected override Color GetColor() => new Color(1f, 0.95f, 0.5f);
}

public sealed partial class Pivot : Token
{
    public override string Id => "pivot";
    public override string DisplayName => "Pivot";
    public override string Description => "Rotates around the centre to one of five positions.";

    public override IEnumerable<HexCoord> LegalMoves(HexCoord from, int boardRadius)
    {
        int q = from.Q, r = from.R;
        var moves = new List<HexCoord>
        {
            new(-r, q + r),
            new(-q - r, q),
            new(-q, -r),
            new(r, -q - r),
            new(q + r, -q),
        };
        return Filter(moves, from, boardRadius);
    }

    protected override Mesh CreateMesh() => new TorusMesh { InnerRadius = 0.05f, OuterRadius = 0.35f };
    protected override Color GetColor() => new Color(0.2f, 0.75f, 0.6f);
}

public sealed partial class Skipper : Token
{
    public override string Id => "skipper";
    public override string DisplayName => "Skipper";
    public override string Description => "Lands exactly 4 hexes away in a straight line.";

    public override IEnumerable<HexCoord> LegalMoves(HexCoord from, int boardRadius)
    {
        var moves = new List<HexCoord>();
        foreach (var d in HexCoord.Directions) moves.Add(from + d * 4);
        return Filter(moves, from, boardRadius);
    }

    protected override Mesh CreateMesh() => new CylinderMesh { TopRadius = 0.12f, BottomRadius = 0.12f, Height = 1.0f };
    protected override Color GetColor() => new Color(0.2f, 0.55f, 0.25f);
}

public sealed partial class Drifter : Token
{
    public override string Id => "drifter";
    public override string DisplayName => "Drifter";
    public override string Description => "Drifts to any hex within distance 3.";

    public override IEnumerable<HexCoord> LegalMoves(HexCoord from, int boardRadius)
    {
        var moves = new List<HexCoord>();
        foreach (var h in HexCoord.Within(3)) moves.Add(from + h);
        return Filter(moves, from, boardRadius);
    }

    protected override Mesh CreateMesh() => new SphereMesh { Radius = 0.34f, Height = 0.5f };
    protected override Color GetColor() => new Color(1f, 0.7f, 0.8f);
}

public sealed partial class Shrine : Token
{
    public override string Id => "shrine";
    public override string DisplayName => "Shrine";
    public override string Description => "Returns to the centre hex.";

    public override IEnumerable<HexCoord> LegalMoves(HexCoord from, int boardRadius)
    {
        var moves = new List<HexCoord> { HexCoord.Zero };
        return Filter(moves, from, boardRadius);
    }

    protected override Mesh CreateMesh() => new BoxMesh { Size = new Vector3(0.5f, 0.4f, 0.5f) };
    protected override Color GetColor() => new Color(0.65f, 0.1f, 0.1f);
}
