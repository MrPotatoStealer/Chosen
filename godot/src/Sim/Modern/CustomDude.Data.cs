using System;
using System.Collections.Generic;
using Chosen.Data;

namespace Chosen.Sim.Modern;

/// <summary>
/// A custom dude (NBlood's CUSTOMDUDE; sprite type 254): a monster made by a descriptor file (CDUDnnnn.CDU, the dude's
/// data1; version 2) or, in older maps, by its data fields alone (version 1: data1 its weapon, data2 its first
/// sequence, data3 its first sound): its health, mass, speeds, animations by state and posture, sounds, up to twelve
/// weapons, what damage does to it, the effects it makes, what it drops and what it turns into. The game's part is in
/// World.Cdude.cs and World.CdudeAi.cs, the descriptor's reading in CustomDudeSetup.cs.
/// </summary>
public sealed class CustomDude
{
    public int Version;
    public bool Initialized;
    public int NumEffects, NumWeapons, NumAvailWeapons;
    public Sprite Spr;
    public Sprite Leech;                // its thrown life leech (pXLeech)
    public readonly CdWeapon[] Weapons = new CdWeapon[Cd.MaxWeapons];
    public CdWeapon Weapon;             // the one in use
    // damage control: the scale by kind (256: full), who is ignored, the damage of the wrong medium and of stomping
    public readonly int[] DamageId = new int[K.kDamageMax];
    public int DamageIgnoreSources;
    public readonly CdTimer DmgWaterDelay = new(), DmgLandDelay = new(), DmgStompDelay = new();
    public int DmgWaterAmount, DmgLandAmount, DmgStompAmount;
    // velocity: by posture and kind, the modifiers by AI activity
    public readonly int[,] VelocityId = new int[Cd.PostureMax, Cd.VelMax];
    public readonly int[] VelocityMods = new int[Cd.VelModMax];
    public int VelocityModID;
    public readonly CdSound[] Sound = new CdSound[Cd.SndMax];
    public readonly CdOnDamage Dodge = new(), Recoil = new(), LStand = new(), Knockout = new(), Crouch = new(), ChgTarg = new();
    public int DodgeZDir, DodgeOnAimMissChance;
    public readonly int[,] LStandAnim = new int[3, Cd.PostureMax];
    public int LStandWakeChance, LStandWakeHealth;
    public readonly int[] MorphId = new int[K.kDamageMax];
    public bool MorphTrDeath;
    // flight
    public readonly CdFlyType[] FlyType = { new(), new() };
    public bool FlyAbsGoalZ, FlyMustReach = true;
    public int FlyMaxHeight = int.MaxValue, FlyCfDist = Cd.MinCFDist, FlyFriction, FlyClipDist = 1024, FlyBackOnTrackAccel;
    // drop items: (item, chance in percents)
    public readonly int[,] DropItems = new int[Cd.MaxDropItems, 2];
    public int SprDropItemChance = Cd.ChanceMax;
    public readonly CdEffect[] Effects = new CdEffect[Cd.MaxEffectGroups];
    public readonly List<Sprite> Slaves = new();
    public bool SlavesNoSetTarget, SlavesKillOnDeath;
    public readonly AiState[,] States = new AiState[Cd.StateMax, Cd.PostureMax];
    public readonly CdTimer TimerFLaunch = new(), TimerFLand = new(), TimerGoalZ = new(), TimerCrouch = new(), TimerMoveDir = new(),
        TimerFloor = new();
    public int RandStateTime, RandVelocity, RandThinkTime, RandAnimScale, RandWeapChance;
    public int Medium, Posture, Mass, Health, SeeDist, HearDist, Periphery, SleepDist, Height, EyeHeight, FallHeight, GoalZ,
        TurnAng, ThinkClock;
    public bool StopMoveOnTurn;
    /// <summary>What it turns into (-1: nothing; below -1: the vanilla dude of that type, negated; from 0: the custom
    /// dude of that descriptor id).</summary>
    public int NextDude = -1;
    public readonly int[] GibType = { -1, -1, -1 };
    // a kind's template: the clipdist and animation scale its dudes' sprites get (NBlood keeps a hidden sprite)
    public int TemplateClipDist, TemplateScale;
    // the dude's own numbers that vanilla keeps in DUDEINFO (set from the descriptor or its kind's)
    public int FleeHealth, ChangeTarget, ChangeTargetKin, AimHeight;

