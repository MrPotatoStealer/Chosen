using System;
using System.Collections.Generic;
using Chosen.Data;
using Chosen.Sim.Collision;
using Godot;

namespace Chosen.Sim;

/// <summary>
/// The player (player.cpp, via Blood3D's server/player.qc and main.qc's playerLatchInput): input, movement
/// acceleration, jumping and crouching, the view's bob and inertia, use (ActionScan), pickups, powerups and the
/// pack, damage and death, start and reset.
/// </summary>
public sealed partial class World
{
    /// <summary>A start spot (info_player_start / info_player_deathmatch).</summary>
    public readonly struct StartSpot
    {
        public readonly Vector3 Origin;
        public readonly int Ang, Data1;
        public readonly bool Deathmatch;
        public readonly int Sector;      // the marker's Build sector, -1 not known
        public StartSpot(Vector3 o, int ang, int data1, bool dm, int sector = -1) { Origin = o; Ang = ang; Data1 = data1; Deathmatch = dm; Sector = sector; }
    }

    public readonly List<StartSpot> StartSpots = new();
    public bool InfiniteAmmo;             // gInfiniteAmmo
    public int WeaponSettings;            // gWeaponSettings (0 in single player)

    // ---------------------------------------------------------------- input (main.qc playerLatchInput)
    /// <summary>playerLatchInput: the frame's input onto the player.</summary>
    public static void PlayerLatchInput(Player p, in PlayerInput inp)
    {
        p.InputForward = Blood.ClipRange(inp.Forward, -2048, 2048);
        p.InputStrafe = Blood.ClipRange(inp.Strafe, -2048, 2048);
        p.InputShoot = inp.Fire;
        p.InputShoot2 = inp.AltFire;
        p.InputJump = inp.Jump;
        p.InputCrouch = inp.Crouch;
        if (inp.Use && !p.LastButton5)
            p.InputAction = true;
        p.LastButton5 = inp.Use;
        if (p.XHealth > 0 && p.Spin >= 0)
            p.Ang = (int)Blood.YawToAng(inp.Yaw);
        double pitch = inp.Pitch;
        if (pitch > 180)
            pitch -= 360;
        p.Look = Blood.ClipRange(-pitch * 2048 / 360, -347, 289);
        int imp = inp.Impulse;
        if (imp != 0)
        {
            if (imp >= 1 && imp <= 10) p.InputNewWeapon = imp;
            else if (imp == 11) p.InputNextWeapon = true;
            else if (imp == 12) p.InputPrevWeapon = true;
            else if (imp == 13) p.InputLastWeapon = true;
            else if (imp == 14) p.InputHolster = true;
            else if (imp == 20) p.InputUseItem = true;
            else if (imp == 21) p.InputNextItem = true;
            else if (imp == 22) p.InputPrevItem = true;
            else if (imp == 23) p.UseMedKit = true;
            else if (imp == 24) p.UseCrystalBall = true;
            else if (imp == 25) p.UseBeastVision = true;
            else if (imp == 26) p.UseJumpBoots = true;
            else if (imp == 27) p.InputSpin180 = true;
        }
    }

    // ---------------------------------------------------------------- powerups / pack
    public static double PowerupInfo(int n, int f) => GenTables.tbl_powerup[n * 4 + f];
    public static double AmmoMax(int n) => GenTables.tbl_ammoinfo[n * 2];

    static int PowerupToPackItem(int nPowerUp) =>
        nPowerUp == K.kPwUpDivingSuit ? Player.PackDivingSuit : nPowerUp == K.kPwUpCrystalBall ? Player.PackCrystalBall
        : nPowerUp == K.kPwUpBeastVision ? Player.PackBeastVision : nPowerUp == K.kPwUpJumpBoots ? Player.PackJumpBoots : -1;

    static int PackItemToPowerup(int nPack) =>
        nPack == Player.PackDivingSuit ? K.kPwUpDivingSuit : nPack == Player.PackCrystalBall ? K.kPwUpCrystalBall
        : nPack == Player.PackBeastVision ? K.kPwUpBeastVision : nPack == Player.PackJumpBoots ? K.kPwUpJumpBoots : -1;

    public static bool PackItemActive(Player p, int nPack) => p.PackActive[nPack] != 0;

    public static double PowerupCheck(Sprite e, int nPowerUp)
    {
        if (e is not Player p)
            return 0;
        int nPack = PowerupToPackItem(nPowerUp);
        if (nPack >= 0 && !PackItemActive(p, nPack))
            return 0;
        return p.PwUpTime[nPowerUp];
    }

    static bool PowerupAkimboWeapons(int w) =>
        w == K.kWeaponFlare || w == K.kWeaponShotgun || w == K.kWeaponTommy || w == K.kWeaponNapalm || w == K.kWeaponTesla;

    static void AddAllDamageControl(Player p, int d)
    {
        for (int i = 0; i < K.kDamageMax; i++)
            p.DamageControl[i] += d;
    }

    public bool PowerupActivate(Player p, int nPowerUp)
    {
        if (PowerupCheck(p, nPowerUp) > 0 && PowerupInfo(nPowerUp, 1) != 0)
            return false;
        if (p.PwUpTime[nPowerUp] == 0)
            p.PwUpTime[nPowerUp] = PowerupInfo(nPowerUp, 2);
        int nPack = PowerupToPackItem(nPowerUp);
        if (nPack >= 0)
            p.PackActive[nPack] = 1;
        PowerupDamageControl(p, nPowerUp, 1);
        Sound3D(p, 776, -1);
        return true;
    }

    void PowerupDamageControl(Player p, int nPowerUp, int d)
    {
        int item = nPowerUp + K.kItemBase;
        if (item == K.kItemFeatherFall || item == K.kItemJumpBoots)
            p.DamageControl[K.kDamageFall] += d;
        else if (item == K.kItemDeathMask)
            AddAllDamageControl(p, d);
        else if (item == K.kItemDivingSuit || item == K.kItemGasMask)
            p.DamageControl[K.kDamageDrown] += d;
        else if (item == K.kItemArmorAsbest)
            p.DamageControl[K.kDamageBurn] += d;
        else if (item == K.kItemTwoGuns && PowerupAkimboWeapons(p.CurWeapon))
        {
            p.InputNewWeapon = p.CurWeapon;
            WeaponRaise(p);
        }
    }

    public void PowerupDeactivate(Player p, int nPowerUp)
    {
        int nPack = PowerupToPackItem(nPowerUp);
        if (nPack >= 0)
            p.PackActive[nPack] = 0;
        PowerupDamageControl(p, nPowerUp, -1);
    }

    void PowerupSetState(Player p, int nPowerUp, bool bState)
    {
        if (!bState)
            PowerupActivate(p, nPowerUp);
        else
            PowerupDeactivate(p, nPowerUp);
    }

    void PowerupProcess(Player p)
    {
        p.PackItemTime = (int)Blood.ClipLow(p.PackItemTime - GameClock.TicsPerFrame, 0);
        for (int i = K.kMaxPowerUps - 1; i >= 0; i--)
        {
            int nPack = PowerupToPackItem(i);
            if (nPack >= 0)
            {
                if (p.PackActive[nPack] != 0)
                {
                    double t = Blood.ClipLow(p.PwUpTime[i] - GameClock.TicsPerFrame, 0);
                    p.PwUpTime[i] = t;
                    if (t != 0)
                        p.PackAmount[nPack] = Blood.Int(100 * t / PowerupInfo(i, 2));
                    else
                    {
                        PowerupDeactivate(p, i);
                        if (p.PackItemId == nPack)
                            p.PackItemId = 0;
                    }
                }
            }
            else if (p.PwUpTime[i] > 0)
            {
                double t = Blood.ClipLow(p.PwUpTime[i] - GameClock.TicsPerFrame, 0);
                p.PwUpTime[i] = t;
                if (t == 0)
                    PowerupDeactivate(p, i);
            }
        }
    }

