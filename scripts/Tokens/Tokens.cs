// =============================================================================
// Tokens.cs — Concrete Token Subclasses
// =============================================================================
// Purpose:
//   Defines the 14 concrete Token pieces (Walker, Runner, Jumper, Halo,
//   Knight, Camel, Stepper, Hopper, Spiral, Charger, Diamond, Glider,
//   Skipper, Drifter). Each sealed partial class supplies its Id, a
//   LegalMoves rule that fills a caller-supplied buffer with destination
//   HexCoords from a starting hex within the board radius, and a
//   CreateMesh / GetColor pair that gives the piece its 3D appearance.
//   Display names and descriptions live in TokenCatalog.
//
// Interactions:
//   - Token: every class here inherits from Token and implements its
//     abstract members (LegalMoves, CreateMesh, GetColor) plus uses the
//     inherited Filter helper to clamp moves to the board.
//   - HexCoord: used throughout for movement math — HexCoord.Directions,
//     HexCoord.Ring(n), HexCoord.Within(n), plus arithmetic.
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

    private static readonly Mesh SharedMesh = new CylinderMesh { TopRadius = 0.22f, BottomRadius = 0.28f, Height = 0.45f };
    private static readonly StandardMaterial3D SharedMaterial = MakeMaterial(Colors.White);
    protected override Mesh GetSharedMesh() => SharedMesh;
    protected override StandardMaterial3D GetSharedMaterial() => SharedMaterial;
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

    private static readonly Mesh SharedMesh = new CylinderMesh { TopRadius = 0.18f, BottomRadius = 0.22f, Height = 0.85f };
    private static readonly StandardMaterial3D SharedMaterial = MakeMaterial(new Color(0.2f, 0.55f, 1f));
    protected override Mesh GetSharedMesh() => SharedMesh;
    protected override StandardMaterial3D GetSharedMaterial() => SharedMaterial;
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

    private static readonly Mesh SharedMesh = new SphereMesh { Radius = 0.28f, Height = 0.56f };
    private static readonly StandardMaterial3D SharedMaterial = MakeMaterial(new Color(1f, 0.25f, 0.25f));
    protected override Mesh GetSharedMesh() => SharedMesh;
    protected override StandardMaterial3D GetSharedMaterial() => SharedMaterial;
}

public sealed partial class Halo : Token
{
    public override string Id => "halo";

    public override void LegalMoves(HexCoord from, int boardRadius, List<HexCoord> output)
    {
        output.Clear();
        int start = output.Count;
        HexCoord.Ring(3, output);
        for (int i = start; i < output.Count; i++) output[i] = from + output[i];
        Filter(output, from, boardRadius);
    }

    private static readonly Mesh SharedMesh = new TorusMesh { InnerRadius = 0.28f, OuterRadius = 0.4f };
    private static readonly StandardMaterial3D SharedMaterial = MakeMaterial(new Color(0.65f, 0.5f, 0.95f));
    protected override Mesh GetSharedMesh() => SharedMesh;
    protected override StandardMaterial3D GetSharedMaterial() => SharedMaterial;
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

    private static readonly Mesh SharedMesh = new BoxMesh { Size = new Vector3(0.45f, 0.45f, 0.45f) };
    private static readonly StandardMaterial3D SharedMaterial = MakeMaterial(new Color(1f, 0.85f, 0.1f));
    protected override Mesh GetSharedMesh() => SharedMesh;
    protected override StandardMaterial3D GetSharedMaterial() => SharedMaterial;
}

public sealed partial class Camel : Token
{
    public override string Id => "camel";

    public override void LegalMoves(HexCoord from, int boardRadius, List<HexCoord> output)
    {
        output.Clear();
        var dirs = HexCoord.Directions;
        for (int i = 0; i < 6; i++)
        {
            var d1 = dirs[i];
            var d2a = dirs[(i + 1) % 6];
            var d2b = dirs[(i + 5) % 6];
            output.Add(from + d1 * 3 + d2a);
            output.Add(from + d1 * 3 + d2b);
        }
        Filter(output, from, boardRadius);
    }

    private static readonly Mesh SharedMesh = new BoxMesh { Size = new Vector3(0.55f, 0.35f, 0.55f) };
    private static readonly StandardMaterial3D SharedMaterial = MakeMaterial(new Color(0.85f, 0.7f, 0.45f));
    protected override Mesh GetSharedMesh() => SharedMesh;
    protected override StandardMaterial3D GetSharedMaterial() => SharedMaterial;
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

