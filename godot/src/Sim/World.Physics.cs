using System;
using System.Collections.Generic;
using Chosen.Data;
using Chosen.Sim.Collision;
using Godot;
using Chosen.Shared.Blood.Map;

namespace Chosen.Sim;

/// <summary>
/// Actor physics (actor.cpp, via Blood3D's server/actor_physics.qc): air drag, things' and dudes' movement with
/// Build's clipmove and getzrange, missiles, explosions, concussion and radius damage, water and links, touching.
/// </summary>
public sealed partial class World
{
    public const int DudeDrag = 10752;    // gDudeDrag (0x2a00, actor.cpp)

    // the last GetZRange / ClipMove (engine.qc's globals)
    ZRange _zr;
    Physics.ClipResult _clip;

    ZRange GetZRange(Sprite e, double nDist, bool clipParallax = false)
    {
        long t = Core.Perf.Start();
        // a converted level: Build's getzrange on its sectors (World.BuildClip); else the brushes
        _zr = UseSectorClip && SpriteBSector(e) >= 0 ? GetZRangeSectors(e, nDist, clipParallax)
            : Physics.GetZRange(Collision, e.Origin, nDist, e.ZVel, e, clipParallax);
        Core.Perf.Add("zrange", t);
        FloorNormal = _zr.FloorNormal;
        return _zr;
    }

    int ClipMove(Sprite e, double xv, double yv, double wd, double cd, double fd)
    {
        long t = Core.Perf.Start();
        if (UseSectorClip && SpriteBSector(e) >= 0)
        {
            // a converted level: Build's clipmove on its sectors (World.BuildClip)
            _clip = ClipMoveSectors(e, xv, yv, wd, cd, fd);
            Core.Perf.Add("clipmove", t);
            return _clip.Hit;
        }
        var o = e.Origin;
        _clip = Physics.ClipMove(Collision, ref o, xv, yv, wd, cd, fd, e);
        Core.Perf.Add("clipmove", t);
        e.Origin = o;
        return _clip.Hit;
    }

    (double x, double y) WallBounce(double xv, double yv, double a4) => Physics.WallBounceVector(xv, yv, a4, _clip.Normal);

    (double top, double bottom) Extents(Sprite e) => Physics.SpriteExtents(e.BZ, e.Picnum, e.YRepeat, e.Cstat);

    static double VelOfMoverZ(object mover) => 0;     // movers place themselves (no velocity), as in Blood3D

    // ---------------------------------------------------------------- actAirDrag
    void ActAirDrag(Sprite e, double a2)
    {
        double vbp = 0, v4 = 0;
        var sec = SpriteSector(e);
        if (sec != null && sec.WindVel != 0 && (sec.WindAlways != 0 || sec.XBusy != 0))
        {
            double vcx = sec.WindVel * 4096.0;
            if (sec.WindAlways == 0 && sec.XBusy != 0)
                vcx = Blood.MulScale16(vcx, sec.XBusy);
            vbp = Blood.MulScale30(vcx, Blood.Cos(sec.WindAng));
            v4 = Blood.MulScale30(vcx, Blood.Sin(sec.WindAng));
        }
        e.XVel += Blood.MulScale16(vbp - e.XVel, a2);
        e.YVel += Blood.MulScale16(v4 - e.YVel, a2);
        e.ZVel -= Blood.MulScale16(e.ZVel, a2);
    }

    // ---------------------------------------------------------------- ConcussSprite / actRadiusDamage
    void ConcussSprite(Sprite src, Sprite e, double x, double y, double z, double a6)
    {
        double dx = e.BX - x, dy = e.BY - y, dz = Blood.Sar(e.BZ - z, 4);
        double dist2 = 262144 + dx * dx + dy * dy + dz * dz;
        a6 = Blood.Scale3(262144, a6, dist2);
        if ((e.HitTag & K.kPhysMove) != 0)
        {
            int mass;
            if (Info.IsDude(e.Type))
                mass = DudeMass(e);
            else if (Info.IsThing(e.Type))
                mass = Info.Thing(e.Type, K.TI_mass);
            else
                return;
            if (mass <= 0)
                mass = 1;
            double size = Blood.Sar((double)Tiles.W(e.Picnum) * e.XRepeat * Tiles.H(e.Picnum) * e.YRepeat, 1);
            double t = Blood.Scale3(a6, size, mass);
            Log?.Append($" [concuss {e} from {src} a6 {a6} size {size} mass {mass} t {t} dx {dx} dy {dy} dz {dz} pic {e.Picnum} rep {e.XRepeat}x{e.YRepeat}]");
            e.XVel += Blood.MulScale16(t, dx);
            e.YVel += Blood.MulScale16(t, dy);
            e.ZVel += Blood.MulScale16(t, dz);
        }
        ActDamageSprite(src, e, K.kDamageExplode, a6);
    }

    public void ActRadiusDamage(Sprite src, double x, double y, double z, double nDist, double baseDmg, double distDmg, int dmgType, int flags, double burn)
    {
        // a level from its Build data: only what is in the sectors the blast reaches (GetClosestSpriteSectors)
        HashSet<int> sectmap = null;
        int ns = SectorOfPointB(x, y, z, src != null ? SpriteBSector(src) : -1);
        if (ns >= 0)
            sectmap = ClosestSpriteSectors(ns, x, y, nDist, null, src != null && Info.IsDude(src.Type));
        nDist *= 16;
        if ((flags & 2) != 0)
        {
            foreach (var e in StatList(K.kStatDude))
            {
                if (e.Removed || (e == src && (flags & 1) == 0) || !e.HasX || (e.HitTag & K.kHitagFree) != 0)
                    continue;
                if (sectmap != null && !sectmap.Contains(SpriteBSector(e)))
                    continue;
                if (!CheckProximity(e, x, y, z, nDist))
                    continue;
                double dx = Math.Abs(x - e.BX), dy = Math.Abs(y - e.BY), dz = Blood.Sar(Math.Abs(z - e.BZ), 4);
                double dist = Math.Floor(Math.Sqrt(dx * dx + dy * dy + dz * dz));
                if (dist > nDist)
                    continue;
                double vcx = dist != 0 ? baseDmg + Blood.Int((nDist - dist) * distDmg / nDist) : baseDmg + distDmg;
                ActDamageSprite(src, e, dmgType, vcx * 16);
                if (burn != 0 && !e.Removed)
                    ActBurnSprite(src, e, burn);
            }
        }
        if ((flags & 4) != 0)
        {
            foreach (var e in StatList(K.kStatThing))
            {
                if (e.Removed || !e.HasX || (e.HitTag & K.kHitagFree) != 0)
                    continue;
                if (sectmap != null && !sectmap.Contains(SpriteBSector(e)))
                    continue;
                if (!CheckProximity(e, x, y, z, nDist) || e.XLocked != 0)
                    continue;
                double dx = Math.Abs(x - e.BX), dy = Math.Abs(y - e.BY);
                double dist = Math.Floor(Math.Sqrt(dx * dx + dy * dy));
                if (dist > nDist)
                    continue;
                double vcx = dist != 0 ? baseDmg + Blood.Int((nDist - dist) * distDmg / nDist) : baseDmg + distDmg;
                ActDamageSprite(src, e, dmgType, vcx * 16);
                if (burn != 0 && !e.Removed)
                    ActBurnSprite(src, e, burn);
            }
        }
    }

