// Chosen core: original code.
using System.Collections.Generic;
using System.Linq;
using Chosen.Forge.Config;
using Chosen.Forge.View;
using Chosen.Shared.Engine.Geometry;
using Godot;

namespace Chosen.Forge.Editing;

/// <summary>Vertices mode: the points (walls starting at one place that move together).</summary>
public class VerticesMode : ClassicMode
{
    public override string Name => "Vertices";
    public override string Action => "verticesmode";
    protected override ElementKind Kind => ElementKind.Point;

    protected override Element Pick(Vector2 m)
    {
        if (Doc == null)
            return Element.None;
        // points at one place that aren't joined (rooms over rooms): Tab goes through them
        double r = View.Units(HighlightRange);
        var near = new List<(int, double)>();
        for (int i = 0; i < Doc.Points.Count; i++)
        {
            // (the current layer's only: Document/Layers.cs)
            if (!Doc.PointEditable(i))
                continue;
            double d = System.Math.Sqrt(System.Math.Pow(Doc.Points[i].X - m.X, 2) + System.Math.Pow(Doc.Points[i].Y - m.Y, 2));
            if (d <= r)
                near.Add((i, d));
        }
        return PickAmong(ElementKind.Point, near);
    }

    protected override IEnumerable<int> InRect(Rect2 r)
    {
        for (int i = 0; i < Doc.Points.Count; i++)
            if (Doc.PointEditable(i) && r.HasPoint(new Vector2(Doc.Points[i].X, Doc.Points[i].Y)))
                yield return i;
    }

    protected override (List<int> points, List<int> sprites, Vector2 reference) DragSet(List<int> items)
    {
        var p = Doc.Points[Grabbed(items)];
        return (items, new List<int>(), new Vector2(p.X, p.Y));
    }

    /// <summary>Insert: a point on the wall nearest the cursor, where the cursor is (snapped onto the wall).</summary>
    public override void OnInsert() => InsertOnWall(Window, View, Doc);

    internal static void InsertOnWall(MainWindow w, Chosen.Forge.View.MapView2D v, Chosen.Forge.Document.MapDocument doc)
    {
        int line = doc.LineNear(v.Cursor.X, v.Cursor.Y, v.Units(HighlightRange * 2), doc.LineEditable);
        if (line < 0)
        {
            w.DisplayStatus("insert: no wall near the cursor");
            return;
        }
        int wall = doc.Lines[line].Front;
        var at = OnWall(doc, wall, v.Snap ? v.SnapToGrid(v.Cursor) : v.Cursor);
        w.Operation("Insert point", () => doc.InsertPoint(wall, at.X, at.Y) ? null : "that is a point already");
    }

    /// <summary>The point of wall w nearest p, rounded.</summary>
    internal static Vector2I OnWall(Chosen.Forge.Document.MapDocument doc, int w, Vector2 p)
    {
        var (ax, ay, bx, by) = BuildGeom.WallEnds(doc, w);
        double dx = bx - ax, dy = by - ay, len = dx * dx + dy * dy;
        double t = len > 0 ? System.Math.Clamp(((p.X - ax) * dx + (p.Y - ay) * dy) / len, 0, 1) : 0;
        return new Vector2I((int)System.Math.Round(ax + t * dx), (int)System.Math.Round(ay + t * dy));
    }

    /// <summary>Delete: the selected points, else the highlighted one (UDB's): the walls at each join into one, the
    /// points before and after it then joined; why a point can't go (rooms meeting there) in the status bar.</summary>
    public override void OnDelete()
    {
        var targets = Selection.Count > 0 ? Selection.ToList() : Highlighted.IsNone ? new List<int>() : new List<int> { Highlighted.Index };
        if (targets.Count == 0)
        {
            Window.DisplayStatus("delete: no point selected or under the cursor");
            return;
        }
        // by place: each deletion renumbers the points
        var places = targets.Where(p => p >= 0 && p < Doc.Points.Count).Select(p => new Vector2I(Doc.Points[p].X, Doc.Points[p].Y)).ToList();
        int done = 0;
        string why = null;
        Window.Operation(places.Count == 1 ? "Delete point" : $"Delete {places.Count} points", () =>
        {
            foreach (var at in places)
            {
                int p = Enumerable.Range(0, Doc.Points.Count).FirstOrDefault(i => Doc.Points[i].X == at.X && Doc.Points[i].Y == at.Y, -1);
                if (p < 0)
                    continue;
                string e = Doc.DeletePoint(p);
                if (e == null)
                    done++;
                else
                    why ??= e;
            }
            return done > 0 ? null : why ?? "nothing to delete";
        });
        if (done > 0 && why != null)
            Window.DisplayStatus($"deleted {done} of {places.Count} points; one couldn't go: {why}");
        Highlighted = Element.None;
    }

