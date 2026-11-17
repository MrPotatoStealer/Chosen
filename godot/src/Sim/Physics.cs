using System;
using System.Collections.Generic;
using Chosen.Sim.Collision;
using Godot;

namespace Chosen.Sim;

/// <summary>Floor and ceiling around an actor (GetZRange): Build z of each (z down) and what they are.</summary>
public struct ZRange
{
    public double CeilZ, FloorZ;
    public int CeilHit, FloorHit;
    public object CeilEnt, FloorEnt;
    public int FloorSurf, CeilSurf;
    public Vector3 FloorNormal;
}

/// <summary>
/// Build's movement and range queries on the brush collision, ported from Blood3D's QuakeC (server/engine.qc):
/// GetSpriteExtents, GetZRange (getzrange), ClipMove (clipmove with Build's pushmove), and the bounce vectors.
/// Room-over-room links are not handled yet (linkClampBox, traceFollowLinks).
/// </summary>
public static class Physics
{
    public const int SurfStone = 1, SurfWater = 5, SurfLava = 4;     // Blood surface types used here

    /// <summary>Top and bottom (Build z) of an upright sprite's picture (db.h GetSpriteExtents).</summary>
    public static (double top, double bottom) SpriteExtents(double z, int picnum, int yrepeat, int cstat)
    {
        if ((cstat & Cstat.AlignMask) == Cstat.Floor)
            return (z, z);
        double h = Data.Tiles.H(picnum);
        double centre = Math.Floor(h / 2) + Data.Tiles.YOfs(picnum);
        return (z - (yrepeat * 4) * centre, z + (yrepeat * 4) * (h - centre));
    }

    /// <summary>Blood's surface type of a texture (its tile's, from SURFACE.DAT); stone if it isn't a tile.</summary>
    public static int TexSurfType(string tex)
    {
        if (string.IsNullOrEmpty(tex))
            return SurfStone;
        int slash = tex.LastIndexOf('/');
        int i = slash + 1, n = 0, t = 0;
        while (i < tex.Length && char.IsDigit(tex[i]))
        {
            t = t * 10 + (tex[i] - '0');
            i++;
            n++;
        }
        return n == 0 ? SurfStone : Data.Tiles.SurfType(t);
    }

    static double Q2BZ(Vector3 v) => -v.Z * Blood.ZScale;

