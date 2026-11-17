using System;
using Chosen.Data;
using Godot;

namespace Chosen.Sim;

/// <summary>Bat (aibat.cpp) and gargoyle (aigarg.cpp) AI, with the flyers' shared moves; via Blood3D's
/// server/ai_bat.qc and ai_garg.qc.</summary>
public sealed partial class World
{
    double AiTurnToGoal(Sprite e)
    {
        double nAng = Blood.AngMask(e.GoalAng + 1024 - e.Ang) - 1024;
        int nTurnRange = AiTurnRange(e);
        e.Ang = (int)Blood.AngMask(e.Ang + Blood.ClipRange(nAng, -nTurnRange, nTurnRange));
        return nAng;
    }

    double FloorZBelow(Sprite e)
    {
        return SpriteBSector(e) is int ns && ns >= 0 ? Board.FloorZAt(ns, e.BX, e.BY) : e.BZ;
    }

    // ---------------------------------------------------------------- flyers' shared moves
    void FlyerDodge(Sprite e, double zv, bool requireDir)
    {
        AiTurnToGoal(e);
        if (requireDir && e.DodgeDir == 0)
            return;
        var (t1, t2) = VelToLocal(e);
        t2 += e.DodgeDir > 0 ? Info.Dude(e.Type, K.DI_sideSpeed) : -Info.Dude(e.Type, K.DI_sideSpeed);
        LocalToVel(e, t1, t2);
        e.ZVel = zv;
    }

    /// <summary>The flying dudes' target think (gargoyle, phantasm): wanders back home after a while.</summary>
    void FlyerThinkTarget(Sprite e, AiState turnState)
    {
        if (e.DudeActive != 0 && e.ThinkTime < 10)
            e.ThinkTime++;
        else if (e.ThinkTime >= 10 && e.DudeActive != 0)
        {
            e.GoalAng = (int)Blood.AngMask(e.GoalAng + 256);
            var home = e.BaseOrigin;
            AiSetTargetXYZ(e, Q2BX(home), Q2BY(home), Q2BZ(home));
            AiNewState(e, turnState);
            return;
        }
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

    /// <summary>Forward flight toward the target (bat, gargoyle, phantasm): nearDist: how close counts as there.</summary>
    void FlyerMoveForward(Sprite e, double nearDist)
    {
        double nAng = AiTurnToGoal(e);
        double nAccel = Info.Dude(e.Type, K.DI_frontSpeed) * 4;
        if (Math.Abs(nAng) > 341)
            return;
        if (e.XTarget == null)
            e.Ang = (int)Blood.AngMask(e.Ang + 256);
        double nDist = Blood.ApproxDist(e.TargetX - e.BX, e.TargetY - e.BY);
        if (Random(64) < 32 && nDist <= nearDist)
            return;
        var (t1, t2) = VelToLocal(e);
        t1 += e.XTarget == null ? nAccel : Blood.Sar(nAccel, 1);
        LocalToVel(e, t1, t2);
    }

    // ---------------------------------------------------------------- bat
    void bat_BiteSeqCallback(Sprite e)
    {
        var t = e.XTarget;
        if (t == null)
        {
            AiNewState(e, batSearch);
            return;
        }
        double height = e.YRepeat * Info.Dude(e.Type, K.DI_eyeHeight) * 4;
        double height2 = t.YRepeat * Info.Dude(t.Type, K.DI_eyeHeight) * 4;
        ActFireVector(e, 0, 0, Blood.Sar(Blood.Cos(e.Ang), 16), Blood.Sar(Blood.Sin(e.Ang), 16), height2 - height, K.kVectorBatBite);
    }

    void bat_thinkTarget(Sprite e)
    {
        if (e.DudeActive != 0 && e.ThinkTime < 10)
            e.ThinkTime++;
        else if (e.ThinkTime >= 10 && e.DudeActive != 0)
        {
            e.ThinkTime = 0;
            e.GoalAng = (int)Blood.AngMask(e.GoalAng + 256);
            var home = e.BaseOrigin;
            AiSetTargetXYZ(e, Q2BX(home), Q2BY(home), Q2BZ(home));
            AiNewState(e, batTurn);
            return;
        }
        if (Chance(Info.Dude(e.Type, K.DI_alertChance)))
            AiTryTargetPlayers(e);
    }

    void bat_thinkSearch(Sprite e)
    {
        AiChooseDirection(e, e.GoalAng);
        bat_thinkTarget(e);
    }

    void bat_thinkGoto(Sprite e)
    {
        double dx = e.TargetX - e.BX, dy = e.TargetY - e.BY;
        int nAngle = Blood.GetAngle(dx, dy);
        double nDist = Blood.ApproxDist(dx, dy);
        AiChooseDirection(e, nAngle);
        if (nDist < 512 && Math.Abs(e.Ang - nAngle) < Info.Dude(e.Type, K.DI_periphery))
            AiNewState(e, batSearch);
        bat_thinkTarget(e);
    }

    void bat_thinkPonder(Sprite e)
    {
        var t = e.XTarget;
        if (t == null)
        {
            AiNewState(e, batSearch);
            return;
        }
        double dx = t.BX - e.BX, dy = t.BY - e.BY;
        AiChooseDirection(e, Blood.GetAngle(dx, dy));
        if (t.XHealth == 0)
        {
            AiNewState(e, batSearch);
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
                if (dh < 12288 && nDist < 6144 && nDist > 3072 && ad < 85) AiNewState(e, batDodgeUp);
                else if (dh > 20480 && nDist < 6144 && nDist > 3072 && ad < 85) AiNewState(e, batDodgeDown);
                else if (dh < 8192 && nDist < 512 && ad < 85) AiNewState(e, batDodgeUp);
                else if (dh > 24576 && nDist < 5120 && nDist > 2048 && ad < 85) AiNewState(e, batDodgeDown);
                else if (dh < 8192 && nDist < 5120 && nDist > 2048 && ad < 85) AiNewState(e, batDodgeUp);
                else if (dh < 8192 && ad < 85 && nDist > 5120) AiNewState(e, batDodgeUp);
                else if (dh > 16384) AiNewState(e, batDodgeDown);
                else AiNewState(e, batDodgeUp);
                return;
            }
        }
        AiNewState(e, batGoto);
        e.XTarget = null;
    }

