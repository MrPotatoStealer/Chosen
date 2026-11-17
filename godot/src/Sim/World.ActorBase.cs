using System;
using Chosen.Data;
using Godot;
using static Chosen.Data.GenTables;

namespace Chosen.Sim;

/// <summary>
/// Actor basics (actor.cpp / db.cpp, via Blood3D's server/actor_base.qc): the per-type tables, deferred status
/// changes (actPostSprite), spawning sprites and dropping items, healing.
/// </summary>
public sealed partial class World
{
    public static int MissileInfo(int nType, int field) =>
        nType < K.kMissileBase || nType >= K.kMissileMax ? 0 : tbl_missile[(nType - K.kMissileBase) * tbl_missile_STRIDE + field];
    public static int ExplodeInfo(int n, int field) => tbl_explode[n * tbl_explode_STRIDE + field];
    public static int VectorData(int n, int field) => tbl_vector[n * tbl_vector_STRIDE + field];
    public static int VectorSurfHit(int n, int surf, int k) => tbl_vector[n * tbl_vector_STRIDE + 8 + surf * 4 + k];

    public static bool IsBurningDude(Sprite e)
    {
        int t = e.Type;
        return t == K.kDudeBurningInnocent || t == K.kDudeBurningCultist || t == K.kDudeBurningZombieAxe
            || t == K.kDudeBurningZombieButcher || t == K.kDudeBurningTinyCaleb || t == K.kDudeBurningBeast;
    }

    /// <summary>DUDEINFO.curDamage scaled by difficulty (actInit) or, for players, by their skill's handicap.</summary>
    public static int DudeDifficulty(int skill) => skill <= 0 ? 512 : skill == 1 ? 384 : skill == 2 ? 256 : skill == 3 ? 208 : 160;

    /// <summary>A dude's mass: its kind's, a custom dude's own.</summary>
    public static int DudeMass(Sprite e) =>
        e.Type == KM.kDudeModernCustom && e.Custom != null ? e.Custom.Mass : Info.Dude(e.Type, K.DI_mass);

    public int DmgControl(Sprite e, int n)
    {
        if (e is Player p)
            return (int)Blood.MulScale8(tbl_handicap[(int)Blood.ClipRange(Skill, 0, 4)], Info.PlayerTemplate(p.LifeMode, K.DI_startDamage + n));
        return (int)Blood.MulScale8(DudeDifficulty(Skill), Info.Dude(e.Type, K.DI_startDamage + n));
    }

    // ---------------------------------------------------------------- lifecycle
    /// <summary>InsertSprite: a new sprite (not yet in a status list's place: callers set it).</summary>
    public Sprite InsertSprite()
    {
        var e = new Sprite { ClassName = "blood_sprite", Cstat = Cstat.YCenter, ClipDist = 32, XRepeat = 64, YRepeat = 64, StatNum = K.kStatDecoration, SprIndex = ++_spriteCounter };
        e.HasX = false;
        AddSprite(e);
        return e;
    }

    /// <summary>dbInsertXSprite: give a sprite x-data.</summary>
    public static void DbInsertXSprite(Sprite e)
    {
        if (e.HasX)
            return;
        e.HasX = true;
        e.XTarget = null;
        e.BurnSource = null;
        e.AiState = null;
    }

    /// <summary>actPostSprite: Blood defers status changes to the end of the frame (actPostProcess). Pending sprites
    /// carry kHitagFree and are skipped by the per-status loops.</summary>
    public void ActPostSprite(Sprite e, int nStatus)
    {
        if (e == null || e.Removed)
            return;
        e.HitTag |= K.kHitagFree;
        e.PostStat = nStatus;
        e.Posted = true;
    }

    void ActPostProcess()
    {
        foreach (var e in Sprites.ToArray())
        {
            if (!e.Posted || e.Removed)
                continue;
            e.Posted = false;
            e.HitTag &= ~K.kHitagFree;
            if (e.PostStat == K.kStatFree)
            {
                EvKill(e);
                Seq.Kill(e);
                Delete(e);
            }
            else
                e.StatNum = e.PostStat;
        }
    }

    /// <summary>actSpawnSprite(x, y, z, nStat, bXSprite).</summary>
    public Sprite ActSpawnSprite(double x, double y, double z, int nStat, bool bXSprite)
    {
        var e = InsertSprite();
        e.StatNum = nStat;
        e.Origin = B2Q(x, y, z);
        e.PrevOrigin = e.Origin;
        e.XVel = e.YVel = e.ZVel = 0;
        if (bXSprite)
            DbInsertXSprite(e);
        return e;
    }

