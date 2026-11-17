using System;
using System.Collections.Generic;
using Chosen.Data;
using Chosen.Shared.Blood.Map;
using Godot;

namespace Chosen.Sim;

/// <summary>
/// The modern sprite types (NBlood's nnexts.cpp): how each answers a command (modernTypeOperateSprite, and the sectors'
/// and walls'), what each does to the objects it sends kCmdModernUse to (modernTypeTrigger and its use... functions:
/// the changers of properties, pictures, sizes, data, slopes, velocity and sector lighting, the damager, the effect,
/// sequence, wind, sound, missile, drip and item generators, the teleport target, the dude spawners, the gib objects),
/// and the random and sequential TX switches.
/// </summary>
public sealed partial class World
{
    // ---------------------------------------------------------------- sending (modernTypeSendCommand)

    /// <summary>modernTypeSetSpriteState: SetSpriteState, the command sent the modern way.</summary>
    bool ModernTypeSetSpriteState(Sprite e, int nState, XObject causer)
    {
        if ((e.XBusy & 0xffff) == 0 && e.XState == nState)
            return false;
        e.XBusy = nState << 16;
        e.XState = nState;
        EvKillBy(e, causer);
        if (e.RestState != nState && e.WaitTime > 0)
            EvPost(e, e.WaitTime * 120 / 10, e.RestState != 0 ? K.kCmdOn : K.kCmdOff, causer);
        if (e.TxID != 0 && ((e.TriggerOn != 0 && e.XState != 0) || (e.TriggerOff != 0 && e.XState == 0)))
            ModernTypeSendCommand(e, e.TxID, e.XCommand, causer);
        return true;
    }

    /// <summary>modernTypeSendCommand: kCmdModernUse (the receivers take the sender's settings) with the command.</summary>
    void ModernTypeSendCommand(Sprite e, int destChannel, int command, XObject causer)
    {
        switch (command)
        {
            case K.kCmdLink:
                EvSend(e, destChannel, KM.kCmdModernUse, causer);
                return;
            case K.kCmdUnlock:
                EvSend(e, destChannel, command, causer);
                EvSend(e, destChannel, KM.kCmdModernUse, causer);
                return;
            default:
                EvSend(e, destChannel, KM.kCmdModernUse, causer);
                EvSend(e, destChannel, command, causer);
                return;
        }
    }

    /// <summary>modernTypeTrigger: kCmdModernUse arriving at an object: the sending sprite does to it what its type does.</summary>
    void ModernTypeTrigger(XObject dest, XObject source, XObject causer)
    {
        if (source is not Sprite src || src.Removed || !src.HasX || dest == null || !dest.HasX)
            return;
        if (dest is Sprite ds)
        {
            if ((ds.HitTag & K.kHitagFree) != 0)
                return;
            // a random or sequential TX switch that links passes the event on to its channels
            if ((ds.Type == KM.kModernRandomTX || ds.Type == KM.kModernSequentialTX) && ds.XCommand == K.kCmdLink && ds.XLocked == 0)
            {
                if (ds.Type == KM.kModernRandomTX)
                    UseRandomTx(ds, src.XCommand, false, causer);
                else
                {
                    if ((ds.HitTag & KM.Flag1) != 0)
                    {
                        SeqTxSendCmdAll(ds, src, src.XCommand, true, causer);
                        return;
                    }
                    UseSequentialTx(ds, src.XCommand, false, causer);
                }
                if (ds.TxID <= 0 || ds.TxID >= K.kChannelUserMax)
                    return;
                ModernTypeSendCommand(src, ds.TxID, src.XCommand, causer);
                return;
            }
        }
        var dspr = dest as Sprite;
        switch (src.Type)
        {
            case K.kGenDripBlood:
            case K.kGenDripWater:
                if (dspr != null)
                    UseDripGenerator(src, dspr);
                break;
            case K.kThingObjectExplode:
            case K.kThingObjectGib:
                if (dspr != null)
                    UseGibObject(src, dspr);
                break;
            case K.kMarkerWarpDest:
                if (dspr != null)
                    UseTeleportTarget(src, dspr);
                break;
            case KM.kModernSlopeChanger:
                if (dest is Sprite or Sector)
                    UseSlopeChanger(src, dest);
                break;
            case KM.kModernSpriteDamager:
                if (dest is Sprite or Sector)
                    UseSpriteDamager(src, dest, false);
                break;
            case KM.kModernEffectSpawner:
                if (dspr != null)
                    UseEffectGen(src, dspr);
                break;
            case KM.kModernSeqSpawner:
                UseSeqSpawnerGen(src, dest);
                break;
            case KM.kModernWindGenerator:
                if (dest is Sector ws && src.Data2 >= 0)
                    UseSectorWindGen(src, ws);
                break;
            case KM.kModernObjSizeChanger:
                UseObjResizer(src, dest);
                break;
            case KM.kModernObjDataAccumulator:
                UseIncDecGen(src, dest);
                break;
            case KM.kModernObjDataChanger:
                UseDataChanger(src, dest);
                break;
            case KM.kModernSectorFXChanger:
                if (dest is Sector fs)
                    UseSectorLightChanger(src, fs);
                break;
            case KM.kModernDudeTargetChanger:
                if (dspr != null)
                    UseTargetChanger(src, dspr);
                break;
            case KM.kModernObjPicnumChanger:
                UsePictureChanger(src, dest);
                break;
            case KM.kModernObjPropertiesChanger:
                UsePropertiesChanger(src, dest, false);
                break;
            case KM.kModernVelocityChanger:
                if (dest is Sprite or Sector)
                    UseVelocityChanger(src, causer, dest);
                break;
            case KM.kGenModernSound:
                if (dspr != null)
                    UseSoundGen(src, dspr);
                break;
            case KM.kGenModernMissileUniversal:
                if (dspr != null)
                    UseUniMissileGen(src, dspr);
                break;
            case K.kMarkerDudeSpawn:
                if (dspr != null)
                    UseDudeSpawn(src, dspr);
                break;
            case KM.kModernCustomDudeSpawn:
                if (dspr != null)
                    UseCustomDudeSpawn(src, dspr);
                break;
        }
    }

    // ---------------------------------------------------------------- operate: sectors and walls

    static bool SectorIsPaused(Sector s) => s.MotionPaused;

    void SectorKillSounds(Sector s)
    {
        foreach (var e in SpritesInB(s.B))
            if (e.Type == K.kSoundSector)
                Kill3DSound(e);
    }

    /// <summary>sectorPauseMotion: the sector stops where it is.</summary>
    void SectorPauseMotion(Sector s, XObject causer)
    {
        s.MotionPaused = true;
        EvKillBy(s, causer);
        SectorKillSounds(s);
        if ((s.XBusy == 0 && s.XState == 0) || (s.XBusy == 65536 && s.XState != 0))
            SectorEndSound(s, s.XState);
        // (NBlood's busy loop drops a paused sector's entry: status 3)
        s.BusyActive = false;
    }

    /// <summary>sectorContinueMotion: a paused sector goes on, the way the command says.</summary>
    void SectorContinueMotion(Sector s, int cmd, XObject causer)
    {
        s.MotionPaused = false;
        int busyTimeA = s.BusyTimeA, waitTimeA = s.WaitTimeA, busyTimeB = s.BusyTimeB, waitTimeB = s.WaitTimeB;
        if (s.Type == K.kSectorPath)
        {
            if (s.SMarker0 is not Sprite m)
                return;
            busyTimeA = busyTimeB = m.BusyTime;
            waitTimeA = waitTimeB = m.WaitTime;
        }
        if (s.Interruptable == 0 && cmd != KM.kCmdSectorMotionContinue
            && ((s.XState == 0 && s.XBusy != 0) || (s.XState != 0 && s.XBusy != 65536)))
            cmd = KM.kCmdSectorMotionContinue;
        else if (cmd == K.kCmdToggle)
            cmd = s.XState != 0 ? K.kCmdOn : K.kCmdOff;
        int nDelta = 1;
        switch (cmd)
        {
            case K.kCmdOff:
                if (s.XBusy == 0)
                {
                    if (s.ReTriggerB != 0 && waitTimeB != 0)
                        EvPost(s, waitTimeB * 120 / 10, K.kCmdOff, causer);
                    return;
                }
                s.XState = 1;
                nDelta = 65536 / (int)Blood.ClipLow(busyTimeB * 120 / 10, 1);
                break;
            case K.kCmdOn:
                if (s.XBusy == 65536)
                {
                    if (s.ReTriggerA != 0 && waitTimeA != 0)
                        EvPost(s, waitTimeA * 120 / 10, K.kCmdOn, causer);
                    return;
                }
                s.XState = 0;
                nDelta = 65536 / (int)Blood.ClipLow(busyTimeA * 120 / 10, 1);
                break;
            case KM.kCmdSectorMotionContinue:
                nDelta = 65536 / (int)Blood.ClipLow((s.XState != 0 ? busyTimeB : busyTimeA) * 120 / 10, 1);
                break;
        }
        int busyFunc;
        switch (s.Type)
        {
            case K.kSectorZMotion: busyFunc = K.BUSYID_2; break;
            case K.kSectorZMotionSprite: busyFunc = K.BUSYID_1; break;
            case K.kSectorSlideMarked:
            case K.kSectorSlide: busyFunc = K.BUSYID_3; break;
            case K.kSectorRotateMarked:
            case K.kSectorRotate: busyFunc = K.BUSYID_4; break;
            case K.kSectorRotateStep: busyFunc = K.BUSYID_5; break;
            case K.kSectorPath: busyFunc = K.BUSYID_7; break;
            default: return;
        }
        SectorStartSound(s, s.XState);
        s.BusyActive = true;
        s.BusyProc = busyFunc;
        s.BusyVal = s.XBusy;
        s.BusyDelta = s.XState != 0 ? -nDelta : nDelta;
    }

    /// <summary>modernTypeOperateSector: locks (a counter sector checks again), a paused sector's motion. True: done.</summary>
    bool ModernTypeOperateSector(Sector s, int cmd, XObject causer)
    {
        if (cmd >= K.kCmdLock && cmd <= K.kCmdToggleLock)
        {
            s.XLocked = cmd == K.kCmdLock ? 1 : cmd == K.kCmdUnlock ? 0 : s.XLocked ^ 1;
            if (s.Type == K.kSectorCounter && s.XLocked == 1)
            {
                SetSectorState(s, 0, causer);
                EvPostCallback(s, 0, K.kCallbackCounterCheck);
            }
            return true;
        }
        if (s.MotionPaused)
        {
            if (cmd == K.kCmdOff || cmd == K.kCmdOn || cmd == K.kCmdToggle || cmd == KM.kCmdSectorMotionContinue)
            {
                SectorContinueMotion(s, cmd, causer);
                return true;
            }
        }
        else if (cmd == KM.kCmdSectorMotionPause)
        {
            SectorPauseMotion(s, causer);
            return true;
        }
        if (s.Type == KM.kModernSectorPathSprite)
        {
            ModernMissing("path sprite sectors (type 611)");
            return true;
        }
        return false;
    }

    /// <summary>modernTypeOperateWall: a one-way switch on a wall.</summary>
    bool ModernTypeOperateWall(Wall w, int cmd, XObject causer)
    {
        if (w.Type != K.kSwitchOneWay)
            return false;
        SetWallState(w, cmd == K.kCmdOff ? 0 : cmd == K.kCmdOn ? 1 : w.RestState ^ 1, causer);
        return true;
    }

    // ---------------------------------------------------------------- operate: sprites

    // the generators' shared answer: off, on (then as a repeat), repeat (do it and come again), else toggle
    bool GenOperate(Sprite e, int cmd, XObject causer, Action act, Func<int> repeatDelay, bool killOnOn = true, Action onOff = null)
    {
        switch (cmd)
        {
            case K.kCmdOff:
                SetSpriteState(e, 0, causer);
                onOff?.Invoke();
                break;
            case K.kCmdOn:
            case K.kCmdRepeat:
                if (cmd == K.kCmdOn)
                {
                    if (killOnOn)
                        EvKillBy(e, causer);
                    SetSpriteState(e, 1, causer);
                }
                else if (e.XState == 0)
                    break;
                act();
                if (e.BusyTime > 0)
                    EvPost(e, repeatDelay(), K.kCmdRepeat, causer);
                break;
            default:
                EvPost(e, 0, e.XState ^ 1, causer);
                break;
        }
        return true;
    }

