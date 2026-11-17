// Chosen core: original code (UDB's draw modes' handling, rewritten for Build's geometry).
using System;
using System.Collections.Generic;
using System.Linq;
using Chosen.Forge.Config;
using Chosen.Forge.Document;
using Chosen.Forge.View;
using Chosen.Shared.Engine.Geometry;
using Godot;

namespace Chosen.Forge.Editing;

/// <summary>
/// Draw lines (UDB's; Ctrl+D, or a right click on empty space in the classic modes): a click adds a point (snapped to a point or a wall near the cursor, else the grid), Backspace
/// takes the last back, a click on the first point closes the shape, Enter or a right click finishes an open path, Esc
/// cancels. What was drawn decides the operation: a closed shape outside every sector makes a sector; inside one
/// (touching none of its walls), a room in it; a path from one point of a sector's outer walls to another, through
/// it, splits it; a single point on a wall inserts a point there. Tab picks among sectors sharing the place.
/// </summary>
public class DrawGeometryMode : EditMode
{
    public override string Name => "Draw Lines";
    public override string Action => "drawlinesmode";

    protected readonly List<Vector2I> Points = new();
    protected Vector2I Preview;
    int _pick;
    EditMode _previous;

    public override void OnEngage()
    {
        _previous = Window.Editing.LastClassic ?? Window.Editing.Modes[0];
        Points.Clear();
        _pick = 0;
        Window.ShowInfo(Element.None);
        Window.DisplayStatus(Help);
    }

    /// <summary>A box drawn by two corners (Rectangle, Circle, Grid): Shift draws it from the first click as its
    /// middle, Alt makes it square, Ctrl doesn't snap the corner to the grid.</summary>
    protected virtual bool BoxDrawing => false;

    protected const string BoxKeys = "Shift: from the middle, Alt: square, Ctrl: off the grid";

    protected virtual string Help => Name + ": click to add points, on the first point to close; right-click or Enter finishes a line"
        + " (across a sector: splits it); Backspace takes a point back; Esc cancels";

    protected void Done() => Window.Editing.Switch(_previous);

    /// <summary>The first point at map (a right click on empty space, Insert in Walls or Sectors mode).</summary>
    public void Begin(Vector2 map)
    {
        Preview = SnapAt(map);
        Points.Add(Preview);
        View.QueueRedraw();
    }

    /// <summary>Where a click at map point m puts a point: on a point near it, on a wall near it, or on the grid.</summary>
    protected Vector2I SnapAt(Vector2 m)
    {
        float r = View.Units(ClassicMode.HighlightRange);
        if (Points.Count >= 3 && m.DistanceTo(Points[0]) <= r)
            return Points[0];
        // (snapped to what shows: a hidden layer's points and walls aren't there)
        int p = Doc.PointNear(m.X, m.Y, r, Doc.PointShown);
        if (p >= 0)
            return new Vector2I(Doc.Points[p].X, Doc.Points[p].Y);
        int l = Doc.LineNear(m.X, m.Y, r, Doc.LineShown);
        var grid = View.Snap && !(BoxDrawing ? ForgeInput.Ctrl : ForgeInput.Shift) ? View.SnapToGrid(m) : m.Round();
        if (l >= 0)
        {
            // on the wall: the grid point on it if the grid line crosses near, else the nearest point of it
            int w = Doc.Lines[l].Front;
            var onWall = VerticesMode.OnWall(Doc, w, m);
            var (ax, ay, bx, by) = BuildGeom.WallEnds(Doc, w);
            if (MapDocument.SegmentDistance(grid.X, grid.Y, ax, ay, bx, by) < 0.5)
                return new Vector2I((int)grid.X, (int)grid.Y);
            return onWall;
        }
        return new Vector2I((int)grid.X, (int)grid.Y);
    }

    public override void OnMouseMove(Vector2 map)
    {
        Preview = SnapAt(map);
        View.QueueRedraw();
    }

    public virtual void HandleInput(InputEvent e)
    {
        if (e is InputEventMouseMotion mm)
            OnMouseMove(View.ScreenToMap(mm.Position));
        else if (e is InputEventMouseButton { Pressed: true } mb)
        {
            var p = SnapAt(View.ScreenToMap(mb.Position));
            if (mb.ButtonIndex == MouseButton.Left)
            {
                if (Points.Count >= 3 && p == Points[0])
                {
                    // Alt on the closing click: a sector over what lies there (a room over a room)
                    Finish(true, mb.AltPressed);
                    return;
                }
                if (Points.Count == 0 || Points[^1] != p)
                    Points.Add(p);
                View.QueueRedraw();
            }
            else if (mb.ButtonIndex == MouseButton.Right)
                Finish(false);
        }
    }

