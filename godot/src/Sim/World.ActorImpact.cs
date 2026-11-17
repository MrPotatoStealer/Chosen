using Chosen.Data;

namespace Chosen.Sim;

/// <summary>Missile impacts, kicking and floor touch (actor.cpp; Blood3D's server/actor_impact.qc).</summary>
public sealed partial class World
{
    static bool HitIsThingOrDude(Sprite e) => e != null && (e.StatNum == K.kStatThing || e.StatNum == K.kStatDude);

    bool HitCanBurn(Sprite e)
    {
        if (e.StatNum == K.kStatThing)
            return Info.Thing(e.Type, K.TI_dmgControl + K.kDamageBurn) != 0;
        if (e.StatNum == K.kStatDude)
            return DmgControl(e, K.kDamageBurn) != 0;
        return false;
    }

    /// <summary>The angle facing out of the last hit surface.</summary>
    int HitSurfaceAngle() => Blood.GetAngle(HitNormal.X, -HitNormal.Y);

    static void MakeNotSolid(Sprite e)
    {
        e.Cstat &= ~257;
        if (e.Body != null)
            e.Body.Enabled = false;
    }

    void BurnHit(Sprite missile, Sprite pHit, int burn)
    {
        if ((pHit.StatNum == K.kStatThing || pHit.StatNum == K.kStatDude) && pHit.BurnTime == 0)
            EvPostCallback(pHit, 0, K.kCallbackFXFlameLick);
        ActBurnSprite(missile.Owner, pHit, burn);
    }

