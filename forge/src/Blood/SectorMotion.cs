// Chosen Blood module (GPL v2): where Blood's moving sectors are between off and on (NBlood triggers.cpp: trInit,
// TranslateSector, ZTranslateSector, VSpriteBusy, GetWaveValue), for the editor's Off / On views and the 3D preview's
// Play. A sliding or turning sector's map stores it ON (trInit takes it back to OFF first, then to its state).
using System;
using System.Collections.Generic;
using System.Linq;
using Chosen.Forge.Document;
using Chosen.Shared.Blood.Map;
using Chosen.Shared.Engine.Geometry;

namespace Chosen.Forge.Blood;

public static class SectorMotion
{
    public const int ZMotion = 600, ZMotionSprite = 602, Path = 612, RotateStep = 613, SlideMarked = 614, RotateMarked = 615,
        Slide = 616, Rotate = 617;

    /// <summary>Does sector s move between an off and an on place (its type, the x-sector, the markers it needs)?</summary>
    public static bool IsMover(MapDocument d, int s)
    {
        if (s < 0 || s >= d.Sectors.Count || d.Sectors[s].Xs is not { } x)
            return false;
        switch (d.Sectors[s].Type)
        {
            case ZMotion:
            case ZMotionSprite:
                return x.OnFloorZ != x.OffFloorZ || x.OnCeilZ != x.OffCeilZ;
            case SlideMarked:
            case Slide:
                return Marker(d, x.Marker0) && Marker(d, x.Marker1);
            case RotateMarked:
            case Rotate:
                return Marker(d, x.Marker0);
        }
        return false;
    }

    static bool Marker(MapDocument d, int i) => i >= 0 && i < d.Sprites.Count;

    /// <summary>The sectors whose type moves their floor and ceiling between Off and On heights (with an x-sector),
    /// moving already or not yet (the heights alike).</summary>
    public static List<int> HeightMovers(MapDocument d) => Enumerable.Range(0, d.Sectors.Count)
        .Where(s => d.Sectors[s].Xs != null && d.Sectors[s].Type is ZMotion or ZMotionSprite or SlideMarked or RotateMarked or Slide or Rotate)
        .ToList();

    /// <summary>The sectors that move.</summary>
    public static List<int> Movers(MapDocument d) => Enumerable.Range(0, d.Sectors.Count).Where(s => IsMover(d, s)).ToList();

    /// <summary>GetWaveValue: how far along (0..1) a busy t (0..1) is by the sector's wave (0 eased both ends, 1 even,
    /// 2 eased in, 3 eased out).</summary>
    public static double Wave(double t, int wave) => wave switch
    {
        0 => 0.5 - Math.Cos(t * Math.PI) / 2,
        1 => t,
        2 => 1 - Math.Cos(t * Math.PI / 2),
        3 => Math.Sin(t * Math.PI / 2),
        _ => t,
    };

    /// <summary>
    /// The shown map with every mover where busy puts it (0 off, 1 on, through the wave already; null: as the map
    /// stores it), from the stored map: shown is a copy of stored (the same walls, sectors and sprites), changed only
    /// in the movers' walls (and the points they share with their neighbours), floors, ceilings and the sprites
    /// that ride on them, each set afresh from stored (a pose a frame: no drift).
    /// </summary>
    public static void Pose(MapDocument stored, MapDocument shown, Func<int, double?> busy, IEnumerable<int> movers = null)
    {
        foreach (int s in movers ?? Movers(stored))
        {
            if (busy(s) is not { } t)
                continue;
            var sec = stored.Sectors[s];
            var x = sec.Xs;
            switch (sec.Type)
            {
                case SlideMarked:
                case Slide:
                {
                    var m0 = stored.Sprites[x.Marker0];
                    var m1 = stored.Sprites[x.Marker1];
                    Translate(stored, shown, s, t, m0.X, m0.Y, m0.Ang, m1.X, m1.Y, m1.Ang, sec.Type == Slide);
                    break;
                }
                case RotateMarked:
                case Rotate:
                {
                    var m0 = stored.Sprites[x.Marker0];
                    Translate(stored, shown, s, t, m0.X, m0.Y, 0, m0.X, m0.Y, m0.Ang, sec.Type == Rotate);
                    break;
                }
            }
            ZTranslate(stored, shown, s, t);
        }
    }