    public override bool OnKey(InputEventKey e)
    {
        // (a modifier only redraws: the keys bound with it still go on)
        if (BoxModifier(e))
            return false;
        switch (e.Keycode)
        {
            case Key.Escape:
                Done();
                return true;
            case Key.Backspace:
                if (Points.Count > 0)
                    Points.RemoveAt(Points.Count - 1);
                View.QueueRedraw();
                return true;
            case Key.Enter or Key.KpEnter:
                Finish(false);
                return true;
        }
        return false;
    }

    public override void OnCycle()
    {
        _pick++;
        Window.DisplayStatus(Target(Points) is int s and >= 0 ? $"drawing in sector {s} (Tab: the next sharing the place)" : "drawing outside the sectors");
    }

    /// <summary>The sector the drawing is in: of those holding its first point's neighbourhood, the Tab-picked one.</summary>
    int Target(List<Vector2I> pts)
    {
        if (pts.Count == 0)
            return -1;
        Vector2 probe = pts.Count > 1 ? ((Vector2)pts[0] + pts[1]) * 0.5f : pts[0];
        var c = Doc.EditableSectorsAt(probe.X, probe.Y);
        return c.Count == 0 ? -1 : c[_pick % c.Count];
    }

    protected void Finish(bool closed, bool over = false)
    {
        var pts = new List<Vector2I>(Points);
        Points.Clear();
        // back to the mode drawn from first: what the drawing did (or why not) is then the status bar's last word
        Done();
        if (pts.Count == 0)
            return;
        if (closed && over)
            Window.Operation("Draw a sector over the others", () => Doc.MakeSector(pts, out string why, over: true) >= 0 ? null : why);
        else if (closed)
            Closed(pts);
        else if (pts.Count == 1)
            SinglePoint(pts[0]);
        else
            Path(pts);
    }

    void SinglePoint(Vector2I p)
    {
        int l = Doc.LineNear(p.X, p.Y, 0.75, Doc.LineEditable);
        if (l < 0)
        {
            Window.DisplayStatus("draw: a single point inserts a point only on a wall");
            return;
        }
        int w = Doc.Lines[l].Front;
        Window.Operation("Insert point", () => Doc.InsertPoint(w, p.X, p.Y) ? null : "that is a point already");
    }

    /// <summary>Is p on sector s's outer loop: at one of its points, or on one of its walls (then the wall)?</summary>
    (bool on, int wall) OnOuter(int s, Vector2I p) => Doc.OnOuterLoop(s, p);

    /// <summary>The places telling where a shape lies: its corners and the middles of its sides.</summary>
    static IEnumerable<Vector2> Samples(List<Vector2I> pts) =>
        pts.Select(p => (Vector2)p).Concat(pts.Select((p, i) => ((Vector2)p + pts[(i + 1) % pts.Count]) * 0.5f));

    /// <summary>Is p inside a sector, not on one of its walls?</summary>
    bool StrictlyInside(Vector2 p) => Doc.EditableSectorsAt(p.X, p.Y).Count > 0 && Doc.LineNear(p.X, p.Y, 0.5, Doc.LineEditable) < 0;

    protected void Closed(List<Vector2I> pts)
    {
        var (what, op) = ClosedPlan(pts);
        Window.Operation(what, op);
    }

    /// <summary>What a closed shape drawn makes (as the map is now) and the work making it (null: done, else why
    /// not): Draw Grid makes its cells one after the other in one undo step.</summary>
    protected (string what, Func<string> op) ClosedPlan(List<Vector2I> pts)
    {
        // outside the sectors (it may lie against them: sharing their walls and points): a new sector joined to them
        // (a sector poking into it through a side, its corners and the middles of the sides outside it: drawn over)
        if (!Samples(pts).Any(StrictlyInside) && Doc.CrossesWalls(pts, true, -1, null, false, Doc.Enclosed(pts)) == null)
            return ("Draw sector", () => Doc.MakeSectorJoined(pts, out string why) >= 0 ? null : why ?? "a sector needs 3 points");
        int s = Target(pts);
        // a room: inside the sector, touching its walls at their points at most (not along them), crossing none
        if (Doc.FitsAsRoom(s, pts))
            return ($"Draw a room in sector {s}", () =>
                Doc.CrossesWalls(pts, true, s, Doc.TouchingCorners(s, pts)) ?? (Doc.InsertInnerLoop(s, pts) >= 0 ? null : "a room needs 3 points inside the sector"));
        // over sectors (partly, or inside one along its walls): the sectors it runs through cut along it, the part
        // outside them a new sector (UDB's drawing)
        return ("Draw sector", () => Doc.MakeSectorAcross(pts, out string why) != null ? null : why ?? "the shape makes no sector");
    }

