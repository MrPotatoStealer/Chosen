// Chosen Blood module (GPL v2): the rebuild test fills Blood's properties dialogs (their fields as EditDialogs names
// them from NBlood's db.h) and compares Blood's records field by field.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Chosen.Forge.Config;
using Chosen.Forge.Document;
using Chosen.Forge.Editing;
using Chosen.Forge.Testing;
using Chosen.Forge.View;
using Chosen.Shared.Blood.Map;
using Chosen.Shared.Engine.Geometry;
using Godot;

namespace Chosen.Forge;

/// <summary>
/// --rebuild=&lt;map&gt;: a map made again from nothing the way a mapper makes it, by input only: each sector's outline
/// clicked in Draw Lines mode (sectors against each other join), first walls set with Shift+F, every sector's, wall's
/// and sprite's properties typed into their dialogs (game units), sprites placed from the browser with Insert, the
/// header through Map Options. Then the result is compared with the original field by field: what differs is an
/// editor problem, or something it can't do yet. The rebuilt map goes to tools/forge_rebuild/&lt;MAP&gt;.mapx and .map
/// with the report (&lt;MAP&gt;.txt); the session is recorded (a regression replay: --addtest).
/// </summary>
public partial class MainWindow
{
    static string I(int v) => v.ToString(CultureInfo.InvariantCulture);

