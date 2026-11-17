// Chosen core: original code.
using System;
using System.Collections.Generic;
using System.Linq;
using Chosen.Forge.Config;
using Chosen.Forge.Document;
using Chosen.Shared.Engine.Geometry;
using Godot;

namespace Chosen.Forge.View;

/// <summary>What is highlighted or selected: a point, line, sector or sprite by index.</summary>
public enum ElementKind { None, Point, Line, Sector, Sprite, Light }

public readonly record struct Element(ElementKind Kind, int Index)
{
    public static readonly Element None = new(ElementKind.None, -1);
    public bool IsNone => Kind == ElementKind.None || Index < 0;
}

/// <summary>
/// The 2D map view in Build's orientation (x right, y down, Build units): the grid, walls (white, red pairs, blocking,
/// masked, one-way), the normal tick of each wall toward its own sector, the first wall of each sector, points, sprites
/// (circle facing, bar wall-aligned, square floor-aligned, with their angle) and sector labels; the mode draws its
/// highlight and selection on top (EditMode.OnRedraw). Wheel zooms at the cursor; Space held (UDB's pan view) or the
/// middle button held pans.
/// </summary>
public partial class MapView2D : Control
{
    public MapDocument Doc;
    /// <summary>The map point at the view's centre, and pixels per Build unit.</summary>
    public Vector2 Centre;
    public float Zoom = 0.05f;
    public int GridSize = 64;
    public bool ShowGrid = true;
    /// <summary>The grid drawn over the filled sectors (View > Grid Over Floors / Ceilings) instead of under them:
    /// it shows on the floors in the Floors, Ceilings and Brightness modes.</summary>
    public bool GridOverFill;
    /// <summary>UDB's view mode: lines only, or the sectors filled (brightness, floor or ceiling pictures).</summary>
    public ViewMode Mode = ViewMode.Wireframe;
    /// <summary>Sprites drawn with their pictures under their markers (UDB's things as sprites); an invisible one
    /// (cstat 32768) keeps its marker alone.</summary>
    public bool SpritePictures;
    /// <summary>View > Full Brightness: floors, ceilings and sprites' pictures by no shade (Preview3D.FullBright).</summary>
    public bool FullBright;
    /// <summary>The map's dynamic lights drawn (Lights mode): DrawLights.</summary>
    public bool ShowLights;
    readonly SectorFill2D _fill = new();

    /// <summary>Another layer's (shown, locked) colour: faded into the background.</summary>
    public static Color Dim(Color c) => c.Lerp(ColorSettings.Background, 0.6f);

    /// <summary>The map or the pictures changed: the filled sectors made again.</summary>
    public void MapChanged()
    {
        _fill.Invalidate();
        _tintDirty = true;
        QueueRedraw();
    }

    /// <summary>Sectors tinted over their fill, under the walls (Atmosphere mode: each by its atmosphere's colour; null:
    /// none, or a sector left as it is); TintChanged when the colours change.</summary>
    public Func<int, Color?> SectorTint;

    public void TintChanged()
    {
        _tintDirty = true;
        QueueRedraw();
    }

    readonly List<ArrayMesh> _sectorTints = new();
    bool _tintDirty = true;

    void DrawTints()
    {
        if (SectorTint == null || Doc == null)
            return;
        if (_tintDirty)
        {
            _tintDirty = false;
            _sectorTints.Clear();
            // (the largest first: a room in a room or over one tinted over it; another layer's under the current one's)
            var order = Enumerable.Range(0, Doc.Sectors.Count).Where(s => s < Doc.Areas.Length)
                .OrderBy(s => Doc.SectorEditable(s) ? 1 : 0).ThenByDescending(s => Doc.Areas[s]);
            foreach (int s in order)
            {
                if (!Doc.SectorShown(s) || SectorTint(s) is not { } c)
                    continue;
                var tris = PreviewMesher.Fill(Doc, s);
                if (tris.Count == 0)
                    continue;
                if (!Doc.SectorEditable(s))
                    c = new Color(c.R, c.G, c.B, c.A * 0.4f);
                var v = new Vector2[tris.Count];
                var col = new Color[tris.Count];
                for (int i = 0; i < tris.Count; i++)
                {
                    v[i] = new Vector2((float)tris[i].x, (float)tris[i].y);
                    col[i] = c;
                }
                var arrays = new Godot.Collections.Array();
                arrays.Resize((int)Mesh.ArrayType.Max);
                arrays[(int)Mesh.ArrayType.Vertex] = v;
                arrays[(int)Mesh.ArrayType.Color] = col;
                var mesh = new ArrayMesh();
                mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
                _sectorTints.Add(mesh);
            }
        }
        DrawSetTransformMatrix(new Transform2D(new Vector2(Zoom, 0), new Vector2(0, Zoom), Size * 0.5f - Centre * Zoom));
        foreach (var m in _sectorTints)
            DrawMesh(m, null);
        DrawSetTransformMatrix(Transform2D.Identity);
    }