    /// <summary>modernTypeOperateSprite: a command arriving at a sprite of a modern map. True: done here.</summary>
    bool ModernTypeOperateSprite(Sprite e, int cmd, XObject causer, XObject source)
    {
        if (cmd >= K.kCmdLock && cmd <= K.kCmdToggleLock)
        {
            e.XLocked = cmd == K.kCmdLock ? 1 : cmd == K.kCmdUnlock ? 0 : e.XLocked ^ 1;
            switch (e.Type)
            {
                case KM.kModernCondition:
                case KM.kModernConditionFalse:
                    CondSetIsLocked(e, e.XLocked);
                    break;
                case KM.kModernEffectSpawner:
                    if (e.XLocked != 0)
                        KillEffectGenCallbacks(e);
                    break;
            }
            return true;
        }
        if (cmd == KM.kCmdDudeFlagsSet)
        {
            if (source is not Sprite fs)
                return true;
            if (fs.HasX)
                AiPatrolFlagsMgr(fs, e, true, false);     // the source's dude flags copied
        }
        if (e.StatNum == K.kStatDude && IsDudeSprite(e))
        {
            switch (cmd)
            {
                case K.kCmdOff:
                    if (e.XState != 0)
                        SetSpriteState(e, 0, causer);
                    break;
                case K.kCmdOn:
                    if (e.XState == 0)
                        SetSpriteState(e, 1, causer);
                    if (!IsDudeSprite(e) || e is Player || e.XHealth <= 0)
                        break;
                    if (AiInPatrolState(e.AiState))
                        break;
                    if (e.AiState != null && (e.AiState.Type == AiState.Idle || e.AiState.Type == AiState.GenIdle))
                        AiActivateDude(e);
                    break;
                case KM.kCmdDudeFlagsSet:
                    if (DudeIsAlive(e) && source is Sprite ps)
                        AiPatrolFlagsMgr(ps, e, false, true);       // the dude starts over with the flags it has now
                    break;
                default:
                    EvPost(e, 0, e.XState == 0 ? K.kCmdOn : K.kCmdOff, causer);
                    break;
            }
            return true;
        }
        switch (e.Type)
        {
            default:
                return false;
            case K.kThingBloodBits:
            case K.kThingBloodChunks:
                // a dude that became a thing still gets its commands: only those that gib it
                if (!Info.IsDude(e.InitType))
                    return false;
                if (cmd != K.kCmdToggle && cmd != K.kCmdOff && cmd != K.kCmdSpriteImpact)
                    return true;
                DudeToGibCallback1(e);
                return false;
            case K.kThingObjectGib:
            case K.kThingObjectExplode:
                if (cmd == K.kCmdOff || cmd == K.kCmdOn)
                {
                    if (!SetSpriteState(e, cmd, causer))
                        return true;
                }
                else if (!SetSpriteState(e, e.XState ^ 1, causer))
                    return true;
                if ((e.SysData1 & KM.Flag128) == 0)
                    UseGibObject(e, e);
                else if (e.TxID != 0)
                    ModernTypeSetSpriteState(e, e.XState ^ 1, causer);
                return true;
            case KM.kModernLaserGen:
                ModernMissing("laser generators (type 505)");
                return true;
            case KM.kModernCondition:
            case KM.kModernConditionFalse:
                if (e.IsTriggered == 0)
                    UseCondition(e, cmd, causer, source);
                return true;
            case K.kMarkerDudeSpawn:
                if (!Monsters)
                    return true;
                if ((e.HitTag & KM.Flag4) == 0)
                    UseDudeSpawn(e, e);
                else if (e.TxID != 0)
                    EvSend(e, e.TxID, KM.kCmdModernUse, causer);
                return true;
            case KM.kModernCustomDudeSpawn:
                if (!Monsters)
                    return true;
                if ((e.HitTag & KM.Flag4) == 0)
                    UseCustomDudeSpawn(e, e);
                else if (e.TxID != 0)
                    EvSend(e, e.TxID, KM.kCmdModernUse, causer);
                return true;
            case KM.kModernRandomTX:
            case KM.kModernSequentialTX:
                if (e.XCommand == K.kCmdLink)
                    return true;        // an event redirector
                if (e.Type == KM.kModernRandomTX)
                    UseRandomTx(e, e.XCommand, true, causer);
                else if ((e.HitTag & KM.Flag1) == 0)
                    UseSequentialTx(e, e.XCommand, true, causer);
                else
                    SeqTxSendCmdAll(e, e, e.XCommand, false, causer);
                return true;
            case KM.kModernSpriteDamager:
                return GenOperate(e, cmd, causer, () =>
                {
                    if (e.TxID > 0)
                        ModernTypeSendCommand(e, e.TxID, e.XCommand, causer);
                    else if (e.Data1 == 0 && SpriteBSector(e) >= 0)
                        UseSpriteDamager(e, EnsureXSector(SpriteBSector(e)), false);
                    else if (e.Data1 >= 666 && e.Data1 < 669)
                        UseSpriteDamager(e, null, true);
                    else if (GetPlayerById(e.Data1) is { } p)
                        UseSpriteDamager(e, p, false);
                }, () => e.BusyTime, false);
            case K.kMarkerWarpDest:
                if (e.TxID <= 0)
                {
                    if (GetPlayerById(e.Data1) is { } p && SetSpriteState(e, e.XState ^ 1, causer))
                        UseTeleportTarget(e, p);
                    return true;
                }
                ModernTypeSetSpriteState(e, e.XState ^ 1, causer);
                return true;
            case KM.kModernObjPropertiesChanger:
                if (e.TxID <= 0)
                {
                    if (SetSpriteState(e, e.XState ^ 1, causer))
                        UsePropertiesChanger(e, null, true);
                    return true;
                }
                ModernTypeSetSpriteState(e, e.XState ^ 1, causer);
                return true;
            case KM.kModernSectorFXChanger:
                if (e.TxID <= 0)
                {
                    if (SetSpriteState(e, e.XState ^ 1, causer))
                        UseSectorLightChanger(e, null);
                    return true;
                }
                ModernTypeSetSpriteState(e, e.XState ^ 1, causer);
                return true;
            case KM.kModernSlopeChanger:
            case KM.kModernObjSizeChanger:
            case KM.kModernObjPicnumChanger:
            case KM.kModernObjDataChanger:
                ModernTypeSetSpriteState(e, e.XState ^ 1, causer);
                return true;
            case KM.kModernSeqSpawner:
            case KM.kModernEffectSpawner:
                switch (cmd)
                {
                    case K.kCmdOff:
                        if (e.XState == 1)
                        {
                            SetSpriteState(e, 0, causer);
                            if (e.Type == KM.kModernEffectSpawner)
                                KillEffectGenCallbacks(e);
                        }
                        break;
                    case K.kCmdOn:
                    case K.kCmdRepeat:
                        if (cmd == K.kCmdOn)
                        {
                            EvKillBy(e, causer);
                            SetSpriteState(e, 1, causer);
                            if (e.Type == KM.kModernSeqSpawner && e.TxID >= K.kChannelUser && (e.HitTag & KM.Flag16) == 0)
                                SeqSpawnerOffSameTx(e);
                        }
                        else if (e.XState == 0)
                            break;
                        if (e.TxID > 0)
                            ModernTypeSendCommand(e, e.TxID, e.XCommand, causer);
                        else if (e.Type == KM.kModernSeqSpawner)
                            UseSeqSpawnerGen(e, e);
                        else
                            UseEffectGen(e, null);
                        if (e.BusyTime > 0)
                            EvPost(e, (int)Blood.ClipLow((e.BusyTime + Random2(e.Data1)) * 120 / 10, 0), K.kCmdRepeat, causer);
                        break;
                    default:
                        EvPost(e, 0, e.XState ^ 1, causer);
                        break;
                }
                return true;
            case KM.kModernWindGenerator:
                return GenOperate(e, cmd, causer, () =>
                {
                    if (e.TxID > 0)
                        ModernTypeSendCommand(e, e.TxID, e.XCommand, causer);
                    else
                        UseSectorWindGen(e, null);
                }, () => e.BusyTime, true, () => WindGenStopWindOnSectors(e));
            case KM.kModernVelocityChanger:
                return GenOperate(e, cmd, causer, () =>
                {
                    if (e.TxID > 0)
                        ModernTypeSendCommand(e, e.TxID, e.XCommand, causer);
                    else if (SpriteBSector(e) >= 0)
                        UseVelocityChangerSector(e, causer, SpriteBSector(e));
                }, () => e.BusyTime);
            case KM.kModernDudeTargetChanger:
                // (its data4 changed while it works: the dudes it holds idle wake)
                if (e.DropMsg == 3 && e.Data4 != 3)
                    AiFightActivateDudes(e.TxID);
                switch (cmd)
                {
                    case K.kCmdOff:
                        if (e.Data4 == 3)
                            AiFightActivateDudes(e.TxID);
                        SetSpriteState(e, 0, causer);
                        break;
                    case K.kCmdOn:
                    case K.kCmdRepeat:
                        if (cmd == K.kCmdOn)
                        {
                            EvKill(e);
                            SetSpriteState(e, 1, causer);
                        }
                        else if (e.XState == 0)
                            break;
                        if (e.TxID <= 0 || !AiFightGetDudesForBattle(e))
                        {
                            AiFightFreeAllTargets(e);
                            EvPost(e, 0, K.kCmdOff, causer);
                            break;
                        }
                        ModernTypeSendCommand(e, e.TxID, e.XCommand, causer);
                        if (e.BusyTime != 0)
                            EvPost(e, e.BusyTime, K.kCmdRepeat, causer);
                        break;
                    default:
                        EvPost(e, 0, e.XState ^ 1, causer);
                        break;
                }
                e.DropMsg = e.Data4;
                return true;
            case K.kGenDripWater:
            case K.kGenDripBlood:
                return GenOperate(e, cmd, causer, () =>
                {
                    if (e.TxID != 0)
                        ModernTypeSendCommand(e, e.TxID, e.XCommand, causer);
                    else
                        UseDripGenerator(e, e);
                }, () => 120 * (Random2(e.Data1) + e.BusyTime) / 10);
            case K.kGenMissileFireball:
                // a repeat of a generator switched off is dropped; the rest is Blood's own
                return cmd == K.kCmdRepeat && e.XState == 0;
            case K.kGenTrigger:
                if ((e.HitTag & KM.Flag1) == 0)
                    return cmd == K.kCmdRepeat && e.XState == 0;
                // a fast generator
                return GenOperate(e, cmd, causer, () =>
                {
                    if (e.TxID != 0)
                        EvSend(e, e.TxID, e.XCommand, causer);
                }, () => e.BusyTime);
            case KM.kModernObjDataAccumulator:
                switch (cmd)
                {
                    case K.kCmdOff:
                        SetSpriteState(e, 0, causer);
                        break;
                    case K.kCmdOn:
                    case K.kCmdRepeat:
                        if (cmd == K.kCmdOn)
                        {
                            EvKillBy(e, causer);
                            SetSpriteState(e, 1, causer);
                        }
                        else if (e.XState == 0)
                            break;
                        if (e.HitTag == 0 && IncDecGoalValueIsReached(e))
                        {
                            // off once every TX object has the goal value
                            EvPost(e, 0, K.kCmdOff, causer);
                            break;
                        }
                        ModernTypeSendCommand(e, e.TxID, e.XCommand, causer);
                        if (e.BusyTime != 0)
                            EvPost(e, e.BusyTime, K.kCmdRepeat, causer);
                        break;
                    default:
                        EvPost(e, 0, e.XState ^ 1, causer);
                        break;
                }
                return true;
            case KM.kModernRandom:
            case KM.kModernRandom2:
                return GenOperate(e, cmd, causer, () => UseRandomItemGen(e), () => 120 * e.BusyTime / 10);
            case KM.kModernThingTNTProx:
                if (e.StatNum != K.kStatRespawn)
                {
                    switch (cmd)
                    {
                        case K.kCmdSpriteProximity:
                            if (e.XState != 0)
                                break;
                            Sound3D(e, 452);
                            EvPost(e, 30, K.kCmdOff, causer);
                            e.XState = 1;
                            Sound3D(e, 451);
                            e.XProximity = 1;
                            break;
                        case K.kCmdOn:
                            Sound3D(e, 451);
                            e.XProximity = 1;
                            break;
                        default:
                            ActExplodeSprite(e);
                            break;
                    }
                }
                return true;
            case KM.kModernThingEnemyLifeLeech:
                CdudeLeechOperate(e);
                return true;
            case KM.kModernPlayerControl:
                OperatePlayerControl(e, cmd, causer);
                return true;
            case KM.kGenModernSound:
                return GenOperate(e, cmd, causer, () =>
                {
                    if (e.TxID != 0)
                        ModernTypeSendCommand(e, e.TxID, e.XCommand, causer);
                    else
                        UseSoundGen(e, e);
                }, () => 120 * e.BusyTime / 10);
            case KM.kGenModernMissileUniversal:
                return GenOperate(e, cmd, causer, () =>
                {
                    if (e.TxID != 0)
                        ModernTypeSendCommand(e, e.TxID, e.XCommand, causer);
                    else
                        UseUniMissileGen(e, e);
                }, () => 120 * e.BusyTime / 10);
        }
    }

    // ---------------------------------------------------------------- random and sequential TX

    static bool TxIsRanged(Sprite src)
    {
        if (src.Data1 > 0 && src.Data2 <= 0 && src.Data3 <= 0 && src.Data4 > 0)
        {
            if (src.Data1 > src.Data4)
                (src.Data1, src.Data4) = (src.Data4, src.Data1);
            return true;
        }
        return false;
    }

