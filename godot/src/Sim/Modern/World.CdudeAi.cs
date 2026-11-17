using System;
using System.Collections.Generic;
using Chosen.Data;
using Chosen.Sim.Modern;
using Godot;

namespace Chosen.Sim;

/// <summary>
/// The custom dudes' AI (NBlood's aicdud.cpp): the states every custom dude gets (idle, search, dodge, chase, flee,
/// recoils, burning, morphing, knockout, sleep, wake, deaths, one attack state per weapon), what it does in them
/// (looking for a target, chasing it and picking a weapon, fleeing, flying up and down to its target, dodging) and its
/// weapons' shots: a vector, a missile, a thrown thing, a summoned dude, blowing itself up, the special moves (the
/// beast's stomp, ramming, teleporting), turning into another dude, dealing damage or healing from afar. Also the
/// dudes made by a custom dude spawner, the enemy life leech and the effects an appearance names.
/// </summary>
public sealed partial class World
{
    /// <summary>The settings of each kind of version 2 dude met in the level (by descriptor id): the others copy them.</summary>
    public readonly Dictionary<(int ver, int id), CustomDude> CdTemplates = new();
    // the sprites version 1 dudes turn into (waiting on the inactive list): a dude's NextDude numbers one
    readonly List<Sprite> _cdIncarnations = new();
    AiState _cdStateDeath;
    /// <summary>A hitscan's reach for the vector fired next (a custom dude's weapon's; below 0: the vector's own).</summary>
    public double VectorRangeOverride = -1;

    const int CdAttackOk = 0, CdAttackSprite = 1, CdAttackFail = -1, CdAttackContinue = -2, CdAttackBreakAll = -3;

    void CdReset()
    {
        CdTemplates.Clear();
        _cdIncarnations.Clear();
        CustomDudeSetup.ForgetDescriptors();
    }

    public int SpriteMassOf(Sprite e) => GetSpriteMassBySize(e);

    static double DivScale(double a, double b, int bits) => b == 0 ? 0 : Blood.Int(a * (1L << bits) / b);

    // ---------------------------------------------------------------- the states

    /// <summary>The dude's states as NBlood's templates give them (gCdudeStateTemplate, the dying and attack templates):
    /// the default animations are the cultist's (the gargoyle's when flying).</summary>
    public void CdInitStates(CustomDude d)
    {
        _cdStateDeath ??= new AiState("cdudeDeath") { Type = AiState.Other, Seq = -1, Enter = CdEnterDeath };
        const int C = Cd.DefaultSeq, G = Cd.DefaultSeqF;
        void Set(int state, int type, int s0, int s1, int s2, int s3, int ticks, Action<Sprite> enter, Action<Sprite> move,
            Action<Sprite> think, int next)
        {
            int[] seq = { C + s0, C + s1, C + s2, G + s3 };
            for (int j = 0; j < Cd.PostureMax; j++)
            {
                var st = d.States[state, j];
                st.Type = type;
                st.Seq = seq[j];
                st.SeqCallback = null;
                st.Ticks = ticks;
                st.Enter = enter;
                st.MoveFn = move;
                st.Think = think;
                st.Next = next >= 0 ? d.States[next, j] : null;
            }
        }
        Set(Cd.StateIdle, AiState.Idle, 0, 17, 13, 0, 0, CdEnterIdle, null, CdThinkTarget, -1);
        Set(Cd.StateSearch, AiState.Search, 9, 14, 13, 0, 800, CdEnterSearch, CdMoveNormal, CdThinkSearch, Cd.StateIdle);
        Set(Cd.StateDodge, AiState.Move, 9, 14, 13, 0, 90, null, CdMoveDodge, null, Cd.StateChase);
        d.States[Cd.StateDodge, Cd.PostureF].Enter = CdEnterDodgeFly;
        Set(Cd.StateChase, AiState.Chase, 9, 14, 13, 0, 30, CdEnterChase, CdMoveNormal, CdThinkChase, -1);
        Set(Cd.StateFlee, AiState.Move, 9, 14, 13, 0, 256, CdEnterFlee, CdMoveNormal, CdThinkFlee, Cd.StateSearch);
        Set(Cd.StateRecoil, AiState.Recoil, 5, 5, 5, 5, 0, null, null, null, Cd.StateChase);
        Set(Cd.StateRecoilT, AiState.Recoil, 4, 4, 4, 4, 0, null, null, null, Cd.StateChase);
        Set(Cd.BurnStateSearch, AiState.Search, 3, 3, 3, 3, 3600, CdEnterSearchBurn, CdMoveNormal, null, Cd.BurnStateSearch);
        // (morphing thinks in its move function: every frame)
        Set(Cd.StateMorph, AiState.Other, 18, 18, 18, 0, 0, CdEnterMorph, CdThinkMorph, null, -1);
        Set(Cd.StateKnockEnter, AiState.Knockout, 0, 0, 0, 0, 0, CdEnterStartKnock, null, null, Cd.StateKnock);
        d.States[Cd.StateKnockEnter, Cd.PostureW].MoveFn = CdMoveKnockout;
        Set(Cd.StateKnock, AiState.Knockout, 0, 0, 0, 0, 0, CdEnterKnock, null, CdThinkKnock, Cd.StateKnockExit);
        d.States[Cd.StateKnock, Cd.PostureW].MoveFn = CdMoveKnockout;
        Set(Cd.StateKnockExit, AiState.Knockout, 0, 0, 0, 0, 0, CdExitKnock, CdTurnToTarget, null, Cd.StateSearch);
        Set(Cd.StateSleep, AiState.Idle, 0, 0, 0, 0, 0, CdEnterSleep, null, CdThinkTarget, -1);
        Set(Cd.StateWake, AiState.Idle, 0, 0, 0, 0, 0, CdEnterWake, CdTurnToTarget, null, Cd.StateSearch);
        Set(Cd.StateGenIdle, AiState.GenIdle, 0, 17, 13, 0, 0, CdResetTarget, null, null, -1);
        for (int i = Cd.StateDeathBase; i < Cd.StateDeathMax; i++)
        {
            Set(i, AiState.Other, 1, 1, 1, 1, 0, CdEnterDying, null, CdThinkDying, -1);
            for (int j = 0; j < Cd.PostureMax; j++)
                d.States[i, j].Next = _cdStateDeath;        // turns the dude into a gib
        }
        for (int i = Cd.StateAttackBase; i < Cd.StateMax; i++)
        {
            // (attacking thinks in its move function too)
            Set(i, AiState.Attack, 6, 8, 8, 6, 0, CdEnterAttack, CdThinkChase, null, -1);
            for (int j = 0; j < Cd.PostureMax; j++)
            {
                d.States[i, j].SeqCallback = CdWeaponShotCallback;
                d.States[i, j].Next = d.States[i, j];
            }
        }
    }

    static void CdResetTarget(Sprite e) => e.XTarget = null;

    static void CdMoveForwardStop(Sprite e) => e.XVel = e.YVel = 0;

    // ---------------------------------------------------------------- targets

    struct CdTargetInfo
    {
        public Sprite Spr;
        public double Dist;
        public int Ang, Dang, Code;
    }

    /// <summary>checkTarget: 1 the dude sees it, 2 it hears it, below 0 neither (and why).</summary>
    int CdCheckTarget(CustomDude d, Sprite target, ref CdTargetInfo o)
    {
        var e = d.Spr;
        if (target == null || target.Removed || !target.HasX)
            return -1;
        if (e.Owner == target || target.XHealth <= 0)
            return -2;
        if (target is Player pl && (PowerupCheck(pl, K.kPwUpShadowCloak) > 0 || pl.NoTarget))
            return -3;
        double x = target.BX, y = target.BY, z = target.BZ;
        double dx = x - e.BX, dy = y - e.BY;
        double nDist = Blood.ApproxDist(dx, dy);
        int nHeigh = d.EyeHeight;
        bool s = nDist < d.SeeDist, h = nDist < d.HearDist;
        if (!s && !h)
            return -4;
        if (d.IsFlipped)
            nHeigh = -nHeigh;
        if (!CanSee(x, y, z, e.BX, e.BY, e.BZ - nHeigh, target, e))
            return -5;
        int nAng = Blood.GetAngle(dx, dy);
        if (s)
        {
            int nDang = Math.Abs(DAngle(nAng, e.Ang));
            if (nDang <= d.Periphery)
            {
                o = new CdTargetInfo { Spr = target, Dist = nDist, Dang = nDang, Ang = nAng, Code = 1 };
                return 1;
            }
        }
        if (h)
        {
            o = new CdTargetInfo { Spr = target, Dist = nDist, Dang = 0, Ang = nAng, Code = 2 };
            return 2;
        }
        return -255;
    }

    /// <summary>thinkTarget: the nearest player seen or heard (a cruel dude awake: an innocent too) becomes the target.</summary>
    void CdThinkTarget(Sprite e)
    {
        if (e.Custom is not { } d)
            return;
        var target = new CdTargetInfo { Dist = double.MaxValue, Code = -255 };
        var tmp = new CdTargetInfo();
        if (!Chance(Info.Dude(KM.kDudeModernCustom, Info.DI_alertChance)))
            return;
        foreach (var pl in Players)
            if (CdCheckTarget(d, pl, ref tmp) > 0 && tmp.Dist < target.Dist)
                target = tmp;
        if (target.Code < 0 && e.XState != 0 && d.StatusTest(Cd.StatusCruel))
        {
            foreach (var t in StatList(K.kStatDude))
            {
                if (t == e)
                    continue;
                if (t.Type == KM.kDudeModernCustom)
                {
                    var td = t.Custom;
                    if (td == null || !(td.StatusTest(Cd.StatusInnocent)
                        || (td.NumWeapons == 1 && td.Weapon != null && td.Weapon.Type == Cd.WeaponNone)))
                        continue;
                }
                else if (t.Type != K.kDudeInnocent)
                    continue;
                if (CdCheckTarget(d, t, ref tmp) > 0 && tmp.Dist < target.Dist)
                    target = tmp;
            }
        }
        if (target.Code > 0)
        {
            var t = target.Spr;
            if (!d.IsBurning && (e.XTarget != t || Chance(0x0400)))
                d.Sound[Cd.SndTargetSpot].Play(this, e);
            e.GoalAng = target.Ang & K.kAngMask;
            if (target.Code == 1) AiSetTarget(e, t);
            else AiSetTargetXYZ(e, t.BX, t.BY, t.BZ);
            AiActivateDude(e);
        }
    }

    // ---------------------------------------------------------------- entering states

    void CdEnterIdle(Sprite e)
    {
        if (e.Custom is not { } d)
            return;
        d.VelocityModID = Cd.VelModNone;
        e.ThinkTime = 0;
        e.DudeActive = 0;
        CdResetTarget(e);
    }

    void CdEnterSearch(Sprite e)
    {
        if (e.Custom is not { } d)
            return;
        d.VelocityModID = Cd.VelModSearch;
        if (!d.CanFly() || d.IsCrouching || d.IsSwimming)
            return;
        if (d.IsFlying)
        {
            e.GoalAng = (e.GoalAng + Random2(K.kAng45)) & K.kAngMask;
            return;
        }
        // on the ground: it takes off when its target is well above it or out of sight
        double dz = e.TargetZ - e.BZ;
        if (dz >= 0 || Math.Abs(dz) < Cd.Perc2Val(150, d.Height))
        {
            var targ = e.XTarget;
            if (targ == null || CanSee(e.BX, e.BY, e.BZ, targ.BX, targ.BY, targ.BZ, e, targ))
                return;
        }
        e.ZVel = CdGetStartFlyVel(d);
        CdChangePosture(d, Cd.PostureF);
        d.TimerFLaunch.Set(this);
    }

