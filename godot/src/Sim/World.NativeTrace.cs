// Chosen game (GPL v2), with code ported from Build's engine as NBlood has it (see LICENSE.md):
// "Build Engine & Tools" Copyright (c) 1993-1997 Ken Silverman
// Ken Silverman's official web site: "http://www.advsys.net/ken"
// See the included license file "BUILDLIC.TXT" for license info.
// This file has been modified from Ken Silverman's original release
using System;
using System.Collections.Generic;
using Chosen.Data;
using Godot;

namespace Chosen.Sim;

/// <summary>
/// Build's and Blood's queries on a level's Build data (a level loaded from its .MAP): hitscan (clip.cpp, Blood's
/// 1996 mode: the sector walk from the start along two-sided walls, floors and ceilings sloped or not, face, wall and
/// floor sprites by cstat), Blood's HitScan and VectorScan over it (gameutil.cpp: a face sprite's or masked wall's clear
/// texel lets the shot on, a link plane takes it to the other room), cansee (engine.cpp: the walls a line crosses,
/// two-sided ones open where the line passes them).
/// </summary>
public sealed partial class World
{
    /// <summary>The Build wall and sector the last native hitscan hit (-1: none).</summary>
    public int HitWallB = -1, HitSectB = -1;
    public Sprite HitSpriteB;

    // hitscangoal: a range's end (Build x, y); far off without one
    double _goalX = 0x1fffffff, _goalY = 0x1fffffff;

    /// <summary>hitscan: from (x, y, z) in sector sect along (vx, vy, vz) (vz Build z per xy step), walls blocking that
    /// are one-sided or match wallMask (cstat), sprites whose cstat matches spriteMask. Fills hitSect, hitWall,
    /// hitSprite, (hx, hy, hz); false when nothing was hit.</summary>
    bool HitScanBuild(double x, double y, double z, int sect, double vx, double vy, double vz, int wallMask, int spriteMask,
        out int hitSect, out int hitWall, out Sprite hitSprite, out double hx, out double hy, out double hz, Sprite skip = null)
    {
        var b = Board;
        hitSect = hitWall = -1;
        hitSprite = null;
        hx = _goalX;
        hy = _goalY;
        hz = z;
        if (!b.Valid(sect))
            return false;
        var list = new List<int> { sect };
        var seen = new HashSet<int> { sect };
        EnsureSectSprites();
        for (int n = 0; n < list.Count; n++)
        {
            int ds = list[n];
            var sec = b.Sectors[ds];
            // the ceiling and floor
            if (TrySector(ds, x, y, z, vx, vy, vz, true, ref hx, ref hy, ref hz))
            {
                hitSect = ds;
                hitWall = -1;
                hitSprite = null;
            }
            else if (TrySector(ds, x, y, z, vx, vy, vz, false, ref hx, ref hy, ref hz))
            {
                hitSect = ds;
                hitWall = -1;
                hitSprite = null;
            }
            for (int w = sec.WallPtr; w < sec.WallPtr + sec.WallNum; w++)
            {
                var wal = b.Walls[w];
                var wal2 = b.Walls[wal.Point2];
                double x1 = wal.X, y1 = wal.Y, x2 = wal2.X, y2 = wal2.Y;
                // seen from behind: not hit
                if ((x1 - x) * (y2 - y) < (x2 - x) * (y1 - y))
                    continue;
                if (!RIntersect(x, y, z, vx, vy, vz, x1, y1, x2, y2, out double ix, out double iy, out double iz, out _))
                    continue;
                if (Math.Abs(ix - x) + Math.Abs(iy - y) >= Math.Abs(hx - x) + Math.Abs(hy - y))
                    continue;
                int nextsector = wal.NextSector;
                if (nextsector < 0 || (wal.Cstat & wallMask) != 0)
                {
                    (hitSect, hitWall, hitSprite, hx, hy, hz) = (ds, w, null, ix, iy, iz);
                    continue;
                }
                b.GetZsOfSlope(nextsector, ix, iy, out double daz, out double daz2);
                if (iz <= daz || iz >= daz2)
                {
                    (hitSect, hitWall, hitSprite, hx, hy, hz) = (ds, w, null, ix, iy, iz);
                    continue;
                }
                if (seen.Add(nextsector))
                    list.Add(nextsector);
            }
            if (spriteMask == 0 || !_sectSprites.TryGetValue(ds, out var sprites))
                continue;
            foreach (var spr in sprites)
            {
                if (spr.Removed || spr == skip || (spr.Cstat & spriteMask) == 0)
                    continue;
                if (HitSprite(spr, x, y, z, vx, vy, vz, ref hx, ref hy, ref hz))
                {
                    hitSect = ds;
                    hitWall = -1;
                    hitSprite = spr;
                }
            }
        }
        return hitSect >= 0 || hitWall >= 0 || hitSprite != null;
    }

