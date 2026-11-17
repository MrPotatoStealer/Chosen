using System;
using Chosen.Data;
using Godot;

namespace Chosen.Sim;

/// <summary>Cerberus (aicerber.cpp) and bone eel (aiboneel.cpp) AI, via Blood3D's server/ai_cerber.qc and ai_eel.qc.</summary>
public sealed partial class World
{
    // ---------------------------------------------------------------- cerberus
    void cerb_BiteSeqCallback(Sprite e)
    {
        var t = e.XTarget;
        if (t == null)
            return;
        double dx = Blood.Sar(Blood.Cos(e.Ang), 16), dy = Blood.Sar(Blood.Sin(e.Ang), 16), dz = t.BZ - e.BZ;
        ActFireVector(e, 350, -100, dx, dy, dz, K.kVectorCerberusHack);
        ActFireVector(e, -350, 0, dx, dy, dz, K.kVectorCerberusHack);
        ActFireVector(e, 0, 0, dx, dy, dz, K.kVectorCerberusHack);
    }

    /// <summary>Cerberus target tracking; useAimHeight selects the BurnSeqCallback2 variant.</summary>
    (double dx, double dy, double dz) CerbAimEx(Sprite e, bool useAimHeight, double at10)
    {
        double x = e.BX, y = e.BY;
        double z = Info.Dude(e.Type, K.DI_eyeHeight) * e.YRepeat;   // "z = height; // ???" in the original
        double aimDx = Blood.Sar(Blood.Cos(e.Ang), 16), aimDy = Blood.Sar(Blood.Sin(e.Ang), 16), aimDz = e.DudeSlope;
        double nClosest = 2147483647;
        foreach (var e2 in StatList(K.kStatDude))
        {
            if (e == e2 || (e2.HitTag & 8) == 0)
                continue;
            double x2 = e2.BX, y2 = e2.BY, z2 = e2.BZ;
            double nDist = Blood.ApproxDist(x2 - x, y2 - y);
            if (nDist == 0 || nDist > 10240)
                continue;
            double t = Blood.Int(nDist * 4096 / at10);
            x2 += Blood.Sar(e2.XVel * t, 12);
            y2 += Blood.Sar(e2.YVel * t, 12);
            z2 += Blood.Sar(e2.ZVel * t, 8);
            double tx = x + Blood.MulScale30(Blood.Cos(e.Ang), nDist), ty = y + Blood.MulScale30(Blood.Sin(e.Ang), nDist);
            double tz = z + Math.Floor(e.DudeSlope * nDist / 1024);
            double tsr = Math.Floor(9460 * nDist / 1024);
            var (top, bottom) = Extents(e2);
            if (tz - tsr > bottom || tz + tsr < top)
                continue;
            double dx = Blood.Sar(tx - x2, 4), dy = Blood.Sar(ty - y2, 4), dz = Blood.Sar(tz - z2, 8);
            double nDist2 = Math.Floor(Math.Sqrt(dx * dx + dy * dy + dz * dz));
            if (nDist2 >= nClosest)
                continue;
            int nAngle = Blood.GetAngle(x2 - x, y2 - y);
            if (Math.Abs(Blood.AngMask(nAngle - e.Ang + 1024) - 1024) > 256)
                continue;
            double tz2 = useAimHeight ? (z2 - Info.Dude(e2.Type, K.DI_aimHeight) * e2.YRepeat * 4) - z : e2.BZ - e.BZ;
            if (CanSee(x, y, z, x2, y2, z2, e, e2))
            {
                nClosest = nDist2;
                aimDx = Blood.Sar(Blood.Cos(nAngle), 16);
                aimDy = Blood.Sar(Blood.Sin(nAngle), 16);
                aimDz = Blood.DivScale10(tz2, nDist);
            }
            else
                aimDz = tz2;
        }
        return (aimDx, aimDy, aimDz);
    }

    void cerb_BurnSeqCallback(Sprite e)
    {
        if (e.XTarget == null)
            return;
        var (dx, dy, dz) = CerbAimEx(e, false, 1747626);
        if (e.Type == K.kDudeCerberusTwoHead)
        {
            ActFireMissile(e, -350, 0, dx, dy, dz, K.kMissileFireballCerberus);
            ActFireMissile(e, 350, -100, dx, dy, dz, K.kMissileFireballCerberus);
        }
        else if (e.Type == K.kDudeCerberusOneHead)
            ActFireMissile(e, 350, -100, dx, dy, dz, K.kMissileFireballCerberus);
    }