    // ---------------------------------------------------------------- GetZRange
    /// <summary>
    /// The floor and ceiling above and below an actor whose box is nDist (Build xy) around its origin. Mirrors
    /// engine.qc: blocking walls narrow the probe box, sprites and doors the box starts inside are left out (but a
    /// mover the actor fell into this frame is its floor), and a point inside geometry searches upward for room (one that
    /// rose into a ceiling this frame goes back down under it).
    /// </summary>
    /// <param name="clipParallax">NBlood's PARALLAXCLIP_CEILING|PARALLAXCLIP_FLOOR (MoveDude passes it): a sky ceiling
    /// or floor stops the sprite; without it (things, missiles, callbacks) a sky is no ceiling / no floor.</param>
    public static ZRange GetZRange(CollisionWorld cw, Vector3 origin, double nDist, double zvel, object self, bool clipParallax = false)
    {
        double r = nDist / Blood.XYScale;
        if (r < 0.5)
            r = 0.5;
        var mn = new Vector3((float)-r, (float)-r, 0);
        var mx = new Vector3((float)r, (float)r, 0);
        var start = origin;
        // blocking walls (clip brushes) only stop horizontal movement: narrow the probe until it stays on our side
        float sh;
        for (sh = 1f; sh > 0.03f; sh *= 0.5f)
        {
            var t0 = cw.Trace(start, mn * sh, mx * sh, start, Contents.PlayerClip | Contents.MonsterClip, self);
            if (!t0.StartSolid)
                break;
        }
        mn *= sh;
        mx *= sh;
        const int mask = Contents.MaskZRange;
        var exclude = new HashSet<object>();
        int k;
        TraceResult tr;
        for (k = 0; k < 6; k++)
        {
            tr = cw.Trace(start, mn, mx, start, mask, self, exclude);
            if (!tr.StartSolid || tr.Entity == null || tr.Entity == self)
                break;
            var ov = tr.Entity;
            if (ov is not Sprite)
            {
                // a mover is wall and floor in one: narrow the probe until it clears it
                float s2;
                for (s2 = 0.5f; s2 > 0.03f; s2 *= 0.5f)
                {
                    tr = cw.Trace(start, mn * s2, mx * s2, start, mask, self, exclude);
                    if (!tr.StartSolid || tr.Entity != ov)
                        break;
                }
                if (!tr.StartSolid || tr.Entity != ov)
                {
                    mn *= s2;
                    mx *= s2;
                    continue;
                }
                // the origin is inside it: fell through its top this frame?
                double sunk = SunkInto(cw, ov, start, zvel, self, exclude);
                if (sunk > 0)
                {
                    start.Z += (float)sunk;
                    continue;
                }
            }
            exclude.Add(ov);
        }
        // floor
        tr = cw.TraceZ(start, mn, mx, -8192, mask, self, exclude);
        if (tr.StartSolid)
        {
            for (float s3 = 0.5f; s3 >= 0.03f && tr.StartSolid; s3 *= 0.5f)
                tr = cw.TraceZ(start, mn * s3, mx * s3, -8192, mask, self, exclude);
        }
        if (tr.StartSolid)
        {
            // inside geometry (a sprite sunk into the floor): search upwards for open space, trace down from there
            int d;
            for (d = 4; d <= 128; d += 4)
            {
                var up = start + new Vector3(0, 0, d);
                tr = cw.Trace(up, mn, mx, up, mask, self, exclude);
                while (tr.StartSolid && tr.Entity != null && tr.Entity != self && k < 12)
                {
                    exclude.Add(tr.Entity);
                    k++;
                    tr = cw.Trace(up, mn, mx, up, mask, self, exclude);
                }
                if (!tr.StartSolid)
                    break;
            }
            tr = cw.Trace(start + new Vector3(0, 0, d), mn, mx, start - new Vector3(0, 0, 8192), mask, self, exclude);
        }
        var shift = cw.TraceFollowLinks(ref tr, mn, mx, 1, mask, self, exclude);
        float floorAt = AtCentre(cw, ref tr, start, mx, shift, -1, mask, self);
        var z = new ZRange();
        z.FloorZ = Q2BZ(new Vector3(0, 0, floorAt) - shift);
        z.FloorSurf = SurfStone;
        if (tr.Fraction >= 1)
        {
            z.FloorZ = 2147483647;
            z.FloorHit = 0;
        }
        else if ((tr.Contents & Contents.Sky) != 0 && !clipParallax)
        {
            z.FloorZ = 2147483647;
            z.FloorHit = Hit.Sector;
        }
        else if (tr.Entity is Sprite fs)
        {
            z.FloorHit = Hit.Sprite;
            z.FloorEnt = fs;
            z.FloorSurf = Data.Tiles.SurfType(fs.Picnum);
        }
        else
        {
            z.FloorHit = Hit.Sector;
            z.FloorEnt = tr.Entity;
            z.FloorSurf = TexSurfType(tr.Texture);
        }
        z.FloorNormal = tr.Normal;
        // ceiling
        tr = cw.TraceZ(start, mn, mx, 8192, mask, self, exclude);
        for (float s4 = 0.5f; s4 >= 0.03f && tr.StartSolid; s4 *= 0.5f)
            tr = cw.TraceZ(start, mn * s4, mx * s4, 8192, mask, self, exclude);
        shift = cw.TraceFollowLinks(ref tr, mn, mx, -1, mask, self, exclude);
        z.CeilZ = Q2BZ(tr.EndPos - shift);
        if (tr.Fraction >= 1)
        {
            z.CeilZ = -2147483647;
            z.CeilHit = 0;
        }
        else if ((tr.Contents & Contents.Sky) != 0 && !clipParallax)
        {
            z.CeilZ = -2147483647;
            z.CeilHit = Hit.Sector;
        }
        else if (tr.Entity is Sprite)
        {
            z.CeilHit = Hit.Sprite;
            z.CeilEnt = tr.Entity;
        }
        else
        {
            z.CeilHit = Hit.Sector;
            z.CeilEnt = tr.Entity;
            z.CeilSurf = TexSurfType(tr.Texture);
        }
        return z;
    }

