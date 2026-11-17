// Chosen Blood module (GPL v2): a sprite set up for its type as xmapedit's auto-adjust does it (aadjust.cpp's
// adjSetApperance and its group rules, with OBJTYPES.INI's AutoAdjustSprites table), its status list by NBlood's ranges.
using System.Collections.Generic;
using System.Linq;
using Chosen.Forge.Document;
using Chosen.Shared.Blood.Map;
using Chosen.Shared.Blood.Types;

namespace Chosen.Forge.Blood;

public static class SpriteTypes
{
    /// <summary>Sector markers: made with their sector's type (slide, rotate, teleport), not placed by hand.</summary>
    static readonly HashSet<int> SectorMarkers = new() { 3, 4, 5, 8 };

    /// <summary>The types the sprite browser offers, by category (modern: NoOne's modern types too).</summary>
    public static IEnumerable<IGrouping<SpriteCategory, TypeInfo>> Placeable(bool modern) =>
        BloodNames.Types.All(ObjKind.Sprite)
            .Where(t => !SectorMarkers.Contains(t.Type) && (modern || !t.Modern) && t.Name.Length > 0)
            .GroupBy(t => TypeTable.Category(t.Type))
            .OrderBy(g => g.Key);

    /// <summary>Sprite i shows tile as Blood's maps have it as a decoration (Blood.TileUse): its picture, its most
    /// common size, and placed on a floor or ceiling (where: "floor", "ceiling", "wall"), flat when Blood's maps mostly
    /// lay it flat on floors (a pool, a rug). A wall placing keeps its wall lie.</summary>
    public static void Decorate(MapDocument doc, int i, int tile, string where)
    {
        var s = doc.Sprites[i];
        s.Picnum = tile;
        var u = TileUse.Of(tile);
        if (u.XRepeat > 0 && u.YRepeat > 0)
        {
            s.XRepeat = u.XRepeat;
            s.YRepeat = u.YRepeat;
        }
        if (where != "wall" && u.DecoFloor > u.Deco && u.DecoFloor >= u.DecoWall)
            s.Cstat = (s.Cstat & ~(16 | 32)) | 32;
    }

    /// <summary>The tile a type shows as (its look's, else -1).</summary>
    public static int Tile(int type) => BloodNames.Types.Look(type)?.Tile ?? -1;

    /// <summary>Sprite i becomes a sprite of the type: its status list, and the look the table gives it (tile, size,
    /// palette, hitscan, an x-sprite where it needs one; markers invisible, not blocking, full bright; switches,
    /// pickups and enemies not blocking).</summary>
    public static void Apply(MapDocument doc, int i, int type)
    {
        var s = doc.Sprites[i];
        s.Type = type;
        s.StatNum = TypeTable.StatFor(type);
        if (BloodNames.Types.Look(type) is { } look)
        {
            if (look.Tile >= 0)
                s.Picnum = look.Tile;
            // -1: any palette; a sprite changing type then gets the normal one (xmapedit)
            s.Pal = look.Pal >= 0 ? look.Pal : 0;
            if (look.SizeX >= 0)
                s.XRepeat = look.SizeX;
            if (look.SizeY >= 0)
                s.YRepeat = look.SizeY;
            if (look.Hit == 0)
                s.Cstat &= ~256;
            else if (look.Hit == 1)
                s.Cstat |= 256;
            if (look.XSprite)
                doc.EnsureXSprite(i);
            if (look.Group == "Marker")
            {
                if (type != 15)
                    s.Cstat |= 32768;
                s.Cstat &= ~(1 | 256);
                s.Shade = -128;
            }
        }
        if (type is >= 20 and < 24 or >= 40 and < 200 || TypeTable.Category(type) == SpriteCategory.Dude)
            s.Cstat &= ~1;
    }
}
