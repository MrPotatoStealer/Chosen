// Chosen Blood module (GPL v2): copying sectors and sprites and pasting them elsewhere (Blood's records copied field by
// field through Properties: a sector's, its walls', its sprites' and their x-structures; markers re-linked).
using System;
using System.Collections.Generic;
using System.Linq;
using Chosen.Forge.Document;
using Chosen.Forge.View;
using Chosen.Shared.Blood.Map;
using Chosen.Shared.Engine.Geometry;
using Godot;

namespace Chosen.Forge.Blood;

/// <summary>What Ctrl+C took: the map as it was (a snapshot), the sectors and sprites copied, or the walls (Walls mode: a
/// drawing to lay down again), the outlines to show while placing it, and the point placed on the grid (its first point,
/// else its first sprite).</summary>
public sealed class MapClip
{
    public byte[] Snapshot;
    public List<int> Sectors = new(), Sprites = new();
    public List<List<Vector2I>> Outlines = new();
    public HashSet<int> OpenOutlines = new();          // Outlines' indices drawn open (a chain of walls, not a loop)
    public List<Vector2I> SpriteAt = new();
    public Vector2I Ref;
    /// <summary>Walls copied: each line's ends (its front wall's way) and its walls in the snapshot (back -1: white).</summary>
    public List<(Vector2I a, Vector2I b, int front, int back)> Lines = new();
    /// <summary>Dynamic lights copied (Lights mode's, a copied sprite's attached one, a point light in a copied sector):
    /// each as it was, where it shone from (Build units) and how high over its sector's floor (null: in none).</summary>
    public List<ClipLight> Lights = new();

    public string What
    {
        get
        {
            if (Lines.Count > 0)
                return $"{Lines.Count} wall(s)";
            var parts = new List<string>();
            if (Sectors.Count > 0)
                parts.Add($"{Sectors.Count} sector(s)");
            if (Sprites.Count > 0 || Lights.Count == 0)
                parts.Add($"{Sprites.Count} sprite(s)");
            if (Lights.Count > 0)
                parts.Add($"{Lights.Count} light(s)");
            return string.Join(", ", parts);
        }
    }
}

/// <summary>A light in a copy: the light as it was (an attached one's Sprite the copied map's index), where it shone
/// from and its height over its sector's floor there (null: in no sector).</summary>
public sealed record ClipLight(MapLight Light, double X, double Y, double Z, double? Height);