    /// <summary>
    /// The height a vertical box trace (dir -1 down onto a floor, 1 up to a ceiling) gives the actor: Build's
    /// getzrange takes each sector's plane at the sprite's x, y (the neighbours' too, extended), where the box stops
    /// on a slope where it is highest within it. A sloped hit: its plane at the centre, or what lies straight above
    /// or below the centre when that is nearer (E2M6's underwater room beside the strip's floor sloping up: the box
    /// caught the slope's top 9 units above Build's, the ceiling pushed back, the swimmer bounced). Floors only: a head
    /// let up into a sloped ceiling puts ClipMove's box in it (Build's clipmove doesn't see its own sector's slopes;
    /// the box does, and pushmove shoved the player off E1M3's kitchen spot under its sloped ceiling), where feet let
    /// down into a sloped floor stay clear of the box (its bottom is a quarter of the way to them) on all but the
    /// steepest.
    /// </summary>
    static float AtCentre(CollisionWorld cw, ref TraceResult tr, Vector3 start, Vector3 mx, Vector3 shift, int dir, int mask,
        object self)
    {
        float z = tr.EndPos.Z;
        float nz = Mathf.Abs(tr.Normal.Z);
        if (tr.StartSolid || tr.Fraction >= 1 || shift != Vector3.Zero || tr.Entity is Sprite || nz < 0.05f || nz > 0.999f
            || tr.Normal.Z * dir > 0)
            return z;
        float rise = (Mathf.Abs(tr.Normal.X) * mx.X + Mathf.Abs(tr.Normal.Y) * mx.Y) / nz;
        float plane = z + dir * rise;
        var line = cw.TraceLine(start, start + new Vector3(0, 0, 8192 * dir), mask, self);
        // (a link side there is no floor or ceiling: the room beyond has it)
        if (line.Fraction < 1 && !line.StartSolid && cw.LinkAtPlane(line.EndPos, -dir) == null)
            plane = dir < 0 ? Mathf.Max(plane, line.EndPos.Z) : Mathf.Min(plane, line.EndPos.Z);
        return plane;
    }

    /// <summary>How far above start a falling actor's origin is free of mover m (0: not within this frame's fall).</summary>
    static double SunkInto(CollisionWorld cw, object m, Vector3 start, double zvel, object self, HashSet<object> exclude)
    {
        if (zvel <= 0)
            return 0;
        double lim = zvel / 65536 + 1;
        var tiny = new Vector3(0.1f, 0.1f, 0.1f);
        for (double d = 0.25; d <= lim; d += 0.25)
        {
            var p = start + new Vector3(0, 0, (float)d);
            var tr = cw.Trace(p, -tiny, tiny, p, Contents.MaskZRange, self, exclude);
            if (!tr.StartSolid || tr.Entity != m)
                return d + 0.125;
        }
        return 0;
    }

    // ---------------------------------------------------------------- ClipMove
    public struct ClipResult
    {
        public int Hit;
        public object Ent;
        public Vector3 Normal;
        public int Surf;
    }

