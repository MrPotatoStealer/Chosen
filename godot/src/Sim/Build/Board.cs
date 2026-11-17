// Chosen game (GPL v2), with code ported from Build's engine as NBlood has it (see LICENSE.md):
// "Build Engine & Tools" Copyright (c) 1993-1997 Ken Silverman
// Ken Silverman's official web site: "http://www.advsys.net/ken"
// See the included license file "BUILDLIC.TXT" for license info.
// This file has been modified from Ken Silverman's original release
using System;
using System.Collections.Generic;
using Chosen.Shared.Blood.Map;

namespace Chosen.Sim.Build;

/// <summary>
/// A level's Build data as the game plays it (Build units: x, y as stored, z down, 256 per Quake unit vertically and
/// 16 horizontally): the sectors and walls of its .MAP file, changed in play as Blood changes them (TranslateSector
/// drags wall points, ZTranslateSector moves planes, effects shade and pan), with what they were at the last game
/// frame (the renderer draws between the two) and which sectors changed shape. The engine's geometry on it is Build's
/// own in Blood's mode (enginecompatibilitymode ENGINE_19960925, NBlood engine.cpp): inside, getzsofslope,
/// updatesector[z], lastwall, dragpoint.
/// </summary>
public sealed class Board
{
    public readonly BloodMapFile File;
    public readonly BSector[] Sectors;
    public readonly BWall[] Walls;
    public int SectorCount => Sectors.Length;
    public int WallCount => Walls.Length;

    /// <summary>trInit's base positions: where TranslateSector moves a wall's point from (baseWall), and the planes
    /// (baseFloor, baseCeil).</summary>
    public readonly int[] BaseWallX, BaseWallY, BaseFloor, BaseCeil;
    /// <summary>The wall points and planes at the last game frame (the renderer draws between those and these).</summary>
    public readonly int[] PrevWallX, PrevWallY, PrevFloorZ, PrevCeilZ;
    /// <summary>Each sector's wall loops (by point2, each from its first wall not yet walked; a hole can come first).</summary>
    public readonly int[][][] Loops;
    /// <summary>Each sector's neighbours through its two-sided walls.</summary>
    public readonly int[][] Neighbours;

    /// <summary>Counts up whenever a sector's wall points move (its shape) or its planes do; per sector.</summary>
    public readonly int[] ShapeVersion, PlaneVersion;
    /// <summary>Counts up whenever what is drawn of a sector changes without it moving (a modern map's changers: a
    /// picture, palette, shade, flags, repeats or panning of its planes or walls); per sector.</summary>
    public readonly int[] LookVersion;
    /// <summary>Sectors whose look changed since the renderer last took them (it clears the list).</summary>
    public readonly List<int> Relooked = new();
    /// <summary>Sectors whose shape changed this game frame (cleared by <see cref="BeginFrame"/>).</summary>
    public readonly List<int> Reshaped = new();
    readonly bool[] _reshaped;

    public Board(BloodMapFile file)
    {
        File = file;
        Sectors = file.Sectors.ToArray();
        Walls = file.Walls.ToArray();
        int nw = Walls.Length, ns = Sectors.Length;
        BaseWallX = new int[nw];
        BaseWallY = new int[nw];
        PrevWallX = new int[nw];
        PrevWallY = new int[nw];
        for (int i = 0; i < nw; i++)
        {
            BaseWallX[i] = PrevWallX[i] = Walls[i].X;
            BaseWallY[i] = PrevWallY[i] = Walls[i].Y;
        }
        BaseFloor = new int[ns];
        BaseCeil = new int[ns];
        PrevFloorZ = new int[ns];
        PrevCeilZ = new int[ns];
        ShapeVersion = new int[ns];
        PlaneVersion = new int[ns];
        LookVersion = new int[ns];
        _reshaped = new bool[ns];
        Loops = new int[ns][][];
        Neighbours = new int[ns][];
        for (int s = 0; s < ns; s++)
        {
            BaseFloor[s] = PrevFloorZ[s] = Sectors[s].FloorZ;
            BaseCeil[s] = PrevCeilZ[s] = Sectors[s].CeilingZ;
            Loops[s] = FindLoops(s);
            var nb = new List<int>();
            foreach (int w in SectorWalls(s))
                if (Walls[w].NextSector >= 0 && Walls[w].NextSector < ns && !nb.Contains(Walls[w].NextSector))
                    nb.Add(Walls[w].NextSector);
            Neighbours[s] = nb.ToArray();
        }
    }

