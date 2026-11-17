using Chosen.Data;

namespace Chosen.Sim;

/// <summary>Actor death and damage (actor.cpp actKillDude, actDamageSprite; Blood3D's server/actor_damage.qc).</summary>
public sealed partial class World
{
    void ActKillDude(Sprite src, Sprite dude, int damageType, double damage)
    {
        int seqBase = Info.Dude(dude.Type, K.DI_seqStartID);
        int t = dude.Type;
        if (t == KM.kDudeModernCustom && dude.Custom != null)
        {
            CdKill(dude.Custom, src, damageType, damage);
            return;
        }
        if (t == K.kDudeCerberusTwoHead)
        {
            Seq.Spawn(this, seqBase + 1, dude);
            return;
        }
        bool cultist = t == K.kDudeCultistTommy || t == K.kDudeCultistShotgun || t == K.kDudeCultistTesla || t == K.kDudeCultistTNT;
        if (damageType == K.kDamageBurn && dude.Medium == K.kMediumNormal)
        {
            if (cultist)
            {
                dude.Type = K.kDudeBurningCultist;
                AiNewState(dude, cultistBurnGoto);
                ActHealDude(dude, Info.Dude(K.kDudeBurningCultist, K.DI_startHealth), Info.Dude(K.kDudeBurningCultist, K.DI_startHealth));
                return;
            }
            if (t == K.kDudeBeast)
            {
                dude.Type = K.kDudeBurningBeast;
                AiNewState(dude, beastBurnGoto);
                ActHealDude(dude, Info.Dude(K.kDudeBurningBeast, K.DI_startHealth), Info.Dude(K.kDudeBurningBeast, K.DI_startHealth));
                return;
            }
            if (t == K.kDudeInnocent)
            {
                dude.Type = K.kDudeBurningInnocent;
                AiNewState(dude, innocentBurnGoto);
                ActHealDude(dude, Info.Dude(K.kDudeBurningInnocent, K.DI_startHealth), Info.Dude(K.kDudeBurningInnocent, K.DI_startHealth));
                return;
            }
            if (t == K.kDudeTinyCaleb)
            {
                dude.Type = K.kDudeBurningTinyCaleb;
                AiNewState(dude, tinycalebBurnGoto);
                ActHealDude(dude, Info.Dude(K.kDudeBurningInnocent, K.DI_startHealth), Info.Dude(K.kDudeBurningInnocent, K.DI_startHealth));
                return;
            }
        }
        PlayerClearFragger(dude);
        if (dude.Type != K.kDudeCultistBeast)
            TrTriggerSprite(dude, K.kCmdOff, src);
        dude.HitTag |= 7;
        if (GameType == K.kGameTypeCoop && src is Player sp && dude.StatNum == K.kStatDude)
        {
            if (dude.Type != K.kDudeBat && dude.Type != K.kDudeRat && dude.Type != K.kDudeInnocent && dude.Type != K.kDudeBurningInnocent)
                sp.FragCount++;
        }
        if (dude.XKey > 0)
            ActDropObject(dude, K.kItemKeyBase + dude.XKey - 1);
        if (dude.DropMsg > 0)
            ActDropObject(dude, dude.DropMsg);
        if (dude.Type == K.kDudeCultistTommy)
        {
            int nRand = Random(100);
            if (nRand < 10) ActDropObject(dude, K.kItemWeaponTommygun);
            else if (nRand < 50) ActDropObject(dude, K.kItemAmmoTommygunFew);
        }
        else if (dude.Type == K.kDudeCultistShotgun)
        {
            int nRand = Random(100);
            if (nRand <= 10) ActDropObject(dude, K.kItemWeaponSawedoff);
            else if (nRand <= 50) ActDropObject(dude, K.kItemAmmoSawedoffFew);
        }
        int nSeq;
        int g = dude.Type;
        if (damageType == K.kDamageExplode)
        {
            nSeq = 2;
            if (g == K.kDudeCultistTommy || g == K.kDudeCultistShotgun || g == K.kDudeCultistTommyProne || g == K.kDudeBurningInnocent
                || g == K.kDudeBurningCultist || g == K.kDudeInnocent || g == K.kDudeCultistShotgunProne || g == K.kDudeCultistTesla
                || g == K.kDudeCultistTNT || g == K.kDudeCultistBeast || g == K.kDudeTinyCaleb || g == K.kDudeBurningTinyCaleb)
                Sound3D(dude, 717, -1);
        }
        else if (damageType == K.kDamageBurn)
        {
            nSeq = 3;
            Sound3D(dude, 351, -1);
        }
        else if (damageType == K.kDamageSpirit)
            nSeq = g == K.kDudeZombieAxeNormal || g == K.kDudeZombieAxeBuried ? 14 : g == K.kDudeZombieButcher ? 11 : 1;
        else
            nSeq = 1;
        if (!SeqData.Exists(seqBase + nSeq))
        {
            Seq.Kill(dude);
            KillMgrAddKill(dude);
            ActPostSprite(dude, K.kStatFree);
            return;
        }
        if (g == K.kDudeZombieAxeNormal)
        {
            Sound3D(dude, 1107 + Random(2), -1);
            if (nSeq == 2)
            {
                Seq.Spawn(this, seqBase + nSeq, dude, DudeToGibCallback1);
                var (top, _) = Physics.SpriteExtents(dude.BZ, dude.Picnum, dude.YRepeat, dude.Cstat);
                GibSpriteAt(dude, K.GIBTYPE_27, dude.BX, dude.BY, top, Blood.Sar(dude.XVel, 1), Blood.Sar(dude.YVel, 1), -838860);
            }
            else if (nSeq == 1 & Chance(16384))
            {
                Seq.Spawn(this, seqBase + 7, dude, DudeToGibCallback1);
                EvPostCallback(dude, 0, K.kCallbackFXZombieSpurt);
                Sound3D(dude, 362, -1);
                dude.Data1 = 35;
                dude.Data2 = 5;
                var (top, _) = Physics.SpriteExtents(dude.BZ, dude.Picnum, dude.YRepeat, dude.Cstat);
                GibSpriteAt(dude, K.GIBTYPE_27, dude.BX, dude.BY, top, Blood.Sar(dude.XVel, 1), Blood.Sar(dude.YVel, 1), -1118481);
            }
            else if (nSeq == 14)
                Seq.Spawn(this, seqBase + nSeq, dude);
            else if (nSeq == 3)
                Seq.Spawn(this, seqBase + 13, dude, DudeToGibCallback2);
            else
                Seq.Spawn(this, seqBase + nSeq, dude, DudeToGibCallback1);
        }
        else if (cultist)
        {
            Sound3D(dude, 1018 + Random(2), 6, 1 | 8);
            Seq.Spawn(this, seqBase + nSeq, dude, nSeq == 3 ? DudeToGibCallback2 : DudeToGibCallback1);
        }
        else if (g == K.kDudeBurningCultist)
        {
            if (Chance(16384) && nSeq == 3)
                Sound3D(dude, 718, -1);
            else
                Sound3D(dude, 1018 + Random(2), -1);
            damageType = K.kDamageExplode;
            if (Chance(32768))
            {
                for (int i = 0; i < 3; i++)
                    GibSprite(dude, K.GIBTYPE_7);
                Seq.Spawn(this, seqBase + 16 - Random(1), dude, DudeToGibCallback1);
            }
            else
                Seq.Spawn(this, seqBase + 15, dude, DudeToGibCallback2);
        }
        else if (g == K.kDudeBurningZombieAxe)
        {
            if (Chance(32768) && nSeq == 3)
                Sound3D(dude, 1109, -1);
            else
                Sound3D(dude, 1107 + Random(2), -1);
            damageType = K.kDamageExplode;
            if (Chance(32768))
            {
                Seq.Spawn(this, seqBase + 13, dude, DudeToGibCallback1);
                var (top, _) = Physics.SpriteExtents(dude.BZ, dude.Picnum, dude.YRepeat, dude.Cstat);
                GibSpriteAt(dude, K.GIBTYPE_27, dude.BX, dude.BY, top, Blood.Sar(dude.XVel, 1), Blood.Sar(dude.YVel, 1), -838860);
            }
            else
                Seq.Spawn(this, seqBase + 13, dude, DudeToGibCallback2);
        }
        else if (g == K.kDudeBurningZombieButcher)
        {
            if (Chance(16384) && nSeq == 3)
                Sound3D(dude, 1206, -1);
            else
                Sound3D(dude, 1204 + Random(2), -1);
            Seq.Spawn(this, Info.Dude(K.kDudeZombieButcher, K.DI_seqStartID) + 10, dude);
        }
        else if (g == K.kDudeBurningInnocent)
        {
            damageType = K.kDamageExplode;
            Seq.Spawn(this, seqBase + 7, dude, DudeToGibCallback1);
        }
        else if (g == K.kDudeZombieButcher)
        {
            if (nSeq == 14)
            {
                Sound3D(dude, 1206, -1);
                Seq.Spawn(this, seqBase + 11, dude);
            }
            else
            {
                Sound3D(dude, 1204 + Random(2), -1);
                Seq.Spawn(this, seqBase + (nSeq == 3 ? 10 : nSeq), dude);
            }
        }
        else if (g == K.kDudeGargoyleFlesh || g == K.kDudeGargoyleStone || g == K.kDudePhantasm || g == K.kDudeHellHound || g == K.kDudeHand
            || g == K.kDudeGillBeast || g == K.kDudeBoneEel || g == K.kDudeBat || g == K.kDudeRat || g == K.kDudePodMother || g == K.kDudeTentacleMother)
        {
            int s0 = g == K.kDudeGargoyleFlesh ? 1403 : g == K.kDudeGargoyleStone ? 1453 : g == K.kDudePhantasm ? 1603
                : g == K.kDudeHellHound ? 1303 : g == K.kDudeHand ? 1903 : g == K.kDudeGillBeast ? 1703 : g == K.kDudeBoneEel ? 1503
                : g == K.kDudeBat ? 2003 : g == K.kDudeRat ? 2103 : 2203;
            if (Chance(16384) && nSeq == 3)
                Sound3D(dude, s0 + 2, -1);
            else
                Sound3D(dude, s0 + Random(2), -1);
            Seq.Spawn(this, seqBase + nSeq, dude);
        }
        else if (g == K.kDudeSpiderBrown || g == K.kDudeSpiderRed || g == K.kDudeSpiderBlack)
        {
            if (dude.Owner != null && dude.Owner.HasX)
                dude.Owner.BirthCounter--;
            if (Chance(16384) && nSeq == 3)
                Sound3D(dude, 1805, -1);
            else
                Sound3D(dude, 1803 + Random(2), -1);
            Seq.Spawn(this, seqBase + nSeq, dude);
        }
        else if (g == K.kDudeSpiderMother)
        {
            Sound3D(dude, 1850, -1);
            Seq.Spawn(this, seqBase + nSeq, dude);
        }
        else if (g == K.kDudePodGreen || g == K.kDudeTentacleGreen || g == K.kDudePodFire || g == K.kDudeTentacleFire)
        {
            dude.Cstat &= ~Cstat.YFlip;
            if (g == K.kDudePodGreen)
            {
                if (Chance(16384) && nSeq == 3)
                    Sound3D(dude, 2205, -1);
                else
                    Sound3D(dude, 2203 + Random(2), -1);
            }
            else if (g == K.kDudeTentacleGreen)
                Sound3D(dude, damage == 5 ? 2471 : 2472, -1);
            else if (g == K.kDudePodFire)
                Sound3D(dude, damage == 5 ? 2451 : 2452, -1);
            else
                Sound3D(dude, 2501, -1);
            Seq.Spawn(this, seqBase + nSeq, dude);
        }
        else if (g == K.kDudeCerberusTwoHead || g == K.kDudeCerberusOneHead)
        {
            if (Chance(16384) && nSeq == 3)
                Sound3D(dude, 2305, -1);
            else
                Sound3D(dude, 2305 + Random(2), -1);
            Seq.Spawn(this, seqBase + nSeq, dude);
        }
        else if (g == K.kDudeTchernobog)
        {
            Sound3D(dude, 2380, -1);
            Seq.Spawn(this, seqBase + nSeq, dude);
        }
        else if (g == K.kDudeBurningTinyCaleb)
        {
            damageType = K.kDamageExplode;
            Seq.Spawn(this, seqBase + 11, dude, DudeToGibCallback1);
        }
        else if (g == K.kDudeBeast)
        {
            Sound3D(dude, 9000 + Random(2), -1);
            Seq.Spawn(this, seqBase + nSeq, dude, nSeq == 3 ? DudeToGibCallback2 : DudeToGibCallback1);
        }
        else if (g == K.kDudeBurningBeast)
        {
            damageType = K.kDamageExplode;
            Seq.Spawn(this, seqBase + 12, dude, DudeToGibCallback1);
        }
        else
            Seq.Spawn(this, seqBase + nSeq, dude);
        if (damageType == K.kDamageExplode)
        {
            for (int i = 0; i < 3; i++)
            {
                int gib = Info.Dude(dude.Type, K.DI_gibType + i);
                if (gib > -1)
                    GibSprite(dude, gib);
            }
            for (int i = 0; i < 4; i++)
                FxSpawnBlood(dude, damage);
        }
        KillMgrAddKill(dude);
        ActCheckRespawn(dude);
        dude.Type = K.kThingBloodChunks;
        ActPostSprite(dude, K.kStatThing);
    }

