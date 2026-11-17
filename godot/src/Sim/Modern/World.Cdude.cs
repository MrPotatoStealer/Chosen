using System;
using System.Collections.Generic;
using Chosen.Data;
using Chosen.Sim.Modern;
using Godot;

namespace Chosen.Sim;

/// <summary>
/// The custom dudes' game side (NBlood's nnextcdud.cpp, CUSTOMDUDE's methods): a dude's state changes by posture, its
/// game frame (the damage of the wrong medium, postures: standing, crouching, swimming, flying; stomping, effects),
/// what damage does to it (changing target, burning, last stand, knockout, recoil, crouch, dodge), its death (morphing,
/// kamikaze, burning, drops, gibs, the death animation by damage kind and posture), its weapons' choice, its thrown
/// life leech and the dudes it summoned.
/// </summary>
public sealed partial class World
{
    // helpers the custom dude classes call
    public bool SpriteIsUnderwaterAny(Sprite e) => SpriteIsUnderwater(e, true);
    public void NnExtListSpritePublic(Sprite e) => NnExtListSprite(e);
    public static void NnExtOffsetPosPublic(double oX, double oY, double oZ, int nAng, ref double x, ref double y, ref double z) =>
        NnExtOffsetPos(oX, oY, oZ, nAng, ref x, ref y, ref z);
    public static bool SeqCanOverridePublic(int seq, int from, out bool xrp, out bool yrp, out bool plu) =>
        SeqCanOverride(seq, from, out xrp, out yrp, out plu);

    /// <summary>Is one of the sounds playing (on the sprite, or anywhere when null)? The client side answers.</summary>
    public Func<Sprite, int[], bool> SoundPlayingQuery;

    public bool SoundIsPlaying(Sprite e, int[] ids) => SoundPlayingQuery != null && SoundPlayingQuery(e, ids);

    /// <summary>nnExtGetStartHealth: a dude's start health (a player's template's, the dude's own data4, its kind's), as
    /// health units.</summary>
    public int NnExtGetStartHealth(Sprite e)
    {
        int n = 0;
        if (IsDudeSprite(e))
        {
            if (e is Player) n = Info.PlayerTemplate(Player.ModeHuman, Info.DI_startHealth);
            else if (e.HasX && e.Data4 != 0) n = e.Data4;
            else n = Info.Dude(e.Type, Info.DI_startHealth);
        }
        return Math.Min(n << 4, 65535);
    }

    /// <summary>An effect spawned for a sprite at a place, in the sprite's sector.</summary>
    public Sprite FxSpawnAt(int nFx, Sprite e, double x, double y, double z) => FxSpawnIn(nFx, SpriteBSector(e), x, y, z);

    /// <summary>nnExtGibSprite: a sprite gibbed at a place; the effects and things that made.</summary>
    public List<Sprite> NnExtGibSprite(Sprite e, int nGibType, double x, double y, double z)
    {
        var before = new HashSet<Sprite>();
        foreach (var f in Sprites)
            if (f.StatNum == K.kStatFX || f.StatNum == K.kStatThing)
                before.Add(f);
        GibSpriteAt(e, nGibType, x, y, z, 0, 0, 0);
        var made = new List<Sprite>();
        foreach (var f in Sprites)
            if (!f.Removed && (f.HitTag & K.kHitagFree) == 0 && (f.StatNum == K.kStatFX || f.StatNum == K.kStatThing) && !before.Contains(f))
                made.Add(f);
        return made;
    }

    /// <summary>nnExtFixDudeDrag: a dude off the floor slowed as on it.</summary>
    void NnExtFixDudeDrag(Sprite e, int nDrag)
    {
        if (e.XVel == 0 && e.YVel == 0)
            return;
        double fz = FloorZOf(e);
        var (_, zb) = Extents(e);
        if (Blood.Sar(fz - zb, 8) >= 0x100)
        {
            double d = 0x2a00 - Blood.Int(0x2a00 * (double)nDrag / 0x100);
            e.XVel -= Blood.MulScale16R(e.XVel, d);
            e.YVel -= Blood.MulScale16R(e.YVel, d);
            if (Blood.ApproxDist(e.XVel, e.YVel) < 0x1000)
                e.XVel = e.YVel = 0;
        }
    }

    // ---------------------------------------------------------------- weapons

    /// <summary>What a weapon is picked by (ARG_PICK_WEAPON).</summary>
    public struct CdPickArg
    {
        public int Angle, Distance, Height, Slope, DudeHealth, TargHealth, TargBurnTime;
    }

    CdPickArg CdMakePickArg(Sprite e, Sprite targ, int nDist, int nAng, int nSlope)
    {
        int Perc(Sprite s) => (int)Math.Min(KM.kPercFull * s.XHealth / Math.Max(NnExtGetStartHealth(s), 1), 255);
        var a = new CdPickArg { Distance = nDist, Angle = nAng, DudeHealth = Perc(e), TargHealth = Perc(targ), TargBurnTime = targ.BurnTime / 100 };
        var (zt, zb) = Extents(e);
        if (!(targ.BZ >= zt && targ.BZ <= zb))
        {
            a.Slope = nSlope;
            if (targ.BZ < zt) a.Height = (int)Math.Abs(targ.BZ - zt);
            else if (targ.BZ > zb) a.Height = -(int)Math.Abs(targ.BZ - zb);
        }
        return a;
    }

    /// <summary>AdjustSlope(target, zOffs): the slope to a target.</summary>
    int CdAdjustSlope(CustomDude d, Sprite target, double zOffs)
    {
        if (target == null)
            return 0;
        var e = d.Spr;
        return (int)Blood.DivScale10(target.BZ - e.BZ - zOffs, Blood.ApproxDist(target.BX - e.BX, target.BY - e.BY));
    }

