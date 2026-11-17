// Chosen core: original code (the editor's layers: rooms over rooms kept apart while one is edited).
using System;
using System.Collections.Generic;
using System.Linq;
using Chosen.Shared.Blood.Map;
using Chosen.Shared.Engine.Geometry;

namespace Chosen.Forge.Document;

/// <summary>
/// Layers (the .MAPX's LAYR, the editor's only: the game ignores them): each sector is in one, its walls and the
/// sprites in it with it. Only the current layer is edited (picked, selected, drawn on, cut, joined); another layer
/// that shows is drawn dimmed and locked (a new sector may still join its walls); a hidden one is out of the way
/// entirely (not drawn, not picked, not in the 3D preview; drawing goes through it as if it weren't there). New
/// sectors go into the current layer. Layers follow the sectors through every change (Commit).
/// </summary>
public sealed partial class MapDocument
{
    public List<MapLayer> Layers
    {
        get
        {
            if (Extras.Layers.Count == 0)
                Extras.Layers.Add(new MapLayer { Name = "Level" });
            return Extras.Layers;
        }
    }

    public int CurrentLayer
    {
        get => Math.Clamp(Extras.CurrentLayer, 0, Layers.Count - 1);
        set => Extras.CurrentLayer = Math.Clamp(value, 0, Layers.Count - 1);
    }

    public int LayerOf(int s) =>
        s >= 0 && s < Extras.SectorLayers.Count && Extras.SectorLayers[s] >= 0 && Extras.SectorLayers[s] < Layers.Count ? Extras.SectorLayers[s] : 0;

    public void SetLayer(int s, int layer)
    {
        while (Extras.SectorLayers.Count < Sectors.Count)
            Extras.SectorLayers.Add(0);
        Extras.SectorLayers[s] = layer;
    }

    /// <summary>Only one layer, or every layer shown and none locked: nothing is out of the way.</summary>
    bool OneLayer => Extras.Layers.Count <= 1;

    public bool SectorShown(int s) => OneLayer || s < 0 || Layers[LayerOf(s)].Visible;

    public bool SectorEditable(int s) => OneLayer || s < 0 || (LayerOf(s) == CurrentLayer && Layers[CurrentLayer].Visible);

    /// <summary>A shown sector of another layer: drawn dimmed, not edited.</summary>
    public bool SectorLocked(int s) => SectorShown(s) && !SectorEditable(s);

    public bool WallShown(int w) => w < 0 || w >= Walls.Count || SectorShown(Walls[w].Sector);
    public bool WallEditable(int w) => w < 0 || w >= Walls.Count || SectorEditable(Walls[w].Sector);

    public bool LineShown(MapLine l) => WallShown(l.Front) || (l.Back >= 0 && WallShown(l.Back));
    public bool LineEditable(MapLine l) => WallEditable(l.Front) || (l.Back >= 0 && WallEditable(l.Back));

    public bool PointShown(int p) => OneLayer || Points[p].Walls.Any(WallShown);
    public bool PointEditable(int p) => OneLayer || Points[p].Walls.Any(WallEditable);

    /// <summary>A sprite's layer: its own (moved into one, or placed while that layer was current), else its
    /// sector's.</summary>
    public int SpriteLayer(int i) =>
        i < Extras.SpriteLayers.Count && Extras.SpriteLayers[i] >= 0 && Extras.SpriteLayers[i] < Layers.Count ? Extras.SpriteLayers[i]
        : LayerOf(Sprites[i].SectNum);

    public void SetSpriteLayer(int i, int layer)
    {
        while (Extras.SpriteLayers.Count < Sprites.Count)
            Extras.SpriteLayers.Add(-1);
        Extras.SpriteLayers[i] = layer;
    }

    public bool SpriteShown(int i) => (SpritesGone == null || !SpritesGone.Contains(i)) && (OneLayer || Layers[SpriteLayer(i)].Visible);

    /// <summary>Sprites gone from a played copy (Play: broken, blown up; null: none).</summary>
    public HashSet<int> SpritesGone;
    public bool SpriteEditable(int i) => OneLayer || (SpriteLayer(i) == CurrentLayer && Layers[CurrentLayer].Visible);

