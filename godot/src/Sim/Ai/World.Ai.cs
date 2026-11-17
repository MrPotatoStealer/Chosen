using System;
using Chosen.Data;
using Godot;

namespace Chosen.Sim;

/// <summary>
/// The AI core (ai.cpp, via Blood3D's server/ai.qc): states, moving and turning, choosing a direction, finding and
/// activating on targets, damage reactions and recoil, and the per-frame processing of every dude. The monsters' own
/// think and attack functions live in the Ai/World.Ai*.cs files.
/// </summary>
public sealed partial class World
{
    public const int AI_SFX_PRIORITY_0 = 0, AI_SFX_PRIORITY_1 = 1, AI_SFX_PRIORITY_2 = 2;

    public static int CultTeslaFireChance(int skill) =>
        skill <= 0 ? 8192 : skill == 1 ? 16384 : skill == 2 ? 32768 : skill == 3 ? 40960 : 57344;

    static bool DudeIsPlayingSeq(Sprite e, int nSeq) =>
        e.StatNum == K.kStatDude && Info.IsDude(e.Type)
        && Seq.Id(e) == Info.Dude(e.Type, K.DI_seqStartID) + nSeq && Seq.Status(e) >= 0;

    void AiPlay3DSound(Sprite e, int soundId, int nPriority, int chanId)
    {
        if (nPriority == AI_SFX_PRIORITY_0)
            Sound3D(e, soundId, chanId, 2);
        else if (nPriority > e.SfxPriority || e.DudeClock <= FrameClock)
        {
            Kill3DSound(e);
            Sound3D(e, soundId, chanId, 0);
            e.SfxPriority = nPriority;
            e.DudeClock = FrameClock + K.kTicRate;
        }
    }

    public void AiNewState(Sprite e, AiState st)
    {
        if (st == null)
            return;
        e.StateTimer = st.Ticks;
        e.AiState = st;
        int seqStartId = Info.Dude(e.Type, K.DI_seqStartID);
        if (st.Seq >= 0)
        {
            seqStartId += st.Seq;
            if (SeqData.Exists(seqStartId))
                Seq.Spawn(this, seqStartId, e, st.SeqCallback);
        }
        st.Enter?.Invoke(e);
    }

    bool IsImmune(Sprite e, int dmgType, int minScale)
    {
        if (dmgType >= K.kDamageFall && dmgType < K.kDamageMax && e.HasX && e.XLocked != 1)
        {
            if (Info.IsThing(e.Type))
                return Info.Thing(e.Type, K.TI_dmgControl + dmgType) <= minScale;
            if (Info.IsDude(e.Type))
                return DmgControl(e, dmgType) <= minScale;
        }
        return true;
    }

    // the destination probe CanMove uses
    bool _probeUnderwater, _probeDepth, _probeCrusher, _probeValid, _probeWater;
    double _probeFloorZ;
    Sector _probeSector;

    void AiProbePoint(Sprite e, double x, double y, double z)
    {
        // CanMove (ai.cpp): FindSector from the dude's own; the floor there, the sector's water, depth and damage
        int ns = FindSectorB(x, y, z, SpriteBSector(e));
        _probeValid = ns >= 0;
        if (ns < 0)
            return;
        _probeFloorZ = Board.FloorZAt(ns, x, y);
        _probeUnderwater = _probeDepth = _probeCrusher = _probeWater = false;
        var xs = XSectorOf(ns);
        _probeSector = xs;
        if (xs != null)
        {
            if (xs.SUnderwater != 0) _probeUnderwater = true;
            if (xs.SDepth != 0) _probeDepth = true;
            if (Board.Sectors[ns].Type == K.kSectorDamage || xs.DamageType > 0)
                _probeCrusher = true;
        }
        var up = UpperLinkOf(ns);
        if (up != null && (up.Type == K.kMarkerUpWater || up.Type == K.kMarkerUpGoo))
            _probeDepth = _probeWater = true;
        var lo = LowerLinkOf(ns);
        if (lo != null && (lo.Type == K.kMarkerLowWater || lo.Type == K.kMarkerLowGoo))
            _probeDepth = true;
    }

    bool CanMove(Sprite e, Sprite a2, double nAngle, double nRange)
    {
        var (_, bottom) = Extents(e);
        double x = e.BX, y = e.BY, z = e.BZ;
        HitScan(e, z, Blood.Sar(Blood.Cos(nAngle), 16), Blood.Sar(Blood.Sin(nAngle), 16), 0, nRange, Sim.Collision.Contents.MaskBlock);
        if (HitType < 0)
        {
            HitX = x + Blood.MulScale30(nRange * 2, Blood.Cos(nAngle));
            HitY = y + Blood.MulScale30(nRange * 2, Blood.Sin(nAngle));
            HitEnt = null;
        }
        double nDist = Blood.ApproxDist(x - HitX, y - HitY);
        if (nDist - e.ClipDist * 4 < nRange)
            return HitType == 3 && a2 != null && HitEnt == a2;
        x += Blood.MulScale30(nRange, Blood.Cos(nAngle));
        y += Blood.MulScale30(nRange, Blood.Sin(nAngle));
        AiProbePoint(e, x, y, z);
        if (!_probeValid)
            return false;
        double floorZ = _probeFloorZ;
        int t = e.Type;
        if (t == KM.kDudeModernCustom && e.Custom != null)
            return CdCanMove(e.Custom, _probeSector, _probeCrusher, _probeWater, _probeUnderwater, bottom, floorZ);
        if (t == K.kDudeGargoyleFlesh || t == K.kDudeGargoyleStone || t == K.kDudeBat)
            return !(e.ClipDist > nDist) && !_probeDepth;
        if (t == K.kDudeBoneEel)
            return _probeUnderwater;
        if ((t == K.kDudeCerberusTwoHead || t == K.kDudeCerberusOneHead) && _probeCrusher && _probeSector != null
            && IsImmune(e, _probeSector.DamageType, 16))
            return true;
        if (t == K.kDudeCerberusTwoHead || t == K.kDudeCerberusOneHead || t == K.kDudeZombieButcher
            || t == K.kDudeSpiderBrown || t == K.kDudeSpiderRed || t == K.kDudeSpiderBlack
            || t == K.kDudeSpiderMother || t == K.kDudeHellHound || t == K.kDudeRat || t == K.kDudeInnocent)
        {
            if (_probeCrusher || _probeDepth || _probeUnderwater)
                return false;
            return !(floorZ - bottom > 8192);
        }
        if (_probeCrusher)
            return false;
        return !(!_probeUnderwater && !_probeDepth && floorZ - bottom > 8192);
    }