    public bool Valid(int s) => s >= 0 && s < Sectors.Length;

    /// <summary>A sector's walls in storage order.</summary>
    public IEnumerable<int> SectorWalls(int s)
    {
        var sec = Sectors[s];
        for (int w = sec.WallPtr; w < sec.WallPtr + sec.WallNum; w++)
            yield return w;
    }

    int[][] FindLoops(int s)
    {
        var sec = Sectors[s];
        var loops = new List<int[]>();
        var seen = new HashSet<int>();
        for (int first = sec.WallPtr; first < sec.WallPtr + sec.WallNum; first++)
        {
            if (seen.Contains(first))
                continue;
            var loop = new List<int>();
            int w = first;
            for (int k = 0; k <= sec.WallNum; k++)
            {
                loop.Add(w);
                seen.Add(w);
                w = Walls[w].Point2;
                if (w == first || seen.Contains(w) || w < 0 || w >= Walls.Length)
                    break;
            }
            loops.Add(loop.ToArray());
        }
        return loops.ToArray();
    }

    // ---------------------------------------------------------------- frames
    /// <summary>The start of a game frame: what is now becomes what was (the renderer's previous positions).</summary>
    public void BeginFrame()
    {
        foreach (int s in Reshaped)
            _reshaped[s] = false;
        Reshaped.Clear();
        for (int i = 0; i < Walls.Length; i++)
        {
            PrevWallX[i] = Walls[i].X;
            PrevWallY[i] = Walls[i].Y;
        }
        for (int s = 0; s < Sectors.Length; s++)
        {
            PrevFloorZ[s] = Sectors[s].FloorZ;
            PrevCeilZ[s] = Sectors[s].CeilingZ;
        }
    }

    /// <summary>A wall's start moved: its sector (and the one beyond, whose wall shares the point) changed shape.</summary>
    void Touched(int w)
    {
        int s = Walls[w].Sector;
        if (s >= 0 && !_reshaped[s])
        {
            _reshaped[s] = true;
            Reshaped.Add(s);
        }
        if (s >= 0)
            ShapeVersion[s]++;
    }

    /// <summary>What is drawn of a sector changed (its pictures, palettes, shades, flags).</summary>
    public void TouchLook(int s)
    {
        LookVersion[s]++;
        if (!Relooked.Contains(s))
            Relooked.Add(s);
    }

    /// <summary>A sector's planes were tilted (a slope changer): its shape counts as changed.</summary>
    public void TouchShape(int s)
    {
        if (!_reshaped[s])
        {
            _reshaped[s] = true;
            Reshaped.Add(s);
        }
        ShapeVersion[s]++;
    }

    /// <summary>A sector's floor or ceiling height set (ZTranslateSector, bob).</summary>
    public void SetFloorZ(int s, int z)
    {
        if (Sectors[s].FloorZ == z)
            return;
        Sectors[s].FloorZ = z;
        PlaneVersion[s]++;
    }

    public void SetCeilZ(int s, int z)
    {
        if (Sectors[s].CeilingZ == z)
            return;
        Sectors[s].CeilingZ = z;
        PlaneVersion[s]++;
    }

    // ---------------------------------------------------------------- engine
    /// <summary>lastwall: the wall whose point2 is this one.</summary>
    public int LastWall(int point)
    {
        if (point > 0 && Walls[point - 1].Point2 == point)
            return point - 1;
        int i = point;
        for (int cnt = Walls.Length; cnt > 0; cnt--)
        {
            int j = Walls[i].Point2;
            if (j == point)
                return i;
            i = j;
        }
        return point;
    }

