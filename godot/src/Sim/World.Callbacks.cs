using System;
using Chosen.Data;
using Godot;

namespace Chosen.Sim;

/// <summary>Event callbacks (callback.cpp, via Blood3D's server/callback.qc).</summary>
public sealed partial class World
{
    void FxFlameLick(Sprite e)
    {
        var (top, bottom) = Extents(e);
        for (int i = 0; i < 3; i++)
        {
            double nDist = Blood.Sar(e.XRepeat * Blood.Int(Tiles.W(e.Picnum) / 2.0), 3);
            int nAngle = Random(2048);
            double dx = Blood.MulScale30(nDist, Blood.Cos(nAngle)), dy = Blood.MulScale30(nDist, Blood.Sin(nAngle));
            double z = bottom - Random((int)(bottom - top));
            var fx = FxSpawn(K.FX_32, e.BX + dx, e.BY + dy, z, 0);
            if (fx != null)
            {
                fx.XVel = e.XVel + Random2((int)-dx);
                fx.YVel = e.YVel + Random2((int)-dy);
                fx.ZVel = e.ZVel - Random(109226);
            }
        }
        if (e.BurnTime > 0)
            EvPostCallback(e, 5, K.kCallbackFXFlameLick);
    }

    void CbRemove(Sprite e)
    {
        EvKill(e);
        Seq.Kill(e);
        Kill3DSound(e);
        if (e.StatNum < K.kStatFree)
            Delete(e);
    }

    void FlareBurst(Sprite e)
    {
        int nAngle = Blood.GetAngle(e.XVel, e.YVel);
        const double nRadius = 349525;
        for (int i = 0; i < 8; i++)
        {
            var s = ActSpawnSpriteFrom(e, K.kStatProjectile);
            s.ClassName = "missile";
            s.Picnum = 2424;
            s.Shade = -128;
            s.XRepeat = 32;
            s.YRepeat = 32;
            s.Type = s.InitType = K.kMissileFlareAlt;
            s.ClipDist = 2;
            s.Owner = e.Owner;
            double nAngle2 = Blood.Int(i * 2048.0 / 8);
            double dy = Blood.MulScale30R(nRadius, Blood.Sin(nAngle2));
            double dz = Blood.MulScale30R(nRadius, -Blood.Cos(nAngle2));
            if ((i & 1) != 0)
            {
                dy = Blood.Sar(dy, 1);
                dz = Blood.Sar(dz, 1);
            }
            var r = Blood.RotateVector(0, dy, nAngle);
            s.XVel += r.x;
            s.YVel += r.y;
            s.ZVel += dz;
            UpdateBox(s);
            if (s.Body != null)
                s.Body.Enabled = false;
            EvPostCallback(s, 960, K.kCallbackRemove);
        }
        EvPostCallback(e, 0, K.kCallbackRemove);
    }

    void SparkFollow(Sprite e, int fxid, int rx, int ry, int rz, int zsign, int next, int cb)
    {
        var fx = FxSpawn(fxid, e.BX, e.BY, e.BZ, 0);
        if (fx != null)
        {
            fx.XVel = e.XVel + Random2(rx);
            fx.YVel = e.YVel + Random2(ry);
            fx.ZVel = zsign < 0 ? e.ZVel - Random(rz) : e.ZVel + Random2(rz);
        }
        if (next >= 0)
            EvPostCallback(e, next, cb);
    }

    void FxZombieBloodSpurt(Sprite e)
    {
        var (top, _) = Extents(e);
        var fx = FxSpawn(K.FX_27, e.BX, e.BY, top, 0);
        if (fx != null)
        {
            fx.XVel = e.XVel + Random2(69905);
            fx.YVel = e.YVel + Random2(69905);
            fx.ZVel = e.ZVel - 436906;
        }
        if (e.Data1 > 0)
        {
            EvPostCallback(e, 4, K.kCallbackFXZombieSpurt);
            e.Data1 -= GameClock.TicsPerFrame;
        }
        else if (e.Data2 > 0)
        {
            EvPostCallback(e, 60, K.kCallbackFXZombieSpurt);
            e.Data1 = 40;
            e.Data2--;
        }
    }

    void FxBloodSpurt(Sprite e)
    {
        var fx = FxSpawn(K.FX_27, e.BX, e.BY, e.BZ, 0);
        if (fx != null)
        {
            fx.Ang = 0;
            fx.XVel = Blood.Sar(e.XVel, 8);
            fx.YVel = Blood.Sar(e.YVel, 8);
            fx.ZVel = Blood.Sar(e.ZVel, 8);
        }
        EvPostCallback(e, 6, K.kCallbackFXBloodSpurt);
    }