    /// <summary>The box from corner a to corner b along the grid's axes (View's grid frame: an untransformed grid an
    /// ordinary box): its four corners from a, rounded; null when it has no width or height there.</summary>
    protected List<Vector2I> Box(Vector2I a, Vector2I b) => GridShape(a, b, (ga, gb) => new[] { ga, new Vector2(gb.X, ga.Y), gb, new Vector2(ga.X, gb.Y) });

    /// <summary>A shape made in the grid's frame from the box a..b (given there), back in the map, rounded; null for a
    /// box without width or height.</summary>
    protected List<Vector2I> GridShape(Vector2I a, Vector2I b, Func<Vector2, Vector2, IEnumerable<Vector2>> shape)
    {
        var (ga, gb) = Frame(a, b);
        if (Math.Abs(ga.X - gb.X) < 1 || Math.Abs(ga.Y - gb.Y) < 1)
            return null;
        var l = new List<Vector2I>();
        foreach (var g in shape(ga, gb))
        {
            var m = View.FromGrid(g).Round();
            var p = new Vector2I((int)m.X, (int)m.Y);
            if (l.Count == 0 || l[^1] != p)
                l.Add(p);
        }
        if (l.Count > 1 && l[0] == l[^1])
            l.RemoveAt(l.Count - 1);
        return l;
    }

    /// <summary>The box's corners in the grid's frame from the first click a and the mouse b, as the held keys say:
    /// Alt: square (its longer side both ways), Shift: a its middle (b a corner).</summary>
    protected (Vector2 ga, Vector2 gb) Frame(Vector2I a, Vector2I b)
    {
        var ga = View.ToGrid(a);
        var d = View.ToGrid(b) - ga;
        if (ForgeInput.Alt)
        {
            float s = Math.Max(Math.Abs(d.X), Math.Abs(d.Y));
            d = new Vector2(d.X < 0 ? -s : s, d.Y < 0 ? -s : s);
        }
        return ForgeInput.Shift ? (ga - d, ga + d) : (ga, ga + d);
    }

    /// <summary>The box modes: Shift, Alt or Ctrl pressed or let go redraws the box where it now goes.</summary>
    protected bool BoxModifier(InputEventKey e)
    {
        if (!BoxDrawing || e.Keycode is not (Key.Shift or Key.Alt or Key.Ctrl))
            return false;
        OnMouseMove(View.Cursor);
        return true;
    }

    /// <summary>The mode drawn from (where a finished drawing goes back to).</summary>
    protected void Back() => Done();

    void Path(List<Vector2I> pts)
    {
        // outside the sectors, from one sector's outer wall back to it: an extension (UDB's drawing onto a room)
        var open = pts.Select(p => (Vector2)p).Concat(pts.Zip(pts.Skip(1), (p, q) => ((Vector2)p + q) * 0.5f));
        if (!open.Any(StrictlyInside))
        {
            Extension(pts);
            return;
        }
        // the sector the path runs through: the one holding its first segment's middle (Tab: another)
        int s = Target(pts);
        if (s < 0)
        {
            Window.DisplayStatus("draw: a line splits a sector when it runs through one from its outer walls to its outer walls");
            return;
        }
        var a = OnOuter(s, pts[0]);
        var b = OnOuter(s, pts[^1]);
        if (!a.on || !b.on)
        {
            Window.DisplayStatus($"draw: to split sector {s} the line must start and end on its outer walls");
            return;
        }
        var start = pts[0];
        var end = pts[^1];
        Window.Operation($"Split sector {s}", () =>
        {
            // the ends on walls become points first
            if (a.wall >= 0 && !Doc.InsertPoint(a.wall, start.X, start.Y))
                return "the start can't become a point";
            if (b.wall >= 0)
            {
                int w = WallAt(s, end);
                if (w < 0 || !Doc.InsertPoint(w, end.X, end.Y))
                    return "the end can't become a point";
            }
            var (_, error) = Doc.SplitSector(s, pts);
            return error;
        });
    }