    void cerb_BurnSeqCallback2(Sprite e)
    {
        if (e.XTarget == null)
            return;
        double ax = Blood.Sar(Blood.Cos(e.Ang), 16), ay = Blood.Sar(Blood.Sin(e.Ang), 16);
        var (dx, dy, dz) = CerbAimEx(e, true, 1747626);
        if (e.Type == K.kDudeCerberusTwoHead)
        {
            ActFireMissile(e, 350, -100, dx, dy, -dz, K.kMissileFlameHound);
            ActFireMissile(e, -350, 0, ax, ay, 0, K.kMissileFlameHound);
        }
        else if (e.Type == K.kDudeCerberusOneHead)
            ActFireMissile(e, 350, -100, dx, dy, -dz, K.kMissileFlameHound);
    }

    void cerb_thinkSearch(Sprite e) => AiThinkSearchTarget(e);

    void cerb_thinkTarget(Sprite e) => FlyerThinkTarget(e, e.Type == K.kDudeCerberusTwoHead ? cerberus139890 : cerberus1398AC);

    AiState CerbPick(Sprite e, AiState two, AiState one) => e.Type == K.kDudeCerberusTwoHead ? two : one;

    void cerb_thinkGoto(Sprite e) => AiThinkGotoThen(e, 512, CerbPick(e, cerberusSearch, cerberus2Search));

    void cerb_thinkChase(Sprite e)
    {
        var t = e.XTarget;
        if (t == null)
        {
            AiNewState(e, CerbPick(e, cerberusGoto, cerberus2Goto));
            return;
        }
        AiChooseDirection(e, Blood.GetAngle(t.BX - e.BX, t.BY - e.BY));
        if (t.XHealth == 0 || (t is Player && PowerupCheck(t, K.kPwUpShadowCloak) > 0))
        {
            AiNewState(e, CerbPick(e, cerberusSearch, cerberus2Search));
            return;
        }
        if (AiSeesTarget(e, t, out double dx, out double dy, out double nDist, out double nDeltaAngle))
        {
            AiSetTarget(e, t);
            double ad = Math.Abs(nDeltaAngle);
            if (nDist < 6912 && nDist > 3328 && ad < 85)
                AiNewState(e, CerbPick(e, cerberusBurn, cerberus2Burn));
            else if (nDist < 2816 && nDist > 1280 && ad < 85)
                AiNewState(e, CerbPick(e, cerberus3Burn, cerberus4Burn));
            else if (nDist < 512 && ad < 85)
            {
                int nHit = HitScan(e, e.BZ, dx, dy, 0, 0);
                if (nHit == 3)
                {
                    var h = (Sprite)HitEnt;
                    if (e.Type != h.Type && h.Type != K.kDudeHellHound)
                        AiNewState(e, CerbPick(e, cerberusBite, cerberus2Bite));
                }
                else if (nHit != 0 && nHit != 4)
                    AiNewState(e, CerbPick(e, cerberusBite, cerberus2Bite));
            }
            return;
        }
        AiNewState(e, CerbPick(e, cerberusGoto, cerberus2Goto));
        e.XTarget = null;
    }

    // ---------------------------------------------------------------- bone eel
    void eel_BiteSeqCallback(Sprite e)
    {
        var t = e.XTarget;
        if (t == null)
        {
            AiNewState(e, eelSearch);
            return;
        }
        double height = e.YRepeat * Info.Dude(e.Type, K.DI_eyeHeight) * 4;
        double height2 = t.YRepeat * Info.Dude(t.Type, K.DI_eyeHeight) * 4;
        ActFireVector(e, 0, 0, Blood.Sar(Blood.Cos(e.Ang), 16), Blood.Sar(Blood.Sin(e.Ang), 16), height2 - height, K.kVectorBoneelBite);
    }

