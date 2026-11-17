using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Chosen.Data;

namespace Chosen.Sim.Modern;

/// <summary>
/// A custom dude's set-up (NBlood's CUSTOMDUDE_SETUP, CUSTOMDUDEV2_SETUP and CUSTOMDUDEV1_SETUP): from its descriptor
/// (an INI file of groups: General, Velocity, Animation, Sound, WeaponN, Damage, Dodge, Recoil, Crouch, Knockout,
/// FXEffectN, DropItem, MoveSetup, Flight, Morphing, Sleeping, Slaves, Randomness, LastStand, TargetChange, Tweaks; its
/// values numbers, percents, keywords with an id (Projectile_10, FX_27, GIB_8) and arrays in brackets, plain or of
/// key=value pairs), or for a version 1 dude from the sprite's data fields. A descriptor read once is kept as a
/// template the other dudes of its id copy.
/// </summary>
public sealed class CustomDudeSetup
{
    const int ValFix = 2, ValUfix = 3, ValPerc = 4, ValBool = 7;
    const int NoVal = int.MaxValue;

    readonly World _w;
    readonly CustomDude _d;
    Dictionary<string, Dictionary<string, string>> _ini;
    string _file = "", _group = "", _param = "";
    int _defaultPosture = Cd.PostureL, _warnings;
    bool _sortWeapons = true, _showWarnings = true;

    CustomDudeSetup(World w, CustomDude d)
    {
        _w = w;
        _d = d;
    }

    // ---------------------------------------------------------------- tables

    static readonly (int id, string text)[] ParamAnim =
    {
        (Cd.StateIdle, "Idle"), (Cd.StateSearch, "Search"), (Cd.StateDodge, "Dodge"), (Cd.StateChase, "Chase"), (Cd.StateFlee, "Flee"),
        (Cd.StateRecoil, "RecoilDefault"), (Cd.StateRecoilT, "RecoilElectric"), (Cd.BurnStateSearch, "Burning"),
        (Cd.StateMorph, "Morphing"), (Cd.StateKnockEnter, "KnockoutEnter"), (Cd.StateKnock, "Knockout"),
        (Cd.StateKnockExit, "KnockoutExit"), (Cd.StateSleep, "IdleSleep"), (Cd.StateWake, "IdleWake"),
        (Cd.StateDeathBase, "DeathDefault"), (Cd.StateDeathBurn, "DeathBurning"), (Cd.StateDeathVector, "DeathBullet"),
        (Cd.StateDeathExplode, "DeathExplode"), (Cd.StateDeathChoke, "DeathChoke"), (Cd.StateDeathSpirit, "DeathSpirit"),
        (Cd.StateDeathElectric, "DeathElectric"), (Cd.StateDeathLStand, "DeathLastStand"), (Cd.AnimScale, "Scale"),
        (Cd.StateDeath, "Death"), (Cd.StateMove, "Move"), (Cd.StateAttack, "Attack"), (Cd.AnimLStandEnter, "LastStandEnter"),
        (Cd.AnimLStand, "LastStand"), (Cd.AnimLStandExit, "LastStandExit"),
    };

    static readonly (int id, string text)[] ParamSounds =
    {
        (Cd.SndTargetSpot, "TargetSpot"), (Cd.SndTargetChase, "TargetChase"), (Cd.SndTargetDead, "TargetDead"),
        (Cd.SndGotHit, "GotHit"), (Cd.SndBurning, "Burning"), (Cd.SndDeathNormal, "DeathDefault"),
        (Cd.SndDeathExplode, "DeathBurning"), (Cd.SndTransforming, "Morphing"), (Cd.SndWake, "IdleWake"),
        (Cd.SndLStandEnter, "LastStandEnter"), (Cd.SndLStand, "LastStand"), (Cd.SndLStandWake, "LastStandExit"),
    };

    static readonly (int id, string text)[] ParamPosture =
        { (Cd.PostureL, "Stand"), (Cd.PostureC, "Crouch"), (Cd.PostureW, "Swim"), (Cd.PostureF, "Fly") };
    static readonly (int id, string text)[] ParamMedium = { (0, "Any"), (1, "Land"), (2, "Water"), (3, "None") };
    static readonly (int id, string text)[] ParamVelocity =
        { (Cd.VelForward, "Forward"), (Cd.VelTurn, "Turn"), (Cd.VelDodge, "Dodge"), (Cd.VelZ, "Vertical") };
    static readonly (int id, string text)[] ParamVelMod =
        { (Cd.VelModChase, "Chase"), (Cd.VelModSearch, "Search"), (Cd.VelModBurn, "Burn"), (Cd.VelModFlee, "Flee") };
    static readonly (int id, string text)[] ParamDamage =
    {
        (K.kDamageFall, "Falling"), (K.kDamageBurn, "Burning"), (K.kDamageBullet, "Bullet"), (K.kDamageExplode, "Explode"),
        (K.kDamageDrown, "Choke"), (K.kDamageSpirit, "Spirit"), (K.kDamageTesla, "Electric"),
    };
    static readonly (int id, string text)[] ParamDamageSource =
    {
        (Cd.DmgSourcePlayer, "Player"), (Cd.DmgSourceDude, "Dude"), (Cd.DmgSourceSelf, "Self"), (Cd.DmgSourceFriend, "Friend"),
        (Cd.DmgSourceEnemy, "Enemy"), (Cd.DmgSourceSlave, "Slave"), (Cd.DmgSourceKin, "Kin"),
    };
    static readonly (int id, string text)[] ParamTriggerFlags =
        { (0, "None"), (Cd.TrigVector, "Vector"), (Cd.TrigImpact, "Impact"), (Cd.TrigTouch, "Touch"), (Cd.TrigLocked, "Locked") };
    static readonly (int id, string text)[] ParamPhysics =
        { (0, "None"), (K.kPhysGravity | K.kPhysFalling, "Gravity"), (K.kPhysMove, "Motion") };

    // a weapon kind's keywords, id range and what its hitscans hit (1: what shots hit, 2: what blocks, 0: nothing)
    static readonly (int type, bool spawns, string a, string b, int lo, int hi, int mask)[] WeaponInfo =
    {
        (Cd.WeaponNone, false, "NONE", null, 0, 0, 0),
        (Cd.WeaponHitscan, false, "VECTOR", null, 0, 23, 1),
        (Cd.WeaponMissile, true, "PROJECTILE", null, K.kMissileBase, K.kMissileMax, 2),
        (Cd.WeaponThrow, true, "THROW", null, K.kThingBase, K.kThingMax, 2),
        (Cd.WeaponSummon, true, "VDUD", null, K.kDudeCultistTommy, 254, 2),
        (Cd.WeaponSummonCdude, true, "CDUD", null, 1, 10000, 2),
        (Cd.WeaponKamikaze, true, "KAMIKAZE", null, K.kTrapExploder, K.kTrapExploder + 8, 0),
        (Cd.WeaponSpecial, false, "SPECIAL", null, Cd.WeaponIdSpecialBase, Cd.WeaponIdSpecialMax, 2),
        (Cd.WeaponMorph, false, "MORPH", "VDUD", K.kDudeCultistTommy, 254, 0),
        (Cd.WeaponMorphCdude, false, "MORPH", "CDUD", 1, 10000, 0),
        (Cd.WeaponDamage, false, "DAMAGE", null, K.kDamageFall, K.kDamageMax, 0),
        (Cd.WeaponHealth, false, "HEALTH", null, 0, 0, 0),
    };

    // gSoundTemplate: the sounds a dude has when its descriptor names none
    static readonly int[][] SoundTemplate =
    {
        new[] { 1003, 1004, 1003 }, new[] { 1013, 1014, 1013 }, new[] { 1018, 1019, 1018 }, new[] { 1031, 1032, 1031 },
        new[] { 1018, 1019, 1018 }, new[] { 4021, 4022, 4021 }, new[] { 1005, 1006, 1005 }, new[] { 9008, 9008, 9008 },
        new[] { 745, 745, 745 }, new[] { 728, 729, 730 }, new[] { 715, 715, 715 }, new[] { 3009, 3010, 3011 },
    };

    // gSeqCompat: a version 1 dude's animations as offsets from its first sequence (standing, crouching, swimming)
    static readonly (int[] ofs, int state)[] SeqCompat =
    {
        (new[] { 0, 17, 13 }, Cd.StateIdle), (new[] { 0, 17, 13 }, Cd.StateGenIdle), (new[] { 9, 14, 13 }, Cd.StateSearch),
        (new[] { 9, 14, 13 }, Cd.StateDodge), (new[] { 9, 14, 13 }, Cd.StateChase), (new[] { 9, 14, 13 }, Cd.StateFlee),
        (new[] { 5, 5, 5 }, Cd.StateRecoil), (new[] { 4, 4, 4 }, Cd.StateRecoilT), (new[] { 6, 8, 8 }, Cd.StateAttackBase),
        (new[] { 7, 7, 7 }, Cd.StateAttackBase + 1), (new[] { 10, 10, 10 }, Cd.StateAttackBase + 2),
        (new[] { 3, 3, 3 }, Cd.BurnStateSearch), (new[] { 18, 18, 18 }, Cd.StateMorph), (new[] { 1, 1, 1 }, Cd.StateDeathBase),
        (new[] { 2, 2, 2 }, Cd.StateDeathExplode), (new[] { 15, 15, 15 }, Cd.StateDeathBurn), (new[] { 16, 16, 16 }, Cd.StateDeathBurn),
    };

    // gSndCompat: a version 1 dude's sounds as offsets from its first sound: (offset, how many, which)
    static readonly (int ofs, int range, int type)[] SndCompat =
    {
        (0, 2, Cd.SndTargetSpot), (2, 2, Cd.SndGotHit), (4, 2, Cd.SndDeathNormal), (6, 2, Cd.SndBurning), (8, 2, Cd.SndDeathExplode),
        (10, 2, Cd.SndTargetDead), (12, 2, Cd.SndTargetChase), (14, 1, Cd.SndCompatAttack1), (15, 1, Cd.SndCompatAttack2),
        (16, 1, Cd.SndCompatAttack3), (17, 1, Cd.SndTransforming),
    };

    static int FindParam(string str, (int id, string text)[] db)
    {
        if (str != null)
            foreach (var (id, text) in db)
                if (string.Equals(str, text, StringComparison.OrdinalIgnoreCase))
                    return id;
        return -1;
    }

    // ---------------------------------------------------------------- strings (nnextstr.cpp)

    static bool IsEmpty(string s) => string.IsNullOrEmpty(s);

    static bool IsArray(string s, out int len)
    {
        len = 0;
        if (s == null || s.Length == 0 || s[0] != '(' || s[^1] != ')')
            return false;
        len = 1;
        foreach (char c in s)
            if (c == ',')
                len++;
        return true;
    }

    static bool IsArray(string s) => IsArray(s, out _);

    // 0: no array, 1: a plain one, 2: one of key=value pairs
    static int ArrayType(string s) => IsArray(s) ? (s.Contains('=') ? 2 : 1) : 0;

    static bool IsFix(string s, bool signed)
    {
        if (IsEmpty(s))
            return false;
        if (!char.IsAsciiDigit(s[0]))
        {
            if (s[0] == '-')
            {
                if (!signed) return false;
            }
            else
                return false;
        }
        for (int i = 1; i < s.Length; i++)
            if (!char.IsAsciiDigit(s[i]))
                return false;
        return true;
    }

    static bool IsUfix(string s) => IsFix(s, false);

    static bool IsPerc(string s)
    {
        if (s == null || s.Length < 2 || s[^1] != '%')
            return false;
        for (int l = s.Length - 2; l > 0; l--)
            if (!char.IsAsciiDigit(s[l]))
                return false;
        return char.IsAsciiDigit(s[0]) || s[0] == '-' || s[0] == '+';
    }

    static int Btoi(string s) => s == null ? -1 : s.ToLowerInvariant() switch
    {
        "0" or "no" or "false" => 0,
        "1" or "yes" or "true" => 1,
        _ => -1,
    };

    static int Atoi(string s)
    {
        // as C's atoi: the leading number, 0 without one
        int i = 0, sign = 1;
        long v = 0;
        s = s.TrimStart();
        if (i < s.Length && (s[i] == '-' || s[i] == '+'))
            sign = s[i++] == '-' ? -1 : 1;
        while (i < s.Length && char.IsAsciiDigit(s[i]) && v < int.MaxValue)
            v = v * 10 + (s[i++] - '0');
        return (int)Math.Clamp(v * sign, int.MinValue, int.MaxValue);
    }

    static string Strip(string s)
    {
        var sb = new System.Text.StringBuilder(s.Length);
        foreach (char c in s)
            if (!char.IsWhiteSpace(c))
                sb.Append(c);
        string t = sb.ToString();
        if (t.Length > 0 && t[0] == '(')
            t = t[1..];
        if (t.Length > 0 && t[^1] == ')')
            t = t[..^1];
        return t;
    }

    // enumStr(offset, str, val): the element at an offset of a plain list (split at every comma)
    static bool EnumVal(string str, int offs, out string val)
    {
        val = "";
        if (str == null)
            return false;
        var parts = Strip(str).Split(',');
        if (offs < 0 || offs >= parts.Length || parts[offs].Length == 0)
            return false;
        val = parts[offs];
        return true;
    }

    // enumStr(offset, str, key, val): the key=value pair at an offset of an array (a value that opens a bracket takes
    // the elements up to the one that closes it); the offset after it, 0 at the end
    static int EnumKV(string str, int offs, out string key, out string val)
    {
        key = val = "";
        if (!IsArray(str))
            return 0;
        var parts = Strip(str).Split(',');
        if (offs < 0 || offs >= parts.Length || parts[offs].Length == 0)
            return 0;
        string el = parts[offs];
        int eq = el.IndexOf('=');
        key = eq >= 0 ? el[..eq] : el;
        val = eq >= 0 ? el[(eq + 1)..] : "";
        // (enumStrGetChar(1, .., '=') takes what lies between the first and second '=')
        int eq2 = val.IndexOf('=');
        if (eq2 >= 0)
            val = val[..eq2];
        if (val.Length > 0 && val[0] == '(' && val[^1] != ')')
        {
            offs++;
            while (true)
            {
                if (offs >= parts.Length || parts[offs].Length == 0)
                    return 0;       // no end of the array
                val += "," + parts[offs];
                if (parts[offs][^1] == ')')
                    return offs + 1;
                offs++;
            }
        }
        return offs + 1;
    }