    /// <summary>actDamageSprite(nSource, pSprite, damageType, damage): returns damage >> 4.</summary>
    public int ActDamageSprite(XObject srcObj, Sprite e, int damageType, double damage)
    {
        if (e == null || e.Removed || (e.HitTag & K.kHitagFree) != 0 || !e.HasX)
            return 0;        if ((e.XHealth == 0 && e.StatNum != K.kStatDude) || e.XLocked != 0)
            return 0;
        var src = srcObj as Sprite ?? e;
        var srcPlayer = src as Player;
        if (!FriendlyFire && IsTargetTeammate(srcPlayer, e))
            return 0;
        if (e.StatNum == K.kStatDude)
        {
            if (!Info.IsDude(e.Type))
                return (int)Blood.Sar(damage, 4);
            int nDamageFactor = e.Type == KM.kDudeModernCustom && e.Custom != null
                ? e.Custom.GetDamage(srcObj as Sprite, damageType) : DmgControl(e, damageType);
            if (nDamageFactor == 0)
                return 0;
            if (nDamageFactor != 256)
                damage = Blood.MulScale8(damage, nDamageFactor);
            if (e is not Player ep)
            {
                if (e.XHealth <= 0)
                    return 0;
                damage = AiDamageSprite(e, src, damageType, damage);
                if (e.XHealth <= 0)
                {
                    if (damageType == K.kDamageExplode && damage < 160)
                        ActKillDude(src, e, K.kDamageFall, damage);
                    else
                        ActKillDude(src, e, damageType, damage);
                }
            }
            else if (e.XHealth > 0 || PlayerSeqPlaying(ep, 16))
                damage = PlayerDamageSprite(src, ep, damageType, damage);
        }
        else if (e.StatNum == K.kStatThing)
        {
            int nDamageFactor = Info.Thing(e.Type, K.TI_dmgControl + damageType);
            if (nDamageFactor == 0)
                return 0;
            if (nDamageFactor != 256)
                damage = Blood.MulScale8(damage, nDamageFactor);
            e.XHealth = Blood.ClipLow(e.XHealth - damage, 0);
            if (e.XHealth <= 0)
            {
                if (e.Type == K.kThingDroppedLifeLeech || e.Type == KM.kModernThingEnemyLifeLeech)
                {
                    GibSprite(e, K.GIBTYPE_14);
                    e.Data1 = e.Data2 = e.Data3 = 0;
                    e.DudeLockout = 0;
                    e.StateTimer = 0;
                    e.Data4 = 0;
                    e.IsTriggered = 0;
                    // a custom dude's leech: its weapon is gone
                    if (e.Owner is { } leechOwner && IsCustomDude(leechOwner) && leechOwner.Custom != null)
                        CdLeechKill(leechOwner.Custom, false);
                }
                else if ((e.HitTag & K.kHitagRespawn) == 0)
                    e.Owner = src;
                TrTriggerSprite(e, K.kCmdOff, src);
                int t = e.Type;
                if (t == K.kThingObjectGib || t == K.kThingObjectExplode || t == K.kThingBloodBits || t == K.kThingBloodChunks || t == K.kThingZombieHead)
                {
                    if ((damageType == 3 && srcPlayer != null && FrameClock > srcPlayer.LaughCount) & Chance(16384))
                    {
                        Sound3D(srcPlayer, GenTables.tbl_gibcomments[Random(10)], 0, 2);
                        srcPlayer.LaughCount = FrameClock + 3600;
                    }
                }
                else if (t == K.kTrapMachinegun)
                    Seq.Spawn(this, 28, e);
                else if (t == K.kThingFluorescent)
                {
                    Seq.Spawn(this, 12, e);
                    GibSprite(e, K.GIBTYPE_6);
                }
                else if (t == K.kThingSpiderWeb)
                    Seq.Spawn(this, 15, e);
                else if (t == K.kThingMetalGrate)
                {
                    Seq.Spawn(this, 21, e);
                    GibSprite(e, K.GIBTYPE_4);
                }
                else if (t == K.kThingFlammableTree)
                {
                    if (e.Data1 == -1)
                    {
                        GibSprite(e, K.GIBTYPE_14);
                        Sound3DAt(e.Origin, 312);
                        ActPostSprite(e, K.kStatFree);
                    }
                    else if (e.Data1 == 0)
                    {
                        Seq.Spawn(this, 25, e, TreeToGibCallback);
                        Sound3D(e, 351, -1);
                    }
                    else if (e.Data1 == 1)
                    {
                        Seq.Spawn(this, 26, e, TreeToGibCallback);
                        Sound3D(e, 351, -1);
                    }
                }
            }
        }
        return (int)Blood.Sar(damage, 4);
    }

    // ---------------------------------------------------------------- kill manager (levels.qc CKillMgr)
    static bool KillMgrAllowedType(Sprite e)
    {
        if (e == null || e.StatNum != K.kStatDude)
            return false;
        int t = e.Type;
        if (t == K.kDudeGargoyleStatueFlesh || t == K.kDudeGargoyleStatueStone)
            return false;
        return t != K.kDudeBat && t != K.kDudeRat && t != K.kDudeInnocent && t != K.kDudeBurningInnocent;
    }

    void KillMgrAddCountSprite(Sprite e) { if (KillMgrAllowedType(e)) TotalKills++; }
    void KillMgrAddKill(Sprite e) { if (KillMgrAllowedType(e)) Kills++; }
    void KillMgrRemoveKill(Sprite e) { if (Kills > 0 && KillMgrAllowedType(e)) Kills--; }

    void KillMgrCountTotalKills()
    {
        TotalKills = 0;
        foreach (var e in Sprites)
            if (e is not Player && KillMgrAllowedType(e))
                TotalKills++;
    }
}