    /// <summary>hitscan_trysector: where the ray meets sector s's ceiling (how -1) or floor, nearer than the hit so far
    /// and inside the sector.</summary>
    bool TrySector(int s, double x, double y, double z, double vx, double vy, double vz, bool ceiling,
        ref double hx, ref double hy, ref double hz)
    {
        var b = Board;
        var sec = b.Sectors[s];
        int stat = ceiling ? sec.CeilingStat : sec.FloorStat;
        int heinum = ceiling ? sec.CeilingHeinum : sec.FloorHeinum;
        double pz = ceiling ? sec.CeilingZ : sec.FloorZ;
        int how = ceiling ? -1 : 1;
        double x1 = double.MaxValue, y1 = 0, z1 = 0;
        if ((stat & 2) != 0)
        {
            var wal = b.Walls[sec.WallPtr];
            var wal2 = b.Walls[wal.Point2];
            double dax = wal2.X - wal.X, day = wal2.Y - wal.Y;
            double i = Math.Floor(Math.Sqrt(dax * dax + day * day));
            if (i == 0)
                return false;
            i = heinum * 32768.0 / i;       // divscale15
            dax *= i;
            day *= i;
            double j = vz * 256 - (dax * vy - day * vx) / 32768;
            if (j != 0)
            {
                double ii = (pz - z) * 256 + (dax * (y - wal.Y) - day * (x - wal.X)) / 32768;
                if ((ii >= 0) == (j >= 0) && Math.Abs(ii) / 2 < Math.Abs(j))
                {
                    double t = ii / j;
                    x1 = x + vx * t;
                    y1 = y + vy * t;
                    z1 = z + vz * t;
                }
            }
        }
        else if (how * vz > 0 && how * z <= how * pz)
        {
            z1 = pz;
            double i = z1 - z;
            if (Math.Abs(i) / 2 < vz * how)
            {
                double t = i / vz;
                x1 = x + vx * t;
                y1 = y + vy * t;
            }
        }
        if (x1 == double.MaxValue || Math.Abs(x1 - x) + Math.Abs(y1 - y) >= Math.Abs(hx - x) + Math.Abs(hy - y))
            return false;
        if (!b.Inside(x1, y1, s))
            return false;
        (hx, hy, hz) = (x1, y1, z1);
        return true;
    }

    /// <summary>rintersect: where the ray from (x1, y1, z1) along (vx, vy, vz) crosses the segment (x3, y3)-(x4, y4); u: how far
    /// along the segment (0..1).</summary>
    static bool RIntersect(double x1, double y1, double z1, double vx, double vy, double vz, double x3, double y3, double x4, double y4,
        out double ix, out double iy, out double iz, out double u)
    {
        ix = iy = iz = u = 0;
        double x34 = x3 - x4, y34 = y3 - y4, x31 = x3 - x1, y31 = y3 - y1;
        double bot = vx * y34 - vy * x34;
        if (bot == 0)
            return false;
        double topt = x31 * y34 - y31 * x34;
        double topu = vx * y31 - vy * x31;
        if (bot > 0 && (topt < 0 || topu < 0 || topu >= bot))
            return false;
        if (bot < 0 && (topt > 0 || topu > 0 || topu <= bot))
            return false;
        double t = topt / bot;
        ix = x1 + vx * t;
        iy = y1 + vy * t;
        iz = z1 + vz * t;
        u = topu / bot;
        return true;
    }