    void SeqTxSendCmdAll(Sprite src, Sprite sender, int cmd, bool modernSend, XObject causer)
    {
        if (TxIsRanged(src))
        {
            for (src.TxID = src.Data1; src.TxID <= src.Data4; src.TxID++)
            {
                if (src.TxID <= 0 || src.TxID >= K.kChannelUserMax)
                    continue;
                if (!modernSend)
                    EvSend(sender, src.TxID, cmd, causer);
                else
                    ModernTypeSendCommand(sender, src.TxID, cmd, causer);
            }
        }
        else
        {
            for (int i = 0; i <= 3; i++)
            {
                src.TxID = GetDataVal(src, i);
                if (src.TxID <= 0 || src.TxID >= K.kChannelUserMax)
                    continue;
                if (!modernSend)
                    EvSend(sender, src.TxID, cmd, causer);
                else
                    ModernTypeSendCommand(sender, src.TxID, cmd, causer);
            }
        }
        src.TxID = src.SysData1 = 0;
    }

    /// <summary>randomGetDataValue: one of the data fields at random, when at least two hold a value of the kind.</summary>
    int RandomGetDataValue(Sprite e, int randType)
    {
        int[] rData = { e.Data1, e.Data2, e.Data3, e.Data4 };
        int bad = 0;
        for (int i = 0; i < 4; i++)
        {
            bool ok = randType switch
            {
                KM.kRandomizeItem => rData[i] >= K.kItemWeaponBase && rData[i] < K.kItemMax,
                KM.kRandomizeDude => rData[i] >= K.kDudeBase && rData[i] < K.kDudeMax,
                KM.kRandomizeTX => rData[i] > K.kChannelZero && rData[i] < K.kChannelUserMax,
                _ => false,
            };
            if (!ok)
                bad++;
        }
        if (bad < 3)
        {
            for (int retries = KM.kMaxRandomizeRetries; retries > 0; retries--)
            {
                int r = NnExtRandom(0, 3);
                if (rData[r] > 0)
                    return rData[r];
            }
        }
        return -1;
    }

    void UseRandomTx(Sprite src, int cmd, bool setState, XObject causer)
    {
        int tx = 0, maxRetries = KM.kMaxRandomizeRetries;
        if (TxIsRanged(src))
        {
            while (maxRetries-- >= 0)
                if ((tx = NnExtRandom(src.Data1, src.Data4)) != src.TxID)
                    break;
        }
        else
        {
            while (maxRetries-- >= 0)
                if ((tx = RandomGetDataValue(src, KM.kRandomizeTX)) > 0 && tx != src.TxID)
                    break;
        }
        src.TxID = tx > 0 && tx < K.kChannelUserMax ? tx : 0;
        SetRxTxChanged(src);
        if (setState)
            SetSpriteState(src, src.XState ^ 1, causer);
    }

    void UseSequentialTx(Sprite src, int cmd, bool setState, XObject causer)
    {
        bool range = TxIsRanged(src);
        int cnt = 3, tx = 0;
        if (range)
        {
            // sysData1: the TX ID it is at
            if (src.SysData1 < src.Data1) src.SysData1 = src.Data1;
            else if (src.SysData1 > src.Data4) src.SysData1 = src.Data4;
        }
        else
        {
            // sysData1: the data field it is at
            if (src.SysData1 > 3) src.SysData1 = 0;
            else if (src.SysData1 < 0) src.SysData1 = 3;
        }
        if (cmd == K.kCmdOff)
        {
            if (!range)
            {
                while (cnt-- >= 0)
                {
                    if (src.SysData1-- < 0)
                        src.SysData1 = 3;
                    if ((tx = GetDataVal(src, src.SysData1)) <= 0)
                        continue;
                    break;
                }
            }
            else
            {
                if (--src.SysData1 < src.Data1)
                    src.SysData1 = src.Data4;
                tx = src.SysData1;
            }
        }
        else
        {
            if (!range)
            {
                while (cnt-- >= 0)
                {
                    if (src.SysData1 > 3)
                        src.SysData1 = 0;
                    if ((tx = GetDataVal(src, src.SysData1++)) <= 0)
                        continue;
                    break;
                }
            }
            else
            {
                tx = src.SysData1;
                if (src.SysData1 >= src.Data4)
                    src.SysData1 = src.Data1;
                else
                    src.SysData1++;
            }
        }
        src.TxID = tx > 0 && tx < K.kChannelUserMax ? tx : 0;
        SetRxTxChanged(src);
        if (setState)
            SetSpriteState(src, src.XState ^ 1, causer);
    }

    // (a switch's TX ID changes as it works; nothing indexes objects by TX)
    static void SetRxTxChanged(Sprite e) { }

    // an event redirector: a random or sequential TX switch with the link command, unlocked
    static Sprite EvrIsRedirector(XObject o) =>
        o is Sprite e && !e.Removed && (e.Type == KM.kModernRandomTX || e.Type == KM.kModernSequentialTX) && e.HasX
        && e.XCommand == K.kCmdLink && e.XLocked == 0 ? e : null;

    /// <summary>The channels an object's TX reaches through the event redirectors receiving on it (evrListRedirectors:
    /// each redirector's every TX ID).</summary>
    List<int> RedirectedChannels(XObject o)
    {
        var list = new List<int>();
        if (!_eventRedirectsUsed || o == null || o.TxID <= 0)
            return list;
        foreach (var r in Receivers(o.TxID))
        {
            if (EvrIsRedirector(r) is not { } red)
                continue;
            if (TxIsRanged(red))
            {
                for (int tx = red.Data1; tx <= red.Data4; tx++)
                    list.Add(tx);
            }
            else
            {
                for (int i = 0; i <= 3; i++)
                    if (GetDataVal(red, i) > 0)
                        list.Add(GetDataVal(red, i));
            }
        }
        return list;
    }

    // ---------------------------------------------------------------- data fields

    const int NoData = -65535;

    /// <summary>getDataFieldOfObject.</summary>
    static int GetDataFieldOfObject(XObject o, int dataIndex)
    {
        switch (o)
        {
            case Sprite e:
                return dataIndex switch
                {
                    1 => e.Data1,
                    2 => e.Data2,
                    3 => e.Type == KM.kDudeModernCustom ? e.SysData1 : e.Data3,
                    4 => e.Data4,
                    _ => NoData,
                };
            case Sector s: return s.SData;
            case Wall w: return w.Data1;
        }
        return NoData;
    }

    /// <summary>setDataValueOfObject.</summary>
    bool SetDataValueOfObject(XObject o, int dataIndex, int value)
    {
        switch (o)
        {
            case Sprite e:
            {
                Modern.CustomDude dude = null;
                if (IsDudeSprite(e))
                {
                    if (e.XHealth <= 0)
                        return true;
                    if (IsCustomDude(e))
                        dude = e.Custom;
                }
                else if (e.Type == K.kThingBloodBits || e.Type == K.kThingBloodChunks || e.Type == K.kThingZombieHead)
                    return true;
                switch (dataIndex)
                {
                    case 1:
                        e.Data1 = value;
                        if (e.Type == K.kSwitchCombo)
                        {
                            SetSpriteState(e, value == e.Data2 ? 1 : 0, null);
                            return true;
                        }
                        if (dude != null)
                            Modern.CustomDudeSetup.Resetup(this, dude);
                        return true;
                    case 2:
                        e.Data2 = value;
                        if (dude != null && dude.Version == 1)
                            Modern.CustomDudeSetup.Resetup(this, dude);
                        return true;
                    case 3:
                        e.Data3 = value;
                        if (dude != null && dude.Version == 1)
                        {
                            e.SysData1 = value;
                            Modern.CustomDudeSetup.Resetup(this, dude);
                        }
                        return true;
                    case 4:
                        e.Data4 = value;
                        return true;
                }
                return false;
            }
            case Sector s:
                s.SData = value;
                return true;
            case Wall w:
                w.Data1 = value;
                return true;
        }
        return false;
    }

    // the data fields a number names by its digits (data1 of an accumulator: 13 = fields 1 and 3)
    static IEnumerable<int> DataDigits(int v)
    {
        foreach (char c in Math.Abs(v).ToString(System.Globalization.CultureInfo.InvariantCulture))
            yield return c - '0';
    }

    /// <summary>incDecGoalValueIsReached: has every TX object's field the goal value?</summary>
    bool IncDecGoalValueIsReached(Sprite e)
    {
        if (e.Data3 != e.SysData1)
            return false;
        foreach (var o in Receivers(e.TxID))
        {
            if (EvrIsRedirector(o) != null)
                continue;
            foreach (int d in DataDigits(e.Data1))
                if (GetDataFieldOfObject(o, d) != e.Data3)
                    return false;
        }
        foreach (int rx in RedirectedChannels(e))
            foreach (var o in Receivers(rx))
                foreach (int d in DataDigits(e.Data1))
                    if (GetDataFieldOfObject(o, d) != e.Data3)
                        return false;
        return true;
    }

    /// <summary>useIncDecGen: the accumulator steps the fields of an object towards its goal.</summary>
    void UseIncDecGen(Sprite src, XObject obj)
    {
        foreach (int dataIndex in DataDigits(src.Data1))
        {
            int data = GetDataFieldOfObject(obj, dataIndex);
            if (data == NoData)
                continue;
            if (src.Data2 < src.Data3)
            {
                data = (int)Blood.ClipRange(data, src.Data2, src.Data3);
                if ((data += src.Data4) >= src.Data3)
                {
                    switch (src.HitTag)
                    {
                        case 0:
                        case KM.Flag1:
                            if (data > src.Data3) data = src.Data3;
                            break;
                        case KM.Flag2:
                            if (data > src.Data3) data = src.Data3;
                            if (!IncDecGoalValueIsReached(src)) break;
                            (src.Data3, src.Data2) = (src.Data2, src.Data3);
                            break;
                        case KM.Flag3:
                            if (data > src.Data3) data = src.Data2;
                            break;
                    }
                }
            }
            else if (src.Data2 > src.Data3)
            {
                data = (int)Blood.ClipRange(data, src.Data3, src.Data2);
                if ((data -= src.Data4) <= src.Data3)
                {
                    switch (src.HitTag)
                    {
                        case 0:
                        case KM.Flag1:
                            if (data < src.Data3) data = src.Data3;
                            break;
                        case KM.Flag2:
                            if (data < src.Data3) data = src.Data3;
                            if (!IncDecGoalValueIsReached(src)) break;
                            (src.Data3, src.Data2) = (src.Data2, src.Data3);
                            break;
                        case KM.Flag3:
                            if (data < src.Data3) data = src.Data2;
                            break;
                    }
                }
            }
            src.SysData1 = data;
            SetDataValueOfObject(obj, dataIndex, data);
        }
    }

    void UseDataChanger(Sprite src, XObject obj)
    {
        bool flag1 = (src.HitTag & KM.Flag1) != 0;
        switch (obj)
        {
            case Sector:
            case Wall:
                if (flag1 || ValueIsBetween(src.Data1, -1, 32767)) SetDataValueOfObject(obj, 1, src.Data1);
                break;
            case Sprite:
                if (flag1 || ValueIsBetween(src.Data1, -1, 32767)) SetDataValueOfObject(obj, 1, src.Data1);
                if (flag1 || ValueIsBetween(src.Data2, -1, 32767)) SetDataValueOfObject(obj, 2, src.Data2);
                if (flag1 || ValueIsBetween(src.Data3, -1, 32767)) SetDataValueOfObject(obj, 3, src.Data3);
                if (flag1 || ValueIsBetween(src.Data4, -1, 65535)) SetDataValueOfObject(obj, 4, src.Data4);
                break;
        }
    }

    // ---------------------------------------------------------------- pictures, sizes, properties

    /// <summary>What the renderer draws of a Build sector changed (a picture, palette, shade, flags, repeats or
    /// panning of its planes or walls).</summary>
    void LookChanged(int b)
    {
        if (b >= 0 && b < Board.SectorCount)
            Board.TouchLook(b);
    }

    void WallLookChanged(int w)
    {
        LookChanged(Board.Walls[w].Sector);
        if (Board.Walls[w].NextSector >= 0)
            LookChanged(Board.Walls[w].NextSector);
    }

    void SpriteLookChanged(Sprite e)
    {
        e.Visible = (e.Cstat & Cstat.Invisible) == 0;
        UpdateBox(e);
    }

