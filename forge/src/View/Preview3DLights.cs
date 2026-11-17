// Chosen core: original code. The map's dynamic lights in the 3D preview, as the game adds them over Build's shading;
// in Lights mode each a bulb that can be picked, selected and held to move it.
using System;
using System.Collections.Generic;
using System.Linq;
using Chosen.Shared.Blood.Map;
using Godot;

namespace Chosen.Forge.View;

public partial class Preview3D
{
    /// <summary>View > Dynamic Lights (3D): the map's own lights shown (Document/Lights.cs; a map without its own: the
    /// ones the game would make from its sprites, so the preview looks as the game will).</summary>
    public bool ShowLights = true;
    /// <summary>View > Dynamic Light Shadows (3D): the nearest lights that cast shadows do.</summary>
    public bool LightShadows = true;
    /// <summary>Each of the map's own lights drawn as a small bulb of its colour where it shines from (Lights mode):
    /// picked by the crosshair, selected by a click, held to move it, the wheel raising it.</summary>
    public bool LightMarkers;
    /// <summary>The lights shown in place of the map's (the light dialog's changes as they are made: a copy of the
    /// list, the map's indices); null: the map's.</summary>
    public List<MapLight> LightsShown;
    /// <summary>Lights held and moved with the crosshair, let go: the lights, and the move (Build x, y, z; z down).</summary>
    public Action<List<int>, int, int, int> MoveLights;

    /// <summary>The most lights shown at once (the nearest the camera), and of them the most casting shadows.</summary>
    const int MaxLights = 96, MaxShadows = 12;
    /// <summary>The game's lights' energy over the faces (DynamicLights.EnergyScale), times the preview's scale to the
    /// power of the falloff: Godot's falls off with the distance itself, and a game unit here is 1 where the game's is
    /// 1/32 (DynamicLights' OmniAttenuation 1.5).</summary>
    const float Attenuation = 1.5f;
    static readonly float EnergyScale = 2.5f * Mathf.Pow(32f, Attenuation);
    /// <summary>The bulb icon's size on the screen (Sprite3D's fixed-size pixel size) and the angle (radians, about) its
    /// half takes as seen: picking and the rings round it go by it.</summary>
    const float IconPixel = 0.00126f, IconHalf = 0.0147f;

    readonly List<(OmniLight3D light, bool shadows)> _lights = new();
    /// <summary>The map's own lights' nodes by index: the omni light (null: dark, or lights off) and the bulb (null:
    /// not in Lights mode), and where it shines from (the preview's space).</summary>
    readonly Dictionary<int, (OmniLight3D omni, Sprite3D bulb, Vector3 at)> _lightOf = new();
    /// <summary>Each bulb's own colour (the selected ones are tinted red over it).</summary>
    readonly Dictionary<int, Color> _bulbColour = new();

    /// <summary>The selected lights' bulbs tinted the selection's red, as the selected surfaces and sprites are; the
    /// others their own colour (each time the marks are drawn).</summary>
    void TintBulbs()
    {
        foreach (var (i, e) in _lightOf)
        {
            if (e.bulb == null || !_bulbColour.TryGetValue(i, out var own))
                continue;
            var want = Selected.Contains(new VisualTarget(VisualPart.Light, -1, -1, -1, i)) && !MarksHidden
                ? own.Lerp(Config.ColorSettings.Selection, 0.7f) : own;
            if (e.bulb.Modulate != want)
                e.bulb.Modulate = want;
        }
    }
    double _lightsTimer;
    Vector3 _lightsFrom = new(float.NaN, 0, 0);

