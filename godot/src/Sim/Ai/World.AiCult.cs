using System;
using Chosen.Data;

namespace Chosen.Sim;

/// <summary>Cultist AI (aicult.cpp, via Blood3D's server/ai_cult.qc).</summary>
public sealed partial class World
{
    void cult_TommySeqCallback(Sprite e)
    {
        double dx = Blood.Sar(Blood.Cos(e.Ang), 16) + Random3((5 - Skill) * 1000);
        double dy = Blood.Sar(Blood.Sin(e.Ang), 16) + Random3((5 - Skill) * 1000);
        double dz = e.DudeSlope + Random3((5 - Skill) * 500);
        ActFireVector(e, 0, 0, dx, dy, dz, K.kVectorBullet);
        Sound3D(e, 4001, -1);
    }

    void cult_TeslaSeqCallback(Sprite e)
    {
        if (!Chance(CultTeslaFireChance(Skill)))
            return;
        double dx = Blood.Sar(Blood.Cos(e.Ang), 16) + Random3((5 - Skill) * 1000);
        double dy = Blood.Sar(Blood.Sin(e.Ang), 16) + Random3((5 - Skill) * 1000);
        double dz = e.DudeSlope + Random3((5 - Skill) * 500);
        ActFireMissile(e, 0, 0, dx, dy, dz, K.kMissileTeslaRegular);
        Sound3D(e, 470, -1);
    }

    void cult_ShotSeqCallback(Sprite e)
    {
        double dx = Blood.Sar(Blood.Cos(e.Ang), 16) + Random2((5 - Skill) * 1000 - 500);
        double dy = Blood.Sar(Blood.Sin(e.Ang), 16) + Random2((5 - Skill) * 1000 - 500);
        double dz = e.DudeSlope + Random2((5 - Skill) * 500);
        for (int i = 0; i < 8; i++)
        {
            int r1 = Random3(500), r2 = Random3(1000), r3 = Random3(1000);
            ActFireVector(e, 0, 0, dx + r3, dy + r2, dz + r1, K.kVectorShell);
        }
        Sound3D(e, Chance(32768) ? 1001 : 1002, -1);
    }

    int CultTNTType() => Skill > 2 ? K.kThingArmedTNTBundle : K.kThingArmedTNTStick;

    void cult_ThrowSeqCallback(Sprite e)
    {
        bool v4 = Chance(24576);
        Sound3D(e, 455, -1);
        var t = e.XTarget;
        if (t == null)
            return;
        double dx = t.BX - e.BX, dy = t.BY - e.BY, dz = t.BZ - e.BZ;
        double nDist = Blood.ApproxDist(dx, dy);
        double nDist2 = Blood.Int(nDist / 540);
        if (nDist > 7680)
            v4 = false;
        var m = ActFireThing(e, 0, 0, Blood.Int(dz / 128) - 14500, CultTNTType(), Blood.Int(nDist2 * 8388608 / 120));
        if (v4)
            m.XImpact = 1;
        else
            EvPost(m, 120 * (1 + Random(2)), K.kCmdOn, e);
    }

    void cult_sub_68170(Sprite e)
    {
        Sound3D(e, 455, -1);
        var m = ActFireThing(e, 0, 0, e.DudeSlope - 9460, CultTNTType(), 1258291);
        EvPost(m, 120 * (2 + Random(2)), K.kCmdOn, e);
    }

    void cult_sub_68230(Sprite e)
    {
        Sound3D(e, 455, -1);
        var t = e.XTarget;
        if (t == null)
            return;
        double dx = t.BX - e.BX, dy = t.BY - e.BY, dz = t.BZ - e.BZ;
        double nDist2 = Blood.Int(Blood.ApproxDist(dx, dy) / 540);
        var m = ActFireThing(e, 0, 0, Blood.Int(dz / 128) - 14500, CultTNTType(), Blood.Int(nDist2 * 8388608 / 120));
        m.XImpact = 1;
    }

