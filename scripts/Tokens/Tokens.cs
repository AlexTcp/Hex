// =============================================================================
// Tokens.cs — Concrete Token Subclasses
// =============================================================================
// Purpose:
//   Defines all 18 concrete Token pieces (Walker, Runner, Jumper, Knight,
//   Stepper, Spiral, Mirror, Ringwalk, Charger, Diamond, Orbit, Edge,
//   Anchor, Echo, Pivot, Skipper, Drifter, Shrine). Each sealed partial
//   class supplies its Id, a LegalMoves rule that fills a caller-supplied
//   buffer with destination HexCoords from a starting hex within the
//   board radius, and a CreateMesh / GetColor pair that gives the piece
//   its 3D appearance. Display names and descriptions live in TokenCatalog.
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

    public override void LegalMoves(HexCoord from, int boardRadius, List<HexCoord> output)
    {
        output.Clear();
        foreach (var d in HexCoord.Directions) output.Add(from + d);
        Filter(output, from, boardRadius);
    }

    protected override Mesh CreateMesh() => new CylinderMesh { TopRadius = 0.22f, BottomRadius = 0.28f, Height = 0.45f };
    protected override Color GetColor() => Colors.White;
}

public sealed partial class Runner : Token
{
    public override string Id => "runner";

    public override void LegalMoves(HexCoord from, int boardRadius, List<HexCoord> output)
    {
        output.Clear();
        foreach (var d in HexCoord.Directions)
        {
            var pos = from + d;
            int dist = 1;
            while (dist <= boardRadius)
            {
                output.Add(pos);
                pos += d;
                dist++;
            }
        }
        Filter(output, from, boardRadius);
    }

    protected override Mesh CreateMesh() => new CylinderMesh { TopRadius = 0.18f, BottomRadius = 0.22f, Height = 0.85f };
    protected override Color GetColor() => new Color(0.2f, 0.55f, 1f);
}

public sealed partial class Jumper : Token
{
    public override string Id => "jumper";

    public override void LegalMoves(HexCoord from, int boardRadius, List<HexCoord> output)
    {
        output.Clear();
        int start = output.Count;
        HexCoord.Ring(2, output);
        for (int i = start; i < output.Count; i++) output[i] = from + output[i];
        Filter(output, from, boardRadius);
    }

    protected override Mesh CreateMesh() => new SphereMesh { Radius = 0.28f, Height = 0.56f };
    protected override Color GetColor() => new Color(1f, 0.25f, 0.25f);
}

public sealed partial class Knight : Token
{
    public override string Id => "knight";

    public override void LegalMoves(HexCoord from, int boardRadius, List<HexCoord> output)
    {
        output.Clear();
        var dirs = HexCoord.Directions;
        for (int i = 0; i < 6; i++)
        {
            var d1 = dirs[i];
            var d2a = dirs[(i + 1) % 6];
            var d2b = dirs[(i + 5) % 6];
            output.Add(from + d1 * 2 + d2a);
            output.Add(from + d1 * 2 + d2b);
        }
        Filter(output, from, boardRadius);
    }

    protected override Mesh CreateMesh() => new BoxMesh { Size = new Vector3(0.45f, 0.45f, 0.45f) };
    protected override Color GetColor() => new Color(1f, 0.85f, 0.1f);
}

public sealed partial class Stepper : Token
{
    public override string Id => "stepper";

    public override void LegalMoves(HexCoord from, int boardRadius, List<HexCoord> output)
    {
        output.Clear();
        foreach (var d in HexCoord.Directions) output.Add(from + d * 3);
        Filter(output, from, boardRadius);
    }

    protected override Mesh CreateMesh() => new CylinderMesh { TopRadius = 0.0f, BottomRadius = 0.3f, Height = 0.6f };
    protected override Color GetColor() => new Color(0.3f, 0.85f, 0.35f);
}

public sealed partial class Spiral : Token
{
    public override string Id => "spiral";

    public override void LegalMoves(HexCoord from, int boardRadius, List<HexCoord> output)
    {
        output.Clear();
        HexCoord.Within(2, output);
        for (int i = 0; i < output.Count; i++) output[i] = from + output[i];
        Filter(output, from, boardRadius);
    }

    protected override Mesh CreateMesh() => new TorusMesh { InnerRadius = 0.15f, OuterRadius = 0.32f };
    protected override Color GetColor() => new Color(1f, 0.2f, 0.85f);
}

public sealed partial class Mirror : Token
{
    public override string Id => "mirror";

    public override void LegalMoves(HexCoord from, int boardRadius, List<HexCoord> output)
    {
        output.Clear();
        output.Add(-from);
        Filter(output, from, boardRadius);
    }

    protected override Mesh CreateMesh() => new CapsuleMesh { Radius = 0.2f, Height = 0.7f };
    protected override Color GetColor() => new Color(0.2f, 0.95f, 0.95f);
}

public sealed partial class Ringwalk : Token
{
    public override string Id => "ringwalk";

    public override void LegalMoves(HexCoord from, int boardRadius, List<HexCoord> output)
    {
        output.Clear();
        int ring = from.DistanceFromOrigin();
        if (ring == 0) return;
        HexCoord.Ring(ring, output);
        Filter(output, from, boardRadius);
    }

    protected override Mesh CreateMesh() => new BoxMesh { Size = new Vector3(0.28f, 0.75f, 0.28f) };
    protected override Color GetColor() => new Color(1f, 0.55f, 0.1f);
}

public sealed partial class Charger : Token
{
    public override string Id => "charger";

    public override void LegalMoves(HexCoord from, int boardRadius, List<HexCoord> output)
    {
        output.Clear();
        foreach (var d in HexCoord.Directions) output.Add(from + d * 2);
        Filter(output, from, boardRadius);
    }

