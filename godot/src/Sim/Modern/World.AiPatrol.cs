using System;
using System.Collections.Generic;
using Chosen.Data;
using Chosen.Sim.Modern;

namespace Chosen.Sim;

/// <summary>
/// Patrolling dudes (NBlood's aipatrol.cpp; a modern map's dudes with the patrol flag, dudeFlag4): a dude walks from
/// path marker to path marker (data1 a marker's id, data2 the next one's; back along the path where it ends with -2),
/// waits at a marker for its wait time (turning about, or walking around within its radius: guard mode), sends the
/// marker's command when it arrives and when it leaves, crouches where a marker says so. Meanwhile it looks out for
/// players: touching one, hearing sounds made by one's things, seeing one (in stealth mode: a spot value built up by
/// what it hears and sees, the stealth regions changing the chances) ends the patrol with that player its target, and
/// an alarmed dude calls the others in sight.
/// </summary>
public sealed partial class World
{
    public const int kAiStatePatrolBase = 9, kAiStatePatrolIdle = 9, kAiStatePatrolMove = 10, kAiStatePatrolTurn = 11,
        kAiStatePatrolGuard = 12, kAiStatePatrolMax = 13;

    const int kPatrolAlarmSeeDist = 10000, kMaxPatrolVelocity = 500000, kMinPatrolTurnDelay = 8, kPatrolTurnDelayRange = 20,
        kMaxPatrolSpotValue = 512, kPatrolMoveForward = 0, kPatrolMoveBackward = 1;
    /// <summary>kMaxPatrolSpotValue: a patrolling dude's data3 at which it has spotted the player.</summary>
    public const int PatrolSpotMax = kMaxPatrolSpotValue;
    const int kDudeFlagStealth = 0x0001, kDudeFlagIgnoreTouch = 0x0002, kDudeFlagCrouch = 0x0004;

    AiState[] _patrolStates;

    AiState[] PatrolStates => _patrolStates ??= new[]
    {
        new AiState("patrolIdle") { Type = kAiStatePatrolIdle, Seq = 0, Enter = AiPatrolMoveStop, Think = AiPatrolThink },
        new AiState("patrolMove") { Type = kAiStatePatrolMove, Seq = 0, MoveFn = AiPatrolMove, Think = AiPatrolThink },
        new AiState("patrolTurn") { Type = kAiStatePatrolTurn, Seq = 0, Enter = AiPatrolEnterTurn, MoveFn = AiPatrolMove, Think = AiPatrolThink },
        new AiState("patrolGuard") { Type = kAiStatePatrolGuard, Seq = 0, MoveFn = AiPatrolMove, Think = AiPatrolThink },
    };

    static bool AiInPatrolState(AiState st) => st != null && st.Type >= kAiStatePatrolBase && st.Type < kAiStatePatrolMax;
    static bool IsPathMarker(Sprite s) => s != null && !s.Removed && s.Type == K.kMarkerPath;

    // the sounds playing (NBlood's BonkleCache as far as patrolling asks): what, where, how loud, until when
    readonly List<(int Id, XObject Src, double X, double Y, int Sect, int Vol, int Until)> _heardSounds = new();

    /// <summary>A sound a patrolling dude may hear, kept while it plays (about its length: the game doesn't know it).</summary>
    void PatrolNoteSound(XObject src, double x, double y, int sect, int soundId, int volume)
    {
        if (!IsModern || soundId <= 0)
            return;
        _heardSounds.RemoveAll(s => s.Until <= FrameClock);
        if (_heardSounds.Count >= 256)
            _heardSounds.RemoveAt(0);
        if (volume <= 0)
            volume = SfxData.TryGet(soundId, out var sfx) ? sfx.RelVol : 80;
        _heardSounds.Add((soundId, src, x, y, sect, volume, FrameClock + 120));
    }

    void PatrolReset() => _heardSounds.Clear();

    static PatrolState PatrolOf(Sprite e) => e.Patrol ??= new PatrolState();

