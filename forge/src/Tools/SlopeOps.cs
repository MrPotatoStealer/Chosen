// Chosen core: original code (a floor's or ceiling's edge raised or lowered: the plane tilted about its far corner).
using System;
using Chosen.Forge.Document;
using Chosen.Shared.Engine.Geometry;

namespace Chosen.Forge.Tools;

/// <summary>
/// The 3D preview's slope edges (UDB's slope handles, as Build's planes allow): an edge of a sector's floor or ceiling
/// raised or lowered while the sector's corner farthest from it keeps its height, the plane tilting between them. Build
/// slopes a plane only across its sector's first wall (z at that wall's line, heinum the rise across it), so the edge
/// is made the first wall (as Shift+F does: a floor or ceiling relative to the first wall then lies along it) and the
/// plane set through the edge at its new height and through the far corner. Tilt is Build's own slope: the plane
/// turned about an edge made the first wall (the hinge), its far corner moved.
/// </summary>
public static class SlopeOps
{
    /// <summary>The wall of sector s nearest (x, y) (any of its loops), -1 for none.</summary>
    public static int NearestEdge(MapDocument d, int s, double x, double y)
    {
        if (s < 0 || s >= d.Sectors.Count)
            return -1;
        var sec = d.Sectors[s];
        int best = -1;
        double bd = double.MaxValue;
        for (int w = sec.WallPtr; w < sec.WallPtr + sec.WallNum; w++)
        {
            var (x1, y1, x2, y2) = BuildGeom.WallEnds(d, w);
            double dist = MapDocument.SegmentDistance(x, y, x1, y1, x2, y2);
            if (dist < bd)
            {
                bd = dist;
                best = w;
            }
        }
        return best;
    }

    /// <summary>The wall of sector s from (x1, y1) to (x2, y2), -1 for none (an edge named by its ends: raising one
    /// renumbers its sector's walls).</summary>
    public static int FindEdge(MapDocument d, int s, int x1, int y1, int x2, int y2)
    {
        if (s < 0 || s >= d.Sectors.Count)
            return -1;
        var sec = d.Sectors[s];
        for (int w = sec.WallPtr; w < sec.WallPtr + sec.WallNum; w++)
        {
            var (ax, ay, bx, by) = BuildGeom.WallEnds(d, w);
            if (ax == x1 && ay == y1 && bx == x2 && by == y2)
                return w;
        }
        return -1;
    }

    /// <summary>The corner of sector s farthest from wall's line (its perpendicular distance), for the pivot.</summary>
    public static (double x, double y, double dist) FarCorner(MapDocument d, int s, int wall)
    {
        var (ax, ay, bx, by) = BuildGeom.WallEnds(d, wall);
        double ex = bx - ax, ey = by - ay, len = Math.Sqrt(ex * ex + ey * ey);
        var sec = d.Sectors[s];
        (double x, double y, double dist) far = (ax, ay, 0);
        if (len < 1)
            return far;
        for (int w = sec.WallPtr; w < sec.WallPtr + sec.WallNum; w++)
        {
            var p = d.Walls[w];
            double dist = Math.Abs((p.X - ax) * ey - (p.Y - ay) * ex) / len;
            if (dist > far.dist)
                far = (p.X, p.Y, dist);
        }
        return far;
    }

    /// <summary>
    /// The slope handle across from a sector's first wall: a line parallel to it through the corner farthest from it,
    /// as long as the sector reaches along it (its corners' extent along the wall's direction). Raising it tilts the
    /// plane about the first wall (Tilt): Build's slope, the rise across the first wall. Null for a sector with no
    /// corner off its first wall's line.
    /// </summary>
    public static (double x1, double y1, double x2, double y2)? FarLine(MapDocument d, int s)
    {
        if (s < 0 || s >= d.Sectors.Count || d.Sectors[s].WallNum < 3)
            return null;
        var sec = d.Sectors[s];
        var (ax, ay, bx, by) = BuildGeom.WallEnds(d, sec.WallPtr);
        double ux = bx - ax, uy = by - ay, len = Math.Sqrt(ux * ux + uy * uy);
        if (len < 1)
            return null;
        ux /= len;
        uy /= len;
        var far = FarCorner(d, s, sec.WallPtr);
        if (far.dist < 1)
            return null;
        double lo = double.MaxValue, hi = double.MinValue;
        for (int w = sec.WallPtr; w < sec.WallPtr + sec.WallNum; w++)
        {
            double t = (d.Walls[w].X - ax) * ux + (d.Walls[w].Y - ay) * uy;
            lo = Math.Min(lo, t);
            hi = Math.Max(hi, t);
        }
        // (through the far corner, along the wall)
        double ft = (far.x - ax) * ux + (far.y - ay) * uy;
        double px = far.x - ft * ux, py = far.y - ft * uy;
        return (px + lo * ux, py + lo * uy, px + hi * ux, py + hi * uy);
    }