    /// <summary>Play moved these sectors (and their neighbours): their fills made again.</summary>
    public void SectorsMoved(IEnumerable<int> sectors)
    {
        _fill.Invalidate(sectors);
        QueueRedraw();
    }

    /// <summary>What the view shows besides the map as stored (the moving sectors Off, On, playing): a frame round
    /// the view and a label at its top in that colour; null in Edit.</summary>
    public (string text, Color color)? Banner;
    /// <summary>The moving sectors, outlined in the banner's colour while it shows.</summary>
    public Func<int, bool> Mover;
    public bool Snap = true;
    /// <summary>Draws the mode's highlight and selection (after the map).</summary>
    public Action<MapView2D> DrawOverlay;
    /// <summary>A sprite drawn dim and not highlighted (the sprite filter).</summary>
    public Func<int, bool> SpriteHidden;
    /// <summary>A sprite's marker colour.</summary>
    public Func<int, Color> SpriteColor;
    /// <summary>A sector's label text when zoomed in enough (null: its index).</summary>
    public Func<int, string> SectorLabel;
    /// <summary>What a sector does (Blood.SectorCaption: its line and its effects; null: nothing), drawn boxed in its
    /// place of the label while ShowSectorEffects (View > Show Sector Effects).</summary>
    public Func<int, (string head, string effects)?> SectorCaption;
    public bool ShowSectorEffects = true;
    /// <summary>The cursor in map units (where the mouse is).</summary>
    public Vector2 Cursor { get; private set; }
    public event Action<Vector2> CursorMoved;
    public event Action<InputEvent> MapInput;
    public event Action ViewChanged;

    public const float MinScale = 1f / 1024, MaxScale = 8f;

    bool _panning, _keyPan;
    Vector2 _panFrom, _mouse;

    /// <summary>The pan key (Space) went down or up: while it is held the mouse drags the view. True when it went
    /// down, or came up as a tap (soon, the view not dragged: the window's context menu).</summary>
    public bool SetPanKey(bool down)
    {
        if (down == _keyPan)
            return false;
        _keyPan = down;
        _panFrom = _mouse;
        MouseDefaultCursorShape = down ? CursorShape.Drag : CursorShape.Arrow;
        if (down)
        {
            _panDown = _mouse;
            _panDownAt = Time.GetTicksMsec();
            return true;
        }
        return _mouse.DistanceTo(_panDown) < 4 && Time.GetTicksMsec() - _panDownAt < 400;
    }

    Vector2 _panDown;
    ulong _panDownAt;

    public MapView2D()
    {
        ClipContents = true;
        FocusMode = FocusModeEnum.All;
        MouseFilter = MouseFilterEnum.Stop;
        // (the filled sectors' and the sprites' pictures: Build's pixels, repeated over a floor)
        TextureFilter = TextureFilterEnum.Nearest;
        TextureRepeat = TextureRepeatEnum.Enabled;
    }

    // ---------------------------------------------------------------- coordinates
    public Vector2 MapToScreen(Vector2 m) => (m - Centre) * Zoom + Size * 0.5f;
    public Vector2 MapToScreen(float x, float y) => MapToScreen(new Vector2(x, y));
    public Vector2 ScreenToMap(Vector2 s) => (s - Size * 0.5f) / Zoom + Centre;

    /// <summary>A distance of px pixels in map units.</summary>
    public float Units(float px) => px / Zoom;

    /// <summary>The grid's frame: where it starts and how far it is turned (radians, the way Build's angles turn: y
    /// down). Both 0 for now; snapping and the move mode's axis locks (G, then X or Y) go by it, so a grid moved or
    /// turned later (UDB's grid transform) is followed.</summary>
    public Vector2 GridOrigin = Vector2.Zero;

    /// <summary>A mode taking the wheel (up: true) for itself; true: taken, the view doesn't zoom.</summary>
    public Func<bool, bool, bool> WheelTaken;
    public float GridAngle;

    public Vector2 GridAxisX => new(Mathf.Cos(GridAngle), Mathf.Sin(GridAngle));
    public Vector2 GridAxisY => new(-Mathf.Sin(GridAngle), Mathf.Cos(GridAngle));

