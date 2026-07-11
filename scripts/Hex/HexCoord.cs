// =============================================================================
// HexCoord
// =============================================================================
// Purpose:
//   Immutable readonly struct representing axial hex coordinates (Q, R) with
//   derived S = -Q-R. Provides arithmetic operators, hex distance, the six
//   neighbor direction vectors, equality/hashing, and enumerators for all
//   coordinates Within(radius) and on a Ring(radius). Serves as the
//   foundational math type for all hex-grid logic in the project.
//
// Interactions:
//   - (none — self-contained value type; used by HexBoard, HexLayout, Token,
//     Tokens, and others, but does not itself reference other project
//     classes)
// =============================================================================

using System;
using System.Collections.Generic;

namespace HexGame.Hex;

public readonly struct HexCoord : IEquatable<HexCoord>
{
    public readonly int Q;
    public readonly int R;
    public readonly int S;

    public HexCoord(int q, int r) { Q = q; R = r; S = -q - r; }

    public static readonly HexCoord Zero = new(0, 0);

    public static readonly HexCoord[] Directions = new[]
    {
        new HexCoord(1, 0),   // E
        new HexCoord(1, -1),  // NE
        new HexCoord(0, -1),  // NW
        new HexCoord(-1, 0),  // W
        new HexCoord(-1, 1),  // SW
        new HexCoord(0, 1),   // SE
    };

    public static HexCoord operator +(HexCoord a, HexCoord b) => new(a.Q + b.Q, a.R + b.R);
    public static HexCoord operator -(HexCoord a, HexCoord b) => new(a.Q - b.Q, a.R - b.R);
    public static HexCoord operator *(HexCoord a, int k) => new(a.Q * k, a.R * k);
    public static HexCoord operator -(HexCoord a) => new(-a.Q, -a.R);

    public int Distance(HexCoord other)
    {
        var d = this - other;
        return (Math.Abs(d.Q) + Math.Abs(d.R) + Math.Abs(d.S)) / 2;
    }

    public int DistanceFromOrigin() => (Math.Abs(Q) + Math.Abs(R) + Math.Abs(S)) / 2;

    public bool Equals(HexCoord other) => Q == other.Q && R == other.R;
    public override bool Equals(object obj) => obj is HexCoord o && Equals(o);
    public override int GetHashCode() => HashCode.Combine(Q, R);
    public static bool operator ==(HexCoord a, HexCoord b) => a.Equals(b);
    public static bool operator !=(HexCoord a, HexCoord b) => !a.Equals(b);
    public override string ToString() => $"({Q},{R})";

    public static void Within(int radius, List<HexCoord> output)
    {
        for (int q = -radius; q <= radius; q++)
        {
            int rMin = Math.Max(-radius, -q - radius);
            int rMax = Math.Min(radius, -q + radius);
            for (int r = rMin; r <= rMax; r++)
                output.Add(new HexCoord(q, r));
        }
    }

    public static void Ring(int radius, List<HexCoord> output)
    {
        if (radius == 0) { output.Add(Zero); return; }
        var hex = Directions[4] * radius;
        for (int side = 0; side < 6; side++)
        {
            for (int step = 0; step < radius; step++)
            {
                output.Add(hex);
                hex += Directions[side];
            }
        }
    }

    public static IEnumerable<HexCoord> Within(int radius)
    {
        for (int q = -radius; q <= radius; q++)
        {
            int rMin = Math.Max(-radius, -q - radius);
            int rMax = Math.Min(radius, -q + radius);
            for (int r = rMin; r <= rMax; r++)
                yield return new HexCoord(q, r);
        }
    }

}
