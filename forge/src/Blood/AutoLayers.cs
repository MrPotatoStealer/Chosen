// Chosen Blood module (GPL v2): a Blood map's rooms over rooms sorted into the editor's layers when a .MAP opens.
using System;
using System.Collections.Generic;
using System.Linq;
using Chosen.Forge.Document;
using Chosen.Forge.View;
using Chosen.Shared.Blood.Map;
using Chosen.Shared.Engine.Geometry;

namespace Chosen.Forge.Blood;

/// <summary>
/// The layers Forge makes for a plain .MAP (it has none of its own): the sectors Blood links (an upper link marker,
/// types 7 9 11 13, in one; a lower one, 6 10 12 14, with the same data1 in the other: a room over a room seen through
/// a plane, NBlood's warp.cpp) form a layer each group ("Link 1: 65, 90"); of the rest, the sectors lying over others
/// go into "Above 1", "Above 2"... by floor height, the lowest staying in "Level". No layer then holds two sectors
/// sharing a place (a link's ends lying over each other: "Link 1" and "Link 1 above").
/// </summary>
public static class AutoLayers
{
    internal static readonly int[] Upper = { 7, 9, 11, 13 }, Lower = { 6, 10, 12, 14 };

    public static void Make(MapDocument d)
    {
        int n = d.Sectors.Count;
        d.Extras.Layers.Clear();
        d.Extras.SectorLayers.Clear();
        d.Extras.SpriteLayers.Clear();
        d.Extras.CurrentLayer = 0;
        if (n == 0)
            return;
        // the links: union of the sectors at either end
        var parent = Enumerable.Range(0, n).ToArray();
        int Find(int a) => parent[a] == a ? a : parent[a] = Find(parent[a]);
        var linked = new HashSet<int>();
        foreach (var (upper, lower) in Links(d))
        {
            linked.Add(upper);
            linked.Add(lower);
            parent[Find(upper)] = Find(lower);
        }
        var layerOf = new int[n];
        var fills = new Dictionary<int, List<(double x, double y)>>();
        var layers = new List<(string name, List<int> members)> { ("Level", new List<int>()) };
        // a group of linked sectors: its own layers, the lowest floors first
        int link = 0;
        foreach (var group in linked.GroupBy(Find).OrderBy(gr => gr.Min()))
        {
            link++;
            string name = $"Link {link}: {string.Join(", ", group.OrderBy(s => s).Take(4))}{(group.Count() > 4 ? "..." : "")}";
            var mine = new List<List<int>>();
            foreach (int s in group.OrderByDescending(s => d.Sectors[s].FloorZ))
            {
                int k = mine.FindIndex(l => !l.Any(o => Overlap(d, s, o, fills)));
                if (k < 0)
                {
                    mine.Add(new List<int>());
                    k = mine.Count - 1;
                    layers.Add((k == 0 ? name : $"{name} above{(k > 1 ? " " + k : "")}", mine[k]));
                }
                mine[k].Add(s);
                layerOf[s] = layers.FindIndex(l => ReferenceEquals(l.members, mine[k]));
            }
        }
        // the rest: the lowest floors first, each in the first of Level, Above 1... holding nothing it lies over
        var level = new List<List<int>> { layers[0].members };
        foreach (int s in Enumerable.Range(0, n).Where(s => !linked.Contains(s)).OrderByDescending(s => d.Sectors[s].FloorZ))
        {
            int k = level.FindIndex(l => !l.Any(o => Overlap(d, s, o, fills)));
            if (k < 0)
            {
                level.Add(new List<int>());
                k = level.Count - 1;
                layers.Add(($"Above {k}", level[k]));
            }
            level[k].Add(s);
            layerOf[s] = layers.FindIndex(l => ReferenceEquals(l.members, level[k]));
        }
        if (layers.Count <= 1)
            return;
        foreach (var (name, _) in layers)
            d.Extras.Layers.Add(new MapLayer { Name = name });
        d.Extras.SectorLayers.AddRange(layerOf);
        d.Extras.LayersAuto = true;
    }