    /// <summary>hitscan_sprite for one sprite: a face sprite by its picture's height and width round its centre, a wall
    /// sprite by its line and height, a floor sprite by its quad.</summary>
    bool HitSprite(Sprite spr, double x, double y, double z, double vx, double vy, double vz, ref double hx, ref double hy, ref double hz)
    {
        switch (spr.Cstat & Cstat.AlignMask)
        {
            case 0:
            {
                double topt = vx * (spr.BX - x) + vy * (spr.BY - y);
                if (topt <= 0)
                    return false;
                double bot = vx * vx + vy * vy;
                if (bot == 0)
                    return false;
                double nz = z + vz * topt / bot;
                var (siz, ofs) = SpriteHeightOfs(spr);
                double z1 = spr.BZ + ofs;
                if (nz < z1 - siz || nz > z1)
                    return false;
                double topu = vx * (spr.BY - y) - vy * (spr.BX - x);
                double offx = vx * topu / bot, offy = vy * topu / bot;
                double dist = offx * offx + offy * offy;
                double w = Tiles.W(spr.Picnum) * spr.XRepeat;
                if (dist > w * w / 128)
                    return false;
                double nx = x + vx * topt / bot, ny = y + vy * topt / bot;
                if (Math.Abs(nx - x) + Math.Abs(ny - y) > Math.Abs(hx - x) + Math.Abs(hy - y))
                    return false;
                (hx, hy, hz) = (nx, ny, nz);
                return true;
            }
            case Cstat.Wall:
            {
                WallSpritePoints(spr, out double x1, out double y1, out double x2, out double y2);
                if ((spr.Cstat & Cstat.OneSided) != 0 && (x1 - x) * (y2 - y) < (x2 - x) * (y1 - y))
                    return false;
                if (!RIntersect(x, y, z, vx, vy, vz, x1, y1, x2, y2, out double ix, out double iy, out double iz, out _))
                    return false;
                if (Math.Abs(ix - x) + Math.Abs(iy - y) > Math.Abs(hx - x) + Math.Abs(hy - y))
                    return false;
                var (k, ofs) = SpriteHeightOfs(spr);
                double daz = spr.BZ + ofs;
                if (iz > daz - k && iz < daz)
                {
                    (hx, hy, hz) = (ix, iy, iz);
                    return true;
                }
                return false;
            }
            case Cstat.Slope:
            {
                // a sloped floor sprite: the ray against its plane
                int heinum = spr.SlopeNow;
                if (heinum == 0)
                    goto case Cstat.Floor;
                double dax = heinum * Sin14(spr.Ang + 1024) * 2, day = heinum * Sin14(spr.Ang + 512) * 2;
                double j = vz * 256 - Math.Floor((dax * vy - day * vx) / 32768);
                if (j == 0)
                    return false;
                if ((spr.Cstat & Cstat.OneSided) != 0 && (j < 0) == ((spr.Cstat & Cstat.YFlip) == 0))
                    return false;
                double i = (spr.BZ - z) * 256 + Math.Floor((dax * (y - spr.BY) - day * (x - spr.BX)) / 32768);
                if ((i < 0) != (j < 0) || Math.Floor(Math.Abs(i) / 2) >= Math.Abs(j))
                    return false;
                double t = i / j;
                double sx = x + Blood.Int(vx * t), sy = y + Blood.Int(vy * t), sz = z + Blood.Int(vz * t);
                if (Math.Abs(sx - x) + Math.Abs(sy - y) > Math.Abs(hx - x) + Math.Abs(hy - y))
                    return false;
                var qx = new double[4];
                var qy = new double[4];
                FloorSpritePoints(spr, sx, sy, qx, qy);
                if (!FloorSpriteClipYou(qx, qy))
                    return false;
                (hx, hy, hz) = (sx, sy, sz);
                return true;
            }
            case Cstat.Floor:
            {
                double iz = spr.BZ;
                if (vz == 0 || (iz - z) * vz < 0)
                    return false;
                if ((spr.Cstat & Cstat.OneSided) != 0 && (z > iz) == ((spr.Cstat & Cstat.YFlip) == 0))
                    return false;
                double ix = x + Blood.Int((iz - z) * vx / vz), iy = y + Blood.Int((iz - z) * vy / vz);
                if (Math.Abs(ix - x) + Math.Abs(iy - y) > Math.Abs(hx - x) + Math.Abs(hy - y))
                    return false;
                var rx = new double[4];
                var ry = new double[4];
                FloorSpritePoints(spr, ix, iy, rx, ry);
                if (!FloorSpriteClipYou(rx, ry))
                    return false;
                (hx, hy, hz) = (ix, iy, iz);
                return true;
            }
        }
        return false;
    }