    // ---------------------------------------------------------------- a new point on a wall (UDB's)
    // With no point under the cursor, the place on the nearest wall where one would go shows (snapped: the grid's
    // place projected onto the wall); the edit button pressed there puts the point in, held and moved drags it.
    Vector2I? _ghost;
    int _ghostWall = -1;
    bool _inserted;

    public override void OnMouseMove(Vector2 map)
    {
        base.OnMouseMove(map);
        Vector2I? g = null;
        int wall = -1;
        if (Doc != null && Highlighted.IsNone)
        {
            int line = Doc.LineNear(map.X, map.Y, View.Units(HighlightRange), Doc.LineEditable);
            if (line >= 0)
            {
                wall = Doc.Lines[line].Front;
                var at = OnWall(Doc, wall, View.Snap && !ForgeInput.Shift ? View.SnapToGrid(map) : map);
                var (ax, ay, bx, by) = BuildGeom.WallEnds(Doc, wall);
                if (at != new Vector2I(ax, ay) && at != new Vector2I(bx, by))
                    g = at;
            }
        }
        if (g != _ghost)
            View.QueueRedraw();
        _ghost = g;
        _ghostWall = g != null ? wall : -1;
    }

    public override void OnMouseLeave()
    {
        base.OnMouseLeave();
        _ghost = null;
    }

    public override void OnEditBegin(Vector2 map)
    {
        _inserted = false;
        if (Highlighted.IsNone && _ghost is { } at && _ghostWall >= 0 && Doc != null)
        {
            int wall = _ghostWall;
            _ghost = null;
            if (!Window.Operation("Insert point", () => Doc.InsertPoint(wall, at.X, at.Y) ? null : "that is a point already"))
                return;
            // the new point under the cursor: moved with the button held, it is dragged (one undo step with the insert)
            Highlight(Pick(new Vector2(at.X, at.Y)));
            _inserted = true;
            JoinLastUndo = true;
            return;
        }
        base.OnEditBegin(map);
    }

    public override void OnEditEnd(Vector2 map)
    {
        // a click that put a point in: no properties dialog
        if (_inserted)
        {
            _inserted = false;
            JoinLastUndo = false;
            return;
        }
        base.OnEditEnd(map);
    }

    public override void OnRedraw(MapView2D v)
    {
        base.OnRedraw(v);
        if (_ghost is { } g)
            v.DrawGhostPoint(new Vector2(g.X, g.Y), ColorSettings.Highlight, 4);
    }

    public override void OnDragStop(Vector2 map)
    {
        _inserted = false;
        // (by place: the grabbed item may be gone from the highlight by now)
        var moved = DraggedPointPlaces();
        base.OnDragStop(map);
        if (!LastDragMoved)
            return;
        // a point dragged onto another point of its own loop would make a wall of no length: refused
        foreach (var p in moved)
            if (Doc.TwoCornersOfOneLoopAt(p))
            {
                Window.Undo.Revert();
                Window.UndoChanged();
                Window.DisplayStatus("a point can't be dragged onto another point of its own loop");
                return;
            }
    }
}

/// <summary>Walls mode (UDB's linedefs): a white wall, or a red wall and the wall behind it as one line.</summary>
public class WallsMode : ClassicMode
{
    public override string Name => "Walls";
    public override string Action => "linedefsmode";
    protected override ElementKind Kind => ElementKind.Line;

    protected override Element Pick(Vector2 m)
    {
        if (Doc == null)
            return Element.None;
        // lines lying on each other (rooms over rooms): Tab goes through them
        double r = View.Units(HighlightRange);
        var near = new List<(int, double)>();
        for (int i = 0; i < Doc.Lines.Count; i++)
        {
            if (!Doc.LineEditable(Doc.Lines[i]))
                continue;
            var (ax, ay, bx, by) = BuildGeom.WallEnds(Doc, Doc.Lines[i].Front);
            double d = Chosen.Forge.Document.MapDocument.SegmentDistance(m.X, m.Y, ax, ay, bx, by);
            if (d <= r)
                near.Add((i, d));
        }
        return PickAmong(ElementKind.Line, near);
    }

