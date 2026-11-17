// Chosen game (GPL v2), with code ported from Build's engine as NBlood has it (see LICENSE.md):
// "Build Engine & Tools" Copyright (c) 1993-1997 Ken Silverman
// Ken Silverman's official web site: "http://www.advsys.net/ken"
// See the included license file "BUILDLIC.TXT" for license info.
// This file has been modified from Ken Silverman's original release
using System;
using System.Collections.Generic;
using Chosen.Data;
using Godot;
using Chosen.Shared.Blood.Map;

namespace Chosen.Sim;

/// <summary>
/// Build's collision on the level's sectors, as Blood's engine has it (clip.cpp in its 1996 mode, ENGINE_19960925):
/// clipmove (walls and blocking sprites in the sectors around the move, a two-sided wall blocking where the next
/// sector's floor or ceiling is too far for the sprite's clip band), pushmove, and getzrange (the highest floor and
/// lowest ceiling of the sectors within reach, and the blocking sprites), with Blood's GetZRange on top (sky, Depth,
/// links to the room beyond). The level's walls and heights are the board's as the game has moved them. In Build
/// units (x = Quake x * 16, y = -Quake y * 16, z = -Quake z * 256) so the arithmetic is Build's.
/// </summary>
public sealed partial class World
{
    /// <summary>Does movement collide with Build's sectors (a level loaded)?</summary>
    public bool UseSectorClip => Map != null;

    const double MaxClipDist = 1024;          // MAXCLIPDIST
    const int ClipMask = 1;                    // CLIPMASK0: walls' and sprites' cstat 1

    // a Build wall's x-object (blood_wall: switches pushed, walls shot)
    Wall[] _xwall = Array.Empty<Wall>();

    void IndexClip()
    {
        int n = Board?.WallCount ?? 0;
        _xwall = new Wall[n];
        foreach (var w in Walls)
            if (w.BWall >= 0 && w.BWall < n)
                _xwall[w.BWall] = w;
    }

    // ---------------------------------------------------------------- the level now, in Build units
    static double Sin14(double a) => Math.Sin(Blood.AngMask(a) * (Math.PI / 1024)) * 16384;

    static Vector3 QOf(double bx, double by, double bz) => new((float)(bx / 16), (float)(-by / 16), (float)(-bz / 256));

    /// <summary>Wall w's ends now (Build x, y): the board's, as the game has moved them.</summary>
    void WallNow(int w, out double x1, out double y1, out double x2, out double y2)
    {
        var a = Board.Walls[w];
        var b = Board.Walls[a.Point2];
        x1 = a.X;
        y1 = a.Y;
        x2 = b.X;
        y2 = b.Y;
    }

    /// <summary>getzsofslope: sector s's floor and ceiling (Build z) at (x, y) now.</summary>
    void ZsOfSlope(int s, double x, double y, out double ceilz, out double florz)
    {
        Board.GetZsOfSlope(s, x, y, out ceilz, out florz);
    }

    bool InsideB(int s, double x, double y) => InSectorNow(s, QOf(x, y, 0), out _);

    // each sector's sprites (Build's headspritesect), gathered once a frame and kept as sprites change sector
    readonly Dictionary<int, List<Sprite>> _sectSprites = new();
    int _sectSpritesFrame = -1;

    void EnsureSectSprites()
    {
        if (_sectSpritesFrame == Frame)
            return;
        foreach (var l in _sectSprites.Values)
            l.Clear();
        _sectSpritesFrame = -1;          // (SpriteBSector below doesn't move them while gathering)
        foreach (var e in Sprites)
            if (!e.Removed)
            {
                int s = SpriteBSector(e);
                if (s < 0)
                    continue;
                if (!_sectSprites.TryGetValue(s, out var l))
                    _sectSprites[s] = l = new List<Sprite>();
                l.Add(e);
            }
        _sectSpritesFrame = Frame;
    }

    /// <summary>A sprite went from sector from to sector to: the frame's lists follow.</summary>
    void SectSpriteMoved(Sprite e, int from, int to)
    {
        if (_sectSpritesFrame != Frame || from == to)
            return;
        if (from >= 0 && _sectSprites.TryGetValue(from, out var a))
            a.Remove(e);
        if (to >= 0)
        {
            if (!_sectSprites.TryGetValue(to, out var b))
                _sectSprites[to] = b = new List<Sprite>();
            if (!b.Contains(e))
                b.Add(e);
        }
    }

    /// <summary>The sprites Build clips against in sector s (cstat 1), not self.</summary>
    IEnumerable<Sprite> ClipSprites(int s, Sprite self)
    {
        EnsureSectSprites();
        if (!_sectSprites.TryGetValue(s, out var l))
            yield break;
        for (int i = 0; i < l.Count; i++)
        {
            var e = l[i];
            if (!e.Removed && e != self && (e.Cstat & ClipMask) != 0 && e.StatNum != K.kStatFree)
                yield return e;
        }
    }

    /// <summary>The bottom and top (Build z) of a face or wall sprite (Blood's GetSpriteExtents: centred on z by the
    /// tile's y offset).</summary>
    static (double bottom, double top) SpriteSpan(Sprite e)
    {
        var (top, bottom) = Physics.SpriteExtents(e.BZ, e.Picnum, e.YRepeat, e.Cstat);
        return (bottom, top);
    }

    /// <summary>get_wallspr_points: a wall sprite's ends.</summary>
    static void WallSpritePoints(Sprite e, out double x1, out double y1, out double x2, out double y2)
    {
        double xoff = Tiles.XOfs(e.Picnum) + e.XOffset;
        if ((e.Cstat & Cstat.XFlip) != 0)
            xoff = -xoff;
        double dax = Sin14(e.Ang) * e.XRepeat, day = Sin14(e.Ang + 1536) * e.XRepeat;
        double l = Tiles.W(e.Picnum), k = Math.Floor(l / 2) + xoff;
        x1 = e.BX - dax * k / 65536;
        y1 = e.BY - day * k / 65536;
        x2 = x1 + dax * l / 65536;
        y2 = y1 + day * l / 65536;
    }