    static void PowerupClear(Player p) => Array.Clear(p.PwUpTime);

    bool PackAddItem(Player p, int nPack)
    {
        if (nPack < Player.PackMax)
        {
            if (p.PackAmount[nPack] >= 100)
                return false;
            p.PackAmount[nPack] = 100;
            int nPowerUp = PackItemToPowerup(nPack);
            if (nPowerUp >= 0)
                p.PwUpTime[nPowerUp] = PowerupInfo(nPowerUp, 2);
            if (p.PackItemId == -1)
                p.PackItemId = nPack;
            if (p.PackAmount[p.PackItemId] == 0)
                p.PackItemId = nPack;
        }
        return true;
    }

    void PackUseItem(Player p, int nPack)
    {
        bool bActivate = false;
        int nPowerUp = -1;
        if (p.PackAmount[nPack] > 0)
        {
            if (nPack == Player.PackMedKit)
            {
                double health = Blood.Sar(p.XHealth, 4);
                if (health < 100)
                {
                    double heal = Blood.ClipHigh(100 - health, p.PackAmount[Player.PackMedKit]);
                    ActHealDude(p, (int)heal, 100);
                    p.PackAmount[Player.PackMedKit] -= heal;
                }
            }
            else
            {
                bActivate = true;
                nPowerUp = PackItemToPowerup(nPack);
            }
        }
        p.PackItemTime = 0;
        if (bActivate)
            PowerupSetState(p, nPowerUp, PackItemActive(p, nPack));
    }

    static void PackPrevItem(Player p)
    {
        if (p.PackItemTime > 0)
            for (int n = Math.Max(p.PackItemId - 1, 0); n >= 0; n--)
                if (p.PackAmount[n] != 0)
                {
                    p.PackItemId = n;
                    break;
                }
        p.PackItemTime = 600;
    }

    static void PackNextItem(Player p)
    {
        if (p.PackItemTime > 0)
            for (int n = Math.Min(p.PackItemId + 1, Player.PackMax); n < Player.PackMax; n++)
                if (p.PackAmount[n] != 0)
                {
                    p.PackItemId = n;
                    break;
                }
        p.PackItemTime = 600;
    }

    static bool PlayerSeqPlaying(Player p, int nSeq) =>
        Info.Dude(K.kDudePlayer1, Info.DI_seqStartID) + nSeq == Seq.Id(p) && Seq.Status(p) >= 0;

    public static void PlayerSetGodMode(Player p, bool on)
    {
        AddAllDamageControl(p, on ? 1 : -1);
        p.GodMode = on;
    }

    static void PlayerResetPowerUps(Player p)
    {
        for (int i = 0; i < K.kMaxPowerUps; i++)
            if (i != K.kPwUpJumpBoots && i != K.kPwUpDivingSuit && i != K.kPwUpCrystalBall && i != K.kPwUpBeastVision)
                p.PwUpTime[i] = 0;
    }

    static void PlayerResetPosture(Player p)
    {
        p.BobPhase = p.BobAmp = p.SwayAmp = p.BobHeight = p.BobWidth = p.SwayHeight = p.SwayWidth = 0;
    }

    public bool IsTargetTeammate(Sprite src, Sprite t)
    {
        if (src is not Player sp || t is not Player tp || src == t)
            return false;
        if (GameType == K.kGameTypeCoop)
            return true;
        return GameType == K.kGameTypeTeams && (sp.TeamId & 3) == (tp.TeamId & 3);
    }

    void PlayerClearFragger(Sprite dude)
    {
        foreach (var p in Players)
            if (p.FraggerId == dude && p.DeathTime > 0)
                p.FraggerId = null;
    }

    // ---------------------------------------------------------------- start / reset
    public void PlayerReset(Player p)
    {
        for (int i = 0; i < K.kWeaponMax; i++)
        {
            p.HasWeapon[i] = InfiniteAmmo ? 1 : 0;
            p.WeaponMode[i] = 0;
        }
        p.HasWeapon[K.kWeaponPitchfork] = 1;
        p.CurWeapon = K.kWeaponNone;
        p.QavCallback = null;
        p.InputNewWeapon = K.kWeaponPitchfork;
        p.LastWeapon = K.kWeaponPitchfork;
        for (int i = 0; i < 12; i++)
            p.Ammo[i] = InfiniteAmmo ? AmmoMax(i) : 0;
        Array.Clear(p.Armor);
        p.WeaponTimer = 0;
        p.WeaponState = 0;
        p.WeaponQav = -1;
        p.QavLoop = false;
        p.PackItemId = -1;
        Array.Clear(p.PackActive);
        Array.Clear(p.PackAmount);
        PlayerResetPosture(p);
    }

    StartSpot? FindStartSpot(int nPlayer)
    {
        if (GameType == K.kGameTypeBloodBath || GameType == K.kGameTypeTeams)
        {
            var dm = StartSpots.FindAll(s => s.Deathmatch);
            if (dm.Count > 0)
                return dm[Random(dm.Count)];
        }
        StartSpot? first = null, best = null;
        foreach (var s in StartSpots)
        {
            if (s.Deathmatch)
                continue;
            first ??= s;
            if (s.Data1 == nPlayer)
                best = s;
        }
        if (best != null)
            return best;
        if (first != null)
            return first;
        foreach (var s in StartSpots)
            if (s.Deathmatch)
                return s;
        return null;
    }

    /// <summary>Adds a player to the level at its start spot (PutClientInServer + playerReset + playerStart).</summary>
    public Player AddPlayer(int index = 0)
    {
        var p = new Player { PlayerIndex = index, TeamId = index };
        PlayerReset(p);
        AddSprite(p);
        Edicts.Alloc(_edictTime);      // its view entity (net.qc netCreateView)
        Players.Add(p);
        PlayerStart(p, true);
        return p;
    }

