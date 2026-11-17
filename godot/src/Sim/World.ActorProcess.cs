using System;
using System.Collections.Generic;
using Chosen.Data;

namespace Chosen.Sim;

/// <summary>Per-frame sprite processing (actor.cpp actProcessSprites; Blood3D's server/actor_process.qc).</summary>
public sealed partial class World
{
    public void ActBurnSprite(Sprite src, Sprite e, double nTime)
    {
        if (e == null || !e.HasX)
            return;
        double lim = e.StatNum == K.kStatDude ? 2400 : 1200;
        e.BurnTime = (int)Blood.ClipHigh(e.BurnTime + nTime, lim);
        e.BurnSource = src;
    }

    void ActTeslaHit(Sprite pMissile, int a2)
    {
        double x = pMissile.BX, y = pMissile.BY, z = pMissile.BZ;
        const double nDist = 300;
        var nOwner = pMissile.Owner;
        bool v4 = !(a2 == 3 && HitEnt is Sprite hs && hs.StatNum == K.kStatDude);
        foreach (var e in Sprites.ToArray())
        {
            if (e.StatNum != K.kStatDude || e.Removed)
                continue;
            if (e != nOwner || v4)
            {
                if ((e.HitTag & 32) != 0)
                    continue;
                if (CheckProximity(e, x, y, z, nDist))
                {
                    double nDamage = Blood.Sar(nDist - Blood.Sar(Blood.KSqrt(x - e.BX, y - e.BY), 4) + 20, 1);
                    if (nDamage < 0)
                        nDamage = 10;
                    if (e == nOwner)
                        nDamage = Blood.Int(nDamage / 2);
                    ActDamageSprite(nOwner, e, K.kDamageTesla, nDamage * 16);
                }
            }
        }
        foreach (var e in Sprites.ToArray())
        {
            if (e.StatNum != K.kStatThing || e.Removed || (e.HitTag & 32) != 0)
                continue;
            if (CheckProximity(e, x, y, z, nDist) && e.XLocked == 0)
            {
                double nDamage = nDist - Blood.Sar(Blood.KSqrt(x - e.BX, y - e.BY), 4) + 20;
                if (nDamage < 0)
                    nDamage = 20;
                ActDamageSprite(nOwner, e, K.kDamageTesla, nDamage * 16);
            }
        }
    }

    /// <summary>Conveyor (panning) floors: sprites standing on a panning sector get pushed.</summary>
    void ActApplyPan(Sprite e, Sector sec)
    {
        if (sec == null || sec.PanVel == 0)
            return;
        // Blood pushes only what reaches its sector's own floor (getflorzofslope <= bottom): one on the bridge over
        // E3M6's flowing water stands far above the river's floor (on its sprites, or by the bank's floor beside it)
        if (Map != null && SpriteBSector(e) is int b && b >= 0)
        {
            ZsOfSlope(b, e.BX, e.BY, out _, out double fz);
            if (fz > Extents(e).bottom)
                return;
        }
        else
        {
            if (e.FlorHit == 0 && e.XHeight > 0)
                return;
            if (e.FlorHit == Hit.Sprite)
                return;
        }
        double speed = 0;
        if (sec.PanAlways != 0 || sec.XState != 0 || sec.XBusy != 0)
        {
            speed = sec.PanVel * 512;
            if (sec.PanAlways == 0 && sec.XBusy != 0)
                speed = Blood.MulScale16(speed, sec.XBusy);
        }
        // a relative floor (floorstat 64) pushes along panAngle turned by its first wall's angle + 512
        int angle = (sec.PanAngle + sec.PanPushRot) & 2047;
        e.XVel += Blood.MulScale30(speed, Blood.Cos(angle));
        e.YVel += Blood.MulScale30(speed, Blood.Sin(angle));
    }

    /// <summary>
    /// A status list the way Blood3D's QuakeC goes through it, in entity slot order: for (e = findfloat(world,
    /// statnum, n); e; e = n) with n = findfloat(e, statnum, n) taken before e is handled. A sprite spawned meanwhile
    /// in a later slot is visited too; one in an earlier slot, or between e and n, is not.
    /// </summary>
    IEnumerable<Sprite> StatList(int statnum)
    {
        var e = NextInStat(-1, statnum);
        while (e != null)
        {
            var n = NextInStat(e.Edict, statnum);
            yield return e;
            while (n != null && n.Removed)
                n = NextInStat(n.Edict, statnum);
            e = n;
        }
    }