    void AiChooseDirection(Sprite e, double a3)
    {
        double vc = Blood.AngMask(a3 + 1024 - e.Ang) - 1024;
        double nCos = Blood.Cos(e.Ang), nSin = Blood.Sin(e.Ang);
        double t1 = Math.Floor((e.XVel * nCos + e.YVel * nSin) / 1073741824);
        double vsi = Blood.Int(Blood.Sar(t1 * 15, 12) / 2);
        double v8 = vc < 0 ? -341 : 341;
        double half = Blood.Int(vc / 2);
        if (CanMove(e, e.XTarget, e.Ang + vc, vsi)) e.GoalAng = (int)(e.Ang + vc);
        else if (CanMove(e, e.XTarget, e.Ang + half, vsi)) e.GoalAng = (int)(e.Ang + half);
        else if (CanMove(e, e.XTarget, e.Ang - half, vsi)) e.GoalAng = (int)(e.Ang - half);
        else if (CanMove(e, e.XTarget, e.Ang + v8, vsi)) e.GoalAng = (int)(e.Ang + v8);
        else if (CanMove(e, e.XTarget, e.Ang, vsi)) e.GoalAng = e.Ang;
        else if (CanMove(e, e.XTarget, e.Ang - v8, vsi)) e.GoalAng = (int)(e.Ang - v8);
        else e.GoalAng = e.Ang + 341;
        e.GoalAng = (int)Blood.AngMask(e.GoalAng);
        e.DodgeDir = Chance(32768) ? 1 : -1;
        if (!CanMove(e, e.XTarget, e.Ang + e.DodgeDir * 512, 512))
        {
            e.DodgeDir = -e.DodgeDir;
            if (!CanMove(e, e.XTarget, e.Ang + e.DodgeDir * 512, 512))
                e.DodgeDir = 0;
        }
    }

    static int AiTurnRange(Sprite e) => (int)Blood.Sar(Info.Dude(e.Type, K.DI_angSpeed) * 4, 4);

    void AiTurnTowardGoal(Sprite e)
    {
        double nAng = Blood.AngMask(e.GoalAng + 1024 - e.Ang) - 1024;
        int nTurnRange = AiTurnRange(e);
        e.Ang = (int)Blood.AngMask(e.Ang + Blood.ClipRange(nAng, -nTurnRange, nTurnRange));
    }

    void aiMoveForward(Sprite e)
    {
        double nAng = Blood.AngMask(e.GoalAng + 1024 - e.Ang) - 1024;
        AiTurnTowardGoal(e);
        if (Math.Abs(nAng) > 341)
            return;
        e.XVel += Blood.MulScale30(Info.Dude(e.Type, K.DI_frontSpeed), Blood.Cos(e.Ang));
        e.YVel += Blood.MulScale30(Info.Dude(e.Type, K.DI_frontSpeed), Blood.Sin(e.Ang));
    }

    void aiMoveTurn(Sprite e) => AiTurnTowardGoal(e);

    // world velocity as (forward t1, side t2) and back (the dodge/swoop movers)
    static (double t1, double t2) VelToLocal(Sprite e)
    {
        double nCos = Blood.Cos(e.Ang), nSin = Blood.Sin(e.Ang);
        return (Math.Floor((e.XVel * nCos + e.YVel * nSin) / 1073741824), Math.Floor((e.XVel * nSin - e.YVel * nCos) / 1073741824));
    }

    static void LocalToVel(Sprite e, double t1, double t2)
    {
        double nCos = Blood.Cos(e.Ang), nSin = Blood.Sin(e.Ang);
        e.XVel = Math.Floor((t1 * nCos + t2 * nSin) / 1073741824);
        e.YVel = Math.Floor((t1 * nSin - t2 * nCos) / 1073741824);
    }

    void aiMoveDodge(Sprite e)
    {
        AiTurnTowardGoal(e);
        if (e.DodgeDir != 0)
        {
            var (t1, t2) = VelToLocal(e);
            t2 += e.DodgeDir > 0 ? Info.Dude(e.Type, K.DI_sideSpeed) : -Info.Dude(e.Type, K.DI_sideSpeed);
            LocalToVel(e, t1, t2);
        }
    }

    static void AiSetTargetXYZ(Sprite e, double x, double y, double z)
    {
        e.XTarget = null;
        e.TargetX = x;
        e.TargetY = y;
        e.TargetZ = z;
    }

    public void AiSetTarget(Sprite e, Sprite t)
    {
        if (t == null || !Info.IsDude(t.Type) || e.Owner == t)
            return;
        e.XTarget = t;
        e.TargetX = t.BX;
        e.TargetY = t.BY;
        e.TargetZ = t.BZ - Info.Dude(t.Type, K.DI_eyeHeight) * t.YRepeat * 4;
    }

