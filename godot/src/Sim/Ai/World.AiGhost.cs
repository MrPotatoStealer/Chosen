using System;
using Chosen.Data;

namespace Chosen.Sim;

/// <summary>Phantasm AI (aighost.cpp, via Blood3D's server/ai_ghost.qc); its movement mirrors the gargoyle's.</summary>
public sealed partial class World
{
    void ghost_SlashSeqCallback(Sprite e)
    {
        var t = e.XTarget;
        if (t == null)
            return;
        double height = e.YRepeat * Info.Dude(e.Type, K.DI_eyeHeight) * 4;
        double height2 = t.YRepeat * Info.Dude(t.Type, K.DI_eyeHeight) * 4;
        double dz = height - height2;
        double dx = Blood.Sar(Blood.Cos(e.Ang), 16), dy = Blood.Sar(Blood.Sin(e.Ang), 16);
        Sound3D(e, 1406);
        ActFireVector(e, 0, 0, dx, dy, dz, K.kVectorGhost);
        int r1 = Random(50), r2 = Random(50);
        ActFireVector(e, 0, 0, dx + r2, dy - r1, dz, K.kVectorGhost);
        r1 = Random(50);
        r2 = Random(50);
        ActFireVector(e, 0, 0, dx - r2, dy + r1, dz, K.kVectorGhost);
    }

    void ghost_ThrowSeqCallback(Sprite e) => ActFireThing(e, 0, 0, e.DudeSlope - 7500, K.kThingBone, 978670);

    void ghost_BlastSeqCallback(Sprite e)
    {
        WRand();
        var t = e.XTarget;
        double height = e.YRepeat * Info.Dude(e.Type, K.DI_eyeHeight) * 4;
        var (dx, dy, dz) = AiTrackAim(e, e.BX, e.BY, height, 256, 1747626);
        if (t is Player || IsModern)      // (a modern map: at other dudes too)
        {
            Sound3D(e, 489);
            ActFireMissile(e, 0, 0, dx, dy, dz, K.kMissileEctoSkull);
        }
    }

    void ghost_thinkTarget(Sprite e) => FlyerThinkTarget(e, ghostTurn);
    void ghost_thinkSearch(Sprite e) => AiThinkSearchTarget(e);
    void ghost_thinkGoto(Sprite e) => AiThinkGotoThen(e, 512, ghostSearch);
    void ghost_MoveDodgeUp(Sprite e) => FlyerDodge(e, -120149, false);
    void ghost_MoveDodgeDown(Sprite e) => FlyerDodge(e, 279620, true);

    void ghost_thinkChase(Sprite e)
    {
        var t = e.XTarget;
        if (t == null)
        {
            AiNewState(e, ghostGoto);
            return;
        }
        double dx = t.BX - e.BX, dy = t.BY - e.BY;
        AiChooseDirection(e, Blood.GetAngle(dx, dy));
        if (t.XHealth == 0 || (t is Player && PowerupCheck(t, K.kPwUpShadowCloak) > 0))
        {
            AiNewState(e, ghostSearch);
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
                    if (nDist < 8192 && nDist > 4096 && ad < 85)
                        GargTryAttack(e, ghostBlast, -1, dx, dy, K.kDudePhantasm);
                    else if (nDist < 1024 && ad < 85)
                        GargTryAttack(e, ghostSlash, -1, dx, dy, K.kDudePhantasm);
                    else if ((height2 - height > 8192 || floorZ - bottom > 8192) && nDist < 5120 && nDist > 2048)
                    {
                        AiPlay3DSound(e, 1600, AI_SFX_PRIORITY_1, -1);
                        AiNewState(e, ghostSwoop);
                    }
                    else if ((height2 - height < 8192 || floorZ - bottom < 8192) && ad < 85)
                        AiPlay3DSound(e, 1600, AI_SFX_PRIORITY_1, -1);
                }
                return;
            }
            AiNewState(e, ghostFly);
            return;
        }
        AiNewState(e, ghostGoto);
        e.XTarget = null;
    }

    void ghost_MoveForward(Sprite e) => garg_MoveForward(e);

    void ghost_MoveSlow(Sprite e)
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
        if (e.Type == K.kDudePhantasm)
            e.ZVel = 279620;
    }

    void ghost_MoveSwoop(Sprite e)
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
        if (e.Type == K.kDudePhantasm)
            e.ZVel = t1;
    }

    void ghost_MoveFly(Sprite e)
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
        if (e.Type == K.kDudePhantasm)
            e.ZVel = -t1;
    }
}