    void ActNapalmMove(Sprite e)
    {
        ActPostSprite(e, K.kStatDecoration);
        Seq.Spawn(this, 9, e);
        if (Chance(32768))
            e.Cstat |= 4;
        Sound3D(e, 303, 24 + (e.HitTag & 3), 1);
        ActRadiusDamage(e.Owner, e.BX, e.BY, e.BZ, 128, 0, 60, K.kDamageExplode, 15, 120);
        if (e.Data4 > 1)
        {
            GibSprite(e, K.GIBTYPE_5);
            int v14a = e.Data4 >> 1, v14b = e.Data4 - v14a;
            int v4 = e.Ang;
            e.XVel = e.YVel = e.ZVel = 0;
            for (int i = 0; i < 2; i++)
            {
                int t1 = Random(209715) + 209715;
                int t2 = Random2(113);
                e.Ang = (int)Blood.AngMask(t2 + v4 + 2048);
                var e2 = ActFireThing(e, 0, 0, -37840, K.kThingNapalmBall, t1);
                e2.Owner = e.Owner;
                Seq.Spawn(this, 61, e2, NapalmSeqCallback);
                e2.Data4 = i == 0 ? v14a : v14b;
            }
        }
    }

    // ---------------------------------------------------------------- water and links (CheckLink)
    /// <summary>CheckLink: a link plane crossed (room over room), or liquid entered (kMarkerUpWater/UpGoo) or left
    /// (kMarkerLowWater/LowGoo) this frame; 0 if none.</summary>
    int CheckLink(Sprite e) => NativeCheckLink(e);

    /// <summary>The water surface z (Build) above an underwater sprite, searching up to its top.</summary>
    double WaterSurfaceZ(Sprite e, double topz)
    {
        var lo = e.Origin;
        var hi = B2Q(e.BX, e.BY, topz);
        if (PointIsWater(hi))
            return topz - 1;
        for (int i = 0; i < 8; i++)
        {
            var mid = (lo + hi) * 0.5f;
            if (PointIsWater(mid))
                lo = mid;
            else
                hi = mid;
        }
        return Q2BZ(lo);
    }

    HashSet<Sector> _waterMovers;      // underwater z-motion sectors, found on first use

    /// <summary>An underwater z-motion sector: one part of it (the ceiling's where both planes move)?</summary>
    bool IsWaterMover(Sector s)
    {
        if (_waterMovers == null)
        {
            _waterMovers = new HashSet<Sector>();
            var ceilings = new HashSet<int>();
            foreach (var c in Sectors)
                if (c.SUnderwater != 0 && c.IsMover && c.SOpen != "" && c.ZPart == "ceiling")
                    ceilings.Add(c.BSector);
            foreach (var c in Sectors)
                if (c.SUnderwater != 0 && c.IsMover && c.SOpen != "" && c.ZPart != ""
                    && !(c.ZPart == "floor" && ceilings.Contains(c.BSector)))
                    _waterMovers.Add(c);
        }
        return _waterMovers.Contains(s);
    }

    public bool IsUnderwaterSprite(Sprite e)
    {
        var sec = SpriteSector(e);
        if (sec != null && sec.SUnderwater != 0)
            return true;
        return e.Medium != K.kMediumNormal && PointIsWater(e.Origin);
    }

