using System;
using Chosen.Data;

namespace Chosen.Sim;

/// <summary>Beast AI (aibeast.cpp, via Blood3D's server/ai_beast.qc), with the swimmers' shared moves.</summary>
public sealed partial class World
{
    void beast_SlashSeqCallback(Sprite e)
    {
        var t = e.XTarget;
        if (t == null)
            return;
        double dx = Blood.Sar(Blood.Cos(e.Ang), 16) + Random3(4000 - 700 * Skill);
        double dy = Blood.Sar(Blood.Sin(e.Ang), 16) + Random3(4000 - 700 * Skill);
        double dz = e.BZ - t.BZ;
        ActFireVector(e, 0, 0, dx, dy, dz, K.kVectorGargSlash);
        ActFireVector(e, 0, 0, dx, dy, dz, K.kVectorGargSlash);
        ActFireVector(e, 0, 0, dx, dy, dz, K.kVectorGargSlash);
        Sound3D(e, 9012 + Random(2), -1);
    }

    void beast_StompSeqCallback(Sprite e)
    {
        double dx = Blood.Sar(Blood.Cos(e.Ang), 16), dy = Blood.Sar(Blood.Sin(e.Ang), 16);
        double x = e.BX, y = e.BY, z = e.BZ;
        double vc = 400;
        double v1c = 5 + 2 * Skill, v10 = 25 + 30 * Skill;
        bool v4 = false;
        int nHit = HitScan(e, e.BZ, dx, dy, 0, 0);
        if (nHit == 3 && HitEnt is Sprite hs && hs.StatNum == K.kStatDude)
            v4 = false;
        vc *= 16;
        double floorz = FloorZBelow(e);
        foreach (var e2 in StatList(K.kStatDude))
        {
            if (e == e2 && !v4)
                continue;
            if (!e2.HasX || e2.Type == K.kDudeBeast || (e2.HitTag & 32) != 0)
                continue;
            if (!CheckProximity(e2, x, y, z, vc))
                continue;
            var (_, bottom) = Extents(e);
            if (Math.Abs(bottom - floorz) >= 256)
                continue;
            double nDist2 = Blood.KSqrt(Math.Abs(x - e2.BX), Math.Abs(y - e2.BY));
            if (nDist2 > vc)
                continue;
            double nDamage = nDist2 == 0 ? v1c + v10 : v1c + Blood.Int((vc - nDist2) * v10 / vc);
            if (e2 is Player pl)
                pl.QuakeEffect += nDamage * 4;
            ActDamageSprite(e, e2, K.kDamageFall, nDamage * 16);
        }
        foreach (var e2 in StatList(K.kStatThing))
        {
            if ((e2.HitTag & 32) != 0 || !CheckProximity(e2, x, y, z, vc) || e2.XLocked != 0)
                continue;
            double nDist2 = Blood.KSqrt(Math.Abs(x - e2.BX), Math.Abs(y - e2.BY));
            if (nDist2 > vc)
                continue;
            double nDamage = nDist2 == 0 ? v1c + v10 : v1c + Blood.Int((vc - nDist2) * v10 / vc);
            ActDamageSprite(e, e2, K.kDamageFall, nDamage * 16);
        }
        Sound3D(e, 9015 + Random(2), -1);
    }

    void beast_MorphToBeast(Sprite e)
    {
        ActHealDude(e, Info.Dude(K.kDudeBeast, K.DI_startHealth), Info.Dude(K.kDudeBeast, K.DI_startHealth));
        e.Type = K.kDudeBeast;
    }

    void beast_thinkSearch(Sprite e) => AiThinkSearchTarget(e);

    void BeastGoSearch(Sprite e) => AiNewState(e, IsUnderwaterSprite(e) ? beastSwimSearch : beastSearch);

    void beast_thinkGoto(Sprite e)
    {
        double dx = e.TargetX - e.BX, dy = e.TargetY - e.BY;
        int nAngle = Blood.GetAngle(dx, dy);
        double nDist = Blood.ApproxDist(dx, dy);
        AiChooseDirection(e, nAngle);
        if (nDist < 512 && Math.Abs(e.Ang - nAngle) < Info.Dude(e.Type, K.DI_periphery))
            BeastGoSearch(e);
        aiThinkTarget(e);
    }