    public void PlayerStart(Player p, bool bNewLevel)
    {
        var start = FindStartSpot(p.PlayerIndex);
        Kill3DSound(p);
        if (start is StartSpot s)
        {
            p.Origin = s.Origin;
            p.Ang = s.Ang;
        }
        p.Removed = false;
        p.StatNum = K.kStatDude;
        p.LifeMode = Player.ModeHuman;
        p.Type = K.kDudePlayer1 + p.PlayerIndex;
        p.InitType = p.Type;
        p.ClipDist = Info.PlayerTemplate(Player.ModeHuman, Info.DI_clipdist);
        PlayerResetPosture(p);
        p.SeqPlaying = false;
        p.SeqId = -1;
        p.XRepeat = 64;
        p.YRepeat = 64;
        p.Cstat = Cstat.YCenter | Cstat.Block | Cstat.Hitscan;
        Seq.Spawn(this, Info.Dude(K.kDudePlayer1, Info.DI_seqStartID), p);
        var (_, bottom) = Physics.SpriteExtents(p.BZ, p.Picnum, p.YRepeat, p.Cstat);
        p.SetBZ(p.BZ - (bottom - p.BZ));
        // in the marker's own sector (Blood's start zone): where rooms lie over each other the place doesn't say which (E1M6's
        // start, its room inside another, was looked up as a room under it: dropped there among its cultists)
        if (start is StartSpot at && at.Sector >= 0)
            SetSpriteBSector(p, at.Sector);
        p.Pal = 11 + (p.TeamId & 3);
        p.AngOld = p.Ang;
        p.HitTag = 15;
        p.BurnTime = 0;
        p.BurnSource = null;
        p.XHealth = Info.PlayerTemplate(Player.ModeHuman, Info.DI_startHealth) * 16;
        p.Cstat &= ~Cstat.Invisible;
        p.Horiz = p.SlopeHoriz = p.Look = p.Slope = 0;
        p.FraggerId = null;
        p.UnderwaterTime = 1200;
        p.BubbleTime = 0;
        p.RestTime = 0;
        p.KickPower = 0;
        p.LaughCount = 0;
        p.Spin = 0;
        p.Posture = Player.PostureStand;
        p.VoodooTarget = null;
        p.VoodooTargets = 0;
        p.VoodooVar1 = p.VoodooVar2 = 0;
        PlayerResetInertia(p);
        p.ZWeaponVel = 0;
        p.RelAimDx = 16384;
        p.RelAimDy = p.RelAimDz = 0;
        p.AimTarget = null;
        p.ZViewVel = p.ZWeaponVel;
        for (int i = 0; i < 8; i++)
            p.HasKey[i] = GameType >= K.kGameTypeBloodBath ? 1 : 0;
        p.HasFlag = 0;
        Array.Clear(p.DamageControl);
        if (p.GodMode)
            PlayerSetGodMode(p, true);
        p.ThrowPower = 0;
        p.DeathTime = 0;
        p.NextWeapon = K.kWeaponNone;
        p.LastWeapon = K.kWeaponPitchfork;
        p.XVel = p.YVel = p.ZVel = 0;
        p.InputForward = p.InputStrafe = 0;
        p.FlickerEffect = p.QuakeEffect = p.TiltEffect = p.Visibility = p.PainEffect = p.BlindEffect = p.ChokeEffect = 0;
        p.HandTime = 0;
        p.ChokeCounter = 0;
        p.WeaponTimer = 0;
        p.WeaponState = 0;
        p.WeaponQav = -1;
        p.Hand = 0;
        PlayerResetPowerUps(p);
        p.Medium = Player.MediumNormal;
        if (PointIsWater(p.Origin))
        {
            p.Posture = Player.PostureSwim;
            p.Medium = Player.MediumWater;
        }
        UpdateBox(p);
        p.Ready = true;
        p.FixAngle = true;
        p.PrevOrigin = p.Origin;
        p.MoveSpeedPerc = p.JumpHeightPerc = 100;
        p.SceneQav = -1;
        ModernPlayerStart(p);
    }

    public bool PointIsWater(Vector3 p) => NativePointIsWater(p);

    // ---------------------------------------------------------------- pickups
    static readonly string[] ItemNames =
    {
        "Skull Key", "Eye Key", "Fire Key", "Dagger Key", "Spider Key", "Moon Key", "Key 7", "Doctor's Bag",
        "Medicine Pouch", "Life Essence", "Life Seed", "Red Potion", "Feather Fall", "Limited Invisibility",
        "INVULNERABILITY", "Boots of Jumping", "Raven Flight", "Guns Akimbo", "Diving Suit", "Gas mask", "Clone",
        "Crystal Ball", "Decoy", "Doppleganger", "Reflective shots", "Beast Vision", "ShadowCloak", "Rage shroom",
        "Delirium Shroom", "Grow shroom", "Shrink shroom", "Death mask", "Wine Goblet", "Wine Bottle", "Skull Grail",
        "Silver Grail", "Tome", "Black Chest", "Wooden Chest", "Asbestos Armor", "Basic Armor", "Body Armor",
        "Fire Armor", "Spirit Armor", "Super Armor", "Blue Team Base", "Red Team Base", "Blue Flag", "Red Flag",
    };

    static string ItemText(int n) { int i = n - K.kItemBase; return i >= 0 && i < ItemNames.Length ? ItemNames[i] : "item"; }

    static string AmmoText(int n) => (n - K.kItemAmmoBase) switch
    {
        0 => "Spray can", 1 => "Bundle of TNT*", 2 => "Bundle of TNT", 3 => "Case of TNT", 4 => "Proximity Detonator",
        5 => "Remote Detonator", 6 => "Trapped Soul", 7 => "4 shotgun shells", 8 => "Box of shotgun shells",
        9 => "A few bullets", 10 => "Voodoo Doll", 12 => "Full drum of bullets", 13 => "Tesla Charge", 16 => "Flares",
        19 => "Gasoline Can", _ => "ammo",
    };

    static string WeaponText(int n) => (n - K.kItemWeaponBase) switch
    {
        1 => "Sawed-off", 2 => "Tommy Gun", 3 => "Flare Pistol", 4 => "Voodoo Doll", 5 => "Tesla Cannon",
        6 => "Napalm Launcher", 7 => "Pitchfork", 8 => "Spray Can", 9 => "Dynamite", 10 => "Life Leech", _ => "RANDOM",
    };

    /// <summary>viewSetMessage: a message on this player's screen.</summary>
    public void ViewSetMessage(Player p, string msg)
    {
        if (p != null && p == Me)
            ShowMessage(msg);
    }

    bool FindDroppedLeech(Player p, Sprite skip)
    {
        foreach (var e in Sprites)
            if (e != skip && e.StatNum == K.kStatThing && e.Type == K.kThingDroppedLifeLeech && e.Owner == p && !e.Removed)
                return true;
        return false;
    }

    bool PickupItem(Player p, Sprite item)
    {
        int pickupSnd = 775;
        int nType = item.Type - K.kItemBase;
        int t = item.Type;
        if (t == K.kItemShadowCloak)
        {
            if (!PowerupActivate(p, nType)) return false;
        }
        else if (t == K.kItemFlagABase || t == K.kItemFlagBBase || t == K.kItemFlagA || t == K.kItemFlagB)
            return false;     // team flags: multiplayer only, not handled in single player
        else if (t >= K.kItemArmorBasic && t <= K.kItemArmorSuper)
        {
            int b = (t - K.kItemArmorBasic) * 6;
            var a = GenTables.tbl_armor;
            bool picked = false;
            if (p.Armor[1] < a[b + 3]) { p.Armor[1] = Blood.ClipHigh(p.Armor[1] + a[b + 2], a[b + 3]); picked = true; }
            if (p.Armor[0] < a[b + 1]) { p.Armor[0] = Blood.ClipHigh(p.Armor[0] + a[b], a[b + 1]); picked = true; }
            if (p.Armor[2] < a[b + 5]) { p.Armor[2] = Blood.ClipHigh(p.Armor[2] + a[b + 4], a[b + 5]); picked = true; }
            if (!picked) return false;
            pickupSnd = 779;
        }
        else if (t == K.kItemCrystalBall)
        {
            if (GameType == K.kGameTypeSinglePlayer || !PackAddItem(p, Info.Item(t, 6))) return false;
        }
        else if (t >= K.kItemKeySkull && t <= K.kItemKeyKey7)
        {
            int k = t - 99;
            if (k < 8 && p.HasKey[k] != 0) return false;
            if (k < 8) p.HasKey[k] = 1;
            pickupSnd = 781;
        }
        else if (t == K.kItemHealthMedPouch || t == K.kItemHealthLifeEssense || t == K.kItemHealthLifeSeed || t == K.kItemHealthRedPotion)
        {
            int addPower = (int)PowerupInfo(nType, 2);
            if (item.HasX && item.Data1 > 0)
                addPower = item.Data1;
            return ActHealDude(p, addPower, (int)PowerupInfo(nType, 3));
        }
        else if (t == K.kItemHealthDoctorBag || t == K.kItemJumpBoots || t == K.kItemDivingSuit || t == K.kItemBeastVision)
        {
            if (!PackAddItem(p, Info.Item(t, 6))) return false;
        }
        else
            return PowerupActivate(p, nType);
        Sound3DAt(p.Origin, pickupSnd);
        return true;
    }