    /// <summary>get_floorspr_points (flat): a floor sprite's corners, less (px, py).</summary>
    static void FloorSpritePoints(Sprite e, double px, double py, double[] rx, double[] ry)
    {
        double cosang = Sin14(e.Ang + 512), sinang = Sin14(e.Ang);
        double spanx = Tiles.W(e.Picnum), spany = Tiles.H(e.Picnum);
        double adjx = Tiles.XOfs(e.Picnum) + e.XOffset, adjy = Tiles.YOfs(e.Picnum) + e.YOffset;
        if ((e.Cstat & Cstat.XFlip) != 0)
            adjx = -adjx;
        if ((e.Cstat & Cstat.YFlip) != 0)
            adjy = -adjy;
        double cx = (Math.Floor(spanx / 2) + adjx) * e.XRepeat, cy = (Math.Floor(spany / 2) + adjy) * e.YRepeat;
        double rsx = spanx * e.XRepeat, rsy = spany * e.YRepeat;
        // (a sloped one: its length along the slope, so shorter on the map)
        int heinum = e.SlopeNow;
        double k = heinum == 0 ? 1 : 4096 / Math.Sqrt((double)heinum * heinum + 16777216);
        double ofsx = -(cosang * rsy / 65536) * k, ofsy = -(sinang * rsy / 65536) * k;
        rx[0] = e.BX + (sinang * cx + cosang * k * cy) / 65536 - px;
        ry[0] = e.BY + (sinang * k * cy - cosang * cx) / 65536 - py;
        rx[1] = rx[0] - sinang * rsx / 65536;
        ry[1] = ry[0] + cosang * rsx / 65536;
        rx[2] = rx[1] + ofsx;
        ry[2] = ry[1] + ofsy;
        rx[3] = rx[0] + ofsx;
        ry[3] = ry[0] + ofsy;
    }

    // ---------------------------------------------------------------- clipmove
    struct ClipLine
    {
        public double X1, Y1, X2, Y2;
        public int Kind;          // Hit.Wall or Hit.Sprite
        public int Wall;          // the Build wall (Hit.Wall)
        public Sprite Spr;        // the sprite (Hit.Sprite)
    }

    readonly List<ClipLine> _clipLines = new();
    readonly List<int> _clipSects = new();
    readonly HashSet<int> _clipSectSet = new();

    void AddClipSect(int s)
    {
        if (_clipSectSet.Add(s))
            _clipSects.Add(s);
    }

    void AddClipLine(double x1, double y1, double x2, double y2, int kind, int wall, Sprite spr) =>
        _clipLines.Add(new ClipLine { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Kind = kind, Wall = wall, Spr = spr });

    /// <summary>clipinsideboxline: 0 if the line misses the box (walldist around x, y), 1 crossing it in front, 2 behind.</summary>
    static int ClipInsideBoxLine(double x, double y, double x1, double y1, double x2, double y2, double walldist)
    {
        double r = walldist * 2;
        x1 += walldist - x;
        x2 += walldist - x;
        if ((x1 < 0 && x2 < 0) || (x1 >= r && x2 >= r))
            return 0;
        y1 += walldist - y;
        y2 += walldist - y;
        if ((y1 < 0 && y2 < 0) || (y1 >= r && y2 >= r))
            return 0;
        x2 -= x1;
        y2 -= y1;
        if (x2 * (walldist - y1) >= y2 * (walldist - x1))
        {
            x2 *= x2 > 0 ? 0 - y1 : r - y1;
            y2 *= y2 > 0 ? r - x1 : 0 - x1;
            return x2 < y2 ? 1 : 0;
        }
        x2 *= x2 > 0 ? r - y1 : 0 - y1;
        y2 *= y2 > 0 ? 0 - x1 : r - x1;
        return x2 >= y2 ? 2 : 0;
    }

    /// <summary>cliptestsector (1996): does the two-sided wall into nextsect block a sprite at z (its clip band
    /// flordist below, ceildist above) at (x, y)? Its floor more than a unit higher and the sprite low enough to hit
    /// it, or its ceiling lower and the sprite high enough.</summary>
    bool ClipTestSector(int dasect, int nextsect, double flordist, double ceildist, double x, double y, double posz)
    {
        ZsOfSlope(dasect, x, y, out double cz, out double fz);
        ZsOfSlope(nextsect, x, y, out double cz2, out double fz2);
        if (fz2 < fz - 256 && !Map.FloorSky(nextsect) && posz >= fz2 - (flordist - 1))
            return true;
        if (cz2 > cz + 256 && !Map.CeilSky(nextsect) && posz <= cz2 + (ceildist - 1))
            return true;
        return false;
    }

    /// <summary>The sprites of sector s as clip lines (clipmove_sprite: faces as boxes, walls and floors as lines).</summary>
    void ClipMoveSprites(Sprite self, int s, double px, double py, double pz, double walldist, double ceildist, double flordist,
        double minx, double miny, double maxx, double maxy, double centx, double centy, double diffx, double diffy, double rad)
    {
        var rx = new double[4];
        var ry = new double[4];
        foreach (var e in ClipSprites(s, self))
        {
            double x1 = e.BX, y1 = e.BY;
            switch (e.Cstat & Cstat.AlignMask)
            {
                case 0:
                {
                    if (x1 < minx || x1 > maxx || y1 < miny || y1 > maxy)
                        break;
                    var (bottom, top) = SpriteSpan(e);
                    if (pz > top - flordist && pz < bottom + ceildist)
                    {
                        double bsz = e.ClipDist * 4 + walldist;
                        // (b3d_fixblood: a missile starting its move already inside a dude's box, MoveMissileSectors)
                        if (_insideDude && _startInside == null && Info.IsDude(e.Type) && Math.Abs(px - x1) < bsz && Math.Abs(py - y1) < bsz)
                            _startInside = e;
                        double b = diffx < 0 ? -bsz : bsz;
                        AddClipLine(x1 - b, y1 - b, x1 - b, y1 + b, Hit.Sprite, -1, e);
                        b = diffy < 0 ? -bsz : bsz;
                        AddClipLine(x1 + b, y1 - b, x1 - b, y1 - b, Hit.Sprite, -1, e);
                    }
                    break;
                }
                case Cstat.Wall:
                {
                    var (bottom, top) = SpriteSpan(e);
                    if (!(pz > top - flordist && pz < bottom + ceildist))
                        break;
                    WallSpritePoints(e, out x1, out y1, out double x2, out double y2);
                    if (ClipInsideBoxLine(centx, centy, x1, y1, x2, y2, rad) == 0)
                        break;
                    double vx = Sin14(e.Ang + 256 + 512) * walldist / 16384, vy = Sin14(e.Ang + 256) * walldist / 16384;
                    if ((x1 - px) * (y2 - py) >= (x2 - px) * (y1 - py))
                        AddClipLine(x1 + vx, y1 + vy, x2 + vy, y2 - vx, Hit.Sprite, -1, e);
                    else
                    {
                        if ((e.Cstat & Cstat.OneSided) != 0)
                            break;
                        AddClipLine(x2 - vx, y2 - vy, x1 - vy, y1 + vx, Hit.Sprite, -1, e);
                    }
                    // side blocker
                    if ((x2 - x1) * (px - x1) + (y2 - y1) * (py - y1) < 0)
                        AddClipLine(x1 - vy, y1 + vx, x1 + vx, y1 + vy, Hit.Sprite, -1, e);
                    else if ((x1 - x2) * (px - x2) + (y1 - y2) * (py - y2) < 0)
                        AddClipLine(x2 + vy, y2 - vx, x2 - vx, y2 - vy, Hit.Sprite, -1, e);
                    break;
                }
                case Cstat.Slope:
                case Cstat.Floor:
                {
                    double sz = e.ZOfSlope(px, py);
                    if (!(pz > sz - flordist && pz < sz + ceildist))
                    {
                        ClipMoveSlopeSprite(e, px, py, pz, walldist, ceildist, flordist, centx, centy, rad);
                        break;
                    }
                    if ((e.Cstat & Cstat.OneSided) != 0 && (pz > sz) == ((e.Cstat & Cstat.YFlip) == 0))
                        break;
                    FloorSpritePoints(e, 0, 0, rx, ry);
                    double vx = Sin14(e.Ang - 256 + 512) * walldist / 16384, vy = Sin14(e.Ang - 256) * walldist / 16384;
                    if ((rx[0] - px) * (ry[1] - py) < (rx[1] - px) * (ry[0] - py))
                    {
                        if (ClipInsideBoxLine(centx, centy, rx[1], ry[1], rx[0], ry[0], rad) != 0)
                            AddClipLine(rx[1] - vy, ry[1] + vx, rx[0] + vx, ry[0] + vy, Hit.Sprite, -1, e);
                    }
                    else if ((rx[2] - px) * (ry[3] - py) < (rx[3] - px) * (ry[2] - py))
                    {
                        if (ClipInsideBoxLine(centx, centy, rx[3], ry[3], rx[2], ry[2], rad) != 0)
                            AddClipLine(rx[3] + vy, ry[3] - vx, rx[2] - vx, ry[2] - vy, Hit.Sprite, -1, e);
                    }
                    if ((rx[1] - px) * (ry[2] - py) < (rx[2] - px) * (ry[1] - py))
                    {
                        if (ClipInsideBoxLine(centx, centy, rx[2], ry[2], rx[1], ry[1], rad) != 0)
                            AddClipLine(rx[2] - vx, ry[2] - vy, rx[1] - vy, ry[1] + vx, Hit.Sprite, -1, e);
                    }
                    else if ((rx[3] - px) * (ry[0] - py) < (rx[0] - px) * (ry[3] - py))
                    {
                        if (ClipInsideBoxLine(centx, centy, rx[0], ry[0], rx[3], ry[3], rad) != 0)
                            AddClipLine(rx[0] + vx, ry[0] + vy, rx[3] + vy, ry[3] - vx, Hit.Sprite, -1, e);
                    }
                    ClipMoveSlopeSprite(e, px, py, pz, walldist, ceildist, flordist, centx, centy, rad);
                    break;
                }
            }
        }
    }