public static class MapPaste
{
    /// <summary>The copy of sectors (rooms in them too: the caller's choice), sprites (a copied sector's own sprites
    /// and markers come with it) and lights (Lights mode's; a copied sprite's attached light and a copied sector's
    /// point lights come with them), or null with nothing.</summary>
    public static MapClip Copy(MapDocument d, IEnumerable<int> sectors, IEnumerable<int> sprites, IEnumerable<int> lights = null,
        bool sectorSprites = true)
    {
        var c = new MapClip { Snapshot = d.Snapshot() };
        c.Sectors = sectors.Distinct().Where(s => s >= 0 && s < d.Sectors.Count).OrderByDescending(s => Area(d, s)).ToList();
        var set = new HashSet<int>(c.Sectors);
        var sp = new SortedSet<int>(sprites.Where(i => i >= 0 && i < d.Sprites.Count));
        // (sectorSprites false: the sectors alone, Add Floor's copy)
        for (int i = 0; i < d.Sprites.Count && sectorSprites; i++)
            if (set.Contains(d.Sprites[i].SectNum) || (d.Sprites[i].StatNum == 10 && set.Contains(d.Sprites[i].Owner)))
                sp.Add(i);
        c.Sprites = sp.ToList();
        // the lights: those asked for, a copied sprite's, a copied sector's own (not in a copy of the sectors alone)
        var li = new SortedSet<int>((lights ?? Enumerable.Empty<int>()).Where(i => i >= 0 && i < d.Lights.Count));
        for (int i = 0; i < d.Lights.Count && d.HasLights; i++)
        {
            var l = d.Lights[i];
            if (l.Attached ? sp.Contains(l.Sprite) : sectorSprites && set.Contains(d.LightSector(i)))
                li.Add(i);
        }
        foreach (int i in li)
        {
            if (d.LightPlace(i) is not { } at)
                continue;
            int ls = d.LightSector(i);
            double? h = null;
            if (ls >= 0)
            {
                BuildGeom.GetZsOfSlope(d, ls, at.x, at.y, out _, out double f);
                h = f - at.z;
            }
            c.Lights.Add(new ClipLight(d.Lights[i].Clone(), at.x, at.y, at.z, h));
        }
        if (c.Sectors.Count == 0 && c.Sprites.Count == 0 && c.Lights.Count == 0)
            return null;
        foreach (int s in c.Sectors)
            foreach (var loop in BuildGeom.Loops(d, s))
                c.Outlines.Add(loop.Select(w => new Vector2I(d.Walls[w].X, d.Walls[w].Y)).ToList());
        c.SpriteAt = c.Sprites.Select(i => new Vector2I(d.Sprites[i].X, d.Sprites[i].Y)).ToList();
        c.Ref = c.Outlines.Count > 0 ? c.Outlines[0][0] : c.SpriteAt.Count > 0 ? c.SpriteAt[0]
            : new Vector2I((int)Math.Round(c.Lights[0].X), (int)Math.Round(c.Lights[0].Y));
        return c;
    }

    /// <summary>The copy of walls (Walls mode's lines) as a drawing: chained where they meet (an end shared by two of them
    /// and no more), each chain an open line or a closed loop; null with none.</summary>
    public static MapClip CopyWalls(MapDocument d, IEnumerable<int> lines)
    {
        var c = new MapClip { Snapshot = d.Snapshot() };
        foreach (int l in lines.Distinct().Where(l => l >= 0 && l < d.Lines.Count))
        {
            var (f, b) = d.Lines[l];
            var (ax, ay, bx, by) = BuildGeom.WallEnds(d, f);
            if (ax != bx || ay != by)
                c.Lines.Add((new Vector2I(ax, ay), new Vector2I(bx, by), f, b));
        }
        if (c.Lines.Count == 0)
            return null;
        foreach (var (pts, closed) in Chains(c.Lines.Select(x => (x.a, x.b)).ToList()))
        {
            if (!closed)
                c.OpenOutlines.Add(c.Outlines.Count);
            c.Outlines.Add(pts);
        }
        c.Ref = c.Outlines[0][0];
        return c;
    }

    /// <summary>Segments joined into chains: from each end met by one segment or by three or more, along the segments
    /// whose ends two share; then what is left: loops.</summary>
    static List<(List<Vector2I> pts, bool closed)> Chains(List<(Vector2I a, Vector2I b)> segs)
    {
        var at = new Dictionary<Vector2I, List<int>>();
        for (int i = 0; i < segs.Count; i++)
            foreach (var p in new[] { segs[i].a, segs[i].b })
            {
                if (!at.TryGetValue(p, out var l))
                    at[p] = l = new List<int>();
                l.Add(i);
            }
        var used = new bool[segs.Count];
        var chains = new List<(List<Vector2I>, bool)>();
        List<Vector2I> Walk(Vector2I from, int seg)
        {
            var pts = new List<Vector2I> { from };
            var p = from;
            while (seg >= 0 && !used[seg])
            {
                used[seg] = true;
                p = segs[seg].a == p ? segs[seg].b : segs[seg].a;
                pts.Add(p);
                seg = at[p].Count == 2 ? at[p].FirstOrDefault(o => !used[o], -1) : -1;
            }
            return pts;
        }
        foreach (var (p, l) in at.Where(kv => kv.Value.Count != 2).ToList())
            foreach (int seg in l)
                if (!used[seg])
                    chains.Add((Walk(p, seg), false));
        for (int i = 0; i < segs.Count; i++)
            if (!used[i])
            {
                var pts = Walk(segs[i].a, i);
                bool closed = pts.Count > 3 && pts[^1] == pts[0];
                if (closed)
                    pts.RemoveAt(pts.Count - 1);
                chains.Add((pts, closed));
            }
        return chains;
    }