    void FxDynPuff(Sprite e)
    {
        if (e.ZVel != 0)
        {
            double nDist = Blood.Sar(e.XRepeat * Blood.Int(Tiles.W(e.Picnum) / 2.0), 2);
            double x = e.BX + Blood.MulScale30(nDist, Blood.Cos(e.Ang - 512));
            double y = e.BY + Blood.MulScale30(nDist, Blood.Sin(e.Ang - 512));
            var fx = FxSpawn(K.FX_7, x, y, e.BZ, 0);
            if (fx != null)
            {
                fx.XVel = e.XVel;
                fx.YVel = e.YVel;
                fx.ZVel = e.ZVel;
            }
        }
        EvPostCallback(e, 12, K.kCallbackFXDynPuff);
    }

    void Respawn(Sprite e)
    {
        if (e.StatNum != K.kStatRespawn && e.StatNum != K.kStatThing)
            return;
        if ((e.HitTag & K.kHitagRespawn) == 0)
            return;
        if (e.RespawnPending == 1)
        {
            e.RespawnPending = 2;
            EvPostCallback(e, (int)Blood.MulScale16(ActGetRespawnTime(e), 16384), K.kCallbackRespawn);
        }
        else if (e.RespawnPending == 2)
        {
            e.RespawnPending = 3;
            EvPostCallback(e, (int)Blood.MulScale16(ActGetRespawnTime(e), 8192), K.kCallbackRespawn);
        }
        else if (e.RespawnPending == 3)
        {
            e.StatNum = e.RespawnStat;
            e.Type = e.InitType;
            e.HitTag &= ~K.kHitagRespawn;
            e.XVel = e.YVel = e.ZVel = 0;
            e.RespawnPending = 0;
            e.BurnTime = 0;
            e.IsTriggered = 0;
            if (Info.IsDude(e.Type))
            {
                KillMgrRemoveKill(e);
                e.Origin = e.BaseOrigin;
                e.Cstat |= 4353;
                if (IsModern)
                {
                    // the dude's own health; one on patrol starts its patrol anew; a custom dude sets itself up
                    e.XHealth = NnExtDudeStartHealth(e, e.SysData2);
                    if (e.DudeFlag4 != 0)
                    {
                        e.Data3 = 0;
                        e.XTarget = null;
                    }
                }
                else
                    e.XHealth = Info.Dude(e.Type, K.DI_startHealth) * 16;
                if (!(IsModern && e.Type == KM.kDudeModernCustom))
                {
                    e.ClipDist = Info.Dude(e.Type, K.DI_clipdist);
                    if (SeqData.Exists(Info.Dude(e.Type, K.DI_seqStartID)))
                        Seq.Spawn(this, Info.Dude(e.Type, K.DI_seqStartID), e);
                }
                AiInitSprite(e);
                e.XKey = 0;
            }
            else if (e.Type == K.kThingTNTBarrel)
            {
                e.Cstat |= Cstat.Block | Cstat.Hitscan;
                e.Cstat &= ~Cstat.Invisible;
            }
            e.Visible = (e.Cstat & Cstat.Invisible) == 0;
            UpdateBox(e);
            FxSpawn(K.FX_29, e.BX, e.BY, e.BZ, 0);
            Sound3D(e, 350, -1);
        }
    }

    void SpawnBubbles(Sprite e, double count)
    {
        var (top, bottom) = Extents(e);
        for (int i = 0; i < count; i++)
        {
            double nDist = Blood.Sar(e.XRepeat * Blood.Int(Tiles.W(e.Picnum) / 2.0), 2);
            int nAngle = Random(2048);
            double x = e.BX + Blood.MulScale30(nDist, Blood.Cos(nAngle));
            double y = e.BY + Blood.MulScale30(nDist, Blood.Sin(nAngle));
            double z = bottom - Random((int)(bottom - top));
            var fx = FxSpawn(K.FX_23 + Random(3), x, y, z, 0);
            if (fx != null)
            {
                fx.XVel = e.XVel + Random2(109226);
                fx.YVel = e.YVel + Random2(109226);
                fx.ZVel = e.ZVel + Random2(109226);
            }
        }
    }

    void PlayerBubble(Sprite e)
    {
        if (e is not Player p || p.BubbleTime == 0)
            return;
        SpawnBubbles(e, Blood.Sar(p.BubbleTime, 6));
        EvPostCallback(e, 4, K.kCallbackPlayerBubble);
    }