    void CdEnterChase(Sprite e)
    {
        if (e.Custom is { } d)
            d.VelocityModID = Cd.VelModChase;
    }

    void CdEnterFlee(Sprite e)
    {
        if (e.Custom is not { } d)
            return;
        var wp = d.Weapon;
        d.VelocityModID = Cd.VelModFlee;
        if (wp != null && wp.Type == Cd.WeaponNone && wp.Available)
        {
            // the weapon "none" is fleeing: its ammo, cooldown and time
            wp.AmmoDec();
            if (wp.CooldownCheck(this) != 0)
                wp.Available = false;
            int ticks = d.States[wp.StateID, Cd.PostureL].Ticks;
            if (ticks == 0)
                ticks = 256;
            if (e.AiState != null)
                e.AiState.Ticks = ticks;
            e.StateTimer = ticks;
        }
        CdThinkFlee(e);
    }

    void CdEnterSearchBurn(Sprite e)
    {
        if (e.Custom is not { } d)
            return;
        d.VelocityModID = Cd.VelModBurn;
        if (e.AiState != null)
            e.AiState.Think = Chance(0x8000) ? CdThinkSearch : null;
    }

    void CdEnterDodgeFly(Sprite e)
    {
        if (e.Custom is { } d)
            d.DodgeZDir = Chance(0x8000) ? 1 : 0;
    }

    void CdEnterAttack(Sprite e)
    {
        if (e.Custom is not { } d)
            return;
        var wp = d.Weapon;
        if (e.XTarget != null)
            AiSetTarget(e, e.XTarget);
        if (wp == null)
            return;
        if (wp.Locked && e.XLocked == 0)
        {
            // nothing hurts it for the attack's time
            int nTime = e.StateTimer;
            if (nTime <= 0 && e.SeqId >= 0 && SeqData.Exists(e.SeqId))
                nTime = SeqData.TicksPerFrame(e.SeqId) * SeqData.FrameCount(e.SeqId);
            if (nTime > 0)
            {
                e.XLocked = 1;
                EvPost(e, nTime, K.kCmdUnlock, null);
            }
        }
        wp.AttackSound.Play(this, e);
        if (!wp.Inertia)
        {
            CdMoveForwardStop(e);
            if (d.IsFlying)
                e.ZVel = 0;
        }
        else if (d.IsFlying)
        {
            var (zt, zb) = Extents(e);
            int flyHg = CdGetMaxFlyHeigh(d, d.FlyClipDist != 0);
            int nVel = Cd.Perc2Val(10, d.GetVelocity(Cd.VelZ));
            double fz = FloorZOf(e);
            double tDist = Math.Abs(zt - fz - flyHg), bDist = Math.Abs(fz - zb);
            if (Chance(0x8000) && bDist > d.FlyCfDist) e.ZVel = nVel;
            else if (tDist > d.FlyCfDist) e.ZVel = -nVel;
            else e.ZVel = 0;
        }
    }

    void CdEnterStartKnock(Sprite e)
    {
        if (e.Custom is not { } d)
            return;
        d.StatusSet(Cd.StatusKnocked);
        if (d.IsLastStand)
        {
            e.XLocked = 1;
            int nSeq = d.LStandAnim[0, d.Posture];
            if (nSeq > 0)
                Seq.Spawn(this, nSeq, e, e.AiState?.SeqCallback);
        }
        if (d.IsFlying)
            e.HitTag |= K.kPhysGravity | K.kPhysFalling;
    }

    void CdEnterKnock(Sprite e)
    {
        if (e.Custom is not { } d || !d.IsLastStand)
            return;
        e.XLocked = 0;
        int nSeq = d.LStandAnim[1, d.Posture];
        if (nSeq > 0)
            Seq.Spawn(this, nSeq, e, e.AiState?.SeqCallback);
    }

    void CdExitKnock(Sprite e)
    {
        if (e.Custom is not { } d)
            return;
        d.StatusRem(Cd.StatusKnocked);
        if (d.IsFlying)
        {
            e.HitTag &= ~(K.kPhysGravity | K.kPhysFalling);
            if (e.ZVel == 0)
                e.ZVel = -0x100;
        }
        if (!d.IsLastStand)
            return;
        if (Chance(d.LStandWakeChance))
        {
            if (d.LStandWakeHealth != 0)
                e.XHealth = Math.Min(Cd.Perc2Val(d.LStandWakeHealth, NnExtGetStartHealth(e)), 65535);
            int nSeq = d.LStandAnim[2, d.Posture];
            if (nSeq > 0)
                Seq.Spawn(this, nSeq, e, e.AiState?.SeqCallback);
            d.Sound[Cd.SndLStandWake].Play(this, e);
        }
        else
            CdKill(d, e, d.IsUnderwater ? K.kDamageDrown : K.kDamageFall, e.XHealth * 16);
        d.StatusRem(Cd.StatusLStand);
    }

    void CdEnterSleep(Sprite e)
    {
        if (e.Custom is not { } d)
            return;
        d.StatusSet(Cd.StatusSleep);
        CdMoveForwardStop(e);
        CdResetTarget(e);
        // asleep it notices less
        d.SeeDist = d.SleepDist;
        d.HearDist = d.SleepDist >> 1;
        d.Periphery = K.kAng360;
    }

    void CdEnterWake(Sprite e)
    {
        if (e.Custom is not { } d)
            return;
        if (d.StatusTest(Cd.StatusSleep))
        {
            d.StatusRem(Cd.StatusSleep);
            if (CdTemplates.TryGetValue((2, e.Data1), out var model))
            {
                d.SeeDist = model.SeeDist;
                d.HearDist = model.HearDist;
                d.Periphery = model.Periphery;
            }
            else
            {
                d.SeeDist = Info.Dude(KM.kDudeModernCustom, Info.DI_seeDist);
                d.HearDist = Info.Dude(KM.kDudeModernCustom, Info.DI_hearDist);
                d.Periphery = Info.Dude(KM.kDudeModernCustom, Info.DI_periphery);
            }
        }
        d.Sound[Cd.SndWake].Play(this, e);
    }

    void CdEnterDying(Sprite e)
    {
        if (e.Custom is { } d && d.Mass > 48)
            d.Mass = Math.Max(d.Mass >> 2, 48);
    }

    void CdThinkDying(Sprite e)
    {
        if (e.FlorHit == 0 && SpriteIsUnderwater(e, true))
            e.ZVel = Math.Max(e.ZVel, 0x40000);
    }

    /// <summary>enterDeath: the dead dude becomes its gibs' thing (one waiting to respawn keeps its data).</summary>
    void CdEnterDeath(Sprite e)
    {
        if ((e.HitTag & K.kHitagRespawn) == 0)
            DudeToGibCallback1(e);
        e.Type = K.kThingBloodChunks;
        ActPostSprite(e, K.kStatThing);
    }

    void CdEnterMorph(Sprite e)
    {
        if (e.Custom is not { } d || d.IsMorphing)
            return;
        d.Sound[Cd.SndTransforming].Play(this, e);
        d.StatusSet(Cd.StatusMorph);
        e.XLocked = 1;      // nothing hurts it meanwhile
        e.HitTag &= ~K.kPhysMove;
        CdMoveForwardStop(e);
        if (e.AiState != null && e.AiState.Seq <= 0)
            Seq.Kill(e);
    }

    /// <summary>thinkMorph: its animation over, the dude turns into its next form: a waiting dude with its TX ID
    /// (version 1), a vanilla dude or another custom dude.</summary>
    void CdThinkMorph(Sprite e)
    {
        if (e.Custom is not { } d)
            return;
        if (d.SeqPlaying)
        {
            CdMoveForwardStop(e);
            return;
        }
        int nextDude = d.NextDude;
        CdClearEffectCallbacks(d);
        d.StatusRem(Cd.StatusMorph);
        e.BurnSource = null;
        e.BurnTime = 0;
        e.XLocked = 0;
        e.XScale = 0;
        if (nextDude >= 0 && nextDude < _cdIncarnations.Count && nextDude < CustomDudeSetup.MorphCdudeBase)
        {
            var n = _cdIncarnations[nextDude];
            e.XKey = e.DropMsg = 0;
            // the incarnation mustn't send its commands; the dude's death does
            int trOn = n.TriggerOn, trOff = n.TriggerOff;
            n.TriggerOn = n.TriggerOff = 0;
            TrTriggerSprite(e, K.kCmdOff, e);
            e.Type = e.InitType = n.Type;
            e.HitTag = n.HitTag;
            e.Pal = n.Pal;
            e.Shade = n.Shade;
            e.ClipDist = n.ClipDist;
            e.XRepeat = n.XRepeat;
            e.YRepeat = n.YRepeat;
            e.TxID = n.TxID;
            e.XCommand = n.XCommand;
            e.TriggerOn = trOn;
            e.TriggerOff = trOff;
            e.BusyTime = n.BusyTime;
            e.WaitTime = n.WaitTime;
            e.XRespawn = n.XRespawn;
            e.RespawnPending = n.RespawnPending;
            e.Data1 = n.Data1;
            e.Data2 = n.Data2;
            e.Data3 = e.SysData1 = n.SysData1;
            e.Data4 = e.SysData2 = n.SysData2;
            e.DudeGuard = n.DudeGuard;
            e.DudeDeaf = n.DudeDeaf;
            e.DudeAmbush = n.DudeAmbush;
            e.DudeFlag4 = n.DudeFlag4;
            e.Unused1 = n.Unused1;
            e.DropMsg = n.DropMsg;
            e.XKey = n.XKey;
            e.Decoupled = n.Decoupled;
            e.XLocked = n.XLocked;
            e.XHealth = NnExtDudeStartHealth(e, e.Data4);
            n.TriggerOn = trOn;
            n.TriggerOff = trOff;
        }
        else
        {
            if (d.MorphTrDeath)
                TrTriggerSprite(e, K.kCmdOff, e);
            if (nextDude >= CustomDudeSetup.MorphCdudeBase)
                e.Data1 = nextDude - CustomDudeSetup.MorphCdudeBase;
            else if (nextDude < -1)
            {
                e.Type = -nextDude - 1;
                e.ClipDist = Info.Dude(e.Type, Info.DI_clipdist);
                e.Data1 = 0;
            }
            e.InitType = e.Type;
            e.XHealth = NnExtDudeStartHealth(e, 0);
            e.Data4 = e.SysData2 = 0;
            e.Data2 = 0;
            e.Data3 = 0;
        }
        var target = e.XTarget;
        CdBecome(e);
        if (e.Type == K.kDudePodMother || e.Type == K.kDudeTentacleMother || e.DudeFlag4 != 0)
            return;
        if (target != null && !target.Removed) AiSetTarget(e, target);
        else AiSetTargetXYZ(e, e.BX, e.BY, e.BZ);
        AiActivateDude(e);
    }

    /// <summary>The sprite starts over as the dude its type and data now say (aiInitSprite with the old dude forgotten).</summary>
    void CdBecome(Sprite e)
    {
        e.Custom = null;
        e.ClassName = ClassOfType(e.Type);
        if (e.Type != KM.kDudeModernCustom && SeqData.Exists(Info.Dude(e.Type, Info.DI_seqStartID)))
            Seq.Spawn(this, Info.Dude(e.Type, Info.DI_seqStartID), e);
        AiInitSprite(e);
        e.Visible = (e.Cstat & Cstat.Invisible) == 0;
        UpdateBox(e);
    }

    // ---------------------------------------------------------------- thinking

    void CdThinkSearch(Sprite e)
    {
        if (e.Custom is not { } d)
            return;
        if (d.TimerMoveDir.Pass(this))
            AiChooseDirection(e, e.GoalAng);
        CdThinkTarget(e);
    }