    /// <summary>
    /// Copied walls laid down moved by (dx, dy), each chain as drawing it makes it with what lies there now: an open
    /// line splits the sectors it runs across from wall to wall, the parts of it outside every sector cut off
    /// (MapDocument.DrawLineAcross); a loop all within the sectors a room or a sector cut out of them as a closed
    /// drawing makes it, one reaching out of them a line round from where it leaves them. The new walls along the
    /// copied ones take their look (both sides the copied front wall's, but where it had a back wall the side going the
    /// other way that one's). The walls given a look, how many sectors were split or made, and what was left out.
    /// </summary>
    public static (List<int> walls, int made, string note) PasteWalls(MapDocument doc, MapClip clip, int dx, int dy)
    {
        var (from, _) = MapXFile.Read(clip.Snapshot);
        var off = new Vector2I(dx, dy);
        var before = new List<(Vector2I a, Vector2I b)>();
        var had = new HashSet<(int, int, int, int)>();
        for (int w = 0; w < doc.Walls.Count; w++)
        {
            if (doc.Walls[w].Sector < 0)
                continue;
            var (x1, y1, x2, y2) = BuildGeom.WallEnds(doc, w);
            had.Add((x1, y1, x2, y2));
            before.Add((new Vector2I(x1, y1), new Vector2I(x2, y2)));
        }
        int made = 0;
        var notes = new List<string>();
        bool OnWall(Vector2 p) => doc.LineNear(p.X, p.Y, 0.5, doc.LineEditable) >= 0;
        bool In(Vector2 p) => doc.EditableSectorsAt(p.X, p.Y).Count > 0;
        for (int k = 0; k < clip.Outlines.Count; k++)
        {
            var pts = clip.Outlines[k].Select(p => p + off).ToList();
            if (clip.OpenOutlines.Contains(k))
            {
                made += doc.DrawLineAcross(pts, out string note);
                if (note != null)
                    notes.Add(note);
                continue;
            }
            var samples = pts.Select(p => (Vector2)p).Concat(pts.Select((p, i) => ((Vector2)p + pts[(i + 1) % pts.Count]) * 0.5f)).ToList();
            if (samples.All(p => In(p) || OnWall(p)) && samples.Any(p => !OnWall(p)))
            {
                // within the sectors: a room in the one it fits in, else cut out of those it lies over
                var probe = samples.First(p => !OnWall(p));
                int parent = doc.EditableSectorsAt(probe.X, probe.Y).FirstOrDefault(n => doc.FitsAsRoom(n, pts), -1);
                string why = null;
                bool ok = parent >= 0 ? doc.InsertInnerLoop(parent, pts) >= 0 : doc.MakeSectorAcross(pts, out why) != null;
                doc.NotifyChanged();
                if (ok)
                    made++;
                else
                    notes.Add(why ?? "a loop couldn't be made there");
                continue;
            }
            // reaching out of them: an open line round the loop from a corner outside them (the parts outside cut off)
            int start = pts.FindIndex(p => !In(p));
            var line = start < 0 ? pts : pts.Skip(start).Concat(pts.Take(start)).ToList();
            line.Add(line[0]);
            made += doc.DrawLineAcross(line, out string n2);
            if (n2 != null)
                notes.Add(n2);
        }
        doc.NotifyChanged();
        // the new walls (not a piece of a wall there before) along a copied one: its look
        static bool OnSeg(Vector2I p, Vector2I a, Vector2I b) => MapDocument.SegmentDistance(p.X, p.Y, a.X, a.Y, b.X, b.Y) < 0.75;
        var walls = new List<int>();
        for (int w = 0; w < doc.Walls.Count; w++)
        {
            if (doc.Walls[w].Sector < 0)
                continue;
            var (x1, y1, x2, y2) = BuildGeom.WallEnds(doc, w);
            var p = new Vector2I(x1, y1);
            var q = new Vector2I(x2, y2);
            if (p == q || had.Contains((x1, y1, x2, y2)) || before.Any(e => OnSeg(p, e.a, e.b) && OnSeg(q, e.a, e.b)))
                continue;
            int li = clip.Lines.FindIndex(c => OnSeg(p, c.a + off, c.b + off) && OnSeg(q, c.a + off, c.b + off));
            if (li < 0)
                continue;
            var (ca, cb, front, back) = clip.Lines[li];
            bool same = (long)(q.X - p.X) * (cb.X - ca.X) + (long)(q.Y - p.Y) * (cb.Y - ca.Y) > 0;
            var o = from.Walls[!same && back >= 0 ? back : front];
            var t = doc.Walls[w];
            t.Picnum = o.Picnum;
            t.OverPicnum = o.OverPicnum;
            t.Shade = o.Shade;
            t.Pal = o.Pal;
            t.YRepeat = o.YRepeat;
            t.XPanning = o.XPanning;
            t.YPanning = o.YPanning;
            // (a white wall's blocking, masking and one-way flags mean nothing on the red wall it now is)
            t.Cstat = back >= 0 ? o.Cstat : o.Cstat & ~(1 | 16 | 32 | 64);
            double was = Math.Sqrt((double)(cb.X - ca.X) * (cb.X - ca.X) + (double)(cb.Y - ca.Y) * (cb.Y - ca.Y));
            double now = Math.Sqrt((double)(x2 - x1) * (x2 - x1) + (double)(y2 - y1) * (y2 - y1));
            t.XRepeat = was > 0 ? Math.Clamp((int)Math.Round(o.XRepeat * now / was), 1, 255) : o.XRepeat;
            walls.Add(w);
        }
        doc.NotifyChanged();
        return (walls, made, notes.Count > 0 ? string.Join("; ", notes.Distinct()) : null);
    }

