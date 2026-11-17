// Chosen core: original code. Sprites placed in the 3D preview (UDB's visual mode insert): at the crosshair, on the
// floor, under the ceiling or flat on a wall (wall aligned: decals).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Chosen.Forge.View;
using Chosen.Shared.Engine.Geometry;
using Godot;

namespace Chosen.Forge;

public partial class MainWindow
{
    static readonly CultureInfo PlaceInv = CultureInfo.InvariantCulture;

    /// <summary>
    /// Insert in the preview (and the context menu's Place): a sprite of the docker's type where the crosshair is.
    /// On a floor (or a sprite: the floor under it) standing there, turned to the camera; on a ceiling hanging from it;
    /// on a wall flat against it (wall aligned, facing into its sector, its middle where the crosshair is): a decal.
    /// A type with no picture of its own (a decoration) takes the picture Ctrl+C copied, else the tile picker opens.
    /// </summary>
    public void PlaceAtCrosshair()
    {
        if (_preview.Target is not { } t)
        {
            DisplayStatus("place: aim at a floor, a ceiling or a wall");
            return;
        }
        if (Posed)
        {
            DisplayStatus("sprites are placed in Edit");
            return;
        }
        var (hx, hy, hz) = _preview.HitPoint;
        var (cx, cy, _) = _preview.CameraBuild;
        string mode;
        int sector = t.Sector, ang;
        double x = hx, y = hy, z = hz;
        if (t.IsWall)
        {
            mode = "wall";
            // facing into the wall's sector (its room on the wall's right as drawn), a little off it to that side
            var (x1, y1, x2, y2) = BuildGeom.WallEnds(Doc, t.Wall);
            double wa = Math.Atan2(y2 - y1, x2 - x1);
            ang = Angle(wa) + 512 & 2047;
            double na = wa + Math.PI / 2;
            x += Math.Cos(na) * 2;
            y += Math.Sin(na) * 2;
        }
        else
        {
            mode = t.Part == VisualPart.Ceiling ? "ceiling" : "floor";
            if (t.IsSprite)
                sector = Doc.Sprites[t.Sprite].SectNum;
            // turned to the camera (an enemy facing the one placing it)
            ang = Angle(Math.Atan2(cy - y, cx - x));
        }
        if (sector < 0 || sector >= Doc.Sectors.Count)
        {
            DisplayStatus("place: not inside a sector");
            return;
        }
        // the picture of a type without its own: the browser's decoration (with Blood's look: "d"), else Ctrl+C's
        int deco = DecorationTile();
        UiDo(string.Format(PlaceInv, "visualplace {0} {1} {2} {3} {4} {5} {6}", SpriteType, mode, (int)Math.Round(x),
            (int)Math.Round(y), (int)Math.Round(z), sector, ang) + (deco >= 0 ? $" {deco} d" : TextureClip >= 0 ? $" {TextureClip}" : ""));
    }

    static int Angle(double radians) => (int)Math.Round(radians * 1024 / Math.PI) & 2047;

    /// <summary>"standonfloor index...": sprites made to stand as Blood's maps have them (centred, Blood's bottom on
    /// their sector's floor: where a thing settles in the game); floor and wall sprites left alone. One undo step.</summary>
    void StandOnFloor(string action)
    {
        var sprites = action.Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1)
            .Select(x => int.TryParse(x, out int i) ? i : -1)
            .Where(i => i >= 0 && i < Doc.Sprites.Count && Doc.SpriteEditable(i) && (Doc.Sprites[i].Cstat & 48) == 0).Distinct().ToList();
        if (sprites.Count == 0)
        {
            DisplayStatus("stand on floor: select sprites that face the camera (not floor or wall sprites)");
            return;
        }
        if (Posed)
        {
            DisplayStatus("sprites are moved in Edit");
            return;
        }
        Undo.CreateUndo(sprites.Count == 1 ? "Stand sprite on the floor" : $"Stand {sprites.Count} sprites on the floor");
        _visualKey = null;
        foreach (int i in sprites)
            Blood.SpriteExtents.StandOnFloor(Doc, i);
        Doc.NotifyChanged();
        UndoChanged();
        DisplayStatus(sprites.Count == 1 ? $"sprite {sprites[0]} stands on the floor" : $"{sprites.Count} sprites stand on the floor");
    }

    /// <summary>"visualplace type floor|ceiling|wall x y z sector angle [tile [d]]": the sprite placed (one undo step);
    /// tile: the picture for a type without its own; d: with the size and lie Blood's maps give that decoration
    /// (Blood.SpriteTypes.Decorate).</summary>
    void VisualPlace(string action)
    {
        var p = action.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (p.Length < 8)
            return;
        int I(int k) => int.TryParse(p[k], NumberStyles.Integer, PlaceInv, out int v) ? v : 0;
        int type = I(1), x = I(3), y = I(4), z = I(5), sector = I(6), ang = I(7), tile = p.Length > 8 ? I(8) : -1;
        string mode = p[2];
        if (sector < 0 || sector >= Doc.Sectors.Count)
            return;
        Undo.CreateUndo("Place sprite");
        _visualKey = null;
        int i = Doc.AddSprite(x, y, 0, sector);
        Blood.SpriteTypes.Apply(Doc, i, type);
        var sp = Doc.Sprites[i];
        // a decoration (no picture of its own): the copied picture
        bool ownTile = Blood.BloodNames.Types.Look(type) is { Tile: >= 0 };
        if (!ownTile && tile >= 0)
        {
            sp.Picnum = tile;
            if (p.Length > 9 && p[9] == "d")
                Blood.SpriteTypes.Decorate(Doc, i, tile, mode);
        }
        sp.Ang = ang;
        BuildGeom.GetZsOfSlope(Doc, sector, x, y, out double ceil, out double floor);
        // centred, as Blood's maps have their sprites; its top and bottom as Blood works them out (Blood.SpriteExtents:
        // a thing settles with that bottom on the floor)
        sp.Cstat |= 128;
        switch (mode)
        {
            case "wall":
            {
                sp.Cstat = (sp.Cstat & ~(16 | 32)) | 16;
                // its middle at the crosshair, kept between the floor and the ceiling
                sp.Z = z;
                var (top, bottom) = Blood.SpriteExtents.Of(sp);
                sp.Z += z - (top + bottom) / 2;
                (top, bottom) = Blood.SpriteExtents.Of(sp);
                if (bottom > floor)
                    sp.Z = Blood.SpriteExtents.ZForBottom(sp, floor);
                else if (top < ceil)
                    sp.Z = Blood.SpriteExtents.ZForTop(sp, ceil);
                break;
            }
            case "ceiling":
                sp.Z = Blood.SpriteExtents.ZForTop(sp, ceil);
                break;
            default:
                sp.Z = Blood.SpriteExtents.ZForBottom(sp, floor);
                break;
        }
        Doc.NotifyChanged();
        UndoChanged();
        string name = Blood.BloodNames.Sprite(type);
        DisplayStatus($"sprite {i} ({name}) placed {(mode == "wall" ? "on the wall, wall aligned" : "on the " + mode)} in sector {sector}");
        if (Replaying)
            return;
        if (!ownTile && tile < 0)
        {
            // no picture yet: chosen now (shown on it as it is picked)
            PickTexture(new List<VisualTarget> { new(VisualPart.Sprite, sector, -1, i) });
        }
        else if (EditNewSprite)
            EditProperties(ElementKind.Sprite, new List<int> { i });
    }
}
