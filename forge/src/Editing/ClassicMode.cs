// Chosen core: original code (UDB's ClassicMode mouse behaviour, rewritten).
using System;
using System.Collections.Generic;
using System.Linq;
using Chosen.Forge.Config;
using Chosen.Forge.View;
using Godot;

namespace Chosen.Forge.Editing;

/// <summary>
/// The mouse of UDB's classic modes. Left button: a click toggles the highlighted item's selection; pressed on empty
/// space and moved, a rectangle selects (Ctrl: deselects; without Shift the rectangle replaces the selection); pressed on
/// an item and moved, drags. Right button: released without moving edits (the properties dialog of the selection or of
/// the highlighted item); moved past a few pixels while held, drags the highlighted or selected items; pressed on empty
/// space, starts drawing lines there (UDB's auto-draw; Sprites mode places a sprite instead). A subclass says what is
/// highlighted (Pick) and what a selection holds (Kind).
/// </summary>
public abstract class ClassicMode : EditMode
{
    public const float DragThreshold = 3;      // pixels
    /// <summary>Pixels from the cursor an item is highlighted within (UDB's highlight range; Preferences).</summary>
    public static float HighlightRange = 10;

    public Element Highlighted { get; protected set; } = Element.None;
    protected abstract ElementKind Kind { get; }
    protected HashSet<int> Selection => Window.SelectionOf(Kind);

    enum Press { None, Select, Edit }
    Press _press;
    Vector2 _pressScreen, _pressMap;
    bool _dragging, _rect;
    Vector2 _rectTo;
    bool _ctrl, _shift;
    /// <summary>The left press was a double-click's second (OnSelectEnd: the item and what lies within it).</summary>
    bool _double;

    /// <summary>What a double-click on an item takes along with it (Sectors mode: the sectors inside it), or null
    /// (a double-click is two clicks).</summary>
    protected virtual List<int> Within(int item) => null;

    /// <summary>The status words for a double-click's item and what came with it.</summary>
    protected virtual string WithinWhat(int item, int count) => $"sector {item} and the {count} sector{(count == 1 ? "" : "s")} inside it";

    /// <summary>What is under the cursor (map units).</summary>
    protected abstract Element Pick(Vector2 map);

    public override void OnEngage() => Highlight(Pick(View.Cursor));
    public override void OnDisengage()
    {
        // a mode switched mid-drag (V/L/S/T while the button is held): the drag is put back
        if (_dragging)
            CancelDrag();
        if (_rotating)
            EndRotate(false);
        if (_moving)
            EndMove(false);
        if (_scaling)
            EndScale(false);
        _press = Press.None;
        _rect = false;
        Highlight(Element.None);
    }

    /// <summary>The last drag moved something (an undo level stands for it).</summary>
    protected bool LastDragMoved { get; private set; }
    public override void OnDocumentChanged() => Highlight(Pick(View.Cursor));

    protected void Highlight(Element e)
    {
        if (e == Highlighted)
            return;
        Highlighted = e;
        Window.ShowInfo(e);
        View.QueueRedraw();
    }

    public override void OnMouseMove(Vector2 map)
    {
        if (!_dragging && !_rect)
            Highlight(Pick(map));
    }

    public override void OnMouseLeave()
    {
        if (!_dragging && !_rect)
            Highlight(Element.None);
    }

    /// <summary>The view's input as UDB's classic modes take it.</summary>
    public void HandleInput(InputEvent e)
    {
        // scaling (Shift+S): a handle dragged scales it, a click off the handles keeps it, the right button puts it back
        if (_scaling)
        {
            ScaleInput(e);
            return;
        }
        // moving (G): the mouse moves it, a click keeps it, the right button puts it back
        if (_moving)
        {
            if (e is InputEventMouseMotion gm)
                MoveTo(View.ScreenToMap(gm.Position), gm.ShiftPressed);
            else if (e is InputEventMouseButton { Pressed: true } gb && gb.ButtonIndex is MouseButton.Left or MouseButton.Right)
                EndMove(gb.ButtonIndex == MouseButton.Left);
            return;
        }
        // turning (R): the mouse turns it, a click keeps it, the right button puts it back
        if (_rotating)
        {
            if (e is InputEventMouseMotion rm)
                RotateTo(View.ScreenToMap(rm.Position), rm.ShiftPressed);
            else if (e is InputEventMouseButton { Pressed: true } rb && rb.ButtonIndex is MouseButton.Left or MouseButton.Right)
                EndRotate(rb.ButtonIndex == MouseButton.Left);
            return;
        }
        if (e is InputEventMouseButton mb && (mb.ButtonIndex == MouseButton.Left || mb.ButtonIndex == MouseButton.Right))
        {
            var map = View.ScreenToMap(mb.Position);
            _ctrl = mb.CtrlPressed;
            _shift = mb.ShiftPressed;
            if (mb.Pressed && _press == Press.None)
            {
                _double = mb.DoubleClick && mb.ButtonIndex == MouseButton.Left;
                _press = mb.ButtonIndex == MouseButton.Left ? Press.Select : Press.Edit;
                _pressScreen = mb.Position;
                _pressMap = map;
                if (_press == Press.Select)
                    OnSelectBegin(map);
                else
                    OnEditBegin(map);
            }
            else if (!mb.Pressed && (_press == Press.Select && mb.ButtonIndex == MouseButton.Left
                || _press == Press.Edit && mb.ButtonIndex == MouseButton.Right))
            {
                var was = _press;
                _press = Press.None;
                if (_dragging)
                {
                    _dragging = false;
                    OnDragStop(map);
                }
                else if (_rect)
                {
                    _rect = false;
                    OnEndMultiSelection(_pressMap, map, _ctrl);
                    View.QueueRedraw();
                }
                else if (was == Press.Select)
                    OnSelectEnd(map);
                else
                    OnEditEnd(map);
                Highlight(Pick(map));
            }
        }
        else if (e is InputEventMouseMotion mm)
        {
            var map = View.ScreenToMap(mm.Position);
            if (_press != Press.None && !_dragging && !_rect && mm.Position.DistanceTo(_pressScreen) > DragThreshold)
            {
                if (_press == Press.Select && Highlighted.IsNone)
                    _rect = true;
                else if (CanDrag())
                {
                    _dragging = true;
                    OnDragStart(_pressMap, _press == Press.Select);
                }
            }
            if (_dragging)
                OnDragMove(map);
            else if (_rect)
            {
                _rectTo = map;
                OnUpdateMultiSelection(_pressMap, map);
                View.QueueRedraw();
            }
            else
                OnMouseMove(map);
        }
    }