    /// <summary>findfloat(after, statnum, n): the first live sprite of the list in a later slot.</summary>
    Sprite NextInStat(int afterEdict, int statnum)
    {
        int lo = 0, hi = Sprites.Count;
        while (lo < hi)
        {
            int mid = (lo + hi) >> 1;
            if (Sprites[mid].Edict <= afterEdict)
                lo = mid + 1;
            else
                hi = mid;
        }
        for (int i = lo; i < Sprites.Count; i++)
        {
            var s = Sprites[i];
            if (!s.Removed && s.StatNum == statnum)
                return s;
        }
        return null;
    }

    /// <summary>A modern map's explosion: the debris sprites in reach are pushed and damaged, the sprites with the
    /// Impact flag triggered.</summary>
    void ModernExplosion(Sprite e, Sprite nOwner, int nType, double x, double y, double z, int radius, HashSet<int> sectmap)
    {
        if (ExplodeInfo(nType, K.EI_dmgType) != 0)
        {
            foreach (var d in _physSprites.ToArray())
            {
                if (d.Removed || (d.HitTag & K.kHitagFree) != 0 || IsOnRespawn(d))
                    continue;
                if (sectmap != null && !sectmap.Contains(SpriteBSector(d)))
                    continue;
                if (CheckProximity(d, x, y, z, radius))
                    DebrisConcuss(nOwner, d, x, y, z, ExplodeInfo(nType, K.EI_dmgType));
            }
        }
        for (int i = _impactSprites.Count - 1; i >= 0; i--)
        {
            if (i >= _impactSprites.Count)
                continue;
            var s = _impactSprites[i];
            if (s.Removed || (s.HitTag & K.kHitagFree) != 0 || !s.HasX || s.XImpact == 0 || s.IsTriggered != 0)
            {
                _impactSprites.RemoveAt(i);
                continue;
            }
            if (IsOnRespawn(s) || (sectmap != null && !sectmap.Contains(SpriteBSector(s))))
                continue;
            if (CheckProximity(s, x, y, z, radius))
                TrTriggerSprite(s, K.kCmdSpriteImpact, (XObject)nOwner ?? e);
        }
    }

