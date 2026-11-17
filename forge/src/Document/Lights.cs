// Chosen core: original code. The map's dynamic lights (a .MAPX's LITE chunk: Chosen.Blood's MapLights.cs).
using System;
using System.Collections.Generic;
using System.Linq;
using Chosen.Forge.Data;
using Chosen.Shared.Blood.Map;
using Chosen.Shared.Engine.Geometry;

namespace Chosen.Forge.Document;

public sealed partial class MapDocument
{
    /// <summary>The map's own dynamic lights (Extras.Lights; HasLights: it has them, the game makes none from its
    /// sprites).</summary>
    public List<MapLight> Lights => Extras.Lights;

    /// <summary>The map has its own lights (placed or made here: the game gives it those alone), else it gets the
    /// game's from its sprites.</summary>
    public bool HasLights => Extras.HasLights;

    /// <summary>A light added (the map then has its own lights); its index.</summary>
    public int AddLight(MapLight l)
    {
        Extras.HasLights = true;
        Lights.Add(l);
        return Lights.Count - 1;
    }

    /// <summary>Lights removed (any order).</summary>
    public void DeleteLights(IEnumerable<int> indices)
    {
        foreach (int i in indices.Where(i => i >= 0 && i < Lights.Count).Distinct().OrderByDescending(i => i))
            Lights.RemoveAt(i);
    }

    /// <summary>The lights the sprites give by the game's rules (LightRules: torches and candles, light fixtures by their
    /// tile, decorations drawn fullbright), for the sprites that have none attached yet (the ones there, moved or
    /// changed, stay). The map then has its own lights. How many were added.</summary>
    public int GenerateLights()
    {
        var lit = new HashSet<int>(Lights.Where(l => l.Attached).Select(l => l.Sprite));
        int n = 0;
        Extras.HasLights = true;
        foreach (var l in LightRules.Generate(Map))
            if (!lit.Contains(l.Sprite))
            {
                Lights.Add(l);
                n++;
            }
        return n;
    }

    /// <summary>A tile's size and offset for the lights' spots (zeros without the pictures).</summary>
    public static (int w, int h, int yofs) LightTile(int tile)
    {
        var t = ForgeData.Ready && tile >= 0 ? ForgeData.Tiles[tile] : null;
        return t == null || t.Empty ? (0, 0, 0) : (t.Width, t.Height, t.YOffset);
    }

    /// <summary>Where light i shines from, Build units (z down): a point light's place, an attached one's spot on its
    /// sprite plus its offset; null when its sprite isn't in the map.</summary>
    public (double x, double y, double z)? LightPlace(int i) =>
        i < 0 || i >= Lights.Count ? null : LightPlace(Lights[i]);

    /// <summary>Where a light (the map's, or a changed copy of one: the light dialog's preview) shines from here.</summary>
    public (double x, double y, double z)? LightPlace(MapLight l) => LightRules.Place(l, Sprites, LightTile);

    /// <summary>The spot an attached light's offset is measured from (its sprite's), or (0, 0, 0) for a point light.</summary>
    public (double x, double y, double z) LightSpot(int i) => LightSpot(Lights[i]);

    public (double x, double y, double z) LightSpot(MapLight l) =>
        l.Attached && l.Sprite < Sprites.Count ? LightRules.Spot(l, Sprites[l.Sprite], LightTile) : (0, 0, 0);

    /// <summary>The lights changed and nothing else (their places, settings, how many): the map's views redraw only
    /// them (the 3D preview's lights made again without its meshes). LightsOnly is true while Changed runs.</summary>
    public void NotifyLightsChanged()
    {
        Dirty = true;
        LightsOnly = true;
        try
        {
            Changed?.Invoke();
        }
        finally
        {
            LightsOnly = false;
        }
    }

    /// <summary>The change being told (Changed) is to the lights alone (NotifyLightsChanged).</summary>
    public bool LightsOnly { get; private set; }

    /// <summary>A light moved to (x, y) in the map (z kept): a point light's place, an attached one's offset from its
    /// spot.</summary>
    public void MoveLight(int i, int x, int y)
    {
        var l = Lights[i];
        if (!l.Attached)
        {
            l.X = x;
            l.Y = y;
            SettleLight(l);
            return;
        }
        var (sx, sy, _) = LightSpot(i);
        l.X = (int)Math.Round(x - sx);
        l.Y = (int)Math.Round(y - sy);
    }

    /// <summary>The sector a light is in: an attached one's sprite's, a point light's of those holding its place the one
    /// whose floor and ceiling hold its height (else the nearest in height); -1: none.</summary>
    public int LightSector(int i) => LightSector(Lights[i]);