    /// <summary>AdjustSlope(dist): the slope that hits the target, tried from far to near.</summary>
    bool CdAdjustSlopeDist(CustomDude d, int nDist, ref int nSlope)
    {
        var e = d.Spr;
        if (e.XTarget is not { } target || d.Weapon == null)
            return false;
        var (zTop, zBot) = Extents(target);
        int nStep = (int)Math.Max(Blood.Sar(Math.Abs(zTop - zBot), 4), 10);
        while (nDist > 0)
        {
            nSlope = (int)Blood.DivScale10(target.BZ - e.BZ, nDist);
            HitScan(e, e.BZ, Blood.Sar(Blood.Cos(e.Ang), 16), Blood.Sar(Blood.Sin(e.Ang), 16), nSlope, nDist, CdClipMask(d.Weapon));
            if (HitType == 3 && HitEnt == target)
                return true;
            nDist -= nStep;
        }
        return false;
    }

    static int CdClipMask(CdWeapon w) => w.ClipMask == 1 ? Sim.Collision.Contents.MaskShot : Sim.Collision.Contents.MaskBlock;

    /// <summary>PickWeapon: the weapons that can be used now are marked, one of them picked by chance (else the
    /// default one).</summary>
    CdWeapon CdPickWeapon(CustomDude d, CdPickArg a)
    {
        CdWeapon defl = null, retn = null;
        d.NumAvailWeapons = 0;
        for (int i = 0; i < d.NumWeapons; i++)
        {
            var wp = d.Weapons[i];
            wp.Available = false;
            if (!wp.CooldownDelay.Pass(this))
                continue;
            if (wp.CooldownTotalUseCount != 0 && wp.CooldownUseCount >= wp.CooldownTotalUseCount)
                wp.CooldownUseCount = 0;
            if (a.Angle >= wp.Angle) continue;
            if (!Cd.RngOk(a.Distance, wp.DistRange[0], wp.DistRange[1])) continue;
            if (!Cd.IRngOk(a.Height, wp.HeighRange[2], wp.HeighRange[3]) && !Cd.IRngOk(a.Height, wp.HeighRange[0], wp.HeighRange[1])) continue;
            if (!wp.HaveAmmo) continue;
            if (!d.IsMediumMatch(wp.Medium)) continue;
            if (!d.IsPostureMatch(wp.Posture)) continue;
            if (!Cd.IRngOk(a.DudeHealth, wp.DudeHpRange[0], wp.DudeHpRange[1])) continue;
            if (!Cd.IRngOk(a.TargHealth, wp.TargHpRange[0], wp.TargHpRange[1])) continue;
            if (!Cd.IRngOk(a.Slope, wp.SlopeRange[0], wp.SlopeRange[1])) continue;
            if (!Cd.IRngOk(a.TargBurnTime, wp.TargBurnRange[0], wp.TargBurnRange[1])) continue;
            if (!CdCheckUnits(d, wp)) continue;
            if (wp.RealCheck && CdudeWeaponShot(d, wp, true) < 0) continue;
            wp.Available = true;        // for akimbo at least
            d.NumAvailWeapons++;
            if (retn == null)
            {
                if (Chance(wp.PickChance)) retn = wp;
                else if (defl == null && wp.IsDefault) defl = wp;
            }
        }
        return retn ?? defl;
    }

    static readonly int[] _cdUnitStats = { K.kStatDude, K.kStatThing, K.kStatProjectile, K.kStatFlare };

    /// <summary>CheckUnits: are fewer of the weapon's objects (missiles, things, summoned dudes) about than it allows?</summary>
    bool CdCheckUnits(CustomDude d, CdWeapon wp)
    {
        if (wp.MaxUnitsShared == 0 && wp.MaxUnitsThis == 0)
            return true;
        int current = 0, shared = 0;
        for (int i = _cdUnitStats.Length - 1; i >= 0; i--)
        {
            foreach (var u in StatList(_cdUnitStats[i]))
            {
                var own = u.Owner;
                if (own == null || own.Removed || own.Type != KM.kDudeModernCustom)
                    continue;
                bool ok = false;
                if (u.StatNum == K.kStatDude || u.StatNum == K.kStatThing)
                {
                    if (u.HasX && u.XHealth != 0 && (wp.Id == u.Type || (IsCustomDude(u) && u.Data1 == wp.Id)))
                        ok = true;
                }
                else if (wp.Id == u.Type)
                    ok = true;
                if (!ok)
                    continue;
                shared++;
                if (own == d.Spr)
                    current++;
                if (wp.MaxUnitsShared != 0 && shared >= wp.MaxUnitsShared) return false;
                if (wp.MaxUnitsThis != 0 && current >= wp.MaxUnitsThis) return false;
            }
        }
        return true;
    }

    bool CdIsTooTight(CustomDude d)
    {
        var e = d.Spr;
        if (SpriteBSector(e) < 0)
            return false;
        var zr = GetZRange(e, e.ClipDist << 2);
        return Math.Abs(zr.FloorZ - zr.CeilZ) - 256 < d.Height;
    }

    // ---------------------------------------------------------------- flight

