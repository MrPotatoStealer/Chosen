// Chosen core: original code (Build's index rules kept as the map format states them).
using System;
using System.Collections.Generic;
using System.Linq;
using Chosen.Shared.Blood.Map;
using Chosen.Shared.Engine.Geometry;

namespace Chosen.Forge.Document;

/// <summary>
/// The changes every mode makes go through here, so Build's rules hold: a point moves with its whole chain of walls,
/// a sprite's sector follows it, indices are renumbered everywhere they are stored when something is added or
/// removed, an x-structure exists exactly when Extra &gt; 0 and refers back to its owner. Callers take an undo level
/// first (UndoManager.CreateUndo) and call NotifyChanged after.
/// </summary>
public sealed partial class MapDocument
{
    /// <summary>The map's core as .MAPX bytes (undo's snapshot).</summary>
    public byte[] Snapshot() => MapXFile.Write(Map, Extras);

    /// <summary>Back to a snapshot (undo, redo, a cancelled drag); the file, its name and format stay.</summary>
    public void Restore(byte[] snapshot)
    {
        Put(snapshot);
        NotifyChanged();
    }

    /// <summary>
    /// A change tried on the map and taken back at once: the map as it would be after it (a copy), this one as it was
    /// (not marked changed, nobody told). The dialogs' live previews: their fields applied the way Apply will.
    /// </summary>
    public MapDocument TryOn(Action change)
    {
        var before = Snapshot();
        bool dirty = Dirty;
        try
        {
            change();
            return Copy();
        }
        catch (Exception)
        {
            return null;
        }
        finally
        {
            Put(before);
            Rebuild();
            Dirty = dirty;
        }
    }

    void Put(byte[] snapshot)
    {
        var (m, x) = MapXFile.Read(snapshot);
        m.Name = Map.Name;
        m.FromMapX = Map.FromMapX;
        // which layers show and which is current are the editor's view, not the edit: they stay (the same layers)
        if (x.Layers.Count == Extras.Layers.Count)
        {
            for (int i = 0; i < x.Layers.Count; i++)
                x.Layers[i].Visible = Extras.Layers[i].Visible;
            x.CurrentLayer = Extras.CurrentLayer;
        }
        x.LayersAuto = Extras.LayersAuto;
        Map = m;
        Extras = x;
    }

    /// <summary>A copy of the map (the same elements, indices and layers; its own file name, no file): the 2D view's
    /// and the 3D preview's map while they show the moving sectors elsewhere than stored (Off, On, Play).</summary>
    public MapDocument Copy()
    {
        var (m, x) = MapXFile.Read(Snapshot());
        m.Name = Map.Name;
        m.FromMapX = Map.FromMapX;
        return new MapDocument(m, x) { Format = Format };
    }

    // ---------------------------------------------------------------- points and sprites

    /// <summary>A point (its chain of walls) to (x, y).</summary>
    public void MovePoint(int point, int x, int y)
    {
        var p = Points[point];
        foreach (int w in p.Walls)
        {
            Walls[w].X = x;
            Walls[w].Y = y;
        }
        p.X = x;
        p.Y = y;
    }

    /// <summary>A sprite to (x, y), its sector the one now holding it (kept when none does, as Blood keeps it).</summary>
    public void MoveSprite(int i, int x, int y)
    {
        var s = Sprites[i];
        s.X = x;
        s.Y = y;
        UpdateSpriteSector(i);
    }

    /// <summary>A sprite's sector: of those holding its place, the one whose floor and ceiling hold its height (rooms
    /// over rooms share places: an item on a balcony is the balcony's), else the nearest in height; kept when no
    /// sector holds the place (as Blood keeps it).</summary>
    public void UpdateSpriteSector(int i)
    {
        var s = Sprites[i];
        // (its layer's sectors first: a sprite placed or moved in the room being edited, or moved to a layer, goes
        // into that layer's room: the game starts the player, wakes a monster, in the sector it names)
        int layer = Extras.Layers.Count > 1 ? SpriteLayer(i) : -1;
        var here = SectorsAt(s.X, s.Y);
        if (layer >= 0 && here.Any(sec => LayerOf(sec) == layer))
            here = here.Where(sec => LayerOf(sec) == layer).ToList();
        if (here.Count == 0)
            return;
        if (here.Count == 1)
        {
            s.SectNum = here[0];
            return;
        }
        int best = here[0];
        double bd = double.MaxValue;
        foreach (int sec in here)
        {
            BuildGeom.GetZsOfSlope(this, sec, s.X, s.Y, out double c, out double f);
            double d = s.Z < c ? c - s.Z : s.Z > f ? s.Z - f : 0;
            // a tie (inside both): the one it is in already, else the first
            if (d < bd || (d == bd && sec == s.SectNum))
            {
                bd = d;
                best = sec;
            }
        }
        s.SectNum = best;
    }

