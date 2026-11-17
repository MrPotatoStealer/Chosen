using System;
using Chosen.Data;
using Godot;

namespace Chosen.Sim;

/// <summary>
/// The player control sprite of a modern map (NBlood's nnexts.cpp trPlayerCtrl...; type 500): with the link command it
/// gives a player's sprite its own trigger settings and TX ID (so the map's triggers and conditions can address the
/// player), with the numbered commands 64..74 it changes the player: life form, movement speed and jump height,
/// screen effects, the look and view angle, taking and giving weapons, ammo, armour, items, keys and powerups.
/// </summary>
public sealed partial class World
{
    void PlayerCtrlReset() { }

    /// <summary>trPlayerCtrlLink: the player's sprite takes the linker's trigger settings.</summary>
    void TrPlayerCtrlLink(Sprite src, Player p, bool checkCondition)
    {
        src.SysData1 = p.SprIndex;
        src.Linked = p;
        p.TxID = src.TxID;
        p.XCommand = K.kCmdToggle;
        p.TriggerOn = src.TriggerOn;
        p.TriggerOff = src.TriggerOff;
        p.BusyTime = src.BusyTime;
        p.WaitTime = src.WaitTime;
        p.RestState = src.RestState;
        p.XPush = src.XPush;
        p.XImpact = src.XImpact;
        p.XVector = src.XVector;
        p.XTouch = src.XTouch;
        p.XSight = src.XSight;
        p.XProximity = src.XProximity;
        p.Decoupled = src.Decoupled;
        p.Interruptable = src.Interruptable;
        p.DudeLockout = src.DudeLockout;
        p.Data1 = src.Data1;
        p.Data2 = src.Data2;
        p.Data3 = src.Data3;
        p.Data4 = src.Data4;
        p.XKey = src.XKey;
        p.DropMsg = src.DropMsg;
        // the tracking conditions that watch the linker's channel watch the player
        if (checkCondition && src.TxID >= K.kChannelUser)
            CondLinkPlayer(src, p);
    }

    /// <summary>playerStart's modern part: the player linkers for this player (data1 0: any) link it.</summary>
    void ModernPlayerStart(Player p)
    {
        if (!IsModern)
            return;
        foreach (var ctrl in new System.Collections.Generic.List<Sprite>(StatList(KM.kStatModernPlayerLinker)))
        {
            if (ctrl.Data1 != 0 && ctrl.Data1 != p.PlayerIndex + 1)
                continue;
            TrPlayerCtrlLink(ctrl, p, ctrl.Linked == null);
        }
    }

    void TrPlayerCtrlSetRace(Sprite src, Player p)
    {
        PlayerSetRace(p, src.Data2);
        switch (p.LifeMode)
        {
            case Player.ModeHuman:
            case Player.ModeBeast:
                p.XScale = 0;
                break;
            case Player.ModeHumanShrink:
                p.XScale = 256 / 2;
                break;
            case Player.ModeHumanGrown:
                p.XScale = 256 * 2;
                break;
        }
    }

    /// <summary>playerSetRace: the player's life form (its dude template's damage scaling with it).</summary>
    void PlayerSetRace(Player p, int nLifeMode)
    {
        // (the mode's damage scales are read from its template as damage is dealt, World.DmgControl)
        p.LifeMode = nLifeMode;
        p.ClipDist = Info.PlayerTemplate(nLifeMode, Info.DI_clipdist);
    }

    static void TrPlayerCtrlSetScreenEffect(Sprite src, Player p)
    {
        int eff = (int)Blood.ClipLow(src.Data2, 0);
        int time = eff > 0 ? src.Data3 : 0;
        if (eff == 0 || eff == 1) p.TiltEffect = Blood.ClipRange(time, 0, 220);
        if (eff == 0 || eff == 2) p.PainEffect = Blood.ClipRange(time, 0, 2048);
        if (eff == 0 || eff == 3) p.BlindEffect = Blood.ClipRange(time, 0, 2048);
        if (eff == 0 || eff == 4) p.PickupEffect = Blood.ClipRange(time, 0, 2048);
        if (eff == 0 || eff == 5) p.QuakeEffect = Blood.ClipRange(time, 0, 2048);
        if (eff == 0 || eff == 6) p.Visibility = Blood.ClipRange(time, 0, 2048);
        if (eff == 0 || eff == 7) p.PwUpTime[K.kPwUpDeliriumShroom] = Blood.ClipHigh(time << 1, 432000);
    }