    /// <summary>
    /// A line drawn outside the sectors from a point (or wall) of a sector's outer loop to another: with the stretch of
    /// the sector's walls between its ends (the one on the line's side) it closes a new sector, joined to the room by
    /// red walls there. Ends on walls become points first.
    /// </summary>
    void Extension(List<Vector2I> pts)
    {
        var a = pts[0];
        var b = pts[^1];
        int s = Enumerable.Range(0, Doc.Sectors.Count).FirstOrDefault(i => Doc.SectorEditable(i) && OnOuter(i, a).on && OnOuter(i, b).on, -1);
        if (s < 0 || a == b)
        {
            Window.DisplayStatus("draw: a line outside the sectors makes a sector when it starts and ends on the walls of one room "
                + "(or close the shape on its first point)");
            return;
        }
        Window.Operation($"Extend sector {s}", () =>
        {
            foreach (var p in new[] { a, b })
                if (WallAt(s, p) is var w and >= 0 && !Doc.InsertPoint(w, p.X, p.Y))
                    return "an end can't become a point";
            var loop = BuildGeom.OuterLoop(Doc, s);
            var at = loop.Select(w => new Vector2I(Doc.Walls[w].X, Doc.Walls[w].Y)).ToList();
            int ia = at.IndexOf(a), ib = at.IndexOf(b), n = at.Count;
            if (ia < 0 || ib < 0)
                return "the ends aren't on the room's walls";
            List<Vector2I> best = null;
            foreach (var (from, to) in new[] { (ia, ib), (ib, ia) })
            {
                // the room's points strictly between from and to, going round its loop
                var chain = new List<Vector2I>();
                for (int k = (from + 1) % n; k != to; k = (k + 1) % n)
                    chain.Add(at[k]);
                // the new sector: the line a..b, then back to a along this stretch
                var poly = new List<Vector2I>(pts);
                if (from == ia)
                    chain.Reverse();
                poly.AddRange(chain);
                // it must lie beside the room, not over it: the room's side of the stretch's first wall is outside it
                var (x1, y1, x2, y2) = BuildGeom.WallEnds(Doc, loop[from]);
                var mid = new Vector2((x1 + x2) * 0.5f, (y1 + y2) * 0.5f);
                var normal = new Vector2(-(y2 - y1), x2 - x1).Normalized() * 0.5f;
                var roomSide = BuildGeom.Inside(Doc, s, mid.X + normal.X, mid.Y + normal.Y) ? mid + normal : mid - normal;
                if (MapDocument.InPolygon(poly, roomSide.X, roomSide.Y))
                    continue;
                if (best == null || Mathf.Abs(Area(poly)) < Mathf.Abs(Area(best)))
                    best = poly;
            }
            if (best == null)
                return "the line would go round the whole room";
            return Doc.MakeSectorJoined(best, out string why) >= 0 ? null : why ?? "a sector needs 3 points";
        });
    }

    static float Area(List<Vector2I> p)
    {
        double a = 0;
        for (int i = 0; i < p.Count; i++)
            a += (double)p[i].X * p[(i + 1) % p.Count].Y - (double)p[(i + 1) % p.Count].X * p[i].Y;
        return (float)(a / 2);
    }

    /// <summary>The wall of sector s's outer loop that p lies on (after an insert renumbered them).</summary>
    int WallAt(int s, Vector2I p)
    {
        foreach (int w in BuildGeom.OuterLoop(Doc, s))
        {
            var (ax, ay, bx, by) = BuildGeom.WallEnds(Doc, w);
            if (MapDocument.SegmentDistance(p.X, p.Y, ax, ay, bx, by) < 0.75 && !(ax == p.X && ay == p.Y) && !(bx == p.X && by == p.Y))
                return w;
        }
        return -1;
    }

    public override void OnRedraw(MapView2D v)
    {
        var c = ColorSettings.Highlight;
        var font = ThemeDB.FallbackFont;
        for (int i = 0; i + 1 < Points.Count; i++)
            Segment(v, Points[i], Points[i + 1], c, font);
        if (Points.Count > 0)
            Segment(v, Points[^1], Preview, c.Darkened(0.3f), font);
        foreach (var p in Points)
            v.DrawRect(new Rect2(v.MapToScreen(p) - new Vector2(3, 3), new Vector2(6, 6)), c);
        v.DrawRect(new Rect2(v.MapToScreen(Preview) - new Vector2(3, 3), new Vector2(6, 6)), ColorSettings.Indication, false, 1);
    }