    /// <summary>spriteheightofs: a sprite's picture height (Build z) and how far its bottom lies below its z (centred:
    /// half the height; the tile's y offset raises it).</summary>
    static (double height, double ofs) SpriteHeightOfs(Sprite spr)
    {
        double hei = Tiles.H(spr.Picnum) * spr.YRepeat * 4;
        double ofs = (spr.Cstat & Cstat.YCenter) != 0 ? Math.Floor(hei / 2) : 0;
        ofs -= Tiles.YOfs(spr.Picnum) * spr.YRepeat * 4;
        return (hei, ofs);
    }

    /// <summary>The hit globals from a native hitscan's result.</summary>
    int SetHitInfo(int type, int sect, int wall, Sprite spr, double hx, double hy, double hz, double dx, double dy)
    {
        HitWallB = wall;
        HitSectB = sect;
        HitSpriteB = spr;
        HitX = hx;
        HitY = hy;
        HitZ = hz;
        HitPos = B2Q(hx, hy, hz);
        HitEnt = null;
        HitType = type;
        if (type < 0)
            return -1;
        if (spr != null)
        {
            HitEnt = spr;
            HitSurf = Tiles.SurfType(spr.Picnum);
            HitNormal = new Vector3((float)-dx, (float)dy, 0).Normalized();
        }
        else if (wall >= 0)
        {
            var bw = Board.Walls[wall];
            HitEnt = wall < _xwall.Length ? _xwall[wall] : null;
            HitSurf = Tiles.SurfType(type == 4 ? bw.OverPicnum : bw.Picnum);
            var (x1, y1, x2, y2) = Board.WallEnds(wall);
            double wx = x2 - x1, wy = y2 - y1, len = Math.Sqrt(wx * wx + wy * wy);
            // facing the wall's sector: Build normal (-dy, dx), Quake (-dy, -dx)
            HitNormal = len > 0 ? new Vector3((float)(-wy / len), (float)(-wx / len), 0) : Vector3.Up;
        }
        else if (sect >= 0)
        {
            bool ceiling = type == 1;
            HitEnt = PlaneObject(sect, ceiling);
            var bs = Board.Sectors[sect];
            HitSurf = ((ceiling ? bs.CeilingStat : bs.FloorStat) & 1) != 0 ? K.kSurfNone
                : Tiles.SurfType(ceiling ? bs.CeilingPicnum : bs.FloorPicnum);
            HitNormal = PlaneNormal(sect, hx, hy, ceiling);
            if (ceiling)
                HitNormal = -HitNormal;
        }
        return type;
    }

    /// <summary>Blood's HitScan (gameutil.cpp) on the Build data: from the sprite's x, y at z along (dx, dy, dz), the
    /// sprite itself not hit; 0 a wall, 4 a two-sided wall's opening passed at a masked wall, 1 / 2 the ceiling or floor,
    /// 3 a sprite, -1 nothing (or out of range).</summary>
    int NativeHitScan(Sprite e, double z, double dx, double dy, double dz, double range, int wallMask, int spriteMask)
    {
        int sect = SpriteBSector(e);
        if (range > 0)
        {
            _goalX = e.BX + Blood.MulScale30(range * 16, Blood.Cos(e.Ang));
            _goalY = e.BY + Blood.MulScale30(range * 16, Blood.Sin(e.Ang));
        }
        HitScanBuild(e.BX, e.BY, z, sect, dx, dy, dz * 16, wallMask, spriteMask, out int hs, out int hw, out var hspr,
            out double hx, out double hy, out double hz, e);
        _goalX = _goalY = 0x1fffffff;
        int type;
        if (hspr != null)
            type = 3;
        else if (hw >= 0)
        {
            int ns = Board.Walls[hw].NextSector;
            if (ns < 0)
                type = 0;
            else
            {
                Board.GetZsOfSlope(ns, hx, hy, out double nc, out double nf);
                type = hz <= nc || hz >= nf ? 0 : 4;
            }
        }
        else if (hs >= 0)
            type = 1 + (z < hz ? 1 : 0);
        else
            type = -1;
        return SetHitInfo(type, hs, hw, hspr, hx, hy, hz, dx, dy);
    }