    void AiPatrolThink(Sprite e)
    {
        if (!DudeIsAlive(e))
            return;
        if (AiPatrolThinkTarget(e) is { } targ)
        {
            AiPatrolStop(e, targ, e.DudeAmbush != 0);
            return;
        }
        if (e.XTarget is not { } mark || !XSprIsFine(mark))
        {
            AiPatrolStop(e, null, e.DudeAmbush != 0);
            return;
        }
        bool itMovesBack = e.Unused2 == kPatrolMoveBackward;
        int stType = e.AiState.Type;
        int nAng;
        if (stType == kAiStatePatrolTurn)
        {
            if (e.Ang == e.GoalAng)
                AiPatrolState(e, (mark.HitTag & KM.Flag64) != 0 ? kAiStatePatrolGuard : kAiStatePatrolIdle, e.StateTimer);
            return;
        }
        if (stType == kAiStatePatrolGuard)
        {
            // around the marker, within its radius
            int vAng = GetVelocityAngle(e);
            nAng = Blood.GetAngle(mark.BX - e.BX, mark.BY - e.BY);
            if (!PatrolCanMove(e, null, vAng, e.ClipDist << 2))
            {
                AiPatrolState(e, kAiStatePatrolIdle, e.StateTimer);
                return;
            }
            int nDist = Math.Max(mark.ClipDist << 1, 8);
            double x = Blood.Sar(Math.Abs(mark.BX - e.BX), 4), y = Blood.Sar(Math.Abs(mark.BY - e.BY), 4);
            if (Blood.ApproxDist(x, y) < nDist)
            {
                nDist -= 32;
                if (Blood.ApproxDist(x, y) >= nDist && Math.Abs(DAngle(vAng, nAng)) > K.kAng90)
                    AiPatrolState(e, kAiStatePatrolIdle, e.StateTimer);
                return;
            }
        }
        else if (stType == kAiStatePatrolIdle)
        {
            if (e.StateTimer > 0 || mark.Data1 == mark.Data2)
            {
                AiPatrolMoveStop(e);        // (slopes, wind)
                // turning while it waits
                if ((mark.HitTag & KM.Flag16) != 0 && --e.ThinkTime <= 0)
                {
                    e.ThinkTime = kMinPatrolTurnDelay + Random(kPatrolTurnDelayRange);
                    AiPatrolState(e, kAiStatePatrolTurn, e.StateTimer);
                }
                return;
            }
            // leaving: the marker's command
            if (mark.TriggerOff != 0)
            {
                if (mark.TxID != 0)
                    EvSend(mark, mark.TxID, mark.XCommand, e);
                else if (mark.XCommand == KM.kCmdDudeFlagsSet)
                {
                    AiPatrolFlagsMgr(mark, e, true, true);
                    if (e.DudeFlag4 == 0)
                        return;     // no patrol any more
                }
            }
            if (!AiPatrolSetMarker(e))
            {
                AiPatrolStop(e, null);
                return;
            }
            AiPatrolState(e, kAiStatePatrolMove);
        }
        else if (AiPatrolMarkerReached(e))
        {
            mark.IsTriggered = mark.TriggerOnce;        // (once only: no dude takes it again)
            if (mark.WaitTime > 0 || mark.Data1 == mark.Data2)
            {
                // the marker's angle first
                if ((mark.HitTag & KM.Flag4) == 0)
                {
                    nAng = mark.Ang;
                    if (itMovesBack && (mark.HitTag & KM.Flag8) == 0)
                        nAng += K.kAng180;
                    e.GoalAng = nAng & K.kAngMask;
                    if (e.Ang != e.GoalAng)
                        return;     // still turning (in its move animation)
                }
                // arriving: the marker's command
                if (mark.TriggerOn != 0)
                {
                    if (mark.TxID != 0)
                        EvSend(mark, mark.TxID, mark.XCommand, e);
                    else if (mark.XCommand == KM.kCmdDudeFlagsSet)
                    {
                        AiPatrolFlagsMgr(mark, e, true, true);
                        if (e.DudeFlag4 == 0)
                            return;
                    }
                }
                int full = mark.WaitTime * 120 / 10;
                int nWTime = full;
                if ((mark.HitTag & KM.Flag128) != 0)
                    nWTime = Cd.Perc2Val(5, full) + Cd.Perc2Val(Random(95), full);
                int nState = (mark.HitTag & KM.Flag64) != 0 ? kAiStatePatrolGuard : kAiStatePatrolIdle;
                if ((mark.HitTag & KM.Flag16) != 0)
                    e.ThinkTime = kMinPatrolTurnDelay + Random(kPatrolTurnDelayRange);
                AiPatrolSetupCrouchFlag(e, mark.HitTag & KM.Flag3);
                AiPatrolState(e, nState, nWTime);
                return;
            }
            if (mark.TriggerOn != 0 || mark.TriggerOff != 0)
            {
                if (mark.TxID != 0)
                {
                    if (mark.TriggerOn != 0)
                        EvSend(mark, mark.TxID, mark.XCommand, e);
                    if (mark.TriggerOff != 0)
                        EvSend(mark, mark.TxID, mark.XCommand, e);
                }
                else if (mark.XCommand == KM.kCmdDudeFlagsSet)
                {
                    AiPatrolFlagsMgr(mark, e, true, true);
                    if (e.DudeFlag4 == 0)
                        return;
                }
            }
            AiPatrolSetupCrouchFlag(e, mark.HitTag & KM.Flag3);
            if (!AiPatrolSetMarker(e))
            {
                AiPatrolStop(e, null);
                return;
            }
        }
        mark = e.XTarget;
        if (mark == null)
            return;
        AiPatrolSetDirection(e, Blood.GetAngle(mark.BX - e.BX, mark.BY - e.BY));
    }