    void CdThinkFlee(Sprite e)
    {
        int nAng = Blood.GetAngle(e.BX - e.TargetX, e.BY - e.TargetY);
        int nDang = Math.Abs(DAngle(nAng, e.Ang));
        if (nDang > K.kAng45)
            e.GoalAng = (nAng + K.kAng15 * Random2(2)) & K.kAngMask;
        AiChooseDirection(e, e.GoalAng);
    }

    void CdThinkKnock(Sprite e)
    {
        if (e.Custom is { } d && d.StatusTest(Cd.StatusLStand) && Chance(0x4000))
            d.Sound[Cd.SndLStand].Play(this, e);
    }

    /// <summary>thinkChase: the target kept in sight, a direction chosen, taking off and landing, and the weapon: one
    /// picked, its line of fire checked, the attack state entered (in an attack: turning, the next shot or the end).</summary>
    void CdThinkChase(Sprite e)
    {
        if (e.Custom is not { } d)
            return;
        var wp = d.Weapon;
        bool thinkTime = d.IsThinkTime(this), turn2target = false, interrupt = false, inAttack = d.IsAttacking;
        int nSlope = 0;
        var target = e.XTarget;
        if (target == null || target.Removed || target.Owner == e || !IsDudeSprite(target) || !XSprIsFine(target))
        {
            CdNewState(d, Cd.StateSearch);
            return;
        }
        void Search()
        {
            if (inAttack) CdNextState(d, Cd.StateSearch);
            else CdNewState(d, Cd.StateSearch);
        }
        if (target.XHealth <= 0)
        {
            if (target is not Player tp || tp.FraggerId == e)
                d.Sound[Cd.SndTargetDead].Play(this, e);
            Search();
            return;
        }
        if (target is Player cloaked && PowerupCheck(cloaked, K.kPwUpShadowCloak) > 0)
        {
            CdNewState(d, Cd.StateSearch);
            return;
        }
        double dx = target.BX - e.BX, dy = target.BY - e.BY, dz = target.BZ - e.BZ;
        if (dx == 0) dx = 1;
        if (dy == 0) dy = 1;
        int nDist = (int)Blood.ApproxDist(dx, dy);
        int nDAng = Math.Abs(DAngle(Blood.GetAngle(dx, dy), e.Ang));
        int nHeigh = d.IsFlipped ? -d.EyeHeight : d.EyeHeight;
        if (nDist > d.SeeDist || !CanSee(target.BX, target.BY, target.BZ, e.BX, e.BY, e.BZ - nHeigh, target, e) || nDAng > d.Periphery)
        {
            Search();
            return;
        }
        if (thinkTime)
        {
            AiSetTarget(e, target);
            if (!inAttack)
            {
                if (d.TimerMoveDir.Pass(this) || !CanMove(e, target, e.Ang, e.ClipDist << 2))
                {
                    AiChooseDirection(e, Blood.GetAngle(dx, dy));
                    d.TimerMoveDir.Set(this);
                }
                if (d.IsFlying)
                {
                    var fly = d.FlyType[Cd.FlyLand];
                    if (d.TimerFLand.Pass(this) && Chance(fly.Chance) && Cd.IRngOk(nDist, fly.Distance[0], fly.Distance[1])
                        && (fly.Distance[2] == 0 || (dz >= -fly.Distance[2] && dz < fly.Distance[2])))
                    {
                        d.TimerFLand.Set(this);
                        if (Chance(0x8000))
                            return;
                    }
                }
                else if (d.CanFly())
                {
                    var fly = d.FlyType[Cd.FlyStart];
                    if (d.TimerFLaunch.Pass(this) && Chance(fly.Chance) && Cd.IRngOk(nDist, fly.Distance[0], fly.Distance[1])
                        && (fly.Distance[2] == 0 || dz <= -fly.Distance[2]))
                    {
                        e.ZVel = CdGetStartFlyVel(d);
                        CdChangePosture(d, Cd.PostureF);
                        d.TimerFLaunch.Set(this);
                        if (Chance(0x8000))
                            return;
                    }
                }
            }
            if (Chance(0x2000))
                d.Sound[Cd.SndTargetChase].Play(this, e);
        }
        if (wp != null)
        {
            nSlope = CdAdjustSlope(d, target, wp.ShotOffZ);
            turn2target = wp.TurnToTarget;
            interrupt = wp.Interruptable;
        }
        var weapData = CdMakePickArg(e, target, nDist, nDAng, nSlope);
        if (inAttack)
        {
            if (turn2target && thinkTime)
            {
                e.GoalAng = CdGetTargetAng(e);
                CdMoveTurn(e);
            }
            if (e.AiState != null && e.AiState.Ticks != 0)
            {
                // an attack on a timer
                if (e.StateTimer == 0)
                {
                    wp = CdPickWeapon(d, weapData);
                    if (wp != null && wp == d.Weapon)
                        CdNewState(d, wp.StateID);
                    else
                        CdNewState(d, Cd.StateChase);
                }
                else if (interrupt)
                {
                    CdPickWeapon(d, weapData);
                    if (wp != null && !wp.Available)
                        CdNewState(d, Cd.StateChase);
                }
                return;
            }
            if (!d.SeqPlaying)
            {
                // its last frame
                wp = CdPickWeapon(d, weapData);
                if (wp == null)
                {
                    CdNewState(d, Cd.StateChase);
                    return;
                }
                d.Weapon = wp;
            }
            else
            {
                if (!interrupt)
                {
                    if (wp == null)
                        CdNextState(d, Cd.StateChase);
                    return;
                }
                CdPickWeapon(d, weapData);
                if (!wp.Available)
                {
                    CdNewState(d, Cd.StateChase);
                    return;
                }
            }
        }
        else
        {
            wp = CdPickWeapon(d, weapData);
            if (wp != null)
                d.Weapon = wp;
        }
        if (wp != null)
        {
            switch (wp.Type)
            {
                case Cd.WeaponNone:
                {
                    int st = d.CanMove() ? Cd.StateFlee : Cd.StateSearch;
                    if (inAttack) CdNextState(d, st);
                    else CdNewState(d, st);
                    return;
                }
                case Cd.WeaponHitscan:
                case Cd.WeaponMissile:
                case Cd.WeaponThrow:
                case Cd.WeaponSpecial:
                    HitScan(e, e.BZ, dx, dy, nSlope, nDist, CdClipMask(wp));
                    if (!(HitType == 3 && HitEnt == target) && !CdAdjustSlopeDist(d, nDist, ref nSlope))
                    {
                        // something in the way
                        bool changePos = true;
                        if (HitType == 3 && HitEnt is Sprite hs)
                        {
                            bool fine = XSprIsFine(hs);
                            if (IsDudeSprite(hs))
                            {
                                if (fine)
                                {
                                    if (hs.XTarget == e)
                                        return;
                                    if (hs.DodgeDir > 0)
                                        e.DodgeDir = -hs.DodgeDir;
                                }
                            }
                            else if (hs.Owner == e)
                            {
                                // its own missiles, things and effects
                                if (!fine || hs.XHealth == 0)
                                    changePos = false;
                            }
                            if (changePos && d.CanMove() && Chance(d.DodgeOnAimMissChance))
                            {
                                CdNewState(d, Cd.StateDodge, 30 * (Random(2) + 1));
                                return;
                            }
                        }
                        if (changePos)
                        {
                            CdNewState(d, Cd.StateChase);
                            return;
                        }
                    }
                    goto default;
                default:
                    CdNewState(d, wp.StateID);
                    if (d.IsAttacking)
                        CdNextState(d, wp.NextStateID);
                    return;
            }
        }
        if (!d.CanMove())
            CdNextState(d, Cd.StateSearch);
    }

    // ---------------------------------------------------------------- moving

    int CdGetTargetAng(Sprite e)
    {
        double x = e.TargetX, y = e.TargetY;
        if (e.XTarget is { } t && !t.Removed)
        {
            x = t.BX;
            y = t.BY;
        }
        return Blood.GetAngle(x - e.BX, y - e.BY);
    }

    void CdTurnToTarget(Sprite e)
    {
        e.Ang = CdGetTargetAng(e);
        e.GoalAng = e.Ang;
    }

    void CdMoveTurn(Sprite e)
    {
        if (e.Custom is not { } d)
            return;
        int nVel = d.GetVelocityBase(d.Posture, Cd.VelTurn);
        int nAng = DAngle(e.GoalAng, e.Ang);
        e.Ang = (e.Ang + Math.Clamp(nAng, -nVel, nVel)) & K.kAngMask;
    }

    void CdMoveDodge(Sprite e)
    {
        if (e.Custom is not { } d)
            return;
        CdMoveTurn(e);
        if (e.DodgeDir == 0 || !d.CanMove())
            return;
        int nVelDodge = d.GetVelocity(Cd.VelDodge);
        var (t1, t2) = VelToLocal(e);
        t2 += e.DodgeDir > 0 ? nVelDodge : -nVelDodge;
        LocalToVel(e, t1, t2);
        if (d.IsFlying && d.DodgeZDir != 0)
        {
            int r = 4 + Random(2);
            int sect = SpriteBSector(e);
            if (sect < 0)
                return;
            Board.GetZsOfSlope(sect, e.BX, e.BY, out double cz, out double fz);
            var (zt, zb) = Extents(e);
            if (Chance(0x8000) && Math.Abs(fz - zb) > d.FlyCfDist) e.ZVel = nVelDodge * r;
            else if (Math.Abs(zt - cz) > d.FlyCfDist) e.ZVel = -(nVelDodge * r);
            else e.ZVel = 0;
        }
    }

    static void CdMoveKnockout(Sprite e)
    {
        double zv = e.ZVel;
        e.ZVel = Blood.ClipRange(zv + Blood.MulScale16(zv, 0x3000), 0x1000, 0x40000);
    }

    /// <summary>clipFlying: the flying dude's z velocity towards its goal height (0: there or stopped at the floor or
    /// ceiling, 1 going down, 2 going up).</summary>
    int CdClipFlying(CustomDude d)
    {
        const int MinHG = 0x1000;
        var e = d.Spr;
        int sect = SpriteBSector(e);
        if (sect < 0)
            return 0;
        double flyVel = d.GetVelocity(Cd.VelZ);
        int flyHg = CdGetMaxFlyHeigh(d, false);
        double curz = e.BZ, goalz = d.GoalZ, zs = Blood.Sar(Math.Abs(e.ZVel), 8);
        int cf = d.FlyCfDist;
        Board.GetZsOfSlope(sect, e.BX, e.BY, out double cz, out double fz);
        var (zt, zb) = Extents(e);
        if (d.FlyBackOnTrackAccel != 0)
        {
            if ((curz < e.TargetZ && Math.Abs(zb - e.TargetZ) > flyHg) || (curz > e.TargetZ && Math.Abs(zt - e.TargetZ) > flyHg))
                flyVel += Cd.Perc2Val(d.FlyBackOnTrackAccel, (int)flyVel);
        }
        if (d.TimerFLand.Pass(this))
        {
            if ((e.ZVel > 0 && zb + zs >= fz - cf) || (e.ZVel < 0 && zt - zs <= cz + cf))
            {
                e.ZVel = Blood.Sar(e.ZVel, 2);
                return 0;
            }
        }
        if (curz < goalz)
        {
            curz += zs;     // above its height
            if (curz < goalz)
            {
                double t = Math.Floor(flyVel * Math.Max(Math.Abs(goalz - curz), MinHG) / 1048576);
                e.ZVel = Math.Min(e.ZVel + t, flyVel);
                return 1;
            }
            e.ZVel = Blood.Sar(e.ZVel, 1);
        }
        else if (curz > goalz)
        {
            curz -= zs;     // below it
            if (curz > goalz)
            {
                double t = Math.Floor(flyVel * Math.Max(Math.Abs(goalz - curz), MinHG) / 1048576);
                e.ZVel = Math.Max(e.ZVel - t, -flyVel);
                return 2;
            }
            e.ZVel = Blood.Sar(e.ZVel, 1);
        }
        return 0;
    }

