using System;
using System.Collections.Generic;
using Chosen.Data;

namespace Chosen.Sim.Modern;

/// <summary>The custom dudes' constants (NBlood's nnextcdud.h).</summary>
public static class Cd
{
    public const int MaxVelocity = 2345648, MaxSounds = 3, MaxWeapons = 12, MaxDmgScale = 32767, MaxDispersion = 3500,
        DefaultSeq = 11520, DefaultSeqF = 4864, V1MaxAttackDist = 20000, MaxDropItems = 5, DefaultAnimScale = 256,
        MaxEffectGroups = 16, MaxEffects = 3, FXEffectBase = 0, FXEffectCallbackBase = 512, GIBEffectBase = 1024,
        StateTypeDeathPosture = AiState.Other - 1, MinCFDist = 0x2000, LandDist = 0xB00, LaunchDist = 0x1000,
        CrouchDist = 0x2000, MinSeeDist = 3000, MinHearDist = MinSeeDist >> 1, BurningHealth = 25 << 4,
        FlyStartZvel = -0x22222, ChanceMax = 0x10000;

    // postures: on land, crouching, in water, flying
    public const int PostureL = 0, PostureC = 1, PostureW = 2, PostureF = 3, PostureMax = 4;

    // sounds
    public const int SndTargetSpot = 0, SndGotHit = 1, SndDeathNormal = 2, SndBurning = 3, SndDeathExplode = 4, SndTargetDead = 5,
        SndTargetChase = 6, SndTransforming = 7, SndWake = 8, SndLStandEnter = 9, SndLStand = 10, SndLStandWake = 11, SndMax = 12,
        SndCompatAttack1 = 13, SndCompatAttack2 = 14, SndCompatAttack3 = 15, ParSndMultiSrc = 16;

    // weapon kinds
    public const int WeaponNone = 0, WeaponHitscan = 1, WeaponMissile = 2, WeaponThrow = 3, WeaponSummon = 4, WeaponSummonCdude = 5,
        WeaponKamikaze = 6, WeaponSpecial = 7, WeaponMorph = 8, WeaponMorphCdude = 9, WeaponDamage = 10, WeaponHealth = 11,
        WeaponMax = 12;
    public const int WeaponIdSpecialBase = 900, WeaponIdSpecialBeastStomp = 900, WeaponIdSpecialRam = 901,
        WeaponIdSpecialTeleport = 902, WeaponIdSpecialMax = 903;

    // AI states
    public const int StateIdle = 0, StateMoveBase = 1, StateSearch = 1, StateDodge = 2, StateChase = 3, StateFlee = 4,
        StateMoveMax = 5, StateRecoil = 5, StateRecoilT = 6, BurnStateSearch = 7, StateMorph = 8, StateKnockEnter = 9,
        StateKnock = 10, StateKnockExit = 11, StateSleep = 12, StateWake = 13, StateGenIdle = 14, StateNormalMax = 15,
        StateDeathBase = 15, StateDeathBurn = 16, StateDeathVector = 17, StateDeathExplode = 18, StateDeathChoke = 19,
        StateDeathSpirit = 20, StateDeathElectric = 21, StateDeathLStand = 22, StateDeathMax = 23, StateAttackBase = 23,
        StateAttackMax = StateAttackBase + MaxWeapons, StateMax = StateAttackMax + 1, StateMove = StateMax + 1,
        StateDeath = StateMax + 2, StateAttack = StateMax + 3, AnimScale = StateMax + 4, AnimLStandEnter = StateMax + 5,
        AnimLStand = StateMax + 6, AnimLStandExit = StateMax + 7;

    // status bits (kept in the x-sprite's sysData3)
    public const int StatusAwaked = 0x01, StatusForceCrouch = 0x02, StatusLStandAwaked = 0x04, StatusCruel = 0x08,
        StatusInnocent = 0x10, StatusSleep = 0x10000, StatusMorph = 0x20000, StatusBurning = 0x40000, StatusDying = 0x80000,
        StatusRespawn = 0x100000, StatusKnocked = 0x200000, StatusFlipped = 0x400000, StatusLStand = 0x800000;

    // velocities and their modifiers
    public const int VelForward = 0, VelTurn = 1, VelDodge = 2, VelZ = 3, VelMax = 4;
    public const int VelModNone = 0, VelModChase = 1, VelModSearch = 2, VelModBurn = 3, VelModFlee = 4, VelModMax = 5;

