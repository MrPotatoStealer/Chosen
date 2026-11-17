// Chosen Blood module (GPL v2): a sprite's top and bottom as Blood works them out (db.h's GetSpriteExtents: things
// settle with that bottom on the floor), and the z that puts a sprite there.
using System;
using Chosen.Forge.Data;
using Chosen.Forge.Document;
using Chosen.Shared.Blood.Map;
using Chosen.Shared.Engine.Geometry;

namespace Chosen.Forge.Blood;

public static class SpriteExtents
{
    /// <summary>The picture's height in pixels and Blood's centre in it (half the height plus the tile's own y offset).</summary>
    static (int height, int centre) Picture(BSprite sp)
    {
        var t = ForgeData.Tiles?[sp.Picnum];
        int h = t != null && t.Height > 0 ? t.Height : 64;
        return (h, h / 2 + (t?.YOffset ?? 0));
    }

    /// <summary>Blood's top and bottom (Build z): z less yrepeat * 4 a pixel up to the centre, plus the same down from
    /// it (a floor sprite: z both). Blood keeps z there whether or not the sprite is drawn centred; its maps' sprites
    /// are all centred (cstat 128), drawn as they settle.</summary>
    public static (int top, int bottom) Of(BSprite sp)
    {
        if ((sp.Cstat & 0x30) == 0x20)
            return (sp.Z, sp.Z);
        var (h, c) = Picture(sp);
        return (sp.Z - (sp.YRepeat << 2) * c, sp.Z + (sp.YRepeat << 2) * (h - c));
    }

    /// <summary>The z putting Blood's bottom at bottom.</summary>
    public static int ZForBottom(BSprite sp, double bottom)
    {
        if ((sp.Cstat & 0x30) == 0x20)
            return (int)Math.Round(bottom);
        var (h, c) = Picture(sp);
        return (int)Math.Round(bottom - (sp.YRepeat << 2) * (h - c));
    }

    /// <summary>The z putting Blood's top at top.</summary>
    public static int ZForTop(BSprite sp, double top)
    {
        if ((sp.Cstat & 0x30) == 0x20)
            return (int)Math.Round(top);
        var (_, c) = Picture(sp);
        return (int)Math.Round(top + (sp.YRepeat << 2) * c);
    }

    /// <summary>A sprite made to stand as Blood's maps have them: centred (cstat 128), its bottom on its sector's floor
    /// at its place.</summary>
    public static void StandOnFloor(MapDocument d, int i)
    {
        var sp = d.Sprites[i];
        if ((sp.Cstat & 0x30) != 0x20)
            sp.Cstat |= 128;
        if (sp.SectNum < 0 || sp.SectNum >= d.Sectors.Count)
            return;
        BuildGeom.GetZsOfSlope(d, sp.SectNum, sp.X, sp.Y, out _, out double floor);
        sp.Z = ZForBottom(sp, floor);
    }

    /// <summary>A sprite's picture changed keeping its bottom where it was (it goes on standing on what it stood on);
    /// a wall sprite keeps its middle (a decal stays where it was put).</summary>
    public static void SetPicture(BSprite sp, int tile)
    {
        var (top, bottom) = Of(sp);
        bool wall = (sp.Cstat & 0x30) == 0x10;
        sp.Picnum = tile;
        if (wall)
        {
            var (t2, b2) = Of(sp);
            sp.Z += (top + bottom) / 2 - (t2 + b2) / 2;
        }
        else
            sp.Z = ZForBottom(sp, bottom);
    }
}