    /// <summary>clipmove_sprite's part for a sloped floor sprite: where the mover's height (its middle, feet and head)
    /// meets the slope, a line across the sprite stops it (walking into the slope from below or above).</summary>
    void ClipMoveSlopeSprite(Sprite e, double px, double py, double pz, double walldist, double ceildist, double flordist,
        double centx, double centy, double rad)
    {
        int heinum = e.SlopeNow;
        if (heinum == 0)
            return;
        double cosang = Sin14(e.Ang + 512), sinang = Sin14(e.Ang);
        double spanx = Tiles.W(e.Picnum), spany = Tiles.H(e.Picnum);
        double adjx = Tiles.XOfs(e.Picnum) * ((e.Cstat & Cstat.XFlip) != 0 ? -1 : 0), adjy = Tiles.YOfs(e.Picnum) * ((e.Cstat & Cstat.YFlip) != 0 ? -1 : 0);
        double cx = (Math.Floor(spanx / 2) + adjx) * e.XRepeat, cy = (Math.Floor(spany / 2) + adjy) * e.YRepeat;
        double rsx = spanx * e.XRepeat, rsy = spany * e.YRepeat;
        double ratio = Math.Floor(Math.Sqrt((double)heinum * heinum + 16777216));
        double[] zz = { pz, pz + flordist, pz - ceildist };
        for (int k = 0; k < 3; k++)
        {
            double jj = Blood.Int((e.BZ - zz[k]) * 262144 / heinum);
            double jj2 = Math.Floor(jj * ratio / 4096);
            if (jj2 > cy * 256 || jj2 < (cy - rsy) * 256)
                continue;
            double v1x = e.BX + Math.Floor(sinang * cx / 65536) + Math.Floor(jj * cosang / 16777216);
            double v1y = e.BY - Math.Floor(cosang * cx / 65536) + Math.Floor(jj * sinang / 16777216);
            double v2x = v1x - Math.Floor(sinang * rsx / 65536), v2y = v1y + Math.Floor(cosang * rsx / 65536);
            double vx = Sin14(e.Ang + 1024 + 256 + 512) * walldist / 16384, vy = Sin14(e.Ang + 1024 + 256) * walldist / 16384;
            if (ClipInsideBoxLine(centx, centy, v1x, v1y, v2x, v2y, rad) == 0)
                continue;
            // the nearest point of that line: the slope's height there
            double dx = v2x - v1x, dy = v2y - v1y, len = dx * dx + dy * dy;
            double t = len > 0 ? Math.Clamp(((px - v1x) * dx + (py - v1y) * dy) / len, 0, 1) : 0;
            double sz = e.ZOfSlope(v1x + dx * t, v1y + dy * t);
            if (pz < sz - flordist || pz > sz + ceildist)
                continue;
            if ((v1x - px) * (v2y - py) >= (v2x - px) * (v1y - py))
                AddClipLine(v1x + vx, v1y + vy, v2x + vy, v2y - vx, Hit.Sprite, -1, e);
            else
            {
                if ((e.Cstat & Cstat.OneSided) != 0)
                    continue;
                AddClipLine(v2x - vx, v2y - vy, v1x - vy, v1y + vx, Hit.Sprite, -1, e);
            }
        }
    }

    /// <summary>cliptrace: the first clip line the move pos-goal runs into from its front; goal set to just before it.</summary>
    int ClipTrace(double px, double py, ref double gx, ref double gy)
    {
        int hit = -1;
        for (int z = _clipLines.Count - 1; z >= 0; z--)
        {
            var l = _clipLines[z];
            double ax = l.X2 - l.X1, ay = l.Y2 - l.Y1;
            double topu = ax * (py - l.Y1) - (px - l.X1) * ay;
            if (topu <= 0 || ax * (gy - l.Y1) > (gx - l.X1) * ay)
                continue;
            double dx = gx - px, dy = gy - py;
            if (dx * (l.Y1 - py) > (l.X1 - px) * dy || dx * (l.Y2 - py) <= (l.X2 - px) * dy)
                continue;
            double bot = dx * ay - ax * dy;
            if (bot == 0)
                continue;
            // where the move meets the line, a little before it (Build steps back a unit of topu at a time until
            // the point is in front)
            double t = topu / bot;
            double len = Math.Sqrt(dx * dx + dy * dy);
            if (len > 0)
                t = Math.Max(0, t - 0.25 / len);
            double nx = px + dx * t, ny = py + dy * t;
            if (Math.Abs(px - nx) + Math.Abs(py - ny) < Math.Abs(px - gx) + Math.Abs(py - gy))
            {
                gx = nx;
                gy = ny;
                hit = z;
            }
        }
        return hit;
    }