    static void Segment(MapView2D v, Vector2I a, Vector2I b, Color c, Font font)
    {
        var sa = v.MapToScreen(a);
        var sb = v.MapToScreen(b);
        v.DrawLine(sa, sb, c, 2);
        float len = ((Vector2)(b - a)).Length();
        if (len > 0 && (sb - sa).Length() > 40)
            v.DrawString(font, (sa + sb) * 0.5f + new Vector2(4, -4), Units.Length(len), HorizontalAlignment.Left, -1, 11, c);
    }
}

/// <summary>Draw rectangle (UDB's): two clicks give opposite corners; the rectangle is then a closed shape as in draw
/// lines.</summary>
public class DrawRectangleMode : DrawGeometryMode
{
    public override string Name => "Draw Rectangle";
    public override string Action => "drawrectanglemode";
    protected override bool BoxDrawing => true;
    protected override string Help => Name + $": click two opposite corners (outside the sectors: a new sector; inside one: a room in it); {BoxKeys}; Esc cancels";

    public override void HandleInput(InputEvent e)
    {
        if (e is InputEventMouseMotion mm)
            OnMouseMove(View.ScreenToMap(mm.Position));
        else if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } mb)
        {
            var p = SnapAt(View.ScreenToMap(mb.Position));
            if (Points.Count == 0)
            {
                Points.Add(p);
                return;
            }
            var a = Points[0];
            Points.Clear();
            if (Box(a, p) is not { Count: 4 } box)
            {
                Window.DisplayStatus("draw rectangle: the corners must differ along both of the grid's axes");
                Done();
                return;
            }
            Points.AddRange(box);
            Finish(true);
        }
        else if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right })
            Done();
    }

    public override void OnRedraw(MapView2D v)
    {
        if (Points.Count == 1)
        {
            if (Box(Points[0], Preview) is { } box)
                for (int i = 0; i < box.Count; i++)
                    v.DrawLine(v.MapToScreen(box[i]), v.MapToScreen(box[(i + 1) % box.Count]), ColorSettings.Highlight, 1);
        }
        else
            v.DrawRect(new Rect2(v.MapToScreen(Preview) - new Vector2(3, 3), new Vector2(6, 6)), ColorSettings.Indication, false, 1);
    }
}

/// <summary>
/// Draw Circle: two clicks give a box (its corners on the grid, on points or walls as drawing snaps), the circle (an
/// ellipse in a box that isn't square) a sector of that many sides inside it, its points where they fall (not on the
/// grid); Left / Right or - / + take a side off or add one (3..64; kept while Forge runs). Drawn as any closed shape:
/// a new sector, a room in one, or cut across.
/// </summary>
public class DrawCircleMode : DrawGeometryMode
{
    public override string Name => "Draw Circle";
    public override string Action => "drawcirclemode";
    static int _sides = 16;
    /// <summary>The sides drawn (a session's state: replays start with it).</summary>
    public static int Sides { get => _sides; set => _sides = Math.Clamp(value, 3, 64); }
    protected override bool BoxDrawing => true;
    protected override string Help => $"{Name} ({_sides} sides): click two opposite corners of its box; Left / Right (or - / +): fewer or more sides; {BoxKeys}; Esc cancels";

    /// <summary>The ellipse in the box from a to b with n sides: its points rounded, from its top, the way the view
    /// turns clockwise (repeated ones dropped).</summary>
    public static IEnumerable<Vector2> Ellipse(Vector2 a, Vector2 b, int n)
    {
        double cx = (a.X + b.X) / 2.0, cy = (a.Y + b.Y) / 2.0, rx = Math.Abs(b.X - a.X) / 2.0, ry = Math.Abs(b.Y - a.Y) / 2.0;
        for (int i = 0; i < n; i++)
        {
            double t = -Math.PI / 2 + 2 * Math.PI * i / n;
            yield return new Vector2((float)(cx + rx * Math.Cos(t)), (float)(cy + ry * Math.Sin(t)));
        }
    }

    /// <summary>The circle in the box a..b along the grid's axes, in the map (null: no box).</summary>
    List<Vector2I> Circle(Vector2I a, Vector2I b) => GridShape(a, b, (ga, gb) => Ellipse(ga, gb, _sides));