    // who a dude takes no damage from
    public const int DmgSourceDude = 0x01, DmgSourcePlayer = 0x02, DmgSourceSelf = 0x04, DmgSourceFriend = 0x08,
        DmgSourceEnemy = 0x10, DmgSourceSlave = 0x20, DmgSourceKin = 0x40;

    public const int TrigVector = 0x01, TrigTouch = 0x02, TrigImpact = 0x04, TrigLocked = 0x08;

    public const int FlyStart = 0, FlyLand = 1, FlightMax = 2;

    /// <summary>gCdudeCustomCallback: the callback effects a descriptor names FX_512 and up.</summary>
    public static readonly int[] CustomCallback =
    {
        K.kCallbackFXFlameLick, K.kCallbackFXFlareSpark, K.kCallbackFXFlareSparkLite, K.kCallbackFXBloodSpurt, K.kCallbackFXBloodSpurt,
        K.kCallbackFXArcSpark, K.kCallbackFXDynPuff, K.kCallbackFXTeslaAlt, KM.kCallbackFxPodGreenBloodSpray,
        K.kCallbackFXPodBloodSpray,
    };

    public static int Perc2Val(int reqPerc, int val) => (int)((long)val * reqPerc / 100);
    public static bool IRngOk(int v, int lo, int hi) => v >= lo && v <= hi;
    public static bool RngOk(int v, int lo, int hi) => v >= lo && v < hi;
}

/// <summary>A timer against the game clock, with the range its time is picked from (CUSTOMDUDE_TIMER).</summary>
public sealed class CdTimer
{
    public int Time, Rng0, Rng1, Mul;

    public void Clear() => Time = 0;
    public bool Exists => Time != 0;
    public int Diff(World w) => Time - w.FrameClock;
    public bool Pass(World w) => Diff(w) < 0;
    public void Set(World w, int n) => Time = w.FrameClock + n;

    public void Set(World w)
    {
        int n = Rng0;
        if (Rng1 != 0)
            n += w.Random(Rng1 - Rng0);
        if (Mul != 0)
            n *= Mul;
        Set(w, n);
    }

    public void CopyFrom(CdTimer o)
    {
        Time = o.Time;
        Rng0 = o.Rng0;
        Rng1 = o.Rng1;
        Mul = o.Mul;
    }
}

/// <summary>One of a dude's sounds: up to three ids one is picked from, where it may play, its volume and pitch.</summary>
public sealed class CdSound
{
    public readonly int[] Id = new int[Cd.MaxSounds];
    public int Medium;          // 1 land, 2 water (0: any)
    public bool Ai, Interruptable;
    public int Once;            // 1: not while any plays it, 2: not while this dude plays it
    public int Volume, Pitch;

    public CdSound() { }

    public CdSound(int a, int b, int c, int medium, bool ai, bool interruptable, bool once, int volume = 0, int pitch = 0)
    {
        Id[0] = a;
        Id[1] = b;
        Id[2] = c;
        Medium = medium;
        Ai = ai;
        Interruptable = interruptable;
        Once = once ? 1 : 0;
        Volume = volume;
        Pitch = pitch;
    }

    public void CopyFrom(CdSound o)
    {
        Array.Copy(o.Id, Id, Id.Length);
        Medium = o.Medium;
        Ai = o.Ai;
        Interruptable = o.Interruptable;
        Once = o.Once;
        Volume = o.Volume;
        Pitch = o.Pitch;
    }

    public int Pick(World w) => Id[w.Random(Cd.MaxSounds)];
    public void Play(World w, Sprite e) => Play(w, e, Pick(w));

    public void Play(World w, Sprite e, int nID)
    {
        if (nID <= 0)
            return;
        int nClock = w.FrameClock;
        bool uwater = w.SpriteIsUnderwaterAny(e);
        int nRand = w.Random2(80);
        if (Medium != 0 && !((Medium & 1) != 0 && !uwater) && !((Medium & 2) != 0 && uwater))
            return;
        if (Once != 0 && w.SoundIsPlaying(Once == 2 ? e : null, Id))
            return;
        if (Interruptable)
        {
            e.DudeClock = nClock;
            Kill(w, e);
        }
        if (Ai)
        {
            if (e.DudeClock <= nClock)
            {
                w.Kill3DSound(e, World.AI_SFX_PRIORITY_2);
                w.Sound3DCP(e, nID, World.AI_SFX_PRIORITY_2, 0, Pitch, Volume);
                e.SfxPriority = World.AI_SFX_PRIORITY_2;
                e.DudeClock = nClock + 384 + nRand;
            }
        }
        else
            w.Sound3DCP(e, nID, -1, 0, Pitch, Volume);
    }