    void UsePictureChanger(Sprite src, XObject obj)
    {
        switch (obj)
        {
            case Sector s:
            {
                var sec = Board.Sectors[s.B];
                if (ValueIsBetween(src.Data1, -1, 32767)) sec.FloorPicnum = src.Data1;
                if (ValueIsBetween(src.Data2, -1, 32767)) sec.CeilingPicnum = src.Data2;
                if (ValueIsBetween(src.Data3, -1, 32767)) sec.FloorPal = src.Data3 & 255;
                if (ValueIsBetween(src.Data4, -1, 65535)) sec.CeilingPal = src.Data4 & 255;
                LookChanged(s.B);
                break;
            }
            case Sprite e:
                if (ValueIsBetween(src.Data1, -1, 32767)) e.Picnum = src.Data1;
                if (src.Data2 >= 0) e.Shade = src.Data2 > 127 ? 127 : src.Data2;
                else if (src.Data2 < -1) e.Shade = src.Data2 < -127 ? -127 : src.Data2;
                if (ValueIsBetween(src.Data3, -1, 32767)) e.Pal = src.Data3 & 255;
                SpriteLookChanged(e);
                break;
            case Wall w:
            {
                var bw = Board.Walls[w.BWall];
                if (ValueIsBetween(src.Data1, -1, 32767)) bw.Picnum = src.Data1;
                if (ValueIsBetween(src.Data2, -1, 32767)) bw.OverPicnum = src.Data2;
                if (ValueIsBetween(src.Data3, -1, 32767)) bw.Pal = src.Data3 & 255;
                WallLookChanged(w.BWall);
                break;
            }
        }
    }

    void UseObjResizer(Sprite src, XObject obj)
    {
        switch (obj)
        {
            case Sector s:
            {
                var sec = Board.Sectors[s.B];
                if (ValueIsBetween(src.Data1, -1, 32767)) sec.FloorXPanning = (int)Blood.ClipRange(src.Data1, 0, 255);
                if (ValueIsBetween(src.Data2, -1, 32767)) sec.FloorYPanning = (int)Blood.ClipRange(src.Data2, 0, 255);
                if (ValueIsBetween(src.Data3, -1, 32767)) sec.CeilingXPanning = (int)Blood.ClipRange(src.Data3, 0, 255);
                if (ValueIsBetween(src.Data4, -1, 65535)) sec.CeilingYPanning = (int)Blood.ClipRange(src.Data4, 0, 255);
                LookChanged(s.B);
                break;
            }
            case Sprite e:
                if ((src.HitTag & KM.Flag1) != 0)
                {
                    // by the sequences' scale
                    if (ValueIsBetween(src.Data1, -255, 32767))
                    {
                        int mulDiv = ValueIsBetween(src.Data2, 0, 257) ? src.Data2 : 256;
                        if (src.Data1 > 0) e.XScale = mulDiv * (int)Blood.ClipHigh(src.Data1, 25);
                        else if (src.Data1 < 0) e.XScale = mulDiv / (int)Blood.ClipHigh(Math.Abs(src.Data1), 25);
                        else e.XScale = 0;
                    }
                }
                else
                {
                    if (ValueIsBetween(src.Data1, -1, 32767)) e.XRepeat = (int)Blood.ClipRange(src.Data1, 0, 255);
                    if (ValueIsBetween(src.Data2, -1, 32767)) e.YRepeat = (int)Blood.ClipRange(src.Data2, 0, 255);
                }
                if (ValueIsBetween(src.Data3, -1, 32767)) e.XOffset = (sbyte)(int)Blood.ClipRange(src.Data3, 0, 255);
                if (ValueIsBetween(src.Data4, -1, 65535)) e.YOffset = (sbyte)(int)Blood.ClipRange(src.Data4, 0, 255);
                SpriteLookChanged(e);
                break;
            case Wall w:
            {
                var bw = Board.Walls[w.BWall];
                if (ValueIsBetween(src.Data1, -1, 32767)) bw.XRepeat = (int)Blood.ClipRange(src.Data1, 0, 255);
                if (ValueIsBetween(src.Data2, -1, 32767)) bw.YRepeat = (int)Blood.ClipRange(src.Data2, 0, 255);
                if (ValueIsBetween(src.Data3, -1, 32767)) bw.XPanning = (int)Blood.ClipRange(src.Data3, 0, 255);
                if (ValueIsBetween(src.Data4, -1, 65535)) bw.YPanning = (int)Blood.ClipRange(src.Data4, 0, 255);
                WallLookChanged(w.BWall);
                break;
            }
        }
    }

    /// <summary>The level's visibility a properties changer without a TX ID set (gVisibility; -1: the map's own).</summary>
    public int GlobalVisibility = -1;

    /// <summary>usePropertiesChanger: a wall's flags, a sprite's flags and debris physics, a sector's water, depth,
    /// visibility and plane flags; without an object (global) the level's visibility.</summary>
    void UsePropertiesChanger(Sprite src, XObject obj, bool global)
    {
        bool flag1 = (src.HitTag & KM.Flag1) != 0, flag2 = (src.HitTag & KM.Flag2) != 0, flag4 = (src.HitTag & KM.Flag4) != 0;
        int data3 = (short)src.Data3, data4 = (short)src.Data4;
        if (global)
        {
            if (ValueIsBetween(src.Data2, -1, 32767))
                GlobalVisibility = (int)Blood.ClipRange(src.Data2, 0, 4096);
            return;
        }
        switch (obj)
        {
            case Wall w:
            {
                var bw = Board.Walls[w.BWall];
                if (ValueIsBetween(src.Data1, -1, 32767))
                {
                    if (src.Data1 == 1)
                        ModernMissing("a properties changer making a wall its sector's first (data1 = 1)");
                    else if (src.Data1 == 2)
                        ModernMissing("a properties changer setting a sector's slope alignment wall (data1 = 2)");
                }
                if (ValueIsBetween(data3, -1, 32767))
                {
                    if (flag1)
                    {
                        if (flag4) bw.Hitag &= ~data3;
                        else bw.Hitag |= data3;
                    }
                    else
                        bw.Hitag = data3;
                }
                if (ValueIsBetween(src.Data4, -1, 65535))
                {
                    int old = bw.Cstat;
                    if (flag1)
                    {
                        if (flag4) bw.Cstat &= ~data4 & 0xffff;
                        else bw.Cstat |= data4 & 0xffff;
                    }
                    else
                    {
                        bw.Cstat = data4 & 0xffff;
                        if (!flag2)
                        {
                            // what must stay as it was
                            if ((old & 0x2) != 0) bw.Cstat |= 0x2;
                            if ((old & 0x4) != 0) bw.Cstat |= 0x4;
                            if ((old & 0x20) != 0) bw.Cstat |= 0x20;
                            if ((old & 0x4000) != 0) bw.Cstat |= 0x4000;
                            else if ((old & 0x8000) != 0) bw.Cstat |= 0x8000;
                        }
                    }
                    if (Map != null && Map.HasClipData)
                        Map.SetWallCstat(w.BWall, bw.Cstat);
                    WallLookChanged(w.BWall);
                }
                break;
            }
            case Sprite e:
            {
                bool thing2debris = false;
                if (ValueIsBetween(data3, -1, 32767))
                {
                    int old = e.HitTag;
                    if (flag1)
                    {
                        if (flag4) e.HitTag &= ~data3;
                        else e.HitTag |= data3;
                    }
                    else
                        e.HitTag = data3;
                    // the system's own flags stay
                    if ((old & K.kHitagFree) != 0) e.HitTag |= K.kHitagFree;
                    if ((old & K.kHitagRespawn) != 0) e.HitTag |= K.kHitagRespawn;
                    thing2debris = e.StatNum == K.kStatThing;
                }
                if ((ValueIsBetween(src.Data2, -1, 32767) || thing2debris)
                    && e.StatNum != K.kStatDude && e.StatNum != K.kStatFree && e.StatNum != K.kStatMarker && e.StatNum != K.kStatPathMarker)
                    SetDebrisPhysics(e, src, thing2debris);
                if (ValueIsBetween(src.Data4, -1, 65535))
                {
                    int old = e.Cstat;
                    if (flag1)
                    {
                        if (flag4) e.Cstat &= ~data4 & 0xffff;
                        else e.Cstat |= data4 & 0xffff;
                    }
                    else
                    {
                        e.Cstat = data4 & 0xffff;
                        if (!flag2)
                        {
                            if ((old & 0x1000) != 0) e.Cstat |= 0x1000;
                            if ((old & 0x80) != 0) e.Cstat |= 0x80;
                            if ((old & 0x2000) != 0) e.Cstat |= 0x2000;
                            else if ((old & 0x4000) != 0) e.Cstat |= 0x4000;
                        }
                    }
                    SpriteLookChanged(e);
                }
                break;
            }
            case Sector s:
            {
                var sec = Board.Sectors[s.B];
                if (src.Data1 >= 0 && src.Data1 < 2)
                    SetSectorUnderwater(s, src.Data1 != 0);
                else if (src.Data1 > 9)
                    s.SDepth = 7;
                else if (src.Data1 > 1)
                    s.SDepth = src.Data1 - 2;
                if (ValueIsBetween(src.Data2, -1, 32767))
                    sec.Visibility = (int)Blood.ClipRange(src.Data2, 0, 234);
                if (ValueIsBetween(data3, -1, 32767))
                {
                    if (flag1)
                    {
                        if (flag4) sec.CeilingStat &= ~data3;
                        else sec.CeilingStat |= data3;
                    }
                    else
                        sec.CeilingStat = data3;
                }
                if (ValueIsBetween(src.Data4, -1, 65535))
                {
                    if (flag1)
                    {
                        if (flag4) sec.FloorStat &= ~data4 & 0xffff;
                        else sec.FloorStat |= data4 & 0xffff;
                    }
                    else
                        sec.FloorStat = data4 & 0xffff;
                }
                LookChanged(s.B);
                Board.PlaneVersion[s.B]++;
                break;
            }
        }
    }

    /// <summary>A sector made underwater or dry: its links' kinds with it, and the dudes in it.</summary>
    void SetSectorUnderwater(Sector s, bool underwater)
    {
        s.SUnderwater = underwater ? 1 : 0;
        Board.Sectors[s.B].Xs.Underwater = s.SUnderwater;
        var lower = LowerLinkOf(s.B);
        Sprite upper = null;
        if (lower != null)
        {
            // the upper link of the same id
            for (int i = 0; i < Board.SectorCount; i++)
                if (UpperLinkOf(i) is { } u && u.Data1 == lower.Data1)
                {
                    upper = u;
                    break;
                }
        }
        bool rorCeil = Board.Sectors[s.B].CeilingPicnum == 504, rorFloor = Board.Sectors[s.B].FloorPicnum == 504;
        if (lower != null)
        {
            if (underwater)
            {
                if (lower.Type == K.kMarkerLowStack || lower.Type == K.kMarkerLowLink)
                {
                    lower.SysData1 = lower.Type;
                    lower.Type = K.kMarkerLowWater;
                }
                else
                    lower.SysData1 = rorCeil ? K.kMarkerLowStack : K.kMarkerLowLink;
            }
            else if (lower.SysData1 > 0) lower.Type = lower.SysData1;
            else if (!rorCeil) lower.Type = K.kMarkerLowLink;
            else lower.Type = K.kMarkerLowStack;
        }
        if (upper != null)
        {
            if (underwater)
            {
                if (upper.Type == K.kMarkerUpStack || upper.Type == K.kMarkerUpLink)
                {
                    upper.SysData1 = upper.Type;
                    upper.Type = K.kMarkerUpWater;
                }
                else if (lower != null)
                    lower.SysData1 = rorFloor ? K.kMarkerUpStack : K.kMarkerUpLink;
            }
            else if (upper.SysData1 > 0) upper.Type = upper.SysData1;
            else if (!rorFloor) upper.Type = K.kMarkerUpLink;
            else upper.Type = K.kMarkerUpStack;
        }
        foreach (var e in SpritesInB(s.B))
        {
            if (e.StatNum != K.kStatDude || !IsDudeSprite(e) || !e.HasX)
                continue;
            var p = e as Player;
            if (underwater)
            {
                if (lower != null)
                    e.Medium = lower.Type == K.kMarkerUpGoo ? K.kMediumGoo : K.kMediumWater;
                if (p != null)
                {
                    p.Posture = Player.PostureSwim;
                    p.BurnTime = 0;
                    if (lower == null)
                        p.Medium = K.kMediumWater;
                }
            }
            else
            {
                e.Medium = K.kMediumNormal;
                if (p != null)
                    p.Posture = p.InputCrouch ? Player.PostureCrouch : Player.PostureStand;
            }
        }
    }

    // ---------------------------------------------------------------- sector lighting, wind

