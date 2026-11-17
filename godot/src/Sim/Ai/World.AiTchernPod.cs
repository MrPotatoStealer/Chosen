using System;
using Chosen.Data;

namespace Chosen.Sim;

/// <summary>Tchernobog (aitchern.cpp) and pod/tentacle (aipod.cpp) AI, via Blood3D's server/ai_tchern_pod.qc.</summary>
public sealed partial class World
{
    // ---------------------------------------------------------------- Tchernobog
    void tcher_sub_71A90(Sprite e)
    {
        var t = e.XTarget;
        if (t == null || !t.HasX)
            return;
        if (t.BurnTime == 0)
            EvPostCallback(t, 0, K.kCallbackFXFlameLick);
        ActBurnSprite(e, t, 40);
        if (Chance(24576))
            AiNewState(e, tcherno13A9D4);
    }

    void tcher_sub_71BD4(Sprite e)
    {
        if (e.XTarget == null)
            return;
        var (dx, dy, dz) = CerbAimEx(e, false, 1048576);
        ActFireMissile(e, -350, 0, dx, dy, dz, K.kMissileFireballTchernobog);
        ActFireMissile(e, 350, 0, dx, dy, dz, K.kMissileFireballTchernobog);
    }

    void tcher_sub_720AC(Sprite e)
    {
        if (e.XTarget == null)
            return;
        double ax = Blood.Sar(Blood.Cos(e.Ang), 16), ay = Blood.Sar(Blood.Sin(e.Ang), 16);
        var (dx, dy, dz) = CerbAimEx(e, false, 1048576);
        ActFireMissile(e, 350, 0, dx, dy, -dz, K.kMissileFireballTchernobog);
        ActFireMissile(e, -350, 0, ax, ay, 0, K.kMissileFireballTchernobog);
    }

    void tcher_sub_72580(Sprite e) => AiThinkSearchTarget(e);
    void tcher_sub_725A4(Sprite e) => FlyerThinkTarget(e, tcherno13AA28);
    void tcher_sub_72850(Sprite e) => AiThinkGotoThen(e, 512, tchernobogSearch);

    void tcher_sub_72934(Sprite e)
    {
        var t = e.XTarget;
        if (t == null)
        {
            AiNewState(e, tcherno13A9B8);
            return;
        }
        AiChooseDirection(e, Blood.GetAngle(t.BX - e.BX, t.BY - e.BY));
        if (t.XHealth == 0 || (t is Player && PowerupCheck(t, K.kPwUpShadowCloak) > 0))
        {
            AiNewState(e, tchernobogSearch);
            return;
        }
        if (AiSeesTarget(e, t, out _, out _, out double nDist, out double nDeltaAngle))
        {
            AiSetTarget(e, t);
            double ad = Math.Abs(nDeltaAngle);
            if (nDist < 7936 && nDist > 3328 && ad < 85)
                AiNewState(e, tcherno13AA0C);
            else if (nDist < 3328 && nDist > 2816 && ad < 85)
                AiNewState(e, tcherno13A9D4);
            else if (nDist < 2816 && nDist > 1280 && ad < 85)
                AiNewState(e, tcherno13A9F0);
            return;
        }
        AiNewState(e, tcherno13A9B8);
        e.XTarget = null;
    }

    // ---------------------------------------------------------------- pods and tentacles
    void pod_sub_6FF08(Sprite e) => Sound3D(e, 2503, -1);
    void pod_sub_6FF54(Sprite e) => Sound3D(e, 2500, -1);

    void pod_sub_6FFA0(Sprite e)
    {
        var t = e.XTarget;
        if (t == null)
            return;
        double x = t.BX - e.BX + Random2(1000), y = t.BY - e.BY + Random2(1000), dz = t.BZ - e.BZ;
        double nDist = Blood.ApproxDist(x, y);
        double nDist2 = Blood.Int(nDist / 540);
        Sprite m = null;
        if (e.Type == K.kDudePodGreen)
        {
            dz += 8000;
            if (Info.Dude(e.Type, K.DI_seeDist) * 0.1 < nDist)
            {
                Sound3D(e, Chance(32768) ? 2474 : 2475, -1);
                m = ActFireThing(e, 0, -8000, Blood.Int(dz / 128) - 14500, K.kThingPodGreenBall, Blood.Int(nDist2 * 8388608 / 120));
            }
            if (m != null)
                Seq.Spawn(this, 68, m);
        }
        else if (e.Type == K.kDudePodFire)
        {
            dz += 8000;
            if (Info.Dude(e.Type, K.DI_seeDist) * 0.1 < nDist)
            {
                Sound3D(e, 2454, -1);
                m = ActFireThing(e, 0, -8000, Blood.Int(dz / 128) - 14500, K.kThingPodFireBall, Blood.Int(nDist2 * 8388608 / 120));
            }
            if (m != null)
                Seq.Spawn(this, 22, m);
        }
        for (int i = 0; i < 4; i++)
            FxSpawnPodBlood(e, 240);
    }

    void pod_sub_70284(Sprite e)
    {
        Sound3D(e, 2502, -1);
        bool fire = e.Type == K.kDudeTentacleFire;
        double nBurn = fire ? Blood.Sar(Skill * 120, 2) : 0;
        int dmgType = fire ? K.kDamageExplode : K.kDamageBullet;
        double nDist = fire ? 75 : 50;
        ActRadiusDamage(e, e.BX, e.BY, e.BZ, nDist, 1, 5 * (1 + Skill), dmgType, 2, nBurn);
    }

    void pod_sub_7034C(Sprite e) => AiThinkSearchTarget(e);

    static bool IsPod(Sprite e) => e.Type == K.kDudePodGreen || e.Type == K.kDudePodFire;

    void pod_sub_70380(Sprite e)
    {
        double dx = e.TargetX - e.BX, dy = e.TargetY - e.BY;
        int nAngle = Blood.GetAngle(dx, dy);
        double nDist = Blood.ApproxDist(dx, dy);
        AiChooseDirection(e, nAngle);
        if (nDist < 512 && Math.Abs(e.Ang - nAngle) < Info.Dude(e.Type, K.DI_periphery))
        {
            if (IsPod(e))
                AiNewState(e, podSearch);
            else if (e.Type == K.kDudeTentacleGreen || e.Type == K.kDudeTentacleFire)
                AiNewState(e, tentacleSearch);
        }
        aiThinkTarget(e);
    }

    void pod_sub_704D8(Sprite e)
    {
        var t = e.XTarget;
        if (t == null)
        {
            AiNewState(e, IsPod(e) ? pod13A600 : tentacle13A718);
            return;
        }
        AiChooseDirection(e, Blood.GetAngle(t.BX - e.BX, t.BY - e.BY));
        if (t.XHealth == 0)
        {
            AiNewState(e, IsPod(e) ? podSearch : tentacleSearch);
            return;
        }
        if (AiSeesTarget(e, t, out _, out _, out _, out double nDeltaAngle))
        {
            AiSetTarget(e, t);
            if (Math.Abs(nDeltaAngle) < 85 && t.Type != K.kDudePodGreen && t.Type != K.kDudePodFire)
                AiNewState(e, IsPod(e) ? pod13A638 : tentacle13A750);
            return;
        }
        AiNewState(e, IsPod(e) ? pod13A600 : tentacle13A718);
        e.XTarget = null;
    }
}