    /// <summary>keepaway: (x, y) moved off line w to its front, a unit at a time.</summary>
    void KeepAway(ref double x, ref double y, int w)
    {
        var l = _clipLines[w];
        double dx = l.X2 - l.X1, dy = l.Y2 - l.Y1;
        double ox = Math.Sign(-dy), oy = Math.Sign(dx);
        bool first = Math.Abs(dx) <= Math.Abs(dy);
        for (int i = 0; i < 64; i++)
        {
            if (dx * (y - l.Y1) > (x - l.X1) * dy)
                return;
            if (!first)
                x += ox;
            else
                y += oy;
            first = !first;
        }
    }

    /// <summary>
    /// clipmove (Blood's 1996 engine): moves (x, y) by (xv, yv) Build units from sector sect, sliding along what it
    /// meets (three tries); the sector it ends in (clipmove_compat: the first of the sectors looked at that holds it,
    /// else the one whose column is nearest z); -1 when none holds it. Returns what it hit first (Hit.Wall, Hit.Sprite,
    /// 0) with the wall or sprite.
    /// </summary>
    // b3d_fixblood, a missile's move: Build's clip lines stop only what crosses them from outside, and a missile isn't
    // solid (its SEQ frames not blockable: napalm's 61), so a dude stepping onto it between two game frames (its move
    // never meets the missile) left it inside the dude's box, and it flew on through (DWE3M1's gill beasts and the
    // napalm). Set: a dude whose box holds the start is hit there.
    bool _insideDude;
    Sprite _startInside;

    int ClipMoveB(Sprite self, ref double x, ref double y, double z, ref int sect, double xv, double yv,
        double walldist, double ceildist, double flordist, out int hitWall, out Sprite hitSprite, int tries = 3)
    {
        hitWall = -1;
        hitSprite = null;
        _startInside = null;
        if ((xv == 0 && yv == 0) || sect < 0)
            return 0;
        double gx = x + xv, gy = y + yv;
        double cx = (x + gx) / 2, cy = (y + gy) / 2;
        double diffx = gx - x, diffy = gy - y;
        double rad = Math.Sqrt(diffx * diffx + diffy * diffy) + MaxClipDist + walldist + 8;
        double minx = cx - rad, miny = cy - rad, maxx = cx + rad, maxy = cy + rad;
        _clipLines.Clear();
        _clipSects.Clear();
        _clipSectSet.Clear();
        AddClipSect(sect);
        for (int i = 0; i < _clipSects.Count && i < 256; i++)
        {
            int dasect = _clipSects[i];
            foreach (int j in Map.SectorWalls(dasect))
            {
                WallNow(j, out double x1, out double y1, out double x2, out double y2);
                if ((x1 < minx && x2 < minx) || (x1 > maxx && x2 > maxx) || (y1 < miny && y2 < miny) || (y1 > maxy && y2 > maxy))
                    continue;
                double dx = x2 - x1, dy = y2 - y1;
                if (dx * (y - y1) < (x - x1) * dy)
                    continue;      // not facing you
                double rxx = dy > 0 ? maxx : minx, ryy = dx > 0 ? miny : maxy;
                if (dx * (ryy - y1) >= dy * (rxx - x1))
                    continue;
                int next = Map.WallNext(j);
                bool clipyou;
                if (next < 0 || (Map.WallCstat(j) & ClipMask) != 0)
                    clipyou = true;
                else
                {
                    // clipmove_tweak_pos: where the move crosses the wall, else the start
                    double vx = x, vy = y;
                    double den = diffx * dy - diffy * dx;
                    if (den != 0)
                    {
                        double t = ((x1 - x) * dy - (y1 - y) * dx) / den, u = ((x1 - x) * diffy - (y1 - y) * diffx) / den;
                        if (t >= 0 && t <= 1 && u >= 0 && u <= 1)
                        {
                            vx = x + diffx * t;
                            vy = y + diffy * t;
                        }
                    }
                    clipyou = ClipTestSector(dasect, next, flordist, ceildist, vx, vy, z);
                }
                if (clipyou)
                {
                    // boxes at the ends (Build's signed size on both axes)
                    double bsz = diffx < 0 ? -walldist : walldist;
                    AddClipLine(x1 - bsz, y1 - bsz, x1 - bsz, y1 + bsz, Hit.Wall, j, null);
                    AddClipLine(x2 - bsz, y2 - bsz, x2 - bsz, y2 + bsz, Hit.Wall, j, null);
                    bsz = diffy < 0 ? -walldist : walldist;
                    AddClipLine(x1 + bsz, y1 - bsz, x1 - bsz, y1 - bsz, Hit.Wall, j, null);
                    AddClipLine(x2 + bsz, y2 - bsz, x2 - bsz, y2 - bsz, Hit.Wall, j, null);
                    double ox = dy > 0 ? -walldist : walldist, oy = dx < 0 ? -walldist : walldist;
                    AddClipLine(x1 + ox, y1 + oy, x2 + ox, y2 + oy, Hit.Wall, j, null);
                }
                else if (next >= 0)
                    AddClipSect(next);
            }
            ClipMoveSprites(self, dasect, x, y, z, walldist, ceildist, flordist, minx, miny, maxx, maxy, cx, cy, diffx, diffy, rad);
        }
        if (_startInside != null)
        {
            hitSprite = _startInside;
            _startInside = null;
            return Hit.Sprite;
        }
        var hitwalls = new int[4];
        int ret = 0;
        int cnt = tries;
        int hitwall;
        double mvx = xv, mvy = yv;
        do
        {
            double vx = gx, vy = gy;
            hitwall = ClipTrace(x, y, ref vx, ref vy);
            if (hitwall >= 0)
            {
                var l = _clipLines[hitwall];
                double crx = l.X2 - l.X1, cry = l.Y2 - l.Y1;
                double templl = crx * crx + cry * cry;
                if (templl > 0)
                {
                    double templl2 = (gx - vx) * crx + (gy - vy) * cry;
                    double f = templl2 / templl;
                    gx = crx * f + vx;
                    gy = cry * f + vy;
                }
                double tempint = crx * mvx + cry * mvy;
                for (int i = cnt + 1; i <= tries; i++)
                {
                    var lj = _clipLines[hitwalls[i]];
                    double tempint2 = (lj.X2 - lj.X1) * mvx + (lj.Y2 - lj.Y1) * mvy;
                    if ((tempint < 0) != (tempint2 < 0))
                    {
                        ClipMoveCompat(x, y, z, ref sect);
                        return ret;
                    }
                }
                KeepAway(ref gx, ref gy, hitwall);
                xv = gx - vx;
                yv = gy - vy;
                if (cnt == tries)
                {
                    ret = l.Kind;
                    hitWall = l.Wall;
                    hitSprite = l.Spr;
                }
                hitwalls[cnt] = hitwall;
            }
            x = vx;
            y = vy;
            cnt--;
        }
        while ((Math.Abs(xv) > 1e-9 || Math.Abs(yv) > 1e-9) && hitwall >= 0 && cnt > 0);
        ClipMoveCompat(x, y, z, ref sect);
        return ret;
    }