    void eel_thinkTarget(Sprite e)
    {
        if (e.DudeActive != 0 && e.ThinkTime < 10)
            e.ThinkTime++;
        else if (e.ThinkTime >= 10 && e.DudeActive != 0)
        {
            e.ThinkTime = 0;
            e.GoalAng = (int)Blood.AngMask(e.GoalAng + 256);
            var home = e.BaseOrigin;
            AiSetTargetXYZ(e, Q2BX(home), Q2BY(home), Q2BZ(home));
            AiNewState(e, eelTurn);
            return;
        }
        // the rest is the flyers' player search (without the wander-home part, done above)
        if (!Chance(Info.Dude(e.Type, K.DI_alertChance)))
            return;
        foreach (var pl in Players)
        {
            if (pl.XHealth == 0 || PowerupCheck(pl, K.kPwUpShadowCloak) > 0)
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
                e.ThinkTime = 0;
                AiSetTarget(e, pl);
                AiActivateDude(e);
            }
            else if (nDist < Info.Dude(e.Type, K.DI_hearDist))
            {
                e.ThinkTime = 0;
                AiSetTargetXYZ(e, x, y, z);
                AiActivateDude(e);
            }
            else
                continue;
            break;
        }
    }

    void eel_thinkSearch(Sprite e)
    {
        AiChooseDirection(e, e.GoalAng);
        eel_thinkTarget(e);
    }

    void eel_thinkGoto(Sprite e)
    {
        double dx = e.TargetX - e.BX, dy = e.TargetY - e.BY;
        int nAngle = Blood.GetAngle(dx, dy);
        double nDist = Blood.ApproxDist(dx, dy);
        AiChooseDirection(e, nAngle);
        if (nDist < 512 && Math.Abs(e.Ang - nAngle) < Info.Dude(e.Type, K.DI_periphery))
            AiNewState(e, eelSearch);
        eel_thinkTarget(e);
    }

    void eel_thinkPonder(Sprite e)
    {
        var t = e.XTarget;
        if (t == null)
        {
            AiNewState(e, eelSearch);
            return;
        }
        double dx = t.BX - e.BX, dy = t.BY - e.BY;
        AiChooseDirection(e, Blood.GetAngle(dx, dy));
        if (t.XHealth == 0)
        {
            AiNewState(e, eelSearch);
            return;
        }
        double nDist = Blood.ApproxDist(dx, dy);
        if (nDist <= Info.Dude(e.Type, K.DI_seeDist))
        {
            double ad = Math.Abs(Blood.AngMask(Blood.GetAngle(dx, dy) + 1024 - e.Ang) - 1024);
            double height = Info.Dude(e.Type, K.DI_eyeHeight) * e.YRepeat * 4;
            double height2 = Info.Dude(t.Type, K.DI_eyeHeight) * t.YRepeat * 4;
            if (CanSee(t.BX, t.BY, t.BZ, e.BX, e.BY, e.BZ - height, t, e))
            {
                AiSetTarget(e, t);
                double dh = height2 - height;
                if (dh < -8192 && nDist < 6144 && nDist > 3072 && ad < 85) AiNewState(e, eelDodgeUp);
                else if (dh > 3276 && nDist < 6144 && nDist > 3072 && ad < 85) AiNewState(e, eelDodgeDown);
                else if (dh < 3276 && nDist < 921 && ad < 85) AiNewState(e, eelDodgeUp);
                else if (dh > 3276 && nDist < 5120 && nDist > 2048 && ad < 85) AiNewState(e, eelDodgeDown);
                else if (dh < -8192 && nDist < 5120 && nDist > 2048 && ad < 85) AiNewState(e, eelDodgeUp);
                else if (dh < -8192 && ad < 85 && nDist > 5120) AiNewState(e, eelDodgeUp);
                else if (dh > 3276) AiNewState(e, eelDodgeDown);
                else AiNewState(e, eelDodgeUp);
                return;
            }
        }
        AiNewState(e, eelGoto);
        e.XTarget = null;
    }

    void eel_MoveDodgeUp(Sprite e) => FlyerDodge(e, -32768, false);
    void eel_MoveDodgeDown(Sprite e) => FlyerDodge(e, 279620, true);

    void eel_thinkChase(Sprite e)
    {
        var t = e.XTarget;
        if (t == null)
        {
            AiNewState(e, eelGoto);
            return;
        }
        double dx = t.BX - e.BX, dy = t.BY - e.BY;
        AiChooseDirection(e, Blood.GetAngle(dx, dy));
        if (t.XHealth == 0 || (t is Player && PowerupCheck(t, K.kPwUpShadowCloak) > 0))
        {
            AiNewState(e, eelSearch);
            return;
        }
        double nDist = Blood.ApproxDist(dx, dy);
        if (nDist <= Info.Dude(e.Type, K.DI_seeDist))
        {
            double nDeltaAngle = Blood.AngMask(Blood.GetAngle(dx, dy) + 1024 - e.Ang) - 1024;
            double height = Info.Dude(e.Type, K.DI_eyeHeight) * e.YRepeat * 4;
            var (top, _) = Extents(e);
            var (top2, bottom2) = Extents(t);
            if (CanSee(t.BX, t.BY, t.BZ, e.BX, e.BY, e.BZ - height, t, e)
                && nDist < Info.Dude(e.Type, K.DI_seeDist) && Math.Abs(nDeltaAngle) <= Info.Dude(e.Type, K.DI_periphery))
            {
                AiSetTarget(e, t);
                double ad = Math.Abs(nDeltaAngle);
                if (nDist < 921 && top2 > top && ad < 85)
                    AiNewState(e, eelSwoop);
                else if (nDist <= 921 && ad < 85)
                    AiNewState(e, eelBite);
                else if (bottom2 > top && ad < 85)
                    AiNewState(e, eelSwoop);
                else if (top2 < top && ad < 85)
                    AiNewState(e, eelFly);
            }
            return;
        }
        e.XTarget = null;
        AiNewState(e, eelSearch);
    }

    double EelAccel(Sprite e) => (Info.Dude(e.Type, K.DI_frontSpeed) - Blood.Int(Blood.Int((4 - Skill) * 67108864.0 / 120) / 120)) * 4;

    void eel_MoveForward(Sprite e)
    {
        double nAng = AiTurnToGoal(e);
        double nAccel = EelAccel(e);
        if (Math.Abs(nAng) > 341)
            return;
        if (e.XTarget == null)
            e.Ang = (int)Blood.AngMask(e.Ang + 256);
        double nDist = Blood.ApproxDist(e.TargetX - e.BX, e.TargetY - e.BY);
        if (nDist <= 921)
            return;
        var (t1, t2) = VelToLocal(e);
        t1 += e.XTarget == null ? nAccel : Blood.Sar(nAccel, 1);
        LocalToVel(e, t1, t2);
    }

    void eel_MoveSwoop(Sprite e)
    {
        double nAng = AiTurnToGoal(e);
        double nAccel = EelAccel(e);
        if (Math.Abs(nAng) > 341)
            return;
        double nDist = Blood.ApproxDist(e.TargetX - e.BX, e.TargetY - e.BY);
        if (Chance(32768) && nDist <= 921)
            return;
        var (t1, t2) = VelToLocal(e);
        LocalToVel(e, t1 + Blood.Sar(nAccel, 1), t2);
        e.ZVel = 139810;
    }

    void eel_MoveAscend(Sprite e)
    {
        double nAng = AiTurnToGoal(e);
        double nAccel = EelAccel(e);
        if (Math.Abs(nAng) > 341)
            return;
        double nDist = Blood.ApproxDist(e.TargetX - e.BX, e.TargetY - e.BY);
        if (Chance(16384) && nDist <= 921)
            return;
        var (t1, t2) = VelToLocal(e);
        LocalToVel(e, t1 + Blood.Sar(nAccel, 1), t2);
        e.ZVel = -32768;
    }

    void eel_MoveToCeil(Sprite e)
    {
        double z = e.BZ;
        if (z - e.TargetZ < 4096)
        {
            e.DudeActive = 0;
            e.HitTag = 0;
            AiNewState(e, eelIdle);
            return;
        }
        double cz = SpriteBSector(e) is int ns && ns >= 0 ? Board.Sectors[ns].CeilingZ : e.BZ;
        AiSetTargetXYZ(e, e.BX, e.BY, cz);
    }
}