    /// <summary>The lights made again with the world (Rebuild): each an omni light where it shines from, its colour,
    /// reach and brightness as the game's; none with Full Brightness. In Lights mode the map's own each with its bulb
    /// (dark ones too: dimmed).</summary>
    void BuildLights()
    {
        _lights.Clear();
        _lightOf.Clear();
        _bulbColour.Clear();
        _lightsFrom = new Vector3(float.NaN, 0, 0);
        if (Doc == null)
            return;
        bool lit = !FullBright && ShowLights;
        // (a map without lights of its own: the game's from its sprites, so a .MAP shows as it plays)
        var list = Doc.HasLights ? LightsShown ?? Doc.Lights : lit ? LightRules.Generate(Doc.Map) : new List<MapLight>();
        for (int i = 0; i < list.Count; i++)
        {
            var l = list[i];
            if (Doc.HasLights && !Doc.LightShown(l))
                continue;
            if (Doc.LightPlace(l) is not { } at)
                continue;
            if (l.Attached && Doc.SpritesGone != null && Doc.SpritesGone.Contains(l.Sprite))
                continue;
            var c = new Color(l.R / 255f, l.G / 255f, l.B / 255f);
            var p = PreviewMesher.P(at.x, at.y, at.z);
            // (dark as the level starts, as the game has it: a torch not lit, a lamp not showing its tile, a hidden sprite)
            bool shines = LightRules.ShinesAtStart(l, l.Attached ? Doc.Sprites[l.Sprite] : null);
            OmniLight3D o = null;
            if (lit && shines)
            {
                // (kept out of the rooms sharing its own room's place: neither lit nor shadowing, RoomMask)
                uint rooms = RoomMask(l.Attached ? (l.Sprite < Doc.Sprites.Count ? Doc.Sprites[l.Sprite].SectNum : -1) : Doc.LightSector(l));
                o = new OmniLight3D
                {
                    Position = p, LightColor = c, LightEnergy = l.Energy * EnergyScale,
                    OmniRange = l.Radius / 16f, OmniAttenuation = Attenuation, LightSpecular = 0, Visible = false,
                    ShadowEnabled = false, OmniShadowMode = OmniLight3D.ShadowMode.Cube, LightCullMask = rooms, ShadowCasterMask = rooms,
                    // (the fog and the ground mist know no render layers: kept to its room, it glows in none, or it lit the
                    // other room's mist)
                    LightVolumetricFogEnergy = rooms == 0xFFFFFFFFu ? 1f : 0f,
                };
                _world.AddChild(o);
                _lights.Add((o, l.Shadows));
            }
            Sprite3D bulb = null;
            if (LightMarkers && Doc.HasLights)
            {
                // a light-bulb icon in the light's colour, outlined black, the same size on the screen near and far;
                // drawn over everything, the sprites a lamp's bulb sits in too (hidden behind the walls and floors by
                // Occluded, not by the depth: BulbsSeen); a dark one (not shining as the level starts) dimmed
                bulb = new Sprite3D
                {
                    Texture = BulbIcon, Position = p, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, FixedSize = true,
                    PixelSize = IconPixel, NoDepthTest = true, Shaded = false, AlphaCut = SpriteBase3D.AlphaCutMode.Disabled,
                    TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                    Modulate = shines ? c.Lightened(0.15f) : c.Darkened(0.55f),
                    // (the glass's middle where the light is: the screw below it)
                    Offset = new Vector2(0, -IconGlassBelowMiddle),
                };
                _world.AddChild(bulb);
                _bulbColour[i] = bulb.Modulate;
            }
            if (Doc.HasLights)
                _lightOf[i] = (o, bulb, p);
        }
        UpdateLights(true);
    }

    /// <summary>The lights alone made again (a light moved or changed; the dialog's preview), the meshes kept.</summary>
    public void RefreshLights()
    {
        foreach (var (o, _) in _lights)
            o.QueueFree();
        foreach (var (_, b, _) in _lightOf.Values)
            b?.QueueFree();
        _held = null;
        BuildLights();
        DrawMarks();
    }

    /// <summary>The map's lights changed and nothing else: made again now when showing, else with the rest when shown.</summary>
    public void LightsChanged()
    {
        if (Visible && !_dirty)
            RefreshLights();
        else
            _dirty = true;
    }

    /// <summary>Where the map's light i shines from as drawn (the preview's space), if it is drawn.</summary>
    Vector3? LightAt(int i) => _lightOf.TryGetValue(i, out var e) ? e.at : null;