    void ActProcessSprites()
    {
        if (IsModern)
            NnExtProcessSuperSprites();
        // ---- things: burning, proximity
        foreach (var e in StatList(K.kStatThing))
        {
            if (e.Removed || (e.HitTag & 32) != 0 || !e.HasX || e.RespawnPending > 0)
                continue;
            if (e.Type == K.kThingBloodBits || e.Type == K.kThingBloodChunks || e.Type == K.kThingZombieHead)
                if (e.XLocked != 0 && FrameClock >= e.TargetX)
                    e.XLocked = 0;
            if (e.BurnTime > 0)
            {
                e.BurnTime = (int)Blood.ClipLow(e.BurnTime - GameClock.TicsPerFrame, 0);
                ActDamageSprite(e.BurnSource, e, K.kDamageBurn, 8);
                if (e.Removed)
                    continue;
            }
            if (e.XProximity != 0)
            {
                // (a modern map: no proximity for a locked or already triggered one-shot thing)
                if (IsModern && (e.XLocked != 0 || e.IsTriggered != 0))
                    continue;
                if (e.Type == K.kThingDroppedLifeLeech)
                    e.XTarget = null;
                var dudes = System.Linq.Enumerable.ToArray(StatList(K.kStatDude));
                for (int i = 0; i < dudes.Length; i++)
                {
                    var e2 = dudes[i];
                    if (e2.Removed || (e2.HitTag & 32) != 0 || e2.XHealth <= 0)
                        continue;
                    double proxyDist = 96;
                    if (IsModern)
                    {
                        // players only when the thing says so; an enemy's life leech looks far, for its target alone;
                        // a proximity TNT of the modern kind answers players only
                        if (e.Type != K.kThingDroppedLifeLeech && e.DudeLockout != 0 && e2 is not Player)
                            continue;
                        if (e.Type == KM.kModernThingEnemyLifeLeech)
                        {
                            proxyDist = 512;
                            if (e.XTarget != e2)
                                continue;
                        }
                        else if (e.Type == KM.kModernThingTNTProx && e2 is not Player)
                            continue;
                    }
                    if (e.Type == K.kThingDroppedLifeLeech && e.XTarget == null)
                    {
                        if (e2 == e.Owner || e2.Type == K.kDudeZombieAxeBuried || e2.Type == K.kDudeRat || e2.Type == K.kDudeBat)
                            continue;
                        if (GameType == K.kGameTypeCoop && e2 is Player)
                            continue;
                        proxyDist = 512;
                    }
                    if (CheckProximity(e2, e.BX, e.BY, e.BZ, proxyDist))
                    {
                        if (e.Type == K.kThingDroppedLifeLeech)
                        {
                            if ((Chance(16384) || i == dudes.Length - 1) && (e2.Cstat & 257) != 0)
                                e.XTarget = e2;
                            else
                                continue;
                        }
                        if (e.Type == KM.kModernThingTNTProx)
                            e.Pal = 0;
                        e.Owner ??= e2;
                        TrTriggerSprite(e, K.kCmdSpriteProximity, e2);
                        if (e.Removed)
                            break;
                    }
                }
            }
        }
        Core.Perf.Mark("a.thingprox");
        // ---- things: movement
        foreach (var e in StatList(K.kStatThing))
        {
            if (e.Removed || (e.HitTag & 32) != 0)
                continue;
            var sec = SpriteSector(e);
            if (sec != null && sec.PanVel != 0 && (sec.PanAlways != 0 || sec.XState != 0 || sec.XBusy != 0))
            {
                if ((Info.Thing(e.Type, K.TI_flags) & 1) != 0)
                    e.HitTag |= 1;
                if ((Info.Thing(e.Type, K.TI_flags) & 2) != 0)
                    e.HitTag |= 4;
            }
            if ((e.HitTag & 3) == 0)
                continue;
            if (sec != null && sec.PanVel != 0)
                ActApplyPan(e, sec);
            ActAirDrag(e, 128);
            if (((e.SprIndex >> 8) & 15) == (Frame & 15) && (e.HitTag & 2) != 0)
                e.HitTag |= 4;
            if ((e.HitTag & 4) == 0 && e.XVel == 0 && e.YVel == 0 && e.ZVel == 0)
                continue;
            long tm = Core.Perf.Start();
            int nHit = MoveThing(e);
            Core.Perf.Add("movething", tm);
            if (e.Removed || nHit == 0 || !e.HasX)
                continue;
            if (e.XImpact != 0)
                TrTriggerSprite(e, K.kCmdOff, nHit == K.kHitSprite ? e.HitEnt as XObject : null);
            if (e.Removed)
                continue;
            if (e.Type == K.kThingDripWater || e.Type == K.kThingDripBlood)
                MakeSplash(e);
            else if (e.Type == K.kThingBone)
            {
                Seq.Spawn(this, 24, e);
                if (nHit == K.kHitSprite && e.HitEnt is Sprite hs)
                    ActDamageSprite(e.Owner, hs, K.kDamageFall, 12);
            }
            else if (e.Type == K.kThingPodGreenBall)
            {
                if (nHit == K.kHitSector)
                    ActRadiusDamage(e.Owner, e.BX, e.BY, e.BZ, 200, 1, 20, K.kDamageExplode, 6, 0);
                else if (nHit == K.kHitSprite && e.HitEnt is Sprite hs)
                    ActDamageSprite(e.Owner, hs, K.kDamageFall, 12);
                EvPostCallback(e, 0, K.kCallbackFXPodBloodSplat);
            }
            else if (e.Type == K.kThingPodFireBall)
                ActExplodeSprite(e);
            else if (e.Type == KM.kModernThingThrowableRock)
            {
                Seq.Spawn(this, 24, e);
                if (nHit == K.kHitSprite && e.HitEnt is Sprite rs)
                {
                    e.XRepeat = 32;
                    e.YRepeat = 32;
                    ActDamageSprite(e.Owner, rs, K.kDamageFall, e.Data1);
                }
            }
        }
        Core.Perf.Mark("a.thingmove");
        // ---- projectiles
        foreach (var e in StatList(K.kStatProjectile))
        {
            if (e.Removed || (e.HitTag & 32) != 0)
                continue;
            int nHit = MoveMissile(e);
            if (nHit >= 0)
                ActImpactMissile(e, nHit);
        }
        Core.Perf.Mark("a.proj");
        // ---- explosions
        foreach (var e in StatList(K.kStatExplosion))
        {
            if (e.Removed || (e.HitTag & 32) != 0)
                continue;
            var nOwner = e.Owner;
            int nType = e.Type;
            double x = e.BX, y = e.BY, z = e.BZ;
            int radius = ExplodeInfo(nType, K.EI_radius);
            // (a modern map: an explosion's own radius, its data4)
            if (IsModern && e.HasX && e.Data4 > 0)
                radius = e.Data4;
            HashSet<int> sectmap = null;
            // the sectors the blast reaches and the impact walls on the way (GetClosestSpriteSectors)
            var xwalls = new List<int>();
            int ns = SpriteBSector(e);
            if (ns >= 0)
            {
                sectmap = ClosestSpriteSectors(ns, x, y, radius, xwalls, nOwner != null && Info.IsDude(nOwner.Type));
                foreach (int w in xwalls)
                    TrTriggerWall(_xwall[w], K.kCmdWallImpact, (XObject)nOwner ?? e);
            }
            foreach (var e2 in StatList(K.kStatDude))
            {
                if (e2.Removed || (e2.HitTag & 32) != 0)
                    continue;
                if (sectmap != null && !sectmap.Contains(SpriteBSector(e2)))
                    continue;
                if (e.Data1 == 0 || !CheckProximity(e2, x, y, z, radius))
                    continue;
                if (ExplodeInfo(nType, K.EI_dmg) != 0 && e.TargetX == 0)
                {
                    e.TargetX = 1;
                    ActDamageSprite(nOwner, e2, K.kDamageFall, (ExplodeInfo(nType, K.EI_dmg) + Random(ExplodeInfo(nType, K.EI_dmgRng))) * 16);
                }
                if (e2.Removed)
                    continue;
                if (ExplodeInfo(nType, K.EI_dmgType) != 0)
                    ConcussSprite(nOwner, e2, x, y, z, ExplodeInfo(nType, K.EI_dmgType));
                if (e2.Removed)
                    continue;
                if (ExplodeInfo(nType, K.EI_burnTime) != 0)
                {
                    if (e2.BurnTime == 0)
                        EvPostCallback(e2, 0, K.kCallbackFXFlameLick);
                    ActBurnSprite(e.Owner, e2, ExplodeInfo(nType, K.EI_burnTime) * 4);
                }
            }
            foreach (var e2 in StatList(K.kStatThing))
            {
                if (e2.Removed || (e2.HitTag & 32) != 0)
                    continue;
                if (sectmap != null && !sectmap.Contains(SpriteBSector(e2)))
                    continue;
                if (e.Data1 == 0 || !CheckProximity(e2, x, y, z, radius) || e2.XLocked != 0)
                    continue;
                if (ExplodeInfo(nType, K.EI_dmgType) != 0)
                    ConcussSprite(nOwner, e2, x, y, z, ExplodeInfo(nType, K.EI_dmgType));
                if (e2.Removed)
                    continue;
                if (ExplodeInfo(nType, K.EI_burnTime) != 0)
                {
                    if (e2.Type == K.kThingTNTBarrel && e2.BurnTime == 0)
                        EvPostCallback(e2, 0, K.kCallbackFXFlameLick);
                    ActBurnSprite(e.Owner, e2, ExplodeInfo(nType, K.EI_burnTime) * 4);
                }
            }
            // screen flicker for players
            foreach (var pl in Players)
            {
                double dx = Blood.Sar(x - pl.BX, 4), dy = Blood.Sar(y - pl.BY, 4), dz = Blood.Sar(z - pl.BZ, 8);
                double nDist = dx * dx + dy * dy + dz * dz + 262144;
                pl.FlickerEffect += Blood.DivScale16(e.Data2, nDist);
            }
            if (IsModern && e.Data1 != 0)
                ModernExplosion(e, nOwner, nType, x, y, z, radius, sectmap);
            // (a modern map's explosion with flag 1 stays: an explosion generator put in the map by hand)
            if (IsModern && (e.HitTag & KM.Flag1) != 0)
                continue;
            e.Data1 = (int)Blood.ClipLow(e.Data1 - GameClock.TicsPerFrame, 0);
            e.Data2 = (int)Blood.ClipLow(e.Data2 - GameClock.TicsPerFrame, 0);
            e.Data3 = (int)Blood.ClipLow(e.Data3 - GameClock.TicsPerFrame, 0);
            if (e.Data1 == 0 && e.Data2 == 0 && e.Data3 == 0 && Seq.Status(e) < 0)
                ActPostSprite(e, K.kStatFree);
        }
        Core.Perf.Mark("a.expl");
        // ---- traps
        foreach (var e in StatList(K.kStatTraps))
        {
            if (e.Removed || (e.HitTag & 32) != 0 || !e.HasX)
                continue;
            if (e.Type == K.kTrapSawCircular)
                e.Data2 = (int)Blood.ClipLow(e.Data2 - GameClock.TicsPerFrame, 0);
            else if (e.Type == K.kTrapFlame && e.XState != 0 && Seq.Status(e) < 0)
            {
                double x = e.BX, y = e.BY, z = e.BZ;
                double t = Blood.Int(e.Data1 * 8388608.0 / 120);
                double dx = Blood.MulScale30(t, Blood.Cos(e.Ang)), dy = Blood.MulScale30(t, Blood.Sin(e.Ang));
                for (int i = 0; i < 2; i++)
                {
                    var fx = FxSpawn(K.FX_32, x, y, z, 0);
                    if (fx != null)
                    {
                        fx.XVel = dx + Random2(34952);
                        fx.YVel = dy + Random2(34952);
                        fx.ZVel = Random2(34952);
                    }
                    x += Blood.Sar(Blood.Int(dx / 2), 12);
                    y += Blood.Sar(Blood.Int(dy / 2), 12);
                }
                dy = Blood.Sar(Blood.Sin(e.Ang), 16);
                dx = Blood.Sar(Blood.Cos(e.Ang), 16);
                VectorBurnRange = e.Data1 * 512;
                ActFireVector(e, 0, 0, dx, dy, Random2(34952), K.kVectorTchernobogBurn);
                VectorBurnRange = 0;
            }
        }
        Core.Perf.Mark("a.traps");
        // ---- dudes: burning, cerberus head loss, proximity, player status
        foreach (var e in StatList(K.kStatDude))
        {
            if (e.Removed || (e.HitTag & 32) != 0 || !e.HasX)
                continue;
            if (e.BurnTime > 0 || IsBurningDude(e))
            {
                int t = e.Type;
                if (!(t == K.kDudeBurningInnocent || t == K.kDudeBurningCultist || t == K.kDudeBurningZombieAxe || t == K.kDudeBurningZombieButcher))
                    e.BurnTime = (int)Blood.ClipLow(e.BurnTime - GameClock.TicsPerFrame, 0);
                ActDamageSprite(e.BurnSource, e, K.kDamageBurn, 8);
                if (e.Removed)
                    continue;
            }
            if (e.Type == K.kDudeCerberusTwoHead && e.XHealth <= 0 && Seq.Status(e) < 0)
            {
                e.XHealth = Info.Dude(K.kDudeCerberusOneHead, K.DI_startHealth) * 16;
                e.Type = K.kDudeCerberusOneHead;
                if (e.XTarget != null)
                    AiSetTarget(e, e.XTarget);
                AiActivateDude(e);
            }
            if (e.XProximity != 0 && e.IsTriggered == 0)
            {
                foreach (var e2 in StatList(K.kStatDude))
                {
                    if (e2.Removed || (e2.HitTag & 32) != 0)
                        continue;
                    if (e2.XHealth > 0 && e2 is Player && CheckProximity(e2, e.BX, e.BY, e.BZ, 128))
                        TrTriggerSprite(e, K.kCmdSpriteProximity, e2);
                }
            }
            if (e is Player pp)
                PlayerProcessStatus(pp);
            if (e.Removed)
                continue;
            ProcessTouchObjects(e);
        }
        Core.Perf.Mark("a.dudes");
        // ---- dudes: movement
        foreach (var e in StatList(K.kStatDude))
        {
            if (e.Removed || (e.HitTag & 32) != 0)
                continue;
            var sec = SpriteSector(e);
            if (sec != null && sec.PanVel != 0)
                ActApplyPan(e, sec);
            ActAirDrag(e, (sec != null && sec.SUnderwater != 0) || e.Medium != K.kMediumNormal ? 5376 : 128);
            if (e is Player) Log?.Append($" [before movedude vel {e.XVel} {e.YVel}]");
            if ((e.HitTag & 4) != 0 || e.XVel != 0 || e.YVel != 0 || e.ZVel != 0 || StandingOnMover(e))
                MoveDude(e);
        }
        Core.Perf.Mark("a.dudemove");
        // ---- flares stuck in flesh
        foreach (var e in StatList(K.kStatFlare))
        {
            if (e.Removed || (e.HitTag & 32) != 0)
                continue;
            var e2 = e.XTarget;
            if (e2 == null || e2.Removed || e2.StatNum == K.kStatFree || !(e2.HasX && e2.XHealth > 0))
            {
                GibSprite(e, K.GIBTYPE_17);
                ActPostSprite(e, K.kStatFree);
                continue;
            }
            double x = e2.BX + Blood.MulScale30R(Blood.Cos(e.GoalAng + e2.Ang), e2.ClipDist * 2);
            double y = e2.BY + Blood.MulScale30R(Blood.Sin(e.GoalAng + e2.Ang), e2.ClipDist * 2);
            double z = e2.BZ + e.TargetZ;
            e.Origin = B2Q(x, y, z);
            e.XVel = e2.XVel;
            e.YVel = e2.YVel;
            e.ZVel = e2.ZVel;
        }
        Core.Perf.Mark("a.flares");
        AiProcessDudes();
        Core.Perf.Mark("a.ai");
        FxProcess();
        Core.Perf.Mark("a.fx");
    }
}
