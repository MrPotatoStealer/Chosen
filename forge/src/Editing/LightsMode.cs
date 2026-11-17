// Chosen core: original code. Lights mode: the map's dynamic lights (Document/Lights.cs), as Sprites mode the sprites.
using System.Collections.Generic;
using System.Linq;
using Chosen.Forge.Config;
using Chosen.Forge.View;
using Chosen.Shared.Blood.Map;
using Chosen.Shared.Engine.Geometry;
using Godot;

namespace Chosen.Forge.Editing;

/// <summary>
/// Lights mode: each light a bulb in its colour where it shines from, its reach a circle round it, a line to the sprite
/// it is attached to. A left click selects, a drag moves (a point light's place, an attached one's offset from its
/// sprite: it still follows the sprite); a right click on empty space places a point light there at the room's middle
/// height (held and moved, it follows; released, its properties open), on a light its properties; Insert places one at
/// the cursor; Delete takes the selected (else the highlighted) away; Ctrl+C copies them (Ctrl+X cuts), Ctrl+V places
/// the copy. A map without lights of its own gets them here (the game then gives it those alone). In the 3D preview
/// (Preview3DLights.cs) each is a bulb: clicked selected, held moved with the crosshair, the wheel raising it.
/// </summary>
public class LightsMode : ClassicMode
{
    public override string Name => "Lights";
    public override string Action => "lightsmode";
    protected override ElementKind Kind => ElementKind.Light;
    protected override bool DrawsOnEdit => false;

    int _placing = -1;

    public override void OnEngage()
    {
        View.ShowLights = true;
        View.QueueRedraw();
        Window.LightMarkers(true);
        base.OnEngage();
    }

    public override void OnDisengage()
    {
        _placing = -1;
        View.ShowLights = false;
        View.QueueRedraw();
        Window.LightMarkers(false);
        base.OnDisengage();
    }

    Vector2I Snapped(Vector2 map)
    {
        var at = View.Snap && !ForgeInput.Shift ? View.SnapToGrid(map) : map.Round();
        return new Vector2I((int)at.X, (int)at.Y);
    }

    /// <summary>A new point light at (x, y), half way up the room there (the caller took the undo level): white-warm,
    /// a lamp's reach and energy, casting shadows. Its index.</summary>
    public static int NewLight(Document.MapDocument d, int x, int y)
    {
        var l = new MapLight { Kind = LightKind.Point, X = x, Y = y, R = 255, G = 210, B = 160, Radius = 200 * 16, Energy = 1 };
        l.Z = MiddleHeight(d, x, y);
        d.SettleLight(l);
        return d.AddLight(l);
    }

    /// <summary>Half way between the floor and ceiling of the editable sector at (x, y) (else any there; else 0).</summary>
    public static int MiddleHeight(Document.MapDocument d, int x, int y)
    {
        var here = d.EditableSectorsAt(x, y);
        if (here.Count == 0)
            here = d.SectorsAt(x, y);
        if (here.Count == 0)
            return 0;
        int s = here.OrderBy(i => i < d.Areas.Length ? d.Areas[i] : double.MaxValue).First();
        BuildGeom.GetZsOfSlope(d, s, x, y, out double c, out double f);
        return (int)System.Math.Round((c + f) / 2);
    }