    /// <summary>The nearest lights to the camera shown, the nearest of those that cast shadows casting them (again when
    /// the camera has moved a little: a shadow's map is drawn again only when it changes).</summary>
    void UpdateLights(bool now = false)
    {
        if (_lights.Count == 0)
            return;
        var eye = _cam.GlobalPosition;
        if (!now && eye.DistanceSquaredTo(_lightsFrom) < 16 * 16)
            return;
        _lightsFrom = eye;
        var order = Enumerable.Range(0, _lights.Count)
            .OrderBy(i => _lights[i].light.Position.DistanceSquaredTo(eye) / Mathf.Max(1, _lights[i].light.OmniRange * _lights[i].light.OmniRange))
            .ToList();
        int shadowed = 0;
        for (int k = 0; k < order.Count; k++)
        {
            var (o, casts) = _lights[order[k]];
            bool shown = k < MaxLights;
            if (o.Visible != shown)
                o.Visible = shown;
            bool shadow = shown && LightShadows && casts && shadowed < MaxShadows;
            if (shadow)
                shadowed++;
            if (o.ShadowEnabled != shadow)
                o.ShadowEnabled = shadow;
        }
    }

    /// <summary>Each frame: the lights held moved with the crosshair, the lights kept to the nearest (a few times a
    /// second).</summary>
    void LightsFrame(double delta)
    {
        HoldFrame();
        _lightsTimer += delta;
        if (_lightsTimer < 0.25)
            return;
        _lightsTimer = 0;
        UpdateLights();
    }

    // ---------------------------------------------------------------- rooms sharing a place

    /// <summary>The render layers of the rooms sharing a place (Shared.Blood.Map.LightRules.RoomColours, as the game's):
    /// from 4, sixteen.</summary>
    const int RoomBit = 4, RoomColourCount = 16;
    int[] _roomColours;
    List<int>[] _roomOverlaps, _roomNeighbours;
    /// <summary>The map's shape the rooms were worked out for (its walls and sectors: made again when it changes).</summary>
    long _roomsShape = long.MinValue;
    Document.MapDocument _roomsDoc;

    /// <summary>The rooms sharing a place coloured (a sector's meshes take its colour as their render layer, a light
    /// its room's mask), again only when the map's shape changed.</summary>
    void Rooms()
    {
        long shape = Doc.Sectors.Count;
        foreach (var w in Doc.Walls)
            shape = shape * 1000003 + w.X * 31 + w.Y * 17 + w.Point2 * 7 + w.NextSector;
        foreach (var s in Doc.Sectors)
            shape = shape * 1000003 + s.WallPtr * 13 + s.WallNum;
        if (shape == _roomsShape && _roomsDoc == Doc && _roomColours != null)
            return;
        _roomsShape = shape;
        _roomsDoc = Doc;
        var (overlaps, neighbours) = Blood.AutoLayers.Rooms(Doc);
        _roomOverlaps = overlaps;
        _roomNeighbours = neighbours;
        _roomColours = LightRules.RoomColours(Doc.Sectors.Count, s => overlaps[s], s => neighbours[s], RoomColourCount);
    }

    uint RoomLayer(int s) => LightRules.RoomLayer(_roomColours != null && s >= 0 && s < _roomColours.Length ? _roomColours[s] : 0, RoomBit);

    uint RoomMask(int s) => _roomOverlaps == null ? 0xFFFFFFFFu : LightRules.RoomMask(s, _roomColours, o => _roomOverlaps[o], RoomBit);

    // ---------------------------------------------------------------- the icon

    /// <summary>The icon's size (pixels), and how far above its middle the glass's middle is.</summary>
    const int IconSize = 64;
    const float IconGlassBelowMiddle = 7;
    static ImageTexture _bulbIcon;

    /// <summary>A light bulb (white glass, a grey screw with its threads) with a thick black outline, drawn once
    /// (smoothed: 4 x 4 samples a pixel); Modulate gives the glass its light's colour, the outline stays black.</summary>
    static ImageTexture BulbIcon => _bulbIcon ??= MakeBulbIcon();

