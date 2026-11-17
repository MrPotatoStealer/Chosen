using System;
using Chosen.Data;

namespace Chosen.Sim;

/// <summary>
/// Axe zombie (aizomba.cpp) and butcher (aizombf.cpp) AI, burning dudes (aiburn.cpp), the innocent (aiinnoc.cpp),
/// rat (airat.cpp) and hand (aihand.cpp); via Blood3D's server/ai_zombie.qc and ai_misc.qc.
/// </summary>
public sealed partial class World
{
    static bool TargetCloakedOrMasked(Sprite t) =>
        t is Player && (PowerupCheck(t, K.kPwUpShadowCloak) > 0 || PowerupCheck(t, K.kPwUpDeathMaskUseless) > 0);

    /// <summary>The usual "can I see my target in front of me" test of the chase thinks; returns the distance and
    /// angle to it, or false.</summary>
    bool AiSeesTarget(Sprite e, Sprite t, out double dx, out double dy, out double nDist, out double nDeltaAngle, bool needSeeDistStrict = true)
    {
        dx = t.BX - e.BX;
        dy = t.BY - e.BY;
        nDist = Blood.ApproxDist(dx, dy);
        nDeltaAngle = 0;
        if (nDist > Info.Dude(e.Type, K.DI_seeDist))
            return false;
        nDeltaAngle = Blood.AngMask(Blood.GetAngle(dx, dy) + 1024 - e.Ang) - 1024;
        double height = Info.Dude(e.Type, K.DI_eyeHeight) * e.YRepeat * 4;
        if (!CanSee(t.BX, t.BY, t.BZ, e.BX, e.BY, e.BZ - height, t, e))
            return false;
        if (needSeeDistStrict && nDist >= Info.Dude(e.Type, K.DI_seeDist))
            return false;
        return Math.Abs(nDeltaAngle) <= Info.Dude(e.Type, K.DI_periphery);
    }

    /// <summary>The usual goto think: head for the target position; close enough and facing it, search.</summary>
    void AiThinkGotoThen(Sprite e, double nearDist, AiState search)
    {
        double dx = e.TargetX - e.BX, dy = e.TargetY - e.BY;
        int nAngle = Blood.GetAngle(dx, dy);
        double nDist = Blood.ApproxDist(dx, dy);
        AiChooseDirection(e, nAngle);
        if (nDist < nearDist && Math.Abs(e.Ang - nAngle) < Info.Dude(e.Type, K.DI_periphery))
            AiNewState(e, search);
        aiThinkTarget(e);
    }

    void AiThinkSearchTarget(Sprite e)
    {
        AiChooseDirection(e, e.GoalAng);
        aiThinkTarget(e);
    }

    // ---------------------------------------------------------------- axe zombie
    void zomba_HackSeqCallback(Sprite e)
    {
        var t = e.XTarget;
        if (t == null)
            return;
        int nAngle = Blood.GetAngle(e.TargetX - e.BX, e.TargetY - e.BY);
        double height = e.YRepeat * Info.Dude(e.Type, K.DI_eyeHeight) * 4;
        double height2 = t.YRepeat * Info.Dude(t.Type, K.DI_eyeHeight) * 4;
        Sound3D(e, 1101, 1);
        ActFireVector(e, 0, 0, Blood.Sar(Blood.Cos(nAngle), 16), Blood.Sar(Blood.Sin(nAngle), 16), height - height2, K.kVectorAxe);
    }

    void zomba_StandSeqCallback(Sprite e) => Sound3D(e, 1102, -1);

    void zomba_thinkSearch(Sprite e)
    {
        AiChooseDirection(e, e.GoalAng);
        AiLookForTarget(e);
    }

    void zomba_thinkGoto(Sprite e) => AiThinkGotoThen(e, 921, zombieASearch);

    void zomba_thinkChase(Sprite e)
    {
        var t = e.XTarget;
        if (t == null)
        {
            AiNewState(e, zombieASearch);
            return;
        }
        AiChooseDirection(e, Blood.GetAngle(t.BX - e.BX, t.BY - e.BY));
        if (t.XHealth == 0)
        {
            AiNewState(e, zombieASearch);
            return;
        }
        if (TargetCloakedOrMasked(t))
        {
            AiNewState(e, zombieAGoto);
            return;
        }
        if (AiSeesTarget(e, t, out _, out _, out double nDist, out double nDeltaAngle, false))
        {
            AiSetTarget(e, t);
            if (nDist < 1024 && Math.Abs(nDeltaAngle) < 85)
                AiNewState(e, zombieAHack);
            return;
        }
        AiNewState(e, zombieAGoto);
        e.XTarget = null;
    }