    bool PickupAmmo(Player p, Sprite ammo)
    {
        int nAmmoType = Info.AmmoItem(ammo.Type, 7);
        int cnt = Info.AmmoItem(ammo.Type, 6);
        int wt = Info.AmmoItem(ammo.Type, 8);
        if (p.Ammo[nAmmoType] >= AmmoMax(nAmmoType)) return false;
        if (ammo.HasX && ammo.Data1 > 0)
            cnt = ammo.Data1;
        p.Ammo[nAmmoType] = Blood.ClipHigh(p.Ammo[nAmmoType] + cnt, AmmoMax(nAmmoType));
        if (wt != 0)
            p.HasWeapon[wt] = 1;
        Sound3D(p, 782, -1);
        return true;
    }

    bool PickupWeapon(Player p, Sprite w)
    {
        int nWeaponType = Info.WeaponItem(w.Type, 6);
        int nAmmoType = Info.WeaponItem(w.Type, 7);
        int cnt = Info.WeaponItem(w.Type, 8);
        if (w.HasX && w.Data1 > 0)
            cnt = w.Data1;
        if (p.HasWeapon[nWeaponType] == 0 || WeaponSettings == 2 || WeaponSettings == 3)
        {
            if (w.Type == K.kItemWeaponLifeLeech && GameType >= K.kGameTypeBloodBath && FindDroppedLeech(p, null))
                return false;
            p.HasWeapon[nWeaponType] = 1;
            if (nAmmoType == -1) return false;
            p.Ammo[nAmmoType] = Blood.ClipHigh(p.Ammo[nAmmoType] + cnt, AmmoMax(nAmmoType));
            int nNewWeapon = WeaponUpgrade(p, nWeaponType);
            if (nNewWeapon != p.CurWeapon)
            {
                p.WeaponState = 0;
                p.NextWeapon = nNewWeapon;
            }
            Sound3D(p, 777, -1);
            return true;
        }
        if (ActGetRespawnTime(w) == 0 || nAmmoType == -1 || p.Ammo[nAmmoType] >= AmmoMax(nAmmoType)) return false;
        p.Ammo[nAmmoType] = Blood.ClipHigh(p.Ammo[nAmmoType] + cnt, AmmoMax(nAmmoType));
        Sound3D(p, 777, -1);
        return true;
    }

    void PickUp(Player p, Sprite item)
    {
        bool picked = false;
        int nType = item.Type;
        string msg = "";
        if (nType >= K.kItemBase && nType <= K.kItemMax)
        {
            picked = PickupItem(p, item);
            if (picked) msg = "Picked up " + ItemText(nType);
        }
        else if (nType >= K.kItemAmmoBase && nType < K.kItemAmmoMax)
        {
            picked = PickupAmmo(p, item);
            if (picked) msg = "Picked up " + AmmoText(nType);
        }
        else if (nType >= K.kItemWeaponBase && nType < K.kItemWeaponMax)
        {
            picked = PickupWeapon(p, item);
            if (picked) msg = "Picked up " + WeaponText(nType);
        }
        if (!picked)
            return;
        if (item.HasX && item.XPickup != 0)
            TrTriggerSprite(item, K.kCmdSpritePickup, p);
        if (!ActCheckRespawn(item))
            ActPostSprite(item, K.kStatFree);
        p.PickupEffect = 30;
        // a modern map: the item's own message (of the level's messages) in place of "Picked up"
        if (IsModern && item.HasX && item.TxID != 3 && item.LockMsg > 0)
            TextOver(item.LockMsg - 1);
        else
            ViewSetMessage(p, msg);
    }

    void CheckPickUp(Player p)
    {
        double x = p.BX, y = p.BY, z = p.BZ;
        foreach (var item in Sprites.ToArray())
        {
            if (item.StatNum != K.kStatItem || item.Removed || (item.HitTag & 32) != 0)
                continue;
            double dx = Blood.Sar(Math.Abs(x - item.BX), 4);
            if (dx > 48)
                continue;
            double dy = Blood.Sar(Math.Abs(y - item.BY), 4);
            if (dy > 48)
                continue;
            var (top, bottom) = Physics.SpriteExtents(p.BZ, p.Picnum, p.YRepeat, p.Cstat);
            double vb = 0;
            if (item.BZ < top)
                vb = Blood.Sar(top - item.BZ, 8);
            else if (item.BZ > bottom)
                vb = Blood.Sar(item.BZ - bottom, 8);
            if (vb > 32)
                continue;
            if (Blood.ApproxDist(dx, dy) > 48)
                continue;
            var (itop, ibottom) = Physics.SpriteExtents(item.BZ, item.Picnum, item.YRepeat, item.Cstat);
            if (CanSee(x, y, z, item.BX, item.BY, item.BZ, p, item) || CanSee(x, y, z, item.BX, item.BY, itop, p, item)
                || CanSee(x, y, z, item.BX, item.BY, ibottom, p, item))
                PickUp(p, item);
        }
    }

    // ---------------------------------------------------------------- action (use) scan
    XObject _actionTarget;

    /// <summary>ActionScan (player.cpp) on a level from its Build data: Blood's HitScan along the view (walls cstat 64,
    /// sprites flagged pushable 4096) within 64 units: a sprite, a wall to push (or the next sector's wall push), a floor
    /// or ceiling's sector to push; else the player's own sector.</summary>
    int NativeActionScan(Player p)
    {
        _actionTarget = null;
        double x = Blood.Sar(Blood.Cos(p.Ang), 16), y = Blood.Sar(Blood.Sin(p.Ang), 16), z = p.Slope;
        int nHit = NativeHitScan(p, p.ZView, x, y, z, 128, 64, 4096);
        double hitDist = Blood.Sar(Blood.ApproxDist(p.BX - HitX, p.BY - HitY), 4);
        if (nHit >= 0 && hitDist < 64)
        {
            switch (nHit)
            {
                case 3:
                {
                    var e = (Sprite)HitEnt;
                    _actionTarget = e;
                    if (e.HasX && e.StatNum == K.kStatThing && e.Type == K.kThingDroppedLifeLeech)
                    {
                        if (GameType >= K.kGameTypeBloodBath && FindDroppedLeech(p, e))
                            return -1;
                        e.Data4 = p.PlayerIndex;
                        e.IsTriggered = 0;
                    }
                    if (e.HasX && e.XPush != 0)
                        return 3;
                    if (e.StatNum == K.kStatDude)
                    {
                        int nMass = DudeMass(e);
                        if (nMass != 0)
                        {
                            double t2 = Blood.Int(838860.0 * 256 / nMass);
                            e.XVel += Blood.MulScale16(x, t2);
                            e.YVel += Blood.MulScale16(y, t2);
                            e.ZVel += Blood.MulScale16(z, t2);
                        }
                        if (e.HasX && e.XPush != 0 && e.XState == 0 && e.IsTriggered == 0)
                            TrTriggerSprite(e, K.kCmdSpritePush, p);
                    }
                    break;
                }
                case 0:
                case 4:
                {
                    int w = HitWallB;
                    if (w >= 0 && w < _xwall.Length && _xwall[w] is { } xw && xw.XPush != 0)
                    {
                        _actionTarget = xw;
                        return 0;
                    }
                    int ns = w >= 0 ? Board.Walls[w].NextSector : -1;
                    if (ns >= 0 && XSectorOf(ns) is { } nxs && nxs.SWallpush != 0)
                    {
                        _actionTarget = nxs;
                        return 6;
                    }
                    break;
                }
                case 1:
                case 2:
                    if (XSectorOf(HitSectB) is { } hxs && hxs.XPush != 0)
                    {
                        _actionTarget = hxs;
                        return 6;
                    }
                    break;
            }
        }
        if (XSectorOf(SpriteBSector(p)) is { } own && own.XPush != 0)
        {
            _actionTarget = own;
            return 6;
        }
        return -1;
    }