    protected override IEnumerable<int> InRect(Rect2 r)
    {
        for (int i = 0; i < Doc.Lines.Count; i++)
        {
            if (!Doc.LineEditable(Doc.Lines[i]))
                continue;
            var (ax, ay, bx, by) = BuildGeom.WallEnds(Doc, Doc.Lines[i].Front);
            if (r.HasPoint(new Vector2(ax, ay)) && r.HasPoint(new Vector2(bx, by)))
                yield return i;
        }
    }

    /// <summary>A line's points: its front wall's start and end.</summary>
    protected override (List<int> points, List<int> sprites, Vector2 reference) DragSet(List<int> items)
    {
        var pts = new HashSet<int>();
        foreach (int l in items)
        {
            int w = Doc.Lines[l].Front;
            pts.Add(Doc.PointOf[w]);
            pts.Add(Doc.PointOf[Doc.Walls[w].Point2]);
        }
        var g = Doc.Walls[Doc.Lines[Grabbed(items)].Front];
        return (pts.ToList(), new List<int>(), new Vector2(g.X, g.Y));
    }

    /// <summary>Insert: drawing lines from the cursor (UDB's).</summary>
    public override void OnInsert() => StartDrawing(View.Cursor);

    /// <summary>A double-click on a wall: the walls going on from it in a line, both ways: at each end the one going on
    /// straightest (where walls part, the straightest way), while it turns no more than 45 degrees (a curve of short walls
    /// followed, a room's corner not).</summary>
    protected override List<int> Within(int item) => item >= 0 && item < Doc.Lines.Count ? InLine(Doc, item) : null;

    protected override string WithinWhat(int item, int count) => $"wall {item} and the {count} wall{(count == 1 ? "" : "s")} in line with it";

    /// <summary>The lines in line with line l (not l itself): see Within.</summary>
    public static List<int> InLine(Chosen.Forge.Document.MapDocument d, int l)
    {
        Vector2I Start(int i) => new(d.Walls[d.Lines[i].Front].X, d.Walls[d.Lines[i].Front].Y);
        Vector2I End(int i) => new(d.Walls[d.Walls[d.Lines[i].Front].Point2].X, d.Walls[d.Walls[d.Lines[i].Front].Point2].Y);
        var at = new Dictionary<Vector2I, List<int>>();
        for (int i = 0; i < d.Lines.Count; i++)
        {
            if (!d.LineEditable(d.Lines[i]))
                continue;
            foreach (var p in new[] { Start(i), End(i) })
            {
                if (!at.TryGetValue(p, out var list))
                    at[p] = list = new List<int>();
                list.Add(i);
            }
        }
        var taken = new HashSet<int> { l };
        foreach (bool forward in new[] { true, false })
        {
            int cur = l;
            var from = forward ? Start(l) : End(l);
            var to = forward ? End(l) : Start(l);
            while (true)
            {
                var dir = ((Vector2)(to - from)).Normalized();
                int best = -1;
                float bestDot = Mathf.Cos(Mathf.DegToRad(45f)) - 1e-4f;
                Vector2I bestTo = default;
                foreach (int o in at.TryGetValue(to, out var list) ? list : new List<int>())
                {
                    if (o == cur)
                        continue;
                    var far = Start(o) == to ? End(o) : Start(o);
                    float dot = dir.Dot(((Vector2)(far - to)).Normalized());
                    if (dot > bestDot)
                        (best, bestDot, bestTo) = (o, dot, far);
                }
                if (best < 0 || !taken.Add(best))
                    break;
                (cur, from, to) = (best, to, bestTo);
            }
        }
        taken.Remove(l);
        return taken.ToList();
    }