    /// <summary>useSectorLigthChanger: a sector's light effect set (wave, amplitude, frequency, phase; what it shades).</summary>
    void UseSectorLightChanger(Sprite src, Sector s)
    {
        s ??= EnsureXSector(SpriteBSector(src));
        if (s == null)
            return;
        bool relative = (src.HitTag & KM.Flag16) != 0;
        if (ValueIsBetween(src.Data1, -1, 32767))
            s.FxWave = (int)Blood.ClipHigh(relative ? s.FxWave + src.Data1 : src.Data1, 11);
        if (ValueIsBetween(src.Data2, -128, 128))
            s.FxAmp = relative ? (int)Blood.ClipRange(s.FxAmp + src.Data2, -127, 127) : src.Data2;
        if (ValueIsBetween(src.Data3, -1, 32767))
            s.FxFreq = (int)Blood.ClipHigh(relative ? s.FxFreq + src.Data3 : src.Data3, 255);
        if (ValueIsBetween(src.Data4, -1, 65535))
            s.FxPhase = (int)Blood.ClipHigh(relative ? s.FxPhase + src.Data4 : src.Data4, 255);
        if (src.HitTag != 0)
        {
            if (src.HitTag != KM.Flag1)
            {
                s.FxAlways = (src.HitTag & KM.Flag1) != 0 ? 1 : 0;
                s.FxFloor = (src.HitTag & KM.Flag2) != 0 ? 1 : 0;
                s.FxCeil = (src.HitTag & KM.Flag4) != 0 ? 1 : 0;
                s.FxShadeWalls = (src.HitTag & KM.Flag8) != 0 ? 1 : 0;
                // (the sector's lit palette: the changer's own, on the planes its floor sprite faces)
                if (src.Pal != 0 && (src.Cstat & Cstat.AlignMask) == Cstat.Floor)
                {
                    var sec = Board.Sectors[s.B];
                    int status = (src.Cstat & Cstat.OneSided) == 0 ? 3 : (src.Cstat & Cstat.YFlip) != 0 ? 2 : 1;
                    if (s.FxFloor != 0 && (status & 1) != 0)
                        sec.FloorPal = src.Pal;
                    if (s.FxCeil != 0 && (status & 2) != 0)
                        sec.CeilingPal = src.Pal;
                    LookChanged(s.B);
                }
            }
            else
                s.FxAlways = 1;
        }
        var x = Board.Sectors[s.B].Xs;
        x.Wave = s.FxWave;
        x.Amplitude = s.FxAmp;
        x.Freq = s.FxFreq;
        x.Phase = s.FxPhase;
        x.ShadeAlways = s.FxAlways;
        x.ShadeFloor = s.FxFloor;
        x.ShadeCeiling = s.FxCeil;
        x.ShadeWalls = s.FxShadeWalls;
        _fxSectors = null;      // the light-effect sectors are listed again
        SectorFxChanged?.Invoke(s);
    }

    /// <summary>A sector's light effect was changed in play (the renderer's shaded surfaces follow).</summary>
    public event Action<Sector> SectorFxChanged;

    /// <summary>useSectorWindGen: wind (and with it panning) set on a sector.</summary>
    void UseSectorWindGen(Sprite src, Sector s)
    {
        if (s == null)
        {
            int b = SpriteBSector(src);
            bool had = XSectorOf(b) != null;
            s = EnsureXSector(b);
            if (s == null)
                return;
            if (!had)
                s.WindAlways = 1;
        }
        int windVel = (int)Blood.ClipRange(src.Data2, 0, 32767);
        if ((src.Data1 & 1) != 0)
            windVel = NnExtRandom(0, windVel);
        s.WindVel = windVel;
        if ((src.HitTag & KM.Flag1) != 0)
            s.PanAlways = s.WindAlways = 1;
        int ang = src.Ang;
        if (src.Data4 <= 0)
        {
            if ((src.Data1 & 2) != 0)
            {
                while (src.Ang == ang)
                    src.Ang = NnExtRandom(-2048, 2048) & 2047;
            }
        }
        else if ((src.Cstat & 0x2000) != 0) src.Ang += src.Data4;
        else if ((src.Cstat & 0x4000) != 0) src.Ang -= src.Data4;
        else if (src.SysData1 == 0)
        {
            if ((ang += src.Data4) >= 1024)
                src.SysData1 = 1;
            src.Ang = (int)Blood.ClipHigh(ang, 1024);
        }
        else
        {
            if ((ang -= src.Data4) <= -1024)
                src.SysData1 = 0;
            src.Ang = (int)Blood.ClipLow(ang, -1024);
        }
        s.WindAng = src.Ang;
        var x = Board.Sectors[s.B].Xs;
        x.WindVel = s.WindVel;
        x.WindAng = s.WindAng & 2047;
        x.WindAlways = s.WindAlways;
        if (src.Data3 > 0 && src.Data3 < 4)
        {
            x.PanFloor = src.Data3 == 1 || src.Data3 == 3 ? 1 : 0;
            x.PanCeiling = src.Data3 == 2 || src.Data3 == 3 ? 1 : 0;
            s.PanAngle = x.PanAngle = s.WindAng & 2047;
            s.PanVel = x.PanVel = s.WindVel & 255;
            x.PanAlways = s.PanAlways;
        }
    }

    /// <summary>windGenStopWindOnSectors: the wind generator went off: its sectors' wind stops.</summary>
    void WindGenStopWindOnSectors(Sprite src)
    {
        void Stop(Sector s)
        {
            s.WindVel = 0;
            Board.Sectors[s.B].Xs.WindVel = 0;
        }
        if (src.TxID <= 0)
        {
            if (SpriteSector(src) is { } own)
                Stop(own);
            return;
        }
        foreach (var o in Receivers(src.TxID))
            if (o is Sector s && ((s.XState == 1 && s.WindAlways == 0) || ((src.HitTag & KM.Flag1) != 0 && (src.HitTag & KM.Flag2) == 0)))
                Stop(s);
        foreach (int rx in RedirectedChannels(src))
            foreach (var o in Receivers(rx))
                if (o is Sector s && ((s.XState == 1 && s.WindAlways == 0) || (src.HitTag & KM.Flag2) != 0))
                    Stop(s);
    }

    // ---------------------------------------------------------------- slopes

    /// <summary>spriteSetSlope: a floor sprite sloped (Build keeps the slope in the sprite's offsets).</summary>
    void SpriteSetSlope(Sprite e, int slope)
    {
        e.Slope = slope;
        e.XOffset = e.YOffset = 0;      // (Build keeps the slope in the offsets: a sloped sprite has none)
        if (slope != 0)
            e.Cstat |= KM.CstatSlope;
        else if ((e.Cstat & Cstat.AlignMask) == KM.CstatSlope)
            e.Cstat = (e.Cstat & ~Cstat.AlignMask) | Cstat.Floor;
        SpriteLookChanged(e);
    }

    void Sprite2SectorSlope(Sprite e, int b, int rel, bool forcez)
    {
        var sec = Board.Sectors[b];
        bool touchFloor = (e.Cstat & Cstat.AlignMask) == Cstat.Floor && e.HasX && e.XTouch != 0;
        double z;
        int slope;
        if (rel == 1)
        {
            z = Board.CeilZAt(b, e.BX, e.BY);
            if (touchFloor) z++;
            slope = sec.CeilingHeinum;
        }
        else
        {
            z = Board.FloorZAt(b, e.BX, e.BY);
            if (touchFloor) z--;
            slope = sec.FloorHeinum;
        }
        SpriteSetSlope(e, slope);
        if (forcez)
            e.SetBZ(z);
    }

    /// <summary>useSlopeChanger: a sector's floor or ceiling slope (the sloped floor sprites lying on it with it), or a
    /// sprite's.</summary>
    void UseSlopeChanger(Sprite src, XObject obj)
    {
        bool flag2 = (src.HitTag & KM.Flag2) != 0;
        int slope = (src.HitTag & KM.Flag1) != 0 ? (int)Blood.ClipRange(src.Data2, -32767, 32767)
            : (32767 / KM.kPercFull) * (int)Blood.ClipRange(src.Data2, -KM.kPercFull, KM.kPercFull);
        if (obj is Sector s)
        {
            int b = s.B;
            var sec = Board.Sectors[b];
            if (src.Data1 == 0 || src.Data1 == 2)
            {
                if (slope == 0) sec.FloorStat &= ~2;
                else sec.FloorStat |= 2;
                if (!flag2)
                {
                    int oslope = sec.FloorHeinum;
                    foreach (var e in SpritesInB(b))
                    {
                        if ((e.Cstat & Cstat.AlignMask) != KM.CstatSlope)
                            continue;
                        if (Board.FloorZAt(b, e.BX, e.BY) - KM.kSlopeDist <= e.BZ)
                        {
                            Sprite2SectorSlope(e, b, 0, true);
                            sec.FloorHeinum = slope;
                            Sprite2SectorSlope(e, b, 0, true);
                            sec.FloorHeinum = oslope;
                        }
                    }
                }
                sec.FloorHeinum = slope;
            }
            if (src.Data1 == 1 || src.Data1 == 2)
            {
                if (slope == 0) sec.CeilingStat &= ~2;
                else sec.CeilingStat |= 2;
                if (!flag2)
                {
                    int oslope = sec.CeilingHeinum;
                    foreach (var e in SpritesInB(b))
                    {
                        if ((e.Cstat & Cstat.AlignMask) != KM.CstatSlope)
                            continue;
                        if (Board.CeilZAt(b, e.BX, e.BY) + KM.kSlopeDist >= e.BZ)
                        {
                            Sprite2SectorSlope(e, b, 1, true);
                            sec.CeilingHeinum = slope;
                            Sprite2SectorSlope(e, b, 1, true);
                            sec.CeilingHeinum = oslope;
                        }
                    }
                }
                sec.CeilingHeinum = slope;
            }
            Board.PlaneVersion[b]++;
            Board.TouchShape(b);
            LookChanged(b);
            // the physics sprites get a nudge
            foreach (var e in SpritesInB(b))
            {
                if (e.HasX && e.PhysAttr > 0)
                {
                    e.PhysAttr |= K.kPhysFalling;
                    e.ZVel++;
                }
                else if ((e.StatNum == K.kStatThing || e.StatNum == K.kStatDude) && (e.HitTag & K.kPhysGravity) != 0)
                {
                    e.HitTag |= K.kPhysFalling;
                    e.ZVel++;
                }
            }
        }
        else if (obj is Sprite e)
        {
            e.Cstat |= Cstat.Floor;
            e.Cstat |= KM.CstatSlope;
            int b = SpriteBSector(e);
            switch (src.Data4)
            {
                case 1:
                    if (b >= 0) Sprite2SectorSlope(e, b, 0, flag2);
                    break;
                case 2:
                    if (b >= 0) Sprite2SectorSlope(e, b, 1, flag2);
                    break;
                case 3:
                    if (b < 0)
                        break;
                    if (Board.FloorZAt(b, e.BX, e.BY) - KM.kSlopeDist <= e.BZ) Sprite2SectorSlope(e, b, 0, flag2);
                    if (Board.CeilZAt(b, e.BX, e.BY) + KM.kSlopeDist >= e.BZ) Sprite2SectorSlope(e, b, 1, flag2);
                    break;
                default:
                    SpriteSetSlope(e, slope);
                    break;
            }
        }
    }

    // ---------------------------------------------------------------- velocity, teleport

    void UseVelocityChangerSector(Sprite src, XObject causer, int b)
    {
        foreach (var e in SpritesInB(b))
            UseVelocityChanger(src, causer, e);
    }

