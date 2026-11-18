// Chosen Blood module (GPL v2): Door Properties: a door read back from the map (Make Door's, or Blood's own sliding
// (614) and swinging (617) sectors) and changed: its settings where they are (the sector's timing and channel, the
// leaves' pictures, the sound sprite, the walls pushed); its shape (Make Door's only: the notched jambs, the hinged pair
// of leaves) by taking it back to the bare doorway and making it again.
using System;
using System.Collections.Generic;
using System.Linq;
using Chosen.Forge.Document;
using Chosen.Shared.Blood.Map;
using Chosen.Shared.Engine.Geometry;
using Godot;

namespace Chosen.Forge.Blood;

public static partial class Doors
{
    /// <summary>A door as found in the map.</summary>
    public sealed class Door
    {
        public DoorKind Kind;
        public bool Swing => Swings(Kind);
        /// <summary>The doorway (a sliding door: its own sector; a swinging pair: the sector beside their room, -1
        /// when not found).</summary>
        public int Doorway = -1;
        /// <summary>The moving sectors: a sliding door's own; a swinging pair's first (the one pushed, passing its
        /// motion on) and second.</summary>
        public int[] Leaves;
        /// <summary>A swinging pair's room (the leaves lie in it).</summary>
        public int Room = -1;
        public int Channel;
        /// <summary>The channel the first leaf passes its motion on to the second on (-1: none).</summary>
        public int Link = -1;
        /// <summary>The walls pushed to open it (sending on its channel), and whether its sectors are pushed through
        /// their walls themselves (Blood's Wallpush).</summary>
        public readonly List<int> Pushers = new();
        public bool SectorPush;
        /// <summary>The leaves' broad sides and their ends (a swinging pair's: their room side too).</summary>
        public readonly List<int> Faces = new(), Edges = new();
        public readonly List<int> SoundSprites = new();
        /// <summary>A sliding door's notches: each jamb's six points (Make Door's shape).</summary>
        public Vector2I[][] Notches;
        /// <summary>Its options as they are (its shape's when it has Make Door's).</summary>
        public Options Now;
        /// <summary>Why its shape can't be changed (null: Make Door's shape, made again when changed).</summary>
        public string NotShaped;

        public string What => Kind switch
        {
            DoorKind.SwingPair => $"the swinging pair {Leaves[0]} and {Leaves[1]}",
            DoorKind.SwingSingle => $"the swinging leaf {Leaves[0]}",
            DoorKind.LiftUp => $"the door {Leaves[0]} lifting up",
            DoorKind.LiftDown => $"the door {Leaves[0]} lowering down",
            DoorKind.LiftSplit => $"the door {Leaves[0]} parting up and down",
            DoorKind.Pivot => $"the pivot {Leaves[0]}",
            _ => $"the sliding door {Leaves[0]}",
        };
    }

    /// <summary>The door sector s is part of (a sliding sector, a leaf of a swinging pair, or the doorway beside a pair
    /// Make Door made), or null.</summary>
    public static Door Find(MapDocument d, int s)
    {
        if (s < 0 || s >= d.Sectors.Count)
            return null;
        var sec = d.Sectors[s];
        if (sec.Type == SlideMarked && sec.Xs != null)
            return ReadSlide(d, s);
        if (sec.Type == Rotate && sec.Xs != null)
            return ReadSwing(d, Pair(d, s), -1);
        // (a lift that closes: its off or on heights meet; a lift carrying the player up a floor doesn't)
        if (sec.Type == ZMotion && sec.Xs is { } zx && (Math.Abs(zx.OffCeilZ - zx.OffFloorZ) <= 4 || Math.Abs(zx.OnCeilZ - zx.OnFloorZ) <= 4))
            return ReadLift(d, s);
        if (sec.Type == RotateMarked && sec.Xs != null)
            return ReadPivot(d, s);
        if (Sides(d, s) is { } sides)
            for (int l = 0; l < d.Sectors.Count; l++)
                if (d.Sectors[l].Type == Rotate && d.Sectors[l].Xs != null && sides.Contains(RoomOf(d, l)) && Pair(d, l) is var pair && pair[0] == l
                    && ReadSwing(d, pair, s) is { NotShaped: null } door)
                    return door;
        return null;
    }