    public void SetSpriteZ(int i, int z) => Sprites[i].Z = z;

    /// <summary>
    /// The header's start when it lies in no sector (a new map's -1): Blood starts every player there unless a player
    /// start sprite overrides it (NBlood warp.cpp), so it becomes player 1's start (type 1, data 1 = 0), else a place in
    /// the sector under the old start, else in sector 0, on the floor. A start in a sector is left as it is. True if it
    /// changed.
    /// </summary>
    public bool RepairStart()
    {
        var m = Map;
        if (Sectors.Count == 0 || (m.StartSect >= 0 && m.StartSect < Sectors.Count))
            return false;
        var p1 = Sprites.FirstOrDefault(s => s.Type == 1 && s.Xsp is { Data1: 0 } && s.SectNum >= 0 && s.SectNum < Sectors.Count);
        if (p1 != null)
        {
            (m.StartX, m.StartY, m.StartZ, m.StartAng, m.StartSect) = (p1.X, p1.Y, p1.Z, p1.Ang, p1.SectNum);
            return true;
        }
        int x = m.StartX, y = m.StartY;
        int sect = BuildGeom.UpdateSector(this, x, y, -1);
        if (sect < 0)
        {
            sect = 0;
            var at = Labels.Length > 0 ? Labels[0] : new Godot.Vector2(Walls[Sectors[0].WallPtr].X, Walls[Sectors[0].WallPtr].Y);
            x = (int)Math.Round(at.X);
            y = (int)Math.Round(at.Y);
        }
        BuildGeom.GetZsOfSlope(this, sect, x, y, out _, out double floor);
        (m.StartX, m.StartY, m.StartZ, m.StartSect) = (x, y, (int)Math.Round(floor), sect);
        return true;
    }

    /// <summary>A sprite stood on the floor of its sector where it is (its z is its bottom).</summary>
    public void SpriteToFloor(int i)
    {
        var s = Sprites[i];
        if (s.SectNum < 0 || s.SectNum >= Sectors.Count)
            return;
        BuildGeom.GetZsOfSlope(this, s.SectNum, s.X, s.Y, out _, out double floor);
        s.Z = (int)Math.Round(floor);
    }

    /// <summary>A new sprite at (x, y) on the floor of the sector holding it (or the one given); its index.</summary>
    public int AddSprite(int x, int y, int type, int sector = -1)
    {
        if (sector < 0)
            sector = SectorsAt(x, y).OrderBy(s => SectorEditable(s) ? 0 : 1).FirstOrDefault(-1);
        int z = 0;
        if (sector >= 0)
        {
            BuildGeom.GetZsOfSlope(this, sector, x, y, out _, out double floor);
            z = (int)Math.Round(floor);
        }
        var s = new BSprite
        {
            X = x, Y = y, Z = z, SectNum = sector, StatNum = 0, Type = type, XRepeat = 64, YRepeat = 64, ClipDist = 32,
            Owner = -1, Index = Sprites.Count, FileIndex = Sprites.Count, Extra = -1,
        };
        Sprites.Add(s);
        RenumberSprites();
        // (in the layer being edited: Document/Layers.cs)
        if (Extras.Layers.Count > 1)
            SetSpriteLayer(Sprites.Count - 1, CurrentLayer);
        return Sprites.Count - 1;
    }