    /// <summary>clipmove_compat: the first sector looked at that holds (x, y), else of all the one whose column is
    /// nearest z (inside it: that one; b3d_fixblood: the start's neighbours first); -1 if none holds it.</summary>
    void ClipMoveCompat(double x, double y, double z, ref int sect)
    {
        foreach (int s in _clipSects)
            if (InsideB(s, x, y))
            {
                sect = s;
                return;
            }
        double best = double.MaxValue;
        int start = _clipSects.Count > 0 ? _clipSects[0] : -1;
        sect = -1;
        var q = QOf(x, y, z);
        // b3d_fixblood: Blood's own order (FindSector: the sector it started in, its neighbours, then every one), not
        // every sector from the last. A dude standing on a turning platform's edge from the sector beside it, the
        // platform turned under its middle (whose walls then face away: never looked at), stepped onto it and was put
        // in a room sharing that ground at the same heights, far from both (reports 20261105-095023, -105957: the
        // room over the yard)
        int found = FixBlood && start >= 0 ? Board.UpdateSectorZ(x, y, z, start) : FindSectorZ(-1, q, q.Z, q.Z);
        if (found >= 0)
        {
            sect = found;
            return;
        }
        for (int j = Map.SectorCount - 1; j >= 0; j--)
        {
            if (!InsideB(j, x, y))
                continue;
            ZsOfSlope(j, x, y, out double cz, out double fz);
            double d = cz - z;
            if (d > 0)
            {
                if (d < best)
                {
                    sect = j;
                    best = d;
                }
            }
            else
            {
                d = z - fz;
                if (d <= 0)
                {
                    sect = j;
                    return;
                }
                if (d < best)
                {
                    sect = j;
                    best = d;
                }
            }
        }
    }

    // ---------------------------------------------------------------- pushmove
    /// <summary>pushmove: a sprite stuck in a wall (a moving sector's) pushed out of it, a unit at a time; -1 when it
    /// can't be (Blood crushes it).</summary>
    int PushMoveB(ref double x, ref double y, double z, ref int sect, double walldist, double ceildist, double flordist)
    {
        if (sect < 0)
            return -1;
        int k = 32, dir = 1, bad;
        do
        {
            bad = 0;
            _clipSects.Clear();
            _clipSectSet.Clear();
            AddClipSect(sect);
            for (int c = 0; c < _clipSects.Count; c++)
            {
                var walls = Map.SectorWalls(_clipSects[c]);
                for (int n = 0; n < walls.Count; n++)
                {
                    int i = walls[dir > 0 ? n : walls.Count - 1 - n];
                    WallNow(i, out double x1, out double y1, out double x2, out double y2);
                    if (ClipInsideBoxLine(x, y, x1, y1, x2, y2, walldist - 4) != 1)
                        continue;
                    bool j;
                    int next = Map.WallNext(i);
                    if (next < 0 || (Map.WallCstat(i) & ClipMask) != 0)
                        j = true;
                    else
                    {
                        double dax = x2 - x1, day = y2 - y1, daz = dax * (x - x1) + day * (y - y1);
                        double t = daz <= 0 ? 0 : daz >= dax * dax + day * day ? 1 : daz / (dax * dax + day * day);
                        j = ClipTestSector(_clipSects[c], next, flordist, ceildist, x1 + dax * t, y1 + day * t, z);
                    }
                    if (j)
                    {
                        double ang = Blood.GetAngle(x2 - x1, y2 - y1);
                        double dx = Math.Floor(Sin14(ang + 1024) / 2048), dy = Math.Floor(Sin14(ang + 512) / 2048);
                        for (int bad2 = 16; bad2 > 0; bad2--)
                        {
                            x += dx;
                            y += dy;
                            WallNow(i, out x1, out y1, out x2, out y2);
                            if (ClipInsideBoxLine(x, y, x1, y1, x2, y2, walldist - 4) == 0)
                                break;
                        }
                        bad = -1;
                        if (--k <= 0)
                            return bad;
                        sect = UpdateSector(sect, QOf(x, y, z), QOf(x, y, z), QOf(x, y, z).Z, QOf(x, y, z).Z);
                    }
                    else if (next >= 0)
                        AddClipSect(next);
                }
            }
            dir = -dir;
        }
        while (bad != 0);
        return bad;
    }