    public override void HandleInput(InputEvent e)
    {
        if (e is InputEventMouseMotion mm)
            OnMouseMove(View.ScreenToMap(mm.Position));
        else if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } mb)
        {
            var p = SnapAt(View.ScreenToMap(mb.Position));
            if (Points.Count == 0)
            {
                Points.Add(p);
                return;
            }
            var a = Points[0];
            Points.Clear();
            if (Circle(a, p) is not { Count: >= 3 } circle)
            {
                Window.DisplayStatus("draw circle: the box's corners must differ along both of the grid's axes");
                Back();
                return;
            }
            Points.AddRange(circle);
            Finish(true);
        }
        else if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right })
            Back();
    }

    public override bool OnKey(InputEventKey e)
    {
        int by = e.Keycode is Key.Left or Key.Minus or Key.KpSubtract ? -1 : e.Keycode is Key.Right or Key.Equal or Key.Plus or Key.KpAdd ? 1 : 0;
        if (by != 0)
        {
            _sides = Math.Clamp(_sides + by, 3, 64);
            Window.DisplayStatus(Help);
            View.QueueRedraw();
            return true;
        }
        return base.OnKey(e);
    }

    public override void OnRedraw(MapView2D v)
    {
        if (Points.Count == 1)
        {
            var a = Points[0];
            if (Box(a, Preview) is { } box)
                for (int i = 0; i < box.Count; i++)
                    v.DrawLine(v.MapToScreen(box[i]), v.MapToScreen(box[(i + 1) % box.Count]), ColorSettings.Highlight.Darkened(0.4f), 1);
            if (Circle(a, Preview) is { } pts)
                for (int i = 0; i < pts.Count; i++)
                    v.DrawLine(v.MapToScreen(pts[i]), v.MapToScreen(pts[(i + 1) % pts.Count]), ColorSettings.Highlight, 1);
        }
        else
            v.DrawRect(new Rect2(v.MapToScreen(Preview) - new Vector2(3, 3), new Vector2(6, 6)), ColorSettings.Indication, false, 1);
    }
}

/// <summary>
/// Draw Grid: two clicks give a box (its corners snapped as drawing snaps), cut into columns and rows of sectors
/// sharing their walls (A / D: columns, W / S: rows, 1..32; kept while Forge runs); each cell drawn as a
/// rectangle is (a new sector, a room in one, or cut across), all in one undo step.
/// </summary>
public class DrawGridMode : DrawGeometryMode
{
    public override string Name => "Draw Grid";
    public override string Action => "drawgridmode";
    static int _cols = 4, _rows = 4;
    /// <summary>The columns and rows drawn (a session's state: replays start with them).</summary>
    public static (int cols, int rows) Size { get => (_cols, _rows); set => (_cols, _rows) = (Math.Clamp(value.cols, 1, 32), Math.Clamp(value.rows, 1, 32)); }
    protected override bool BoxDrawing => true;
    protected override string Help => $"{Name} ({_cols} x {_rows}): click two opposite corners; A / D: columns, W / S: rows; {BoxKeys}; Esc cancels";

    /// <summary>The cells of the box from a to b (along the grid's axes) in cols x rows, each a rectangle (row by row;
    /// their shared corners the same points).</summary>
    List<List<Vector2I>> Cells(Vector2I a, Vector2I b, int cols, int rows)
    {
        var (ga, gb) = Frame(a, b);
        Vector2I At(int i, int j)
        {
            var m = View.FromGrid(new Vector2(ga.X + (gb.X - ga.X) * i / cols, ga.Y + (gb.Y - ga.Y) * j / rows)).Round();
            return new Vector2I((int)m.X, (int)m.Y);
        }
        var cells = new List<List<Vector2I>>();
        for (int j = 0; j < rows; j++)
            for (int i = 0; i < cols; i++)
                cells.Add(new List<Vector2I> { At(i, j), At(i + 1, j), At(i + 1, j + 1), At(i, j + 1) });
        return cells;
    }