    static ImageTexture MakeBulbIcon()
    {
        const float Cx = 32, Cy = 32 - IconGlassBelowMiddle, R = 16, Line = 3.5f;
        // the shape grown by d: the glass (a circle), its neck and the screw
        static bool Glass(float x, float y, float d) => (x - Cx) * (x - Cx) + (y - Cy) * (y - Cy) <= (R + d) * (R + d);
        static bool Neck(float x, float y, float d)
        {
            // narrowing from the glass to the screw
            if (y < Cy + 6 - d || y > 46 + d)
                return false;
            float half = 11 - (y - (Cy + 6)) * 0.15f;
            return Math.Abs(x - Cx) <= half + d;
        }
        static bool Screw(float x, float y, float d) => Math.Abs(x - Cx) <= 8.5f + d && y >= 45 - d && y <= 56 + d
            || Math.Abs(x - Cx) <= 4 + d && y >= 55 - d && y <= 59.5f + d;
        var img = Image.CreateEmpty(IconSize, IconSize, false, Image.Format.Rgba8);
        for (int py = 0; py < IconSize; py++)
            for (int px = 0; px < IconSize; px++)
            {
                float r = 0, g = 0, b = 0, a = 0;
                for (int sy = 0; sy < 4; sy++)
                    for (int sx = 0; sx < 4; sx++)
                    {
                        float x = px + (sx + 0.5f) / 4, y = py + (sy + 0.5f) / 4;
                        Color s;
                        if (Glass(x, y, 0) || Neck(x, y, 0))
                            s = Colors.White;
                        else if (Screw(x, y, 0))
                            // (its threads: dark lines across it)
                            s = (int)(y - 45) % 4 == 3 ? Colors.Black : new Color(0.62f, 0.62f, 0.62f);
                        else if (Glass(x, y, Line) || Neck(x, y, Line) || Screw(x, y, Line))
                            s = Colors.Black;
                        else
                            continue;
                        r += s.R;
                        g += s.G;
                        b += s.B;
                        a += 1;
                    }
                if (a > 0)
                    img.SetPixel(px, py, new Color(r / a, g / a, b / a, a / 16));
            }
        img.GenerateMipmaps();
        return ImageTexture.CreateFromImage(img);
    }

    // ---------------------------------------------------------------- picking and holding the bulbs

    /// <summary>The bulb the crosshair's ray (from o along g, a unit) passes by, of those seen (drawn over the sprites:
    /// one in a lamp is picked through its picture; behind a wall, floor or ceiling not): the light and how far; a bulb
    /// is picked a little round it, more so far off. The bulbs' sight is worked out here first (BulbsSeen).</summary>
    (int light, float t)? PickLight(Vector3 o, Vector3 g)
    {
        if (!LightMarkers || _lightOf.Count == 0)
            return null;
        BulbsSeen(o);
        (int, float)? best = null;
        float bestT = float.MaxValue;
        foreach (var (i, e) in _lightOf)
        {
            if (e.bulb == null || !e.bulb.Visible || i >= Doc.Lights.Count || !Doc.LightEditable(i))
                continue;
            var v = e.at - o;
            float t = v.Dot(g);
            if (t <= 0)
                continue;
            float r = Mathf.Max(0.3f, t * IconHalf * 1.6f);
            if ((v - g * t).LengthSquared() > r * r || t >= bestT)
                continue;
            bestT = t;
            best = (i, t);
        }
        return best;
    }

    /// <summary>Each bulb shown where the camera (o) sees its place: no wall, floor or ceiling of the sectors seen
    /// between (sprites and masked walls don't hide it); a held one always.</summary>
    void BulbsSeen(Vector3 o)
    {
        foreach (var (i, e) in _lightOf)
        {
            if (e.bulb == null)
                continue;
            bool seen = (_held != null && _held.Contains(i)) || !Occluded(o, e.at);
            if (e.bulb.Visible != seen)
                e.bulb.Visible = seen;
        }
    }