    // ---------------------------------------------------------------- MoveThing
    int MoveThing(Sprite e)
    {
        int v8 = 0;
        int elastic = Info.Thing(e.Type, K.TI_elastic);
        var (top, bottom) = Extents(e);
        if (e.XVel != 0 || e.YVel != 0)
        {
            double z = e.BZ;
            int nHit = ClipMove(e, Blood.Sar(e.XVel, 12), Blood.Sar(e.YVel, 12), e.ClipDist * 4, (z - top) / 4, (bottom - z) / 4);
            e.Hit = nHit;
            e.HitEnt = _clip.Ent;
            v8 = nHit;
            if (nHit == Hit.Wall)
            {
                (e.XVel, e.YVel) = WallBounce(e.XVel, e.YVel, elastic);
                if (e.Type == K.kThingZombieHead)
                {
                    Sound3D(e, 607);
                    ActDamageSprite(null, e, K.kDamageFall, 80);
                }
                else if (e.Type == K.kThingKickablePail)
                    Sound3D(e, 374);
            }
        }
        if (e.ZVel != 0)
            e.SetBZ(e.BZ + Blood.Sar(e.ZVel, 8));
        var zr = GetZRange(e, e.ClipDist * 4);
        (_, bottom) = Extents(e);
        if ((e.HitTag & 2) != 0 && bottom < zr.FloorZ)
        {
            e.SetBZ(e.BZ + 455);
            e.ZVel += 58254;
            if (e.Type == K.kThingZombieHead)
            {
                var fx = FxSpawn(K.FX_27, e.BX, e.BY, e.BZ, 0);
                if (fx != null)
                {
                    double v34 = Blood.AngMask(FrameClock * 3), v30 = Blood.AngMask(FrameClock * 5), vbx2 = Blood.AngMask(FrameClock * 11);
                    var a = Blood.RotateVector(279620, 0, vbx2);
                    var b = Blood.RotateVector(a.x, 0, v30);
                    var c = Blood.RotateVector(a.y, b.y, v34);
                    fx.XVel = e.XVel + b.x;
                    fx.YVel = e.YVel + c.x;
                    fx.ZVel = e.ZVel + c.y;
                }
            }
        }
        if (CheckLink(e) != 0)
            zr = GetZRange(e, e.ClipDist * 4);
        (top, bottom) = Extents(e);
        if (bottom >= zr.FloorZ)
        {
            ActTouchFloor(e);
            e.FlorHit = zr.FloorHit;
            e.FlorHitEnt = zr.FloorEnt;
            e.FlorSurf = zr.FloorSurf;
            e.SetBZ(e.BZ + zr.FloorZ - bottom);
            double v20 = e.ZVel - VelOfMoverZ(zr.FloorEnt);
            if (v20 > 0)
            {
                e.HitTag |= 4;
                double xv = e.XVel, yv = e.YVel;
                double vax = Physics.FloorBounceVector(ref xv, ref yv, ref v20, elastic, FloorNormal);
                e.XVel = xv;
                e.YVel = yv;
                double nDamage = Blood.MulScale30(vax, vax) - Info.Thing(e.Type, K.TI_dmgResist);
                if (nDamage > 0)
                    ActDamageSprite(e, e, K.kDamageFall, nDamage);
                if (e.Removed)
                    return v8;
                e.ZVel = v20;
                if (VelOfMoverZ(zr.FloorEnt) == 0 && Math.Abs(e.ZVel) < 65536)
                {
                    e.ZVel = 0;
                    e.HitTag &= ~4;
                }
                if (e.Type == K.kThingNapalmBall)
                {
                    if (e.ZVel == 0 | Chance(40960))
                        ActNapalmMove(e);
                }
                else if (e.Type == K.kThingZombieHead)
                {
                    if (Math.Abs(e.ZVel) > 524288)
                    {
                        Sound3D(e, 607);
                        ActDamageSprite(null, e, K.kDamageFall, 80);
                    }
                }
                else if (e.Type == K.kThingKickablePail && Math.Abs(e.ZVel) > 524288)
                    Sound3D(e, 374);
                v8 = Hit.Sector;
            }
            else if (e.ZVel == 0)
                e.HitTag &= ~4;
        }
        else
        {
            e.FlorHit = 0;
            if ((e.HitTag & 2) != 0)
                e.HitTag |= 4;
        }
        if (top <= zr.CeilZ)
        {
            e.CeilHit = zr.CeilHit;
            e.CeilHitEnt = zr.CeilEnt;
            e.SetBZ(e.BZ + Blood.ClipLow(zr.CeilZ - top, 0));
            if (e.ZVel < 0)
            {
                e.XVel = Blood.MulScale16(e.XVel, 49152);
                e.YVel = Blood.MulScale16(e.YVel, 49152);
                e.ZVel = Blood.MulScale16(-e.ZVel, 16384);
                if (e.Type == K.kThingZombieHead && Math.Abs(e.ZVel) > 524288)
                {
                    Sound3D(e, 607);
                    ActDamageSprite(null, e, K.kDamageFall, 80);
                }
                else if (e.Type == K.kThingKickablePail && Math.Abs(e.ZVel) > 524288)
                    Sound3D(e, 374);
            }
        }
        else
            e.CeilHit = 0;
        if (bottom >= zr.FloorZ)
        {
            double nVel = Blood.ApproxDist(e.XVel, e.YVel);
            double nVelClipped = Blood.ClipHigh(nVel, 69905);
            if (zr.FloorHit == Hit.Sprite && zr.FloorEnt is Sprite hitSpr && (hitSpr.Cstat & 48) == 0)
            {
                e.XVel += e.BX - hitSpr.BX;
                e.YVel += e.BY - hitSpr.BY;
                v8 = e.Hit;
            }
            if (nVel > 0)
            {
                double t = Blood.DivScale16(nVelClipped, nVel);
                e.XVel -= Blood.MulScale16(t, e.XVel);
                e.YVel -= Blood.MulScale16(t, e.YVel);
            }
        }
        if (e.XVel != 0 || e.YVel != 0)
            e.Ang = Blood.GetAngle(e.XVel, e.YVel);
        if (e.Body != null)
            UpdateBoxOrigin(e);
        return v8;
    }

    static void UpdateBoxOrigin(Sprite e) { }     // bodies follow their sprite's origin