    void AiStartChaseOrSearch(Sprite e, AiState search, AiState chase, Action sound = null)
    {
        if (e.XTarget == null)
            AiNewState(e, search);
        else
        {
            sound?.Invoke();
            AiNewState(e, chase);
        }
    }

    public void AiActivateDude(Sprite e)
    {
        if (e.XState == 0)
        {
            AiChooseDirection(e, Blood.GetAngle(e.TargetX - e.BX, e.TargetY - e.BY));
            e.XState = 1;
        }
        int t = e.Type;
        if (t == KM.kDudeModernCustom && e.Custom != null)
        {
            CdActivate(e.Custom);
            return;
        }
        if (t == K.kDudePhantasm)
        {
            e.ThinkTime = 0;
            e.DudeActive = 1;
            AiStartChaseOrSearch(e, ghostSearch, ghostChase, () => AiPlay3DSound(e, 1600, AI_SFX_PRIORITY_1, -1));
        }
        else if (t == K.kDudeCultistTommy || t == K.kDudeCultistShotgun || t == K.kDudeCultistTesla || t == K.kDudeCultistTNT || t == K.kDudeCultistBeast)
        {
            e.DudeActive = 1;
            if (e.XTarget == null)
            {
                if (e.Medium == K.kMediumNormal)
                {
                    AiNewState(e, cultistSearch);
                    if (Chance(32768))
                        AiPlay3DSound(e, (t == K.kDudeCultistTommy ? 4008 : 1008) + Random(5), AI_SFX_PRIORITY_1, 6);
                }
                else
                    AiNewState(e, cultistSwimSearch);
            }
            else
            {
                if (Chance(32768))
                    AiPlay3DSound(e, (t == K.kDudeCultistTommy ? 4003 : 1003) + Random(4), AI_SFX_PRIORITY_1, 6);
                if (e.Medium == K.kMediumNormal)
                    AiNewState(e, t == K.kDudeCultistTommy ? fanaticChase : cultistChase);
                else
                    AiNewState(e, cultistSwimChase);
            }
        }
        else if (t == K.kDudeCultistTommyProne || t == K.kDudeCultistShotgunProne)
        {
            e.DudeActive = 1;
            e.Type = t == K.kDudeCultistTommyProne ? K.kDudeCultistTommy : K.kDudeCultistShotgun;
            if (e.XTarget == null)
            {
                if (e.Medium == K.kMediumNormal)
                {
                    AiNewState(e, cultistSearch);
                    if (Chance(32768))
                        AiPlay3DSound(e, (t == K.kDudeCultistTommyProne ? 4008 : 1008) + Random(5), AI_SFX_PRIORITY_1, 6);
                }
                else
                    AiNewState(e, cultistSwimSearch);
            }
            else
            {
                if (Chance(32768))
                {
                    if (t == K.kDudeCultistTommyProne) AiPlay3DSound(e, 4008 + Random(5), AI_SFX_PRIORITY_1, 6);
                    else AiPlay3DSound(e, 1003 + Random(4), AI_SFX_PRIORITY_1, 6);
                }
                AiNewState(e, e.Medium == K.kMediumNormal ? cultistProneChase : cultistSwimChase);
            }
        }
        else if (t == K.kDudeBurningCultist)
            AiNewState(e, e.XTarget == null ? cultistBurnSearch : cultistBurnChase);
        else if (t == K.kDudeBat)
        {
            e.ThinkTime = 0;
            e.DudeActive = 1;
            if (e.HitTag == 0)
                e.HitTag = 9;
            AiStartChaseOrSearch(e, batSearch, batChase, () => { if (Chance(40960)) AiPlay3DSound(e, 2000, AI_SFX_PRIORITY_1, -1); });
        }
        else if (t == K.kDudeBoneEel)
        {
            e.ThinkTime = 0;
            e.DudeActive = 1;
            AiStartChaseOrSearch(e, eelSearch, eelChase, () => AiPlay3DSound(e, Chance(16384) ? 1501 : 1500, AI_SFX_PRIORITY_1, -1));
        }
        else if (t == K.kDudeGillBeast)
        {
            e.ThinkTime = 0;
            e.DudeActive = 1;
            bool under = IsUnderwaterSprite(e);
            AiStartChaseOrSearch(e, under ? gillBeastSwimSearch : gillBeastSearch, under ? gillBeastSwimChase : gillBeastChase,
                () => AiPlay3DSound(e, Chance(16384) ? 1701 : 1700, AI_SFX_PRIORITY_1, -1));
        }
        else if (t == K.kDudeZombieAxeNormal)
        {
            e.ThinkTime = 1;
            AiStartChaseOrSearch(e, zombieASearch, zombieAChase, () =>
            {
                if (Chance(40960))
                {
                    int r = Random(3);
                    AiPlay3DSound(e, r == 1 ? 1104 : r == 2 ? 1105 : 1103, AI_SFX_PRIORITY_1, -1);
                }
            });
        }
        else if (t == K.kDudeZombieAxeBuried)
        {
            e.ThinkTime = 1;
            if (e.AiState == zombieEIdle)
                AiNewState(e, zombieEUp);
        }
        else if (t == K.kDudeZombieAxeLaying)
        {
            e.ThinkTime = 1;
            if (e.AiState == zombieSIdle)
                AiNewState(e, zombie13AC2C);
        }
        else if (t == K.kDudeZombieButcher)
        {
            e.ThinkTime = 1;
            AiStartChaseOrSearch(e, zombieFSearch, zombieFChase, () => AiPlay3DSound(e, Chance(16384) ? 1201 : 1200, AI_SFX_PRIORITY_1, -1));
        }
        else if (t == K.kDudeBurningZombieAxe)
            AiNewState(e, e.XTarget == null ? zombieABurnSearch : zombieABurnChase);
        else if (t == K.kDudeBurningZombieButcher)
            AiNewState(e, e.XTarget == null ? zombieFBurnSearch : zombieFBurnChase);
        else if (t == K.kDudeGargoyleFlesh || t == K.kDudeGargoyleStone)
        {
            e.ThinkTime = 0;
            e.DudeActive = 1;
            AiStartChaseOrSearch(e, gargoyleFSearch, gargoyleFChase, () =>
            {
                if (t == K.kDudeGargoyleFlesh) AiPlay3DSound(e, Chance(16384) ? 1401 : 1400, AI_SFX_PRIORITY_1, -1);
                else AiPlay3DSound(e, Chance(16384) ? 1451 : 1450, AI_SFX_PRIORITY_1, -1);
            });
        }
        else if (t == K.kDudeGargoyleStatueFlesh || t == K.kDudeGargoyleStatueStone)
        {
            TotalKills++;
            if (IsModern && e.Data1 == 1)
                AiNewState(e, t == K.kDudeGargoyleStatueFlesh ? statueFBreakSEQ : statueSBreakSEQ);     // it breaks out
            else
            {
                AiPlay3DSound(e, Chance(16384) ? 1401 : 1400, AI_SFX_PRIORITY_1, -1);
                AiNewState(e, t == K.kDudeGargoyleStatueFlesh ? gargoyleFMorph : gargoyleSMorph);
            }
        }
        else if (t == K.kDudeCerberusTwoHead)
            AiStartChaseOrSearch(e, cerberusSearch, cerberusChase, () => AiPlay3DSound(e, 2300, AI_SFX_PRIORITY_1, -1));
        else if (t == K.kDudeCerberusOneHead)
            AiStartChaseOrSearch(e, cerberus2Search, cerberus2Chase, () => AiPlay3DSound(e, 2300, AI_SFX_PRIORITY_1, -1));
        else if (t == K.kDudeHellHound)
            AiStartChaseOrSearch(e, houndSearch, houndChase, () => AiPlay3DSound(e, 1300, AI_SFX_PRIORITY_1, -1));
        else if (t == K.kDudeHand)
            AiStartChaseOrSearch(e, handSearch, handChase, () => AiPlay3DSound(e, 1900, AI_SFX_PRIORITY_1, -1));
        else if (t == K.kDudeRat)
            AiStartChaseOrSearch(e, ratSearch, ratChase, () => AiPlay3DSound(e, 2100, AI_SFX_PRIORITY_1, -1));
        else if (t == K.kDudeInnocent)
            AiStartChaseOrSearch(e, innocentSearch, innocentChase, () => { if (e.XHealth > 0) AiPlay3DSound(e, 7000 + Random(6), AI_SFX_PRIORITY_1, -1); });
        else if (t == K.kDudeTchernobog)
            AiStartChaseOrSearch(e, tchernobogSearch, tchernobogChase, () => AiPlay3DSound(e, 2350 + Random(7), AI_SFX_PRIORITY_1, -1));
        else if (t == K.kDudeSpiderBrown || t == K.kDudeSpiderRed || t == K.kDudeSpiderBlack)
        {
            e.HitTag |= 2;
            e.Cstat &= ~8;
            AiStartChaseOrSearch(e, spidSearch, spidChase, () => AiPlay3DSound(e, 1800, AI_SFX_PRIORITY_1, -1));
        }
        else if (t == K.kDudeSpiderMother)
        {
            e.DudeActive = 1;
            e.HitTag |= 2;
            e.Cstat &= ~8;
            AiStartChaseOrSearch(e, spidSearch, spidChase, () => AiPlay3DSound(e, 1853 + Random(1), AI_SFX_PRIORITY_1, -1));
        }
        else if (t == K.kDudeTinyCaleb)
        {
            e.ThinkTime = 1;
            bool normal = e.Medium == K.kMediumNormal;
            AiNewState(e, e.XTarget == null ? (normal ? tinycalebSearch : tinycalebSwimSearch) : (normal ? tinycalebChase : tinycalebSwimChase));
        }
        else if (t == K.kDudeBeast)
        {
            e.ThinkTime = 1;
            bool normal = e.Medium == K.kMediumNormal;
            if (e.XTarget == null)
                AiNewState(e, normal ? beastSearch : beastSwimSearch);
            else
            {
                AiPlay3DSound(e, 9009 + Random(2), AI_SFX_PRIORITY_1, -1);
                AiNewState(e, normal ? beastChase : beastSwimChase);
            }
        }
        else if (t == K.kDudePodGreen || t == K.kDudePodFire)
            AiStartChaseOrSearch(e, podSearch, podChase, () => AiPlay3DSound(e, t == K.kDudePodFire ? 2453 : 2473, AI_SFX_PRIORITY_1, -1));
        else if (t == K.kDudeTentacleGreen || t == K.kDudeTentacleFire)
            AiStartChaseOrSearch(e, tentacleSearch, tentacleChase, () => AiPlay3DSound(e, 2503, AI_SFX_PRIORITY_1, -1));
    }