    /// <summary>The sector whose walls come nearest (x, y) (-1: none).</summary>
    /// <summary>The floor (Build z) of the sector at (x, y) a light at height z is in (an editable one first; of those
    /// holding the point the one whose floor and ceiling hold z, else the nearest in height); null: none there.</summary>
    static double? FloorAt(MapDocument d, int x, int y, double z)
    {
        var here = d.EditableSectorsAt(x, y);
        if (here.Count == 0)
            here = d.SectorsAt(x, y);
        double? best = null;
        double bd = double.MaxValue;
        foreach (int s in here)
        {
            BuildGeom.GetZsOfSlope(d, s, x, y, out double c, out double f);
            double dist = z < c ? c - z : z > f ? z - f : 0;
            if (dist < bd)
                (bd, best) = (dist, f);
        }
        return best;
    }

    public static int NearestSector(MapDocument d, int x, int y)
    {
        int best = -1;
        double bd = double.MaxValue;
        for (int w = 0; w < d.Walls.Count; w++)
        {
            var (ax, ay, bx, by) = BuildGeom.WallEnds(d, w);
            double dist = MapDocument.SegmentDistance(x, y, ax, ay, bx, by);
            if (dist < bd && d.Walls[w].Sector is var s and >= 0 && s < d.Sectors.Count)
                (bd, best) = (dist, s);
        }
        return best;
    }