    int CdGetMaxFlyHeigh(CustomDude d, bool targClip)
    {
        var e = d.Spr;
        int sect = SpriteBSector(e);
        if (sect < 0)
            return 0;
        Board.GetZsOfSlope(sect, e.BX, e.BY, out double cz, out double fz);
        int nSHeigh = (int)Math.Abs(fz - cz) - d.FlyCfDist;
        if (nSHeigh <= 0)
            return 0;
        int percHg = 100 - (int)((long)d.Height * 100 / nSHeigh);
        if (percHg <= 0)
            return 0;
        cz = fz - Cd.Perc2Val(percHg, nSHeigh);
        nSHeigh = (int)Math.Abs(fz - cz);
        if (targClip && e.XTarget is { } targ && Blood.ApproxDist(e.TargetX - e.BX, e.TargetY - e.BY) <= d.FlyClipDist)
        {
            int ts = SpriteBSector(targ);
            if (ts >= 0 && ts != sect)
            {
                Board.GetZsOfSlope(ts, targ.BX, targ.BY, out double cz2, out double fz2);
                cz = Math.Max(cz, cz2);
                fz = Math.Min(fz, fz2);
                if (cz == cz2 || fz == fz2)
                {
                    int t = (int)Math.Abs(fz - cz) - d.FlyCfDist;
                    if (t > 0)
                    {
                        percHg = 100 - (int)((long)d.Height * 100 / t);
                        if (percHg > 0)
                        {
                            cz = fz - Cd.Perc2Val(percHg, t);
                            nSHeigh = (int)Math.Abs(fz - cz);
                        }
                    }
                }
            }
        }
        return Math.Min(nSHeigh, d.FlyMaxHeight);
    }

    int CdGetStartFlyVel(CustomDude d)
    {
        int nHeigh = CdGetMaxFlyHeigh(d, d.FlyClipDist != 0);
        return (int)Math.Max(Blood.MulScale16(nHeigh, Cd.FlyStartZvel), Cd.FlyStartZvel);
    }

    // ---------------------------------------------------------------- states

    void CdChangePosture(CustomDude d, int nNewPosture)
    {
        if (d.Posture == nNewPosture)
            return;
        var e = d.Spr;
        switch (nNewPosture)
        {
            case Cd.PostureL:
            case Cd.PostureC:
                d.Posture = nNewPosture;
                if (d.IsFlipped)
                {
                    e.HitTag &= ~(K.kPhysGravity | K.kPhysFalling);
                    e.HitTag |= K.kPhysMove;
                }
                else
                    e.HitTag |= K.kPhysMove | K.kPhysGravity | K.kPhysFalling;
                break;
            case Cd.PostureW:
                d.Posture = nNewPosture;
                e.HitTag |= K.kPhysMove | K.kPhysGravity;
                break;
            case Cd.PostureF:
                d.Posture = nNewPosture;
                e.HitTag &= ~(K.kPhysGravity | K.kPhysFalling);
                e.HitTag |= K.kPhysMove;
                break;
            default:
                return;
        }
        CdSyncState(d);     // the animations follow the posture
    }

    /// <summary>NewState(pState): the dude enters a state (not while it morphs).</summary>
    bool CdNewState(CustomDude d, AiState st)
    {
        if (st == null || d.IsMorphing)
            return false;
        var e = d.Spr;
        e.StateTimer = st.Ticks;
        e.AiState = st;
        if (st.Seq > 0)
            Seq.Spawn(this, st.Seq, e, st.SeqCallback);
        else if (st.Seq == 0)
            Seq.Kill(e);
        st.Enter?.Invoke(e);
        return true;
    }

    /// <summary>NewState(type): the state of the dude's posture when that has an animation, else the standing one.</summary>
    void CdNewState(CustomDude d, int nStateType, int nTimeOverride = -1)
    {
        var tmp = d.States[nStateType, d.Posture];
        var st = tmp.Seq > 0 ? tmp : d.States[nStateType, Cd.PostureL];
        if (CdNewState(d, st) && nTimeOverride >= 0)
            d.Spr.StateTimer = nTimeOverride;
    }

    void CdNextState(CustomDude d, AiState st, int nTimeOverride = 0)
    {
        if (d.Spr.AiState == null)
            return;
        d.Spr.AiState.Next = st;
        if (st != null && nTimeOverride > 0)
            st.Ticks = nTimeOverride;
    }

    void CdNextState(CustomDude d, int nStateType, int nTimeOverride = 0) =>
        CdNextState(d, nStateType >= 0 ? d.States[nStateType, d.Posture] : null, nTimeOverride);

    void CdSyncState(CustomDude d)
    {
        var e = d.Spr;
        if (e.AiState != null && d.FindState(e.AiState, out int t1, out int t2) && t2 != d.Posture)
        {
            int time = e.StateTimer;
            var st = d.States[t1, d.Posture];
            CdNewState(d, st);
            if (st.Ticks != 0)
                e.StateTimer = time;
        }
    }

    // ---------------------------------------------------------------- the dude's life

