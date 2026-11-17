// Chosen Blood module (GPL v2): rooms over rooms as Blood links them (NBlood's warp.cpp, mirrors.cpp): Add Floor (a
// copy of sectors above or below, linked) and Make Portal (two sectors linked).
using System;
using System.Collections.Generic;
using System.Linq;
using Chosen.Forge.Document;
using Chosen.Shared.Blood.Map;
using Chosen.Shared.Engine.Geometry;
using Godot;

namespace Chosen.Forge.Blood;

/// <summary>
/// Blood's rooms over rooms: an upper link marker in the upper sector at its floor, a lower one in the lower sector at
/// its ceiling, the same data1 on both (warp.cpp pairs them), the upper floor and the lower ceiling tile 504 (the plane
/// the other room is drawn through and things cross; mirrors.cpp). The game moves and draws across by the markers'
/// offset (x, y and z), so the rooms may lie anywhere: over each other on separate layers, or apart. Kinds: plain
/// (stack, 11 / 12, as E1M1's), water (9 / 10: the lower room underwater, the surface water picture 2915 as most of
/// Blood's) and goo (13 / 14, the lower room underwater).
/// </summary>
public static class RoomLinks
{
    public static readonly string[] KindNames = { "Plain (stack markers 11 / 12)", "Water (9 / 10: the lower room underwater)", "Goo (13 / 14)" };
    public static readonly string[] KindCodes = { "plain", "water", "goo" };
    static readonly (int upper, int lower)[] Kinds = { (11, 12), (9, 10), (13, 14) };

    public static int KindOf(string code) => Math.Max(0, Array.IndexOf(KindCodes, code));

    /// <summary>What a link of this kind made of the planes between the rooms (the status line); upper: the upper
    /// sector, when given one already underwater shown as linked under water (Link).</summary>
    public static string PlanesNote(MapDocument d, int kind, int upper = -1) =>
        kind != 0 && upper >= 0 && upper < d.Sectors.Count && d.Sectors[upper].Xs?.Underwater == 1
            ? "the room above underwater already: linked as Blood links two rooms under water (E4M4's), stack markers and 504, no surface between them, the room under it underwater too"
            : PlanesNote(kind);

    static string PlanesNote(int kind) => kind switch
    {
        1 => "the planes between them water (2915), the room under it underwater",
        2 => "the planes between them 504, the room under it underwater",
        _ => "the planes between them 504",
    };

    /// <summary>A link number no link marker (data1) nor wall link or stack (its x-wall's data) of the map has: they
    /// share the numbers (xmapedit's findUnusedStack).</summary>
    public static int NextLinkId(MapDocument d)
    {
        var used = d.Sprites.Where(s => Array.IndexOf(AutoLayers.Upper, s.Type) >= 0 || Array.IndexOf(AutoLayers.Lower, s.Type) >= 0)
            .Select(s => s.Xsp?.Data1 ?? 0)
            .Concat(d.Walls.Where(w => w.Type is WallLink or WallStack).Select(w => w.Xw?.Data ?? 0)).ToList();
        return used.Count == 0 ? 1 : used.Max() + 1;
    }

    /// <summary>Wall types: a wall link (xmapedit's, walked through: not in NBlood) and a wall stack (NBlood's: a window
    /// showing the room behind the other wall of the same number).</summary>
    public const int WallLink = 500, WallStack = 501;
    public const int MirrorTile = 504;