    void zomba_thinkPonder(Sprite e)
    {
        var t = e.XTarget;
        if (t == null || !t.HasX)
        {
            AiNewState(e, zombieASearch);
            return;
        }
        AiChooseDirection(e, Blood.GetAngle(t.BX - e.BX, t.BY - e.BY));
        if (t.XHealth == 0)
        {
            AiNewState(e, zombieASearch);
            return;
        }
        if (TargetCloakedOrMasked(t))
        {
            AiNewState(e, zombieAGoto);
            return;
        }
        if (AiSeesTarget(e, t, out _, out _, out double nDist, out double nDeltaAngle, false))
        {
            AiSetTarget(e, t);
            if (nDist < 1024)
            {
                if (Math.Abs(nDeltaAngle) < 85)
                {
                    Sound3D(e, 1101, 1);
                    AiNewState(e, zombieAHack);
                }
                return;
            }
        }
        AiNewState(e, zombieAChase);
    }

    void zomba_myThinkTarget(Sprite e)
    {
        foreach (var pl in Players)
        {
            if (e.Owner == pl || pl.XHealth == 0 || PowerupCheck(pl, K.kPwUpShadowCloak) > 0)
                continue;
            double x = pl.BX, y = pl.BY, z = pl.BZ, dx = x - e.BX, dy = y - e.BY;
            double nDist = Blood.ApproxDist(dx, dy);
            if (nDist > Info.Dude(e.Type, K.DI_seeDist) && nDist > Info.Dude(e.Type, K.DI_hearDist))
                continue;
            if (!CanSee(x, y, z, e.BX, e.BY, DudeEyeZ(e), pl, e))
                continue;
            double nDeltaAngle = Blood.AngMask(Blood.GetAngle(dx, dy) + 1024 - e.Ang) - 1024;
            if (nDist < Info.Dude(e.Type, K.DI_seeDist) && Math.Abs(nDeltaAngle) <= Info.Dude(e.Type, K.DI_periphery))
            {
                AiSetTarget(e, pl);
                AiActivateDude(e);
            }
            else if (nDist < Info.Dude(e.Type, K.DI_hearDist))
            {
                AiSetTargetXYZ(e, x, y, z);
                AiActivateDude(e);
            }
            else
                continue;
            break;
        }
    }

    void zomba_myThinkSearch(Sprite e)
    {
        AiChooseDirection(e, e.GoalAng);
        zomba_myThinkTarget(e);
    }

    void zomba_entryEZombie(Sprite e)
    {
        e.Type = K.kDudeZombieAxeNormal;
        e.HitTag |= 1;
    }

    void zomba_entryAIdle(Sprite e) => e.XTarget = null;

    void zomba_entryEStand(Sprite e)
    {
        Sound3D(e, 1100, -1);
        e.Ang = Blood.GetAngle(e.TargetX - e.BX, e.TargetY - e.BY);
    }

    // ---------------------------------------------------------------- butcher
    void zombf_HackSeqCallback(Sprite e)
    {
        if (e.Type != K.kDudeZombieButcher || e.XTarget is not Sprite t)
            return;
        double height = Info.Dude(e.Type, K.DI_eyeHeight) * e.YRepeat;
        double height2 = Info.Dude(t.Type, K.DI_eyeHeight) * t.YRepeat;
        ActFireVector(e, 0, 0, Blood.Sar(Blood.Cos(e.Ang), 16), Blood.Sar(Blood.Sin(e.Ang), 16), height - height2, K.kVectorCleaver);
    }