    /// <summary>Blood's DragPoint (triggers.cpp): a wall's start and every wall joined to it through two-sided walls
    /// (the same point of the sectors around it) set to (x, y).</summary>
    public void DragPoint(int nWall, int x, int y)
    {
        SetPoint(nWall, x, y);
        int vsi = Walls.Length;
        int vb = nWall;
        do
        {
            if (Walls[vb].NextWall >= 0)
            {
                vb = Walls[Walls[vb].NextWall].Point2;
                SetPoint(vb, x, y);
            }
            else
            {
                vb = nWall;
                do
                {
                    int lw = LastWall(vb);
                    if (Walls[lw].NextWall >= 0)
                    {
                        vb = Walls[lw].NextWall;
                        SetPoint(vb, x, y);
                    }
                    else
                        break;
                    vsi--;
                } while (vb != nWall && vsi > 0);
                break;
            }
            vsi--;
        } while (vb != nWall && vsi > 0);
    }

    void SetPoint(int w, int x, int y)
    {
        if (Walls[w].X == x && Walls[w].Y == y)
            return;
        Walls[w].X = x;
        Walls[w].Y = y;
        Touched(w);
        // the wall ending at this point changed too (its sector is the same)
    }

    /// <summary>inside (inside_compat): is (x, y) in sector s, by the sign bits Build counts the walls crossing its row
    /// with (even-odd over all loops; a point on a wall's start row counts as above it).</summary>
    public bool Inside(double x, double y, int s)
    {
        if (s < 0 || s >= Sectors.Length)
            return false;
        var sec = Sectors[s];
        bool odd = false;
        for (int w = sec.WallPtr, n = sec.WallNum; n > 0; w++, n--)
        {
            var a = Walls[w];
            var b = Walls[a.Point2];
            double y1 = a.Y - y, y2 = b.Y - y;
            if ((y1 < 0) == (y2 < 0))
                continue;
            double x1 = a.X - x, x2 = b.X - x;
            if ((x1 < 0) == (x2 < 0))
            {
                if (x1 < 0)
                    odd = !odd;
            }
            else
            {
                double c = x1 * y2 - x2 * y1;
                // (x1*y2 - x2*y1) ^ y2: its sign bit set when the signs differ
                if ((c < 0) != (y2 < 0))
                    odd = !odd;
            }
        }
        return odd;
    }

    /// <summary>getzsofslope: a sector's ceiling and floor heights at (x, y) (a sloped plane through its first wall,
    /// rising heinum/4096 per unit across it).</summary>
    public void GetZsOfSlope(int s, double x, double y, out double ceilz, out double florz)
    {
        var sec = Sectors[s];
        ceilz = sec.CeilingZ;
        florz = sec.FloorZ;
        if (((sec.CeilingStat | sec.FloorStat) & 2) == 0)
            return;
        var w = Walls[sec.WallPtr];
        var w2 = Walls[w.Point2];
        double dx = w2.X - w.X, dy = w2.Y - w.Y;
        double i = Math.Floor(Math.Sqrt(dx * dx + dy * dy)) * 32;
        if (i == 0)
            return;
        double j = Math.Floor((dx * (y - w.Y) - dy * (x - w.X)) / 8);
        if ((sec.CeilingStat & 2) != 0)
            ceilz += Blood.Int(sec.CeilingHeinum * j / i);
        if ((sec.FloorStat & 2) != 0)
            florz += Blood.Int(sec.FloorHeinum * j / i);
    }

    public double FloorZAt(int s, double x, double y)
    {
        GetZsOfSlope(s, x, y, out _, out double f);
        return f;
    }

    public double CeilZAt(int s, double x, double y)
    {
        GetZsOfSlope(s, x, y, out double c, out _);
        return c;
    }

