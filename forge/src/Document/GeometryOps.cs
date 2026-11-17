// Chosen core: original code. The operations are written from Build's map rules (a sector is loops of walls stored
// together, the outer loop first and clockwise as drawn, inner loops the other way; a red wall and its NextWall are
// the same line reversed), not from Build's or xmapedit's routines.
using System;
using System.Collections.Generic;
using System.Linq;
using Chosen.Shared.Blood.Map;
using Chosen.Shared.Engine.Geometry;
using Godot;

namespace Chosen.Forge.Document;

public sealed partial class MapDocument
{
    /// <summary>What the last operation could only do part of (a sloped plane that turned), or null.</summary>
    public string Warning;

    /// <summary>A sector's first wall and heights before an operation, for RepairFirstWall after it.</summary>
    (int x1, int y1, int x2, int y2, double ceil, double floor) FirstOf(int s) =>
        BuildGeom.WallEnds(this, Sectors[s].WallPtr) is var (x1, y1, x2, y2) ? (x1, y1, x2, y2, Sectors[s].CeilingZ, Sectors[s].FloorZ) : default;

    void Repair(int s, (int x1, int y1, int x2, int y2, double ceil, double floor) old)
    {
        if (s >= 0 && s < Sectors.Count && !RepairFirstWall(s, (old.x1, old.y1, old.x2, old.y2), old.ceil, old.floor))
            Warning = $"sector {s} has no wall along its old first wall: its slope turned";
    }

    // ---------------------------------------------------------------- the map as a graph of objects
    /// <summary>A sector as loops of wall objects (outer first); OldIndex its index before the change (-1: new).</summary>
    sealed class SectorG
    {
        public BSector Rec;
        public int OldIndex = -1;
        public List<List<BWall>> Loops = new();
    }

    sealed class Graph
    {
        public readonly List<SectorG> Sectors = new();
        /// <summary>Each red wall's partner (both ways): the mutual pairs the operations work with.</summary>
        public readonly Dictionary<BWall, BWall> Partner = new(ReferenceEqualityComparer.Instance);
        /// <summary>Every wall's link as stored (NextWall's object, null for none or an index out of range; its raw
        /// NextWall and NextSector; the sector it was in): a wall no operation touched keeps it as it was (one-way
        /// links, a NextSector that doesn't match, as the maps have them).</summary>
        public readonly Dictionary<BWall, (BWall next, int rawNext, int nextSector, int ownSector)> Stored = new(ReferenceEqualityComparer.Instance);
        /// <summary>Walls whose link an operation set (Link, Unlink): theirs come from Partner.</summary>
        public readonly HashSet<BWall> Touched = new(ReferenceEqualityComparer.Instance);
        /// <summary>Walls in no sector's range (kept after all the sectors' walls, in their order), with their stored
        /// Point2's object.</summary>
        public readonly List<(BWall wall, BWall point2, int rawPoint2)> Orphans = new();

        public void Link(BWall a, BWall b)
        {
            Unlink(a);
            Unlink(b);
            Partner[a] = b;
            Partner[b] = a;
            Touched.Add(a);
            Touched.Add(b);
        }

        public void Unlink(BWall a)
        {
            Touched.Add(a);
            if (Partner.Remove(a, out var b))
            {
                Partner.Remove(b);
                Touched.Add(b);
            }
        }

        public BWall Of(BWall w) => Partner.TryGetValue(w, out var p) ? p : null;

        public (SectorG s, List<BWall> loop, int at) Find(BWall w)
        {
            foreach (var s in Sectors)
                foreach (var l in s.Loops)
                {
                    int i = l.IndexOf(w);
                    if (i >= 0)
                        return (s, l, i);
                }
            return (null, null, -1);
        }
    }

    Graph Decompose()
    {
        var g = new Graph();
        for (int s = 0; s < Sectors.Count; s++)
        {
            var sg = new SectorG { Rec = Sectors[s], OldIndex = s };
            foreach (var loop in BuildGeom.Loops(this, s))
                sg.Loops.Add(loop.Select(w => Walls[w]).ToList());
            g.Sectors.Add(sg);
        }
        var inLoop = new HashSet<BWall>(g.Sectors.SelectMany(s => s.Loops.SelectMany(l => l)), ReferenceEqualityComparer.Instance);
        for (int w = 0; w < Walls.Count; w++)
        {
            var wall = Walls[w];
            int n = wall.NextWall;
            bool valid = n >= 0 && n < Walls.Count;
            if (valid && Walls[n].NextWall == w)
                g.Partner[wall] = Walls[n];
            g.Stored[wall] = (valid ? Walls[n] : null, n, wall.NextSector, wall.Sector);
            if (!inLoop.Contains(wall))
                g.Orphans.Add((wall, wall.Point2 >= 0 && wall.Point2 < Walls.Count ? Walls[wall.Point2] : null, wall.Point2));
        }
        return g;
    }

    /// <summary>The graph written back as Build's arrays: walls stored sector by sector, loop by loop; Point2, NextWall,
    /// NextSector, the first walls; sector indices renumbered wherever stored (sprites, markers' owners, x-references,
    /// the start sector, names). A sprite whose sector went away gets -1 (the caller re-sectors it).</summary>
    void Commit(Graph g)
    {
        var newOfSector = new int[Sectors.Count];
        Array.Fill(newOfSector, -1);
        for (int i = 0; i < g.Sectors.Count; i++)
            if (g.Sectors[i].OldIndex >= 0)
                newOfSector[g.Sectors[i].OldIndex] = i;
        int ReS(int old) => old >= 0 && old < newOfSector.Length ? newOfSector[old] : old;

        // names of walls follow their objects
        var wallNames = new Dictionary<BWall, string>(ReferenceEqualityComparer.Instance);
        foreach (var kv in Extras.Names.Where(kv => kv.Key.kind == 1).ToList())
        {
            if (kv.Key.index < Walls.Count)
                wallNames[Walls[kv.Key.index]] = kv.Value;
            Extras.Names.Remove(kv.Key);
        }

        var walls = new List<BWall>();
        var index = new Dictionary<BWall, int>(ReferenceEqualityComparer.Instance);
        var sectorOf = new Dictionary<BWall, int>(ReferenceEqualityComparer.Instance);
        for (int s = 0; s < g.Sectors.Count; s++)
        {
            var sg = g.Sectors[s];
            sg.Rec.WallPtr = walls.Count;
            foreach (var loop in sg.Loops)
            {
                int first = walls.Count;
                for (int k = 0; k < loop.Count; k++)
                {
                    index[loop[k]] = walls.Count;
                    sectorOf[loop[k]] = s;
                    walls.Add(loop[k]);
                }
                for (int k = 0; k < loop.Count; k++)
                    loop[k].Point2 = k + 1 < loop.Count ? first + k + 1 : first;
            }
            sg.Rec.WallNum = walls.Count - sg.Rec.WallPtr;
        }
        // walls in no sector's range: kept after the rest, in their order
        foreach (var (w, _, _) in g.Orphans)
            if (!index.ContainsKey(w))
            {
                index[w] = walls.Count;
                walls.Add(w);
            }
        foreach (var (w, p2, raw) in g.Orphans)
            w.Point2 = p2 != null && index.TryGetValue(p2, out int pi2) ? pi2 : raw;
        foreach (var w in walls)
        {
            if (!g.Touched.Contains(w) && g.Stored.TryGetValue(w, out var st))
            {
                // as stored, renumbered: the link's wall where it is now (gone: none); a NextSector that matched the
                // linked wall's sector follows it (a sector split moved it), another value is renumbered as it is
                if (st.next == null)
                {
                    w.NextWall = st.rawNext;
                    w.NextSector = ReS(st.nextSector);
                }
                else if (index.TryGetValue(st.next, out int ni))
                {
                    w.NextWall = ni;
                    bool matched = g.Stored.TryGetValue(st.next, out var nst) && nst.ownSector == st.nextSector;
                    w.NextSector = matched && sectorOf.TryGetValue(st.next, out int ns) ? ns : ReS(st.nextSector);
                }
                else
                {
                    w.NextWall = -1;
                    w.NextSector = -1;
                }
                continue;
            }
            if (g.Partner.TryGetValue(w, out var p) && index.TryGetValue(p, out int pi))
            {
                w.NextWall = pi;
                w.NextSector = sectorOf[p];
            }
            else
            {
                w.NextWall = -1;
                w.NextSector = -1;
            }
        }
        for (int i = 0; i < walls.Count; i++)
            if (walls[i].Xw != null)
                walls[i].Xw.Reference = i;
        foreach (var (w, name) in wallNames)
            if (index.TryGetValue(w, out int wi))
                Extras.Names[(1, wi)] = name;

        foreach (var sp in Sprites)
        {
            sp.SectNum = ReS(sp.SectNum);
            if (sp.StatNum == 10)
                sp.Owner = ReS(sp.Owner);
        }
        Map.StartSect = ReS(Map.StartSect);
        foreach (var kv in Extras.Names.Where(kv => kv.Key.kind == 0).ToList())
        {
            Extras.Names.Remove(kv.Key);
            int ni = ReS(kv.Key.index);
            if (ni >= 0)
                Extras.Names[(0, ni)] = kv.Value;
        }
        var layers = LayersByObject();
        var atmospheres = AtmospheresByObject();
        Sectors.Clear();
        Sectors.AddRange(g.Sectors.Select(x => x.Rec));
        RestoreLayers(layers);
        for (int s = 0; s < Sectors.Count; s++)
            if (Sectors[s].Xs != null)
                Sectors[s].Xs.Reference = s;
        Walls.Clear();
        Walls.AddRange(walls);
        Rebuild();
        // (after the walls: a new sector takes its neighbours' atmosphere)
        RestoreAtmospheres(atmospheres);
    }

