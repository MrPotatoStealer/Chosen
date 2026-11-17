// Chosen core: original code. Walls split into several (a curve, UDB's Curve Linedefs), written from Build's map
// rules (a wall runs from its point to the next wall's; a red wall's partner is the same line reversed).
using System;
using System.Collections.Generic;
using System.Linq;
using Chosen.Shared.Blood.Map;
using Chosen.Shared.Engine.Geometry;
using Godot;

namespace Chosen.Forge.Document;

/// <summary>The curves a wall can be bent into.</summary>
public enum CurveShape { Arc, Parabola, Sine, SCurve, Zigzag }

public static class Curves
{
    public static readonly string[] Names = { "Arc (part of a circle)", "Parabola", "Sine bump", "S-curve (a wave)", "Zigzag" };
    public static readonly string[] Codes = { "arc", "parabola", "sine", "scurve", "zigzag" };

    public static CurveShape? Parse(string code)
    {
        int i = Array.IndexOf(Codes, code);
        return i < 0 ? null : (CurveShape)i;
    }

    /// <summary>
    /// The point at t (0 at A, 1 at B; index: its place along the curve, for the zigzag) of a curve from A to B,
    /// depth Build units out at its deepest: out of the sector of a wall running from A to B (a Build wall has its
    /// sector on the right), reverse: into it. The arc's t runs at an even angle.
    /// </summary>
    public static Vector2 At(Vector2 a, Vector2 b, CurveShape shape, double depth, bool reverse, double t, int index)
    {
        double len = a.DistanceTo(b);
        if (len < 1)
            return a.Lerp(b, (float)t);
        var u = (b - a) / (float)len;
        // the sector's side (screen y down) is (-dy, dx); out of it the other way
        var n = new Vector2(u.Y, -u.X);
        double h = reverse ? -depth : depth;
        if (shape == CurveShape.Arc)
        {
            if (Math.Abs(h) < 0.5)
                return a.Lerp(b, (float)t);
            // through A, B and the point h out from the middle: centre C, radius R
            double half = len / 2, hh = Math.Abs(h), R = (half * half + hh * hh) / (2 * hh);
            var nn = h > 0 ? n : -n;
            var c = (a + b) * 0.5f + nn * (float)(hh - R);
            double alpha = Math.Atan2(half, R - hh);
            double beta = -alpha + 2 * alpha * t;
            return c + nn * (float)(R * Math.Cos(beta)) + u * (float)(R * Math.Sin(beta));
        }
        double f = shape switch
        {
            CurveShape.Parabola => 4 * t * (1 - t),
            CurveShape.Sine => Math.Sin(Math.PI * t),
            CurveShape.SCurve => Math.Sin(2 * Math.PI * t),
            CurveShape.Zigzag => index % 2 == 1 ? 1 : -1,
            _ => 0,
        };
        return a.Lerp(b, (float)t) + n * (float)(h * f);
    }

    public static Vector2I Round(Vector2 p) => new((int)Math.Round(p.X), (int)Math.Round(p.Y));
}

/// <summary>What a curve does to the map: points moved (a run of walls bent as one), points put into walls (each
/// wall's in its own order), and the lines as they will run (for the 2D view's preview).</summary>
public sealed class CurvePlan
{
    public readonly List<(int point, Vector2I to)> Moves = new();
    public readonly List<(BWall wall, List<Vector2I> points)> Inserts = new();
    public readonly List<Vector2[]> Paths = new();
    /// <summary>Runs of walls asked to bend as one that couldn't (a closed loop, a branch): curved each on its own.</summary>
    public int NotAsOne;
}

