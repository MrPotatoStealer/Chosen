using System;
using Chosen.Data;

namespace Chosen.Sim;

/// <summary>
/// Cheats (messages.cpp CCheatMgr, via Blood3D's server/cheats.qc), as console commands: give all|weapons|ammo|armor|
/// keys|items, god, noclip, kill, testkit (god mode and every weapon, no keys), nomonsters, trigger &lt;channel&gt;, and
/// Blood's cheat names (mpkfa, idaho, griswold, montana, cousteau, kevorkian...), and warp x y z [yaw] [pitch]. A bug
/// recording replays them.
/// </summary>
public sealed partial class World
{
    void CheatSetAmmo(Player p, bool on)
    {
        for (int i = 0; i < 12; i++)
            p.Ammo[i] = on ? AmmoMax(i) : 0;
        ViewSetMessage(p, on ? "You have full ammo." : "You have no ammo.");
    }

    void CheatSetWeapons(Player p, bool on)
    {
        for (int i = 0; i < K.kWeaponMax; i++)
            p.HasWeapon[i] = on ? 1 : 0;
        CheatSetAmmo(p, on);
        if (on)
            ViewSetMessage(p, "You have all weapons.");
        else
        {
            p.HasWeapon[K.kWeaponPitchfork] = 1;
            p.CurWeapon = K.kWeaponNone;
            p.NextWeapon = K.kWeaponPitchfork;
            ViewSetMessage(p, "You have no weapons.");
        }
    }

    void CheatSetArmor(Player p, bool on)
    {
        for (int i = 0; i < 3; i++)
            p.Armor[i] = on ? 3200 : 0;
        ViewSetMessage(p, on ? "You have full armor." : "You have no armor.");
    }

    void CheatSetKeys(Player p, bool on)
    {
        for (int i = 1; i <= 6; i++)
            p.HasKey[i] = on ? 1 : 0;
        ViewSetMessage(p, on ? "You have all keys." : "You have no keys.");
    }

    void CheatSetToys(Player p)
    {
        for (int i = 0; i < Player.PackMax; i++)
            p.PackAmount[i] = 100;
        p.PackItemId = Player.PackMedKit;
        ViewSetMessage(p, "Your inventory is full.");
    }

    void CheatGod(Player p)
    {
        PlayerSetGodMode(p, !p.GodMode);
        ViewSetMessage(p, p.GodMode ? "You are immortal." : "You are mortal.");
    }

    void CheatNoclip(Player p)
    {
        p.NoClip = !p.NoClip;
        ViewSetMessage(p, p.NoClip ? "Unlocked noclip." : "Disabled noclip.");
    }

    static double Num(string s) => double.Parse(s, System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>warp x y z [yaw] [pitch] (Quake units, degrees; z: the feet), for testing and gotobug (cheats.qc). The
    /// pitch is the client's view (the console command sets it).</summary>
    void CheatWarp(Player p, string[] a)
    {
        if (a.Length < 3)
            return;
        p.Origin = new Godot.Vector3((float)Num(a[0]), (float)Num(a[1]), (float)Num(a[2]));
        p.LinkPrevSet = false;
        var (_, bottom) = Physics.SpriteExtents(p.BZ, p.Picnum, p.YRepeat, p.Cstat);
        p.SetBZ(p.BZ - (bottom - p.BZ));
        p.XVel = p.YVel = p.ZVel = 0;
        p.HitTag |= K.kPhysFalling;
        PlayerResetInertia(p);
        if (a.Length > 3)
        {
            p.Ang = (int)Blood.YawToAng(Num(a[3]));
            p.FixAngle = true;
        }
        // its sector looked up afresh where it lands; a Build sector given (after the pitch): that one, and the layer
        // of rooms sharing space it is in (as NBlood's -b3dwarp)
        SpriteSectorForget(p);
        if (a.Length > 5 && int.TryParse(a[5], out int sec) && sec >= 0)
            SetSpriteBSector(p, sec);
        // in water or out as where it lands (no link was crossed to tell): its sector underwater (NBlood's -b3dwarp, the
        // sector given), else playerStart's test
        int ws = SpriteBSector(p);
        bool water = ws >= 0 && XSectorOf(ws) is { } wx && wx.SUnderwater != 0 || PointIsWater(p.Origin);
        if (water != (p.Medium == Player.MediumWater))
        {
            p.Medium = water ? Player.MediumWater : Player.MediumNormal;
            p.Posture = water ? Player.PostureSwim : Player.PostureStand;
        }
        UpdateBox(p);
    }

    /// <summary>cheatCommand: returns true if c is a cheat (a is its argument).</summary>
    public bool CheatCommand(Player p, string c, string a = "")
    {
        if (p == null)
            return false;
        switch (c)
        {
            case "setweaponsv10x":
                // the v1.0x weapons option changed during a level (recorded like setautoaim)
                WeaponsV10x = a != "0";
                return true;
            case "setfixblood":
                // the Fix Blood's bugs option changed during a level (recorded like setautoaim)
                FixBlood = a != "0";
                return true;
            case "setautoaim":
                // the auto aim option changed during a level (Game records it so replays change it on the same frame)
                if (int.TryParse(a, out int aim))
                    AutoAim = Math.Clamp(aim, 0, 3);
                return true;
            case "give":
                if (a == "all" || a == "") { CheatSetWeapons(p, true); CheatSetArmor(p, true); CheatSetKeys(p, true); CheatSetToys(p); }
                else if (a == "weapons") CheatSetWeapons(p, true);
                else if (a == "ammo") CheatSetAmmo(p, true);
                else if (a == "armor") CheatSetArmor(p, true);
                else if (a == "keys") CheatSetKeys(p, true);
                else if (a == "items") CheatSetToys(p);
                return true;
            case "god":
            case "mpkfa":
                CheatGod(p);
                if (c == "mpkfa")
                {
                    CheatSetWeapons(p, true);
                    CheatSetArmor(p, true);
                    CheatSetKeys(p, true);
                }
                return true;
            case "testkit":
                // testing a level as it plays: immortal with every weapon, but no keys (doors stay locked as in Blood)
                if (!p.GodMode)
                    CheatGod(p);
                CheatSetWeapons(p, true);
                return true;
            case "noclip":
                CheatNoclip(p);
                return true;
            case "cousteau":
                // 200% health and a diving suit (kCheatCousteau)
                ActHealDude(p, 200, 200);
                p.PackAmount[Player.PackDivingSuit] = 100;
                p.PwUpTime[K.kPwUpDivingSuit] = PowerupInfo(K.kPwUpDivingSuit, 2);
                return true;
            case "idaho":
            case "lara":
                CheatSetWeapons(p, true);
                return true;
            case "griswold":
                CheatSetArmor(p, true);
                return true;
            case "montana":
                CheatSetWeapons(p, true);
                CheatSetArmor(p, true);
                CheatSetKeys(p, true);
                CheatSetToys(p);
                return true;
            case "kevorkian":
            case "kill":
                ActDamageSprite(p, p, K.kDamageBullet, 8000);
                return true;
            case "nomonsters":
                foreach (var d in Sprites)
                    if (d.StatNum == K.kStatDude && d is not Player)
                        ActPostSprite(d, K.kStatFree);
                return true;
            case "warp":
                CheatWarp(p, a.Split(' ', System.StringSplitOptions.RemoveEmptyEntries));
                return true;
            case "trigger":
                // trigger <channel>: send a toggle on a channel, as a switch would (testing doors and lifts)
                if (int.TryParse(a, out int ch))
                    EvSend(p, ch, K.kCmdToggle, p);
                return true;
        }
        return false;
    }
}