    /// <summary>Sector s's floor (or ceiling) edge along wall raised by dz (Build z: up negative), its far corner
    /// keeping its height: the wall made first, the plane's z its edge's new height (the middle of the edge when it
    /// wasn't level), its slope through the far corner (flat again: the slope bit off). Null when done, else why not.</summary>
    public static string RaiseEdge(MapDocument d, int s, bool ceiling, int wall, int dz)
    {
        if (s < 0 || s >= d.Sectors.Count)
            return "no sector";
        var sec = d.Sectors[s];
        if (wall < sec.WallPtr || wall >= sec.WallPtr + sec.WallNum)
            return "the edge isn't the sector's";
        var (ax, ay, bx, by) = BuildGeom.WallEnds(d, wall);
        var far = FarCorner(d, s, wall);
        if (far.dist < 1)
            return "the sector has no corner off the edge's line";
        double edge = (BuildGeom.PlaneZ(d, s, ceiling, ax, ay) + BuildGeom.PlaneZ(d, s, ceiling, bx, by)) / 2;
        double pivot = BuildGeom.PlaneZ(d, s, ceiling, far.x, far.y);
        return Through(d, s, ceiling, wall, edge + dz, pivot, far);
    }

    /// <summary>Sector s's floor (or ceiling) tilted about its edge along wall (Build's own slope: the plane turning
    /// about its first wall): the wall made first (the plane kept through the edge's middle and the far corner), the
    /// far corner moved by dz (up negative), the edge kept. Null when done, else why not.</summary>
    public static string Tilt(MapDocument d, int s, bool ceiling, int wall, int dz)
    {
        if (s < 0 || s >= d.Sectors.Count)
            return "no sector";
        var sec = d.Sectors[s];
        if (wall < sec.WallPtr || wall >= sec.WallPtr + sec.WallNum)
            return "the edge isn't the sector's";
        var (ax, ay, bx, by) = BuildGeom.WallEnds(d, wall);
        var far = FarCorner(d, s, wall);
        if (far.dist < 1)
            return "the sector has no corner off the edge's line";
        double edge = (BuildGeom.PlaneZ(d, s, ceiling, ax, ay) + BuildGeom.PlaneZ(d, s, ceiling, bx, by)) / 2;
        double pivot = BuildGeom.PlaneZ(d, s, ceiling, far.x, far.y);
        return Through(d, s, ceiling, wall, edge, pivot + dz, far);
    }

    /// <summary>The wall made sector s's first (the hinge), the plane through it level at edgeZ and through the far
    /// corner at farZ.</summary>
    static string Through(MapDocument d, int s, bool ceiling, int wall, double edgeZ, double farZ, (double x, double y, double dist) far)
    {
        double pivot = farZ;
        int z = (int)Math.Round(edgeZ);
        if (!d.SetFirstWall(s, wall))
            return "the edge can't be made the sector's first wall";
        var sec = d.Sectors[s];
        // the rise across the first wall (heinum) is linear in the height it gives a point: measured at one value
        void Set(int zz, int heinum, bool sloped)
        {
            if (ceiling)
            {
                sec.CeilingZ = zz;
                sec.CeilingHeinum = heinum;
                sec.CeilingStat = sloped ? sec.CeilingStat | 2 : sec.CeilingStat & ~2;
            }
            else
            {
                sec.FloorZ = zz;
                sec.FloorHeinum = heinum;
                sec.FloorStat = sloped ? sec.FloorStat | 2 : sec.FloorStat & ~2;
            }
        }
        Set(z, 4096, true);
        double per = (BuildGeom.PlaneZ(d, s, ceiling, far.x, far.y) - z) / 4096.0;
        int h = Math.Abs(per) < 1e-9 ? 0 : (int)Math.Clamp(Math.Round((pivot - z) / per), -32767, 32767);
        Set(z, h, h != 0);
        return null;
    }
}