    async Task<int> RebuildTest(string path)
    {
        var src = MapDocument.Open(path);
        string name = Path.GetFileNameWithoutExtension(path).ToUpperInvariant();
        var notes = new List<string>();
        string cfgPath = ProjectSettings.GlobalizePath(Chosen.Forge.Config.ForgeConfig.FileName);
        byte[] cfgBefore = File.Exists(cfgPath) ? File.ReadAllBytes(cfgPath) : null;
        bool units = Units.Game;
        int result;
        try
        {
            SetDocument(MapDocument.New());
            await Frames(3);
            // as a careful mapper: no grid snap (the original's points lie anywhere), game units, new sprites' dialogs
            View.Snap = false;
            Units.Game = true;
            EditNewSprite = true;
            double feature = SmallestFeature(src);
            View.Zoom = Mathf.Clamp(ClassicMode.HighlightRange * 3 / (float)feature, 0.02f, 8f);
            GD.Print($"rebuild {name}: {src.Sectors.Count} sectors, {src.Walls.Count} walls, {src.Sprites.Count} sprites; "
                + $"smallest feature {feature:0.#} units, zoom {View.Zoom:0.###}");

            var newOf = await DrawSectors(src, notes);
            await VoidHoles(src, newOf, notes);
            await RepairJoins(src, newOf, notes);
            await ExtraPoints(src, newOf, notes);
            await FirstWalls(src, newOf, notes);
            await SectorProperties(src, newOf, notes);
            await WallProperties(src, newOf, notes);
            await PlaceMarkers(src, newOf, notes);
            await PlaceSprites(src, notes);
            await Header(src);
            result = Compare(src, newOf, notes);
            string outDir = Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), "..", "tools", "forge_rebuild"));
            Directory.CreateDirectory(outDir);
            File.WriteAllBytes(Path.Combine(outDir, name + ".mapx"), Doc.SaveBytes(MapFormat.MapX, false, out _));
            File.WriteAllBytes(Path.Combine(outDir, name + ".map"), Doc.SaveBytes(MapFormat.Map, false, out _));
            File.WriteAllLines(Path.Combine(outDir, name + ".txt"), notes);
            foreach (var n in notes.Take(80))
                GD.Print("rebuild: " + n);
            GD.Print($"rebuild {name}: {(result == 0 ? "identical" : $"{result} difference(s)")} (report: {Path.Combine(outDir, name + ".txt")}; session {Rec?.Folder})");
        }
        catch (Exception e)
        {
            GD.PrintErr("rebuild: " + e);
            result = -1;
        }
        finally
        {
            Units.Game = units;
            if (cfgBefore != null)
                File.WriteAllBytes(cfgPath, cfgBefore);
        }
        return result == 0 ? 0 : 1;
    }

    // ---------------------------------------------------------------- input as a mapper gives it

    /// <summary>A click at a map point, the view centred on it first (as a mapper scrolls to it).</summary>
    async Task ClickAt(MouseButton b, Vector2 at)
    {
        View.CentreOn(at);
        await Frames(1);
        await Click(b, at);
    }

    async Task Hover(Vector2 at)
    {
        View.CentreOn(at);
        await Frames(1);
        await Move(at);
    }

    /// <summary>Hovered, then Tab until the item meant is the one highlighted (items at one place: rooms over rooms).
    /// False when it never is.</summary>
    async Task<bool> HoverOn(Vector2 at, ElementKind kind, int item)
    {
        await Hover(at);
        for (int k = 0; k < 8; k++)
        {
            if (Editing.Mode is ClassicMode c && c.Highlighted.Kind == kind && c.Highlighted.Index == item)
                return true;
            await PressKey(Godot.Key.Tab);
        }
        return false;
    }

    async Task PressMods(MouseButton b, Vector2 map, bool down, bool alt)
    {
        var g = Screen(map);
        Input.ParseInputEvent(new InputEventMouseButton
        {
            ButtonIndex = b, Pressed = down, Position = g, GlobalPosition = g, AltPressed = alt,
            ButtonMask = down ? (b == MouseButton.Left ? MouseButtonMask.Left : MouseButtonMask.Right) : 0,
        });
        await Frames(1);
    }

    async Task PressKey(Key k, bool shift = false)
    {
        foreach (bool down in new[] { true, false })
            Input.ParseInputEvent(new InputEventKey { Keycode = k, PhysicalKeycode = k, Pressed = down, ShiftPressed = shift });
        await Frames(1);
    }

    /// <summary>The dialog a right click (or Insert) just opened, or null.</summary>
    Window Opened(Window before) => LastDialog != null && LastDialog != before && IsInstanceValid(LastDialog) && LastDialog.Visible ? LastDialog : null;

    /// <summary>The smallest distance between two points, or a point and a wall it isn't on (the snap range must be
    /// less).</summary>
    static double SmallestFeature(MapDocument d)
    {
        double best = 1e9;
        var pts = d.Points.Select(p => (x: (double)p.X, y: (double)p.Y)).Distinct().ToList();
        for (int i = 0; i < pts.Count; i++)
        {
            for (int j = i + 1; j < pts.Count; j++)
                best = Math.Min(best, Math.Sqrt(Math.Pow(pts[i].x - pts[j].x, 2) + Math.Pow(pts[i].y - pts[j].y, 2)));
            for (int w = 0; w < d.Walls.Count; w++)
            {
                var (ax, ay, bx, by) = BuildGeom.WallEnds(d, w);
                double dist = MapDocument.SegmentDistance(pts[i].x, pts[i].y, ax, ay, bx, by);
                if (dist > 0.01)
                    best = Math.Min(best, dist);
            }
        }
        return Math.Max(best, 1);
    }

    /// <summary>A point well inside sector s (the middle of the widest stretch of its fill).</summary>
    static Vector2 Inside(MapDocument d, int s)
    {
        var edges = new List<(double x1, double y1, double x2, double y2)>();
        var ys = new SortedSet<double>();
        foreach (var loop in BuildGeom.Loops(d, s))
            foreach (int w in loop)
            {
                var e = BuildGeom.WallEnds(d, w);
                edges.Add(e);
                ys.Add(e.y1);
            }
        var yl = ys.ToList();
        var best = Vector2.Zero;
        double bestW = -1;
        for (int i = 0; i + 1 < yl.Count; i++)
        {
            double ym = (yl[i] + yl[i + 1]) * 0.5;
            var xs = edges.Where(e => (e.y1 <= ym) != (e.y2 <= ym)).Select(e => e.x1 + (e.x2 - e.x1) * (ym - e.y1) / (e.y2 - e.y1)).OrderBy(x => x).ToList();
            for (int k = 0; k + 1 < xs.Count; k += 2)
                if (Math.Min(xs[k + 1] - xs[k], yl[i + 1] - yl[i]) > bestW)
                {
                    bestW = Math.Min(xs[k + 1] - xs[k], yl[i + 1] - yl[i]);
                    best = new Vector2((float)((xs[k] + xs[k + 1]) * 0.5), (float)ym);
                }
        }
        return best;
    }

    // ---------------------------------------------------------------- the steps

    static double Area(MapDocument d, int s) => BuildGeom.LoopArea(d, BuildGeom.OuterLoop(d, s));

    /// <summary>Is sector s a room over a room: does its inside lie in a larger sector's fill (the larger is the level)?</summary>
    static bool Over(MapDocument d, int s)
    {
        var q = Inside(d, s);
        double a = Area(d, s);
        return Enumerable.Range(0, d.Sectors.Count).Any(o => o != s && Area(d, o) > a && BuildGeom.Inside(d, o, q.X, q.Y));
    }

    /// <summary>Does sector s lie over another anywhere (a corner or the middle of a side inside another's fill, not on
    /// its walls)? It is then drawn with Alt (over what is there), and what fills its holes as rooms in it.</summary>
    static bool OverAny(MapDocument d, int s)
    {
        if (Over(d, s))
            return true;
        var outer = BuildGeom.OuterLoop(d, s).Select(w => new Vector2(d.Walls[w].X, d.Walls[w].Y)).ToList();
        var samples = outer.Concat(outer.Select((p, i) => (p + outer[(i + 1) % outer.Count]) * 0.5f));
        foreach (var p in samples)
            for (int o = 0; o < d.Sectors.Count; o++)
            {
                if (o == s || !BuildGeom.Inside(d, o, p.X, p.Y))
                    continue;
                bool onWall = false;
                for (int w = d.Sectors[o].WallPtr; w < d.Sectors[o].WallPtr + d.Sectors[o].WallNum && !onWall; w++)
                {
                    var (ax, ay, bx, by) = BuildGeom.WallEnds(d, w);
                    onWall = MapDocument.SegmentDistance(p.X, p.Y, ax, ay, bx, by) < 0.5;
                }
                if (!onWall)
                    return true;
            }
        return false;
    }

    /// <summary>How many sectors' holes sector s lies in (the rooms inside a sector are drawn before it: it is drawn
    /// round them).</summary>
    static int Depth(MapDocument d, int s)
    {
        var p = d.Walls[d.Sectors[s].WallPtr];
        int depth = 0;
        for (int a = 0; a < d.Sectors.Count; a++)
        {
            if (a == s)
                continue;
            var loops = BuildGeom.Loops(d, a);
            if (loops.Count < 2)
                continue;
            var outer = loops.First(l => BuildGeom.LoopIsOuter(d, l)).Select(w => new Vector2I(d.Walls[w].X, d.Walls[w].Y)).ToList();
            // inside its outer loop, not in its fill: in one of its holes
            var q = Inside(d, s);
            if (MapDocument.InPolygon(outer, q.X, q.Y) && !BuildGeom.Inside(d, a, q.X, q.Y))
                depth++;
        }
        return depth;
    }

    /// <summary>Each sector's outer loop clicked in Draw Lines mode and closed on its first point, the deepest first
    /// (a sector with holes drawn round the rooms in them). The new sector of each original one (-1: not made).</summary>
    async Task<int[]> DrawSectors(MapDocument src, List<string> notes)
    {
        var newOf = Enumerable.Repeat(-1, src.Sectors.Count).ToArray();
        // the level first, rooms over rooms after it (a sector drawn round others takes them as islands: the stacked
        // ones mustn't be there yet), the deepest first
        // a room over a room filling a hole alone is drawn last, as a room in its holder (Tab to the holder);
        // the level by depth (islands before the sector round them), then the rooms over rooms, the largest first
        var holder = new int[src.Sectors.Count];
        Array.Fill(holder, -1);
        var overAny = Enumerable.Range(0, src.Sectors.Count).Select(s => OverAny(src, s)).ToArray();
        for (int h = 0; h < src.Sectors.Count; h++)
            foreach (var hole in BuildGeom.Loops(src, h).Where(l => !BuildGeom.LoopIsOuter(src, l)))
            {
                var set = hole.Select(w => (src.Walls[w].X, src.Walls[w].Y)).ToHashSet();
                // (only a room over a room: one of the level is drawn first and the holder round it, an island)
                // (its outline the hole's, and joined to it: holes of other sectors may have that outline too)
                for (int o = 0; o < src.Sectors.Count; o++)
                    if (o != h && (Over(src, o) || overAny[h]) && BuildGeom.OuterLoop(src, o) is var ol && ol.Count == set.Count && ol.All(w => set.Contains((src.Walls[w].X, src.Walls[w].Y)))
                        && ol.Any(w => src.Walls[w].NextSector == h))
                        holder[o] = h;
            }
        // what fills a room's holes is a room in it too (a ring round a column, drawn as a room: the column a room in the ring)
        for (bool more = true; more;)
        {
            more = false;
            for (int h = 0; h < src.Sectors.Count; h++)
            {
                if (holder[h] < 0)
                    continue;
                foreach (var hole in BuildGeom.Loops(src, h).Where(l => !BuildGeom.LoopIsOuter(src, l)))
                {
                    var set = hole.Select(w => (src.Walls[w].X, src.Walls[w].Y)).ToHashSet();
                    for (int o = 0; o < src.Sectors.Count; o++)
                        if (o != h && holder[o] < 0 && BuildGeom.OuterLoop(src, o) is var ol && ol.Count == set.Count
                            && ol.All(w => set.Contains((src.Walls[w].X, src.Walls[w].Y))) && ol.Any(w => src.Walls[w].NextSector == h))
                        {
                            holder[o] = h;
                            more = true;
                        }
                }
            }
        }
        var plain = Enumerable.Range(0, src.Sectors.Count).Where(s => holder[s] < 0).ToList();
        var order = plain.Where(s => !overAny[s]).OrderByDescending(s => Depth(src, s)).ThenBy(s => s)
            .Concat(plain.Where(s => overAny[s]).OrderByDescending(s => Area(src, s))).ToList();
        var rooms = Enumerable.Range(0, src.Sectors.Count).Where(s => holder[s] >= 0).ToList();
        foreach (int s in order)
        {
            var loops = BuildGeom.Loops(src, s);
            if (loops.Count == 0)
                continue;
            var outer = loops.FirstOrDefault(l => BuildGeom.LoopIsOuter(src, l)) ?? loops[0];
            int before = Doc.Sectors.Count;
            // drawn as an ordinary sector; refused (it lies over others: a room over a room), again with Alt on the
            // closing click
            // a room over a room: Alt straight away (drawn plainly inside its level's sector it would be a room in it)
            string refused = null;
            foreach (bool over in overAny[s] ? new[] { true } : new[] { false, true })
            {
                UiDo("drawlinesmode");
                foreach (int w in outer)
                    await ClickAt(MouseButton.Left, new Vector2(src.Walls[w].X, src.Walls[w].Y));
                var first = new Vector2(src.Walls[outer[0]].X, src.Walls[outer[0]].Y);
                View.CentreOn(first);
                await Frames(1);
                await Move(first);
                await PressMods(MouseButton.Left, first, true, over);
                await PressMods(MouseButton.Left, first, false, over);
                if (Doc.Sectors.Count == before + 1)
                {
                    newOf[s] = Doc.Sectors.Count - 1;
                    if (over)
                        notes.Add($"sector {s}: drawn over the sectors there (Alt): \"{_status.Text}\"" + (refused != null ? $" (drawn plainly: \"{refused}\")" : ""));
                    break;
                }
                refused = _status.Text;
                // drawn plainly over sectors it cut them (UDB's drawing): not this sector's shape; undone, then Alt
                if (Doc.Sectors.Count > before + 1)
                {
                    refused = $"cut the sectors there into {Doc.Sectors.Count - before} more";
                    UiDo("undo");
                }
                if (over)
                    notes.Add($"sector {s}: its outline ({outer.Count} points) made no sector: \"{_status.Text}\"");
            }
        }
        // the rooms filling holes, each once its holder is there (a room's holder may be a room)
        for (int pass = 0; pass < 8 && rooms.Count > 0; pass++)
            foreach (int s in rooms.ToList())
            {
                int h = holder[s];
                if (newOf[h] < 0)
                    continue;
                rooms.Remove(s);
                var outer = BuildGeom.OuterLoop(src, s);
                var pts = outer.Select(w => new Vector2I(src.Walls[w].X, src.Walls[w].Y)).ToList();
                int before = Doc.Sectors.Count;
                UiDo("drawlinesmode");
                foreach (var p in pts)
                    await ClickAt(MouseButton.Left, p);
                var probe = ((Vector2)pts[0] + pts[1]) * 0.5f;
                int pick = Doc.SectorsAt(probe.X, probe.Y).IndexOf(newOf[h]);
                for (int k = 0; k < pick; k++)
                    await PressKey(Godot.Key.Tab);
                await ClickAt(MouseButton.Left, pts[0]);
                if (Doc.Sectors.Count == before + 1)
                    newOf[s] = Doc.Sectors.Count - 1;
                else
                    notes.Add($"sector {s}: drawn as a room in sector {h} it made none: \"{_status.Text}\"");
            }
        notes.Add($"drawn: {newOf.Count(i => i >= 0)} of {src.Sectors.Count} sectors; {Doc.Walls.Count} walls (the original {src.Walls.Count})");
        for (int s = 0; s < src.Sectors.Count; s++)
            if (newOf[s] >= 0 && Doc.Sectors[newOf[s]].WallNum != src.Sectors[s].WallNum)
            {
                notes.Add($"sector {s}: {Doc.Sectors[newOf[s]].WallNum} walls drawn, the original has {src.Sectors[s].WallNum}");
                string L(MapDocument d, int sec) => string.Join(" | ", BuildGeom.Loops(d, sec).Select(l => string.Join(" ", l.Select(w => $"{d.Walls[w].X},{d.Walls[w].Y}"))));
                notes.Add($"    drawn:    {L(Doc, newOf[s])}");
                notes.Add($"    original: {L(src, s)}");
            }
        return newOf;
    }

    /// <summary>A hole no sector fills (a pillar of nothing): drawn as a room inside its sector, then that room deleted
    /// (Sectors mode, Delete): the hole stays.</summary>
    async Task VoidHoles(MapDocument src, int[] newOf, List<string> notes)
    {
        for (int s = 0; s < src.Sectors.Count; s++)
        {
            if (newOf[s] < 0)
                continue;
            foreach (var hole in BuildGeom.Loops(src, s).Where(l => !BuildGeom.LoopIsOuter(src, l)))
            {
                var pts = hole.Select(w => new Vector2I(src.Walls[w].X, src.Walls[w].Y)).ToList();
                // filled: its walls joined to what is in it (a room over a room merely lying there doesn't fill it)
                if (hole.Any(w => src.Walls[w].NextSector >= 0))
                    continue;
                int before = Doc.Sectors.Count;
                UiDo("drawlinesmode");
                foreach (var p in pts)
                    await ClickAt(MouseButton.Left, p);
                // the sector it goes in, where others share the place: Tab
                var probe = ((Vector2)pts[0] + pts[1]) * 0.5f;
                int pick = Doc.SectorsAt(probe.X, probe.Y).IndexOf(newOf[s]);
                for (int k = 0; k < pick; k++)
                    await PressKey(Godot.Key.Tab);
                await ClickAt(MouseButton.Left, pts[0]);
                if (Doc.Sectors.Count != before + 1)
                {
                    notes.Add($"sector {s}: its empty hole ({pts.Count} points) drawn as a room made none: \"{_status.Text}\"");
                    continue;
                }
                UiDo("sectorsmode");
                int room = Doc.Sectors.Count - 1;
                if (await HoverOn(Inside(Doc, room), ElementKind.Sector, room))
                    await PressKey(Godot.Key.Delete);
                if (Doc.Sectors.Count != before)
                    notes.Add($"sector {s}: the room in its hole couldn't be deleted: \"{_status.Text}\"");
            }
        }
    }

    /// <summary>The new map's wall of the original's wall w: the same ends, in the sector drawn for w's (walls of rooms
    /// over rooms lie on each other), or -1.</summary>
    int NewWall(MapDocument src, int w, int[] newOf)
    {
        int s = src.Walls[w].Sector;
        int ns = s >= 0 && s < newOf.Length ? newOf[s] : -1;
        if (ns < 0)
            return -1;
        var ends = BuildGeom.WallEnds(src, w);
        for (int k = Doc.Sectors[ns].WallPtr; k < Doc.Sectors[ns].WallPtr + Doc.Sectors[ns].WallNum; k++)
            if (BuildGeom.WallEnds(Doc, k) == ends)
                return k;
        return -1;
    }

    /// <summary>The joins as the original has them: a wall joined to another than its original partner (a room over
    /// a room lying there too) detached (Backspace), then the two meant selected (Tab to each, a click) and joined (J).</summary>
    async Task RepairJoins(MapDocument src, int[] newOf, List<string> notes)
    {
        UiDo("linedefsmode");
        int repaired = 0, failed = 0;
        async Task<bool> Line(int wall, bool select)
        {
            var (ax, ay, bx, by) = BuildGeom.WallEnds(Doc, wall);
            var at = new Vector2((ax + bx) * 0.5f, (ay + by) * 0.5f);
            if (!await HoverOn(at, ElementKind.Line, Doc.LineOf[wall]))
                return false;
            if (select)
            {
                await Press(MouseButton.Left, at, true);
                await Press(MouseButton.Left, at, false);
            }
            return true;
        }
        for (int w = 0; w < src.Walls.Count; w++)
        {
            int a = NewWall(src, w, newOf);
            if (a < 0)
                continue;
            int sp = src.Walls[w].NextWall;
            bool red = sp >= 0 && sp < src.Walls.Count && src.Walls[sp].NextWall == w;
            int b = red ? NewWall(src, sp, newOf) : -1;
            if (red ? Doc.Walls[a].NextWall == b || b < 0 : Doc.Walls[a].NextWall < 0)
                continue;
            if (Doc.Walls[a].NextWall >= 0)
            {
                if (!await Line(a, false))
                {
                    failed++;
                    continue;
                }
                await PressKey(Godot.Key.Backspace);
                a = NewWall(src, w, newOf);
            }
            if (red)
            {
                b = NewWall(src, sp, newOf);
                if (Doc.Walls[b].NextWall >= 0 && await Line(b, false))
                {
                    await PressKey(Godot.Key.Backspace);
                    a = NewWall(src, w, newOf);
                    b = NewWall(src, sp, newOf);
                }
                await PressKey(Godot.Key.C);
                if (!await Line(a, true) || !await Line(b, true))
                {
                    failed++;
                    continue;
                }
                await PressKey(Godot.Key.J);
                a = NewWall(src, w, newOf);
                b = NewWall(src, sp, newOf);
                if (Doc.Walls[a].NextWall != b)
                {
                    failed++;
                    notes.Add($"wall {w}: couldn't be joined to wall {sp} ({_status.Text})");
                    continue;
                }
            }
            repaired++;
        }
        if (repaired + failed > 0)
            notes.Add($"joins: {repaired} put right (detached, joined), {failed} not");
    }

    /// <summary>Points a sector got that the original's hasn't (another sector's corner on its side when it was drawn: a
    /// room over a room there): the walls there detached if joined (Backspace), the point deleted (Vertices mode,
    /// Delete: its two walls become one).</summary>
    async Task ExtraPoints(MapDocument src, int[] newOf, List<string> notes)
    {
        int deleted = 0, kept = 0;
        for (int s = 0; s < src.Sectors.Count; s++)
        {
            int ns = newOf[s];
            if (ns < 0)
                continue;
            var want = new HashSet<(int, int)>(Enumerable.Range(src.Sectors[s].WallPtr, src.Sectors[s].WallNum).Select(w => (src.Walls[w].X, src.Walls[w].Y)));
            for (int guard = 0; guard < 64; guard++)
            {
                int extra = Enumerable.Range(Doc.Sectors[ns].WallPtr, Doc.Sectors[ns].WallNum)
                    .FirstOrDefault(w => !want.Contains((Doc.Walls[w].X, Doc.Walls[w].Y)), -1);
                if (extra < 0)
                    break;
                // the walls at it (this one and the one before it) white
                UiDo("linedefsmode");
                foreach (int w in new[] { extra, Doc.PrevWall[extra] })
                    if (w >= 0 && Doc.Walls[w].NextWall >= 0)
                    {
                        var (ax, ay, bx, by) = BuildGeom.WallEnds(Doc, w);
                        if (await HoverOn(new Vector2((ax + bx) * 0.5f, (ay + by) * 0.5f), ElementKind.Line, Doc.LineOf[w]))
                            await PressKey(Godot.Key.Backspace);
                    }
                extra = Enumerable.Range(Doc.Sectors[ns].WallPtr, Doc.Sectors[ns].WallNum)
                    .FirstOrDefault(w => !want.Contains((Doc.Walls[w].X, Doc.Walls[w].Y)), -1);
                UiDo("verticesmode");
                int count = Doc.Walls.Count;
                if (extra >= 0 && await HoverOn(new Vector2(Doc.Walls[extra].X, Doc.Walls[extra].Y), ElementKind.Point, Doc.PointOf[extra]))
                    await PressKey(Godot.Key.Delete);
                if (Doc.Walls.Count >= count)
                {
                    kept++;
                    notes.Add($"sector {s}: its extra point at {Doc.Walls[extra].X}, {Doc.Walls[extra].Y} couldn't be deleted ({_status.Text})");
                    break;
                }
                deleted++;
            }
        }
        if (deleted + kept > 0)
            notes.Add($"extra points: {deleted} deleted, {kept} not");
    }

    /// <summary>Where a sector's first wall isn't the original's: the wall hovered from inside the sector, Shift+F.</summary>
    async Task FirstWalls(MapDocument src, int[] newOf, List<string> notes)
    {
        UiDo("linedefsmode");
        for (int s = 0; s < src.Sectors.Count; s++)
        {
            int ns = newOf[s];
            if (ns < 0)
                continue;
            var want = BuildGeom.WallEnds(src, src.Sectors[s].WallPtr);
            if (BuildGeom.WallEnds(Doc, Doc.Sectors[ns].WallPtr) == want)
                continue;
            // the middle of that wall, a little inside the sector (the side the mouse is on picks the sector)
            var mid = new Vector2((want.x1 + want.x2) * 0.5f, (want.y1 + want.y2) * 0.5f);
            var dir = new Vector2(want.x2 - want.x1, want.y2 - want.y1).Normalized();
            var normal = new Vector2(-dir.Y, dir.X);
            var at = BuildGeom.Inside(Doc, ns, mid.X + normal.X, mid.Y + normal.Y) ? mid + normal : mid - normal;
            int target = Enumerable.Range(Doc.Sectors[ns].WallPtr, Doc.Sectors[ns].WallNum).FirstOrDefault(w => BuildGeom.WallEnds(Doc, w) == want, -1);
            if (target < 0 || !await HoverOn(at, ElementKind.Line, Doc.LineOf[target]))
            {
                notes.Add($"sector {s}: the wall to make its first can't be highlighted");
                continue;
            }
            await PressKey(Godot.Key.F, true);
            if (BuildGeom.WallEnds(Doc, Doc.Sectors[ns].WallPtr) != want)
                notes.Add($"sector {s}: its first wall couldn't be set ({_status.Text})");
        }
    }

    static readonly HashSet<string> XNever = new() { "Reference", "Marker0", "Marker1", "Target", "TargetX", "TargetY", "TargetZ", "BurnSource" };

    /// <summary>An x-structure's fields typed into the dialog's tab as Blood.XFields lays them out: a number in its
    /// field (a height in the units shown), a flag's check box, each bit's check box.</summary>
    static void TypeX(Window d, string tab, object x, Blood.FieldSpec[] specs, List<string> notes, string what, string presence)
    {
        DialogDriver.Check(d, tab, presence, x != null);
        if (x == null)
            return;
        foreach (var spec in specs)
        {
            if (XNever.Contains(spec.Field))
                continue;
            int v = (int)x.GetType().GetField(spec.Field).GetValue(x);
            bool ok;
            switch (spec.Kind)
            {
                case Blood.FieldKind.Flag:
                    ok = DialogDriver.Check(d, tab, spec.Label, v != 0) && v is 0 or 1;
                    break;
                case Blood.FieldKind.Bits:
                    ok = true;
                    int known = 0;
                    foreach (var (bit, name) in spec.Choices)
                    {
                        known |= bit;
                        ok &= DialogDriver.Check(d, tab, name, (v & bit) != 0);
                    }
                    ok &= (v & ~known) == 0;
                    break;
                default:
                    ok = DialogDriver.Has(d, tab, spec.Label);
                    if (ok)
                        DialogDriver.Set(d, tab, spec.Label, spec.Kind == Blood.FieldKind.Z ? Units.Z(v) : I(v));
                    break;
            }
            if (!ok && v != 0)
                notes.Add($"{what}: x-field {spec.Field} has no field in the dialog");
        }
    }

    static void TypeFlags(Window d, string tab, (int bit, string name)[] bits, int value, List<string> notes, string what)
    {
        int known = 0;
        foreach (var (bit, flag) in bits)
        {
            known |= bit;
            DialogDriver.Check(d, tab, flag, (value & bit) != 0);
        }
        if ((value & ~known) != 0)
            notes.Add($"{what}: flag bits {value & ~known} have no check box");
    }

    async Task SectorProperties(MapDocument src, int[] newOf, List<string> notes)
    {
        UiDo("sectorsmode");
        for (int s = 0; s < src.Sectors.Count; s++)
        {
            int ns = newOf[s];
            if (ns < 0)
                continue;
            var o = src.Sectors[s];
            var at = Inside(Doc, ns);
            var before = LastDialog;
            if (!await HoverOn(at, ElementKind.Sector, ns))
            {
                notes.Add($"sector {s}: it can't be highlighted ({_status.Text})");
                continue;
            }
            await Press(MouseButton.Right, at, true);
            await Press(MouseButton.Right, at, false);
            var d = Opened(before);
            if (d == null)
            {
                notes.Add($"sector {s}: a right click inside it opened no dialog ({_status.Text})");
                continue;
            }
            foreach (var (tab, c) in new[] { ("Floor", false), ("Ceiling", true) })
            {
                DialogDriver.Set(d, tab, "Picture", I(c ? o.CeilingPicnum : o.FloorPicnum));
                DialogDriver.Set(d, tab, "Height", Units.Z(c ? o.CeilingZ : o.FloorZ));
                DialogDriver.Set(d, tab, "Slope", I(c ? o.CeilingHeinum : o.FloorHeinum));
                DialogDriver.Set(d, tab, "Shade", I(c ? o.CeilingShade : o.FloorShade));
                DialogDriver.Set(d, tab, "Palette", I(c ? o.CeilingPal : o.FloorPal));
                DialogDriver.Set(d, tab, "X panning", I(c ? o.CeilingXPanning : o.FloorXPanning));
                DialogDriver.Set(d, tab, "Y panning", I(c ? o.CeilingYPanning : o.FloorYPanning));
                TypeFlags(d, tab, Blood.EditDialogs.PlaneBits, c ? o.CeilingStat : o.FloorStat, notes, $"sector {s} {tab.ToLowerInvariant()}");
            }
            DialogDriver.Set(d, "Effects", "Type", I(o.Type));
            DialogDriver.Set(d, "Effects", "Visibility", I(o.Visibility));
            DialogDriver.Set(d, "Effects", "Hitag", I(o.Hitag));
            TypeX(d, "Effects", o.Xs, Blood.XFields.Sector, notes, $"sector {s}", "Has an x-sector");
            DialogDriver.Ok(d);
            await Frames(1);
        }
    }

    async Task WallProperties(MapDocument src, int[] newOf, List<string> notes)
    {
        UiDo("linedefsmode");
        var origOf = new Dictionary<int, int>();
        for (int w = 0; w < src.Walls.Count; w++)
            if (NewWall(src, w, newOf) is var nw and >= 0)
                origOf[nw] = w;
        for (int l = 0; l < Doc.Lines.Count; l++)
        {
            var line = Doc.Lines[l];
            int front = origOf.GetValueOrDefault(line.Front, -1);
            int back = line.Back >= 0 ? origOf.GetValueOrDefault(line.Back, -1) : -1;
            if (front < 0)
            {
                notes.Add($"line {l} (wall {line.Front}): no wall of the original runs there");
                continue;
            }
            var (ax, ay, bx, by) = BuildGeom.WallEnds(Doc, line.Front);
            var at = new Vector2((ax + bx) * 0.5f, (ay + by) * 0.5f);
            var before = LastDialog;
            if (!await HoverOn(at, ElementKind.Line, l))
            {
                notes.Add($"wall {front}: its line can't be highlighted ({_status.Text})");
                continue;
            }
            await Press(MouseButton.Right, at, true);
            await Press(MouseButton.Right, at, false);
            var d = Opened(before);
            if (d == null)
            {
                notes.Add($"wall {front}: a right click on it opened no dialog ({_status.Text})");
                continue;
            }
            // each side its own flags (the dialog would copy the front's blocking and hitscan onto the back)
            DialogDriver.Check(d, "Front", "Blocking and hitscan flags on both sides", false);
            int origBack = src.Walls[front].NextWall;
            if (line.Back < 0 && origBack >= 0)
                notes.Add($"wall {front}: white here, red in the original (its other side, wall {origBack}, wasn't joined)");
            foreach (var (tab, w) in new[] { ("Front", front), ("Back", back) })
            {
                if (w < 0)
                    continue;
                var o = src.Walls[w];
                DialogDriver.Set(d, tab, "Picture", I(o.Picnum));
                DialogDriver.Set(d, tab, "Masked picture", I(o.OverPicnum));
                DialogDriver.Set(d, tab, "Shade", I(o.Shade));
                DialogDriver.Set(d, tab, "Palette", I(o.Pal));
                DialogDriver.Set(d, tab, "X repeat", I(o.XRepeat));
                DialogDriver.Set(d, tab, "Y repeat", I(o.YRepeat));
                DialogDriver.Set(d, tab, "X panning", I(o.XPanning));
                DialogDriver.Set(d, tab, "Y panning", I(o.YPanning));
                DialogDriver.Set(d, tab, "Type", I(o.Type));
                DialogDriver.Set(d, tab, "Hitag", I(o.Hitag));
                TypeFlags(d, tab, Blood.EditDialogs.WallBits, o.Cstat, notes, $"wall {w}");
                TypeX(d, tab, o.Xw, Blood.XFields.Wall, notes, $"wall {w}", "Has an x-wall");
            }
            DialogDriver.Ok(d);
            await Frames(1);
        }
    }

    /// <summary>The markers the sector types made (Sectors' dialogs) moved where the original's are: each one's dialog
    /// (a right click on it in Sprites mode), its position and look typed.</summary>
    async Task PlaceMarkers(MapDocument src, int[] newOf, List<string> notes)
    {
        UiDo("thingsmode");
        foreach (var o in src.Sprites.Where(sp => sp.StatNum == 10))
        {
            int owner = o.Owner >= 0 && o.Owner < newOf.Length ? newOf[o.Owner] : -1;
            int i = Enumerable.Range(0, Doc.Sprites.Count).FirstOrDefault(j => Doc.Sprites[j].StatNum == 10 && Doc.Sprites[j].Owner == owner
                && Doc.Sprites[j].Type == o.Type, -1);
            if (owner < 0 || i < 0)
            {
                notes.Add($"marker (type {o.Type}) of sector {o.Owner}: its sector's type made none");
                continue;
            }
            var m = Doc.Sprites[i];
            var at = new Vector2(m.X, m.Y);
            var before = LastDialog;
            if (!await HoverOn(at, ElementKind.Sprite, i))
            {
                notes.Add($"marker of sector {o.Owner}: it can't be highlighted ({_status.Text})");
                continue;
            }
            await Press(MouseButton.Right, at, true);
            await Press(MouseButton.Right, at, false);
            var d = Opened(before);
            if (d == null)
            {
                notes.Add($"marker of sector {o.Owner}: a right click on it opened no dialog ({_status.Text})");
                continue;
            }
            DialogDriver.Set(d, "Sprite", "X", Units.Xy(o.X));
            DialogDriver.Set(d, "Sprite", "Y", Units.Xy(o.Y));
            TypeSprite(d, o, notes, $"marker of sector {o.Owner}");
            DialogDriver.Ok(d);
            await Frames(1);
        }
    }

    /// <summary>A sprite's look typed into its dialog (not its position or type).</summary>
    static void TypeSprite(Window d, BSprite o, List<string> notes, string what)
    {
        DialogDriver.Set(d, "Sprite", "Picture", I(o.Picnum));
        DialogDriver.Set(d, "Sprite", "Z", Units.Z(o.Z));
        DialogDriver.Set(d, "Sprite", "Angle", I(o.Ang));
        DialogDriver.Set(d, "Sprite", "Shade", I(o.Shade));
        DialogDriver.Set(d, "Sprite", "Palette", I(o.Pal));
        DialogDriver.Set(d, "Sprite", "X repeat", I(o.XRepeat));
        DialogDriver.Set(d, "Sprite", "Y repeat", I(o.YRepeat));
        DialogDriver.Set(d, "Sprite", "Clip distance", I(o.ClipDist));
        DialogDriver.Set(d, "Sprite", "X offset", I(o.XOffset));
        DialogDriver.Set(d, "Sprite", "Y offset", I(o.YOffset));
        DialogDriver.Set(d, "Sprite", "Hitag", I(o.Flags));
        DialogDriver.Set(d, "Sprite", "Status", I(o.StatNum));
        TypeFlags(d, "Sprite", Blood.EditDialogs.SpriteBits, o.Cstat, notes, what);
        TypeX(d, "X-sprite", o.Xsp, Blood.XFields.Sprite, notes, what, "Has an x-sprite");
    }

    async Task PlaceSprites(MapDocument src, List<string> notes)
    {
        UiDo("thingsmode");
        for (int i = 0; i < src.Sprites.Count; i++)
        {
            var o = src.Sprites[i];
            if (o.StatNum == 10)
                continue;
            // the browser's type, the cursor there, Insert: the new sprite's dialog
            SpriteType = o.Type;
            var at = new Vector2(o.X, o.Y);
            int count = Doc.Sprites.Count;
            var before = LastDialog;
            await Hover(at);
            await PressKey(Godot.Key.Insert);
            var d = Opened(before);
            if (Doc.Sprites.Count != count + 1 || d == null)
            {
                notes.Add($"sprite {i}: Insert at {o.X}, {o.Y} placed no sprite or opened no dialog ({_status.Text})");
                continue;
            }
            TypeSprite(d, o, notes, $"sprite {i}");
            DialogDriver.Ok(d);
            await Frames(1);
        }
    }

    async Task Header(MapDocument src)
    {
        var before = LastDialog;
        UiDo("mapoptions");
        await Frames(1);
        var d = Opened(before);
        if (d == null)
            return;
        DialogDriver.Set(d, null, "Visibility", I(src.Map.Visibility));
        DialogDriver.Set(d, null, "Song id", I(src.Map.SongId));
        DialogDriver.Set(d, null, "Parallax type", I(src.Map.ParallaxType));
        DialogDriver.Set(d, null, "Sky tiles (bits)", I(src.Map.PSkyBits));
        DialogDriver.Set(d, null, "Sky offsets", string.Join(", ", src.Map.SkyOfs));
        DialogDriver.Ok(d);
        await Frames(1);
    }

    // ---------------------------------------------------------------- the comparison

    // structure (indices, links), and the x-structures (compared on their own)
    static readonly HashSet<string> SectorStructure = new() { "WallPtr", "WallNum", "Extra", "Xs" };
    static readonly HashSet<string> WallStructure = new() { "X", "Y", "Point2", "NextWall", "NextSector", "Extra", "Sector", "Xw" };
    static readonly HashSet<string> SpriteStructure = new() { "Index", "FileIndex", "Extra", "Owner", "SectNum", "Xsp" };
    static readonly HashSet<string> XSkip = new(XNever) { "Raw" };

    /// <summary>The fields of a and b (records or x-structures) that differ, but those in skip.</summary>
    static IEnumerable<string> Differ(object a, object b, HashSet<string> skip)
    {
        if (a == null && b == null)
            yield break;
        if (a == null || b == null)
        {
            yield return a == null ? "x-structure added" : "x-structure missing";
            yield break;
        }
        foreach (var f in a.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            if (skip.Contains(f.Name))
                continue;
            object va = f.GetValue(a), vb = f.GetValue(b);
            // an angle is the same turned a whole turn (the dialog keeps 0..2047)
            if (f.Name == "Ang" && va is int aa && vb is int ab && (aa & 2047) == (ab & 2047))
                continue;
            if (!Equals(va, vb))
                yield return $"{f.Name} {va} -> {vb}";
        }
        // the bits no field names (an x-structure's Raw): as written, the structure's fields (markers' indices) the same
        if (a.GetType().GetMethod("Write") is { } write && a.GetType().GetProperty("Size") is { } size
            && !Differ(a, b, skip, false).Any())
        {
            var ba = new byte[Math.Max((int)size.GetValue(a), (int)size.GetValue(b))];
            var bb = new byte[ba.Length];
            var structure = a.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance)
                .Where(f => XNever.Contains(f.Name) && f.FieldType == typeof(int)).ToList();
            var kept = structure.Select(f => f.GetValue(b)).ToList();
            foreach (var f in structure)
                f.SetValue(b, f.GetValue(a));
            write.Invoke(a, new object[] { ba, 0 });
            write.Invoke(b, new object[] { bb, 0 });
            for (int k = 0; k < structure.Count; k++)
                structure[k].SetValue(b, kept[k]);
            for (int i = 0; i < ba.Length; i++)
                if (ba[i] != bb[i])
                {
                    yield return $"bits no field names differ (byte {i}: {ba[i]:x2} -> {bb[i]:x2})";
                    break;
                }
        }
    }

    static IEnumerable<string> Differ(object a, object b, HashSet<string> skip, bool _) =>
        a.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance)
            .Where(f => !skip.Contains(f.Name) && f.FieldType == typeof(int) && !Equals(f.GetValue(a), f.GetValue(b)))
            .Select(f => f.Name);

    /// <summary>The rebuilt map against the original: the number of differences (each noted).</summary>
    int Compare(MapDocument src, int[] newOf, List<string> notes)
    {
        int n = 0;
        var tally = new SortedDictionary<string, int>();
        void Note(string what, string field)
        {
            n++;
            string key = what.Split(' ')[0] + " " + field.Split(' ')[0];
            tally[key] = tally.GetValueOrDefault(key) + 1;
            if (tally[key] <= 3)
                notes.Add($"{what}: {field}");
        }
        for (int s = 0; s < src.Sectors.Count; s++)
        {
            if (newOf[s] < 0)
            {
                Note($"sector {s}", "missing");
                continue;
            }
            foreach (var f in Differ(src.Sectors[s], Doc.Sectors[newOf[s]], SectorStructure))
                Note($"sector {s}", f);
            foreach (var f in Differ(src.Sectors[s].Xs, Doc.Sectors[newOf[s]].Xs, XSkip))
                Note($"sector {s} x", f);
        }
        for (int w = 0; w < src.Walls.Count; w++)
        {
            var ends = BuildGeom.WallEnds(src, w);
            int nw = NewWall(src, w, newOf);
            if (nw < 0)
            {
                Note($"wall {w}", $"missing ({ends.Item1}, {ends.Item2} to {ends.Item3}, {ends.Item4})");
                continue;
            }
            var o = src.Walls[w];
            var r = Doc.Walls[nw];
            foreach (var f in Differ(o, r, WallStructure))
                Note($"wall {w}", f);
            int wantNext = o.NextSector >= 0 && o.NextSector < newOf.Length ? newOf[o.NextSector] : -1;
            if (r.NextSector != wantNext)
                Note($"wall {w}", $"next sector {r.NextSector}, the original's is new sector {wantNext}");
            foreach (var f in Differ(o.Xw, r.Xw, XSkip))
                Note($"wall {w} x", f);
        }
        if (Doc.Walls.Count != src.Walls.Count)
            Note("map", $"walls {src.Walls.Count} -> {Doc.Walls.Count}");
        var mine = Enumerable.Range(0, Doc.Sprites.Count).Where(j => Doc.Sprites[j].StatNum != 10).ToList();
        for (int i = 0, k = 0; i < src.Sprites.Count; i++)
        {
            var o = src.Sprites[i];
            BSprite r;
            if (o.StatNum == 10)
            {
                int owner = o.Owner >= 0 && o.Owner < newOf.Length ? newOf[o.Owner] : -1;
                r = Doc.Sprites.FirstOrDefault(sp => sp.StatNum == 10 && sp.Owner == owner && sp.Type == o.Type);
                if (r == null)
                {
                    Note($"sprite {i}", $"marker (type {o.Type}) of sector {o.Owner} missing");
                    continue;
                }
            }
            else if (k >= mine.Count)
            {
                Note($"sprite {i}", "missing");
                continue;
            }
            else
                r = Doc.Sprites[mine[k++]];
            foreach (var f in Differ(o, r, SpriteStructure))
                Note($"sprite {i}", f);
            int wantSector = o.SectNum >= 0 && o.SectNum < newOf.Length ? newOf[o.SectNum] : -1;
            if (r.SectNum != wantSector)
                Note($"sprite {i}", $"sector {r.SectNum}, the original's is new sector {wantSector}");
            foreach (var f in Differ(o.Xsp, r.Xsp, XSkip))
                Note($"sprite {i} x", f);
        }
        foreach (var f in new[] { "Visibility", "SongId", "ParallaxType", "PSkyBits" })
        {
            var p = typeof(BloodMapFile).GetField(f);
            if (!Equals(p.GetValue(src.Map), p.GetValue(Doc.Map)))
                Note("header", $"{f} {p.GetValue(src.Map)} -> {p.GetValue(Doc.Map)}");
        }
        if (!src.Map.SkyOfs.AsSpan().SequenceEqual(Doc.Map.SkyOfs))
            Note("header", $"sky offsets {string.Join(",", src.Map.SkyOfs)} -> {string.Join(",", Doc.Map.SkyOfs)}");
        notes.Add("");
        notes.Add($"{n} difference(s), by kind (the first 3 of each are listed above):");
        foreach (var (k, c) in tally.OrderByDescending(kv => kv.Value))
            notes.Add($"  {c,5}  {k}");
        return n;
    }
}