    /// <summary>
    /// Sectors mirrored (the scale tool dragged through itself): their loops wind the wrong way for Build, so each is
    /// turned round: every wall goes from its end to its start (it starts where the next one did), the loop's order
    /// reversed with its first wall kept first, its picture x-flipped (cstat 8) so it doesn't read backwards. A red
    /// wall to a sector not among them is cut (it no longer runs against its partner).
    /// </summary>
    public void ReverseLoops(IEnumerable<int> sectors)
    {
        var set = new HashSet<int>(sectors.Where(s => s >= 0 && s < Sectors.Count));
        if (set.Count == 0)
            return;
        var g = Decompose();
        var mine = new HashSet<BWall>(ReferenceEqualityComparer.Instance);
        foreach (int s in set)
            foreach (var loop in g.Sectors[s].Loops)
                mine.UnionWith(loop);
        foreach (int s in set)
        {
            var sg = g.Sectors[s];
            for (int l = 0; l < sg.Loops.Count; l++)
            {
                var loop = sg.Loops[l];
                int n = loop.Count;
                var starts = loop.Select(w => (w.X, w.Y)).ToList();
                for (int k = 0; k < n; k++)
                {
                    (loop[k].X, loop[k].Y) = starts[(k + 1) % n];
                    loop[k].Cstat ^= 8;
                }
                var turned = new List<BWall> { loop[0] };
                for (int k = n - 1; k >= 1; k--)
                    turned.Add(loop[k]);
                sg.Loops[l] = turned;
            }
        }
        foreach (var w in mine)
            if (g.Of(w) is { } p && !mine.Contains(p))
                g.Unlink(w);
        Commit(g);
    }

    static BWall CloneWall(BWall w, int x, int y) => new()
    {
        X = x, Y = y, Point2 = -1, NextWall = -1, NextSector = -1, Cstat = w.Cstat, Picnum = w.Picnum, OverPicnum = w.OverPicnum,
        Shade = w.Shade, Pal = w.Pal, XRepeat = w.XRepeat, YRepeat = w.YRepeat, XPanning = w.XPanning, YPanning = w.YPanning,
        Type = 0, Hitag = 0, Extra = -1,
    };

    static BSector CloneSector(BSector s) => new()
    {
        CeilingZ = s.CeilingZ, FloorZ = s.FloorZ, CeilingStat = s.CeilingStat, FloorStat = s.FloorStat,
        CeilingPicnum = s.CeilingPicnum, CeilingHeinum = s.CeilingHeinum, CeilingShade = s.CeilingShade, CeilingPal = s.CeilingPal,
        CeilingXPanning = s.CeilingXPanning, CeilingYPanning = s.CeilingYPanning, FloorPicnum = s.FloorPicnum,
        FloorHeinum = s.FloorHeinum, FloorShade = s.FloorShade, FloorPal = s.FloorPal, FloorXPanning = s.FloorXPanning,
        FloorYPanning = s.FloorYPanning, Visibility = s.Visibility, FogPal = s.FogPal, Type = 0, Hitag = 0, Extra = -1,
    };

    /// <summary>A wall's x repeat for its length (8 for 1024 units, as maps usually have it).</summary>
    static int RepeatFor(long len) => (int)Math.Clamp(Math.Round(len / 128.0), 1, 255);

    static double Area(IReadOnlyList<Vector2I> pts)
    {
        double a = 0;
        for (int i = 0; i < pts.Count; i++)
        {
            var p = pts[i];
            var q = pts[(i + 1) % pts.Count];
            a += (double)p.X * q.Y - (double)q.X * p.Y;
        }
        return a / 2;
    }

    static double Area(IReadOnlyList<BWall> loop) => Area(loop.Select(w => new Vector2I(w.X, w.Y)).ToList());

    public static bool InPolygon(IReadOnlyList<Vector2I> pts, double x, double y)
    {
        bool c = false;
        for (int i = 0, j = pts.Count - 1; i < pts.Count; j = i++)
        {
            var a = pts[i];
            var b = pts[j];
            if ((a.Y > y) != (b.Y > y) && x < a.X + (y - a.Y) * (b.X - a.X) / (double)(b.Y - a.Y))
                c = !c;
        }
        return c;
    }

    static List<Vector2I> Points2(IEnumerable<BWall> loop) => loop.Select(w => new Vector2I(w.X, w.Y)).ToList();

    /// <summary>A point inside a loop (for telling which part an inner loop goes to): just inside its first wall.</summary>
    static (double x, double y) InsideOf(IReadOnlyList<BWall> loop)
    {
        var a = loop[0];
        var b = loop[1 % loop.Count];
        double mx = (a.X + b.X) * 0.5, my = (a.Y + b.Y) * 0.5, dx = b.X - a.X, dy = b.Y - a.Y, len = Math.Sqrt(dx * dx + dy * dy);
        if (len == 0)
            return (a.X, a.Y);
        // outer loops have their room on the right as drawn; an inner loop's room (the hole) on its right too
        return (mx - dy / len * 0.5, my + dx / len * 0.5);
    }

    // ---------------------------------------------------------------- the first wall and the slopes