    /// <summary>useVelocityChanger: a sprite's velocity set or added to, along given or angle-made directions.</summary>
    void UseVelocityChanger(Sprite src, XObject causer, XObject obj)
    {
        const int kVelShift = 8, kScaleVal = 0x10000;
        if (obj is Sector sec)
        {
            UseVelocityChangerSector(src, causer, sec.B);
            return;
        }
        if (obj is not Sprite e)
            return;
        var causerSpr = causer as Sprite;
        bool relative = (src.HitTag & KM.Flag1) != 0;
        bool toDstAng = (src.HitTag & KM.Flag2) != 0;
        bool toSrcAng = (src.HitTag & KM.Flag4) != 0;
        bool toRndAng = (src.HitTag & KM.Flag8) != 0;
        bool chgDstAng = (src.HitTag & KM.Flag16) == 0;
        bool toRelAng = (src.HitTag & KM.Flag64) != 0;
        bool toEvnAng = toDstAng && toSrcAng && causerSpr != null;
        bool toAng = toRelAng || toDstAng || toSrcAng || toEvnAng || toRndAng;
        bool toAng180 = toRndAng && (toDstAng || toSrcAng || toEvnAng);
        double xv = 0, yv = 0, zv = 0;
        int nAng = 0;
        int r = (int)Blood.MulScale14(src.Data4 << kVelShift, kScaleVal);
        if (r != 0)
            r = NnExtRandom(-r, r);
        if (ValueIsBetween(src.Data3, -32767, 32767))
        {
            if ((zv = Blood.MulScale14(src.Data3 << kVelShift, kScaleVal)) != 0)
                zv += r;
        }
        else if (!relative)
            zv = e.ZVel;
        if (!toAng)
        {
            if (ValueIsBetween(src.Data1, -32767, 32767))
            {
                if ((xv = Blood.MulScale14(src.Data1 << kVelShift, kScaleVal)) != 0)
                    xv += r;
            }
            else if (!relative)
                xv = e.XVel;
            if (ValueIsBetween(src.Data2, -32767, 32767))
            {
                if ((yv = Blood.MulScale14(src.Data2 << kVelShift, kScaleVal)) != 0)
                    yv += r;
            }
            else if (!relative)
                yv = e.YVel;
        }
        else
        {
            if (toEvnAng) nAng = causerSpr.Ang;
            else if (toSrcAng) nAng = src.Ang;
            else nAng = e.Ang;
            if (toRelAng)
                nAng += src.Ang;
            nAng &= 2047;
            if (!toAng180 && toRndAng)
            {
                int t0 = nAng;
                while (t0 == nAng)
                    nAng = NnExtRandom(0, 2048);
            }
            if (chgDstAng)
                ChangeSpriteAngle(e, nAng);
            int t = src.Data1 << kVelShift;
            if (t != 0)
                t += r;
            xv = Blood.MulScale14(t, Blood.Sar(Blood.Cos(nAng), 16));
            yv = Blood.MulScale14(t, Blood.Sar(Blood.Sin(nAng), 16));
        }
        if (src.PhysAttr != 0)
        {
            bool debris = true;
            switch (e.StatNum)
            {
                case K.kStatThing:
                    break;
                case K.kStatFX:
                case K.kStatDude:
                case K.kStatProjectile:
                    if (e.StatNum == K.kStatFX)
                        debris = false;
                    e.HitTag = (src.PhysAttr & K.kPhysMove) != 0 ? e.HitTag | K.kPhysMove : e.HitTag & ~K.kPhysMove;
                    e.HitTag = (src.PhysAttr & K.kPhysGravity) != 0 ? e.HitTag | K.kPhysGravity : e.HitTag & ~K.kPhysGravity;
                    e.HitTag = (src.PhysAttr & K.kPhysFalling) != 0 ? e.HitTag | K.kPhysFalling : e.HitTag & ~K.kPhysFalling;
                    break;
            }
            if (debris)
            {
                if (!e.HasX)
                    DbInsertXSprite(e);
                if (e.StatNum == K.kStatThing)
                {
                    e.HitTag &= ~(K.kPhysMove | K.kPhysGravity | K.kPhysFalling);
                    e.StatNum = K.kStatDecoration;
                }
                e.PhysAttr = src.PhysAttr;
                if (!_physSprites.Contains(e))
                {
                    GetSpriteMassBySize(e);
                    _physSprites.Add(e);
                }
            }
        }
        if (relative)
        {
            e.XVel += xv;
            e.YVel += yv;
            e.ZVel += zv;
        }
        else
        {
            e.XVel = xv;
            e.YVel = yv;
            e.ZVel = zv;
        }
        int vAng = GetVelocityAngle(e);
        if (toAng)
        {
            RotateVel(e, toAng180 ? 1024 : (nAng - vAng) & 2047);
            vAng = GetVelocityAngle(e);
        }
        if (chgDstAng)
            ChangeSpriteAngle(e, vAng);
        // a player's missile then hurts its owner too
        if (e.Owner is Player && e.StatNum == K.kStatProjectile)
            e.Owner = src;
    }

    /// <summary>useTeleportTarget: a sprite taken to the warp destination marker.</summary>
    void UseTeleportTarget(Sprite src, Sprite e)
    {
        var p = e as Player;
        int srcSect = SpriteBSector(src);
        var xs = XSectorOf(srcSect);
        bool isDude = p == null && IsDudeSprite(e);
        var (_, zBot) = Extents(src);
        SetBuildPos(e, src.BX, src.BY, zBot);
        SpriteSectorForget(e);
        if (srcSect >= 0)
            SetSpriteBSector(e, srcSect);
        ClampSprite(e, 1);
        if ((src.HitTag & KM.Flag1) != 0)
            TeleFrag(e, srcSect);
        if ((e.HitTag & K.kPhysGravity) != 0)
            e.HitTag |= K.kPhysFalling;
        if (xs != null)
        {
            if (xs.SEnter != 0 && (p != null || (isDude && xs.DudeLockout == 0)))
                TrTriggerSector(xs, K.kCmdSectorEnter, e);
            if (xs.SUnderwater != 0)
            {
                Sprite link = LowerLinkOf(srcSect);
                if (link != null)
                {
                    for (int i = 0; i < Board.SectorCount; i++)
                        if (UpperLinkOf(i) is { } u && u.Data1 == link.Data1)
                        {
                            link = u;
                            break;
                        }
                    e.Medium = link.Type == K.kMarkerUpGoo ? K.kMediumGoo : K.kMediumWater;
                }
                if (p != null)
                {
                    if (link == null)
                        p.Medium = K.kMediumWater;
                    p.Posture = Player.PostureSwim;
                    p.BurnTime = 0;
                }
            }
            else
            {
                e.Medium = K.kMediumNormal;
                if (p != null)
                    p.Posture = p.InputCrouch ? Player.PostureCrouch : Player.PostureStand;
            }
        }
        if (e.StatNum == K.kStatDude && IsDudeSprite(e) && p == null)
        {
            double x = e.TargetX, y = e.TargetY, z = e.TargetZ;
            var target = e.XTarget;
            AiInitSprite(e);
            if (target != null && IsDudeSprite(target))
            {
                e.TargetX = x;
                e.TargetY = y;
                e.TargetZ = z;
                e.XTarget = target;
                AiActivateDude(e);
            }
        }
        if (src.Data3 == 1)
            e.XVel = e.YVel = e.ZVel = 0;
        else if (src.Data3 > 0)
        {
            // the movement turned to the marker's angle
            if ((src.Data3 & KM.Flag2) != 0)
                RotateVel(e, (src.Ang - GetVelocityAngle(e)) & 2047);
            if ((src.Data3 & KM.Flag4) != 0)
                e.ZVel = 0;
        }
        if (src.Data2 == 1)
            ChangeSpriteAngle(e, src.Ang);
        e.PrevOrigin = e.Origin;
        e.PrevAng = e.Ang;
        if (src.Data4 > 0)
            Sound3D(src, src.Data4, -1);
        if (p != null)
        {
            PlayerResetInertia(p);
            if (src.Data2 == 1)
                p.ZViewVel = p.ZWeaponVel = 0;
        }
        UpdateBox(e);
    }

    /// <summary>TeleFrag: whoever stands where a sprite arrives dies.</summary>
    void TeleFrag(Sprite killer, int b)
    {
        if (b < 0)
            return;
        foreach (var e in SpritesInB(b))
        {
            if (e.StatNum == K.kStatDude && e != killer)
                ActDamageSprite(killer, e, K.kDamageExplode, 4000);
            else if (e.StatNum == K.kStatThing)
                ActDamageSprite(killer, e, K.kDamageExplode, 4000);
        }
    }

    // ---------------------------------------------------------------- effects, sequences, sounds, missiles, drips

    static void EffectGenPropagateAppearance(Sprite src, Sprite dest, Sprite fx)
    {
        if ((src.HitTag & KM.Flag1) != 0)
        {
            fx.Pal = src.Pal;
            fx.XOffset = src.XOffset;
            fx.YOffset = src.YOffset;
            fx.XRepeat = src.XRepeat;
            fx.YRepeat = src.YRepeat;
            fx.Shade = src.Shade;
        }
        if ((src.HitTag & KM.Flag2) != 0)
            fx.Cstat = src.Cstat & ~Cstat.Invisible;
        if ((src.HitTag & KM.Flag8) != 0)
            fx.Ang = dest.Ang;
        else if ((src.HitTag & KM.Flag4) != 0)
            fx.Ang = src.Ang;
        if ((src.HitTag & KM.Flag16) != 0)
            fx.Picnum = src.Picnum;
    }

    /// <summary>useEffectGen: an effect (data2, or a callback effect from 200) made at a sprite.</summary>
    void UseEffectGen(Sprite src, Sprite e)
    {
        int fxId = src.Data3 <= 0 ? src.Data2 : src.Data2 + Random(src.Data3 + 1);
        e ??= src;
        int sect = SpriteBSector(e);
        if (sect < 0)
            return;
        var (top, bottom) = Extents(e);
        if (RngOk(fxId, KM.kEffectGenCallbackBase, KM.kEffectGenCallbackBase + KM.EffectGenCallbacks.Length))
        {
            int cb = KM.EffectGenCallbacks[fxId - KM.kEffectGenCallbackBase];
            EvKillCallback(e, cb);
            EvPostCallback(e, 0, cb);
        }
        else if (ValueIsBetween(fxId, 0, K.FX_56 + 1))
        {
            double pos = src.Data4 switch
            {
                1 => bottom,
                2 => top + Blood.Sar(bottom - top, 1),
                3 => Board.FloorZAt(sect, e.BX, e.BY),
                4 => Board.CeilZAt(sect, e.BX, e.BY),
                _ => top,
            };
            var fx = FxSpawnIn(fxId, sect, e.BX, e.BY, pos);
            if (fx != null)
            {
                fx.Owner = src;
                EffectGenPropagateAppearance(src, e, fx);
                fx.Cstat &= ~Cstat.OneSided;
                SpriteLookChanged(fx);
            }
        }
    }

    /// <summary>An effect spawned in a known Build sector.</summary>
    Sprite FxSpawnIn(int nFx, int sect, double x, double y, double z)
    {
        var fx = FxSpawn(nFx, x, y, z, 0);
        if (fx != null && sect >= 0)
            SetSpriteBSector(fx, sect);
        return fx;
    }

    void KillEffectGenCallbacksOn(XObject o)
    {
        foreach (int cb in KM.EffectGenCallbacks)
            EvKillCallback(o, cb);
    }

    /// <summary>killEffectGenCallbacks: the callback effects an effect generator started stop.</summary>
    void KillEffectGenCallbacks(Sprite src)
    {
        if (src.Data2 < KM.kEffectGenCallbackBase)
            return;
        switch (src.TxID)
        {
            case K.kChannelZero:
                KillEffectGenCallbacksOn(src);
                break;
            case K.kChannelAllPlayers:
                foreach (var p in Players)
                    KillEffectGenCallbacksOn(p);
                break;
            case K.kChannelEventCauser:
                foreach (var e in Sprites)
                    KillEffectGenCallbacksOn(e);
                break;
            default:
                if (src.TxID >= K.kChannelUser)
                {
                    foreach (var o in Receivers(src.TxID))
                        if (o is Sprite)
                            KillEffectGenCallbacksOn(o);
                }
                else if (RngOk(src.TxID, K.kChannelPlayer0, K.kChannelPlayer0 + KM.kMaxPlayers - 1)
                    && GetPlayerById(src.TxID - K.kChannelPlayer0) is { } p)
                    KillEffectGenCallbacksOn(p);
                break;
        }
    }

    /// <summary>seqSpawnerOffSameTx: the other sequence spawners sending on the same channel go off.</summary>
    void SeqSpawnerOffSameTx(Sprite src)
    {
        foreach (var e in Sprites)
        {
            if (e.Type != KM.kModernSeqSpawner || e == src || e.Removed || !e.HasX || e.XState == 0 || e.TxID != src.TxID)
                continue;
            EvKill(e);
            e.XState = 0;
        }
    }

    /// <summary>useSeqSpawnerGen: a sequence (data2; none: stop it) played on a sprite (or a copy made for it), a wall
    /// or a sector's floor or ceiling.</summary>
    void UseSeqSpawnerGen(Sprite src, XObject obj)
    {
        if (src.Data2 > 0 && !SeqData.Exists(src.Data2))
        {
            ModernMissing($"sequence #{src.Data2}");
            return;
        }
        switch (obj)
        {
            case Sector s:
                if (src.Data2 <= 0)
                {
                    if (src.Data3 == 3 || src.Data3 == 1) SurfSeq.Kill(this, SurfSeq.Floor, s.B);
                    if (src.Data3 == 3 || src.Data3 == 2) SurfSeq.Kill(this, SurfSeq.Ceiling, s.B);
                }
                else
                {
                    if (src.Data3 == 3 || src.Data3 == 1) SurfSeq.Spawn(this, src.Data2, SurfSeq.Floor, s.B);
                    if (src.Data3 == 3 || src.Data3 == 2) SurfSeq.Spawn(this, src.Data2, SurfSeq.Ceiling, s.B);
                }
                return;
            case Wall w:
            {
                var bw = Board.Walls[w.BWall];
                if (src.Data2 <= 0)
                {
                    if (src.Data3 == 3 || src.Data3 == 1) SurfSeq.Kill(this, SurfSeq.Wall, w.BWall);
                    if ((src.Data3 == 3 || src.Data3 == 2) && (bw.Cstat & 16) != 0) SurfSeq.Kill(this, SurfSeq.Masked, w.BWall);
                    return;
                }
                if (src.Data3 == 3 || src.Data3 == 1)
                    SurfSeq.Spawn(this, src.Data2, SurfSeq.Wall, w.BWall);
                if (src.Data3 == 3 || src.Data3 == 2)
                {
                    if (bw.NextWall < 0)
                    {
                        if (src.Data3 == 3)
                            SurfSeq.Spawn(this, src.Data2, SurfSeq.Wall, w.BWall);
                    }
                    else
                    {
                        if ((bw.Cstat & 16) == 0)
                        {
                            bw.Cstat |= 16;
                            WallLookChanged(w.BWall);
                        }
                        SurfSeq.Spawn(this, src.Data2, SurfSeq.Masked, w.BWall);
                    }
                }
                if (src.Data4 > 0)
                {
                    var (x1, y1, x2, y2) = Board.WallEnds(w.BWall);
                    double cx = (x1 + x2) >> 1, cy = (y1 + y2) >> 1;
                    Board.GetZsOfSlope(bw.Sector, cx, cy, out double ceilZ, out double floorZ);
                    if (bw.NextSector >= 0)
                    {
                        Board.GetZsOfSlope(bw.NextSector, cx, cy, out double ceilZ2, out double floorZ2);
                        ceilZ = Blood.ClipLow(ceilZ, ceilZ2);
                        floorZ = Blood.ClipHigh(floorZ, floorZ2);
                    }
                    Sound3DAt(B2Q(cx, cy, Blood.Sar(ceilZ + floorZ, 1)), src.Data4);
                }
                return;
            }
            case Sprite e:
            {
                if (src.Data2 <= 0)
                {
                    Seq.Kill(e);
                    return;
                }
                int sect = SpriteBSector(e);
                if (sect < 0)
                    return;
                if (src.Data3 > 0)
                {
                    var (top, bottom) = Extents(e);
                    double z = src.Data3 switch
                    {
                        2 => bottom,
                        3 => top,
                        4 => top + Blood.Sar(bottom - top, 1),
                        5 => Board.FloorZAt(sect, e.BX, e.BY),
                        6 => Board.CeilZAt(sect, e.BX, e.BY),
                        _ => e.BZ,
                    };
                    var n = ActSpawnSprite(e.BX, e.BY, z, K.kStatDecoration, true);
                    SetSpriteBSector(n, sect);
                    Seq.Spawn(this, src.Data2, n);
                    if ((src.HitTag & KM.Flag1) != 0)
                    {
                        n.Pal = src.Pal;
                        n.Shade = src.Shade;
                        n.XRepeat = src.XRepeat;
                        n.YRepeat = src.YRepeat;
                        n.XOffset = src.XOffset;
                        n.YOffset = src.YOffset;
                    }
                    if ((src.HitTag & KM.Flag2) != 0)
                        n.Cstat |= src.Cstat;
                    if ((src.HitTag & KM.Flag4) != 0)
                        n.Ang = src.Ang;
                    SpriteLookChanged(n);
                    EvPostCallback(n, 1000, K.kCallbackRemove);
                }
                else
                    Seq.Spawn(this, src.Data2, e);
                if (src.Data4 > 0)
                    Sound3D(e, src.Data4, -1);
                return;
            }
        }
    }