    /// <summary>A wall, floor or ceiling between o and p (the preview's space): the seen sectors' planes from inside
    /// and their walls' solid parts from their sector's side, as the crosshair picks them (Pick).</summary>
    bool Occluded(Vector3 o, Vector3 p)
    {
        // Build space: z down, 16 z a unit; the segment's t from 0 (o) to 1 (p), a little short of p
        double cx = o.X * 16, cy = o.Z * 16, cz = -o.Y * 256;
        var d = p - o;
        double dx = d.X * 16, dy = d.Z * 16, dz = -d.Y * 256;
        const double End = 0.995;
        IEnumerable<int> sectors = _seenSectors ?? (IEnumerable<int>)Enumerable.Range(0, Doc.Sectors.Count);
        foreach (int s in sectors)
        {
            if (s < 0 || s >= Doc.Sectors.Count || !Doc.SectorShown(s))
                continue;
            var sec = Doc.Sectors[s];
            if (sec.WallNum < 3)
                continue;
            foreach (bool ceiling in new[] { false, true })
            {
                var (a, b, c) = PlaneOf(s, ceiling);
                double den = dz - a * dx - b * dy;
                if (Math.Abs(den) < 1e-9 || (ceiling ? den > 0 : den < 0))
                    continue;
                double t = (a * cx + b * cy + c - cz) / den;
                if (t > 0 && t < End && Shared.Engine.Geometry.BuildGeom.Inside(Doc, s, cx + dx * t, cy + dy * t))
                    return true;
            }
            for (int w = sec.WallPtr; w < sec.WallPtr + sec.WallNum; w++)
            {
                var (x1, y1, x2, y2) = Shared.Engine.Geometry.BuildGeom.WallEnds(Doc, w);
                double ex = x2 - x1, ey = y2 - y1;
                if ((cx - x1) * -ey + (cy - y1) * ex <= 0)
                    continue;
                double den = dx * ey - dy * ex;
                if (Math.Abs(den) < 1e-12)
                    continue;
                double t = ((x1 - cx) * ey - (y1 - cy) * ex) / den;
                double u = ((x1 - cx) * dy - (y1 - cy) * dx) / den;
                if (t <= 0 || t >= End || u < 0 || u > 1)
                    continue;
                double hx = cx + dx * t, hy = cy + dy * t, hz = cz + dz * t;
                Shared.Engine.Geometry.BuildGeom.GetZsOfSlope(Doc, s, hx, hy, out double ceil, out double floor);
                int ns = Doc.Walls[w].NextSector;
                if (ns < 0 || ns >= Doc.Sectors.Count)
                {
                    if (hz >= ceil && hz <= floor)
                        return true;
                    continue;
                }
                Shared.Engine.Geometry.BuildGeom.GetZsOfSlope(Doc, ns, hx, hy, out double nc, out double nf);
                bool skies = (sec.CeilingStat & 1) != 0 && (Doc.Sectors[ns].CeilingStat & 1) != 0;
                if (!skies && hz >= ceil && hz < nc || hz > nf && hz <= floor)
                    return true;
            }
        }
        return false;
    }

    /// <summary>The lights held (a click on a bulb held down: the selection when the bulb is in it), where each was,
    /// the one grabbed, how far ahead it is held, and from where on the crosshair's ray.</summary>
    List<int> _held;
    /// <summary>A light is held (testing).</summary>
    internal bool Holding => _held != null;
    Dictionary<int, Vector3> _heldFrom;
    VisualTarget _heldTarget;
    float _heldDistance;
    Vector3 _heldGrab, _heldMove;
    bool _heldMoved;

    /// <summary>A press on a bulb: it (with the selection when it is in it) held; let go without moving, a click.</summary>
    void StartHold(VisualTarget t)
    {
        var items = Selected.Contains(t) ? Selected.Where(s => s.IsLight).Select(s => s.Light).ToList() : new List<int> { t.Light };
        items = items.Where(i => LightAt(i) != null).Distinct().ToList();
        if (LightAt(t.Light) is not { } at || items.Count == 0)
            return;
        var o = _cam.GlobalPosition;
        var g = -_cam.GlobalTransform.Basis.Z;
        _held = items;
        _heldFrom = items.ToDictionary(i => i, i => LightAt(i).Value);
        _heldTarget = t;
        _heldDistance = Mathf.Max(2, (at - o).Dot(g));
        _heldGrab = at - (o + g * _heldDistance);
        _heldMove = Vector3.Zero;
        _heldMoved = false;
    }