    /// <summary>An armed TNT stick or an explosion in the target's sector.</summary>
    bool CultTargetNearExplosion(Sprite t)
    {
        var sec = SpriteSector(t);
        foreach (var e in SpritesNear(t.Origin, 256))
            if ((e.Type == K.kThingArmedTNTStick || e.StatNum == K.kStatExplosion) && (sec == null || SpriteSector(e) == sec))
                return true;
        return false;
    }

    void cult_thinkSearch(Sprite e)
    {
        AiChooseDirection(e, e.GoalAng);
        AiLookForTarget(e);
    }

    void CultGoSearch(Sprite e) => AiNewState(e, e.Medium == K.kMediumNormal ? cultistSearch : cultistSwimSearch);

    void cult_thinkGoto(Sprite e)
    {
        double dx = e.TargetX - e.BX, dy = e.TargetY - e.BY;
        int nAngle = Blood.GetAngle(dx, dy);
        double nDist = Blood.ApproxDist(dx, dy);
        AiChooseDirection(e, nAngle);
        if (nDist < 5120 && Math.Abs(e.Ang - nAngle) < Info.Dude(e.Type, K.DI_periphery))
            CultGoSearch(e);
        aiThinkTarget(e);
    }

    /// <summary>The fire state for the cultist's posture and medium.</summary>
    void CultFire(Sprite e, AiState stand, AiState prone, AiState swim, bool swimNeedsSeq)
    {
        if (!DudeIsPlayingSeq(e, 14) && e.Medium == K.kMediumNormal)
            AiNewState(e, stand);
        else if (DudeIsPlayingSeq(e, 14) && e.Medium == K.kMediumNormal)
            AiNewState(e, prone);
        else if ((e.Medium == K.kMediumWater || e.Medium == K.kMediumGoo) && (!swimNeedsSeq || DudeIsPlayingSeq(e, 13)))
            AiNewState(e, swim);
    }

    void CultDodge(Sprite e)
    {
        if (!DudeIsPlayingSeq(e, 14) && e.Medium == K.kMediumNormal)
            AiNewState(e, cultistDodge);
        else if (DudeIsPlayingSeq(e, 14) && e.Medium == K.kMediumNormal)
            AiNewState(e, cultistProneDodge);
        else if (e.Medium == K.kMediumWater || e.Medium == K.kMediumGoo)
            AiNewState(e, cultistSwimDodge);
    }

    void CultTryThrow(Sprite e, AiState throwState, double dx, double dy, bool allowWallHit)
    {
        int nHit = HitScan(e, e.BZ, dx, dy, 0, 0);
        bool water = e.Medium == K.kMediumWater || e.Medium == K.kMediumGoo;
        if (nHit == -1)
        {
            if (!water)
                AiNewState(e, throwState);
        }
        else if (nHit == 4)
        {
        }
        else if (nHit == 0)
        {
            if (allowWallHit)
                AiNewState(e, throwState);
        }
        else if (nHit == 3)
        {
            var h = (Sprite)HitEnt;
            if (e.Type != h.Type && h.Type != K.kDudeCultistShotgun && !water)
                AiNewState(e, throwState);
        }
        else
            AiNewState(e, throwState);
    }

    void CultTryFire(Sprite e, AiState stand, AiState prone, AiState swim, double dx, double dy, int friendType, bool swimNeedsSeq)
    {
        int nHit = HitScan(e, e.BZ, dx, dy, 0, 0);
        if (nHit == -1)
            CultFire(e, stand, prone, swim, swimNeedsSeq);
        else if (nHit == 3)
        {
            var h = (Sprite)HitEnt;
            if (e.Type != h.Type && h.Type != friendType)
                CultFire(e, stand, prone, swim, false);
            else
                CultDodge(e);
        }
        else
            CultFire(e, stand, prone, swim, false);
    }

