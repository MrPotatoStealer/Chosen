// Chosen core: original code (UDB's visual mode as a preview: the map in 3D, flown through).
using System;
using System.Collections.Generic;
using System.Linq;
using Chosen.Forge.Data;
using Chosen.Forge.Document;
using Chosen.Shared.Engine.Geometry;
using Godot;

namespace Chosen.Forge.View;

/// <summary>
/// The 3D preview (Q, as the visual mode key): the map's faces (PreviewMesher) and sprites (facing ones turn to the
/// camera about the vertical, wall- and floor-aligned ones lie as their angle says) in their own world, seen from a
/// camera flown with the mouse (captured) and W A S D (along the view: look up or down to rise or sink; Shift faster). It starts where the cursor was
/// in 2D, at eye height over the floor there; Q or Esc goes back. Rebuilt when the map changes while it shows.
/// </summary>
public partial class Preview3D : SubViewportContainer
{
    public MapDocument Doc;
    /// <summary>Back to 2D (the window's visual mode command).</summary>
    public Action Leave;
    /// <summary>A key the preview doesn't use, for the window's actions (save, undo, test map...).</summary>
    public Func<InputEventKey, bool> OtherKey;

    SubViewport _vp;
    Camera3D _cam;
    Node3D _world;
    Label _help;
    float _yaw, _pitch;
    bool _dirty;
    readonly Dictionary<(int, PreviewMesher.Kind, bool, bool, int), Material> _materials = new();

    /// <summary>Build units of eye height over the floor (Blood's player: about 46 game units).</summary>
    const double Eye = 46 * 256;

    public Preview3D()
    {
        Stretch = true;
        Visible = false;
        FocusMode = FocusModeEnum.All;
        MouseFilter = MouseFilterEnum.Stop;
    }