    /// <summary>aiPatrolThinkTarget: the player the patrolling dude notices now, if any.</summary>
    Sprite AiPatrolThinkTarget(Sprite e)
    {
        bool stealthMode = (e.Unused1 & kDudeFlagStealth) != 0;
        bool canHear = e.DudeDeaf == 0, canSee = e.DudeGuard == 0;
        int sndMax = Math.Max((Skill + 1) >> 1, 1);
        int periphery, seeDist0, hearDist0;
        double eyeAboveZ;
        if (e.Type == KM.kDudeModernCustom && e.Custom is { } cd)
        {
            periphery = Math.Max(cd.Periphery, K.kAng60);
            seeDist0 = stealthMode ? cd.SeeDist / 3 : cd.SeeDist / 2;
            hearDist0 = cd.HearDist;
            eyeAboveZ = cd.EyeHeight;
        }
        else
        {
            periphery = Math.Max(Info.Dude(e.Type, Info.DI_periphery), K.kAng60);
            int see = Info.Dude(e.Type, Info.DI_seeDist);
            seeDist0 = stealthMode ? see / 3 : see / 2;
            hearDist0 = Info.Dude(e.Type, Info.DI_hearDist);
            eyeAboveZ = (Info.Dude(e.Type, Info.DI_eyeHeight) * e.YRepeat) << 2;
        }
        double nSpotDist = seeDist0, nDist = int.MaxValue;
        // the regions that cut the see and hear distances
        foreach (var reg in StatList(KM.kStatModernPatrolRegion))
        {
            if (!XSprIsFine(reg) || (reg.HitTag & KM.Flag64) == 0)
                continue;
            if (reg.XLocked != 0 || (reg.Data2 < 0 && reg.Data3 < 0))
                continue;
            if (reg.Data1 >= 1 && reg.Data1 < nDist)
            {
                nDist = reg.Data1;
                if (Blood.ApproxDist(Blood.Sar(Math.Abs(reg.BX - e.BX), 4), Blood.Sar(Math.Abs(reg.BY - e.BY), 4)) < nDist)
                {
                    if (reg.Data2 >= 0) hearDist0 = reg.Data2 << 4;
                    if (reg.Data3 >= 0) seeDist0 = reg.Data3 << 4;
                    if (hearDist0 > nSpotDist)
                        nSpotDist = hearDist0;
                }
            }
        }
        var heardIds = new List<(int snd, int cnt)>();
        _heardSounds.RemoveAll(s => s.Until <= FrameClock);
        foreach (var play in Players)
        {
            Sprite targ = play;
            if (!XSprIsFine(targ) || targ.XHealth <= 0 || play.NoTarget)
                continue;
            double dx = targ.BX - e.BX, dy = targ.BY - e.BY;
            if ((nDist = Blood.ApproxDist(dx, dy)) > nSpotDist)
                continue;
            var (ztop, _) = Extents(targ);
            if (!CanSee(targ.BX, targ.BY, ztop, e.BX, e.BY, e.BZ - eyeAboveZ, targ, e))
                continue;
            double seeDist = seeDist0, hearDist = hearDist0, feelDist = Blood.Sar(hearDist0, 1);
            bool isInvisible = PowerupCheck(play, K.kPwUpShadowCloak) > 0;
            double seeChance = 0, hearChance = 0;
            if ((e.Unused1 & kDudeFlagIgnoreTouch) == 0 && (SpritesTouching(e, targ) || SpritesTouching(targ, e)))
            {
                if (isInvisible)
                    play.PwUpTime[K.kPwUpShadowCloak] = 0;
                return targ;
            }
            if (canHear)
            {
                for (int j = 0; j < _heardSounds.Count && hearChance < kMaxPatrolSpotValue; j++)
                {
                    var snd = _heardSounds[j];
                    double d = Blood.ApproxDist(snd.X - e.BX, snd.Y - e.BY);
                    if (d > hearDist)
                        continue;
                    // so many of the same sound for one dude (more of them the higher the skill)
                    int k = heardIds.FindIndex(h => h.snd == snd.Id);
                    if (k >= 0)
                    {
                        heardIds[k] = (snd.Id, heardIds[k].cnt + 1);
                        if (heardIds[k].cnt >= sndMax)
                            continue;
                    }
                    else
                        heardIds.Add((snd.Id, 0));
                    int nSect = snd.Sect;
                    if (snd.Src is Sprite ss && !ss.Removed && ss != e && ss.Owner != e)
                        nSect = SpriteBSector(ss);
                    // made by something of the target's in that sector?
                    bool found = false;
                    if (nSect >= 0)
                        foreach (var s in SpritesInB(nSect))
                            if (s.Owner == targ)
                            {
                                found = true;
                                break;
                            }
                    if (found)
                        hearChance += Blood.MulScale8(snd.Vol, Blood.Sar(hearDist - d, 3));
                }
            }
            if (!isInvisible && (canHear || canSee))
            {
                if (stealthMode && play.Posture == Player.PostureCrouch)
                {
                    seeDist -= Cd.Perc2Val(25, (int)seeDist);
                    feelDist -= Cd.Perc2Val(75, (int)feelDist);
                }
                bool hears = canHear, sees = canSee;
                if (hearDist != 0)
                {
                    if (hears)
                        hears = nDist < hearDist || hearChance > 0;
                    if (hears && nDist < feelDist && (targ.XVel != 0 || targ.YVel != 0 || targ.ZVel != 0))
                    {
                        double v = Math.Abs(targ.XVel) + Math.Abs(targ.YVel) + Math.Abs(targ.ZVel);
                        hearChance += Math.Max(Blood.MulScale8(1, Math.Max(Blood.Sar(feelDist - nDist + v, 6), 0)), 0);
                    }
                }
                if (seeDist != 0)
                {
                    int nAng = Math.Abs(DAngle(Blood.GetAngle(dx, dy), e.Ang));
                    if (sees)
                        sees = nDist < seeDist && nAng < periphery;
                    if (sees)
                    {
                        int baseV = 100 + (20 * Skill - nAng / 5);
                        double d = Blood.Sar(nDist, 2);
                        double m = Blood.Int(d * 256 / 0x2000);
                        double t = Blood.MulScale8(d, m);
                        seeChance = t == 0 ? kMaxPatrolSpotValue >> 1 : Blood.ClipRange(Blood.Int(baseV * 256.0 / t), 0, kMaxPatrolSpotValue >> 1);
                    }
                }
                // (NBlood keeps the narrowed senses for the players after this one)
                canHear = hears;
                canSee = sees;
                if (!canHear && !canSee)
                    continue;
                if (stealthMode)
                {
                    // the stealth regions change the chances
                    foreach (var reg in new List<Sprite>(StatList(KM.kStatModernPatrolRegion)))
                    {
                        if (reg.Removed || !reg.HasX || (reg.HitTag & KM.Flag64) != 0 || reg.XLocked != 0)
                            continue;
                        bool fixd = (reg.HitTag & KM.Flag1) != 0, both = (reg.HitTag & KM.Flag4) != 0;
                        bool dude = both || (reg.HitTag & KM.Flag2) != 0, trgt = both || !dude, crch = (reg.HitTag & KM.Flag8) != 0;
                        if (trgt)
                        {
                            if (crch && play.Posture != Player.PostureCrouch)
                                continue;
                            if (reg.Data1 > 0)
                            {
                                if (Blood.ApproxDist(Blood.Sar(Math.Abs(reg.BX - targ.BX), 4), Blood.Sar(Math.Abs(reg.BY - targ.BY), 4)) >= reg.Data1)
                                    continue;
                            }
                            else if (SpriteBSector(targ) != SpriteBSector(reg))
                                continue;
                        }
                        if (dude)
                        {
                            if (reg.Data1 > 0)
                            {
                                if (Blood.ApproxDist(Blood.Sar(Math.Abs(reg.BX - e.BX), 4), Blood.Sar(Math.Abs(reg.BY - e.BY), 4)) >= reg.Data1)
                                    continue;
                            }
                            else if (SpriteBSector(e) != SpriteBSector(reg))
                                continue;
                        }
                        if (hearDist != 0)
                        {
                            if (fixd)
                                hearChance = Math.Max(hearChance, reg.Data2);
                            double mod = Cd.Perc2Val(reg.Data2, (int)hearChance);
                            hearChance = fixd ? mod : hearChance + mod;
                            hearChance = Blood.ClipRange(hearChance, -kMaxPatrolSpotValue, kMaxPatrolSpotValue);
                        }
                        if (seeDist != 0)
                        {
                            if (fixd)
                                seeChance = Math.Max(seeChance, reg.Data3);
                            double mod = Cd.Perc2Val(reg.Data3, (int)seeChance);
                            seeChance = fixd ? mod : seeChance + mod;
                            seeChance = Blood.ClipRange(seeChance, -kMaxPatrolSpotValue, kMaxPatrolSpotValue);
                        }
                        // the region is triggered when the target is about to be spotted
                        if (reg.TxID != 0 && e.Data3 + hearChance + seeChance >= kMaxPatrolSpotValue)
                            TrTriggerSprite(reg, K.kCmdToggle, play);
                    }
                }
                if (hearDist != 0 && hearChance > 0)
                {
                    e.Data3 = (int)Blood.ClipRange(e.Data3 + hearChance, -kMaxPatrolSpotValue, kMaxPatrolSpotValue);
                    if (!stealthMode)
                        return targ;
                }
                if (seeDist != 0 && seeChance > 0)
                {
                    e.Data3 = (int)Blood.ClipRange(e.Data3 + seeChance, -kMaxPatrolSpotValue, kMaxPatrolSpotValue);
                    if (!stealthMode)
                        return targ;
                }
            }
            e.Data3 = Math.Clamp(e.Data3, 0, kMaxPatrolSpotValue);
            if (e.Data3 == kMaxPatrolSpotValue)
                return targ;
        }
        // (NBlood's 16-bit field wraps around after half an hour of this; held at its lowest here)
        e.Data3 = Math.Max(e.Data3 - Math.Max((KM.kPercFull * e.Data3 / kMaxPatrolSpotValue) >> 2, 3), short.MinValue);
        return null;
    }