    /// <summary>After an operation moved a sector's first wall: a wall of its outer loop along the old first wall's
    /// line direction becomes first, the heights moved to it so the sloped planes stay (heinum turned with the
    /// direction). False (heinum kept, a sloped plane then turns) when none lies that way.</summary>
    bool RepairFirstWall(int s, (int x1, int y1, int x2, int y2) old, double oldCeil, double oldFloor)
    {
        var sec = Sectors[s];
        // (a plane with the slope bit but no rise, heinum 0, is flat: nothing turns)
        bool sloped = ((sec.FloorStat & 2) != 0 && sec.FloorHeinum != 0) || ((sec.CeilingStat & 2) != 0 && sec.CeilingHeinum != 0);
        var (cx1, cy1, cx2, cy2) = BuildGeom.WallEnds(this, sec.WallPtr);
        if (cx1 == old.x1 && cy1 == old.y1 && cx2 == old.x2 && cy2 == old.y2)
            return true;
        long odx = old.x2 - old.x1, ody = old.y2 - old.y1;
        var loops = BuildGeom.Loops(this, s);
        if (loops.Count == 0)
            return false;
        // a wall of any loop along the old first wall's line (the first wall may lie on a hole)
        foreach (int w in loops.SelectMany(l => l))
        {
            var (x1, y1, x2, y2) = BuildGeom.WallEnds(this, w);
            long dx = x2 - x1, dy = y2 - y1;
            if (dx * ody - dy * odx != 0 || (dx == 0 && dy == 0))
                continue;
            bool reversed = dx * odx + dy * ody < 0;
            // the planes' heights at the new first wall's start, from the old first wall
            double ceil = SlopeZ(old, sec.CeilingHeinum, oldCeil, x1, y1, (sec.CeilingStat & 2) != 0);
            double floor = SlopeZ(old, sec.FloorHeinum, oldFloor, x1, y1, (sec.FloorStat & 2) != 0);
            SetFirstWall(s, w);
            sec = Sectors[s];
            sec.CeilingZ = (int)Math.Round(ceil);
            sec.FloorZ = (int)Math.Round(floor);
            if (reversed)
            {
                sec.CeilingHeinum = -sec.CeilingHeinum;
                sec.FloorHeinum = -sec.FloorHeinum;
            }
            return true;
        }
        return !sloped;
    }

    /// <summary>A plane's height at (x, y) relative to a first wall (Build's: heinum / 4096, 16 z a unit across).</summary>
    static double SlopeZ((int x1, int y1, int x2, int y2) first, int heinum, double z, double x, double y, bool sloped)
    {
        if (!sloped)
            return z;
        double dx = first.x2 - first.x1, dy = first.y2 - first.y1;
        double len = Math.Floor(Math.Sqrt(dx * dx + dy * dy));
        if (len == 0)
            return z;
        double dist = (dx * (y - first.y1) - dy * (x - first.x1)) / len;
        return z + heinum * dist * 16 / 4096;
    }

    /// <summary>
    /// Does a drawn path (closed: a loop) cross a wall (of sector s; -1: of every sector), or, closed, hold one inside?
    /// Touching at an allowed point (a split's two ends) doesn't count. Why, or null.
    /// </summary>
    public string CrossesWalls(IReadOnlyList<Vector2I> path, bool closed, int s, IReadOnlyList<Vector2I> allowed, bool strict = true,
        ISet<int> enclosed = null)
    {
        int segs = closed ? path.Count : path.Count - 1;
        // (every sector's: another layer's are out of the way, drawn over freely)
        IEnumerable<int> walls = s >= 0 ? Enumerable.Range(Sectors[s].WallPtr, Sectors[s].WallNum)
            : Enumerable.Range(0, Walls.Count).Where(w => !WallOutOfTheWay(w));
        foreach (int w in walls)
        {
            var (ax, ay, bx, by) = BuildGeom.WallEnds(this, w);
            var c = new Vector2I(ax, ay);
            var d = new Vector2I(bx, by);
            for (int k = 0; k < segs; k++)
                if (Cross(path[k], path[(k + 1) % path.Count], c, d, allowed, strict))
                    return $"the drawing crosses wall {w}";
            // a wall of a sector the drawing goes round (enclosed) may lie inside it
            if (closed && InPolygon(path, ax + 0.0, ay + 0.0) && !OnPath(path, c) && !(enclosed?.Contains(Walls[w].Sector) ?? false))
                return $"wall {w} lies inside the drawing";
        }
        return null;
    }

    /// <summary>The sectors inside a closed loop (each point inside it or on it, one at least strictly inside: an island
    /// may touch the outline at a point, as Build allows): a sector drawn round them has them as islands.</summary>
    public HashSet<int> Enclosed(IReadOnlyList<Vector2I> loop)
    {
        var e = new HashSet<int>();
        for (int s = 0; s < Sectors.Count; s++)
        {
            var sec = Sectors[s];
            if (sec.WallNum <= 0 || OutOfTheWay(s))
                continue;
            bool all = true, strictly = false;
            for (int w = sec.WallPtr; w < sec.WallPtr + sec.WallNum && all; w++)
            {
                var p = new Vector2I(Walls[w].X, Walls[w].Y);
                bool on = OnPath(loop, p);
                all = on || InPolygon(loop, p.X, p.Y);
                strictly |= !on;
            }
            if (all && strictly)
                e.Add(s);
        }
        return e;
    }

    static bool OnPath(IReadOnlyList<Vector2I> path, Vector2I p)
    {
        for (int k = 0; k < path.Count; k++)
            if (SegmentDistance(p.X, p.Y, path[k].X, path[k].Y, path[(k + 1) % path.Count].X, path[(k + 1) % path.Count].Y) < 0.5)
                return true;
        return false;
    }

    static long Orient(Vector2I p, Vector2I q, Vector2I r) => (long)(q.X - p.X) * (r.Y - p.Y) - (long)(q.Y - p.Y) * (r.X - p.X);

    static bool OnSegment(Vector2I p, Vector2I q, Vector2I r) =>
        Math.Min(p.X, q.X) <= r.X && r.X <= Math.Max(p.X, q.X) && Math.Min(p.Y, q.Y) <= r.Y && r.Y <= Math.Max(p.Y, q.Y);

    /// <summary>Do segments a-b and c-d meet anywhere but at an allowed point?</summary>
    static bool Cross(Vector2I a, Vector2I b, Vector2I c, Vector2I d, IReadOnlyList<Vector2I> allowed, bool strict)
    {
        long o1 = Orient(a, b, c), o2 = Orient(a, b, d), o3 = Orient(c, d, a), o4 = Orient(c, d, b);
        if (((o1 > 0 && o2 < 0) || (o1 < 0 && o2 > 0)) && ((o3 > 0 && o4 < 0) || (o3 < 0 && o4 > 0)))
            return true;
        if (!strict)
            return false;
        bool Allowed(Vector2I p) => allowed != null && allowed.Contains(p);
        // touching: an end of one on the other
        if (o1 == 0 && OnSegment(a, b, c) && !Allowed(c)) return true;
        if (o2 == 0 && OnSegment(a, b, d) && !Allowed(d)) return true;
        if (o3 == 0 && OnSegment(c, d, a) && !Allowed(a)) return true;
        if (o4 == 0 && OnSegment(c, d, b) && !Allowed(b)) return true;
        return false;
    }

    // ---------------------------------------------------------------- the operations (each: the caller takes an undo level)

    /// <summary>A point on wall w at (x, y): the wall and the wall behind it both split there.</summary>
    public bool InsertPoint(int w, int x, int y)
    {
        var a = Walls[w];
        var (ax, ay, bx, by) = BuildGeom.WallEnds(this, w);
        if ((x == ax && y == ay) || (x == bx && y == by))
            return false;
        var g = Decompose();
        var (_, loop, at) = g.Find(a);
        if (loop == null)
            return false;
        var n1 = CloneWall(a, x, y);
        loop.Insert(at + 1, n1);
        var p = g.Of(a);
        if (p != null)
        {
            var (_, ploop, pat) = g.Find(p);
            var n2 = CloneWall(p, x, y);
            ploop.Insert(pat + 1, n2);
            // a: A to X, n1: X to B; p: B to X, n2: X to A
            g.Link(a, n2);
            g.Link(n1, p);
        }
        long la = (long)Math.Sqrt((double)(x - ax) * (x - ax) + (double)(y - ay) * (y - ay));
        long lb = (long)Math.Sqrt((double)(bx - x) * (bx - x) + (double)(by - y) * (by - y));
        a.XRepeat = RepeatFor(la);
        n1.XRepeat = RepeatFor(lb);
        if (p != null)
        {
            // p: B to X, its new wall n2: X to A
            p.XRepeat = RepeatFor(lb);
            var (_, pl2, pi2) = g.Find(p);
            if (pl2 != null)
                pl2[(pi2 + 1) % pl2.Count].XRepeat = RepeatFor(la);
        }
        Commit(g);
        return true;
    }