    // ---------------------------------------------------------------- items at one place (rooms over rooms): Tab
    List<int> _here = new();
    int _cycle;

    /// <summary>The highlighted one of the items at one place (candidates by distance, nearest first; those as near
    /// as the nearest share the place): the one picked with Tab while they stay the same, else the first.</summary>
    protected Element PickAmong(ElementKind kind, List<(int item, double dist)> near)
    {
        var here = near.Count == 0 ? new List<int>()
            : near.Where(n => n.dist <= near.Min(o => o.dist) + 0.5).OrderBy(n => n.item).Select(n => n.item).ToList();
        if (!here.SequenceEqual(_here))
        {
            int keep = _cycle < _here.Count ? _here[_cycle] : -1;
            _here = here;
            _cycle = keep >= 0 && here.Contains(keep) ? here.IndexOf(keep) : 0;
        }
        Window.SetOverlapCount(_here.Count);
        return _here.Count > 0 ? new Element(kind, _here[_cycle]) : Element.None;
    }

    /// <summary>Tab: the next item at the place under the cursor.</summary>
    public override void OnCycle()
    {
        if (_here.Count < 2)
            return;
        _cycle = (_cycle + 1) % _here.Count;
        Highlight(new Element(Kind, _here[_cycle]));
    }

    /// <summary>The edit button on empty space starts drawing lines there (UDB's auto-draw on edit).</summary>
    protected virtual bool DrawsOnEdit => true;

    public override void OnEditBegin(Vector2 map)
    {
        if (Highlighted.IsNone && DrawsOnEdit && Doc != null)
            StartDrawing(map);
    }

    /// <summary>Draw Lines mode, its first point at map (snapped as it snaps), or none.</summary>
    protected void StartDrawing(Vector2? at)
    {
        var d = Window.Editing.Modes.First(m => m.GetType() == typeof(DrawGeometryMode)) as DrawGeometryMode;
        Window.Editing.Switch(d);
        if (at is { } p)
            d.Begin(p);
    }

    /// <summary>Whether a press on the highlighted item and a move drags it.</summary>
    protected virtual bool CanDrag() => !Highlighted.IsNone;

    // ---------------------------------------------------------------- dragging (UDB's drag modes)
    /// <summary>What dragging items moves: points, sprites, and the place snapped to the grid (the grabbed item's).</summary>
    protected virtual (List<int> points, List<int> sprites, Vector2 reference) DragSet(List<int> items) => (new(), new(), default);

    List<int> _dragPoints, _dragSprites;
    Vector2I[] _pointFrom, _spriteFrom;
    Vector2 _dragRef, _grab;
    Vector2I _delta;

    /// <summary>Where the dragged points are now (the drag going on), or none.</summary>
    protected List<Vector2I> DraggedPointPlaces() => _dragPoints == null ? new List<Vector2I>()
        : _dragPoints.Select((p, i) => _pointFrom[i] + _delta).ToList();

    /// <summary>The item a drag set measures from: the highlighted one when it is among items, else the first (R turning
    /// a selection with the mouse on nothing).</summary>
    protected int Grabbed(List<int> items) => !Highlighted.IsNone && items.Contains(Highlighted.Index) ? Highlighted.Index : items[0];

    /// <summary>What Ctrl+C copies of items: sectors (and the rooms inside them) and sprites; none in Vertices and Walls
    /// mode.</summary>
    public virtual (List<int> sectors, List<int> sprites) CopySet(List<int> items) => (new List<int>(), new List<int>());

    /// <summary>The lights Ctrl+C takes of the items (Lights mode's; a copied sprite's and sector's come anyway).</summary>
    public virtual List<int> CopyLights(List<int> items) => new();

    /// <summary>The items Ctrl+C takes: the selection, else what the mouse is on.</summary>
    public List<int> CopyItems() => Selection.Count > 0 ? new List<int>(Selection) : Highlighted.IsNone ? new List<int>() : new List<int> { Highlighted.Index };

    /// <summary>The items a drag takes: the selection when the grabbed item is in it, else the grabbed item.</summary>
    protected List<int> DragItems() =>
        !Highlighted.IsNone && Selection.Contains(Highlighted.Index) ? new List<int>(Selection) : new List<int> { Highlighted.Index };

    /// <summary>The next drag is part of the step just taken (a point put in and dragged out: one undo step).</summary>
    protected bool JoinLastUndo;
    bool _ownUndo;

    public override void OnDragStart(Vector2 map, bool select)
    {
        var (pts, sprs, reference) = DragSet(DragItems());
        _ownUndo = !JoinLastUndo;
        JoinLastUndo = false;
        if (pts.Count == 0 && sprs.Count == 0)
        {
            _dragPoints = null;
            return;
        }
        if (_ownUndo)
            Window.Undo.CreateUndo($"Drag {Name.ToLowerInvariant()}");
        _dragPoints = pts;
        _dragSprites = sprs;
        _pointFrom = pts.ConvertAll(p => new Vector2I(Doc.Points[p].X, Doc.Points[p].Y)).ToArray();
        _spriteFrom = sprs.ConvertAll(s => new Vector2I(Doc.Sprites[s].X, Doc.Sprites[s].Y)).ToArray();
        _dragRef = reference;
        _grab = map;
        _delta = Vector2I.Zero;
    }