    /// <summary>Delete: the selected walls, else the highlighted one. A red wall: the two rooms on its sides joined
    /// into one (UDB's deleting a two-sided line; the larger keeps its properties). A white wall: collapsed, its two
    /// ends one point at its middle (a loop must stay closed: the walls before and after it meet there).</summary>
    public override void OnDelete()
    {
        var targets = Selection.Count > 0 ? Selection.ToList() : Highlighted.IsNone ? new List<int>() : new List<int> { Highlighted.Index };
        if (targets.Count == 0)
        {
            Window.DisplayStatus("delete: no wall selected or under the cursor");
            return;
        }
        // by their ends: each change renumbers the walls
        var ends = targets.Where(l => l >= 0 && l < Doc.Lines.Count).Select(l => BuildGeom.WallEnds(Doc, Doc.Lines[l].Front)).ToList();
        int done = 0;
        string why = null;
        Window.Operation(ends.Count == 1 ? "Delete wall" : $"Delete {ends.Count} walls", () =>
        {
            foreach (var (x1, y1, x2, y2) in ends)
            {
                int line = Enumerable.Range(0, Doc.Lines.Count).FirstOrDefault(l =>
                    BuildGeom.WallEnds(Doc, Doc.Lines[l].Front) is var e && (e == (x1, y1, x2, y2) || e == (x2, y2, x1, y1)), -1);
                if (line < 0)
                    continue;
                var ln = Doc.Lines[line];
                string e;
                if (ln.Back >= 0)
                {
                    int a = Doc.Walls[ln.Front].Sector, b = Doc.Walls[ln.Back].Sector;
                    e = a == b ? "both sides are one room" : Doc.Areas[a] >= Doc.Areas[b] ? Doc.JoinSectors(a, b) : Doc.JoinSectors(b, a);
                }
                else
                    e = Doc.CollapseWall(ln.Front);
                if (e == null)
                    done++;
                else
                    why ??= e;
            }
            return done > 0 ? null : why ?? "nothing to delete";
        });
        if (done > 0 && why != null)
            Window.DisplayStatus($"deleted {done} of {ends.Count} walls; one couldn't go: {why}");
    }

    /// <summary>J: the two selected white walls, lying on each other the other way (rooms over rooms leave a choice),
    /// joined into a red pair.</summary>
    public void JoinWalls()
    {
        if (Selection.Count != 2)
        {
            Window.DisplayStatus("join walls: select the two one-sided walls lying on each other (Tab picks among walls at one place)");
            return;
        }
        var l = Selection.ToList();
        int a = Doc.Lines[l[0]].Front, b = Doc.Lines[l[1]].Front;
        Window.Operation("Join walls", () => Doc.JoinWalls(a, b));
        Highlighted = Element.None;
    }

    /// <summary>Backspace: the highlighted (or selected) red walls made white on both sides.</summary>
    public void Detach()
    {
        var lines = Selection.Count > 0 ? Selection.ToList() : Highlighted.IsNone ? new List<int>() : new List<int> { Highlighted.Index };
        var walls = lines.Where(l => Doc.Lines[l].Back >= 0).Select(l => Doc.Walls[Doc.Lines[l].Front]).ToList();
        if (walls.Count == 0)
        {
            Window.DisplayStatus("detach: no two-sided wall chosen");
            return;
        }
        Window.Operation("Detach wall", () =>
        {
            foreach (var w in walls)
                Doc.DetachWall(Doc.Walls.IndexOf(w));
            return null;
        });
    }

    /// <summary>The highlighted wall becomes its sector's first wall (slopes are relative to it): of a red wall's two
    /// sides, the one of the room the mouse is in (xmapedit's).</summary>
    public void MakeFirstWall()
    {
        if (Highlighted.IsNone)
            return;
        var line = Doc.Lines[Highlighted.Index];
        int w = line.Front;
        if (line.Back >= 0)
        {
            // a wall's room is on its right as drawn (Build's y down): the mouse on the front's left is the back's room
            var (ax, ay, bx, by) = BuildGeom.WallEnds(Doc, line.Front);
            double side = (double)(bx - ax) * (View.Cursor.Y - ay) - (double)(by - ay) * (View.Cursor.X - ax);
            if (side < 0)
                w = line.Back;
        }
        int s = Doc.Walls[w].Sector;
        if (s < 0)
            return;
        bool sloped = ((Doc.Sectors[s].FloorStat | Doc.Sectors[s].CeilingStat) & 2) != 0;
        Window.Operation($"First wall of sector {s}", () => Doc.SetFirstWall(s, w) ? null : "not the sector's wall");
        if (sloped)
            Window.DisplayStatus($"sector {s}'s first wall set: its slopes now turn about it");
    }
}