    int ActionScan(Player p)
    {
        return NativeActionScan(p);
    }

    void PlayerDropHand(Player p)
    {
        var h = ActSpawnDude(p, K.kDudeHand, p.ClipDist * 2, 0);
        if (h != null)
        {
            h.Ang = (int)Blood.AngMask(p.Ang + 1024);
            double x = Blood.Sar(Blood.Cos(p.Ang), 16), y = Blood.Sar(Blood.Sin(p.Ang), 16);
            h.XVel = p.XVel + Blood.MulScale14(1398101, x);
            h.YVel = p.YVel + Blood.MulScale14(1398101, y);
            h.ZVel = p.ZVel;
        }
        p.Hand = 0;
    }

    /// <summary>gFloorNormal: the normal of the floor the last GetZRange found (slope look-ahead).</summary>
    public Vector3 FloorNormal = Vector3.Up;

    void UseKeyed(Player p, XObject t, Action trigger)
    {
        int key = t.XKey;
        if (t.XLocked != 0)
        {
            ViewSetMessage(p, "It's locked");
            SoundLocal(p, 3062);
        }
        if (key == 0 || (key < 8 && p.HasKey[key] != 0))
            trigger();
        else
        {
            ViewSetMessage(p, "That requires a key.");
            SoundLocal(p, 3063);
        }
    }

    void ProcessInput(Player p)
    {
        p.IsRunning = Math.Abs(p.InputForward) > 1024 || Math.Abs(p.InputStrafe) > 1024;
        if (p.InputShoot || p.InputShoot2 || p.InputJump || p.InputCrouch || p.InputForward != 0 || p.InputStrafe != 0)
            p.RestTime = 0;
        else if (p.RestTime >= 0)
            p.RestTime += GameClock.TicsPerFrame;
        if (p.XHealth == 0)
        {
            WeaponProcess(p);
            bool bSeqStat = PlayerSeqPlaying(p, 16);
            if (p.FraggerId != null && !p.FraggerId.Removed)
            {
                p.Ang = Blood.GetAngle(p.FraggerId.BX - p.BX, p.FraggerId.BY - p.BY);
                p.AngOld = p.Ang;
                p.FixAngle = true;
            }
            p.DeathTime += GameClock.TicsPerFrame;
            if (!bSeqStat)
                p.Horiz = Blood.MulScale16(32768 - Blood.Sar(Blood.Cos(Blood.ClipHigh(p.DeathTime * 8, 1024)), 15), 120);
            if (p.CurWeapon != 0)
                p.InputNewWeapon = p.CurWeapon;
            if (p.InputAction)
            {
                if (bSeqStat)
                {
                    if (p.DeathTime > 360)
                        Seq.Spawn(this, Info.Dude(K.kDudePlayer1, Info.DI_seqStartID) + 14, p, PlayerSurvive);
                }
                else if (Seq.Status(p) < 0)
                {
                    // leave a corpse and restart (single player restarts the level)
                    PlayerLeaveCorpse(p);
                    PlayerReset(p);
                    if (GameType == K.kGameTypeSinglePlayer)
                        RestartRequested = true;
                    else
                        PlayerStart(p, false);
                }
                p.InputAction = false;
            }
            return;
        }
        if (p.Posture == Player.PostureSwim)
        {
            double x = Blood.Cos(p.Ang), y = Blood.Sin(p.Ang);
            if (p.InputForward != 0)
            {
                double forward = p.InputForward > 0 ? Blood.MulScale8(p.PostureInfo(Player.PO_frontAccel), p.InputForward)
                    : Blood.MulScale8(p.PostureInfo(Player.PO_backAccel), p.InputForward);
                p.XVel += Blood.MulScale30(forward, x);
                p.YVel += Blood.MulScale30(forward, y);
            }
            if (p.InputStrafe != 0)
            {
                double strafe = Blood.MulScale8(p.PostureInfo(Player.PO_sideAccel), p.InputStrafe);
                p.XVel += Blood.MulScale30(strafe, y);
                p.YVel -= Blood.MulScale30(strafe, x);
            }
        }
        else if (p.XHeight < 256)
        {
            double speed = 65536;
            if (p.XHeight > 0)
                speed -= Blood.DivScale16(p.XHeight, 256);
            double x = Blood.Cos(p.Ang), y = Blood.Sin(p.Ang);
            if (p.InputForward != 0)
            {
                double forward = p.InputForward > 0 ? Blood.MulScale8(p.PostureInfo(Player.PO_frontAccel), p.InputForward)
                    : Blood.MulScale8(p.PostureInfo(Player.PO_backAccel), p.InputForward);
                if (p.XHeight != 0)
                    forward = Blood.MulScale16(forward, speed);
                p.XVel += Blood.MulScale30(forward, x);
                p.YVel += Blood.MulScale30(forward, y);
            }
            if (p.InputStrafe != 0)
            {
                double strafe = Blood.MulScale8(p.PostureInfo(Player.PO_sideAccel), p.InputStrafe);
                if (p.XHeight != 0)
                    strafe = Blood.MulScale16(strafe, speed);
                p.XVel += Blood.MulScale30(strafe, y);
                p.YVel -= Blood.MulScale30(strafe, x);
            }
        }
        // turning comes straight from the client's view angles (mouse/keyboard turning is client-side)
        if (p.InputSpin180)
        {
            if (p.Spin == 0)
                p.Spin = -Blood.Ang180;
            p.InputSpin180 = false;
        }
        if (p.Spin < 0)
        {
            double speed = p.Posture == Player.PostureSwim ? 64 : 128;
            p.Spin = Math.Min(p.Spin + speed, 0);
            p.Ang = (int)Blood.AngMask(p.Ang + speed);
            p.FixAngle = true;
        }
        p.AngOld = p.Ang;
        if (!p.InputJump)
            p.CantJump = false;
        if (p.Posture == Player.PostureSwim)
        {
            if (p.InputJump)
                p.ZVel -= p.PostureInfo(Player.PO_normalJumpZ);
            if (p.InputCrouch)
                p.ZVel += p.PostureInfo(Player.PO_normalJumpZ);
        }
        else if (p.Posture == Player.PostureCrouch)
        {
            if (!p.InputCrouch)
                p.Posture = Player.PostureStand;
        }
        else
        {
            if (!p.CantJump && p.InputJump && p.XHeight == 0)
            {
                bool boots = PackItemActive(p, Player.PackJumpBoots);
                if ((boots && p.PostureInfo(Player.PO_pwupJumpZ) != 0) || p.PostureInfo(Player.PO_normalJumpZ) != 0)
                    Sound3D(p, 700);
                p.ZVel = boots ? p.PostureInfo(Player.PO_pwupJumpZ) : p.PostureInfo(Player.PO_normalJumpZ);
                p.CantJump = true;
            }
            if (p.InputCrouch)
                p.Posture = Player.PostureCrouch;
        }
        if (p.InputAction)
        {
            int nHit = ActionScan(p);
            var t = _actionTarget;
            if (nHit == 6 && t is Sector ts)
                UseKeyed(p, t, () => TrTriggerSectorBoth(ts, K.kCmdSpritePush, p));
            else if (nHit == 0 && t is Wall tw)
                UseKeyed(p, t, () => TrTriggerWall(tw, K.kCmdWallPush, p));
            else if (nHit == 3 && t is Sprite tsp)
            {
                int key = t.XKey;
                if (t.XLocked != 0 && t.LockMsg != 0)
                    TextOver(t.LockMsg);
                if (key == 0 || (key < 8 && p.HasKey[key] != 0))
                    TrTriggerSprite(tsp, K.kCmdSpritePush, p);
                else
                {
                    ViewSetMessage(p, "That requires a key.");
                    SoundLocal(p, 3063);
                }
            }
            if (p.HandTime > 0)
                p.HandTime = Blood.ClipLow(p.HandTime - GameClock.TicsPerFrame * (6 - Skill), 0);
            if (p.HandTime <= 0 && p.Hand != 0)
                PlayerDropHand(p);
            p.InputAction = false;
        }
        // mouse look: Blood's look angle range is +289/-347 Build units; horiz = 100*tan(look)
        p.Horiz = 100 * Math.Tan(p.Look * Math.PI / 1024);
        bool va = p.XHeight < 16 && (p.FlorHit == Hit.Sector || p.FlorHit == 0);
        // playerProcess: on a sloped floor, the view tilts by its rise over 64 units ahead (in the same sector)
        int ns = SpriteBSector(p);
        if (va && ns >= 0 && (Board.Sectors[ns].FloorStat & 2) != 0)
        {
            double z1 = Board.FloorZAt(ns, p.BX, p.BY);
            double x2 = p.BX + Blood.MulScale30(64, Blood.Cos(p.Ang)), y2 = p.BY + Blood.MulScale30(64, Blood.Sin(p.Ang));
            if (Board.UpdateSector(x2, y2, ns) == ns)
                p.SlopeHoriz += Blood.MulScale16((z1 - Board.FloorZAt(ns, x2, y2)) / 8 - p.SlopeHoriz, 16384);
        }
        else
        {
            p.SlopeHoriz += Blood.MulScale16(0 - p.SlopeHoriz, 16384);
            if (Math.Abs(p.SlopeHoriz) < 4.0 / 65536)
                p.SlopeHoriz = 0;
        }
        p.Slope = -p.Horiz * 128;
        WeaponProcess(p);
        if (p.InputPrevItem) { p.InputPrevItem = false; PackPrevItem(p); }
        if (p.InputNextItem) { p.InputNextItem = false; PackNextItem(p); }
        if (p.InputUseItem)
        {
            p.InputUseItem = false;
            if (p.PackItemId >= 0 && p.PackAmount[p.PackItemId] > 0)
                PackUseItem(p, p.PackItemId);
        }
        if (p.UseBeastVision) { p.UseBeastVision = false; if (p.PackAmount[Player.PackBeastVision] > 0) PackUseItem(p, Player.PackBeastVision); }
        if (p.UseCrystalBall) { p.UseCrystalBall = false; if (p.PackAmount[Player.PackCrystalBall] > 0) PackUseItem(p, Player.PackCrystalBall); }
        if (p.UseJumpBoots) { p.UseJumpBoots = false; if (p.PackAmount[Player.PackJumpBoots] > 0) PackUseItem(p, Player.PackJumpBoots); }
        if (p.UseMedKit) { p.UseMedKit = false; if (p.PackAmount[Player.PackMedKit] > 0) PackUseItem(p, Player.PackMedKit); }
        if (p.InputHolster)
        {
            p.InputHolster = false;
            if (p.CurWeapon != 0)
            {
                WeaponLower(p);
                ViewSetMessage(p, "Holstering weapon");
            }
        }
        CheckPickUp(p);
    }