    void UseSoundGen(Sprite src, Sprite e)
    {
        int pitch = src.Data4 << 1;
        if (pitch < 2000)
            pitch = 0;
        Sound3DCP(e, src.Data2, -1, 0, pitch, (int)Blood.ClipRange(src.Data3, -1, 255));
    }

    /// <summary>sfxPlay3DSoundCP: a sound with its own pitch and volume (0: the sound's own).</summary>
    public void Sound3DCP(XObject e, int soundId, int channel, int flags, int pitch, int volume)
    {
        if (soundId == 0)
            return;
        if (pitch <= 0 && volume == 0)
            Sound3D(e, soundId, channel, flags);
        else
        {
            SoundPlayedCP?.Invoke(e, soundId, channel, flags, pitch, volume);
            if (IsModern && e is Sprite s)
                PatrolNoteSound(s, s.BX, s.BY, SpriteBSector(s), soundId, volume);
        }
    }

    /// <summary>A sound played with a pitch and volume of its own (object, sound id, channel, flags; the pitch a
    /// 16.16 factor of the sample's rate, 0 or less the sound's own; the volume 1..255, 0 the sound's own, -1 silent).</summary>
    public event Action<XObject, int, int, int, int, int> SoundPlayedCP;

    /// <summary>useUniMissileGen: a missile of any kind fired from a sprite, with its own velocity, slope and bursting.</summary>
    void UseUniMissileGen(Sprite src, Sprite e)
    {
        e ??= src;
        if (src.Data1 < K.kMissileBase || src.Data1 >= K.kMissileMax)
            return;
        double dx = 0, dy = 0, dz;
        if ((e.Cstat & 32) != 0)
            dz = (e.Cstat & 8) != 0 ? 0x4000 : -0x4000;
        else
        {
            dx = Blood.Sar(Blood.Cos(e.Ang), 16);
            dy = Blood.Sar(Blood.Sin(e.Ang), 16);
            dz = Blood.ClipRange(src.Data3 << 6, -0x10000, 0x10000);
        }
        var m = ActFireMissile(e, 0, 0, dx, dy, dz, src.Data1);
        if (m == null)
            return;
        int from = src.HitTag & KM.Flag3;
        if (from > 0)
        {
            // what its sequence doesn't set, it takes from the generator (1) or the sprite fired from
            int canInherit = 0xF;
            if (m.HasX && Seq.Status(m) >= 0)
            {
                canInherit &= ~0x8;
                int id = Seq.Id(m), n = SeqData.FrameCount(id);
                for (int i = 0; i < n; i++)
                {
                    var (xr, yr) = SeqData.FrameRepeat(id, i);
                    if (SeqData.FramePal(id, i) != 0) canInherit &= ~0x4;
                    if (xr != 0) canInherit &= ~0x2;
                    if (yr != 0) canInherit &= ~0x1;
                }
            }
            var o = from == KM.Flag1 ? src : e;
            if ((canInherit & 0x2) != 0) m.XRepeat = o.XRepeat;
            if ((canInherit & 0x1) != 0) m.YRepeat = o.YRepeat;
            if ((canInherit & 0x4) != 0) m.Pal = o.Pal;
            if ((canInherit & 0x8) != 0) m.Shade = o.Shade;
            SpriteLookChanged(m);
        }
        if (src.Data2 > 0)
        {
            int velocity = src.Data2 << 12;
            m.XVel = Blood.MulScale14(velocity, dx);
            m.YVel = Blood.MulScale14(velocity, dy);
            m.ZVel = Blood.MulScale14(velocity, dz);
        }
        if (m.Type != K.kMissileFlareAlt && src.Data4 > 0)
            EvPostCallback(m, (int)Blood.ClipHigh(src.Data4, 500), KM.kCallbackMissileBurst);
    }

    /// <summary>callbackUniMissileBurst: the missile bursts into eight smaller ones.</summary>
    void UniMissileBurst(Sprite e)
    {
        if (e.StatNum != K.kStatProjectile)
            return;
        int nAngle = Blood.GetAngle(e.XVel, e.YVel);
        const int nRadius = 0x55555;
        for (int i = 0; i < 8; i++)
        {
            var b = ActSpawnSpriteFrom(e, K.kStatProjectile);
            b.Type = e.Type;
            b.Shade = e.Shade;
            b.Picnum = e.Picnum;
            b.Cstat = e.Cstat;
            if ((b.Cstat & Cstat.Block) != 0)
            {
                b.Cstat &= ~Cstat.Block;       // they don't hit each other at once
                EvPostCallback(b, 100, KM.kCallbackMissileSpriteBlock);
            }
            b.Pal = e.Pal;
            b.ClipDist = e.ClipDist / 4;
            b.HitTag = e.HitTag;
            b.XRepeat = e.XRepeat / 2;
            b.YRepeat = e.YRepeat / 2;
            b.Ang = (e.Ang + MissileInfo(e.Type, K.MI_angleOfs)) & 2047;
            b.Owner = e.Owner;
            ActBuildMissile(b, e);
            int nAngle2 = (i << 11) / 8;
            double dx = 0;
            double dy = Blood.MulScale30R(nRadius, Blood.Sin(nAngle2));
            double dz = Blood.MulScale30R(nRadius, -Blood.Cos(nAngle2));
            if ((i & 1) != 0)
            {
                dy = Blood.Sar(dy, 1);
                dz = Blood.Sar(dz, 1);
            }
            (dx, dy) = Blood.RotateVector(dx, dy, nAngle);
            b.XVel += dx;
            b.YVel += dy;
            b.ZVel += dz;
            SpriteLookChanged(b);
            EvPostCallback(b, 960, K.kCallbackRemove);
        }
        EvPostCallback(e, 0, K.kCallbackRemove);
    }

    void MakeMissileBlocking(Sprite e)
    {
        if (e.StatNum != K.kStatProjectile)
            return;
        e.Cstat |= Cstat.Block;
        UpdateBox(e);
    }

    void UseDripGenerator(Sprite src, Sprite e)
    {
        var (_, bottom) = Extents(e);
        var thing = ActSpawnThing(e.BX, e.BY, bottom, e.Type == K.kGenDripWater ? K.kThingDripWater : K.kThingDripBlood);
        if (thing == null)
            return;
        thing.Owner = e;
        if (src.Data4 != 0)
            thing.ZVel = Blood.MulScale8(0x10000, src.Data4);
    }

    // ---------------------------------------------------------------- dudes and items

    /// <summary>nnExtSpawnDude: a dude made by a dude spawner (the modern marker: what it inherits, and awake at once
    /// when the spawner says so).</summary>
    Sprite NnExtSpawnDude(Sprite src, Sprite at, int nType, double a3, double a4)
    {
        if (nType < K.kDudeBase || nType >= K.kDudeMax)
            return null;
        var d = ActSpawnSpriteFrom(at, K.kStatDude);
        int angle = at.Ang;
        double x = at.BX, y = at.BY, z = a4 + at.BZ;
        if (a3 >= 0)
        {
            x += Blood.MulScale30R(Blood.Cos(angle), a3);
            y += Blood.MulScale30R(Blood.Sin(angle), a3);
        }
        SetBuildPos(d, x, y, z);
        d.PrevOrigin = d.Origin;
        SpriteSectorForget(d);
        d.Type = d.InitType = nType;
        d.Ang = angle;
        d.Cstat |= 0x1101;
        d.ClipDist = Info.Dude(nType, Info.DI_clipdist);
        d.XRespawn = 1;
        d.XHealth = Info.Dude(nType, Info.DI_startHealth) << 4;
        d.ClassName = ClassOfType(nType);
        if (SeqData.Exists(Info.Dude(nType, Info.DI_seqStartID)))
            Seq.Spawn(this, Info.Dude(nType, Info.DI_seqStartID), d);
        if ((src.HitTag & KM.Flag1) != 0)
        {
            // what the dude inherits from its spawner
            if (d.Pal <= 0)
                d.Pal = src.Pal;
            d.TxID = src.TxID;
            d.XCommand = src.XCommand;
            d.TriggerOn = src.TriggerOn;
            d.TriggerOff = src.TriggerOff;
            d.DropMsg = src.DropMsg;
            d.DudeDeaf = src.DudeDeaf;
            d.DudeGuard = src.DudeGuard;
            d.DudeAmbush = src.DudeAmbush;
            d.DudeFlag4 = src.DudeFlag4;
            d.Unused1 = src.Unused1;
        }
        UpdateBox(d);
        AiInitSprite(d);
        KillMgrAddCountSprite(d);
        bool burning = IsBurningDude(d);
        if (burning)
        {
            d.BurnTime = 10;
            d.XTarget = null;
        }
        if ((burning || (src.HitTag & KM.Flag3) != 0) && d.DudeFlag4 == 0)
            AiActivateDude(d);
        return d;
    }

    /// <summary>useDudeSpawn: one of the spawner's data fields' dudes at random (two or more given), else its data1's.</summary>
    void UseDudeSpawn(Sprite src, Sprite at)
    {
        int selected = RandomGetDataValue(src, KM.kRandomizeDude);
        if (selected <= 0 || NnExtSpawnDude(src, at, selected, at.ClipDist << 1, 0) == null)
            NnExtSpawnDude(src, at, src.Data1, at.ClipDist << 1, 0);
    }

    void UseCustomDudeSpawn(Sprite src, Sprite at) => CdudeSpawn(src, at, at.ClipDist << 1);

    /// <summary>randomDropPickupObject: one of the generator's items at random, at its place.</summary>
    Sprite RandomDropPickupObject(Sprite src, int prevItem)
    {
        int selected = -1, maxRetries = 9;
        while ((selected = RandomGetDataValue(src, KM.kRandomizeItem)) == prevItem)
            if (maxRetries-- <= 0)
                break;
        if (selected <= 0)
            return null;
        var item = ActDropObject(src, selected);
        if (item == null)
            return null;
        src.DropMsg = item.Type;        // what it dropped
        item.Origin = item.PrevOrigin = src.Origin;
        if ((src.HitTag & KM.Flag1) != 0 && (src.TxID > 0 || (src.TxID != 3 && src.LockMsg > 0)))
        {
            // picked up, the item sends what the generator would
            DbInsertXSprite(item);
            item.TxID = src.TxID;
            item.XCommand = src.SysData1;
            item.TriggerOn = src.TriggerOn;
            item.TriggerOff = src.TriggerOff;
            item.XPickup = 1;
        }
        UpdateBox(item);
        return item;
    }