    /// <summary>A plane as z = a x + b y + c over Build x, y (Build z), as getzsofslope has it (unrounded).</summary>
    public (double a, double b, double c) Plane(int s, bool ceiling)
    {
        var sec = Sectors[s];
        var w = Walls[sec.WallPtr];
        var w2 = Walls[w.Point2];
        return Plane(sec, ceiling, ceiling ? sec.CeilingZ : sec.FloorZ, w.X, w.Y, w2.X, w2.Y);
    }

    /// <summary>The plane as it was when the game frame began (BeginFrame: its height and first wall then; the slope's
    /// heinum as now).</summary>
    public (double a, double b, double c) PrevPlane(int s, bool ceiling)
    {
        var sec = Sectors[s];
        int w = sec.WallPtr, w2 = Walls[w].Point2;
        return Plane(sec, ceiling, ceiling ? PrevCeilZ[s] : PrevFloorZ[s], PrevWallX[w], PrevWallY[w], PrevWallX[w2], PrevWallY[w2]);
    }

    static (double a, double b, double c) Plane(BSector sec, bool ceiling, double z, double wx, double wy, double w2x, double w2y)
    {
        int stat = ceiling ? sec.CeilingStat : sec.FloorStat;
        int heinum = ceiling ? sec.CeilingHeinum : sec.FloorHeinum;
        if ((stat & 2) == 0 || heinum == 0)
            return (0, 0, z);
        double dx = w2x - wx, dy = w2y - wy;
        double ln = Math.Sqrt(dx * dx + dy * dy);
        if (ln == 0)
            return (0, 0, z);
        // z + heinum * (dx (y - wy) - dy (x - wx)) / (256 ln)
        double k = heinum / (256.0 * ln);
        return (-dy * k, dx * k, z + k * (dy * wx - dx * wy));
    }

    /// <summary>updatesector (updatesector_compat): sect while it holds (x, y), else the first neighbour that does,
    /// else the last sector that does; -1 if none.</summary>
    public int UpdateSector(double x, double y, int sect)
    {
        if (Inside(x, y, sect))
            return sect;
        if (Valid(sect))
            foreach (int w in SectorWalls(sect))
            {
                int next = Walls[w].NextSector;
                if (Inside(x, y, next))
                    return next;
            }
        for (int i = Sectors.Length - 1; i >= 0; i--)
            if (Inside(x, y, i))
                return i;
        return -1;
    }

    /// <summary>updatesectorz (updatesectorz_compat): as updatesector, a sector counting only while z lies between its
    /// ceiling and floor there.</summary>
    public int UpdateSectorZ(double x, double y, double z, int sect)
    {
        if (Valid(sect))
        {
            GetZsOfSlope(sect, x, y, out double cz, out double fz);
            if (z >= cz && z <= fz && Inside(x, y, sect))
                return sect;
            foreach (int w in SectorWalls(sect))
            {
                int next = Walls[w].NextSector;
                if (next >= 0 && InsideZ(x, y, z, next))
                    return next;
            }
        }
        for (int i = Sectors.Length - 1; i >= 0; i--)
            if (InsideZ(x, y, z, i))
                return i;
        return -1;
    }

    public bool InsideZ(double x, double y, double z, int s)
    {
        if (!Valid(s))
            return false;
        GetZsOfSlope(s, x, y, out double cz, out double fz);
        return z >= cz && z <= fz && Inside(x, y, s);
    }

    /// <summary>Blood's RotatePoint (gameutil.cpp): (x, y) turned by ang about (ox, oy), rounded.</summary>
    public static void RotatePoint(ref int x, ref int y, int ang, int ox, int oy)
    {
        double dx = x - ox, dy = y - oy;
        double c = Blood.Cos(ang), s = Blood.Sin(ang);
        x = ox + (int)Blood.DMulScale30R(dx, c, -dy, s);
        y = oy + (int)Blood.DMulScale30R(dx, s, dy, c);
    }

    /// <summary>A wall's ends now (Build x, y).</summary>
    public (int x1, int y1, int x2, int y2) WallEnds(int w)
    {
        var a = Walls[w];
        var b = Walls[a.Point2];
        return (a.X, a.Y, b.X, b.Y);
    }
}