    /// <summary>Blood's VectorScan (gameutil.cpp) on the Build data: a hitscan from the sprite's place (offset sideways and
    /// up), going on past a face sprite where its picture is clear, past a masked wall where its picture is clear, and
    /// through a link's floor or ceiling into the other room.</summary>
    int NativeVectorScan(Sprite e, double nOffset, double nZOffset, double dx, double dy, double dz, double nRange, int ac = 0)
    {
        var b = Board;
        double x1 = e.BX + Blood.MulScale30(nOffset, Blood.Cos(e.Ang + 512));
        double y1 = e.BY + Blood.MulScale30(nOffset, Blood.Sin(e.Ang + 512));
        double z1 = e.BZ + nZOffset;
        int sect = SpriteBSector(e);
        double srcX = e.BX, srcY = e.BY;
        if (nRange > 0)
        {
            _goalX = x1 + Blood.MulScale30(nRange * 16, Blood.Cos(e.Ang));
            _goalY = y1 + Blood.MulScale30(nRange * 16, Blood.Sin(e.Ang));
        }
        var skipSprites = new HashSet<Sprite> { e };
        var skipMasked = new HashSet<int>();
        double hx = 0, hy = 0, hz = 0;
        int hs = -1, hw = -1;
        Sprite hspr = null;
        void Scan(double sx, double sy, double sz, int s)
        {
            hs = -1;
            hw = -1;
            hspr = null;
            if (!b.Valid(s))
                return;
            HitScanBuildEx(sx, sy, sz, s, dx, dy, dz * 16, skipSprites, skipMasked, out hs, out hw, out hspr, out hx, out hy, out hz);
        }
        Scan(x1, y1, z1, sect);
        _goalX = _goalY = 0x1fffffff;
        for (int nNum = 256; nNum > 0; nNum--)
        {
            if (hs < 0 && hw < 0 && hspr == null)
                return SetHitInfo(-1, -1, -1, null, hx, hy, hz, dx, dy);
            if (nRange > 0 && Blood.ApproxDist(hx - srcX, hy - srcY) > nRange)
                return SetHitInfo(-1, -1, -1, null, hx, hy, hz, dx, dy);
            if (hspr != null)
            {
                var other = hspr;
                if ((other.HitTag & 8) != 0 && (ac & 1) == 0)
                    return SetHitInfo(3, hs, -1, other, hx, hy, hz, dx, dy);
                if ((other.Cstat & 0x30) != 0)
                    return SetHitInfo(3, hs, -1, other, hx, hy, hz, dx, dy);
                int pic = other.Picnum;
                int sx = Tiles.W(pic), sy = Tiles.H(pic);
                if (sx == 0 || sy == 0)
                    return SetHitInfo(3, hs, -1, other, hx, hy, hz, dx, dy);
                double height = sy * other.YRepeat * 4.0;
                double otherZ = other.BZ;
                if ((other.Cstat & 0x80) != 0)
                    otherZ += Math.Floor(height / 2);
                int yofs = Tiles.YOfs(pic);
                if (yofs != 0)
                    otherZ -= yofs * other.YRepeat * 4.0;
                int height2 = (int)Blood.Int((otherZ - hz) * sy / height);
                if ((other.Cstat & 8) == 0)
                    height2 = sy - height2;
                if (height2 >= 0 && height2 < sy)
                {
                    double width = Math.Floor(sx * other.XRepeat / 4.0);
                    width = Math.Floor(width * 3 / 4);
                    double check1 = Blood.Int(((y1 - other.BY) * dx - (x1 - other.BX) * dy) / Math.Floor(Math.Sqrt(dx * dx + dy * dy)));
                    int width2 = width > 0 ? (int)Blood.Int(check1 * sx / width) : 0;
                    width2 += Tiles.XOfs(pic) + sx / 2;
                    if (width2 >= 0 && width2 < sx && !TileTexelClear(pic, width2, height2))
                        return SetHitInfo(3, hs, -1, other, hx, hy, hz, dx, dy);
                }
                skipSprites.Add(other);
                x1 = hx;
                y1 = hy;
                z1 = hz;
                Scan(x1, y1, z1, SpriteBSector(other));
                continue;
            }
            if (hw >= 0)
            {
                var wall = b.Walls[hw];
                if (wall.NextSector < 0)
                    return SetHitInfo(0, hs, hw, null, hx, hy, hz, dx, dy);
                var sec = b.Sectors[hs];
                var next = b.Sectors[wall.NextSector];
                b.GetZsOfSlope(wall.NextSector, hx, hy, out double nc, out double nf);
                if (hz <= nc)
                    return SetHitInfo(0, hs, hw, null, hx, hy, hz, dx, dy);
                if (hz >= nf)
                {
                    if ((sec.FloorStat & 1) == 0 || (next.FloorStat & 1) == 0)
                        return SetHitInfo(0, hs, hw, null, hx, hy, hz, dx, dy);
                    return SetHitInfo(2, hs, -1, null, hx, hy, hz, dx, dy);
                }
                if ((wall.Cstat & 0x30) == 0)
                    return SetHitInfo(0, hs, hw, null, hx, hy, hz, dx, dy);
                double off = (wall.Cstat & 4) != 0 ? Math.Min(sec.FloorZ, next.FloorZ) : Math.Max(sec.CeilingZ, next.CeilingZ);
                int nOffset2 = (int)Math.Floor((hz - off) / 256);
                if ((wall.Cstat & 256) != 0)
                    nOffset2 = -nOffset2;
                int pic = wall.OverPicnum;
                int sx = Tiles.W(pic), sy = Tiles.H(pic);
                if (sx == 0 || sy == 0)
                    return SetHitInfo(0, hs, hw, null, hx, hy, hz, dx, dy);
                nOffset2 = nOffset2 * wall.YRepeat / 8;
                nOffset2 += sy * wall.YPanning / 256;
                var w2 = b.Walls[wall.Point2];
                double len = Blood.ApproxDist(wall.X - w2.X, wall.Y - w2.Y);
                double hoff = (wall.Cstat & 8) != 0 ? Blood.ApproxDist(hx - w2.X, hy - w2.Y) : Blood.ApproxDist(hx - wall.X, hy - wall.Y);
                int nH = wall.XPanning + (len > 0 ? (int)Blood.Int(hoff * wall.XRepeat * 8 / len) : 0);
                nH = ((nH % sx) + sx) % sx;
                nOffset2 = ((nOffset2 % sy) + sy) % sy;
                if (TileTexelClear(pic, nH, nOffset2))
                {
                    skipMasked.Add(hw);
                    if (wall.NextWall >= 0)
                        skipMasked.Add(wall.NextWall);
                    x1 = hx;
                    y1 = hy;
                    z1 = hz;
                    Scan(x1, y1, z1, wall.NextSector);
                    continue;
                }
                return SetHitInfo(4, hs, hw, null, hx, hy, hz, dx, dy);
            }
            if (hs >= 0)
            {
                Sprite link = dz > 0 ? UpperLinkOf(hs) : LowerLinkOf(hs);
                if (link == null)
                    return SetHitInfo(dz > 0 ? 2 : 1, hs, -1, null, hx, hy, hz, dx, dy);
                var far = LinkOwnerOf(link);
                if (far == null)
                    return SetHitInfo(dz > 0 ? 2 : 1, hs, -1, null, hx, hy, hz, dx, dy);
                x1 = hx + far.BX - link.BX;
                y1 = hy + far.BY - link.BY;
                z1 = hz + far.BZ - link.BZ;
                srcX += far.BX - link.BX;
                srcY += far.BY - link.BY;
                Scan(x1, y1, z1, SpriteBSector(far));
                continue;
            }
            return SetHitInfo(-1, -1, -1, null, hx, hy, hz, dx, dy);
        }
        return SetHitInfo(-1, -1, -1, null, hx, hy, hz, dx, dy);
    }