    // ---------------------------------------------------------------- MoveDude
    /// <summary>MoveDude: a dude's (or player's) movement: clipmove, sector enter/exit, gravity and buoyancy,
    /// links and water, floor and ceiling, drag.</summary>
    public void MoveDude(Sprite e)
    {
        var pl = e as Player;
        if (!Info.IsDude(e.Type))
            return;
        var (top, bottom) = Extents(e);
        double z = e.BZ;
        double bzd = (bottom - z) / 4, tzd = (z - top) / 4;
        double wd = e.ClipDist * 4;
        var oldsec = SpriteSector(e);
        if (e.XVel != 0 || e.YVel != 0)
        {
            if (pl != null && pl.NoClip)
            {
                e.Origin = B2Q(e.BX + Blood.Sar(e.XVel, 12), e.BY + Blood.Sar(e.YVel, 12), e.BZ);
                e.Hit = 0;
            }
            else
            {
                e.Hit = ClipMove(e, Blood.Sar(e.XVel, 12), Blood.Sar(e.YVel, 12), wd, tzd, bzd);
                e.HitEnt = _clip.Ent;
            }
            if (e.Hit == Hit.Sprite && e.HitEnt is Sprite hitSpr)
            {
                if (hitSpr.StatNum == K.kStatProjectile && (hitSpr.HitTag & 32) == 0 && e != hitSpr.Owner)
                {
                    var savedHit = HitEnt;
                    int savedType = HitType;
                    HitEnt = e;
                    HitType = 3;
                    ActImpactMissile(hitSpr, 3);
                    HitEnt = savedHit;
                    HitType = savedType;
                }
                // (a modern map's Touch triggers fire from ProcessTouchObjects, for floor and ceiling hits too)
                if (!IsModern && !hitSpr.Removed && hitSpr.HasX && hitSpr.XTouch != 0 && hitSpr.XState == 0 && hitSpr.IsTriggered == 0)
                    TrTriggerSprite(hitSpr, K.kCmdSpriteTouch, e);
                if (!hitSpr.Removed && Info.Dude(e.Type, K.DI_lockOut) != 0 && hitSpr.HasX && hitSpr.XPush != 0 && hitSpr.XKey == 0
                    && hitSpr.DudeLockout == 0 && hitSpr.XState == 0 && hitSpr.XBusy == 0 && pl == null)
                    TrTriggerSprite(hitSpr, K.kCmdSpritePush, e);
            }
            else if (e.Hit == Hit.Wall)
            {
                if (e.HitEnt is XObject ho && ho.HasX && Info.Dude(e.Type, K.DI_lockOut) != 0 && ho.XKey == 0
                    && ho.DudeLockout == 0 && ho.XState == 0 && ho.XBusy == 0 && pl == null)
                {
                    // brush objects (doors etc.) act as walls and sectors
                    if (ho is Wall hw && hw.XPush != 0)
                        TrTriggerWall(hw, K.kCmdWallPush, e);
                    if (ho is Sector hs && hs.SWallpush != 0)
                        TrTriggerSectorBoth(hs, K.kCmdSectorPush, e);
                }
                (e.XVel, e.YVel) = WallBounce(e.XVel, e.YVel, 0);
            }
        }
        else if (FixBlood)
        {
            // Blood changes a dude's sector only as it moves: a sector moving away from under one standing still (E2M1's
            // iceberg 114, no Drag) left it in that sector, held up by its floor's plane extended, until its first step.
            // b3d_fixblood: the sector holding it now, so it falls at once (and the sector's enter and exit happen).
            int b = SpriteBSector(e);
            if (b >= 0 && !Board.Inside(e.BX, e.BY, b))
            {
                int now = UpdateSector(b, e.Origin, e.Origin, 0, 0);
                if (now != b)
                    SetSpriteBSector(e, now);
            }
        }
        // sector change: enter/exit triggers
        var sec = SpriteSectorUpdate(e);
        if (sec != oldsec)
        {
            if (oldsec != null && oldsec.HasX && oldsec.SExit != 0 && (pl != null || oldsec.DudeLockout == 0))
                TrTriggerSectorParts(oldsec, K.kCmdSectorExit, e, false);
            if (sec != null && sec.HasX && sec.SEnter != 0 && (pl != null || sec.DudeLockout == 0))
            {
                if (sec.Type == K.kSectorTeleport)
                    sec.SDataEnt = pl;
                TrTriggerSectorParts(sec, K.kCmdSectorEnter, e, true);
            }
        }
        bool bUnderwater = sec != null && sec.SUnderwater != 0;
        bool bDepth = sec != null && sec.SDepth != 0;
        // Blood: a sector is deep where it has Depth or a water or goo link on its floor or ceiling, and the buoyancy
        // below measures from its own ceiling (underwater) or floor; a level without Build sectors goes by the medium.
        // (By the medium, E4M4's water 117 under water 118, joined by a stack link, floated the player at 117's ceiling,
        // and on E3M3's flooded stairs the room over the water had no lift: a crouching swimmer bounced through the link.)
        int bs = UseSectorClip ? SpriteBSector(e) : -1;
        if (bs >= 0)
        {
            if (SectorLink(bs, 1) is { } ul && (ul.Type == K.kMarkerUpWater || ul.Type == K.kMarkerUpGoo))
                bDepth = true;
            if (SectorLink(bs, -1) is { } ll && (ll.Type == K.kMarkerLowWater || ll.Type == K.kMarkerLowGoo))
                bDepth = true;
        }
        else if (e.Medium != K.kMediumNormal)
        {
            bUnderwater = true;
            bDepth = true;
        }
        if (pl != null)
            wd += 16;
        if (e.ZVel != 0)
            e.SetBZ(e.BZ + Blood.Sar(e.ZVel, 8));
        var zr = GetZRange(e, wd, true);
        (top, bottom) = Extents(e);
        Log?.Append($" [movedude z {e.Origin.Z:0.###} top {top} bottom {bottom} ceil {zr.CeilZ} ({zr.CeilHit} {zr.CeilEnt}) floor {zr.FloorZ} ({zr.FloorHit} {zr.FloorEnt}) hit {e.Hit} {e.HitEnt}]");
        if ((e.HitTag & 2) != 0)
        {
            double vc = 58254;
            if (bDepth)
            {
                double cz = 0, fz = zr.FloorZ;
                if (bs >= 0)
                    ZsOfSlope(bs, e.BX, e.BY, out cz, out fz);
                if (bUnderwater)
                {
                    if (bs < 0)
                        cz = WaterSurfaceZ(e, top);
                    vc = cz > top ? vc + Blood.Int((bottom - cz) * -80099 / (bottom - top)) : 0;
                }
                else if (fz < bottom)
                    vc += Blood.Int((bottom - fz) * -80099 / (bottom - top));
            }
            else if (bUnderwater || bottom >= zr.FloorZ)
                vc = 0;
            if (vc != 0)
            {
                e.SetBZ(e.BZ + Blood.Sar(Blood.Int(vc * 4 / 2), 8));
                e.ZVel += vc;
            }
        }
        if (pl != null && e.ZVel > 1398101 && !pl.FallScream && e.XHeight > 0 && e.XHealth > 0)
        {
            pl.FallScream = true;
            Sound3D(e, 719);
        }
        int nLink = CheckLink(e);
        if (nLink != 0)
        {
            zr = GetZRange(e, wd, true);
            EvKillCallback(e, pl != null ? K.kCallbackPlayerBubble : K.kCallbackEnemeyBubble);
            bool wasPatrolling = IsModern && AiInPatrolState(e.AiState);
            if (nLink == K.kMarkerLowWater || nLink == K.kMarkerLowGoo)
            {
                e.Medium = K.kMediumNormal;
                if (pl != null)
                {
                    pl.Posture = Player.PostureStand;
                    pl.BubbleTime = 0;
                    if (!pl.CantJump && pl.InputJump)
                    {
                        e.ZVel = -436906;
                        pl.CantJump = true;
                    }
                    Sound3D(e, 721, -1);
                }
                else if (e.Type == K.kDudeCultistTommy || e.Type == K.kDudeCultistShotgun)
                    AiNewState(e, cultistGoto);
                else if (e.Type == K.kDudeGillBeast)
                {
                    AiNewState(e, gillBeastGoto);
                    e.HitTag |= 6;
                }
                else if (e.Type == K.kDudeBoneEel)
                    ActKillDude(e, e, K.kDamageFall, 16000);
                // out of the water: the patrol goes on
                if (wasPatrolling && !e.Removed && e is not Player && Info.IsDude(e.Type) && e.XHealth > 0)
                    AiPatrolState(e, kAiStatePatrolMove);
            }
            else if (nLink == K.kMarkerUpWater || nLink == K.kMarkerUpGoo)
            {
                int chance = nLink == K.kMarkerUpGoo ? 1024 : 2560;
                e.Medium = nLink == K.kMarkerUpGoo ? K.kMediumGoo : K.kMediumWater;
                int t = e.Type;
                if (pl != null)
                {
                    pl.Posture = Player.PostureSwim;
                    e.BurnTime = 0;
                    pl.BubbleTime = Blood.Sar(Math.Abs(e.ZVel), 12);
                    EvPostCallback(e, 0, K.kCallbackPlayerBubble);
                    Sound3D(e, 720, -1);
                }
                else if (t == K.kDudeCultistTommy || t == K.kDudeCultistShotgun)
                {
                    SwimEnter(e);
                    AiNewState(e, cultistSwimGoto);
                }
                else if (t == K.kDudeBurningCultist)
                {
                    if (Info.IsDude(e.InitType) && e.InitType != e.Type)
                        e.Type = e.InitType;
                    else
                        e.Type = Chance(chance) ? K.kDudeCultistTommy : K.kDudeCultistShotgun;
                    SwimEnter(e);
                    AiNewState(e, cultistSwimGoto);
                }
                else if (t == K.kDudeZombieAxeNormal)
                {
                    SwimEnter(e);
                    AiNewState(e, zombieAGoto);
                }
                else if (t == K.kDudeZombieButcher)
                {
                    SwimEnter(e);
                    AiNewState(e, zombieFGoto);
                }
                else if (t == K.kDudeGillBeast)
                {
                    SwimEnter(e);
                    AiNewState(e, gillBeastSwimGoto);
                    e.HitTag &= ~6;
                }
                else if (t == K.kDudeGargoyleFlesh || t == K.kDudeHellHound || t == K.kDudeSpiderBrown || t == K.kDudeSpiderRed
                    || t == K.kDudeSpiderBlack || t == K.kDudeBat || t == K.kDudeRat || t == K.kDudeBurningInnocent)
                {
                    ActKillDude(e, e, K.kDamageFall, 16000);
                    return;
                }
                if (IsModern && t == KM.kDudeModernCustom && e.Custom != null)
                {
                    // a custom dude that can't swim drowns
                    EvPostCallback(e, 0, K.kCallbackEnemeyBubble);
                    if (!e.Custom.CanSwim())
                    {
                        ActKillDude(e, e, K.kDamageDrown, 16000);
                        return;
                    }
                }
                // into the water: the patrol goes on
                if (wasPatrolling && !e.Removed && e is not Player && Info.IsDude(e.Type) && e.XHealth > 0)
                    AiPatrolState(e, kAiStatePatrolMove);
            }
        }
        (top, bottom) = Extents(e);
        if (pl != null && bottom >= zr.FloorZ)
        {
            var saved = zr;
            zr = GetZRange(e, e.ClipDist * 4, true);
            if (bottom <= zr.FloorZ && e.BZ - saved.FloorZ < bzd)
            {
                zr.FloorZ = saved.FloorZ;
                zr.FloorHit = saved.FloorHit;
                zr.FloorEnt = saved.FloorEnt;
                zr.FloorSurf = saved.FloorSurf;
            }
        }
        if (zr.FloorZ <= bottom)
        {
            e.FlorHit = zr.FloorHit;
            e.FlorHitEnt = zr.FloorEnt;
            e.FlorSurf = zr.FloorSurf;
            e.SetBZ(e.BZ + zr.FloorZ - bottom);
            double v30 = e.ZVel - VelOfMoverZ(zr.FloorEnt);
            if (v30 > 0)
            {
                double xv = e.XVel, yv = e.YVel;
                double vax = Physics.FloorBounceVector(ref xv, ref yv, ref v30, 0, FloorNormal);
                e.XVel = xv;
                e.YVel = yv;
                double nDamage = Blood.MulScale30(vax, vax);
                if (pl != null)
                {
                    pl.FallScream = false;
                    if (nDamage > 240 && (e.HitTag & 4) != 0)
                        PlayerLandingSound(pl);
                    if (nDamage > 480)
                        Sound3D(e, 701);
                }
                nDamage -= 1600;
                if (nDamage > 0)
                    ActDamageSprite(e, e, K.kDamageFall, nDamage);
                if (e.Removed)
                    return;
                e.ZVel = v30;
                if (Math.Abs(e.ZVel) < 65536)
                {
                    e.ZVel = VelOfMoverZ(zr.FloorEnt);
                    e.HitTag &= ~4;
                }
                else
                    e.HitTag |= 4;
                if (zr.FloorSurf == K.kSurfWater)
                    FxSpawn(K.FX_9, e.BX, e.BY, zr.FloorZ, 0);
                else if (zr.FloorSurf == K.kSurfLava)
                {
                    var fx = FxSpawn(K.FX_10, e.BX, e.BY, zr.FloorZ, 0);
                    if (fx != null)
                    {
                        for (int i = 0; i < 7; i++)
                        {
                            var fx2 = FxSpawn(K.FX_14, fx.BX, fx.BY, fx.BZ, 0);
                            if (fx2 != null)
                            {
                                fx2.XVel = Random2(436906);
                                fx2.YVel = Random2(436906);
                                fx2.ZVel = -Random(873813);
                            }
                        }
                    }
                }
            }
            else if (e.ZVel == 0)
                e.HitTag &= ~4;
        }
        else
        {
            e.FlorHit = 0;
            if ((e.HitTag & 2) != 0)
                e.HitTag |= 4;
        }
        if (top <= zr.CeilZ)
        {
            e.CeilHit = zr.CeilHit;
            e.CeilHitEnt = zr.CeilEnt;
            e.SetBZ(e.BZ + Blood.ClipLow(zr.CeilZ - top, 0));
            if (e.ZVel <= 0 && (e.HitTag & 4) != 0)
                e.ZVel = Blood.MulScale16(-e.ZVel, 8192);
        }
        else
            e.CeilHit = 0;
        (_, bottom) = Extents(e);
        e.XHeight = Blood.Sar(Blood.ClipLow(zr.FloorZ - bottom, 0), 8);
        if (e.XVel != 0 || e.YVel != 0)
        {
            if (zr.FloorHit == Hit.Sprite && zr.FloorEnt is Sprite fe && (fe.Cstat & 48) == 0)
            {
                e.XVel += e.BX - fe.BX;
                e.YVel += e.BY - fe.BY;
                return;
            }
            if (sec != null && sec.SUnderwater != 0)
                return;
            if (e.XHeight >= 256)
                return;
            double nDrag = DudeDrag;
            if (e.XHeight > 0)
                nDrag -= Blood.Scale3(DudeDrag, e.XHeight, 256);
            e.XVel -= Blood.MulScale16R(e.XVel, nDrag);
            e.YVel -= Blood.MulScale16R(e.YVel, nDrag);
            if (Blood.ApproxDist(e.XVel, e.YVel) < 4096)
            {
                e.XVel = 0;
                e.YVel = 0;
            }
        }
    }