/// <summary>Sectors mode: the smallest sector under the cursor; Tab goes to the next one sharing the place (stacked
/// rooms, movers stored open).</summary>
public class SectorsMode : ClassicMode
{
    public override string Name => "Sectors";
    public override string Action => "sectorsmode";
    protected override ElementKind Kind => ElementKind.Sector;

    List<int> _here = new();
    int _pick;
    Vector2 _at;

    /// <summary>The sectors under the cursor (smallest first) and which of them is highlighted.</summary>
    public IReadOnlyList<int> Candidates => _here;

    protected override Element Pick(Vector2 m)
    {
        if (Doc == null)
            return Element.None;
        var here = Doc.EditableSectorsAt(m.X, m.Y);
        if (!here.SequenceEqual(_here))
        {
            // a new place: the smallest, unless the one picked with Tab is still among them
            int keep = _pick < _here.Count ? _here[_pick] : -1;
            _here = here;
            _pick = keep >= 0 && here.Contains(keep) && m.DistanceTo(_at) < View.Units(40) ? here.IndexOf(keep) : 0;
        }
        _at = m;
        Window.SetOverlapCount(_here.Count);
        return _here.Count > 0 ? new Element(ElementKind.Sector, _here[_pick]) : Element.None;
    }

    public override void OnCycle()
    {
        if (_here.Count < 2)
            return;
        _pick = (_pick + 1) % _here.Count;
        Highlight(new Element(ElementKind.Sector, _here[_pick]));
    }

    protected override IEnumerable<int> InRect(Rect2 r)
    {
        for (int s = 0; s < Doc.Sectors.Count; s++)
        {
            if (!Doc.SectorEditable(s))
                continue;
            var (x0, y0, x1, y1) = BuildGeom.SectorBounds(Doc, s);
            if (r.HasPoint(new Vector2(x0, y0)) && r.HasPoint(new Vector2(x1, y1)))
                yield return s;
        }
    }

    /// <summary>A sector's points (its neighbours' walls at them follow), the sectors inside its outline (rooms in it,
    /// rooms in those, a room over it lying within it: UDB's dragging moves what lies inside), the sprites in all of
    /// them and their markers.</summary>
    protected override (List<int> points, List<int> sprites, Vector2 reference) DragSet(List<int> items)
    {
        var pts = new HashSet<int>();
        var set = new HashSet<int>(items);
        foreach (int s in items)
            set.UnionWith(Inside(s));
        foreach (int s in set)
        {
            var sec = Doc.Sectors[s];
            for (int w = sec.WallPtr; w < sec.WallPtr + sec.WallNum; w++)
                pts.Add(Doc.PointOf[w]);
        }
        var sprites = new List<int>();
        for (int i = 0; i < Doc.Sprites.Count; i++)
            if (set.Contains(Doc.Sprites[i].SectNum) || (Doc.Sprites[i].StatNum == 10 && set.Contains(Doc.Sprites[i].Owner)))
                sprites.Add(i);
        var first = Doc.Walls[Doc.Sectors[Grabbed(items)].WallPtr];
        return (pts.ToList(), sprites, new Vector2(first.X, first.Y));
    }

    /// <summary>Ctrl+C: the sectors and the rooms inside them (their sprites and markers come with them).</summary>
    public override (List<int> sectors, List<int> sprites) CopySet(List<int> items)
    {
        var set = new HashSet<int>(items);
        foreach (int s in items)
            set.UnionWith(Inside(s));
        return (set.ToList(), new List<int>());
    }

    /// <summary>A double-click on a sector: the sectors inside it come with it (as Ctrl+C and dragging take them).</summary>
    protected override List<int> Within(int item) => item >= 0 && item < Doc.Sectors.Count ? Inside(item) : null;