public sealed partial class MapDocument
{
    /// <summary>
    /// The curve of lines (indices): each line with count new points (asOne: a run of connected lines as one curve
    /// from its first point to its last, count points along it and its corners moved onto it; a run that closes or
    /// branches each line on its own). Out of the sector of the first line's front wall; reverse: into it.
    /// </summary>
    public CurvePlan PlanCurve(IEnumerable<int> lines, CurveShape shape, int count, double depth, bool reverse, bool asOne)
    {
        var plan = new CurvePlan();
        var sel = lines.Where(l => l >= 0 && l < Lines.Count && LineEditable(Lines[l])).Distinct().ToList();
        int StartOf(int w) => PointOf[w];
        int EndOf(int w) => Walls[w].Point2 >= 0 && Walls[w].Point2 < Walls.Count ? PointOf[Walls[w].Point2] : -1;
        Vector2 Pos(int p) => new(Points[p].X, Points[p].Y);

        // runs of lines joined at their points
        var runs = new List<List<int>>();
        if (asOne)
        {
            var at = new Dictionary<int, List<int>>();
            foreach (int l in sel)
                foreach (int p in new[] { StartOf(Lines[l].Front), EndOf(Lines[l].Front) })
                    if (p >= 0)
                        (at.TryGetValue(p, out var list) ? list : at[p] = new List<int>()).Add(l);
            var seen = new HashSet<int>();
            foreach (int l in sel)
            {
                if (!seen.Add(l))
                    continue;
                var comp = new List<int> { l };
                for (int i = 0; i < comp.Count; i++)
                    foreach (int p in new[] { StartOf(Lines[comp[i]].Front), EndOf(Lines[comp[i]].Front) })
                        if (p >= 0)
                            foreach (int o in at[p])
                                if (seen.Add(o))
                                    comp.Add(o);
                runs.Add(comp);
            }
        }
        else
            runs.AddRange(sel.Select(l => new List<int> { l }));

        foreach (var run in runs)
        {
            var walk = run.Count > 1 ? Walk(run, StartOf, EndOf) : null;
            if (run.Count > 1 && walk == null)
                plan.NotAsOne++;
            if (walk == null)
            {
                // each line on its own: its front wall's points
                foreach (int l in run)
                {
                    int w = Lines[l].Front, e = EndOf(w);
                    if (e < 0)
                        continue;
                    var a = Pos(StartOf(w));
                    var b = Pos(e);
                    var pts = new List<Vector2I>();
                    for (int i = 1; i <= count; i++)
                        pts.Add(Curves.Round(Curves.At(a, b, shape, depth, reverse, (double)i / (count + 1), i)));
                    plan.Inserts.Add((Walls[w], pts));
                    var path = new List<Vector2> { a };
                    path.AddRange(pts.Select(p => new Vector2(p.X, p.Y)));
                    path.Add(b);
                    plan.Paths.Add(path.ToArray());
                }
                continue;
            }

            // one curve from the run's first point to its last: every point (the corners and the new ones) at its
            // share of the run's length; the new points evenly along it
            var (points, walls) = walk.Value;
            var p0 = Pos(points[0]);
            var pk = Pos(points[^1]);
            var cum = new double[points.Count];
            for (int i = 1; i < points.Count; i++)
                cum[i] = cum[i - 1] + Pos(points[i - 1]).DistanceTo(Pos(points[i]));
            double total = cum[^1];
            if (total < 1)
                continue;
            // every point along it, by t: the corners (k: their place in the walk) and the new ones (k -1, in the wall they fall in)
            var all = new List<(double t, int corner, int seg)>();
            for (int i = 1; i < points.Count - 1; i++)
                all.Add((cum[i] / total, i, -1));
            for (int i = 1; i <= count; i++)
            {
                double t = (double)i / (count + 1);
                int seg = 0;
                while (seg < walls.Count - 1 && cum[seg + 1] / total <= t)
                    seg++;
                all.Add((t, -1, seg));
            }
            all.Sort((x, y) => x.t.CompareTo(y.t));
            var perWall = walls.Select(_ => new List<Vector2I>()).ToList();
            var line = new List<Vector2> { p0 };
            for (int k = 0; k < all.Count; k++)
            {
                var q = Curves.Round(Curves.At(p0, pk, shape, depth, reverse, all[k].t, k + 1));
                if (all[k].corner >= 0)
                    plan.Moves.Add((points[all[k].corner], q));
                else
                    perWall[all[k].seg].Add(q);
                line.Add(new Vector2(q.X, q.Y));
            }
            line.Add(pk);
            plan.Paths.Add(line.ToArray());
            for (int s = 0; s < walls.Count; s++)
            {
                if (perWall[s].Count == 0)
                    continue;
                // the wall running along the walk takes them in order; one running against it (a white wall the other
                // way) in reverse
                var (w, along) = walls[s];
                if (!along)
                    perWall[s].Reverse();
                plan.Inserts.Add((Walls[w], perWall[s]));
            }
        }
        return plan;
    }