    void SwimEnter(Sprite e)
    {
        e.BurnTime = 0;
        EvPostCallback(e, 0, K.kCallbackEnemeyBubble);
        Sound3D(e, 720, -1);
    }

    /// <summary>When set, MoveDude notes what it found (strict replay probes).</summary>
    public System.Text.StringBuilder Log;

    // ---------------------------------------------------------------- MoveMissile
    int MoveMissile(Sprite e)
    {
        int vdi = -1;
        HitEnt = null;
        HitType = -1;
        if (e.Type == K.kMissileFlameSpray)
            ActAirDrag(e, 4096);
        var tgt = e.XTarget;
        if (tgt != null && !tgt.Removed && (e.XVel != 0 || e.YVel != 0 || e.ZVel != 0))
        {
            if (tgt.StatNum == K.kStatDude && tgt.HasX && tgt.XHealth > 0)
            {
                int nTargetAngle = Blood.GetAngle(-(tgt.BY - e.BY), tgt.BX - e.BX);
                var rv = Blood.RotateVector(MissileInfo(e.Type, K.MI_velocity), 0, Blood.AngMask(nTargetAngle + 1536));
                e.XVel = rv.x;
                e.YVel = rv.y;
                double dz = tgt.BZ - e.BZ;
                double vax = Blood.Int(dz / 10);
                if (tgt.BZ < e.BZ)
                    vax = -vax;
                e.ZVel += vax;
            }
        }
        // a converted level: Build's clipmove and getzrange on its sectors (World.BuildClip)
        if (UseSectorClip && SpriteBSector(e) >= 0)
            return MoveMissileSectors(e);
        double vx = Blood.Sar(e.XVel, 12), vy = Blood.Sar(e.YVel, 12), vz = Blood.Sar(e.ZVel, 8);
        float r = (float)Math.Max(e.ClipDist * 4 / Blood.XYScale, 0.5);
        var (top, bottom) = Extents(e);
        var mn = new Vector3(-r, -r, (float)(-(bottom - e.BZ) / 4 / Blood.ZScale));
        var mx = new Vector3(r, r, (float)((e.BZ - top) / 4 / Blood.ZScale));
        if (mx.Z - mn.Z < 0.5f)
        {
            mn.Z -= 0.25f;
            mx.Z += 0.25f;
        }
        var start = e.Origin;
        var end = start + (B2Q(vx, vy, vz) - B2Q(0, 0, 0));
        // room over room: the part of the box beyond a link plane is in the other room, and a missile whose origin
        // crosses one continues there
        (mn, mx) = Collision.LinkClampBox(start, mn, mx);
        var lnk = Collision.LinkPathCross(start, end, out float frac);
        TraceResult tr;
        if (lnk != null)
        {
            // up to the plane only the origin reaches it: the box side towards it would stop on the solid behind it
            var cross = start + (end - start) * frac;
            var bmn = mn;
            var bmx = mx;
            if (lnk.Dir > 0) bmn.Z = 0; else bmx.Z = 0;
            tr = Collision.Trace(start, bmn, bmx, cross, Contents.MaskMove, e);
            if (tr.Fraction >= 1 && !tr.StartSolid)
            {
                start = cross + lnk.Offset;
                start.Z -= lnk.Dir * 0.1f;
                end += lnk.Offset;
                e.Origin = start;
                if (Map != null)
                    SetSpriteBSector(e, LinkFarSector(lnk, e));
                SpriteSectorUpdate(e);
                if (lnk.Dir > 0) mx.Z = 0; else mn.Z = 0;
                tr = Collision.Trace(start, mn, mx, end, Contents.MaskMove, e);
            }
        }
        else
        {
            // a box that already overlaps an actor (fired point-blank past its picture) and goes on into it: a hit.
            // Build's clipmove stops only a move into a sprite's clip box, so one moving away from it goes free (the
            // dropped life leech's bolts start against the leech)
            tr = Collision.Trace(start, mn, mx, start, Contents.MaskMove, e);
            if (tr.StartSolid && tr.Entity is Sprite over)
            {
                var into = over.Origin - start;
                into.Z = 0;
                if ((end - start).Dot(into) > 0)
                {
                    tr.Fraction = 0;
                    tr.EndPos = start;
                    tr.Normal = (start - end).Normalized();
                }
                else
                    tr = Collision.Trace(start, mn, mx, end, Contents.MaskMove, e, new HashSet<object> { over });
            }
            else
                tr = Collision.Trace(start, mn, mx, end, Contents.MaskMove, e);
        }
        if (tr.Fraction < 1)
        {
            ClassifyTrace(tr);
            if (HitType == 3)
                vdi = 3;
            else
            {
                vdi = HitType;
                if (HitEnt is Wall hw && hw.HasX && hw.XVector != 0)
                    TrTriggerWall(hw, K.kCmdWallImpact, e.Owner);
            }
            e.Origin = tr.EndPos;
            if (vdi >= 0 && vdi != 3)
            {
                // back off from the surface like Blood does (16 units along the flight angle)
                int nAngle = Blood.GetAngle(e.XVel, e.YVel);
                e.Origin = B2Q(e.BX - Blood.MulScale30(Blood.Cos(nAngle), 16), e.BY - Blood.MulScale30(Blood.Sin(nAngle), 16), e.BZ);
            }
        }
        else
            e.Origin = end;
        if (vdi == 1)
            e.CeilHit = Hit.Sector;
        else if (vdi == 2)
        {
            e.FlorHit = Hit.Sector;
            e.FlorSurf = HitSurf;
        }
        if (vdi < 0)
        {
            HitX = e.BX;
            HitY = e.BY;
            HitZ = e.BZ;
            HitPos = e.Origin;
        }
        return vdi;
    }