    /// <summary>Removes a point, joining the two walls at it (on both sides of a red wall). Refused (with why) where
    /// the walls face different rooms, more than two rooms meet, or a loop would get fewer than 3 walls.</summary>
    public string DeletePoint(int point)
    {
        var chain = Points[point].Walls;
        var touched = chain.Select(w => Walls[w].Sector).Where(s => s >= 0).Distinct().ToList();
        var firsts = touched.ToDictionary(s => s, FirstOf);
        var g = Decompose();
        var w = Walls[chain[0]];
        var (_, loop, at) = g.Find(w);
        if (loop == null)
            return "the point's wall is in no loop";
        var pw = loop[(at - 1 + loop.Count) % loop.Count];
        var pW = g.Of(w);
        var pPw = g.Of(pw);
        if (chain.Length == 1)
        {
            if (pW != null || pPw != null)
                return "the walls at the point face other rooms";
            if (loop.Count <= 3)
                return "the loop would have fewer than 3 walls";
            loop.Remove(w);
        }
        else if (chain.Length == 2)
        {
            // both walls red, facing the same room: v starts at the point in it
            var v = Walls[chain[1]];
            var (_, vloop, vat) = g.Find(v);
            if (vloop == null || pPw != v)
                return "the walls at the point face different rooms";
            var pv = vloop[(vat - 1 + vloop.Count) % vloop.Count];
            if (pW != pv)
                return "the walls at the point face different rooms";
            // a triangle on one side (its sector's only loop) closes up and goes (UDB's): the other side's wall runs
            // straight across and faces what the triangle's third wall faced
            var (ws, _, _) = g.Find(w);
            var (vs, _, _) = g.Find(v);
            if (vloop.Count == 3 && loop.Count > 3 && vs.Loops.Count == 1)
                return DropTriangle(g, vs, vloop, v, loop, w, pw, firsts);
            if (loop.Count == 3 && vloop.Count > 3 && ws.Loops.Count == 1)
                return DropTriangle(g, ws, loop, w, vloop, v, pv, firsts);
            if (loop.Count <= 3 || vloop.Count <= 3)
                return "a loop would have fewer than 3 walls";
            loop.Remove(w);
            vloop.Remove(v);
            g.Link(pw, pv);
        }
        else
            return $"{chain.Length} walls meet at the point (more than two rooms)";
        Commit(g);
        foreach (var (s, old) in firsts)
            Repair(s, old);
        Rebuild();
        return null;
    }

    /// <summary>
    /// DeletePoint's triangle: the point P is a corner of triangle tri (tv starts at P in it) and of the room on the
    /// other side (keep, w starting at P, pw ending there; w and pw face the triangle's two walls at P). The triangle
    /// goes; pw then runs from its start to w's end, joined to whatever the triangle's third wall faced (white if
    /// nothing). The triangle's sprites go to the sector now under them, its markers go.
    /// </summary>
    string DropTriangle(Graph g, SectorG tri, List<BWall> triLoop, BWall tv, List<BWall> keep, BWall w, BWall pw,
        Dictionary<int, (int x1, int y1, int x2, int y2, double ceil, double floor)> firsts)
    {
        int at = triLoop.IndexOf(tv);
        var third = triLoop[(at + 1) % 3];
        var outside = g.Of(third);
        if (outside != null && ReferenceEquals(g.Find(outside).s, g.Find(w).s))
            return "the triangle's third wall faces the same room";
        int triIndex = tri.OldIndex;
        keep.Remove(w);
        foreach (var x in triLoop)
            g.Unlink(x);
        g.Sectors.Remove(tri);
        if (outside != null)
            g.Link(pw, outside);
        var end = keep[(keep.IndexOf(pw) + 1) % keep.Count];
        pw.XRepeat = RepeatFor((long)Math.Sqrt((double)(end.X - pw.X) * (end.X - pw.X) + (double)(end.Y - pw.Y) * (end.Y - pw.Y)));
        var markers = Sprites.Where(sp => sp.StatNum == 10 && sp.Owner == triIndex).ToList();
        var homeless = Sprites.Where(sp => sp.SectNum == triIndex && !markers.Contains(sp)).ToList();
        Commit(g);
        foreach (var sp in homeless)
            sp.SectNum = BuildGeom.UpdateSector(this, sp.X, sp.Y, -1);
        DeleteSprites(markers.Concat(homeless.Where(sp => sp.SectNum < 0)).Select(sp => Sprites.IndexOf(sp)).ToList());
        // the sectors after the triangle moved down one
        foreach (var (s, old) in firsts)
            if (s != triIndex)
                Repair(s > triIndex ? s - 1 : s, old);
        Rebuild();
        return null;
    }

    /// <summary>Deletes a sector: the walls facing it turn white, its markers go (its room links' at both ends), the
    /// sprites in it go to the sector now under them (deleted where none is).</summary>
    public void DeleteSector(int s)
    {
        var g = Decompose();
        var sg = g.Sectors[s];
        foreach (var loop in sg.Loops)
            foreach (var w in loop)
                g.Unlink(w);
        g.Sectors.RemoveAt(s);
        // its markers go, its room links' (both ends: Blood/RoomLinks.cs); every other sprite in it (a marker of another
        // sector too) to the sector now under it
        var markers = Sprites.Where(sp => sp.StatNum == 10 && sp.Owner == s).Concat(Chosen.Forge.Blood.RoomLinks.Unlink(this, s)).Distinct().ToList();
        var homeless = Sprites.Where(sp => sp.SectNum == s && !markers.Contains(sp)).ToList();
        Commit(g);
        foreach (var sp in homeless)
        {
            sp.SectNum = BuildGeom.UpdateSector(this, sp.X, sp.Y, -1);
            // another sector's marker (a pivot may lie outside every sector) stays, in its owner's sector
            if (sp.SectNum < 0 && sp.StatNum == 10 && sp.Owner >= 0 && sp.Owner < Sectors.Count)
                sp.SectNum = sp.Owner;
        }
        var gone = markers.Concat(homeless.Where(sp => sp.SectNum < 0)).Select(sp => Sprites.IndexOf(sp)).ToList();
        DeleteSprites(gone);
        Rebuild();
    }

    /// <summary>Two white walls lying on each other the other way (the same two points reversed) joined into a red
    /// pair (Walls mode: both selected, J). Why not, or null.</summary>
    public string JoinWalls(int a, int b)
    {
        if (a == b || Walls[a].NextWall >= 0 || Walls[b].NextWall >= 0)
            return "join walls: both must be white walls";
        var (ax1, ay1, ax2, ay2) = BuildGeom.WallEnds(this, a);
        var (bx1, by1, bx2, by2) = BuildGeom.WallEnds(this, b);
        if (ax1 != bx2 || ay1 != by2 || ax2 != bx1 || ay2 != by1)
            return "join walls: they don't lie on each other (the same two points, the other way)";
        if (Walls[a].Sector == Walls[b].Sector)
            return "join walls: they are walls of one sector";
        var g = Decompose();
        g.Link(Walls[a], Walls[b]);
        Commit(g);
        return null;
    }

    /// <summary>
    /// After a move: the white walls of these sectors lying along white walls of other sectors (of the shown, editable
    /// layer) the other way joined into red pairs, so their points merge (UDB's merge on drop): a wall only partly along
    /// the other first split where the other's end falls on it (both ways). The number of pairs joined.
    /// </summary>
    public int JoinTouching(ICollection<int> sectors)
    {
        int joined = 0;
        for (int guard = 0; guard < 4096; guard++)
        {
            if (!JoinOneTouching(sectors, ref joined))
                break;
        }
        return joined;
    }