    /// <summary>A run of lines as a path: its points in order and for each step the wall to split (along: it runs
    /// that way). Started so the first line's front wall runs along it (its sector on the right). Null for a run that
    /// closes or branches.</summary>
    (List<int> points, List<(int wall, bool along)> walls)? Walk(List<int> run, Func<int, int> startOf, Func<int, int> endOf)
    {
        var ends = new Dictionary<int, List<int>>();
        foreach (int l in run)
            foreach (int p in new[] { startOf(Lines[l].Front), endOf(Lines[l].Front) })
                (ends.TryGetValue(p, out var list) ? list : ends[p] = new List<int>()).Add(l);
        if (ends.Values.Any(v => v.Count > 2))
            return null;
        var tips = ends.Where(kv => kv.Value.Count == 1).Select(kv => kv.Key).ToList();
        if (tips.Count != 2)
            return null;
        List<int> Path(int from)
        {
            var pts = new List<int> { from };
            var used = new HashSet<int>();
            int at = from;
            while (true)
            {
                int next = ends[at].FirstOrDefault(l => !used.Contains(l), -1);
                if (next < 0)
                    break;
                used.Add(next);
                int f = Lines[next].Front;
                at = startOf(f) == at ? endOf(f) : startOf(f);
                pts.Add(at);
            }
            return pts;
        }
        var points = Path(tips[0]);
        // the first line's front along the walk, else walked from the other end
        int first = ends[tips[0]][0];
        if (startOf(Lines[first].Front) != tips[0])
        {
            points = Path(tips[1]);
        }
        var walls = new List<(int, bool)>();
        for (int i = 0; i + 1 < points.Count; i++)
        {
            int a = points[i], b = points[i + 1];
            var l = run.First(x => (startOf(Lines[x].Front) == a && endOf(Lines[x].Front) == b)
                || (startOf(Lines[x].Front) == b && endOf(Lines[x].Front) == a));
            var ln = Lines[l];
            if (startOf(ln.Front) == a)
                walls.Add((ln.Front, true));
            else if (ln.Back >= 0)
                walls.Add((ln.Back, true));
            else
                walls.Add((ln.Front, false));
        }
        return (points, walls);
    }

    /// <summary>A curve planned by PlanCurve done: the points moved, then the walls split (one undo step: the caller's).</summary>
    public string ApplyCurve(CurvePlan plan)
    {
        if (plan.Moves.Count == 0 && plan.Inserts.Count == 0)
            return "nothing to curve";
        foreach (var (p, to) in plan.Moves)
            MovePoint(p, to.X, to.Y);
        if (plan.Moves.Count > 0)
            Rebuild();
        int done = plan.Moves.Count;
        foreach (var (wall, pts) in plan.Inserts)
        {
            int w = Walls.IndexOf(wall);
            if (w >= 0 && InsertPoints(w, pts))
                done++;
        }
        return done > 0 ? null : "the points fall on the walls' ends";
    }

    /// <summary>
    /// Wall w split into a chain through pts (in order from its point to the next): new walls after it in its loop,
    /// and the wall behind a red wall split the same way, reversed, each pair linked. Points equal to the one before
    /// or to the wall's ends are left out. False when nothing is left to put in.
    /// </summary>
    public bool InsertPoints(int w, IReadOnlyList<Vector2I> pts)
    {
        if (w < 0 || w >= Walls.Count)
            return false;
        var a = Walls[w];
        var (ax, ay, bx, by) = BuildGeom.WallEnds(this, w);
        var start = new Vector2I(ax, ay);
        var end = new Vector2I(bx, by);
        var list = new List<Vector2I>();
        var prev = start;
        foreach (var p in pts)
        {
            if (p == prev || p == end || p == start)
                continue;
            list.Add(p);
            prev = p;
        }
        if (list.Count == 0)
            return false;
        var g = Decompose();
        var (_, loop, at) = g.Find(a);
        if (loop == null)
            return false;
        var partner = g.Of(a);

        // front[i]: ends[i] to ends[i + 1]
        var ends = new List<Vector2I> { start };
        ends.AddRange(list);
        ends.Add(end);
        var front = new List<BWall> { a };
        foreach (var q in list)
            front.Add(CloneWall(a, q.X, q.Y));
        loop.InsertRange(at + 1, front.GetRange(1, list.Count));
        for (int i = 0; i < front.Count; i++)
            front[i].XRepeat = RepeatFor(Length(ends[i], ends[i + 1]));

        if (partner != null)
        {
            // back[j]: ends[n + 1 - j] to ends[n - j] (the partner runs from the end back to the start)
            var (_, ploop, pat) = g.Find(partner);
            int n = list.Count;
            var back = new List<BWall> { partner };
            for (int i = n - 1; i >= 0; i--)
                back.Add(CloneWall(partner, list[i].X, list[i].Y));
            ploop.InsertRange(pat + 1, back.GetRange(1, n));
            for (int i = 0; i <= n; i++)
            {
                g.Link(front[i], back[n - i]);
                back[n - i].XRepeat = front[i].XRepeat;
            }
        }
        Commit(g);
        return true;
    }

    static long Length(Vector2I a, Vector2I b) =>
        (long)Math.Sqrt((double)(b.X - a.X) * (b.X - a.X) + (double)(b.Y - a.Y) * (b.Y - a.Y));
}