    // ---------------------------------------------------------------- actExplodeSprite
    public void ActExplodeSprite(Sprite e)
    {
        if (!e.HasX || e.StatNum == K.kStatExplosion)
            return;
        Kill3DSound(e);
        EvKill(e);
        int nType;
        int t = e.Type;
        if (t == K.kMissileFireballNapalm)
        {
            nType = K.kExplosionNapalm;
            Seq.Spawn(this, 4, e);
            if (Chance(32768)) e.Cstat |= 4;
            Sound3D(e, 303, -1);
            GibSprite(e, K.GIBTYPE_5);
        }
        else if (t == K.kMissileFlareAlt)
        {
            nType = K.kExplosionFireball;
            Seq.Spawn(this, 9, e);
            if (Chance(32768)) e.Cstat |= 4;
            Sound3D(e, 306, 24 + (e.SprIndex & 3), 1);
            GibSprite(e, K.GIBTYPE_5);
        }
        else if (t == K.kMissileFireballCerberus || t == K.kMissileFireballTchernobog)
        {
            nType = K.kExplosionFireball;
            Seq.Spawn(this, 5, e);
            Sound3D(e, 304, -1);
            GibSprite(e, K.GIBTYPE_5);
        }
        else if (t == K.kThingArmedTNTStick)
        {
            nType = K.kExplosionSmall;
            Seq.Spawn(this, e.FlorHit == 0 ? 4 : 3, e);
            Sound3D(e, 303, -1);
            GibSprite(e, K.GIBTYPE_5);
        }
        else if (t == K.kThingArmedProxBomb || t == K.kThingArmedRemoteBomb || t == K.kThingArmedTNTBundle || t == KM.kModernThingTNTProx)
        {
            nType = K.kExplosionStandard;
            Seq.Spawn(this, e.FlorHit == 0 ? 4 : 3, e);
            Sound3D(e, 304, -1);
            GibSprite(e, K.GIBTYPE_5);
        }
        else if (t == K.kThingArmedSpray)
        {
            nType = K.kExplosionSpray;
            Seq.Spawn(this, 5, e);
            Sound3D(e, 307, -1);
            GibSprite(e, K.GIBTYPE_5);
        }
        else if (t == K.kThingTNTBarrel)
        {
            var e2 = ActSpawnSprite(e.BX, e.BY, e.BZ, K.kStatDecoration, true);
            e2.Owner = e.Owner;
            if (ActCheckRespawn(e))
            {
                e.XState = 1;
                e.XHealth = Info.Thing(K.kThingTNTBarrel, K.TI_startHealth) * 16;
            }
            else
                ActPostSprite(e, K.kStatFree);
            nType = K.kExplosionLarge;
            Seq.Spawn(this, 4, e2);
            Sound3D(e2, 305, -1);
            GibSprite(e2, K.GIBTYPE_14);
            e = e2;
        }
        else if (t == K.kTrapExploder)
        {
            nType = 1;
            int nSnd = 304, nSeq = 4;
            if (IsModern)
            {
                // a modern map's hidden exploder: its kind (data1), sequence (data2) and sound (data3)
                nType = e.Data1;
                if (nType <= 1 || nType > 8) { nType = 1; nSeq = 4; nSnd = 304; }
                else if (nType == 2) { nSeq = 4; nSnd = 305; }
                else if (nType == 3) { nSeq = 9; nSnd = 307; }
                else if (nType == 4) { nSeq = 5; nSnd = 307; }
                else if (nType <= 7) { nSeq = 4; nSnd = 303; }
                else { nType = 0; nSeq = 3; nSnd = 303; }
                if (e.Data2 > 0) nSeq = e.Data2;
                if (e.Data3 > 0) nSnd = e.Data3;
            }
            if (SeqData.Exists(nSeq))
                Seq.Spawn(this, nSeq, e);
            Sound3D(e, nSnd, -1);
        }
        else if (t == K.kThingPodFireBall)
        {
            nType = K.kExplosionFireball;
            Seq.Spawn(this, 9, e);
            Sound3D(e, 307, -1);
            GibSprite(e, K.GIBTYPE_5);
            FxSpawnPodBlood(e, 240);
        }
        else
        {
            nType = K.kExplosionStandard;
            Seq.Spawn(this, 4, e);
            if (Chance(32768)) e.Cstat |= 4;
            Sound3D(e, 303, -1);
            GibSprite(e, K.GIBTYPE_5);
        }
        e.XVel = e.YVel = e.ZVel = 0;
        ActPostSprite(e, K.kStatExplosion);
        e.XRepeat = ExplodeInfo(nType, K.EI_repeat);
        e.YRepeat = e.XRepeat;
        e.HitTag &= ~3;
        e.Type = nType;
        e.XTarget = null;
        e.TargetX = 0;   // "damage dealt" flag (xsprite.target = 0/1)
        e.Data1 = ExplodeInfo(nType, K.EI_ticks);
        e.Data2 = ExplodeInfo(nType, K.EI_quakeEffect);
        e.Data3 = ExplodeInfo(nType, K.EI_flashEffect);
        e.Cstat &= ~257;
        UpdateBox(e);
    }