    void AiPatrolRandGoalAng(Sprite e)
    {
        int[] angArr = { K.kAng45, K.kAng60, K.kAng90 };
        int nAng = angArr[Random(angArr.Length)];
        if (AiPatrolMarkerReached(e) && (e.XTarget.HitTag & KM.Flag64) != 0)
            nAng = -nAng;
        if (nAng > 0 && Chance(0x8000))
            nAng = -nAng;
        e.GoalAng = (e.Ang + nAng) & K.kAngMask;
    }

    void AiPatrolMove(Sprite e)
    {
        if (!DudeIsAlive(e))
            return;
        bool isFlying;
        int nFrontSpeed, nTurnSpeed;
        if (e.Type == KM.kDudeModernCustom && e.Custom is { } d)
        {
            isFlying = d.IsFlying;
            int nPosture = SpriteIsUnderwater(e) ? Cd.PostureW : (e.Unused4 & kDudeFlagCrouch) != 0 ? Cd.PostureC
                : isFlying ? Cd.PostureF : Cd.PostureL;
            nFrontSpeed = d.GetVelocity(nPosture, Cd.VelForward);
            nTurnSpeed = d.GetVelocity(nPosture, Cd.VelTurn);
        }
        else
        {
            isFlying = PatrolExtra(e.Type).flying;
            nFrontSpeed = Info.Dude(e.Type, Info.DI_frontSpeed);
            nTurnSpeed = (Info.Dude(e.Type, Info.DI_angSpeed) << 2) >> 4;
            if ((e.Unused4 & kDudeFlagCrouch) != 0)
            {
                nFrontSpeed >>= 1;
                nTurnSpeed >>= 1;
            }
        }
        int nAng = DAngle(e.GoalAng, e.Ang);
        e.Ang = (e.Ang + Math.Clamp(nAng, -nTurnSpeed, nTurnSpeed)) & K.kAngMask;
        if (e.AiState.Type == kAiStatePatrolTurn || e.XTarget is not { } targ || targ.Removed)
            return;
        bool xfine = XSprIsFine(targ);
        int gAng = K.kAng60;
        if (isFlying || SpriteIsUnderwater(e))
        {
            e.ZVel = (targ.BZ - e.BZ) * 6;
            gAng >>= 1;
        }
        if (Math.Abs(nAng) > gAng)
        {
            AiPatrolMoveStop(e);
            return;
        }
        bool guardMode = e.AiState.Type == kAiStatePatrolGuard;
        if (!guardMode)
        {
            if (AiPatrolMarkerReached(e))
            {
                if (xfine && (targ.WaitTime > 0 || targ.Data1 == targ.Data2))
                {
                    AiPatrolMoveStop(e);
                    return;
                }
                if (isFlying)
                    NnExtFixDudeDrag(e, 128);
            }
        }
        else if (isFlying)
            NnExtFixDudeDrag(e, 128);
        if (xfine && targ.BusyTime > 0)
            nFrontSpeed = Math.Min(nFrontSpeed / 3 + 2500 * targ.BusyTime, 0x47956);
        e.XVel += Blood.MulScale30(nFrontSpeed, Blood.Cos(e.Ang));
        e.YVel += Blood.MulScale30(nFrontSpeed, Blood.Sin(e.Ang));
        if (!guardMode)
        {
            // slower as it nears the marker
            double nVel = Blood.MulScale16(kMaxPatrolVelocity, Blood.ApproxDist(targ.BX - e.BX, targ.BY - e.BY) * 64);
            e.XVel = Blood.ClipRange(e.XVel, -nVel, nVel);
            e.YVel = Blood.ClipRange(e.YVel, -nVel, nVel);
        }
    }

