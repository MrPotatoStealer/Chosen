// Chosen core: original code (a drawn shape cutting the sectors it lies over, as UDB's drawing does, written from
// Build's map rules: the walls and the drawing split where they cross, the faces of what they bound traced as loops).
using System;
using System.Collections.Generic;
using System.Linq;
using Chosen.Shared.Blood.Map;
using Chosen.Shared.Engine.Geometry;
using Godot;

namespace Chosen.Forge.Document;

public sealed partial class MapDocument
{
    /// <summary>
    /// A closed shape drawn over sectors (partly or along their walls from inside), as UDB's drawing works: the walls
    /// and the drawing split where they cross (on both sides of a red wall); each sector the drawing runs through is
    /// cut along it (the part inside the drawing a sector of its own, with that sector's properties: the larger part
    /// outside keeps the sector, its type and triggers); the drawing's part outside every sector a new sector, joined
    /// to the walls it lies against; sectors inside it its islands. Red walls join the parts. Refused (why) where
    /// rooms over rooms share the place (Alt draws over them). The sectors made, or null.
    /// </summary>
    public List<int> MakeSectorAcross(List<Vector2I> drawn, out string why)
    {
        why = null;
        var path = new List<Vector2I>(drawn);
        if (path.Count < 3)
        {
            why = "a sector needs 3 points";
            return null;
        }
        if (Area(path) < 0)
            path.Reverse();
        // a shape crossing itself makes no sector
        for (int i = 0; i < path.Count; i++)
            for (int j = i + 2; j < path.Count; j++)
            {
                if (i == 0 && j == path.Count - 1)
                    continue;
                if (Cross(path[i], path[(i + 1) % path.Count], path[j], path[(j + 1) % path.Count], null, false))
                {
                    why = "the shape crosses itself";
                    return null;
                }
            }

        // 1. the drawing and the walls split where they meet: the drawing gets a point where it crosses a wall or
        // passes a wall's point, a wall where the drawing crosses it or has a corner on it
        for (int guard = 0; guard < 4096; guard++)
        {
            if (!SplitOnce(path))
                break;
        }

        // 2. each piece of the drawing: along a wall (nothing to add), inside one sector (a cut), outside them (the
        // new sector's side); rooms over rooms share a place: which to cut is unclear
        var pieces = new List<(Vector2I a, Vector2I b, int sector)>();
        var cut = new HashSet<int>();
        for (int k = 0; k < path.Count; k++)
        {
            var a = path[k];
            var b = path[(k + 1) % path.Count];
            if (a == b)
                continue;
            double mx = (a.X + b.X) * 0.5, my = (a.Y + b.Y) * 0.5;
            if (OnAnyWall(mx, my))
                continue;
            var at = EditableSectorsAt(mx, my);
            if (at.Count > 1)
            {
                why = $"the shape lies over rooms over rooms (sectors {string.Join(", ", at)}): hold Alt on the closing click to draw a sector over them";
                return null;
            }
            pieces.Add((a, b, at.Count == 1 ? at[0] : -1));
            if (at.Count == 1)
                cut.Add(at[0]);
        }
        if (cut.Count == 0 && pieces.All(p => p.sector < 0))
        {
            // nothing crossed: the plain operations do it
            int s = MakeSectorJoined(path, out why);
            return s >= 0 ? new List<int> { s } : null;
        }

        var firsts = cut.ToDictionary(s => s, FirstOf);
        var g = Decompose();
        var tmplSector = Template(path.Average(p => p.X), path.Average(p => p.Y)) ?? DefaultSector;
        var outsideWall = Walls.Count > 0 ? Walls[tmplSector.WallPtr >= 0 && tmplSector.WallPtr < Walls.Count ? tmplSector.WallPtr : 0] : DefaultWall;

        // 3. the half-edges of the region rebuilt (each with its room on its left, the way the outer loops go): the cut
        // sectors' walls, both sides of a cut, the drawing's outside pieces, and the outer side of white walls facing
        // the drawing's outside part
        var edges = new List<(Vector2I from, Vector2I to, BWall wall)>();
        foreach (int s in cut)
            foreach (var loop in g.Sectors[s].Loops)
                for (int k = 0; k < loop.Count; k++)
                {
                    var e = loop[(k + 1) % loop.Count];
                    edges.Add((new Vector2I(loop[k].X, loop[k].Y), new Vector2I(e.X, e.Y), loop[k]));
                }
        foreach (var (a, b, s) in pieces)
        {
            if (s >= 0)
            {
                var tmpl = g.Sectors[s].Loops.OrderByDescending(l => Area(l)).First()[0];
                var w1 = NewWall(tmpl, a, b);
                var w2 = NewWall(tmpl, b, a);
                g.Link(w1, w2);
                edges.Add((a, b, w1));
                edges.Add((b, a, w2));
            }
            else
                edges.Add((a, b, NewWall(outsideWall, a, b)));
        }
        for (int w = 0; w < Walls.Count; w++)
        {
            if (Walls[w].NextWall >= 0 || Walls[w].Sector < 0 || WallOutOfTheWay(w))
                continue;
            var (x1, y1, x2, y2) = BuildGeom.WallEnds(this, w);
            double dx = x2 - x1, dy = y2 - y1, len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 1)
                continue;
            // the outer side: right of the wall in the loops' turn (their room is on the left)
            double ox = (x1 + x2) * 0.5 + dy / len * 0.5, oy = (y1 + y2) * 0.5 - dx / len * 0.5;
            if (!InPolygon(path, ox, oy) || EditableSectorsAt(ox, oy).Count > 0)
                continue;
            var back = NewWall(Walls[w], new Vector2I(x2, y2), new Vector2I(x1, y1));
            back.Cstat &= ~(1 | 16 | 32 | 64);
            g.Link(back, Walls[w]);
            edges.Add((new Vector2I(x2, y2), new Vector2I(x1, y1), back));
        }