    /// <summary>A sector the drawing operations leave alone (another layer's, shown or not): not cut, not split,
    /// not crossed, no island.</summary>
    public bool OutOfTheWay(int s) => !SectorEditable(s);

    /// <summary>A wall of a sector out of the way.</summary>
    public bool WallOutOfTheWay(int w) => w >= 0 && w < Walls.Count && OutOfTheWay(Walls[w].Sector);

    /// <summary>The sectors holding (x, y) that the current layer edits (smallest first).</summary>
    public List<int> EditableSectorsAt(double x, double y) => SectorsAt(x, y).Where(SectorEditable).ToList();

    /// <summary>A layer added (made current); its index.</summary>
    public int AddLayer(string name)
    {
        Layers.Add(new MapLayer { Name = name });
        Extras.LayersAuto = false;
        CurrentLayer = Layers.Count - 1;
        return CurrentLayer;
    }

    /// <summary>A layer deleted: its sectors go to the first layer (the last one left can't go).</summary>
    public bool DeleteLayer(int layer)
    {
        if (Layers.Count <= 1 || layer < 0 || layer >= Layers.Count)
            return false;
        for (int s = 0; s < Sectors.Count; s++)
        {
            int l = LayerOf(s);
            SetLayer(s, l == layer ? 0 : l > layer ? l - 1 : l);
        }
        for (int i = 0; i < Extras.SpriteLayers.Count; i++)
        {
            int l = Extras.SpriteLayers[i];
            if (l >= 0)
                Extras.SpriteLayers[i] = l == layer ? 0 : l > layer ? l - 1 : l;
        }
        Layers.RemoveAt(layer);
        if (CurrentLayer >= layer && CurrentLayer > 0)
            CurrentLayer--;
        Extras.LayersAuto = false;
        return true;
    }

    /// <summary>Sectors moved into a layer, every sprite in them with them (their own layer too), and sprites moved
    /// on their own.</summary>
    public void MoveToLayer(IEnumerable<int> sectors, IEnumerable<int> sprites, int layer)
    {
        var set = new HashSet<int>(sectors.Where(s => s >= 0 && s < Sectors.Count));
        foreach (int s in set)
            SetLayer(s, layer);
        for (int i = 0; i < Sprites.Count; i++)
            if (set.Contains(Sprites[i].SectNum) || (Sprites[i].StatNum == 10 && set.Contains(Sprites[i].Owner)))
                SetSpriteLayer(i, layer);
        foreach (int i in sprites)
            if (i >= 0 && i < Sprites.Count && !set.Contains(Sprites[i].SectNum))
            {
                // into the layer's sector holding it (a player start moved to the room below starts there)
                SetSpriteLayer(i, layer);
                int was = Sprites[i].SectNum;
                UpdateSpriteSector(i);
                // (outside its new room's height, a room above or below: on its floor)
                var sp = Sprites[i];
                if (sp.SectNum != was && sp.SectNum >= 0)
                {
                    BuildGeom.GetZsOfSlope(this, sp.SectNum, sp.X, sp.Y, out double c, out double f);
                    if (sp.Z < c || sp.Z > f)
                        sp.Z = (int)Math.Round(f);
                }
            }
        Extras.LayersAuto = false;
    }

    /// <summary>The layers as Commit writes the sectors back: each kept sector's own (by its object), a new one in
    /// the current layer.</summary>
    Dictionary<BSector, int> LayersByObject()
    {
        var d = new Dictionary<BSector, int>(ReferenceEqualityComparer.Instance);
        for (int s = 0; s < Sectors.Count; s++)
            d[Sectors[s]] = LayerOf(s);
        return d;
    }

    void RestoreLayers(Dictionary<BSector, int> byObject)
    {
        Extras.SectorLayers.Clear();
        foreach (var s in Sectors)
            Extras.SectorLayers.Add(byObject.TryGetValue(s, out int l) ? l : CurrentLayer);
    }
}