    void BurnInto(Sprite e, int newType, AiState goTo, int healType, int sound2 = 0, bool setClock = true)
    {
        e.Type = newType;
        AiNewState(e, goTo);
        AiPlay3DSound(e, 361, AI_SFX_PRIORITY_0, -1);
        if (sound2 != 0)
            AiPlay3DSound(e, sound2, AI_SFX_PRIORITY_2, -1);
        if (setClock)
            e.DudeClock = FrameClock + 360;
        ActHealDude(e, Info.Dude(healType, K.DI_startHealth), Info.Dude(healType, K.DI_startHealth));
        EvKillCallback(e, K.kCallbackFXFlameLick);
    }

    double AiDamageSprite(Sprite e, Sprite src, int nDmgType, double nDamage)
    {
        if (e.XHealth == 0)
            return 0;
        e.XHealth = Blood.ClipLow(e.XHealth - nDamage, 0);
        e.CumulDamage += nDamage;
        int fleeHealth = Info.Dude(e.Type, K.DI_fleeHealth);
        if (src == null)
            return nDamage;
        if (e == src)
            return 0;
        if (e.XTarget == null)
        {
            AiSetTarget(e, src);
            AiActivateDude(e);
        }
        else if (src != e.XTarget)
        {
            double nThresh = nDamage * Info.Dude(e.Type, e.Type == src.Type ? K.DI_changeTargetKin : K.DI_changeTarget);
            if (Chance((int)nThresh))
            {
                AiSetTarget(e, src);
                AiActivateDude(e);
            }
        }
        if (IsModern)
        {
            // a patrolling dude goes for who hurt it
            if (AiInPatrolState(e.AiState))
            {
                AiPatrolStop(e, src, e.DudeAmbush != 0);
                return nDamage;
            }
            if (IsCustomDude(e) && e.Custom != null)
                return CdDamage(e.Custom, src, nDmgType, nDamage);
        }
        e.TeslaHit = nDmgType == K.kDamageTesla ? 1 : 0;
        int t = e.Type;
        if (t == K.kDudeCultistTommy || t == K.kDudeCultistShotgun || t == K.kDudeCultistTesla || t == K.kDudeCultistTNT)
        {
            if (nDmgType != K.kDamageBurn)
            {
                if (!DudeIsPlayingSeq(e, 14) && e.Medium == 0)
                    AiNewState(e, cultistDodge);
                else if (DudeIsPlayingSeq(e, 14) && e.Medium == 0)
                    AiNewState(e, cultistProneDodge);
                else if (DudeIsPlayingSeq(e, 13) && (e.Medium == K.kMediumWater || e.Medium == K.kMediumGoo))
                    AiNewState(e, cultistSwimDodge);
            }
            else if (e.XHealth <= fleeHealth)
                BurnInto(e, K.kDudeBurningCultist, cultistBurnGoto, K.kDudeBurningCultist, 1031 + Random(2));
        }
        else if (t == K.kDudeInnocent)
        {
            if (nDmgType == K.kDamageBurn && e.XHealth <= fleeHealth)
                BurnInto(e, K.kDudeBurningInnocent, cultistBurnGoto, K.kDudeBurningInnocent);
        }
        else if (t == K.kDudeBurningCultist)
        {
            if (Chance(16384) && e.DudeClock < FrameClock)
            {
                AiPlay3DSound(e, 1031 + Random(2), AI_SFX_PRIORITY_2, -1);
                e.DudeClock = FrameClock + 360;
            }
            if (e.Medium == K.kMediumWater || e.Medium == K.kMediumGoo)
            {
                if (Info.IsDude(e.InitType) && e.InitType != e.Type)
                    e.Type = e.InitType;
                else
                    e.Type = Chance(1536) ? K.kDudeCultistTommy : K.kDudeCultistShotgun;
                e.BurnTime = 0;
                AiNewState(e, cultistSwimGoto);
            }
        }
        else if (t == K.kDudeGargoyleFlesh)
            AiNewState(e, gargoyleFChase);
        else if (t == K.kDudeZombieButcher)
        {
            if (nDmgType == K.kDamageBurn && e.XHealth <= fleeHealth)
            {
                AiPlay3DSound(e, 361, AI_SFX_PRIORITY_0, -1);
                AiPlay3DSound(e, 1202, AI_SFX_PRIORITY_2, -1);
                e.Type = K.kDudeBurningZombieButcher;
                AiNewState(e, zombieFBurnGoto);
                ActHealDude(e, Info.Dude(K.kDudeBurningZombieButcher, K.DI_startHealth), Info.Dude(K.kDudeBurningZombieButcher, K.DI_startHealth));
                EvKillCallback(e, K.kCallbackFXFlameLick);
            }
        }
        else if (t == K.kDudeTinyCaleb)
        {
            if (nDmgType == K.kDamageBurn && e.XHealth <= fleeHealth)
                BurnInto(e, K.kDudeBurningTinyCaleb, tinycalebBurnGoto, K.kDudeBurningInnocent);
        }
        else if (t == K.kDudeCultistBeast)
        {
            if (e.XHealth <= fleeHealth)
            {
                e.Type = K.kDudeBeast;
                AiPlay3DSound(e, 9008, AI_SFX_PRIORITY_1, -1);
                AiNewState(e, beastMorphFromCultist);
                ActHealDude(e, Info.Dude(K.kDudeBeast, K.DI_startHealth), Info.Dude(K.kDudeBeast, K.DI_startHealth));
            }
        }
        else if (t == K.kDudeZombieAxeNormal || t == K.kDudeZombieAxeBuried)
        {
            if (nDmgType == K.kDamageBurn && e.XHealth <= fleeHealth)
            {
                AiPlay3DSound(e, 361, AI_SFX_PRIORITY_0, -1);
                AiPlay3DSound(e, 1106, AI_SFX_PRIORITY_2, -1);
                e.Type = K.kDudeBurningZombieAxe;
                AiNewState(e, zombieABurnGoto);
                ActHealDude(e, Info.Dude(K.kDudeBurningZombieAxe, K.DI_startHealth), Info.Dude(K.kDudeBurningZombieAxe, K.DI_startHealth));
                EvKillCallback(e, K.kCallbackFXFlameLick);
            }
        }
        return nDamage;
    }