    /// <summary>The wheel while holding: the lights pushed away or pulled near (game units).</summary>
    void PushHeld(float by) => _heldDistance = Mathf.Max(2, _heldDistance + by);

    /// <summary>Each frame while holding: the lights where the crosshair holds them (the one grabbed as far ahead as it
    /// was, the others kept beside it), their light and bulb moved there (the map once let go).</summary>
    void HoldFrame()
    {
        if (_held == null)
            return;
        var o = _cam.GlobalPosition;
        var g = -_cam.GlobalTransform.Basis.Z;
        var move = o + g * _heldDistance + _heldGrab - _heldFrom[_heldTarget.Light];
        if (!_heldMoved && move.Length() < 0.5f)
            return;
        _heldMoved = true;
        if (move.IsEqualApprox(_heldMove))
            return;
        _heldMove = move;
        foreach (int i in _held)
            if (_lightOf.TryGetValue(i, out var e))
            {
                var p = _heldFrom[i] + move;
                if (e.omni != null)
                    e.omni.Position = p;
                if (e.bulb != null)
                    e.bulb.Position = p;
                _lightOf[i] = (e.omni, e.bulb, p);
            }
        DrawMarks();
        var (dx, dy, dz) = BuildMove(move);
        Says?.Invoke($"moving {(_held.Count == 1 ? $"light {_held[0]}" : $"{_held.Count} lights")} by {Config.Units.Xy(dx)}, {Config.Units.Xy(dy)}, "
            + $"z {Config.Units.Z(dz)} (the wheel pushes it away or pulls it near; let go to put it there)");
    }

    /// <summary>A move in the preview's space as Build's (x, y, z down).</summary>
    static (int dx, int dy, int dz) BuildMove(Vector3 m) =>
        ((int)Math.Round(m.X * 16), (int)Math.Round(m.Z * 16), (int)Math.Round(-m.Y * 256));

    /// <summary>The press let go: moved, the lights put there (MoveLights: a command); else a click on the bulb.</summary>
    void EndHold()
    {
        if (_held == null)
            return;
        var (held, moved, t) = (_held, _heldMoved, _heldTarget);
        var (dx, dy, dz) = BuildMove(_heldMove);
        _held = null;
        if (!moved)
        {
            if (Click != null)
                Click(t);
            else
                ToggleSelected(t);
        }
        else if (dx != 0 || dy != 0 || dz != 0)
            MoveLights?.Invoke(held, dx, dy, dz);
        // (put back where the map has them unless the command moved them: it made them again)
        if (moved)
            RefreshLights();
    }

    /// <summary>A hold given up (the preview left, the mouse let go): the lights back where they are.</summary>
    void CancelHold()
    {
        if (_held == null)
            return;
        bool moved = _heldMoved;
        _held = null;
        if (moved)
            RefreshLights();
    }

    /// <summary>A light's mark: a ring round its bulb facing the camera (lines into the marks mesh, colour c); how many
    /// vertices.</summary>
    int LightRing(int i, Color c, float scale)
    {
        // (none round a bulb a wall hides)
        if (LightAt(i) is not { } at || _lightOf.TryGetValue(i, out var e) && e.bulb is { Visible: false })
            return 0;
        var b = _cam.GlobalTransform.Basis;
        float r = Mathf.Max(0.2f, (at - _cam.GlobalPosition).Length() * IconHalf * scale);
        int n = 0;
        const int Segments = 16;
        for (int k = 0; k < Segments; k++)
        {
            float a0 = k * Mathf.Tau / Segments, a1 = (k + 1) * Mathf.Tau / Segments;
            _markMesh.SurfaceSetColor(c);
            _markMesh.SurfaceAddVertex(at + (b.X * Mathf.Cos(a0) + b.Y * Mathf.Sin(a0)) * r);
            _markMesh.SurfaceSetColor(c);
            _markMesh.SurfaceAddVertex(at + (b.X * Mathf.Cos(a1) + b.Y * Mathf.Sin(a1)) * r);
            n += 2;
        }
        return n;
    }
}