    public override void HandleInput(InputEvent e)
    {
        if (e is InputEventMouseMotion mm)
            OnMouseMove(View.ScreenToMap(mm.Position));
        else if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } mb)
        {
            var p = SnapAt(View.ScreenToMap(mb.Position));
            if (Points.Count == 0)
            {
                Points.Add(p);
                return;
            }
            var a = Points[0];
            Points.Clear();
            Back();
            var (ga, gp) = Frame(a, p);
            if (Math.Abs(ga.X - gp.X) < _cols || Math.Abs(ga.Y - gp.Y) < _rows)
            {
                Window.DisplayStatus("draw grid: the box is too small for its cells");
                return;
            }
            var cells = Cells(a, p, _cols, _rows);
            int cols = _cols, rows = _rows;
            Window.Operation($"Draw a grid of {cols} x {rows}", () =>
            {
                for (int k = 0; k < cells.Count; k++)
                {
                    var (_, op) = ClosedPlan(cells[k]);
                    if (op() is { } why)
                        return $"cell {k % cols + 1}, {k / cols + 1}: {why}";
                    Doc.NotifyChanged();
                }
                return null;
            });
        }
        else if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right })
            Back();
    }

    public override bool OnKey(InputEventKey e)
    {
        switch (e.Keycode)
        {
            case Key.A: _cols = Math.Max(1, _cols - 1); break;
            case Key.D: _cols = Math.Min(32, _cols + 1); break;
            case Key.S: _rows = Math.Max(1, _rows - 1); break;
            case Key.W: _rows = Math.Min(32, _rows + 1); break;
            default: return base.OnKey(e);
        }
        Window.DisplayStatus(Help);
        View.QueueRedraw();
        return true;
    }

    public override void OnRedraw(MapView2D v)
    {
        if (Points.Count == 1)
        {
            var a = Points[0];
            foreach (var c in Cells(a, Preview, _cols, _rows))
                for (int i = 0; i < 4; i++)
                    v.DrawLine(v.MapToScreen(c[i]), v.MapToScreen(c[(i + 1) % 4]), ColorSettings.Highlight, 1);
        }
        else
            v.DrawRect(new Rect2(v.MapToScreen(Preview) - new Vector2(3, 3), new Vector2(6, 6)), ColorSettings.Indication, false, 1);
    }
}

/// <summary>
/// Align Grid: a click on a vertex moves the grid's origin there; on a wall, to the wall's start, the grid turned
/// along it; on empty space, the grid back to the map's (origin 0, 0, not turned). Snapping, G's X / Y and the drawing
/// boxes follow the grid (MapView2D.GridOrigin, GridAngle). Esc or the mode buttons leave it.
/// </summary>
public class AlignGridMode : EditMode
{
    public override string Name => "Align Grid";
    public override string Action => "aligngridmode";
    Element _hover = Element.None;
    EditMode _previous;

    public override void OnEngage()
    {
        _previous = Window.Editing.LastClassic ?? Window.Editing.Modes[0];
        Window.DisplayStatus("Align Grid: click a vertex (the grid's origin there) or a wall (the grid along it); empty space: the grid back; Esc leaves");
    }

    Element Pick(Vector2 m)
    {
        float r = View.Units(ClassicMode.HighlightRange);
        int p = Doc.PointNear(m.X, m.Y, r, Doc.PointShown);
        if (p >= 0)
            return new Element(ElementKind.Point, p);
        int l = Doc.LineNear(m.X, m.Y, r, Doc.LineShown);
        return l >= 0 ? new Element(ElementKind.Line, l) : Element.None;
    }

    public override void OnMouseMove(Vector2 map)
    {
        _hover = Pick(map);
        View.QueueRedraw();
    }