    /// <summary>The other sectors lying within sector s's outline: every point inside it or on it, their label (a
    /// place inside them) inside it.</summary>
    List<int> Inside(int s)
    {
        var outline = BuildGeom.OuterLoop(Doc, s).Select(w => new Vector2I(Doc.Walls[w].X, Doc.Walls[w].Y)).ToList();
        var r = new List<int>();
        if (outline.Count < 3)
            return r;
        bool On(Vector2I p)
        {
            for (int k = 0; k < outline.Count; k++)
            {
                var a = outline[k];
                var b = outline[(k + 1) % outline.Count];
                if (Chosen.Forge.Document.MapDocument.SegmentDistance(p.X, p.Y, a.X, a.Y, b.X, b.Y) < 0.5)
                    return true;
            }
            return false;
        }
        for (int t = 0; t < Doc.Sectors.Count; t++)
        {
            var sec = Doc.Sectors[t];
            // (the current layer's: another layer's room over this one stays where it is)
            if (t == s || sec.WallNum < 3 || t >= Doc.Labels.Length || !Doc.SectorEditable(t))
                continue;
            var label = Doc.Labels[t];
            if (!Chosen.Forge.Document.MapDocument.InPolygon(outline, label.X, label.Y))
                continue;
            bool all = true;
            for (int w = sec.WallPtr; w < sec.WallPtr + sec.WallNum && all; w++)
            {
                var p = new Vector2I(Doc.Walls[w].X, Doc.Walls[w].Y);
                all = On(p) || Chosen.Forge.Document.MapDocument.InPolygon(outline, p.X, p.Y);
            }
            if (all)
                r.Add(t);
        }
        return r;
    }

    /// <summary>Insert: drawing lines from the cursor (UDB's).</summary>
    public override void OnInsert() => StartDrawing(View.Cursor);

    public override void OnDelete()
    {
        var t = Selection.Count > 0 ? Selection.OrderByDescending(i => i).ToList()
            : Highlighted.IsNone ? new List<int>() : new List<int> { Highlighted.Index };
        if (t.Count == 0)
            return;
        Window.Operation(t.Count == 1 ? $"Delete sector {t[0]}" : $"Delete {t.Count} sectors", () =>
        {
            foreach (int s in t)
                Doc.DeleteSector(s);
            return null;
        });
        Highlighted = Element.None;
        _here.Clear();
    }

    /// <summary>J: two selected sectors sharing walls joined into one: the highlighted one of them keeps its
    /// properties, else the lower-numbered.</summary>
    public void Join()
    {
        if (Selection.Count != 2)
        {
            Window.DisplayStatus("join: select the two sectors to join (the one under the cursor keeps its properties)");
            return;
        }
        var s = Selection.OrderBy(i => i).ToList();
        if (!Highlighted.IsNone && Highlighted.Index == s[1])
            s.Reverse();
        Window.Operation($"Join sectors {s[0]} and {s[1]}", () => Doc.JoinSectors(s[0], s[1]));
        Highlighted = Element.None;
        _here.Clear();
    }