    /// <summary>Do two options make the same shape (the fields the kind has: a lift has none; lengths within 4, a game
    /// unit's quarter, as the dialog rounds them)?</summary>
    static bool SameShape(Options a, Options b)
    {
        static bool Near(int x, int y) => Math.Abs(x - y) <= 4;
        if (a.Kind != b.Kind)
            return false;
        return a.Kind switch
        {
            DoorKind.SlidePair => Near(a.Thickness, b.Thickness) && Near(a.Lip, b.Lip),
            DoorKind.SwingPair => Near(a.Thickness, b.Thickness) && Near(a.Inset, b.Inset) && a.Side == b.Side,
            DoorKind.SwingSingle => Near(a.Thickness, b.Thickness) && Near(a.Inset, b.Inset) && a.Side == b.Side && a.Hinge == b.Hinge,
            DoorKind.Pivot => Near(a.Thickness, b.Thickness),
            _ => true,
        };
    }

    /// <summary>The rooms either side of a door's doorway, as Sides gives them once the door is taken away (a sliding
    /// door's sector has its notches: its red walls in their order); null when its doorway isn't known.</summary>
    public static int[] SidesOf(MapDocument d, Door door)
    {
        if (door.Swing)
            return door.Doorway >= 0 ? Sides(d, door.Doorway) : null;
        var red = WallsOf(d, door.Doorway).Where(w => d.Walls[w].NextSector >= 0).Select(w => d.Walls[w].NextSector).ToArray();
        return red.Length == 2 ? red : null;
    }

    /// <summary>Door Properties' change: the door sector s is part of given options o (one undo step for the caller).
    /// Its settings change where they are; a changed shape (kind, thickness, lip, inset, side) makes it again from its
    /// doorway (Make Door's shape only). Null when done, else why not.</summary>
    public static string Adjust(MapDocument d, int s, Options o, out string made)
    {
        made = null;
        if (Find(d, s) is not { } door)
            return $"sector {s} is no door (a sliding sector, 614, a swinging one, 617, or the doorway beside a swinging pair Make Door made)";
        var now = door.Now;
        int ch = o.Channel > 0 ? o.Channel : door.Channel;
        if (SameShape(o, now))
            return Settings(d, door, o with { Channel = ch }, out made);
        if (door.NotShaped != null)
            return $"its shape can't be changed ({door.NotShaped}); its settings can, the shape as it was";
        int doorway = Unmake(d, door);
        if (doorway < 0)
            return "the door couldn't be taken back to its doorway";
        string why = Make(d, doorway, o with { Channel = ch }, out made);
        if (why == null)
            made = "made again: " + made;
        return why;
    }

    // ---------------------------------------------------------------- reading

    static double WallLength(MapDocument d, int w)
    {
        var (ax, ay, bx, by) = BuildGeom.WallEnds(d, w);
        return Len(bx - ax, by - ay);
    }

    /// <summary>A leaf's walls into its broad sides and its ends (all sides when they are much the same length).</summary>
    static void ByLength(MapDocument d, IEnumerable<int> walls, List<int> faces, List<int> edges)
    {
        var l = walls.Select(w => (w, len: WallLength(d, w))).ToList();
        if (l.Count == 0)
            return;
        double min = l.Min(x => x.len), max = l.Max(x => x.len);
        foreach (var (w, len) in l)
            (max > min * 1.2 && len < (min + max) / 2 ? edges : faces).Add(w);
    }

    static IEnumerable<int> WallsOf(MapDocument d, int s) => Enumerable.Range(d.Sectors[s].WallPtr, d.Sectors[s].WallNum);

    /// <summary>The sector a leaf lies in (the other side of its walls).</summary>
    static int RoomOf(MapDocument d, int leaf) =>
        WallsOf(d, leaf).Select(w => d.Walls[w].NextSector).FirstOrDefault(n => n >= 0 && n != leaf, -1);

