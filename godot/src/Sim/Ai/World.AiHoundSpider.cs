using System;
using Chosen.Data;

namespace Chosen.Sim;

/// <summary>Hellhound (aihound.cpp) and spider (aispid.cpp) AI, via Blood3D's server/ai_hound_spid.qc.</summary>
public sealed partial class World
{
    // ---------------------------------------------------------------- hellhound
    void hound_BiteSeqCallback(Sprite e)
    {
        if (e.XTarget is { } t && (t is Player || IsModern))        // (a modern map: other dudes too)
            ActFireVector(e, 0, 0, Blood.Sar(Blood.Cos(e.Ang), 16), Blood.Sar(Blood.Sin(e.Ang), 16), t.BZ - e.BZ, K.kVectorHoundBite);
    }

    void hound_BurnSeqCallback(Sprite e) =>
        ActFireMissile(e, 0, 0, Blood.Sar(Blood.Cos(e.Ang), 16), Blood.Sar(Blood.Sin(e.Ang), 16), 0, K.kMissileFlameHound);

    void hound_thinkSearch(Sprite e) => AiThinkSearchTarget(e);
    void hound_thinkGoto(Sprite e) => AiThinkGotoThen(e, 512, houndSearch);

    void hound_thinkChase(Sprite e)
    {
        var t = e.XTarget;
        if (t == null)
        {
            AiNewState(e, houndGoto);
            return;
        }
        AiChooseDirection(e, Blood.GetAngle(t.BX - e.BX, t.BY - e.BY));
        if (t.XHealth == 0 || (t is Player && PowerupCheck(t, K.kPwUpShadowCloak) > 0))
        {
            AiNewState(e, houndSearch);
            return;
        }
        if (AiSeesTarget(e, t, out _, out _, out double nDist, out double nDeltaAngle))
        {
            AiSetTarget(e, t);
            double ad = Math.Abs(nDeltaAngle);
            if (nDist < 2816 && nDist > 1280 && ad < 85)
                AiNewState(e, houndBurn);
            else if (nDist < 614 && ad < 85)
                AiNewState(e, houndBite);
            return;
        }
        AiNewState(e, houndGoto);
        e.XTarget = null;
    }

    // ---------------------------------------------------------------- spiders
    static bool SpidPoisonPlayer(Sprite t, double nBlind, double mx)
    {
        if (t is not Player p)
            return false;
        nBlind *= 16;
        mx *= 16;
        if (p.BlindEffect >= mx)
            return false;
        p.BlindEffect = Blood.ClipHigh(p.BlindEffect + nBlind, mx);
        return true;
    }

    void spid_SpidBiteSeqCallback(Sprite e)
    {
        double dx = Blood.Sar(Blood.Cos(e.Ang), 16) + Random2(2000);
        double dy = Blood.Sar(Blood.Sin(e.Ang), 16) + Random2(2000);
        double dz = Random2(2000);
        if (e.XTarget is not Player t)
            return;
        int nHit = HitScan(e, e.BZ, dx, dy, 0, 0);
        if (nHit != 3 || HitEnt is not Player)
            return;
        dz += t.BZ - e.BZ;
        switch (e.Type)
        {
            case K.kDudeSpiderBrown:
                ActFireVector(e, 0, 0, dx, dy, dz, K.kVectorSpiderBite);
                if ((!t.GodMode && PowerupCheck(t, K.kPwUpDeathMask) <= 0) & Chance(16384))
                    PowerupActivate(t, K.kPwUpDeliriumShroom);
                break;
            case K.kDudeSpiderRed:
                ActFireVector(e, 0, 0, dx, dy, dz, K.kVectorSpiderBite);
                if (Chance(20480))
                    SpidPoisonPlayer(t, 4, 16);
                break;
            case K.kDudeSpiderBlack:
                ActFireVector(e, 0, 0, dx, dy, dz, K.kVectorSpiderBite);
                SpidPoisonPlayer(t, 8, 16);
                break;
            case K.kDudeSpiderMother:
                ActFireVector(e, 0, 0, dx, dy, dz, K.kVectorSpiderBite);
                dx += Random2(2000);
                dy += Random2(2000);
                dz += Random2(2000);
                ActFireVector(e, 0, 0, dx, dy, dz, K.kVectorSpiderBite);
                SpidPoisonPlayer(t, 8, 16);
                break;
        }
    }

    void spid_SpidJumpSeqCallback(Sprite e)
    {
        double dx = Blood.Sar(Blood.Cos(e.Ang), 16) + Random2(200);
        double dy = Blood.Sar(Blood.Sin(e.Ang), 16) + Random2(200);
        double dz = Random2(200);
        if (e.XTarget is Player t)
        {
            dz += t.BZ - e.BZ;
            if (e.Type == K.kDudeSpiderBrown || e.Type == K.kDudeSpiderBlack)
            {
                e.XVel = dx * 65536;
                e.YVel = dy * 65536;
                e.ZVel = dz * 65536;
            }
        }
    }

    void spid_SpidBirthSeqCallback(Sprite e)
    {
        double dx = e.TargetX - e.BX, dy = e.TargetY - e.BY;
        int nAngle = Blood.GetAngle(dx, dy);
        double nDist = Blood.ApproxDist(dx, dy);
        if (e.XTarget is not Player || e.BirthCounter >= 10)
            return;
        Sprite spawn = null;
        if (Math.Abs(e.Ang - nAngle) < Info.Dude(e.Type, K.DI_periphery))
        {
            if (nDist < 6656 && nDist > 5120)
                spawn = ActSpawnDude(e, K.kDudeSpiderRed, e.ClipDist, 0);
            else if (nDist < 3072 || (nDist < 5120 && nDist > 3072))
                spawn = ActSpawnDude(e, K.kDudeSpiderBrown, e.ClipDist, 0);
        }
        if (spawn != null)
        {
            e.BirthCounter++;
            spawn.Owner = e;
            KillMgrAddCountSprite(spawn);
        }
    }

    void spid_thinkSearch(Sprite e) => AiThinkSearchTarget(e);
    void spid_thinkGoto(Sprite e) => AiThinkGotoThen(e, 512, spidSearch);

    void spid_thinkChase(Sprite e)
    {
        var t = e.XTarget;
        if (t == null)
        {
            AiNewState(e, spidGoto);
            return;
        }
        AiChooseDirection(e, Blood.GetAngle(t.BX - e.BX, t.BY - e.BY));
        if (t.XHealth == 0 || (t is Player && PowerupCheck(t, K.kPwUpShadowCloak) > 0))
        {
            AiNewState(e, spidSearch);
            return;
        }
        if (AiSeesTarget(e, t, out _, out _, out double nDist, out double nDeltaAngle))
        {
            AiSetTarget(e, t);
            double ad = Math.Abs(nDeltaAngle);
            if (e.Type == K.kDudeSpiderRed)
            {
                if (nDist < 921 && ad < 85)
                    AiNewState(e, spidBite);
            }
            else if (e.Type == K.kDudeSpiderBrown || e.Type == K.kDudeSpiderBlack)
            {
                if (nDist < 1843 && nDist > 921 && ad < 85)
                    AiNewState(e, spidJump);
                else if (nDist < 921 && ad < 85)
                    AiNewState(e, spidBite);
            }
            else if (e.Type == K.kDudeSpiderMother)
            {
                if (nDist < 1843 && nDist > 921 && ad < 85)
                    AiNewState(e, spidJump);
                else if (Chance(32768))
                    AiNewState(e, spidBirth);
            }
            return;
        }
        AiNewState(e, spidGoto);
        e.XTarget = null;
    }
}