    void CdMoveNormalF(CustomDude d)
    {
        var e = d.Spr;
        int fwdVel = d.GetVelocity(Cd.VelForward);
        int flyHg = CdGetMaxFlyHeigh(d, d.FlyClipDist != 0);
        int cf = d.FlyCfDist;
        int stat = CdClipFlying(d);
        int sect = SpriteBSector(e);
        if (d.IsThinkTime(this) && sect >= 0)
        {
            // the height it flies at
            Board.GetZsOfSlope(sect, e.BX, e.BY, out double cz, out double fz);
            if (!d.TimerFLand.Pass(this))
                d.GoalZ = (int)Blood.ClipRange(e.TargetZ, cz + cf, fz - cf);
            else if (d.IsSearching && d.TimerGoalZ.Pass(this))
            {
                double z = e.TargetZ > fz ? fz : e.TargetZ;
                d.GoalZ = (int)(z - (flyHg >> 1) - (d.FlyAbsGoalZ ? Random(flyHg >> 1) : Cd.Perc2Val(Random(50), flyHg)));
                d.GoalZ = (int)Blood.ClipRange(d.GoalZ, cz + cf, fz - cf);
                d.TimerGoalZ.Set(this);
            }
            else if (d.TimerGoalZ.Pass(this) && (stat == 0 || !d.FlyMustReach || Math.Abs(e.TargetZ - d.GoalZ) > flyHg))
            {
                d.GoalZ = (int)(e.TargetZ - (d.FlyAbsGoalZ ? Random(flyHg) : Cd.Perc2Val(Random(100), flyHg)));
                d.GoalZ = (int)Blood.ClipRange(d.GoalZ, cz + cf, fz - cf);
                d.TimerGoalZ.Set(this);
            }
        }
        if (d.FlyFriction != 0)
            NnExtFixDudeDrag(e, d.FlyFriction);
        e.XVel += Blood.MulScale30(Blood.Cos(e.Ang), fwdVel);
        e.YVel += Blood.MulScale30(Blood.Sin(e.Ang), fwdVel);
    }

    void CdMoveNormalW(CustomDude d)
    {
        var e = d.Spr;
        double dz = 0;
        if (e.XTarget is { } targ && !targ.Removed)
        {
            if (SpriteIsUnderwater(targ, true))
                dz = targ.BZ - e.BZ + (10 << Random(12));
        }
        else
            dz = e.TargetZ - e.BZ;
        if (Chance(0x0500))
            dz *= 2;
        int fwdVel = d.GetVelocity(Cd.VelForward);
        e.XVel += Blood.MulScale30(Blood.Cos(e.Ang), fwdVel);
        e.YVel += Blood.MulScale30(Blood.Sin(e.Ang), fwdVel);
        e.ZVel += dz;
    }

    void CdMoveNormal(Sprite e)
    {
        if (e.Custom is not { } d)
            return;
        int nAng = DAngle(e.GoalAng, e.Ang);
        CdMoveTurn(e);
        // turning around: no step forward
        if (Math.Abs(nAng) > d.TurnAng)
        {
            if (d.StopMoveOnTurn)
                CdMoveForwardStop(e);
            return;
        }
        if (!d.CanMove())
            return;
        if (d.IsUnderwater) CdMoveNormalW(d);
        else if (d.IsFlying) CdMoveNormalF(d);
        else
        {
            int fwdVel = d.GetVelocity(Cd.VelForward);
            e.XVel += Blood.MulScale30(Blood.Cos(e.Ang), fwdVel);
            e.YVel += Blood.MulScale30(Blood.Sin(e.Ang), fwdVel);
        }
    }

    // ---------------------------------------------------------------- the weapons' shots

    struct CdShotArg
    {
        public double OffX, OffY, OffZ, Dx, Dy, Dz;
        public bool Check;
    }

    Sprite _cdShot;     // the sprite the last shot made

    void NnExtScaleVelocity(Sprite e, double nVel, double dx, double dy, double dz, int which = 3)
    {
        if ((which & 1) != 0)
        {
            e.XVel = Blood.MulScale14(nVel, dx);
            e.YVel = Blood.MulScale14(nVel, dy);
        }
        if ((which & 2) != 0)
            e.ZVel = Blood.MulScale14(nVel, dz);
    }

    void NnExtScaleVelocityRel(Sprite e, double nVel, double dx, double dy, double dz)
    {
        e.XVel += Blood.MulScale14(nVel, dx);
        e.YVel += Blood.MulScale14(nVel, dy);
        e.ZVel += Blood.MulScale14(nVel, dz);
    }

    /// <summary>nnExtOffsetSprite: a sprite just made, moved in its own frame (x right, y forward, z down).</summary>
    void NnExtOffsetSprite(Sprite e, double oX, double oY, double oZ)
    {
        double x = e.BX, y = e.BY, z = e.BZ;
        NnExtOffsetPos(oX, oY, oZ, e.Ang, ref x, ref y, ref z);
        SetBuildPos(e, x, y, z);
        e.PrevOrigin = e.Origin;
        SpriteSectorForget(e);
    }

    /// <summary>nnExtFireMissile: actFireMissile with an offset in the shooter's frame, the shooter's own missiles
    /// not in its way, and never blocking.</summary>
    Sprite NnExtFireMissile(Sprite e, double a2, double a3, double a4, double a5, double a6, int nType)
    {
        int mclip = MissileInfo(nType, K.MI_clipDist);
        double nClip = mclip + e.ClipDist;
        double x = e.BX + Math.Floor(nClip * Blood.Cos(e.Ang) / 268435456);
        double y = e.BY + Math.Floor(nClip * Blood.Sin(e.Ang) / 268435456);
        double z = e.BZ;
        NnExtOffsetPos(a2, 0, a3, e.Ang, ref x, ref y, ref z);
        int nHit = HitScan(e, z, x - e.BX, y - e.BY, 0, nClip, Sim.Collision.Contents.MaskBlock);
        var hitEnt = HitEnt;
        if (nHit >= 0)
        {
            if (nHit == 0 || nHit == 3)
            {
                if (hitEnt is Sprite hs && hs.StatNum == K.kStatProjectile)
                    nHit = -1;
                if (nHit >= 0)
                {
                    x = HitX - Blood.MulScale30(Blood.Cos(e.Ang), 16);
                    y = HitY - Blood.MulScale30(Blood.Sin(e.Ang), 16);
                }
            }
            else
            {
                x = HitX - Math.Floor(mclip * 2 * Blood.Cos(e.Ang) / 268435456);
                y = HitY - Math.Floor(mclip * 2 * Blood.Sin(e.Ang) / 268435456);
            }
        }
        var m = ActSpawnSprite(x, y, z, K.kStatProjectile, true);
        m.ClassName = "missile";
        m.Type = m.InitType = nType;
        m.Shade = MissileInfo(nType, K.MI_shade);
        m.Pal = 0;
        m.ClipDist = mclip;
        m.HitTag = K.kPhysMove;
        m.XRepeat = MissileInfo(nType, K.MI_xrepeat);
        m.YRepeat = MissileInfo(nType, K.MI_yrepeat);
        m.Picnum = MissileInfo(nType, K.MI_picnum);
        m.Ang = (int)Blood.AngMask(e.Ang + MissileInfo(nType, K.MI_angleOfs));
        m.XVel = Blood.MulScale14(MissileInfo(nType, K.MI_velocity), a4);
        m.YVel = Blood.MulScale14(MissileInfo(nType, K.MI_velocity), a5);
        m.ZVel = Blood.MulScale14(MissileInfo(nType, K.MI_velocity), a6);
        ActBuildMissile(m, e);
        m.Owner = e;
        m.Cstat &= ~Cstat.Block;
        m.XTarget = null;
        UpdateBox(m);
        if (m.Body != null)
            m.Body.Enabled = false;
        if (nHit >= 0)
        {
            HitEnt = hitEnt;
            if (nHit == 3)
                HitType = 3;
            ActImpactMissile(m, nHit);
            return null;
        }
        EvPostCallback(m, 600, K.kCallbackRemove);
        return m;
    }

    /// <summary>nnExtSpawnDude(src, type, x, y, z): a dude made at a place, its maker its owner.</summary>
    Sprite NnExtSpawnDudeAt(Sprite src, int nType, double x, double y, double z)
    {
        var s = ActSpawnSpriteFrom(src, K.kStatDude);
        s.Type = s.InitType = nType;
        s.ClassName = ClassOfType(nType);
        s.Ang = src.Ang;
        s.Cstat |= 0x1101;
        s.Owner = src;
        s.ClipDist = Info.Dude(nType, Info.DI_clipdist);
        SetBuildPos(s, x, y, z);
        s.PrevOrigin = s.Origin;
        SpriteSectorForget(s);
        s.XHealth = NnExtDudeStartHealth(s, s.Data4);
        s.XRespawn = 1;
        UpdateBox(s);
        return s;
    }

    int CdShotHitscan(CustomDude d, CdWeapon wp, ref CdShotArg a)
    {
        if (a.Check)
            return CdAttackOk;
        // the vector reaches as far as the weapon does
        VectorRangeOverride = wp.GetDistance();
        try
        {
            ActFireVector(d.Spr, a.OffX, a.OffZ, a.Dx, a.Dy, a.Dz, wp.Id);
        }
        finally
        {
            VectorRangeOverride = -1;
        }
        return CdAttackOk;
    }

    // (CanSee of aicdud.cpp: the target's heights taken from the source too, as NBlood has it)
    bool CdCanSee3(Sprite src, Sprite dst)
    {
        var (zt, zb) = Extents(src);
        double[] zA = { zt, src.BZ, zb }, zB = { zt, dst.BZ, zb };
        for (int i = 0; i < 3; i++)
            if (CanSee(src.BX, src.BY, zA[i], dst.BX, dst.BY, zB[i], src, dst))
                return true;
        return false;
    }

    double PlayerEffectGet(Player p, int nEffect) => nEffect switch
    {
        0 => p.PickupEffect,
        1 => p.PainEffect,
        3 => p.BlindEffect,
        4 => p.TiltEffect,
        5 => p.QuakeEffect,
        6 => p.Visibility,
        7 => p.PwUpTime[K.kPwUpDeliriumShroom],
        8 => p.FlickerEffect,
        9 => p.FlashEffect != 0 ? p.Visibility : 0,
        _ => 0,
    };

    void PlayerEffectSet(Player p, int nEffect, double nTime)
    {
        switch (nEffect)
        {
            case 0: p.PickupEffect = nTime; break;
            case 1: p.PainEffect = nTime; break;
            case 3: p.BlindEffect = nTime; break;
            case 4: p.TiltEffect = nTime; break;
            case 5: p.QuakeEffect = nTime; break;
            case 6: p.Visibility = nTime; break;
            case 7: p.PwUpTime[K.kPwUpDeliriumShroom] = nTime; break;
            case 8: p.FlickerEffect = nTime; break;
            case 9:
                p.Visibility = nTime;
                p.FlashEffect = 1;
                break;
        }
    }