    protected override Mesh CreateMesh() => new PrismMesh { Size = new Vector3(0.5f, 0.6f, 0.4f) };
    protected override Color GetColor() => new Color(0.95f, 0.4f, 0.1f);
}

public sealed partial class Diamond : Token
{
    public override string Id => "diamond";

    public override void LegalMoves(HexCoord from, int boardRadius, List<HexCoord> output)
    {
        output.Clear();
        var dirs = HexCoord.Directions;
        for (int i = 0; i < 6; i++)
            output.Add(from + dirs[i] + dirs[(i + 1) % 6]);
        Filter(output, from, boardRadius);
    }

    protected override Mesh CreateMesh() => new BoxMesh { Size = new Vector3(0.32f, 0.55f, 0.32f) };
    protected override Color GetColor() => new Color(0.4f, 0.75f, 1f);
}

public sealed partial class Orbit : Token
{
    public override string Id => "orbit";

    public override void LegalMoves(HexCoord from, int boardRadius, List<HexCoord> output)
    {
        output.Clear();
        int ring = from.DistanceFromOrigin();
        if (ring - 1 >= 0)
            HexCoord.Ring(ring - 1, output);
        if (ring + 1 <= boardRadius)
            HexCoord.Ring(ring + 1, output);
        Filter(output, from, boardRadius);
    }

    protected override Mesh CreateMesh() => new TorusMesh { InnerRadius = 0.22f, OuterRadius = 0.4f };
    protected override Color GetColor() => new Color(0.7f, 0.3f, 0.95f);
}

public sealed partial class Edge : Token
{
    public override string Id => "edge";

    public override void LegalMoves(HexCoord from, int boardRadius, List<HexCoord> output)
    {
        output.Clear();
        if (boardRadius > 0)
            HexCoord.Ring(boardRadius, output);
        Filter(output, from, boardRadius);
    }

    protected override Mesh CreateMesh() => new BoxMesh { Size = new Vector3(0.6f, 0.18f, 0.6f) };
    protected override Color GetColor() => new Color(0.7f, 0.78f, 0.85f);
}

public sealed partial class Anchor : Token
{
    public override string Id => "anchor";

    public override void LegalMoves(HexCoord from, int boardRadius, List<HexCoord> output)
    {
        output.Clear();
        HexCoord.Ring(1, output);
        Filter(output, from, boardRadius);
    }

    protected override Mesh CreateMesh() => new CapsuleMesh { Radius = 0.28f, Height = 0.5f };
    protected override Color GetColor() => new Color(0.15f, 0.25f, 0.65f);
}

public sealed partial class Echo : Token
{
    public override string Id => "echo";

    public override void LegalMoves(HexCoord from, int boardRadius, List<HexCoord> output)
    {
        output.Clear();
        output.Add(new HexCoord(from.Q, -from.Q - from.R));
        output.Add(new HexCoord(-from.Q - from.R, from.R));
        output.Add(new HexCoord(from.R, from.Q));
        Filter(output, from, boardRadius);
    }

    protected override Mesh CreateMesh() => new PrismMesh { Size = new Vector3(0.5f, 0.35f, 0.5f) };
    protected override Color GetColor() => new Color(1f, 0.95f, 0.5f);
}

public sealed partial class Pivot : Token
{
    public override string Id => "pivot";

    public override void LegalMoves(HexCoord from, int boardRadius, List<HexCoord> output)
    {
        output.Clear();
        int q = from.Q, r = from.R;
        output.Add(new HexCoord(-r, q + r));
        output.Add(new HexCoord(-q - r, q));
        output.Add(new HexCoord(-q, -r));
        output.Add(new HexCoord(r, -q - r));
        output.Add(new HexCoord(q + r, -q));
        Filter(output, from, boardRadius);
    }

    protected override Mesh CreateMesh() => new TorusMesh { InnerRadius = 0.05f, OuterRadius = 0.35f };
    protected override Color GetColor() => new Color(0.2f, 0.75f, 0.6f);
}

public sealed partial class Skipper : Token
{
    public override string Id => "skipper";

    public override void LegalMoves(HexCoord from, int boardRadius, List<HexCoord> output)
    {
        output.Clear();
        foreach (var d in HexCoord.Directions) output.Add(from + d * 4);
        Filter(output, from, boardRadius);
    }

    protected override Mesh CreateMesh() => new CylinderMesh { TopRadius = 0.12f, BottomRadius = 0.12f, Height = 1.0f };
    protected override Color GetColor() => new Color(0.2f, 0.55f, 0.25f);
}

public sealed partial class Drifter : Token
{
    public override string Id => "drifter";

    public override void LegalMoves(HexCoord from, int boardRadius, List<HexCoord> output)
    {
        output.Clear();
        HexCoord.Within(3, output);
        for (int i = 0; i < output.Count; i++) output[i] = from + output[i];
        Filter(output, from, boardRadius);
    }

    protected override Mesh CreateMesh() => new SphereMesh { Radius = 0.34f, Height = 0.5f };
    protected override Color GetColor() => new Color(1f, 0.7f, 0.8f);
}

public sealed partial class Shrine : Token
{
    public override string Id => "shrine";

    public override void LegalMoves(HexCoord from, int boardRadius, List<HexCoord> output)
    {
        output.Clear();
        output.Add(HexCoord.Zero);
        Filter(output, from, boardRadius);
    }

    protected override Mesh CreateMesh() => new BoxMesh { Size = new Vector3(0.5f, 0.4f, 0.5f) };
    protected override Color GetColor() => new Color(0.65f, 0.1f, 0.1f);
}