    public void ActActivateGibObject(Sprite e)
    {
        int vdx = (int)Blood.ClipRange(e.Data1, 0, 31), vc = (int)Blood.ClipRange(e.Data2, 0, 31), v4 = (int)Blood.ClipRange(e.Data3, 0, 31);
        int vbp = e.Data4, v8 = e.DropMsg;
        if (vdx > 0)
            GibSprite(e, vdx - 1);
        if (vc > 0)
            GibSprite(e, vc - 1);
        if (v4 > 0 && e.BurnTime > 0)
            GibSprite(e, v4 - 1);
        if (vbp > 0)
            Sound3DAt(e.Origin, vbp);
        if (v8 > 0)
            ActDropObject(e, v8);
        if ((e.Cstat & 32768) == 0 && (e.HitTag & K.kHitagRespawn) == 0)
            ActPostSprite(e, K.kStatFree);
    }

    // ---------------------------------------------------------------- ProcessTouchObjects
    void TouchSaw(Sprite e, Sprite e2)
    {
        if (e2.XState == 0)
            ActDamageSprite(e, e, K.kDamageBullet, 1);
        else
        {
            e2.Data1 = 1;
            e2.Data2 = (int)Blood.ClipHigh(e2.Data2 + 8, 600);
            ActDamageSprite(e, e, K.kDamageBullet, 16);
        }
    }