    /// <summary>weaponShotDmgOrHp: damage dealt to the target (or health given to the dude or its target) from where
    /// the dude stands, with a screen effect on a player.</summary>
    int CdShotDmgOrHp(CustomDude d, CdWeapon wp, ref CdShotArg a)
    {
        var e = d.Spr;
        Sprite targ = null;
        bool heal = wp.Type == Cd.WeaponHealth;
        bool targOrig = ((wp.Data5 & 0x001) == 0) ^ heal;
        if (e.XTarget != null && XSprIsFine(e.XTarget))
            targ = e.XTarget;
        else if (targOrig)
            return CdAttackFail;
        bool dst2val = (wp.Data5 & 0x002) == 0, dst2eff = (wp.Data5 & 0x004) != 0, clipVal = (wp.Data5 & 0x008) == 0,
            clipEff = (wp.Data5 & 0x010) == 0, relEff = (wp.Data5 & 0x020) != 0, mustSee = (wp.Data5 & 0x040) == 0,
            mustDist = (wp.Data5 & 0x080) != 0, mustScan = (wp.Data5 & 0x100) != 0, mustVuln = (wp.Data5 & 0x200) != 0 && !heal;
        var orig = targOrig ? targ : e;
        double nBVal = wp.Data1 << 4, nVal = nBVal, nDist = 0;
        if (targOrig)
        {
            nDist = Blood.ApproxDist(e.BX - (e.TargetX + a.Dx), e.BY - (e.TargetY + a.Dy));
            if ((mustSee && !CdCanSee3(e, targ)) || (mustDist && nDist > wp.GetDistance()))
                return CdAttackFail;
            if (mustScan)
            {
                int nSlope = CdAdjustSlope(d, targ, 0);
                HitScan(e, e.BZ, Blood.Sar(Blood.Cos(e.Ang), 16), Blood.Sar(Blood.Sin(e.Ang), 16), nSlope, 0);
                if (!(HitType == 3 && HitEnt == targ))
                    return CdAttackFail;
            }
            if (dst2val)
                nVal = DivScale(nVal, nDist, 12);
        }
        var play = orig as Player;
        if (mustVuln && (NnExtIsImmune(orig, wp.Id, 0) || (play != null && (PowerupCheck(play, K.kPwUpDeathMask) > 0 || play.GodMode))))
            return CdAttackFail;
        if (a.Check)
            return CdAttackOk;
        if (clipVal)
            nVal = Blood.ClipRange(nVal, 0, nBVal);
        if (wp.Data3 > 0 && wp.Data2 >= 1 && wp.Data2 <= 10 && Chance(Cd.Perc2Val(wp.Data4, Cd.ChanceMax)) && targ is Player tp)
        {
            int effectID = wp.Data2 - 1;
            double nBTime = wp.Data3, nTime = nBTime, t = PlayerEffectGet(tp, effectID);
            if (dst2eff)
                nTime = Blood.ClipRange(DivScale(nTime, nDist, 12), -nBTime, nBTime);
            if (relEff)
                nTime = Blood.Int(nTime / NnExtRandom(1, 6)) + t;
            if (t < nTime)
            {
                t = clipEff ? wp.Data3 : 1280;
                PlayerEffectSet(tp, effectID, Blood.ClipRange(nTime, -t, t));
            }
        }
        if (nVal > 0)
        {
            if (heal)
            {
                int nMax = Cd.Perc2Val(targOrig ? wp.TargHpRange[1] : wp.DudeHpRange[1], NnExtGetStartHealth(orig)) + 16;
                if (orig.XHealth < nMax)
                    orig.XHealth = Math.Min(orig.XHealth + nVal, nMax);
            }
            else
                ActDamageSprite(e, orig, wp.Id, nVal);
        }
        wp.ShotAppearance.Set(this, orig, e);
        if (wp.HaveVelocity)
            NnExtScaleVelocity(orig, wp.ShotVelocity, a.Dx, a.Dy, wp.HaveSlope ? a.Dz : 0);
        return CdAttackOk;
    }

    int CdShotMissile(CustomDude d, CdWeapon wp, ref CdShotArg a)
    {
        if (a.Check)
            return CdAttackOk;
        var e = d.Spr;
        var shot = NnExtFireMissile(e, a.OffX, a.OffZ, a.Dx, a.Dy, a.Dz, wp.Id);
        if (shot == null)
            return CdAttackFail;
        NnExtOffsetSprite(shot, 0, a.OffY, 0);
        if (wp.ShotClipdist != 0)
            shot.ClipDist = wp.ShotClipdist;
        if (wp.HaveVelocity)
        {
            shot.XTarget = null;        // (vanilla would set its velocity back)
            NnExtScaleVelocity(shot, wp.ShotVelocity, a.Dx, a.Dy, a.Dz);
        }
        wp.ShotAppearance.Set(this, shot, e);
        if (wp.ShotTargetFollow != 0)
        {
            shot.GoalAng = wp.ShotTargetFollow;
            shot.FollowTarg = e.XTarget;
            shot.XTarget = null;        // it follows by the modern rules
            if (!_followSprites.Contains(shot))
                _followSprites.Add(shot);
        }
        UpdateBox(shot);
        _cdShot = shot;
        return CdAttackSprite;
    }

    static readonly int[] _cdDebrisPics = { 2406, 2280, 2185, 2155, 2620, 3135 };

    int CdShotThing(CustomDude d, CdWeapon wp, ref CdShotArg a)
    {
        if (a.Check)
            return CdAttackOk;
        var e = d.Spr;
        int nTime = 120 * Random(2) + 120;
        double nDist = Blood.ApproxDist(a.Dx, a.Dy);
        double nVel = DivScale(Blood.Int(nDist / 540), 120, 23);
        double tx = e.BX + a.Dx, ty = e.BY + a.Dy;
        int ta = e.Ang;
        double nSlope = wp.HaveSlope ? wp.ShotSlope : Blood.Int(a.Dz / 128) - 12000;
        e.Ang = Blood.GetAngle(e.BX - tx, e.BY - ty);
        var shot = ActFireThing(e, -a.OffX, a.OffZ, nSlope, wp.Id, nVel);
        e.Ang = ta;
        if (shot == null)
            return CdAttackFail;
        NnExtOffsetSprite(shot, 0, a.OffY, 0);
        shot.Picnum = Math.Max(Info.Thing(wp.Id, Info.TI_picnum), 0);
        shot.Owner = e;
        switch (wp.Id)
        {
            case K.kThingArmedProxBomb:
            case KM.kModernThingTNTProx:
                shot.XState = 0;
                goto case K.kThingArmedRemoteBomb;
            case K.kThingArmedRemoteBomb:
            case K.kThingArmedTNTBundle:
            case K.kThingArmedTNTStick:
            case K.kThingTNTBarrel:
                if (wp.Data1 > 0)
                {
                    nTime = wp.Data1;
                    if (wp.Data2 > wp.Data1)
                        nTime += Random(wp.Data2 - wp.Data1);
                }
                break;
            case KM.kModernThingThrowableRock:
                shot.Picnum = _cdDebrisPics[Random(_cdDebrisPics.Length)];
                shot.XRepeat = shot.YRepeat = 24 + Random(42);
                shot.Cstat |= Cstat.Block;
                shot.Pal = 5;
                if (Chance(0x5000)) shot.Cstat |= Cstat.XFlip;
                if (Chance(0x5000)) shot.Cstat |= Cstat.YFlip;
                if (wp.Data1 > 0)
                {
                    shot.Data1 = wp.Data1;
                    if (wp.Data2 > wp.Data1)
                        shot.Data1 += Random(wp.Data2 - wp.Data1);
                }
                else if (shot.XRepeat > 60) shot.Data1 = 43;
                else if (shot.XRepeat > 40) shot.Data1 = 33;
                else if (shot.XRepeat > 30) shot.Data1 = 23;
                else shot.Data1 = 12;
                break;
            case K.kThingBloodBits:
            case K.kThingBloodChunks:
                DudeToGibCallback1(shot);
                break;
            case K.kThingNapalmBall:
                shot.XRepeat = shot.YRepeat = 24;
                if (wp.Data1 > 0)
                {
                    shot.Data4 = wp.Data1;
                    if (wp.Data2 > wp.Data1)
                        shot.Data4 += Random(wp.Data2 - wp.Data1);
                }
                else
                    shot.Data4 = 3 + Random2(2);
                break;
            case KM.kModernThingEnemyLifeLeech:
            case K.kThingDroppedLifeLeech:
                shot.XHealth = ((Info.Thing(wp.Id, Info.TI_startHealth) << 4) * Math.Max(Skill, 1)) >> 1;
                shot.Cstat &= ~Cstat.Block;
                shot.Pal = 6;
                shot.ClipDist = 0;
                shot.Data3 = 512 / (Skill + 1);
                shot.XTarget = e.XTarget;
                shot.XProximity = 1;
                shot.StateTimer = 1;
                EvPostCallback(shot, 80, K.kCallbackLeechStateTimer);
                d.Leech = shot;
                break;
        }
        if (wp.ShotClipdist != 0)
            shot.ClipDist = wp.ShotClipdist;
        if (wp.HaveVelocity)
            NnExtScaleVelocity(shot, -(wp.ShotVelocity << 3), a.Dx, a.Dy, a.Dz, 1);
        wp.ShotAppearance.Set(this, shot, e);
        if (wp.ShotTargetFollow != 0)
        {
            shot.GoalAng = wp.ShotTargetFollow;
            shot.FollowTarg = e.XTarget;
            if (!_followSprites.Contains(shot))
                _followSprites.Add(shot);
        }
        shot.XImpact = wp.ShotImpact ? 1 : 0;
        if (shot.XImpact == 0)
            EvPost(shot, nTime, K.kCmdOn, e);
        NnExtListSprite(shot);
        shot.Visible = (shot.Cstat & Cstat.Invisible) == 0;
        UpdateBox(shot);
        _cdShot = shot;
        return CdAttackSprite;
    }

    /// <summary>posObstructed: is a place outside the map, or (with a radius) a blocking sprite or a living dude there?</summary>
    bool CdPosObstructed(double x, double y, int nRadius)
    {
        int i = Board.SectorCount;
        while (--i >= 0 && !Board.Inside(x, y, i))
        {
        }
        if (i < 0)
            return true;
        if (nRadius == 0)
            return false;
        foreach (var s in Sprites)
        {
            if (s.Removed || (s.HitTag & (K.kHitagFree | K.kHitagRespawn)) != 0 || (s.Cstat & Cstat.AlignMask) != 0)
                continue;
            if ((s.Cstat & Cstat.Block) == 0)
            {
                if (!IsDudeSprite(s) || !DudeIsAlive(s))
                    continue;
            }
            else if (Tiles.W(s.Picnum) <= 0 || Tiles.H(s.Picnum) <= 0)
                continue;
            if (Blood.ApproxDist(s.BX - x, s.BY - y) <= nRadius)
                return true;
        }
        return false;
    }