    /// <summary>InitSprite: the dude as it starts (or respawns): its flags, posture by where it is, its first state.</summary>
    void CdInitSprite(CustomDude d)
    {
        var e = d.Spr;
        if (d.StatusTest(Cd.StatusRespawn))
        {
            SpriteSectorForget(e);
            d.StatusRem(Cd.StatusAwaked);
            d.StatusRem(Cd.StatusRespawn);
            d.StatusRem(Cd.StatusBurning);
            d.StatusRem(Cd.StatusDying);
            d.StatusRem(Cd.StatusLStand);
            d.StatusRem(Cd.StatusLStandAwaked);
            if (d.StatusTest(Cd.StatusFlipped))
            {
                e.Cstat |= Cstat.YFlip;
                d.StatusRem(Cd.StatusFlipped);
            }
        }
        e.Cstat |= 4096 | Cstat.Hitscan | Cstat.Block;
        e.HitTag |= K.kPhysMove | K.kPhysGravity | K.kPhysFalling;
        d.Posture = Cd.PostureL;
        d.GoalZ = (int)e.BZ;
        if (e.XScale != 0)
            CustomDude.SprScaleSet(e, e.XScale);
        if (d.IsBurning)
        {
            CdNewState(d, Cd.BurnStateSearch);
            e.BurnTime = 1200;
        }
        else
        {
            e.DudeActive = 0;
            e.ThinkTime = 0;
            if (SpriteIsUnderwater(e))
            {
                e.Medium = K.kMediumWater;
                CdChangePosture(d, Cd.PostureW);
            }
            else
            {
                e.Medium = K.kMediumNormal;
                if (d.CanFly())
                {
                    if (d.CanStand())
                    {
                        var (_, zb) = Extents(e);
                        if (FloorZOf(e) - 0x1000 > zb)
                            CdChangePosture(d, Cd.PostureF);
                    }
                    else
                        CdChangePosture(d, Cd.PostureF);
                }
            }
            if (d.IsKnockout) CdNewState(d, Cd.StateKnock);
            else if (d.IsMorphing) CdNewState(d, Cd.StateMorph);
            else if (d.CanSleep()) CdNewState(d, Cd.StateSleep);
            else
            {
                if (d.IsStanding && (d.StatusTest(Cd.StatusForceCrouch) || CdIsTooTight(d)))
                {
                    if (d.GetStateSeq(Cd.StateIdle, Cd.PostureC) != 0)
                        CdChangePosture(d, Cd.PostureC);
                    else
                        d.StatusRem(Cd.StatusForceCrouch);
                }
                CdNewState(d, Cd.StateIdle);
            }
        }
        if (d.IsFlipped) e.HitTag &= ~(K.kPhysGravity | K.kPhysFalling);
        else if (!d.IsFlying) e.HitTag |= K.kPhysGravity | K.kPhysFalling;
        ClampSprite(e);
        e.Visible = (e.Cstat & Cstat.Invisible) == 0;
        UpdateBox(e);
    }

    /// <summary>Activate: the dude wakes: chasing or searching (burning: its burning search; asleep: its waking).</summary>
    void CdActivate(CustomDude d)
    {
        if (d.IsDying)
            return;
        var e = d.Spr;
        if (!d.IsBurning)
        {
            if (!d.IsMorphing && !d.IsKnockout)
            {
                if (!d.IsSleeping)
                {
                    if (e.DudeActive == 0 && d.StatusTest(Cd.StatusForceCrouch) && d.IsCrouching)
                        CdChangePosture(d, Cd.PostureL);
                    e.DudeActive = 1;
                    CdNewState(d, e.XTarget != null ? Cd.StateChase : Cd.StateSearch);
                }
                else
                {
                    CdNewState(d, Cd.StateWake);
                    d.StatusSet(Cd.StatusAwaked);       // awake once, asleep no more (until it respawns)
                }
            }
        }
        else
            CdNewState(d, Cd.BurnStateSearch);
    }

    /// <summary>Process: the dude's game frame.</summary>
    void CdProcess(CustomDude d)
    {
        var e = d.Spr;
        var st = e.AiState;
        st?.MoveFn?.Invoke(e);
        if (e.Removed)
            return;
        st = e.AiState;
        if (st?.Think != null && d.IsThinkTime(this))
            st.Think(e);
        if (e.Removed || e.Custom != d)
            return;
        if (!d.IsMorphing && !d.IsDying)
        {
            if (e.XLocked == 0 && !d.IsMediumMatch(d.Medium))
            {
                // the wrong medium hurts
                bool water = d.IsUnderwater;
                int amount = water ? d.DmgWaterAmount : d.DmgLandAmount;
                var delay = water ? d.DmgWaterDelay : d.DmgLandDelay;
                if (amount != 0 && delay.Pass(this))
                {
                    int nDType = water ? K.kDamageDrown : (d.IsBurning ? K.kDamageBurn : K.kDamageFall);
                    e.XHealth = Math.Max(e.XHealth - amount, 0);
                    e.CumulDamage += amount;
                    if (e.XHealth > 0)
                    {
                        CdDamage(d, e, nDType, amount);
                        delay.Set(this);
                        if (e.DudeActive == 0)
                            AiActivateDude(e);
                    }
                    else
                    {
                        CdKill(d, e, nDType, amount);
                        return;
                    }
                }
            }
            if (d.IsFlipped)
            {
                // on the ceiling
                int nVel = d.GetVelocity(Cd.VelZ);
                NnExtFixDudeDrag(e, 16);
                var (zt, _) = Extents(e);
                double cz = CeilZOf(e);
                if (e.ZVel <= 0 && Math.Abs(cz - zt) > 0)
                    e.ZVel -= nVel;
                else
                    e.ZVel = 0;
            }
            CdSlavesUpdate(d);
            if (e.DudeActive != 0)
                CdProcessPosture(d);
        }
        if (d.DmgStompAmount != 0 && (d.IsStanding || d.IsCrouching))
        {
            if (e.FlorHit == Hit.Sprite && d.DmgStompDelay.Pass(this) && e.FlorHitEnt is Sprite hs && DudeIsAlive(hs))
            {
                d.DmgStompDelay.Set(this);
                ActDamageSprite(e, hs, K.kDamageFall, d.DmgStompAmount);
            }
        }
        if (e.Removed)
            return;
        st = e.AiState;
        if (st?.Next != null && e.StateTimer == 0)
        {
            if (st.Ticks > 0 || !d.SeqPlaying)
                CdNewState(d, st.Next);
        }
        if (d.NumEffects != 0)
            CdProcessEffects(d);
    }