    /// <summary>hitscan with CLIPMASK1 (walls cstat 64, sprites cstat 256), some sprites and masked walls let through (Blood
    /// clears their hitscan bit and looks again).</summary>
    void HitScanBuildEx(double x, double y, double z, int sect, double vx, double vy, double vz, HashSet<Sprite> skip,
        HashSet<int> skipWalls, out int hs, out int hw, out Sprite hspr, out double hx, out double hy, out double hz)
    {
        var saved = new List<(int w, int c)>();
        foreach (int w in skipWalls)
        {
            saved.Add((w, Board.Walls[w].Cstat));
            Board.Walls[w].Cstat &= ~64;
        }
        var savedSpr = new List<(Sprite s, int c)>();
        foreach (var s in skip)
        {
            savedSpr.Add((s, s.Cstat));
            s.Cstat &= ~256;
        }
        HitScanBuild(x, y, z, sect, vx, vy, vz, 64, 256, out hs, out hw, out hspr, out hx, out hy, out hz);
        foreach (var (w, c) in saved)
            Board.Walls[w].Cstat = c;
        foreach (var (s, c) in savedSpr)
            s.Cstat = c;
    }

    /// <summary>Is a tile's texel (column x, row y) clear (palette index 255)?</summary>
    static bool TileTexelClear(int tile, int x, int y) => TileArt.Clear(tile, x, y);