    /// <summary>
    /// Make Portal on two walls, a wall stack as NBlood draws it (InitMirrors, DoWallMirrors): two red walls of the same
    /// length running opposite ways (each one's room on its own side), type 501 with the same x-wall data and the
    /// picture 504 on their one-way side (overpicnum, cstat one-way): each shows the room behind the other, shifted by
    /// the walls' offset (not turned or scaled), in place of what lies behind it; blocking (moves and shots: a window).
    /// The first line's side seen from: its back side's room (fromBack) or its front's. Why not, or null.
    /// </summary>
    public static string MakeWallPortal(MapDocument d, int lineA, int lineB, bool fromBack, out int wa, out int wb)
    {
        wa = wb = -1;
        if (lineA < 0 || lineB < 0 || lineA >= d.Lines.Count || lineB >= d.Lines.Count || lineA == lineB)
            return "select two walls";
        var (fa, ba) = d.Lines[lineA];
        var (fb, bb) = d.Lines[lineB];
        if (ba < 0 || bb < 0)
            return "a wall portal joins two red walls: each a window, its one-way picture (504) showing the other's room in place of what lies behind it";
        wa = fromBack ? ba : fa;
        var (ax, ay, bx, by) = BuildGeom.WallEnds(d, wa);
        var (cx, cy, ex, ey) = BuildGeom.WallEnds(d, fb);
        // (the second line's side running the other way)
        wb = (long)(bx - ax) * (ex - cx) + (long)(by - ay) * (ey - cy) < 0 ? fb : bb;
        (cx, cy, ex, ey) = BuildGeom.WallEnds(d, wb);
        double la = Math.Sqrt((double)(bx - ax) * (bx - ax) + (double)(by - ay) * (by - ay));
        double lb = Math.Sqrt((double)(ex - cx) * (ex - cx) + (double)(ey - cy) * (ey - cy));
        if (Math.Abs(la - lb) > 1)
            return $"the walls differ in length ({la:0} and {lb:0}): NBlood shows the other wall's room shifted, not scaled";
        double cross = (double)(bx - ax) * (ey - cy) - (double)(by - ay) * (ex - cx);
        if (Math.Abs(cross) > la * lb * 0.002)
            return "the walls aren't parallel: NBlood shows the other wall's room shifted, not turned";
        foreach (int w in new[] { wa, wb })
            if (d.Walls[w].Type is WallLink or WallStack || d.Walls[d.Walls[w].NextWall].Type is WallLink or WallStack)
                return $"wall {w} is a wall portal already";
        int id = NextLinkId(d);
        foreach (int w in new[] { wa, wb })
        {
            var wall = d.Walls[w];
            wall.Type = WallStack;
            d.EnsureXWall(w).Data = id;
            wall.OverPicnum = MirrorTile;
            // (one-way, not masked both sides; blocking moves and shots)
            wall.Cstat = (wall.Cstat & ~16) | 32 | 1 | 64;
        }
        return null;
    }

    /// <summary>Make Mirror: white walls given Blood's mirror picture (504: InitMirrors makes each a mirror of its
    /// room). The walls made mirrors, or why none.</summary>
    public static string MakeMirror(MapDocument d, IEnumerable<int> lines, out List<int> made)
    {
        made = new List<int>();
        foreach (int l in lines.Where(l => l >= 0 && l < d.Lines.Count))
        {
            var (f, b) = d.Lines[l];
            if (b >= 0)
                continue;
            d.Walls[f].Picnum = MirrorTile;
            made.Add(f);
        }
        return made.Count > 0 ? null : "a mirror is a white wall (one side, nothing behind it)";
    }

    /// <summary>A link marker already in sector s of these types (upper: 7 9 11 13; lower: 6 10 12 14), or -1.</summary>
    static int MarkerIn(MapDocument d, int s, int[] types) =>
        Enumerable.Range(0, d.Sprites.Count).FirstOrDefault(i => d.Sprites[i].SectNum == s && Array.IndexOf(types, d.Sprites[i].Type) >= 0, -1);

    /// <summary>Why sector upper can't take another link up from below, nor lower another down (Blood keeps one each
    /// way a sector), or null.</summary>
    static string Taken(MapDocument d, int upper, int lower)
    {
        if (MarkerIn(d, upper, AutoLayers.Upper) is int u and >= 0)
            return $"sector {upper}'s floor is linked already (marker sprite {u})";
        if (MarkerIn(d, lower, AutoLayers.Lower) is int l and >= 0)
            return $"sector {lower}'s ceiling is linked already (marker sprite {l})";
        return null;
    }

    /// <summary>
    /// The link between upper (its floor) and lower (its ceiling): both planes tile 504 (not parallax), the markers of
    /// kind placed at upperAt and lowerAt (their offset is the rooms': the copy's or the portal's), at the planes'
    /// heights there, linked by a new number (water: picture 2915 instead, and the lower sector underwater; goo: underwater;
    /// either under a room underwater already: stack markers and 504, the lower underwater). The marker sprites.
    /// </summary>
    static (int upper, int lower) Link(MapDocument d, int upper, int lower, int kind, Vector2 upperAt, Vector2 lowerAt)
    {
        kind = Math.Clamp(kind, 0, Kinds.Length - 1);
        // water or goo under a room underwater already: no surface between them, as Blood links two rooms under water
        // (E4M4's links 13 and 25: stack markers, 504 both sides, both rooms underwater)
        bool underUnder = kind != 0 && d.Sectors[upper].Xs?.Underwater == 1;
        var (tu, tl) = Kinds[underUnder ? 0 : kind];
        int id = NextLinkId(d);
        // (water as Blood's: the surface a water picture both sides, not seen through; plain and goo 504)
        int pic = kind == 1 && !underUnder ? WaterTile : PreviewMeshLink;
        var us = d.Sectors[upper];
        us.FloorPicnum = pic;
        us.FloorStat &= ~1;
        var ls = d.Sectors[lower];
        ls.CeilingPicnum = pic;
        ls.CeilingStat &= ~1;
        // water and goo: the lower room is under it (the player swims; every lower water marker's sector in Blood's
        // maps has it)
        if (kind != 0)
            d.EnsureXSector(lower).Underwater = 1;
        int Marker(int type, int s, Vector2 at, bool ceiling)
        {
            int i = d.AddSprite((int)Math.Round(at.X), (int)Math.Round(at.Y), type, s);
            SpriteTypes.Apply(d, i, type);
            var m = d.Sprites[i];
            // (as Blood's own: E1M1's stack markers)
            m.SectNum = s;
            m.Cstat = 128;
            m.Shade = -8;
            m.StatNum = 0;
            m.Owner = -1;
            BuildGeom.GetZsOfSlope(d, s, m.X, m.Y, out double cz, out double fz);
            m.Z = (int)Math.Round(ceiling ? cz : fz);
            d.EnsureXSprite(i).Data1 = id;
            // (in its sector's layer, not the one being edited)
            if (d.Extras.Layers.Count > 1)
                d.SetSpriteLayer(i, -1);
            return i;
        }
        int mu = Marker(tu, upper, upperAt, false);
        int ml = Marker(tl, lower, lowerAt, true);
        return (mu, ml);
    }