    /// <summary>ProcessPosture: into the water, down to a crouch and up again, landing and taking off.</summary>
    void CdProcessPosture(CustomDude d)
    {
        var e = d.Spr;
        bool tight = false;
        if (d.IsUnderwater)
        {
            if (!d.IsSwimming)
            {
                CdChangePosture(d, Cd.PostureW);
                if (e.BurnTime != 0)
                {
                    EvKillCallback(e, K.kCallbackFXFlameLick);
                    e.BurnTime = 0;
                }
            }
        }
        else if (d.IsCrouching)
        {
            if (d.TimerCrouch.Pass(this))
            {
                if (!CdIsTooTight(d))
                {
                    CdNewState(d, Cd.StateSearch);
                    CdChangePosture(d, Cd.PostureL);
                }
                d.TimerCrouch.Clear();
            }
        }
        else if (d.CanCrouch() && ((tight = CdIsTooTight(d)) || d.TimerCrouch.Exists))
        {
            if (tight)
            {
                CdChangePosture(d, Cd.PostureC);
                if (!d.TimerCrouch.Exists)
                    d.TimerCrouch.Set(this);
            }
            else if (e.ZVel >= -0x1000 && d.GetDistToFloor() <= Cd.CrouchDist)
                CdChangePosture(d, Cd.PostureC);
            else
                d.TimerCrouch.Clear();
        }
        else if (d.IsFlying)
        {
            if (d.CanStand() && !d.IsAttacking && !d.IsRecoil && d.TimerFLaunch.Pass(this))
            {
                // near the floor for a while: it lands
                if (d.GetDistToFloor() <= Cd.LandDist)
                {
                    if (e.ZVel >= 0 && !d.TimerFloor.Exists)
                        d.TimerFloor.Set(this, 8 + Random(8));
                    if (d.TimerFloor.Exists && d.TimerFloor.Pass(this))
                    {
                        CdChangePosture(d, Cd.PostureL);
                        d.TimerFloor.Clear();
                    }
                }
            }
        }
        else if (d.CanFly())
        {
            if (!d.IsAttacking && !d.IsRecoil)
            {
                // falling far from the floor for a while: it flies
                if (e.ZVel > 0x2000 && d.GetDistToFloor() > Cd.LaunchDist)
                {
                    if (!d.TimerFloor.Exists)
                        d.TimerFloor.Set(this, 12 + Random(8));
                    else if (d.TimerFloor.Pass(this))
                    {
                        e.ZVel = CdGetStartFlyVel(d);
                        CdChangePosture(d, Cd.PostureF);
                        d.TimerFLaunch.Set(this);
                        d.TimerFloor.Clear();
                    }
                }
                else
                    CdChangePosture(d, Cd.PostureL);
            }
        }
        else if (!d.IsStanding)
            CdChangePosture(d, Cd.PostureL);
    }

    /// <summary>ProcessEffects: the dude's effects, while a player is near or sees it.</summary>
    void CdProcessEffects(CustomDude d)
    {
        var e = d.Spr;
        foreach (var p in Players)
        {
            if (!p.Ready || p.Removed)
                continue;
            if (!CheckProximityPoint(p.BX, p.BY, p.BZ, e.BX, e.BY, e.BZ, 2048) && !CanSee(e.BX, e.BY, e.BZ, p.BX, p.BY, p.BZ, e, p))
                continue;
            for (int j = 0; j < d.NumEffects; j++)
            {
                var eff = d.Effects[j];
                if (!eff.Delay.Pass(this) || !d.IsPostureMatch(eff.Posture) || !d.IsMediumMatch(eff.Medium))
                    continue;
                if (eff.States.Count != 0)
                {
                    if (AiInPatrolState(e.AiState))
                    {
                        int pt = e.AiState.Type;
                        if ((pt == kAiStatePatrolMove || pt == kAiStatePatrolTurn) && eff.States.Contains(Cd.StateMove)) { }
                        else if (pt == kAiStatePatrolIdle && eff.States.Contains(Cd.StateIdle)) { }
                        else continue;
                    }
                    else if (!d.FindState(e.AiState, out int nS, out _) || !eff.States.Contains(nS))
                        continue;
                }
                if (eff.CanSpawn(this, e))
                {
                    eff.Spawn(this, e);
                    eff.Delay.Set(this);
                }
            }
            break;
        }
    }