    static void AiPatrolMoveStop(Sprite e)
    {
        e.XVel = e.YVel = 0;
        if ((e.HitTag & K.kPhysGravity) == 0)
            e.ZVel = 0;
    }

    void AiPatrolEnterTurn(Sprite e)
    {
        AiPatrolMoveStop(e);
        AiPatrolRandGoalAng(e);
    }

    static (bool flying, bool melee, int idlg, int mveg, int idlw, int mvew, int idlc, int mvec) PatrolExtra(int type) =>
        type >= K.kDudeBase && type - K.kDudeBase < KM.DudeExtra.Length ? KM.DudeExtra[type - K.kDudeBase]
            : (false, false, -1, -1, -1, -1, -1, -1);

    /// <summary>aiPatrolState: the dude enters a patrol state, with the animation of where it is (on the ground,
    /// crouching, in water).</summary>
    void AiPatrolState(Sprite e, int nState, int nTime = 0)
    {
        int nSeq = -1;
        bool uwater = SpriteIsUnderwater(e);
        bool crouch = (e.Unused4 & kDudeFlagCrouch) != 0 && !uwater;
        if (!crouch)
            e.Unused4 &= ~kDudeFlagCrouch;
        if (e.Type == K.kDudeCultistShotgunProne) e.Type = K.kDudeCultistShotgun;
        else if (e.Type == K.kDudeCultistTommyProne) e.Type = K.kDudeCultistTommy;
        var ex = PatrolExtra(e.Type);
        bool ok = false;
        if (nState == kAiStatePatrolMove || nState == kAiStatePatrolTurn || nState == kAiStatePatrolGuard)
        {
            nSeq = uwater ? ex.mvew : crouch ? ex.mvec : ex.mveg;
            if (nSeq >= 0)
                ok = true;
            else if ((nSeq = ex.mveg) >= 0)
            {
                crouch = uwater = false;
                ok = true;
            }
            else
                nState = kAiStatePatrolIdle;
        }
        if (!ok && nState == kAiStatePatrolIdle)
        {
            nSeq = uwater ? ex.idlw : crouch ? ex.idlc : ex.idlg;
            if (nSeq >= 0)
                ok = true;
            else if ((nSeq = ex.idlg) >= 0)
            {
                crouch = uwater = false;
                ok = true;
            }
            else
                Core.GameConsole.Print($"Dude type {e.Type} unavailable for patrol.");
        }
        if (!ok)
        {
            AiPatrolStop(e, null);
            return;
        }
        if (e.Type == KM.kDudeModernCustom && e.Custom is { } d)
        {
            d.StatusRem(Cd.StatusForceCrouch);
            if (uwater && d.CanSwim()) CdChangePosture(d, Cd.PostureW);
            else if (crouch && d.CanCrouch()) CdChangePosture(d, Cd.PostureC);
            else if (d.CanFly()) CdChangePosture(d, Cd.PostureF);
            else CdChangePosture(d, Cd.PostureL);
            // (for a custom dude the offsets are into its states: 0 idle, 4 search)
            int flat = nSeq + d.Posture;
            nSeq = d.States[flat / Cd.PostureMax, flat % Cd.PostureMax].Seq;
        }
        else if ((e.Type == K.kDudeCultistTesla || e.Type == K.kDudeCultistTNT) && nState == kAiStatePatrolIdle && crouch)
            nSeq = 11537;       // these have no crouching idle animation
        else
            nSeq += Info.Dude(e.Type, Info.DI_seqStartID);
        var st = PatrolStates[nState - kAiStatePatrolBase];
        e.StateTimer = nTime > 0 ? nTime : st.Ticks;
        e.AiState = st;
        if (nSeq > 0 && SeqData.Exists(nSeq))
            Seq.Spawn(this, nSeq, e);
        else
            Seq.Kill(e);
        st.Enter?.Invoke(e);
    }

    /// <summary>aiPatrolMarkerReached: is the dude at its marker (within the marker's radius; flying or swimming:
    /// near its height too)?</summary>
    bool AiPatrolMarkerReached(Sprite e)
    {
        if (e.XTarget is not { } mark || !IsPathMarker(mark))
            return false;
        int okDist = Math.Max(mark.ClipDist << 1, 8);
        double oX = Blood.Sar(Math.Abs(mark.BX - e.BX), 4), oY = Blood.Sar(Math.Abs(mark.BY - e.BY), 4);
        if (Blood.ApproxDist(oX, oY) > okDist)
            return false;
        bool checkZ = SpriteIsUnderwater(e);
        if (!checkZ)
            checkZ = IsCustomDude(e) ? e.Custom != null && e.Custom.IsFlying : PatrolExtra(e.Type).flying;
        if (checkZ)
        {
            okDist = mark.ClipDist << 4;
            var (zt0, zb0) = Extents(e);
            var (zt1, zb1) = Extents(mark);
            if (Blood.Sar(Math.Abs(zb0 - zt1), 6) > okDist && Blood.Sar(Math.Abs(zt0 - zb1), 6) > okDist)
                return false;
        }
        return true;
    }

    /// <summary>aiPatrolMarkerBusy: the living patrol dude (other than one) heading for a marker.</summary>
    Sprite AiPatrolMarkerBusy(Sprite except, Sprite marker)
    {
        foreach (var dude in StatList(K.kStatDude))
            if (dude != except && DudeIsAlive(dude) && dude.DudeFlag4 != 0 && dude.XTarget == marker)
                return dude;
        return null;
    }

    static bool MarkerUsable(Sprite m) => XSprIsFine(m) && m.XLocked == 0 && m.IsTriggered == 0 && m.DudeLockout == 0;