    public void HandleInput(InputEvent e)
    {
        if (e is InputEventMouseMotion mm)
            OnMouseMove(View.ScreenToMap(mm.Position));
        else if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } mb)
            Window.InputDo(Pick(View.ScreenToMap(mb.Position)) switch
            {
                { Kind: ElementKind.Point } pt => $"aligngrid point {pt.Index}",
                { Kind: ElementKind.Line } ln => $"aligngrid line {ln.Index}",
                _ => "aligngrid reset",
            });
        else if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right })
            Window.Editing.Switch(_previous);
    }

    public override bool OnKey(InputEventKey e)
    {
        if (e.Keycode != Key.Escape)
            return false;
        Window.Editing.Switch(_previous);
        return true;
    }

    /// <summary>The grid set from a vertex, a wall or back to the map's (the "aligngrid" command: recorded, replayed).</summary>
    public static string Align(MapView2D v, Document.MapDocument d, string what, int index)
    {
        switch (what)
        {
            case "point" when index >= 0 && index < d.Points.Count:
                v.GridOrigin = new Vector2(d.Points[index].X, d.Points[index].Y);
                v.QueueRedraw();
                return $"grid: its origin at vertex {index} ({d.Points[index].X}, {d.Points[index].Y})";
            case "line" when index >= 0 && index < d.Lines.Count:
                var (ax, ay, bx, by) = BuildGeom.WallEnds(d, d.Lines[index].Front);
                v.GridOrigin = new Vector2(ax, ay);
                v.GridAngle = Mathf.Atan2(by - ay, bx - ax);
                v.QueueRedraw();
                return $"grid: along wall {d.Lines[index].Front}, turned {Mathf.RadToDeg(v.GridAngle):0.#}°";
            default:
                v.GridOrigin = Vector2.Zero;
                v.GridAngle = 0;
                v.QueueRedraw();
                return "grid: back to the map's";
        }
    }

    public override void OnRedraw(MapView2D v)
    {
        if (_hover.Kind == ElementKind.Point && _hover.Index < Doc.Points.Count)
            v.DrawPoint(_hover.Index, ColorSettings.Highlight, 5);
        else if (_hover.Kind == ElementKind.Line && _hover.Index < Doc.Lines.Count)
            v.DrawLine(_hover.Index, ColorSettings.Highlight, 3);
        // the grid's origin and its X axis
        var o = v.MapToScreen(v.GridOrigin);
        v.DrawLine(o, o + v.GridAxisX * 40, new Color(1f, 0.35f, 0.35f), 2);
        v.DrawLine(o, o + v.GridAxisY * 40, new Color(0.45f, 1f, 0.45f), 2);
    }
}

/// <summary>
/// Paste (Ctrl+V): what Ctrl+C copied follows the mouse, its first point on the grid (Shift: free); a click puts it
/// there (the "pastehere" command: one undo step, the pasted sectors or sprites selected), Esc or the right button
/// leaves it.
/// </summary>
public class PasteMode : EditMode
{
    public override string Name => "Paste";
    public override string Action => "pastemode";
    EditMode _previous;
    Vector2I _at;

    public Blood.MapClip Clip;

    public override void OnEngage()
    {
        _previous = Window.Editing.LastClassic ?? Window.Editing.Modes[0];
        OnMouseMove(View.Cursor);
        Window.DisplayStatus($"Paste {Clip?.What}: click where it goes (its first point on the grid, Shift: free); Esc or right click leaves");
    }

    /// <summary>How far it moves with the mouse at map: its first point there, on the grid.</summary>
    Vector2I Offset(Vector2 map)
    {
        var to = View.Snap && !ForgeInput.Shift ? View.SnapToGrid(map) : map.Round();
        return new Vector2I((int)to.X - Clip.Ref.X, (int)to.Y - Clip.Ref.Y);
    }

    public override void OnMouseMove(Vector2 map)
    {
        if (Clip == null)
            return;
        _at = Offset(map);
        View.QueueRedraw();
    }

    public void HandleInput(InputEvent e)
    {
        if (e is InputEventMouseMotion mm)
            OnMouseMove(View.ScreenToMap(mm.Position));
        else if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } mb && Clip != null)
        {
            var d = Offset(View.ScreenToMap(mb.Position));
            Window.Editing.Switch(_previous);
            Window.InputDo($"pastehere {d.X} {d.Y}");
        }
        else if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right })
            Window.Editing.Switch(_previous);
    }

    public override bool OnKey(InputEventKey e)
    {
        if (e.Keycode != Key.Escape)
            return false;
        Window.Editing.Switch(_previous);
        return true;
    }

    public override void OnRedraw(MapView2D v)
    {
        if (Clip == null)
            return;
        var off = new Vector2(_at.X, _at.Y);
        for (int k = 0; k < Clip.Outlines.Count; k++)
        {
            var loop = Clip.Outlines[k];
            for (int i = 0; i < (Clip.OpenOutlines.Contains(k) ? loop.Count - 1 : loop.Count); i++)
                v.DrawLine(v.MapToScreen((Vector2)loop[i] + off), v.MapToScreen((Vector2)loop[(i + 1) % loop.Count] + off), ColorSettings.Highlight, 1);
        }
        foreach (var p in Clip.SpriteAt)
            v.DrawArc(v.MapToScreen((Vector2)p + off), 5, 0, Mathf.Tau, 12, ColorSettings.Highlight, 1.5f);
        foreach (var l in Clip.Lights)
            v.DrawCircle(v.MapToScreen(new Vector2((float)l.X, (float)l.Y) + off), 4, new Color(l.Light.R / 255f, l.Light.G / 255f, l.Light.B / 255f));
    }
}