    /// <summary>actSpawnSprite(pSource, nStat).</summary>
    public Sprite ActSpawnSpriteFrom(Sprite src, int nStat)
    {
        var e = InsertSprite();
        e.StatNum = nStat;
        e.Origin = e.PrevOrigin = src.Origin;
        e.XVel = src.XVel;
        e.YVel = src.YVel;
        e.ZVel = src.ZVel;
        e.HitTag = 0;
        DbInsertXSprite(e);
        return e;
    }

    // ---------------------------------------------------------------- drops (actDrop*)
    Sprite ActSpawnFloor(Sprite src)
    {
        // actSpawnFloor: on the floor under it, in the sector holding its x, y
        int ns = Board.UpdateSector(src.BX, src.BY, SpriteBSector(src));
        double fz = ns >= 0 ? Board.FloorZAt(ns, src.BX, src.BY) : src.BZ;
        var spawned = ActSpawnSprite(src.BX, src.BY, fz, K.kStatItem, false);
        if (ns >= 0)
            SetSpriteBSector(spawned, ns);
        spawned.Cstat &= ~(Cstat.Block | Cstat.Hitscan);
        return spawned;
    }

    Sprite ActDropTyped(Sprite src, int nType, Func<int, int, int> data)
    {
        var e = ActSpawnFloor(src);
        e.Type = e.InitType = nType;
        e.Picnum = data(nType, 1);
        e.Shade = data(nType, 2);
        e.XRepeat = data(nType, 4);
        e.YRepeat = data(nType, 5);
        e.ClassName = "dropped";
        return e;
    }

    Sprite ActDropKey(Sprite src, int nType)
    {
        if (src == null || nType < K.kItemKeyBase || nType >= K.kItemKeyMax)
            return null;
        var e = ActDropTyped(src, nType, Info.Item);
        if (GameType == K.kGameTypeCoop)
        {
            DbInsertXSprite(e);
            e.XRespawn = 3;
            e.FlorHit = 0;
            e.CeilHit = 0;
        }
        return e;
    }

    public Sprite ActDropObject(Sprite src, int nType)
    {
        Sprite e = null;
        if (src == null)
            return null;
        if (nType >= K.kItemKeyBase && nType < K.kItemKeyMax) e = ActDropKey(src, nType);
        else if (nType >= K.kItemBase && nType < K.kItemMax) e = ActDropTyped(src, nType, Info.Item);
        else if (nType >= K.kItemAmmoBase && nType < K.kItemAmmoMax) e = ActDropTyped(src, nType, Info.AmmoItem);
        else if (nType >= K.kItemWeaponBase && nType < K.kItemWeaponMax) e = ActDropTyped(src, nType, Info.WeaponItem);
        if (e != null)
        {
            double z = e.BZ;
            var (_, bottom) = Physics.SpriteExtents(z, e.Picnum, e.YRepeat, e.Cstat);
            if (bottom >= z)
                e.SetBZ(z - (bottom - z));
            UpdateBox(e);
        }
        return e;
    }

    /// <summary>actHealDude(pXDude, a2, a3): heal by a2 up to a3 (health points).</summary>
    public bool ActHealDude(Sprite e, int a2, int a3)
    {
        a2 *= 16;
        a3 *= 16;
        if (e.XHealth < a3)
        {
            if (e is Player)
                Sound3DAt(e.Origin, 780);
            e.XHealth = Blood.ClipHigh(e.XHealth + a2, a3);
            return true;
        }
        return false;
    }

    /// <summary>A sound at a point (sfxPlay3DSoundXYZ).</summary>
    public void Sound3DAt(Vector3 at, int soundId)
    {
        if (soundId != 0)
            SoundAtPlayed?.Invoke(at, soundId);
        if (IsModern)
            PatrolNoteSound(null, Q2BX(at), Q2BY(at), SectorOfPointB(Q2BX(at), Q2BY(at), Q2BZ(at)), soundId, 0);
        if (SfxData.HasFile(soundId))
            _tempSlots.Add((Edicts.Alloc(_edictTime), Frame + 300));     // an sfx_temp entity for 10 s
    }

    public event Action<Vector3, int> SoundAtPlayed;
}