    /// <summary>A map point in the grid's frame (along its X and Y axes from its origin), and back.</summary>
    public Vector2 ToGrid(Vector2 m)
    {
        var d = m - GridOrigin;
        return new Vector2(d.Dot(GridAxisX), d.Dot(GridAxisY));
    }

    public Vector2 FromGrid(Vector2 g) => GridOrigin + GridAxisX * g.X + GridAxisY * g.Y;

    /// <summary>A grid frame value on the nearest grid line.</summary>
    public float SnapGridValue(float v) => Mathf.Round(v / GridSize) * GridSize;

    public Vector2 SnapToGrid(Vector2 m)
    {
        if (!Snap)
            return m.Round();
        var g = ToGrid(m);
        var on = FromGrid(new Vector2(SnapGridValue(g.X), SnapGridValue(g.Y)));
        // (an untransformed grid: its lines exactly, as before)
        return GridAngle == 0 && GridOrigin == Vector2.Zero ? new Vector2(SnapGridValue(m.X), SnapGridValue(m.Y)) : on.Round();
    }

    public void ZoomAt(Vector2 screen, float factor)
    {
        var before = ScreenToMap(screen);
        Zoom = Mathf.Clamp(Zoom * factor, MinScale, MaxScale);
        Centre += before - ScreenToMap(screen);
        Changed();
    }

    /// <summary>Zoom to fit once the view has its size (a map opened before the window is laid out).</summary>
    public bool FitPending;

    public override void _Notification(int what)
    {
        if (what == NotificationResized && FitPending && Size.X > 0 && Size.Y > 0)
            ZoomToFit();
    }

    public void ZoomToFit()
    {
        FitPending = false;
        if (Doc == null)
            return;
        var b = Doc.Bounds;
        if (b.Size.X <= 0 || b.Size.Y <= 0 || Size.X <= 0 || Size.Y <= 0)
            return;
        Centre = b.GetCenter();
        Zoom = Mathf.Clamp(Mathf.Min(Size.X / b.Size.X, Size.Y / b.Size.Y) * 0.9f, MinScale, MaxScale);
        Changed();
    }

    public void CentreOn(Vector2 m)
    {
        Centre = m;
        Changed();
    }

    void Changed()
    {
        QueueRedraw();
        ViewChanged?.Invoke();
    }

    // ---------------------------------------------------------------- input
    /// <summary>Every input event the GUI gives the view, before it is handled (the session recorder).</summary>
    public event Action<InputEvent> GuiEvent;
    bool _inGui;

    public override void _GuiInput(InputEvent e)
    {
        GuiEvent?.Invoke(e);
        _inGui = true;
        try
        {
            HandleInput(e);
        }
        finally
        {
            _inGui = false;
        }
    }

    /// <summary>Marks the event handled when it came from the GUI (a replayed one has no GUI dispatch to stop).</summary>
    public void Accept()
    {
        if (_inGui)
            AcceptEvent();
    }

    /// <summary>An input event as the view takes it: from the GUI, or replayed (SessionReplay).</summary>
    public void HandleInput(InputEvent e)
    {
        if (e is InputEventMouseMotion mm)
        {
            if (_panning || _keyPan)
            {
                Centre -= (mm.Position - _panFrom) / Zoom;
                _panFrom = mm.Position;
                Changed();
            }
            _mouse = mm.Position;
            Cursor = ScreenToMap(mm.Position);
            CursorMoved?.Invoke(Cursor);
        }
        else if (e is InputEventMouseButton mb)
        {
            if (mb.Pressed && _inGui)
                GrabFocus();
            // while the pan key is held the buttons don't select or edit
            if (_keyPan && mb.ButtonIndex is MouseButton.Left or MouseButton.Right)
            {
                Accept();
                return;
            }
            // a mode using the wheel itself (the move's falloff radius): not a zoom
            if (mb.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown && mb.Pressed && WheelTaken?.Invoke(mb.ButtonIndex == MouseButton.WheelUp, mb.ShiftPressed) == true)
            {
                Accept();
                return;
            }
            switch (mb.ButtonIndex)
            {
                case MouseButton.WheelUp when mb.Pressed && !mb.CtrlPressed && !mb.ShiftPressed:
                    ZoomAt(mb.Position, 1.25f);
                    Accept();
                    return;
                case MouseButton.WheelDown when mb.Pressed && !mb.CtrlPressed && !mb.ShiftPressed:
                    ZoomAt(mb.Position, 0.8f);
                    Accept();
                    return;
                case MouseButton.Middle:
                    _panning = mb.Pressed;
                    _panFrom = mb.Position;
                    Accept();
                    return;
            }
        }
        MapInput?.Invoke(e);
    }

