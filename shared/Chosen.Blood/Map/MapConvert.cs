// Chosen Blood module (GPL v2): converting between Blood's .MAP and Chosen's .MAPX.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Chosen.Shared.Blood.Types;

namespace Chosen.Shared.Blood.Map;

public static class MapConvert
{
    /// <summary>A .MAP (as read: Parse/Read) into a .MAPX's content, with its .patch applied when given (sector heights
    /// into the data, sprite extras into SPRX), so the level plays as its .MAP did. The map is changed in place.</summary>
    public static (BloodMapFile map, MapXExtras extras) ToMapX(BloodMapFile m, IEnumerable<string> patchLines,
        string generator = "Chosen")
    {
        var x = new MapXExtras();
        x.SetInfo("generator", generator);
        x.SetInfo("created", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
        x.SetInfo("source_crc", m.Crc.ToString("x8", CultureInfo.InvariantCulture));
        if (patchLines != null)
        {
            var p = MapPatch.Parse(patchLines);
            p.ApplyHeights(m);
            x.SpriteExtras.AddRange(p.Sprites.Where(e => e.Sprite < m.Sprites.Count));
            MapPatch.ApplySprites(m, x.SpriteExtras);
        }
        m.FromMapX = true;
        return (m, x);
    }

    /// <summary>
    /// A .MAPX's content as .MAP bytes (BloodMapWriter), and what the .MAP can't hold: the sprite extras (Chosen reads
    /// them from a NAME.patch beside it: MapPatch.SpriteLines), names, the editor's view, unknown chunks; counts over
    /// the target's limits; NoOne's modern types when the target is vanilla Blood.
    /// </summary>
    public static (byte[] bytes, List<string> dropped) ToMap(BloodMapFile m, MapXExtras x, Limits target,
        bool bumpRevision = false)
    {
        var dropped = new List<string>();
        if (x != null)
        {
            if (x.SpriteExtras.Count > 0)
                dropped.Add($"{x.SpriteExtras.Count} sprite extras (voxel model fixes): only a .patch beside the .MAP keeps them");
            if (x.Names.Count > 0)
                dropped.Add($"{x.Names.Count} object names");
            if (x.Edit != null)
                dropped.Add("the editor's view");
            if (x.Layers.Count > 1 && !x.LayersAuto)
                dropped.Add($"{x.Layers.Count} layers (opened again, the rooms over rooms go into layers of their own)");
            if (x.Art.Count > 0)
                dropped.Add($"the map's art list ({x.Art.Count} set{(x.Art.Count == 1 ? "" : "s")}: a .MAP can't name them; ship their ART files with it)");
            if (x.HasLights)
                dropped.Add($"the map's own {x.Lights.Count} dynamic light{(x.Lights.Count == 1 ? "" : "s")} (a .MAP keeps none: the game makes its lights from the sprites)");
            if (x.HasAtmosphere)
                dropped.Add($"the map's own {x.Atmospheres.Count} atmosphere{(x.Atmospheres.Count == 1 ? "" : "s")} (a .MAP keeps none: the game guesses its fog, mist and weather)");
            foreach (var (cc, payload) in x.Unknown)
                dropped.Add($"chunk {cc.TrimEnd()} ({payload.Length} bytes)");
        }
        // pictures past Blood's tiles: kept, but only NBlood and Chosen draw them
        int high = m.Walls.Count(w => w.Picnum >= Files.MapArt.BloodTiles || w.OverPicnum >= Files.MapArt.BloodTiles)
            + m.Sectors.Count(s => s.FloorPicnum >= Files.MapArt.BloodTiles || s.CeilingPicnum >= Files.MapArt.BloodTiles)
            + m.Sprites.Count(s => s.Picnum >= Files.MapArt.BloodTiles);
        if (high > 0)
            dropped.Add($"{high} walls, sectors or sprites with pictures numbered {Files.MapArt.BloodTiles} and up (kept: Blood and Refreshed Supply have none past {Files.MapArt.BloodTiles - 1}; NBlood and Chosen draw them)");
        void Over(string what, int n, int max)
        {
            if (n > max)
                dropped.Add($"{n} {what}: more than {target.Name}'s {max}");
        }
        Over("sectors", m.Sectors.Count, target.Sectors);
        Over("walls", m.Walls.Count, target.Walls);
        Over("sprites", m.Sprites.Count, target.Sprites);
        Over("x-sectors", m.Sectors.Count(s => s.Extra > 0), target.XSectors);
        Over("x-walls", m.Walls.Count(w => w.Extra > 0), target.XWalls);
        Over("x-sprites", m.Sprites.Count(s => s.Extra > 0), target.XSprites);
        if (target == Limits.Vanilla)
        {
            var types = TypeTable.Load();
            int modern = m.Sprites.Count(s => types.Get(ObjKind.Sprite, s.Type)?.Modern == true)
                + m.Walls.Count(w => types.Get(ObjKind.Wall, w.Type)?.Modern == true)
                + m.Sectors.Count(s => types.Get(ObjKind.Sector, s.Type)?.Modern == true);
            if (modern > 0)
                dropped.Add($"{modern} objects of NoOne's modern types: vanilla Blood doesn't know them");
        }
        return (BloodMapWriter.Write(m, bumpRevision), dropped);
    }
}