    public void Kill(World w, Sprite e)
    {
        for (int i = Cd.MaxSounds - 1; i >= 0; i--)
            if (Id[i] > 0)
                w.Kill3DSound(e, -1, Id[i]);
    }
}

/// <summary>How something a dude makes looks (APPEARANCE): a sequence, picture, palette, shade, size, flags, a sound,
/// an effect; -129 for a value means "as the source's".</summary>
public sealed class CdAppearance
{
    public readonly CdSound Sound = new();
    public int Scl0, Scl1;
    public bool Available, SoundAvailable;
    public int Seq, Clb, Eff, Pic, Csta, Cstr, Xrp, Yrp, Xof, Yof, Pal = -1, Shd = 128;

    public void Clear()
    {
        Scl0 = Scl1 = 0;
        Available = SoundAvailable = false;
        Seq = Clb = Eff = Pic = Csta = Cstr = Xrp = Yrp = Xof = Yof = 0;
        Pal = -1;
        Shd = 128;
        Array.Clear(Sound.Id);
    }

    void SetScale(World w, Sprite e, Sprite src)
    {
        if (!Available)
            return;
        int nScale;
        if (src == null)
        {
            nScale = Scl0;
            if (Scl1 > 0)
                nScale += w.Random(Scl1 - Scl0);
        }
        else
            nScale = src.XScale;
        CustomDude.SprScaleSet(e, nScale);
        e.XScale = nScale;
    }

    /// <summary>The appearance given to a sprite (src: whose values "as the source's" names; null: its own).</summary>
    public void Set(World w, Sprite e, Sprite src)
    {
        if (!Available)
            return;
        if (!e.HasX)
            World.DbInsertXSprite(e);
        bool xs = true, ys = true, plu = true;
        int nSeq = Seq;
        Action<Sprite> callback = null;
        src ??= e;
        if (SoundAvailable)
            Sound.Play(w, e);
        if (Pic != 0)
        {
            Sim.Seq.Kill(e);
            e.Picnum = Pic;
            nSeq = -1;
        }
        if (nSeq >= 0)
        {
            if (Sim.Seq.Status(e) >= 0)
            {
                World.SeqCanOverridePublic(Sim.Seq.Id(e), 0, out xs, out ys, out plu);
                callback = e.SeqCallback;
            }
            if (nSeq > 0)
            {
                Sim.Seq.Spawn(w, nSeq, e, Clb != 0 ? null : callback);
                if (Sim.Seq.Status(e) >= 0)
                    World.SeqCanOverridePublic(Sim.Seq.Id(e), 0, out xs, out ys, out plu);
            }
            if (Clb != 0 && Sim.Seq.Status(e) >= 0)
            {
                e.SysData4 = Clb - 1;
                e.SeqCallback = w.CdudeAppearanceSeqCallback;
            }
        }
        if (Eff != 0)
            w.CdudeDoAppearanceEffect(e, Eff - 1);
        if (Shd != 128)
        {
            Sim.Seq.Kill(e);
            e.Shade = Shd == -129 ? src.Shade : Shd;
        }
        if (Pal == -129 || Pal >= 0)
        {
            if (!plu)
                Sim.Seq.Kill(e);
            e.Pal = Pal == -129 ? src.Pal : Pal;
        }
        if (Xrp == -129 || Xrp > 0)
        {
            if (!xs)
                Sim.Seq.Kill(e);
            e.XRepeat = Xrp == -129 ? src.XRepeat : Xrp;
        }
        if (Yrp == -129 || Yrp > 0)
        {
            if (!ys)
                Sim.Seq.Kill(e);
            e.YRepeat = Yrp == -129 ? src.YRepeat : Yrp;
        }
        if (Cstr != 0)
            e.Cstat &= ~(Cstr & 0xffff);
        if (Csta != 0)
            e.Cstat |= Csta & 0xffff;
        if (Xof != 0) e.XOffset = Xof;
        if (Yof != 0) e.YOffset = Yof;
        if (Scl0 != 0)
            SetScale(w, e, Scl0 == 1024 && Scl1 == Scl0 && src.HasX ? src : null);
        e.Visible = (e.Cstat & Cstat.Invisible) == 0;
        w.UpdateBox(e);
    }
}