    /// <summary>Blood's links (NBlood's warp.cpp): an upper link marker (types 7 9 11 13) in the upper sector, a lower
    /// one (6 10 12 14) with the same data1 in the lower: (upper sector, lower sector) pairs.</summary>
    public static List<(int upper, int lower)> Links(MapDocument d)
    {
        int n = d.Sectors.Count;
        var up = d.Sprites.Where(sp => Array.IndexOf(Upper, sp.Type) >= 0 && sp.Xsp != null && sp.SectNum >= 0 && sp.SectNum < n).ToList();
        var low = d.Sprites.Where(sp => Array.IndexOf(Lower, sp.Type) >= 0 && sp.Xsp != null && sp.SectNum >= 0 && sp.SectNum < n).ToList();
        var r = new List<(int, int)>();
        foreach (var u in up)
            foreach (var l in low.Where(l => l.Xsp.Data1 == u.Xsp.Data1))
                r.Add((u.SectNum, l.SectNum));
        return r;
    }

    /// <summary>Layers of a .MAP: "Level 150, Above 1 5, Link 1: 65, 90 2".</summary>
    public static string Summary(MapDocument d) =>
        string.Join(", ", d.Layers.Select((l, i) => $"{l.Name} {Enumerable.Range(0, d.Sectors.Count).Count(s => d.LayerOf(s) == i)}"));

    /// <summary>Do sectors a and b share a place (a point well inside one lies inside the other, not on its walls)?
    /// Touching along a wall or at a point isn't sharing.</summary>
    public static bool Overlap(MapDocument d, int a, int b) => Overlap(d, a, b, null);

    /// <summary>For each sector the sectors sharing a place with it (Overlap), and its neighbours through two-sided
    /// walls: the 3D preview's rooms for its lights (Shared.Blood.Map.LightRules.RoomColours).</summary>
    public static (List<int>[] overlaps, List<int>[] neighbours) Rooms(MapDocument d)
    {
        int n = d.Sectors.Count;
        var overlaps = new List<int>[n];
        var neighbours = new List<int>[n];
        var bounds = new (double x0, double y0, double x1, double y1)[n];
        for (int s = 0; s < n; s++)
        {
            overlaps[s] = new List<int>();
            neighbours[s] = new List<int>();
            bounds[s] = d.Sectors[s].WallNum >= 3 ? BuildGeom.SectorBounds(d, s) : (0, 0, -1, -1);
            var sec = d.Sectors[s];
            for (int w = sec.WallPtr; w < sec.WallPtr + sec.WallNum && w < d.Walls.Count; w++)
                if (d.Walls[w].NextSector is int ns and >= 0 && ns < n && ns != s && !neighbours[s].Contains(ns))
                    neighbours[s].Add(ns);
        }
        var fills = new Dictionary<int, List<(double x, double y)>>();
        for (int a = 0; a < n; a++)
        {
            var ba = bounds[a];
            if (ba.x1 < ba.x0)
                continue;
            for (int b = a + 1; b < n; b++)
            {
                var bb = bounds[b];
                if (bb.x1 < bb.x0 || ba.x1 <= bb.x0 || bb.x1 <= ba.x0 || ba.y1 <= bb.y0 || bb.y1 <= ba.y0)
                    continue;
                if (Overlap(d, a, b, fills) || OutlinesOverlap(d, a, b))
                {
                    overlaps[a].Add(b);
                    overlaps[b].Add(a);
                }
            }
        }
        return (overlaps, neighbours);
    }