    void RecoilDude(Sprite e)
    {
        bool v4 = Chance(32768);
        if (e.StatNum != K.kStatDude || !Info.IsDude(e.Type))
            return;
        int t = e.Type;
        int third = (int)Blood.Int(Info.Dude(t, K.DI_startHealth) / 3.0);
        bool tesla = e.TeslaHit != 0;
        bool water = e.Medium == K.kMediumWater || e.Medium == K.kMediumGoo;
        if (t == KM.kDudeModernCustom && e.Custom != null)
            CdRecoil(e.Custom);
        else if (t == K.kDudeCultistTommy || t == K.kDudeCultistShotgun || t == K.kDudeCultistTesla || t == K.kDudeCultistTNT || t == K.kDudeCultistBeast)
        {
            AiPlay3DSound(e, (t == K.kDudeCultistTommy ? 4013 : 1013) + Random(2), AI_SFX_PRIORITY_2, -1);
            if (!v4 && e.Medium == K.kMediumNormal)
                AiNewState(e, tesla ? cultistTeslaRecoil : cultistRecoil);
            else if (v4 && e.Medium == K.kMediumNormal)
                AiNewState(e, tesla ? cultistTeslaRecoil : Skill > 0 ? cultistProneRecoil : cultistRecoil);
            else if (water)
                AiNewState(e, cultistSwimRecoil);
            else
                AiNewState(e, tesla ? cultistTeslaRecoil : cultistRecoil);
        }
        else if (t == K.kDudeBurningCultist)
            AiNewState(e, cultistBurnGoto);
        else if (t == K.kDudeZombieButcher)
        {
            AiPlay3DSound(e, 1202, AI_SFX_PRIORITY_2, -1);
            AiNewState(e, tesla ? zombieFTeslaRecoil : zombieFRecoil);
        }
        else if (t == K.kDudeZombieAxeNormal || t == K.kDudeZombieAxeBuried)
        {
            AiPlay3DSound(e, 1106, AI_SFX_PRIORITY_2, -1);
            if (tesla && e.Data3 > third) AiNewState(e, zombieATeslaRecoil);
            else if (e.Data3 > third) AiNewState(e, zombieARecoil2);
            else AiNewState(e, zombieARecoil);
        }
        else if (t == K.kDudeBurningZombieAxe)
        {
            AiPlay3DSound(e, 1106, AI_SFX_PRIORITY_2, -1);
            AiNewState(e, zombieABurnGoto);
        }
        else if (t == K.kDudeBurningZombieButcher)
        {
            AiPlay3DSound(e, 1202, AI_SFX_PRIORITY_2, -1);
            AiNewState(e, zombieFBurnGoto);
        }
        else if (t == K.kDudeGargoyleFlesh || t == K.kDudeGargoyleStone)
        {
            AiPlay3DSound(e, 1402, AI_SFX_PRIORITY_2, -1);
            AiNewState(e, gargoyleFRecoil);
        }
        else if (t == K.kDudeCerberusTwoHead)
        {
            AiPlay3DSound(e, 2302 + Random(2), AI_SFX_PRIORITY_2, -1);
            AiNewState(e, tesla && e.Data3 > third ? cerberusTeslaRecoil : cerberusRecoil);
        }
        else if (t == K.kDudeCerberusOneHead)
        {
            AiPlay3DSound(e, 2302 + Random(2), AI_SFX_PRIORITY_2, -1);
            AiNewState(e, cerberus2Recoil);
        }
        else if (t == K.kDudeHellHound)
        {
            AiPlay3DSound(e, 1302, AI_SFX_PRIORITY_2, -1);
            AiNewState(e, tesla ? houndTeslaRecoil : houndRecoil);
        }
        else if (t == K.kDudeTchernobog)
        {
            AiPlay3DSound(e, 2370 + Random(6), AI_SFX_PRIORITY_2, -1);
            AiNewState(e, tchernobogRecoil);
        }
        else if (t == K.kDudeHand)
        {
            AiPlay3DSound(e, 1902, AI_SFX_PRIORITY_2, -1);
            AiNewState(e, handRecoil);
        }
        else if (t == K.kDudeRat)
        {
            AiPlay3DSound(e, 2102, AI_SFX_PRIORITY_2, -1);
            AiNewState(e, ratRecoil);
        }
        else if (t == K.kDudeBat)
        {
            AiPlay3DSound(e, 2002, AI_SFX_PRIORITY_2, -1);
            AiNewState(e, batRecoil);
        }
        else if (t == K.kDudeBoneEel)
        {
            AiPlay3DSound(e, 1502, AI_SFX_PRIORITY_2, -1);
            AiNewState(e, eelRecoil);
        }
        else if (t == K.kDudeGillBeast)
        {
            AiPlay3DSound(e, 1702, AI_SFX_PRIORITY_2, -1);
            AiNewState(e, IsUnderwaterSprite(e) ? gillBeastSwimRecoil : gillBeastRecoil);
        }
        else if (t == K.kDudePhantasm)
        {
            AiPlay3DSound(e, 1602, AI_SFX_PRIORITY_2, -1);
            AiNewState(e, tesla ? ghostTeslaRecoil : ghostRecoil);
        }
        else if (t == K.kDudeSpiderBrown || t == K.kDudeSpiderRed || t == K.kDudeSpiderBlack)
        {
            AiPlay3DSound(e, 1802 + Random(1), AI_SFX_PRIORITY_2, -1);
            AiNewState(e, spidDodge);
        }
        else if (t == K.kDudeSpiderMother)
        {
            AiPlay3DSound(e, 1851 + Random(1), AI_SFX_PRIORITY_2, -1);
            AiNewState(e, spidDodge);
        }
        else if (t == K.kDudeInnocent)
        {
            AiPlay3DSound(e, 7007 + Random(2), AI_SFX_PRIORITY_2, -1);
            AiNewState(e, tesla ? innocentTeslaRecoil : innocentRecoil);
        }
        else if (t == K.kDudeTinyCaleb)
            AiNewState(e, water ? tinycalebSwimRecoil : tesla ? tinycalebTeslaRecoil : tinycalebRecoil);
        else if (t == K.kDudeBeast)
        {
            AiPlay3DSound(e, 9004 + Random(2), AI_SFX_PRIORITY_2, -1);
            AiNewState(e, water ? beastSwimRecoil : tesla ? beastTeslaRecoil : beastRecoil);
        }
        else if (t == K.kDudePodGreen || t == K.kDudePodFire)
            AiNewState(e, podRecoil);
        else if (t == K.kDudeTentacleGreen || t == K.kDudeTentacleFire)
            AiNewState(e, tentacleRecoil);
        else if (t == K.kDudeGargoyleStatueFlesh || t == K.kDudeGargoyleStatueStone)
        {
        }
        else
            AiNewState(e, genRecoil);
        e.TeslaHit = 0;
    }