    /// <summary>
    /// Sector s about to be deleted: its link markers and their partners in the other rooms (the same data1, the
    /// matching type), the other rooms' linked planes given back a picture (s's other plane's: an Add Floor copy has the
    /// original's) and the underwater flag of a room under water whose surface goes. The marker sprites to delete.
    /// </summary>
    public static List<BSprite> Unlink(MapDocument d, int s)
    {
        var gone = new List<BSprite>();
        var ss = d.Sectors[s];
        foreach (var m in d.Sprites.Where(sp => sp.SectNum == s).ToList())
        {
            int ku = Array.IndexOf(AutoLayers.Upper, m.Type), kl = Array.IndexOf(AutoLayers.Lower, m.Type);
            if (ku < 0 && kl < 0)
                continue;
            gone.Add(m);
            int partnerType = ku >= 0 ? AutoLayers.Lower[ku] : AutoLayers.Upper[kl];
            int id = m.Xsp?.Data1 ?? 0;
            foreach (var p in d.Sprites.Where(sp => sp.Type == partnerType && (sp.Xsp?.Data1 ?? 0) == id && sp.SectNum != s).ToList())
            {
                gone.Add(p);
                if (p.SectNum < 0 || p.SectNum >= d.Sectors.Count)
                    continue;
                var o = d.Sectors[p.SectNum];
                if (ku >= 0)
                {
                    // s was above (its floor linked): the room under gets s's ceiling picture back on its ceiling
                    if (o.CeilingPicnum == PreviewMeshLink || o.CeilingPicnum == WaterTile)
                    {
                        o.CeilingPicnum = ss.CeilingPicnum;
                        o.CeilingStat = (o.CeilingStat & ~1) | (ss.CeilingStat & 1);
                    }
                    if (partnerType is 10 or 14 && o.Xs != null)
                        o.Xs.Underwater = 0;
                }
                else if (o.FloorPicnum == PreviewMeshLink || o.FloorPicnum == WaterTile)
                {
                    o.FloorPicnum = ss.FloorPicnum;
                    o.FloorStat = (o.FloorStat & ~1) | (ss.FloorStat & 1);
                }
            }
        }
        return gone.Distinct().ToList();
    }

    /// <summary>Tile 504: the linked plane (Blood's kMirrorTile).</summary>
    const int PreviewMeshLink = 504;

    /// <summary>Tile 2915: the water surface most of Blood's water links have (E2M1's).</summary>
    const int WaterTile = 2915;