    /// <summary>A swinging sector with the one it passes its motion on to, or from (kCmdLink): first, second.</summary>
    static int[] Pair(MapDocument d, int s)
    {
        var xs = d.Sectors[s].Xs;
        int Rotating(Func<XSector, bool> f) => Enumerable.Range(0, d.Sectors.Count)
            .FirstOrDefault(i => i != s && d.Sectors[i].Type == Rotate && d.Sectors[i].Xs is { } x && f(x), -1);
        if (xs.Command == 5 && xs.TxID > 0 && Rotating(x => x.RxID == xs.TxID) is var b and >= 0)
            return new[] { s, b };
        if (xs.RxID > 0 && Rotating(x => x.Command == 5 && x.TxID == xs.RxID) is var a and >= 0)
            return new[] { a, s };
        return new[] { s };
    }

    /// <summary>What a door's walls and sprites say: its pushers, its sounds, its options.</summary>
    static void ReadRest(MapDocument d, Door door, IEnumerable<int> pushable, int side, int thick, int lip, int inset, int hinge = 0)
    {
        var x0 = d.Sectors[door.Leaves[0]].Xs;
        door.Channel = x0.RxID;
        foreach (int w in pushable.Distinct())
            if (d.Walls[w].Xw is { TriggerPush: not 0 } xw && xw.TxID == door.Channel && door.Channel > 0)
                door.Pushers.Add(w);
        door.SectorPush = door.Leaves.Any(l => d.Sectors[l].Xs.Wallpush != 0);
        for (int i = 0; i < d.Sprites.Count; i++)
            if (d.Sprites[i].Type == SoundSector && door.Leaves.Contains(d.Sprites[i].SectNum) && d.Sprites[i].Xsp != null)
                door.SoundSprites.Add(i);
        int face = door.Faces.Count > 0 ? d.Walls[door.Faces[0]].Picnum : -1;
        int edge = door.Edges.Count > 0 ? d.Walls[door.Edges[0]].Picnum : face;
        var snd = door.SoundSprites.Count > 0 ? d.Sprites[door.SoundSprites[0]].Xsp : null;
        int[] sounds = snd != null ? new[] { snd.Data1, snd.Data2, snd.Data3, snd.Data4 } : new int[4];
        var pw = door.Pushers.Count > 0 ? d.Walls[door.Pushers[0]].Xw : null;
        bool push = pw != null || door.SectorPush;
        int key = pw?.Key ?? (door.SectorPush ? x0.Key : 0);
        bool once = pw != null ? pw.TriggerOnce != 0 : door.SectorPush && x0.TriggerOnce != 0;
        door.Now = new Options(door.Kind, side, thick, lip, inset, face, edge, x0.BusyTimeA, x0.WaitTimeA, sounds, push, door.Channel, key, once, hinge);
    }

    /// <summary>A lifting door (600 closing: its off heights meet): its kind from what moves, its faces the walls facing
    /// it (pushed through them), its edge its bottom (the ceiling) or its top (lowering: the floor).</summary>
    static Door ReadLift(MapDocument d, int s)
    {
        var x = d.Sectors[s].Xs;
        bool up = x.OffCeilZ != x.OnCeilZ, down = x.OffFloorZ != x.OnFloorZ;
        var door = new Door { Kind = up && down ? DoorKind.LiftSplit : down ? DoorKind.LiftDown : DoorKind.LiftUp, Leaves = new[] { s }, Doorway = s };
        foreach (int w in WallsOf(d, s))
            if (d.Walls[w].NextWall >= 0)
                door.Faces.Add(d.Walls[w].NextWall);
        door.NotShaped = Doorway(d, s, out _) == null ? null : "not a doorway of four walls, two two-sided ones across";
        ReadRest(d, door, door.Faces, 0, 0, 64, 256);
        door.Now = door.Now with { Edge = door.Kind == DoorKind.LiftDown ? d.Sectors[s].FloorPicnum : d.Sectors[s].CeilingPicnum };
        return door;
    }