    /// <summary>The level asked to restart (single player death); the game reloads the map.</summary>
    public bool RestartRequested;

    /// <summary>playerProcess: the player's part of the frame, before the busy sectors, events and actors.</summary>
    public void PlayerProcess(Player p)
    {
        PowerupProcess(p);
        // pushmove out of the walls it stands in (a sector turning into it; one just come down a link beside a block
        // that fills the room below: E3M5's turning block under link 30, whose top had been taken for the floor)
        if (UseSectorClip && !p.NoClip && SpriteBSector(p) >= 0)
        {
            var (top, bottom) = Extents(p);
            double x = p.BX, y = p.BY, z = p.BZ;
            int sect = SpriteBSector(p);
            if (PushMoveB(ref x, ref y, z, ref sect, p.ClipDist * 4, (z - top) / 4, (bottom - z) / 4) == -1)
                ActDamageSprite(p, p, K.kDamageFall, 500 << 4);
            if (x != p.BX || y != p.BY)
                p.Origin = QOf(x, y, z);
            if (sect != SpriteBSector(p))
            {
                if (sect == -1)
                    ActDamageSprite(p, p, K.kDamageFall, 500 << 4);
                else
                    SetSpriteBSector(p, sect);
            }
        }
        Log?.Append($" [process start vel {p.XVel} {p.YVel} fwd {p.InputForward} shoot {p.InputShoot} weapon {p.CurWeapon} qav {p.WeaponQav}]");
        ProcessInput(p);
        Log?.Append($" [after input vel {p.XVel} {p.YVel}]");
        if (p.Removed)
            return;
        double nSpeed = Blood.ApproxDist(p.XVel, p.YVel);
        p.ZViewVel = Blood.Interpolate(p.ZViewVel, p.ZVel, 28672);
        double dz = p.BZ - p.PostureInfo(Player.PO_eyeAboveZ) - p.ZView;
        p.ZViewVel += dz > 0 ? Blood.MulScale16(dz * 256, 40960) : Blood.MulScale16(dz * 256, 6144);
        p.ZView += Blood.Sar(p.ZViewVel, 8);
        p.ZWeaponVel = Blood.Interpolate(p.ZWeaponVel, p.ZVel, 20480);
        dz = p.BZ - p.PostureInfo(Player.PO_weaponAboveZ) - p.ZWeapon;
        p.ZWeaponVel += dz > 0 ? Blood.MulScale16(dz * 256, 32768) : Blood.MulScale16(dz * 256, 3072);
        p.ZWeapon += Blood.Sar(p.ZWeaponVel, 8);
        p.BobPhase = Blood.ClipLow(p.BobPhase - GameClock.TicsPerFrame, 0);
        nSpeed = Blood.Sar(nSpeed, 16);
        if (p.Posture == Player.PostureSwim)
        {
            p.BobAmp = Blood.AngMask(p.BobAmp + 17);
            p.SwayAmp = Blood.AngMask(p.SwayAmp + 17);
            p.BobHeight = Blood.MulScale30(p.PostureInfo(Player.PO_bobV) * 10, Blood.Sin(p.BobAmp * 2));
        }
        else
        {
            if (p.XHeight < 256)
            {
                double pace = p.IsRunning ? p.PostureInfo(Player.PO_pace1) : p.PostureInfo(Player.PO_pace0);
                p.BobAmp = Blood.AngMask(p.BobAmp + pace * 4);
                p.SwayAmp = Blood.AngMask(p.SwayAmp + Blood.Int(pace * 4 / 2));
                double cap = p.IsRunning ? 60 : 30;
                if (p.BobPhase < cap)
                    p.BobPhase = Blood.ClipHigh(p.BobPhase + nSpeed, cap);
            }
            p.BobHeight = Blood.MulScale30(p.PostureInfo(Player.PO_bobV) * p.BobPhase, Blood.Sin(p.BobAmp * 2));
        }
        p.BobWidth = Blood.MulScale30(p.PostureInfo(Player.PO_bobH) * p.BobPhase, Blood.Sin(p.BobAmp - 256));
        p.SwayHeight = Blood.MulScale30(p.PostureInfo(Player.PO_swayV) * p.BobPhase, Blood.Sin(p.SwayAmp * 2));
        p.SwayWidth = Blood.MulScale30(p.PostureInfo(Player.PO_swayH) * p.BobPhase, Blood.Sin(p.SwayAmp - 341));
        p.FlickerEffect = 0;
        p.QuakeEffect = Blood.ClipLow(p.QuakeEffect - GameClock.TicsPerFrame, 0);
        p.TiltEffect = Blood.ClipLow(p.TiltEffect - GameClock.TicsPerFrame, 0);
        p.Visibility = Blood.ClipLow(p.Visibility - GameClock.TicsPerFrame, 0);
        p.PainEffect = Blood.ClipLow(p.PainEffect - GameClock.TicsPerFrame, 0);
        p.BlindEffect = Blood.ClipLow(p.BlindEffect - GameClock.TicsPerFrame, 0);
        p.PickupEffect = Blood.ClipLow(p.PickupEffect - GameClock.TicsPerFrame, 0);
        if (p.XHealth == 0)
        {
            p.Hand = 0;
            return;
        }
        p.IsUnderwater = false;
        if (p.Posture == Player.PostureSwim)
        {
            p.IsUnderwater = true;
            // head above the water surface: Blood's test, the eye over the ceiling of a sector whose lower link is a
            // water or goo one (a stack link's ceiling has water beyond: E4M4's 117 under 118); a level without Build
            // sectors, the eye out of the water
            int bs = Map != null ? SpriteBSector(p) : -1;
            if (bs >= 0)
            {
                foreach (var l in Collision.Links)
                    if (l.Sector == bs && (l.Type == K.kMarkerLowWater || l.Type == K.kMarkerLowGoo)
                        && Q2BZ(new Vector3(0, 0, l.Plane)) > p.ZView)
                        p.IsUnderwater = false;
            }
            else if (!PointIsWater(B2Q(p.BX, p.BY, p.ZView)))
                p.IsUnderwater = false;
        }
        if (!p.IsUnderwater)
        {
            p.UnderwaterTime = 1200;
            p.ChokeEffect = 0;
            if (PackItemActive(p, Player.PackDivingSuit))
                PackUseItem(p, Player.PackDivingSuit);
        }
        int seqStart = Info.Dude(K.kDudePlayer1, Info.DI_seqStartID);
        if (p.Posture == Player.PostureSwim)
            Seq.Spawn(this, seqStart + 9, p);
        else if (p.Posture == Player.PostureCrouch)
            Seq.Spawn(this, seqStart + 10, p);
        else
            Seq.Spawn(this, nSpeed == 0 ? seqStart : seqStart + 8, p);
    }

