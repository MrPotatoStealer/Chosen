using System.Collections.Generic;
using Godot;

namespace Chosen.Render;

/// <summary>
/// A sector's floor filled from its wall loops as they are now, for a sector whose loop a mover carries (E3M5's lift
/// ring, 13: its inner loop turns with the cabin; the rooms beside a sliding door, whose shared points Build drags),
/// where the converter's pieces, cut once, no longer fit. A scanline fill, as Build's own: bands between the corners'
/// heights (y), in each band the edges crossing it sorted across and every other gap filled (even-odd), a trapezoid each.
/// Loops touching each other, running out into a wall pocket and back, or a door's loop resting in its pocket on the
/// outline's walls fill as Build fills them, where ear clipping broke on them.
/// </summary>
public static class PolyFill
{
    const float Near = 0.001f;

    /// <summary>The triangles (three points each) filling the loops (indices into pts). middles: if given, each point
    /// that lies on no edge (a fan's middle) with the outline it is the mean of.</summary>
    public static List<Vector2> Fill(IReadOnlyList<Vector2> pts, List<List<int>> loops, Dictionary<Vector2, Vector2[]> middles = null)
    {
        var tris = new List<Vector2>();
        var edges = new List<(Vector2 a, Vector2 b)>();
        foreach (var l in loops)
            for (int i = 0; i < l.Count; i++)
            {
                Vector2 a = pts[l[i]], b = pts[l[(i + 1) % l.Count]];
                if (Mathf.Abs(a.Y - b.Y) > Near)
                    edges.Add(a.Y < b.Y ? (a, b) : (b, a));
            }
        var ys = new List<float>();
        foreach (var (a, b) in edges)
        {
            ys.Add(a.Y);
            ys.Add(b.Y);
        }
        // where two edges cross (a loop folded over itself as its points moved): a band breaks there too
        for (int i = 0; i < edges.Count; i++)
            for (int j = i + 1; j < edges.Count; j++)
                if (Cross(edges[i], edges[j], out float y))
                    ys.Add(y);
        ys.Sort();
        // the trapezoids of each band (k: between ys[k] and ys[k + 1]), and every corner on each height, of the bands
        // below and above it: a trapezoid's edge takes those lying along it, so that neighbours share their corners (one
        // band's span split in two above it left a seam the background showed through: DWE3M1's courtyard floor)
        var traps = new List<(int k, float l0, float l1, float r0, float r1)>();
        var at = new Dictionary<int, List<float>>();
        var band = new List<(float x0, float x1, float xm)>();
        for (int k = 0; k + 1 < ys.Count; k++)
        {
            float y0 = ys[k], y1 = ys[k + 1];
            if (y1 - y0 < Near)
                continue;
            float ym = (y0 + y1) / 2;
            band.Clear();
            foreach (var e in edges)
                if (e.a.Y < ym && e.b.Y > ym)
                    band.Add((X(e, y0), X(e, y1), X(e, ym)));
            band.Sort((p, q) => p.xm.CompareTo(q.xm));
            for (int i = 0; i + 1 < band.Count; i += 2)
            {
                var (l0, l1, _) = band[i];
                var (r0, r1, _) = band[i + 1];
                traps.Add((k, l0, l1, r0, r1));
                Corners(at, k).AddRange(new[] { l0, r0 });
                Corners(at, k + 1).AddRange(new[] { l1, r1 });
            }
        }
        foreach (var l in at.Values)
            l.Sort();
        var poly = new List<Vector2>();
        foreach (var (k, l0, l1, r0, r1) in traps)
        {
            float y0 = ys[k], y1 = ys[k + 1];
            poly.Clear();
            // round it: along the bottom left to right, along the top right to left, the corners lying on each
            poly.Add(new Vector2(l0, y0));
            foreach (float x in at[k])
                if (x > l0 + Near && x < r0 - Near)
                    poly.Add(new Vector2(x, y0));
            poly.Add(new Vector2(r0, y0));
            poly.Add(new Vector2(r1, y1));
            var top = at[k + 1];
            for (int i = top.Count - 1; i >= 0; i--)
                if (top[i] > l1 + Near && top[i] < r1 - Near)
                    poly.Add(new Vector2(top[i], y1));
            poly.Add(new Vector2(l1, y1));
            // (a side of no width: its two corners one)
            for (int i = poly.Count - 1; i >= 0 && poly.Count > 2; i--)
                if ((poly[i] - poly[(i + 1) % poly.Count]).LengthSquared() < Near * Near)
                    poly.RemoveAt(i);
            if (poly.Count < 3)
                continue;
            if (poly.Count <= 4)
            {
                // a plain trapezoid or triangle, as before
                for (int i = 1; i + 1 < poly.Count; i++)
                {
                    tris.Add(poly[0]); tris.Add(poly[i]); tris.Add(poly[i + 1]);
                }
                continue;
            }
            // with corners along its edges: a fan from its middle, every piece of its outline an edge of one
            var mid = Vector2.Zero;
            foreach (var p in poly)
                mid += p;
            mid /= poly.Count;
            if (middles != null)
                middles[mid] = poly.ToArray();
            for (int i = 0; i < poly.Count; i++)
            {
                tris.Add(mid); tris.Add(poly[i]); tris.Add(poly[(i + 1) % poly.Count]);
            }
        }
        return tris;
    }

    static List<float> Corners(Dictionary<int, List<float>> at, int k)
    {
        if (!at.TryGetValue(k, out var l))
            at[k] = l = new List<float>();
        return l;
    }

    static float X((Vector2 a, Vector2 b) e, float y) => e.a.X + (y - e.a.Y) * (e.b.X - e.a.X) / (e.b.Y - e.a.Y);

    /// <summary>Do the two edges cross inside both (not at their ends), and at what y?</summary>
    static bool Cross((Vector2 a, Vector2 b) p, (Vector2 a, Vector2 b) q, out float y)
    {
        y = 0;
        Vector2 r = p.b - p.a, s = q.b - q.a;
        float den = r.X * s.Y - r.Y * s.X;
        if (Mathf.Abs(den) < 1e-9f)
            return false;
        var w = q.a - p.a;
        float t = (w.X * s.Y - w.Y * s.X) / den, u = (w.X * r.Y - w.Y * r.X) / den;
        if (t <= 1e-4f || t >= 1 - 1e-4f || u <= 1e-4f || u >= 1 - 1e-4f)
            return false;
        y = p.a.Y + t * r.Y;
        return true;
    }
}
