using System;
using Chosen.Data;

namespace Chosen.Sim;

/// <summary>Tiny Caleb (aicaleb.cpp) and gill beast (aigilbst.cpp) AI, via Blood3D's server/ai_caleb_gill.qc.</summary>
public sealed partial class World
{
    /// <summary>The amphibians' chase: land states when dry, swim states in an underwater sector.</summary>
    void AmphibChase(Sprite e, double range, AiState landAttack, AiState swimAttack, AiState landDodge, AiState swimDodge,
        AiState landSearch, AiState swimSearch, AiState landGoto, AiState swimGoto, int deathSnd)
    {
        bool uw = IsUnderwaterSprite(e);
        var t = e.XTarget;
        if (t == null)
        {
            AiNewState(e, uw ? swimSearch : landSearch);
            return;
        }
        double dx = t.BX - e.BX, dy = t.BY - e.BY;
        AiChooseDirection(e, Blood.GetAngle(dx, dy));
        if (t.XHealth == 0)
        {
            if (uw)
                AiNewState(e, swimSearch);
            else
            {
                if (deathSnd > 0)
                    AiPlay3DSound(e, deathSnd + Random(4), AI_SFX_PRIORITY_1, -1);
                AiNewState(e, landSearch);
            }
            return;
        }
        if (t is Player && PowerupCheck(t, K.kPwUpShadowCloak) > 0)
        {
            AiNewState(e, uw ? swimSearch : landSearch);
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
                    if (nDist < range && Math.Abs(nDeltaAngle) < 28)
                    {
                        int nHit = HitScan(e, e.BZ, dx, dy, 0, 0);
                        if (nHit == 3 && e.Type == ((Sprite)HitEnt).Type)
                            AiNewState(e, uw ? swimDodge : landDodge);
                        else
                            AiNewState(e, uw ? swimAttack : landAttack);
                    }
                }
                return;
            }
        }
        AiNewState(e, uw ? swimGoto : landGoto);
        e.XTarget = null;
    }

    void AmphibGoto(Sprite e, AiState landSearch, AiState swimSearch)
    {
        double dx = e.TargetX - e.BX, dy = e.TargetY - e.BY;
        int nAngle = Blood.GetAngle(dx, dy);
        double nDist = Blood.ApproxDist(dx, dy);
        AiChooseDirection(e, nAngle);
        if (nDist < 512 && Math.Abs(e.Ang - nAngle) < Info.Dude(e.Type, K.DI_periphery))
            AiNewState(e, IsUnderwaterSprite(e) ? swimSearch : landSearch);
        aiThinkTarget(e);
    }

    // ---------------------------------------------------------------- tiny Caleb
    void caleb_SeqAttackCallback(Sprite e)
    {
        double dx = Blood.Sar(Blood.Cos(e.Ang), 16) + Random2(1500);
        double dy = Blood.Sar(Blood.Sin(e.Ang), 16) + Random2(1500);
        double dz = e.DudeSlope + Random2(1500);
        for (int i = 0; i < 2; i++)
        {
            int r1 = Random3(500), r2 = Random3(1000), r3 = Random3(1000);
            ActFireVector(e, 0, 0, dx + r3, dy + r2, dz + r1, K.kVectorShell);
        }
        if (Chance(32768))
            Sound3D(e, 10000 + Random(5), -1);
        Sound3D(e, Chance(32768) ? 1001 : 1002, -1);
    }

    void caleb_thinkSearch(Sprite e) => AiThinkSearchTarget(e);
    void caleb_thinkGoto(Sprite e) => AmphibGoto(e, tinycalebSearch, tinycalebSwimSearch);

    void caleb_thinkChase(Sprite e) =>
        AmphibChase(e, 1433, tinycalebAttack, tinycalebSwimAttack, tinycalebDodge, tinycalebSwimDodge,
            tinycalebSearch, tinycalebSwimSearch, tinycalebGoto, tinycalebSwimGoto, 11000);

    void caleb_thinkSwimGoto(Sprite e) => AiThinkGotoThen(e, 512, tinycalebSwimSearch);
    void caleb_thinkSwimChase(Sprite e) => SwimChase(e, tinycalebSwimGoto, tinycalebSwimSearch, tinycalebSwimAttack, tinycaleb13967C, 0, 0);
    void caleb_sub_65D04(Sprite e) => SwimForward(e, 2);
    void caleb_sub_65F44(Sprite e) => SwimDescend(e);
    void caleb_sub_661E0(Sprite e) => SwimAscend(e);

    // ---------------------------------------------------------------- gill beast
    void gill_GillBiteSeqCallback(Sprite e)
    {
        var t = e.XTarget;
        if (t == null)
            return;
        double dx = Blood.Sar(Blood.Cos(e.Ang), 16) + Random3(2000);
        double dy = Blood.Sar(Blood.Sin(e.Ang), 16) + Random3(2000);
        double dz = e.BZ - t.BZ;
        ActFireVector(e, 0, 0, dx, dy, dz, K.kVectorGillBite);
        ActFireVector(e, 0, 0, dx, dy, dz, K.kVectorGillBite);
        ActFireVector(e, 0, 0, dx, dy, dz, K.kVectorGillBite);
    }

    void gill_thinkSearch(Sprite e) => AiThinkSearchTarget(e);
    void gill_thinkGoto(Sprite e) => AmphibGoto(e, gillBeastSearch, gillBeastSwimSearch);

    void gill_thinkChase(Sprite e) =>
        AmphibChase(e, 921, gillBeastBite, gillBeastSwimBite, gillBeastDodge, gillBeastSwimDodge,
            gillBeastSearch, gillBeastSwimSearch, gillBeastGoto, gillBeastSwimGoto, 0);

    void gill_thinkSwimGoto(Sprite e) => AiThinkGotoThen(e, 512, gillBeastSwimSearch);
    void gill_thinkSwimChase(Sprite e) => SwimChase(e, gillBeastSwimGoto, gillBeastSwimSearch, gillBeastSwimBite, gillBeast13A154, 1700, 1);
    void gill_sub_6CB00(Sprite e) => SwimForward(e, 2);
    void gill_sub_6CD74(Sprite e) => SwimDescend(e);
    void gill_sub_6D03C(Sprite e) => SwimAscend(e);
}