    int CdShotSummon(CustomDude d, CdWeapon wp, ref CdShotArg a)
    {
        var e = d.Spr;
        var dst = e;
        bool blockCheck = (wp.Data2 & 0x001) == 0, addKill = (wp.Data2 & 0x002) == 0, targetOrigin = (wp.Data2 & 0x004) != 0,
            resetVel = (wp.Data2 & 0x008) != 0;
        if (targetOrigin && e.XTarget != null)
            dst = e.XTarget;
        double x = dst.BX + a.Dx, y = dst.BY + a.Dy, z = dst.BZ + a.Dz;
        int nAng = 0;
        int nRadius = blockCheck ? 32 : 0;
        int nDude = wp.Type == Cd.WeaponSummonCdude ? KM.kDudeModernCustom : wp.Id;
        if (nRadius != 0 && wp.Data3 > 0)
            nRadius = wp.Data3;
        NnExtOffsetPos(a.OffX, 800 + a.OffY, a.OffZ, dst.Ang, ref x, ref y, ref z);
        while (nAng < K.kAng180)
        {
            if (CdPosObstructed(x, y, nRadius))
            {
                // the next place around
                double rx = x - dst.BX, ry = y - dst.BY;
                x = dst.BX + Blood.DMulScale30R(rx, Blood.Cos(nAng), -ry, Blood.Sin(nAng));
                y = dst.BY + Blood.DMulScale30R(rx, Blood.Sin(nAng), ry, Blood.Cos(nAng));
                nAng += K.kAng15;
                continue;
            }
            if (a.Check)
                return CdAttackOk;
            if (nDude < K.kDudeBase || nDude >= K.kDudeMax)
                break;
            var shot = NnExtSpawnDudeAt(dst, nDude, x, y, z);
            if (resetVel)
                shot.XVel = shot.YVel = shot.ZVel = 0;
            if (nDude == KM.kDudeModernCustom)
                shot.Data1 = wp.Id;
            if (wp.ShotClipdist != 0)
                shot.ClipDist = wp.ShotClipdist;
            if (wp.HaveVelocity)
                NnExtScaleVelocity(shot, wp.ShotVelocity, a.Dx, a.Dy, a.Dz);
            if (wp.Data1 != 0)
            {
                int nHealth = Math.Min(wp.Data1, 65535);
                shot.XHealth = Math.Min(nHealth << 4, 65535);
                shot.Data4 = shot.SysData2 = nHealth;
            }
            if (nDude != KM.kDudeModernCustom && SeqData.Exists(Info.Dude(nDude, Info.DI_seqStartID)))
                Seq.Spawn(this, Info.Dude(nDude, Info.DI_seqStartID), shot);
            AiInitSprite(shot);
            wp.ShotAppearance.Set(this, shot, e);
            shot.TargetX = e.TargetX;
            shot.TargetY = e.TargetY;
            shot.TargetZ = e.TargetZ;
            shot.XTarget = e.XTarget;
            shot.Ang = e.Ang;
            shot.Owner = e;
            AiActivateDude(shot);
            d.Slaves.Add(shot);
            if (addKill)
                KillMgrAddCountSprite(shot);
            shot.Visible = (shot.Cstat & Cstat.Invisible) == 0;
            UpdateBox(shot);
            _cdShot = shot;
            return CdAttackSprite;
        }
        return CdAttackFail;
    }

    /// <summary>weaponShotKamikaze: the dude blows itself up.</summary>
    int CdShotKamikaze(CustomDude d, CdWeapon wp, ref CdShotArg a)
    {
        if (a.Check)
            return CdAttackOk;
        var e = d.Spr;
        int nType = Math.Clamp(wp.Id - K.kTrapExploder, 0, KM.ExplodeExtra.Length - 1);
        var shot = ActSpawnSprite(e.BX, e.BY, e.BZ, K.kStatExplosion, true);
        shot.ClassName = "explosion";
        shot.Type = nType;
        shot.Cstat |= Cstat.Invisible;
        shot.Owner = e;
        shot.Shade = -127;
        shot.XRepeat = shot.YRepeat = ExplodeInfo(nType, K.EI_repeat);
        shot.Ang = e.Ang;
        shot.Data1 = ExplodeInfo(nType, K.EI_ticks);
        shot.Data2 = ExplodeInfo(nType, K.EI_quakeEffect);
        shot.Data3 = ExplodeInfo(nType, K.EI_flashEffect);
        shot.Data4 = Math.Max(wp.GetDistance() >> 4, ExplodeInfo(nType, K.EI_radius));
        SpriteSectorForget(shot);
        Seq.Spawn(this, KM.ExplodeExtra[nType].seq, shot);
        if (KM.ExplodeExtra[nType].ground)
            shot.SetBZ(FloorZOf(shot));
        wp.ShotAppearance.Set(this, shot, e);
        ClampSprite(shot);
        NnExtOffsetSprite(shot, a.OffX, a.OffY, a.OffZ);
        shot.Visible = (shot.Cstat & Cstat.Invisible) == 0;
        UpdateBox(shot);
        if (e.XHealth != 0)
        {
            e.XHealth = 0;      // it attacks once
            CdKill(d, e, K.kDamageExplode, 0x10000);
        }
        _cdShot = shot;
        return CdAttackSprite;
    }

    /// <summary>cdudeDoExplosion: a dying kamikaze's explosion.</summary>
    void CdudeDoExplosion(CustomDude d)
    {
        var wp = d.Weapon;
        if (wp == null || wp.Type != Cd.WeaponKamikaze)
            return;
        var a = new CdShotArg { OffX = wp.ShotOffX, OffY = wp.ShotOffY, OffZ = wp.ShotOffZ };
        _cdShot = null;
        if (CdShotKamikaze(d, wp, ref a) >= 0 && _cdShot != null)
            wp.ShotSound.Play(this, _cdShot);
    }

    static readonly int[] _cdStompStats = { K.kStatDude, K.kStatThing };

    /// <summary>The beast's stomp: everything near takes falling damage, players' screens shake.</summary>
    int CdShotBeastStomp(CustomDude d, CdWeapon wp, ref CdShotArg a)
    {
        var e = d.Spr;
        const int vc = 400 << 4;
        int v1c = 7 * Skill, v10 = 55 * Skill;
        foreach (int stat in _cdStompStats)
        {
            foreach (var s2 in new List<Sprite>(StatList(stat)))
            {
                if (s2 == e || s2.Removed || !XSprIsFine(s2) || s2.Owner == e)
                    continue;
                if (!CheckProximity(s2, e.BX, e.BY, e.BZ, wp.GetDistance() << 4))
                    continue;
                double dx = Math.Abs(e.BX - s2.BX), dy = Math.Abs(e.BY - s2.BY);
                int nDist2 = (int)Math.Sqrt(dx * dx + dy * dy);
                if (nDist2 > vc)
                    continue;
                int nDamage = nDist2 == 0 ? v1c + v10 : v1c + (vc - nDist2) * v10 / vc;
                if (a.Check)
                {
                    if ((nDamage << 4) > 0 && s2 == e.XTarget)
                        return CdAttackOk;
                    continue;
                }
                if (s2 is Player pl)
                    pl.QuakeEffect = Math.Min(pl.QuakeEffect + (nDamage << 2), 1280);
                ActDamageSprite(e, s2, K.kDamageFall, nDamage << 4);
            }
        }
        return a.Check ? CdAttackFail : CdAttackOk;
    }

    /// <summary>helperPounce: what the dude rams is kicked along and hurt.</summary>
    void CdHelperPounce(Sprite e, Sprite hs, int nDmgType, double nDmg, double kickPow)
    {
        if (kickPow != 0)
        {
            double nVel = Math.Min(Blood.MulScale16(Blood.ApproxDist(e.XVel, e.YVel), kickPow), kickPow);
            hs.XVel += Blood.MulScale30(nVel, Blood.Cos(e.Ang + Random2(K.kAng15)));
            hs.YVel += Blood.MulScale30(nVel, Blood.Sin(e.Ang + Random2(K.kAng15)));
            hs.HitTag |= K.kPhysFalling;
        }
        if (nDmg != 0)
            ActDamageSprite(e, hs, Math.Clamp(nDmgType, 0, K.kDamageMax - 1), nDmg);
    }

    /// <summary>Ramming: the dude runs on at its weapon's speed while the way is free, kicking what it touches.</summary>
    int CdShotRam(CustomDude d, CdWeapon wp, ref CdShotArg a)
    {
        if (a.Check)
            return CdAttackOk;
        var e = d.Spr;
        double tx = e.TargetX - e.BX, ty = e.TargetY - e.BY;
        bool targetOnly = (wp.Data1 & 0x001) == 0, touchOnce = (wp.Data1 & 0x002) != 0, absoluteVel = (wp.Data1 & 0x004) != 0,
            scaleDmg = (wp.Data1 & 0x008) == 0, checkCF = (wp.Data1 & 0x010) == 0, keepDrag = (wp.Data1 & 0x020) != 0,
            stopOnAng = (wp.Data1 & 0x040) != 0, scaleKick = (wp.Data1 & 0x080) != 0, stopOnDist = (wp.Data1 & 0x100) == 0;
        var touch = e.XTarget;
        int nAng = Blood.GetAngle(tx, ty);
        while (true)
        {
            if (e.Hit == Hit.Wall)
                break;
            if (e.Hit == Hit.Sprite)
            {
                if (e.HitEnt is not Sprite hs || !hs.HasX)
                    break;
                if (targetOnly && e.XTarget != hs)
                    break;
                if ((hs.PhysAttr & KM.kPhysDebrisTouch) == 0)
                {
                    if (hs.StatNum != K.kStatDude && hs.StatNum != K.kStatThing)
                        break;
                    if ((hs.HitTag & (K.kPhysMove | K.kPhysGravity)) == 0)
                        break;
                }
                double k = wp.Data4 << 8;
                if (scaleKick && k > 0)
                {
                    int m = IsCustomDude(hs) ? hs.Custom?.Mass ?? 0 : IsDudeSprite(hs) ? Info.Dude(hs.Type, Info.DI_mass) : 0;
                    if (m != 0)
                        k = DivScale(k, m, 4);
                }
                double dmg = scaleDmg ? Math.Floor(wp.Data3 * Blood.ApproxDist(e.XVel, e.YVel) / 262144) : wp.Data3;
                CdHelperPounce(e, hs, wp.Data2, dmg, k);
                if (touchOnce)
                    break;
                // (NBlood asks clipmove whether the one kicked can move on; here: whether it moves at all)
                if (Blood.ApproxDist(Blood.Sar(hs.XVel, 12), Blood.Sar(hs.YVel, 12)) <= 0)
                    break;
                d.GoalZ = (int)e.TargetZ;
                checkCF = !keepDrag;
                touch = hs;
            }
            int nDist = wp.GetDistance();
            int nDAng = Math.Abs(DAngle(nAng, e.GoalAng));
            if (nDAng > K.kAng90 && (stopOnAng || (stopOnDist && Blood.ApproxDist(tx, ty) >= nDist)))
                break;
            if (!CanMove(e, touch, e.Ang, e.ClipDist << 2))
                break;
            if (checkCF && d.IsFlying)
            {
                int sect = SpriteBSector(e);
                if (sect >= 0)
                {
                    Board.GetZsOfSlope(sect, e.BX, e.BY, out double cz, out double fz);
                    var (zt, zb) = Extents(e);
                    if ((e.ZVel > 0 && zb >= fz - d.FlyCfDist) || (e.ZVel < 0 && zt <= cz + d.FlyCfDist))
                        break;
                }
            }
            if (wp.HaveVelocity)
            {
                if (!d.IsFlying && !wp.HaveSlope)
                    a.Dz = 0;
                if (!absoluteVel && !d.IsFlying)
                    NnExtScaleVelocityRel(e, wp.ShotVelocity, a.Dx, a.Dy, a.Dz);
                else
                    NnExtScaleVelocity(e, wp.ShotVelocity, a.Dx, a.Dy, a.Dz);
            }
            return CdAttackContinue;        // it runs on
        }
        e.GoalAng = (nAng + Random2(K.kAng45)) & K.kAngMask;
        CdNewState(d, Cd.StateSearch);
        return CdAttackFail;
    }