    static bool IsIdKeyword(string full, string prefix, out int id)
    {
        id = 0;
        if (full == null || prefix == null)
            return false;
        int l1 = full.Length, l2 = prefix.Length;
        if (l2 >= l1 || !full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;
        while (full[l2] == '_')
            if (++l2 >= l1)
                return false;
        if (!IsUfix(full[l2..]))
            return false;
        id = Atoi(full[l2..]);
        return true;
    }

    static bool IsIdKeyword(string full, string prefixA, string prefixB, out int id)
    {
        id = 0;
        if (full == null || prefixA == null || prefixB == null)
            return false;
        int l1 = full.Length, l2 = prefixA.Length;
        if (l2 >= l1 || !full.StartsWith(prefixA, StringComparison.OrdinalIgnoreCase))
            return false;
        while (full[l2] == '_')
            if (++l2 >= l1)
                return false;
        return IsIdKeyword(full[l2..], prefixB, out id);
    }

    // ---------------------------------------------------------------- the descriptor

    static readonly Dictionary<string, Dictionary<string, Dictionary<string, string>>> _descriptors = new();

    /// <summary>The descriptors read are forgotten (an add-on activated or left: its own files).</summary>
    public static void ForgetDescriptors() => _descriptors.Clear();

    static Dictionary<string, Dictionary<string, string>> ReadIni(string path)
    {
        var ini = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string> cur = null;
        foreach (var raw in File.ReadLines(path, System.Text.Encoding.Latin1))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line[0] == ';')
                continue;
            if (line[0] == '[')
            {
                int e = line.IndexOf(']');
                string name = (e > 0 ? line[1..e] : line[1..]).Trim();
                if (!ini.TryGetValue(name, out cur))
                    ini[name] = cur = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                continue;
            }
            int eq = line.IndexOf('=');
            if (eq <= 0 || cur == null)
                continue;
            cur[line[..eq].Trim()] = line[(eq + 1)..].Trim();
        }
        return ini;
    }

    /// <summary>DescriptLoad: the dude's descriptor, CDUDnnnn.CDU (or CDUDnnnn_anything.CDU), in the data folder's
    /// data/cdud (the active add-on's first).</summary>
    bool DescriptLoad(int nID)
    {
        if (nID < 0 || nID >= 10000)
            return false;
        string rel = $"data/cdud/cdud{nID.ToString(CultureInfo.InvariantCulture)}.cdu";
        string path = BloodData.Find(rel);
        if (!File.Exists(path))
        {
            path = null;
            foreach (var dir in new[] { Path.GetDirectoryName(BloodData.Find(rel)), Path.Combine(BloodData.Folder, "data", "cdud") })
            {
                if (dir == null || !Directory.Exists(dir))
                    continue;
                foreach (var f in Directory.GetFiles(dir, $"cdud{nID.ToString(CultureInfo.InvariantCulture)}_*.cdu"))
                {
                    path = f;
                    break;
                }
                if (path != null)
                    break;
            }
            if (path == null)
                return false;
        }
        if (!_descriptors.TryGetValue(path, out _ini))
            _descriptors[path] = _ini = ReadIni(path);
        _file = Path.GetFileName(path);
        return true;
    }

    bool GroupExist(string group) => _ini != null && _ini.ContainsKey(group);

    string GetValue(string group, string param)
    {
        _param = param;
        if (_ini == null || !_ini.TryGetValue(group, out var g) || !g.TryGetValue(param, out var v) || v.Length == 0)
            return null;
        return v;
    }

    string GetValue(string param) => GetValue(_group, param);

    int DescriptCheck()
    {
        if (!GroupExist("General"))
        {
            _group = "General";
            Warning("Required group \"General\" not found");
            return 0;
        }
        int ver = 0;
        string v = GetValue("General", "Version");
        if (v != null && v.Length < 5 && char.IsAsciiDigit(v[0]))
            ver = v[0] - '0';
        if (Btoi(GetValue("Parser", "ShowWarnings")) is int b && b != -1)
            _showWarnings = b != 0;
        return ver;
    }

    static readonly HashSet<string> _warned = new();

    void Warning(string what)
    {
        _warnings++;
        if (!_showWarnings)
            return;
        string text = $"custom dude {_file}: {what} (group {_group}, parameter {_param})";
        if (_warned.Add(text))
            Core.GameConsole.Print(text);
    }

    // ---------------------------------------------------------------- value checks

    int CheckRange(string str, int nVal, int nMin, int nMax)
    {
        if (nVal < nMin || nVal > nMax)
        {
            Warning($"Correct range of \"{str}\" is {nMin} - {nMax}");
            return nMin;
        }
        return nVal;
    }

    int CheckValue(string str, int nValType, int nDefault)
    {
        bool ok = nValType switch
        {
            ValFix => IsFix(str, true),
            ValUfix => IsUfix(str),
            ValPerc => IsPerc(str),
            ValBool => Btoi(str) != -1,
            _ => false,
        };
        if (!ok)
        {
            Warning($"The value \"{str}\" is not a correct {(nValType == ValPerc ? "percent" : nValType == ValBool ? "boolean" : "number")} value");
            return nDefault;
        }
        return nValType == ValBool ? Btoi(str) : Atoi(str);
    }

    int CheckValue(string str, int nValType, int nMin, int nMax, int nDefault)
    {
        int n = CheckValue(str, nValType, NoVal);
        if (n == NoVal)
            return nDefault;
        if (n < nMin || n > nMax)
        {
            Warning($"Correct range of \"{str}\" is {nMin} - {nMax}");
            return nDefault;
        }
        return n;
    }

    int CheckValue(string str, int nValType, int nMin, int nMax)
    {
        int n = CheckValue(str, nValType, NoVal);
        return n != NoVal ? CheckRange(str, n, nMin, nMax) : nMin;
    }

    int CheckArray(string str, int nMin = 0, int nMax = 0, int nDefault = 1)
    {
        if (!IsArray(str, out int nLen))
            return nDefault;
        if (nMax == 0 && nMin > 0 && nLen < nMin)
        {
            Warning($"Invalid array length! Expected: {nMin}. Given: {nLen}");
            return nDefault;
        }
        if ((nMin > 0 && nLen < nMin) || (nMax > 0 && nLen > nMax))
        {
            Warning($"Invalid array length! Expected: {nMin} - {nMax}. Given: {nLen}");
            return nDefault;
        }
        return nLen;
    }

    int ParseKeywords(string str, (int id, string text)[] db)
    {
        int nRetn = 0;
        for (int i = 0; i < db.Length && EnumVal(str, i, out string tmp); i++)
        {
            int nPar = FindParam(tmp, db);
            if (nPar >= 0)
                nRetn |= nPar;
            else
                Warning($"Invalid parameter \"{tmp}\"");
        }
        return nRetn;
    }

    // ParseRange: one or two values; how many were read. out0, out1: the allowed range in, the values out
    int ParseRange(string str, int nValType, ref int out0, ref int out1, int nBaseVal = 0)
    {
        int nMin = out0, nMax = out1;
        bool chkrng = nMin != 0 && nMax != 0;
        if (IsEmpty(str))
            return 0;
        int nLen = CheckArray(str, 0, 2);
        int i = 0;
        while (i < nLen && EnumVal(str, i, out string tmp))
        {
            int nVal = chkrng ? CheckValue(tmp, nValType, nMin, nMax) : CheckValue(tmp, nValType, 0);
            if (nValType == ValPerc && nBaseVal != 0)
                nVal = Cd.Perc2Val(nVal, nBaseVal);
            if (i == 0) out0 = nVal;
            else out1 = nVal;
            i++;
        }
        return i;
    }

    int ParseIDs(string str, int nValType, List<int> o, int nMax = 0)
    {
        if (IsArray(str, out int nLen))
        {
            if (nMax > 0 && nMax < nLen)
            {
                Warning($"Invalid array length! Expected: 1 - {nMax}. Given: {nLen}");
                return 0;
            }
            nMax = nLen;
        }
        for (int i = 0; (nMax <= 0 || i < nMax) && EnumVal(str, i, out string tmp); i++)
        {
            int nVal = CheckValueId(tmp, nValType);
            if (nVal != NoVal)
                o.Add(nVal);
        }
        return o.Count;
    }

    // a value of a plain type, or (type 100 + n) an id keyword: 100 FX, 101 GIB
    int CheckValueId(string tmp, int nValType)
    {
        if (nValType == 101)
        {
            if (IsIdKeyword(tmp, "GIB", out int g))
                return g;
            Warning($"The value \"{tmp}\" is not a correct GIB value");
            return NoVal;
        }
        return CheckValue(tmp, nValType, NoVal);
    }

    int ParseIDs(string str, int nValType, int[] o, int nMax)
    {
        var l = new List<int>();
        ParseIDs(str, nValType, l, nMax);
        for (int i = 0; i < l.Count && i < o.Length; i++)
            o[i] = l[i];
        return Math.Min(l.Count, o.Length);
    }

    int ParseMedium(string str) => FindParam(str, ParamMedium) switch { 1 => 1, 2 => 2, 3 => 4, 0 => 0, _ => -1 };

    int ParsePosture(string str)
    {
        int nRetn = 0;
        for (int i = 0; i < Cd.PostureMax && EnumVal(str, i, out string key); i++)
        {
            switch (FindParam(key, ParamPosture))
            {
                case Cd.PostureL: nRetn |= 1; break;
                case Cd.PostureC: nRetn |= 2; break;
                case Cd.PostureW: nRetn |= 4; break;
                case Cd.PostureF: nRetn |= 8; break;
            }
        }
        return nRetn;
    }

    int ParseTimer(string str, CdTimer t)
    {
        int a = 0, b = 32767;
        int n = ParseRange(str, ValUfix, ref a, ref b);
        if (n == 2)
        {
            t.Rng0 = a & 0xffff;
            t.Rng1 = b & 0xffff;
        }
        else if (n == 1)
        {
            t.Rng0 = a & 0xffff;
            t.Rng1 = 0;
        }
        return n;
    }

    bool ParseSkill(string str)
    {
        int a = 1, b = 5;
        int nSkill = _w.Skill + 1;
        return ParseRange(str, ValUfix, ref a, ref b) switch
        {
            1 => nSkill == a,
            2 => nSkill >= a && nSkill <= b,
            _ => false,
        };
    }

    int ParseOffsets(string str, out int x, out int y, out int z)
    {
        x = y = z = 0;
        int nLen = CheckArray(str, 3, 0, 0);
        int i = 0;
        while (i < nLen && EnumVal(str, i, out string tmp))
        {
            int nVal = CheckValue(tmp, ValFix, -16383, 16383, 0);
            if (i == 0) x = nVal;
            else if (i == 1) y = nVal;
            else if (i == 2) z = nVal << 4;
            i++;
        }
        return i;
    }

    // ---------------------------------------------------------------- animation and sound

    void AnimationFill(int state, int nAnim)
    {
        for (int i = 0; i < Cd.PostureMax; i++)
            _d.States[state, i].Seq = nAnim;
    }

    static bool SeqExists(int nSeq) => nSeq > 0 && SeqData.Exists(nSeq);
    static bool SndExists(int nSnd) => nSnd > 0 && SfxData.HasFile(nSnd);

    // ParseAnimation into four sequence ids (by posture, or one for every posture)
    bool ParseAnimation(string str, int[] seq, bool asPosture)
    {
        int nLen = CheckArray(str, 0, Cd.PostureMax, 0);
        if (nLen > 0)
        {
            int i;
            for (i = 0; i < nLen; i++)
            {
                if (asPosture)
                {
                    // (each element its own pair: none of an animation's values is an array)
                    if (EnumKV(str, i, out string key, out string val) != 0)
                    {
                        int nPar = FindParam(key, ParamPosture);
                        if (nPar >= 0 && nPar < Cd.PostureMax)
                            seq[nPar] = CheckValue(val, ValUfix, 0, 65535);
                        continue;
                    }
                }
                else if (EnumVal(str, i, out string v))
                {
                    int nVal = CheckValue(v, ValUfix, 0, 65535);
                    for (int j = i; j < Cd.PostureMax; j++)
                        seq[j] = nVal;
                    continue;
                }
                break;
            }
            return i > 0;
        }
        if (!IsEmpty(str))
        {
            int i = asPosture ? _defaultPosture : 0;
            seq[i] = CheckValue(str, ValUfix, 0, 65535);
            if (!asPosture)
                for (int j = 0; j < Cd.PostureMax; j++)
                    seq[j] = seq[i];
            return true;
        }
        return false;
    }

    void SetupAnimationOf(int state, string value, bool asPosture)
    {
        var seq = new int[Cd.PostureMax];
        ParseAnimation(value, seq, asPosture);
        for (int i = 0; i < Cd.PostureMax; i++)
            _d.States[state, i].Seq = seq[i];
    }

    /// <summary>AnimationConvert: a version 1 dude's animations from its first sequence.</summary>
    void AnimationConvert(int baseID)
    {
        for (int i = 0; i < Cd.StateMax; i++)
            AnimationFill(i, 0);
        foreach (var (ofs, state) in SeqCompat)
        {
            if (state >= Cd.StateDeathBase && state < Cd.StateDeathMax)
            {
                // deaths are filled, one is picked at random
                for (int j = 0, k = 0; j < Cd.PostureMax; j++)
                {
                    if (!SeqExists(baseID + ofs[k]))
                        _d.States[state, j].Seq = baseID + ofs[k];
                    k = (k + 1) % ofs.Length;
                }
            }
            else
            {
                for (int j = 0; j < ofs.Length; j++)
                    _d.States[state, j].Seq = baseID + ofs[j];
            }
        }
    }

    void AnimationFillAll()
    {
        for (int i = 0; i < Cd.StateMax; i++)
            for (int j = 0; j < Cd.PostureMax; j++)
                if (!SeqExists(_d.States[i, j].Seq))
                    _d.States[i, j].Seq = 0;
    }

    static void SoundFill(CdSound s, int nSnd)
    {
        for (int i = 0; i < Cd.MaxSounds; i++)
            s.Id[i] = nSnd;
    }

    static void SoundFromTemplate(CdSound s, int which)
    {
        Array.Copy(SoundTemplate[which], s.Id, Cd.MaxSounds);
        s.Medium = 3;
        s.Ai = true;
        s.Interruptable = true;
        s.Once = 1;
        s.Volume = 0;
        s.Pitch = 0;
    }

    void WeaponSoundSetDefault(CdWeapon wp)
    {
        var s = wp.ShotSound;
        int j = 0;
        for (int i = 0; i < Cd.MaxSounds; i++)
        {
            switch (wp.Type)
            {
                case Cd.WeaponHitscan:
                    if (wp.Id >= 0 && wp.Id * 2 + 1 < KM.VectorFireSound.Length)
                        s.Id[i] = KM.VectorFireSound[wp.Id * 2 + j];
                    j = (j + 1) % 2;
                    break;
                case Cd.WeaponMissile:
                {
                    int k = wp.Id - K.kMissileBase;
                    if (k >= 0 && k < KM.MissileExtra.Length)
                        s.Id[i] = j == 0 ? KM.MissileExtra[k].snd0 : KM.MissileExtra[k].snd1;
                    j = (j + 1) % 2;
                    break;
                }
                case Cd.WeaponKamikaze:
                {
                    int k = wp.Id - K.kTrapExploder;
                    if (k >= 0 && k < KM.ExplodeExtra.Length)
                        s.Id[i] = KM.ExplodeExtra[k].snd;
                    break;
                }
                case Cd.WeaponThrow:
                    s.Id[i] = 455;
                    break;
                case Cd.WeaponSummon:
                    s.Id[i] = 379;
                    break;
            }
        }
    }

    /// <summary>SoundConvert: the default sounds, and a version 1 dude's own from its first sound.</summary>
    void SoundConvert(int baseID)
    {
        for (int i = 0; i < Cd.SndMax; i++)
            SoundFromTemplate(_d.Sound[i], i);
        for (int i = 0; i < _d.NumWeapons; i++)
            WeaponSoundSetDefault(_d.Weapons[i]);
        if (baseID <= 0)
            return;
        foreach (var (ofs, range, type) in SndCompat)
        {
            CdSound s;
            if (type >= Cd.SndCompatAttack1 && type <= Cd.SndCompatAttack3)
            {
                CdWeapon wp;
                if (type == Cd.SndCompatAttack3)
                    wp = _d.Weapons[1];         // the melee attack: the second weapon now
                else if (type == Cd.SndCompatAttack2)
                {
                    wp = _d.Weapons[0];
                    if (wp.Type != Cd.WeaponThrow)
                        continue;
                }
                else
                    wp = _d.Weapons[0];
                s = wp.ShotSound;
                s.Medium = 3;
                s.Interruptable = true;
                s.Ai = false;
                s.Once = 0;
                if (!SndExists(baseID + ofs))
                    continue;       // the weapon keeps its default sounds
            }
            else
                s = _d.Sound[type];
            for (int j = 0, k = 0; j < Cd.MaxSounds; j++)
            {
                s.Id[j] = baseID + k + ofs;
                k = (k + 1) % range;
            }
        }
    }

    // a sound's missing ids take its first existing one
    static void SoundFillMissing(CdSound s)
    {
        int nSnd = -1;
        for (int j = 0; j < Cd.MaxSounds; j++)
        {
            if (nSnd < 0)
            {
                if (SndExists(s.Id[j]))
                {
                    nSnd = s.Id[j];
                    j = -1;
                }
            }
            else if (!SndExists(s.Id[j]))
                s.Id[j] = nSnd;
        }
    }

    void SoundFillAll()
    {
        foreach (var s in _d.Sound)
            SoundFillMissing(s);
        foreach (var wp in _d.Weapons)
            SoundFillMissing(wp.ShotSound);
    }

    bool ParseSound(string str, CdSound s)
    {
        int nMedium = s.Medium, nVolume = s.Volume, nPitch = s.Pitch;
        int t = Cd.MaxSounds, i = 0, percCnt = 0;
        int nLen = CheckArray(str, 0, t + 3, 0);
        if (nLen > 0)
        {
            // one to three ids first
            string tmp = "";
            while (i < t && EnumVal(str, i, out tmp) && IsUfix(tmp))
            {
                int nVal = CheckValue(tmp, ValUfix, 0, 65535);
                for (int j = i; j < t; j++)
                    s.Id[j] = nVal;
                i++;
            }
            if (i == 0)
            {
                Warning($"Invalid parameter \"{tmp}\"");
                t = 0;
            }
            else if (t < nLen)
                t += 3;
            // then the volume, the pitch (percents) and the medium
            while (i < t && EnumVal(str, i, out tmp))
            {
                if (IsPerc(tmp))
                {
                    if (nLen - i <= 3 && ++percCnt <= 2)
                    {
                        if (percCnt == 1) nVolume = CheckValue(tmp, ValPerc, 0, 1023, 100);
                        else nPitch = CheckValue(tmp, ValPerc, 0, 2048, 0);
                    }
                    else
                    {
                        Warning($"Invalid position of value \"{tmp}\"");
                        break;
                    }
                }
                else if ((nMedium = ParseMedium(tmp)) >= 0)
                {
                    if (nLen - i != 1)
                    {
                        Warning($"Invalid position of value \"{tmp}\"");
                        break;
                    }
                }
                else
                {
                    Warning($"Invalid parameter \"{tmp}\"");
                    break;
                }
                i++;
            }
            if (t != 0)
            {
                s.Volume = Cd.Perc2Val(nVolume, 255);
                s.Pitch = Cd.Perc2Val(nPitch, 0x10000);
                s.Medium = Math.Max(nMedium, 0);
                return true;
            }
        }
        else if (!IsEmpty(str))
        {
            int nVal = CheckValue(str, ValUfix, 0, 65535);
            s.Medium = 0;
            SoundFill(s, nVal);
            return true;
        }
        return false;
    }

    /// <summary>CountHeight: the dude's height from the tallest frame of its idle and moving animations.</summary>
    void CountHeight()
    {
        var e = _d.Spr;
        _d.Height = 128;
        for (int i = Cd.StateIdle; i < Cd.StateMoveMax; i++)
        {
            int seq = _d.States[i, Cd.PostureL].Seq;
            if (!SeqExists(seq))
                continue;
            int n = SeqData.FrameCount(seq);
            for (int j = 0; j < n; j++)
            {
                var (_, fy) = SeqData.FrameRepeat(seq, j);
                int yr = fy > 0 ? fy : e.YRepeat;
                if (e.XScale != 0)
                    yr = Math.Clamp((int)Blood.MulScale8(yr, e.XScale), 0, 255);
                var (zt, zb) = Physics.SpriteExtents(e.BZ, SeqData.FrameTile(seq, j), yr, e.Cstat);
                int hg = (int)Math.Abs(zb - zt);
                if (hg > _d.Height)
                    _d.Height = hg;
            }
        }
        _d.EyeHeight = Cd.Perc2Val(85, _d.Height >> 1);
    }

    int FirstPic()
    {
        int seq = _d.GetStateSeq(Cd.StateIdle, Cd.PostureL);
        return SeqExists(seq) && SeqData.FrameCount(seq) > 0 ? SeqData.FrameTile(seq, 0) : _d.Spr.Picnum;
    }

    // an attack animation with no trigger frame gets one on its last frame
    void EnsureTrigger(int state)
    {
        for (int t = 0; t < Cd.PostureMax; t++)
        {
            int seq = _d.States[state, t].Seq;
            if (SeqExists(seq) && !SeqData.HasTrigger(seq))
                SeqData.SetLastFrameTrigger(seq);
        }
    }

    // ---------------------------------------------------------------- defaults shared by both versions

    void VelocitySetDefault(int nMaxVel)
    {
        var e = _d.Spr;
        int nFrontSpeed = Info.Dude(KM.kDudeModernCustom, Info.DI_frontSpeed);
        int nSideSpeed = Info.Dude(KM.kDudeModernCustom, Info.DI_sideSpeed);
        int nAngSpeed = Info.Dude(KM.kDudeModernCustom, Info.DI_angSpeed);
        if (e.BusyTime != 0)
            nFrontSpeed = Math.Min(nFrontSpeed / 3 + 2500 * e.BusyTime, nMaxVel);
        _d.VelocityModID = Cd.VelModNone;
        Array.Clear(_d.VelocityMods);
        for (int i = 0; i < Cd.PostureMax; i++)
        {
            _d.SetVelocityBase(nFrontSpeed, i, Cd.VelForward);
            _d.SetVelocityBase(nSideSpeed, i, Cd.VelDodge);
            _d.SetVelocityBase(nAngSpeed, i, Cd.VelTurn);
            _d.SetVelocityBase(nFrontSpeed, i, Cd.VelZ);
        }
    }

    void DamageSetDefault()
    {
        for (int i = 0; i < K.kDamageMax; i++)
            _d.DamageId[i] = Info.Dude(KM.kDudeModernCustom, Info.DI_startDamage + i);
    }

    void DamageScaleToSkill(int nSkill)
    {
        for (int i = 0; i < K.kDamageMax; i++)
            _d.DamageSet((int)Blood.MulScale8(World.DudeDifficulty(nSkill), _d.DamageId[i]), i);
    }

    static void EventDefaults(CdOnDamage on)
    {
        foreach (var ev in on.OnDamage)
        {
            ev.Amount = ev.Health = ev.Chance = ev.StateTime0 = ev.StateTime1 = 0;
            ev.Cooldown.Time = ev.Cooldown.Rng1 = ev.Cooldown.Mul = 0;
            ev.Cooldown.Rng0 = 1;       // kCdudeDmgCheckDelay
            ev.Cumulative = true;
            ev.HitCount = 1;
        }
    }

    static void EventClear(CdOnDamage on)
    {
        foreach (var ev in on.OnDamage)
            ev.CopyFrom(new CdEventDamage());
    }

    // ---------------------------------------------------------------- version 2: the groups

    void SetupGeneral()
    {
        var e = _d.Spr;
        _d.Mass = 75;
        _d.Medium = 0;
        _d.NextDude = -1;
        string v;
        if ((v = GetValue("SearchTime")) != null)
        {
            int nVal = CheckValue(v, ValUfix, 0, 32767, 800);
            for (int i = 0; i < Cd.PostureMax; i++)
                _d.States[Cd.StateSearch, i].Ticks = nVal;
        }
        if ((v = GetValue("Mass")) != null)
            _d.Mass = CheckValue(v, ValUfix, 1, 65535, 75);
        if ((v = GetValue("Medium")) != null)
        {
            int nVal = ParseMedium(v);
            if (nVal >= 0) _d.Medium = nVal;
            else Warning($"\"{v}\" is not a valid value of \"Medium\"");
        }
        if ((v = GetValue("Health")) != null && !_d.Initialized)
        {
            _d.Health = CheckValue(v, ValUfix, 1, 65535, 60);
            if (e.SysData2 == 0)
                e.Data4 = e.SysData2 = _d.Health;
            e.XHealth = Math.Clamp(e.SysData2 << 4, 0, 65535);
        }
        if ((v = GetValue("Clipdist")) != null)
            e.ClipDist = CheckValue(v, ValUfix, 0, 255, 48);
        if ((v = GetValue("Seedist")) != null)
            _d.SeeDist = CheckValue(v, ValUfix, 0, 65535, Info.Dude(KM.kDudeModernCustom, Info.DI_seeDist)) << 3;
        if ((v = GetValue("Heardist")) != null)
            _d.HearDist = CheckValue(v, ValUfix, 0, 65535, Info.Dude(KM.kDudeModernCustom, Info.DI_hearDist)) << 3;
        if ((v = GetValue("Periphery")) != null)
            _d.Periphery = CheckValue(v, ValUfix, 0, K.kAng360, Info.Dude(KM.kDudeModernCustom, Info.DI_periphery));
    }

    bool ParseBaseVelocity(string str, int posture)
    {
        int nMod = _d.Spr.BusyTime;
        if (IsArray(str))
        {
            for (int i = 0; EnumKV(str, i, out string key, out string val) != 0; i++)
            {
                int nPar = FindParam(key, ParamVelocity), nVal;
                switch (nPar)
                {
                    case Cd.VelDodge:
                    case Cd.VelForward:
                        if ((nVal = CheckValue(val, ValUfix, NoVal)) != NoVal)
                        {
                            nVal <<= 2;
                            if (nMod != 0 && nPar == Cd.VelForward)
                                nVal = nVal / 3 + 2500 * nMod;      // the sprite's busy time changes the speed
                            _d.VelocityId[posture, nPar] = nVal;
                        }
                        break;
                    case Cd.VelTurn:
                        if ((nVal = CheckValue(val, ValUfix, 0, 100, -1)) != -1)
                            _d.VelocityId[posture, nPar] = nVal << 2;
                        break;
                    case Cd.VelZ:
                        if ((nVal = CheckValue(val, ValUfix, NoVal)) != NoVal)
                        {
                            nVal <<= 5;
                            if (nMod != 0)
                                nVal = nVal / 3 + 2500 * nMod;
                            _d.VelocityId[posture, nPar] = nVal;
                        }
                        break;
                }
            }
            return true;
        }
        if (!IsEmpty(str))
        {
            int nVal = CheckValue(str, ValUfix, NoVal);
            if (nVal != NoVal)
            {
                nVal <<= 2;
                if (nMod != 0)
                    nVal = nVal / 3 + 2500 * nMod;
                _d.VelocityId[posture, Cd.VelForward] = nVal;
                return true;
            }
        }
        return false;
    }

    void SetupVelocity()
    {
        VelocitySetDefault(Cd.MaxVelocity);
        if (!GroupExist(_group))
            return;
        int nPosture = -1, c = 0;
        foreach (var (id, text) in ParamPosture)
        {
            if (ParseBaseVelocity(GetValue(text), id))
            {
                if (nPosture < 0)
                    nPosture = id;
                c++;
            }
        }
        string v = GetValue("Modifiers");
        if (v != null && IsArray(v))
        {
            for (int i = 0; EnumKV(v, i, out string key, out string val) != 0; i++)
            {
                int nPar = FindParam(key, ParamVelMod);
                int nVal;
                if (nPar >= Cd.VelModChase && nPar < Cd.VelModMax && (nVal = CheckValue(val, ValPerc, 0)) != 0)
                    _d.VelocityMods[nPar] = Math.Clamp(nVal, -32768, 32767);
            }
        }
        if (nPosture >= 0 && c < 2)
            _defaultPosture = nPosture;
    }

    void SetupAnimation()
    {
        var e = _d.Spr;
        e.XScale = Cd.DefaultAnimScale;
        if (!GroupExist(_group))
        {
            // no group: the version 1 set of animations
            AnimationConvert(Cd.DefaultSeq);
            return;
        }
        foreach (var (id, text) in ParamAnim)
        {
            string v = GetValue(text);
            if (id == Cd.AnimScale)
            {
                int a = 0, b = 1024;
                switch (ParseRange(v, ValPerc, ref a, ref b, Cd.DefaultAnimScale))
                {
                    case 1: e.XScale = a; break;
                    case 2: e.XScale = a + _w.Random(b - a); break;
                }
            }
            else if (id == Cd.StateMove)
            {
                // the moving states that have no animation of their own
                for (int i = Cd.StateMoveBase; i < Cd.StateMoveMax; i++)
                {
                    string own = null;
                    foreach (var (pid, ptext) in ParamAnim)
                        if (pid == i)
                            own = GetValue(ptext);
                    _param = text;
                    if (own == null)
                        SetupAnimationOf(i, v, true);
                }
            }
            else if (id >= 0 && id < Cd.StateNormalMax)
            {
                SetupAnimationOf(id, v, true);
                if (id == Cd.StateIdle)
                    SetupAnimationOf(Cd.StateGenIdle, v, true);
            }
            else if (id >= Cd.StateDeathBase && id < Cd.StateDeathMax)
            {
                if (ArrayType(v) == 2)
                {
                    // by posture: the dude dies as its posture says
                    SetupAnimationOf(id, v, true);
                    for (int i = 0; i < Cd.PostureMax; i++)
                        _d.States[id, i].Type = Cd.StateTypeDeathPosture;
                }
                else
                    SetupAnimationOf(id, v, false);     // one of them at random
            }
        }
    }

    void SetupSound()
    {
        if (!GroupExist(_group))
        {
            SoundConvert(0);
            return;
        }
        int allowMultiSrc = -1;
        string v = GetValue("MultipleSources");
        if (v != null)
        {
            int nVal = CheckValue(v, ValBool, -1);
            if (nVal != -1)
                allowMultiSrc = nVal;
        }
        foreach (var (id, text) in ParamSounds)
        {
            var s = _d.Sound[id];
            v = GetValue(text);
            SoundFromTemplate(s, id);
            SoundFill(s, 0);
            ParseSound(v, s);
            if (s.Once == 1 && allowMultiSrc > 0)
                s.Once = 2;
        }
    }

    void SetupDamage()
    {
        Array.Clear(_d.DamageId);
        _d.DamageIgnoreSources = 0;
        _d.DmgWaterAmount = _d.DmgLandAmount = 65535;       // the wrong medium kills
        _d.DmgStompAmount = 0;
        foreach (var t in new[] { _d.DmgWaterDelay, _d.DmgLandDelay, _d.DmgStompDelay })
            t.Time = t.Rng0 = t.Rng1 = t.Mul = 0;
        DamageSetDefault();
        if (GroupExist(_group))
        {
            string v;
            var data = new int[2];
            if ((v = GetValue("IgnoreDamageFrom")) != null)
                _d.DamageIgnoreSources = ParseKeywords(v, ParamDamageSource);
            if ((v = GetValue("StompDamage")) != null)
            {
                int n = ParseIDs(v, ValUfix, data, 2);
                if (n == 2)
                {
                    _d.DmgStompDelay.Rng0 = data[1];
                    _d.DmgStompDelay.Rng1 = 0;
                }
                if (n >= 1)
                    _d.DmgStompAmount = Math.Min(data[0] << 2, 65535);
            }
            // (NBlood names both medium parameters the land one: MediumWaterDamage and MediumLandDamage set the same)
            foreach (string name in new[] { "MediumWaterDamage", "MediumLandDamage" })
            {
                if ((v = GetValue(name)) == null)
                    continue;
                int n = ParseIDs(v, ValUfix, data, 2);
                if (n == 2)
                {
                    _d.DmgLandDelay.Rng0 = data[1];
                    _d.DmgLandDelay.Rng1 = 0;
                }
                if (n >= 1)
                    _d.DmgLandAmount = Math.Min(data[0] << 2, 65535);
            }
            foreach (var (id, text) in ParamDamage)
            {
                if ((v = GetValue(text)) == null)
                    continue;
                int t = _d.DamageId[id];
                int nVal = CheckValue(v, ValPerc, 0, Cd.MaxDmgScale, 100);
                _d.DamageSet(Cd.Perc2Val(nVal, t), id);
            }
        }
        DamageScaleToSkill(_w.Skill);
    }

    int ParseOnEventDmgEx(string str, CdEventDamage o)
    {
        int nArrType = ArrayType(str);
        if (nArrType == 1)
        {
            // the old plain form: amount, chance, state time
            var old = new int[3];
            for (int i = 0; i < 3 && EnumVal(str, i, out string key); i++)
            {
                old[i] = i switch
                {
                    0 => CheckValue(key, ValUfix, 0, 16384) << 4,
                    1 => Cd.Perc2Val(CheckValue(key, ValPerc, 0, 100), Cd.ChanceMax),
                    _ => CheckValue(key, ValUfix, 0, 2048),
                };
            }
            o.HitCount = 1;
            o.Amount = old[0];
            o.Chance = old[1];
            o.StateTime0 = old[2];
            o.StateTime1 = 0;
            return 1;
        }
        if (nArrType == 2)
        {
            o.HitCount = 0;
            int i = 0;
            while ((i = EnumKV(str, i, out string key, out string val)) != 0)
            {
                switch (key.ToLowerInvariant())
                {
                    case "amount": o.Amount = CheckValue(val, ValUfix, 0, 16384) << 4; break;
                    case "chance": o.Chance = Cd.Perc2Val(CheckValue(val, ValPerc, 0, 100), Cd.ChanceMax); break;
                    case "health": o.Health = CheckValue(val, ValPerc, 0, 255); break;
                    case "cumulative": o.Cumulative = CheckValue(val, ValBool, 0, 1, 1) != 0; break;
                    case "counthits": o.HitCount = CheckValue(val, ValBool, 0, 1, 1); break;
                    case "cooldown": ParseTimer(val, o.Cooldown); break;
                    case "statetime":
                    {
                        int a = 0, b = 32767;
                        int n = ParseRange(val, ValUfix, ref a, ref b);
                        if (n >= 1)
                        {
                            o.StateTime0 = a & 0xffff;
                            o.StateTime1 = n == 2 ? b & 0xffff : 0;
                        }
                        break;
                    }
                }
            }
            return 2;
        }
        Warning($"The value \"{str}\" is not a correct array value");
        return 0;
    }

    void SetupEventDamage(CdOnDamage on)
    {
        string v = GetValue("OnDamage");
        if (v != null && ParseOnEventDmgEx(v, on.OnDamage[0]) != 0)
        {
            for (int i = K.kDamageMax - 1; i > 0; i--)
                on.OnDamage[i].CopyFrom(on.OnDamage[0]);
        }
        foreach (var (id, text) in ParamDamage)
            if ((v = GetValue("OnDamage" + text)) != null)
                ParseOnEventDmgEx(v, on.OnDamage[id]);
    }

    void SetupOnDamageGroup(CdOnDamage on)
    {
        EventDefaults(on);
        if (GroupExist(_group))
            SetupEventDamage(on);
    }

    void SetupDodge()
    {
        EventDefaults(_d.Dodge);
        _d.DodgeZDir = 0;
        _d.DodgeOnAimMissChance = Cd.ChanceMax;
        if (!GroupExist(_group))
            return;
        SetupEventDamage(_d.Dodge);
        string v = GetValue("OnAimMiss");
        if (v != null)
            _d.DodgeOnAimMissChance = Cd.Perc2Val(CheckValue(v, ValPerc, 0, 100), Cd.ChanceMax);
    }

    void SetupLastStand()
    {
        EventDefaults(_d.LStand);
        Array.Clear(_d.LStandAnim);
        _d.LStandWakeChance = Cd.ChanceMax >> 1;
        _d.LStandWakeHealth = 0;
        if (!GroupExist(_group))
            return;
        SetupEventDamage(_d.LStand);
        string v = GetValue("Wake");
        if (!IsEmpty(v))
        {
            if (IsPerc(v))
                _d.LStandWakeChance = Cd.Perc2Val(CheckValue(v, ValPerc, 0, 100, 50), Cd.ChanceMax);
            else if (ArrayType(v) == 2)
            {
                int i = 0;
                while ((i = EnumKV(v, i, out string key, out string val)) != 0)
                {
                    if (key.Equals("Chance", StringComparison.OrdinalIgnoreCase))
                        _d.LStandWakeChance = Cd.Perc2Val(CheckValue(val, ValPerc, 0, 100, 50), Cd.ChanceMax);
                    else if (key.Equals("Health", StringComparison.OrdinalIgnoreCase))
                        _d.LStandWakeHealth = CheckValue(val, ValPerc, 0, 255, 1);
                }
            }
        }
        // its animations, from the Animation group
        if (GroupExist("Animation"))
        {
            string og = _group;
            _group = "Animation";
            foreach (var (id, text) in ParamAnim)
            {
                if (id < Cd.AnimLStandEnter || id > Cd.AnimLStandExit || (v = GetValue(text)) == null)
                    continue;
                var seq = new int[Cd.PostureMax];
                ParseAnimation(v, seq, true);
                for (int i = 0; i < Cd.PostureMax; i++)
                    _d.LStandAnim[id - Cd.AnimLStandEnter, i] = seq[i];
            }
            _group = og;
        }
    }

    void SetupCrouch()
    {
        EventDefaults(_d.Crouch);
        _d.TimerCrouch.Rng0 = 32;
        _d.TimerCrouch.Rng1 = 0;
        if (GroupExist(_group))
            SetupEventDamage(_d.Crouch);
    }

    bool ParseWeaponBasicInfo(string str, CdWeapon wp)
    {
        bool isNum = IsUfix(str);
        int nID = 0;
        if (IsEmpty(str))
            isNum = true;
        else if (isNum)
            nID = CheckValue(str, ValUfix, 0);
        foreach (var info in WeaponInfo)
        {
            if (isNum)
            {
                // the old way: the id itself
                if (nID != 0 && !(nID >= info.lo && nID < info.hi))
                    continue;
                wp.Id = nID;
            }
            else if (info.hi == info.lo)
            {
                if (!string.Equals(str, info.a, StringComparison.OrdinalIgnoreCase))
                    continue;
                wp.Id = info.lo;
            }
            else if ((info.b != null && IsIdKeyword(str, info.a, info.b, out nID)) || IsIdKeyword(str, info.a, out nID))
                wp.Id = info.lo + CheckRange(str, nID, 1, info.hi - info.lo) - 1;
            else
                continue;
            wp.SpawnsSprite = info.spawns;
            wp.ClipMask = info.mask;
            wp.Type = info.type;
            return true;
        }
        return false;
    }

    void ParseShotSetup(string str, CdWeapon wp)
    {
        int i = 0;
        while ((i = EnumKV(str, i, out string key, out string val)) != 0)
        {
            switch (key.ToLowerInvariant())
            {
                case "offset":
                    ParseOffsets(val, out wp.ShotOffX, out wp.ShotOffY, out wp.ShotOffZ);
                    break;
                case "velocity":
                    wp.ShotVelocity = CheckValue(val, ValFix, NoVal);
                    if (wp.ShotVelocity != NoVal)
                        wp.ShotVelocity <<= 10;
                    break;
                case "slope":
                    wp.ShotSlope = CheckValue(val, ValFix, NoVal);
                    if (wp.ShotSlope != NoVal)
                        wp.ShotSlope <<= 4;
                    break;
                case "followangle":
                    wp.ShotTargetFollow = Math.Min(CheckValue(val, ValUfix, 0, K.kAng360), K.kAng360 - 1);
                    break;
                case "clipdist":
                    wp.ShotClipdist = CheckValue(val, ValUfix, 0, 255, 32);
                    break;
                case "impact":
                    wp.ShotImpact = CheckValue(val, ValBool, 0) != 0;
                    break;
                case "livetime":
                    wp.ShotRemTime = CheckValue(val, ValUfix, 0, 4095);
                    break;
            }
        }
    }

    void ParseAttackSetup(string str, CdWeapon wp)
    {
        int i = 0;
        while ((i = EnumKV(str, i, out string key, out string val)) != 0)
        {
            switch (key.ToLowerInvariant())
            {
                case "timer":
                {
                    int nVal = CheckValue(val, ValUfix, 0, 32767);
                    for (int j = 0; j < Cd.PostureMax; j++)
                        _d.States[wp.StateID, j].Ticks = nVal;
                    break;
                }
                case "interruptable": wp.Interruptable = CheckValue(val, ValBool, 0) != 0; break;
                case "turntotarget": wp.TurnToTarget = CheckValue(val, ValBool, 0) != 0; break;
                case "numshots": wp.NumShots = CheckValue(val, ValUfix, 1, 255); break;
                case "inertia": wp.Inertia = CheckValue(val, ValBool, 0) != 0; break;
                case "immune": wp.Locked = CheckValue(val, ValBool, 0) != 0; break;
                case "prediction":
                    wp.PredAccuracy = 0x1AAAAA;
                    wp.PredDistance = 0;
                    wp.PredAngle = K.kAng15;
                    for (int k = 0; EnumVal(val, k, out string buf); k++)
                    {
                        if (k == 0)
                            wp.PredDistance = CheckValue(buf, ValUfix, 0, 65535) << 3;
                        else if (k == 1)
                        {
                            wp.PredAngle = CheckValue(buf, ValUfix, 0, K.kAng360);
                            if (wp.PredAngle == 0)
                                wp.PredAngle = K.kAng360;
                        }
                        else if (k == 2)
                            wp.PredAccuracy = Cd.Perc2Val(CheckValue(buf, ValUfix, 1, 1000, 100), 0x1AAAAA);
                    }
                    break;
            }
        }
    }

    void ParseWeaponStyle(string str, CdWeapon wp)
    {
        wp.StyleAngle = wp.StyleOffX = wp.StyleOffY = wp.StyleOffZ = 0;
        int i = 0;
        while ((i = EnumKV(str, i, out string key, out string val)) != 0)
        {
            if (key.Equals("Offset", StringComparison.OrdinalIgnoreCase))
                ParseOffsets(val, out wp.StyleOffX, out wp.StyleOffY, out wp.StyleOffZ);
            else if (key.Equals("Angle", StringComparison.OrdinalIgnoreCase))
                wp.StyleAngle = CheckValue(val, ValUfix, 0, K.kAng360);
        }
        wp.StyleAvailable = true;
    }

    void ParseWeaponHeight(string str, CdWeapon wp)
    {
        if (IsEmpty(str))
            return;
        int a, b;
        if (ArrayType(str) == 2)
        {
            int found = 0, i = 0;
            while ((i = EnumKV(str, i, out string key, out string val)) != 0)
            {
                a = int.MinValue;
                b = int.MaxValue;
                if (key.Equals("Above", StringComparison.OrdinalIgnoreCase))
                {
                    int n = ParseRange(val, ValUfix, ref a, ref b);
                    if (n == 2) { wp.HeighRange[0] = -b; wp.HeighRange[1] = -a; }
                    else if (n == 1) { wp.HeighRange[0] = -a; wp.HeighRange[1] = 0; }
                    found |= 1;
                }
                else if (key.Equals("Below", StringComparison.OrdinalIgnoreCase))
                {
                    int n = ParseRange(val, ValUfix, ref a, ref b);
                    if (n == 2) { wp.HeighRange[2] = a; wp.HeighRange[3] = b; }
                    else if (n == 1) { wp.HeighRange[2] = 0; wp.HeighRange[3] = a; }
                    found |= 2;
                }
            }
            if ((found & 1) == 0) wp.HeighRange[0] = wp.HeighRange[1] = int.MinValue;
            if ((found & 2) == 0) wp.HeighRange[2] = wp.HeighRange[3] = int.MaxValue;
        }
        else
        {
            a = int.MinValue;
            b = int.MaxValue;
            int n = ParseRange(str, ValUfix, ref a, ref b);
            if (n == 2) { wp.HeighRange[0] = -b; wp.HeighRange[1] = -a; wp.HeighRange[2] = a; wp.HeighRange[3] = b; }
            else if (n == 1) { wp.HeighRange[0] = -a; wp.HeighRange[1] = 0; wp.HeighRange[2] = 0; wp.HeighRange[3] = a; }
            else return;
        }
        for (int i = 0; i < 4; i++)
            if (wp.HeighRange[i] > int.MinValue && wp.HeighRange[i] < int.MaxValue)
                wp.HeighRange[i] <<= 4;
    }

    void ParseWeaponCooldown(string str, CdWeapon wp)
    {
        if (IsEmpty(str))
            return;
        if (ArrayType(str) == 2)
        {
            int i = 0;
            while ((i = EnumKV(str, i, out string key, out string val)) != 0)
            {
                if (key.Equals("Timer", StringComparison.OrdinalIgnoreCase))
                    ParseTimer(val, wp.CooldownDelay);
                else if (key.Equals("UseCount", StringComparison.OrdinalIgnoreCase))
                {
                    int a = 1, b = 16384;
                    int n = ParseRange(val, ValUfix, ref a, ref b);
                    if (n >= 1)
                    {
                        wp.CooldownRng0 = a & 0xffff;
                        wp.CooldownRng1 = n == 2 ? b & 0xffff : 0;
                    }
                    wp.CooldownTotalUseCount = wp.CooldownRng0;
                }
            }
            return;
        }
        var data = new int[2];
        int cnt = ParseIDs(str, ValUfix, data, 2);
        if (cnt >= 1)
        {
            wp.CooldownDelay.Rng0 = CheckRange("Cooldown", data[0], 1, 32767);
            wp.CooldownDelay.Rng1 = 0;
        }
        if (cnt == 2)
        {
            wp.CooldownRng0 = wp.CooldownTotalUseCount = CheckRange("Cooldown", data[1], 1, 16384);
            wp.CooldownRng1 = 0;
        }
    }

    void SetupWeapons()
    {
        _d.NumWeapons = 0;
        var data = new int[32];
        for (int i = 0; i < Cd.MaxWeapons; i++)
        {
            var wp = _d.Weapons[_d.NumWeapons] = new CdWeapon();
            string grp = $"Weapon{(i + 1).ToString(CultureInfo.InvariantCulture)}";
            if (!GroupExist(grp))
                continue;
            _group = grp;
            string v = GetValue("Skill");
            if (v != null && !ParseSkill(v))
                continue;
            wp.Group = i + 1;
            _d.NumWeapons++;
            // (in gParamWeapon's order: the weapon's kind first)
            v = GetValue("Type");
            if (!ParseWeaponBasicInfo(v, wp))
                Warning($"\"{v}\" is not a valid value of \"Type\"");
            {
                int a = 0, b = 0x10000;
                if (ParseRange(GetValue("Distance"), ValUfix, ref a, ref b) == 2)
                {
                    wp.DistRange[0] = a << 3;
                    wp.DistRange[1] = b << 3;
                }
                else
                {
                    Warning("Distance is not an array of two values");
                    wp.DistRange[0] = 0;
                    wp.DistRange[1] = 32767;
                }
            }
            if ((v = GetValue("Dispersion")) != null)
            {
                int a = 0, b = 1024;
                int n = ParseRange(v, ValPerc, ref a, ref b, Cd.MaxDispersion);
                if (n == 2) { wp.Dispersion[0] = a; wp.Dispersion[1] = b; }
                else if (n == 1) { wp.Dispersion[0] = a; wp.Dispersion[1] = a >> 1; }
                else Warning($"\"{v}\" is not a valid value of \"Dispersion\"");
            }
            wp.Angle = CheckValue(GetValue("AimAngle"), ValUfix, 0, K.kAng360, K.kAng15);
            if ((v = GetValue("Medium")) != null)
            {
                int nVal = ParseMedium(v);
                if (nVal >= 0) wp.Medium = nVal;
                else Warning($"\"{v}\" is not a valid value of \"Medium\"");
            }
            if ((v = GetValue("Ammo")) != null)
            {
                wp.AmmoTotal = CheckValue(v, ValUfix, 0, 32767);
                wp.AmmoSet(wp.AmmoTotal);
            }
            wp.PickChance = Cd.Perc2Val(CheckValue(GetValue("PickChance"), ValPerc, 0, 100, 100), Cd.ChanceMax);
            ParseAppearance(GetValue("ShotAppearance"), wp.ShotAppearance);
            if (!ParseSound(GetValue("ShotSound"), wp.ShotSound))
                WeaponSoundSetDefault(wp);
            // the attack's state and animation
            wp.StateID = Cd.StateAttackBase + _d.NumWeapons - 1;
            wp.NextStateID = wp.StateID;
            SetupAnimationOf(wp.StateID, GetValue("AttackAnimation"), true);
            EnsureTrigger(wp.StateID);
            foreach (string name in new[] { "ShotSetup", "AttackSetup" })
            {
                if ((v = GetValue(name)) == null)
                    continue;
                if (!IsArray(v))
                {
                    Warning($"The value \"{v}\" is not a correct array value");
                    continue;
                }
                if (name == "ShotSetup") ParseShotSetup(v, wp);
                else ParseAttackSetup(v, wp);
            }
            wp.Posture = ParsePosture(GetValue("Posture"));
            foreach (string name in new[] { "TargetHealth", "DudeHealth" })
            {
                if ((v = GetValue(name)) == null)
                    continue;
                int a = 0, b = 200;
                if (ParseRange(v, ValPerc, ref a, ref b) == 2)
                {
                    var r = name == "DudeHealth" ? wp.DudeHpRange : wp.TargHpRange;
                    r[0] = a;
                    r[1] = b;
                }
                else
                    Warning($"The value \"{v}\" is not a correct array value");
            }
            if ((v = GetValue("Akimbo")) != null)
                wp.SharedId = CheckValue(v, ValUfix, 0, Cd.MaxWeapons);
            ParseWeaponCooldown(GetValue("Cooldown"), wp);
            if ((v = GetValue("Style")) != null && IsArray(v))
                ParseWeaponStyle(v, wp);
            {
                int a = int.MinValue, b = int.MaxValue;
                int n = ParseRange(GetValue("AimSlope"), ValFix, ref a, ref b);
                if (n == 1) { wp.SlopeRange[0] = -(a << 3); wp.SlopeRange[1] = Math.Abs(a << 3); }
                else if (n == 2) { wp.SlopeRange[0] = a << 3; wp.SlopeRange[1] = b << 3; }
                else { wp.SlopeRange[0] = int.MinValue; wp.SlopeRange[1] = int.MaxValue; }
            }
            if ((v = GetValue("AkimboAnimFrame")) != null)
            {
                wp.Frames = new List<int>();
                ParseIDs(v, ValUfix, wp.Frames);
                for (int k = 0; k < wp.Frames.Count; k++)
                    wp.Frames[k]++;
            }
            ParseWeaponHeight(GetValue("Height"), wp);
            Array.Clear(data);
            if (ParseIDs(GetValue("Data"), ValFix, data, data.Length) > 0)
            {
                wp.Data1 = data[0];
                wp.Data2 = data[1];
                wp.Data3 = data[2];
                wp.Data4 = data[3];
                wp.Data5 = data[4];
            }
            if (ParseSound(GetValue("AttackSound"), wp.AttackSound))
            {
                wp.AttackSound.Ai = false;
                wp.AttackSound.Once = 1;
                wp.AttackSound.Interruptable = true;
            }
            if ((v = GetValue("Default")) != null)
                wp.IsDefault = CheckValue(v, ValBool, 0, 1, 0) != 0;
            {
                int a = 0, b = 32767;
                int n = ParseRange(GetValue("MaxObjects"), ValUfix, ref a, ref b);
                if (n == 2) { wp.MaxUnitsThis = a; wp.MaxUnitsShared = b; }
                else if (n == 1) { wp.MaxUnitsThis = a; wp.MaxUnitsShared = 0; }
            }
            if ((v = GetValue("TargetBurnTime")) != null)
            {
                int a = 0, b = 255;
                int n = ParseRange(v, ValUfix, ref a, ref b);
                if (n == 1) { wp.TargBurnRange[0] = 0; wp.TargBurnRange[1] = a; }
                else if (n == 2) { wp.TargBurnRange[0] = a; wp.TargBurnRange[1] = b; }
            }
            if ((v = GetValue("RealTimeCheck")) != null)
                wp.RealCheck = Btoi(v) == 1;
        }
        _group = "Weapon";
        // the weapon picked least comes first
        if (_sortWeapons && _d.NumWeapons > 1)
        {
            // (a stable sort over the weapons there are; the states keep their ids)
            var list = new List<CdWeapon>();
            for (int i = 0; i < _d.NumWeapons; i++)
                list.Add(_d.Weapons[i]);
            list.Sort((x, y) => x.PickChance.CompareTo(y.PickChance));
            for (int i = 0; i < list.Count; i++)
                _d.Weapons[i] = list[i];
        }
        for (int i = _d.NumWeapons; i < Cd.MaxWeapons; i++)
            _d.Weapons[i] ??= new CdWeapon();
    }

    bool ParseAppearance(string str, CdAppearance a)
    {
        a.Clear();
        if (IsEmpty(str))
        {
            a.Available = false;
            return false;
        }
        int i = 0;
        var range = new int[2];
        while ((i = EnumKV(str, i, out string key, out string val)) != 0)
        {
            int nVal;
            string k = key.ToLowerInvariant();
            bool inherit = val.Equals("Inherit", StringComparison.OrdinalIgnoreCase);
            switch (k)
            {
                case "callback":
                case "effect":
                {
                    int v = 0;
                    if (IsIdKeyword(val, "FX", out nVal)) v = Cd.FXEffectBase + nVal;
                    else if (IsIdKeyword(val, "GIB", out nVal)) v = Cd.GIBEffectBase + nVal;
                    else if (IsUfix(val)) v = CheckValue(val, ValUfix, 0, Cd.CustomCallback.Length);
                    else Warning($"The value \"{val}\" is not a correct value type");
                    if (k == "callback") a.Clb = v;
                    else a.Eff = v;
                    break;
                }
                case "seq":
                    a.Seq = CheckValue(val, ValUfix, 0, 65535);
                    break;
                case "sound":
                    a.SoundAvailable = ParseSound(val, a.Sound);
                    a.Sound.Once = 1;
                    break;
                case "tile":
                    a.Pic = CheckValue(val, ValUfix, 1, 30463, 0);
                    break;
                case "shade":
                    a.Shd = inherit ? -129 : CheckValue(val, ValFix, -128, 64, 128);
                    break;
                case "pal":
                    a.Pal = inherit ? -129 : CheckValue(val, ValUfix, 0, 255);
                    break;
                case "size":
                    if (inherit)
                    {
                        a.Xrp = a.Yrp = -129;
                        break;
                    }
                    switch (ParseIDs(val, ValUfix, range, 2))
                    {
                        case 1: a.Xrp = a.Yrp = CheckRange(val, range[0], 0, 255); break;
                        case 2: a.Xrp = CheckRange(val, range[0], 0, 255); a.Yrp = CheckRange(val, range[1], 0, 255); break;
                    }
                    break;
                case "scale":
                    if (inherit)
                    {
                        a.Scl0 = a.Scl1 = 1024;
                        break;
                    }
                    {
                        int r0 = 0, r1 = 1024;
                        int n = ParseRange(val, ValPerc, ref r0, ref r1, Cd.DefaultAnimScale);
                        if (n == 2) { a.Scl0 = r0; a.Scl1 = r1; }
                        else if (n == 1) { a.Scl0 = r0; a.Scl1 = 0; }
                    }
                    break;
                case "offset":
                    switch (ParseIDs(val, ValFix, range, 2))
                    {
                        case 1: a.Xof = a.Yof = CheckRange(val, range[0], -128, 127); break;
                        case 2: a.Xof = CheckRange(val, range[0], -128, 127); a.Yof = CheckRange(val, range[1], -128, 127); break;
                    }
                    break;
                case "cstat":
                    switch (ParseIDs(val, ValUfix, range, 2))
                    {
                        case 2: a.Csta = range[0]; a.Cstr = range[1]; break;
                        case 1: a.Csta = range[0]; a.Cstr = 0; break;
                        default: Warning($"The value \"{val}\" is not a correct array value"); break;
                    }
                    break;
            }
        }
        a.Available = true;
        return true;
    }

    int ParseEffectIDs(string str, string paramName, int[] o)
    {
        int nMax = o.Length;
        if (IsArray(str, out int nLen) && nMax < nLen)
        {
            Warning($"Invalid array length! Expected: 1 - {nMax}. Given: {nLen}");
            return 0;
        }
        int i = 0;
        while (i < nMax && EnumVal(str, i, out string tmp))
        {
            if (IsIdKeyword(tmp, "FX", out int nVal))
            {
                o[i] = nVal >= Cd.FXEffectCallbackBase
                    ? CheckRange(tmp, nVal, Cd.FXEffectCallbackBase, Cd.FXEffectCallbackBase + Cd.CustomCallback.Length - 1)
                    : CheckRange(tmp, nVal, 1, 56) + Cd.FXEffectBase;
            }
            else if (IsIdKeyword(tmp, "GIB", out nVal))
                o[i] = CheckRange(tmp, nVal, 1, 31) + Cd.GIBEffectBase - 1;
            else
            {
                Warning($"\"{tmp}\" is not a valid value of \"{paramName}\"");
                i++;
                continue;
            }
            for (int j = i; j < nMax; j++)
                o[j] = o[i];
            i++;
        }
        return i;
    }

    void ParseStatesToList(string str, List<int> o)
    {
        void Add(int v)
        {
            if (!o.Contains(v))
                o.Add(v);
        }
        for (int i = 0; EnumVal(str, i, out string tmp); i++)
        {
            int nPar = FindParam(tmp, ParamAnim);
            if (nPar == Cd.StateMove)
            {
                for (int j = Cd.StateMoveBase; j < Cd.StateMoveMax; j++)
                    Add(j);
                Add(nPar);
                continue;
            }
            if (nPar == Cd.StateDeath)
            {
                for (int j = Cd.StateDeathBase; j < Cd.StateDeathMax; j++)
                    Add(j);
                Add(nPar);
                continue;
            }
            if (nPar == Cd.StateAttack)
            {
                for (int j = 0; j < _d.NumWeapons; j++)
                    if (_d.Weapons[j].Group != 0)
                    {
                        Add(_d.Weapons[j].StateID);
                        break;
                    }
                Add(nPar);
                continue;
            }
            if (nPar == Cd.StateIdle)
            {
                Add(Cd.StateGenIdle);
                Add(nPar);
                continue;
            }
            if (nPar >= 0 && nPar < Cd.StateMax)
            {
                Add(nPar);
                continue;
            }
            if (IsIdKeyword(tmp, "WEAPON", out int nVal))
            {
                if (nVal >= 1 && nVal <= Cd.MaxWeapons)
                {
                    int j = _d.NumWeapons;
                    while (--j >= 0)
                        if (_d.Weapons[j].Group == nVal)
                        {
                            Add(_d.Weapons[j].StateID);
                            break;
                        }
                    // a weapon this skill has not: a state no dude is in, so its effects don't spawn
                    if (j < 0 && o.Count == 0)
                        Add(Cd.StateMax);
                }
                else
                    Warning($"Correct range of \"{tmp}\" is 1 - {Cd.MaxWeapons}");
                continue;
            }
            Warning($"\"{tmp}\" is not a valid value of \"{str}\"");
        }
    }

    void ParseGibSetup(string str, CdGib g)
    {
        g.Available = false;
        g.Force = true;
        g.TrFlags = 0;
        g.Physics = K.kPhysMove | K.kPhysGravity | K.kPhysFalling;
        g.ThingType = K.kThingObjectExplode - K.kThingBase;
        g.Data1 = g.Data2 = g.Data3 = g.Data4 = 0;
        if (IsEmpty(str))
            return;
        var data = new int[3];
        int i = 0;
        while ((i = EnumKV(str, i, out string key, out string val)) != 0)
        {
            switch (key.ToLowerInvariant())
            {
                case "type":
                    if (ParseIDs(val, 101, data, data.Length) > 0)
                    {
                        g.Data1 = data[0];
                        g.Data2 = data[1];
                        g.Data3 = data[2];
                    }
                    break;
                case "soundid": g.Data4 = CheckValue(val, ValUfix, 0, 65535); break;
                case "flags": g.TrFlags = ParseKeywords(val, ParamTriggerFlags); break;
                case "physics": g.Physics = ParseKeywords(val, ParamPhysics); break;
                case "force": g.Force = CheckValue(val, ValBool, 0) != 0; break;
            }
        }
        g.Available = true;
    }

    void SetupEffect()
    {
        _d.NumEffects = 0;
        for (int i = 0; i < Cd.MaxEffectGroups; i++)
        {
            var eff = _d.Effects[_d.NumEffects] = new CdEffect();
            string grp = $"FXEffect{(i + 1).ToString(CultureInfo.InvariantCulture)}";
            if (!GroupExist(grp))
                continue;
            _group = grp;
            string v = GetValue("Type");
            if (v == null || ParseEffectIDs(v, "Type", eff.Id) <= 0)
                Warning("Required parameter \"Type\" not found");
            else
            {
                if ((v = GetValue("Delay")) != null) ParseTimer(v, eff.Delay);
                if ((v = GetValue("Offset")) != null) ParseOffsets(v, out eff.OffX, out eff.OffY, out eff.OffZ);
                if ((v = GetValue("Appearance")) != null) ParseAppearance(v, eff.Appearance);
                if ((v = GetValue("OnState")) != null) ParseStatesToList(v, eff.States);
                if ((v = GetValue("AnimFrame")) != null) ParseIDs(v, ValUfix, eff.Frames);
                if ((v = GetValue("Velocity")) != null) eff.Velocity = CheckValue(v, ValUfix, 0) << 10;
                if ((v = GetValue("Angle")) != null) eff.Angle = CheckValue(v, ValFix, -(K.kAng360 - 1), K.kAng360 - 1, 0);
                if ((v = GetValue("VelocitySlope")) != null) eff.VelocitySlope = CheckValue(v, ValFix, 0) << 4;
                if ((v = GetValue("Posture")) != null) eff.Posture = ParsePosture(v);
                if ((v = GetValue("Medium")) != null)
                {
                    int nVal = ParseMedium(v);
                    if (nVal >= 0) eff.Medium = nVal;
                    else Warning($"\"{v}\" is not a valid value of \"Medium\"");
                }
                if ((v = GetValue("LiveTime")) != null) eff.LiveTime = CheckValue(v, ValFix, -1, 65535);
                if ((v = GetValue("SpawnChance")) != null) eff.Chance = Cd.Perc2Val(CheckValue(v, ValPerc, 0, 100, 100), Cd.ChanceMax);
                if ((v = GetValue("SpawnAll")) != null) eff.AllUnique = CheckValue(v, ValBool, 0) != 0;
                if ((v = GetValue("OnAnim")) != null) ParseIDs(v, ValUfix, eff.Anims);
                if ((v = GetValue("AddSourceVelocity")) != null) eff.SrcVelocity = CheckValue(v, ValBool, 1) != 0;
                if ((v = GetValue("GibSetup")) != null) ParseGibSetup(v, eff.Spr2Gib);
                if ((v = GetValue("SourceHealth")) != null)
                {
                    int a = 0, b = 200;
                    int n = ParseRange(v, ValPerc, ref a, ref b);
                    if (n == 2) { eff.HpRange0 = a; eff.HpRange1 = b; }
                    else if (n == 1) { eff.HpRange0 = 0; eff.HpRange1 = a; }
                    else Warning($"The value \"{v}\" is not a correct array value");
                }
            }
            _d.NumEffects++;
        }
        _group = "FXEffect";
    }

    void SetupMovePattern()
    {
        _d.FallHeight = int.MaxValue;
        _d.TurnAng = K.kAng60;
        _d.StopMoveOnTurn = false;
        string v;
        if ((v = GetValue("FallHeight")) != null)
        {
            int nVal = CheckValue(v, ValUfix, NoVal);
            if (nVal != NoVal)
                _d.FallHeight = nVal << 4;
        }
        if ((v = GetValue("TurnAngle")) != null) _d.TurnAng = CheckValue(v, ValUfix, 0, K.kAng360, K.kAng60);
        if ((v = GetValue("StopMoveOnTurn")) != null) _d.StopMoveOnTurn = CheckValue(v, ValBool, 0) != 0;
        if ((v = GetValue("MoveDirTimer")) != null) ParseTimer(v, _d.TimerMoveDir);
    }

    void ParseFlyType(string str, CdFlyType o, CdTimer timer)
    {
        Array.Clear(o.Distance);
        o.Chance = 0;
        if (IsEmpty(str))
            return;
        int i = 0;
        while ((i = EnumKV(str, i, out string key, out string val)) != 0)
        {
            switch (key.ToLowerInvariant())
            {
                case "distance":
                    switch (ParseIDs(val, ValUfix, o.Distance, 3))
                    {
                        case 1:
                            o.Distance[1] = o.Distance[0] << 3;
                            o.Distance[0] = 0;
                            o.Distance[2] = int.MaxValue;
                            break;
                        case 2:
                            o.Distance[0] <<= 3;
                            o.Distance[1] <<= 3;
                            o.Distance[2] = int.MaxValue;
                            break;
                        case 3:
                            o.Distance[0] <<= 3;
                            o.Distance[1] <<= 3;
                            o.Distance[2] <<= 4;
                            break;
                    }
                    break;
                case "time":
                    ParseTimer(val, timer);
                    break;
                case "chance":
                    o.Chance = Cd.Perc2Val(CheckValue(val, ValPerc, 0, 100, 100), Cd.ChanceMax);
                    break;
            }
        }
    }

    void SetupFlyPattern()
    {
        _d.FlyType[0].Chance = _d.FlyType[1].Chance = 0;
        Array.Clear(_d.FlyType[0].Distance);
        Array.Clear(_d.FlyType[1].Distance);
        _d.FlyAbsGoalZ = false;
        _d.FlyMustReach = true;
        _d.FlyMaxHeight = int.MaxValue;
        _d.FlyCfDist = Cd.MinCFDist;
        _d.FlyFriction = 0;
        _d.FlyClipDist = 1024;
        _d.FlyBackOnTrackAccel = 0;
        _d.TimerGoalZ.Rng0 = 32;
        string v;
        if ((v = GetValue("LaunchSetup")) != null) ParseFlyType(v, _d.FlyType[Cd.FlyStart], _d.TimerFLaunch);
        if ((v = GetValue("LandSetup")) != null) ParseFlyType(v, _d.FlyType[Cd.FlyLand], _d.TimerFLand);
        if ((v = GetValue("MaxHeight")) != null)
        {
            int nVal = CheckValue(v, ValUfix, NoVal);
            if (nVal != NoVal)
                _d.FlyMaxHeight = nVal << 4;
        }
        if ((v = GetValue("SetHeightTime")) != null) ParseTimer(v, _d.TimerGoalZ);
        if ((v = GetValue("DragCompensation")) != null) _d.FlyFriction = CheckValue(v, ValUfix, 0, 0x100);
        if ((v = GetValue("ClipHeightDistance")) != null) _d.FlyClipDist = CheckValue(v, ValUfix, 0, 65535, 1024) << 3;
        if ((v = GetValue("BackOnTrackAccel")) != null) _d.FlyBackOnTrackAccel = CheckValue(v, ValPerc, -2000, 2000, 0);
        if ((v = GetValue("MinCFHeight")) != null) _d.FlyCfDist = CheckValue(v, ValUfix, 0, 65535, Cd.MinCFDist >> 4) << 4;
        if ((v = GetValue("SetHeightInZ")) != null) _d.FlyAbsGoalZ = CheckValue(v, ValBool, 0) != 0;
        if ((v = GetValue("MustReachHeight")) != null) _d.FlyMustReach = CheckValue(v, ValBool, 1) != 0;
    }

    bool ParseDropItem(string str, out int item, out int perc)
    {
        item = 0;
        perc = 100;
        if (IsArray(str))
        {
            int i = 0;
            while ((i = EnumKV(str, i, out string key, out string val)) != 0)
            {
                switch (key.ToLowerInvariant())
                {
                    case "skill":
                        if (!ParseSkill(val))
                            return false;
                        break;
                    case "chance":
                        perc = CheckValue(val, ValPerc, 0, 100, 100);
                        break;
                    case "type":
                        item = CheckValue(val, ValUfix, K.kItemWeaponBase, K.kDudeBase - 1, 0);
                        if (!(item >= K.kItemWeaponBase && item < K.kItemMax))
                            item = 0;
                        break;
                }
            }
            return item != 0;
        }
        if (!IsEmpty(str))
        {
            item = CheckValue(str, ValUfix, K.kItemWeaponBase, K.kItemMax - 1, 0);
            if (!(item >= K.kItemWeaponBase && item < K.kItemMax))
                item = 0;
            perc = 100;
            return item != 0;
        }
        return false;
    }

    void SetupDropItem()
    {
        string v = GetValue("SpriteDropItemChance");
        if (v != null)
            _d.SprDropItemChance = Cd.Perc2Val(CheckValue(v, ValPerc, 0, 100, 100), Cd.ChanceMax);
        for (int i = 0, c = 0; i < Cd.MaxDropItems; i++)
        {
            v = GetValue($"Item{(i + 1).ToString(CultureInfo.InvariantCulture)}");
            if (v != null && ParseDropItem(v, out int item, out int perc))
            {
                _d.DropItems[c, 0] = item;
                _d.DropItems[c, 1] = perc;
                c++;
            }
        }
    }

    /// <summary>What a dude turns into, as a number: -1 nothing, below -1 a vanilla dude (-(type) - 1), from
    /// MorphCdudeBase a custom dude's descriptor id.</summary>
    public const int MorphCdudeBase = 1000000;

    int ParseDudeType(string str)
    {
        if (IsIdKeyword(str, "VDUD", out int nVal))
            return -(K.kDudeBase + CheckRange("VDUD", nVal, K.kDudeCultistTommy - K.kDudeBase, 254 - K.kDudeBase)) - 1;
        if (IsIdKeyword(str, "CDUD", out nVal))
            return CheckRange("CDUD", nVal, 0, 9999) + MorphCdudeBase;
        return -1;
    }

    bool ParseMorphData(string str, out int o)
    {
        o = -1;
        if (IsArray(str))
        {
            int i = 0;
            while ((i = EnumKV(str, i, out string key, out string val)) != 0)
                if (key.Equals("Skill", StringComparison.OrdinalIgnoreCase) && !ParseSkill(val))
                    return true;        // not on this skill
            i = 0;
            while ((i = EnumKV(str, i, out string key, out string val)) != 0)
                if (key.Equals("Dude", StringComparison.OrdinalIgnoreCase))
                {
                    o = ParseDudeType(val);
                    return true;
                }
            return false;
        }
        o = ParseDudeType(str);
        return true;
    }

    void SetupMorphing()
    {
        _d.MorphTrDeath = false;
        _d.NextDude = -1;
        int nDude = -1;
        // the old parameter of the General group first
        string v = GetValue("General", "MorphTo");
        if (!IsEmpty(v) && ParseMorphData(v, out int nVal))
            nDude = nVal;
        for (int i = 0; i < K.kDamageMax; i++)
            _d.MorphId[i] = nDude;
        if (!GroupExist(_group))
            return;
        v = GetValue("OnDeath");
        if (!IsEmpty(v) && ParseMorphData(v, out nVal))
            for (int i = 0; i < K.kDamageMax; i++)
                _d.MorphId[i] = nVal;
        foreach (var (id, text) in ParamDamage)
        {
            v = GetValue("OnDeath" + text);
            if (!IsEmpty(v) && ParseMorphData(v, out nVal))
                _d.MorphId[id] = nVal;
        }
        v = GetValue("TriggerDeath");
        if (!IsEmpty(v))
            _d.MorphTrDeath = CheckValue(v, ValBool, 0) != 0;
    }

    void SetupV2()
    {
        // (the groups in NBlood's order: the weapons before the effects that name them)
        _group = "General"; SetupGeneral();
        _group = "Velocity"; SetupVelocity();
        _group = "Animation"; SetupAnimation();
        _group = "Sound"; SetupSound();
        _group = "Tweaks";
        {
            _d.ThinkClock = 3;
            _sortWeapons = true;
            string v;
            if ((v = GetValue("ThinkClock")) != null) _d.ThinkClock = CheckValue(v, ValUfix, 0, 255, 3);
            if ((v = GetValue("SortWeapons")) != null) _sortWeapons = CheckValue(v, ValBool, 0, 1, 1) != 0;
        }
        _group = "Weapon"; SetupWeapons();
        _group = "Dodge"; SetupDodge();
        _group = "Recoil"; SetupOnDamageGroup(_d.Recoil);
        _group = "Damage"; SetupDamage();
        _group = "Crouch"; SetupCrouch();
        _group = "Knockout"; SetupOnDamageGroup(_d.Knockout);
        _group = "FXEffect"; SetupEffect();
        _group = "DropItem"; SetupDropItem();
        _group = "MoveSetup"; SetupMovePattern();
        _group = "Flight"; SetupFlyPattern();
        _group = "Morphing"; SetupMorphing();
        _group = "Sleeping";
        {
            _d.SleepDist = Cd.MinSeeDist;
            string v = GetValue("SpotRadius");
            if (v != null)
                _d.SleepDist = CheckValue(v, ValUfix, 0, 65535, _d.SleepDist) << 3;
        }
        _group = "Slaves";
        {
            _d.SlavesKillOnDeath = _d.SlavesNoSetTarget = false;
            string v;
            if ((v = GetValue("NoTargetTrack")) != null) _d.SlavesNoSetTarget = CheckValue(v, ValBool, 0, 1, 0) != 0;
            if ((v = GetValue("DeathOnMasterDeath")) != null) _d.SlavesKillOnDeath = CheckValue(v, ValBool, 0, 1, 0) != 0;
        }
        _group = "Randomness";
        {
            _d.RandStateTime = 20;
            string v;
            if ((v = GetValue("StateTime")) != null) _d.RandStateTime = CheckValue(v, ValPerc, 0, 100, 20);
            if ((v = GetValue("Velocity")) != null) _d.RandVelocity = CheckValue(v, ValPerc, 0, 100, 0);
            if ((v = GetValue("ThinkClock")) != null) _d.RandThinkTime = CheckValue(v, ValPerc, 0, 100, 0);
            if ((v = GetValue("AnimationScale")) != null) _d.RandAnimScale = CheckValue(v, ValPerc, 0, 100, 0);
            if ((v = GetValue("WeaponPickChance")) != null) _d.RandWeapChance = CheckValue(v, ValPerc, 0, 100, 0);
        }
        _group = "LastStand"; SetupLastStand();
        _group = "TargetChange"; SetupOnDamageGroup(_d.ChgTarg);
    }

    // ---------------------------------------------------------------- version 1

    static void WeaponRangeSet(CdWeapon wp, int nMin, int nMax)
    {
        wp.DistRange[0] = Math.Min(nMin, nMax);
        wp.DistRange[1] = Math.Max(nMin, nMax);
    }

    CdWeapon WeaponMeleeSet(int index)
    {
        bool avail = _d.Weapons[index].Available;
        var wp = _d.Weapons[index] = new CdWeapon
        {
            Type = Cd.WeaponHitscan, Id = KM.kVectorGenDudePunch, StateID = Cd.StateAttackBase + 2, Angle = K.kAng90, ClipMask = 1,
            Available = avail,
        };
        WeaponRangeSet(wp, 0, 512);
        EnsureTrigger(wp.StateID);
        return wp;
    }

    void WeaponConvert(int nWeaponID)
    {
        bool avail = _d.Weapons[0].Available;
        var w1 = _d.Weapons[0] = new CdWeapon();
        if (nWeaponID >= 1 && nWeaponID < 23) { w1.Type = Cd.WeaponHitscan; w1.ClipMask = 1; }
        else if (nWeaponID >= K.kDudeBase && nWeaponID < K.kDudeMax) { w1.Type = Cd.WeaponSummon; w1.ClipMask = 2; }
        else if (nWeaponID >= K.kMissileBase && nWeaponID < K.kMissileMax) { w1.Type = Cd.WeaponMissile; w1.ClipMask = 2; }
        else if (nWeaponID >= K.kThingBase && nWeaponID < K.kThingMax) { w1.Type = Cd.WeaponThrow; w1.ClipMask = 2; }
        else if (nWeaponID >= K.kTrapExploder && nWeaponID < K.kTrapExploder + 8) { w1.Type = Cd.WeaponKamikaze; w1.ClipMask = 2; }
        else w1.Type = Cd.WeaponNone;
        w1.Id = nWeaponID;
        w1.Available = avail;
        w1.SpawnsSprite = w1.Type != Cd.WeaponHitscan && w1.Type != Cd.WeaponNone;
        _d.NumWeapons = 1;
        w1.AmmoTotal = 0;
        w1.AmmoSet(0);
        int nDisp = Math.Max(3000 / (_w.Skill + 1) + _w.Random2(500), 500);
        w1.Dispersion[0] = nDisp;
        w1.Dispersion[1] = nDisp >> 1;
        switch (w1.Type)
        {
            case Cd.WeaponNone:
                WeaponRangeSet(w1, 0, Cd.V1MaxAttackDist);
                w1.Angle = K.kAng360;
                break;
            case Cd.WeaponThrow:
                w1.Angle = K.kAng15;
                w1.StateID = Cd.StateAttackBase + 1;
                if (w1.Id == KM.kModernThingEnemyLifeLeech || w1.Id == K.kThingDroppedLifeLeech)
                {
                    w1.ShotSlope = -5000;
                    w1.AmmoTotal = 1;
                    w1.AmmoSet(1);
                    WeaponRangeSet(w1, 3000, 11071);
                }
                else if (w1.Id == KM.kModernThingThrowableRock)
                    WeaponRangeSet(w1, 3000, 11071);
                else
                    WeaponRangeSet(w1, 5000, 12264);
                WeaponMeleeSet(1);
                _d.NumWeapons++;
                break;
            case Cd.WeaponKamikaze:
                WeaponRangeSet(w1, 0, Math.Max(World.ExplodeInfo(w1.Id - K.kTrapExploder, K.EI_radius), 768));
                w1.Angle = K.kAng90;
                break;
            case Cd.WeaponSummon:
                w1.MaxUnitsThis = _w.Skill + 1;
                WeaponRangeSet(w1, 2000, Cd.V1MaxAttackDist);
                w1.ShotOffY = _d.Spr.ClipDist << 4;
                w1.Angle = K.kAng90;
                WeaponMeleeSet(1);
                _d.NumWeapons++;
                break;
            case Cd.WeaponHitscan:
            {
                int maxDist = World.VectorData(w1.Id, K.VI_maxDist);
                WeaponRangeSet(w1, 0, maxDist > 0 ? maxDist : Cd.V1MaxAttackDist);
                if (maxDist > 0 && maxDist <= 2048)
                {
                    w1.Dispersion[0] = w1.Dispersion[1] = 0;
                    w1.Angle = Info.Dude(KM.kDudeModernCustom, Info.DI_periphery);
                }
                else
                    w1.Angle = 56;
                break;
            }
            case Cd.WeaponMissile:
                w1.Angle = 56;
                switch (w1.Id)
                {
                    case K.kMissileFireball:
                    case K.kMissileFireballNapalm:
                    case K.kMissileFireballCerberus:
                    case K.kMissileFireballTchernobog:
                        WeaponRangeSet(w1, 3000, Cd.V1MaxAttackDist);
                        WeaponMeleeSet(1);
                        _d.NumWeapons++;
                        break;
                    case K.kMissileFlareAlt:
                        WeaponRangeSet(w1, 2500, Cd.V1MaxAttackDist);
                        WeaponMeleeSet(1);
                        _d.NumWeapons++;
                        break;
                    case K.kMissileLifeLeechRegular:
                        WeaponRangeSet(w1, 1500, Cd.V1MaxAttackDist);
                        WeaponMeleeSet(1);
                        _d.NumWeapons++;
                        break;
                    case K.kMissileFlameSpray:
                    case K.kMissileFlameHound:
                        WeaponRangeSet(w1, 2000, 3500 + _w.Skill * 400);
                        WeaponMeleeSet(1);
                        _d.NumWeapons++;
                        w1.Medium = 1;
                        break;
                    default:
                        WeaponRangeSet(w1, 0, Cd.V1MaxAttackDist);
                        break;
                }
                break;
        }
    }

    void DamageScaleToWeapon(CdWeapon wp)
    {
        var d = _d;
        if (wp.Type == Cd.WeaponKamikaze)
        {
            d.DamageSet(1024, K.kDamageBurn);
            d.DamageSet(1024, K.kDamageExplode);
            d.DamageSet(1024, K.kDamageTesla);
            return;
        }
        if (wp.Type != Cd.WeaponMissile && wp.Type != Cd.WeaponThrow)
            return;
        switch (wp.Id)
        {
            case K.kMissileButcherKnife:
                d.DamageSet(100, K.kDamageBullet);
                d.DamageSet(32, K.kDamageSpirit);
                break;
            case K.kMissileLifeLeechAltNormal:
            case K.kMissileLifeLeechAltSmall:
            case K.kMissileArcGargoyle:
                d.DamageDec(32, K.kDamageSpirit);
                d.DamageSet(52, K.kDamageTesla);
                break;
            case K.kMissileFlareRegular:
            case K.kMissileFlareAlt:
            case K.kMissileFlameSpray:
            case K.kMissileFlameHound:
            case K.kThingArmedSpray:
            case K.kThingPodFireBall:
            case K.kThingNapalmBall:
                d.DamageSet(32, K.kDamageBurn);
                break;
            case K.kThingDroppedLifeLeech:
            case KM.kModernThingEnemyLifeLeech:
                d.DamageSet(32, K.kDamageSpirit);
                break;
            case K.kMissileFireball:
            case K.kMissileFireballNapalm:
            case K.kMissileFireballCerberus:
            case K.kMissileFireballTchernobog:
                d.DamageSet(50, K.kDamageBurn);
                d.DamageDec(32, K.kDamageExplode);
                d.DamageSet(65, K.kDamageFall);
                break;
            case K.kThingTNTBarrel:
            case K.kThingArmedProxBomb:
            case K.kThingArmedRemoteBomb:
            case K.kThingArmedTNTBundle:
            case K.kThingArmedTNTStick:
            case KM.kModernThingTNTProx:
                d.DamageDec(32, K.kDamageBurn);
                d.DamageDec(32, K.kDamageExplode);
                d.DamageSet(65, K.kDamageFall);
                break;
            case K.kMissileTeslaAlt:
            case K.kMissileTeslaRegular:
                d.DamageSet(32, K.kDamageTesla);
                break;
        }
    }

    void DamageScaleToSurface(int nSurface)
    {
        var d = _d;
        const int F = K.kDamageFall, B = K.kDamageBurn, V = K.kDamageBullet, X = K.kDamageExplode, C = K.kDamageDrown,
            E = K.kDamageTesla;
        switch (nSurface)
        {
            case K.kSurfStone:
                d.DamageSet(0, F); d.DamageDec(200, V); d.DamageDec(100, B); d.DamageDec(80, X); d.DamageInc(30, C); d.DamageInc(20, E);
                break;
            case K.kSurfMetal:
                d.DamageDec(16, F); d.DamageDec(128, V); d.DamageDec(90, B); d.DamageDec(55, X); d.DamageInc(20, C); d.DamageInc(30, E);
                break;
            case K.kSurfWood:
                d.DamageDec(10, V); d.DamageInc(50, B); d.DamageInc(40, X); d.DamageInc(10, C); d.DamageDec(60, E);
                break;
            case K.kSurfWater:
            case K.kSurfDirt:
            case K.kSurfClay:
            case K.kSurfGoo:
                d.DamageSet(8, F); d.DamageDec(20, V); d.DamageDec(200, B); d.DamageDec(60, X); d.DamageSet(0, C); d.DamageInc(40, E);
                break;
            case K.kSurfSnow:
            case K.kSurfIce:
                d.DamageSet(8, F); d.DamageDec(20, V); d.DamageDec(100, B); d.DamageDec(50, X); d.DamageSet(0, C); d.DamageInc(40, E);
                break;
            case K.kSurfLeaves:
            case K.kSurfPlant:
            case K.kSurfCloth:
                d.DamageSet(0, F); d.DamageDec(10, V); d.DamageInc(70, B); d.DamageInc(50, X);
                break;
            case K.kSurfLava:
                d.DamageSet(0, B); d.DamageSet(0, X); d.DamageInc(30, C);
                break;
        }
    }

    void SetupV1()
    {
        var e = _d.Spr;
        int nBaseSeq = e.Data2 <= 0 ? Cd.DefaultSeq : e.Data2;
        AnimationConvert(nBaseSeq);
        WeaponConvert(e.Data1);
        SoundConvert(e.SysData1);
        VelocitySetDefault(146603);
        // mass by its first picture, clipdist by its size
        if (!_d.Initialized)
        {
            int nPic = e.Picnum;
            e.Mass = null;
            e.Picnum = FirstPic();
            _d.Mass = Math.Max(_w.SpriteMassOf(e), 40);
            e.Picnum = nPic;
        }
        e.ClipDist = Math.Clamp((e.XRepeat + e.YRepeat) >> 1, 10, 128);
        // damage, dodge and recoil
        EventClear(_d.Dodge);
        EventClear(_d.Recoil);
        EventClear(_d.Knockout);
        EventClear(_d.Crouch);
        EventClear(_d.LStand);
        EventClear(_d.ChgTarg);
        Array.Clear(_d.LStandAnim);
        _d.DodgeOnAimMissChance = 0;
        var rEvn = _d.Recoil.OnDamage[0];
        rEvn.Cooldown.Rng0 = 1;
        rEvn.Health = 0;
        rEvn.HitCount = 1;
        rEvn.Cumulative = true;
        var dEvn = _d.Dodge.OnDamage[0];
        dEvn.CopyFrom(rEvn);
        var wp = _d.Weapons[0];
        DamageSetDefault();
        DamageScaleToWeapon(wp);
        DamageScaleToSurface(Tiles.SurfType(FirstPic()));
        DamageScaleToSkill(_w.Skill);
        bool isMelee = wp.Type == Cd.WeaponKamikaze || (wp.Type == Cd.WeaponHitscan && wp.GetDistance() <= 2048);
        if (isMelee)
        {
            // no dodging; recoils rarer and only from more damage
            EventClear(_d.Dodge);
            rEvn.Amount = 25;
            rEvn.Chance = 0x0400;
        }
        else
        {
            dEvn.Chance = Math.Clamp((0x6000 / Math.Max(_d.Mass, 1)) << 7, 0, Cd.ChanceMax);
            dEvn.Amount = 10;
            rEvn.Chance = Math.Clamp((0x8000 / Math.Max(_d.Mass, 1)) << 7, 0, Cd.ChanceMax);
            rEvn.Amount = 15;
        }
        for (int i = K.kDamageMax - 1; i > 0; i--)
        {
            _d.Recoil.OnDamage[i].CopyFrom(rEvn);
            _d.Dodge.OnDamage[i].CopyFrom(_d.Dodge.OnDamage[0]);
        }
        if (e.TxID != 0)
            _w.CdSetupIncarnation(_d);
    }

    // ---------------------------------------------------------------- the whole set-up

    /// <summary>The dude's settings copied from another of its kind (SetupFromDude: the template's).</summary>
    public static void CopySettings(CustomDude d, CustomDude src, World w)
    {
        var e = d.Spr;
        d.Version = src.Version;
        d.Mass = src.Mass;
        d.SeeDist = src.SeeDist;
        d.HearDist = src.HearDist;
        d.Periphery = src.Periphery;
        d.SleepDist = src.SleepDist;
        d.Medium = src.Medium;
        d.Posture = src.Posture;
        d.FallHeight = src.FallHeight;
        d.EyeHeight = src.EyeHeight;
        d.NextDude = src.NextDude;
        d.Height = src.Height;
        d.TurnAng = src.TurnAng;
        d.StopMoveOnTurn = src.StopMoveOnTurn;
        d.ThinkClock = src.ThinkClock;
        d.Health = src.Health;
        if (e != null)
        {
            e.ClipDist = src.TemplateClipDist;
            e.XScale = src.TemplateScale;
            if (!d.Initialized)
            {
                if (e.SysData2 == 0)
                    e.Data4 = e.SysData2 = d.Health;
                e.XHealth = e.SysData2 != 0 ? Math.Clamp(e.SysData2 << 4, 0, 65535) : Info.Dude(e.Type, Info.DI_startHealth) << 4;
            }
        }
        for (int i = 0; i < Cd.StateMax; i++)
            for (int j = 0; j < Cd.PostureMax; j++)
            {
                d.States[i, j].Type = src.States[i, j].Type;
                d.States[i, j].Ticks = src.States[i, j].Ticks;
                d.States[i, j].Seq = src.States[i, j].Seq;
            }
        d.NumAvailWeapons = src.NumAvailWeapons;
        d.NumWeapons = src.NumWeapons;
        for (int i = 0; i < Cd.MaxWeapons; i++)
            d.Weapons[i] = CloneWeapon(src.Weapons[i]);
        d.Weapon = d.Weapons[0];
        d.NumEffects = src.NumEffects;
        for (int i = 0; i < Cd.MaxEffectGroups; i++)
            d.Effects[i] = CloneEffect(src.Effects[i]);
        d.SlavesKillOnDeath = src.SlavesKillOnDeath;
        d.SlavesNoSetTarget = src.SlavesNoSetTarget;
        Array.Copy(src.DamageId, d.DamageId, d.DamageId.Length);
        d.DamageIgnoreSources = src.DamageIgnoreSources;
        d.DmgWaterAmount = src.DmgWaterAmount;
        d.DmgLandAmount = src.DmgLandAmount;
        d.DmgStompAmount = src.DmgStompAmount;
        d.DmgWaterDelay.CopyFrom(src.DmgWaterDelay);
        d.DmgLandDelay.CopyFrom(src.DmgLandDelay);
        d.DmgStompDelay.CopyFrom(src.DmgStompDelay);
        Array.Copy(src.VelocityId, d.VelocityId, d.VelocityId.Length);
        Array.Copy(src.VelocityMods, d.VelocityMods, d.VelocityMods.Length);
        d.VelocityModID = src.VelocityModID;
        for (int i = 0; i < Cd.SndMax; i++)
            d.Sound[i].CopyFrom(src.Sound[i]);
        void CopyOn(CdOnDamage a, CdOnDamage b)
        {
            for (int i = 0; i < a.OnDamage.Length; i++)
                a.OnDamage[i].CopyFrom(b.OnDamage[i]);
        }
        CopyOn(d.Dodge, src.Dodge);
        CopyOn(d.Recoil, src.Recoil);
        CopyOn(d.Crouch, src.Crouch);
        CopyOn(d.Knockout, src.Knockout);
        CopyOn(d.LStand, src.LStand);
        CopyOn(d.ChgTarg, src.ChgTarg);
        d.DodgeZDir = src.DodgeZDir;
        d.DodgeOnAimMissChance = src.DodgeOnAimMissChance;
        Array.Copy(src.LStandAnim, d.LStandAnim, d.LStandAnim.Length);
        d.LStandWakeChance = src.LStandWakeChance;
        d.LStandWakeHealth = src.LStandWakeHealth;
        Array.Copy(src.MorphId, d.MorphId, d.MorphId.Length);
        d.MorphTrDeath = src.MorphTrDeath;
        for (int i = 0; i < 2; i++)
        {
            Array.Copy(src.FlyType[i].Distance, d.FlyType[i].Distance, 3);
            d.FlyType[i].Chance = src.FlyType[i].Chance;
        }
        d.FlyAbsGoalZ = src.FlyAbsGoalZ;
        d.FlyMustReach = src.FlyMustReach;
        d.FlyMaxHeight = src.FlyMaxHeight;
        d.FlyCfDist = src.FlyCfDist;
        d.FlyFriction = src.FlyFriction;
        d.FlyClipDist = src.FlyClipDist;
        d.FlyBackOnTrackAccel = src.FlyBackOnTrackAccel;
        Array.Copy(src.DropItems, d.DropItems, d.DropItems.Length);
        d.SprDropItemChance = src.SprDropItemChance;
        d.TimerFLaunch.CopyFrom(src.TimerFLaunch);
        d.TimerFLand.CopyFrom(src.TimerFLand);
        d.TimerGoalZ.CopyFrom(src.TimerGoalZ);
        d.TimerCrouch.CopyFrom(src.TimerCrouch);
        d.TimerMoveDir.CopyFrom(src.TimerMoveDir);
        d.TimerFloor.CopyFrom(src.TimerFloor);
        d.RandStateTime = src.RandStateTime;
        d.RandVelocity = src.RandVelocity;
        d.RandThinkTime = src.RandThinkTime;
        d.RandAnimScale = src.RandAnimScale;
        d.RandWeapChance = src.RandWeapChance;
    }

    static CdWeapon CloneWeapon(CdWeapon s)
    {
        var w = new CdWeapon
        {
            Type = s.Type, NumShots = s.NumShots, Id = s.Id, SharedId = s.SharedId, Angle = s.Angle, Medium = s.Medium,
            PickChance = s.PickChance, Posture = s.Posture, Available = s.Available, Interruptable = s.Interruptable,
            TurnToTarget = s.TurnToTarget, SpawnsSprite = s.SpawnsSprite, Inertia = s.Inertia, IsDefault = s.IsDefault,
            RealCheck = s.RealCheck, Locked = s.Locked, StateID = s.StateID, NextStateID = s.NextStateID, ClipMask = s.ClipMask,
            Group = s.Group, Data1 = s.Data1, Data2 = s.Data2, Data3 = s.Data3, Data4 = s.Data4, Data5 = s.Data5,
            MaxUnitsThis = s.MaxUnitsThis, MaxUnitsShared = s.MaxUnitsShared, PredDistance = s.PredDistance, PredAngle = s.PredAngle,
            PredAccuracy = s.PredAccuracy, ShotVelocity = s.ShotVelocity, ShotSlope = s.ShotSlope,
            ShotTargetFollow = s.ShotTargetFollow, ShotClipdist = s.ShotClipdist, ShotRemTime = s.ShotRemTime,
            ShotImpact = s.ShotImpact, ShotOffX = s.ShotOffX, ShotOffY = s.ShotOffY, ShotOffZ = s.ShotOffZ, AmmoCur = s.AmmoCur,
            AmmoTotal = s.AmmoTotal, CooldownUseCount = s.CooldownUseCount, CooldownTotalUseCount = s.CooldownTotalUseCount,
            CooldownRng0 = s.CooldownRng0, CooldownRng1 = s.CooldownRng1, StyleAvailable = s.StyleAvailable,
            StyleAngle = s.StyleAngle, StyleOffX = s.StyleOffX, StyleOffY = s.StyleOffY, StyleOffZ = s.StyleOffZ,
            Frames = s.Frames != null ? new List<int>(s.Frames) : null,
        };
        Array.Copy(s.Dispersion, w.Dispersion, 2);
        Array.Copy(s.DistRange, w.DistRange, 2);
        Array.Copy(s.HeighRange, w.HeighRange, 4);
        Array.Copy(s.SlopeRange, w.SlopeRange, 2);
        Array.Copy(s.TargHpRange, w.TargHpRange, 2);
        Array.Copy(s.DudeHpRange, w.DudeHpRange, 2);
        Array.Copy(s.TargBurnRange, w.TargBurnRange, 2);
        w.ShotSound.CopyFrom(s.ShotSound);
        w.AttackSound.CopyFrom(s.AttackSound);
        CopyAppearance(w.ShotAppearance, s.ShotAppearance);
        w.CooldownDelay.CopyFrom(s.CooldownDelay);
        return w;
    }

    static void CopyAppearance(CdAppearance a, CdAppearance s)
    {
        a.Sound.CopyFrom(s.Sound);
        a.Scl0 = s.Scl0; a.Scl1 = s.Scl1; a.Available = s.Available; a.SoundAvailable = s.SoundAvailable; a.Seq = s.Seq;
        a.Clb = s.Clb; a.Eff = s.Eff; a.Pic = s.Pic; a.Csta = s.Csta; a.Cstr = s.Cstr; a.Xrp = s.Xrp; a.Yrp = s.Yrp; a.Xof = s.Xof;
        a.Yof = s.Yof; a.Pal = s.Pal; a.Shd = s.Shd;
    }

    static CdEffect CloneEffect(CdEffect s)
    {
        var e = new CdEffect
        {
            LiveTime = s.LiveTime, Velocity = s.Velocity, VelocitySlope = s.VelocitySlope, Angle = s.Angle, Posture = s.Posture,
            Medium = s.Medium, Chance = s.Chance, AllUnique = s.AllUnique, SrcVelocity = s.SrcVelocity, HpRange0 = s.HpRange0,
            HpRange1 = s.HpRange1, OffX = s.OffX, OffY = s.OffY, OffZ = s.OffZ,
        };
        Array.Copy(s.Id, e.Id, e.Id.Length);
        e.Delay.CopyFrom(s.Delay);
        e.Spr2Gib.Available = s.Spr2Gib.Available; e.Spr2Gib.Force = s.Spr2Gib.Force; e.Spr2Gib.TrFlags = s.Spr2Gib.TrFlags;
        e.Spr2Gib.Physics = s.Spr2Gib.Physics; e.Spr2Gib.ThingType = s.Spr2Gib.ThingType; e.Spr2Gib.Data1 = s.Spr2Gib.Data1;
        e.Spr2Gib.Data2 = s.Spr2Gib.Data2; e.Spr2Gib.Data3 = s.Spr2Gib.Data3; e.Spr2Gib.Data4 = s.Spr2Gib.Data4;
        CopyAppearance(e.Appearance, s.Appearance);
        e.Anims.AddRange(s.Anims);
        e.Frames.AddRange(s.Frames);
        e.States.AddRange(s.States);
        return e;
    }

    void RandomizeDudeSettings()
    {
        var d = _d;
        if (d.RandStateTime != 0)
        {
            foreach (int st in new[] { Cd.StateSearch, Cd.StateKnock, Cd.StateFlee })
                for (int j = 0; j < Cd.PostureMax; j++)
                {
                    // (NBlood steps its pointer along the postures but reads the first one's time each time)
                    var s = d.States[st, j];
                    int nVal = s.Ticks;
                    s.Ticks -= Cd.Perc2Val(d.RandStateTime, nVal);
                    s.Ticks += Cd.Perc2Val(_w.Random(d.RandStateTime + 1), nVal);
                }
        }
        if (d.RandVelocity != 0)
        {
            for (int i = 0; i < Cd.PostureMax; i++)
                for (int j = 0; j < Cd.VelMax; j++)
                {
                    if (j == Cd.VelTurn)
                        continue;
                    int nVal = d.GetVelocityBase(i, j), nVel = nVal;
                    nVel -= Cd.Perc2Val(d.RandVelocity, nVal);
                    nVel += Cd.Perc2Val(_w.Random(d.RandVelocity + 1), nVal);
                    d.SetVelocityBase(nVel, i, j);
                }
        }
        if (d.RandThinkTime != 0)
        {
            int nVal = Cd.Perc2Val(d.RandThinkTime, d.ThinkClock);
            d.ThinkClock -= nVal;
            d.ThinkClock += _w.Random(nVal + 1);
        }
        if (d.RandAnimScale != 0)
        {
            if (d.Spr.XScale == 0)
                d.Spr.XScale = Cd.DefaultAnimScale;
            int nVal = Cd.Perc2Val(d.RandAnimScale, d.Spr.XScale);
            d.Spr.XScale -= nVal;
            d.Spr.XScale += _w.Random(nVal + 1);
            CountHeight();
        }
        if (d.RandWeapChance != 0)
        {
            for (int i = 0; i < d.NumWeapons; i++)
            {
                int nVal = d.Weapons[i].PickChance;
                if (nVal >= Cd.ChanceMax)
                    continue;
                d.Weapons[i].PickChance -= Cd.Perc2Val(d.RandWeapChance, nVal);
                d.Weapons[i].PickChance += Cd.Perc2Val(_w.Random(d.RandWeapChance + 1), nVal);
            }
        }
    }

    /// <summary>CUSTOMDUDE_SETUP::Setup(sprite): a custom dude made for a sprite: its defaults, its states, its
    /// descriptor's (or its data fields') settings, or its kind's template's.</summary>
    public static CustomDude Setup(World w, Sprite e)
    {
        var d = new CustomDude { Spr = e };
        e.Custom = d;
        d.RandStateTime = 20;
        d.SeeDist = Info.Dude(KM.kDudeModernCustom, Info.DI_seeDist);
        d.HearDist = Info.Dude(KM.kDudeModernCustom, Info.DI_hearDist);
        d.Periphery = Info.Dude(KM.kDudeModernCustom, Info.DI_periphery);
        d.FleeHealth = Info.Dude(KM.kDudeModernCustom, Info.DI_fleeHealth);
        d.SleepDist = Cd.MinSeeDist;
        d.FallHeight = int.MaxValue;
        d.ThinkClock = 3;
        w.CdInitStates(d);
        var setup = new CustomDudeSetup(w, d);
        bool fromTemplate = setup.SetupSettings();
        d.Initialized = true;
        if (d.Version == 2)
        {
            if (!fromTemplate)
                w.CdTemplates[(2, e.Data1)] = MakeTemplate(d, w);
            if ((e.Data2 & Cd.StatusAwaked) != 0)
            {
                for (int i = 0; i < Cd.PostureMax; i++)
                    d.States[Cd.StateSleep, i].Seq = 0;
                d.StatusSet(Cd.StatusAwaked);
            }
            if ((e.Data2 & Cd.StatusLStandAwaked) != 0)
            {
                EventClear(d.LStand);
                Array.Clear(d.LStandAnim);
                d.LStandWakeChance = d.LStandWakeHealth = 0;
                d.StatusSet(Cd.StatusLStandAwaked);
            }
            if ((e.Data2 & Cd.StatusForceCrouch) != 0) d.StatusSet(Cd.StatusForceCrouch);
            if ((e.Data2 & Cd.StatusCruel) != 0) d.StatusSet(Cd.StatusCruel);
            if ((e.Data2 & Cd.StatusInnocent) != 0) d.StatusSet(Cd.StatusInnocent);
        }
        w.CdSetupLeech(d);
        w.CdSetupSlaves(d);
        if (d.Version == 2)
            setup.RandomizeDudeSettings();
        return d;
    }

    static CustomDude MakeTemplate(CustomDude d, World w)
    {
        var t = new CustomDude { TemplateClipDist = d.Spr.ClipDist, TemplateScale = d.Spr.XScale };
        CopySettings(t, d, w);
        t.Initialized = true;
        return t;
    }

    /// <summary>CUSTOMDUDE_SETUP::Setup(pOver): a dude's settings made again after its data changed (a data changer):
    /// the dude itself stays as it is, unless its version changed (then it starts over).</summary>
    public static void Resetup(World w, CustomDude d)
    {
        int prev = d.Version;
        if (prev != 0)
            w.CdClearEffectCallbacks(d);
        new CustomDudeSetup(w, d).SetupSettings(prev);
    }

    // true: the settings came from the kind's template
    bool SetupSettings(int prevVer = 0)
    {
        var e = _d.Spr;
        if (_w.CdTemplates.TryGetValue((2, e.Data1), out var templ))
        {
            CopySettings(_d, templ, _w);
            return true;
        }
        _d.Version = 1;
        if (DescriptLoad(e.Data1))
        {
            if (DescriptCheck() == 2)
                _d.Version = 2;
            else
            {
                Warning("Invalid descriptor version");
                _ini = null;
                e.Data1 = e.Data2 = e.Data3 = 0;
            }
        }
        if (prevVer != 0 && prevVer != _d.Version)
        {
            Setup(_w, e);
            return false;
        }
        Array.Clear(_d.DropItems);
        _d.SprDropItemChance = Cd.ChanceMax;
        if (_d.Version == 2)
            SetupV2();
        else
            SetupV1();
        AnimationFillAll();
        CountHeight();
        SoundFillAll();
        return false;
    }
}