    /// <summary>A sector turning its marked walls (615): a pivot as Make Door makes it (a doorway with a leaf of four
    /// walls about its axis, along the way through: stored open) or another (its settings only).</summary>
    static Door ReadPivot(MapDocument d, int s)
    {
        var door = new Door { Kind = DoorKind.Pivot, Leaves = new[] { s }, Doorway = s };
        var marked = WallsOf(d, s).Where(w => (d.Walls[w].Cstat & (16384 | 32768)) != 0).ToList();
        ByLength(d, marked, door.Faces, door.Edges);
        door.NotShaped = PivotShape(d, s, marked, out int thick);
        ReadRest(d, door, marked, 0, door.NotShaped == null ? thick : 0, 64, 256);
        return door;
    }

    /// <summary>Is sector s Make Door's pivot: four walls in a rectangle round it (two red across), and inside a leaf of
    /// four marked walls centred on the axis marker, as long as the doorway is wide less the gaps, lying along the way
    /// through? Its thickness; null when it is.</summary>
    static string PivotShape(MapDocument d, int s, List<int> marked, out int thick)
    {
        thick = 0;
        const string not = "not Make Door's pivot (a leaf of four walls turning about the doorway's middle)";
        var loops = BuildGeom.Loops(d, s);
        if (loops.Count == 2 && loops[0].Count > 4 && loops[1].Count == 4 && loops[1].All(marked.Contains))
            return "its doorway was carved into the rooms for it to turn (undo Make Door to take the carving back)";
        if (loops.Count != 2 || loops[0].Count != 4 || loops[1].Count != 4 || !loops[1].All(marked.Contains) || marked.Count != 4)
            return not;
        if (!DoorwayLoop(d, loops[0], out var walls))
            return not;
        var (ax, ay, bx, by) = BuildGeom.WallEnds(d, walls[0]);
        double width = Len(bx - ax, by - ay);
        var c = Mid(walls.SelectMany(w => new[] { d.Walls[w].X, d.Walls[w].Y }).ToArray());
        int m = d.Sectors[s].Xs.Marker0;
        if (m < 0 || m >= d.Sprites.Count || new Vector2(d.Sprites[m].X, d.Sprites[m].Y).DistanceTo(c) > 2)
            return not;
        var lens = marked.Select(w => WallLength(d, w)).OrderBy(x => x).ToArray();
        if (Math.Abs(lens[3] - (width - 2 * PivotGap)) > 3 || Math.Abs(lens[2] - lens[3]) > 2 || Math.Abs(lens[0] - lens[1]) > 2)
            return not;
        thick = (int)Math.Round((lens[0] + lens[1]) / 2);
        return null;
    }

    static Door ReadSlide(MapDocument d, int s)
    {
        var door = new Door { Kind = DoorKind.SlidePair, Leaves = new[] { s }, Doorway = s };
        door.NotShaped = SlideShape(d, door, out int thick, out int lip);
        if (door.NotShaped != null)
            ByLength(d, WallsOf(d, s).Where(w => (d.Walls[w].Cstat & (16384 | 32768)) != 0), door.Faces, door.Edges);
        ReadRest(d, door, WallsOf(d, s), 0, door.NotShaped == null ? thick : 0, door.NotShaped == null ? lip : 64, 256);
        return door;
    }