        // 4. the faces: from each half-edge the next is the sharpest turn left at its end (the room on the left)
        var outOf = new Dictionary<Vector2I, List<int>>();
        var around = new Dictionary<Vector2I, HashSet<Vector2I>>();
        for (int i = 0; i < edges.Count; i++)
        {
            var (a, b, _) = edges[i];
            if (!outOf.TryGetValue(a, out var l))
                outOf[a] = l = new List<int>();
            l.Add(i);
            foreach (var (p, q) in new[] { (a, b), (b, a) })
            {
                if (!around.TryGetValue(p, out var set))
                    around[p] = set = new HashSet<Vector2I>();
                set.Add(q);
            }
        }
        var used = new bool[edges.Count];
        var cycles = new List<List<int>>();
        for (int start = 0; start < edges.Count; start++)
        {
            if (used[start])
                continue;
            var cyc = new List<int>();
            int h = start;
            while (!used[h])
            {
                used[h] = true;
                cyc.Add(h);
                var (p, q, _) = edges[h];
                double back = Math.Atan2(p.Y - q.Y, p.X - q.X);
                Vector2I best = p;
                double bestTurn = double.MaxValue;
                foreach (var r in around[q])
                {
                    double turn = back - Math.Atan2(r.Y - q.Y, r.X - q.X);
                    while (turn <= 1e-12)
                        turn += 2 * Math.PI;
                    if (turn < bestTurn)
                    {
                        bestTurn = turn;
                        best = r;
                    }
                }
                int next = outOf.TryGetValue(q, out var outs) ? outs.FirstOrDefault(o => edges[o].to == best && !used[o], -1) : -1;
                if (next < 0)
                {
                    if (outs != null && outs.Contains(start) && edges[start].to == best)
                        break;
                    why = $"the shape makes a place Forge can't build sectors of (at {q.X}, {q.Y})";
                    return null;
                }
                h = next;
            }
            if (edges[cyc[^1]].to != edges[cyc[0]].from)
            {
                why = $"the shape leaves a loop open (at {edges[cyc[^1]].to.X}, {edges[cyc[^1]].to.Y})";
                return null;
            }
            cycles.Add(cyc);
        }
        List<Vector2I> Pts(List<int> c) => c.Select(i => edges[i].from).ToList();
        var faces = cycles.Where(c => Area(Pts(c)) > 0).ToList();
        var holes = cycles.Where(c => Area(Pts(c)) < 0).ToList();
        if (cycles.Any(c => Area(Pts(c)) == 0))
        {
            why = "the shape makes a sector of no area";
            return null;
        }
        // just left of a half-edge: inside its face
        (double x, double y) LeftOf(int i)
        {
            var (a, b, _) = edges[i];
            double dx = b.X - a.X, dy = b.Y - a.Y, len = Math.Sqrt(dx * dx + dy * dy);
            return ((a.X + b.X) * 0.5 - dy / len * 0.5, (a.Y + b.Y) * 0.5 + dx / len * 0.5);
        }
        var holesOf = faces.ToDictionary(f => f, _ => new List<List<int>>());
        foreach (var hc in holes)
        {
            var (x, y) = LeftOf(hc[0]);
            var home = faces.Where(f => InPolygon(Pts(f), x, y)).OrderBy(f => Area(Pts(f))).FirstOrDefault();
            if (home == null)
            {
                why = "a loop of the shape lies in no sector";
                return null;
            }
            holesOf[home].Add(hc);
        }