    // ---------------------------------------------------------------- sliding and turning

    /// <summary>TranslateSector as trInit and the busy functions use it: the stored places (ON) taken back to OFF
    /// (t -1 of the way), then t of the way to ON; pivot the first marker's place; angles in Build's 2048.</summary>
    public static void Translate(MapDocument stored, MapDocument shown, int s, double t, int x0, int y0, int a0, int x1, int y1, int a1, bool allWalls)
    {
        (double x, double y) Move(double px, double py, double f, bool reverse)
        {
            double cx = x0 + (x1 - x0) * f, cy = y0 + (y1 - y0) * f, ang = a0 + (a1 - a0) * f;
            if (reverse)
            {
                var (rx, ry) = RotatePoint(px, py, -ang, x0, y0);
                return (rx - (cx - x0), ry - (cy - y0));
            }
            var (fx, fy) = RotatePoint(px, py, ang, x0, y0);
            return (fx + cx - x0, fy + cy - y0);
        }
        (int x, int y) Place(double px, double py, bool reverse)
        {
            var b = Move(px, py, -1, reverse);
            var p = Move(b.x, b.y, t, reverse);
            return ((int)Math.Round(p.x), (int)Math.Round(p.y));
        }
        var sec = stored.Sectors[s];
        void Drag(int w, bool reverse)
        {
            var (px, py) = Place(stored.Walls[w].X, stored.Walls[w].Y, reverse);
            int point = w < shown.PointOf.Length ? shown.PointOf[w] : -1;
            if (point >= 0)
                shown.MovePoint(point, px, py);
        }
        for (int w = sec.WallPtr; w < sec.WallPtr + sec.WallNum; w++)
        {
            var wall = stored.Walls[w];
            if (allWalls)
            {
                Drag(w, false);
                continue;
            }
            bool fwd = (wall.Cstat & 16384) != 0, rev = !fwd && (wall.Cstat & 32768) != 0;
            if (!fwd && !rev)
                continue;
            Drag(w, rev);
            // (the wall's end too, when that wall isn't marked itself)
            int p2 = wall.Point2;
            if (p2 >= 0 && p2 < stored.Walls.Count && (stored.Walls[p2].Cstat & 49152) == 0)
                Drag(p2, rev);
        }
        // the sprites in it that move with it (8192), against it (16384), or (Drag) stand on its floor; not markers
        int dAng = a1 - a0;
        for (int i = 0; i < stored.Sprites.Count; i++)
        {
            var sp = stored.Sprites[i];
            if (sp.SectNum != s || sp.StatNum == 10 || sp.StatNum == 16)
                continue;
            var to = shown.Sprites[i];
            if ((sp.Cstat & 8192) != 0 || (sp.Cstat & 16384) != 0)
            {
                bool rev = (sp.Cstat & 8192) == 0;
                (to.X, to.Y) = Place(sp.X, sp.Y, rev);
                to.Ang = (short)(((sp.Ang + (rev ? -1 : 1) * (int)Math.Round(dAng * (t - 1))) % 2048 + 2048) % 2048);
            }
            else if (sec.Xs.Drag != 0 && (sp.Cstat & 48) == 0 && OnFloor(stored, s, sp))
            {
                // carried by the floor: the sector's own move from OFF (t 0) to t, about where the pivot is then
                var b = DragMove(sp.X, sp.Y, 0, -1, x0, y0, a0, x1, y1, a1);
                var p = DragMove(b.x, b.y, 0, t, x0, y0, a0, x1, y1, a1);
                (to.X, to.Y) = ((int)Math.Round(p.x), (int)Math.Round(p.y));
                to.Ang = (short)(((sp.Ang + (int)Math.Round(dAng * (t - 1))) % 2048 + 2048) % 2048);
            }
        }
    }