    public void ActImpactMissile(Sprite pMissile, int hitCode)
    {
        var pHit = hitCode == 3 ? HitEnt as Sprite : null;
        var nOwner = pMissile.Owner;
        int mtype = pMissile.Type;
        if (mtype == K.kMissileLifeLeechRegular)
        {
            if (hitCode == 3 && HitIsThingOrDude(pHit))
            {
                int rand1 = Random(7);
                int nDamage = (7 + Random(7)) * 16;
                double dealt = ActDamageSprite(nOwner, pHit, rand1, nDamage);
                if (HitCanBurn(pHit))
                    ActBurnSprite(pMissile.Owner, pHit, 360);
                // NBlood's v1.0x weapons: the leech heals its user
                if (WeaponsV10x && pHit.StatNum == K.kStatDude && nOwner is Sprite src && Info.IsDude(src.Type) && src.HasX && src.XHealth != 0)
                    ActHealDude(src, (int)Blood.Sar(dealt, 2), Info.Dude(src.Type, K.DI_startHealth));
            }
            if (pMissile.HasX)
            {
                ActPostSprite(pMissile, K.kStatDecoration);
                if (pMissile.Ang == 1024)
                    Sound3D(pMissile, 307, -1);
                pMissile.Type = K.kSpriteDecoration;
                Seq.Spawn(this, 9, pMissile);
            }
            else
                ActPostSprite(pMissile, K.kStatFree);
        }
        else if (mtype == K.kMissileTeslaAlt)
        {
            ActTeslaHit(pMissile, hitCode);
            if (hitCode == 0 || hitCode == 4)
            {
                var pFX = FxSpawn(K.FX_52, pMissile.BX, pMissile.BY, pMissile.BZ, 0);
                if (pFX != null)
                    pFX.Ang = HitSurfaceAngle();
            }
            GibSprite(pMissile, K.GIBTYPE_24);
            ActPostSprite(pMissile, K.kStatFree);
        }
        else if (mtype == K.kMissilePukeGreen)
        {
            Seq.Kill(pMissile);
            if (hitCode == 3 && HitIsThingOrDude(pHit))
                ActDamageSprite(nOwner, pHit, K.kDamageBullet, (15 + Random(7)) * 16);
            ActPostSprite(pMissile, K.kStatFree);
        }
        else if (mtype == K.kMissileArcGargoyle)
        {
            Kill3DSound(pMissile);
            Sound3DAt(pMissile.Origin, 306);
            GibSprite(pMissile, K.GIBTYPE_6);
            if (hitCode == 3 && HitIsThingOrDude(pHit))
                ActDamageSprite(nOwner, pHit, K.kDamageSpirit, (25 + Random(20)) * 16);
            ActPostSprite(pMissile, K.kStatFree);
        }
        else if (mtype == K.kMissileLifeLeechAltNormal || mtype == K.kMissileLifeLeechAltSmall)
        {
            Kill3DSound(pMissile);
            Sound3DAt(pMissile.Origin, 306);
            if (hitCode == 3 && HitIsThingOrDude(pHit))
            {
                int nDmgMul = mtype == K.kMissileLifeLeechAltSmall ? 6 : 3;
                ActDamageSprite(nOwner, pHit, K.kDamageSpirit, (nDmgMul + Random(nDmgMul)) * 16);
            }
            ActPostSprite(pMissile, K.kStatFree);
        }
        else if (mtype == K.kMissileFireball || mtype == K.kMissileFireballNapalm)
        {
            if (hitCode == 3 && HitIsThingOrDude(pHit))
            {
                if (pHit.StatNum == K.kStatThing && pHit.Type == K.kThingTNTBarrel && pHit.BurnTime == 0)
                    EvPostCallback(pHit, 0, K.kCallbackFXFlameLick);
                ActDamageSprite(nOwner, pHit, K.kDamageBullet, (50 + Random(50)) * 16);
            }
            ActExplodeSprite(pMissile);
        }
        else if (mtype == K.kMissileFlareAlt)
        {
            Kill3DSound(pMissile);
            ActExplodeSprite(pMissile);
        }
        else if (mtype == K.kMissileFlareRegular)
        {
            Kill3DSound(pMissile);
            if (hitCode == 3 && HitIsThingOrDude(pHit))
            {
                if (HitCanBurn(pHit))
                {
                    if (pHit.StatNum == K.kStatThing && pHit.Type == K.kThingTNTBarrel && pHit.BurnTime == 0)
                        EvPostCallback(pHit, 0, K.kCallbackFXFlameLick);
                    ActBurnSprite(pMissile.Owner, pHit, 480);
                    ActRadiusDamage(nOwner, pMissile.BX, pMissile.BY, pMissile.BZ, 16, 20, 10, K.kDamageBullet, 6, 480);
                    // NBlood's v1.0x weapons: a burning flare hits too
                    if (WeaponsV10x)
                        ActDamageSprite(nOwner, pHit, K.kDamageBullet, (20 + Random(10)) * 16);
                }
                else
                    ActDamageSprite(nOwner, pHit, K.kDamageBullet, (20 + Random(10)) * 16);
                if (Tiles.SurfType(pHit.Picnum) == K.kSurfFlesh)
                {
                    pMissile.Picnum = 2123;
                    pMissile.XTarget = pHit;
                    pMissile.TargetZ = pMissile.BZ - pHit.BZ;
                    pMissile.GoalAng = Blood.GetAngle(pMissile.BX - pHit.BX, pMissile.BY - pHit.BY) - pHit.Ang;
                    pMissile.XState = 1;
                    ActPostSprite(pMissile, K.kStatFlare);
                    MakeNotSolid(pMissile);
                    return;
                }
            }
            GibSprite(pMissile, K.GIBTYPE_17);
            ActPostSprite(pMissile, K.kStatFree);
        }
        else if (mtype == K.kMissileFlameSpray || mtype == K.kMissileFlameHound)
        {
            if (hitCode == 3 && pHit != null && pHit.HasX)
            {
                BurnHit(pMissile, pHit, (4 + Skill) * 4);
                ActDamageSprite(nOwner, pHit, K.kDamageBurn, 8);
            }
        }
        else if (mtype == K.kMissileFireballCerberus)
        {
            ActExplodeSprite(pMissile);
            if (hitCode == 3 && pHit != null && pHit.HasX)
            {
                BurnHit(pMissile, pHit, (4 + Skill) * 4);
                ActDamageSprite(nOwner, pHit, K.kDamageBurn, 8);
                ActDamageSprite(nOwner, pHit, K.kDamageBullet, (25 + Random(10)) * 16);
            }
        }
        else if (mtype == K.kMissileFireballTchernobog)
        {
            ActExplodeSprite(pMissile);
            if (hitCode == 3 && pHit != null && pHit.HasX)
            {
                BurnHit(pMissile, pHit, 32);
                ActDamageSprite(nOwner, pHit, K.kDamageSpirit, 12);
                ActDamageSprite(nOwner, pHit, K.kDamageBullet, (25 + Random(10)) * 16);
            }
        }
        else if (mtype == K.kMissileEctoSkull)
        {
            Kill3DSound(pMissile);
            Sound3DAt(pMissile.Origin, 522);
            ActPostSprite(pMissile, K.kStatDebris);
            Seq.Spawn(this, 20, pMissile);
            if (hitCode == 3 && pHit != null && pHit.StatNum == K.kStatDude)
                ActDamageSprite(nOwner, pHit, K.kDamageSpirit, (25 + Random(10)) * 16);
        }
        else if (mtype == K.kMissileButcherKnife)
        {
            ActPostSprite(pMissile, K.kStatDebris);
            pMissile.Cstat &= ~16;
            pMissile.Type = K.kSpriteDecoration;
            Seq.Spawn(this, 20, pMissile);
            if (hitCode == 3 && pHit != null && pHit.StatNum == K.kStatDude)
            {
                ActDamageSprite(nOwner, pHit, K.kDamageSpirit, (10 + Random(10)) * 16);
                if (nOwner != null && nOwner.HasX && nOwner.XHealth > 0)
                    ActHealDude(nOwner, 10, Info.Dude(nOwner.Type, K.DI_startHealth));
            }
        }
        else if (mtype == K.kMissileTeslaRegular)
        {
            Kill3DSound(pMissile);
            Sound3DAt(pMissile.Origin, 518);
            GibSprite(pMissile, hitCode == 2 ? K.GIBTYPE_23 : K.GIBTYPE_22);
            EvKill(pMissile);
            Seq.Kill(pMissile);
            ActPostSprite(pMissile, K.kStatFree);
            if (hitCode == 3 && pHit != null)
                ActDamageSprite(nOwner, pHit, K.kDamageTesla, (15 + Random(10)) * 16);
        }
        else
        {
            Seq.Kill(pMissile);
            ActPostSprite(pMissile, K.kStatFree);
            if (hitCode == 3 && pHit != null)
                ActDamageSprite(nOwner, pHit, K.kDamageFall, (10 + Random(10)) * 16);
        }
        // a modern map: a sprite with the Impact flag is triggered by the missile that hits it
        if (IsModern && pHit != null && !pHit.Removed && pHit.HasX && pHit.XState != pHit.RestState && pHit.XImpact != 0)
            TrTriggerSprite(pHit, K.kCmdSpriteImpact, nOwner);
        if (!pMissile.Removed)
            MakeNotSolid(pMissile);
    }