    void bat_MoveDodgeUp(Sprite e) => FlyerDodge(e, -338602, false);
    void bat_MoveDodgeDown(Sprite e) => FlyerDodge(e, 279620, true);

    void bat_thinkChase(Sprite e)
    {
        var t = e.XTarget;
        if (t == null)
        {
            AiNewState(e, batGoto);
            return;
        }
        double dx = t.BX - e.BX, dy = t.BY - e.BY;
        AiChooseDirection(e, Blood.GetAngle(dx, dy));
        if (t.XHealth == 0 || (t is Player && PowerupCheck(t, K.kPwUpShadowCloak) > 0))
        {
            AiNewState(e, batSearch);
            return;
        }
        double nDist = Blood.ApproxDist(dx, dy);
        if (nDist <= Info.Dude(e.Type, K.DI_seeDist))
        {
            double nDeltaAngle = Blood.AngMask(Blood.GetAngle(dx, dy) + 1024 - e.Ang) - 1024;
            double height = Info.Dude(e.Type, K.DI_eyeHeight) * e.YRepeat * 4;
            double height2 = Info.Dude(e.Type, K.DI_eyeHeight) * t.YRepeat * 4;
            var (_, bottom) = Extents(e);
            if (CanSee(t.BX, t.BY, t.BZ, e.BX, e.BY, e.BZ - height, t, e))
            {
                if (nDist < Info.Dude(e.Type, K.DI_seeDist) && Math.Abs(nDeltaAngle) <= Info.Dude(e.Type, K.DI_periphery))
                {
                    AiSetTarget(e, t);
                    double floorZ = FloorZBelow(e);
                    double ad = Math.Abs(nDeltaAngle);
                    if (height2 - height < 8192 && nDist < 512 && ad < 85)
                        AiNewState(e, batBite);
                    else if ((height2 - height > 20480 || floorZ - bottom > 20480) && nDist < 5120 && nDist > 2048 && ad < 85)
                        AiNewState(e, batSwoop);
                    else if ((height2 - height < 12288 || floorZ - bottom < 12288) && ad < 85)
                        AiNewState(e, batFly);
                    return;
                }
            }
            else
            {
                AiNewState(e, batFly);
                return;
            }
        }
        e.XTarget = null;
        AiNewState(e, batHide);
    }

    void bat_MoveForward(Sprite e) => FlyerMoveForward(e, 512);