    public void Pan(Vector2 pixels)
    {
        Centre += pixels / Zoom;
        Changed();
    }

    // ---------------------------------------------------------------- drawing
    readonly List<Vector2> _lines = new();
    readonly List<Color> _lineColors = new();

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), ColorSettings.Background);
        bool over = GridOverFill && Mode != ViewMode.Wireframe && Doc != null;
        if (ShowGrid && !over)
            DrawGrid();
        if (Doc == null)
            return;
        _fill.Draw(this, Doc, Mode);
        DrawTints();
        if (ShowGrid && over)
            DrawGrid(true);
        // (sprites' pictures under the lines, their marks over them: a sector full of tile 0 logic sprites, bricks,
        // doesn't hide its walls)
        DrawSprites(pictures: true);
        DrawWalls();
        DrawPoints();
        DrawSprites(pictures: false);
        if (ShowLights)
            DrawLights();
        DrawLabels();
        DrawOverlay?.Invoke(this);
        // (over the highlight: what it is tied to)
        DrawLinks();
        DrawBanner();
    }

    void DrawBanner()
    {
        if (Banner is not { } b)
            return;
        DrawRect(new Rect2(Vector2.One * 1.5f, Size - Vector2.One * 3), b.color, false, 3);
        var font = ThemeDB.FallbackFont;
        const int size = 14;
        var w = font.GetStringSize(b.text, HorizontalAlignment.Left, -1, size);
        var box = new Rect2(new Vector2((Size.X - w.X) * 0.5f - 10, 6), new Vector2(w.X + 20, size + 10));
        DrawRect(box, new Color(0, 0, 0, 0.75f));
        DrawRect(box, b.color, false, 2);
        DrawString(font, box.Position + new Vector2(10, size + 2), b.text, HorizontalAlignment.Left, -1, size, b.color);
    }

    /// <summary>A grid line's colour over the filled sectors: light and see-through (the dark grid colours vanish on
    /// the pictures), the major lines stronger, the origin's kept.</summary>
    static Color OverFill(Color c, bool over) =>
        !over || c == ColorSettings.Origin ? c : new Color(1, 1, 1, c == ColorSettings.Grid64 ? 0.38f : 0.16f);

    void DrawGrid(bool over = false)
    {
        float step = GridSize * Zoom;
        int major = 8;
        while (step < 12 && step > 0)
        {
            step *= 2;
            major = Math.Max(1, major / 2);
        }
        float g = step / Zoom;
        _lines.Clear();
        _lineColors.Clear();
        // in the grid's frame (GridOrigin, GridAngle): the view's corners there give the lines to draw, each drawn back
        // in the map across the view
        var corners = new[] { Vector2.Zero, new Vector2(Size.X, 0), Size, new Vector2(0, Size.Y) }.Select(c => ToGrid(ScreenToMap(c))).ToArray();
        float gx0 = corners.Min(c => c.X), gx1 = corners.Max(c => c.X), gy0 = corners.Min(c => c.Y), gy1 = corners.Max(c => c.Y);
        for (double x = Math.Floor(gx0 / g) * g; x <= gx1; x += g)
        {
            var c = Math.Abs(Math.Round(x / g) % major) < 0.5 ? ColorSettings.Grid64 : ColorSettings.Grid;
            if (Math.Abs(x) < g * 0.5)
                c = ColorSettings.Origin;
            c = OverFill(c, over);
            Line(MapToScreen(FromGrid(new Vector2((float)x, gy0))), MapToScreen(FromGrid(new Vector2((float)x, gy1))), c);
        }
        for (double y = Math.Floor(gy0 / g) * g; y <= gy1; y += g)
        {
            var c = Math.Abs(Math.Round(y / g) % major) < 0.5 ? ColorSettings.Grid64 : ColorSettings.Grid;
            if (Math.Abs(y) < g * 0.5)
                c = ColorSettings.Origin;
            c = OverFill(c, over);
            Line(MapToScreen(FromGrid(new Vector2(gx0, (float)y))), MapToScreen(FromGrid(new Vector2(gx1, (float)y))), c);
        }
        FlushLines(1);
    }

    void Line(Vector2 a, Vector2 b, Color c)
    {
        _lines.Add(a);
        _lines.Add(b);
        _lineColors.Add(c);
    }

    void FlushLines(float width)
    {
        if (_lines.Count > 0)
            DrawMultilineColors(_lines.ToArray(), _lineColors.ToArray(), width);
        _lines.Clear();
        _lineColors.Clear();
    }

    /// <summary>Does using this wall do something: its own trigger (pushed, shot, touched), or its sector opens when
    /// its walls are pushed (Blood's wall push: doors, secret doors)?</summary>
    bool Used(int wall)
    {
        var w = Doc.Walls[wall];
        if (w.Xw is { } x && (x.TriggerPush != 0 || x.TriggerVector != 0 || x.TriggerTouch != 0))
            return true;
        return w.Sector >= 0 && w.Sector < Doc.Sectors.Count && Doc.Sectors[w.Sector].Xs is { Wallpush: not 0 };
    }

    /// <summary>The colour a line is drawn in (UDB: one-sided bright, two-sided dim; blocking, masked, one-way; one
    /// that does something when used: actions).</summary>
    public Color LineColor(MapLine l)
    {
        var w = Doc.Walls[l.Front];
        int cstat = w.Cstat | (l.Back >= 0 ? Doc.Walls[l.Back].Cstat : 0);
        if (Used(l.Front) || (l.Back >= 0 && Used(l.Back)))
            return ColorSettings.Actions;
        if ((cstat & 32) != 0)
            return ColorSettings.OneWay;
        if ((cstat & 16) != 0)
            return ColorSettings.Masked;
        if (l.Back < 0)
            return ColorSettings.Linedefs;
        return (cstat & 1) != 0 ? ColorSettings.Blocking : ColorSettings.TwoSided;
    }

    /// <summary>A line and the normal tick into its front wall's own sector (Build's sector lies right of a wall, y
    /// down).</summary>
    void WallLine(MapLine l, Color c, float tick)
    {
        var (ax, ay, bx, by) = BuildGeom.WallEnds(Doc, l.Front);
        var a = MapToScreen(ax, ay);
        var b = MapToScreen(bx, by);
        Line(a, b, c);
        var d = b - a;
        float len = d.Length();
        if (len > 12)
        {
            var mid = (a + b) * 0.5f;
            var n = new Vector2(-d.Y, d.X) / len;
            Line(mid, mid + n * tick, c);
        }
    }

    void DrawWalls()
    {
        var view = new Rect2(ScreenToMap(Vector2.Zero), Size / Zoom).Grow(Units(8));
        float tick = 4;
        // another layer's lines first (dimmed), then the current layer's, the ones that do something last, over a
        // wall lying on them (rooms over rooms)
        var mine = new List<(MapLine l, Color c)>();
        var later = new List<(MapLine l, Color c)>();
        foreach (var l in Doc.Lines)
        {
            var (ax, ay, bx, by) = BuildGeom.WallEnds(Doc, l.Front);
            if (Math.Max(ax, bx) < view.Position.X || Math.Min(ax, bx) > view.End.X
                || Math.Max(ay, by) < view.Position.Y || Math.Min(ay, by) > view.End.Y)
                continue;
            if (!Doc.LineShown(l))
                continue;
            var c = LineColor(l);
            // (the moving sectors' walls in the colour of what is shown: Off, On, Play)
            if (Banner is { } bn && Mover != null && (Mover(Doc.Walls[l.Front].Sector) || (l.Back >= 0 && Mover(Doc.Walls[l.Back].Sector))))
                c = bn.color;
            if (!Doc.LineEditable(l))
                WallLine(l, Dim(c), tick);
            else if (c == ColorSettings.Actions)
                later.Add((l, c));
            else
                mine.Add((l, c));
        }
        foreach (var (l, c) in mine.Concat(later))
            WallLine(l, c, tick);
        // the first wall of each sector: a short tick across its start
        if (Zoom > 0.02f)
            for (int si = 0; si < Doc.Sectors.Count; si++)
            {
                var s = Doc.Sectors[si];
                if (s.WallPtr < 0 || s.WallPtr >= Doc.Walls.Count || !Doc.SectorEditable(si))
                    continue;
                var (ax, ay, bx, by) = BuildGeom.WallEnds(Doc, s.WallPtr);
                var a = MapToScreen(ax, ay);
                var d = MapToScreen(bx, by) - a;
                float len = d.Length();
                if (len < 16)
                    continue;
                var u = d / len;
                var p = a + u * 6;
                var n = new Vector2(-u.Y, u.X);
                Line(p - n * 4, p + n * 4, ColorSettings.FirstWall);
            }
        FlushLines(1);
    }

    void DrawPoints()
    {
        if (Zoom < 0.03f)
            return;
        float r = Zoom > 0.15f ? 2.5f : 1.5f;
        var rect = new Rect2(Vector2.Zero, Size).Grow(4);
        for (int i = 0; i < Doc.Points.Count; i++)
        {
            var p = Doc.Points[i];
            var s = MapToScreen(p.X, p.Y);
            // (a hidden layer's not at all, another shown layer's dimmed: Document/Layers.cs)
            if (rect.HasPoint(s) && Doc.PointShown(i))
                DrawRect(new Rect2(s - new Vector2(r, r), new Vector2(r * 2, r * 2)), Doc.PointEditable(i) ? ColorSettings.Vertices : Dim(ColorSettings.Vertices));
        }
    }

    /// <summary>A sprite's marker radius in map units (its clipping distance, at least a few pixels).</summary>
    public float SpriteRadius(int i) => Math.Max(Doc.Sprites[i].ClipDist * 4, Units(4));

    /// <summary>The sprites' pictures (Sprites mode: SpritePictures), or their marks.</summary>
    void DrawSprites(bool pictures)
    {
        if (pictures && !SpritePictures)
            return;
        var rect = new Rect2(Vector2.Zero, Size).Grow(32);
        for (int i = 0; i < Doc.Sprites.Count; i++)
        {
            var sp = Doc.Sprites[i];
            var s = MapToScreen(sp.X, sp.Y);
            if (!rect.HasPoint(s) || !Doc.SpriteShown(i))
                continue;
            bool dim = SpriteHidden != null && SpriteHidden(i);
            var c = SpriteColor?.Invoke(i) ?? Colors.White;
            if (dim)
                c = c.Darkened(0.7f);
            else if (!Doc.SpriteEditable(i))
            {
                c = Dim(c);
                dim = true;
            }
            if (!pictures)
                DrawSprite(i, c, 1);
            else if (!dim && (sp.Cstat & 32768) == 0)
                DrawSpritePicture(i);
        }
    }

    /// <summary>
    /// A sprite's picture as seen from above (UDB's things drawn as sprites): one facing the camera or on a wall
    /// stands up in its full size (xrepeat / 4 Build units a pixel across, yrepeat / 4 up), a floor sprite lies
    /// turned by its angle; through its palette, x-flipped as it is.
    /// </summary>
    void DrawSpritePicture(int i)
    {
        var sp = Doc.Sprites[i];
        var tex = Data.TileImages.Get(sp.Picnum, sp.Pal);
        if (tex == null || sp.XRepeat == 0 || sp.YRepeat == 0)
            return;
        float w = tex.GetWidth() * sp.XRepeat / 4f * Zoom, h = tex.GetHeight() * sp.YRepeat / 4f * Zoom;
        if (w < 2 && h < 2)
            return;
        var s = MapToScreen(sp.X, sp.Y);
        float sx = (sp.Cstat & 4) != 0 ? -1 : 1;
        // (floor-aligned, a sloped one too: turned by its angle; the slope shortens it on the map)
        bool flat = (sp.Cstat & 32) != 0;
        float rot = flat ? sp.Ang * Mathf.Tau / 2048f + Mathf.Pi / 2 : 0;
        float slope = Blood.ModernTypes.Slope(sp) / 4096f;
        float sy = ((sp.Cstat & 8) != 0 && flat ? -1 : 1) / Mathf.Sqrt(1 + slope * slope);
        DrawSetTransformMatrix(new Transform2D(rot, new Vector2(sx, sy), 0, s));
        DrawTextureRect(tex, new Rect2(-w / 2, -h / 2, w, h), false, FullBright ? Colors.White : PreviewMesher.Shade(sp.Shade));
        DrawSetTransformMatrix(Transform2D.Identity);
    }

    /// <summary>The map's dynamic lights (Lights mode): each its reach as a faint circle, a line to the sprite it is
    /// attached to, a bulb in its colour where it shines from.</summary>
    /// <summary>The lights drawn in place of the map's (the light dialog's changes as they are made); null: the map's.</summary>
    public List<Shared.Blood.Map.MapLight> LightsShown;

    void DrawLights()
    {
        var rect = new Rect2(Vector2.Zero, Size);
        var lights = LightsShown ?? Doc.Lights;
        for (int i = 0; i < lights.Count; i++)
        {
            var l = lights[i];
            if (!Doc.LightShown(l) || Doc.LightPlace(l) is not { } p)
                continue;
            var s = MapToScreen((float)p.x, (float)p.y);
            float reach = l.Radius * Zoom;
            if (!rect.Grow(reach + 8).HasPoint(s))
                continue;
            var c = new Color(l.R / 255f, l.G / 255f, l.B / 255f);
            bool editable = i >= Doc.Lights.Count || Doc.LightEditable(i);
            if (reach > 6)
                DrawArc(s, reach, 0, Mathf.Tau, 48, new Color(c, editable ? 0.35f : 0.15f), 1);
            if (l.Attached && l.Sprite < Doc.Sprites.Count)
                DrawLine(MapToScreen(Doc.Sprites[l.Sprite].X, Doc.Sprites[l.Sprite].Y), s, new Color(c, 0.6f), 1);
            DrawCircle(s, 5, editable ? c : Dim(c));
            DrawArc(s, 5, 0, Mathf.Tau, 16, editable ? Colors.Black : Dim(Colors.Black), 1);
        }
    }

    /// <summary>A light's mark in a colour (highlight, selection): a ring round its bulb and its reach.</summary>
    public void DrawLight(int i, Color c, float width)
    {
        var lights = LightsShown ?? Doc.Lights;
        if (i >= lights.Count || Doc.LightPlace(lights[i]) is not { } p)
            return;
        var s = MapToScreen((float)p.x, (float)p.y);
        // (the bulb tinted the mark's colour too: the selection's red, as the other things selected)
        var own = new Color(lights[i].R / 255f, lights[i].G / 255f, lights[i].B / 255f);
        DrawCircle(s, 5, own.Lerp(c, 0.7f));
        DrawArc(s, 5, 0, Mathf.Tau, 16, Colors.Black, 1);
        DrawArc(s, 9, 0, Mathf.Tau, 20, c, width);
        float reach = lights[i].Radius * Zoom;
        if (reach > 6)
            DrawArc(s, reach, 0, Mathf.Tau, 48, new Color(c, 0.7f), 1);
    }

    /// <summary>A sprite's marker: circle (facing), a bar its width (wall-aligned), a square (floor-aligned), with
    /// its angle as an arrow.</summary>
    public void DrawSprite(int i, Color c, float width)
    {
        var sp = Doc.Sprites[i];
        var s = MapToScreen(sp.X, sp.Y);
        float r = Math.Max(SpriteRadius(i) * Zoom, 3);
        float ang = sp.Ang * Mathf.Tau / 2048f;
        var dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
        switch (sp.Cstat & 48)
        {
            case 16:
            {
                var side = new Vector2(-dir.Y, dir.X);
                float half = Math.Max(Data.TileImages.Size(sp.Picnum).X * sp.XRepeat / 8f * Zoom, r);
                DrawLine(s - side * half, s + side * half, c, width + 1);
                break;
            }
            case 32:
                DrawRect(new Rect2(s - new Vector2(r, r), new Vector2(r * 2, r * 2)), c, false, width);
                break;
            case 48:
                // a sloped floor sprite: the square with its rising edge doubled
                DrawRect(new Rect2(s - new Vector2(r, r), new Vector2(r * 2, r * 2)), c, false, width);
                DrawLine(s + dir * r * 0.6f - new Vector2(-dir.Y, dir.X) * r, s + dir * r * 0.6f + new Vector2(-dir.Y, dir.X) * r, c, width);
                break;
            default:
                DrawArc(s, r, 0, Mathf.Tau, 16, c, width);
                break;
        }
        if (r > 4)
            DrawLine(s, s + dir * r * 1.4f, c, width);
    }

    void DrawLabels()
    {
        if (Zoom < 0.04f || SectorLabel == null)
            return;
        var font = ThemeDB.FallbackFont;
        int size = 11;
        var rect = new Rect2(Vector2.Zero, Size);
        for (int s = 0; s < Doc.Sectors.Count; s++)
        {
            var p = MapToScreen(Doc.Labels[s]);
            if (!rect.HasPoint(p) || !Doc.SectorShown(s))
                continue;
            if (ShowSectorEffects && SectorCaption?.Invoke(s) is { } cap)
            {
                DrawCaption(font, p, cap.head, Zoom > 0.08f ? cap.effects : "", Doc.SectorEditable(s));
                continue;
            }
            string t = SectorLabel(s);
            var w = font.GetStringSize(t, HorizontalAlignment.Left, -1, size);
            DrawString(font, p - new Vector2(w.X * 0.5f, -size * 0.35f), t, HorizontalAlignment.Left, -1, size,
                Doc.SectorEditable(s) ? ColorSettings.Label : Dim(ColorSettings.Label));
        }
    }

    /// <summary>A sector's caption, as xmapedit boxes them: its line, what it does under it, centred on p.</summary>
    void DrawCaption(Font font, Vector2 p, string head, string effects, bool editable)
    {
        const int size = 11, pad = 3;
        float wh = font.GetStringSize(head, HorizontalAlignment.Left, -1, size).X;
        float we = effects.Length > 0 ? font.GetStringSize(effects, HorizontalAlignment.Left, -1, size - 1).X : 0;
        float w = Mathf.Max(wh, we), h = size + (effects.Length > 0 ? size + 1 : 0);
        var box = new Rect2(p - new Vector2(w / 2 + pad, h / 2 + pad), new Vector2(w + pad * 2, h + pad * 2));
        float a = editable ? 1f : 0.45f;
        DrawRect(box, new Color(0.08f, 0.08f, 0.1f, 0.78f * a));
        DrawRect(box, new Color(ColorSettings.Label, 0.5f * a), false, 1);
        float y = box.Position.Y + pad + size * 0.85f;
        DrawString(font, new Vector2(p.X - wh / 2, y), head, HorizontalAlignment.Left, -1, size, new Color(ColorSettings.Label, a));
        if (effects.Length > 0)
            DrawString(font, new Vector2(p.X - we / 2, y + size + 1), effects, HorizontalAlignment.Left, -1, size - 1,
                new Color(1f, 0.82f, 0.45f, a));
    }

    // ---------------------------------------------------------------- for the modes' overlays
    public void DrawLine(int line, Color c, float width)
    {
        var (ax, ay, bx, by) = BuildGeom.WallEnds(Doc, Doc.Lines[line].Front);
        DrawLine(MapToScreen(ax, ay), MapToScreen(bx, by), c, width);
    }

    public void DrawPoint(int point, Color c, float r)
    {
        var p = Doc.Points[point];
        var s = MapToScreen(p.X, p.Y);
        DrawRect(new Rect2(s - new Vector2(r, r), new Vector2(r * 2, r * 2)), c);
    }

    /// <summary>A point that isn't one yet (where Vertices mode would put one on a wall): an outlined square.</summary>
    public void DrawGhostPoint(Vector2 map, Color c, float r)
    {
        var s = MapToScreen(map.X, map.Y);
        DrawRect(new Rect2(s - new Vector2(r, r), new Vector2(r * 2, r * 2)), c, false, 1.5f);
    }

    public void DrawSector(int sector, Color c, float width)
    {
        var sec = Doc.Sectors[sector];
        for (int w = sec.WallPtr; w < sec.WallPtr + sec.WallNum && w < Doc.Walls.Count; w++)
        {
            var (ax, ay, bx, by) = BuildGeom.WallEnds(Doc, w);
            DrawLine(MapToScreen(ax, ay), MapToScreen(bx, by), c, width);
        }
    }

    // a sector's triangles (PreviewMesher.Fill) by its walls' points: made again when they change
    readonly Dictionary<int, (long key, Vector2[] tris)> _tints = new();

    /// <summary>A sector filled with a see-through colour (the selected sectors' red tint).</summary>
    public void TintSector(int sector, Color c)
    {
        if (sector < 0 || sector >= Doc.Sectors.Count)
            return;
        var sec = Doc.Sectors[sector];
        if (sec.WallNum < 3 || sec.WallPtr < 0 || sec.WallPtr + sec.WallNum > Doc.Walls.Count)
            return;
        long key = sec.WallPtr * 7919L + sec.WallNum;
        for (int w = sec.WallPtr; w < sec.WallPtr + sec.WallNum; w++)
            key = key * 31 + Doc.Walls[w].X * 65537L + Doc.Walls[w].Y + Doc.Walls[w].Point2 * 257L;
        if (!_tints.TryGetValue(sector, out var t) || t.key != key)
        {
            var fill = PreviewMesher.Fill(Doc, sector);
            t = (key, fill.Select(p => new Vector2((float)p.Item1, (float)p.Item2)).ToArray());
            _tints[sector] = t;
        }
        if (t.tris.Length < 3)
            return;
        var pts = new Vector2[t.tris.Length];
        for (int i = 0; i < pts.Length; i++)
            pts[i] = MapToScreen(t.tris[i]);
        var cols = new Color[pts.Length];
        Array.Fill(cols, c);
        RenderingServer.CanvasItemAddTriangleArray(GetCanvasItem(), Enumerable.Range(0, pts.Length - pts.Length % 3).ToArray(), pts, cols);
    }

    public void DrawRectangle(Vector2 a, Vector2 b, Color c)
    {
        var r = new Rect2(MapToScreen(a), Vector2.Zero).Expand(MapToScreen(b));
        DrawRect(r, c, false, 1);
    }
}