    public CustomDude()
    {
        for (int i = 0; i < Weapons.Length; i++)
            Weapons[i] = new CdWeapon();
        for (int i = 0; i < Sound.Length; i++)
            Sound[i] = new CdSound();
        for (int i = 0; i < Effects.Length; i++)
            Effects[i] = new CdEffect();
        for (int i = 0; i < Cd.StateMax; i++)
            for (int j = 0; j < Cd.PostureMax; j++)
                States[i, j] = new AiState("cdude") { Seq = -1, CdState = i, CdPosture = j };
        for (int i = 0; i < MorphId.Length; i++)
            MorphId[i] = -1;
        Weapon = Weapons[0];
    }

    public int GetStateSeq(int nState, int nPosture) => States[nState, nPosture].Seq;
    public int GetVelocityBase(int nPosture, int nFor) => VelocityId[nPosture, nFor];
    public int GetVelocity(int nPosture, int nVelType) =>
        GetVelocityBase(nPosture, nVelType) + Cd.Perc2Val(VelocityMods[VelocityModID], GetVelocityBase(nPosture, nVelType));
    public int GetVelocity(int nVelType) => GetVelocity(Posture, nVelType);
    public void SetVelocityBase(int nVal, int nPosture, int nFor) => VelocityId[nPosture, nFor] = Math.Max(nVal, 0);
    public void DamageSet(int nVal, int nFor) => DamageId[nFor] = Math.Clamp(nVal, 0, Cd.MaxDmgScale);
    public void DamageInc(int nVal, int nFor) => DamageSet(DamageId[nFor] + Math.Abs(nVal), nFor);
    public void DamageDec(int nVal, int nFor) => DamageSet(DamageId[nFor] - Math.Abs(nVal), nFor);

    public int GetDistToFloor() => (int)Spr.XHeight << 8;
    public bool IsUnderwater => Spr.Medium != K.kMediumNormal;
    public bool IsStanding => Posture == Cd.PostureL;
    public bool IsCrouching => Posture == Cd.PostureC;
    public bool IsSwimming => Posture == Cd.PostureW;
    public bool IsFlying => Posture == Cd.PostureF;
    public bool SeqPlaying => Seq.Status(Spr) >= 0;
    public bool IsAttacking => Spr.AiState != null && Spr.AiState.Type == AiState.Attack;
    public bool IsRecoil => Spr.AiState != null && Spr.AiState.Type == AiState.Recoil;
    public bool IsChasing => Spr.AiState != null && Spr.AiState.Type == AiState.Chase;
    public bool IsSearching => Spr.AiState != null && Spr.AiState.Type == AiState.Search;
    public bool IsKnockout => StatusTest(Cd.StatusKnocked);
    public bool IsLastStand => StatusTest(Cd.StatusLStand);
    public bool IsBurning => StatusTest(Cd.StatusBurning);
    public bool IsMorphing => StatusTest(Cd.StatusMorph);
    public bool IsDying => StatusTest(Cd.StatusDying);
    public bool IsSleeping => StatusTest(Cd.StatusSleep);
    public bool IsLeechBroken => Leech != null && Leech.XLocked != 0;
    public bool IsFlipped => (Spr.Cstat & Cstat.YFlip) != 0 || (Spr.HitTag & 2048) != 0;
    public bool IsThinkTime(World w) => (w.Frame & ThinkClock) == (Spr.SprIndex & ThinkClock);
    public void StatusSet(int s) => Spr.SysData3 |= s;
    public void StatusRem(int s) => Spr.SysData3 &= ~s;
    public bool StatusTest(int s) => (Spr.SysData3 & s) != 0;
    public bool CanRecoil() => GetStateSeq(Cd.StateRecoil, Posture) > 0;
    public bool CanElectrocute() => GetStateSeq(Cd.StateRecoilT, Posture) > 0;
    public bool CanKnockout() => GetStateSeq(Cd.StateKnock, Posture) != 0;
    public bool CanLastStand() => !StatusTest(Cd.StatusLStandAwaked) && LStandAnim[0, Posture] > 0;
    public bool CanBurn() => GetStateSeq(Cd.BurnStateSearch, Posture) > 0;
    public bool CanStand() => GetStateSeq(Cd.StateSearch, Cd.PostureL) > 0;
    public bool CanCrouch() => GetStateSeq(Cd.StateSearch, Cd.PostureC) > 0;
    public bool CanSwim() => GetStateSeq(Cd.StateSearch, Cd.PostureW) > 0;
    public bool CanFly() => GetStateSeq(Cd.StateSearch, Cd.PostureF) > 0;
    public bool CanSleep() => !StatusTest(Cd.StatusAwaked) && GetStateSeq(Cd.StateSleep, Posture) > 0;
    public bool CanMove() => GetStateSeq(Cd.StateSearch, Posture) > 0;
    public int SlaveCount => Slaves.Count;
    public int CurrentDispersion => Weapon?.Dispersion[0] ?? 0;