    /// <summary>Is sliding door s Make Door's shape: two red thresholds opposite and between them each jamb notched
    /// for its leaf (P, the six points, Q)? Its leaves' thickness and lip; its faces and edges; null when it is.</summary>
    static string SlideShape(MapDocument d, Door door, out int thick, out int lip)
    {
        thick = lip = 0;
        int s = door.Leaves[0];
        var sec = d.Sectors[s];
        const string not = "not Make Door's sliding pair (two two-sided walls across, each jamb notched for its leaf)";
        if (sec.WallNum != 16)
            return not;
        var w = WallsOf(d, s).ToArray();
        // (one loop: each wall's end the next one's start)
        for (int k = 0; k < 16; k++)
            if (d.Walls[w[k]].Point2 != w[(k + 1) % 16])
                return not;
        int first = Array.FindIndex(w, i => d.Walls[i].NextSector >= 0);
        if (first < 0)
            return not;
        int At(int k) => w[(first + k) % 16];
        if (d.Walls[At(8)].NextSector < 0 || Enumerable.Range(1, 7).Concat(Enumerable.Range(9, 7)).Any(k => d.Walls[At(k)].NextSector >= 0))
            return not;
        double width = WallLength(d, At(0));
        var notches = new List<Vector2I[]>();
        int t0 = 0, l0 = 0;
        foreach (int c in new[] { 1, 9 })
        {
            var pts = Enumerable.Range(0, 8).Select(k =>
            {
                var (x1, y1, _, _) = BuildGeom.WallEnds(d, At(c + Math.Min(k, 6)));
                if (k < 7)
                    return new Vector2(x1, y1);
                var (_, _, x2, y2) = BuildGeom.WallEnds(d, At(c + 6));
                return new Vector2(x2, y2);
            }).ToArray();
            // P, A0..A5, Q
            var p = pts[0];
            var q = pts[7];
            double depth = p.DistanceTo(q);
            if (depth < 16)
                return not;
            var a = (q - p) / (float)depth;
            var o = new Vector2(a.Y, -a.X);
            double U(Vector2 x) => (x - p).Dot(o);
            double V(Vector2 x) => (x - p).Dot(a);
            double v1 = V(pts[1]), v2 = V(pts[6]), slide = U(pts[2]), lp = -U(pts[3]);
            bool Near(double x, double y) => Math.Abs(x - y) <= 1.5;
            if (!Near(U(pts[1]), 0) || !Near(U(pts[6]), 0) || !Near(V(pts[2]), v1) || !Near(V(pts[3]), v1) || !Near(V(pts[4]), v2)
                || !Near(V(pts[5]), v2) || !Near(U(pts[4]), -lp) || !Near(U(pts[5]), slide) || slide < 8 || lp < -1
                || !Near(v1, (depth - (v2 - v1)) / 2) || !Near(slide, width / 2 - lp))
                return not;
            int th = (int)Math.Round(v2 - v1), li = (int)Math.Round(lp);
            if (c == 9 && (Math.Abs(th - t0) > 1 || Math.Abs(li - l0) > 1))
                return not;
            (t0, l0) = (th, li);
            notches.Add(Enumerable.Range(1, 6).Select(k => new Vector2I((int)Math.Round(pts[k].X), (int)Math.Round(pts[k].Y))).ToArray());
            // A1-A2 and A3-A4 its faces, A2-A3 its edge
            door.Faces.Add(At(c + 2));
            door.Faces.Add(At(c + 4));
            door.Edges.Add(At(c + 3));
        }
        door.Notches = notches.ToArray();
        (thick, lip) = (t0, l0);
        return null;
    }

    /// <summary>A swinging sector or pair (first, second); doorway: the doorway beside its room (-1: looked for).</summary>
    static Door ReadSwing(MapDocument d, int[] leaves, int doorway)
    {
        var door = new Door { Kind = leaves.Length > 1 ? DoorKind.SwingPair : DoorKind.SwingSingle, Leaves = leaves, Room = RoomOf(d, leaves[0]) };
        var x0 = d.Sectors[leaves[0]].Xs;
        door.Link = leaves.Length > 1 ? x0.TxID : -1;
        foreach (int l in leaves)
            ByLength(d, WallsOf(d, l), door.Faces, door.Edges);
        // (their room's side too)
        foreach (var list in new[] { door.Faces, door.Edges })
            list.AddRange(list.Select(w => d.Walls[w].NextWall).Where(n => n >= 0).ToList());
        int side = 0, thick = 0, inset = 256, hinge = 0;
        door.NotShaped = "it lies in no room";
        if (door.Room >= 0)
        {
            var tries = doorway >= 0 ? new[] { doorway }
                : WallsOf(d, door.Room).Select(w => d.Walls[w].NextSector).Where(n => n >= 0 && !leaves.Contains(n)).Distinct().ToArray();
            door.NotShaped = "no doorway beside its room where Make Door would hinge it";
            foreach (int dw in tries)
                if (SwingShape(d, door, dw, out side, out thick, out inset, out hinge) == null)
                {
                    door.Doorway = dw;
                    door.NotShaped = null;
                    break;
                }
        }
        if (door.NotShaped != null)
            (side, thick, inset, hinge) = (0, 0, 256, 0);
        var pushable = leaves.SelectMany(l => WallsOf(d, l)).SelectMany(w => new[] { w, d.Walls[w].NextWall }).Where(w => w >= 0);
        ReadRest(d, door, pushable, side, thick, 64, inset, hinge);
        return door;
    }