/// <summary>One of a dude's weapons (CUSTOMDUDE_WEAPON): what it fires (a vector, missile, thrown thing, summoned
/// dude, an explosion of itself, a special move), when it may be picked, how it is aimed and what the shot looks like.</summary>
public sealed class CdWeapon
{
    public int Type, NumShots = 1, Id, SharedId, Angle = K.kAng45, Medium, PickChance = Cd.ChanceMax, Posture;
    public bool Available, Interruptable, TurnToTarget = true, SpawnsSprite, Inertia, IsDefault, RealCheck, Locked;
    public int StateID = Cd.StateAttackBase, NextStateID, ClipMask, Group;
    public int Data1, Data2, Data3, Data4, Data5;
    public int MaxUnitsThis, MaxUnitsShared;
    public readonly int[] Dispersion = new int[2];
    public readonly int[] DistRange = { 0, 20000 };
    public readonly int[] HeighRange = { int.MinValue, 0, 0, int.MaxValue };
    public readonly int[] SlopeRange = { int.MinValue, int.MaxValue };
    public readonly int[] TargHpRange = { 0, 255 }, DudeHpRange = { 0, 255 }, TargBurnRange = { 0, 255 };
    public readonly CdSound ShotSound = new(), AttackSound = new();
    public List<int> Frames;
    // prediction
    public int PredDistance, PredAngle, PredAccuracy;
    // the shot
    public int ShotVelocity = int.MaxValue, ShotSlope = int.MaxValue, ShotTargetFollow, ShotClipdist, ShotRemTime = -1;
    public bool ShotImpact;
    public readonly CdAppearance ShotAppearance = new();
    public int ShotOffX, ShotOffY, ShotOffZ;
    // ammo
    public int AmmoCur, AmmoTotal;
    // cooldown
    public readonly CdTimer CooldownDelay = new() { Mul = K.kTicsPerFrame };
    public int CooldownUseCount, CooldownTotalUseCount, CooldownRng0, CooldownRng1;
    // style
    public bool StyleAvailable;
    public int StyleAngle, StyleOffX, StyleOffY, StyleOffZ;

    public void AmmoSet(int v) => AmmoCur = Math.Clamp(v, 0, AmmoTotal);
    public void AmmoDec(int by = 1) => AmmoSet(AmmoCur - by);
    public bool HaveAmmo => AmmoTotal == 0 || AmmoCur != 0;
    public int GetDistance() => Math.Max(DistRange[1] - DistRange[0], 0);
    public int GetNumshots() => AmmoTotal != 0 ? Math.Min(AmmoCur, NumShots) : NumShots;
    public bool HaveSlope => ShotSlope != int.MaxValue;
    public bool HaveVelocity => ShotVelocity != int.MaxValue;

    /// <summary>COOLDOWN::Check: 2 still cooling, 1 the cooldown starts now, 0 free to use.</summary>
    public int CooldownCheck(World w)
    {
        if (!CooldownDelay.Pass(w))
            return 2;
        if (CooldownTotalUseCount != 0 && ++CooldownUseCount < CooldownTotalUseCount)
            return 0;
        CooldownTotalUseCount = CooldownRng0;
        if (CooldownRng1 != 0)
            CooldownTotalUseCount += w.Random(CooldownRng1 - CooldownRng0);
        CooldownDelay.Set(w);
        return CooldownDelay.Diff(w) > 0 ? 1 : 0;
    }
}

/// <summary>An effect sprite turned into a thing (CUSTOMDUDE_GIB): a gib object that can be shot, touched or blown up.</summary>
public sealed class CdGib
{
    public bool Available, Force = true;
    public int TrFlags, Physics = K.kPhysMove | K.kPhysGravity | K.kPhysFalling, ThingType = K.kThingObjectExplode - K.kThingBase;
    public int Data1, Data2, Data3, Data4;