    void cult_thinkChase(Sprite e)
    {
        var t = e.XTarget;
        if (t == null)
        {
            AiNewState(e, e.Medium == K.kMediumNormal ? cultistGoto : cultistSwimGoto);
            return;
        }
        double dx = t.BX - e.BX, dy = t.BY - e.BY;
        AiChooseDirection(e, Blood.GetAngle(dx, dy));
        if (t.XHealth == 0)
        {
            if (e.Medium == K.kMediumNormal)
            {
                AiNewState(e, cultistSearch);
                AiPlay3DSound(e, (e.Type == K.kDudeCultistTommy ? 4021 : 1021) + Random(4), AI_SFX_PRIORITY_1, -1);
            }
            else
                AiNewState(e, cultistSwimSearch);
            return;
        }
        if (t is Player && PowerupCheck(t, K.kPwUpShadowCloak) > 0)
        {
            CultGoSearch(e);
            return;
        }
        double nDist = Blood.ApproxDist(dx, dy);
        if (nDist <= Info.Dude(e.Type, K.DI_seeDist))
        {
            double nDeltaAngle = Blood.AngMask(Blood.GetAngle(dx, dy) + 1024 - e.Ang) - 1024;
            double height = Info.Dude(e.Type, K.DI_eyeHeight) * e.YRepeat * 4;
            if (CanSee(t.BX, t.BY, t.BZ, e.BX, e.BY, e.BZ - height, t, e)
                && nDist < Info.Dude(e.Type, K.DI_seeDist) && Math.Abs(nDeltaAngle) <= Info.Dude(e.Type, K.DI_periphery))
            {
                AiSetTarget(e, t);
                e.DudeSlope = Blood.DivScale10(t.BZ - e.BZ, nDist);
                bool running = t is Player tp && tp.IsRunning;
                bool tp2 = t is Player;
                double ad = Math.Abs(nDeltaAngle);
                switch (e.Type)
                {
                    case K.kDudeCultistTommy:
                        if ((nDist < 7680 && nDist > 3584 && ad < 85 && !CultTargetNearExplosion(t) && (t.HitTag & 2) != 0 && Skill > 2 && tp2 && running) & Chance(32768))
                            CultTryThrow(e, cultistTThrow, dx, dy, false);
                        else if (nDist < 17920 && ad < 28)
                            CultTryFire(e, cultistTFire, cultistTProneFire, cultistTSwimFire, dx, dy, K.kDudeCultistShotgun, true);
                        break;
                    case K.kDudeCultistShotgun:
                        if ((nDist < 11264 && nDist > 5120 && !CultTargetNearExplosion(t) && (t.HitTag & 2) != 0 && Skill >= 2 && tp2 && !running) & Chance(32768))
                            CultTryThrow(e, cultistSThrow, dx, dy, false);
                        else if (nDist < 12800 && ad < 28)
                            CultTryFire(e, cultistSFire, cultistSProneFire, cultistSSwimFire, dx, dy, K.kDudeCultistTommy, false);
                        break;
                    case K.kDudeCultistTesla:
                        if ((nDist < 7680 && nDist > 3584 && !CultTargetNearExplosion(t) && (t.HitTag & 2) != 0 && Skill > 2 && tp2 && running) & Chance(32768))
                            CultTryThrow(e, cultistTsThrow, dx, dy, false);
                        else if (nDist < 12800 && ad < 28)
                            CultTryFire(e, cultistTsFire, cultistTsProneFireFixed, cultistTsSwimFire, dx, dy, K.kDudeCultistTommy, false);
                        break;
                    case K.kDudeCultistTNT:
                        if (nDist < 11264 && nDist > 5120 && ad < 85 && (t.HitTag & 2) != 0 && tp2)
                            CultTryThrow(e, cultistDThrow, dx, dy, true);
                        else if (nDist < 5120 && ad < 85 && (t.HitTag & 2) != 0 && tp2)
                            CultTryThrow(e, cultist139A78, dx, dy, true);
                        break;
                    case K.kDudeCultistBeast:
                        if ((nDist < 7680 && nDist > 3584 && !CultTargetNearExplosion(t) && (t.HitTag & 2) != 0 && Skill > 2 && tp2 && running) & Chance(32768))
                            CultTryThrow(e, cultistSThrow, dx, dy, false);
                        else if (nDist < 12800 && ad < 28)
                            CultTryFire(e, cultistSFire, cultistSProneFire, cultistSSwimFire, dx, dy, K.kDudeCultistTommy, false);
                        break;
                }
                return;
            }
        }
        AiNewState(e, e.Medium == K.kMediumNormal ? cultistGoto : cultistSwimGoto);
        e.XTarget = null;
    }
}