    public override void _Ready()
    {
        _vp = new SubViewport { OwnWorld3D = true, HandleInputLocally = false, Msaa3D = Viewport.Msaa.Msaa2X };
        AddChild(_vp);
        _env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = new Color(0.16f, 0.18f, 0.26f),
        };
        _vp.AddChild(new WorldEnvironment { Environment = _env });
        // Blood's view: 90 degrees across at 4:3, wider on a wide window (the height's angle kept)
        _cam = new Camera3D { Fov = 73.74f, KeepAspect = Camera3D.KeepAspectEnum.Height, Near = 0.5f, Far = 20000 };
        _vp.AddChild(_cam);
        _world = new Node3D();
        _vp.AddChild(_world);
        _help = new Label { Text = Help, Position = new Vector2(8, 6) };
        _help.AddThemeColorOverride("font_shadow_color", Colors.Black);
        AddChild(_help);
        // what is shown besides the map as stored (Off, On, Play): a frame round the view, a label at its top
        _frame = new Panel { MouseFilter = MouseFilterEnum.Ignore, Visible = false };
        _frame.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_frame);
        _banner = new Label { MouseFilter = MouseFilterEnum.Ignore, Visible = false, HorizontalAlignment = HorizontalAlignment.Center };
        _banner.SetAnchorsPreset(LayoutPreset.CenterTop);
        _banner.GrowHorizontal = GrowDirection.Both;
        // (under the help line)
        _banner.OffsetTop = 30;
        _banner.AddThemeFontSizeOverride("font_size", 15);
        AddChild(_banner);
        // what the crosshair is on, and the selection (Preview3DEdit.cs)
        MakeMarks();
    }

    Panel _frame;
    Label _banner;

    /// <summary>The frame and label of what is shown (null: the map as stored, none).</summary>
    public void SetBanner((string text, Color color)? b)
    {
        _frame.Visible = _banner.Visible = b != null;
        if (b is not { } v)
            return;
        _frame.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = Colors.Transparent, BorderColor = v.color, BorderWidthLeft = 3, BorderWidthTop = 3, BorderWidthRight = 3,
            BorderWidthBottom = 3, DrawCenter = false,
        });
        _banner.Text = v.text;
        _banner.AddThemeColorOverride("font_color", v.color);
        _banner.AddThemeStyleboxOverride("normal", new StyleBoxFlat
        {
            BgColor = new Color(0, 0, 0, 0.75f), BorderColor = v.color, BorderWidthLeft = 2, BorderWidthTop = 2, BorderWidthRight = 2,
            BorderWidthBottom = 2, ContentMarginLeft = 10, ContentMarginRight = 10, ContentMarginTop = 3, ContentMarginBottom = 3,
        });
        float bw = _banner.GetCombinedMinimumSize().X;
        _banner.OffsetLeft = -bw / 2;
        _banner.OffsetRight = bw / 2;
    }

    /// <summary>Shows the preview from map point at (2D cursor), facing as last time, in sector prefer when it holds
    /// the point (rooms sharing the place: the one being worked on), else in one holding it.</summary>
    public void Enter(Vector2 at, int prefer = -1)
    {
        Visible = true;
        Selected.Clear();
        Locked = null;
        Rebuild();
        int s = prefer >= 0 && prefer < Doc.Sectors.Count && BuildGeom.Inside(Doc, prefer, at.X, at.Y) ? prefer : BuildGeom.UpdateSector(Doc, at.X, at.Y, -1);
        if (s < 0 && Doc.Sectors.Count > 0)
        {
            // outside every sector: over the first sector's middle
            s = 0;
            var (x0, y0, x1, y1) = BuildGeom.SectorBounds(Doc, 0);
            at = new Vector2((x0 + x1) * 0.5f, (y0 + y1) * 0.5f);
        }
        double z = 0;
        if (s >= 0)
        {
            BuildGeom.GetZsOfSlope(Doc, s, at.X, at.Y, out double c, out double f);
            z = Math.Max(f - Eye, c + 4 * 256);
        }
        _cam.Position = PreviewMesher.P(at.X, at.Y, z);
        // the camera starts in that sector and stays in it while it holds the camera (CameraSector)
        _camSector = s;
        (_camX, _camY) = (at.X, at.Y);
        Aim();
        GrabFocus();
        Chosen.Forge.Editing.ForgeInput.Capture();
    }

    public void Exit()
    {
        CancelHold();
        Visible = false;
        if (Input.MouseMode == Input.MouseModeEnum.Captured)
            Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    const string Help = "3D preview: mouse looks, W A S D move (where you look), Shift faster; click selects, wheel raises / lowers, C clears, right click: texture, Ctrl+C / Ctrl+V: copy / paste texture, arrows: move it, keypad: repeats and shade, T: settings, Ctrl+A: align, I: place a sprite, H: slope edges, K: lights, Delete: delete sprites, Space: menu; Q or Esc: back to 2D";
    const string LightsHelp = "3D preview, Lights mode (K: back): W A S D move; click a bulb selects, hold it to move it (wheel: nearer / farther), wheel raises / lowers, arrows move, right click: its settings, I: place a light, Delete, Ctrl+C / Ctrl+V: copy / paste lights; Q or Esc: back to 2D";

    /// <summary>The help line for Lights mode (its keys) or the others.</summary>
    public void ShowLightsHelp(bool on)
    {
        if (_help != null)
            _help.Text = on ? LightsHelp : Help;
    }

    /// <summary>The map changed: rebuilt now when showing, else when shown.</summary>
    public void MapChanged()
    {
        _dirty = true;
        _guessDirty = true;
        if (Visible)
            Rebuild();
    }

    void Aim() => _cam.Rotation = new Vector3(_pitch, _yaw, 0);

    // ---------------------------------------------------------------- the world

    public void Rebuild()
    {
        _dirty = false;
        foreach (var c in _world.GetChildren())
            if (c != _marks && c != _tint && c != _plane)
                c.QueueFree();
        _sectorMesh.Clear();
        _spriteNodes.Clear();
        _spriteNodeOf.Clear();
        if (Doc == null)
            return;
        ApplyVisibility();
        Sky();
        _links = Blood.AutoLayers.Links(Doc);
        // the map as given: as stored (Edit), or the shown copy with the moving sectors at Off or On (Blood.SectorMotion)
        var m = new PreviewMesher(Doc) { PanFrames = _panFrames, PanOn = PanOn };
        m.Build();
        _panSectors = Blood.PanMotion.Sectors(Doc, PanOn);
        Rooms();
        AddMeshes(m);
        _wallSpritesAt = null;
        for (int i = 0; i < Doc.Sprites.Count; i++)
            Sprite(i);
        BuildLights();
        if (ShowSectorEffects)
            for (int s = 0; s < Doc.Sectors.Count; s++)
                Caption(s);
        BuildAtmosphere();
    }

    /// <summary>What each sector does (Blood.SectorCaption) floating in its room (View > Show Sector Effects (3D)).</summary>
    public bool ShowSectorEffects = true;

    /// <summary>Sector s's caption over its label point, a third of the way down from its ceiling: hidden by the walls
    /// in front of it, faded out far away.</summary>
    void Caption(int s)
    {
        if (!Doc.SectorShown(s) || Blood.SectorCaption.Of(Doc, s) is not { } cap)
            return;
        var at = Doc.Labels[s];
        BuildGeom.GetZsOfSlope(Doc, s, at.X, at.Y, out double cz, out double fz);
        var label = new Label3D
        {
            Text = cap.effects.Length > 0 ? cap.head + "\n" + cap.effects : cap.head,
            Position = PreviewMesher.P(at.X, at.Y, cz + (fz - cz) / 3),
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, FixedSize = true, PixelSize = 0.0009f,
            FontSize = 26, OutlineSize = 8, Modulate = new Color(1f, 0.85f, 0.5f), OutlineModulate = new Color(0, 0, 0, 0.85f),
            Shaded = false, DoubleSided = true, VisibilityRangeEnd = 320, VisibilityRangeEndMargin = 60,
            VisibilityRangeFadeMode = GeometryInstance3D.VisibilityRangeFadeModeEnum.Self,
        };
        _world.AddChild(label);
        // (shown as a sprite is: only where its sector is seen through the openings, Preview3DPortals)
        _spriteNodes.Add((label, s));
    }

    /// <summary>The mesher's faces as a mesh a sector (a surface a picture): each drawn where it is seen (Portals).</summary>
    void AddMeshes(PreviewMesher m)
    {
        var meshes = new Dictionary<int, ArrayMesh>();
        foreach (var ((tile, kind, oneSided, wall, pal, sector), part) in m.Parts.OrderBy(p => p.Key.kind))
        {
            if (part.V.Count == 0)
                continue;
            var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = part.V.ToArray();
            arrays[(int)Mesh.ArrayType.TexUV] = part.Uv.ToArray();
            arrays[(int)Mesh.ArrayType.Color] = part.C.ToArray();
            // flat normals, for the dynamic lights (a triangle's front: clockwise as Godot draws it)
            var v = part.V;
            var n = new Vector3[v.Count];
            for (int t = 0; t + 2 < v.Count; t += 3)
            {
                var nn = (v[t + 2] - v[t]).Cross(v[t + 1] - v[t]);
                nn = nn.LengthSquared() > 0 ? nn.Normalized() : Vector3.Up;
                n[t] = n[t + 1] = n[t + 2] = nn;
            }
            arrays[(int)Mesh.ArrayType.Normal] = n;
            if (!meshes.TryGetValue(sector, out var mesh))
                meshes[sector] = mesh = new ArrayMesh();
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
            mesh.SurfaceSetMaterial(mesh.GetSurfaceCount() - 1, MaterialFor(tile, kind, oneSided, wall, pal));
        }
        foreach (var (sector, mesh) in meshes)
        {
            // (both sides cast shadows from the lights, as the game's world: a one-sided wall seen from behind too)
            // (its room's render layer: a light of a room sharing its place neither lights nor is shadowed by it)
            var mi = new MeshInstance3D { Mesh = mesh, CastShadow = GeometryInstance3D.ShadowCastingSetting.DoubleSided, Layers = RoomLayer(sector) };
            _world.AddChild(mi);
            _sectorMesh[sector] = mi;
        }
    }

    /// <summary>Some sectors' faces made again and some sprites placed again (Play: what moved this frame).</summary>
    void Remake(IEnumerable<int> sectors, IEnumerable<int> sprites)
    {
        var set = sectors.ToHashSet();
        foreach (int s in set)
            if (_sectorMesh.Remove(s, out var mi))
                mi.QueueFree();
        var m = new PreviewMesher(Doc) { PanFrames = _panFrames, PanOn = PanOn };
        m.Build(set);
        AddMeshes(m);
        _wallSpritesAt = null;
        foreach (int i in sprites)
        {
            if (_spriteNodeOf.Remove(i, out var node))
            {
                _spriteNodes.RemoveAll(n => n.node == node);
                node.QueueFree();
            }
            Sprite(i);
        }
    }

    /// <summary>Each sprite's node (a sprite not drawn: none).</summary>
    readonly Dictionary<int, Node3D> _spriteNodeOf = new();

    /// <summary>oneSided: seen from the front only (a wall's quad turns clockwise seen from its own sector, Godot's
    /// front).</summary>
    Material MaterialFor(int tile, PreviewMesher.Kind kind, bool oneSided, bool wall, int pal)
    {
        if (_materials.TryGetValue((tile, kind, oneSided, wall, pal), out var mat) && IsInstanceValid(mat))
            return mat;
        var tex = wall ? TileImages.Wall(tile, pal) : TileImages.Get(tile, pal);
        // (the faces' shader: Preview3DPortals.cs, clipped to where the sector is seen)
        var m = new ShaderMaterial { Shader = FaceShader(kind, oneSided) };
        m.SetShaderParameter("albedo", tex ?? _white);
        m.SetShaderParameter("tint", tex == null ? new Color(0.45f, 0.45f, 0.45f) : Colors.White);
        m.SetShaderParameter("vis_rate", VisRate);
        m.SetShaderParameter("full_bright", FullBright ? 1f : 0f);
        _materials[(tile, kind, oneSided, wall, pal)] = m;
        return m;
    }

    /// <summary>Build's darkening with distance in shades a unit (the map's visibility / 65536: the game's
    /// build_visibility x 16, BuildShading).</summary>
    float VisRate => Doc == null || AtmosphereOn ? 0f : Doc.Map.Visibility / 65536f;

    /// <summary>The faces' materials told the map's visibility (Map Options changed it, another map) and Full
    /// Brightness.</summary>
    void ApplyVisibility()
    {
        float rate = VisRate;
        foreach (var mat in _materials.Values)
            if (IsInstanceValid(mat) && mat is ShaderMaterial sm)
            {
                sm.SetShaderParameter("vis_rate", rate);
                sm.SetShaderParameter("full_bright", FullBright ? 1f : 0f);
            }
    }

    /// <summary>View > Full Brightness (UDB's Toggle Full Brightness): the lighting left out (the shades of faces,
    /// sprites and the sky, the darkening with distance; the lights to come too), to work in a dark map. The view
    /// only: the map keeps its shades.</summary>
    public bool FullBright;

    // ---------------------------------------------------------------- the sky

    Godot.Environment _env;
    ShaderMaterial _skyMat;
    string _skyKey;

    /// <summary>The parallax sky round the view, as Build lays it out: the map's sky segments (1 &lt;&lt; sky bits of
    /// them round the circle) side by side, segment i the sky tile plus the map's offset i; the tile the most sky
    /// ceilings use, at the row their y panning puts at eye level, shaded by their commonest shade. Without sky
    /// ceilings (or pictures): the plain background.</summary>
    void Sky()
    {
        var count = new Dictionary<(int tile, int ypan, int shade), int>();
        foreach (var s in Doc.Sectors)
            if ((s.CeilingStat & 1) != 0)
            {
                var k = (s.CeilingPicnum, s.CeilingYPanning, s.CeilingShade);
                count[k] = count.GetValueOrDefault(k) + 1;
            }
        (int tile, int ypan, int shade) sky = (-1, 0, 0);
        int best = 0;
        foreach (var (k, n) in count)
            if (n > best)
                (sky, best) = (k, n);
        var map = Doc.Map;
        int bits = Math.Clamp(map.PSkyBits, 0, 5), segs = 1 << bits;
        string key = $"{sky.tile}/{sky.ypan}/{sky.shade}/{FullBright}/{bits}/{string.Join(",", map.SkyOfs)}/{ForgeData.Folder}";
        if (key == _skyKey)
            return;
        _skyKey = key;
        var pano = sky.tile >= 0 ? Panorama(sky.tile, segs, map.SkyOfs) : null;
        if (pano == null)
        {
            _env.BackgroundMode = Godot.Environment.BGMode.Color;
            return;
        }
        if (_skyMat == null)
        {
            _skyMat = new ShaderMaterial { Shader = new Shader { Code = SkyShader } };
            _env.Sky = new Godot.Sky { SkyMaterial = _skyMat, ProcessMode = Godot.Sky.ProcessModeEnum.Realtime };
        }
        int w = pano.GetWidth(), h = pano.GetHeight();
        // the row at eye level: half the height, panned by y panning / 256 of the height's power of two
        int pow = 1;
        while (pow < h)
            pow <<= 1;
        double horizon = h / 2.0 + (sbyte)sky.ypan * pow / 256.0;
        _skyMat.SetShaderParameter("pano", ImageTexture.CreateFromImage(pano));
        _skyMat.SetShaderParameter("horizon", (float)(horizon / h));
        // square pixels at the horizon: the panorama's width is the full circle
        _skyMat.SetShaderParameter("rise", (float)(w / (2 * Math.PI) / h));
        _skyMat.SetShaderParameter("brightness", FullBright ? 1f : PreviewMesher.Shade(sky.shade).R);
        _env.BackgroundMode = Godot.Environment.BGMode.Sky;
    }

    static Image Panorama(int tile, int segs, short[] offsets)
    {
        var first = TileImages.ImageOf(tile);
        if (first == null)
            return null;
        int w = first.GetWidth(), h = first.GetHeight();
        var pano = Image.CreateEmpty(w * segs, h, false, Image.Format.Rgba8);
        for (int i = 0; i < segs; i++)
        {
            var seg = TileImages.ImageOf(tile + (i < offsets.Length ? offsets[i] : 0));
            // a segment of another size (a wide sky tile with no strips after it): the first tile round the circle
            if (seg == null || seg.GetWidth() != w || seg.GetHeight() != h)
                return first;
            pano.BlitRect(seg, new Rect2I(0, 0, w, h), new Vector2I(i * w, 0));
        }
        return pano;
    }

    const string SkyShader = @"shader_type sky;
uniform sampler2D pano : source_color, filter_nearest, repeat_enable;
uniform float horizon = 0.5;
uniform float rise = 1.0;
uniform float brightness = 1.0;
void sky() {
    vec3 d = normalize(EYEDIR);
    // Build's angle round the circle (x east, z south here), the height by the tangent above the horizon
    float u = atan(d.z, d.x) / 6.2831853;
    float t = d.y / max(length(d.xz), 0.0001);
    float v = clamp(horizon - t * rise, 0.0, 0.9999);
    COLOR = texture(pano, vec2(fract(u), v)).rgb * brightness;
}";

    /// <summary>Forgets the materials (the Blood folder changed: other pictures).</summary>
    public void ClearMaterials() => _materials.Clear();

    // the wall sprites by their point and angle (made when first asked for after the sprites changed)
    Dictionary<(int x, int y, int ang), List<int>> _wallSpritesAt;

    /// <summary>How many wall sprites lie in sprite i's plane at its point with a higher number (it is drawn over them).</summary>
    int CoplanarAbove(int i, Chosen.Shared.Blood.Map.BSprite sp)
    {
        if (_wallSpritesAt == null)
        {
            _wallSpritesAt = new Dictionary<(int, int, int), List<int>>();
            for (int k = 0; k < Doc.Sprites.Count; k++)
            {
                var o = Doc.Sprites[k];
                if ((o.Cstat & 48) != 16 || (o.Cstat & 32768) != 0)
                    continue;
                var key = (o.X, o.Y, o.Ang & 2047);
                if (!_wallSpritesAt.TryGetValue(key, out var list))
                    _wallSpritesAt[key] = list = new List<int>();
                list.Add(k);
            }
        }
        int n = 0;
        if (_wallSpritesAt.TryGetValue((sp.X, sp.Y, sp.Ang & 2047), out var at))
            foreach (int k in at)
                if (k > i)
                    n++;
        return n;
    }

    void Sprite(int i)
    {
        var sp = Doc.Sprites[i];
        // (invisible ones, the game draws none: faint, to be found and picked; ShowInvisible off: not at all)
        bool invisible = (sp.Cstat & 32768) != 0;
        if ((invisible && !ShowInvisible) || sp.XRepeat == 0 || sp.YRepeat == 0 || !Doc.SpriteShown(i))
            return;
        var tex = TileImages.Get(sp.Picnum, sp.Pal);
        if (tex == null)
            return;
        // a texel xrepeat / 64 game units wide, yrepeat / 64 high (64: one game unit)
        var s3 = new Sprite3D
        {
            Texture = tex, PixelSize = sp.XRepeat / 64f, Shaded = false,
            // shaded as Blood draws sprites: with their sector's floor (Shade is linear light, as the world's vertex
            // colours; Sprite3D takes Modulate as sRGB and decodes it: encoded, else the curve is applied twice)
            Modulate = invisible ? new Color(1, 1, 1, 0.35f) : FullBright ? Colors.White : PreviewMesher.Shade(PreviewMesher.SpriteShade(Doc, sp)).LinearToSrgb(),
            AlphaCut = invisible ? SpriteBase3D.AlphaCutMode.Disabled : SpriteBase3D.AlphaCutMode.Discard,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
            FlipH = (sp.Cstat & 4) != 0, FlipV = (sp.Cstat & 8) != 0, DoubleSided = true,
            Scale = new Vector3(1, sp.YRepeat / (float)sp.XRepeat, 1),
        };
        bool centred = (sp.Cstat & 128) != 0;
        int align = sp.Cstat & 48;
        float a = sp.Ang * Mathf.Pi / 1024f;
        if (align == 32 || align == 48)
        {
            // floor-aligned: lying flat, turned by its angle; a sloped one (both alignment bits, the slope in its
            // offsets: 4096 for 45 degrees) tilted up the way it faces
            float tilt = Mathf.Atan(Blood.ModernTypes.Slope(sp) / 4096f);
            s3.Rotation = new Vector3(-Mathf.Pi / 2 + tilt, -a - Mathf.Pi / 2, 0);
            s3.Scale = new Vector3(1, sp.YRepeat / (float)sp.XRepeat, 1);
        }
        else
        {
            // as Build places them: z the bottom (real-centred: the middle), raised by the tile's and the sprite's
            // y offset (yrepeat * 4 z a pixel); the middle moved by their x offset to the sprite's left (xrepeat / 4
            // Build units a pixel); x-flipped (and a wall sprite y-flipped) the offsets turn too
            var t = ForgeData.Tiles?[sp.Picnum];
            int offX = (t?.XOffset ?? 0) + sp.XOffset, offY = (t?.YOffset ?? 0) + sp.YOffset;
            if ((sp.Cstat & 4) != 0)
                offX = -offX;
            if (align == 16 && (sp.Cstat & 8) != 0)
                offY = -offY;
            double h = tex.GetHeight() * sp.YRepeat * 4.0;
            double bottom = sp.Z - offY * sp.YRepeat * 4.0 + (centred ? h / 2 : 0);
            s3.Position = PreviewMesher.P(sp.X, sp.Y, bottom - h / 2);
            // a wall sprite on a wall flickered with it (their depths alike): lifted off it to its sector's side, as
            // the game lifts its depth (a quarter of a game unit)
            if (align == 16)
            {
                double nx = Math.Cos(a), ny = Math.Sin(a);
                double side = BuildGeom.Inside(Doc, sp.SectNum, sp.X + nx * 8, sp.Y + ny * 8) ? 1
                    : BuildGeom.Inside(Doc, sp.SectNum, sp.X - nx * 8, sp.Y - ny * 8) ? -1 : 0;
                s3.Position += new Vector3((float)(nx * side * Lift / 16), 0, (float)(ny * side * Lift / 16));
                // wall sprites in one plane (at one point, turned alike: a picture on its backing, Eviction's fan on
                // the black board in its vent box): Build draws the lower numbered over the others, so it stands that
                // much further out the way it faces (an eighth of a game unit each)
                int over = CoplanarAbove(i, sp);
                if (over > 0)
                    s3.Position += new Vector3((float)(nx * over * Lift / 32), 0, (float)(ny * over * Lift / 32));
            }
            // (the sprite's own x axis: along a wall sprite, across the view for one facing it)
            s3.Offset = new Vector2(-offX, 0);
            if (align == 16)
                s3.Rotation = new Vector3(0, Mathf.Atan2(Mathf.Cos(a), Mathf.Sin(a)), 0);
            else
                s3.Billboard = BaseMaterial3D.BillboardModeEnum.FixedY;
            LitSprite(s3, tex, invisible, align != 16, sp.SectNum);
            _world.AddChild(s3);
            _spriteNodes.Add((s3, sp.SectNum));
            _spriteNodeOf[i] = s3;
            MarkShade(s3, sp, invisible);
            return;
        }
        // a floor sprite on a floor or a ceiling: lifted off it (the nearer of the two) into the room
        double z = sp.Z;
        if (sp.SectNum >= 0 && sp.SectNum < Doc.Sectors.Count)
        {
            BuildGeom.GetZsOfSlope(Doc, sp.SectNum, sp.X, sp.Y, out double cz, out double fz);
            z += Math.Abs(sp.Z - fz) <= Math.Abs(sp.Z - cz) ? -Lift * 16 : Lift * 16;
        }
        s3.Position = PreviewMesher.P(sp.X, sp.Y, z);
        LitSprite(s3, tex, invisible, false, sp.SectNum);
        _world.AddChild(s3);
        _spriteNodes.Add((s3, sp.SectNum));
        _spriteNodeOf[i] = s3;
        MarkShade(s3, sp, invisible);
    }

    /// <summary>
    /// A sprite lit by the dynamic lights as the game lights them (its shaders' light(): the picture as it is lit, over
    /// Blood's shade as the emission, as the faces): its own material (SpriteShader: the picture, the shade from its
    /// Modulate, turned to the camera about the vertical in the shader for a facing one: Sprite3D's own billboard is its
    /// material's), its room's render layer (rooms sharing a place: another's lights keep off it), no shadow (the game's
    /// pictures cast none). An invisible one (drawn faint) stays as it is.
    /// </summary>
    void LitSprite(Sprite3D s3, Texture2D tex, bool invisible, bool facing, int sector)
    {
        s3.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        if (invisible)
            return;
        if (!_spriteMaterials.TryGetValue((tex, facing), out var m) || !IsInstanceValid(m))
        {
            _spriteShader ??= new Shader { Code = SpriteShader };
            m = new ShaderMaterial { Shader = _spriteShader };
            m.SetShaderParameter("albedo", tex);
            m.SetShaderParameter("billboard", facing);
            _spriteMaterials[(tex, facing)] = m;
        }
        s3.MaterialOverride = m;
        s3.Layers = RoomLayer(sector);
    }

    readonly Dictionary<(Texture2D, bool), ShaderMaterial> _spriteMaterials = new();
    static Shader _spriteShader;

    /// <summary>The sprites' shader: the picture (its clear pixels cut away) lit by the lights, Blood's shade (the
    /// vertex colour: Sprite3D's Modulate, sRGB) the emission; billboard: turned to the camera about the vertical,
    /// keeping its scale (Godot's fixed-Y billboard).</summary>
    const string SpriteShader = @"shader_type spatial;
render_mode ambient_light_disabled, specular_disabled, cull_disabled;
uniform sampler2D albedo : source_color, filter_nearest;
uniform bool billboard = false;
void vertex() {
    if (billboard) {
        MODELVIEW_MATRIX = VIEW_MATRIX * mat4(vec4(normalize(cross(vec3(0.0, 1.0, 0.0), MAIN_CAM_INV_VIEW_MATRIX[2].xyz)), 0.0),
            vec4(0.0, 1.0, 0.0, 0.0), vec4(normalize(cross(MAIN_CAM_INV_VIEW_MATRIX[0].xyz, vec3(0.0, 1.0, 0.0))), 0.0), MODEL_MATRIX[3]);
        MODELVIEW_MATRIX = MODELVIEW_MATRIX * mat4(vec4(length(MODEL_MATRIX[0].xyz), 0.0, 0.0, 0.0), vec4(0.0, length(MODEL_MATRIX[1].xyz), 0.0, 0.0),
            vec4(0.0, 0.0, length(MODEL_MATRIX[2].xyz), 0.0), vec4(0.0, 0.0, 0.0, 1.0));
        MODELVIEW_NORMAL_MATRIX = mat3(MODELVIEW_MATRIX);
    }
}
void fragment() {
    vec4 t = texture(albedo, UV);
    if (t.a < 0.5)
        discard;
    vec3 s = COLOR.rgb;
    vec3 shade = mix(pow((s + vec3(0.055)) / vec3(1.055), vec3(2.4)), s / vec3(12.92), lessThan(s, vec3(0.04045)));
    ALBEDO = t.rgb;
    EMISSION = t.rgb * shade;
}";

    /// <summary>A sprite's shade and its sector's visibility kept on its node, for its darkening with distance
    /// (DarkenSprite, each frame it is seen).</summary>
    void MarkShade(Sprite3D s3, Chosen.Shared.Blood.Map.BSprite sp, bool invisible)
    {
        if (invisible)
            return;
        s3.SetMeta("shade", PreviewMesher.SpriteShade(Doc, sp));
        s3.SetMeta("secvis", sp.SectNum >= 0 && sp.SectNum < Doc.Sectors.Count ? Doc.Sectors[sp.SectNum].Visibility & 255 : 0);
    }

    /// <summary>A seen sprite's brightness: its shade plus Build's darkening at its depth (as the faces' shader).</summary>
    void DarkenSprite(Node3D node)
    {
        if (node is not Sprite3D s3 || !s3.HasMeta("shade"))
            return;
        if (FullBright)
        {
            s3.Modulate = Colors.White;
            return;
        }
        float rate = VisRate;
        int sv = (int)s3.GetMeta("secvis");
        if (sv > 0)
            rate *= ((sv + 16) & 255) / 16f;
        float depth = Mathf.Max(0, (s3.GlobalPosition - _cam.GlobalPosition).Dot(-_cam.GlobalTransform.Basis.Z));
        float s = (int)s3.GetMeta("shade") + depth * rate;
        float b = Mathf.Pow(Mathf.Clamp(1f - s / 64f, 0f, 1f), 2.2f);
        s3.Modulate = new Color(b, b, b).LinearToSrgb();
    }

    /// <summary>Build units a wall or floor sprite is lifted off the surface it lies on (a quarter of a game unit
    /// across; z: 16 Build z a Build unit).</summary>
    const double Lift = 4;

    // ---------------------------------------------------------------- flying

    public override void _Input(InputEvent e)
    {
        if (!Visible || Input.MouseMode != Input.MouseModeEnum.Captured)
            return;
        if (e is InputEventMouseMotion mm)
        {
            _yaw -= mm.Relative.X * 0.0035f;
            _pitch = Mathf.Clamp(_pitch - mm.Relative.Y * 0.0035f, -1.5f, 1.5f);
            Aim();
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _GuiInput(InputEvent e)
    {
        // the mouse held: a click selects what the crosshair is on, the wheel raises and lowers (Preview3DEdit.cs)
        if (e is InputEventMouseButton mb && Input.MouseMode == Input.MouseModeEnum.Captured && EditInput(mb))
            AcceptEvent();
        else if (e is InputEventMouseButton { Pressed: true } && DisplayServer.GetName() != "headless")
        {
            // a click takes the mouse back (after Alt+Tab or a dialog)
            Chosen.Forge.Editing.ForgeInput.Capture();
            GrabFocus();
            AcceptEvent();
        }
        else if (e is InputEventKey k)
        {
            if (k.Pressed && !k.Echo && k.Keycode is Key.Escape or Key.Q && !k.CtrlPressed && !k.AltPressed)
                Leave?.Invoke();
            // (held keys repeat: the window takes only the nudging keys' repeats, arrows and keypad)
            // (PrtScr's release too: Windows sends that alone)
            else if ((k.Pressed || k.Keycode == Key.Print) && !Moving(k.Keycode))
                OtherKey?.Invoke(k);
            // the flying keys (and every other key) stop here: none reaches the 2D modes' bindings
            AcceptEvent();
        }
    }

    static bool Moving(Key k) => k is Key.W or Key.A or Key.S or Key.D or Key.Shift;

    static readonly ImageTexture _white = ImageTexture.CreateFromImage(Image.CreateEmpty(1, 1, false, Image.Format.Rgba8) is var i && i != null ? FillWhite(i) : null);

    static Image FillWhite(Image i)
    {
        i.Fill(Colors.White);
        return i;
    }

    /// <summary>Invisible sprites (cstat 32768: markers, a wall crack's target) drawn faint (View > Show Invisible
    /// Sprites).</summary>
    public bool ShowInvisible = true;

    /// <summary>The map is shown On (the moving sectors' state): panning that pans when on moves.</summary>
    public bool PanOn;
    /// <summary>Blood's panning floors, ceilings and walls: game frames gone by, the sectors that have some.</summary>
    double _panFrames;
    List<int> _panSectors = new();

    public override void _Process(double delta)
    {
        if (!Visible)
            return;
        if (_dirty)
            Rebuild();
        else if (_panSectors.Count > 0)
        {
            // (moved on and made again, the panning sectors only)
            _panFrames += delta * Blood.PanMotion.FramesPerSecond;
            Remake(_panSectors, Array.Empty<int>());
        }
        Fly(delta);
        LightsFrame(delta);
        // what is seen where, from where the camera is now, after this frame's move (as Build draws it): worked out
        // before the move, the openings were a frame behind the camera and showed gaps at their edges while flying
        Portals();
        // (the camera's sector known: its atmosphere's fog, the weather round it)
        AtmosphereFrame(delta);
        // (a tool's result shown: its walls numbered as the map's aren't, nothing picked)
        if (!MarksHidden)
            Pick();
    }

    void Fly(double delta)
    {
        if (!HasFocus())
            return;
        var dir = Vector3.Zero;
        var basis = _cam.GlobalTransform.Basis;
        if (Input.IsKeyPressed(Key.W)) dir -= basis.Z;
        if (Input.IsKeyPressed(Key.S)) dir += basis.Z;
        if (Input.IsKeyPressed(Key.A)) dir -= basis.X;
        if (Input.IsKeyPressed(Key.D)) dir += basis.X;
        if (dir == Vector3.Zero)
            return;
        // game units a second (Blood runs at about 400)
        float speed = Input.IsKeyPressed(Key.Shift) ? 900 : 350;
        _cam.Position += dir.Normalized() * speed * (float)delta;
    }

    /// <summary>The camera's yaw facing a Build angle (0 east, 512 south): forward is -Z turned by the yaw.</summary>
    public static float YawOf(float buildAngle)
    {
        float a = buildAngle * Mathf.Pi / 1024f;
        return Mathf.Atan2(-Mathf.Cos(a), -Mathf.Sin(a));
    }

    /// <summary>How many meshes and sprites the preview holds (testing).</summary>
    public int MeshCount => _world?.GetChildCount() ?? 0;

    /// <summary>The camera: where it is (game units) and its yaw and pitch (testing).</summary>
    public (Vector3 at, float yaw, float pitch) CameraPose => (_cam.Position, _yaw, _pitch);

    /// <summary>The camera's right in Build's x and y (not snapped).</summary>
    public Vector2 CameraRight => new(_cam.GlobalTransform.Basis.X.X, _cam.GlobalTransform.Basis.X.Z);

    /// <summary>Where the camera looks and its right, in Build's x and y, each snapped to the nearer map axis (the
    /// arrows moving sprites: forward, back, left, right as seen).</summary>
    public (Vector2I forward, Vector2I right) ViewAxes
    {
        get
        {
            static Vector2I Snap(Vector3 v) => Mathf.Abs(v.X) >= Mathf.Abs(v.Z)
                ? new Vector2I(v.X >= 0 ? 1 : -1, 0) : new Vector2I(0, v.Z >= 0 ? 1 : -1);
            var b = _cam.GlobalTransform.Basis;
            // (Godot's x is Build's x, its z Build's y: PreviewMesher.P)
            return (Snap(-b.Z), Snap(b.X));
        }
    }

    public void SetCamera(Vector3 at, float yaw, float pitch)
    {
        _cam.Position = at;
        // (put there: the sector by its height)
        _camSector = -1;
        _yaw = yaw;
        _pitch = pitch;
        Aim();
    }
}