    void EnemyBubble(Sprite e)
    {
        SpawnBubbles(e, Blood.Sar(Math.Abs(e.ZVel), 18));
        EvPostCallback(e, 4, K.kCallbackEnemeyBubble);
    }

    /// <summary>CounterCheck: the sector counts the sprites of type data inside it.</summary>
    void CounterCheck(Sector sec)
    {
        if (sec.Type != K.kSectorCounter)
            return;
        int nReq = sec.WaitTimeA, nType = sec.SData, nCount = 0;
        if (nType == 0 || nReq == 0)
            return;
        foreach (var e in Sprites)
            if (!e.Removed && e.Type == nType && SpriteSector(e) == sec)
                nCount++;
        if (nCount < nReq)
        {
            EvPostCallback(sec, 5, K.kCallbackCounterCheck);
            return;
        }
        TrTriggerSector(sec, K.kCmdOn, null);
        sec.XLocked = 1;
    }

    void FinishHim(Sprite e)
    {
        if (e is Player p && PlayerSeqPlaying(p, 16) && p.XTarget == Me)
            SoundLocal(Me, 3313);
    }

    void FxBloodBits(Sprite e)
    {
        var zr = GetZRange(e, e.ClipDist);
        var (_, bottom) = Extents(e);
        e.SetBZ(e.BZ + zr.FloorZ - bottom);
        int nAngle = Random(2048);
        double nDist = Random(16) * 16;
        double x = e.BX + Math.Floor(nDist * Blood.Cos(nAngle) / 268435456);
        double y = e.BY + Math.Floor(nDist * Blood.Sin(nAngle) / 268435456);
        FxSpawn(K.FX_48, x, y, e.BZ, 0);
        if (e.Ang == 1024)
            Sound3D(e, 385, 28 + (e.SprIndex & 2), 1 | 4);
        if (Chance(20480))
        {
            var fx = FxSpawn(K.FX_36, x, y, zr.FloorZ - 64, 0);
            if (fx != null)
                fx.Ang = nAngle;
        }
        FxFree(e);
    }

    void SleeveStopBouncing(Sprite e)
    {
        e.XVel = e.YVel = e.ZVel = 0;
        Seq.Kill(e);
        Kill3DSound(e);
        if (e.Type >= K.FX_37 && e.Type <= K.FX_39)
            e.Picnum = 2465;
        else if (e.Type >= K.FX_40 && e.Type <= K.FX_42)
            e.Picnum = 2464;
        e.Type = K.FX_51;
        e.XRepeat = 10;
        e.YRepeat = 10;
    }

    void FxBouncingSleeve(Sprite e)
    {
        var zr = GetZRange(e, e.ClipDist);
        var (_, bottom) = Extents(e);
        e.SetBZ(e.BZ + zr.FloorZ - bottom);
        double zv = e.ZVel - VelOfMoverZ(zr.FloorEnt);
        if (e.ZVel == 0)
            SleeveStopBouncing(e);
        else if (zv > 0)
        {
            double xv = e.XVel, yv = e.YVel;
            Physics.FloorBounceVector(ref xv, ref yv, ref zv, 36864, FloorNormal);
            e.XVel = xv;
            e.YVel = yv;
            e.ZVel = zv;
            if (VelOfMoverZ(zr.FloorEnt) == 0 && Math.Abs(e.ZVel) < 131072)
            {
                SleeveStopBouncing(e);
                return;
            }
            int nChannel = 28 + (e.SprIndex & 2);
            if (e.Type >= K.FX_37 && e.Type <= K.FX_39)
            {
                Random(3);
                Sound3D(e, 608 + Random(2), nChannel, 1);
            }
            else
                Sound3D(e, Random(2) != 0 ? 612 : 610, nChannel, 1);
        }
    }

    void ReturnFlagToBase(Sprite e)
    {
        if (e.Owner != null && e.Owner.HasX)
        {
            TrTriggerSprite(e.Owner, K.kCmdOn, e.Owner);
            if (e.Type == K.kItemFlagA)
                ShowMessage("Blue Flag returned to base.");
            else if (e.Type == K.kItemFlagB)
                ShowMessage("Red Flag returned to base.");
        }
        EvPostCallback(e, 0, K.kCallbackRemove);
    }

    void FxPodBloodSpray(Sprite e)
    {
        var fx = FxSpawn(e.Type == 53 ? K.FX_53 : K.FX_54, e.BX, e.BY, e.BZ, 0);
        if (fx != null)
        {
            fx.Ang = 0;
            fx.XVel = Blood.Sar(e.XVel, 8);
            fx.YVel = Blood.Sar(e.YVel, 8);
            fx.ZVel = Blood.Sar(e.ZVel, 8);
        }
        EvPostCallback(e, 6, K.kCallbackFXPodBloodSpray);
    }

