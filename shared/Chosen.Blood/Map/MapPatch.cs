// Chosen Blood module (GPL v2): Refreshed Supply's level fixes (its defs/levelHacks, exported by
// pipeline/export_maps.py as <NAME>.patch beside the .MAP).
using System;
using System.Collections.Generic;
using System.Globalization;

namespace Chosen.Shared.Blood.Map;

/// <summary>A sprite's extra as the .patch writes it: its key (angoff, mdxoff, mdyoff, mdzoff, novoxel) and value
/// ("" when the line has none).</summary>
public readonly record struct SpriteExtra(int Sprite, string Key, string Value);

/// <summary>
/// A .patch: lines `sector &lt;n&gt; ceilingz|floorz &lt;z&gt;` (heights Refreshed Supply corrects) and
/// `sprite &lt;n&gt; &lt;key&gt; [value]` (its voxel models' turn, offset or none). The heights go into the map data; the
/// sprite keys are Chosen's extras (BSprite.Patch; .MAPX's SPRX chunk).
/// </summary>
public sealed class MapPatch
{
    public readonly List<(int sector, bool ceiling, int z)> Heights = new();
    public readonly List<SpriteExtra> Sprites = new();

    public static MapPatch Parse(IEnumerable<string> lines)
    {
        var p = new MapPatch();
        foreach (var line in lines)
        {
            var c = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (c.Length >= 4 && c[0] == "sector" && int.TryParse(c[1], out int si) && si >= 0
                && int.TryParse(c[3], out int z) && (c[2] == "ceilingz" || c[2] == "floorz"))
                p.Heights.Add((si, c[2] == "ceilingz", z));
            else if (c.Length >= 3 && c[0] == "sprite" && int.TryParse(c[1], out int sp) && sp >= 0 && GameKey(c[2]) != null)
                p.Sprites.Add(new SpriteExtra(sp, c[2], c.Length > 3 ? c[3] : ""));
        }
        return p;
    }

    /// <summary>The key the game reads in BSprite.Patch for a .patch key, null for one it doesn't know.</summary>
    public static string GameKey(string key) => key switch
    {
        "angoff" => "voxAngOff", "mdxoff" => "voxXOff", "mdyoff" => "voxYOff", "mdzoff" => "voxZOff",
        "novoxel" => "noVoxel", _ => null,
    };

    /// <summary>Both kinds: the heights into the sectors, the sprite extras into BSprite.Patch.</summary>
    public void Apply(BloodMapFile m)
    {
        ApplyHeights(m);
        ApplySprites(m, Sprites);
    }

    public void ApplyHeights(BloodMapFile m)
    {
        foreach (var (si, ceiling, z) in Heights)
            if (si < m.Sectors.Count)
            {
                if (ceiling)
                    m.Sectors[si].CeilingZ = z;
                else
                    m.Sectors[si].FloorZ = z;
            }
    }

    /// <summary>Sprite extras into BSprite.Patch under the game's keys (a key without a value: "1").</summary>
    public static void ApplySprites(BloodMapFile m, IEnumerable<SpriteExtra> extras)
    {
        foreach (var e in extras)
            if (e.Sprite < m.Sprites.Count && GameKey(e.Key) is { } key)
                (m.Sprites[e.Sprite].Patch ??= new Dictionary<string, string>())[key] = e.Value == "" ? "1" : e.Value;
    }

    /// <summary>The .patch lines of sprite extras, as the file writes them.</summary>
    public static IEnumerable<string> SpriteLines(IEnumerable<SpriteExtra> extras)
    {
        foreach (var e in extras)
            yield return e.Value == "" ? $"sprite {e.Sprite.ToString(CultureInfo.InvariantCulture)} {e.Key}"
                : $"sprite {e.Sprite.ToString(CultureInfo.InvariantCulture)} {e.Key} {e.Value}";
    }
}