    void bat_MoveSwoop(Sprite e)
    {
        double nAng = AiTurnToGoal(e);
        double nAccel = Info.Dude(e.Type, K.DI_frontSpeed) * 4;
        if (Math.Abs(nAng) > 341)
        {
            e.GoalAng = (int)Blood.AngMask(e.Ang + 512);
            return;
        }
        double nDist = Blood.ApproxDist(e.TargetX - e.BX, e.TargetY - e.BY);
        if (Chance(1536) && nDist <= 512)
            return;
        var (t1, t2) = VelToLocal(e);
        LocalToVel(e, t1 + Blood.Sar(nAccel, 1), t2);
        e.ZVel = 279620;
    }

    void bat_MoveFly(Sprite e)
    {
        double nAng = AiTurnToGoal(e);
        double nAccel = Info.Dude(e.Type, K.DI_frontSpeed) * 4;
        if (Math.Abs(nAng) > 341)
        {
            e.Ang = (int)Blood.AngMask(e.Ang + 512);
            return;
        }
        double nDist = Blood.ApproxDist(e.TargetX - e.BX, e.TargetY - e.BY);
        if (Chance(16384) && nDist <= 512)
            return;
        var (t1, t2) = VelToLocal(e);
        LocalToVel(e, t1 + Blood.Sar(nAccel, 1), t2);
        e.ZVel = -185685;
    }

    void bat_MoveToCeil(Sprite e)
    {
        if (e.BZ - e.TargetZ < 4096)
        {
            e.DudeActive = 0;
            e.HitTag = 0;
            AiNewState(e, batIdle);
        }
        else
        {
            AiSetTargetXYZ(e, e.BX, e.BY, SpriteBSector(e) is int ns && ns >= 0 ? Board.Sectors[ns].CeilingZ : e.BZ);
        }
    }

    // ---------------------------------------------------------------- gargoyle
    void garg_SlashFSeqCallback(Sprite e)
    {
        var t = e.XTarget;
        if (t == null)
            return;
        double height = e.YRepeat * Info.Dude(e.Type, K.DI_eyeHeight) * 4;
        double height2 = t.YRepeat * Info.Dude(t.Type, K.DI_eyeHeight) * 4;
        double dz = height - height2;
        double dx = Blood.Sar(Blood.Cos(e.Ang), 16), dy = Blood.Sar(Blood.Sin(e.Ang), 16);
        ActFireVector(e, 0, 0, dx, dy, dz, K.kVectorGargSlash);
        int r1 = Random(50), r2 = Random(50);
        ActFireVector(e, 0, 0, dx + r2, dy - r1, dz, K.kVectorGargSlash);
        r1 = Random(50);
        r2 = Random(50);
        ActFireVector(e, 0, 0, dx - r2, dy + r1, dz, K.kVectorGargSlash);
    }

    void garg_ThrowFSeqCallback(Sprite e) => ActFireThing(e, 0, 0, e.DudeSlope - 7500, K.kThingBone, 978670);