    void zombf_PukeSeqCallback(Sprite e)
    {
        if (e.XTarget is not Sprite t)
            return;
        double height = Info.Dude(e.Type, K.DI_eyeHeight) * e.YRepeat;
        double height2 = Info.Dude(t.Type, K.DI_eyeHeight) * t.YRepeat;
        int nAngle = Blood.GetAngle(e.TargetX - e.BX, e.TargetY - e.BY);
        Sound3D(e, 1203, 1);
        ActFireMissile(e, 0, -(height - height2), Blood.Sar(Blood.Cos(nAngle), 16), Blood.Sar(Blood.Sin(nAngle), 16), 0, K.kMissilePukeGreen);
    }

    void zombf_ThrowSeqCallback(Sprite e) =>
        ActFireMissile(e, 0, -Info.Dude(e.Type, K.DI_eyeHeight), Blood.Sar(Blood.Cos(e.Ang), 16), Blood.Sar(Blood.Sin(e.Ang), 16), 0, K.kMissileButcherKnife);

    void zombf_thinkSearch(Sprite e) => AiThinkSearchTarget(e);
    void zombf_thinkGoto(Sprite e) => AiThinkGotoThen(e, 512, zombieFSearch);

    /// <summary>Attack, unless the shot would hit one of its own kind (then dodge).</summary>
    void AiTryAttackSameType(Sprite e, AiState attackState, AiState dodgeState, double dx, double dy)
    {
        int nHit = HitScan(e, e.BZ, dx, dy, 0, 0);
        if (nHit == 3 && e.Type == ((Sprite)HitEnt).Type)
            AiNewState(e, dodgeState);
        else
            AiNewState(e, attackState);
    }

    void zombf_thinkChase(Sprite e)
    {
        var t = e.XTarget;
        if (t == null)
        {
            AiNewState(e, zombieFGoto);
            return;
        }
        AiChooseDirection(e, Blood.GetAngle(t.BX - e.BX, t.BY - e.BY));
        if (t.XHealth == 0 || TargetCloakedOrMasked(t))
        {
            AiNewState(e, zombieFSearch);
            return;
        }
        if (AiSeesTarget(e, t, out double dx, out double dy, out double nDist, out double nDeltaAngle))
        {
            AiSetTarget(e, t);
            double ad = Math.Abs(nDeltaAngle);
            if (nDist < 5120 && nDist > 3584 && ad < 85)
                AiTryAttackSameType(e, zombieFThrow, zombieFDodge, dx, dy);
            else if (nDist < 5120 && nDist > 1536 && ad < 85)
                AiTryAttackSameType(e, zombieFPuke, zombieFDodge, dx, dy);
            else if (nDist < 1024 && ad < 85)
                AiTryAttackSameType(e, zombieFHack, zombieFDodge, dx, dy);
            return;
        }
        AiNewState(e, zombieFSearch);
        e.XTarget = null;
    }

    // ---------------------------------------------------------------- burning dudes
    void burn_BurnSeqCallback(Sprite e) { }

    AiState BurnState(Sprite e, int which) => e.Type switch
    {
        K.kDudeBurningCultist => which == 0 ? cultistBurnSearch : which == 1 ? cultistBurnGoto : cultistBurnAttack,
        K.kDudeBurningZombieAxe => which == 0 ? zombieABurnSearch : which == 1 ? zombieABurnGoto : zombieABurnAttack,
        K.kDudeBurningZombieButcher => which == 0 ? zombieFBurnSearch : which == 1 ? zombieFBurnGoto : zombieFBurnAttack,
        K.kDudeBurningInnocent => which == 0 ? innocentBurnSearch : which == 1 ? innocentBurnGoto : innocentBurnAttack,
        K.kDudeBurningBeast => which == 0 ? beastBurnSearch : which == 1 ? beastBurnGoto : beastBurnAttack,
        K.kDudeBurningTinyCaleb => which == 0 ? tinycalebBurnSearch : which == 1 ? tinycalebBurnGoto : tinycalebBurnAttack,
        _ => null,
    };

    void burn_thinkSearch(Sprite e) => AiThinkSearchTarget(e);

    void burn_thinkGoto(Sprite e)
    {
        double dx = e.TargetX - e.BX, dy = e.TargetY - e.BY;
        int nAngle = Blood.GetAngle(dx, dy);
        double nDist = Blood.ApproxDist(dx, dy);
        AiChooseDirection(e, nAngle);
        if (nDist < 512 && Math.Abs(e.Ang - nAngle) < Info.Dude(e.Type, K.DI_periphery))
            AiNewState(e, BurnState(e, 0));
        aiThinkTarget(e);
    }