    /// <summary>The eye position of a dude (Build z).</summary>
    static double DudeEyeZ(Sprite e) => e.BZ - Info.Dude(e.Type, K.DI_eyeHeight) * e.YRepeat * 4;

    bool AiTryTargetPlayers(Sprite e)
    {
        foreach (var pl in Players)
        {
            if (e.Owner == pl || pl.XHealth == 0 || PowerupCheck(pl, K.kPwUpShadowCloak) > 0 || pl.NoTarget)
                continue;
            double x = pl.BX, y = pl.BY, z = pl.BZ;
            double dx = x - e.BX, dy = y - e.BY;
            double nDist = Blood.ApproxDist(dx, dy);
            if (nDist > Info.Dude(e.Type, K.DI_seeDist) && nDist > Info.Dude(e.Type, K.DI_hearDist))
                continue;
            if (!CanSee(x, y, z, e.BX, e.BY, DudeEyeZ(e), pl, e))
                continue;
            double nDeltaAngle = Blood.AngMask(Blood.GetAngle(dx, dy) + 1024 - e.Ang) - 1024;
            if (nDist < Info.Dude(e.Type, K.DI_seeDist) && Math.Abs(nDeltaAngle) <= Info.Dude(e.Type, K.DI_periphery))
            {
                AiSetTarget(e, pl);
                AiActivateDude(e);
                return true;
            }
            if (nDist < Info.Dude(e.Type, K.DI_hearDist))
            {
                AiSetTargetXYZ(e, x, y, z);
                AiActivateDude(e);
                return true;
            }
        }
        return false;
    }