    // ---------------------------------------------------------------- getzrange
    /// <summary>getzrange: the lowest ceiling and highest floor (Build z) around (x, y, z) within walldist, of the
    /// sectors reachable there and the blocking sprites; hits: (Hit.Sector, sector) or (Hit.Sprite, sprite).</summary>
    void GetZRangeB(Sprite self, double x, double y, double z, int sect, double walldist, out double ceilz, out int ceilSect,
        out Sprite ceilSpr, out double florz, out int florSect, out Sprite florSpr)
    {
        ceilSpr = florSpr = null;
        if (sect < 0)
        {
            ceilz = double.MinValue;
            florz = double.MaxValue;
            ceilSect = florSect = -1;
            return;
        }
        double extra = walldist + MaxClipDist + 1;
        double xmin = x - extra, ymin = y - extra, xmax = x + extra, ymax = y + extra;
        ZsOfSlope(sect, x, y, out ceilz, out florz);
        ceilSect = florSect = sect;
        _clipSects.Clear();
        _clipSectSet.Clear();
        AddClipSect(sect);
        for (int c = 0; c < _clipSects.Count && c < 256; c++)
        {
            foreach (int j in Map.SectorWalls(_clipSects[c]))
            {
                int k = Map.WallNext(j);
                if (k < 0)
                    continue;
                WallNow(j, out double x1, out double y1, out double x2, out double y2);
                if ((x1 < xmin && x2 < xmin) || (x1 > xmax && x2 > xmax) || (y1 < ymin && y2 < ymin) || (y1 > ymax && y2 > ymax))
                    continue;
                double dx = x2 - x1, dy = y2 - y1;
                if (dx * (y - y1) < (x - x1) * dy)
                    continue;      // back
                double dax = dx > 0 ? dx * (ymin - y1) : dx * (ymax - y1);
                double day = dy > 0 ? dy * (xmax - x1) : dy * (xmin - x1);
                if (dax >= day)
                    continue;
                if ((Map.WallCstat(j) & ClipMask) != 0)
                    continue;
                ZsOfSlope(k, x, y, out double kc, out double kf);
                if (!Map.CeilSky(k) && z <= kc + (3 << 8))
                    continue;
                if (!Map.FloorSky(k) && z >= kf - (3 << 8))
                    continue;
                AddClipSect(k);
                if ((x1 < xmin + MaxClipDist && x2 < xmin + MaxClipDist) || (x1 > xmax - MaxClipDist && x2 > xmax - MaxClipDist)
                    || (y1 < ymin + MaxClipDist && y2 < ymin + MaxClipDist) || (y1 > ymax - MaxClipDist && y2 > ymax - MaxClipDist))
                    continue;
                if (dx > 0) dax += dx * MaxClipDist; else dax -= dx * MaxClipDist;
                if (dy > 0) day -= dy * MaxClipDist; else day += dy * MaxClipDist;
                if (dax >= day)
                    continue;
                if (kc > ceilz)
                {
                    ceilz = kc;
                    ceilSect = k;
                }
                if (kf < florz)
                {
                    florz = kf;
                    florSect = k;
                }
            }
        }
        // sprites
        var v = new double[4];
        var w = new double[4];
        foreach (int s in _clipSects.ToArray())
            foreach (var e in ClipSprites(s, self))
            {
                double daz = 0, daz2 = 0;
                bool clipyou = false;
                switch (e.Cstat & Cstat.AlignMask)
                {
                    case 0:
                    {
                        double k = walldist + e.ClipDist * 4 + 1;
                        if (Math.Abs(e.BX - x) <= k && Math.Abs(e.BY - y) <= k)
                        {
                            (daz, daz2) = SpriteSpan(e);
                            clipyou = true;
                        }
                        break;
                    }
                    case Cstat.Wall:
                    {
                        WallSpritePoints(e, out double x1, out double y1, out double x2, out double y2);
                        if (ClipInsideBoxLine(x, y, x1, y1, x2, y2, walldist + 1) != 0)
                        {
                            (daz, daz2) = SpriteSpan(e);
                            clipyou = true;
                        }
                        break;
                    }
                    case Cstat.Slope:
                    case Cstat.Floor:
                    {
                        daz = daz2 = e.ZOfSlope(x, y);
                        FloorSpritePoints(e, x, y, v, w);
                        if (e.SlopeNow != 0)
                        {
                            // a sloped one: the highest and lowest of it under the mover's box, within the sprite's own
                            double slMin = e.BZ, slMax = e.BZ;
                            foreach (int ci in new[] { 0, 2 })
                            {
                                double slz = e.ZOfSlope(v[ci] + x, w[ci] + y);
                                slMin = Math.Min(slMin, slz);
                                slMax = Math.Max(slMax, slz);
                            }
                            double slWd = walldist + 4;
                            foreach (var (ox, oy) in new[] { (slWd, slWd), (slWd, -slWd), (-slWd, slWd), (-slWd, -slWd) })
                            {
                                double cornerZ = e.ZOfSlope(x + ox, y + oy);
                                daz = Math.Max(daz, cornerZ);
                                daz2 = Math.Min(daz2, cornerZ);
                            }
                            daz = Math.Clamp(daz, slMin, slMax);
                            daz2 = Math.Clamp(daz2, slMin, slMax);
                        }
                        if ((e.Cstat & Cstat.OneSided) != 0 && (z > daz) == ((e.Cstat & Cstat.YFlip) == 0))
                            break;
                        double dax = Sin14(e.Ang - 256 + 512) * (walldist + 4) / 16384, day = Sin14(e.Ang - 256) * (walldist + 4) / 16384;
                        v[0] += dax; v[1] -= day; v[2] -= dax; v[3] += day;
                        w[0] += day; w[1] += dax; w[2] -= day; w[3] -= dax;
                        clipyou = FloorSpriteClipYou(v, w);
                        break;
                    }
                }
                if (!clipyou)
                    continue;
                if (z > daz && daz > ceilz)
                {
                    ceilz = daz;
                    ceilSpr = e;
                }
                if (z < daz2 && daz2 < florz)
                {
                    florz = daz2;
                    florSpr = e;
                }
            }
        if (ceilSpr != null)
            ceilSect = -1;
        if (florSpr != null)
            florSect = -1;
    }

    /// <summary>get_floorspr_clipyou: does the quad (relative to the point) hold the origin?</summary>
    static bool FloorSpriteClipYou(double[] x, double[] y)
    {
        int clipyou = 0;
        for (int i = 0; i < 4; i++)
        {
            int j = (i + 1) & 3;
            if ((y[i] < 0) != (y[j] < 0))
            {
                if ((x[i] < 0) != (x[j] < 0))
                    clipyou ^= ((x[i] * y[j] < x[j] * y[i]) ^ (y[i] < y[j])) ? 1 : 0;
                else if (x[i] >= 0)
                    clipyou ^= 1;
            }
        }
        return clipyou != 0;
    }

    // ---------------------------------------------------------------- Blood's wrappers
    /// <summary>The sim's object standing for a Build sector's floor (its mover where it moves) or ceiling.</summary>
    XObject PlaneObject(int s, bool ceiling)
    {
        if (s < 0 || s >= _xsector.Length)
            return null;
        return (ceiling ? _ceilMover[s] : _floorMover[s]) ?? _wholeBy[s] ?? (XObject)_xsector[s];
    }

    /// <summary>The link on sector s's floor (dir 1: gUpperLink) or ceiling (-1: gLowerLink), or null.</summary>
    Collision.Link SectorLink(int s, int dir)
    {
        foreach (var l in Collision.Links)
            if (l.Sector == s && l.Dir == dir)
                return l;
        return null;
    }

    /// <summary>Blood's GetZRange on Build's sectors: getzrange, a sky floor or ceiling none (unless clipParallax),
    /// a floor Depth lower, and through a link the floor or ceiling of the room beyond.</summary>
    ZRange GetZRangeSectors(Sprite e, double nDist, bool clipParallax) =>
        GetZRangeAt(e, e.BX, e.BY, e.BZ, SpriteBSector(e), nDist, clipParallax);

