// Chosen Blood module (GPL v2): how Blood's own maps use each tile (data/tileuse.txt, made by maptool tileuse from the
// game's 55 maps): Blood's tiles have no names, so the tile picker's tabs (walls, floors, ceilings...) and the sprite
// browser's decorations come from where the game's own levels put each picture.
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Chosen.Forge.Data;
using Godot;

namespace Chosen.Forge.Blood;

/// <summary>A tile's uses in Blood's maps: walls, masked walls' over pictures, floors, ceilings, parallax ceilings
/// (skies), decoration sprites facing / flat on a wall / flat on a floor, sprites of another type; the maps it is in;
/// its decorations' most common size (0: none).</summary>
public readonly record struct TileUses(int Wall, int Masked, int Floor, int Ceiling, int Sky, int Deco, int DecoWall,
    int DecoFloor, int Sprite, int Maps, int XRepeat, int YRepeat)
{
    public int Decoration => Deco + DecoWall + DecoFloor;
    /// <summary>A decoration's picture: Blood's maps use it as a decoration at least half as often as on walls, floors
    /// and ceilings (a wall texture put up once as a patch is not one).</summary>
    public bool IsDecoration => Decoration > 0 && Decoration * 2 >= Wall + Masked + Floor + Ceiling + Sky;
    public int Total => Wall + Masked + Floor + Ceiling + Sky + Decoration + Sprite;
}

/// <summary>The tile picker's tabs.</summary>
public enum TileTab { All, Walls, Floors, Ceilings, Masked, Skies, Decorations, Unused, InMap, MapArt }

public static class TileUse
{
    static Dictionary<int, TileUses> _uses;

    /// <summary>Every tile Blood's maps use (loaded from res://data/tileuse.txt on first use).</summary>
    public static IReadOnlyDictionary<int, TileUses> All
    {
        get
        {
            if (_uses != null)
                return _uses;
            _uses = new Dictionary<int, TileUses>();
            string text = FileAccess.GetFileAsString("res://data/tileuse.txt");
            foreach (var line in text.Split('\n'))
            {
                if (line.StartsWith("//"))
                    continue;
                var p = line.Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
                if (p.Length < 13)
                    continue;
                var v = p.Select(x => int.TryParse(x, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n : 0).ToArray();
                _uses[v[0]] = new TileUses(v[1], v[2], v[3], v[4], v[5], v[6], v[7], v[8], v[9], v[10], v[11], v[12]);
            }
            return _uses;
        }
    }

    public static TileUses Of(int tile) => All.TryGetValue(tile, out var u) ? u : default;

    /// <summary>How often Blood's maps use the tile the tab's way (All: in any way; In this map: 0).</summary>
    public static int Count(int tile, TileTab tab)
    {
        var u = Of(tile);
        return tab switch
        {
            TileTab.Walls => u.Wall,
            TileTab.Floors => u.Floor,
            TileTab.Ceilings => u.Ceiling,
            TileTab.Masked => u.Masked,
            TileTab.Skies => u.Sky,
            TileTab.Decorations => u.IsDecoration ? u.Decoration : 0,
            TileTab.Unused => IsUnused(tile) ? 1 : 0,
            TileTab.All => u.Total,
            _ => 0,
        };
    }

    static Shared.Blood.Files.ArtSet _artSeen;
    static HashSet<int> _partOf;

    /// <summary>A tile no map of Blood's or its add-ons uses that looks like a texture: a picture with both sides a power
    /// of two from 16 up, not a sprite's (no views) and not a frame or an angle of another tile.</summary>
    public static bool IsUnused(int tile)
    {
        if (Of(tile).Total > 0 || !ForgeData.Ready || ForgeData.MapTiles.Contains(tile) || ForgeData.Tiles[tile] is not { Empty: false } t || t.View != 0)
            return false;
        static bool Pow2(int n) => n >= 16 && (n & (n - 1)) == 0;
        return Pow2(t.Width) && Pow2(t.Height) && !PartOfAnother().Contains(tile);
    }

    /// <summary>The tiles that are the frames or angles of another (after a tile animated or with views: its frames,
    /// each angle's; an animation playing backwards: the frames before it), for the art loaded.</summary>
    static HashSet<int> PartOfAnother()
    {
        if (_partOf != null && ReferenceEquals(_artSeen, ForgeData.Tiles))
            return _partOf;
        _artSeen = ForgeData.Tiles;
        _partOf = new HashSet<int>();
        foreach (int n in ForgeData.Tiles.Numbers)
        {
            var t = ForgeData.Tiles[n];
            if (t == null)
                continue;
            int angles = t.View switch { 1 => 5, 2 => 8, 3 => 2, _ => 1 };
            int span = (t.AnimFrames + 1) * angles;
            if (t.AnimType == 3 && angles == 1)
                for (int k = 1; k <= t.AnimFrames; k++)
                    _partOf.Add(n - k);
            else
                for (int k = 1; k < span; k++)
                    _partOf.Add(n + k);
        }
        return _partOf;
    }

    /// <summary>The tab a field's kind of picture opens on ("wall", "masked", "floor", "ceiling", "sky", "sprite";
    /// else All).</summary>
    public static TileTab TabFor(string kind) => kind switch
    {
        "wall" => TileTab.Walls,
        "masked" => TileTab.Masked,
        "floor" => TileTab.Floors,
        "ceiling" => TileTab.Ceilings,
        "sky" => TileTab.Skies,
        "sprite" => TileTab.Decorations,
        "unused" => TileTab.Unused,
        _ => TileTab.All,
    };

    public static string TabName(TileTab t) => t switch
    {
        TileTab.InMap => "In this map",
        TileTab.MapArt => "Map's art",
        TileTab.Unused => "Unused",
        _ => t.ToString(),
    };

    /// <summary>What the tile is used as in Blood's maps, for a tooltip ("walls 120, floors 3, in 12 maps"; "" unused).</summary>
    public static string Describe(int tile)
    {
        var u = Of(tile);
        if (u.Total == 0)
            return "";
        var parts = new List<string>();
        void P(int n, string what)
        {
            if (n > 0)
                parts.Add($"{what} {n}");
        }
        P(u.Wall, "walls");
        P(u.Masked, "masked walls");
        P(u.Floor, "floors");
        P(u.Ceiling, "ceilings");
        P(u.Sky, "skies");
        P(u.Deco, "decorations");
        P(u.DecoWall, "decorations on walls");
        P(u.DecoFloor, "decorations on floors");
        P(u.Sprite, "other sprites");
        return $"Blood's maps: {string.Join(", ", parts)} (in {u.Maps} map{(u.Maps == 1 ? "" : "s")})";
    }

    /// <summary>Blood's surface types (SURFACE.DAT: the sound and the effects of a step or a hit), by number.</summary>
    public static readonly string[] Materials =
        { "None", "Stone", "Metal", "Wood", "Flesh", "Water", "Dirt", "Clay", "Snow", "Ice", "Leaves", "Cloth", "Plant", "Goo", "Lava" };

    /// <summary>A tile's surface type (0 without SURFACE.DAT).</summary>
    public static int Material(int tile) =>
        tile >= 0 && tile < ForgeData.Surface.Length && ForgeData.Surface[tile] is int m and >= 0 and < 15 ? m : 0;
}
