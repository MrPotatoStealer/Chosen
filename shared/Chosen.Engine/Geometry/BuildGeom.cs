// Chosen core: original code.
using System;
using System.Collections.Generic;

namespace Chosen.Shared.Engine.Geometry;

/// <summary>
/// Geometry on a level of sectors and wall loops (<see cref="IBuildData"/>), written from the maths and the data's
/// meaning: point in sector by the even-odd rule over all its loops, a sloped plane's height from its first wall's line,
/// loops and their winding. For tools and the editor; the game keeps its own exact port of Blood's engine, and its
/// geomcheck command lists where the two disagree.
/// </summary>
public static class BuildGeom
{
    /// <summary>A wall's two ends: its own point and the next wall's.</summary>
    public static (int x1, int y1, int x2, int y2) WallEnds(IBuildData d, int w)
    {
        var a = d.Wall(w);
        var b = d.Wall(a.Point2);
        return (a.X, a.Y, b.X, b.Y);
    }

    /// <summary>Is (x, y) inside sector s: an odd number of its walls (all loops) cross the ray from it towards +x.
    /// A point exactly on a horizontal boundary belongs to the side below it (y half-open), so sectors sharing a wall
    /// never both claim a point on it.</summary>
    public static bool Inside(IBuildData d, int s, double x, double y)
    {
        if (s < 0 || s >= d.SectorCount)
            return false;
        var sec = d.Sector(s);
        bool inside = false;
        for (int w = sec.WallPtr; w < sec.WallPtr + sec.WallNum; w++)
        {
            var (x1, y1, x2, y2) = WallEnds(d, w);
            if ((y1 > y) == (y2 > y))
                continue;
            double xc = x1 + (y - y1) * (double)(x2 - x1) / (y2 - y1);
            if (x < xc)
                inside = !inside;
        }
        return inside;
    }

    /// <summary>The height of a plane at (x, y): z, or for a sloped plane z plus heinum / 4096 of the point's distance
    /// (in z units, 16 per xy unit) from the line through the sector's first wall, positive to the wall's right.</summary>
    public static double PlaneZ(IBuildData d, int s, bool ceiling, double x, double y)
    {
        var sec = d.Sector(s);
        int z = ceiling ? sec.CeilingZ : sec.FloorZ;
        int stat = ceiling ? sec.CeilingStat : sec.FloorStat;
        int heinum = ceiling ? sec.CeilingHeinum : sec.FloorHeinum;
        if ((stat & 2) == 0 || heinum == 0 || sec.WallNum < 3)
            return z;
        var (x1, y1, x2, y2) = WallEnds(d, sec.WallPtr);
        // the first wall's length as Blood measures it: whole units, rounded down (a 1.2-unit first wall slopes its
        // plane as if it were 1 long: CP05's sector 380)
        double dx = x2 - x1, dy = y2 - y1, len = Math.Floor(Math.Sqrt(dx * dx + dy * dy));
        if (len == 0)
            return z;
        // signed distance (xy units) from the first wall's line, then 16 z units per xy unit, heinum / 4096 the slope
        double dist = (dx * (y - y1) - dy * (x - x1)) / len;
        return z + heinum * dist * 16 / 4096;
    }

    public static void GetZsOfSlope(IBuildData d, int s, double x, double y, out double ceil, out double floor)
    {
        ceil = PlaneZ(d, s, true, x, y);
        floor = PlaneZ(d, s, false, x, y);
    }

    /// <summary>The sector holding (x, y): from first if it does, else one of its neighbours, else any; -1 if none.</summary>
    public static int UpdateSector(IBuildData d, double x, double y, int from)
    {
        if (from >= 0 && from < d.SectorCount)
        {
            if (Inside(d, from, x, y))
                return from;
            var sec = d.Sector(from);
            for (int w = sec.WallPtr; w < sec.WallPtr + sec.WallNum; w++)
            {
                int n = d.Wall(w).NextSector;
                if (n >= 0 && n < d.SectorCount && Inside(d, n, x, y))
                    return n;
            }
        }
        for (int s = 0; s < d.SectorCount; s++)
            if (Inside(d, s, x, y))
                return s;
        return -1;
    }

    /// <summary>A sector's loops, each its walls in order (following Point2 from the first wall not yet in a loop).</summary>
    public static List<List<int>> Loops(IBuildData d, int s)
    {
        var sec = d.Sector(s);
        var loops = new List<List<int>>();
        var seen = new HashSet<int>();
        int end = sec.WallPtr + sec.WallNum;
        for (int start = sec.WallPtr; start < end; start++)
        {
            if (seen.Contains(start))
                continue;
            var loop = new List<int>();
            int w = start;
            while (w >= sec.WallPtr && w < end && seen.Add(w))
            {
                loop.Add(w);
                w = d.Wall(w).Point2;
            }
            loops.Add(loop);
        }
        return loops;
    }

    /// <summary>A loop's signed area (shoelace over its points, in the map's own coordinates): positive when the loop
    /// runs clockwise as the map is drawn (x right, y down), as a sector's outer loop does.</summary>
    public static double LoopArea(IBuildData d, IReadOnlyList<int> loop)
    {
        double a = 0;
        foreach (int w in loop)
        {
            var (x1, y1, x2, y2) = WallEnds(d, w);
            a += (double)x1 * y2 - (double)x2 * y1;
        }
        return a / 2;
    }

    /// <summary>A sector's outer loop: the one running clockwise with the largest area (it needn't be stored first: a
    /// sector's first wall may lie on a hole, as Blood's maps have it).</summary>
    public static List<int> OuterLoop(IBuildData d, int s)
    {
        List<int> best = null;
        double ba = double.MinValue;
        foreach (var l in Loops(d, s))
        {
            double a = LoopArea(d, l);
            if (a > ba)
            {
                ba = a;
                best = l;
            }
        }
        return best ?? new List<int>();
    }

    /// <summary>Is the loop an outer one (clockwise as drawn)? Inner loops (holes, pillars) run the other way.</summary>
    public static bool LoopIsOuter(IBuildData d, IReadOnlyList<int> loop) => LoopArea(d, loop) > 0;

    /// <summary>A sector's bounding box over all its walls' points.</summary>
    public static (int minX, int minY, int maxX, int maxY) SectorBounds(IBuildData d, int s)
    {
        var sec = d.Sector(s);
        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
        for (int w = sec.WallPtr; w < sec.WallPtr + sec.WallNum; w++)
        {
            var p = d.Wall(w);
            minX = Math.Min(minX, p.X); maxX = Math.Max(maxX, p.X);
            minY = Math.Min(minY, p.Y); maxY = Math.Max(maxY, p.Y);
        }
        return (minX, minY, maxX, maxY);
    }

    /// <summary>(x, y) turned about (cx, cy) by ang (2048 to a full turn, clockwise as drawn with y down).</summary>
    public static (double x, double y) RotatePoint(double x, double y, double cx, double cy, double ang)
    {
        double a = ang * Math.PI / 1024, c = Math.Cos(a), s = Math.Sin(a);
        double dx = x - cx, dy = y - cy;
        return (cx + dx * c - dy * s, cy + dx * s + dy * c);
    }
}
