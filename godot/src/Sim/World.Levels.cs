using System.Collections.Generic;
using Chosen.Data;

namespace Chosen.Sim;

/// <summary>Level flow (Blood3D's server/levels.qc): the level's messages, secrets, the end of the level.</summary>
public sealed partial class World
{
    readonly Dictionary<int, string> _levelMessages = new();
    public string NextMap = "", SecretMap = "";   // worldspawn's nextmap / secretmap

    /// <summary>What a player takes to the next level (levels.qc playerCarryString / playerApplyCarry).</summary>
    public sealed class PlayerCarry
    {
        public double Health;
        public int[] HasWeapon, WeaponMode, PackActive;
        public double[] Ammo, Armor, PackAmount;
        public int PackItemId, CurWeapon, LastWeapon, NextWeapon, WeaponState, WeaponAmmo, FragCount, TeamId;
        public double JumpBoots, DivingSuit, CrystalBall, BeastVision;
    }

    public static PlayerCarry CaptureCarry(Player p) => new()
    {
        Health = p.XHealth,
        HasWeapon = (int[])p.HasWeapon.Clone(), WeaponMode = (int[])p.WeaponMode.Clone(), PackActive = (int[])p.PackActive.Clone(),
        Ammo = (double[])p.Ammo.Clone(), Armor = (double[])p.Armor.Clone(), PackAmount = (double[])p.PackAmount.Clone(),
        PackItemId = p.PackItemId, CurWeapon = p.CurWeapon, LastWeapon = p.LastWeapon, NextWeapon = p.NextWeapon,
        WeaponState = p.WeaponState, WeaponAmmo = p.WeaponAmmo, FragCount = p.FragCount, TeamId = p.TeamId,
        JumpBoots = p.PwUpTime[K.kPwUpJumpBoots], DivingSuit = p.PwUpTime[K.kPwUpDivingSuit],
        CrystalBall = p.PwUpTime[K.kPwUpCrystalBall], BeastVision = p.PwUpTime[K.kPwUpBeastVision],
    };

    /// <summary>playerApplyCarry: false (nothing taken) if the player was dead at the end of the level.</summary>
    public static bool ApplyCarry(Player p, PlayerCarry c)
    {
        if (c == null || c.Health <= 0)
            return false;
        c.HasWeapon.CopyTo(p.HasWeapon, 0);
        c.WeaponMode.CopyTo(p.WeaponMode, 0);
        c.Ammo.CopyTo(p.Ammo, 0);
        c.Armor.CopyTo(p.Armor, 0);
        c.PackAmount.CopyTo(p.PackAmount, 0);
        c.PackActive.CopyTo(p.PackActive, 0);
        p.PackItemId = c.PackItemId;
        p.CurWeapon = c.CurWeapon;
        p.LastWeapon = c.LastWeapon;
        p.NextWeapon = c.NextWeapon;
        p.WeaponState = c.WeaponState;
        p.WeaponAmmo = c.WeaponAmmo;
        p.FragCount = c.FragCount;
        p.TeamId = c.TeamId;
        p.PwUpTime[K.kPwUpJumpBoots] = c.JumpBoots;
        p.PwUpTime[K.kPwUpDivingSuit] = c.DivingSuit;
        p.PwUpTime[K.kPwUpCrystalBall] = c.CrystalBall;
        p.PwUpTime[K.kPwUpBeastVision] = c.BeastVision;
        p.XHealth = c.Health;
        p.WeaponQav = -1;
        p.WeaponTimer = 0;
        p.QavCallback = null;
        p.QavLoop = false;
        return true;
    }

    /// <summary>The map the level leads to (levelGoNext): the secret map after a secret exit; "" at the end.</summary>
    public string NextLevel() => ExitKind == 1 && SecretMap != "" ? SecretMap : NextMap;

    void ViewSetMessageAll(string msg)
    {
        foreach (var p in Players)
            ViewSetMessage(p, msg);
    }

    /// <summary>trTextOver: level message n to every player.</summary>
    void TextOver(int n)
    {
        if (_levelMessages.TryGetValue(n, out var s))
            ViewSetMessageAll(s);
    }

    /// <summary>levelTriggerSecret: 0 a secret, above 0 a super secret.</summary>
    void TriggerSecret(int nType)
    {
        if (nType == 0)
            SecretsFound++;
        else if (nType < 0)
            return;
        else
            SuperSecretsFound++;
        if (GameType == K.kGameTypeSinglePlayer)
            ViewSetMessageAll(Random(2) == 0 ? "A secret is revealed." : "You found a secret.");
    }

    /// <summary>levelEndLevel: the level is over (0 normal exit, 1 secret); the client shows the stats.</summary>
    void EndLevel(int kind)
    {
        if (LevelEnded)
            return;
        LevelEnded = true;
        ExitKind = kind;
        foreach (var p in Players)
            SoundLocal(p, 268);
    }
}