    /// <summary>The sector s dissolves into: of the editable sectors its red walls lead to, the one it shares the most
    /// wall length with (a room's: the sector round it); -1: none.</summary>
    public static int DissolveInto(Document.MapDocument d, int s)
    {
        var shared = new Dictionary<int, double>();
        for (int w = d.Sectors[s].WallPtr; w < d.Sectors[s].WallPtr + d.Sectors[s].WallNum; w++)
            if (d.Walls[w].NextSector is var n and >= 0 && n != s && d.SectorEditable(n))
            {
                var (ax, ay, bx, by) = BuildGeom.WallEnds(d, w);
                shared[n] = shared.GetValueOrDefault(n) + System.Math.Sqrt((double)(bx - ax) * (bx - ax) + (double)(by - ay) * (by - ay));
            }
        return shared.Count == 0 ? -1 : shared.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key).First().Key;
    }

    /// <summary>Dissolve (Backspace, the context menu): the selected sectors (else the one under the mouse) joined
    /// each into its neighbour (DissolveInto), whose properties the space takes: a room deleted this way leaves no
    /// hole, the floor round it goes on. One undo step.</summary>
    /// <summary>The selected sectors (else the highlighted one) separated from the sectors round them
    /// (GeometryOps.SeparateSectors): their red walls to those made white, so they move on their own. One undo step.</summary>
    public void Separate()
    {
        var items = Selection.Count > 0 ? Selection.ToList() : Highlighted.IsNone ? new List<int>() : new List<int> { Highlighted.Index };
        items = items.Where(i => i >= 0 && i < Doc.Sectors.Count).ToList();
        if (items.Count == 0)
        {
            Window.DisplayStatus("separate: select the sectors to separate from the sectors round them");
            return;
        }
        int cut = 0;
        bool done = Window.Operation(items.Count == 1 ? $"Separate sector {items[0]}" : $"Separate {items.Count} sectors", () =>
        {
            cut = Doc.SeparateSectors(items);
            Doc.NotifyChanged();
            return cut == 0 ? "separate: no two-sided walls to other sectors (it moves on its own already)" : null;
        });
        if (done)
            Window.DisplayStatus($"separated: {cut} two-sided wall(s) made one-sided; {(items.Count == 1 ? $"sector {items[0]}" : $"the {items.Count} sectors")} move on their own now (moving it against a sector joins it again)");
    }

    /// <summary>Whether a sector has a red wall on its outer loop to a sector outside the set (something to separate).</summary>
    public static bool Joined(Document.MapDocument d, int s, ICollection<int> set) =>
        Chosen.Shared.Engine.Geometry.BuildGeom.OuterLoop(d, s) is { } outer && outer.Any(w => d.Walls[w].NextWall >= 0 && !set.Contains(d.Walls[w].NextSector));

    public void Dissolve()
    {
        var items = Selection.Count > 0 ? Selection.ToList() : Highlighted.IsNone ? new List<int>() : new List<int> { Highlighted.Index };
        items = items.Where(i => i >= 0 && i < Doc.Sectors.Count).OrderByDescending(i => i).ToList();
        if (items.Count == 0)
        {
            Window.DisplayStatus("dissolve: select the sectors to dissolve into the sector next to them (a room: the one round it)");
            return;
        }
        Window.Operation(items.Count == 1 ? $"Dissolve sector {items[0]}" : $"Dissolve {items.Count} sectors", () =>
        {
            // (highest first: a join takes the dissolved sector's number away, the lower ones keep theirs)
            foreach (int s in items)
            {
                int into = DissolveInto(Doc, s);
                if (into < 0)
                    return $"sector {s} has no sector next to it to dissolve into (Delete takes it away)";
                if (Doc.JoinSectors(into, s) is { } why)
                    return $"sector {s}: {why}";
                Doc.NotifyChanged();
            }
            return null;
        });
        Selection.Clear();
        Highlighted = Element.None;
        _here.Clear();
        Window.SelectionChanged();
    }
}

/// <summary>Sprites mode (UDB's things). A right click on empty space places a sprite of the docker's type there; held
/// and moved, the sprite follows until the button is released, then its properties open (UDB's thing insert).</summary>
public class SpritesMode : ClassicMode
{
    public override string Name => "Sprites";
    public override string Action => "thingsmode";
    protected override ElementKind Kind => ElementKind.Sprite;
    protected override bool DrawsOnEdit => false;

    /// <summary>The sprite being placed (the edit button still held), or -1.</summary>
    int _placing = -1;

    Vector2I Snapped(Vector2 map)
    {
        var at = View.Snap && !ForgeInput.Shift ? View.SnapToGrid(map) : map.Round();
        return new Vector2I((int)at.X, (int)at.Y);
    }

    public override void OnEditBegin(Vector2 map)
    {
        if (!Highlighted.IsNone || Doc == null)
            return;
        var at = Snapped(map);
        Window.Undo.CreateUndo("Insert sprite");
        _placing = Window.NewSprite(at.X, at.Y);
        Highlight(new Element(ElementKind.Sprite, _placing));
    }

    protected override bool CanDrag() => _placing < 0 && base.CanDrag();

    public override void OnMouseMove(Vector2 map)
    {
        if (_placing < 0)
        {
            base.OnMouseMove(map);
            return;
        }
        var at = Snapped(map);
        var s = Doc.Sprites[_placing];
        if (s.X == at.X && s.Y == at.Y)
            return;
        Doc.MoveSprite(_placing, at.X, at.Y);
        Doc.SpriteToFloor(_placing);
        Doc.NotifyChanged();
    }

    public override void OnEditEnd(Vector2 map)
    {
        if (_placing < 0)
        {
            base.OnEditEnd(map);
            return;
        }
        int i = _placing;
        _placing = -1;
        Landed(i);
    }