    /// <summary>GetZRangeAtXYZ: Blood's GetZRange at a point in sector sect (Build units; self: not clipped against).</summary>
    ZRange GetZRangeAt(Sprite self, double x, double y, double z, int sect, double nDist, bool clipParallax)
    {
        var e = self;
        GetZRangeB(e, x, y, z, sect, nDist, out double cz, out int cs, out var cspr, out double fz, out int fs, out var fspr);
        var r = new ZRange { CeilZ = cz, FloorZ = fz, FloorNormal = new Vector3(0, 0, 1) };
        if (fspr != null)
        {
            r.FloorHit = Hit.Sprite;
            r.FloorEnt = fspr;
            r.FloorSurf = Tiles.SurfType(fspr.Picnum);
        }
        else if (fs >= 0)
        {
            r.FloorHit = Hit.Sector;
            r.FloorEnt = PlaneObject(fs, false);
            r.FloorSurf = Tiles.SurfType(Map.FloorPic(fs));
            r.FloorNormal = PlaneNormal(fs, x, y, false);
            if (!clipParallax && Map.FloorSky(fs))
                r.FloorZ = 2147483647;
            if (XSectorOf(fs) is { } xs && xs.SDepth != 0)
                r.FloorZ += xs.SDepth << 10;
            if (SectorLink(fs, 1) is { } l && Map.Valid(l.FarSector))
            {
                double ox = l.Offset.X * 16.0, oy = -l.Offset.Y * 16.0, oz = -l.Offset.Z * 256.0;
                GetZRangeB(e, x + ox, y + oy, z + oz, l.FarSector, nDist, out _, out _, out _, out double f2, out int fs2, out var fspr2);
                r.FloorZ = f2 - oz;
                r.FloorEnt = fspr2 ?? (object)PlaneObject(fs2, false);
                r.FloorHit = fspr2 != null ? Hit.Sprite : Hit.Sector;
            }
        }
        if (cspr != null)
        {
            r.CeilHit = Hit.Sprite;
            r.CeilEnt = cspr;
        }
        else if (cs >= 0)
        {
            r.CeilHit = Hit.Sector;
            r.CeilEnt = PlaneObject(cs, true);
            r.CeilSurf = Tiles.SurfType(Map.CeilPic(cs));
            if (!clipParallax && Map.CeilSky(cs))
                r.CeilZ = -2147483647;
            if (SectorLink(cs, -1) is { } l && Map.Valid(l.FarSector))
            {
                double ox = l.Offset.X * 16.0, oy = -l.Offset.Y * 16.0, oz = -l.Offset.Z * 256.0;
                GetZRangeB(e, x + ox, y + oy, z + oz, l.FarSector, nDist, out double c2, out int cs2, out var cspr2, out _, out _, out _);
                r.CeilZ = c2 - oz;
                r.CeilEnt = cspr2 ?? (object)PlaneObject(cs2, true);
                r.CeilHit = cspr2 != null ? Hit.Sprite : Hit.Sector;
            }
        }
        return r;
    }

    /// <summary>
    /// MoveMissile on Build's sectors (actor.cpp): clipmove once from the missile's sector (its owner not in the way),
    /// a sprite hit 3, a wall 0 (4: a two-sided wall it passes at that height); a wall shot through (x-wall Vector,
    /// not cstat 64) lets it on once more; off a wall or plane it backs off 16 units; then getzrange for the floor (2)
    /// or ceiling (1) it reached, its sector updated and CheckLink. The gHitInfo fields (HitType, HitEnt, HitSurf,
    /// HitPos, HitNormal) say what it hit. -1: nothing (or no sector holds it: it stays).
    /// </summary>
    int MoveMissileSectors(Sprite e)
    {
        int vdi = -1;
        HitEnt = null;
        HitType = -1;
        var owner = e.Owner != null && !e.Owner.Removed && Info.IsDude(e.Owner.Type) ? e.Owner : null;
        int bakCstat = owner?.Cstat ?? 0;
        if (owner != null)
            owner.Cstat &= ~257;
        double vx = Blood.Sar(e.XVel, 12), vy = Blood.Sar(e.YVel, 12), vz = Blood.Sar(e.ZVel, 8);
        var (top, bottom) = Extents(e);
        int tries = 1;
        while (true)
        {
            double x = e.BX, y = e.BY, z = e.BZ;
            int s2 = SpriteBSector(e);
            _insideDude = FixBlood;
            int vdx = ClipMoveB(e, ref x, ref y, z, ref s2, vx, vy, e.ClipDist * 4, (z - top) / 4, (bottom - z) / 4,
                out int hitWall, out var hitSpr, 1);
            _insideDude = false;
            if (s2 < 0)
            {
                vdi = -1;
                break;
            }
            int nSector = s2;
            if (vdx == Hit.Sprite)
            {
                vdi = 3;
                HitEnt = hitSpr;
                HitSurf = Tiles.SurfType(hitSpr.Picnum);
            }
            else if (vdx == Hit.Wall && hitWall >= 0)
            {
                int next = Map.WallNext(hitWall);
                vdi = 0;
                if (next >= 0)
                {
                    ZsOfSlope(next, x, y, out double cz, out double fz);
                    if (z > cz && z < fz)
                        vdi = 4;
                }
                HitEnt = (XObject)_xwall[hitWall] ?? XSectorOf(next);
                HitSurf = Tiles.SurfType(Map.WallPic(hitWall));
                HitNormal = WallNormal(hitWall);
                if (vdi == 4 && _xwall[hitWall] is { } xw && xw.HasX && xw.XVector != 0)
                {
                    TrTriggerWall(xw, K.kCmdWallImpact, (XObject)owner ?? e);
                    if ((Map.WallCstat(hitWall) & 64) == 0)
                    {
                        vdi = -1;
                        if (tries-- > 0)
                            continue;
                        vdi = 0;
                        break;
                    }
                }
            }
            if (vdi >= 0 && vdi != 3)
            {
                int nAngle = Blood.GetAngle(e.XVel, e.YVel);
                x -= Blood.MulScale30(Blood.Cos(nAngle), 16);
                y -= Blood.MulScale30(Blood.Sin(nAngle), 16);
                double nVel = Blood.ApproxDist(e.XVel, e.YVel);
                if (nVel != 0)
                    vz -= Blood.Scale3(256, e.ZVel, nVel);
                nSector = UpdateSector(nSector, QOf(x, y, z), QOf(x, y, z), QOf(x, y, z).Z, QOf(x, y, z).Z);
                s2 = nSector;
            }
            var zr = GetZRangeAt(e, x, y, z, s2, e.ClipDist * 4, false);
            (top, bottom) = Extents(e);
            top += vz;
            bottom += vz;
            if (bottom >= zr.FloorZ)
            {
                e.FlorHit = zr.FloorHit;
                e.FlorHitEnt = zr.FloorEnt;
                vz += zr.FloorZ - bottom;
                vdi = 2;
                HitEnt = zr.FloorEnt as XObject;
                HitSurf = zr.FloorSurf;
                HitNormal = zr.FloorNormal;
            }
            if (top <= zr.CeilZ)
            {
                e.CeilHit = zr.CeilHit;
                e.CeilHitEnt = zr.CeilEnt;
                vz += Math.Max(zr.CeilZ - top, 0);
                vdi = 1;
                HitEnt = zr.CeilEnt as XObject;
                HitSurf = zr.CeilSurf;
                HitNormal = new Vector3(0, 0, -1);
            }
            e.Origin = QOf(x, y, z + vz);
            SetSpriteBSector(e, UpdateSector(nSector, e.Origin, e.Origin, e.Origin.Z, e.Origin.Z));
            CheckLink(e);
            if (vdi == 3 || vdi == 0 || vdi == 4)
                HitType = vdi == 4 ? 0 : vdi;
            else
                HitType = vdi;
            HitX = e.BX;
            HitY = e.BY;
            HitZ = e.BZ;
            HitPos = e.Origin;
            break;
        }
        if (owner != null)
            owner.Cstat = bakCstat;
        return vdi;
    }