        // 5. which sector each face is part of (the one its inside lies in), inside the drawing or not
        var faceOf = new Dictionary<List<int>, (int sector, bool inside)>();
        foreach (var f in faces)
        {
            var (x, y) = LeftOf(f[0]);
            int s = cut.FirstOrDefault(c => BuildGeom.Inside(this, c, x, y), -1);
            faceOf[f] = (s, InPolygon(path, x, y));
        }
        var made = new List<SectorG>();
        var partsOf = cut.ToDictionary(s => s, _ => new List<SectorG>());
        List<BWall> Loop(List<int> c, BWall first)
        {
            var l = c.Select(i => edges[i].wall).ToList();
            int at = first == null ? -1 : l.FindIndex(w => ReferenceEquals(w, first));
            return at > 0 ? l.Skip(at).Concat(l.Take(at)).ToList() : l;
        }
        foreach (int s in cut)
        {
            var mine = faces.Where(f => faceOf[f].sector == s).ToList();
            if (mine.Count == 0)
            {
                why = $"sector {s} came apart";
                return null;
            }
            // the sector stays with the larger part outside the drawing (else the larger part)
            var keep = mine.OrderBy(f => faceOf[f].inside).ThenByDescending(f => Area(Pts(f))).First();
            var sg = g.Sectors[s];
            var firstWall = sg.Loops.Count > 0 && sg.Rec.WallPtr >= 0 && sg.Rec.WallPtr < Walls.Count ? Walls[sg.Rec.WallPtr] : null;
            sg.Loops = new List<List<BWall>> { Loop(keep, firstWall) };
            sg.Loops.AddRange(holesOf[keep].Select(hc => Loop(hc, null)));
            partsOf[s].Add(sg);
            foreach (var f in mine.Where(f => f != keep))
            {
                var part = new SectorG { Rec = CloneSector(sg.Rec) };
                part.Loops.Add(Loop(f, null));
                part.Loops.AddRange(holesOf[f].Select(hc => Loop(hc, null)));
                g.Sectors.Add(part);
                partsOf[s].Add(part);
                made.Add(part);
            }
        }
        foreach (var f in faces.Where(f => faceOf[f].sector < 0))
        {
            if (!faceOf[f].inside)
            {
                why = "the shape leaves a place outside it and every sector";
                return null;
            }
            var part = new SectorG { Rec = CloneSector(tmplSector) };
            part.Rec.FloorHeinum = part.Rec.CeilingHeinum = 0;
            part.Rec.FloorStat &= ~2;
            part.Rec.CeilingStat &= ~2;
            part.Loops.Add(Loop(f, null));
            part.Loops.AddRange(holesOf[f].Select(hc => Loop(hc, null)));
            g.Sectors.Add(part);
            made.Add(part);
        }