    /// <summary>useRandomItemGen: the item dropped before goes, a new one comes.</summary>
    void UseRandomItemGen(Sprite src)
    {
        if (src.DropMsg > 0)
        {
            foreach (var item in new List<Sprite>(StatList(K.kStatItem)))
            {
                if (item.Type == src.DropMsg && item.Origin == src.Origin)
                {
                    FxSpawn(K.FX_29, src.BX, src.BY, src.BZ, 0);
                    item.Type = 0;
                    ActPostSprite(item, K.kStatFree);
                    break;
                }
            }
        }
        var drop = RandomDropPickupObject(src, src.DropMsg);
        if (drop == null)
            return;
        ClampSprite(drop);
        // a generator under physics: its item falls
        if (_physSprites.Contains(src))
        {
            if (!drop.HasX)
                DbInsertXSprite(drop);
            drop.PhysAttr |= K.kPhysMove | K.kPhysGravity | K.kPhysFalling;
            drop.Cstat &= ~Cstat.Block;
            if (!_physSprites.Contains(drop))
                _physSprites.Add(drop);
            GetSpriteMassBySize(drop);
        }
        UpdateBox(drop);
    }

    // ---------------------------------------------------------------- damage

    /// <summary>useSpriteDamager: a dude damaged, or the dudes of a sector (all in it, or those touching the plane or
    /// walls the damager lies on), or (all) every dude of the level (data1 667: no players, 668: players only).</summary>
    void UseSpriteDamager(Sprite src, XObject obj, bool all)
    {
        if (all)
        {
            foreach (var d in new List<Sprite>(StatList(K.kStatDude)))
            {
                if (d.StatNum != K.kStatDude)
                    continue;
                if (src.Data1 == 667 && d is Player) continue;
                if (src.Data1 == 668 && d is not Player) continue;
                DamageSprites(src, d);
            }
            return;
        }
        if (obj is Sprite e)
        {
            DamageSprites(src, e);
            return;
        }
        if (obj is not Sector s)
            return;
        int own = SpriteBSector(src);
        if (own < 0)
            return;
        var sec = Board.Sectors[own];
        var (top, bottom) = Extents(src);
        bool floor = bottom >= sec.FloorZ, ceil = top <= sec.CeilingZ, wall = (src.Cstat & 0x10) != 0;
        bool enter = !floor && !ceil && !wall;
        foreach (var d in SpritesInB(s.B))
        {
            if (!IsDudeSprite(d) || !d.HasX)
                continue;
            if (enter)
                DamageSprites(src, d);
            else if (floor && (d.FlorHit & 0xc000) == 0x4000 && SectorOfHit(d.FlorHitEnt, d) == s.B)
                DamageSprites(src, d);
            else if (ceil && (d.CeilHit & 0xc000) == 0x4000 && SectorOfHit(d.CeilHitEnt, d) == s.B)
                DamageSprites(src, d);
            else if (wall && (d.Hit & 0xc000) == 0x8000 && SpriteBSector(d) == s.B)
                DamageSprites(src, d);
        }
    }

    // the Build sector a floor or ceiling hit names (the sprite's own when the hit keeps none)
    int SectorOfHit(object hitEnt, Sprite e) => hitEnt is Sector hs ? hs.B : SpriteBSector(e);

    /// <summary>damageSprites: the damager's damage to a dude (data2 the kind, data3 how much: percents of its start
    /// health, or points with flag 1; 0 kills).</summary>
    void DamageSprites(Sprite src, Sprite e)
    {
        if (!IsDudeSprite(e) || !XSprIsFine(e) || src.Data3 < 0)
            return;
        if (e.XHealth <= 0 || e.XLocked != 0)
            return;
        var p = e as Player;
        if (p != null && (p.PwUpTime[K.kPwUpDeathMask] > 0 || p.GodMode))
            return;
        double health = 0;
        int dmgType = src.Data2 >= K.kDamageFall ? (int)Blood.ClipHigh(src.Data2, K.kDamageTesla) : -1;
        double dmg = e.XHealth;
        bool immune = dmgType >= 0 && NnExtIsImmune(e, dmgType, 0);
        bool showEffects = (src.HitTag & KM.Flag2) == 0;
        bool setHealth = (src.HitTag & KM.Flag8) == 0 || immune;
        bool forceRecoil = (src.HitTag & KM.Flag4) != 0;
        if (src.Data3 > 0)
        {
            if ((src.HitTag & KM.Flag1) != 0) dmg = Blood.ClipHigh(src.Data3 << 1, 65535);
            else if (e.SysData2 > 0) dmg = Blood.Int(Blood.ClipHigh(e.SysData2 << 4, 65535) * src.Data3 / KM.kPercFull);
            else dmg = Blood.Int((Info.Dude(e.Type, Info.DI_startHealth) << 4) * src.Data3 / (double)KM.kPercFull);
            health = Blood.ClipLow(e.XHealth - dmg, 0);
        }
        bool death = health <= 0;
        if (!death)
        {
            if (dmgType >= K.kDamageFall && !immune)
            {
                if (p != null)
                {
                    // the armour takes its part, the dude the plain damage
                    PlayerDamageArmor(p, dmgType, dmg);
                    var armor = (double[])p.Armor.Clone();
                    Array.Clear(p.Armor);
                    ActDamageSprite(src, e, dmgType, dmg);
                    armor.CopyTo(p.Armor, 0);
                }
                else
                    ActDamageSprite(src, e, dmgType, dmg);
            }
            if (!IsDudeSprite(e) || !XSprIsFine(e))
                return;
            if (e.XHealth <= 0)
            {
                if (dmgType < 0)
                    death = true;
                else
                    return;
            }
            else if (IsBurningDude(e))
            {
                if (!RngOk(e.BurnTime, 1, 1200))
                    ActBurnSprite(src, e, 1200);
                return;
            }
            else if (setHealth)
                e.XHealth = health;
            if (!death)
            {
                if (showEffects)
                {
                    switch (dmgType)
                    {
                        case K.kDamageBurn:
                            if (!RngOk(e.BurnTime, 1, 1200))
                            {
                                ActBurnSprite(src, e, Blood.ClipLow(Blood.Sar(dmg, 1), 128));
                                EvKillCallback(e, K.kCallbackFXFlameLick);
                                EvPostCallback(e, 0, K.kCallbackFXFlameLick);
                            }
                            break;
                        case K.kDamageTesla:
                            forceRecoil = true;
                            break;
                        case K.kDamageBullet:
                            EvKillCallback(e, K.kCallbackFXBloodSpurt);
                            for (int i = 0; i < 3; i++)
                                if (Chance(0x10000 >> i))
                                    FxSpawnBlood(e, 0);
                            break;
                        case K.kDamageDrown:
                            if (p != null && Chance(0x2000))
                                p.BlindEffect += Blood.ClipHigh((int)dmg << 2, 128);
                            break;
                    }
                }
                if (forceRecoil && p == null)
                {
                    e.Data3 = 32767;        // so the dude plays the recoil
                    e.TeslaHit = dmgType == K.kDamageTesla ? 1 : 0;
                    if (e.AiState == null || e.AiState.Type != AiState.Recoil)
                        RecoilDude(e);
                }
            }
        }
        if (death)
        {
            if (dmgType < 0 || immune)
                dmgType = K.kDamageBullet;
            if (p != null)
                ActDamageSprite(src, p, dmgType, dmg);
            else
            {
                switch (dmgType)
                {
                    case K.kDamageExplode:
                        break;
                    case K.kDamageBurn:
                        if (IsBurningDude(e) && e.BurnTime > 0)
                            return;
                        break;
                    default:
                        e.XHealth = 0x1000;     // so it doesn't turn to gibs at once
                        break;
                }
                ActKillDude(src, e, dmgType, dmg);
            }
        }
    }

    /// <summary>nnExtIsImmune.</summary>
    bool NnExtIsImmune(Sprite e, int dmgType, int minScale)
    {
        if (dmgType >= K.kDamageFall && dmgType < K.kDamageMax && e.HasX && e.XLocked != 1)
        {
            if (Info.IsThing(e.Type))
                return Info.Thing(e.Type, K.TI_dmgControl + dmgType) <= minScale;
            if (IsDudeSprite(e))
            {
                if (e is Player pl)
                    return pl.DamageControl[dmgType] != 0;
                if (e.Type == KM.kDudeModernCustom && e.Custom != null)
                    return e.Custom.GetDamage(-1, dmgType) <= minScale;
                return DmgControl(e, dmgType) <= minScale;
            }
        }
        return true;
    }

    // ---------------------------------------------------------------- gib objects

    static bool SeqCanOverride(int seq, int from, out bool xrp, out bool yrp, out bool plu)
    {
        xrp = yrp = plu = true;
        int n = SeqData.FrameCount(seq);
        for (int f = from; f < n; f++)
        {
            var (xr, yr) = SeqData.FrameRepeat(seq, f);
            if (xr != 0) xrp = false;
            if (yr != 0) yrp = false;
            if (SeqData.FramePal(seq, f) != 0) plu = false;
        }
        return xrp && yrp && plu;
    }

    // gibPropagateAppearance: what a gib object's new effects and things take from it (or the sprite gibbed)
    void GibPropagateAppearance(Sprite src, Sprite from, Sprite dest)
    {
        int flags = src.SysData1;
        bool setPal = (flags & KM.Flag1) != 0, setShd = (flags & KM.Flag2) != 0, setRep = (flags & KM.Flag4) != 0,
            setPic = (flags & KM.Flag8) != 0, setCst = (flags & KM.Flag16) != 0, setAng = (flags & KM.Flag32) != 0;
        bool killSeq = setPic || setShd;
        if (Seq.Status(dest) >= 0)
        {
            int id = Seq.Id(dest);
            if (!killSeq)
            {
                SeqCanOverride(id, 1, out bool xrp, out bool yrp, out bool plu);
                killSeq = (setRep && (!xrp || !yrp)) || (setPal && !plu);
            }
            if (killSeq)
            {
                if (src.BusyTime == 0 && (SeqData.Flags(id) & SeqData.FlagRemove) != 0)
                {
                    // it would have gone with its sequence: go then
                    EvKillCallback(dest, K.kCallbackRemove);
                    EvPostCallback(dest, dest.SeqTime * K.kTicsPerFrame, K.kCallbackRemove);
                }
                Seq.Kill(dest);
            }
        }
        if (src.BusyTime != 0)
        {
            EvKillCallback(dest, K.kCallbackRemove);
            EvPostCallback(dest, src.BusyTime * 120 / 10, K.kCallbackRemove);
        }
        if (setPal) dest.Pal = from.Pal;
        if (setShd) dest.Shade = from.Shade;
        if (setRep)
        {
            dest.XRepeat = from.XRepeat;
            dest.YRepeat = from.YRepeat;
        }
        if (setPic) dest.Picnum = from.Picnum;
        if (setCst)
            dest.Cstat = from.Cstat & ~Cstat.Invisible;
        if (setAng)
            dest.Ang = from.Ang;
        else
        {
            int t = dest.Ang;
            while (t == dest.Ang)
                dest.Ang = Random2(2048) & 2047;
        }
        SpriteLookChanged(dest);
    }

    /// <summary>useGibObject: a gib or explode object's gibs (data1..3) at a sprite, their look as its flags say; its
    /// sound and dropped item; the object goes unless it is hidden or works on others.</summary>
    void UseGibObject(Sprite src, Sprite e)
    {
        e ??= src;
        int[] a = { (int)Blood.ClipRange(src.Data1, 0, 31), (int)Blood.ClipRange(src.Data2, 0, 31), (int)Blood.ClipRange(src.Data3, 0, 31) };
        int n = src.BurnTime != 0 || ModernMap == 2 ? 3 : 2;
        bool fromSrc = (src.SysData1 & KM.Flag64) == 0;
        bool toDest = (src.SysData1 & KM.Flag128) != 0 && src.TxID != 0;
        bool remove = !toDest && (src.Cstat & Cstat.Invisible) == 0;
        if (src.SysData1 == 0)
        {
            for (int i = 0; i < n; i++)
                if (a[i] != 0)
                    GibSprite(e, a[i] - 1);
        }
        else
        {
            // the effects and things there before
            int sect = SpriteBSector(e);
            var before = new HashSet<Sprite>();
            foreach (var f in SpritesInB(sect))
                if (f.Owner != src && (f.HitTag & K.kHitagFree) == 0 && (f.StatNum == K.kStatFX || f.StatNum == K.kStatThing))
                    before.Add(f);
            for (int i = 0; i < n; i++)
                if (a[i] != 0)
                    GibSprite(e, a[i] - 1);
            // the new ones take the look
            foreach (var f in Sprites.ToArray())
            {
                if (f.Removed || (f.HitTag & K.kHitagFree) != 0 || (f.StatNum != K.kStatFX && f.StatNum != K.kStatThing))
                    continue;
                if (f.Owner == src || before.Contains(f) || SpriteBSector(f) != sect)
                    continue;
                GibPropagateAppearance(src, fromSrc ? src : e, f);
                f.Owner = src;
            }
        }
        if (src.Data4 > 0)
            Sound3DAt(e.Origin, src.Data4);
        if (src.DropMsg > 0)
            ActDropObject(src, src.DropMsg);
        if (remove)
            ActPostSprite(src, K.kStatFree);
    }
}
