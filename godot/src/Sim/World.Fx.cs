using System;
using Chosen.Data;
using Chosen.Sim.Collision;
using Godot;
using static Chosen.Data.GenTables;
using Chosen.Shared.Blood.Map;

namespace Chosen.Sim;

/// <summary>Effect sprites (fx.cpp) and gibs (gib.cpp), via Blood3D's server/fx.qc.</summary>
public sealed partial class World
{
    // FXDATA: funcID, detail, seq, flags, gravity, airdrag, duration, picnum, xrepeat, yrepeat, cstat, shade, pal
    public const int FXD_funcID = 0, FXD_detail = 1, FXD_seq = 2, FXD_flags = 3, FXD_gravity = 4, FXD_airdrag = 5,
        FXD_duration = 6, FXD_picnum = 7, FXD_xrepeat = 8, FXD_yrepeat = 9, FXD_cstat = 10, FXD_shade = 11, FXD_pal = 12;

    static int FxData(int n, int f) => tbl_fx[n * tbl_fx_STRIDE + f];

    int _fxCount;
    public bool AdultContentLock;        // gbAdultContent: the parental lock (no blood or gore)

    void FxKill(Sprite e)
    {
        if (e == null || e.Removed)
            return;
        EvKill(e);
        Seq.Kill(e);
        Delete(e);
    }

    void FxFree(Sprite e)
    {
        if (e == null || e.Removed)
            return;
        Seq.Kill(e);
        if (e.StatNum != K.kStatFree)
            ActPostSprite(e, K.kStatFree);
    }

    bool NoGore(int nFx) =>
        AdultContentLock && GameType == K.kGameTypeSinglePlayer
        && (nFx == K.FX_0 || nFx == K.FX_1 || nFx == K.FX_2 || nFx == K.FX_3 || nFx == K.FX_13 || nFx == K.FX_34 || nFx == K.FX_35 || nFx == K.FX_36);

    /// <summary>fxSpawn(nFx, x, y, z, duration): an effect sprite (0: the effect's own duration).</summary>
    public Sprite FxSpawn(int nFx, double x, double y, double z, int duration)
    {
        if (nFx < 0 || nFx >= K.kFXMax)
            return null;
        if (NoGore(nFx))
            return null;
        if (_fxCount >= 512)
        {
            Sprite old = null;
            foreach (var s in Sprites)
                if (s.StatNum == K.kStatFX && !s.Removed && (s.HitTag & 32) == 0)
                {
                    old = s;
                    break;
                }
            if (old == null)
                return null;
            FxKill(old);
            _fxCount--;
        }
        var e = ActSpawnSprite(x, y, z, K.kStatFX, false);
        e.ClassName = "fx";
        _fxCount++;
        e.Type = e.InitType = nFx;
        e.Picnum = FxData(nFx, FXD_picnum);
        e.Cstat |= FxData(nFx, FXD_cstat);
        e.Shade = FxData(nFx, FXD_shade);
        e.Pal = FxData(nFx, FXD_pal);
        if (FxData(nFx, FXD_xrepeat) > 0)
            e.XRepeat = FxData(nFx, FXD_xrepeat);
        if (FxData(nFx, FXD_yrepeat) > 0)
            e.YRepeat = FxData(nFx, FXD_yrepeat);
        if ((FxData(nFx, FXD_flags) & 1) != 0 & Chance(32768))
            e.Cstat ^= 4;
        if ((FxData(nFx, FXD_flags) & 2) != 0 & Chance(32768))
            e.Cstat ^= 8;
        if (FxData(nFx, FXD_seq) != 0)
        {
            DbInsertXSprite(e);
            Seq.Spawn(this, FxData(nFx, FXD_seq), e);
        }
        if (duration == 0)
            duration = FxData(nFx, FXD_duration);
        if (duration != 0)
            EvPostCallback(e, duration + Random2(duration >> 1), K.kCallbackRemove);
        e.Visible = (e.Cstat & Cstat.Invisible) == 0;
        return e;
    }