    /// <summary>Teleporting: the dude appears by its target (somewhere between the offset place and the target when
    /// that place lies outside the map).</summary>
    int CdShotTeleport(CustomDude d, CdWeapon wp, ref CdShotArg a)
    {
        if (a.Check)
            return CdAttackOk;
        var e = d.Spr;
        double ox = a.Dx, oy = a.Dy, oz = e.TargetZ - a.Dz;
        double x = ox, y = oy, z = oz;
        int s = SpriteBSector(e), c = 32;
        NnExtOffsetPos(a.OffX, a.OffY, a.OffZ, e.Ang, ref x, ref y, ref z);
        double rx = ox - x, ry = oy - y;
        while (--c >= 0)
        {
            if (FindSectorB(x, y, z, s) < 0)
            {
                if (c > 0)
                {
                    x = ox + Random((int)rx);
                    y = oy + Random((int)ry);
                    z = oz;
                }
                else
                {
                    x = e.TargetX;
                    y = e.TargetY;
                    z = e.TargetZ;
                }
                continue;
            }
            SetBuildPos(e, x, y, z);
            e.PrevOrigin = e.Origin;
            SpriteSectorForget(e);
            if (wp.TurnToTarget)
            {
                e.GoalAng = CdGetTargetAng(e);
                e.Ang = e.PrevAng = e.GoalAng;
            }
            if (wp.HaveVelocity)
                NnExtScaleVelocity(e, wp.ShotVelocity, a.Dx, a.Dy, a.Dz);
            ClampSprite(e);
            e.PrevOrigin = e.Origin;
            if (!d.IsFlying && (e.HitTag & K.kPhysGravity) != 0)
                e.HitTag |= K.kPhysFalling;
            UpdateBox(e);
            break;
        }
        return CdAttackOk;
    }

    /// <summary>weaponShotMorph: the dude turns into another (a vanilla dude or a custom one) as an attack.</summary>
    int CdShotMorph(CustomDude d, CdWeapon wp, ref CdShotArg a)
    {
        if (a.Check)
            return CdAttackOk;
        var e = d.Spr;
        var target = e.XTarget;
        double nHealth = e.XHealth;
        int nID = wp.Id;
        bool chgHealth = (wp.Data2 & 0x001) != 0, chgAmmo = (wp.Data2 & 0x002) != 0;
        if (wp.Type == Cd.WeaponMorph)
        {
            e.ClipDist = Info.Dude(e.Type, Info.DI_clipdist);
            e.Type = e.InitType = wp.Id;
            e.Data1 = 0;
            if (chgHealth)
                e.XHealth = NnExtDudeStartHealth(e, 0);
        }
        else if (wp.Type == Cd.WeaponMorphCdude)
            e.Data1 = wp.Id;
        else
            return CdAttackFail;
        e.Data2 = 0;
        e.Data3 = 0;
        if (chgHealth)
            e.SysData2 = e.Data4 = 0;
        var delay = new CdTimer();
        delay.CopyFrom(wp.CooldownDelay);
        if (wp.HaveVelocity)
            NnExtScaleVelocity(e, wp.ShotVelocity, a.Dx, a.Dy, a.Dz);
        wp.ShotAppearance.Set(this, e, null);
        wp.ShotSound.Play(this, e);
        if (wp.Data1 != 0)
        {
            int h = Math.Min(wp.Data1, 65535);
            e.XHealth = Math.Min(h << 4, 65535);
            e.Data4 = e.SysData2 = h;
        }
        CdBecome(e);
        if (!chgHealth)
            e.XHealth = nHealth;
        if (target != null && !target.Removed) AiSetTarget(e, target);
        else AiSetTargetXYZ(e, e.BX, e.BY, e.BZ);
        AiActivateDude(e);
        if (e.Type == KM.kDudeModernCustom && e.Custom is { } nd)
        {
            // the new form's morphing weapons take over the cooldown
            for (int i = nd.NumWeapons - 1; i >= 0; i--)
            {
                var w = nd.Weapons[i];
                if (w.Type != Cd.WeaponMorph && w.Type != Cd.WeaponMorphCdude)
                    continue;
                int n = delay.Rng0;
                if (n > 0)
                {
                    if (delay.Rng1 != 0)
                        n += Random(delay.Rng1 - delay.Rng0);
                    if (delay.Mul != 0)
                        n *= delay.Mul;
                    w.CooldownDelay.Set(this, n);
                }
                if (w.Id == nID && chgAmmo)
                    w.AmmoDec();
            }
        }
        return CdAttackBreakAll;
    }

    int CdShotOne(CustomDude d, CdWeapon wp, ref CdShotArg a)
    {
        switch (wp.Type)
        {
            case Cd.WeaponHitscan: return CdShotHitscan(d, wp, ref a);
            case Cd.WeaponMissile: return CdShotMissile(d, wp, ref a);
            case Cd.WeaponThrow: return CdShotThing(d, wp, ref a);
            case Cd.WeaponSummon:
            case Cd.WeaponSummonCdude: return CdShotSummon(d, wp, ref a);
            case Cd.WeaponKamikaze: return CdShotKamikaze(d, wp, ref a);
            case Cd.WeaponSpecial:
                return wp.Id switch
                {
                    Cd.WeaponIdSpecialBeastStomp => CdShotBeastStomp(d, wp, ref a),
                    Cd.WeaponIdSpecialRam => CdShotRam(d, wp, ref a),
                    Cd.WeaponIdSpecialTeleport => CdShotTeleport(d, wp, ref a),
                    _ => CdAttackFail,
                };
            case Cd.WeaponMorph:
            case Cd.WeaponMorphCdude: return CdShotMorph(d, wp, ref a);
            case Cd.WeaponDamage:
            case Cd.WeaponHealth: return CdShotDmgOrHp(d, wp, ref a);
            default: return CdAttackOk;
        }
    }

    /// <summary>weaponShotCallback: the attack animation's trigger frame fires the dude's weapon.</summary>
    void CdWeaponShotCallback(Sprite e)
    {
        if (e.Custom is { } d && !e.Removed)
            CdudeWeaponShot(d, d.Weapon, false);
    }

    /// <summary>cdudeWeaponShot: the weapon fires (with the weapons sharing its akimbo id): aimed at the target (or
    /// where it will be), each shot with its dispersion, a styled volley spread out. check: only whether the shot
    /// could be made now (below 0: not).</summary>
    internal int CdudeWeaponShot(CustomDude d, CdWeapon main, bool check)
    {
        var e = d.Spr;
        if (main == null)
            return CdAttackBreakAll;
        for (int i = 0; i < d.NumWeapons && (check || d.IsAttacking); i++)
        {
            CdWeapon wp;
            if (!check)
            {
                wp = d.Weapons[i];
                if (!wp.Available)
                    continue;
                if (main != wp && (main.SharedId == 0 || main.SharedId != wp.SharedId))
                    continue;
                if (d.NumAvailWeapons >= 2 && wp.Frames != null && !wp.Frames.Contains(Seq.Status(e)))
                    continue;
            }
            else
                wp = main;
            int nShots = wp.GetNumshots();
            bool styled = nShots > 1 && wp.StyleAvailable;
            double offX = wp.ShotOffX, offY = wp.ShotOffY, offZ = wp.ShotOffZ;
            int nAng = e.Ang;
            double sx = e.BX, sy = e.BY, tx = e.TargetX, ty = e.TargetY;
            int nDang = DAngle(e.Ang, Blood.GetAngle(tx - sx, ty - sy));
            if (!check)
                wp.AmmoDec(nShots);
            if (Math.Abs(nDang) < wp.Angle)
            {
                double zz = 0;
                NnExtOffsetPos(offX, offY, offZ, nAng, ref sx, ref sy, ref zz);
                nAng = Blood.GetAngle(tx - sx, ty - sy);
            }
            if (wp.PredDistance != 0 && e.XTarget is { } targ && !targ.Removed)
            {
                // where the target will be
                tx = targ.BX;
                ty = targ.BY;
                double nDist = Blood.ApproxDist(tx - sx, ty - sy);
                if ((targ.XVel != 0 || targ.YVel != 0) && nDist < wp.PredDistance)
                {
                    double j = DivScale(nDist, wp.PredAccuracy, 12);
                    double tx2 = tx + Blood.Sar(targ.XVel * j, 12), ty2 = ty + Blood.Sar(targ.YVel * j, 12);
                    int a2 = Blood.GetAngle(tx2 - sx, ty2 - sy);
                    if (Math.Abs(DAngle(a2, nAng)) < wp.PredAngle)
                    {
                        nAng = a2;
                        tx = tx2;
                        ty = ty2;
                    }
                }
            }
            double dx1, dy1;
            if (wp.Type == Cd.WeaponSummon || wp.Type == Cd.WeaponSummonCdude)
                dx1 = dy1 = 0;
            else if (wp.Type == Cd.WeaponThrow)
            {
                dx1 = sx - tx;
                dy1 = sy - ty;
            }
            else if (wp.Id == Cd.WeaponIdSpecialTeleport)
            {
                dx1 = tx;
                dy1 = ty;
            }
            else
            {
                dx1 = Blood.Sar(Blood.Cos(nAng), 16);
                dy1 = Blood.Sar(Blood.Sin(nAng), 16);
            }
            int sang = 0, tang = 0, hsht = 0;
            bool hxof = false;
            if (styled)
            {
                int t = nShots;
                hsht = t >> 1;
                if (t % 2 != 0)
                    t++;
                sang = wp.StyleAngle / t;
            }
            double dz1 = wp.ShotSlope == int.MaxValue ? CdAdjustSlope(d, e.XTarget, wp.ShotOffZ) : wp.ShotSlope;
            int nCode = -1;
            double dx2 = 0, dy2 = 0, dz2 = 0, dx3 = 0, dy3 = 0;
            for (int j = nShots; j > 0 && (check || d.IsAttacking); j--)
            {
                if (!styled || j == nShots)
                {
                    dx3 = Random3(wp.Dispersion[0]);
                    dy3 = Random3(wp.Dispersion[0]);
                    double dz3 = Random3(wp.Dispersion[1]);
                    dx2 = dx1 + dx3;
                    dy2 = dy1 + dy3;
                    dz2 = dz1 + dz3;
                }
                var a = new CdShotArg { OffX = offX, OffY = offY, OffZ = offZ, Dx = dx2, Dy = dy2, Dz = dz2, Check = check };
                _cdShot = null;
                if (wp.SpawnsSprite && !CdCheckUnits(d, wp))
                {
                    nCode = CdAttackFail;
                    j = 0;
                }
                else
                    nCode = CdShotOne(d, wp, ref a);
                if (check)
                    return nCode;
                if (nCode == CdAttackBreakAll || e.Removed || e.Custom != d)
                    return CdAttackBreakAll;
                if (nCode < 0)
                    continue;
                var shot = _cdShot;
                if (shot != null)
                {
                    shot.Ang = nAng;
                    if ((shot.Cstat & Cstat.AlignMask) == Cstat.Wall)
                        shot.Ang = (shot.Ang + K.kAng90) & K.kAngMask;
                    shot.PrevAng = shot.Ang;
                    // its own time to live
                    int nTime = wp.ShotRemTime;
                    if (nTime >= 0)
                    {
                        EvKillCallback(shot, K.kCallbackRemove);
                        if (nTime != 0)
                            EvPostCallback(shot, nTime, K.kCallbackRemove);
                    }
                }
                if (!styled)
                    continue;
                if (wp.StyleOffX != 0)
                {
                    int txof = wp.StyleOffX;
                    if (j <= hsht)
                    {
                        if (!hxof)
                        {
                            offX = wp.ShotOffX;
                            hxof = true;
                        }
                        txof = -txof;
                    }
                    offX += txof;
                }
                offY += wp.StyleOffY;
                offZ += wp.StyleOffZ;
                if (wp.StyleAngle == 0)
                    continue;
                // (NBlood turns these about the dude's place, velocities and directions alike; kept)
                if (shot != null)
                {
                    if (j <= hsht && sang > 0)
                    {
                        sang = -sang;
                        tang = sang;
                    }
                    double vx = shot.XVel - e.BX, vy = shot.YVel - e.BY;
                    shot.XVel = e.BX + Blood.DMulScale30R(vx, Blood.Cos(tang), -vy, Blood.Sin(tang));
                    shot.YVel = e.BY + Blood.DMulScale30R(vx, Blood.Sin(tang), vy, Blood.Cos(tang));
                    shot.Ang = GetVelocityAngle(shot);
                    if ((shot.Cstat & Cstat.AlignMask) == Cstat.Wall)
                        shot.Ang = (shot.Ang + K.kAng90) & K.kAngMask;
                    shot.PrevAng = shot.Ang;
                    tang += sang;
                }
                else
                {
                    if (j <= hsht && sang > 0)
                    {
                        dx2 = Blood.Sar(Blood.Cos(e.Ang), 16) + dx3;
                        dy2 = Blood.Sar(Blood.Sin(e.Ang), 16) + dy3;
                        sang = -sang;
                    }
                    double vx = dx2 - e.BX, vy = dy2 - e.BY;
                    dx2 = e.BX + Blood.DMulScale30R(vx, Blood.Cos(sang), -vy, Blood.Sin(sang));
                    dy2 = e.BY + Blood.DMulScale30R(vx, Blood.Sin(sang), vy, Blood.Cos(sang));
                }
            }
            wp.ShotSound.Play(this, e);
            if (nCode != CdAttackContinue && wp.CooldownCheck(this) != 0)
                wp.Available = false;
        }
        return 0;
    }