    /// <summary>
    /// Do two sectors' areas overlap anywhere, however little (the game's SectorMap.OutlinesOverlap)? A point just
    /// inside a wall of one (its middle, stepped a little each way) inside both, or walls crossing. Overlap samples
    /// the middles of the fill's triangles, and missed a corner of one room laid over another (test.mapx: corridor
    /// 14's west end over room 0, lit by room 0's lights: Forge report 20261108-150121).
    /// </summary>
    static bool OutlinesOverlap(MapDocument d, int a, int b)
    {
        foreach (var (p, q) in new[] { (a, b), (b, a) })
        {
            var sec = d.Sectors[p];
            for (int w = sec.WallPtr; w < sec.WallPtr + sec.WallNum && w < d.Walls.Count; w++)
            {
                var (x1, y1, x2, y2) = BuildGeom.WallEnds(d, w);
                double dx = x2 - x1, dy = y2 - y1, len = Math.Sqrt(dx * dx + dy * dy);
                if (len < 1e-6)
                    continue;
                double mx = (x1 + x2) * 0.5, my = (y1 + y2) * 0.5, nx = -dy / len * 2, ny = dx / len * 2;
                if (BuildGeom.Inside(d, p, mx + nx, my + ny) && BuildGeom.Inside(d, q, mx + nx, my + ny)
                    || BuildGeom.Inside(d, p, mx - nx, my - ny) && BuildGeom.Inside(d, q, mx - nx, my - ny))
                    return true;
            }
        }
        var sa = d.Sectors[a];
        var sb = d.Sectors[b];
        for (int i = sa.WallPtr; i < sa.WallPtr + sa.WallNum && i < d.Walls.Count; i++)
        {
            var (ax1, ay1, ax2, ay2) = BuildGeom.WallEnds(d, i);
            for (int j = sb.WallPtr; j < sb.WallPtr + sb.WallNum && j < d.Walls.Count; j++)
            {
                var (bx1, by1, bx2, by2) = BuildGeom.WallEnds(d, j);
                if (ProperCross(ax1, ay1, ax2, ay2, bx1, by1, bx2, by2))
                    return true;
            }
        }
        return false;
    }

    static bool ProperCross(double ax, double ay, double bx, double by, double cx, double cy, double dx, double dy)
    {
        double d1 = Cross(cx, cy, dx, dy, ax, ay), d2 = Cross(cx, cy, dx, dy, bx, by);
        double d3 = Cross(ax, ay, bx, by, cx, cy), d4 = Cross(ax, ay, bx, by, dx, dy);
        return (d1 > 0 && d2 < 0 || d1 < 0 && d2 > 0) && (d3 > 0 && d4 < 0 || d3 < 0 && d4 > 0);
    }

    static double Cross(double ox, double oy, double ax, double ay, double bx, double by) => (ax - ox) * (by - oy) - (ay - oy) * (bx - ox);

    static bool Overlap(MapDocument d, int a, int b, Dictionary<int, List<(double x, double y)>> fills)
    {
        if (a == b)
            return false;
        var (ax0, ay0, ax1, ay1) = BuildGeom.SectorBounds(d, a);
        var (bx0, by0, bx1, by1) = BuildGeom.SectorBounds(d, b);
        if (ax1 <= bx0 || bx1 <= ax0 || ay1 <= by0 || by1 <= ay0)
            return false;
        return Within(d, a, b, fills) || Within(d, b, a, fills);
    }

    /// <summary>A place well inside a (the middles of its fill's triangles) lies inside b?</summary>
    static bool Within(MapDocument d, int a, int b, Dictionary<int, List<(double x, double y)>> fills)
    {
        List<(double x, double y)> tris;
        if (fills == null)
            tris = PreviewMesher.Fill(d, a);
        else if (!fills.TryGetValue(a, out tris))
            fills[a] = tris = PreviewMesher.Fill(d, a);
        int step = Math.Max(1, tris.Count / 3 / 48);
        for (int t = 0; t + 2 < tris.Count; t += 3 * step)
        {
            double x = (tris[t].x + tris[t + 1].x + tris[t + 2].x) / 3, y = (tris[t].y + tris[t + 1].y + tris[t + 2].y) / 3;
            if (!BuildGeom.Inside(d, b, x, y))
                continue;
            // (not on one of b's walls: rooms against each other share their walls, not their places)
            bool onWall = false;
            var sec = d.Sectors[b];
            for (int w = sec.WallPtr; w < sec.WallPtr + sec.WallNum && !onWall; w++)
            {
                var (x1, y1, x2, y2) = BuildGeom.WallEnds(d, w);
                onWall = MapDocument.SegmentDistance(x, y, x1, y1, x2, y2) < 2;
            }
            if (!onWall)
                return true;
        }
        return false;
    }
}