    void FxProcess()
    {
        _fxCount = 0;
        foreach (var e in StatList(K.kStatFX))
        {
            _fxCount++;
            if (e.Removed || (e.HitTag & 32) != 0)
                continue;
            ActAirDrag(e, FxData(e.Type, FXD_airdrag));
            NativeFxMove(e);
        }
    }

    /// <summary>fxProcess's move of one effect on a level from its Build data: by its velocity; past its floor (or out of every
    /// sector) its callback or gone, under a non-sky ceiling gone; then gravity.</summary>
    void NativeFxMove(Sprite e)
    {
        var b = Board;
        int ns = SpriteBSector(e);
        if (ns < 0)
        {
            FxFree(e);
            return;
        }
        double x = e.BX, y = e.BY, z = e.BZ;
        if (e.XVel != 0)
            x += Blood.Sar(e.XVel, 12);
        if (e.YVel != 0)
            y += Blood.Sar(e.YVel, 12);
        if (e.ZVel != 0)
            z += Blood.Sar(e.ZVel, 8);
        e.Origin = B2Q(x, y, z);
        int nFunc = FxData(e.Type, FXD_funcID);
        // (Blood's own test: an x speed, or a y speed below the floor)
        if (e.XVel != 0 || (e.YVel != 0 && z >= b.Sectors[ns].FloorZ))
        {
            int s2 = b.UpdateSector(x, y, ns);
            if (s2 < 0)
            {
                FxFree(e);
                return;
            }
            if (b.FloorZAt(ns, x, y) <= z)
            {
                e.FlorSurf = Tiles.SurfType(b.Sectors[ns].FloorPicnum);
                if (nFunc < 0)
                    FxFree(e);
                else
                    EvCallback(nFunc, e);
                return;
            }
            if (s2 != ns)
            {
                SetSpriteBSector(e, s2);
                ns = s2;
            }
        }
        if (e.XVel != 0 || e.YVel != 0 || e.ZVel != 0)
        {
            b.GetZsOfSlope(ns, x, y, out double cz, out double fz);
            if (cz > z && (b.Sectors[ns].CeilingStat & 1) == 0)
            {
                FxFree(e);
                return;
            }
            if (fz < z)
            {
                e.FlorSurf = Tiles.SurfType(b.Sectors[ns].FloorPicnum);
                if (nFunc < 0)
                    FxFree(e);
                else
                    EvCallback(nFunc, e);
                return;
            }
        }
        e.ZVel += FxData(e.Type, FXD_gravity);
    }

    public void FxSpawnBlood(Sprite e, double a2)
    {
        if (AdultContentLock && GameType == K.kGameTypeSinglePlayer)
            return;
        var b = FxSpawn(K.FX_27, e.BX, e.BY, e.BZ, 0);
        if (b != null)
        {
            b.Ang = 1024;
            b.XVel = Random2(436906);
            b.YVel = Random2(436906);
            b.ZVel = -Random(1092266) - 100;
            EvPostCallback(b, 8, K.kCallbackFXBloodSpurt);
        }
    }

    void FxSpawnPodBlood(Sprite e, double a2)
    {
        if (AdultContentLock && GameType == K.kGameTypeSinglePlayer)
            return;
        var b = FxSpawn(e.Type == K.kDudePodGreen ? K.FX_53 : K.FX_54, e.BX, e.BY, e.BZ, 0);
        if (b != null)
        {
            b.Ang = 1024;
            b.XVel = Random2(436906);
            b.YVel = Random2(436906);
            b.ZVel = -Random(1092266) - 100;
            EvPostCallback(b, 8, K.kCallbackFXPodBloodSpray);
        }
    }