    /// <summary>
    /// Moves the actor horizontally by (xv, yv) Build units, sliding along walls (clipmove). wd: wall distance (Build
    /// xy); cd/fd: the clip band above and below z (Build z). An actor stuck in geometry is pushed out first
    /// (pushmove).
    /// </summary>
    public static ClipResult ClipMove(CollisionWorld cw, ref Vector3 origin, double xv, double yv, double wd, double cd, double fd, object self)
    {
        double r = wd / Blood.XYScale;
        if (r < 0.5)
            r = 0.5;
        var mn = new Vector3((float)-r, (float)-r, (float)(-fd / Blood.ZScale));
        var mx = new Vector3((float)r, (float)r, (float)(cd / Blood.ZScale));
        if (mx.Z - mn.Z < 1)
        {
            mn.Z -= 0.5f;
            mx.Z += 0.5f;
        }
        (mn, mx) = cw.LinkClampBox(origin, mn, mx);
        var move = new Vector3((float)(xv / Blood.XYScale), (float)(-yv / Blood.XYScale), 0);
        var res = new ClipResult();
        const int noMonsters = Contents.MaskMove & ~Contents.Sprites;
        var t0 = cw.Trace(origin, mn, mx, origin, noMonsters, self);
        if (t0.StartSolid && !Unstick(cw, ref origin, mn, mx, self))
        {
            var p = origin + move;
            var t1 = cw.Trace(p, mn, mx, p, noMonsters, self);
            if (t1.StartSolid)
            {
                res.Hit = Hit.Wall;
                res.Normal = -move.Normalized();
                return res;
            }
        }
        for (int i = 0; i < 3; i++)
        {
            if (move.Length() < 0.001f)
                break;
            var start = origin;
            var tr = cw.Trace(start, mn, mx, start + move, Contents.MaskMove, self);
            if (tr.AllSolid)
            {
                if (i == 0 && Unstick(cw, ref origin, mn, mx, self))
                    continue;
                break;
            }
            origin = tr.EndPos;
            if (tr.Fraction >= 1)
                break;
            var n = tr.Normal;
            n.Z = 0;
            if (n.Length() < 0.01f)
                break;
            n = n.Normalized();
            if (res.Hit == 0)
            {
                if (tr.Entity is Sprite)
                    res.Hit = Hit.Sprite;
                else
                {
                    res.Hit = Hit.Wall;
                    res.Surf = TexSurfType(tr.Texture);
                }
                res.Ent = tr.Entity;
                res.Normal = n;
            }
            move *= 1 - tr.Fraction;
            move -= n * move.Dot(n);
        }
        return res;
    }

    /// <summary>Build's pushmove: to the nearest free spot around the actor that its centre reaches without crossing
    /// anything.</summary>
    static bool Unstick(CollisionWorld cw, ref Vector3 origin, Vector3 mn, Vector3 mx, object self)
    {
        var o = origin;
        for (int d = 1; d <= 16; d *= 2)
            for (int a = 0; a < 360; a += 45)
            {
                var p = o + new Vector3((float)(Math.Cos(a * Math.PI / 180) * d), (float)(Math.Sin(a * Math.PI / 180) * d), 0);
                var tl = cw.TraceLine(o, p, Contents.MaskMove, self);
                if (tl.StartSolid || tl.Fraction < 1)
                    continue;
                var tb = cw.Trace(p, mn, mx, p, Contents.MaskMove, self);
                if (tb.StartSolid)
                    continue;
                origin = p;
                return true;
            }
        return false;
    }

    /// <summary>actWallBounceVector: (xv, yv) bounced off the clip normal with elasticity a4 (16.16).</summary>
    public static (double x, double y) WallBounceVector(double xv, double yv, double a4, Vector3 clipNormal)
    {
        double nx = clipNormal.X, ny = -clipNormal.Y;
        double t = xv * nx + yv * ny;
        double t2 = Blood.MulScale16R(t, a4 + 65536);
        return (xv - nx * t2, yv - ny * t2);
    }

    /// <summary>actFloorBounceVector: the velocity bounced off the floor plane; returns the impact (fall damage).</summary>
    public static double FloorBounceVector(ref double xv, ref double yv, ref double zv, double a5, Vector3 floorNormal)
    {
        double t = 65536 - a5;
        if (floorNormal.Z > 0.999f || floorNormal == Vector3.Zero)
        {
            double t9 = Blood.MulScale16(zv, t);
            zv = -(zv - t9);
            return t9;
        }
        double nx = floorNormal.X, ny = -floorNormal.Y, nz = -floorNormal.Z;
        double t8 = xv * nx + yv * ny + zv * nz;
        double t9b = Blood.MulScale16(t8, 65536 + a5);
        xv -= nx * t9b;
        yv -= ny * t9b;
        zv -= nz * t9b;
        return Math.Abs(Blood.MulScale16R(t8, t));
    }
}