    public override void OnDragMove(Vector2 map)
    {
        if (_dragPoints == null)
            return;
        var to = _dragRef + (map - _grab);
        bool snap = View.Snap && !ForgeInput.Shift;
        var placed = snap ? View.SnapToGrid(to) : to.Round();
        var d = new Vector2I((int)(placed.X - _dragRef.X), (int)(placed.Y - _dragRef.Y));
        if (d == _delta)
            return;
        _delta = d;
        for (int i = 0; i < _dragPoints.Count; i++)
            Doc.MovePoint(_dragPoints[i], _pointFrom[i].X + d.X, _pointFrom[i].Y + d.Y);
        for (int i = 0; i < _dragSprites.Count; i++)
        {
            Doc.Sprites[_dragSprites[i]].X = _spriteFrom[i].X + d.X;
            Doc.Sprites[_dragSprites[i]].Y = _spriteFrom[i].Y + d.Y;
        }
        Doc.NotifyChanged();
        Window.DisplayStatus($"moved {Units.Xy(d.X)}, {Units.Xy(d.Y)}");
    }

    public override void OnDragStop(Vector2 map)
    {
        if (_dragPoints == null)
            return;
        LastDragMoved = _delta != Vector2I.Zero;
        if (_delta == Vector2I.Zero)
        {
            if (_ownUndo)
                Window.Undo.WithdrawUndo();
        }
        else
        {
            int joined = JoinAfterMove(_dragPoints);
            foreach (int s in _dragSprites)
                Doc.UpdateSpriteSector(s);
            Doc.NotifyChanged();
            Window.UndoChanged();
            if (joined > 0)
                Window.DisplayStatus($"moved {Units.Xy(_delta.X)}, {Units.Xy(_delta.Y)}" + Joined(joined));
        }
        _dragPoints = null;
    }

    /// <summary>After a move kept (a drag, G): the walls of the sectors at the points that moved now lying along
    /// another sector's walls the other way joined into red pairs, their points merged (GeometryOps.JoinTouching).
    /// The number joined.</summary>
    int JoinAfterMove(IEnumerable<int> movedPoints)
    {
        var sectors = new HashSet<int>();
        foreach (int p in movedPoints)
            if (p >= 0 && p < Doc.Points.Count)
                foreach (int w in Doc.Points[p].Walls)
                    if (w >= 0 && w < Doc.Walls.Count && Doc.Walls[w].Sector >= 0)
                        sectors.Add(Doc.Walls[w].Sector);
        return sectors.Count == 0 ? 0 : Doc.JoinTouching(sectors);
    }

    static string Joined(int n) => n > 0 ? $"; {n} wall(s) joined to the sectors they now lie against (their points merged)" : "";

    /// <summary>A drag put back (Esc, the mode switched mid-drag): a mode dragging its own items (Lights) puts them back.</summary>
    protected virtual void OnDragCancelled() { }

    void CancelDrag()
    {
        OnDragCancelled();
        if (_dragPoints != null)
        {
            // (a drag joined to the step before it: Esc takes that back with it)
            if (_delta != Vector2I.Zero)
                Window.Undo.Revert();
            else if (_ownUndo)
                Window.Undo.WithdrawUndo();
            _dragPoints = null;
        }
        _dragging = false;
        _press = Press.None;
        Window.UndoChanged();
    }

    public override bool OnKey(InputEventKey e)
    {
        if (_moving)
        {
            if (e.Keycode == Key.Escape)
                EndMove(false);
            else if (e.Keycode is Key.Enter or Key.KpEnter or Key.G)
                EndMove(true);
            else if (e.Keycode == Key.O)
            {
                // O: the falloff on or off (Shift+wheel: its curve)
                _falloff = !_falloff;
                _movDelta = new Vector2I(int.MinValue, 0);
                MoveTo(View.Cursor, ForgeInput.Shift);
            }
            else if (e.Keycode is Key.X or Key.Y)
            {
                // the grid's X or Y axis through where it started (again: free)
                char axis = e.Keycode == Key.X ? 'x' : 'y';
                _lock = _lock == axis ? ' ' : axis;
                _movDelta = new Vector2I(int.MinValue, 0);
                MoveTo(View.Cursor, ForgeInput.Shift);
            }
            return true;
        }
        if (_scaling)
        {
            if (e.Keycode == Key.Escape)
                EndScale(false);
            else if (e.Keycode is Key.Enter or Key.KpEnter)
                EndScale(true);
            return true;
        }
        if (_rotating)
        {
            if (e.Keycode == Key.P)
            {
                // the pivot where the mouse is (on the grid, Shift: free); the turn starts again from there
                var at = ForgeInput.Shift ? View.Cursor.Round() : View.SnapToGrid(View.Cursor);
                _rotBy = int.MinValue;
                RotateApply(0);
                _pivot = at;
                _rotStartPending = true;
                Window.DisplayStatus($"Rotate: the pivot at {Units.Xy((int)at.X)}, {Units.Xy((int)at.Y)}; move the mouse round it");
                View.QueueRedraw();
                return true;
            }
            if (e.Keycode == Key.Escape)
                EndRotate(false);
            else if (e.Keycode is Key.Enter or Key.KpEnter or Key.R)
                EndRotate(true);
            // (the other keys wait: a turn goes on until it is kept or put back)
            return true;
        }
        if (_dragging && e.Keycode == Key.Escape)
        {
            CancelDrag();
            return true;
        }
        return false;
    }

    // ---------------------------------------------------------------- moving (G, Blender's grab: X / Y lock it to the grid's axes)
    bool _moving;
    List<int> _movPoints, _movSprites;
    Vector2I[] _movPointFrom, _movSpriteFrom;
    Vector2 _movRef, _movGrab;
    Vector2I _movDelta;
    char _lock = ' ';
    // the falloff (O): points joined by walls to what moves, within the radius of it, move part of the way
    bool _falloff;
    static float _radius = 1024;
    /// <summary>The falloff's curve and radius (a session's state: replays start with them).</summary>
    public static int FallCurve { get => _curve; set => _curve = Math.Clamp(value, 0, Curves.Length - 1); }
    public static float FallRadius { get => _radius; set => _radius = Math.Clamp(value, 16, 65536); }
    static int _curve;
    static readonly string[] Curves = { "smooth", "sphere", "root", "linear", "sharp", "constant" };
    readonly Dictionary<int, Vector2I> _fallFrom = new();
    Dictionary<int, float> _fallWeight = new();

    public bool Moving => _moving;
    public bool Falloff => _falloff;
    public string FalloffCurve => Curves[_curve];