    /// <summary>Removes sprites (any order); the references to the sprites after them shift down.</summary>
    public void DeleteSprites(IEnumerable<int> indices)
    {
        var gone = new HashSet<int>(indices.Where(i => i >= 0 && i < Sprites.Count));
        if (gone.Count == 0)
            return;
        var map = new int[Sprites.Count];
        int n = 0;
        for (int i = 0; i < Sprites.Count; i++)
            map[i] = gone.Contains(i) ? -1 : n++;
        int Re(int old) => old >= 0 && old < map.Length ? map[old] : old;
        foreach (var sec in Sectors)
            if (sec.Xs is { } xs)
            {
                xs.Marker0 = Re(xs.Marker0);
                xs.Marker1 = Re(xs.Marker1);
            }
        // a marker's owner is its sector; other sprites' owner is a sprite
        foreach (var sp in Sprites)
            if (sp.StatNum != 10 && sp.Owner >= 0)
                sp.Owner = Re(sp.Owner);
        var keep = new List<BSprite>();
        for (int i = 0; i < Sprites.Count; i++)
            if (!gone.Contains(i))
                keep.Add(Sprites[i]);
        Sprites.Clear();
        Sprites.AddRange(keep);
        // the sprites' own layers follow them
        if (Extras.SpriteLayers.Count > 0)
        {
            var layers = Extras.SpriteLayers.Where((_, i) => i < map.Length && map[i] >= 0).ToList();
            Extras.SpriteLayers.Clear();
            Extras.SpriteLayers.AddRange(layers);
        }
        for (int i = Extras.SpriteExtras.Count - 1; i >= 0; i--)
        {
            var e = Extras.SpriteExtras[i];
            int ni = Re(e.Sprite);
            if (ni < 0)
                Extras.SpriteExtras.RemoveAt(i);
            else if (ni != e.Sprite)
                Extras.SpriteExtras[i] = e with { Sprite = ni };
        }
        var names = Extras.Names.Where(kv => kv.Key.kind == 2).ToList();
        foreach (var kv in names)
        {
            Extras.Names.Remove(kv.Key);
            int ni = Re(kv.Key.index);
            if (ni >= 0)
                Extras.Names[(2, ni)] = kv.Value;
        }
        // the lights on the sprites follow them, and go with them (Document/Lights.cs)
        for (int i = Extras.Lights.Count - 1; i >= 0; i--)
        {
            var l = Extras.Lights[i];
            if (!l.Attached)
                continue;
            int ni = Re(l.Sprite);
            if (ni < 0)
                Extras.Lights.RemoveAt(i);
            else
                l.Sprite = ni;
        }
        RenumberSprites();
    }

    /// <summary>The sprites' indices are their positions (Index, and FileIndex: the map now differs from the file),
    /// and each x-sprite refers back to its sprite.</summary>
    void RenumberSprites()
    {
        for (int i = 0; i < Sprites.Count; i++)
        {
            Sprites[i].Index = i;
            Sprites[i].FileIndex = i;
            if (Sprites[i].Xsp != null)
                Sprites[i].Xsp.Reference = i;
        }
    }

    /// <summary>Do two walls of one loop start at this place (a wall of no length between them, or a loop touching
    /// itself)?</summary>
    public bool TwoCornersOfOneLoopAt(Godot.Vector2I at)
    {
        for (int s = 0; s < Sectors.Count; s++)
        {
            var sec = Sectors[s];
            if (sec.WallNum <= 0 || sec.WallPtr < 0 || sec.WallPtr + sec.WallNum > Walls.Count)
                continue;
            foreach (var loop in BuildGeom.Loops(this, s))
            {
                int here = 0;
                foreach (int w in loop)
                    if (Walls[w].X == at.X && Walls[w].Y == at.Y && ++here > 1)
                        return true;
            }
        }
        return false;
    }

    /// <summary>Does another wall of a loop through this point start at the same place (a wall of no length)?</summary>
    public bool PointOnOwnLoop(int point)
    {
        var p = Points[point];
        foreach (int w in p.Walls)
        {
            int s = Walls[w].Sector;
            if (s < 0)
                continue;
            foreach (var loop in BuildGeom.Loops(this, s))
            {
                if (!loop.Contains(w))
                    continue;
                foreach (int o in loop)
                    if (o != w && Walls[o].X == p.X && Walls[o].Y == p.Y)
                        return true;
            }
        }
        return false;
    }

    // ---------------------------------------------------------------- x-structures

    static int NextExtra(IEnumerable<int> used)
    {
        var set = new HashSet<int>(used);
        int e = 1;
        while (set.Contains(e))
            e++;
        return e;
    }

    /// <summary>An item's x-structure removed (its triggers and data gone; Extra -1: none).</summary>
    public void RemoveXSector(int s)
    {
        Sectors[s].Xs = null;
        Sectors[s].Extra = -1;
    }

    public void RemoveXWall(int w)
    {
        Walls[w].Xw = null;
        Walls[w].Extra = -1;
    }

    public void RemoveXSprite(int i)
    {
        Sprites[i].Xsp = null;
        Sprites[i].Extra = -1;
    }

