using System;
using System.Collections.Generic;
using Chosen.Data;
using Chosen.Sim.Collision;
using Godot;

namespace Chosen.Sim;

/// <summary>
/// Build's queries on the brush world (Blood3D's server/engine.qc): line of sight (cansee), hitscans that test face
/// sprites by their picture like Build (HitScan, VectorScan), and the classification of what a trace hit (gHitInfo).
/// </summary>
public sealed partial class World
{
    // gHitInfo: what the last hitscan hit
    public int HitType = -1;              // 0 wall, 1 ceiling, 2 floor, 3 sprite, -1 nothing
    public XObject HitEnt;                // the sprite, or the brush entity (door, wall) hit; null for the world
    public Vector3 HitPos, HitNormal;     // Quake space
    public double HitX, HitY, HitZ;       // Build space
    public int HitSurf;

    public const int MaskNoMonsters = Contents.MaskMove & ~Contents.Sprites;

    public static Vector3 B2Q(double x, double y, double z) =>
        new((float)(x / Blood.XYScale), (float)(-y / Blood.XYScale), (float)(-z / Blood.ZScale));
    public static double Q2BX(Vector3 v) => v.X * Blood.XYScale;
    public static double Q2BY(Vector3 v) => -v.Y * Blood.XYScale;
    public static double Q2BZ(Vector3 v) => -v.Z * Blood.ZScale;
    /// <summary>bdir2q: a Build-space direction (dz already in xy scale) as a Quake vector.</summary>
    public static Vector3 BDir2Q(double dx, double dy, double dz) => new((float)dx, (float)-dy, (float)-dz);

    /// <summary>texToTile: the Blood tile a texture shows (textures/blood*/NNNN...), -1 if none.</summary>
    public static int TexToTile(string tex)
    {
        if (string.IsNullOrEmpty(tex))
            return -1;
        int slash = tex.LastIndexOf('/');
        string folder = slash > 0 ? tex[..slash] : "";
        if (folder.StartsWith("textures/"))
            folder = folder[9..];
        if (folder != "blood" && folder != "blood_masked" && folder != "blood_oneway" && folder != "blood_fxhide" && folder != "blood_flat"
            && folder != "blood_above" && folder != "blood_below")
            return -1;
        int i = slash + 1;
        if (tex.Length < i + 4)
            return -1;
        return int.TryParse(tex.AsSpan(i, 4), out int t) ? t : -1;
    }

    public static int TexSurfType(string tex)
    {
        int t = TexToTile(tex);
        return t < 0 ? K.kSurfStone : Tiles.SurfType(t);
    }

    /// <summary>classifyTrace: what a trace hit, into the gHitInfo fields. Returns HitType.</summary>
    public int ClassifyTrace(in TraceResult tr)
    {
        HitPos = tr.EndPos;
        HitX = Q2BX(tr.EndPos);
        HitY = Q2BY(tr.EndPos);
        HitZ = Q2BZ(tr.EndPos);
        HitNormal = tr.Normal;
        HitEnt = null;
        if (tr.Fraction >= 1 && !tr.StartSolid)
            return HitType = -1;
        if (tr.Entity is Sprite s)
        {
            HitEnt = s;
            HitSurf = Tiles.SurfType(s.Picnum);
            return HitType = 3;
        }
        HitEnt = tr.Entity as XObject;      // brush entity (door etc.)
        string tex = tr.Texture;
        HitSurf = (tr.Contents & Contents.Sky) != 0 ? K.kSurfNone : TexSurfType(tex);
        if (tr.Normal.Z > 0.7f)
            HitType = 2;
        else if (tr.Normal.Z < -0.7f)
            HitType = 1;
        else
            HitType = 0;
        return HitType;
    }

    /// <summary>cansee: world-only line of sight (doors and brush entities block too); an end lying on a wall face
    /// counts as seen. Build's cansee walks two-sided walls only and knows nothing of Blood's link markers, so a link
    /// side's slab (botclip) blocks too: nothing sees or hears through room-over-room or water links (E1M6's room 256
    /// lies just over the shaft's ceiling link).</summary>
    public bool QCanSee(Vector3 a, Vector3 b)
    {
        double ax = Q2BX(a), ay = Q2BY(a), az = Q2BZ(a), bx = Q2BX(b), by = Q2BY(b), bz = Q2BZ(b);
        return NativeCanSee(ax, ay, az, SectorOfPointB(ax, ay, az), bx, by, bz, SectorOfPointB(bx, by, bz));
    }