    /// <summary>Is the pair hinged beside doorway dw as Make Door hinges it (each leaf four walls, its hinge marker
    /// the inset into the room from a corner of the threshold, or moved in from it, its length what is left)?</summary>
    static string SwingShape(MapDocument d, Door door, int dw, out int side, out int thick, out int inset, out int hinge)
    {
        side = thick = inset = hinge = 0;
        if (Doorway(d, dw, out var walls) != null)
            return "not a doorway";
        side = d.Walls[walls[0]].NextSector == door.Room ? 0 : d.Walls[walls[2]].NextSector == door.Room ? 1 : -1;
        if (side < 0)
            return "the doorway isn't beside its room";
        var (ax, ay, bx, by) = BuildGeom.WallEnds(d, walls[side == 1 ? 2 : 0]);
        double width = Len(bx - ax, by - ay);
        var t = new Vector2((float)((bx - ax) / width), (float)((by - ay) / width));
        var r = new Vector2(t.Y, -t.X);
        double? ins = null, th = null;
        bool single = door.Leaves.Length == 1;
        // (one leaf: hinged at either jamb, as long as the doorway is wide; a pair: the first at the threshold's start)
        foreach (int hg in single ? new[] { 0, 1 } : new[] { 0 })
        {
            string why = null;
            ins = th = null;
            for (int k = 0; k < door.Leaves.Length && why == null; k++)
            {
                int l = door.Leaves[k];
                int m = d.Sectors[l].Xs.Marker0;
                if (d.Sectors[l].WallNum != 4 || m < 0 || m >= d.Sprites.Count)
                    return "a leaf isn't four walls with its hinge";
                bool atEnd = single ? hg == 1 : k == 1;
                var h = new Vector2(d.Sprites[m].X, d.Sprites[m].Y);
                var corner = atEnd ? new Vector2(bx, by) : new Vector2(ax, ay);
                var along = atEnd ? -t : t;
                double i = (h - corner).Dot(r), mv = (h - corner).Dot(along);
                var lens = WallsOf(d, l).Select(w => WallLength(d, w)).OrderBy(x => x).ToArray();
                double shortSide = (lens[0] + lens[1]) / 2, longSide = (lens[2] + lens[3]) / 2;
                double want = single ? width - 16 - mv : width / 2 - 8 - mv;
                if (Math.Abs(longSide - want) > 3 || mv < -2 || (ins is { } i0 && Math.Abs(i0 - i) > 2) || (th is { } t1 && Math.Abs(t1 - shortSide) > 2))
                    why = single ? "its leaf isn't hinged as Make Door hinges one" : "its leaves aren't hinged as Make Door hinges them";
                ins = i;
                th = shortSide;
            }
            if (why == null)
            {
                hinge = hg;
                break;
            }
            if (!single || hg == 1)
                return why;
        }
        thick = (int)Math.Round(th.Value);
        inset = (int)Math.Round(ins.Value);
        return null;
    }

    // ---------------------------------------------------------------- changing