    Sprite MarkerFindNext(Sprite mark, bool back)
    {
        foreach (var n in StatList(K.kStatPathMarker))
        {
            if (n == mark || !MarkerUsable(n))
                continue;
            if (back ? n.Data2 != mark.Data1 : n.Data1 != mark.Data2)
                continue;
            return n;
        }
        return null;
    }

    bool MarkerIsNode(Sprite mark, bool back)
    {
        int c = 0;
        foreach (var n in StatList(K.kStatPathMarker))
        {
            if (n == mark || !MarkerUsable(n))
                continue;
            if (back ? n.Data2 != mark.Data1 : n.Data1 != mark.Data2)
                continue;
            if (++c > 1)
                return true;
        }
        return false;
    }

    /// <summary>aiPatrolSetMarker: the dude's next marker: the nearest one it sees when it has none, else the one the
    /// path goes on with (forward, or back where the path turns), a free one where the path forks.</summary>
    bool AiPatrolSetMarker(Sprite e)
    {
        Sprite next = null, nextDefault = null, prevNew = null;
        var cur = e.XTarget;
        if (cur != null && (cur.Removed || !XSprIsFine(cur) || cur.Type != K.kMarkerPath))
            cur = null;
        if (cur == null)
        {
            double nDist = int.MaxValue;
            foreach (var n in StatList(K.kStatPathMarker))
            {
                if (!MarkerUsable(n))
                    continue;
                double d = Blood.ApproxDist(n.BX - e.BX, n.BY - e.BY);
                if (d > nDist)
                    continue;
                nDist = d;
                if (PatrolCanSee(e, n))
                    next = n;
            }
        }
        else
        {
            var prev = e.Patrol?.PrevMarker;
            if (prev != null && (prev.Removed || prev.Type != K.kMarkerPath || !XSprIsFine(prev)))
                prev = null;
            prevNew = cur;
            bool isNode = MarkerIsNode(cur, false);
            // back or forward
            if (e.Unused2 == kPatrolMoveForward)
                e.Unused2 = cur.Data2 == -2 ? kPatrolMoveBackward : kPatrolMoveForward;
            else
                e.Unused2 = MarkerFindNext(cur, true) != null ? kPatrolMoveBackward : kPatrolMoveForward;
            if (e.Unused2 == kPatrolMoveBackward && Chance(0x8000) && isNode)
                e.Unused2 = kPatrolMoveForward;
            bool isMovesBack = e.Unused2 == kPatrolMoveBackward;
            int nextID = isMovesBack ? cur.Data1 : cur.Data2;
            if (nextID == -1)
                return false;       // the patrol ends here
            foreach (var n in StatList(K.kStatPathMarker))
            {
                if (n == cur || !XSprIsFine(n))
                    continue;
                if (prev != null && isNode && n == prev && cur.Data2 == prev.Data1)
                    continue;
                if (n.XLocked != 0 || n.IsTriggered != 0 || n.DudeLockout != 0)
                    continue;
                if (isMovesBack ? n.Data2 != nextID : n.Data1 != nextID)
                    continue;
                nextDefault ??= n;
                if (AiPatrolMarkerBusy(e, n) != null)
                    continue;
                next = n;
                if (NnExtRandom(0, 5) == 3)
                    break;
            }
            next ??= nextDefault;
            if (next == null)
                Core.GameConsole.Print($"patrol: no next marker with id {nextID} for dude #{e.SprIndex} (back = {isMovesBack})");
        }
        if (next == null)
            return false;
        e.XTarget = next;
        PatrolOf(e).PrevMarker = prevNew;
        return true;
    }

    /// <summary>aiPatrolStop: the patrol ends: with a target the dude goes for it (and may raise the alarm), without
    /// one it stands as it would have started.</summary>
    void AiPatrolStop(Sprite e, Sprite targ, bool alarm = false)
    {
        if (!e.HasX)
            return;
        e.Data3 = 0;                            // the spot progress
        e.Unused4 &= ~kDudeFlagCrouch;
        e.Unused2 = kPatrolMoveForward;
        if (e.Patrol != null)
            e.Patrol.PrevMarker = null;
        e.ThinkTime = 0;
        if (!DudeIsAlive(e))
            return;
        if (e.XTarget is { } mark && mark.Type == K.kMarkerPath)
        {
            if (targ == null)
                e.Ang = mark.Ang & K.kAngMask;
            e.XTarget = null;
        }
        int patrol = e.DudeFlag4;
        e.DudeFlag4 = 0;
        if (targ != null && !targ.Removed && IsDudeSprite(targ) && targ.HasX)
        {
            AiSetTarget(e, targ);
            AiActivateDude(e);
            if (alarm)
                AiPatrolAlarm(e, targ);
        }
        else
        {
            _aiInitNoPatrol = true;
            AiInitSprite(e);
            _aiInitNoPatrol = false;
            AiSetTargetXYZ(e, e.TargetX, e.TargetY, e.TargetZ);
        }
        e.DudeFlag4 = patrol;       // kept: a respawned dude patrols again
    }

    bool _aiInitNoPatrol;

    /// <summary>aiPatrolAlarm: the dudes that see the alarmed one (or its target) go for the target too.</summary>
    void AiPatrolAlarm(Sprite e, Sprite targ)
    {
        if (!DudeIsAlive(e))
            return;
        foreach (var dude in new List<Sprite>(StatList(K.kStatDude)))
        {
            if (dude == e || dude is Player || !DudeIsAlive(dude) || dude.XTarget == targ)
                continue;
            double nDist = Blood.ApproxDist(dude.BX - e.BX, dude.BY - e.BY);
            if (nDist >= kPatrolAlarmSeeDist || !PatrolCanSee(e, dude))
            {
                nDist = Blood.ApproxDist(dude.BX - targ.BX, dude.BY - targ.BY);
                if (nDist >= kPatrolAlarmSeeDist || !PatrolCanSee(targ, dude))
                    continue;
            }
            if (AiInPatrolState(dude.AiState))
                AiPatrolStop(dude, targ, false);
            else
            {
                AiSetTarget(dude, targ);
                AiActivateDude(dude);
            }
        }
    }

