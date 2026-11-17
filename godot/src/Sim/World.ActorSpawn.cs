using System;
using Chosen.Data;
using Godot;

namespace Chosen.Sim;

/// <summary>
/// Spawning, firing and respawn (actor.cpp, via Blood3D's server/actor_spawn.qc): dudes and things spawned during
/// play, thrown things, missiles, hitscan vectors with their effects, and the respawn timers.
/// </summary>
public sealed partial class World
{
    public int ItemSettings, RespawnSettings;     // gItemSettings, gRespawnSettings
    public int MonsterSettings => Monsters ? 1 : 0;
    public bool FriendlyFire = true;
    public double VectorBurnRange;               // gVectorBurnRange (Tchernobog's burn)

    public Sprite ActSpawnDude(Sprite src, int nType, double a3, double a4)
    {
        var e = ActSpawnSpriteFrom(src, K.kStatDude);
        int angle = src.Ang;
        double z = a4 + src.BZ, x, y;
        if (a3 < 0)
        {
            x = src.BX;
            y = src.BY;
        }
        else
        {
            x = src.BX + Blood.MulScale30R(Blood.Cos(angle), a3);
            y = src.BY + Blood.MulScale30R(Blood.Sin(angle), a3);
        }
        e.ClassName = "dude";
        e.Type = e.InitType = nType;
        e.Ang = angle;
        e.Origin = e.PrevOrigin = B2Q(x, y, z);
        e.Cstat |= 4353;   // 0x1101
        e.ClipDist = Info.Dude(nType, K.DI_clipdist);
        e.XHealth = Info.Dude(nType, K.DI_startHealth) * 16;
        e.XRespawn = 1;
        if (SeqData.Exists(Info.Dude(nType, K.DI_seqStartID)))
            Seq.Spawn(this, Info.Dude(nType, K.DI_seqStartID), e);
        AiInitSprite(e);
        return e;
    }

    public Sprite ActSpawnThing(double x, double y, double z, int nThingType)
    {
        var e = ActSpawnSprite(x, y, z, K.kStatThing, true);
        e.ClassName = "thing";
        e.Type = e.InitType = nThingType;
        e.XHealth = Info.Thing(nThingType, K.TI_startHealth) * 16;
        e.ClipDist = Info.Thing(nThingType, K.TI_clipdist);
        e.HitTag = Info.Thing(nThingType, K.TI_flags);
        if ((e.HitTag & 2) != 0)
            e.HitTag |= 4;
        e.Cstat |= Info.Thing(nThingType, K.TI_cstat);
        e.Picnum = Info.Thing(nThingType, K.TI_picnum);
        e.Shade = Info.Thing(nThingType, K.TI_shade);
        e.Pal = Info.Thing(nThingType, K.TI_pal);
        if (Info.Thing(nThingType, K.TI_xrepeat) != 0)
            e.XRepeat = Info.Thing(nThingType, K.TI_xrepeat);
        if (Info.Thing(nThingType, K.TI_yrepeat) != 0)
            e.YRepeat = Info.Thing(nThingType, K.TI_yrepeat);
        if (nThingType == K.kThingVoodooHead)
        {
            e.Data1 = e.Data2 = e.Data3 = e.Data4 = 0;
            e.XState = 1;
            e.TriggerOnce = 1;
            e.IsTriggered = 0;
        }
        else if (nThingType == K.kThingDroppedLifeLeech)
        {
            e.Data1 = e.Data2 = e.Data3 = e.Data4 = 0;
            e.XState = 1;
            e.TriggerOnce = 0;
            e.IsTriggered = 0;
        }
        else if (nThingType == K.kThingZombieHead)
        {
            e.Data1 = 8; e.Data2 = 0; e.Data3 = 0; e.Data4 = 318;
            e.TargetX = FrameClock + 180;
            e.XLocked = 1;
            e.XState = 1;
            e.TriggerOnce = 0;
            e.IsTriggered = 0;
        }
        else if (nThingType == K.kThingBloodBits || nThingType == K.kThingBloodChunks)
        {
            e.Data1 = nThingType == K.kThingBloodBits ? 19 : 8;
            e.Data2 = 0; e.Data3 = 0; e.Data4 = 319;
            e.TargetX = FrameClock + 180;
            e.XLocked = 1;
            e.XState = 1;
            e.TriggerOnce = 0;
            e.IsTriggered = 0;
        }
        else if (nThingType == K.kThingArmedTNTStick)
        {
            EvPostCallback(e, 0, K.kCallbackFXDynPuff);
            Sound3D(e, 450);
        }
        else if (nThingType == K.kThingArmedTNTBundle)
        {
            Sound3D(e, 450);
            EvPostCallback(e, 0, K.kCallbackFXDynPuff);
        }
        else if (nThingType == K.kThingArmedSpray)
            EvPostCallback(e, 0, K.kCallbackFXDynPuff);
        UpdateBox(e);
        return e;
    }