    void burn_thinkChase(Sprite e)
    {
        var t = e.XTarget;
        if (t == null)
        {
            AiNewState(e, BurnState(e, 1));
            return;
        }
        AiChooseDirection(e, Blood.GetAngle(t.BX - e.BX, t.BY - e.BY));
        if (t.XHealth == 0)
        {
            AiNewState(e, BurnState(e, 0));
            return;
        }
        if (AiSeesTarget(e, t, out _, out _, out double nDist, out double nDeltaAngle))
        {
            AiSetTarget(e, t);
            if (nDist < 819 && Math.Abs(nDeltaAngle) < 85)
                AiNewState(e, BurnState(e, 2));
            return;
        }
        AiNewState(e, BurnState(e, 1));
        e.XTarget = null;
    }

    // ---------------------------------------------------------------- innocent
    void innoc_thinkSearch(Sprite e) => AiThinkSearchTarget(e);
    void innoc_thinkGoto(Sprite e) => AiThinkGotoThen(e, 512, innocentSearch);

    void innoc_thinkChase(Sprite e)
    {
        var t = e.XTarget;
        if (t == null)
        {
            AiNewState(e, innocentGoto);
            return;
        }
        AiChooseDirection(e, Blood.GetAngle(t.BX - e.BX, t.BY - e.BY));
        if (t.XHealth == 0 || t is Player)
        {
            AiNewState(e, innocentSearch);
            return;
        }
        if (AiSeesTarget(e, t, out _, out _, out double nDist, out double nDeltaAngle))
        {
            AiSetTarget(e, t);
            if (nDist < 1638 && Math.Abs(nDeltaAngle) < 85)
                AiNewState(e, innocentIdle);
            return;
        }
        AiPlay3DSound(e, 7000 + Random(6), AI_SFX_PRIORITY_1, -1);
        AiNewState(e, innocentGoto);
        e.XTarget = null;
    }

    // ---------------------------------------------------------------- rat
    void rat_BiteSeqCallback(Sprite e)
    {
        if (e.XTarget is Player t)
            ActFireVector(e, 0, 0, Blood.Sar(Blood.Cos(e.Ang), 16), Blood.Sar(Blood.Sin(e.Ang), 16), t.BZ - e.BZ, K.kVectorRatBite);
    }

    void rat_thinkSearch(Sprite e) => AiThinkSearchTarget(e);
    void rat_thinkGoto(Sprite e) => AiThinkGotoThen(e, 512, ratSearch);

    /// <summary>The simple melee chase (rat, hand).</summary>
    void SimpleMeleeChase(Sprite e, AiState gotoSt, AiState searchSt, AiState attackSt, double range)
    {
        var t = e.XTarget;
        if (t == null)
        {
            AiNewState(e, gotoSt);
            return;
        }
        AiChooseDirection(e, Blood.GetAngle(t.BX - e.BX, t.BY - e.BY));
        if (t.XHealth == 0 || (t is Player && PowerupCheck(t, K.kPwUpShadowCloak) > 0))
        {
            AiNewState(e, searchSt);
            return;
        }
        if (AiSeesTarget(e, t, out _, out _, out double nDist, out double nDeltaAngle))
        {
            AiSetTarget(e, t);
            if (nDist < range && Math.Abs(nDeltaAngle) < 85)
                AiNewState(e, attackSt);
            return;
        }
        AiNewState(e, gotoSt);
        e.XTarget = null;
    }

    void rat_thinkChase(Sprite e) => SimpleMeleeChase(e, ratGoto, ratSearch, ratBite, 921);

    // ---------------------------------------------------------------- hand
    void hand_HandJumpSeqCallback(Sprite e)
    {
        if (e.XTarget is Player t && t.Hand == 0)
        {
            t.Hand = 1;
            ActPostSprite(e, K.kStatFree);
        }
    }

    void hand_thinkSearch(Sprite e) => AiThinkSearchTarget(e);
    void hand_thinkGoto(Sprite e) => AiThinkGotoThen(e, 512, handSearch);
    void hand_thinkChase(Sprite e) => SimpleMeleeChase(e, handGoto, handSearch, handJump, 563);
}