    /// <summary>aiPatrolFlagsMgr: a dude's patrol flags taken from another sprite (copy), and the dude started on its
    /// patrol or taken off it as its flags now say (init).</summary>
    void AiPatrolFlagsMgr(Sprite src, Sprite dst, bool copy, bool init)
    {
        if (copy)
        {
            dst.DudeFlag4 = src.DudeFlag4;
            dst.DudeAmbush = src.DudeAmbush;
            dst.DudeGuard = src.DudeGuard;
            dst.DudeDeaf = src.DudeDeaf;
            dst.Unused1 = src.Unused1;
        }
        if (!init || !IsDudeSprite(dst) || dst is Player)
            return;
        if (dst.DudeFlag4 != 0)
        {
            if (AiInPatrolState(dst.AiState))
                return;
            dst.XTarget = null;
            dst.StateTimer = 0;
            if (AiPatrolSetMarker(dst))
            {
                AiPatrolState(dst, kAiStatePatrolIdle);
                dst.Data3 = 0;
            }
            else
                AiPatrolStop(dst, null);
        }
        else if (AiInPatrolState(dst.AiState))
            AiPatrolStop(dst, null);
    }

    /// <summary>aiInitSprite's patrol part: a dude with the patrol flag goes on with the patrol it was on (its marker
    /// kept over the init) or starts a new one.</summary>
    void AiInitPatrol(Sprite e, Sprite target, Sprite prevMarker, int stateTimer, double tx, double ty, double tz)
    {
        if (!IsModern || e.DudeFlag4 == 0 || _aiInitNoPatrol)
            return;
        bool patrol = false;
        if (target != null && !target.Removed)
        {
            if (target.Type == K.kMarkerPath)
            {
                e.XTarget = target;
                PatrolOf(e).PrevMarker = prevMarker;
                e.TargetX = tx;
                e.TargetY = ty;
                e.TargetZ = tz;
                patrol = true;
            }
        }
        else
        {
            stateTimer = 0;
            e.XTarget = null;
            patrol = AiPatrolSetMarker(e);
        }
        if (!patrol)
            return;
        e.Data3 = 0;
        if (stateTimer > 0)
            AiPatrolState(e, kAiStatePatrolIdle, stateTimer);
        else
            AiPatrolState(e, kAiStatePatrolMove);
    }

    /// <summary>The dudes with the patrol flag, one line each (the patrols console command).</summary>
    public IEnumerable<string> PatrolDescribe()
    {
        foreach (var e in Sprites)
        {
            if (e.Removed || e.DudeFlag4 == 0 || !Info.IsDude(e.Type) || e is Player)
                continue;
            var m = e.XTarget;
            yield return $"dude #{e.SprIndex} type {e.Type} at {e.Origin.X:0} {e.Origin.Y:0} {e.Origin.Z:0} sector {SpriteBSector(e)} stat {e.StatNum}"
                + $" state {e.AiState?.Name ?? "none"} timer {e.StateTimer} health {e.XHealth:0} ang {e.Ang} goal {e.GoalAng} spot {e.Data3}"
                + $" flags deaf {e.DudeDeaf} guard {e.DudeGuard} ambush {e.DudeAmbush} stealth {e.Unused1 & 1} dir {e.Unused2}"
                + (m == null ? " marker none" : m.Type == K.kMarkerPath
                    ? $" marker #{m.SprIndex} id {m.Data1}>{m.Data2} at {m.Origin.X:0} {m.Origin.Y:0} wait {m.WaitTime} flags {m.HitTag}"
                    : $" target type {m.Type}")
                + PatrolSenses(e);
        }
    }

    // what a patrolling dude could notice of the first player now (debugging)
    string PatrolSenses(Sprite e)
    {
        if (Me is not { } pl)
            return "";
        bool stealth = (e.Unused1 & kDudeFlagStealth) != 0;
        int see = e.Custom?.SeeDist ?? Info.Dude(e.Type, Info.DI_seeDist), hear = e.Custom?.HearDist ?? Info.Dude(e.Type, Info.DI_hearDist);
        double eye = e.Custom != null ? e.Custom.EyeHeight : (Info.Dude(e.Type, Info.DI_eyeHeight) * e.YRepeat) << 2;
        double dx = pl.BX - e.BX, dy = pl.BY - e.BY;
        var (ztop, _) = Extents(pl);
        bool los = CanSee(pl.BX, pl.BY, ztop, e.BX, e.BY, e.BZ - eye, pl, e);
        return $" | player dist {Blood.ApproxDist(dx, dy):0} see {(stealth ? see / 3 : see / 2)} hear {hear} off {Math.Abs(DAngle(Blood.GetAngle(dx, dy), e.Ang))}"
            + $" periphery {e.Custom?.Periphery ?? Info.Dude(e.Type, Info.DI_periphery)} in sight {los}";
    }

    static bool SpritesTouching(Sprite a, Sprite b) =>
        (a.Hit == Hit.Sprite && a.HitEnt == b) || (a.FlorHit == Hit.Sprite && a.FlorHitEnt == b)
        || (a.CeilHit == Hit.Sprite && a.CeilHitEnt == b);