    void FxSpawnEjecting(Sprite e, double z, double a3, double a4, int fxBase, int zr)
    {
        double x = e.BX + Math.Floor((e.ClipDist - 4) * Blood.Cos(e.Ang) / 268435456);
        double y = e.BY + Math.Floor((e.ClipDist - 4) * Blood.Sin(e.Ang) / 268435456);
        x += Blood.MulScale30(a3, Blood.Cos(e.Ang + 512));
        y += Blood.MulScale30(a3, Blood.Sin(e.Ang + 512));
        var b = FxSpawn(fxBase + Random(3), x, y, z, 0);
        if (b != null)
        {
            b.Ang = Random(2047);
            double nDist = Blood.Int(a4 * 262144 / 120) + Random2((int)Blood.Int(Blood.Int(a4 / 4) * 262144 / 120));
            double nAngle = e.Ang + Random2(56) + 512;
            b.XVel = Blood.MulScale30(nDist, Blood.Cos(nAngle));
            b.YVel = Blood.MulScale30(nDist, Blood.Sin(nAngle));
            b.ZVel = e.ZVel - (131072 + Blood.Int(Random2(zr) * 262144.0 / 120));
        }
    }

    void FxSpawnEjectingBrass(Sprite e, double z, double a3, double a4) => FxSpawnEjecting(e, z, a3, a4, K.FX_37, 40);
    void FxSpawnEjectingShell(Sprite e, double z, double a3, double a4) => FxSpawnEjecting(e, z, a3, a4, K.FX_40, 20);

    // ---------------------------------------------------------------- gibs
    int ChanceToCount(int a1, int a2)
    {
        int vb = a2;
        if (a1 < 65536)
            for (int i = 0; i < a2; i++)
                if (!Chance(a1))
                    vb--;
        return vb;
    }

    // position/velocity overrides for GibSprite (CGibPosition / CGibVelocity)
    bool _gibUsePos, _gibUseVel;
    double _gibPosX, _gibPosY, _gibPosZ, _gibVelX, _gibVelY, _gibVelZ;

    static int Scaled(double v) => (int)Blood.Int(v * 262144 / 120);

    void GibFX(Sprite e, int g, double x, double y, double z, double dz1, double dz2, double top, double bottom)
    {
        int fxid = tbl_gibfx[g * 6], at1 = tbl_gibfx[g * 6 + 1], chance = tbl_gibfx[g * 6 + 2];
        int at9 = tbl_gibfx[g * 6 + 3], atd = tbl_gibfx[g * 6 + 4], at11 = tbl_gibfx[g * 6 + 5];
        if (AdultContentLock && GameType == K.kGameTypeSinglePlayer && fxid == K.FX_13)
            return;
        int nCount = ChanceToCount(chance, at9);
        double gx = x, gy = y, gz = z;
        for (int i = 0; i < nCount; i++)
        {
            if (!_gibUsePos && (e.Cstat & 48) == 0)
            {
                int nAngle = Random(2048);
                gx = e.BX + Blood.MulScale30(e.ClipDist * 4, Blood.Cos(nAngle));
                gy = e.BY + Blood.MulScale30(e.ClipDist * 4, Blood.Sin(nAngle));
                gz = bottom - Random((int)(bottom - top));
            }
            var fx = FxSpawn(fxid, gx, gy, gz, 0);
            if (fx == null)
                continue;
            if (at1 < 0)
                fx.Pal = e.Pal;
            if (_gibUseVel)
            {
                fx.XVel = _gibVelX + Random2(atd);
                fx.YVel = _gibVelY + Random2(atd);
                fx.ZVel = _gibVelZ - Random(at11);
            }
            else
            {
                fx.XVel = Random2(Scaled(atd));
                fx.YVel = Random2(Scaled(atd));
                if ((e.Cstat & 48) == 16)
                    fx.ZVel = Random2(Scaled(at11));
                else if (dz2 < dz1 && dz2 < 16384)
                    fx.ZVel = 0;
                else if (dz2 > dz1 && dz1 < 16384)
                    fx.ZVel = -Random(Scaled(Math.Abs(at11)));
                else
                {
                    int v = Scaled(at11);
                    fx.ZVel = v < 0 ? -Random(Scaled(Math.Abs(at11))) : Random2(v);
                }
            }
        }
    }

    (double down, double up) FloorCeilDist(Sprite e, double x, double y, double z)
    {
        int ns = SpriteBSector(e);
        if (ns < 0)
            return (0, 0);
        Board.GetZsOfSlope(ns, x, y, out double cz, out double fz);
        return (fz - z, z - cz);
    }