    /// <summary>
    /// A pasted sector's outline made as drawing that shape makes it (DrawModes.ClosedPlan): outside every sector (it may
    /// lie against them) a new sector joined to what it touches; else over sectors, or inside one along its walls (a
    /// coffin against its room's wall), the sectors it runs through cut along it, the part inside it the sector (UDB's
    /// drawing: MakeSectorAcross); only where that is refused (rooms over rooms there) laid over what lies there, unjoined
    /// (a sector the game and the preview reach from nowhere). The sector holding probe of those made, or -1 (why).
    /// </summary>
    static int Make(MapDocument doc, List<Vector2I> pts, Vector2 probe, out string why)
    {
        why = null;
        bool OnWall(Vector2 p) => doc.LineNear(p.X, p.Y, 0.5, doc.LineEditable) >= 0;
        bool In(Vector2 p) => doc.EditableSectorsAt(p.X, p.Y).Count > 0;
        var samples = pts.Select(p => (Vector2)p).Concat(pts.Select((p, i) => ((Vector2)p + pts[(i + 1) % pts.Count]) * 0.5f)).Append(probe).ToList();
        // within the map's sectors, against their walls from inside (a coffin by its room's wall): cut out of them as a
        // drawn shape is, the parts inside it (one a sector it runs through) joined into one
        if (samples.Any(p => In(p) && !OnWall(p)) && samples.All(p => In(p) || OnWall(p)))
        {
            var made = doc.MakeSectorAcross(pts, out why);
            if (made is { Count: > 0 })
            {
                doc.NotifyChanged();
                int keep = made.Where(n => BuildGeom.Inside(doc, n, probe.X, probe.Y)).DefaultIfEmpty(made[0]).First();
                // (joining renumbers: the kept one found again by the probe)
                foreach (var other in made.Where(n => n != keep).OrderByDescending(n => n))
                {
                    int k = keep;
                    if (doc.JoinSectors(k, other) == null)
                    {
                        doc.NotifyChanged();
                        keep = doc.EditableSectorsAt(probe.X, probe.Y).FirstOrDefault(k > other ? k - 1 : k);
                    }
                }
                return keep;
            }
        }
        // else (outside the sectors, or reaching out of them) joined to what it touches; failing that laid over
        int j = doc.MakeSectorJoined(pts, out why);
        return j >= 0 ? j : doc.MakeSector(pts, out why, over: true);
    }

    /// <summary>
    /// The walls round the pasted sectors (the sector each was pasted into or joined to: theirs, not the copy's) given
    /// the look the walls facing the copied sectors had where they were copied (a coffin's sides seen from its room are
    /// the room's walls): pictures, shade, palette, repeats (x scaled to the length), panning and flags; not their
    /// type, triggers or x-wall (the room's). A wall facing another pasted sector, or one the copy had nothing beside
    /// (a one-sided wall now joined), keeps its own.
    /// </summary>
    static void OutsideLooks(MapDocument doc, BloodMapFile from, Dictionary<int, int> newSector, int dx, int dy)
    {
        var pasted = new HashSet<int>(newSector.Values);
        foreach (var (s, ns) in newSector)
        {
            // the copy's walls by where they start and end (moved)
            var byEnds = new Dictionary<(int, int, int, int), int>();
            for (int w = from.Sectors[s].WallPtr; w < from.Sectors[s].WallPtr + from.Sectors[s].WallNum; w++)
            {
                var a = from.Walls[w];
                var b = from.Walls[a.Point2];
                byEnds.TryAdd((a.X + dx, a.Y + dy, b.X + dx, b.Y + dy), w);
            }
            var sec = doc.Sectors[ns];
            for (int w = sec.WallPtr; w < sec.WallPtr + sec.WallNum; w++)
            {
                int nw = doc.Walls[w].NextWall;
                if (nw < 0 || nw >= doc.Walls.Count || pasted.Contains(doc.Walls[nw].Sector))
                    continue;
                // the copied wall along this one (its whole length, else the one starting here)
                var (x1, y1, x2, y2) = BuildGeom.WallEnds(doc, w);
                int src = byEnds.TryGetValue((x1, y1, x2, y2), out int e) ? e
                    : byEnds.FirstOrDefault(kv => kv.Key.Item1 == x1 && kv.Key.Item2 == y1, new(default, -1)).Value;
                if (src < 0 || from.Walls[src].NextWall is not (>= 0 and var fo) || fo >= from.Walls.Count
                    || newSector.ContainsKey(from.Walls[fo].Sector))
                    continue;
                var o = from.Walls[fo];
                var t = doc.Walls[nw];
                t.Picnum = o.Picnum;
                t.OverPicnum = o.OverPicnum;
                t.Shade = o.Shade;
                t.Pal = o.Pal;
                t.YRepeat = o.YRepeat;
                t.XPanning = o.XPanning;
                t.YPanning = o.YPanning;
                t.Cstat = o.Cstat;
                // (x repeat for the length it now has: a wall split where the paste joined)
                var (fx1, fy1, fx2, fy2) = (from.Walls[fo].X, from.Walls[fo].Y, from.Walls[o.Point2].X, from.Walls[o.Point2].Y);
                double was = Math.Sqrt((double)(fx2 - fx1) * (fx2 - fx1) + (double)(fy2 - fy1) * (fy2 - fy1));
                double now = Math.Sqrt((double)(x2 - x1) * (x2 - x1) + (double)(y2 - y1) * (y2 - y1));
                t.XRepeat = was > 0 ? Math.Clamp((int)Math.Round(o.XRepeat * now / was), 1, 255) : o.XRepeat;
            }
        }
    }