    void beast_thinkChase(Sprite e)
    {
        var t = e.XTarget;
        if (t == null)
        {
            BeastGoSearch(e);
            return;
        }
        double dx = t.BX - e.BX, dy = t.BY - e.BY;
        AiChooseDirection(e, Blood.GetAngle(dx, dy));
        if (t.XHealth == 0 || (t is Player && PowerupCheck(t, K.kPwUpShadowCloak) > 0))
        {
            BeastGoSearch(e);
            return;
        }
        double nDist = Blood.ApproxDist(dx, dy);
        if (nDist <= Info.Dude(e.Type, K.DI_seeDist))
        {
            double nDeltaAngle = Blood.AngMask(Blood.GetAngle(dx, dy) + 1024 - e.Ang) - 1024;
            double height = Info.Dude(e.Type, K.DI_eyeHeight) * e.YRepeat * 4;
            if (CanSee(t.BX, t.BY, t.BZ, e.BX, e.BY, e.BZ - height, t, e))
            {
                if (nDist < Info.Dude(e.Type, K.DI_seeDist) && Math.Abs(nDeltaAngle) <= Info.Dude(e.Type, K.DI_periphery))
                {
                    AiSetTarget(e, t);
                    e.DudeSlope = Blood.DivScale10(t.BZ - e.BZ, nDist);
                    bool uw = IsUnderwaterSprite(e);
                    double ad = Math.Abs(nDeltaAngle);
                    if ((nDist < 5120 && nDist > 2560 && ad < 85 && (t.HitTag & 2) != 0 && t is Player) & Chance(32768))
                    {
                        int nHit = HitScan(e, e.BZ, dx, dy, 0, 0);
                        if (t.XHealth > Blood.Int(Info.PlayerTemplate(0, K.DI_startHealth) / 2.0))
                        {
                            if (nHit == 3)
                            {
                                if (e.Type != ((Sprite)HitEnt).Type)
                                {
                                    if (!uw)
                                        AiNewState(e, beastStomp);
                                }
                                else
                                    AiNewState(e, uw ? beastSwimDodge : beastDodge);
                            }
                            else if (!uw)
                                AiNewState(e, beastStomp);
                        }
                    }
                    if (nDist < 921 && ad < 28)
                    {
                        int nHit = HitScan(e, e.BZ, dx, dy, 0, 0);
                        if (nHit == 3 && e.Type == ((Sprite)HitEnt).Type)
                            AiNewState(e, uw ? beastSwimDodge : beastDodge);
                        else
                            AiNewState(e, uw ? beastSwimSlash : beastSlash);
                    }
                }
                return;
            }
        }
        AiNewState(e, IsUnderwaterSprite(e) ? beastSwimGoto : beastGoto);
        e.XTarget = null;
    }

    void beast_thinkSwimGoto(Sprite e) => AiThinkGotoThen(e, 512, beastSwimSearch);

    /// <summary>The swimmers' shared chase (beast, tiny caleb, gill beast).</summary>
    void SwimChase(Sprite e, AiState gotoSt, AiState searchSt, AiState slashSt, AiState ascendSt, int growlSnd, int growlRange)
    {
        var t = e.XTarget;
        if (t == null)
        {
            AiNewState(e, gotoSt);
            return;
        }
        double dx = t.BX - e.BX, dy = t.BY - e.BY;
        AiChooseDirection(e, Blood.GetAngle(dx, dy));
        if (t.XHealth == 0 || (t is Player && PowerupCheck(t, K.kPwUpShadowCloak) > 0))
        {
            AiNewState(e, searchSt);
            return;
        }
        double nDist = Blood.ApproxDist(dx, dy);
        if (nDist <= Info.Dude(e.Type, K.DI_seeDist))
        {
            double nDeltaAngle = Blood.AngMask(Blood.GetAngle(dx, dy) + 1024 - e.Ang) - 1024;
            double height = Info.Dude(e.Type, K.DI_eyeHeight) + e.BZ;   // the original: eyeHeight + z (sic)
            if (CanSee(t.BX, t.BY, t.BZ, e.BX, e.BY, e.BZ - height, t, e))
            {
                if (nDist < Info.Dude(e.Type, K.DI_seeDist) && Math.Abs(nDeltaAngle) <= Info.Dude(e.Type, K.DI_periphery))
                {
                    AiSetTarget(e, t);
                    if (nDist < 1024 && Math.Abs(nDeltaAngle) < 85)
                        AiNewState(e, slashSt);
                    else
                    {
                        if (growlSnd > 0)
                            AiPlay3DSound(e, growlSnd + Random(growlRange), AI_SFX_PRIORITY_1, -1);
                        AiNewState(e, ascendSt);
                    }
                }
            }
            else
                AiNewState(e, ascendSt);
            return;
        }
        AiNewState(e, gotoSt);
        e.XTarget = null;
    }