    private static readonly Mesh SharedMesh = new CylinderMesh { TopRadius = 0.0f, BottomRadius = 0.3f, Height = 0.6f };
    private static readonly StandardMaterial3D SharedMaterial = MakeMaterial(new Color(0.3f, 0.85f, 0.35f));
    protected override Mesh GetSharedMesh() => SharedMesh;
    protected override StandardMaterial3D GetSharedMaterial() => SharedMaterial;
}

public sealed partial class Hopper : Token
{
    public override string Id => "hopper";

    public override void LegalMoves(HexCoord from, int boardRadius, List<HexCoord> output)
    {
        output.Clear();
        foreach (var d in HexCoord.Directions)
        {
            output.Add(from + d);
            output.Add(from + d * 3);
        }
        Filter(output, from, boardRadius);
    }

    private static readonly Mesh SharedMesh = new CapsuleMesh { Radius = 0.18f, Height = 0.55f };
    private static readonly StandardMaterial3D SharedMaterial = MakeMaterial(new Color(1f, 0.5f, 0.4f));
    protected override Mesh GetSharedMesh() => SharedMesh;
    protected override StandardMaterial3D GetSharedMaterial() => SharedMaterial;
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

    private static readonly Mesh SharedMesh = new TorusMesh { InnerRadius = 0.15f, OuterRadius = 0.32f };
    private static readonly StandardMaterial3D SharedMaterial = MakeMaterial(new Color(1f, 0.2f, 0.85f));
    protected override Mesh GetSharedMesh() => SharedMesh;
    protected override StandardMaterial3D GetSharedMaterial() => SharedMaterial;
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

    private static readonly Mesh SharedMesh = new PrismMesh { Size = new Vector3(0.5f, 0.6f, 0.4f) };
    private static readonly StandardMaterial3D SharedMaterial = MakeMaterial(new Color(0.95f, 0.4f, 0.1f));
    protected override Mesh GetSharedMesh() => SharedMesh;
    protected override StandardMaterial3D GetSharedMaterial() => SharedMaterial;
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

    private static readonly Mesh SharedMesh = new BoxMesh { Size = new Vector3(0.32f, 0.55f, 0.32f) };
    private static readonly StandardMaterial3D SharedMaterial = MakeMaterial(new Color(0.4f, 0.75f, 1f));
    protected override Mesh GetSharedMesh() => SharedMesh;
    protected override StandardMaterial3D GetSharedMaterial() => SharedMaterial;
}

public sealed partial class Glider : Token
{
    public override string Id => "glider";

    public override void LegalMoves(HexCoord from, int boardRadius, List<HexCoord> output)
    {
        output.Clear();
        var dirs = HexCoord.Directions;
        for (int i = 0; i < 6; i++)
        {
            var diag = dirs[i] + dirs[(i + 1) % 6];
            var pos = from + diag;
            for (int step = 0; step < boardRadius; step++)
            {
                output.Add(pos);
                pos += diag;
            }
        }
        Filter(output, from, boardRadius);
    }

    private static readonly Mesh SharedMesh = new BoxMesh { Size = new Vector3(0.42f, 0.24f, 0.42f) };
    private static readonly StandardMaterial3D SharedMaterial = MakeMaterial(new Color(0.3f, 0.85f, 0.7f));
    protected override Mesh GetSharedMesh() => SharedMesh;
    protected override StandardMaterial3D GetSharedMaterial() => SharedMaterial;
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

    private static readonly Mesh SharedMesh = new CylinderMesh { TopRadius = 0.12f, BottomRadius = 0.12f, Height = 1.0f };
    private static readonly StandardMaterial3D SharedMaterial = MakeMaterial(new Color(0.2f, 0.55f, 0.25f));
    protected override Mesh GetSharedMesh() => SharedMesh;
    protected override StandardMaterial3D GetSharedMaterial() => SharedMaterial;
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

    private static readonly Mesh SharedMesh = new SphereMesh { Radius = 0.34f, Height = 0.5f };
    private static readonly StandardMaterial3D SharedMaterial = MakeMaterial(new Color(1f, 0.7f, 0.8f));
    protected override Mesh GetSharedMesh() => SharedMesh;
    protected override StandardMaterial3D GetSharedMaterial() => SharedMaterial;
}