    public Sprite ActFireThing(Sprite e, double a2, double a3, double a4, int thingType, double a6)
    {
        double x = e.BX + Blood.MulScale30(a2, Blood.Cos(e.Ang + 512));
        double y = e.BY + Blood.MulScale30(a2, Blood.Sin(e.Ang + 512));
        double z = e.BZ + a3;
        x += Math.Floor(e.ClipDist * Blood.Cos(e.Ang) / 268435456);
        y += Math.Floor(e.ClipDist * Blood.Sin(e.Ang) / 268435456);
        if (HitScan(e, z, x - e.BX, y - e.BY, 0, e.ClipDist, Sim.Collision.Contents.MaskBlock) != -1)
        {
            x = HitX - Math.Floor(e.ClipDist * 2 * Blood.Cos(e.Ang) / 268435456);
            y = HitY - Math.Floor(e.ClipDist * 2 * Blood.Sin(e.Ang) / 268435456);
        }
        var t = ActSpawnThing(x, y, z, thingType);
        t.Owner = e;
        t.Ang = e.Ang;
        t.XVel = Blood.MulScale30(a6, Blood.Cos(t.Ang));
        t.YVel = Blood.MulScale30(a6, Blood.Sin(t.Ang));
        t.ZVel = Blood.MulScale14(a6, a4);
        t.XVel += Blood.Int(e.XVel / 2);
        t.YVel += Blood.Int(e.YVel / 2);
        t.ZVel += Blood.Int(e.ZVel / 2);
        return t;
    }

    void ActBuildMissile(Sprite m, Sprite src)
    {
        int t = m.Type;
        if (t == K.kMissileLifeLeechRegular)
            EvPostCallback(m, 0, K.kCallbackFXFlameLick);
        else if (t == K.kMissileTeslaAlt)
            EvPostCallback(m, 0, K.kCallbackFXTeslaAlt);
        else if (t == K.kMissilePukeGreen)
            Seq.Spawn(this, 29, m);
        else if (t == K.kMissileButcherKnife)
            m.Cstat |= 16;
        else if (t == K.kMissileTeslaRegular)
            Sound3D(m, 251);
        else if (t == K.kMissileEctoSkull)
        {
            Seq.Spawn(this, 2, m);
            Sound3D(m, 493);
        }
        else if (t == K.kMissileFireballNapalm)
        {
            Seq.Spawn(this, 61, m, NapalmSeqCallback);
            Sound3D(m, 441);
        }
        else if (t == K.kMissileFireball)
        {
            Seq.Spawn(this, 22, m, FireballSeqCallback);
            Sound3D(m, 441);
        }
        else if (t == K.kMissileFlameHound)
        {
            Seq.Spawn(this, 27, m);
            m.XVel += Blood.Int(src.XVel / 2) + Random2(69905);
            m.YVel += Blood.Int(src.YVel / 2) + Random2(69905);
            m.ZVel += Blood.Int(src.ZVel / 2) + Random2(69905);
        }
        else if (t == K.kMissileFireballCerberus)
        {
            Seq.Spawn(this, 61, m, FlameSeqCallbackA);
            Sound3D(m, 441);
        }
        else if (t == K.kMissileFireballTchernobog)
        {
            Seq.Spawn(this, 23, m, FlameSeqCallbackB);
            m.XVel += Blood.Int(src.XVel / 2) + Random2(69905);
            m.YVel += Blood.Int(src.YVel / 2) + Random2(69905);
            m.ZVel += Blood.Int(src.ZVel / 2) + Random2(69905);
        }
        else if (t == K.kMissileFlameSpray)
        {
            Seq.Spawn(this, Chance(32768) ? 0 : 1, m);
            m.XVel += src.XVel + Random2(69905);
            m.YVel += src.YVel + Random2(69905);
            m.ZVel += src.ZVel + Random2(69905);
        }
        else if (t == K.kMissileFlareAlt)
        {
            EvPostCallback(m, 30, K.kCallbackFXFlareBurst);
            EvPostCallback(m, 0, K.kCallbackFXFlareSpark);
            Sound3D(m, 422);
        }
        else if (t == K.kMissileFlareRegular)
        {
            EvPostCallback(m, 0, K.kCallbackFXFlareSpark);
            Sound3D(m, 422);
        }
        else if (t == K.kMissileLifeLeechAltSmall)
            EvPostCallback(m, 0, K.kCallbackFXArcSpark);
        else if (t == K.kMissileArcGargoyle)
            Sound3D(m, 252);
    }