    // ---------------------------------------------------------------- set-up helpers, spawners, the leech

    /// <summary>SetupSlaves: the dudes the dude owns (after a morph, a respawn).</summary>
    public void CdSetupSlaves(CustomDude d)
    {
        d.Slaves.Clear();
        foreach (var s in StatList(K.kStatDude))
            if (s.Owner == d.Spr && IsDudeSprite(s) && XSprIsFine(s))
                d.Slaves.Add(s);
    }

    /// <summary>SetupLeech: the life leech the dude threw, if one lies about.</summary>
    public void CdSetupLeech(CustomDude d)
    {
        d.Leech = null;
        foreach (var s in StatList(K.kStatThing))
        {
            if (s.Owner != d.Spr || !s.HasX || s.Type != KM.kModernThingEnemyLifeLeech)
                continue;
            if (s.XLocked != 0)
                CdLeechKill(d, false);      // broken already: no ammo
            d.Leech = s;
            break;
        }
    }

    /// <summary>SetupIncarnation (version 1): the dudes receiving on the dude's TX ID wait unseen; one of them is what
    /// it turns into when it dies.</summary>
    public void CdSetupIncarnation(CustomDude d)
    {
        var e = d.Spr;
        int nDude = -1;
        if (!d.Initialized)
        {
            foreach (var s in new List<Sprite>(StatList(K.kStatDude)))
            {
                if (s == e || !s.HasX || !IsDudeSprite(s) || s is Player || s.XHealth <= 0 || s.RxID != e.TxID)
                    continue;
                s.StatNum = K.kStatInactive;
                Seq.Kill(s);
            }
        }
        foreach (var s in StatList(K.kStatInactive))
        {
            if (s == e || !s.HasX || !IsDudeSprite(s) || s.XHealth <= 0 || s.RxID != e.TxID)
                continue;
            nDude = _cdIncarnations.IndexOf(s);
            if (nDude < 0)
            {
                nDude = _cdIncarnations.Count;
                _cdIncarnations.Add(s);
            }
            if (NnExtRandom(0, 6) == 3)
                break;
        }
        for (int i = 0; i < d.MorphId.Length; i++)
            d.MorphId[i] = nDude;
    }

    /// <summary>cdudeDoAppearanceEffect: a callback effect, a gib or an effect at a sprite.</summary>
    public void CdudeDoAppearanceEffect(Sprite e, int nID)
    {
        if (nID >= 0 && nID < Cd.CustomCallback.Length)
        {
            int nFunc = Cd.CustomCallback[nID];
            if (Chance(0x8000))
                EvKillCallback(e, nFunc);
            EvPostCallback(e, 0, nFunc);
        }
        else if (nID >= Cd.GIBEffectBase && nID < Cd.GIBEffectBase + 31)
            GibSpriteAt(e, nID - Cd.GIBEffectBase, e.BX, e.BY, e.BZ, 0, 0, 0);
        else if (nID >= Cd.FXEffectBase && nID < Cd.FXEffectBase + 57)
            FxSpawnIn(nID - Cd.FXEffectBase, SpriteBSector(e), e.BX, e.BY, e.BZ);
    }

    /// <summary>The sequence callback an appearance sets (callbackSeqCustom): its effect, kept in sysData4.</summary>
    public void CdudeAppearanceSeqCallback(Sprite e) => CdudeDoAppearanceEffect(e, e.SysData4);

    /// <summary>cdudeSpawn: a custom dude made by a spawner (kModernCustomDudeSpawn), with the spawner's data.</summary>
    Sprite CdudeSpawn(Sprite src, Sprite at, int nDist)
    {
        var dude = NnExtSpawnDudeAt(at, KM.kDudeModernCustom, at.BX, at.BY, at.BZ);
        NnExtOffsetSprite(dude, 0, nDist, 0);
        dude.Data1 = src.Data1;
        dude.Data2 = src.Data2;
        dude.Data3 = dude.SysData1 = src.Data3;     // its first sound
        dude.Data4 = dude.SysData2 = src.Data4;     // its health
        dude.XHealth = NnExtDudeStartHealth(dude, src.Data4);
        dude.BusyTime = src.BusyTime;               // its speed
        if (src.ClipDist > 0)
            dude.ClipDist = src.ClipDist;
        if ((src.HitTag & KM.Flag1) != 0 && src.Type == KM.kModernCustomDudeSpawn)
        {
            if (dude.Pal <= 0)
                dude.Pal = src.Pal;
            dude.TxID = src.TxID;
            dude.XCommand = src.XCommand;
            dude.TriggerOn = src.TriggerOn;
            dude.TriggerOff = src.TriggerOff;
            dude.DropMsg = src.DropMsg;
            dude.XKey = src.XKey;
            dude.DudeDeaf = src.DudeDeaf;
            dude.DudeGuard = src.DudeGuard;
            dude.DudeAmbush = src.DudeAmbush;
            dude.DudeFlag4 = src.DudeFlag4;
            dude.Unused1 = src.Unused1;
        }
        if ((src.HitTag & KM.Flag2) != 0)
        {
            dude.XRepeat = src.XRepeat;
            dude.YRepeat = src.YRepeat;
        }
        KillMgrAddCountSprite(dude);
        AiInitSprite(dude);
        dude.Visible = (dude.Cstat & Cstat.Invisible) == 0;
        UpdateBox(dude);
        return dude;
    }

    /// <summary>cdudeLeechOperate: the enemy's life leech takes its owner's target (or gives its own) and fires at it.</summary>
    void CdudeLeechOperate(Sprite e)
    {
        if (e.Owner is { } own && XSprIsFine(own) && IsDudeSprite(own))
        {
            if (own.XTarget != null && !own.XTarget.Removed)
                e.XTarget = own.XTarget;
            else if (e.XTarget is { } lt && !lt.Removed)
            {
                AiSetTargetXYZ(own, lt.BX, lt.BY, lt.BZ);
                AiActivateDude(own);
            }
        }
        var targ = e.XTarget;
        if (targ == null || targ.Removed || targ == e.Owner)
            return;
        if (targ.StatNum != K.kStatDude || !XSprIsFine(targ) || e.StateTimer != 0)
            return;
        if (targ is Player pl && PowerupCheck(pl, K.kPwUpShadowCloak) > 0)
            return;
        double x = targ.BX, y = targ.BY, z = targ.BZ;
        var (zTop, _) = Extents(e);
        double nDist = Blood.ApproxDist(x - e.BX, y - e.BY);
        if (nDist == 0 || !CanSee(e.BX, e.BY, zTop, x, y, z, e, targ))
            return;
        double t = DivScale(nDist, 0x1aaaaa, 12);
        x += Blood.Sar(targ.XVel * t, 12);
        y += Blood.Sar(targ.YVel * t, 12);
        int nAng = Blood.GetAngle(x - e.BX, y - e.BY);
        double dx = Blood.Sar(Blood.Cos(nAng), 16), dy = Blood.Sar(Blood.Sin(nAng), 16);
        double dz = Blood.DivScale10(targ.BZ - zTop, nDist);
        int nMissileType = K.kMissileLifeLeechAltNormal + (e.Data3 != 0 ? 1 : 0);
        int t2 = e.Data3 == 0 ? 12 : 36;
        var missile = ActFireMissile(e, 0, zTop - e.BZ - 384, dx, dy, dz, nMissileType);
        if (missile != null)
        {
            missile.Owner = e.Owner;
            e.StateTimer = 1;
            EvPostCallback(e, t2, K.kCallbackLeechStateTimer);
            e.Data3 = Math.Max(e.Data3 - 1, 0);
        }
    }

    /// <summary>The level's custom dudes, one line each (the cdudes console command; within a radius of a place, or all).</summary>
    public IEnumerable<string> CdDescribe(Vector3 at, float radius)
    {
        foreach (var e in Sprites)
        {
            if (e.Removed || e.Custom is not { } d || e.Type != KM.kDudeModernCustom)
                continue;
            if (radius >= 0 && new Vector2(e.Origin.X - at.X, e.Origin.Y - at.Y).Length() > radius)
                continue;
            string st = d.FindState(e.AiState, out int s, out int p) ? $"{s}/{p}" : e.AiState?.Name ?? "none";
            var wps = new List<string>();
            for (int i = 0; i < d.NumWeapons; i++)
                wps.Add($"{d.Weapons[i].Type}:{d.Weapons[i].Id}" + (d.Weapons[i] == d.Weapon ? "*" : ""));
            yield return $"cdude #{e.SprIndex} v{d.Version} data1 {e.Data1} at {e.Origin.X:0} {e.Origin.Y:0} {e.Origin.Z:0} sector {SpriteBSector(e)} stat {e.StatNum}"
                + $" state {st} seq {Seq.Id(e)} frame {Seq.Status(e)} pic {e.Picnum} health {e.XHealth:0} mass {d.Mass} height {d.Height} posture {d.Posture}"
                + $" active {e.DudeActive} target {(e.XTarget == null ? "none" : e.XTarget.Type.ToString())} status {e.SysData3:x} clip {e.ClipDist}"
                + $" rep {e.XRepeat}x{e.YRepeat} cstat {e.Cstat} flags {e.HitTag} weapons {string.Join(" ", wps)}";
        }
    }

    /// <summary>A sequence's step sound: the floor's surface under a sprite moving on it (seq.cpp surfaceSound).</summary>
    public void SeqSurfaceSound(Sprite e)
    {
        if (!IsModern || e.ZVel != 0 || Blood.ApproxDist(e.XVel, e.YVel) == 0)
            return;
        int sect = SpriteBSector(e);
        if (sect < 0 || UpperLinkOf(sect) != null)
            return;
        int nSurf = Tiles.SurfType(Board.Sectors[sect].FloorPicnum);
        if (nSurf <= 0)
            return;
        int nSnd = 800 + (nSurf << 1) + Random(2);
        if (SfxData.TryGet(nSnd, out var sfx))
            Sound3DCP(e, nSnd, -1, 0, 0, sfx.RelVol != 80 ? sfx.RelVol : 25);
    }
}