        // 6. written back; the sprites of a cut sector to the part holding them, the slopes kept
        var spritesOf = Sprites.Where(sp => cut.Contains(sp.SectNum)).Select(sp => (sp, s: sp.SectNum)).ToList();
        Commit(g);
        int IndexOf(SectorG x) => g.Sectors.IndexOf(x);
        foreach (var (sp, s) in spritesOf)
        {
            var parts = partsOf[s].Select(IndexOf).ToList();
            int home = parts.FirstOrDefault(p => BuildGeom.Inside(this, p, sp.X, sp.Y), parts[0]);
            sp.SectNum = home;
        }
        foreach (int s in cut)
            foreach (var part in partsOf[s])
                Repair(IndexOf(part), firsts[s]);
        Rebuild();
        return made.Select(IndexOf).ToList();
    }

    /// <summary>
    /// An open line drawn across the sectors (a copied wall pasted: a drawing made beforehand): the line and the walls
    /// split where they meet; each stretch of it inside one sector from a point of that sector's outer walls to another
    /// splits the sector there (SplitSector: the new part has its properties); the stretches outside every sector or
    /// along walls are cut off, and those ending in a sector short of its walls (or at its holes), or where rooms over
    /// rooms share the place, are left out (note says how many, null: none). The sectors split.
    /// </summary>
    public int DrawLineAcross(List<Vector2I> drawn, out string note)
    {
        note = null;
        var path = drawn.Where((p, i) => i == 0 || p != drawn[i - 1]).ToList();
        if (path.Count < 2)
            return 0;
        var had = new HashSet<Vector2I>(Points.Select(p => new Vector2I(p.X, p.Y)));
        for (int guard = 0; guard < 4096; guard++)
        {
            if (!SplitOnce(path, false))
                break;
        }
        // the stretches: runs of the line's pieces inside one sector, broken where it meets a wall
        var runs = new List<List<Vector2I>>();
        int shared = 0;
        List<Vector2I> cur = null;
        int curSector = -1;
        for (int k = 0; k + 1 < path.Count; k++)
        {
            var a = path[k];
            var b = path[k + 1];
            double mx = (a.X + b.X) * 0.5, my = (a.Y + b.Y) * 0.5;
            int s = -1;
            if (!OnAnyWall(mx, my))
            {
                var at = EditableSectorsAt(mx, my);
                if (at.Count > 1)
                    shared++;
                s = at.Count == 1 ? at[0] : -1;
            }
            if (cur != null && s == curSector && !OnAnyWall(a.X, a.Y))
            {
                cur.Add(b);
                continue;
            }
            if (cur != null)
                runs.Add(cur);
            cur = s >= 0 ? new List<Vector2I> { a, b } : null;
            curSector = s;
        }
        if (cur != null)
            runs.Add(cur);
        int split = 0, open = 0;
        var refused = new List<string>();
        foreach (var run in runs)
        {
            // (its sector found again: a split before it may have given the place to the new part)
            var a = run[0];
            var b = run[^1];
            double mx = (a.X + run[1].X) * 0.5, my = (a.Y + run[1].Y) * 0.5;
            var at = EditableSectorsAt(mx, my);
            if (at.Count != 1 || a == b)
            {
                open++;
                continue;
            }
            int s = at[0];
            var (aOn, aWall) = OnOuterLoop(s, a);
            var (bOn, _) = OnOuterLoop(s, b);
            if (!aOn || !bOn)
            {
                open++;
                continue;
            }
            if (aWall >= 0)
                InsertPoint(aWall, a.X, a.Y);
            if (OnOuterLoop(s, b) is (true, var bWall and >= 0))
                InsertPoint(bWall, b.X, b.Y);
            var (made, error) = SplitSector(s, run);
            if (made >= 0)
                split++;
            else
                refused.Add(error);
        }
        // the points the line put on walls where nothing came of it (a stretch left out) taken away again
        for (int guard = 0; guard < 4096; guard++)
        {
            int p = Enumerable.Range(0, Points.Count).FirstOrDefault(i => !had.Contains(new Vector2I(Points[i].X, Points[i].Y)) && Straight(i), -1);
            if (p < 0 || DeletePoint(p) != null)
                break;
            Rebuild();
        }
        var notes = new List<string>();
        if (open > 0)
            notes.Add($"{open} stretch(es) reaching no wall of their sector left out");
        if (shared > 0)
            notes.Add("left out where rooms over rooms share the place");
        notes.AddRange(refused.Distinct());
        note = notes.Count > 0 ? string.Join("; ", notes) : null;
        return split;
    }

    /// <summary>Does every wall starting at point p go on straight from the wall ending there (a point only splitting a
    /// wall)?</summary>
    bool Straight(int p)
    {
        foreach (int w in Points[p].Walls)
        {
            int prev = -1;
            var sec = Walls[w].Sector >= 0 && Walls[w].Sector < Sectors.Count ? Sectors[Walls[w].Sector] : null;
            for (int i = sec?.WallPtr ?? 0; sec != null && i < sec.WallPtr + sec.WallNum; i++)
                if (Walls[i].Point2 == w)
                    prev = i;
            if (prev < 0)
                return false;
            var (ax, ay, _, _) = BuildGeom.WallEnds(this, prev);
            var (_, _, bx, by) = BuildGeom.WallEnds(this, w);
            if (Orient(new Vector2I(ax, ay), new Vector2I(Points[p].X, Points[p].Y), new Vector2I(bx, by)) != 0)
                return false;
        }
        return true;
    }

    static BWall NewWall(BWall tmpl, Vector2I a, Vector2I b)
    {
        var w = CloneWall(tmpl, a.X, a.Y);
        w.XRepeat = RepeatFor((long)Math.Sqrt((double)(b.X - a.X) * (b.X - a.X) + (double)(b.Y - a.Y) * (b.Y - a.Y)));
        w.Cstat &= ~(1 | 16 | 32 | 64);
        return w;
    }

    bool OnAnyWall(double x, double y)
    {
        for (int w = 0; w < Walls.Count; w++)
        {
            if (Walls[w].Sector < 0 || WallOutOfTheWay(w))
                continue;
            var (ax, ay, bx, by) = BuildGeom.WallEnds(this, w);
            if (SegmentDistance(x, y, ax, ay, bx, by) < 0.5)
                return true;
        }
        return false;
    }

    /// <summary>One place where the drawing and a wall meet made a point of both; false when there is none left. (closed:
    /// the drawing's last point back to its first is a side of it too.)</summary>
    bool SplitOnce(List<Vector2I> path, bool closed = true)
    {
        for (int k = 0; k < (closed ? path.Count : path.Count - 1); k++)
        {
            var a = path[k];
            var b = path[(k + 1) % path.Count];
            if (a == b)
                continue;
            for (int w = 0; w < Walls.Count; w++)
            {
                // (another layer's walls are out of the way: drawn over, not split)
                if (Walls[w].Sector < 0 || WallOutOfTheWay(w))
                    continue;
                var (cx, cy, dx, dy) = BuildGeom.WallEnds(this, w);
                var c = new Vector2I(cx, cy);
                var d = new Vector2I(dx, dy);
                if (c == d)
                    continue;
                long o1 = Orient(a, b, c), o2 = Orient(a, b, d), o3 = Orient(c, d, a), o4 = Orient(c, d, b);
                // a wall's point on the drawing (not at its ends): a point of the drawing
                if (o1 == 0 && c != a && c != b && OnSegment(a, b, c))
                {
                    path.Insert(k + 1, c);
                    return true;
                }
                // a corner of the drawing on a wall (not at its ends): a point of the wall
                if (o3 == 0 && a != c && a != d && OnSegment(c, d, a))
                    return InsertPoint(w, a.X, a.Y) || Fail();
                // crossing: a point of both, rounded to the map's units
                if (((o1 > 0 && o2 < 0) || (o1 < 0 && o2 > 0)) && ((o3 > 0 && o4 < 0) || (o3 < 0 && o4 > 0)))
                {
                    double t = (double)o3 / (o3 - o4);
                    var x = new Vector2I((int)Math.Round(a.X + (b.X - a.X) * t), (int)Math.Round(a.Y + (b.Y - a.Y) * t));
                    if (x != a && x != b)
                        path.Insert(k + 1, x);
                    if (x != c && x != d)
                        InsertPoint(w, x.X, x.Y);
                    return true;
                }
            }
        }
        return false;
    }

    static bool Fail() => throw new InvalidOperationException("a point couldn't be put on a wall");
}