    /// <summary>One step of JoinTouching (a join or a split); false when there is none left.</summary>
    bool JoinOneTouching(ICollection<int> sectors, ref int joined)
    {
        foreach (int s in sectors)
        {
            if (s < 0 || s >= Sectors.Count)
                continue;
            var sec = Sectors[s];
            for (int a = sec.WallPtr; a < sec.WallPtr + sec.WallNum; a++)
            {
                if (Walls[a].NextWall >= 0)
                    continue;
                var (ax1, ay1, ax2, ay2) = BuildGeom.WallEnds(this, a);
                long dx = ax2 - ax1, dy = ay2 - ay1, len2 = dx * dx + dy * dy;
                if (len2 == 0)
                    continue;
                int minX = Math.Min(ax1, ax2), maxX = Math.Max(ax1, ax2), minY = Math.Min(ay1, ay2), maxY = Math.Max(ay1, ay2);
                for (int b = 0; b < Walls.Count; b++)
                {
                    var wb = Walls[b];
                    if (wb.NextWall >= 0 || wb.Sector == s || wb.Sector < 0 || !SectorEditable(wb.Sector))
                        continue;
                    var (bx1, by1, bx2, by2) = BuildGeom.WallEnds(this, b);
                    if (Math.Max(bx1, bx2) < minX || Math.Min(bx1, bx2) > maxX || Math.Max(by1, by2) < minY || Math.Min(by1, by2) > maxY)
                        continue;
                    // on a's line, the other way, overlapping
                    if (dx * (by1 - ay1) - dy * (bx1 - ax1) != 0 || dx * (by2 - ay2) - dy * (bx2 - ax2) != 0)
                        continue;
                    long t1 = (bx1 - ax1) * dx + (by1 - ay1) * dy, t2 = (bx2 - ax1) * dx + (by2 - ay1) * dy;
                    if (t1 <= t2 || Math.Min(t1, len2) - Math.Max(t2, 0) <= 0)
                        continue;
                    if (bx1 == ax2 && by1 == ay2 && bx2 == ax1 && by2 == ay1)
                    {
                        if (JoinWalls(a, b) == null)
                        {
                            joined++;
                            return true;
                        }
                        continue;
                    }
                    // an end of one inside the other: split there, then look again (the walls renumbered)
                    if (t1 < len2 && t1 > 0 && InsertPoint(a, bx1, by1))
                        return true;
                    if (t2 > 0 && t2 < len2 && InsertPoint(a, bx2, by2))
                        return true;
                    if (t1 > len2 && InsertPoint(b, ax2, ay2))
                        return true;
                    if (t2 < 0 && InsertPoint(b, ax1, ay1))
                        return true;
                }
            }
        }
        return false;
    }

    /// <summary>
    /// A white wall collapsed (Walls mode's Delete): its two ends become one point at its middle, so the wall goes and
    /// the walls before and after it meet there (every wall at either end moves there with them: the rooms against
    /// them follow). Refused (why) for a red wall (Delete joins its two rooms instead), a loop that would get fewer
    /// than 3 walls, or two walls of one loop that would meet there.
    /// </summary>
    public string CollapseWall(int w)
    {
        if (w < 0 || w >= Walls.Count)
            return "no such wall";
        if (Walls[w].NextWall >= 0)
            return "a red wall: Delete joins its two rooms";
        int s = Walls[w].Sector;
        var (ax, ay, bx, by) = BuildGeom.WallEnds(this, w);
        var m = new Vector2I((int)Math.Round((ax + bx) * 0.5), (int)Math.Round((ay + by) * 0.5));
        int pa = PointOf[w], pb = PointOf[Walls[w].Point2];
        if (pa < 0 || pb < 0 || pa == pb)
            return "the wall's ends are one point";
        var atA = Points[pa].Walls.Select(i => Walls[i]).ToList();
        var atB = Points[pb].Walls.Select(i => Walls[i]).ToList();
        var touched = atA.Concat(atB).Select(x => x.Sector).Where(x => x >= 0).Distinct().ToList();
        var firsts = touched.ToDictionary(x => x, FirstOf);
        var g = Decompose();
        var (_, loop, _) = g.Find(Walls[w]);
        if (loop == null)
            return "the wall is in no loop";
        if (loop.Count <= 3)
            return "the loop would have fewer than 3 walls (delete the sector instead)";
        loop.Remove(Walls[w]);
        foreach (var x in atA.Concat(atB))
        {
            x.X = m.X;
            x.Y = m.Y;
        }
        // (a loop of the sector passing both ends some other way would now touch itself)
        foreach (var sg in g.Sectors)
            foreach (var l in sg.Loops)
                if (l.Count(x => x.X == m.X && x.Y == m.Y) > 1)
                    return "two walls of one loop would meet at the wall's middle";
        Commit(g);
        foreach (var (x, old) in firsts)
            Repair(x, old);
        Rebuild();
        return null;
    }

    /// <summary>
    /// Sectors separated from those round them (the context menu's Separate Sector): every red wall of their outer
    /// loops to a sector not among them made white on both sides, so their points are their own and a drag moves them
    /// alone (the sectors against them no longer stretch). The rooms in them (their holes' walls) stay joined: they
    /// move with them. A room separated from the sector round it leaves that sector's hole. The walls cut.
    /// </summary>
    public int SeparateSectors(ICollection<int> sectors)
    {
        var set = new HashSet<int>(sectors.Where(s => s >= 0 && s < Sectors.Count));
        var cut = new List<int>();
        foreach (int s in set)
        {
            var outer = BuildGeom.OuterLoop(this, s);
            if (outer == null)
                continue;
            foreach (int w in outer)
                if (Walls[w].NextWall >= 0 && !set.Contains(Walls[w].NextSector))
                    cut.Add(w);
        }
        if (cut.Count == 0)
            return 0;
        var g = Decompose();
        foreach (int w in cut)
            g.Unlink(Walls[w]);
        Commit(g);
        return cut.Count;
    }

    /// <summary>A red wall made white on both sides.</summary>
    public bool DetachWall(int w)
    {
        if (Walls[w].NextWall < 0)
            return false;
        var g = Decompose();
        g.Unlink(Walls[w]);
        Commit(g);
        return true;
    }

    /// <summary>A white wall joined with a white wall lying on it the other way (the same two points reversed) into a
    /// red pair. False if there is none.</summary>
    public bool AttachWall(int w)
    {
        var g = Decompose();
        bool done = AttachWhite(g, Walls[w]);
        if (done)
            Commit(g);
        return done;
    }

    static (int x1, int y1, int x2, int y2) Ends(Graph g, BWall w)
    {
        var (_, loop, at) = g.Find(w);
        var b = loop[(at + 1) % loop.Count];
        return (w.X, w.Y, b.X, b.Y);
    }

    bool AttachWhite(Graph g, BWall w)
    {
        if (g.Of(w) != null)
            return false;
        var (x1, y1, x2, y2) = Ends(g, w);
        var (ws, _, _) = g.Find(w);
        foreach (var s in g.Sectors)
        {
            if (s == ws || (s.OldIndex >= 0 && !SectorShown(s.OldIndex)))
                continue;
            foreach (var loop in s.Loops)
                for (int k = 0; k < loop.Count; k++)
                {
                    var o = loop[k];
                    var e = loop[(k + 1) % loop.Count];
                    if (o.X == x2 && o.Y == y2 && e.X == x1 && e.Y == y1 && g.Of(o) == null)
                    {
                        g.Link(w, o);
                        return true;
                    }
                }
        }
        return false;
    }

    /// <summary>The sector whose properties a new room drawn at (x, y) takes: the nearest by its label.</summary>
    BSector Template(double x, double y)
    {
        BSector best = null;
        double bd = double.MaxValue;
        for (int s = 0; s < Sectors.Count; s++)
        {
            double dx = Labels[s].X - x, dy = Labels[s].Y - y, d = dx * dx + dy * dy;
            if (d < bd)
            {
                bd = d;
                best = Sectors[s];
            }
        }
        return best;
    }

    static readonly BSector DefaultSector = new() { CeilingZ = -16384 * 4, FloorZ = 0, Visibility = 0 };
    static readonly BWall DefaultWall = new() { XRepeat = 8, YRepeat = 8 };