    /// <summary>TranslateSector from the places the map stores (a path sector's, a step-turning one's: trInit leaves
    /// those where they are): every wall (and the sprites moving with it: 8192, against it: 16384) turned by ang about
    /// the pivot and moved by (cx, cy) less the pivot.</summary>
    public static void TranslateStored(MapDocument stored, MapDocument shown, int s, double pivotX, double pivotY, double cx, double cy, double ang)
    {
        (int x, int y) Place(double px, double py, bool reverse)
        {
            var (rx, ry) = RotatePoint(px, py, reverse ? -ang : ang, pivotX, pivotY);
            double dx = cx - pivotX, dy = cy - pivotY;
            return reverse ? ((int)Math.Round(rx - dx), (int)Math.Round(ry - dy)) : ((int)Math.Round(rx + dx), (int)Math.Round(ry + dy));
        }
        var sec = stored.Sectors[s];
        for (int w = sec.WallPtr; w < sec.WallPtr + sec.WallNum; w++)
        {
            var (px, py) = Place(stored.Walls[w].X, stored.Walls[w].Y, false);
            int point = w < shown.PointOf.Length ? shown.PointOf[w] : -1;
            if (point >= 0)
                shown.MovePoint(point, px, py);
        }
        int turn = (int)Math.Round(ang);
        for (int i = 0; i < stored.Sprites.Count; i++)
        {
            var sp = stored.Sprites[i];
            if (sp.SectNum != s || sp.StatNum == 10 || sp.StatNum == 16 || (sp.Cstat & (8192 | 16384)) == 0)
                continue;
            bool rev = (sp.Cstat & 8192) == 0;
            var to = shown.Sprites[i];
            (to.X, to.Y) = Place(sp.X, sp.Y, rev);
            to.Ang = (short)(((sp.Ang + (rev ? -turn : turn)) % 2048 + 2048) % 2048);
        }
    }

    // ---------------------------------------------------------------- slopes

    /// <summary>AlignSlopes (NBlood triggers.cpp, every game frame): a sector whose filler byte (the map's FogPal)
    /// names one of its walls tilts its floor and ceiling about its first wall to meet the next sector's floor and
    /// ceiling at that wall's middle (alignflorslope, alignceilslope). The sectors whose slopes changed.</summary>
    public static List<int> AlignSlopes(MapDocument d)
    {
        var changed = new List<int>();
        for (int s = 0; s < d.Sectors.Count; s++)
        {
            var sec = d.Sectors[s];
            int filler = sec.FogPal;
            if (filler <= 0 || filler >= sec.WallNum)
                continue;
            var w = d.Walls[sec.WallPtr + filler];
            int ns = w.NextSector;
            if (ns < 0 || ns >= d.Sectors.Count || w.Point2 < 0 || w.Point2 >= d.Walls.Count)
                continue;
            var w2 = d.Walls[w.Point2];
            int x = (w.X + w2.X) / 2, y = (w.Y + w2.Y) / 2;
            bool a = AlignSlope(d, s, x, y, (int)BuildGeom.PlaneZ(d, ns, false, x, y), false);
            bool b = AlignSlope(d, s, x, y, (int)BuildGeom.PlaneZ(d, ns, true, x, y), true);
            if (a || b)
                changed.Add(s);
        }
        return changed;
    }

    /// <summary>alignflorslope / alignceilslope: the plane's heinum so it passes through (x, y, z), turning about the
    /// sector's first wall (its height there kept). Did it change?</summary>
    static bool AlignSlope(MapDocument d, int s, int x, int y, int z, bool ceiling)
    {
        var sec = d.Sectors[s];
        var w = d.Walls[sec.WallPtr];
        var w2 = d.Walls[w.Point2];
        long dax = w2.X - w.X, day = w2.Y - w.Y;
        long i = (long)(y - w.Y) * dax - (long)(x - w.X) * day;
        if (i == 0)
            return false;
        long len = (long)Math.Floor(Math.Sqrt((double)(dax * dax + day * day)));
        int baseZ = ceiling ? sec.CeilingZ : sec.FloorZ;
        int heinum = (int)((((long)(z - baseZ) << 8) * len) / i);
        int stat = ceiling ? sec.CeilingStat : sec.FloorStat;
        int was = ceiling ? sec.CeilingHeinum : sec.FloorHeinum;
        stat = heinum == 0 ? stat & ~2 : stat | 2;
        if (ceiling)
        {
            sec.CeilingHeinum = (short)heinum;
            sec.CeilingStat = (short)stat;
        }
        else
        {
            sec.FloorHeinum = (short)heinum;
            sec.FloorStat = (short)stat;
        }
        return heinum != was;
    }

