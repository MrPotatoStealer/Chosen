// Chosen Blood module (GPL v2): Blood's object types, from xmapedit's OBJTYPES.INI (NoOne, GPL) and NBlood's
// common_game.h type ranges.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace Chosen.Shared.Blood.Types;

public enum ObjKind { Sprite, Wall, Sector }

/// <summary>What the editor shows for a type: its name, a short caption, what data1..data4 mean (null: unused), and
/// whether it is one of NoOne's modern types (Availability=2: NBlood's modern build only, not vanilla Blood).</summary>
public sealed class TypeInfo
{
    public int Type;
    public string Name = "", Caption = "";
    public readonly string[] Data = new string[4];
    /// <summary>Data n only matters with NoOne's modern features (OBJTYPES.INI marks it with a *).</summary>
    public readonly bool[] DataModern = new bool[4];
    public bool Modern;
}

/// <summary>How a new sprite of a type looks (OBJTYPES.INI's AutoAdjustSprites, xmapedit's auto-adjust table): its
/// tile, size and palette (-1: left as it is), whether hitscans hit it (-1: as it is), its group (Marker: invisible
/// editor markers) and whether it needs an x-sprite.</summary>
public sealed class SpriteLook
{
    public int Type, Tile = -1, SizeX = -1, SizeY = -1, Pal = -1, Hit = -1;
    public string Group = "";
    public bool XSprite;
}

/// <summary>Sprite categories for the editor's markers and filter.</summary>
public enum SpriteCategory { Decoration, PlayerStart, Marker, Switch, Weapon, Ammo, Item, Key, Dude, Thing, Trap, Generator, Sound, Other }

/// <summary>Blood's sprite, wall and sector types with their names and data captions (OBJTYPES.INI's Sprites, Walls
/// and Sectors sections).</summary>
public sealed class TypeTable
{
    readonly Dictionary<(ObjKind, int), TypeInfo> _types = new();
    readonly Dictionary<int, SpriteLook> _looks = new();

    public static TypeTable Load()
    {
        using var st = typeof(TypeTable).Assembly.GetManifestResourceStream("objtypes.ini")
            ?? throw new InvalidOperationException("objtypes.ini is not embedded");
        using var r = new StreamReader(st);
        return Parse(r.ReadToEnd());
    }

    static readonly Regex Line = new(@"^Type(\d+)\s*=\s*\((.*)\)\s*$");