    /// <summary>Damage: what a hit does to a living dude beside the health it takes; the damage dealt.</summary>
    double CdDamage(CustomDude d, Sprite from, int nDmgType, double nDmg)
    {
        var e = d.Spr;
        bool selfDmg = from == e;
        double nCDmg = e.CumulDamage;
        double Amount(CdEventDamage ev) => ev.Cumulative ? nCDmg : nDmg;
        if (from != null && e.XTarget != from && !selfDmg)
        {
            var ev = d.ChgTarg.OnDamage[nDmgType];
            int nChance = ev.Chance;
            if (nChance > 0 && DudeIsAlive(from))
            {
                if (IsCustomDude(from) && from.Data1 == e.Data1)
                    nChance = 0;        // its kin: maybe not
                if (from.XTarget == e)
                    nChance += Cd.ChanceMax >> 4;       // but more so when it is that one's target
                if (nChance > 0 && ev.Allow(this, e, Amount(ev), nChance))
                {
                    AiSetTarget(e, from);
                    e.GoalAng = Blood.GetAngle(e.TargetX - e.BX, e.TargetY - e.BY);
                }
            }
        }
        if (e.DudeActive == 0)
            AiActivateDude(e);
        if (d.IsBurning)
        {
            if (d.IsUnderwater)
            {
                e.BurnSource = null;
                e.BurnTime = 0;
                e.XHealth = 1;
                CdNewState(d, Cd.StateSearch);
            }
            else if (e.BurnTime <= 0)
            {
                d.Sound[Cd.SndBurning].Play(this, e);
                e.BurnTime = 1200;
            }
        }
        else if (nDmgType == K.kDamageBurn)
        {
            if (e.XHealth <= d.FleeHealth)
            {
                e.XHealth = 0;      // actDamageSprite kills it then
                return nDmg;
            }
        }
        else
        {
            if (d.IsLastStand)
            {
                if (nDmgType == K.kDamageTesla)
                    e.TeslaHit = 1;
                CdKill(d, from, nDmgType, nDmg);
                return nDmg;
            }
            if (!selfDmg && d.CanLastStand())
            {
                var ev = d.LStand.OnDamage[nDmgType];
                if (ev.Allow(this, e, Amount(ev)))
                {
                    d.StatusSet(Cd.StatusLStand);
                    d.StatusSet(Cd.StatusLStandAwaked);     // once only
                    CdNewState(d, Cd.StateKnockEnter);
                    CdNextState(d, Cd.StateKnock, ev.PickTime(this));
                    d.Sound[Cd.SndLStandEnter].Play(this, e);
                    e.TeslaHit = 0;
                    e.XHealth = 16;
                    return 0;
                }
            }
            if (e.XHealth != 0)
            {
                if (d.IsKnockout)
                {
                    e.TeslaHit = 0;
                    return nDmg;
                }
                if (d.CanKnockout())
                {
                    var ev = d.Knockout.OnDamage[nDmgType];
                    if (ev.Allow(this, e, Amount(ev)))
                    {
                        CdNewState(d, Cd.StateKnockEnter);
                        CdNextState(d, Cd.StateKnock, ev.PickTime(this));
                        d.Sound[Cd.SndGotHit].Play(this, e);
                        e.TeslaHit = 0;
                        return nDmg;
                    }
                }
                if (nDmgType == K.kDamageTesla)
                    e.TeslaHit = 1;
                if (d.CanRecoil() || (e.TeslaHit != 0 && d.CanElectrocute()))
                {
                    var ev = d.Recoil.OnDamage[nDmgType];
                    if (ev.Allow(this, e, Amount(ev)))
                        CdRecoil(d, ev.PickTime(this));
                }
                if (!selfDmg && ((d.IsStanding && d.CanCrouch()) || d.IsCrouching))
                {
                    var ev = d.Crouch.OnDamage[nDmgType];
                    if (ev.Allow(this, e, Amount(ev)))
                        d.TimerCrouch.Set(this, ev.PickTime(this));     // ProcessPosture does the rest
                }
                var dv = d.Dodge.OnDamage[nDmgType];
                if (!selfDmg && d.CanMove() && dv.Allow(this, e, Amount(dv)))
                {
                    int nTime = dv.PickTime(this);
                    if (nTime == 0 && (nTime = 30 * Random(2)) == 0)
                        nTime = 90;
                    if (d.IsRecoil)
                        CdNextState(d, Cd.StateDodge, nTime);
                    else
                    {
                        if (Chance(0x0500))
                            d.Sound[Cd.SndGotHit].Play(this, e);
                        CdNewState(d, Cd.StateDodge, nTime);
                    }
                }
                else if (d.IsRecoil)
                    CdNextState(d, Cd.StateChase);
            }
        }
        e.TeslaHit = 0;
        return nDmg;
    }

    void CdRecoil(CustomDude d, int nStateTime = 0)
    {
        var e = d.Spr;
        int nState = -1;
        if (!d.IsKnockout)
        {
            if (d.CanRecoil())
                nState = Cd.StateRecoil;
            if (e.TeslaHit != 0 && d.CanElectrocute() && !d.IsUnderwater)
                nState = Cd.StateRecoilT;
            d.Sound[Cd.SndGotHit].Play(this, e);
            if (nState >= 0)
                CdNewState(d, nState, nStateTime);
        }
        e.TeslaHit = 0;
    }

    /// <summary>PickDeath: the death state for a kind of damage: the posture's own, a random posture's, any there is,
    /// the default death.</summary>
    AiState CdPickDeath(CustomDude d, int nDmgType)
    {
        int nRand = Random(Cd.PostureMax);
        AiState death;
        if (d.IsLastStand)
        {
            death = d.States[Cd.StateDeathLStand, d.Posture];
            if (death.Type == Cd.StateTypeDeathPosture && death.Seq > 0)
                return death;
            death = d.States[Cd.StateDeathLStand, nRand];
            if (death.Seq > 0)
                return death;
        }
        death = d.States[Cd.StateDeathBase + nDmgType, d.Posture];
        if (death.Type == Cd.StateTypeDeathPosture)
            return death.Seq > 0 ? death : d.States[Cd.StateDeathBase, d.Posture];
        death = d.States[Cd.StateDeathBase + nDmgType, nRand];
        if (death.Seq > 0)
            return death;
        for (int i = 0; i < Cd.PostureMax; i++)
            if (d.States[Cd.StateDeathBase + nDmgType, i].Seq > 0)
                return d.States[Cd.StateDeathBase + nDmgType, i];
        death = d.States[Cd.StateDeathBase, nRand];
        if (death.Type == Cd.StateTypeDeathPosture)
            return d.States[Cd.StateDeathBase, d.Posture];
        return death;
    }