    static double Area(IBuildData d, int s)
    {
        var (x0, y0, x1, y1) = BuildGeom.SectorBounds(d, s);
        return (double)(x1 - x0) * (y1 - y0);
    }

    /// <summary>The copy made again moved by (dx, dy): each sector from its outer loop (biggest first: one inside
    /// another pasted one a room in it, else inside a sector of the map a room in that, else a new sector joined to
    /// what it touches, else over what lies there), its
    /// properties and its walls' (by where they start), then the sprites in the new sectors, markers owned by the new
    /// sectors and their references set. A copied sector's holes not themselves copied come back filled. The new
    /// sectors and sprites, or why not.</summary>
    public static (List<int> sectors, List<int> sprites, string why) Paste(MapDocument doc, MapClip clip, int dx, int dy) =>
        Paste(doc, clip, dx, dy, out _);

    /// <summary>Paste, the lights made too: one attached to a sprite pasted is attached to the new one (its offset kept);
    /// any other (a point light, or one copied without its sprite: a point light where it shone from) moved by (dx, dy)
    /// as high over the floor it lands on as it was over its own (else at its height).</summary>
    public static (List<int> sectors, List<int> sprites, string why) Paste(MapDocument doc, MapClip clip, int dx, int dy, out List<int> lights)
    {
        lights = new List<int>();
        var (from, _) = MapXFile.Read(clip.Snapshot);
        var newSector = new Dictionary<int, int>();
        foreach (int s in clip.Sectors)
        {
            var outer = BuildGeom.OuterLoop(from, s);
            var pts = outer.Select(w => new Vector2I(from.Walls[w].X + dx, from.Walls[w].Y + dy)).ToList();
            // (a room: inside a sector pasted already, the smallest holding it)
            var probe = ((Vector2)pts[0] + pts[1] + pts[2]) / 3;
            int parent = newSector.Values.Where(n => BuildGeom.Inside(doc, n, probe.X, probe.Y)).OrderBy(n => Area(doc, n)).FirstOrDefault(-1);
            int ns;
            string why = null;
            // (else inside a sector of the map: a room in it, the smallest it fits in, as drawing a shape there makes)
            if (parent < 0)
                parent = pts.SelectMany(q => doc.EditableSectorsAt(q.X, q.Y)).Concat(doc.EditableSectorsAt(probe.X, probe.Y)).Distinct().Where(n => !newSector.ContainsValue(n) && doc.FitsAsRoom(n, pts))
                    .OrderBy(n => Area(doc, n)).FirstOrDefault(-1);
            if (parent >= 0)
                ns = doc.InsertInnerLoop(parent, pts);
            else
                ns = Make(doc, pts, probe, out why);
            if (ns < 0)
                return (null, null, $"sector {s}: {why ?? "it couldn't be made there"}");
            doc.NotifyChanged();
            newSector[s] = ns;
            Properties.Paste(doc, from, ElementKind.Sector, s, ns);
            // its walls, each from the copied wall starting at the same place (moved)
            var starts = new Dictionary<(int, int), int>();
            for (int w = from.Sectors[s].WallPtr; w < from.Sectors[s].WallPtr + from.Sectors[s].WallNum; w++)
                starts.TryAdd((from.Walls[w].X + dx, from.Walls[w].Y + dy), w);
            var sec = doc.Sectors[ns];
            for (int w = sec.WallPtr; w < sec.WallPtr + sec.WallNum; w++)
                if (starts.TryGetValue((doc.Walls[w].X, doc.Walls[w].Y), out int src))
                    Properties.PasteWall(doc, from, src, w);
        }
        OutsideLooks(doc, from, newSector, dx, dy);
        // sectors made later move earlier ones' indices? (no: MakeSector adds at the end, InsertInnerLoop too)
        var newSprite = new Dictionary<int, int>();
        foreach (int i in clip.Sprites)
        {
            var o = from.Sprites[i];
            int sect = newSector.TryGetValue(o.SectNum, out int ns) ? ns : -1;
            int ni = doc.AddSprite(o.X + dx, o.Y + dy, o.Type, sect);
            Properties.Paste(doc, from, ElementKind.Sprite, i, ni);
            var n = doc.Sprites[ni];
            n.Z = o.Z;
            n.Ang = o.Ang;
            n.StatNum = o.StatNum;
            n.Owner = o.StatNum == 10 && newSector.TryGetValue(o.Owner, out int owner) ? owner : o.Owner;
            if (sect < 0)
                doc.UpdateSpriteSector(ni);
            // outside every sector (a marker beside its room, pasted near the map's edge): Blood needs a sector all
            // the same (NBlood stops on a sprite of none): a marker its own sector's, else the nearest
            if (n.SectNum < 0)
                n.SectNum = n.StatNum == 10 && n.Owner >= 0 && n.Owner < doc.Sectors.Count ? n.Owner : NearestSector(doc, n.X, n.Y);
            newSprite[i] = ni;
        }
        // a pasted sector's markers: its own pasted ones
        foreach (var (s, ns) in newSector)
            if (from.Sectors[s].Xs is { } fx && doc.Sectors[ns].Xs is { } x)
            {
                if (newSprite.TryGetValue(fx.Marker0, out int m0))
                    x.Marker0 = m0;
                if (newSprite.TryGetValue(fx.Marker1, out int m1))
                    x.Marker1 = m1;
            }
        foreach (var cl in clip.Lights)
        {
            var n = cl.Light.Clone();
            if (n.Attached && newSprite.TryGetValue(n.Sprite, out int to))
                n.Sprite = to;
            else
            {
                n.Sprite = -1;
                n.X = (int)Math.Round(cl.X + dx);
                n.Y = (int)Math.Round(cl.Y + dy);
                n.Z = (int)Math.Round(cl.Z);
                if (cl.Height is { } h && FloorAt(doc, n.X, n.Y, cl.Z) is { } f)
                    n.Z = (int)Math.Round(f - h);
                // (its room there: the edited layer's where rooms share the place)
                n.Sector = -1;
                doc.SettleLight(n);
            }
            lights.Add(doc.AddLight(n));
        }
        doc.NotifyChanged();
        // (the new sectors in the clip's order: clip.Sectors[i] became sectors[i])
        return (clip.Sectors.Select(s => newSector[s]).ToList(), newSprite.Values.ToList(), null);
    }
}
