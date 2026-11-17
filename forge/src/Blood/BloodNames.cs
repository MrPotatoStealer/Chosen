// Chosen Blood module (GPL v2): what Forge shows of Blood's types and flags (xmapedit's OBJTYPES.INI through
// Chosen.Blood's TypeTable; Build's cstat bits as NBlood's build.h names them).
using System.Collections.Generic;
using System.Text;
using Chosen.Shared.Blood.Map;
using Chosen.Shared.Blood.Types;

namespace Chosen.Forge.Blood;

public static class BloodNames
{
    static TypeTable _types;
    public static TypeTable Types => _types ??= TypeTable.Load();

    public static string Sector(int type) => type == 0 ? "None" : Types.Name(ObjKind.Sector, type);
    public static string Wall(int type) => type == 0 ? "None" : Types.Name(ObjKind.Wall, type);
    /// <summary>A sprite type's name (the sector markers named, objtypes.ini has none for them: off, on, axis).</summary>
    public static string Sprite(int type) => type switch
    {
        3 => "Off marker (a sliding sector's off place)",
        4 => "On marker (a sliding sector's on place)",
        5 => "Axis marker (a turning sector's pivot; its angle: how far)",
        _ => Types.Name(ObjKind.Sprite, type),
    };

    /// <summary>A sprite's marker category (its colour and the sprite filter).</summary>
    public static string Category(BSprite s) => s.StatNum == 10 ? "Marker" : TypeTable.Category(s.Type).ToString();

    /// <summary>What data n (1..4) of a sprite means (null: nothing).</summary>
    public static string SpriteData(int type, int n) => Types.DataCaption(ObjKind.Sprite, type, n);

    static readonly (int bit, string name)[] WallBits =
    {
        (1, "blocking"), (2, "swap bottoms"), (4, "align bottom"), (8, "x-flip"), (16, "masked"), (32, "one-way"),
        (64, "hitscan"), (128, "translucent"), (256, "y-flip"), (512, "translucent reversed"),
    };

    static readonly (int bit, string name)[] SpriteBits =
    {
        (1, "blocking"), (2, "translucent"), (4, "x-flip"), (8, "y-flip"), (16, "wall"), (32, "floor"), (64, "one-sided"),
        (128, "centred"), (256, "hitscan"), (512, "translucent reversed"), (32768, "invisible"),
    };

    static readonly (int bit, string name)[] PlaneBits =
    {
        (1, "parallax"), (2, "sloped"), (4, "swap xy"), (8, "double smooth"), (16, "x-flip"), (32, "y-flip"),
        (64, "align to first wall"), (128, "masked"), (256, "translucent"),
    };

    public static string WallCstat(int c) => Flags(c, WallBits);
    public static string SpriteCstat(int c) => Flags(c, SpriteBits);
    public static string PlaneStat(int c) => Flags(c, PlaneBits);

    static string Flags(int c, (int bit, string name)[] bits)
    {
        var sb = new StringBuilder();
        foreach (var (bit, name) in bits)
            if ((c & bit) != 0)
                sb.Append(sb.Length > 0 ? ", " : "").Append(name);
        return sb.Length > 0 ? sb.ToString() : "-";
    }

    /// <summary>An x-object's channels as the labels show them ("rx 12 tx 30").</summary>
    public static string Channels(int rx, int tx)
    {
        var parts = new List<string>();
        if (rx > 0)
            parts.Add("rx " + rx);
        if (tx > 0)
            parts.Add("tx " + tx);
        return string.Join(" ", parts);
    }
}