    void beast_thinkSwimChase(Sprite e) => SwimChase(e, beastSwimGoto, beastSwimSearch, beastSwimSlash, beast138FD0, 9009, 2);

    void beast_MoveForward(Sprite e)
    {
        double nAng = AiTurnToGoal(e);
        if (Math.Abs(nAng) > 341)
            return;
        double nDist = Blood.ApproxDist(e.TargetX - e.BX, e.TargetY - e.BY);
        if (nDist <= 1024 & Random(64) < 32)
            return;
        e.XVel += Blood.MulScale30(Info.Dude(e.Type, K.DI_frontSpeed), Blood.Cos(e.Ang));
        e.YVel += Blood.MulScale30(Info.Dude(e.Type, K.DI_frontSpeed), Blood.Sin(e.Ang));
    }

    /// <summary>Swim forward (sub_628A0 and its equivalents).</summary>
    void SwimForward(Sprite e, int targetAccelShift)
    {
        double nAng = AiTurnToGoal(e);
        double nAccel = Info.Dude(e.Type, K.DI_frontSpeed) * 4;
        if (Math.Abs(nAng) > 341)
            return;
        if (e.XTarget == null)
            e.Ang = (int)Blood.AngMask(e.Ang + 256);
        double nDist = Blood.ApproxDist(e.TargetX - e.BX, e.TargetY - e.BY);
        if (Random(64) < 32 && nDist <= 1024)
            return;
        var (t1, t2) = VelToLocal(e);
        t1 += e.XTarget == null ? nAccel : Blood.Sar(nAccel, targetAccelShift);
        LocalToVel(e, t1, t2);
    }

    void beast_sub_628A0(Sprite e) => SwimForward(e, 2);

    /// <summary>Swim down toward the target (sub_62AE0 and its equivalents): zvel = -dz.</summary>
    void SwimDescend(Sprite e)
    {
        var t = e.XTarget;
        if (t == null)
            return;
        double z = e.BZ + Info.Dude(e.Type, K.DI_eyeHeight), z2 = t.BZ + Info.Dude(t.Type, K.DI_eyeHeight);
        double nAng = AiTurnToGoal(e);
        double nAccel = Info.Dude(e.Type, K.DI_frontSpeed) * 4;
        if (Math.Abs(nAng) > 341)
        {
            e.GoalAng = (int)Blood.AngMask(e.Ang + 512);
            return;
        }
        double dz = z2 - z;
        double nDist = Blood.ApproxDist(e.TargetX - e.BX, e.TargetY - e.BY);
        if (Chance(1536) && nDist <= 1024)
            return;
        var (t1, t2) = VelToLocal(e);
        LocalToVel(e, t1 + nAccel, t2);
        e.ZVel = -dz;
    }

    void beast_sub_62AE0(Sprite e) => SwimDescend(e);

    /// <summary>Swim up toward the target (sub_62D7C and its equivalents): zvel = dz * 8.</summary>
    void SwimAscend(Sprite e)
    {
        var t = e.XTarget;
        if (t == null)
            return;
        double z = e.BZ + Info.Dude(e.Type, K.DI_eyeHeight), z2 = t.BZ + Info.Dude(t.Type, K.DI_eyeHeight);
        double nAng = AiTurnToGoal(e);
        double nAccel = Info.Dude(e.Type, K.DI_frontSpeed) * 4;
        if (Math.Abs(nAng) > 341)
        {
            e.Ang = (int)Blood.AngMask(e.Ang + 512);
            return;
        }
        double dz = (z2 - z) * 8;
        double nDist = Blood.ApproxDist(e.TargetX - e.BX, e.TargetY - e.BY);
        if (Chance(16384) && nDist <= 1024)
            return;
        var (t1, t2) = VelToLocal(e);
        LocalToVel(e, t1 + Blood.Sar(nAccel, 1), t2);
        e.ZVel = dz;
    }

    void beast_sub_62D7C(Sprite e) => SwimAscend(e);
}