    /// <summary>
    /// Add Floor: a copy of the sectors (their walls and properties, not their sprites) above (its floor the sectors'
    /// ceiling, its ceiling the same plane raised by height, 0: each sector's own height) or below (its ceiling their
    /// floor), at the same place on a layer (layer: its index, -1 a new one named for it; the other layers hidden
    /// while it is made, so the copy joins none of their walls) or beside the map (clear of it, east), each sector
    /// linked to its copy (kind). The layer the copy is in is made current. Why not, or null; made: the new sectors.
    /// </summary>
    public static string AddFloor(MapDocument d, List<int> sectors, bool above, bool beside, int layer, int kind, int height, out List<int> made)
    {
        made = new List<int>();
        sectors = sectors.Where(s => s >= 0 && s < d.Sectors.Count).Distinct().ToList();
        if (sectors.Count == 0)
            return "select the sectors to add a floor to";
        foreach (int s in sectors)
            if ((above ? Taken(d, -1, s) : Taken(d, s, -1)) is { } why0)
                return why0;
        // (the same place on these sectors' own layer: the copy would lie on them)
        if (!beside && layer >= 0 && layer < d.Layers.Count && sectors.FirstOrDefault(s => d.LayerOf(s) == layer, -1) is int on and >= 0)
            return $"sector {on} is in layer \"{d.Layers[layer].Name}\": the same place needs another layer (or a new one)";
        // the layer: a new one (named), or one there
        if (layer < 0 || layer >= d.Layers.Count)
            layer = d.AddLayer($"Floor {(above ? "above" : "below")} {string.Join(", ", sectors.Take(4))}");
        d.CurrentLayer = layer;
        int dx = 0;
        if (beside)
        {
            // east of everything, a little apart (whole grid units)
            var b = d.Bounds;
            int minX = sectors.Min(s => BuildGeom.SectorBounds(d, s).minX);
            dx = (int)(Math.Ceiling((b.End.X + 2048 - minX) / 1024.0) * 1024);
        }
        var clip = MapPaste.Copy(d, sectors, Array.Empty<int>(), null, sectorSprites: false);
        // (the same place: only the target layer seen while it is made)
        var shown = d.Layers.Select(l => l.Visible).ToList();
        if (!beside)
            for (int i = 0; i < d.Layers.Count; i++)
                d.Layers[i].Visible = i == layer;
        (List<int> news, List<int> _, string why) r;
        try
        {
            r = MapPaste.Paste(d, clip, dx, 0);
        }
        finally
        {
            for (int i = 0; i < d.Layers.Count && i < shown.Count; i++)
                d.Layers[i].Visible = shown[i];
        }
        if (r.why != null || r.news == null)
            return r.why ?? "the copy couldn't be made";
        for (int k = 0; k < clip.Sectors.Count && k < r.news.Count; k++)
        {
            int o = clip.Sectors[k], n = r.news[k];
            var os = d.Sectors[o];
            var ns = d.Sectors[n];
            d.SetLayer(n, layer);
            int h = height > 0 ? height : Math.Max(256, os.FloorZ - os.CeilingZ);
            if (above)
            {
                // its floor the sectors' ceiling plane, its ceiling that raised (the ceiling's look kept: a sky stays a sky)
                ns.FloorZ = os.CeilingZ;
                ns.FloorHeinum = os.CeilingHeinum;
                ns.FloorStat = (ns.FloorStat & ~2) | (os.CeilingStat & 2);
                ns.CeilingZ = os.CeilingZ - h;
                ns.CeilingHeinum = os.CeilingHeinum;
                ns.CeilingStat = (ns.CeilingStat & ~2) | (os.CeilingStat & 2);
                var at = d.Labels[o];
                Link(d, n, o, kind, at + new Vector2(dx, 0), at);
            }
            else
            {
                ns.CeilingZ = os.FloorZ;
                ns.CeilingHeinum = os.FloorHeinum;
                ns.CeilingStat = (ns.CeilingStat & ~2) | (os.FloorStat & 2);
                ns.FloorZ = os.FloorZ + h;
                ns.FloorHeinum = os.FloorHeinum;
                ns.FloorStat = (ns.FloorStat & ~2) | (os.FloorStat & 2);
                var at = d.Labels[o];
                Link(d, o, n, kind, at, at + new Vector2(dx, 0));
            }
            made.Add(n);
        }
        return null;
    }

    /// <summary>
    /// Make Portal: two sectors linked, the one whose floor is higher (in its middle) above the other, the upper's
    /// floor and the lower's ceiling made the linked planes; the markers set so their offset lays one room's middle
    /// (its bounds') over the other's. Why not, or null.
    /// </summary>
    public static string MakePortal(MapDocument d, int a, int b, int kind, out int upper, out int lower)
    {
        upper = lower = -1;
        if (a < 0 || b < 0 || a >= d.Sectors.Count || b >= d.Sectors.Count || a == b)
            return "select two sectors";
        double FloorAt(int s) => BuildGeom.PlaneZ(d, s, false, d.Labels[s].X, d.Labels[s].Y);
        (upper, lower) = FloorAt(a) <= FloorAt(b) ? (a, b) : (b, a);
        if (Taken(d, upper, lower) is { } why)
            return why;
        static Vector2 Mid(MapDocument d, int s)
        {
            var (x0, y0, x1, y1) = BuildGeom.SectorBounds(d, s);
            return new Vector2((x0 + x1) / 2f, (y0 + y1) / 2f);
        }
        // (the upper marker at its room's label, the lower at the matching place of its own room)
        var ua = d.Labels[upper];
        var la = ua + (Mid(d, lower) - Mid(d, upper));
        if (!BuildGeom.Inside(d, lower, la.X, la.Y))
        {
            la = d.Labels[lower];
            ua = la + (Mid(d, upper) - Mid(d, lower));
        }
        Link(d, upper, lower, kind, ua, la);
        return null;
    }
}