    void ProcessTouchObjects(Sprite e)
    {
        var pl = e as Player;
        // ceiling contact
        if (e.CeilHit == Hit.Sprite && e.CeilHitEnt is Sprite c2 && !c2.Removed && c2.HasX)
        {
            if ((c2.StatNum == K.kStatThing || c2.StatNum == K.kStatDude) && (e.XVel != 0 || e.YVel != 0 || e.ZVel != 0))
            {
                if (c2.StatNum == K.kStatThing)
                {
                    if ((Info.Thing(c2.Type, K.TI_flags) & 1) != 0)
                        c2.HitTag |= 1;
                    if ((Info.Thing(c2.Type, K.TI_flags) & 2) != 0)
                        c2.HitTag |= 4;
                    c2.XVel += c2.BX - e.BX;
                    c2.YVel += c2.BY - e.BY;
                }
                else
                {
                    c2.HitTag |= 5;
                    c2.XVel += c2.BX - e.BX;
                    c2.YVel += c2.BY - e.BY;
                    if ((pl == null || !pl.GodMode) && c2.Type == K.kDudeTchernobog)
                        ActDamageSprite(c2, e, K.kDamageExplode, e.XHealth * 4);
                }
            }
            if (!c2.Removed && c2.Type == K.kTrapSawCircular)
                TouchSaw(e, c2);
        }
        if (e.Removed)
            return;
        // side contact
        if (e.Hit == Hit.Sprite && e.HitEnt is Sprite h2 && !h2.Removed && h2.HasX)
        {
            if (h2.Type == K.kThingKickablePail)
                ActKickObject(e, h2);
            else if (h2.Type == K.kThingZombieHead)
            {
                Sound3DAt(e.Origin, 357);
                ActKickObject(e, h2);
                ActDamageSprite(null, h2, K.kDamageFall, 80);
            }
            else if (h2.Type == K.kDudeBurningInnocent || h2.Type == K.kDudeBurningCultist || h2.Type == K.kDudeBurningZombieAxe || h2.Type == K.kDudeBurningZombieButcher)
            {
                e.BurnTime = (int)Blood.ClipLow(e.BurnTime - GameClock.TicsPerFrame, 0);
                ActDamageSprite(e.BurnSource, e, K.kDamageBurn, 8);
            }
        }
        if (e.Removed)
            return;
        // floor contact
        if (e.FlorHit == Hit.Sector)
            ActTouchFloor(e);
        else if (e.FlorHit == Hit.Sprite && e.FlorHitEnt is Sprite f2 && !f2.Removed && f2.HasX)
        {
            int t = f2.Type;
            if (t == K.kThingKickablePail || t == K.kThingZombieHead)
            {
                if (pl != null)
                {
                    if (pl.KickPower > FrameClock) return;
                    pl.KickPower = FrameClock + 60;
                }
                ActKickObject(e, f2);
                Sound3DAt(e.Origin, 357);
                if (t == K.kThingKickablePail)
                    Sound3D(e, 374);
                else
                    ActDamageSprite(null, f2, K.kDamageFall, 80);
            }
            else if (t == K.kTrapSawCircular)
                TouchSaw(e, f2);
            else if ((t >= K.kDudeCultistTommy && t <= K.kDudeGargoyleStone) || (t >= K.kDudePhantasm && t <= K.kDudeSpiderBlack)
                || t == K.kDudeGillBeast || (t >= K.kDudeBat && t <= K.kDudeTchernobog) || (t >= K.kDudePlayer1 && t <= K.kDudePlayer8))
            {
                if (pl != null)
                    ActDamageSprite(e, f2, K.kDamageBullet, 8);
            }
        }
        if (IsModern && !e.Removed && IsDudeSprite(e))
            ModernTouchTriggers(e);
    }

    /// <summary>A modern map's Touch triggers: the sprites a dude touches (beside, under, over it) and the wall.</summary>
    void ModernTouchTriggers(Sprite e)
    {
        if (e.Hit == Hit.Sprite && e.HitEnt is Sprite hs)
            TriggerTouchSprite(e, hs);
        if (e.FlorHit == Hit.Sprite && e.FlorHitEnt is Sprite fs)
            TriggerTouchSprite(e, fs);
        if (e.CeilHit == Hit.Sprite && e.CeilHitEnt is Sprite cs)
            TriggerTouchSprite(e, cs);
        if (e.Hit == Hit.Wall && e.HitEnt is Wall hw)
            TriggerTouchWall(e, hw);
    }
}