    public bool IsMediumMatch(int nMedium)
    {
        if (nMedium == 0)
            return true;
        if ((nMedium & 4) != 0)
            return false;       // the medium "none"
        bool uwater = IsUnderwater;
        return ((nMedium & 1) != 0 && !uwater) || ((nMedium & 2) != 0 && uwater);
    }

    public bool IsPostureMatch(int nPosture)
    {
        if (nPosture == 0) return true;
        if ((nPosture & 1) != 0 && Posture == Cd.PostureL) return true;
        if ((nPosture & 2) != 0 && Posture == Cd.PostureC) return true;
        if ((nPosture & 4) != 0 && Posture == Cd.PostureW) return true;
        if ((nPosture & 8) != 0 && Posture == Cd.PostureF) return true;
        return false;
    }

    /// <summary>GetDamage: the scale of a kind of damage from a source (0: none of it: an ignored source).</summary>
    public int GetDamage(Sprite src, int nDmgType)
    {
        int nDamage = DamageId[nDmgType];
        int nIgnore = DamageIgnoreSources;
        if (src == null || nIgnore == 0)
            return nDamage;
        bool play = src is Player, dude = Info.IsDude(src.Type), self = src == Spr;
        if (dude && !play && !self)
        {
            if ((nIgnore & Cd.DmgSourceKin) != 0 && src.HasX && src.Type == Spr.Type && src.Data1 == Spr.Data1)
                return 0;
            if ((nIgnore & Cd.DmgSourceSlave) != 0 && Slaves.Contains(src))
                return 0;
            if ((nIgnore & Cd.DmgSourceEnemy) != 0 && (Spr.XTarget == src || (src.HasX && src.XTarget == Spr)))
                return 0;
            if ((nIgnore & Cd.DmgSourceFriend) != 0 && src.HasX && src.XTarget == Spr.XTarget)
                return 0;
        }
        if ((nIgnore & Cd.DmgSourcePlayer) != 0 && play) return 0;
        if ((nIgnore & Cd.DmgSourceDude) != 0 && dude && !play && !self) return 0;
        if ((nIgnore & Cd.DmgSourceSelf) != 0 && self) return 0;
        return nDamage;
    }

    /// <summary>The scale of a kind of damage whoever deals it.</summary>
    public int GetDamage(int anySource, int nDmgType) => DamageId[nDmgType];

    /// <summary>The state and posture an AI state of this dude is (FindState).</summary>
    public bool FindState(AiState st, out int nStateType, out int nPosture)
    {
        nStateType = nPosture = 0;
        if (st == null || st.CdState < 0 || st.CdState >= Cd.StateMax || States[st.CdState, st.CdPosture] != st)
            return false;
        nStateType = st.CdState;
        nPosture = st.CdPosture;
        return true;
    }

    /// <summary>nnExtSprScaleSet: a sprite's repeats scaled (256: as they are).</summary>
    public static void SprScaleSet(Sprite e, int nScale)
    {
        e.XRepeat = Math.Clamp((int)Blood.MulScale8(e.XRepeat, nScale), 0, 255);
        e.YRepeat = Math.Clamp((int)Blood.MulScale8(e.YRepeat, nScale), 0, 255);
    }
}