    /// <summary>Target-tracking aim (gargoyle stone blast, phantasm blast).</summary>
    (double dx, double dy, double dz) AiTrackAim(Sprite e, double x, double y, double z, double at8, double at10)
    {
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
            if (at10 != 0)
            {
                double t = Blood.Int(nDist * 4096 / at10);
                x2 += Blood.Sar(e2.XVel * t, 12);
                y2 += Blood.Sar(e2.YVel * t, 12);
                z2 += Blood.Sar(e2.ZVel * t, 8);
            }
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
            if (Math.Abs(Blood.AngMask(nAngle - e.Ang + 1024) - 1024) > at8)
                continue;
            double tz2 = e2.BZ - e.BZ;
            if (CanSee(x, y, z, x2, y2, z2, e, e2))
            {
                nClosest = nDist2;
                aimDx = Blood.Sar(Blood.Cos(nAngle), 16);
                aimDy = Blood.Sar(Blood.Sin(nAngle), 16);
                if (tz2 > -819)
                    aimDz = Blood.DivScale10(tz2, nDist);
                else if (tz2 < -819 && tz2 > -2867)
                    aimDz = Blood.DivScale10(tz2, nDist) + 9460;
                else if (tz2 < -2867 && tz2 > -12288)
                    aimDz = Blood.DivScale10(tz2, nDist) + 9460;
                else if (tz2 < -12288)
                    aimDz = Blood.DivScale10(tz2, nDist) - 7500;
                else
                    aimDz = Blood.DivScale10(tz2, nDist);
            }
            else
                aimDz = Blood.DivScale10(tz2, nDist);
        }
        return (aimDx, aimDy, aimDz);
    }

    void garg_BlastSSeqCallback(Sprite e)
    {
        WRand();
        var t = e.XTarget;
        double height = e.YRepeat * Info.Dude(e.Type, K.DI_eyeHeight) * 4;
        // the original passes z = height (not the sprite's z + height), kept for fidelity
        var (dx, dy, dz) = AiTrackAim(e, e.BX, e.BY, height, 256, 1747626);
        if (t is Player || IsModern)      // (a modern map: at other dudes too)
        {
            ActFireMissile(e, -120, 0, dx, dy, dz, K.kMissileArcGargoyle);
            ActFireMissile(e, 120, 0, dx, dy, dz, K.kMissileArcGargoyle);
        }
    }

    void garg_ThrowSSeqCallback(Sprite e) =>
        ActFireThing(e, 0, 0, e.DudeSlope - 7500, K.kThingBone, Chance(24576) ? 1258291 : 1118481);

    void garg_thinkTarget(Sprite e) => FlyerThinkTarget(e, gargoyleTurn);

    void garg_thinkSearch(Sprite e)
    {
        AiChooseDirection(e, e.GoalAng);
        AiLookForTarget(e);
    }

    void garg_thinkGoto(Sprite e) => AiThinkGotoThen(e, 512, gargoyleFSearch);

    void garg_MoveDodgeUp(Sprite e) => FlyerDodge(e, -120149, false);
    void garg_MoveDodgeDown(Sprite e) => FlyerDodge(e, 279620, true);

    /// <summary>Gargoyle attack choice: walls never; its own kind or otherType in the way, no.</summary>
    void GargTryAttack(Sprite e, AiState st, int snd, double dx, double dy, int otherType)
    {
        int nHit = HitScan(e, e.BZ, dx, dy, 0, 0);
        if (nHit == 0 || nHit == 4)
            return;
        if (nHit == 3)
        {
            var h = (Sprite)HitEnt;
            if (e.Type == h.Type || h.Type == otherType)
                return;
        }
        if (snd > 0)
            Sound3D(e, snd);
        AiNewState(e, st);
    }

    void garg_thinkChase(Sprite e)
    {
        var t = e.XTarget;
        if (t == null)
        {
            AiNewState(e, gargoyleFGoto);
            return;
        }
        double dx = t.BX - e.BX, dy = t.BY - e.BY;
        AiChooseDirection(e, Blood.GetAngle(dx, dy));
        if (t.XHealth == 0 || (t is Player && PowerupCheck(t, K.kPwUpShadowCloak) > 0))
        {
            AiNewState(e, gargoyleFSearch);
            return;
        }
        double nDist = Blood.ApproxDist(dx, dy);
        if (nDist <= Info.Dude(e.Type, K.DI_seeDist))
        {
            double nDeltaAngle = Blood.AngMask(Blood.GetAngle(dx, dy) + 1024 - e.Ang) - 1024;
            double height = Info.Dude(e.Type, K.DI_eyeHeight) * e.YRepeat * 4;
            double height2 = Info.Dude(e.Type, K.DI_eyeHeight) * t.YRepeat * 4;   // the original uses its own dudeinfo here
            var (_, bottom) = Extents(e);
            if (CanSee(t.BX, t.BY, t.BZ, e.BX, e.BY, e.BZ - height, t, e))
            {
                if (nDist < Info.Dude(e.Type, K.DI_seeDist) && Math.Abs(nDeltaAngle) <= Info.Dude(e.Type, K.DI_periphery))
                {
                    AiSetTarget(e, t);
                    double floorZ = FloorZBelow(e);
                    double ad = Math.Abs(nDeltaAngle);
                    if (e.Type == K.kDudeGargoyleFlesh)
                    {
                        if (nDist < 6144 && nDist > 3072 && ad < 85)
                            GargTryAttack(e, gargoyleFThrow, 1408, dx, dy, K.kDudeGargoyleStone);
                        else if (nDist < 1024 && ad < 85)
                            GargTryAttack(e, gargoyleFSlash, 1406, dx, dy, K.kDudeGargoyleStone);
                        else if ((height2 - height > 8192 || floorZ - bottom > 8192) && nDist < 5120 && nDist > 2560)
                        {
                            AiPlay3DSound(e, 1400, AI_SFX_PRIORITY_1, -1);
                            AiNewState(e, gargoyleSwoop);
                        }
                        else if ((height2 - height < 8192 || floorZ - bottom < 8192) && ad < 85)
                            AiPlay3DSound(e, 1400, AI_SFX_PRIORITY_1, -1);
                    }
                    else if (e.Type == K.kDudeGargoyleStone)
                    {
                        if (nDist < 6144 && nDist > 3072 && ad < 85)
                            GargTryAttack(e, gargoyleSBlast, 1457, dx, dy, K.kDudeGargoyleFlesh);
                        else if (nDist < 1024 && ad < 85)
                            GargTryAttack(e, gargoyleFSlash, -1, dx, dy, K.kDudeGargoyleFlesh);
                        else if ((height2 - height > 8192 || floorZ - bottom > 8192) && nDist < 5120 && nDist > 2048)
                        {
                            AiPlay3DSound(e, 1450, AI_SFX_PRIORITY_1, -1);
                            AiNewState(e, gargoyleSwoop);
                        }
                        else if ((height2 - height < 8192 || floorZ - bottom > 8192) && ad < 85)
                            AiPlay3DSound(e, 1450, AI_SFX_PRIORITY_1, -1);
                    }
                }
                return;
            }
            AiNewState(e, gargoyleFly);
            return;
        }
        AiNewState(e, gargoyleFGoto);
        e.XTarget = null;
    }

    void garg_entryFStatue(Sprite e)
    {
        ActHealDude(e, Info.Dude(K.kDudeGargoyleFlesh, K.DI_startHealth), Info.Dude(K.kDudeGargoyleFlesh, K.DI_startHealth));
        e.Type = K.kDudeGargoyleFlesh;
    }

    void garg_entrySStatue(Sprite e)
    {
        ActHealDude(e, Info.Dude(K.kDudeGargoyleStone, K.DI_startHealth), Info.Dude(K.kDudeGargoyleStone, K.DI_startHealth));
        e.Type = K.kDudeGargoyleStone;
    }

    void garg_playStatueBreakSnd(Sprite e) => AiPlay3DSound(e, 313, AI_SFX_PRIORITY_1, -1);

    void garg_MoveForward(Sprite e) => FlyerMoveForward(e, 1024);

    void garg_MoveSlow(Sprite e)
    {
        double nAng = AiTurnToGoal(e);
        double nAccel = Info.Dude(e.Type, K.DI_frontSpeed) * 4;
        if (Math.Abs(nAng) > 341)
        {
            e.GoalAng = (int)Blood.AngMask(e.Ang + 512);
            return;
        }
        double nDist = Blood.ApproxDist(e.TargetX - e.BX, e.TargetY - e.BY);
        if (Chance(1536) && nDist <= 1024)
            return;
        var (_, t2) = VelToLocal(e);
        LocalToVel(e, Blood.Sar(nAccel, 1), Blood.Sar(t2, 1));
        if (e.Type == K.kDudeGargoyleFlesh)
            e.ZVel = 279620;
        else if (e.Type == K.kDudeGargoyleStone)
            e.ZVel = 218453;
    }

    void garg_MoveSwoop(Sprite e)
    {
        double nAng = AiTurnToGoal(e);
        double nAccel = Info.Dude(e.Type, K.DI_frontSpeed) * 4;
        if (Math.Abs(nAng) > 341)
        {
            e.GoalAng = (int)Blood.AngMask(e.Ang + 512);
            return;
        }
        double nDist = Blood.ApproxDist(e.TargetX - e.BX, e.TargetY - e.BY);
        if (Chance(1536) && nDist <= 1024)
            return;
        var (t1, t2) = VelToLocal(e);
        t1 += Blood.Sar(nAccel, 1);
        LocalToVel(e, t1, t2);
        if (e.Type == K.kDudeGargoyleFlesh || e.Type == K.kDudeGargoyleStone)
            e.ZVel = t1;
    }

    void garg_MoveFly(Sprite e)
    {
        double nAng = AiTurnToGoal(e);
        double nAccel = Info.Dude(e.Type, K.DI_frontSpeed) * 4;
        if (Math.Abs(nAng) > 341)
        {
            e.Ang = (int)Blood.AngMask(e.Ang + 512);
            return;
        }
        double nDist = Blood.ApproxDist(e.TargetX - e.BX, e.TargetY - e.BY);
        if (Chance(16384) && nDist <= 1024)
            return;
        var (t1, t2) = VelToLocal(e);
        t1 += Blood.Sar(nAccel, 1);
        LocalToVel(e, t1, t2);
        if (e.Type == K.kDudeGargoyleFlesh || e.Type == K.kDudeGargoyleStone)
            e.ZVel = -t1;
    }
}