    /// <summary>FindSector (gameutil.cpp): the sector holding (x, y) with z between its ceiling and floor there, looked
    /// for in start, then its neighbours, then every sector; -1 if none.</summary>
    public int FindSectorB(double x, double y, double z, int start)
    {
        var b = Board;
        if (b.Valid(start))
        {
            if (b.InsideZ(x, y, z, start))
                return start;
            foreach (int w in b.SectorWalls(start))
            {
                int o = b.Walls[w].NextSector;
                if (o >= 0 && b.InsideZ(x, y, z, o))
                    return o;
            }
        }
        for (int i = 0; i < b.SectorCount; i++)
            if (b.InsideZ(x, y, z, i))
                return i;
        return -1;
    }

    /// <summary>CheckProximityWall (gameutil.cpp): is (x, y) within nDist (16 Build units each) of wall w's line?</summary>
    bool CheckProximityWall(int w, double x, double y, double nDist)
    {
        var b = Board;
        double x1 = b.Walls[w].X, y1 = b.Walls[w].Y;
        var w2 = b.Walls[b.Walls[w].Point2];
        double x2 = w2.X, y2 = w2.Y;
        nDist *= 16;
        if (x1 < x2)
        {
            if (x <= x1 - nDist || x >= x2 + nDist)
                return false;
        }
        else
        {
            if (x <= x2 - nDist || x >= x1 + nDist)
                return false;
            if (x1 == x2)
            {
                double px1 = x - x1, py1 = y - y1, px2 = x - x2, py2 = y - y2;
                double dist1 = px1 * px1 + py1 * py1, dist2 = px2 * px2 + py2 * py2;
                if (y1 < y2)
                {
                    if (y <= y1 - nDist || y >= y2 + nDist)
                        return false;
                    if (y < y1)
                        return dist1 < nDist * nDist;
                    if (y > y2)
                        return dist2 < nDist * nDist;
                }
                else
                {
                    if (y <= y2 - nDist || y >= y1 + nDist)
                        return false;
                    if (y < y2)
                        return dist2 < nDist * nDist;
                    if (y > y1)
                        return dist1 < nDist * nDist;
                }
                return true;
            }
        }
        if (y1 < y2)
        {
            if (y <= y1 - nDist || y >= y2 + nDist)
                return false;
        }
        else
        {
            if (y <= y2 - nDist || y >= y1 + nDist)
                return false;
            if (y1 == y2)
            {
                double px1 = x - x1, py1 = y - y1, px2 = x - x2, py2 = y - y2;
                double dist1 = px1 * px1 + py1 * py1, dist2 = px2 * px2 + py2 * py2;
                if (x1 < x2)
                {
                    if (x <= x1 - nDist || x >= x2 + nDist)
                        return false;
                    if (x < x1)
                        return dist1 < nDist * nDist;
                    if (x > x2)
                        return dist2 < nDist * nDist;
                }
                else
                {
                    if (x <= x2 - nDist || x >= x1 + nDist)
                        return false;
                    if (x < x2)
                        return dist2 < nDist * nDist;
                    if (x > x1)
                        return dist1 < nDist * nDist;
                }
            }
        }
        double dx = x2 - x1, dy = y2 - y1;
        double px = x - x2, py = y - y2;
        double side = px * dx + dy * py;
        if (side >= 0)
            return px * px + py * py < nDist * nDist;
        px = x - x1;
        py = y - y1;
        side = px * dx + dy * py;
        if (side <= 0)
            return px * px + py * py < nDist * nDist;
        double check1 = px * dy - dx * py;
        double check2 = dy * dy + dx * dx;
        return check1 * check1 < check2 * nDist * nDist;
    }

    /// <summary>GetClosestSpriteSectors (gameutil.cpp): the sectors an explosion at (x, y) in sector s reaches within nDist
    /// through two-sided walls (each wall's end near enough; accurate: the wall itself), and the x-walls on the way that
    /// an impact triggers (vector, not yet triggered, off).</summary>
    HashSet<int> ClosestSpriteSectors(int s, double x, double y, double nDist, List<int> xwalls, bool accurate)
    {
        var b = Board;
        var list = new List<int> { s };
        var bits = new HashSet<int> { s };
        var reached = new HashSet<int> { s };
        for (int i = 0; i < list.Count; i++)
        {
            int cur = list[i];
            foreach (int j in b.SectorWalls(cur))
            {
                var wall = b.Walls[j];
                int ns = wall.NextSector;
                if (ns < 0 || bits.Contains(ns))
                    continue;
                bool within;
                if (!accurate)
                {
                    within = CheckProximityWall(wall.Point2, x, y, nDist);
                    bits.Add(ns);
                }
                else
                {
                    within = WallDistance(j, x, y) <= nDist * 16;
                    if (within)
                        bits.Add(ns);
                }
                if (!within)
                    continue;
                reached.Add(ns);
                list.Add(ns);
                if (xwalls != null && j < _xwall.Length && _xwall[j] is { } xw && xw.XVector != 0 && xw.IsTriggered == 0 && xw.XState == 0)
                    xwalls.Add(j);
            }
        }
        return reached;
    }