    /// <summary>A sprite dragged on a moving floor from f0 to f1 of the way: turned about where the pivot is at f0 by
    /// the angle between, moved by the pivot's move.</summary>
    static (double x, double y) DragMove(double px, double py, double f0, double f1, int x0, int y0, int a0, int x1, int y1, int a1)
    {
        double ax = x0 + (x1 - x0) * f0, ay = y0 + (y1 - y0) * f0, bx = x0 + (x1 - x0) * f1, by = y0 + (y1 - y0) * f1;
        double turn = (a1 - a0) * (f1 - f0);
        var (rx, ry) = RotatePoint(px, py, turn, ax, ay);
        return (rx + bx - ax, ry + by - ay);
    }

    /// <summary>RotatePoint: (x, y) turned by Build angle ang (2048 a full turn, y down) about (ox, oy).</summary>
    public static (double x, double y) RotatePoint(double x, double y, double ang, double ox, double oy)
    {
        double a = ang * Math.PI / 1024, c = Math.Cos(a), s = Math.Sin(a), dx = x - ox, dy = y - oy;
        return (ox + dx * c - dy * s, oy + dx * s + dy * c);
    }

    // ---------------------------------------------------------------- up and down

    /// <summary>ZTranslateSector: the floor and ceiling t of the way from their OFF to their ON heights (where those
    /// differ), what rests on the floor (or moves with it: 8192) and what hangs from the ceiling (16384) with them. A
    /// z-motion sprite sector (602) keeps its planes as the level starts and moves its sprites alone (VSpriteBusy).</summary>
    public static void ZTranslate(MapDocument stored, MapDocument shown, int s, double t, XSector heights = null)
    {
        var sec = stored.Sectors[s];
        var x = heights ?? sec.Xs;
        var to = shown.Sectors[s];
        int dfz = x.OnFloorZ - x.OffFloorZ, dcz = x.OnCeilZ - x.OffCeilZ;
        bool spritesOnly = sec.Type == ZMotionSprite;
        double planeT = spritesOnly ? (sec.Xs.State != 0 ? 1 : 0) : t;
        int floor = dfz != 0 ? (int)Math.Round(x.OffFloorZ + dfz * planeT) : sec.FloorZ;
        int ceil = dcz != 0 ? (int)Math.Round(x.OffCeilZ + dcz * planeT) : sec.CeilingZ;
        to.FloorZ = floor;
        to.CeilingZ = ceil;
        int floorBy = floor - sec.FloorZ, ceilBy = ceil - sec.CeilingZ;
        // (602: the sprites by the busy, from where the level starts them)
        int spriteFloorBy = spritesOnly ? floorBy + (int)Math.Round(dfz * (t - planeT)) : floorBy;
        int spriteCeilBy = spritesOnly ? ceilBy + (int)Math.Round(dcz * (t - planeT)) : ceilBy;
        if (dfz == 0 && dcz == 0)
            return;
        for (int i = 0; i < stored.Sprites.Count; i++)
        {
            var sp = stored.Sprites[i];
            if (sp.SectNum != s || sp.StatNum == 10 || sp.StatNum == 16)
                continue;
            int z = sp.Z;
            if (dfz != 0 && ((sp.Cstat & 8192) != 0 || (!spritesOnly && (sp.Cstat & 48) == 0 && OnFloor(stored, s, sp))))
                z += spriteFloorBy;
            if (dcz != 0 && (sp.Cstat & 16384) != 0)
                z += spriteCeilBy;
            shown.Sprites[i].Z = z;
        }
    }

    /// <summary>Does a sprite stand on sector s's floor (its bottom at or under the floor as stored)?</summary>
    static bool OnFloor(MapDocument d, int s, BSprite sp)
    {
        double floor = BuildGeom.PlaneZ(d, s, false, sp.X, sp.Y);
        double bottom = sp.Z;
        if ((sp.Cstat & 128) != 0)
            bottom += Data.TileImages.Size(sp.Picnum).Y * sp.YRepeat * 2.0;
        return bottom >= floor - 256;
    }
}
