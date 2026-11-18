// Chosen Blood module (GPL v2): Make Door: a doorway made a door as Blood's maps make them (E1M1's): a sliding pair
// (kSectorSlideMarked 614: the doorway's jambs notched for two leaves that slide into the walls) and a swinging pair
// (kSectorRotate 617: two leaf sectors in the room beyond, turning about their hinges). NBlood's triggers.cpp
// (TranslateSector, HDoorBusy, RDoorBusy, trInit) is the reference: the map stores a door open (ON); trInit takes it
// back to OFF, the marked walls by marker 0 - marker 1 (a swing: turned back by the axis marker's angle).
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
    public const int ZMotion = 600, SlideMarked = 614, RotateMarked = 615, Rotate = 617;
    /// <summary>kSoundSector: data1..4 the sounds of going off, stopping off, going on, stopping on.</summary>
    public const int SoundSector = 709;
    /// <summary>E1M1's pictures and sounds: its sliding pair (sector 4) and its swinging pair (sectors 50 and 51); a
    /// door lowering into the floor: the bars Blood's levels have most on theirs, the dark stone over them.</summary>
    public const int SlideFace = 97, SlideEdge = 90, SwingFace = 254, SwingEdge = 20, LowerFace = 211, LowerEdge = 379;

    /// <summary>The doors Make Door makes: a sliding pair (614), a swinging pair or one leaf (617), a door lifting into
    /// the ceiling, lowering into the floor or parting both ways (the doorway itself, 600), a leaf turning about its
    /// middle in the doorway (615).</summary>
    public enum DoorKind { SlidePair, SwingPair, SwingSingle, LiftUp, LiftDown, LiftSplit, Pivot }

    /// <summary>Each kind's word in the command and its name in the dialog (in DoorKind's order).</summary>
    public static readonly string[] KindWords = { "slide", "swing", "single", "up", "down", "split", "pivot" };
    public static readonly string[] KindNames =
    {
        "Sliding pair (into the walls)", "Swinging pair (into a room)", "Swinging leaf (one, hinged at a jamb)",
        "Lifts up (into the ceiling)", "Lowers down (into the floor)", "Splits (top up, bottom down)",
        "Pivot (a leaf turning about its middle)",
    };

    public static bool Swings(DoorKind k) => k is DoorKind.SwingPair or DoorKind.SwingSingle;
    public static bool Lifts(DoorKind k) => k is DoorKind.LiftUp or DoorKind.LiftDown or DoorKind.LiftSplit;
    /// <summary>Kinds whose leaves have a thickness (a lift is the doorway itself).</summary>
    public static bool HasLeaves(DoorKind k) => !Lifts(k);

    /// <summary>E1M1's pictures and sounds for a kind: wood for what swings and turns, stone for the rest.</summary>
    public static int DefaultFace(DoorKind k) => Swings(k) || k == DoorKind.Pivot ? SwingFace : k == DoorKind.LiftDown ? LowerFace : SlideFace;
    public static int DefaultEdge(DoorKind k) => Swings(k) || k == DoorKind.Pivot ? SwingEdge : k == DoorKind.LiftDown ? LowerEdge : SlideEdge;
    public static int[] DefaultSounds(DoorKind k) => Swings(k) || k == DoorKind.Pivot ? SwingSounds : SlideSounds;
    public static readonly int[] SlideSounds = { 106, 157, 106, 157 }, SwingSounds = { 101, 0, 100, 160 };

    /// <summary>Door sounds as Blood's levels have them (a sector sound sprite's data1..4: opening starts, opening
    /// stops, closing starts, closing stops: SectorStartSound plays data1 going on, data3 going off; SectorEndSound
    /// data2 on, data4 off), those its doors use most: E1M1's sliding and swinging pairs, then others.</summary>
    public static readonly (string Name, int[] Ids)[] SoundSets =
    {
        ("Stone slab (E1M1's sliding pair)", SlideSounds),
        ("Stone slab, heavy stop", new[] { 106, 160, 106, 160 }),
        ("Sliding door", new[] { 103, 0, 103, 0 }),
        ("Sliding door, clunk", new[] { 121, 156, 121, 156 }),
        ("Creaking slide, latch", new[] { 119, 203, 119, 203 }),
        ("Wooden door (E1M1's swinging pair)", SwingSounds),
        ("Wooden door, door closing", new[] { 101, 0, 100, 162 }),
        ("Small creak", new[] { 117, 0, 117, 156 }),
        ("Machinery (a lift's)", new[] { 105, 153, 105, 153 }),
    };

    /// <summary>
    /// The options (Build units, Blood's tenths of a second). Face, Edge: the leaves' pictures (-1: E1M1's for the
    /// kind). Sounds: data1..4 of the door's sound sprite (null: E1M1's for the kind; all 0: none). Push: the use key on
    /// the leaves opens and closes it (else only its channel: a switch or a trigger elsewhere sends On, Off or Toggle).
    /// Channel: the one it hears (0: the first free from 100). Key: the key the use key needs (Blood's 1..6, on the
    /// walls pushed). Once: the leaves open it once and it stays open (the walls send On, each spent once pushed).
    /// Hinge: a single swinging leaf's, 0 the left jamb, 1 the right (seen from the doorway, looking into the room it
    /// swings into). A lift's faces are the rooms' walls facing it, its edge its bottom (the doorway's ceiling), or its
    /// top (the floor) lowering, both splitting. Carve: a pivot in a doorway too thin for its turn gets the room it
    /// needs cut out of the rooms either side.
    /// </summary>
    public sealed record Options(DoorKind Kind, int Side, int Thickness, int Lip, int Inset, int Face, int Edge, int BusyTime, int WaitTime,
        int[] Sounds, bool Push = true, int Channel = 0, int Key = 0, bool Once = false, int Hinge = 0, bool Carve = false)
    {
        public bool Swing => Swings(Kind);
        public bool Lift => Lifts(Kind);
        /// <summary>The sounds this door makes (E1M1's for the kind when none are given).</summary>
        public int[] SoundIds => Sounds ?? DefaultSounds(Kind);
        public bool HasSounds => SoundIds.Length == 4 && SoundIds.Any(i => i > 0);
        public int FacePicture => Face >= 0 ? Face : DefaultFace(Kind);
        public int EdgePicture => Edge >= 0 ? Edge : DefaultEdge(Kind);
    }

    /// <summary>The window command making the door: "makedoor &lt;slide|swing|single|up|down|split|pivot&gt; &lt;side&gt; &lt;thickness&gt; &lt;lip&gt; &lt;inset&gt;
    /// &lt;face&gt; &lt;busy&gt; &lt;wait&gt; &lt;sounds 0|1&gt; &lt;sector&gt;" (as the first recordings have it: a picture for faces and edges,
    /// E1M1's sounds or none) and then the rest named: edge=, sounds=a,b,c,d, by=push|channel, channel=, key=, once=,
    /// hinge=.</summary>
    public static string Command(Options o, int s, string verb = "makedoor")
    {
        var ids = o.SoundIds;
        return string.Join(" ", verb, KindWords[(int)o.Kind], o.Side, o.Thickness, o.Lip, o.Inset, o.Face, o.BusyTime, o.WaitTime,
            o.HasSounds ? 1 : 0, s, $"edge={o.Edge}", $"sounds={string.Join(",", ids)}", $"by={(o.Push ? "push" : "channel")}",
            $"channel={o.Channel}", $"key={o.Key}", $"once={(o.Once ? 1 : 0)}", $"hinge={o.Hinge}", $"carve={(o.Carve ? 1 : 0)}");
    }

    /// <summary>Command's options back (p: its words); null when too few.</summary>
    public static Options Parse(string[] p)
    {
        if (p.Length < 11)
            return null;
        static int N(string t, int def) => int.TryParse(t, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int v) ? v : def;
        var named = p.Skip(11).Select(t => t.Split('=', 2)).Where(a => a.Length == 2).GroupBy(a => a[0]).ToDictionary(g => g.Key, g => g.Last()[1]);
        int Named(string k, int def) => named.TryGetValue(k, out var v) ? N(v, def) : def;
        int face = N(p[6], -1);
        int[] sounds = named.TryGetValue("sounds", out var sv)
            ? sv.Split(',').Select(t => N(t, 0)).Concat(new int[4]).Take(4).ToArray()
            : p[9] == "1" ? null : new int[4];
        int kind = Array.IndexOf(KindWords, p[1]);
        return new Options((DoorKind)Math.Max(0, kind), N(p[2], 0), N(p[3], 0), N(p[4], 0), N(p[5], 0), face, Named("edge", face), N(p[7], 10), N(p[8], 0),
            sounds, !named.TryGetValue("by", out var by) || by != "channel", Named("channel", 0), Named("key", 0), Named("once", 0) == 1,
            Named("hinge", 0), Named("carve", 0) == 1);
    }

    /// <summary>A doorway: a sector of four walls in a rectangle, two opposite red ones (the thresholds, to the rooms)
    /// and two white ones (the jambs). Its walls in loop order from a threshold: threshold, jamb, threshold, jamb.</summary>
    public static string Doorway(MapDocument d, int s, out int[] walls)
    {
        walls = null;
        const string what = "a doorway: a sector of four walls in a rectangle, two opposite two-sided walls (to the rooms on either side) and two one-sided ones (the jambs)";
        if (s < 0 || s >= d.Sectors.Count)
            return "no sector";
        var sec = d.Sectors[s];
        if (sec.WallNum != 4)
            return $"sector {s} has {sec.WallNum} walls: a door is made in " + what;
        var w = Enumerable.Range(sec.WallPtr, 4).ToArray();
        if (!Rectangle(d, w))
            return $"sector {s} isn't a rectangle: a door is made in " + what;
        if (!DoorwayLoop(d, w, out walls))
            return $"sector {s}'s walls aren't two opposite two-sided ones and two one-sided ones: a door is made in " + what;
        return null;
    }

    /// <summary>Four walls in a loop making a rectangle (each corner square)?</summary>
    static bool Rectangle(MapDocument d, IReadOnlyList<int> w)
    {
        for (int k = 0; k < 4; k++)
        {
            var (ax, ay, bx, by) = BuildGeom.WallEnds(d, w[k]);
            var (_, _, cx, cy) = BuildGeom.WallEnds(d, w[(k + 1) % 4]);
            double ux = bx - ax, uy = by - ay, vx = cx - bx, vy = cy - by;
            double lu = Math.Sqrt(ux * ux + uy * uy), lv = Math.Sqrt(vx * vx + vy * vy);
            if (lu < 1 || lv < 1 || Math.Abs(ux * vx + uy * vy) / (lu * lv) > 0.01)
                return false;
        }
        return true;
    }

    /// <summary>A loop of four walls as a doorway's (a rectangle, two opposite red, two white): its walls from a
    /// threshold, threshold, jamb, threshold, jamb.</summary>
    static bool DoorwayLoop(MapDocument d, IReadOnlyList<int> w, out int[] walls)
    {
        walls = null;
        if (w.Count != 4 || !Rectangle(d, w))
            return false;
        bool Red(int k) => d.Walls[w[k]].NextSector >= 0;
        int first = Red(0) && Red(2) && !Red(1) && !Red(3) ? 0 : Red(1) && Red(3) && !Red(0) && !Red(2) ? 1 : -1;
        if (first < 0)
            return false;
        walls = Enumerable.Range(0, 4).Select(k => w[(first + k) % 4]).ToArray();
        return true;
    }

    /// <summary>Which way each of a doorway's rooms lies from it (Sides' order: its red walls in their order), as the 2D
    /// view shows north (Build's y grows south): "north", "east"...</summary>
    public static string[] SideWays(MapDocument d, int doorway)
    {
        if (doorway < 0 || doorway >= d.Sectors.Count)
            return null;
        var sec = d.Sectors[doorway];
        var ws = Enumerable.Range(sec.WallPtr, sec.WallNum).ToList();
        var c = Mid(ws.SelectMany(w => new[] { d.Walls[w].X, d.Walls[w].Y }).ToArray());
        return ws.Where(w => d.Walls[w].NextSector >= 0).Select(w =>
        {
            var (ax, ay, bx, by) = BuildGeom.WallEnds(d, w);
            var v = new Vector2((ax + bx) / 2f, (ay + by) / 2f) - c;
            return Math.Abs(v.X) > Math.Abs(v.Y) ? (v.X > 0 ? "east" : "west") : v.Y < 0 ? "north" : "south";
        }).ToArray();
    }

    /// <summary>The rooms on either side of a doorway (its two thresholds' other sectors), or null.</summary>
    public static int[] Sides(MapDocument d, int s) =>
        Doorway(d, s, out var w) == null ? new[] { d.Walls[w[0]].NextSector, d.Walls[w[2]].NextSector } : null;

    /// <summary>Doorway s made a door (one undo step for the caller). Null when done, else why not; made: what was made.</summary>
    public static string Make(MapDocument d, int s, Options o, out string made)
    {
        made = null;
        if (Doorway(d, s, out var walls) is { } why)
            return why;
        return o.Kind switch
        {
            DoorKind.SwingPair or DoorKind.SwingSingle => MakeSwing(d, s, walls, o, out made),
            DoorKind.LiftUp or DoorKind.LiftDown or DoorKind.LiftSplit => MakeLift(d, s, walls, o, out made),
            DoorKind.Pivot => MakePivot(d, s, walls, o, out made),
            _ => MakeSlide(d, s, walls, o, out made),
        };
    }

    // ---------------------------------------------------------------- sliding

    /// <summary>
    /// A sliding pair: each jamb notched for a leaf (E1M1's sector 4): from the jamb out into the wall (a channel of no
    /// width), back to the leaf's edge, across it, out again and back to the jamb; the leaf's three walls move
    /// (cstat 16384 one leaf, 32768 the other), stored open (in the walls, the lip out of them), marker 0 (OFF) in the
    /// middle where they meet, marker 1 (ON) as far off as a leaf moves. Each leaf's faces open and close the door
    /// (push: toggle its channel).
    /// </summary>
    static string MakeSlide(MapDocument d, int s, int[] walls, Options o, out string made)
    {
        made = null;
        var (t0x, t0y, t1x, t1y) = BuildGeom.WallEnds(d, walls[0]);
        var (j0x, j0y, j1x, j1y) = BuildGeom.WallEnds(d, walls[1]);
        double width = Len(t1x - t0x, t1y - t0y), depth = Len(j1x - j0x, j1y - j0y);
        int thick = o.Thickness > 0 ? o.Thickness : (int)Math.Round(depth / 2);
        if (thick < 8 || thick > depth - 16)
            return $"the leaves' thickness ({thick}) must be 8 to {depth - 16:0}: the doorway is {depth:0} deep (a jamb is left either side of them)";
        int lip = o.Lip;
        double slide = width / 2 - lip;
        if (lip < 0 || slide < 16)
            return $"the lip ({lip}) must be under {width / 2 - 16:0}: half the doorway's width ({width:0}) less a little";
        // each jamb from P to Q, its room on its right: the leaf's notch points, from P
        var notches = new List<(Vector2I p, Vector2I q, Vector2I[] pts, Vector2 outward)>();
        foreach (int jw in new[] { walls[1], walls[3] })
        {
            var (px, py, qx, qy) = BuildGeom.WallEnds(d, jw);
            double len = Len(qx - px, qy - py);
            double ax = (qx - px) / len, ay = (qy - py) / len;
            // (into the room: the wall's right as drawn; out into the wall the other way)
            double ox = ay, oy = -ax;
            double v1 = (depth - thick) / 2, v2 = v1 + thick;
            Vector2I At(double v, double u) => new((int)Math.Round(px + ax * v + ox * u), (int)Math.Round(py + ay * v + oy * u));
            var pts = new[] { At(v1, 0), At(v1, slide), At(v1, -lip), At(v2, -lip), At(v2, slide), At(v2, 0) };
            if (Occupied(d, s, new[] { At(v1, 0), At(v1, slide), At(v2, slide), At(v2, 0) }) is { } taken)
            {
                // how deep the wall is solid there (the deepest slot that fits): the lip that would fit, if any
                double lo = 0, hi = slide;
                for (int k = 0; k < 14; k++)
                {
                    double depthTry = (lo + hi) / 2;
                    if (Occupied(d, s, new[] { At(v1, 0), At(v1, depthTry), At(v2, depthTry), At(v2, 0) }) == null)
                        lo = depthTry;
                    else
                        hi = depthTry;
                }
                int room = (int)Math.Floor(lo), fits = (int)Math.Ceiling(width / 2 - room);
                return $"a leaf slides {Math.Round(slide)} into the wall beside the doorway, but the wall there is solid for only {room} ({taken} beyond): "
                    + (width / 2 - fits >= 16 ? $"a lip of {fits} or more fits; " : "") + "or a swinging pair, or a narrower doorway, or a thicker wall";
            }
            notches.Add((new(px, py), new(qx, qy), pts, new Vector2((float)ox, (float)oy)));
        }
        foreach (var n in notches)
        {
            int jw = Tools.SlopeOps.FindEdge(d, s, n.p.X, n.p.Y, n.q.X, n.q.Y);
            if (jw < 0 || !d.InsertPoints(jw, n.pts))
                return "a jamb couldn't be notched for its leaf";
        }
        int ch = o.Channel > 0 ? o.Channel : FreeChannel(d);
        // the leaves' walls: the first jamb's move forward (marker 0 to marker 1 opens it: out into its wall), the
        // other's backward; their faces push the door
        var sec = d.Sectors[s];
        int face = o.FacePicture, edge = o.EdgePicture;
        for (int k = 0; k < 2; k++)
        {
            var p = notches[k].pts;
            int flag = k == 0 ? 16384 : 32768;
            foreach (var (a, b, isFace) in new[] { (p[1], p[2], true), (p[2], p[3], false), (p[3], p[4], true) })
            {
                int w = Tools.SlopeOps.FindEdge(d, s, a.X, a.Y, b.X, b.Y);
                if (w < 0)
                    return "a leaf's walls weren't found after notching the jamb";
                var wall = d.Walls[w];
                wall.Cstat = (wall.Cstat & ~(16384 | 32768)) | flag;
                wall.Picnum = isFace ? face : edge;
                Plain(wall);
                if (isFace)
                {
                    if (o.Push)
                        Pushes(d.EnsureXWall(w), ch, o);
                    OnePicture(wall);
                }
            }
        }
        sec.Type = SlideMarked;
        var xs = d.EnsureXSector(s);
        Timing(xs, ch, o);
        SectorMarkers.Fix(d, s);
        // marker 0 where the leaves meet, marker 1 as far as the forward leaf goes when it opens
        var mid = Mid(t0x, t0y, t1x, t1y, j0x, j0y, j1x, j1y);
        var on = mid + notches[0].outward * (float)slide;
        Place(d, xs.Marker0, mid, s);
        Place(d, xs.Marker1, on, s);
        if (o.HasSounds)
            AddSound(d, s, mid + new Vector2(32, 32), o.SoundIds);
        made = $"sector {s} a sliding door (type 614, channel {ch}): two leaves {thick} thick sliding {Math.Round(slide)} into the walls, "
            + HowOpened(o, ch) + "; stored open, as Blood stores doors (Off in the toolbar: closed)";
        return null;
    }

    // ---------------------------------------------------------------- swinging

    /// <summary>
    /// A swinging pair: two leaf sectors (E1M1's 50 and 51) in the room on the chosen side, each hinged on its own side
    /// of the doorway a little into that room (the inset), closed across the opening and turning a quarter into the
    /// room to open (stored open); floor and ceiling at the room's ceiling (solid: the room's walls round them show the
    /// door). The first leaf takes the push (its channel) and passes its motion on to the other (link).
    /// </summary>
    static string MakeSwing(MapDocument d, int s, int[] walls, Options o, out string made)
    {
        made = null;
        int tw = walls[o.Side == 1 ? 2 : 0];
        int room = d.Walls[tw].NextSector;
        var (ax, ay, bx, by) = BuildGeom.WallEnds(d, tw);
        double width = Len(bx - ax, by - ay);
        double tx = (bx - ax) / width, ty = (by - ay) / width;
        // (into the room beyond the threshold: the doorway lies on the wall's right)
        double rx = ty, ry = -tx;
        int thick = o.Thickness > 0 ? o.Thickness : 128;
        int inset = o.Inset;
        // (one leaf: hinged at the jamb asked for, as long as the doorway is wide)
        bool single = o.Kind == DoorKind.SwingSingle;
        string them = single ? "the leaf" : "the leaves";
        if (thick < 8 || inset < thick + 16)
            return $"the hinges' inset ({inset}) must be at least the leaves' thickness ({thick}) and 16: closed, a leaf stands clear of the threshold";
        // the hinges at the doorway's corners (E1M1's: its room wider than the doorway), else moved in from them (the
        // leaves that much shorter) until the room holds both: a corridor as wide as the doorway has its side walls
        // through the corners, a corner cut off slants across the swing (Forge report 20261107-105619)
        List<(Vector2 hinge, Vector2I[] open, int ang)> leaves = null;
        string why = null;
        double leaf = 0;
        int side = 0;
        foreach (int m in new[] { 0, 32, 64, 128 })
        {
            leaf = single ? width - 16 - m : width / 2 - 8 - m;
            side = m;
            if (leaf < 64)
                break;
            leaves = new List<(Vector2 hinge, Vector2I[] open, int ang)>();
            why = null;
            foreach (bool second in single ? new[] { o.Hinge == 1 } : new[] { false, true })
            {
                // the hinge, the way along the leaf (to the middle), the way across it (to the threshold)
                var along = new Vector2((float)(second ? -tx : tx), (float)(second ? -ty : ty));
                var across = new Vector2((float)-rx, (float)-ry);
                var h = new Vector2((float)((second ? bx : ax) + rx * inset), (float)((second ? by : ay) + ry * inset)) + along * m;
                var closed = new[] { h, h + along * (float)leaf, h + along * (float)leaf + across * thick, h + across * thick };
                // open: turned a quarter about the hinge into the room (Blood takes it back by the axis marker's angle)
                var into = h + new Vector2((float)rx, (float)ry) * (float)leaf;
                int ang = new[] { 512, -512 }.OrderBy(a => Turned(closed[1], a, h).DistanceTo(into)).First();
                var open = closed.Select(p => Turned(p, ang, h)).Select(p => new Vector2I((int)Math.Round(p.X), (int)Math.Round(p.Y))).ToArray();
                // the room holds it open, closed and on the way
                for (int k = 0; k <= 8 && why == null; k++)
                    foreach (var p in closed)
                    {
                        var q = Turned(p, ang * k / 8, h);
                        if (!BuildGeom.Inside(d, room, q.X, q.Y))
                        {
                            why = $"no room in sector {room} for {them} to swing open ({Math.Round(leaf + m)} long, hinged {inset} in): the other side, or a wider room";
                            break;
                        }
                    }
                if (why == null && !Fits(d, room, open))
                    why = $"{them} don't fit in sector {room} where {(single ? "it swings" : "they swing")} open ({Math.Round(leaf + m)} long, hinged {inset} in): the other side, or a wider room";
                if (why != null)
                    break;
                leaves.Add((h, open, ang));
            }
            if (why == null)
                break;
        }
        if (why != null || leaves == null || leaves.Count < (single ? 1 : 2))
            return why ?? $"the doorway ({width:0}) is too narrow for {(single ? "a swinging leaf" : "a swinging pair")}";
        int ch = o.Channel > 0 ? o.Channel : FreeChannel(d), link = single ? -1 : FreeChannel(d, ch);
        int face = o.FacePicture, edge = o.EdgePicture;
        var made2 = new List<int>();
        for (int k = 0; k < leaves.Count; k++)
        {
            int l = d.InsertInnerLoop(room, leaves[k].open.ToList());
            if (l < 0)
                return "a leaf couldn't be put in the room";
            made2.Add(l);
            var ls = d.Sectors[l];
            ls.FloorZ = ls.CeilingZ = d.Sectors[room].CeilingZ;
            ls.FloorStat &= ~2;
            ls.CeilingStat &= ~2;
            ls.Type = Rotate;
            for (int w = ls.WallPtr; w < ls.WallPtr + ls.WallNum; w++)
            {
                // (its long sides the faces, its ends the edges; the room's walls round it show it, and push it)
                var (x1, y1, x2, y2) = BuildGeom.WallEnds(d, w);
                bool isFace = Len(x2 - x1, y2 - y1) > thick + 1;
                int pic = isFace ? face : edge;
                d.Walls[w].Picnum = pic;
                Plain(d.Walls[w]);
                if (isFace)
                    OnePicture(d.Walls[w]);
                int nw = d.Walls[w].NextWall;
                if (nw >= 0)
                {
                    d.Walls[nw].Picnum = pic;
                    Plain(d.Walls[nw]);
                    if (isFace)
                        OnePicture(d.Walls[nw]);
                    if (o.Push)
                        Pushes(d.EnsureXWall(nw), ch, o);
                }
            }
            var xs = d.EnsureXSector(l);
            Timing(xs, k == 0 ? ch : link, o);
            if (k == 0 && !single)
            {
                // (the first passes its motion on to the second: kCmdLink)
                xs.TxID = link;
                xs.Command = 5;
                xs.TriggerOn = xs.TriggerOff = 1;
            }
            else if (k > 0)
                xs.WaitTimeA = 0;
            SectorMarkers.Fix(d, l);
            Place(d, xs.Marker0, leaves[k].hinge, l);
            // (signed, as E1M1 stores it)
            if (xs.Marker0 >= 0)
                d.Sprites[xs.Marker0].Ang = leaves[k].ang;
        }
        if (o.HasSounds)
        {
            // (in the first leaf: the middle of its open place)
            var c = leaves[0].open.Aggregate(Vector2.Zero, (acc, q) => acc + new Vector2(q.X, q.Y)) / 4;
            AddSound(d, made2[0], c, o.SoundIds);
        }
        made = (single ? $"sector {made2[0]} a swinging leaf in sector {room} (type 617, channel {ch}, hinged at the {(o.Hinge == 1 ? "right" : "left")} jamb): leaf"
            : $"sectors {made2[0]} and {made2[1]} a swinging door in sector {room} (type 617, channel {ch}, the second linked on {link}): leaves")
            + $" {thick} thick, {Math.Round(leaf)} long, hinged {inset} in" + (side > 0 ? $" and {side} in from the corners (the room's walls there)" : "") + ", " + HowOpened(o, ch) + "; stored open, as Blood stores doors (Off in the toolbar: closed)";
        return null;
    }

    // ---------------------------------------------------------------- lifting

    /// <summary>
    /// A lifting door: the doorway itself (Z motion, 600) closing to its floor (lifts up: its ceiling comes down), to
    /// its ceiling (lowers down: its floor comes up) or to its middle (splits: both), open at the heights it has now,
    /// stored closed as Blood's levels store theirs (state off: E1M1's 24, 25, 93). The rooms' walls facing it are its
    /// faces, their picture once across and once up it (a split: once on each half), moving with it (not aligned on the
    /// bottom, cstat 4, as most of Blood's doors); its bottom (the doorway's ceiling) or top (its floor) the edge. The use
    /// key on those walls pushes it through them (the sector's Wallpush, its key and once there: Blood's own).
    /// </summary>
    static string MakeLift(MapDocument d, int s, int[] walls, Options o, out string made)
    {
        made = null;
        var sec = d.Sectors[s];
        if (((sec.CeilingStat & 2) != 0 && sec.CeilingHeinum != 0) || ((sec.FloorStat & 2) != 0 && sec.FloorHeinum != 0))
            return $"sector {s}'s floor or ceiling slopes: a door lifting or lowering needs them level";
        int openC = sec.CeilingZ, openF = sec.FloorZ;
        if (openF - openC < 2048)
            return $"the doorway is {openF - openC} high (Build z): too low to open a door in";
        int closed = o.Kind switch
        {
            DoorKind.LiftUp => openF,
            DoorKind.LiftDown => openC,
            _ => (int)Math.Round((openC + openF) / 2.0),
        };
        int ch = o.Channel > 0 ? o.Channel : FreeChannel(d);
        sec.Type = ZMotion;
        var xs = d.EnsureXSector(s);
        Timing(xs, ch, o);
        (xs.OffCeilZ, xs.OnCeilZ) = (o.Kind == DoorKind.LiftDown ? openC : closed, openC);
        (xs.OffFloorZ, xs.OnFloorZ) = (o.Kind == DoorKind.LiftUp ? openF : closed, openF);
        sec.CeilingZ = xs.OffCeilZ;
        sec.FloorZ = xs.OffFloorZ;
        SectorPush(xs, o);
        double tall = o.Kind == DoorKind.LiftSplit ? (openF - openC) / 2.0 : openF - openC;
        foreach (int t in new[] { walls[0], walls[2] })
            if (d.Walls[t].NextWall is var nw and >= 0)
                LiftFace(d.Walls[nw], o.FacePicture, tall);
        JambsStill(d, new[] { walls[1], walls[3] }, o.Kind);
        if (o.Kind != DoorKind.LiftDown)
            sec.CeilingPicnum = o.EdgePicture;
        if (o.Kind != DoorKind.LiftUp)
            sec.FloorPicnum = o.EdgePicture;
        var mid = Mid(walls.SelectMany(w => new[] { d.Walls[w].X, d.Walls[w].Y }).ToArray());
        if (o.HasSounds)
            AddSound(d, s, mid, o.SoundIds);
        string how = o.Kind switch { DoorKind.LiftUp => "lifting into the ceiling", DoorKind.LiftDown => "lowering into the floor", _ => "parting up and down" };
        made = $"sector {s} a door {how} (type 600, channel {ch}): {openF - openC} high open, " + HowOpened(o, ch)
            + "; stored closed, as Blood stores these (On in the toolbar: open)";
        return null;
    }

    /// <summary>A lift's jambs (the doorway's own white walls) with their pictures held where they are: Build pegs a
    /// white wall's picture to its sector's ceiling, or to its floor with cstat 4 (aligned on the bottom), so lifting up
    /// (the ceiling moving) they go by the floor, lowering (the floor moving) by the ceiling; parting moves both, the
    /// floor's half then (pegged to the ceiling, the jambs' pictures rode up with the door).</summary>
    public static void JambsStill(MapDocument d, IEnumerable<int> jambs, DoorKind k)
    {
        foreach (int w in jambs)
            d.Walls[w].Cstat = k == DoorKind.LiftDown ? d.Walls[w].Cstat & ~4 : d.Walls[w].Cstat | 4;
    }

    /// <summary>A lift's face (a room's wall facing it): the picture once across and once up it, moving with it.</summary>
    static void LiftFace(BWall w, int pic, double tall)
    {
        w.Picnum = pic;
        Plain(w);
        w.Cstat &= ~4;
        OnePicture(w);
        OneHigh(w, tall);
    }

    /// <summary>A wall's picture once over a height (Build z): a picture row is 2048 / yrepeat high.</summary>
    static void OneHigh(BWall w, double tall)
    {
        if (tall > 0 && Data.ForgeData.Tiles?[w.Picnum] is { Height: > 0 } tile)
            w.YRepeat = (int)Math.Clamp(Math.Round(tile.Height * 2048.0 / tall), 1, 255);
    }

    /// <summary>A door pushed through its own walls: the sector's Wallpush (Blood's lifting doors), with the key and
    /// once there (trTriggerSector: triggerOnce sets isTriggered).</summary>
    static void SectorPush(XSector xs, Options o)
    {
        xs.Wallpush = o.Push ? 1 : 0;
        xs.Key = o.Push ? Math.Clamp(o.Key, 0, 7) : 0;
        xs.TriggerOnce = o.Push && o.Once ? 1 : 0;
    }

    // ---------------------------------------------------------------- pivot

    /// <summary>The gap a pivot leaf keeps from each jamb, closed.</summary>
    const int PivotGap = 16;

    /// <summary>
    /// A pivot: a leaf in the doorway (a pillar of white walls, all marked to turn: 615, kSectorRotateMarked) turning a
    /// quarter about its middle (the axis marker there, its angle the quarter), closed across the opening and open along
    /// the way through (stored open, as Blood stores turning sectors: trInit turns it back). Turning, it stays inside the
    /// doorway (its walls are the doorway's): the doorway as deep as the leaf is long. Its walls push it.
    /// </summary>
    static string MakePivot(MapDocument d, int s, int[] walls, Options o, out string made)
    {
        made = null;
        var (ax, ay, bx, by) = BuildGeom.WallEnds(d, walls[0]);
        var (jx1, jy1, jx2, jy2) = BuildGeom.WallEnds(d, walls[1]);
        double width = Len(bx - ax, by - ay), depth = Len(jx2 - jx1, jy2 - jy1);
        int thick = o.Thickness > 0 ? o.Thickness : 64;
        double leaf = width - 2 * PivotGap;
        // (its corners' circle, turning: inside the doorway, a little off its walls)
        double reach = Len(leaf / 2, thick / 2.0);
        if (thick < 8 || leaf < 64)
            return $"the doorway ({width:0} wide) is too narrow for a pivot, or the leaf ({thick}) too thin";
        // (the doorway's middle, from its walls before any carving renumbers them)
        var c = Mid(walls.SelectMany(w => new[] { d.Walls[w].X, d.Walls[w].Y }).ToArray());
        // (too thin: the rooms either side carved for its turn, when asked)
        int carved = 0;
        if (reach <= width / 2 - 8 && reach > depth / 2 - 8 && o.Carve)
        {
            carved = (int)Math.Ceiling(reach + 8 - depth / 2);
            if (CarveChamber(d, s, walls, carved) is { } cwhy)
                return cwhy;
            depth = 2 * reach + 16;
        }
        if (reach > depth / 2 - 8 || reach > width / 2 - 8)
            return $"a pivot turns on its own axis inside the doorway, so the doorway must be about as deep as it is wide (a square seen from above): "
                + $"this one is {width:0} wide and {depth:0} deep; draw it at least {Math.Ceiling(2 * reach + 16)} deep "
                + "(its leaf, as wide as the opening, turns a quarter within it), or Carve the room it turns in, or pick another kind";
        var t = new Vector2((float)((bx - ax) / width), (float)((by - ay) / width));
        var n = new Vector2(-t.Y, t.X);
        var closedLeaf = new[] { c - t * (float)(leaf / 2) - n * (thick / 2f), c + t * (float)(leaf / 2) - n * (thick / 2f),
            c + t * (float)(leaf / 2) + n * (thick / 2f), c - t * (float)(leaf / 2) + n * (thick / 2f) };
        const int ang = 512;
        var open = closedLeaf.Select(p => Turned(p, ang, c)).Select(p => new Vector2I((int)Math.Round(p.X), (int)Math.Round(p.Y))).ToList();
        int hw = d.AddHole(s, open);
        if (hw < 0)
            return "the leaf couldn't be put in the doorway";
        int ch = o.Channel > 0 ? o.Channel : FreeChannel(d);
        var loop = BuildGeom.Loops(d, s).FirstOrDefault(l => l.Contains(hw));
        if (loop == null)
            return "the leaf's walls weren't found";
        foreach (int w in loop)
        {
            var wall = d.Walls[w];
            bool isFace = WallLength(d, w) > thick + 1;
            wall.Cstat = (wall.Cstat & ~32768) | 16384;
            wall.Picnum = isFace ? o.FacePicture : o.EdgePicture;
            Plain(wall);
            if (isFace)
                OnePicture(wall);
            if (o.Push)
                Pushes(d.EnsureXWall(w), ch, o);
        }
        d.Sectors[s].Type = RotateMarked;
        var xs = d.EnsureXSector(s);
        Timing(xs, ch, o);
        SectorMarkers.Fix(d, s);
        Place(d, xs.Marker0, c, s);
        if (xs.Marker0 >= 0)
            d.Sprites[xs.Marker0].Ang = ang;
        if (o.HasSounds)
            AddSound(d, s, c + t * (float)(leaf / 2 + PivotGap / 2), o.SoundIds);
        made = $"sector {s} a pivot (type 615, channel {ch}): a leaf {leaf:0} long, {thick} thick, turning a quarter about its middle"
            + (carved > 0 ? $" in a room carved {carved} into the rooms either side" : "") + ", "
            + HowOpened(o, ch) + "; stored open, as Blood stores turning sectors (Off in the toolbar: closed)";
        return null;
    }

    /// <summary>
    /// The room a pivot turns in, cut out of the rooms either side of doorway s: from each threshold, a strip as wide as
    /// the doorway and out deep into the room (split off along the threshold's corners, out and across) joined to the
    /// doorway, which keeps its floor and ceiling (a sector's walls move only inside it: a leaf as wide as the opening
    /// sweeps a circle as wide, out of a thin doorway into both rooms). Null when done, else why not.
    /// </summary>
    static string CarveChamber(MapDocument d, int s, int[] walls, int deep)
    {
        foreach (int k in new[] { 0, 2 })
        {
            // (each time from the doorway as it is now: its walls change as the first side is joined)
            var (ax, ay, bx, by) = BuildGeom.WallEnds(d, walls[k]);
            int room = d.Walls[walls[k]].NextSector;
            double len = Len(bx - ax, by - ay);
            // (out of the doorway into the room: the doorway lies on the threshold's right, the room on its left)
            var o = new Vector2((float)((by - ay) / len), (float)(-(bx - ax) / len));
            Vector2I P(int x, int y) => new((int)Math.Round(x + o.X * deep), (int)Math.Round(y + o.Y * deep));
            // the cut: from the threshold's corners out and across; where a side of the strip runs along one of the
            // room's walls (a doorway as long as the room is wide: Forge report 20261107-190252), from a point put in
            // that wall instead (the cut straight across)
            Vector2I a0 = new(ax, ay), b0 = new(bx, by), a1 = P(ax, ay), b1 = P(bx, by);
            var path = new List<Vector2I>();
            foreach (var (corner, outPt, first) in new[] { (a0, a1, true), (b0, b1, false) })
            {
                bool along = OnWallOf(d, room, (corner + outPt) / 2);
                if (along && PutPoint(d, room, outPt) is { } pwhy)
                    return $"the room it turns in can't be cut out of sector {room} ({deep} into it from the doorway): {pwhy}";
                if (first)
                {
                    if (!along)
                        path.Add(corner);
                    path.Add(outPt);
                }
                else
                {
                    path.Add(outPt);
                    if (!along)
                        path.Add(corner);
                }
            }
            var (piece, why) = d.SplitSector(room, path);
            if (piece < 0)
                return $"the room it turns in can't be cut out of sector {room} ({deep} into it from the doorway): {why}";
            d.NotifyChanged();
            // (the new part holds the threshold, the doorway's partner wall: the room keeps its number and the rest)
            if (!Enumerable.Range(d.Sectors[piece].WallPtr, d.Sectors[piece].WallNum).Any(w => d.Walls[w].NextSector == s))
                return $"the room it turns in wasn't cut out of sector {room} beside the doorway";
            if (d.JoinSectors(s, piece) is { } jwhy)
                return $"the room it turns in couldn't be joined to the doorway: {jwhy}";
            d.NotifyChanged();
            if (piece < s)
                s--;
            if (k == 0)
            {
                // the doorway's walls again for the other side (its threshold opposite the first)
                var opp = Enumerable.Range(d.Sectors[s].WallPtr, d.Sectors[s].WallNum).FirstOrDefault(w =>
                {
                    var (x1, y1, x2, y2) = BuildGeom.WallEnds(d, w);
                    // (as long as the first, across the doorway from it)
                    return d.Walls[w].NextSector >= 0 && Math.Abs(Len(x2 - x1, y2 - y1) - len) < 1
                        && (new Vector2((x1 + x2) / 2f, (y1 + y2) / 2f) - new Vector2((ax + bx) / 2f, (ay + by) / 2f)).Dot(o) < -1;
                }, -1);
                if (opp < 0)
                    return "the doorway's other threshold wasn't found after cutting the first room";
                walls[2] = opp;
            }
        }
        return null;
    }

    /// <summary>Does point p lie on one of sector s's walls (within half a unit)?</summary>
    static bool OnWallOf(MapDocument d, int s, Vector2I p) =>
        Enumerable.Range(d.Sectors[s].WallPtr, d.Sectors[s].WallNum).Any(w =>
        {
            var (ax, ay, bx, by) = BuildGeom.WallEnds(d, w);
            return MapDocument.SegmentDistance(p.X, p.Y, ax, ay, bx, by) < 0.5;
        });

    /// <summary>Point p made a point of sector s's walls (put in the wall it lies on; there already: nothing). Why not,
    /// or null.</summary>
    static string PutPoint(MapDocument d, int s, Vector2I p)
    {
        var ws = Enumerable.Range(d.Sectors[s].WallPtr, d.Sectors[s].WallNum).ToList();
        if (ws.Any(w => d.Walls[w].X == p.X && d.Walls[w].Y == p.Y))
            return null;
        foreach (int w in ws)
        {
            var (ax, ay, bx, by) = BuildGeom.WallEnds(d, w);
            if (MapDocument.SegmentDistance(p.X, p.Y, ax, ay, bx, by) < 0.5)
            {
                if (!d.InsertPoint(w, p.X, p.Y))
                    return $"a point couldn't be put in wall {w}";
                d.NotifyChanged();
                return null;
            }
        }
        return $"the room's wall along it ends before ({p.X}, {p.Y})";
    }

    // ---------------------------------------------------------------- the parts

    static double Len(double x, double y) => Math.Sqrt(x * x + y * y);

    static Vector2 Mid(params int[] xy)
    {
        var c = Vector2.Zero;
        for (int i = 0; i < xy.Length; i += 2)
            c += new Vector2(xy[i], xy[i + 1]);
        return c / (xy.Length / 2);
    }

    /// <summary>p turned by ang (Build's 2048) about o, as the game turns a rotating sector.</summary>
    static Vector2 Turned(Vector2 p, int ang, Vector2 o)
    {
        var (x, y) = SectorMotion.RotatePoint(p.X, p.Y, ang, o.X, o.Y);
        return new Vector2((float)x, (float)y);
    }

    /// <summary>The door's open and close: the channel it hears, its busy and wait times, interruptible.</summary>
    static void Timing(XSector xs, int ch, Options o)
    {
        xs.RxID = ch;
        xs.BusyTimeA = xs.BusyTimeB = Math.Clamp(o.BusyTime, 0, 4095);
        // (used once: it stays open)
        xs.WaitTimeA = o.Once && o.Push ? 0 : Math.Clamp(o.WaitTime, 0, 4095);
        xs.Interruptable = 1;
        xs.State = 0;
        xs.Busy = 0;
    }

    /// <summary>A leaf's wall without the panning and flips of the wall it was copied from (the jamb's, the room's).</summary>
    static void Plain(BWall w)
    {
        w.XPanning = w.YPanning = 0;
        w.Cstat &= ~(8 | 256);
    }

    /// <summary>A leaf's face shows its picture once across (a wall shows its repeat x 8 of the picture's columns
    /// whatever its length: E1M1's leaves 8 for its 64 wide door), not tiled by its length.</summary>
    static void OnePicture(BWall w)
    {
        if (Data.ForgeData.Tiles?[w.Picnum] is { Width: > 0 } tile)
            w.XRepeat = Math.Clamp((tile.Width + 4) / 8, 1, 255);
    }

    /// <summary>A wall that opens and closes the door when pushed: toggle on the door's channel. A push toggles the
    /// wall's own state (trTriggerWall, OperateWall); the wall sends only on the changes its trigger flags name
    /// (SetWallState), so both are on, as E1M1's door walls have them (without: the use key did nothing). Used once: it
    /// sends On and is spent (trTriggerWall: triggerOnce sets isTriggered, and it is never triggered again). A key:
    /// without it the use key does nothing ("That requires a key.": player.cpp checks the pushed wall's key).</summary>
    static void Pushes(XWall xw, int ch, Options o)
    {
        xw.TxID = ch;
        xw.Command = o.Once ? 1 : 3;
        xw.TriggerPush = 1;
        xw.TriggerOn = xw.TriggerOff = 1;
        xw.TriggerOnce = o.Once ? 1 : 0;
        xw.Key = Math.Clamp(o.Key, 0, 7);
    }

    /// <summary>How the door is opened, for the status line.</summary>
    static string HowOpened(Options o, int ch)
    {
        if (!o.Push)
            return $"opened and closed only by what sends on channel {ch} (a switch, a trigger: On, Off or Toggle)";
        string key = o.Key > 0 ? " with the " + (XFields.Keys.FirstOrDefault(k => k.Item1 == o.Key).Item2 ?? $"key {o.Key}").ToLowerInvariant() : "";
        return o.Once ? $"opened once by pushing it{key} (it stays open)" : $"opened and closed by pushing it{key}";
    }

    static void Place(MapDocument d, int sprite, Vector2 at, int sector)
    {
        if (sprite < 0 || sprite >= d.Sprites.Count)
            return;
        var sp = d.Sprites[sprite];
        sp.X = (int)Math.Round(at.X);
        sp.Y = (int)Math.Round(at.Y);
        sp.SectNum = sector;
        BuildGeom.GetZsOfSlope(d, sector, sp.X, sp.Y, out _, out double floor);
        sp.Z = (int)Math.Round(floor);
        SectorsLayer(d, sprite);
    }

    /// <summary>A sprite of the door in its sector's layer (AddSprite puts one in the layer being edited: a swinging
    /// pair made while another layer is edited had its markers and its sound there).</summary>
    static void SectorsLayer(MapDocument d, int sprite)
    {
        if (sprite < d.Extras.SpriteLayers.Count)
            d.Extras.SpriteLayers[sprite] = -1;
    }

    static void AddSound(MapDocument d, int sector, Vector2 at, int[] sounds)
    {
        int i = d.AddSprite((int)Math.Round(at.X), (int)Math.Round(at.Y), SoundSector, sector);
        SpriteTypes.Apply(d, i, SoundSector);
        d.Sprites[i].SectNum = sector;
        SectorsLayer(d, i);
        var x = d.EnsureXSprite(i);
        (x.Data1, x.Data2, x.Data3, x.Data4) = (sounds[0], sounds[1], sounds[2], sounds[3]);
    }

    /// <summary>The first channel from 100 (Blood's own below) no sector, wall or sprite sends or hears, other than but.</summary>
    public static int FreeChannel(MapDocument d, int but = -1)
    {
        var used = new HashSet<int> { but };
        foreach (var s in d.Sectors)
            if (s.Xs is { } x)
            {
                used.Add(x.RxID);
                used.Add(x.TxID);
            }
        foreach (var w in d.Walls)
            if (w.Xw is { } x)
            {
                used.Add(x.RxID);
                used.Add(x.TxID);
            }
        foreach (var sp in d.Sprites)
            if (sp.Xsp is { } x)
            {
                used.Add(x.RxID);
                used.Add(x.TxID);
            }
        int ch = 100;
        while (used.Contains(ch) && ch < 1023)
            ch++;
        return ch;
    }

    /// <summary>A leaf's outline inside sector room, touching none of its walls (FitsAsRoom without its layer: that asks
    /// the layer being edited for the room, and the door goes in whichever layer the doorway's room is).</summary>
    static bool Fits(MapDocument d, int room, Vector2I[] pts)
    {
        if (pts.Any(p => !BuildGeom.Inside(d, room, p.X, p.Y) || d.OnOuterLoop(room, p).on))
            return false;
        for (int k = 0; k < pts.Length; k++)
        {
            double mx = (pts[k].X + pts[(k + 1) % pts.Length].X) * 0.5, my = (pts[k].Y + pts[(k + 1) % pts.Length].Y) * 0.5;
            for (int w = d.Sectors[room].WallPtr; w < d.Sectors[room].WallPtr + d.Sectors[room].WallNum; w++)
            {
                var (ax, ay, bx, by) = BuildGeom.WallEnds(d, w);
                if (MapDocument.SegmentDistance(mx, my, ax, ay, bx, by) < 0.5)
                    return false;
            }
        }
        return d.CrossesWalls(pts, true, room, Array.Empty<Vector2I>()) == null;
    }

    /// <summary>What lies in a quad (corners in order) beyond doorway s's wall: a sector's floor there, or another wall
    /// crossing it; null when it is solid wall (nothing). Only the doorway's own layer counts: a room on another layer
    /// at the same place is a room over a room, somewhere else in the game (the leaves never meet it: a door in a room
    /// over a room was refused for the room under it).</summary>
    static string Occupied(MapDocument d, int s, Vector2I[] quad)
    {
        int layer = d.LayerOf(s);
        bool Here(int sector) => sector >= 0 && sector < d.Sectors.Count && d.LayerOf(sector) == layer;
        // inside: a grid of points short of its first side (the jamb's)
        for (int i = 1; i <= 4; i++)
            for (int j = 1; j <= 4; j++)
            {
                double u = i / 5.0, v = j / 5.0;
                var a = Lerp(quad[0], quad[1], u);
                var b = Lerp(quad[3], quad[2], u);
                var p = a + (b - a) * (float)v;
                if (d.SectorsAt(p.X, p.Y).Where(Here).FirstOrDefault(-1) is int other and >= 0)
                    return $"sector {other} lies there";
            }
        // across it: a wall of another sector crossing one of its sides (not the jamb's own line)
        for (int w = 0; w < d.Walls.Count; w++)
        {
            if (d.Walls[w].Sector == s || !Here(d.Walls[w].Sector))
                continue;
            var (x1, y1, x2, y2) = BuildGeom.WallEnds(d, w);
            for (int k = 0; k < 3; k++)
                if (Cross(new Vector2(x1, y1), new Vector2(x2, y2), quad[k], quad[(k + 1) % 4]))
                    return $"wall {w} crosses it";
        }
        return null;
    }

    static Vector2 Lerp(Vector2I a, Vector2I b, double t) => new Vector2(a.X, a.Y) + (new Vector2(b.X, b.Y) - new Vector2(a.X, a.Y)) * (float)t;

    /// <summary>Do segments ab and cd cross properly (not just touch at an end)?</summary>
    static bool Cross(Vector2 a, Vector2 b, Vector2 c, Vector2 dd)
    {
        static double O(Vector2 p, Vector2 q, Vector2 r) => (double)(q.X - p.X) * (r.Y - p.Y) - (double)(q.Y - p.Y) * (r.X - p.X);
        double o1 = O(a, b, c), o2 = O(a, b, dd), o3 = O(c, dd, a), o4 = O(c, dd, b);
        return o1 * o2 < 0 && o3 * o4 < 0;
    }

}