    /// <summary>A sector's floor (or ceiling) normal at (x, y) Build units, Quake space (up for a floor).</summary>
    Vector3 PlaneNormal(int s, double x, double y, bool ceiling)
    {
        ZsOfSlope(s, x - 16, y, out double c0, out double f0);
        ZsOfSlope(s, x + 16, y, out double c1, out double f1);
        ZsOfSlope(s, x, y - 16, out double c2, out double f2);
        ZsOfSlope(s, x, y + 16, out double c3, out double f3);
        // Quake z per Quake unit along Quake x and y (Build y runs the other way, z down)
        double dzdx = ceiling ? -(c1 - c0) / 256 / 2 : -(f1 - f0) / 256 / 2;
        double dzdy = ceiling ? (c3 - c2) / 256 / 2 : (f3 - f2) / 256 / 2;
        var n = new Vector3((float)-dzdx, (float)-dzdy, 1).Normalized();
        return ceiling ? -n : n;
    }

    /// <summary>Blood's ClipMove on Build's sectors: clipmove from the sprite's sector, then (a sliding or turning
    /// sector) pushmove; its sector follows; the position stays where no sector holds it.</summary>
    Physics.ClipResult ClipMoveSectors(Sprite e, double xv, double yv, double wd, double cd, double fd)
    {
        var res = new Physics.ClipResult();
        int sect = SpriteBSector(e);
        double x = e.BX, y = e.BY, z = e.BZ;
        int s2 = sect;
        int hit = ClipMoveB(e, ref x, ref y, z, ref s2, xv, yv, wd, cd, fd, out int hitWall, out var hitSpr);
        if (s2 < 0)
            return res;
        if (XSectorOf(s2) is { } xs && xs.Type >= K.kSectorPath && xs.Type <= K.kSectorRotate)
        {
            int s3 = s2;
            PushMoveB(ref x, ref y, z, ref s3, wd, cd, fd);
            if (s3 >= 0)
                s2 = s3;
        }
        e.Origin = QOf(x, y, z);
        SetSpriteBSector(e, s2);
        res.Hit = hit;
        if (hit == Hit.Sprite)
            res.Ent = hitSpr;
        else if (hit == Hit.Wall && hitWall >= 0)
        {
            // the wall's x-object, else the sector beyond (its Wallpush)
            res.Ent = (object)_xwall[hitWall] ?? XSectorOf(Map.WallNext(hitWall));
            res.Surf = Tiles.SurfType(Map.WallPic(hitWall));
        }
        if (hit == Hit.Wall && hitWall >= 0)
            res.Normal = WallNormal(hitWall);
        return res;
    }

    /// <summary>
    /// hitscan on Build's walls and planes as they are now (no sprites), from (x, y, z) in sector sect along (dx, dy, dz)
    /// (dz in xy scale: HitScan's dz &lt;&lt; 4), within range (xy): the sector walk stops at the first one-sided or
    /// blocking wall, two-sided wall whose opening the ray passes outside, floor or ceiling. 0: wall (hitWall), 1:
    /// ceiling, 2: floor (hitSect), -1: none in range. For what the brushes can't hold (a sliding sector below another's
    /// floor: DWE3M1's boat under the river, its switch panel in solid).
    /// </summary>
    public int BuildHitScan(int sect, double x, double y, double z, double dx, double dy, double dz, double range,
        out int hitWall, out int hitSect, out double hx, out double hy, out double hz)
    {
        hitWall = -1;
        hitSect = sect;
        hx = x;
        hy = y;
        hz = z;
        double vz = dz * 16, len = Math.Sqrt(dx * dx + dy * dy);
        if (Map == null || !Map.Valid(sect) || len == 0)
            return -1;
        double tMax = range / len, t0 = 0;
        for (int step = 0; step < 64 && Map.Valid(sect); step++)
        {
            // the wall the ray leaves the sector by
            double tw = double.MaxValue;
            int w = -1;
            foreach (int wi in Map.SectorWalls(sect))
            {
                WallNow(wi, out double x1, out double y1, out double x2, out double y2);
                double ex = x2 - x1, ey = y2 - y1, den = dx * ey - dy * ex;
                if (Math.Abs(den) < 1e-9)
                    continue;
                double t = ((x1 - x) * ey - (y1 - y) * ex) / den;
                double u = ((x1 - x) * dy - (y1 - y) * dx) / den;
                if (t > t0 + 1e-9 && u >= 0 && u <= 1 && t < tw)
                    (tw, w) = (t, wi);
            }
            double tEnd = Math.Min(tw, tMax);
            // its floor or ceiling first (the plane under the ray is straight: their difference changes linearly)
            ZsOfSlope(sect, x + dx * t0, y + dy * t0, out double c0, out double f0);
            ZsOfSlope(sect, x + dx * tEnd, y + dy * tEnd, out double c1, out double f1);
            double za = z + vz * t0, zb = z + vz * tEnd;
            if (zb > f1 || zb < c1)
            {
                bool floor = zb > f1;
                double da = floor ? za - f0 : c0 - za, db = floor ? zb - f1 : c1 - zb;
                double t = da >= 0 || db == da ? t0 : t0 + (tEnd - t0) * (-da / (db - da));
                (hx, hy, hz, hitSect) = (x + dx * t, y + dy * t, z + vz * t, sect);
                return floor ? 2 : 1;
            }
            if (w < 0 || tw > tMax)
                return -1;
            (hx, hy, hz) = (x + dx * tw, y + dy * tw, z + vz * tw);
            int next = Map.WallNext(w);
            bool blocks = next < 0 || (Map.WallCstat(w) & ClipMask) != 0;
            if (!blocks)
            {
                ZsOfSlope(next, hx, hy, out double nc, out double nf);
                blocks = hz < nc || hz > nf;
            }
            if (blocks)
            {
                (hitWall, hitSect) = (w, sect);
                return 0;
            }
            (sect, t0) = (next, tw);
        }
        return -1;
    }

    /// <summary>A Build wall's x-object (blood_wall), or null.</summary>
    public Wall XWallOf(int w) => w >= 0 && w < _xwall.Length ? _xwall[w] : null;

    /// <summary>GetWallNormal: a wall's normal into its sector (Quake space), what a hit off it bounces from.</summary>
    Vector3 WallNormal(int w)
    {
        WallNow(w, out double x1, out double y1, out double x2, out double y2);
        // Build's front is to the right of p1-p2 in Build x, y (y down the map): (-dy, dx); Quake flips y
        double nx = -(y2 - y1), ny = x2 - x1, len = Math.Sqrt(nx * nx + ny * ny);
        return len > 0 ? new Vector3((float)(nx / len), (float)(-ny / len), 0) : Vector3.Zero;
    }
}