    public void Setup(World w, Sprite e)
    {
        int nStat = e.StatNum;
        if (e.StatNum != K.kStatThing)
        {
            if (e.StatNum >= 1024 || !Force)
                return;
            w.EvKillCallback(e, K.kCallbackRemove);
            e.StatNum = K.kStatThing;
        }
        if (!e.HasX)
            World.DbInsertXSprite(e);
        e.Type = K.kThingBase + ThingType;
        e.HitTag &= ~(K.kPhysMove | K.kPhysGravity | K.kPhysFalling);
        e.HitTag |= Physics;
        if ((e.HitTag & K.kPhysMove) == 0)
            e.XVel = e.YVel = 0;
        if (nStat == K.kStatFX && e.ZVel != 0)
            e.ZVel += Blood.Int(e.ZVel * 35 / 100);
        e.XHealth = Info.Thing(e.Type, Info.TI_startHealth) << 4;
        e.Data1 = Data1;
        e.Data2 = Data2;
        e.Data3 = Data3;
        e.Data4 = Data4;
        e.IsTriggered = 0;
        e.TriggerOnce = 1;
        e.XState = 1;
        if ((TrFlags & Cd.TrigVector) != 0)
        {
            e.Cstat |= Cstat.Hitscan;
            e.XVector = 1;
        }
        if ((TrFlags & Cd.TrigTouch) != 0)
        {
            e.Cstat |= Cstat.Block;
            e.XTouch = 1;
        }
        if ((TrFlags & Cd.TrigImpact) != 0)
            e.XImpact = 1;
        e.XLocked = 1;
        if ((TrFlags & Cd.TrigLocked) == 0)
            w.EvPost(e, 16, K.kCmdUnlock, null);
        w.UpdateBox(e);
        w.NnExtListSpritePublic(e);
    }
}

/// <summary>A group of effects a dude makes while in given states, animations and frames (CUSTOMDUDE_EFFECT): FX,
/// gibs or callback effects, where and how fast they fly, what they look like.</summary>
public sealed class CdEffect
{
    public readonly int[] Id = new int[Cd.MaxEffects];
    public int LiveTime, Velocity = -1, VelocitySlope, Angle = K.kAng360, Posture, Medium, Chance = Cd.ChanceMax;
    public bool AllUnique, SrcVelocity = true;
    public int HpRange0, HpRange1 = 255;
    public readonly CdTimer Delay = new() { Rng0 = 1, Rng1 = 0, Mul = K.kTicsPerFrame };
    public readonly CdGib Spr2Gib = new();
    public readonly CdAppearance Appearance = new();
    public readonly List<int> Anims = new(), Frames = new(), States = new();
    public int OffX, OffY, OffZ;

    public bool CanSpawn(World w, Sprite e)
    {
        int nFrame = 1;
        if (HpRange1 != 255 && e.HasX)
        {
            int nHealth = Math.Max(w.NnExtGetStartHealth(e), 1);
            int tHealth = Math.Min((int)(KM.kPercFull * e.XHealth / nHealth), 255);
            if (!Cd.IRngOk(tHealth, HpRange0, HpRange1))
                return false;
        }
        if (Anims.Count != 0 || Frames.Count != 0)
        {
            if (Sim.Seq.Status(e) < 0)
                return false;
            if (Anims.Count != 0 && !Anims.Contains(Sim.Seq.Id(e)))
                return false;
            nFrame = Sim.Seq.Status(e) + 1;
            if (Frames.Count != 0)
                return Frames.Contains(nFrame);
        }
        return true;
    }

    void Setup(World w, Sprite src, Sprite eff, bool relVel)
    {
        double dx = 0, dy = 0, dz = VelocitySlope;
        int nAng = (Angle != K.kAng360 ? src.Ang + Angle : w.Random2(K.kAng360)) & K.kAngMask;
        double nVel = Velocity;
        int rp = w.Random(15);
        eff.Owner = src;
        dz += w.Random2(Cd.Perc2Val(rp, (int)dz));
        nAng += w.Random2(Cd.Perc2Val(rp >> 1, nAng)) & K.kAngMask;
        eff.Ang = nAng;
        Appearance.Set(w, eff, src);
        if (nVel >= 0)
        {
            nVel += w.Random2(Cd.Perc2Val(rp, (int)nVel));
            dx = Blood.Sar(Blood.Cos(nAng), 16);
            dy = Blood.Sar(Blood.Sin(nAng), 16);
            if (nVel == 0)
                relVel = false;
        }
        else
            nVel = Math.Abs(dz);
        if (relVel)
        {
            eff.XVel += Blood.MulScale14(nVel, dx);
            eff.YVel += Blood.MulScale14(nVel, dy);
            eff.ZVel += Blood.MulScale14(nVel, dz);
        }
        else
        {
            eff.XVel = Blood.MulScale14(nVel, dx);
            eff.YVel = Blood.MulScale14(nVel, dy);
            eff.ZVel = Blood.MulScale14(nVel, dz);
        }
        if (SrcVelocity)
        {
            eff.XVel += src.XVel;
            eff.YVel += src.YVel;
            eff.ZVel += src.ZVel;
        }
        if (Spr2Gib.Available)
            Spr2Gib.Setup(w, eff);
        if (LiveTime != 0)
        {
            w.EvKillCallback(eff, K.kCallbackRemove);
            if (LiveTime > 0)
                w.EvPostCallback(eff, LiveTime, K.kCallbackRemove);
        }
    }