    static void TrPlayerCtrlSetLookAngle(Sprite src, Player p)
    {
        const int upAngle = 289, downAngle = -347;
        const double lookStepUp = 4.0 * upAngle / 60.0, lookStepDown = -4.0 * downAngle / 60.0;
        int look = src.Data2 << 5;
        if (look > 0) p.Look = Math.Min(lookStepUp * look / 256.0, upAngle);
        else if (look < 0) p.Look = -Math.Max(lookStepDown * Math.Abs(look) / 256.0, downAngle);
        else p.Look = 0;
    }

    void TrPlayerCtrlEraseStuff(Sprite src, Player p)
    {
        int what = src.Data2;
        if (what == 0 || what == 1)
        {
            // weapons, and their ammo
            WeaponLower(p);
            for (int i = 0; i < K.kWeaponMax; i++)
            {
                p.HasWeapon[i] = 0;
                if (i < 12)
                    p.Ammo[i] = 0;
            }
            p.HasWeapon[K.kWeaponPitchfork] = 1;
            p.CurWeapon = K.kWeaponNone;
            p.NextWeapon = K.kWeaponPitchfork;
            WeaponRaise(p);
        }
        if (what == 0 || what == 2)
            for (int i = 0; i < 3; i++)
                p.Armor[i] = 0;
        if (what == 0 || what == 3)
        {
            for (int i = 0; i < 5; i++)
            {
                p.PackActive[i] = 0;
                p.PackAmount[i] = 0;
            }
            p.PackItemId = -1;
        }
        if (what == 0 || what == 4)
            for (int i = 0; i < 8; i++)
                p.HasKey[i] = 0;
        if (what == 0 || what == 5)
            for (int i = 0; i < p.PwUpTime.Length; i++)
                p.PwUpTime[i] = 0;
    }

    void TrPlayerCtrlGiveStuff(Sprite src, Player p)
    {
        int weapon = src.Data3;
        if (src.Data2 != 1 && src.Data2 != 2)
            return;
        // 1: the weapon with its usual ammo, 2: just data4 ammo for it
        if (weapon <= K.kWeaponNone || weapon > K.kWeaponBeast)
            return;
        if (src.Data2 == 2 && src.Data4 == 0)
            return;
        if (weapon == 11 || weapon == 12)
        {
            // remote and proximity bombs: the weapon is its ammo
            p.HasWeapon[weapon] = 1;
            p.Ammo[weapon - 1] = Blood.ClipHigh(p.Ammo[weapon - 1] + (src.Data2 == 2 ? src.Data4 : 1), AmmoMax(weapon - 1));
        }
        else
        {
            for (int i = 0; i < 11; i++)
            {
                int type = K.kItemWeaponBase + i;
                if (Info.WeaponItem(type, 6) != weapon)
                    continue;
                int nAmmoType = Info.WeaponItem(type, 7), count = Info.WeaponItem(type, 8);
                if (src.Data2 == 1)
                {
                    p.HasWeapon[weapon] = 1;
                    if (nAmmoType >= 0 && p.Ammo[nAmmoType] < count)
                        p.Ammo[nAmmoType] = Blood.ClipHigh(p.Ammo[nAmmoType] + count, AmmoMax(nAmmoType));
                }
                else if (nAmmoType >= 0)
                    p.Ammo[nAmmoType] = Blood.ClipHigh(p.Ammo[nAmmoType] + src.Data4, AmmoMax(nAmmoType));
                break;
            }
        }
        if (p.HasWeapon[weapon] != 0 && src.Data4 == 0)
        {
            // and the player takes it in hand
            p.NextWeapon = K.kWeaponNone;
            if (p.CurWeapon != weapon)
            {
                p.InputNewWeapon = weapon;
                WeaponRaise(p);
            }
        }
    }

    void TrPlayerCtrlUsePackItem(Sprite src, Player p, int evCmd)
    {
        int invItem = src.Data2 - 1;
        switch (evCmd)
        {
            case K.kCmdOn:
                if (p.PackActive[invItem] == 0) PackUseItem(p, invItem);
                break;
            case K.kCmdOff:
                if (p.PackActive[invItem] != 0) PackUseItem(p, invItem);
                break;
            default:
                PackUseItem(p, invItem);
                break;
        }
        if (src.Data4 == 0 || src.Data4 == 2)
        {
            if (p.PackAmount[invItem] > 0)
                p.PackItemId = invItem;
        }
        if (src.Data4 == 1 || src.Data4 == 2)
        {
            p.PackActive[invItem] = 0;
            p.PackAmount[invItem] = 0;
        }
    }