    /// <summary>cansee between two places, each in the sector of a sprite there (null: looked up).</summary>
    public bool CanSee(double x1, double y1, double z1, double x2, double y2, double z2, Sprite s1 = null, Sprite s2 = null)
    {
        // Blood's cansee takes the sprites' sectors; a point without one is looked up
        int a = s1 != null ? SpriteBSector(s1) : SectorOfPointB(x1, y1, z1);
        int c = s2 != null ? SpriteBSector(s2) : SectorOfPointB(x2, y2, z2);
        return NativeCanSee(x1, y1, z1, a, x2, y2, z2, c);
    }

    /// <summary>HitScan(pSprite, z, dx, dy, dz, nMask, nRange): from the sprite's x, y at Build z. mask: MaskShot for
    /// CLIPMASK1 (hitscan sprites, cstat 256), MaskBlock for CLIPMASK0 (blocking sprites, cstat 1).</summary>
    public int HitScan(Sprite e, double z, double dx, double dy, double dz, double range, int mask = Contents.MaskShot)
    {
        return NativeHitScan(e, z, dx, dy, dz, range, mask == Contents.MaskShot ? 64 : 1, mask == Contents.MaskShot ? 256 : 1);
    }

    /// <summary>VectorScan(pSprite, nOffset, nZOffset, dx, dy, dz, nRange).</summary>
    public int VectorScan(Sprite e, double nOffset, double nZOffset, double dx, double dy, double dz, double range)
    {
        return NativeVectorScan(e, nOffset, nZOffset, dx, dy, dz, range);
    }

    /// <summary>Debugging (the hitscan console command): a weapon's hitscan from the player along the aim, 16384 long;
    /// what it hit and where (Quake units). Leaves the game's hit globals set, as a shot would.</summary>
    public int DebugHitscan(Player p, out string what, out Vector3 at, bool up = false, bool fire = false)
    {
        if (fire)
            ActFireVector(p, 0, p.ZWeapon - p.BZ, up ? 0 : p.AimDx, up ? 0 : p.AimDy, up ? -16384 : p.AimDz, K.kVectorTommyRegular);
        int r = up ? VectorScan(p, 0, p.ZWeapon - p.BZ, 0, 0, -16384, 16384)
            : VectorScan(p, 0, p.ZWeapon - p.BZ, p.AimDx, p.AimDy, p.AimDz, 16384);
        what = HitEnt?.ToString() ?? "world";
        if (HitEnt is Sprite s)
            what += $" type {s.Type} pic {s.Picnum} cstat {s.Cstat}";
        at = B2Q(HitX, HitY, HitZ);
        return r;
    }

    /// <summary>CheckProximity: within nDist (Blood units/16 xy, /256 z) and in sight.</summary>
    public bool CheckProximity(Sprite e, double nX, double nY, double nZ, double nDist)
    {
        double oX = Blood.Sar(Math.Abs(nX - e.BX), 4);
        if (oX >= nDist) return false;
        double oY = Blood.Sar(Math.Abs(nY - e.BY), 4);
        if (oY >= nDist) return false;
        double oZ = Blood.Sar(Math.Abs(nZ - e.BZ), 8);
        if (oZ >= nDist) return false;
        if (Blood.ApproxDist(oX, oY) >= nDist) return false;
        var (top, bottom) = Physics.SpriteExtents(e.BZ, e.Picnum, e.YRepeat, e.Cstat);
        if (CanSee(e.BX, e.BY, e.BZ, nX, nY, nZ, e)) return true;
        if (CanSee(e.BX, e.BY, bottom - 256, nX, nY, nZ, e)) return true;
        if (CanSee(e.BX, e.BY, top + 256, nX, nY, nZ, e)) return true;
        return false;
    }

    public static bool CheckProximityPoint(double nX1, double nY1, double nZ1, double nX2, double nY2, double nZ2, double nDist)
    {
        double oX = Blood.Sar(Math.Abs(nX2 - nX1), 4);
        if (oX >= nDist) return false;
        double oY = Blood.Sar(Math.Abs(nY2 - nY1), 4);
        if (oY >= nDist) return false;
        if (nZ2 != nZ1 && Blood.Sar(Math.Abs(nZ2 - nZ1), 8) >= nDist) return false;
        return Blood.ApproxDist(oX, oY) < nDist;
    }

    /// <summary>DANGLE: the signed difference of two Build angles (-1024..1023).</summary>
    public static int DAngle(double a1, double a2) => (int)(Blood.AngMask(a1 + 1024 - a2) - 1024);
}