    /// <summary>actFireMissile(pSprite, a2 side offset, a3 z offset, a4/a5/a6 direction (14-bit), nType).</summary>
    public Sprite ActFireMissile(Sprite e, double a2, double a3, double a4, double a5, double a6, int nType)
    {
        bool v4 = false;
        double x = e.BX + Blood.MulScale30(a2, Blood.Cos(e.Ang + 512));
        double y = e.BY + Blood.MulScale30(a2, Blood.Sin(e.Ang + 512));
        double z = e.BZ + a3;
        int mclip = MissileInfo(nType, K.MI_clipDist);
        double cdist = mclip + e.ClipDist;
        x += Math.Floor(cdist * Blood.Cos(e.Ang) / 268435456);
        y += Math.Floor(cdist * Blood.Sin(e.Ang) / 268435456);
        int nHit = HitScan(e, z, x - e.BX, y - e.BY, 0, cdist, Sim.Collision.Contents.MaskBlock);
        if (nHit != -1)
        {
            if (nHit == 3 || nHit == 0)
            {
                v4 = true;
                x = HitX - Blood.MulScale30(Blood.Cos(e.Ang), 16);
                y = HitY - Blood.MulScale30(Blood.Sin(e.Ang), 16);
            }
            else
            {
                x = HitX - Math.Floor(mclip * 2 * Blood.Cos(e.Ang) / 268435456);
                y = HitY - Math.Floor(mclip * 2 * Blood.Sin(e.Ang) / 268435456);
            }
        }
        var hitEnt = HitEnt;
        var m = ActSpawnSprite(x, y, z, K.kStatProjectile, true);
        m.ClassName = "missile";
        m.Type = m.InitType = nType;
        m.Shade = MissileInfo(nType, K.MI_shade);
        m.Pal = 0;
        m.ClipDist = mclip;
        m.HitTag = 1;
        m.XRepeat = MissileInfo(nType, K.MI_xrepeat);
        m.YRepeat = MissileInfo(nType, K.MI_yrepeat);
        m.Picnum = MissileInfo(nType, K.MI_picnum);
        m.Ang = (int)Blood.AngMask(e.Ang + MissileInfo(nType, K.MI_angleOfs));
        m.XVel = Blood.MulScale14(MissileInfo(nType, K.MI_velocity), a4);
        m.YVel = Blood.MulScale14(MissileInfo(nType, K.MI_velocity), a5);
        m.ZVel = Blood.MulScale14(MissileInfo(nType, K.MI_velocity), a6);
        m.Owner = e;
        m.Cstat |= 1;
        m.XTarget = null;
        EvPostCallback(m, 600, K.kCallbackRemove);
        ActBuildMissile(m, e);
        UpdateBox(m);
        if (m.Body != null)
            m.Body.Enabled = false;     // missiles move by tracing; they never block
        if (v4)
        {
            HitEnt = hitEnt;
            if (nHit == 3)
                HitType = 3;
            ActImpactMissile(m, nHit);
            return null;
        }
        return m;
    }