    public XSector EnsureXSector(int s)
    {
        var sec = Sectors[s];
        if (sec.Xs == null)
        {
            sec.Xs = XSector.Read(new byte[Map.XSectorSize], 0, Map.XSectorSize);
            if (sec.Extra <= 0)
                sec.Extra = NextExtra(Sectors.Where(o => o.Extra > 0).Select(o => o.Extra));
        }
        sec.Xs.Reference = s;
        return sec.Xs;
    }

    public XWall EnsureXWall(int w)
    {
        var wall = Walls[w];
        if (wall.Xw == null)
        {
            wall.Xw = XWall.Read(new byte[Map.XWallSize], 0, Map.XWallSize);
            if (wall.Extra <= 0)
                wall.Extra = NextExtra(Walls.Where(o => o.Extra > 0).Select(o => o.Extra));
        }
        wall.Xw.Reference = w;
        return wall.Xw;
    }

    public XSprite EnsureXSprite(int i)
    {
        var sp = Sprites[i];
        if (sp.Xsp == null)
        {
            sp.Xsp = XSprite.Read(new byte[Map.XSpriteSize], 0, Map.XSpriteSize);
            if (sp.Extra <= 0)
                sp.Extra = NextExtra(Sprites.Where(o => o.Extra > 0).Select(o => o.Extra));
        }
        sp.Xsp.Reference = i;
        return sp.Xsp;
    }

    // ---------------------------------------------------------------- walls

    /// <summary>
    /// Renumbers walls: newOf[old] is each wall's new index (a permutation; -1: removed). Every place a wall index is
    /// stored follows: sectors' first walls, Point2, NextWall, the x-walls' Reference; the wall list is reordered.
    /// The caller keeps each sector's walls contiguous (WallPtr, WallNum).
    /// </summary>
    public void RenumberWalls(int[] newOf)
    {
        int Re(int old) => old >= 0 && old < newOf.Length ? newOf[old] : old;
        foreach (var w in Walls)
        {
            w.Point2 = Re(w.Point2);
            if (w.NextWall >= 0)
            {
                w.NextWall = Re(w.NextWall);
                if (w.NextWall < 0)
                    w.NextSector = -1;
            }
        }
        foreach (var s in Sectors)
            s.WallPtr = Re(s.WallPtr);
        int count = newOf.Count(i => i >= 0);
        var re = new BWall[count];
        for (int i = 0; i < newOf.Length; i++)
            if (newOf[i] >= 0)
                re[newOf[i]] = Walls[i];
        Walls.Clear();
        Walls.AddRange(re);
        for (int i = 0; i < Walls.Count; i++)
            if (Walls[i].Xw != null)
                Walls[i].Xw.Reference = i;
        var names = Extras.Names.Where(kv => kv.Key.kind == 1).ToList();
        foreach (var kv in names)
        {
            Extras.Names.Remove(kv.Key);
            int ni = Re(kv.Key.index);
            if (ni >= 0)
                Extras.Names[(1, ni)] = kv.Value;
        }
    }

    /// <summary>
    /// Makes wall the sector's first wall (xmapedit's setFirstWall): its loop is stored first, starting at it, the
    /// sector's other loops after in their order. The slopes stay as numbers (they are relative to the first wall: a
    /// sloped sector's plane turns with it). False when the wall isn't the sector's.
    /// </summary>
    public bool SetFirstWall(int sector, int wall)
    {
        var sec = Sectors[sector];
        if (wall < sec.WallPtr || wall >= sec.WallPtr + sec.WallNum || wall == sec.WallPtr)
            return wall == sec.WallPtr;
        var loops = BuildGeom.Loops(this, sector);
        int li = loops.FindIndex(l => l.Contains(wall));
        // any loop's wall can be first (its loop is then stored first: Blood's maps have first walls on holes too)
        if (li < 0)
            return false;
        var order = new List<int>();
        var first = loops[li];
        int at = first.IndexOf(wall);
        for (int k = 0; k < first.Count; k++)
            order.Add(first[(at + k) % first.Count]);
        for (int l = 0; l < loops.Count; l++)
            if (l != li)
                order.AddRange(loops[l]);
        if (order.Count != sec.WallNum)
            return false;
        var newOf = Enumerable.Range(0, Walls.Count).ToArray();
        for (int k = 0; k < order.Count; k++)
            newOf[order[k]] = sec.WallPtr + k;
        int ptr = sec.WallPtr;
        RenumberWalls(newOf);
        sec.WallPtr = ptr;
        return true;
    }
}