    /// <summary>Kill: the dude dies (or turns into its next form, or starts burning, or blows itself up).</summary>
    void CdKill(CustomDude d, Sprite from, int nDmgType, double nDmg)
    {
        var e = d.Spr;
        if (d.IsDying)
            return;
        for (int i = 0; i < Cd.SndMax; i++)
            d.Sound[i].Kill(this, e);
        if (!d.IsLastStand)
        {
            if (d.NextDude != -1 || (d.NextDude = d.MorphId[nDmgType]) != -1)
            {
                // kept alive: it morphs
                e.XHealth = Math.Max(e.XHealth, 16);
                if (!d.IsMorphing)
                    CdNewState(d, Cd.StateMorph);
                return;
            }
        }
        if (nDmgType == K.kDamageDrown && !d.IsSwimming && SpriteIsUnderwater(e))
            CdChangePosture(d, Cd.PostureW);        // for its choking death
        if (d.Weapon != null && d.Weapon.Type == Cd.WeaponKamikaze)
        {
            if (!d.IsAttacking)
            {
                bool explode = nDmgType == K.kDamageBurn || nDmgType == K.kDamageExplode || nDmgType == K.kDamageBullet
                    || (nDmgType != K.kDamageSpirit && Chance(0x2000));
                if (explode)
                {
                    e.XHealth = 0;      // killed once
                    CdudeDoExplosion(d);
                }
            }
        }
        else if (nDmgType == K.kDamageBurn)
        {
            if (!d.IsUnderwater)
            {
                if (d.CanBurn() && !d.IsBurning)
                {
                    EvKillCallback(e, K.kCallbackFXFlameLick);
                    Sound3D(e, 361, -1, -1);
                    d.StatusSet(Cd.StatusBurning);
                    d.Sound[Cd.SndBurning].Play(this, e);
                    e.XHealth = Cd.BurningHealth;
                    CdNewState(d, Cd.BurnStateSearch);
                    d.DamageSet(256, K.kDamageBurn);
                    return;
                }
            }
            else
            {
                e.BurnTime = 0;
                e.BurnSource = null;
                nDmgType = K.kDamageDrown;
            }
        }
        else if (d.IsBurning)
            nDmgType = K.kDamageBurn;
        CdClearEffectCallbacks(d);
        CdLeechKill(d, true);
        CdDropItems(d);
        if (d.SlavesKillOnDeath)
            CdSlavesKill(d);
        if (GameType != K.kGameTypeSinglePlayer)
        {
            foreach (var p in Players)
            {
                if (p.DeathTime > 0 && p.FraggerId == e)
                    p.FraggerId = null;
                if (GameType == K.kGameTypeCoop && from != null && from.Type == p.Type)
                    p.FragCount++;
            }
        }
        TrTriggerSprite(e, K.kCmdOff, from);
        e.HitTag |= K.kPhysGravity | K.kPhysFalling | K.kPhysMove;
        if (nDmgType == K.kDamageExplode)
            d.Sound[Cd.SndDeathExplode].Play(this, e);
        else if (nDmgType == K.kDamageBurn)
            Sound3D(e, 351, -1);
        if (d.IsBurning)
        {
            d.Sound[Cd.SndDeathExplode].Play(this, e);
            if (Chance(0x4000))
            {
                var (top, _) = Extents(e);
                GibSpriteAt(e, K.GIBTYPE_7, e.BX, e.BY, top, Blood.Sar(e.XVel, 1), Blood.Sar(e.YVel, 1), -0xccccc);
                for (int i = Math.Max(Random(3), 1) - 1; i >= 0; i--)
                    FxSpawnBlood(e, nDmg);
            }
        }
        else
            d.Sound[Cd.SndDeathNormal].Play(this, e);
        if (d.IsUnderwater)
            EvPostCallback(e, 0, K.kCallbackEnemeyBubble);
        if (nDmgType == K.kDamageExplode)
        {
            int n = 0;
            for (int i = 0; i < 3; i++, n++)
            {
                int gib = Info.Dude(KM.kDudeModernCustom, Info.DI_gibType + i);
                if (gib >= 0)
                    GibSprite(e, gib);
            }
            while (--n > 0)
                FxSpawnBlood(e, nDmg);
        }
        e.Type = KM.kDudeModernCustom;
        KillMgrAddKill(e);
        e.XHealth = 0;
        if (ActCheckRespawn(e))
        {
            // stays a dude
            ActPostSprite(e, K.kStatDude);
            d.StatusSet(Cd.StatusRespawn);
        }
        else
            e.DropMsg = 0;      // no second drop
        if ((e.Cstat & Cstat.YFlip) != 0)
            d.StatusSet(Cd.StatusFlipped);
        e.Cstat &= ~(Cstat.Block | Cstat.Hitscan | Cstat.YFlip);
        e.Cstat |= Cstat.Invisible;
        Seq.Kill(e);
        var death = CdPickDeath(d, nDmgType);
        d.StatusSet(Cd.StatusDying);
        CdNewState(d, death);
        e.Visible = (e.Cstat & Cstat.Invisible) == 0;
        UpdateBox(e);
    }