    /// <summary>A falloff curve's weight at t (1 at what moves, 0 at the radius).</summary>
    static float Weight(int curve, float t) => curve switch
    {
        0 => t * t * (3 - 2 * t),
        1 => Mathf.Sqrt(Math.Max(0, 2 * t - t * t)),
        2 => Mathf.Sqrt(t),
        3 => t,
        4 => t * t,
        _ => 1,
    };

    /// <summary>The points the falloff takes and how much each moves: those joined to what moves (wall to wall),
    /// nearer to it (where it was) than the radius.</summary>
    Dictionary<int, float> FalloffWeights()
    {
        var moving = new HashSet<int>(_movPoints);
        var w = new Dictionary<int, float>();
        if (!_falloff || moving.Count == 0)
            return w;
        var from = _movPoints.Select((p, i) => (Vector2)_movPointFrom[i]).ToList();
        float Near(int p)
        {
            var at = (Vector2)Orig(p);
            return from.Min(f => f.DistanceTo(at));
        }
        var queue = new Queue<int>(moving);
        var seen = new HashSet<int>(moving);
        while (queue.Count > 0)
        {
            int p = queue.Dequeue();
            foreach (int wl in Doc.Points[p].Walls)
            {
                int a = Doc.PointOf[Doc.Walls[wl].Point2];
                int b = Doc.PrevWall[wl] >= 0 ? Doc.PointOf[Doc.PrevWall[wl]] : -1;
                foreach (int q in new[] { a, b })
                {
                    if (q < 0 || !seen.Add(q))
                        continue;
                    float d = Near(q);
                    if (d >= _radius || !Doc.PointEditable(q))
                        continue;
                    w[q] = Weight(_curve, 1 - d / _radius);
                    queue.Enqueue(q);
                }
            }
        }
        return w;
    }

    /// <summary>Where a point was when the move began.</summary>
    Vector2I Orig(int p) => _fallFrom.TryGetValue(p, out var f) ? f : new Vector2I(Doc.Points[p].X, Doc.Points[p].Y);

    /// <summary>G: the selection (else what the mouse is on) moved by the mouse: what dragging it would move, its
    /// grabbed item's place snapped to the grid (Shift: free); X or Y locks it to the grid's X or Y axis through where
    /// it was (the grid's frame: View.GridOrigin, GridAngle), the same key again frees it. One undo step: a click or
    /// Enter keeps it, Esc or the right button puts it back.</summary>
    public void StartMove()
    {
        if (_moving || _rotating || _scaling || _dragging || Doc == null)
            return;
        var items = Selection.Count > 0 ? new List<int>(Selection) : Highlighted.IsNone ? new List<int>() : new List<int> { Highlighted.Index };
        if (items.Count == 0)
        {
            Window.DisplayStatus("G moves the selection or what the mouse is on: nothing there");
            return;
        }
        var (pts, sprs, reference) = DragSet(items);
        if (pts.Count == 0 && sprs.Count == 0)
            return;
        Window.Undo.CreateUndo($"Move {Name.ToLowerInvariant()}");
        _movPoints = pts;
        _movSprites = sprs;
        _movPointFrom = pts.ConvertAll(p => new Vector2I(Doc.Points[p].X, Doc.Points[p].Y)).ToArray();
        _movSpriteFrom = sprs.ConvertAll(s => new Vector2I(Doc.Sprites[s].X, Doc.Sprites[s].Y)).ToArray();
        _movRef = reference;
        _movGrab = View.Cursor;
        _movDelta = Vector2I.Zero;
        _lock = ' ';
        _falloff = false;
        _fallFrom.Clear();
        _fallWeight = new Dictionary<int, float>();
        View.WheelTaken = (up, shift) =>
        {
            if (!_moving || !_falloff)
                return false;
            // the wheel: the radius; Shift+wheel: the curve, the next or the one before
            if (shift)
                _curve = (_curve + (up ? 1 : Curves.Length - 1)) % Curves.Length;
            else
                _radius = Math.Clamp(_radius * (up ? 1.25f : 0.8f), 16, 65536);
            _movDelta = new Vector2I(int.MinValue, 0);
            MoveTo(View.Cursor, ForgeInput.Shift);
            return true;
        };
        _moving = true;
        Window.DisplayStatus("Move: the mouse moves it (on the grid, Shift: free); X or Y locks it to the grid's axis; click or Enter keeps it, Esc or right click puts it back");
        View.QueueRedraw();
    }

    void MoveTo(Vector2 map, bool free)
    {
        // in the grid's frame: the reference's place moved by the mouse, held on the locked axis, snapped to the grid
        var gRef = View.ToGrid(_movRef);
        var g = View.ToGrid(_movRef + (map - _movGrab));
        if (_lock == 'x')
            g.Y = gRef.Y;
        else if (_lock == 'y')
            g.X = gRef.X;
        if (View.Snap && !free)
        {
            if (_lock != 'y')
                g.X = View.SnapGridValue(g.X);
            if (_lock != 'x')
                g.Y = View.SnapGridValue(g.Y);
        }
        var placed = View.FromGrid(g).Round();
        var d = new Vector2I((int)(placed.X - _movRef.X), (int)(placed.Y - _movRef.Y));
        View.QueueRedraw();
        if (d == _movDelta)
            return;
        _movDelta = d;
        // the falloff's points: where they were kept; those it no longer takes put back
        var weights = FalloffWeights();
        foreach (int p in weights.Keys)
            if (!_fallFrom.ContainsKey(p))
                _fallFrom[p] = new Vector2I(Doc.Points[p].X, Doc.Points[p].Y);
        foreach (int p in _fallWeight.Keys.Where(p => !weights.ContainsKey(p)))
            Doc.MovePoint(p, _fallFrom[p].X, _fallFrom[p].Y);
        _fallWeight = weights;
        foreach (var (p, wt) in weights)
            Doc.MovePoint(p, _fallFrom[p].X + (int)Math.Round(d.X * wt), _fallFrom[p].Y + (int)Math.Round(d.Y * wt));
        for (int i = 0; i < _movPoints.Count; i++)
            Doc.MovePoint(_movPoints[i], _movPointFrom[i].X + d.X, _movPointFrom[i].Y + d.Y);
        for (int i = 0; i < _movSprites.Count; i++)
        {
            Doc.Sprites[_movSprites[i]].X = _movSpriteFrom[i].X + d.X;
            Doc.Sprites[_movSprites[i]].Y = _movSpriteFrom[i].Y + d.Y;
        }
        Doc.NotifyChanged();
        string locked = _lock == 'x' ? " (on the grid's X)" : _lock == 'y' ? " (on the grid's Y)" : "";
        string fall = _falloff ? $"; falloff {Curves[_curve]}, radius {Units.Xy((int)_radius)} ({_fallWeight.Count} points; wheel: radius, Shift+wheel: curve)" : "; O: falloff";
        Window.DisplayStatus($"Move: {Units.Xy(d.X)}, {Units.Xy(d.Y)}{locked}{fall}; X / Y lock to the grid's axis; click or Enter keeps it, Esc puts it back");
    }