    // ---------------------------------------------------------------- respawn
    public int ActGetRespawnTime(Sprite e)
    {
        if (!e.HasX)
            return -1;
        int t = e.Type;
        if (Info.IsDude(t) && e is not Player)
            return e.XRespawn == 2 || (e.XRespawn != 1 && MonsterSettings == 2) ? MonsterRespawnTime : -1;
        if (t >= K.kItemWeaponBase && t < K.kItemWeaponMax)
        {
            if (e.XRespawn == 3 || WeaponSettings == 1)
                return 0;
            if (e.XRespawn != 1 && WeaponSettings != 0)
                return WeaponRespawnTime;
            return -1;
        }
        if (t >= K.kItemAmmoBase && t < K.kItemAmmoMax)
            return e.XRespawn == 2 || (e.XRespawn != 1 && WeaponSettings != 0) ? WeaponRespawnTime : -1;
        if (t >= K.kItemBase && t < K.kItemMax)
        {
            if (e.XRespawn == 3 && GameType == K.kGameTypeCoop)
                return 0;
            if (e.XRespawn == 2 || (e.XRespawn != 1 && ItemSettings != 0))
            {
                if (t == K.kItemShadowCloak || t == K.kItemTwoGuns || t == K.kItemReflectShots)
                    return SpecialRespawnTime;
                if (t == K.kItemDeathMask)
                    return SpecialRespawnTime * 2;
                return ItemRespawnTime;
            }
        }
        return -1;
    }

    public bool ActCheckRespawn(Sprite e)
    {
        if (!e.HasX)
            return false;
        int t = ActGetRespawnTime(e);
        if (t < 0)
            return false;
        e.RespawnPending = 1;
        if (Info.IsThing(e.Type))
        {
            e.RespawnPending = 3;
            if (e.Type == K.kThingTNTBarrel)
                e.Cstat |= Cstat.Invisible;
        }
        if (t > 0)
        {
            if (e.RespawnPending == 1)
                t = (int)Blood.MulScale16(t, 40960);
            e.RespawnStat = e.StatNum;
            ActPostSprite(e, K.kStatRespawn);
            e.HitTag |= K.kHitagRespawn;
            if (!Info.IsDude(e.Type))
            {
                e.Cstat &= ~257;
                e.Origin = e.BaseOrigin;
                UpdateBox(e);
            }
            EvPostCallback(e, t, K.kCallbackRespawn);
        }
        return true;
    }

    void SpriteRespawnPending(Sprite e) => EvPostCallback(e, MonsterRespawnTime, K.kCallbackRespawn);

    // ---------------------------------------------------------------- actFireVector
    static bool ActCanSplat(XObject e) => e == null;   // brush entities (doors, movers) don't take decals