    /// <summary>A closed loop drawn outside every sector: a new sector of white walls (joined as red pairs where they
    /// lie on white walls of others). Sectors wholly inside it are its islands (UDB's drawing round a pillar or a
    /// room): it gets an inner loop round each group of them, joined to their outer walls. Its index, or -1 (why).</summary>
    public int MakeSector(List<Vector2I> pts) => MakeSector(pts, out _);

    /// <summary>over: a sector over what lies there (a room over a room, Alt on the closing click): no islands, no
    /// crossing checks; it joins only white walls lying exactly on its sides the other way.</summary>
    public int MakeSector(List<Vector2I> pts, out string why, bool over = false)
    {
        why = null;
        if (pts.Count < 3)
        {
            why = "a sector needs 3 points";
            return -1;
        }
        var loop = new List<Vector2I>(pts);
        if (Area(loop) < 0)
            loop.Reverse();
        var c = loop.Aggregate(Vector2.Zero, (acc, p) => acc + new Vector2(p.X, p.Y)) / loop.Count;
        var tmpl = Template(c.X, c.Y) ?? DefaultSector;
        var wallTmpl = Walls.Count > 0 ? Walls[tmpl.WallPtr >= 0 && tmpl.WallPtr < Walls.Count ? tmpl.WallPtr : 0] : DefaultWall;
        var enclosed = over ? new HashSet<int>() : Enclosed(loop);
        // (it may lie along other sectors' walls and share their points: only a crossing, or a wall inside that isn't
        // an island's, refuses)
        if (!over && (why = CrossesWalls(loop, true, -1, null, false, enclosed)) != null)
            return -1;
        var g = Decompose();
        var sg = new SectorG { Rec = CloneSector(tmpl) };
        // flat: a slope is relative to a first wall, which this sector's isn't
        sg.Rec.FloorHeinum = sg.Rec.CeilingHeinum = 0;
        sg.Rec.FloorStat &= ~2;
        sg.Rec.CeilingStat &= ~2;
        sg.Loops.Add(MakeWalls(loop, wallTmpl));
        // the islands: their walls facing no other sector, chained into loops; each a hole of the new sector, the same
        // points the other way round, joined to them
        var edge = g.Sectors.Where(x => x.OldIndex >= 0 && enclosed.Contains(x.OldIndex))
            .SelectMany(x => x.Loops.Take(1).SelectMany(l => l.Select((w, k) => (w, end: l[(k + 1) % l.Count]))))
            .Where(t => g.Of(t.w) == null).ToList();
        var startsAt = edge.GroupBy(t => (t.w.X, t.w.Y)).ToDictionary(gr => gr.Key, gr => gr.ToList());
        var used = new HashSet<BWall>(ReferenceEqualityComparer.Instance);
        foreach (var first in edge)
        {
            if (used.Contains(first.w))
                continue;
            var island = new List<(BWall w, BWall end)>();
            var t = first;
            while (used.Add(t.w))
            {
                island.Add(t);
                if (!startsAt.TryGetValue((t.end.X, t.end.Y), out var next) || next.FirstOrDefault(o => !used.Contains(o.w)) is not { w: not null } nt)
                    break;
                t = nt;
            }
            if (island.Count < 3 || island[^1].end.X != island[0].w.X || island[^1].end.Y != island[0].w.Y)
            {
                why = "the sectors inside the drawing don't make closed islands";
                return -1;
            }
            // hole wall k runs end_k -> start_k, reversing island wall k; in loop order k = n-1 .. 0
            var hole = new List<BWall>();
            for (int k = island.Count - 1; k >= 0; k--)
            {
                var h = CloneWall(wallTmpl, island[k].end.X, island[k].end.Y);
                h.Cstat &= ~(1 | 16 | 32 | 64);
                hole.Add(h);
            }
            for (int k = 0; k < island.Count; k++)
            {
                var hw = hole[island.Count - 1 - k];
                long dx = island[k].end.X - island[k].w.X, dy = island[k].end.Y - island[k].w.Y;
                hw.XRepeat = RepeatFor((long)Math.Sqrt((double)dx * dx + (double)dy * dy));
                g.Link(hw, island[k].w);
            }
            sg.Loops.Add(hole);
        }
        g.Sectors.Add(sg);
        foreach (var w in sg.Loops[0])
            AttachWhite(g, w);
        Commit(g);
        return Sectors.Count - 1;
    }

    /// <summary>
    /// A closed loop drawn outside the sectors, joined to the sectors it lies against (UDB's drawing next to a room):
    /// a point of the map on one of its sides becomes a corner of it, a corner of it on a wall becomes a point of that
    /// wall (on both sides of a red one); then MakeSector joins the walls lying on each other into red pairs. Its index,
    /// or -1.
    /// </summary>
    public int MakeSectorJoined(List<Vector2I> drawn) => MakeSectorJoined(drawn, out _);

    public int MakeSectorJoined(List<Vector2I> drawn, out string why)
    {
        var pts = new List<Vector2I>();
        for (int i = 0; i < drawn.Count; i++)
        {
            var a = drawn[i];
            var b = drawn[(i + 1) % drawn.Count];
            pts.Add(a);
            // the map's points on this side, in order from a to b
            var on = new List<(double t, Vector2I p)>();
            double len = ((Vector2)(b - a)).LengthSquared();
            foreach (var p in Points)
            {
                // a point of a free (white) wall only: a red wall's both sides are taken (a room over a room there)
                if (!p.Walls.Any(w => Walls[w].NextWall < 0 || (PrevWall[w] >= 0 && Walls[PrevWall[w]].NextWall < 0)))
                    continue;
                // (a hidden layer's point isn't there; a shown one's may be joined)
                if (!p.Walls.Any(WallShown))
                    continue;
                var q = new Vector2I(p.X, p.Y);
                if (q == a || q == b || len == 0 || SegmentDistance(q.X, q.Y, a.X, a.Y, b.X, b.Y) > 0.5)
                    continue;
                double t = ((double)(q.X - a.X) * (b.X - a.X) + (double)(q.Y - a.Y) * (b.Y - a.Y)) / len;
                if (t > 0 && t < 1 && on.All(o => o.p != q))
                    on.Add((t, q));
            }
            pts.AddRange(on.OrderBy(o => o.t).Select(o => o.p));
        }
        // the corners lying on walls (not at their ends) split those walls
        foreach (var p in pts)
            for (int w = 0; w < Walls.Count; w++)
            {
                if (Walls[w].NextWall >= 0 || !WallShown(w))
                    continue;
                var (ax, ay, bx, by) = BuildGeom.WallEnds(this, w);
                if ((ax == p.X && ay == p.Y) || (bx == p.X && by == p.Y) || Orient(new(ax, ay), new(bx, by), p) != 0
                    || !OnSegment(new(ax, ay), new(bx, by), p))
                    continue;
                InsertPoint(w, p.X, p.Y);
                break;
            }
        return MakeSector(pts, out why);
    }

    static List<BWall> MakeWalls(List<Vector2I> loop, BWall tmpl)
    {
        var l = new List<BWall>();
        for (int i = 0; i < loop.Count; i++)
        {
            var p = loop[i];
            var q = loop[(i + 1) % loop.Count];
            var w = CloneWall(tmpl, p.X, p.Y);
            w.XRepeat = RepeatFor((long)Math.Sqrt((double)(q.X - p.X) * (q.X - p.X) + (double)(q.Y - p.Y) * (q.Y - p.Y)));
            w.Cstat &= ~(1 | 16 | 32 | 64);
            l.Add(w);
        }
        return l;
    }