    void FxPodGreenBloodSpray(Sprite e)
    {
        var fx = FxSpawn(K.FX_53, e.BX, e.BY, e.BZ, 0);
        if (fx != null)
        {
            fx.Ang = 0;
            fx.XVel = Blood.Sar(e.XVel, 8);
            fx.YVel = Blood.Sar(e.YVel, 8);
            fx.ZVel = Blood.Sar(e.ZVel, 8);
        }
        EvPostCallback(e, 6, KM.kCallbackFxPodGreenBloodSpray);
    }

    void FxPodBloodSplat(Sprite e)
    {
        var zr = GetZRange(e, e.ClipDist);
        var (_, bottom) = Extents(e);
        e.SetBZ(e.BZ + zr.FloorZ - bottom);
        int nAngle = Random(2048);
        double nDist = Random(16) * 16;
        double x = e.BX + Math.Floor(nDist * Blood.Cos(nAngle) / 268435456);
        double y = e.BY + Math.Floor(nDist * Blood.Sin(nAngle) / 268435456);
        if (e.Ang == 1024 && e.Type == 53)
            Sound3D(e, 385, 28 + (e.SprIndex & 2), 1);
        Sprite fx = null;
        if (e.Type == 53 || e.Type == K.kThingPodGreenBall)
        {
            if (Chance(1280) || e.Type == K.kThingPodGreenBall)
                fx = FxSpawn(K.FX_55, x, y, zr.FloorZ - 64, 0);
        }
        else
            fx = FxSpawn(K.FX_32, x, y, zr.FloorZ - 64, 0);
        if (fx != null)
            fx.Ang = nAngle;
        FxFree(e);
    }

    void LeechStateTimer(Sprite e)
    {
        if (e.StatNum == K.kStatThing && (e.HitTag & 32) == 0 && e.Type == K.kThingDroppedLifeLeech)
            e.StateTimer = 0;
    }

    /// <summary>evCallback: a callback event fired.</summary>
    void EvCallback(int funcId, XObject target)
    {
        if (target is Sector sec)
        {
            if (funcId == K.kCallbackCounterCheck)
                CounterCheck(sec);
            return;
        }
        if (target is not Sprite e || e.Removed)
            return;
        switch (funcId)
        {
            case K.kCallbackFXFlameLick: FxFlameLick(e); break;
            case K.kCallbackRemove: CbRemove(e); break;
            case K.kCallbackFXFlareBurst: FlareBurst(e); break;
            case K.kCallbackFXFlareSpark: SparkFollow(e, K.FX_28, 109226, 109226, 109226, -1, 4, K.kCallbackFXFlareSpark); break;
            case K.kCallbackFXFlareSparkLite: SparkFollow(e, K.FX_28, 109226, 109226, 109226, -1, 12, K.kCallbackFXFlareSparkLite); break;
            case K.kCallbackFXZombieSpurt: FxZombieBloodSpurt(e); break;
            case K.kCallbackFXBloodSpurt: FxBloodSpurt(e); break;
            case K.kCallbackFXArcSpark: SparkFollow(e, K.FX_15, 65536, 65536, 109226, -1, 3, K.kCallbackFXArcSpark); break;
            case K.kCallbackFXDynPuff: FxDynPuff(e); break;
            case K.kCallbackRespawn: Respawn(e); break;
            case K.kCallbackPlayerBubble: PlayerBubble(e); break;
            case K.kCallbackEnemeyBubble: EnemyBubble(e); break;
            case K.kCallbackFinishHim: FinishHim(e); break;
            case K.kCallbackFXBloodBits: FxBloodBits(e); break;
            case K.kCallbackFXTeslaAlt: SparkFollow(e, K.FX_49, 109226, 109226, 109226, -1, 3, K.kCallbackFXTeslaAlt); break;
            case K.kCallbackFXBouncingSleeve: FxBouncingSleeve(e); break;
            case K.kCallbackReturnFlag: ReturnFlagToBase(e); break;
            case K.kCallbackFXPodBloodSpray: FxPodBloodSpray(e); break;
            case K.kCallbackFXPodBloodSplat: FxPodBloodSplat(e); break;
            case K.kCallbackLeechStateTimer: LeechStateTimer(e); break;
            case KM.kCallbackMissileBurst: UniMissileBurst(e); break;
            case KM.kCallbackMissileSpriteBlock: MakeMissileBlocking(e); break;
            case KM.kCallbackFxPodGreenBloodSpray: FxPodGreenBloodSpray(e); break;
        }
    }
}