    void aiThinkTarget(Sprite e)
    {
        if (Chance(Info.Dude(e.Type, K.DI_alertChance)))
            AiTryTargetPlayers(e);
    }

    void AiLookForTarget(Sprite e)
    {
        if (!Chance(Info.Dude(e.Type, K.DI_alertChance)))
            return;
        if (AiTryTargetPlayers(e))
            return;
        if (e.XState == 0)
            return;
        foreach (var e2 in StatList(K.kStatDude))
        {
            double nDist = Blood.ApproxDist(e2.BX - e.BX, e2.BY - e.BY);
            if (e2.Type == K.kDudeInnocent)
            {
                if (nDist > Info.Dude(e2.Type, K.DI_seeDist) && nDist > Info.Dude(e2.Type, K.DI_hearDist))
                    continue;
                AiSetTarget(e, e2);
                AiActivateDude(e);
                return;
            }
        }
    }

    void AiProcessDudes()
    {
        foreach (var e in StatList(K.kStatDude))
        {
            if (e.Removed || (e.HitTag & K.kHitagFree) != 0 || e is Player || !e.HasX)
                continue;
            e.StateTimer = (int)Blood.ClipLow(e.StateTimer - GameClock.TicsPerFrame, 0);
            if (e.Type == KM.kDudeModernCustom && e.Custom != null)
            {
                // a custom dude: its own frame, dying too
                CdProcess(e.Custom);
                e.CumulDamage = 0;
                continue;
            }
            if (e.XHealth == 0)
            {
                e.CumulDamage = 0;
                continue;
            }
            var st = e.AiState;
            if (st == null)
                continue;
            st.MoveFn?.Invoke(e);
            if (st.Think != null && (Frame & 3) == (e.SprIndex & 3) && !e.Removed)
                st.Think(e);
            if (e.Removed)
                continue;
            st = e.AiState;
            if (e.StateTimer == 0 && st != null && st.Next != null)
            {
                if (st.Ticks > 0)
                    AiNewState(e, st.Next);
                else if (Seq.Status(e) < 0)
                    AiNewState(e, st.Next);
            }
            if (e.XHealth > 0 && Info.Dude(e.Type, K.DI_hinderDamage) * 16 <= e.CumulDamage)
            {
                e.Data3 = (int)e.CumulDamage;
                RecoilDude(e);
            }
            e.CumulDamage = 0;
        }
    }