    public override void OnEditBegin(Vector2 map)
    {
        if (!Highlighted.IsNone || Doc == null)
            return;
        var at = Snapped(map);
        Window.Undo.CreateUndo("Place light");
        _placing = NewLight(Doc, at.X, at.Y);
        Doc.NotifyLightsChanged();
        Highlight(new Element(ElementKind.Light, _placing));
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
        var l = Doc.Lights[_placing];
        if (l.X == at.X && l.Y == at.Y)
            return;
        l.X = at.X;
        l.Y = at.Y;
        l.Z = MiddleHeight(Doc, at.X, at.Y);
        Doc.SettleLight(l);
        Doc.NotifyLightsChanged();
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

    void Landed(int i)
    {
        Selection.Clear();
        Selection.Add(i);
        Window.SelectionChanged();
        Window.UndoChanged();
        Window.DisplayStatus($"placed light {i}");
        Window.EditProperties(ElementKind.Light, new List<int> { i }, true);
    }

    public override void OnInsert()
    {
        var at = Snapped(View.Cursor);
        Window.Undo.CreateUndo("Place light");
        int i = NewLight(Doc, at.X, at.Y);
        Doc.NotifyLightsChanged();
        Highlight(new Element(ElementKind.Light, i));
        Landed(i);
    }

    /// <summary>A light's place in the 2D view (null: its sprite is gone).</summary>
    Vector2? At(int i) => Doc.LightPlace(i) is { } p ? new Vector2((float)p.x, (float)p.y) : null;

    protected override Element Pick(Vector2 m)
    {
        if (Doc == null)
            return Element.None;
        var near = new List<(int, double)>();
        double r = View.Units(HighlightRange);
        for (int i = 0; i < Doc.Lights.Count; i++)
        {
            if (!Doc.LightShown(i) || !Doc.LightEditable(i) || At(i) is not { } p)
                continue;
            double d = m.DistanceTo(p);
            if (d <= System.Math.Max(r, View.Units(7)))
                near.Add((i, d));
        }
        return PickAmong(ElementKind.Light, near);
    }

    protected override IEnumerable<int> InRect(Rect2 r)
    {
        for (int i = 0; i < Doc.Lights.Count; i++)
            if (Doc.LightShown(i) && Doc.LightEditable(i) && At(i) is { } p && r.HasPoint(p))
                yield return i;
    }

    public override List<int> CopyLights(List<int> items) => items;

    List<int> Targets() => Selection.Count > 0 ? Selection.OrderBy(i => i).ToList()
        : Highlighted.IsNone ? new List<int>() : new List<int> { Highlighted.Index };

    public override void OnDelete()
    {
        var t = Targets();
        if (t.Count == 0)
        {
            Window.DisplayStatus("delete: no light selected or under the cursor");
            return;
        }
        Window.Undo.CreateUndo(t.Count == 1 ? "Delete light" : $"Delete {t.Count} lights");
        Doc.DeleteLights(t);
        Selection.Clear();
        Highlighted = Element.None;
        Doc.NotifyLightsChanged();
        Window.UndoChanged();
        Window.DisplayStatus($"deleted {t.Count} light(s)");
    }

    // ---------------------------------------------------------------- dragging: the lights' places (their own drag:
    // ClassicMode's moves points and sprites)
    List<int> _drag;
    Vector2I[] _from;
    Vector2 _grab, _ref;
    Vector2I _delta;

    public override void OnDragStart(Vector2 map, bool select)
    {
        var items = !Highlighted.IsNone && Selection.Contains(Highlighted.Index) ? Selection.ToList() : new List<int> { Highlighted.Index };
        items = items.Where(i => i >= 0 && i < Doc.Lights.Count && At(i) != null).ToList();
        if (items.Count == 0)
        {
            _drag = null;
            return;
        }
        Window.Undo.CreateUndo(items.Count == 1 ? "Drag light" : $"Drag {items.Count} lights");
        _drag = items;
        _from = items.Select(i => { var p = At(i).Value; return new Vector2I((int)System.Math.Round(p.X), (int)System.Math.Round(p.Y)); }).ToArray();
        int g = items.Contains(Highlighted.Index) ? items.IndexOf(Highlighted.Index) : 0;
        _ref = _from[g];
        _grab = map;
        _delta = Vector2I.Zero;
    }

    public override void OnDragMove(Vector2 map)
    {
        if (_drag == null)
            return;
        var to = _ref + (map - _grab);
        var placed = View.Snap && !ForgeInput.Shift ? View.SnapToGrid(to) : to.Round();
        var d = new Vector2I((int)(placed.X - _ref.X), (int)(placed.Y - _ref.Y));
        if (d == _delta)
            return;
        _delta = d;
        for (int k = 0; k < _drag.Count; k++)
            Doc.MoveLight(_drag[k], _from[k].X + d.X, _from[k].Y + d.Y);
        Doc.NotifyLightsChanged();
        Window.DisplayStatus($"moved {Units.Xy(d.X)}, {Units.Xy(d.Y)}");
    }

    public override void OnDragStop(Vector2 map)
    {
        if (_drag == null)
            return;
        if (_delta == Vector2I.Zero)
            Window.Undo.WithdrawUndo();
        else
        {
            Doc.NotifyLightsChanged();
            Window.UndoChanged();
        }
        _drag = null;
    }

    protected override void OnDragCancelled()
    {
        if (_drag == null)
            return;
        if (_delta != Vector2I.Zero)
            Window.Undo.Revert();
        else
            Window.Undo.WithdrawUndo();
        _drag = null;
    }

    protected override void DrawElement(MapView2D v, Element e, Color c, bool highlight)
    {
        if (e.Kind == ElementKind.Light && e.Index < Doc.Lights.Count)
            v.DrawLight(e.Index, c, highlight ? 3 : 2);
    }
}