    void ActKickObject(Sprite e1, Sprite e2)
    {
        double nSpeed = Blood.ClipLow(Blood.ApproxDist(e1.XVel, e1.YVel) * 2, 699050);
        e2.XVel = Blood.MulScale30(nSpeed, Blood.Cos(e1.Ang + Random2(85)));
        e2.YVel = Blood.MulScale30(nSpeed, Blood.Sin(e1.Ang + Random2(85)));
        e2.ZVel = Blood.MulScale14(nSpeed, -8192);
        e2.HitTag = 7;
    }

    /// <summary>actTouchFloor: sector damage and lava.</summary>
    void ActTouchFloor(Sprite e)
    {
        var sec = SpriteSector(e);
        // (a modern map's damage sector hurts only while it is on)
        if (sec != null && (sec.Type == K.kSectorDamage || sec.DamageType > 0)
            && !(IsModern && sec.Type == K.kSectorDamage && sec.XState == 0))
        {
            int nDamageType = sec.Type == K.kSectorDamage ? (int)Blood.ClipRange(sec.DamageType, K.kDamageFall, K.kDamageTesla)
                : (int)Blood.ClipRange(sec.DamageType - 1, K.kDamageFall, K.kDamageTesla);
            int nDamage = sec.SData != 0 ? (int)Blood.ClipRange(sec.SData, 0, 1000) : 1000;
            ActDamageSprite(e, e, nDamageType, Blood.Scale3(4, nDamage, 120) * 16);
        }
        if (e.FlorSurf == K.kSurfLava)
        {
            ActDamageSprite(e, e, K.kDamageBurn, 16);
            Sound3D(e, 352, 5, 2);
        }
    }
}