    /// <summary>playerProcessStatus: the player's part of the dude loop in actProcessSprites.</summary>
    void PlayerProcessStatus(Player p)
    {
        if (p.VoodooTargets != 0)
            VoodooTargetProc(p);
        if (p.Hand != 0 & Chance(32768))
            ActDamageSprite(p, p, K.kDamageDrown, 12);
        if (p.Removed)
            return;
        if (p.IsUnderwater)
        {
            if (PackItemActive(p, Player.PackDivingSuit) || p.GodMode)
                p.UnderwaterTime = K.kTicRate * 10;
            else
                p.UnderwaterTime = Blood.ClipLow(p.UnderwaterTime - GameClock.TicsPerFrame, 0);
            if (p.UnderwaterTime < 1080 && p.PackAmount[Player.PackDivingSuit] != 0 && !PackItemActive(p, Player.PackDivingSuit))
                PackUseItem(p, Player.PackDivingSuit);
            if (p.UnderwaterTime == 0)
            {
                p.ChokeEffect += GameClock.TicsPerFrame;
                if (Chance((int)p.ChokeEffect))
                    ActDamageSprite(p, p, K.kDamageDrown, 48);
            }
            else
                p.ChokeEffect = 0;
            if (p.XVel != 0 || p.YVel != 0)
                Sound3D(p, 709, 100, 2);
            p.BubbleTime = Blood.ClipLow(p.BubbleTime - GameClock.TicsPerFrame, 0);
        }
        else if (GameType == K.kGameTypeSinglePlayer)
        {
            if ((p.XHealth > 0 && p.RestTime >= 1200) & Chance(512))
            {
                p.RestTime = -1;
                Sound3D(p, 3100 + Random(11), 0, 2);
            }
        }
    }

    public Sprite PlayerFireMissile(Player p, double a2, double a3, double a4, double a5, int a6) =>
        ActFireMissile(p, a2, p.ZWeapon - p.BZ, a3, a4, a5, a6);

    public Sprite PlayerFireThing(Player p, double a2, double a3, int thingType, double a5) =>
        ActFireThing(p, a2, p.ZWeapon - p.BZ, p.Slope + a3, thingType, a5);

    double PlayerDamageArmor(Player p, int nType, double nDamage)
    {
        int nArmorType = GenTables.tbl_damageinfo[nType * 7];
        if (nArmorType >= 0 && p.Armor[nArmorType] != 0)
        {
            double v8 = p.Armor[nArmorType];
            double t = Blood.Scale5(v8, 0, 3200, Blood.Int(nDamage / 4), Blood.Int(nDamage * 7 / 8));
            v8 -= t;
            nDamage -= t;
            p.Armor[nArmorType] = Blood.ClipLow(v8, 0);
        }
        return nDamage;
    }

    void FragPlayer(Player p, Sprite src)
    {
        if (src is Player sp)
        {
            if (sp == p)
                ShowMessage($"{p.NetName} has suicided");
            else
            {
                sp.FragCount++;
                ShowMessage($"{sp.NetName} killed {p.NetName}");
            }
        }
    }