    /// <summary>getwalldist: the Manhattan distance from (x, y) to the nearest point of wall w.</summary>
    double WallDistance(int w, double x, double y)
    {
        var (x1, y1, x2, y2) = Board.WallEnds(w);
        double dx = x2 - x1, dy = y2 - y1, len2 = dx * dx + dy * dy;
        double t = len2 > 0 ? Math.Clamp(((x - x1) * dx + (y - y1) * dy) / len2, 0, 1) : 0;
        double cx = x1 + dx * t, cy = y1 + dy * t;
        return Math.Abs(cx - x) + Math.Abs(cy - y);
    }

    /// <summary>Water at a point of a level from its Build data: its sector underwater (x-sector), or the room under a water
    /// or goo link.</summary>
    bool NativePointIsWater(Vector3 p)
    {
        double x = Q2BX(p), y = Q2BY(p), z = Q2BZ(p);
        int s = SectorOfPointB(x, y, z);
        if (s < 0)
            return false;
        if (XSectorOf(s) is { } xs && xs.SUnderwater != 0)
            return true;
        var lo = LowerLinkOf(s);
        return lo != null && (lo.Type == K.kMarkerLowWater || lo.Type == K.kMarkerLowGoo);
    }

    /// <summary>The Build sector holding a point (Build units) whose column holds its z, looked for from hint first; -1 if
    /// none.</summary>
    public int SectorOfPointB(double x, double y, double z, int hint = -1) => Board.UpdateSectorZ(x, y, z, hint);

    /// <summary>cansee (engine.cpp): is (x2, y2, z2) in sect2 in sight of (x1, y1, z1) in sect1? The walls the line crosses
    /// from sect1 on: a one-sided or one-way (cstat 32) wall stops it, a two-sided one only where the line passes outside
    /// its sector's or the next sector's floor and ceiling; seen if the walk reaches sect2.</summary>
    public bool NativeCanSee(double x1, double y1, double z1, int sect1, double x2, double y2, double z2, int sect2)
    {
        var b = Board;
        if (!b.Valid(sect1) || !b.Valid(sect2))
            return false;
        if (x1 == x2 && y1 == y2)
            return sect1 == sect2;
        double x21 = x2 - x1, y21 = y2 - y1, z21 = z2 - z1;
        var list = new List<int> { sect1 };
        var seen = new HashSet<int> { sect1 };
        for (int n = 0; n < list.Count; n++)
        {
            int ds = list[n];
            var sec = b.Sectors[ds];
            for (int w = sec.WallPtr; w < sec.WallPtr + sec.WallNum; w++)
            {
                var wal = b.Walls[w];
                var wal2 = b.Walls[wal.Point2];
                double x31 = wal.X - x1, x34 = wal.X - wal2.X, y31 = wal.Y - y1, y34 = wal.Y - wal2.Y;
                double bot = y21 * x34 - x21 * y34;
                if (bot <= 0)
                    continue;
                double t = y21 * x31 - x21 * y31;
                if (t < 0 || t >= bot)
                    continue;
                t = y31 * x34 - x31 * y34;
                if (t < 0 || t >= bot)
                    continue;
                int nexts = wal.NextSector;
                if (nexts < 0 || (wal.Cstat & 32) != 0)
                    return false;
                double f = t / bot;
                double x = x1 + x21 * f, y = y1 + y21 * f, z = z1 + z21 * f;
                b.GetZsOfSlope(ds, x, y, out double cz, out double fz);
                if (z <= cz || z >= fz)
                    return false;
                b.GetZsOfSlope(nexts, x, y, out cz, out fz);
                if (z <= cz || z >= fz)
                    return false;
                if (seen.Add(nexts))
                    list.Add(nexts);
            }
        }
        return seen.Contains(sect2);
    }
}