    /// <summary>The settings changed where they are: timing, channel, pictures, sounds, what opens it.</summary>
    static string Settings(MapDocument d, Door door, Options o, out string made)
    {
        made = null;
        var now = door.Now;
        int ch = o.Channel;
        if (door.Link > 0 && ch == door.Link)
            return $"channel {ch} is the one its first leaf passes its motion on to the second on: another";
        var x0 = d.Sectors[door.Leaves[0]].Xs;
        foreach (int l in door.Leaves)
            d.Sectors[l].Xs.BusyTimeA = d.Sectors[l].Xs.BusyTimeB = Math.Clamp(o.BusyTime, 0, 4095);
        x0.WaitTimeA = o.Once && o.Push ? 0 : Math.Clamp(o.WaitTime, 0, 4095);
        x0.RxID = ch;
        // the pictures (only those changed: a door of Blood's own may have faces of more than one)
        var sec0 = d.Sectors[door.Leaves[0]];
        if (o.Face >= 0 && o.Face != now.Face)
            foreach (int w in door.Faces)
            {
                if (door.Kind is DoorKind.LiftUp or DoorKind.LiftDown or DoorKind.LiftSplit && door.NotShaped == null)
                {
                    LiftFace(d.Walls[w], o.Face, (x0.OnFloorZ - x0.OnCeilZ) / (door.Kind == DoorKind.LiftSplit ? 2.0 : 1.0));
                    continue;
                }
                d.Walls[w].Picnum = o.Face;
                if (door.NotShaped == null)
                    OnePicture(d.Walls[w]);
            }
        if (o.Edge >= 0 && o.Edge != now.Edge)
        {
            foreach (int w in door.Edges)
                d.Walls[w].Picnum = o.Edge;
            // (a lift's edge: its bottom, the doorway's ceiling; lowering its top, the floor; parting both)
            if (door.Kind is DoorKind.LiftUp or DoorKind.LiftSplit)
                sec0.CeilingPicnum = o.Edge;
            if (door.Kind is DoorKind.LiftDown or DoorKind.LiftSplit)
                sec0.FloorPicnum = o.Edge;
        }
        // what opens it: the use key on its walls (those that push it now; else its faces, a pair's room side), with the
        // key and once; or only its channel (no wall of it pushes, its sectors not pushed through their walls)
        if (o.Push)
        {
            // (a lift is pushed through the walls facing it, as Blood's are)
            if (door.Pushers.Count == 0 && !door.SectorPush && Lifts(door.Kind))
            {
                x0.Wallpush = 1;
                door.SectorPush = true;
            }
            if (door.Pushers.Count == 0 && !door.SectorPush)
                door.Pushers.AddRange(door.Swing ? door.Faces.Where(w => d.Walls[w].Sector == door.Room) : door.Faces);
            foreach (int w in door.Pushers)
                Pushes(d.EnsureXWall(w), ch, o);
            if (door.SectorPush)
                foreach (int l in door.Leaves)
                {
                    d.Sectors[l].Xs.Key = Math.Clamp(o.Key, 0, 7);
                    d.Sectors[l].Xs.TriggerOnce = o.Once ? 1 : 0;
                }
        }
        else
        {
            foreach (int w in door.Pushers)
                if (d.Walls[w].Xw is { } xw)
                {
                    xw.TriggerPush = 0;
                    xw.TxID = 0;
                    xw.Command = 0;
                    xw.TriggerOn = xw.TriggerOff = 0;
                    xw.Key = 0;
                    xw.TriggerOnce = 0;
                }
            foreach (int l in door.Leaves)
                d.Sectors[l].Xs.Wallpush = 0;
        }
        // the sounds (last: deleting a sprite moves the ones after it)
        if (!o.SoundIds.SequenceEqual(now.SoundIds))
        {
            if (!o.HasSounds)
                d.DeleteSprites(door.SoundSprites);
            else if (door.SoundSprites.Count > 0)
                foreach (int i in door.SoundSprites)
                {
                    var x = d.Sprites[i].Xsp;
                    (x.Data1, x.Data2, x.Data3, x.Data4) = (o.SoundIds[0], o.SoundIds[1], o.SoundIds[2], o.SoundIds[3]);
                }
            else
            {
                int l = door.Leaves[0];
                var c = WallsOf(d, l).Select(w => new Vector2(d.Walls[w].X, d.Walls[w].Y)).Aggregate(Vector2.Zero, (acc, p) => acc + p) / d.Sectors[l].WallNum;
                AddSound(d, l, c, o.SoundIds);
            }
        }
        // (a lift Make Door made before its jambs were held still: held now)
        if (Lifts(door.Kind) && door.NotShaped == null && Doorway(d, door.Doorway, out var dw) == null)
            JambsStill(d, new[] { dw[1], dw[3] }, door.Kind);
        made = $"{door.What} (channel {ch}): its settings changed, {HowOpened(o, ch)}";
        return null;
    }