    /// <summary>A closed loop drawn inside sector parent (touching none of its walls): a new sector there, the
    /// parent's hole around it, joined by red walls; the new sector takes the parent's properties. Its index.</summary>
    public int InsertInnerLoop(int parent, List<Vector2I> pts)
    {
        if (pts.Count < 3)
            return -1;
        var loop = new List<Vector2I>(pts);
        if (Area(loop) < 0)
            loop.Reverse();
        // it may touch the sector's walls at their points (Build allows a hole to share a point with another loop)
        var corners = TouchingCorners(parent, loop);
        if (CrossesWalls(loop, true, parent, corners) != null || loop.Any(p => !corners.Contains(p) && !BuildGeom.Inside(this, parent, p.X, p.Y)))
            return -1;
        // (touching at points only: no side along one of its walls)
        for (int k = 0; k < loop.Count; k++)
        {
            var m = ((Vector2)loop[k] + loop[(k + 1) % loop.Count]) * 0.5f;
            for (int w = Sectors[parent].WallPtr; w < Sectors[parent].WallPtr + Sectors[parent].WallNum; w++)
            {
                var (ax, ay, bx, by) = BuildGeom.WallEnds(this, w);
                if (SegmentDistance(m.X, m.Y, ax, ay, bx, by) < 0.5)
                    return -1;
            }
        }
        var parentFirst = FirstOf(parent);
        var g = Decompose();
        var ps = g.Sectors[parent];
        var tmplWall = ps.Loops[0][0];
        var inner = new SectorG { Rec = CloneSector(ps.Rec) };
        var outerWalls = MakeWalls(loop, tmplWall);
        inner.Loops.Add(outerWalls);
        // the parent's hole: the same points the other way round
        var rev = new List<Vector2I>(loop);
        rev.Reverse();
        var hole = MakeWalls(rev, tmplWall);
        ps.Loops.Add(hole);
        // hole wall k goes rev[k] -> rev[k+1], which is loop[n-1-k] -> loop[n-2-k]: the reverse of loop wall n-2-k
        int n = loop.Count;
        for (int k = 0; k < n; k++)
            g.Link(hole[k], outerWalls[((n - 2 - k) % n + n) % n]);
        // the new sector goes after the others (so the map's sectors keep their indices)
        g.Sectors.Add(inner);
        Commit(g);
        int idx = Sectors.Count - 1;
        Repair(idx, parentFirst);
        foreach (int i in Enumerable.Range(0, Sprites.Count))
            if (Sprites[i].SectNum == parent && InPolygon(loop, Sprites[i].X, Sprites[i].Y))
                Sprites[i].SectNum = idx;
        return idx;
    }

    /// <summary>A pillar in sector s: the loop pts (inside it, crossing and lying along none of its walls) made a hole of
    /// white walls in it (the sector's first wall's look). The index of the hole's first wall, -1 when it doesn't fit.</summary>
    public int AddHole(int s, List<Vector2I> pts)
    {
        if (pts.Count < 3)
            return -1;
        var loop = new List<Vector2I>(pts);
        if (Area(loop) < 0)
            loop.Reverse();
        if (CrossesWalls(loop, true, s, Array.Empty<Vector2I>()) != null || loop.Any(p => !BuildGeom.Inside(this, s, p.X, p.Y)))
            return -1;
        var first = FirstOf(s);
        var g = Decompose();
        var sg = g.Sectors[s];
        // (a hole runs the other way round from the sector's outer loop)
        var rev = new List<Vector2I>(loop);
        rev.Reverse();
        var hole = MakeWalls(rev, sg.Loops[0][0]);
        sg.Loops.Add(hole);
        Commit(g);
        Repair(s, first);
        return Walls.IndexOf(hole[0]);
    }

    /// <summary>The hole (a loop of white walls after a sector's outer one) wall w is in, taken away; false when w is
    /// in an outer loop or the loop has a red wall.</summary>
    public bool RemoveHole(int w)
    {
        if (w < 0 || w >= Walls.Count)
            return false;
        int s = Walls[w].Sector;
        var first = FirstOf(s);
        var g = Decompose();
        var (sg, loop, _) = g.Find(Walls[w]);
        if (sg == null || ReferenceEquals(sg.Loops[0], loop) || loop.Any(x => g.Of(x) != null))
            return false;
        sg.Loops.Remove(loop);
        Commit(g);
        Repair(s, first);
        return true;
    }

    /// <summary>Is p on sector s's outer loop: at one of its points, or on one of its walls (then the wall)?</summary>
    public (bool on, int wall) OnOuterLoop(int s, Vector2I p)
    {
        var outer = BuildGeom.OuterLoop(this, s);
        // a point of the loop first (the wall ending there is near it too)
        foreach (int w in outer)
            if (Walls[w].X == p.X && Walls[w].Y == p.Y)
                return (true, -1);
        foreach (int w in outer)
        {
            var (ax, ay, bx, by) = BuildGeom.WallEnds(this, w);
            if (SegmentDistance(p.X, p.Y, ax, ay, bx, by) < 0.75)
                return (true, w);
        }
        return (false, -1);
    }

    /// <summary>Does the closed loop pts fit in sector s as a room: inside it, touching its walls at their points at
    /// most (not along them), crossing none?</summary>
    public bool FitsAsRoom(int s, List<Vector2I> pts)
    {
        if (s < 0 || s >= Sectors.Count)
            return false;
        var corners = TouchingCorners(s, pts);
        // (a side along one of its walls is no room either: a cell of Draw Grid beside the room the cell before made,
        // sharing its side, crosses nothing but lies along it)
        for (int k = 0; k < pts.Count; k++)
        {
            double mx = (pts[k].X + pts[(k + 1) % pts.Count].X) * 0.5, my = (pts[k].Y + pts[(k + 1) % pts.Count].Y) * 0.5;
            for (int w = Sectors[s].WallPtr; w < Sectors[s].WallPtr + Sectors[s].WallNum; w++)
            {
                var (ax, ay, bx, by) = BuildGeom.WallEnds(this, w);
                if (SegmentDistance(mx, my, ax, ay, bx, by) < 0.5)
                    return false;
            }
        }
        return pts.All(p => corners.Contains(p) || (EditableSectorsAt(p.X, p.Y).Contains(s) && !OnOuterLoop(s, p).on))
            && CrossesWalls(pts, true, s, corners) == null;
    }

    /// <summary>The points of a drawn loop that are points of sector s's walls (a room touching them there).</summary>
    public List<Vector2I> TouchingCorners(int s, IReadOnlyList<Vector2I> loop)
    {
        var mine = new HashSet<Vector2I>(Enumerable.Range(Sectors[s].WallPtr, Sectors[s].WallNum).Select(w => new Vector2I(Walls[w].X, Walls[w].Y)));
        return loop.Where(mine.Contains).ToList();
    }