    void TrPlayerCtrlUsePowerup(Sprite src, Player p)
    {
        bool relative = (src.HitTag & KM.Flag1) != 0;
        int nPower = K.kPwUpFeatherFall + src.Data2 - 1;
        double maxTime = PowerupInfo(nPower, 3);
        double nTime = Blood.ClipRange(Math.Abs(src.Data3) * 100, -maxTime, maxTime);
        if (src.Data3 < 0)
            nTime = -nTime;
        if (p.PwUpTime[nPower] != 0 && !relative && nTime <= 0)
            PowerupDeactivate(p, nPower);
        if (nTime != 0)
        {
            if (p.PwUpTime[nPower] <= 0)
                PowerupActivate(p, nPower);     // first, for powerups like the death mask
            if (relative) p.PwUpTime[nPower] += nTime;
            else p.PwUpTime[nPower] = nTime;
        }
        if (p.PwUpTime[nPower] <= 0)
            PowerupDeactivate(p, nPower);
    }

    /// <summary>kModernPlayerControl's operate: the command (the event's, when numbered; else the sprite's) done to its
    /// player (data1, or the event's causer when that is a player and the sprite names none).</summary>
    void OperatePlayerControl(Sprite e, int evCmd, XObject causer)
    {
        int cmd = evCmd >= K.kCmdNumberic ? evCmd : e.XCommand;
        int playerID = (e.TxID == K.kChannelEventCauser || e.Data1 == 0) && causer is Player cp ? cp.Type : e.Data1;
        var p = GetPlayerById(playerID);
        if (p == null || ((cmd < 67 || cmd > 68) && !ModernTypeSetSpriteState(e, e.XState ^ 1, causer)))
            return;
        if ((cmd -= K.kCmdNumberic) < 0)
            return;
        if (p.XHealth <= 0)
        {
            if (cmd == 36)
            {
                ActHealDude(p, e.Data2 > 0 ? (int)Blood.ClipHigh(e.Data2, 200) : Info.PlayerTemplate(Player.ModeHuman, Info.DI_startHealth), 200);
                p.CurWeapon = K.kWeaponPitchfork;
            }
            return;
        }
        switch (cmd)
        {
            case 0:     // 64: life form
                if (e.Data2 >= Player.ModeHuman && e.Data2 <= Player.ModeHumanGrown)
                    TrPlayerCtrlSetRace(e, p);
                break;
            case 1:     // 65: movement speed and jump height, in percents
                if (ValueIsBetween(e.Data2, -1, 32767))
                    p.MoveSpeedPerc = (int)Blood.ClipRange(e.Data2, 0, 500);
                if (ValueIsBetween(e.Data3, -1, 32767))
                    p.JumpHeightPerc = (int)Blood.ClipRange(e.Data3, 0, 500);
                break;
            case 2:     // 66: screen effects
                if (e.Data3 >= 0)
                    TrPlayerCtrlSetScreenEffect(e, p);
                break;
            case 3:     // 67: a QAV scene starts
            case 4:     // 68: and stops
                ModernMissing("player control QAV scenes (commands 67, 68)");
                break;
            case 5:     // 69: the look angle
                if (e.Data4 == 0 && ValueIsBetween(e.Data2, -128, 128))
                    TrPlayerCtrlSetLookAngle(e, p);
                break;
            case 6:     // 70: take things away
                if (e.Data2 >= 0)
                    TrPlayerCtrlEraseStuff(e, p);
                break;
            case 7:     // 71: give things
                if (e.Data2 > 0)
                    TrPlayerCtrlGiveStuff(e, p);
                break;
            case 8:     // 72: use an inventory item
                if (e.Data2 >= 1 && e.Data2 <= 5)
                    TrPlayerCtrlUsePackItem(e, p, evCmd);
                break;
            case 9:     // 73: the view angle
                if (e.Data4 != 0)
                    break;
                if ((e.HitTag & KM.Flag1) != 0) PlayerSetViewYaw(p, e.Ang & 2047);
                else if (ValueIsBetween(e.Data2, -2048, 2048)) PlayerSetViewYaw(p, e.Data2 & 2047);
                break;
            case 10:    // 74: a powerup on or off
                if (e.Data2 > 0 && e.Data2 <= 51 - (K.kPwUpFeatherFall << 1) + 1)
                    TrPlayerCtrlUsePowerup(e, p);
                break;
        }
    }

    /// <summary>The player turned to a Build angle by the game (its view takes it).</summary>
    void PlayerSetViewYaw(Player p, int ang)
    {
        p.Ang = ang;
        p.FixAngle = true;
    }
}