    public static TypeTable Parse(string ini)
    {
        var t = new TypeTable();
        ObjKind? kind = null;
        bool looks = false;
        foreach (var raw in ini.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith('['))
            {
                kind = line switch { "[Sprites]" => ObjKind.Sprite, "[Walls]" => ObjKind.Wall, "[Sectors]" => ObjKind.Sector, _ => null };
                looks = line == "[AutoAdjustSprites]";
                continue;
            }
            if (looks && Line.Match(line) is { Success: true } lm)
            {
                // a type's first row (xmapedit takes the first; the later ones are picture exceptions)
                int type = int.Parse(lm.Groups[1].Value);
                if (!t._looks.ContainsKey(type))
                    t._looks[type] = ParseLook(type, lm.Groups[2].Value);
                continue;
            }
            if (kind == null)
                continue;
            var m = Line.Match(line);
            if (!m.Success)
                continue;
            var info = new TypeInfo { Type = int.Parse(m.Groups[1].Value) };
            foreach (var part in m.Groups[2].Value.Split(','))
            {
                int eq = part.IndexOf('=');
                if (eq < 0)
                    continue;
                string k = part[..eq].Trim(), v = part[(eq + 1)..].Trim();
                switch (k)
                {
                    case "Name": info.Name = v; break;
                    case "Caption": info.Caption = v; break;
                    case "Availability": info.Modern = v == "2"; break;
                    case "Data1": SetData(info, 0, v); break;
                    case "Data2": SetData(info, 1, v); break;
                    case "Data3": SetData(info, 2, v); break;
                    case "Data4": SetData(info, 3, v); break;
                }
            }
            t._types[(kind.Value, info.Type)] = info;
        }
        return t;
    }

    static readonly Regex Part = new(@"(\w+)\s*=\s*(\([^)]*\)|[^,]*)");

    static SpriteLook ParseLook(int type, string body)
    {
        var l = new SpriteLook { Type = type };
        foreach (Match p in Part.Matches(body))
        {
            string k = p.Groups[1].Value, v = p.Groups[2].Value.Trim();
            int.TryParse(v, out int n);
            switch (k)
            {
                case "Group": l.Group = v; break;
                case "Tile": l.Tile = n; break;
                case "SizeX": l.SizeX = n; break;
                case "SizeY": l.SizeY = n; break;
                case "Pal": l.Pal = n; break;
                case "Hit": l.Hit = n; break;
                case "Flags": l.XSprite = v.Contains("XSprite"); break;
            }
        }
        return l;
    }

    /// <summary>How a new sprite of the type looks, or null when the table doesn't say.</summary>
    public SpriteLook Look(int type) => _looks.GetValueOrDefault(type);

    /// <summary>The status list Blood keeps a sprite of the type in (NBlood common_game.h's kStat*, by xmapedit's
    /// type ranges): items 3, dudes 6, projectiles 5, things 4, traps 11, ambient sound 12, path markers 16, else 0.
    /// Sector markers (10) belong to their sector and are made with it.</summary>
    public static int StatFor(int type) => type switch
    {
        15 => 16,
        >= 40 and < 200 => 3,
        >= 200 and < 262 => 6,
        >= 300 and < 318 => 5,
        >= 400 and < 436 => 4,
        >= 450 and < 460 => 11,
        710 => 12,
        _ => 0,
    };

    // OBJTYPES.INI: "*Caption" a data field only modern features use, "~" a field the type leaves unused
    static void SetData(TypeInfo info, int n, string v)
    {
        if (v.StartsWith('*'))
        {
            info.DataModern[n] = true;
            v = v[1..].Trim();
        }
        info.Data[n] = v == "~" || v == "" ? null : v;
    }

    public int Count => _types.Count;

    public TypeInfo Get(ObjKind kind, int type) => _types.TryGetValue((kind, type), out var i) ? i : null;

    /// <summary>Every type of a kind, by number.</summary>
    public IEnumerable<TypeInfo> All(ObjKind kind)
    {
        var l = new List<TypeInfo>();
        foreach (var ((k, _), info) in _types)
            if (k == kind)
                l.Add(info);
        l.Sort((a, b) => a.Type.CompareTo(b.Type));
        return l;
    }

    /// <summary>The type's name, or "Unnamed n".</summary>
    public string Name(ObjKind kind, int type) => Get(kind, type)?.Name is { Length: > 0 } n ? n : $"Unnamed {type}";

    /// <summary>What data n (1..4) means for the type, or null when it has no meaning there.</summary>
    public string DataCaption(ObjKind kind, int type, int n) => Get(kind, type)?.Data[n - 1];

    /// <summary>A sprite type's category (NBlood common_game.h's ranges).</summary>
    public static SpriteCategory Category(int type) => type switch
    {
        0 => SpriteCategory.Decoration,
        1 or 2 => SpriteCategory.PlayerStart,
        >= 3 and < 20 => SpriteCategory.Marker,
        >= 20 and < 24 => SpriteCategory.Switch,
        >= 40 and < 51 => SpriteCategory.Weapon,
        >= 60 and < 81 => SpriteCategory.Ammo,
        >= 100 and < 107 => SpriteCategory.Key,
        >= 107 and < 151 => SpriteCategory.Item,
        >= 200 and < 262 => SpriteCategory.Dude,
        >= 400 and < 436 => SpriteCategory.Thing,
        >= 450 and < 460 => SpriteCategory.Trap,
        >= 700 and < 708 => SpriteCategory.Generator,
        >= 708 and < 712 => SpriteCategory.Sound,
        _ => SpriteCategory.Other,
    };
}