    // cansee through the sectors' walls only (no room-over-room links), one-way walls blocking
    bool CanSeeNoRor(double x1, double y1, double z1, int sect1, double x2, double y2, double z2, int sect2)
    {
        var b = Board;
        if (!b.Valid(sect1) || !b.Valid(sect2))
            return false;
        if (x1 == x2 && y1 == y2)
            return sect1 == sect2;
        double x21 = x2 - x1, y21 = y2 - y1, z21 = z2 - z1;
        var seen = new HashSet<int> { sect1 };
        var list = new List<int> { sect1 };
        for (int i = 0; i < list.Count; i++)
        {
            int s = list[i];
            foreach (int w in b.SectorWalls(s))
            {
                var wal = b.Walls[w];
                var wal2 = b.Walls[wal.Point2];
                double x31 = wal.X - x1, x34 = wal.X - wal2.X, y31 = wal.Y - y1, y34 = wal.Y - wal2.Y;
                double bot = y21 * x34 - x21 * y34;
                if (bot <= 0)
                    continue;
                double t = y21 * x31 - x21 * y31;
                if (t < 0 || t >= bot)
                    continue;
                t = y31 * x34 - x31 * y34;
                if (t < 0 || t >= bot)
                    continue;
                int nexts = wal.NextSector;
                if (nexts < 0 || (wal.Cstat & 32) != 0)
                    return false;
                t /= bot;
                double x = x1 + x21 * t, y = y1 + y21 * t, z = z1 + z21 * t;
                b.GetZsOfSlope(s, x, y, out double cz, out double fz);
                if (z <= cz || z >= fz)
                    return false;
                b.GetZsOfSlope(nexts, x, y, out cz, out fz);
                if (z <= cz || z >= fz)
                    return false;
                if (seen.Add(nexts))
                    list.Add(nexts);
            }
        }
        return seen.Contains(sect2);
    }

    // (canSee of aipatrol.cpp: the other's heights are taken from the first sprite too, as NBlood has it)
    bool PatrolCanSee(Sprite e, Sprite oth)
    {
        var (zt, zb) = Extents(e);
        double[] za = { zt, e.BZ, zb }, zo = { zt, oth.BZ, zb };
        int s1 = SpriteBSector(e), s2 = SpriteBSector(oth);
        for (int i = 0; i < 3; i++)
            for (int j = 0; j < 3; j++)
                if (CanSeeNoRor(e.BX, e.BY, za[i], s1, oth.BX, oth.BY, zo[j], s2))
                    return true;
        return false;
    }

    /// <summary>canMove of aipatrol.cpp (in place of the dudes' CanMove): the way is free for a range, the place is in
    /// the map, and no sprite the dude touches stands that way.</summary>
    bool PatrolCanMove(Sprite e, Sprite targ, int nAng, double nRange)
    {
        double x = e.BX, y = e.BY, z = e.BZ;
        int hit = HitScan(e, z, Blood.Sar(Blood.Cos(nAng), 16), Blood.Sar(Blood.Sin(nAng), 16), 0, nRange, Sim.Collision.Contents.MaskBlock);
        double hx = hit < 0 ? x + Blood.MulScale30(nRange * 2, Blood.Cos(nAng)) : HitX;
        double hy = hit < 0 ? y + Blood.MulScale30(nRange * 2, Blood.Sin(nAng)) : HitY;
        double nDist = Blood.ApproxDist(x - hx, y - hy);
        if (nDist - (e.ClipDist << 2) < nRange)
        {
            if (hit != 3 || targ == null || HitEnt != targ)
                return false;
        }
        x += Blood.MulScale30(nRange, Blood.Cos(nAng));
        y += Blood.MulScale30(nRange, Blood.Sin(nAng));
        if (FindSectorB(x, y, z, SpriteBSector(e)) < 0)
            return false;
        if (e.Hit == Hit.Sprite && e.HitEnt is Sprite hs)
        {
            int tAng = Blood.GetAngle(hs.BX - e.BX, hs.BY - e.BY);
            if (Math.Abs(DAngle(nAng, tAng)) <= K.kAng45)
                return false;
        }
        return true;
    }

    /// <summary>aiPatrolSetDirection (in place of aiChooseDirection).</summary>
    void AiPatrolSetDirection(Sprite e, int nAng)
    {
        double t1 = Math.Floor((e.XVel * Blood.Cos(e.Ang) + e.YVel * Blood.Sin(e.Ang)) / 1073741824);
        int a = DAngle(nAng, e.Ang), v8 = a < 0 ? -K.kAng60 : K.kAng60;
        double vsi = Blood.Int(Blood.Sar(t1 * 15, 12) / 2);
        var targ = e.XTarget;
        if (PatrolCanMove(e, targ, e.Ang + a, vsi)) e.GoalAng = e.Ang + a;
        else if (PatrolCanMove(e, targ, e.Ang + a / 2, vsi)) e.GoalAng = e.Ang + a / 2;
        else if (PatrolCanMove(e, targ, e.Ang - a / 2, vsi)) e.GoalAng = e.Ang - a / 2;
        else if (PatrolCanMove(e, targ, e.Ang + v8, vsi)) e.GoalAng = e.Ang + v8;
        else if (PatrolCanMove(e, targ, e.Ang, vsi)) e.GoalAng = e.Ang;
        else if (PatrolCanMove(e, targ, e.Ang - v8, vsi)) e.GoalAng = e.Ang - v8;
        else e.GoalAng = e.Ang + K.kAng60;
    }

    void AiPatrolSetupCrouchFlag(Sprite e, int nMask)
    {
        if (nMask == KM.Flag3) e.Unused4 ^= kDudeFlagCrouch;
        else if (nMask == KM.Flag2) e.Unused4 &= ~kDudeFlagCrouch;
        else if (nMask == KM.Flag1) e.Unused4 |= kDudeFlagCrouch;
        else return;
        if ((e.Unused4 & kDudeFlagCrouch) == 0)
            return;
        if (IsCustomDude(e))
        {
            if (e.Custom != null && e.Custom.CanCrouch())
                return;
        }
        else
        {
            var ex = PatrolExtra(e.Type);
            if (ex.idlc >= 0 && ex.mvec >= 0)
                return;
        }
        e.Unused4 &= ~kDudeFlagCrouch;
    }
}