    void EndMove(bool keep)
    {
        if (!_moving)
            return;
        _moving = false;
        View.WheelTaken = null;
        bool moved = _movDelta != Vector2I.Zero && _movDelta.X != int.MinValue;
        int joined = 0;
        if (!moved)
            Window.Undo.WithdrawUndo();
        else if (!keep)
            Window.Undo.Revert();
        else
        {
            joined = JoinAfterMove(_movPoints.Concat(_fallWeight?.Keys ?? Enumerable.Empty<int>()));
            foreach (int s in _movSprites)
                Doc.UpdateSpriteSector(s);
            Doc.NotifyChanged();
        }
        Window.UndoChanged();
        Window.DisplayStatus(!keep || !moved ? "move: put back" : $"moved {Units.Xy(_movDelta.X)}, {Units.Xy(_movDelta.Y)}" + Joined(joined));
        View.QueueRedraw();
    }

    // ---------------------------------------------------------------- scaling (Shift+S: UDB's Edit Selection mode's box)
    bool _scaling;
    List<int> _scPoints, _scSprites;
    Vector2I[] _scPointFrom, _scSpriteFrom;
    int[] _scAngFrom;
    // the box along the grid's axes (in its frame) as it was, and where its min and max corners are now (a side
    // dragged past the other: the max corner's below the min's, the selection mirrored)
    Vector2 _box0Min, _box0Max, _boxMin, _boxMax;
    int _handle = -1, _hover = -1;

    public bool Scaling => _scaling;

    /// <summary>Shift+S: a box round the selection (else what the mouse is on: what dragging it would move) along the
    /// grid's axes, its handles dragged: a corner scales it evenly from the opposite corner, a side scales it across
    /// from the opposite side; the handle on the grid (Shift: free). Dragged past the opposite side (a negative scale)
    /// it mirrors: kept so in one axis, each sector wholly in it has its walls' loops turned round (Build's sectors
    /// wind one way: GeometryOps.ReverseLoops, the walls' pictures x-flipped so they don't read backwards) and sprites'
    /// angles mirror. Points and sprites' places scale. A click off the handles or Enter keeps it (one undo step), Esc
    /// or the right button puts it back.</summary>
    public void StartScale()
    {
        if (_scaling || _moving || _rotating || _dragging || Doc == null)
            return;
        var items = Selection.Count > 0 ? new List<int>(Selection) : Highlighted.IsNone ? new List<int>() : new List<int> { Highlighted.Index };
        if (items.Count == 0)
        {
            Window.DisplayStatus("Shift+S scales the selection or what the mouse is on: nothing there");
            return;
        }
        var (pts, sprs, _) = DragSet(items);
        if (pts.Count + sprs.Count < 2)
        {
            Window.DisplayStatus("scale: it needs two points or more");
            return;
        }
        Window.Undo.CreateUndo($"Scale {Name.ToLowerInvariant()}");
        _scPoints = pts;
        _scSprites = sprs;
        _scPointFrom = pts.ConvertAll(p => new Vector2I(Doc.Points[p].X, Doc.Points[p].Y)).ToArray();
        _scSpriteFrom = sprs.ConvertAll(s => new Vector2I(Doc.Sprites[s].X, Doc.Sprites[s].Y)).ToArray();
        _scAngFrom = sprs.ConvertAll(s => Doc.Sprites[s].Ang).ToArray();
        var g = _scPointFrom.Concat(_scSpriteFrom).Select(p => View.ToGrid(p)).ToList();
        _box0Min = _boxMin = new Vector2(g.Min(p => p.X), g.Min(p => p.Y));
        _box0Max = _boxMax = new Vector2(g.Max(p => p.X), g.Max(p => p.Y));
        _handle = _hover = -1;
        _scaling = true;
        Window.DisplayStatus("Scale: drag a corner (evenly) or a side (across), past the other side to mirror; on the grid, Shift: free; click off the box or Enter keeps it, Esc or right click puts it back");
        View.QueueRedraw();
    }

    /// <summary>The handles in the grid's frame: 0..3 the corners (from min, clockwise), 4..7 the sides' middles.</summary>
    Vector2[] Handles()
    {
        Vector2 a = _boxMin, b = _boxMax, m = (a + b) / 2;
        return new[] { a, new Vector2(b.X, a.Y), b, new Vector2(a.X, b.Y), new Vector2(m.X, a.Y), new Vector2(b.X, m.Y), new Vector2(m.X, b.Y), new Vector2(a.X, m.Y) };
    }

    int HandleAt(Vector2 map)
    {
        var hs = Handles();
        for (int i = 0; i < hs.Length; i++)
            if (View.MapToScreen(View.FromGrid(hs[i])).DistanceTo(View.MapToScreen(map)) <= 8)
                return i;
        return -1;
    }