    /// <summary>
    /// Splits sector s along a path from one point of its outer loop to another (path[0] and path[^1] must be points
    /// of it: insert them first), through the sector: the part from the start round to the end keeps s, the rest is a
    /// new sector with s's properties; the path is a red pair between them; inner loops and sprites go to the part
    /// holding them. The first walls are repaired so sloped planes stay. The new sector's index, or -1 (and why).
    /// </summary>
    public (int sector, string error) SplitSector(int s, List<Vector2I> path)
    {
        if (path.Count < 2)
            return (-1, "the path needs two points");
        var g = Decompose();
        var sg = g.Sectors[s];
        // the outer loop (not necessarily stored first)
        var outer = sg.Loops.OrderByDescending(l => Area(l)).First();
        int i0 = outer.FindIndex(w => w.X == path[0].X && w.Y == path[0].Y);
        int i1 = outer.FindIndex(w => w.X == path[^1].X && w.Y == path[^1].Y);
        if (i0 < 0 || i1 < 0)
            return (-1, "the path must start and end on points of the sector's outer walls");
        if (i0 == i1)
            return (-1, "the path starts and ends at the same point");
        if (path.Count == 2 && (outer[(i0 + 1) % outer.Count] == outer[i1] || outer[(i1 + 1) % outer.Count] == outer[i0]))
            return (-1, "that is a wall already");
        if (CrossesWalls(path, false, s, new[] { path[0], path[^1] }) is { } cross)
            return (-1, cross);
        for (int k = 0; k + 1 < path.Count; k++)
        {
            double mx = (path[k].X + path[k + 1].X) * 0.5, my = (path[k].Y + path[k + 1].Y) * 0.5;
            if (!BuildGeom.Inside(this, s, mx, my))
                return (-1, "the path leaves the sector");
        }
        var oldFirst = BuildGeom.WallEnds(this, Sectors[s].WallPtr);
        double oldCeil = Sectors[s].CeilingZ, oldFloor = Sectors[s].FloorZ;
        var tmpl = outer[i0];
        // part A: outer walls i0 .. i1-1, then the path back from the end to the start
        var a = new List<BWall>();
        for (int k = i0; k != i1; k = (k + 1) % outer.Count)
            a.Add(outer[k]);
        var back = new List<Vector2I>();
        for (int k = path.Count - 1; k > 0; k--)
            back.Add(path[k]);
        var aPath = new List<BWall>();
        for (int k = 0; k < back.Count; k++)
            aPath.Add(CloneWall(tmpl, back[k].X, back[k].Y));
        a.AddRange(aPath);
        // part B: outer walls i1 .. i0-1, then the path from the start to the end
        var b = new List<BWall>();
        for (int k = i1; k != i0; k = (k + 1) % outer.Count)
            b.Add(outer[k]);
        var bPath = new List<BWall>();
        for (int k = 0; k < path.Count - 1; k++)
            bPath.Add(CloneWall(tmpl, path[k].X, path[k].Y));
        b.AddRange(bPath);
        if (Area(a) <= 0 || Area(b) <= 0)
            return (-1, "the path leaves the sector or crosses itself");
        // aPath[k]: back[k] -> back[k+1] = path[n-1-k] -> path[n-2-k]; bPath[j]: path[j] -> path[j+1]: partners j = n-2-k
        int n = path.Count;
        for (int k = 0; k < aPath.Count; k++)
            g.Link(aPath[k], bPath[n - 2 - k]);
        foreach (var w in aPath.Concat(bPath))
        {
            w.Cstat &= ~(1 | 16 | 32 | 64);
            w.Type = 0;
        }
        var pa = Points2(a);
        var pb = Points2(b);
        var inners = sg.Loops.Where(l => !ReferenceEquals(l, outer)).ToList();
        sg.Loops = new List<List<BWall>> { a };
        var nb = new SectorG { Rec = CloneSector(sg.Rec) };
        nb.Loops.Add(b);
        foreach (var l in inners)
        {
            var (x, y) = InsideOf(l);
            if (InPolygon(pb, l[0].X + 0.5 * Math.Sign(x - l[0].X), l[0].Y + 0.5 * Math.Sign(y - l[0].Y)) && !InPolygon(pa, l[0].X, l[0].Y))
                nb.Loops.Add(l);
            else
                sg.Loops.Add(l);
        }
        // the first wall: if it went to part B, part A starts at its first wall and is repaired below
        g.Sectors.Add(nb);
        var moved = Sprites.Where(sp => sp.SectNum == s && InPolygon(pb, sp.X, sp.Y)).ToList();
        Commit(g);
        int ni = Sectors.Count - 1;
        foreach (var sp in moved)
            sp.SectNum = ni;
        bool okA = RepairFirstWall(s, oldFirst, oldCeil, oldFloor);
        bool okB = RepairFirstWall(ni, oldFirst, oldCeil, oldFloor);
        Rebuild();
        if (!okA || !okB)
            Warning = "a sloped part has no wall along the old first wall: its slope turned";
        return (ni, null);
    }

    /// <summary>
    /// Joins sector b into a (they share red walls): the shared walls go, the walls left form a's loops, b's sprites
    /// and properties-free space become a's; b's markers are deleted. Refused (with why) if they share no wall or the
    /// result isn't one room.
    /// </summary>
    public string JoinSectors(int a, int b)
    {
        if (a == b)
            return "the same sector";
        var g = Decompose();
        var sa = g.Sectors[a];
        var sb = g.Sectors[b];
        var setA = new HashSet<BWall>(sa.Loops.SelectMany(l => l), ReferenceEqualityComparer.Instance);
        var setB = new HashSet<BWall>(sb.Loops.SelectMany(l => l), ReferenceEqualityComparer.Instance);
        var shared = setA.Where(w => g.Of(w) is { } p && setB.Contains(p)).ToList();
        if (shared.Count == 0)
            return "the sectors share no wall";
        var oldFirst = BuildGeom.WallEnds(this, Sectors[a].WallPtr);
        double oldCeil = Sectors[a].CeilingZ, oldFloor = Sectors[a].FloorZ;
        var firstWall = Walls[Sectors[a].WallPtr];
        var gone = new HashSet<BWall>(ReferenceEqualityComparer.Instance);
        foreach (var w in shared)
        {
            gone.Add(w);
            gone.Add(g.Of(w));
        }
        // each remaining wall's end, and who starts where
        var rest = sa.Loops.Concat(sb.Loops).SelectMany(l => l.Select((w, k) => (w, end: l[(k + 1) % l.Count], loop: l)))
            .Where(t => !gone.Contains(t.w)).ToList();
        var startsAt = new Dictionary<(int, int), List<BWall>>();
        foreach (var t in rest)
        {
            var key = (t.w.X, t.w.Y);
            if (!startsAt.TryGetValue(key, out var l))
                startsAt[key] = l = new List<BWall>();
            l.Add(t.w);
        }
        var endOf = rest.ToDictionary(t => t.w, t => (t.end.X, t.end.Y), ReferenceEqualityComparer.Instance);
        var nextInLoop = rest.ToDictionary(t => t.w, t => t.end, ReferenceEqualityComparer.Instance);
        var used = new HashSet<BWall>(ReferenceEqualityComparer.Instance);
        var loops = new List<List<BWall>>();
        foreach (var t in rest)
        {
            if (used.Contains(t.w))
                continue;
            var loop = new List<BWall>();
            var w = t.w;
            while (w != null && used.Add(w))
            {
                loop.Add(w);
                // on along its own loop where that wall is left, else the wall starting where this one ends
                var nx = nextInLoop[w];
                if (gone.Contains(nx) || used.Contains(nx))
                    nx = startsAt.GetValueOrDefault(endOf[w])?.FirstOrDefault(o => !used.Contains(o));
                w = nx;
            }
            if (loop.Count < 3)
                return "joining them leaves a loop of fewer than 3 walls";
            var first = loop[0];
            if (endOf[loop[^1]] != (first.X, first.Y))
                return "the walls left don't close into loops";
            loops.Add(loop);
        }
        var outers = loops.Where(l => Area(l) > 0).ToList();
        if (outers.Count != 1)
            return outers.Count == 0 ? "no outer loop is left" : "the sectors would make more than one room";
        var outer = outers[0];
        // the old first wall first when it is left (the planes stay)
        int fi = outer.IndexOf(firstWall);
        if (fi > 0)
            outer = outer.Skip(fi).Concat(outer.Take(fi)).ToList();
        sa.Loops = new List<List<BWall>> { outer };
        sa.Loops.AddRange(loops.Where(l => !ReferenceEquals(l, outers[0])));
        foreach (var w in gone)
            g.Unlink(w);
        g.Sectors.Remove(sb);
        var bSprites = Sprites.Where(sp => sp.SectNum == b && !(sp.StatNum == 10 && sp.Owner == b)).ToList();
        var bMarkers = Enumerable.Range(0, Sprites.Count).Where(i => Sprites[i].StatNum == 10 && Sprites[i].Owner == b).ToList();
        var markerObjs = bMarkers.Select(i => Sprites[i]).ToList();
        int na = a > b ? a - 1 : a;
        Commit(g);
        foreach (var sp in bSprites)
            sp.SectNum = na;
        DeleteSprites(markerObjs.Select(o => Sprites.IndexOf(o)));
        if (!RepairFirstWall(na, oldFirst, oldCeil, oldFloor))
            Warning = $"sector {na} has no wall along its old first wall: its slope turned";
        Rebuild();
        return null;
    }
}