    void GibThing(Sprite e, int g, double top, double bottom)
    {
        int thingType = tbl_gibthing[g * 5], pic = tbl_gibthing[g * 5 + 1], chance = tbl_gibthing[g * 5 + 2];
        int atc = tbl_gibthing[g * 5 + 3], at10 = tbl_gibthing[g * 5 + 4];
        if (AdultContentLock && GameType == K.kGameTypeSinglePlayer && (thingType == K.kThingBloodBits || thingType == K.kThingZombieHead))
            return;
        if (!(chance == 65536 | Chance(chance)))
            return;
        double x, y, z;
        if (!_gibUsePos)
        {
            int nAngle = Random(2048);
            x = e.BX + Blood.MulScale30(e.ClipDist * 4, Blood.Cos(nAngle));
            y = e.BY + Blood.MulScale30(e.ClipDist * 4, Blood.Sin(nAngle));
            z = bottom - Random((int)(bottom - top));
        }
        else
        {
            x = _gibPosX;
            y = _gibPosY;
            z = _gibPosZ;
        }
        var (dz1, dz2) = FloorCeilDist(e, x, y, z);
        var gib = ActSpawnThing(x, y, z, thingType);
        if (pic > -1)
        {
            gib.Picnum = pic;
            UpdateBox(gib);
        }
        if (_gibUseVel)
        {
            gib.XVel = _gibVelX + Random2(atc);
            gib.YVel = _gibVelY + Random2(atc);
            gib.ZVel = _gibVelZ - Random(at10);
        }
        else
        {
            gib.XVel = Random2(Scaled(atc));
            gib.YVel = Random2(Scaled(atc));
            if ((e.Cstat & 48) == 16)
                gib.ZVel = Random2(Scaled(at10));
            else if (dz2 < dz1 && dz2 < 16384)
                gib.ZVel = 0;
            else if (dz2 > dz1 && dz1 < 16384)
                gib.ZVel = -Random(Scaled(at10));
            else
                gib.ZVel = Random2(Scaled(at10));
        }
    }

    void GibSpriteInternal(Sprite e, int nGibType)
    {
        if (nGibType < 0 || nGibType >= tbl_giblist_COUNT)
            return;
        int fxStart = tbl_giblist[nGibType * 5], fxCount = tbl_giblist[nGibType * 5 + 1];
        int thStart = tbl_giblist[nGibType * 5 + 2], thCount = tbl_giblist[nGibType * 5 + 3];
        var (top, bottom) = Extents(e);
        double x = _gibUsePos ? _gibPosX : e.BX, y = _gibUsePos ? _gibPosY : e.BY, z = _gibUsePos ? _gibPosZ : e.BZ;
        var (dz1, dz2) = FloorCeilDist(e, x, y, z);
        for (int i = 0; i < fxCount; i++)
            GibFX(e, fxStart + i, x, y, z, dz1, dz2, top, bottom);
        for (int i = 0; i < thCount; i++)
            GibThing(e, thStart + i, top, bottom);
    }

    public void GibSprite(Sprite e, int nGibType)
    {
        _gibUsePos = false;
        _gibUseVel = false;
        GibSpriteInternal(e, nGibType);
    }

    public void GibSpriteAt(Sprite e, int nGibType, double x, double y, double z, double vx, double vy, double vz)
    {
        _gibUsePos = true;
        _gibPosX = x; _gibPosY = y; _gibPosZ = z;
        _gibUseVel = true;
        _gibVelX = vx; _gibVelY = vy; _gibVelZ = vz;
        GibSpriteInternal(e, nGibType);
        _gibUsePos = false;
        _gibUseVel = false;
    }

    /// <summary>GibWall for breakable brush entities: gibs spread over the brush's bounds.</summary>
    void GibBrush(Wall brush, int nGibType)
    {
        if (nGibType < 0 || nGibType >= tbl_giblist_COUNT)
            return;
        if (Board != null && brush.BWall >= 0 && brush.BWall < Board.WallCount)
        {
            GibWall(brush.BWall, nGibType);
            return;
        }
        GibBrushBox(brush, nGibType);
    }