    void ScaleInput(InputEvent e)
    {
        if (e is InputEventMouseMotion mm)
        {
            var map = View.ScreenToMap(mm.Position);
            if (_handle >= 0)
                ScaleTo(map, mm.ShiftPressed);
            else
            {
                int h = HandleAt(map);
                if (h != _hover)
                {
                    _hover = h;
                    View.QueueRedraw();
                }
            }
        }
        else if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left } lb)
        {
            if (lb.Pressed)
            {
                _handle = HandleAt(View.ScreenToMap(lb.Position));
                if (_handle < 0)
                    EndScale(true);
            }
            else
                _handle = -1;
        }
        else if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right })
            EndScale(false);
    }

    void ScaleTo(Vector2 map, bool free)
    {
        var g = View.ToGrid(map);
        if (View.Snap && !free)
            g = new Vector2(View.SnapGridValue(g.X), View.SnapGridValue(g.Y));
        Vector2 min = _boxMin, max = _boxMax;
        const float least = 1;
        // the opposite side or corner stays; the handle's side moves, past the one staying too (mirrored), never onto
        // it. (min and max: the images of the box's min and max corners, in either order once mirrored.)
        bool corner = _handle < 4;
        int h = _handle;
        bool left = h is 0 or 3 or 7, right = h is 1 or 2 or 5, top = h is 0 or 1 or 4, bottom = h is 2 or 3 or 6;
        static float Off(float v, float stay) => Math.Abs(v - stay) >= least ? v : stay + (v >= stay ? least : -least);
        if (corner)
        {
            // evenly: the corner's diagonal from the one staying, the mouse projected onto it (negative: mirrored
            // through the staying corner)
            var stay = new Vector2(left ? max.X : min.X, top ? max.Y : min.Y);
            var moving = new Vector2(left ? min.X : max.X, top ? min.Y : max.Y);
            var diag = moving - stay;
            float f = 1;
            if (diag.LengthSquared() > 0)
            {
                f = (g - stay).Dot(diag) / diag.LengthSquared();
                float lf = least / diag.Length();
                if (Math.Abs(f) < lf)
                    f = f < 0 ? -lf : lf;
            }
            var to = stay + diag * f;
            if (left)
                min.X = to.X;
            else
                max.X = to.X;
            if (top)
                min.Y = to.Y;
            else
                max.Y = to.Y;
        }
        else
        {
            if (left)
                min.X = Off(g.X, max.X);
            if (right)
                max.X = Off(g.X, min.X);
            if (top)
                min.Y = Off(g.Y, max.Y);
            if (bottom)
                max.Y = Off(g.Y, min.Y);
        }
        _boxMin = min;
        _boxMax = max;
        ScaleApply();
    }

    /// <summary>Everything mapped from the box as it was to the box as it is (in the grid's frame).</summary>
    void ScaleApply()
    {
        var s0 = _box0Max - _box0Min;
        var s1 = _boxMax - _boxMin;
        Vector2 Map(Vector2I p)
        {
            var g = View.ToGrid(p);
            float x = s0.X > 0 ? _boxMin.X + (g.X - _box0Min.X) * s1.X / s0.X : g.X;
            float y = s0.Y > 0 ? _boxMin.Y + (g.Y - _box0Min.Y) * s1.Y / s0.Y : g.Y;
            return View.FromGrid(new Vector2(x, y)).Round();
        }
        for (int i = 0; i < _scPoints.Count; i++)
        {
            var m = Map(_scPointFrom[i]);
            Doc.MovePoint(_scPoints[i], (int)m.X, (int)m.Y);
        }
        var (mx, my) = Mirrored();
        // a mirrored sprite's angle: across the grid's Y axis (x mirrored), its X axis (y mirrored), both: turned half
        int axis = (int)Math.Round(View.GridAngle * 2048 / Mathf.Tau);
        for (int i = 0; i < _scSprites.Count; i++)
        {
            var m = Map(_scSpriteFrom[i]);
            var sp = Doc.Sprites[_scSprites[i]];
            sp.X = (int)m.X;
            sp.Y = (int)m.Y;
            int a = _scAngFrom[i];
            sp.Ang = (mx && my ? a + 1024 : mx ? 2 * axis + 1024 - a : my ? 2 * axis - a : a) & 2047;
        }
        Doc.NotifyChanged();
        float fx = s0.X > 0 ? s1.X / s0.X : 1, fy = s0.Y > 0 ? s1.Y / s0.Y : 1;
        Window.DisplayStatus($"Scale: {fx * 100:0.#}% x {fy * 100:0.#}% ({Units.Xy((int)Math.Abs(s1.X))} x {Units.Xy((int)Math.Abs(s1.Y))})"
            + (mx || my ? $", mirrored {(mx && my ? "both ways (a half turn)" : mx ? "across" : "up and down")}" : "")
            + "; click off the box or Enter keeps it, Esc puts it back");
        View.QueueRedraw();
    }

    /// <summary>Whether the box is mirrored along the grid's X and Y axes (its side dragged past the other).</summary>
    (bool x, bool y) Mirrored()
    {
        var s0 = _box0Max - _box0Min;
        var s1 = _boxMax - _boxMin;
        return (s0.X > 0 && s1.X < 0, s0.Y > 0 && s1.Y < 0);
    }

    void EndScale(bool keep)
    {
        if (!_scaling)
            return;
        _scaling = false;
        _handle = -1;
        bool changed = _boxMin != _box0Min || _boxMax != _box0Max;
        if (!changed)
            Window.Undo.WithdrawUndo();
        else if (!keep)
            Window.Undo.Revert();
        int turned = 0;
        if (changed && keep)
        {
            // mirrored one way: each sector wholly in it wound the other way now; its loops turned round
            var (mx, my) = Mirrored();
            if (mx != my)
            {
                var pts = new HashSet<int>(_scPoints);
                var whole = Enumerable.Range(0, Doc.Sectors.Count).Where(s =>
                {
                    var sec = Doc.Sectors[s];
                    for (int w = sec.WallPtr; w < sec.WallPtr + sec.WallNum; w++)
                        if (w < 0 || w >= Doc.PointOf.Length || !pts.Contains(Doc.PointOf[w]))
                            return false;
                    return sec.WallNum > 0;
                }).ToList();
                turned = whole.Count;
                Doc.ReverseLoops(whole);
            }
            foreach (int s in _scSprites)
                Doc.UpdateSpriteSector(s);
            Doc.NotifyChanged();
        }
        Window.UndoChanged();
        Window.DisplayStatus(!keep || !changed ? "scale: put back" : turned > 0 ? $"scaled and mirrored ({turned} sector(s) turned round)" : "scaled");
        View.QueueRedraw();
    }

    void DrawScale(MapView2D v)
    {
        var hs = Handles();
        for (int i = 0; i < 4; i++)
            v.DrawLine(v.MapToScreen(View.FromGrid(hs[i])), v.MapToScreen(View.FromGrid(hs[(i + 1) % 4])), ColorSettings.Highlight, 1);
        for (int i = 0; i < hs.Length; i++)
        {
            var p = v.MapToScreen(View.FromGrid(hs[i]));
            bool on = i == _hover || i == _handle;
            if (on)
                v.DrawRect(new Rect2(p - new Vector2(5, 5), new Vector2(10, 10)), ColorSettings.Selection);
            else
                v.DrawRect(new Rect2(p - new Vector2(5, 5), new Vector2(10, 10)), ColorSettings.Highlight, false, 1.5f);
        }
    }

    // ---------------------------------------------------------------- turning (R: UDB's Edit Selection mode's rotation)
    bool _rotating;
    List<int> _rotPoints, _rotSprites;
    Vector2I[] _rotPointFrom, _rotSpriteFrom;
    int[] _rotAngFrom;
    Vector2 _pivot;
    double _rotStart;
    int _rotBy;

    public bool Rotating => _rotating;

    /// <summary>R: the selection (else what the mouse is on) turned about its middle as the mouse goes round it, in
    /// steps of 15 degrees (Shift: free): its points (what dragging it would move: a sector's rooms inside it too) and
    /// its sprites, their places and angles. One undo step; a click or Enter keeps it, Esc or the right button puts it
    /// back. A single sprite turns where it stands.</summary>
    public void StartRotate()
    {
        if (_rotating || _moving || _scaling || _dragging || Doc == null)
            return;
        var items = Selection.Count > 0 ? new List<int>(Selection) : Highlighted.IsNone ? new List<int>() : new List<int> { Highlighted.Index };
        if (items.Count == 0)
        {
            Window.DisplayStatus("R turns the selection or what the mouse is on: nothing there");
            return;
        }
        var (pts, sprs, _) = DragSet(items);
        if (pts.Count == 0 && sprs.Count == 0)
            return;
        if (pts.Count == 1 && sprs.Count == 0)
        {
            Window.DisplayStatus("R: a single vertex turns about itself: select two or more (or walls, a sector)");
            return;
        }
        Window.Undo.CreateUndo($"Rotate {Name.ToLowerInvariant()}");
        _rotPoints = pts;
        _rotSprites = sprs;
        _rotPointFrom = pts.ConvertAll(p => new Vector2I(Doc.Points[p].X, Doc.Points[p].Y)).ToArray();
        _rotSpriteFrom = sprs.ConvertAll(s => new Vector2I(Doc.Sprites[s].X, Doc.Sprites[s].Y)).ToArray();
        _rotAngFrom = sprs.ConvertAll(s => Doc.Sprites[s].Ang).ToArray();
        // the middle of what turns (its bounds'), on whole Build units
        var all = _rotPointFrom.Concat(_rotSpriteFrom).ToList();
        _pivot = new Vector2((float)Math.Round((all.Min(p => p.X) + all.Max(p => p.X)) / 2.0), (float)Math.Round((all.Min(p => p.Y) + all.Max(p => p.Y)) / 2.0));
        var c = View.Cursor - _pivot;
        _rotStart = c.LengthSquared() > 0 ? Math.Atan2(c.Y, c.X) : 0;
        _rotBy = 0;
        _rotating = true;
        _rotStartPending = false;
        Window.DisplayStatus("Rotate: move the mouse round the middle (15° steps, Shift: free); P: the pivot at the mouse; click or Enter keeps it, Esc or right click puts it back");
        View.QueueRedraw();
    }

    bool _rotStartPending;

    void RotateTo(Vector2 map, bool free)
    {
        var c = map - _pivot;
        if (c.LengthSquared() < 1)
            return;
        // (a pivot just placed: the arm starts where the mouse first goes from it)
        if (_rotStartPending)
        {
            if (c.Length() < View.Units(8))
                return;
            _rotStart = Math.Atan2(c.Y, c.X);
            _rotStartPending = false;
            // (said: the motion that started the arm is a recorded event, a replay starts it there too)
            Window.DisplayStatus("Rotate: 0° from here; move the mouse round the pivot");
            return;
        }
        double deg = (Math.Atan2(c.Y, c.X) - _rotStart) * 180 / Math.PI;
        deg = ((deg % 360) + 540) % 360 - 180;
        if (!free)
            deg = Math.Round(deg / 15) * 15;
        // Build's angles: 2048 a circle, growing the way the view's y grows (512: south, a quarter turn clockwise)
        int by = (int)Math.Round(deg * 2048 / 360);
        View.QueueRedraw();
        if (by == _rotBy)
            return;
        RotateApply(by);
        Window.DisplayStatus($"Rotate: {by * 360.0 / 2048:0.#}° ({by} in Build's angles, 512 a quarter turn); P: the pivot at the mouse; click or Enter keeps it, Esc puts it back");
    }

    /// <summary>Everything turned by `by` (Build's angles) about the pivot, from where it was.</summary>
    void RotateApply(int by)
    {
        _rotBy = by;
        double rad = by * Math.PI / 1024, cos = Math.Cos(rad), sin = Math.Sin(rad);
        Vector2I Turn(Vector2I p)
        {
            double dx = p.X - _pivot.X, dy = p.Y - _pivot.Y;
            return new Vector2I((int)Math.Round(_pivot.X + dx * cos - dy * sin), (int)Math.Round(_pivot.Y + dx * sin + dy * cos));
        }
        for (int i = 0; i < _rotPoints.Count; i++)
        {
            var p = Turn(_rotPointFrom[i]);
            Doc.MovePoint(_rotPoints[i], p.X, p.Y);
        }
        for (int i = 0; i < _rotSprites.Count; i++)
        {
            var p = Turn(_rotSpriteFrom[i]);
            var sp = Doc.Sprites[_rotSprites[i]];
            sp.X = p.X;
            sp.Y = p.Y;
            sp.Ang = (_rotAngFrom[i] + by) & 2047;
        }
        Doc.NotifyChanged();
    }

    void EndRotate(bool keep)
    {
        if (!_rotating)
            return;
        _rotating = false;
        if (_rotBy == 0)
            Window.Undo.WithdrawUndo();
        else if (!keep)
            Window.Undo.Revert();
        else
        {
            foreach (int s in _rotSprites)
                Doc.UpdateSpriteSector(s);
            Doc.NotifyChanged();
        }
        Window.UndoChanged();
        Window.DisplayStatus(!keep || _rotBy == 0 ? "rotate: put back" : $"rotated {_rotBy * 360.0 / 2048:0.#}°");
        View.QueueRedraw();
    }

    public override void OnSelectEnd(Vector2 map)
    {
        // a click: the highlighted item in or out of the selection (UDB); on nothing, the selection is cleared
        if (Highlighted.IsNone)
        {
            if (!_shift && !_ctrl)
                Window.ClearSelection();
            return;
        }
        // a double-click: the item and what lies within it (a sector's rooms, theirs...) in, whatever its first click
        // did, the rest of the selection kept (Shift or not); Ctrl: all of them out
        if (_double && Within(Highlighted.Index) is { } inner)
        {
            if (_ctrl)
            {
                Selection.Remove(Highlighted.Index);
                Selection.ExceptWith(inner);
            }
            else
            {
                Selection.Add(Highlighted.Index);
                Selection.UnionWith(inner);
            }
            Window.SelectionChanged();
            Window.DisplayStatus(WithinWhat(Highlighted.Index, inner.Count) + " "
                + (_ctrl ? "taken out of the selection" : $"selected ({Selection.Count} selected)"));
            return;
        }
        if (!Selection.Remove(Highlighted.Index))
            Selection.Add(Highlighted.Index);
        Window.SelectionChanged();
    }

    public override void OnEditEnd(Vector2 map)
    {
        // a right click: the properties of the selection, else of the highlighted item (stage 4's dialogs)
        if (Selection.Count > 0 || !Highlighted.IsNone)
            Window.EditProperties(Kind, Selection.Count > 0 ? new List<int>(Selection) : new List<int> { Highlighted.Index });
    }

    public override void OnEndMultiSelection(Vector2 from, Vector2 to, bool subtract)
    {
        var r = new Rect2(from, Vector2.Zero).Expand(to);
        if (!subtract && !_shift)
            Selection.Clear();
        foreach (int i in InRect(r))
            if (subtract)
                Selection.Remove(i);
            else
                Selection.Add(i);
        Window.SelectionChanged();
    }

    /// <summary>The items inside a rectangle (map units).</summary>
    protected abstract IEnumerable<int> InRect(Rect2 r);

    public override void OnCancel()
    {
        if (_dragging)
            CancelDrag();
        _press = Press.None;
        _rect = false;
        View.QueueRedraw();
    }

    public override void OnRedraw(MapView2D v)
    {
        // the selected sectors tinted red under their outlines (easier to see what is selected)
        if (Kind == ElementKind.Sector)
            foreach (int i in Selection)
                v.TintSector(i, new Color(ColorSettings.Selection, 0.28f));
        foreach (int i in Selection)
            DrawElement(v, new Element(Kind, i), ColorSettings.Selection, false);
        if (!Highlighted.IsNone)
            DrawElement(v, Highlighted, Selection.Contains(Highlighted.Index) ? ColorSettings.Selection.Lightened(0.35f) : ColorSettings.Highlight, true);
        if (_rect)
            v.DrawRectangle(_pressMap, _rectTo, ColorSettings.Highlight);
        // moving: where it started to where it is, the locked axis through the start (red X, green Y)
        if (_moving)
        {
            var from = v.MapToScreen(_movRef);
            var to = _movDelta.X == int.MinValue ? from : v.MapToScreen(_movRef + new Vector2(_movDelta.X, _movDelta.Y));
            if (_lock != ' ')
            {
                var axis = _lock == 'x' ? View.GridAxisX : View.GridAxisY;
                float far = View.Units(v.Size.Length());
                v.DrawLine(v.MapToScreen(_movRef - axis * far), v.MapToScreen(_movRef + axis * far),
                    _lock == 'x' ? new Color(1f, 0.35f, 0.35f) : new Color(0.45f, 1f, 0.45f), 1);
            }
            v.DrawLine(from, to, ColorSettings.Highlight, 1);
            v.DrawArc(from, 4, 0, Mathf.Tau, 12, ColorSettings.Highlight, 1.5f);
            if (_falloff)
            {
                v.DrawArc(to, _radius * v.Zoom, 0, Mathf.Tau, 64, new Color(1f, 0.75f, 0.3f, 0.8f), 1);
                foreach (var (p, wt) in _fallWeight)
                    v.DrawRect(new Rect2(v.MapToScreen(new Vector2(Doc.Points[p].X, Doc.Points[p].Y)) - new Vector2(3, 3), new Vector2(6, 6)),
                        new Color(1f, 0.75f, 0.3f, 0.3f + 0.7f * wt));
            }
        }
        if (_scaling)
            DrawScale(v);
        // turning: its middle and the arm the mouse turns
        if (_rotating)
        {
            var p = v.MapToScreen(_pivot);
            v.DrawLine(p, v.MapToScreen(View.Cursor), ColorSettings.Highlight, 1);
            v.DrawArc(p, 6, 0, Mathf.Tau, 16, ColorSettings.Highlight, 2);
            v.DrawLine(p - new Vector2(10, 0), p + new Vector2(10, 0), ColorSettings.Highlight, 1);
            v.DrawLine(p - new Vector2(0, 10), p + new Vector2(0, 10), ColorSettings.Highlight, 1);
        }
    }

    protected virtual void DrawElement(MapView2D v, Element e, Color c, bool highlight)
    {
        switch (e.Kind)
        {
            case ElementKind.Point:
                if (e.Index < Doc.Points.Count)
                    v.DrawPoint(e.Index, c, highlight ? 4 : 3);
                break;
            case ElementKind.Line:
                if (e.Index < Doc.Lines.Count)
                    v.DrawLine(e.Index, c, highlight ? 3 : 2);
                break;
            case ElementKind.Sector:
                if (e.Index < Doc.Sectors.Count)
                    v.DrawSector(e.Index, c, highlight ? 3 : 2);
                break;
            case ElementKind.Sprite:
                if (e.Index < Doc.Sprites.Count)
                    v.DrawSprite(e.Index, c, highlight ? 3 : 2);
                break;
        }
    }
}