    public void ActFireVector(Sprite pShooter, double a2, double a3, double a4, double a5, double a6, int vectorType)
    {
        double nRange = VectorRangeOverride >= 0 ? VectorRangeOverride : VectorData(vectorType, K.VI_maxDist);
        if (vectorType == K.kVectorTchernobogBurn && VectorBurnRange > 0)
            nRange = VectorBurnRange;
        double sx = pShooter.BX, sy = pShooter.BY, sz = pShooter.BZ;
        int nHit = VectorScan(pShooter, a2, a3, a4, a5, a6, nRange);
        var hitNormal = HitNormal;
        if (nHit == 3)
        {
            var ps = (Sprite)HitEnt;
            if (!FriendlyFire && IsTargetTeammate(pShooter, ps))
                return;
            if (ps is Player && PowerupCheck(ps, K.kPwUpReflectShots) != 0)
            {
                HitEnt = pShooter;
                HitX = sx;
                HitY = sy;
                HitZ = sz;
            }
        }
        double x = HitX - Blood.MulScale14(a4, 16);
        double y = HitY - Blood.MulScale14(a5, 16);
        double z = HitZ - Blood.MulScale14(a6, 256);
        int nSurf = K.kSurfNone;
        if (nRange == 0 || Blood.ApproxDist(HitX - sx, HitY - sy) < nRange)
        {
            if (nHit == 1 || nHit == 2)
                nSurf = HitSurf;
            else if (nHit == 0 || nHit == 4)
            {
                nSurf = HitSurf;
                if (HitEnt is Wall hw && hw.HasX && hw.XVector != 0)
                    TrTriggerWall(hw, K.kCmdWallImpact, pShooter);
                if (nHit == 0 && ActCanSplat(HitEnt))
                {
                    int fx1 = VectorSurfHit(vectorType, nSurf, 0);
                    if (fx1 >= 0)
                    {
                        var pFX = FxSpawn(fx1, x, y, z, 0);
                        if (pFX != null)
                        {
                            pFX.Ang = Blood.GetAngle(hitNormal.X, -hitNormal.Y);
                            pFX.Cstat |= 16;
                        }
                    }
                }
            }
            else if (nHit == 3)
            {
                var ps = (Sprite)HitEnt;
                nSurf = Tiles.SurfType(ps.Picnum);
                x -= Blood.MulScale14(a4, 112);
                y -= Blood.MulScale14(a5, 112);
                z -= Blood.MulScale14(a6, 1792);
                int shift = vectorType == K.kVectorTine && ps is not Player ? 8 : 16;
                ActDamageSprite(pShooter, ps, VectorData(vectorType, K.VI_dmgType), VectorData(vectorType, K.VI_dmg) * shift);
                if (!ps.Removed)
                {
                    if (ps.HasX && ps.XVector != 0)
                        TrTriggerSprite(ps, K.kCmdSpriteImpact, pShooter);
                    int mass = ps.StatNum == K.kStatThing && Info.IsThing(ps.Type) ? Info.Thing(ps.Type, K.TI_mass)
                        : ps.StatNum == K.kStatDude ? DudeMass(ps) : 0;
                    if (ps.StatNum == K.kStatThing && Info.IsThing(ps.Type) || ps.StatNum == K.kStatDude)
                    {
                        if (mass > 0 && VectorData(vectorType, K.VI_impulse) != 0)
                        {
                            double t2 = Blood.Int(VectorData(vectorType, K.VI_impulse) * 256.0 / mass);
                            ps.XVel += Blood.MulScale16(a4, t2);
                            ps.YVel += Blood.MulScale16(a5, t2);
                            ps.ZVel += Blood.MulScale16(a6, t2);
                        }
                        if (VectorData(vectorType, K.VI_burnTime) != 0)
                        {
                            if (ps.BurnTime == 0)
                                EvPostCallback(ps, 0, K.kCallbackFXFlameLick);
                            ActBurnSprite(pShooter, ps, VectorData(vectorType, K.VI_burnTime));
                        }
                    }
                    if (ps.StatNum == K.kStatDude)
                    {
                        if (Chance(VectorData(vectorType, K.VI_fxChance)))
                        {
                            // blood spray onto a nearby wall
                            int t = VectorData(19, K.VI_maxDist);
                            a4 += Random3(4000);
                            a5 += Random3(4000);
                            a6 += Random3(4000);
                            if (HitScan(ps, HitZ, a4, a5, a6, t) == 0)
                            {
                                if (Blood.ApproxDist(HitX - ps.BX, HitY - ps.BY) <= t && ActCanSplat(HitEnt))
                                {
                                    double wx = HitX - Blood.MulScale14(a4, 16);
                                    double wy = HitY - Blood.MulScale14(a5, 16);
                                    double wz = HitZ - Blood.MulScale14(a6, 256);
                                    int ns = HitSurf;
                                    int f2 = VectorSurfHit(19, ns, 1), f3 = VectorSurfHit(19, ns, 2);
                                    Sprite pFX = null;
                                    if (f2 > -1 & (f3 == -1 | Chance(16384)))
                                        pFX = FxSpawn(f2, wx, wy, wz, 0);
                                    else if (f3 > -1)
                                        pFX = FxSpawn(f3, wx, wy, wz, 0);
                                    if (pFX != null)
                                    {
                                        pFX.ZVel = 8738;
                                        pFX.Ang = Blood.GetAngle(HitNormal.X, -HitNormal.Y);
                                        pFX.Cstat |= 16;
                                    }
                                }
                            }
                        }
                        for (int i = 0; i < VectorData(vectorType, K.VI_bloodSplats); i++)
                            if (Chance(VectorData(vectorType, K.VI_splatChance)))
                                FxSpawnBlood(ps, VectorData(vectorType, K.VI_dmg) * 16);
                    }
                    // a modern map's debris sprites: pushed by the shot, set alight, a thing among them hurt
                    if (IsModern && _physSprites.Count > 0 && VectorData(vectorType, K.VI_impulse) != 0 && ps.HasX
                        && (ps.PhysAttr & KM.kPhysDebrisVector) != 0)
                    {
                        double impulse = Blood.Int(VectorData(vectorType, K.VI_impulse) * 64.0 / System.Math.Max(ps.Mass?.Mass ?? 0, 10));
                        ps.XVel += Blood.MulScale16(a4, impulse);
                        ps.YVel += Blood.MulScale16(a5, impulse);
                        ps.ZVel += Blood.MulScale16(a6, impulse);
                        if (VectorData(vectorType, K.VI_burnTime) != 0)
                        {
                            if (ps.BurnTime == 0)
                                EvPostCallback(ps, 0, K.kCallbackFXFlameLick);
                            ActBurnSprite(pShooter, ps, VectorData(vectorType, K.VI_burnTime));
                        }
                        if (IsThingSprite(ps) && !ps.Removed)
                        {
                            int stat = ps.StatNum;
                            ps.StatNum = K.kStatThing;      // (for the damage: a debris thing is a decoration)
                            ActDamageSprite(pShooter, ps, VectorData(vectorType, K.VI_dmgType), VectorData(vectorType, K.VI_dmg) * 16);
                            if (!ps.Removed && ps.StatNum == K.kStatThing)
                                ps.StatNum = stat == K.kStatThing ? stat : K.kStatDecoration;
                        }
                    }
                }
            }
        }
        int fx2 = VectorSurfHit(vectorType, nSurf, 1), fx3 = VectorSurfHit(vectorType, nSurf, 2), snd = VectorSurfHit(vectorType, nSurf, 3);
        // (a modern map: the hit's effects are the shooter's, so patrolling dudes hear its sound as the shooter's)
        if (fx2 >= 0 && FxSpawn(fx2, x, y, z, 0) is { } hitFx2 && IsModern)
            hitFx2.Owner = pShooter;
        if (fx3 >= 0 && FxSpawn(fx3, x, y, z, 0) is { } hitFx3 && IsModern)
            hitFx3.Owner = pShooter;
        if (snd >= 0)
            Sound3DAt(B2Q(x, y, z), snd);
    }