    void AiInitSprite(Sprite e)
    {
        int t = e.Type;
        // a modern map's patrolling dude keeps its marker over the init
        Sprite patrolTarget = null, patrolPrev = null;
        int patrolTimer = 0;
        double ptx = 0, pty = 0, ptz = 0;
        if (IsModern)
        {
            patrolTarget = e.XTarget;
            patrolPrev = e.Patrol?.PrevMarker;
            patrolTimer = e.StateTimer;
            ptx = e.TargetX;
            pty = e.TargetY;
            ptz = e.TargetZ;
        }
        e.DudeClock = 0;
        e.TeslaHit = 0;
        e.SfxPriority = AI_SFX_PRIORITY_0;
        e.ThinkTime = 0;
        e.DudeActive = 0;
        if (t == K.kDudeCultistTommy || t == K.kDudeCultistShotgun || t == K.kDudeCultistTesla || t == K.kDudeCultistTNT || t == K.kDudeCultistBeast)
            AiNewState(e, cultistIdle);
        else if (t == K.kDudeCultistTommyProne)
            AiNewState(e, fanaticProneIdle);
        else if (t == K.kDudeCultistShotgunProne)
            AiNewState(e, cultistProneIdle);
        else if (t == K.kDudeZombieButcher)
            AiNewState(e, zombieFIdle);
        else if (t == K.kDudeZombieAxeNormal)
            AiNewState(e, zombieAIdle);
        else if (t == K.kDudeZombieAxeLaying)
        {
            AiNewState(e, zombieSIdle);
            e.HitTag &= ~1;
        }
        else if (t == K.kDudeZombieAxeBuried)
        {
            AiNewState(e, zombieEIdle);
            e.HitTag &= ~1;
        }
        else if (t == K.kDudeGargoyleFlesh || t == K.kDudeGargoyleStone)
            AiNewState(e, gargoyleFIdle);
        else if (t == K.kDudeGargoyleStatueFlesh || t == K.kDudeGargoyleStatueStone)
            AiNewState(e, gargoyleStatueIdle);
        else if (t == K.kDudeCerberusTwoHead)
            AiNewState(e, cerberusIdle);
        else if (t == K.kDudeCerberusOneHead)
            AiNewState(e, cerberus2Idle);
        else if (t == K.kDudeHellHound)
            AiNewState(e, houndIdle);
        else if (t == K.kDudeHand)
            AiNewState(e, handIdle);
        else if (t == K.kDudePhantasm)
            AiNewState(e, ghostIdle);
        else if (t == K.kDudeInnocent)
            AiNewState(e, innocentIdle);
        else if (t == K.kDudeRat)
            AiNewState(e, ratIdle);
        else if (t == K.kDudeBoneEel)
            AiNewState(e, eelIdle);
        else if (t == K.kDudeGillBeast)
            AiNewState(e, gillBeastIdle);
        else if (t == K.kDudeBat)
            AiNewState(e, batIdle);
        else if (t == K.kDudeSpiderBrown || t == K.kDudeSpiderRed || t == K.kDudeSpiderBlack)
            AiNewState(e, spidIdle);
        else if (t == K.kDudeSpiderMother)
        {
            e.BirthCounter = 0;
            AiNewState(e, spidIdle);
        }
        else if (t == K.kDudeTchernobog)
            AiNewState(e, tchernobogIdle);
        else if (t == K.kDudeTinyCaleb)
            AiNewState(e, tinycalebIdle);
        else if (t == K.kDudeBeast)
            AiNewState(e, beastIdle);
        else if (t == K.kDudePodGreen || t == K.kDudePodFire)
            AiNewState(e, podIdle);
        else if (t == K.kDudeTentacleGreen || t == K.kDudeTentacleFire)
            AiNewState(e, tentacleIdle);
        else if (t == KM.kDudeModernCustom)
        {
            if (IsModern)
                CdInitSprite(Modern.CustomDudeSetup.Setup(this, e));
        }
        else
            AiNewState(e, genIdle);
        AiSetTargetXYZ(e, 0, 0, 0);
        e.StateTimer = 0;
        if (IsModern && (t == KM.kDudeModernCustom || t == K.kDudePodMother))
        {
            // their flags are set
            AiInitPatrol(e, patrolTarget, patrolPrev, patrolTimer, ptx, pty, ptz);
            return;
        }
        if (IsModern && (e.Cstat & Cstat.YFlip) != 0 && (t == K.kDudePodGreen || t == K.kDudeTentacleGreen || t == K.kDudePodFire
            || t == K.kDudeTentacleFire || t == K.kDudeTentacleMother))
        {
            // a modern map's pods and tentacles turned over hang from the ceiling: their flags stay
        }
        else if (t == K.kDudeSpiderBrown || t == K.kDudeSpiderRed || t == K.kDudeSpiderBlack)
        {
            if ((e.Cstat & 8) != 0) e.HitTag |= 9;
            else e.HitTag = 15;
        }
        else if (t == K.kDudeGargoyleFlesh || t == K.kDudeGargoyleStone || t == K.kDudePhantasm || t == K.kDudeBoneEel || t == K.kDudeBat)
            e.HitTag |= 9;
        else if (t == K.kDudeGillBeast)
        {
            if (IsUnderwaterSprite(e)) e.HitTag |= 9;
            else e.HitTag = 15;
        }
        else if (t == K.kDudeZombieAxeBuried || t == K.kDudeZombieAxeLaying || t == K.kDudeGargoyleStatueFlesh || t == K.kDudeGargoyleStatueStone)
            e.HitTag = 7;
        else
            e.HitTag = 15;
        AiInitPatrol(e, patrolTarget, patrolPrev, patrolTimer, ptx, pty, ptz);
    }
}