    /// <summary>GibWall: a gib wall's pieces over its face between the two sectors' floors and ceilings at its middle,
    /// the sound there; a piece of an fx with a negative pal flag takes the wall's palette (GibFX for walls).</summary>
    void GibWall(int nWall, int nGibType)
    {
        var b = Board;
        var pWall = b.Walls[nWall];
        var p2 = b.Walls[pWall.Point2];
        int cx = (pWall.X + p2.X) >> 1, cy = (pWall.Y + p2.Y) >> 1;
        int nSector = pWall.Sector;
        b.GetZsOfSlope(nSector, cx, cy, out double ceilZ, out double floorZ);
        if (pWall.NextSector >= 0)
        {
            b.GetZsOfSlope(pWall.NextSector, cx, cy, out double ceilZ2, out double floorZ2);
            ceilZ = Math.Max(ceilZ, ceilZ2);
            floorZ = Math.Min(floorZ, floorZ2);
        }
        int wz = (int)(floorZ - ceilZ), wx = p2.X - pWall.X, wy = p2.Y - pWall.Y;
        double cz = Math.Floor((ceilZ + floorZ) / 2);
        int fxStart = tbl_giblist[nGibType * 5], fxCount = tbl_giblist[nGibType * 5 + 1], snd = tbl_giblist[nGibType * 5 + 4];
        if (snd > 0)
            Sound3DAt(B2Q(cx, cy, cz), snd);
        for (int i = 0; i < fxCount; i++)
        {
            int g = fxStart + i;
            int fxid = tbl_gibfx[g * 6], at1 = tbl_gibfx[g * 6 + 1], chance = tbl_gibfx[g * 6 + 2], at9 = tbl_gibfx[g * 6 + 3];
            int atd = tbl_gibfx[g * 6 + 4], at11 = tbl_gibfx[g * 6 + 5];
            int nCount = ChanceToCount(chance, at9);
            for (int j = 0; j < nCount; j++)
            {
                int r1 = Random(wz), r2 = Random(wy), r3 = Random(wx);
                var fx = FxSpawn(fxid, pWall.X + r3, pWall.Y + r2, ceilZ + r1, 0);
                if (fx == null)
                    continue;
                if (at1 < 0)
                    fx.Pal = pWall.Pal;
                fx.XVel = Random2(Scaled(atd));
                fx.YVel = Random2(Scaled(atd));
                fx.ZVel = -Random(Scaled(at11));
            }
        }
    }

    void GibBrushBox(Wall brush, int nGibType)
    {
        int fxStart = tbl_giblist[nGibType * 5], fxCount = tbl_giblist[nGibType * 5 + 1], snd = tbl_giblist[nGibType * 5 + 4];
        var b = brush.Bounds;
        if (snd > 0)
            Sound3DAt(b.GetCenter(), snd);
        for (int i = 0; i < fxCount; i++)
        {
            int g = fxStart + i;
            int fxid = tbl_gibfx[g * 6], chance = tbl_gibfx[g * 6 + 2], at9 = tbl_gibfx[g * 6 + 3];
            int atd = tbl_gibfx[g * 6 + 4], at11 = tbl_gibfx[g * 6 + 5];
            int nCount = ChanceToCount(chance, at9);
            for (int j = 0; j < nCount; j++)
            {
                var p = new Vector3(
                    b.Position.X + WRand() / 32768f * b.Size.X,
                    b.Position.Y + WRand() / 32768f * b.Size.Y,
                    b.Position.Z + WRand() / 32768f * b.Size.Z);
                var fx = FxSpawn(fxid, Q2BX(p), Q2BY(p), Q2BZ(p), 0);
                if (fx != null)
                {
                    fx.XVel = Random2(Scaled(atd));
                    fx.YVel = Random2(Scaled(atd));
                    fx.ZVel = -Random(Scaled(at11));
                }
            }
        }
    }
}