    /// <summary>DropItems: its key, its sprite's drop item (by chance), the descriptor's items (the certain ones, and
    /// the first of the others chance gives).</summary>
    void CdDropItems(CustomDude d)
    {
        var e = d.Spr;
        var items = new List<int>();
        void Add(int item)
        {
            if (!items.Contains(item))
                items.Add(item);
        }
        if (e.XKey != 0)
            Add(K.kItemKeyBase + e.XKey - 1);
        if (e.DropMsg != 0 && Chance(d.SprDropItemChance))
            Add(e.DropMsg);
        int i;
        for (i = 0; i < Cd.MaxDropItems; i++)
            if (d.DropItems[i, 0] != 0 && d.DropItems[i, 1] >= 100)
                Add(d.DropItems[i, 0]);
        while (--i >= 0)
        {
            int item = d.DropItems[i, 0], perc = d.DropItems[i, 1];
            if (item != 0 && perc < 100 && Chance(Cd.Perc2Val(perc, Cd.ChanceMax)))
            {
                Add(item);
                break;
            }
        }
        foreach (int item in items)
        {
            var drop = ActDropObject(e, item);
            if (drop != null && d.IsUnderwater)
            {
                // it sinks
                drop.SetBZ(e.BZ);
                if (!drop.HasX)
                    DbInsertXSprite(drop);
                drop.PhysAttr |= K.kPhysGravity | K.kPhysFalling;
                if (!_physSprites.Contains(drop))
                    _physSprites.Add(drop);
                GetSpriteMassBySize(drop);
            }
        }
    }

    internal void CdClearEffectCallbacks(CustomDude d)
    {
        foreach (int cb in Cd.CustomCallback)
            EvKillCallback(d.Spr, cb);
    }

    /// <summary>LeechPickup: the dude takes its thrown life leech back.</summary>
    void CdLeechPickup(CustomDude d)
    {
        if (d.Leech is not { } leech)
            return;
        var fx = FxSpawnIn(K.FX_52, SpriteBSector(leech), leech.BX, leech.BY, leech.BZ);
        if (fx != null)
        {
            fx.Ang = leech.Ang;
            fx.Cstat = Cstat.YCenter;
            fx.XRepeat = fx.YRepeat = 64 + Random(50);
            fx.Pal = 6;
        }
        Sound3DCP(leech, 490, -1, 0, 60000, 0);
        ActPostSprite(leech, K.kStatFree);
        d.Leech = null;
    }

    /// <summary>LeechKill: the dude's leech is broken (and with it the weapon).</summary>
    internal void CdLeechKill(CustomDude d, bool delSpr)
    {
        if (d.Leech is { } leech)
        {
            leech.Cstat &= ~(Cstat.Block | Cstat.Hitscan);
            leech.Cstat |= Cstat.Invisible;
            leech.HitTag = 0;
            leech.XHealth = 1;
            leech.XLocked = 1;
            leech.XProximity = 0;
            leech.Visible = false;
            UpdateBox(leech);
            if (delSpr)
                ActPostSprite(leech, K.kStatFree);
        }
        for (int i = 0; i < d.NumWeapons; i++)
        {
            var wp = d.Weapons[i];
            if (wp.Id == KM.kModernThingEnemyLifeLeech)
            {
                wp.AmmoTotal = 1;
                wp.AmmoSet(0);
            }
        }
    }

    /// <summary>SlavesUpdate: the summoned dudes follow their master's target (or call it to theirs).</summary>
    void CdSlavesUpdate(CustomDude d)
    {
        var e = d.Spr;
        for (int i = d.Slaves.Count - 1; i >= 0; i--)
        {
            var slave = d.Slaves[i];
            if (IsDudeSprite(slave) && XSprIsFine(slave) && slave.Owner == e && slave.XHealth > 0)
            {
                if (!d.SlavesNoSetTarget)
                {
                    if (e.XTarget == null)
                    {
                        if (slave.XTarget == null)
                            AiSetTargetXYZ(slave, e.BX, e.BY, e.BZ);        // back to the master
                        else if (e.AiState != null && e.AiState.Type == AiState.Idle)
                        {
                            // the master called
                            var t = slave.XTarget;
                            AiSetTargetXYZ(e, t.BX, t.BY, t.BZ);
                            CdActivate(d);
                        }
                    }
                    else if (e.XTarget != slave.XTarget)
                        AiSetTarget(slave, e.XTarget);
                }
                continue;
            }
            d.Slaves.RemoveAt(i);
        }
    }

    void CdSlavesKill(CustomDude d)
    {
        var e = d.Spr;
        foreach (var slave in d.Slaves.ToArray())
        {
            if (IsDudeSprite(slave) && XSprIsFine(slave) && slave.Owner == e && slave.XHealth > 0)
            {
                int nDType = SpriteIsUnderwater(slave) ? K.kDamageDrown : slave.BurnTime > 0 ? K.kDamageBurn : K.kDamageFall;
                ActKillDude(e, slave, nDType, slave.XHealth * 16);
            }
        }
        d.Slaves.Clear();
    }

    /// <summary>CanMove: may the dude step to a place: not too high a drop (a flipped one: not too far from the
    /// ceiling, and no sky), water only when it swims or flies, no damaging floor it isn't immune to.</summary>
    bool CdCanMove(CustomDude d, Sector xsect, bool crusher, bool water, bool uwater, double bottom, double floorZ)
    {
        var e = d.Spr;
        if (e.XHealth == 0)
            return true;
        if (d.IsFlipped)
        {
            int sect = SpriteBSector(e);
            if (sect < 0 || (Board.Sectors[sect].CeilingStat & 1) != 0)
                return false;
            var (zt, _) = Extents(e);
            if (Math.Abs(zt - CeilZOf(e)) > d.FallHeight)
                return false;
        }
        else
        {
            if (Math.Abs(floorZ - bottom) > d.FallHeight && !uwater)
                return false;
            if (water || uwater)
            {
                if (!d.CanSwim() && !d.IsFlying)
                    return false;
            }
            else if (!d.IsFlying && crusher && xsect != null && !NnExtIsImmune(e, xsect.DamageType, 0))
                return false;
        }
        return true;
    }
}