    void Spawn(World w, int nID, Sprite e)
    {
        double x = e.BX, y = e.BY, z = e.BZ;
        World.NnExtOffsetPosPublic(OffX, OffY, OffZ, e.Ang, ref x, ref y, ref z);
        if (nID >= Cd.GIBEffectBase)
        {
            foreach (var fx in w.NnExtGibSprite(e, nID - Cd.GIBEffectBase, x, y, z))
                Setup(w, e, fx, true);
        }
        else if (nID >= Cd.FXEffectCallbackBase)
        {
            int k = nID - Cd.FXEffectCallbackBase;
            if (k < 0 || k >= Cd.CustomCallback.Length)
                return;
            int cb = Cd.CustomCallback[k];
            w.EvKillCallback(e, cb);
            w.EvPostCallback(e, 0, cb);
        }
        else
        {
            var fx = w.FxSpawnAt(nID - Cd.FXEffectBase, e, x, y, z);
            if (fx != null)
                Setup(w, e, fx, false);
        }
    }

    public void Spawn(World w, Sprite e)
    {
        if (!w.Chance(Chance))
            return;
        if (AllUnique)
        {
            for (int i = 0; i < Cd.MaxEffects; i++)
                Spawn(w, Id[i], e);
        }
        else
            Spawn(w, Id[w.Random(Cd.MaxEffects)], e);
    }
}

/// <summary>When a kind of damage makes a dude do something (recoil, dodge, crouch, get knocked out, change its
/// target, make its last stand): above an amount, below a health, by chance, with a cooldown, counting the hits.</summary>
public sealed class CdEventDamage
{
    public int Amount, Health, Chance, HitCount, StateTime0, StateTime1;
    public bool Cumulative;
    public readonly CdTimer Cooldown = new();

    public bool Allow(World w, Sprite e, double nAmount, int nChance = 0)
    {
        if (nChance == 0)
            nChance = Chance;
        if (nAmount <= Amount)
            return false;
        if (Health != 0)
        {
            int nHealth = Math.Max(w.NnExtGetStartHealth(e), 1);
            nHealth = Math.Min((int)(100 * e.XHealth / nHealth), 255);
            if (nHealth > Health)
                return false;
        }
        if (HitCount != 0)
        {
            if (!Cooldown.Pass(w))
            {
                HitCount += 5 - w.Skill;
                nChance = Math.Min(Cd.Perc2Val(HitCount, nChance), nChance);
                return w.Chance(nChance);
            }
            HitCount = 1;
            Cooldown.Set(w);
            return w.Chance(nChance);
        }
        if (Cooldown.Pass(w))
        {
            Cooldown.Set(w);
            return w.Chance(nChance);
        }
        return false;
    }

    public int PickTime(World w)
    {
        int n = StateTime0;
        if (StateTime1 != 0)
            n += w.Random(StateTime1 - n);
        return n;
    }

    public void CopyFrom(CdEventDamage o)
    {
        Amount = o.Amount;
        Health = o.Health;
        Chance = o.Chance;
        HitCount = o.HitCount;
        StateTime0 = o.StateTime0;
        StateTime1 = o.StateTime1;
        Cumulative = o.Cumulative;
        Cooldown.CopyFrom(o.Cooldown);
    }
}

/// <summary>The events by damage kind (CUSTOMDUDE_RECOIL and its kin).</summary>
public sealed class CdOnDamage
{
    public readonly CdEventDamage[] OnDamage = new CdEventDamage[K.kDamageMax];

    public CdOnDamage()
    {
        for (int i = 0; i < OnDamage.Length; i++)
            OnDamage[i] = new CdEventDamage();
    }
}

/// <summary>A flying dude's launching or landing (CUSTOMDUDE_FLIGHT::TYPE).</summary>
public sealed class CdFlyType
{
    public readonly int[] Distance = new int[3];
    public int Chance;
}