    // ---------------------------------------------------------------- seq callbacks
    void SpawnFxFollow(Sprite e, int fxId)
    {
        var fx = FxSpawn(fxId, e.BX, e.BY, e.BZ, 0);
        if (fx != null)
        {
            fx.XVel = e.XVel;
            fx.YVel = e.YVel;
            fx.ZVel = e.ZVel;
        }
    }

    void FireballSeqCallback(Sprite e) => SpawnFxFollow(e, K.FX_11);
    void NapalmSeqCallback(Sprite e) => SpawnFxFollow(e, K.FX_12);
    void FlameSeqCallbackA(Sprite e) => SpawnFxFollow(e, K.FX_32);
    void FlameSeqCallbackB(Sprite e) => SpawnFxFollow(e, K.FX_33);

    void TreeToGibCallback(Sprite e)
    {
        e.Type = K.kThingObjectExplode;
        e.XState = 1;
        e.Data1 = 15;
        e.Data2 = 0;
        e.Data3 = 0;
        e.XHealth = Info.Thing(K.kThingObjectExplode, K.TI_startHealth);
        e.Data4 = 312;
        e.Cstat |= 257;
        UpdateBox(e);
    }

    void DudeToGib(Sprite e, int d1)
    {
        e.Type = K.kThingBloodChunks;
        e.Data1 = d1;
        e.Data2 = 0;
        e.Data3 = 0;
        e.XHealth = Info.Thing(K.kThingBloodChunks, K.TI_startHealth);
        e.Data4 = 319;
        e.TriggerOnce = 0;
        e.IsTriggered = 0;
        e.XLocked = 0;
        e.TargetX = FrameClock;
        e.XState = 1;
    }

    void DudeToGibCallback1(Sprite e) => DudeToGib(e, 8);
    void DudeToGibCallback2(Sprite e) => DudeToGib(e, 3);

    void MakeSplash(Sprite e)
    {
        e.HitTag &= ~2;
        e.SetBZ(e.BZ - 1024);
        // a modern map's drip: the sound its generator names (data2; below 0: none)
        int own = IsModern ? (e.Owner != null && e.Owner.HasX ? e.Owner.Data2 : e.Data2) : 0;
        if (e.Type == K.kThingDripWater)
        {
            if (e.FlorSurf == K.kSurfWater)
            {
                Seq.Spawn(this, 6, e);
                if (own >= 0)
                    Sound3D(e, own == 0 ? 356 : own, -1);
            }
            else
            {
                Seq.Spawn(this, 7, e);
                if (own >= 0)
                    Sound3D(e, own == 0 ? 354 : own, -1);
            }
        }
        else if (e.Type == K.kThingDripBlood)
        {
            Seq.Spawn(this, 8, e);
            if (own >= 0)
                Sound3D(e, own == 0 ? 354 : own, -1);
        }
    }
}