    public int LightSector(MapLight l)
    {
        if (l.Attached)
            return l.Sprite < Sprites.Count ? Sprites[l.Sprite].SectNum : -1;
        // its own room (SettleLight: where it was placed or moved to) while that still holds it
        if (l.Sector >= 0 && l.Sector < Sectors.Count && Sectors[l.Sector].WallNum >= 3 && BuildGeom.Inside(this, l.Sector, l.X, l.Y))
        {
            BuildGeom.GetZsOfSlope(this, l.Sector, l.X, l.Y, out double c0, out double f0);
            if (l.Z >= c0 - 16 && l.Z <= f0 + 16)
                return l.Sector;
        }
        var here = SectorsAt(l.X, l.Y);
        int best = -1;
        double bd = double.MaxValue, ba = double.MaxValue;
        bool be = false;
        foreach (int s in here)
        {
            BuildGeom.GetZsOfSlope(this, s, l.X, l.Y, out double c, out double f);
            double d = l.Z < c ? c - l.Z : l.Z > f ? l.Z - f : 0;
            // the one whose heights hold it (else the nearest); of sectors overlapping there at the same heights (rooms
            // of two layers drawn over each other: test.mapx's 0 and 15) the one of the layer edited, then the smallest:
            // the first found left a light placed in the layer edited locked, not to be clicked in 2D or 3D
            bool ed = SectorEditable(s);
            double a = s < Areas.Length ? Areas[s] : double.MaxValue;
            if (d < bd || d == bd && (ed && !be || ed == be && a < ba))
                (bd, best, be, ba) = (d, s, ed, a);
        }
        return best;
    }

    /// <summary>A point light placed or moved: its room kept (LightSector: the one it was in while that still holds
    /// it, else of the rooms there the edited layer's), so it stays in it whichever layer is edited later and the game
    /// knows it where rooms share its place (MapLight.Sector, saved). An attached light's is its sprite's.</summary>
    public void SettleLight(MapLight l) => l.Sector = l.Attached ? -1 : LightSector(l);

    public void SettleLight(int i)
    {
        if (i >= 0 && i < Lights.Count)
            SettleLight(Lights[i]);
    }

    /// <summary>
    /// Lights moved into a layer (Move to Layer in Lights mode): a point light into that layer's room holding its place
    /// (of several, the one whose heights hold it, else the nearest in height, then the smallest), as high over its
    /// floor as before when it was outside that room's heights (a room below: as high over that one's floor); an
    /// attached one with its sprite (MoveToLayer). Those with no room of that layer at their place stay. The counts
    /// moved, of those attached, and left.
    /// </summary>
    public (int moved, int attached, int left) MoveLightsToLayer(IEnumerable<int> lights, int layer)
    {
        int moved = 0, attached = 0, left = 0;
        var sprites = new List<int>();
        foreach (int i in lights.Distinct())
        {
            if (i < 0 || i >= Lights.Count)
                continue;
            var l = Lights[i];
            if (l.Attached)
            {
                if (l.Sprite < Sprites.Count)
                {
                    sprites.Add(l.Sprite);
                    attached++;
                }
                continue;
            }
            int best = -1;
            double bd = double.MaxValue, ba = double.MaxValue;
            foreach (int s in SectorsAt(l.X, l.Y))
            {
                if (LayerOf(s) != layer)
                    continue;
                BuildGeom.GetZsOfSlope(this, s, l.X, l.Y, out double c, out double f);
                double d = l.Z < c ? c - l.Z : l.Z > f ? l.Z - f : 0;
                double a = s < Areas.Length ? Areas[s] : double.MaxValue;
                if (d < bd || d == bd && a < ba)
                    (bd, best, ba) = (d, s, a);
            }
            if (best < 0)
            {
                left++;
                continue;
            }
            if (bd > 0)
            {
                // (outside its new room's heights: as high over its floor as it was over its own room's)
                int was = LightSector(l);
                BuildGeom.GetZsOfSlope(this, best, l.X, l.Y, out double c, out double f);
                double over = 0;
                if (was >= 0)
                {
                    BuildGeom.GetZsOfSlope(this, was, l.X, l.Y, out _, out double f0);
                    over = f0 - l.Z;
                }
                double z = f - over;
                l.Z = (int)Math.Round(z < c ? c + Math.Min(256, (f - c) / 2) : z > f ? f - Math.Min(256, (f - c) / 2) : z);
            }
            l.Sector = best;
            moved++;
        }
        if (sprites.Count > 0)
            MoveToLayer(Array.Empty<int>(), sprites, layer);
        return (moved, attached, left);
    }

    /// <summary>A light shows and is edited with its sprite's layer, a point light with its sector's (none: always).</summary>
    public bool LightShown(int i) => LightShown(Lights[i]);

    public bool LightShown(MapLight l)
    {
        if (l.Attached)
            return l.Sprite < Sprites.Count && SpriteShown(l.Sprite);
        int s = LightSector(l);
        return s < 0 || SectorShown(s);
    }

    public bool LightEditable(int i)
    {
        var l = Lights[i];
        if (l.Attached)
            return l.Sprite < Sprites.Count && SpriteEditable(l.Sprite);
        int s = LightSector(i);
        return s < 0 || SectorEditable(s);
    }
}