    public override void OnDisengage()
    {
        _placing = -1;
        base.OnDisengage();
    }

    /// <summary>A placed sprite: selected, its properties opened (Sprites browser: "Edit new").</summary>
    void Landed(int i)
    {
        Selection.Clear();
        Selection.Add(i);
        Window.SelectionChanged();
        Window.UndoChanged();
        Window.DisplayStatus($"placed sprite {i}");
        if (Window.EditNewSprite)
            Window.EditProperties(ElementKind.Sprite, new List<int> { i }, true);
    }

    protected override Element Pick(Vector2 m)
    {
        if (Doc == null)
            return Element.None;
        // sprites at one place (a marker on another, stacked pickups): Tab goes through them
        var near = new List<(int, double)>();
        for (int i = 0; i < Doc.Sprites.Count; i++)
        {
            if ((View.SpriteHidden != null && View.SpriteHidden(i)) || !Doc.SpriteEditable(i))
                continue;
            float d = m.DistanceTo(new Vector2(Doc.Sprites[i].X, Doc.Sprites[i].Y));
            if (d <= System.Math.Max(View.SpriteRadius(i), View.Units(HighlightRange)))
                near.Add((i, d));
        }
        return PickAmong(ElementKind.Sprite, near);
    }

    protected override IEnumerable<int> InRect(Rect2 r)
    {
        for (int i = 0; i < Doc.Sprites.Count; i++)
            if ((View.SpriteHidden == null || !View.SpriteHidden(i)) && Doc.SpriteEditable(i) && r.HasPoint(new Vector2(Doc.Sprites[i].X, Doc.Sprites[i].Y)))
                yield return i;
    }

    /// <summary>Ctrl+C: the sprites.</summary>
    public override (List<int> sectors, List<int> sprites) CopySet(List<int> items) => (new List<int>(), new List<int>(items));

    protected override (List<int> points, List<int> sprites, Vector2 reference) DragSet(List<int> items)
    {
        var s = Doc.Sprites[Grabbed(items)];
        return (new List<int>(), items, new Vector2(s.X, s.Y));
    }

    /// <summary>The selected sprites, else the highlighted one.</summary>
    List<int> Targets() => Selection.Count > 0 ? Selection.OrderBy(i => i).ToList()
        : Highlighted.IsNone ? new List<int>() : new List<int> { Highlighted.Index };

    /// <summary>Insert: a new sprite of the docker's type at the cursor (snapped), in the smallest sector there.</summary>
    public override void OnInsert()
    {
        var at = Snapped(View.Cursor);
        Window.Undo.CreateUndo("Insert sprite");
        int i = Window.NewSprite(at.X, at.Y);
        Highlight(new Element(ElementKind.Sprite, i));
        Landed(i);
    }

    public override void OnDelete()
    {
        var t = Targets();
        if (t.Count == 0)
            return;
        Window.Undo.CreateUndo(t.Count == 1 ? "Delete sprite" : $"Delete {t.Count} sprites");
        Doc.DeleteSprites(t);
        Selection.Clear();
        Highlighted = Element.None;
        Doc.NotifyChanged();
        Window.UndoChanged();
        Window.DisplayStatus($"deleted {t.Count} sprite(s)");
    }

    /// <summary>Turns the sprites by steps of 45 degrees (Build: 256 a step).</summary>
    public void Rotate(int steps)
    {
        var t = Targets();
        if (t.Count == 0)
            return;
        Window.Undo.CreateUndo("Rotate sprites");
        foreach (int i in t)
            Doc.Sprites[i].Ang = (Doc.Sprites[i].Ang + steps * 256) & 2047;
        Doc.NotifyChanged();
        Window.UndoChanged();
    }

    /// <summary>Shift+L: the sprites face the cursor.</summary>
    public void LookAtCursor()
    {
        var t = Targets();
        if (t.Count == 0)
            return;
        Window.Undo.CreateUndo("Point sprites at the cursor");
        foreach (int i in t)
        {
            var s = Doc.Sprites[i];
            double a = System.Math.Atan2(View.Cursor.Y - s.Y, View.Cursor.X - s.X);
            s.Ang = (int)System.Math.Round(a * 1024 / System.Math.PI) & 2047;
        }
        Doc.NotifyChanged();
        Window.UndoChanged();
    }
}