    /// <summary>The door taken away, its doorway left as Make Door found it: a sliding door's notches gone (each jamb one
    /// wall again), a pair's leaves dissolved into their room; their markers, sounds and x-sectors gone. The doorway's
    /// sector after (-1: couldn't).</summary>
    static int Unmake(MapDocument d, Door door)
    {
        if (Lifts(door.Kind) || door.Kind == DoorKind.Pivot)
        {
            // a lift back at its open heights; a pivot's leaf (the hole of marked walls) gone; their sector plain
            int s = door.Doorway;
            var xs = d.Sectors[s].Xs;
            if (Lifts(door.Kind))
            {
                d.Sectors[s].CeilingZ = xs.OnCeilZ;
                d.Sectors[s].FloorZ = xs.OnFloorZ;
                // (its jambs pegged as a still doorway's: to the ceiling)
                foreach (int w in WallsOf(d, s))
                    if (d.Walls[w].NextSector < 0)
                        d.Walls[w].Cstat &= ~4;
            }
            else if (BuildGeom.Loops(d, s).Skip(1).FirstOrDefault(l => l.All(w => (d.Walls[w].Cstat & (16384 | 32768)) != 0)) is not { } hole
                || !d.RemoveHole(hole[0]))
                return -1;
            var gone = Enumerable.Range(0, d.Sprites.Count).Where(i => (d.Sprites[i].StatNum == 10 && d.Sprites[i].Owner == s)
                || (d.Sprites[i].Type == SoundSector && d.Sprites[i].SectNum == s)).ToList();
            d.RemoveXSector(s);
            d.Sectors[s].Type = 0;
            d.DeleteSprites(gone);
            return s;
        }
        if (!door.Swing)
        {
            int s = door.Doorway;
            var gone = Enumerable.Range(0, d.Sprites.Count).Where(i => (d.Sprites[i].StatNum == 10 && d.Sprites[i].Owner == s)
                || (d.Sprites[i].Type == SoundSector && d.Sprites[i].SectNum == s)).ToList();
            d.RemoveXSector(s);
            d.Sectors[s].Type = 0;
            d.DeleteSprites(gone);
            foreach (var p in door.Notches.SelectMany(n => n))
            {
                int pt = Enumerable.Range(0, d.Points.Count).FirstOrDefault(i => d.Points[i].X == p.X && d.Points[i].Y == p.Y
                    && d.Points[i].Walls.Any(w => d.Walls[w].Sector == s), -1);
                if (pt < 0 || d.DeletePoint(pt) != null)
                    return -1;
            }
            // the jambs whole again: unmarked, their picture over their length
            foreach (int w in WallsOf(d, s))
            {
                d.Walls[w].Cstat &= ~(16384 | 32768);
                if (d.Walls[w].NextSector < 0)
                    d.Walls[w].XRepeat = (int)Math.Clamp(Math.Round(WallLength(d, w) / 128.0), 1, 255);
            }
            return d.Sectors[s].WallNum == 4 ? s : -1;
        }
        int room = door.Room, dw = door.Doorway;
        var leaves = door.Leaves.OrderByDescending(x => x).ToList();
        var sprites = Enumerable.Range(0, d.Sprites.Count).Where(i => (d.Sprites[i].StatNum == 10 && leaves.Contains(d.Sprites[i].Owner))
            || (d.Sprites[i].Type == SoundSector && leaves.Contains(d.Sprites[i].SectNum))).ToList();
        foreach (int l in leaves)
        {
            d.RemoveXSector(l);
            d.Sectors[l].Type = 0;
        }
        d.DeleteSprites(sprites);
        foreach (int l in leaves)
        {
            if (d.JoinSectors(room, l) != null)
                return -1;
            d.NotifyChanged();
            if (room > l)
                room--;
            if (dw > l)
                dw--;
        }
        return dw;
    }
}