    /// <summary>playerDamageSprite: armor, pain, death. Returns the damage dealt.</summary>
    public double PlayerDamageSprite(Sprite src, Player p, int nDamageType, double nDamage)
    {
        if (p.DamageControl[nDamageType] != 0)
            return 0;
        nDamage = PlayerDamageArmor(p, nDamageType, nDamage);
        p.PainEffect = Blood.ClipHigh(p.PainEffect + Blood.Sar(nDamage, 3), 600);
        int nDeathSeqID = -1;
        Action<Sprite> kneelCb = null;
        bool va = PlayerSeqPlaying(p, 16);
        if (p.XHealth == 0)
        {
            if (va)
            {
                if (nDamageType == K.kDamageSpirit)
                {
                    nDeathSeqID = 18;
                    Sound3D(p, 716);
                }
                else if (nDamageType == K.kDamageExplode)
                {
                    GibSprite(p, K.GIBTYPE_7);
                    GibSprite(p, K.GIBTYPE_15);
                    p.Cstat |= Cstat.Invisible;
                    p.Visible = false;
                    nDeathSeqID = 17;
                }
                else
                {
                    var (top, _) = Physics.SpriteExtents(p.BZ, p.Picnum, p.YRepeat, p.Cstat);
                    GibSpriteAt(p, K.GIBTYPE_27, p.BX, p.BY, top, Blood.Sar(p.XVel, 1), Blood.Sar(p.YVel, 1), -838860);
                    GibSprite(p, K.GIBTYPE_7);
                    FxSpawnBlood(p, nDamage * 16);
                    FxSpawnBlood(p, nDamage * 16);
                    nDeathSeqID = 17;
                }
            }
        }
        else
        {
            double nHealth = p.XHealth - nDamage;
            p.XHealth = Blood.ClipLow(nHealth, 0);
            if (p.XHealth > 0 && p.XHealth < 16)
            {
                nDamageType = K.kDamageBullet;
                p.XHealth = 0;
                nHealth = -25;
            }
            if (p.XHealth > 0)
            {
                int nSound = nDamage >= 160 ? GenTables.tbl_damageinfo[nDamageType * 7 + 1]
                    : GenTables.tbl_damageinfo[nDamageType * 7 + 1 + Random(3)];
                if (nDamageType == K.kDamageDrown && p.Medium == Player.MediumWater && p.Hand == 0)
                    nSound = 714;
                Sound3D(p, nSound, 0, 6);
                return nDamage;
            }
            Kill3DSound(p, -1);
            p.DeathTime = 0;
            p.QavLoop = false;
            p.CurWeapon = K.kWeaponNone;
            p.FraggerId = src;
            p.VoodooTargets = 0;
            if (nDamageType == K.kDamageExplode && nDamage < 144)
                nDamageType = K.kDamageFall;
            if (nDamageType == K.kDamageExplode)
            {
                Sound3D(p, 717);
                GibSprite(p, K.GIBTYPE_7);
                GibSprite(p, K.GIBTYPE_15);
                p.Cstat |= Cstat.Invisible;
                p.Visible = false;
                nDeathSeqID = 2;
            }
            else if (nDamageType == K.kDamageBurn)
            {
                Sound3D(p, 718);
                nDeathSeqID = 3;
            }
            else if (nDamageType == K.kDamageDrown)
                nDeathSeqID = 1;
            else if ((nHealth < -20 && GameType >= K.kGameTypeBloodBath) & Chance(16384))
            {
                Sound3D(p, GenTables.tbl_damageinfo[nDamageType * 7 + 4], 0, 2);
                nDeathSeqID = 16;
                kneelCb = PlayerKneelsOver;
                PowerupActivate(p, K.kPwUpDeliriumShroom);
                p.XTarget = src;
                EvPostCallback(p, 15, K.kCallbackFinishHim);
            }
            else
            {
                Sound3D(p, 716);
                nDeathSeqID = 1;
            }
        }
        if (nDeathSeqID < 0)
            return nDamage;
        if (nDeathSeqID != 16)
        {
            PowerupClear(p);
            var sec = SpriteSector(p);
            if (sec != null && sec.HasX && sec.SExit != 0)
                TrTriggerSectorParts(sec, K.kCmdSectorExit, src, false);
            p.HitTag |= 7;
            PlayerClearFragger(p);
            FragPlayer(p, src);
            TrTriggerSprite(p, K.kCmdOff, src);
        }
        Seq.Spawn(this, Info.Dude(K.kDudePlayer1, Info.DI_seqStartID) + nDeathSeqID, p, kneelCb);
        return nDamage;
    }

    public double UseAmmo(Player p, int nAmmoType, double nDec)
    {
        if (InfiniteAmmo || nAmmoType == -1)
            return 9999;
        p.Ammo[nAmmoType] = Blood.ClipLow(p.Ammo[nAmmoType] - nDec, 0);
        return p.Ammo[nAmmoType];
    }

    void VoodooTargetProc(Player p)
    {
        double v4 = p.AimDz;
        double dz = p.ZWeapon - p.BZ;
        if (UseAmmo(p, 9, 0) < 8)
        {
            p.VoodooTargets = 0;
            return;
        }
        for (int i = 0; i < 4; i++)
        {
            double ang1 = Blood.AngMask(p.VoodooVar1 + p.VoodooVar2);
            ActFireVector(p, 0, dz, Blood.Sar(Blood.Cos(ang1), 16), Blood.Sar(Blood.Sin(ang1), 16), v4, K.kVectorVoodoo10);
            double ang2 = Blood.AngMask(p.VoodooVar1 + 2048 - p.VoodooVar2);
            ActFireVector(p, 0, dz, Blood.Sar(Blood.Cos(ang2), 16), Blood.Sar(Blood.Sin(ang2), 16), v4, K.kVectorVoodoo10);
        }
        p.VoodooTargets = Math.Max(p.VoodooTargets - 1, 0);
    }

    void PlayerLandingSound(Player p)
    {
        if (p.FlorHit == 0)
            return;
        if (p.FlorHit == Hit.Sprite && IsTargetTeammate(p, p.FlorHitEnt as Sprite))
            return;
        int nSurf = p.FlorSurf;
        if (nSurf == 0)
            return;
        // surfaceSound[] = { -1, 600, 601, 602, 603, 604, 605, 605, 605, 600, 605, 605, 605, 604, 603 }
        int snd = nSurf switch { 1 or 9 => 600, 2 => 601, 3 => 602, 4 or 14 => 603, 5 or 13 => 604, _ => 605 };
        Sound3D(p, snd, -1);
    }

    void PlayerSurvive(Sprite e)
    {
        ActHealDude(e, 1, 2);
        if (GameType != K.kGameTypeSinglePlayer)
        {
            Sound3D(e, 3009, 0, 6);
            if (e is Player p)
            {
                ViewSetMessage(p, "I LIVE...AGAIN!!");
                p.InputNewWeapon = K.kWeaponPitchfork;
            }
        }
    }

    void PlayerKneelsOver(Sprite e)
    {
        if (e is Player p)
            PlayerDamageSprite(p.FraggerId, p, K.kDamageSpirit, 8000);
    }

    public void PlayerHandChoke(Player p)
    {
        int t = Skill + 2;
        if (p.HandTime < 64)
            p.HandTime = Blood.ClipHigh(p.HandTime + t, 64);
        if (p.HandTime > (7 - Skill) * 5)
            p.BlindEffect = Blood.ClipHigh(p.BlindEffect + t * 4, 128);
    }

    /// <summary>playerLeaveCorpse: a dead player leaves its body behind.</summary>
    void PlayerLeaveCorpse(Player p)
    {
        var c = new Sprite
        {
            ClassName = "corpse", Type = K.kThingBloodChunks, InitType = K.kThingBloodChunks, StatNum = K.kStatThing,
            Origin = p.Origin, Picnum = p.Picnum, Pal = p.Pal, Shade = p.Shade, XRepeat = p.XRepeat, YRepeat = p.YRepeat,
            Ang = p.Ang, Cstat = p.Cstat & ~(Cstat.Block | Cstat.Hitscan), HitTag = p.HitTag, ClipDist = p.ClipDist,
        };
        AddSprite(c);
        Seq.Spawn(this, Info.Dude(K.kDudePlayer1, Info.DI_seqStartID) + 15, c);
        UpdateBox(c);
    }
}
