// Chosen core: original code (the 3D preview's atmosphere as the game shows it: fog, ground mist, weather, wind).
using System;
using System.Collections.Generic;
using System.Linq;
using Chosen.Shared.Blood.Map;
using Chosen.Shared.Engine.Geometry;
using Godot;

namespace Chosen.Forge.View;

/// <summary>
/// The map's atmospheres in the 3D preview as the game draws them (its EnhancedFog, GroundMist and Weather, at the
/// preview's scale: a unit is a game unit, the game's Godot unit 32): the distance fog of the camera's sector's
/// atmosphere in place of Build's darkening with distance, fading into another's as the camera goes in; the ground
/// mist over each area's floors; rain, snow and leaves falling where the sky is open, each place's share of them, blown
/// by its wind. A map without atmospheres of its own shows the game's guesses (AtmosphereRules.Guess). View >
/// Atmosphere (3D) turns it off (Build's darkening back); Full Brightness leaves it out.
/// </summary>
public partial class Preview3D
{
    /// <summary>View > Atmosphere (3D).</summary>
    public bool ShowAtmosphere = true;

    /// <summary>The atmosphere dialog's settings shown as they change (null: the map's).</summary>
    public List<MapAtmosphere> AtmospheresShown;

    bool AtmosphereOn => ShowAtmosphere && !FullBright && Doc != null;

    // the game's guesses for a map without its own (made again when the map changes)
    List<MapAtmosphere> _guessList;
    List<int> _guessSectors;
    bool _guessDirty = true;

    IReadOnlyList<MapAtmosphere> AtmosphereList
    {
        get
        {
            if (Doc.HasAtmosphere)
                return AtmospheresShown ?? Doc.Atmospheres;
            Guess();
            return _guessList;
        }
    }

    int AtmosphereIndex(int s)
    {
        if (Doc.HasAtmosphere)
        {
            int a = Doc.AtmosphereOf(s);
            return a < AtmosphereList.Count ? a : 0;
        }
        Guess();
        return s >= 0 && s < _guessSectors.Count ? _guessSectors[s] : 0;
    }

    MapAtmosphere AtmosphereAt(int s) => AtmosphereList[AtmosphereIndex(s)];

    void Guess()
    {
        if (!_guessDirty && _guessList != null)
            return;
        _guessDirty = false;
        (_guessList, _guessSectors) = AtmosphereRules.Guess(Doc.Map.Name, AtmosphereRules.AnySky(Doc.Sectors), Doc.Sectors.Count, Doc.GuessMistGroups());
    }

    /// <summary>The atmospheres alone changed (their settings, the sectors given them, the dialog's preview): the mist
    /// and the weather made again, the meshes kept.</summary>
    public void AtmosphereChanged()
    {
        if (Visible && !_dirty)
            BuildAtmosphere();
    }

    /// <summary>A misty level (Death Wish's: most of its sky ceilings palette 1): its fog white.</summary>
    bool MistLevel
    {
        get
        {
            int sky = 0, fog = 0;
            foreach (var s in Doc.Sectors)
                if ((s.CeilingStat & 1) != 0)
                {
                    sky++;
                    if (s.CeilingPal == 1)
                        fog++;
                }
            return sky > 0 && fog * 2 > sky;
        }
    }

    bool _mistLevel;
    Node3D _atmoRoot;

    /// <summary>After Rebuild, or the atmospheres changed: the mist's volumes and the weather's layers.</summary>
    void BuildAtmosphere()
    {
        _guessDirty = true;
        if (_atmoRoot == null)
        {
            _atmoRoot = new Node3D { Name = "Atmosphere" };
            _vp.AddChild(_atmoRoot);
        }
        foreach (var c in _atmoRoot.GetChildren())
            c.QueueFree();
        _weatherLayers.Clear();
        _gridReady = false;
        _gridNext = -1;
        _fogSnap = true;
        if (Doc == null)
            return;
        _mistLevel = MistLevel;
        ApplyVisibility();
        if (!AtmosphereOn)
        {
            _env.FogEnabled = false;
            _env.VolumetricFogEnabled = false;
            return;
        }
        BuildMist();
        BuildWeather();
        ApplyFog();
    }

    // ---------------------------------------------------------------- the distance fog

    // the camera's area's fog, faded into from the fog as it was (EnhancedFog's)
    float _fogFromEnd = 1, _fogToEnd = 1, _fogT = 1, _fogFade = 1;
    Color _fogFromColour, _fogToColour;
    int _fogArea = -2;
    bool _fogSnap = true;

    float LevelFogEnd()
    {
        int vis = Doc.Map.Visibility;
        float v = (vis > 0 ? vis : 512) / 1048576f;
        return (_mistLevel ? 1.97f : 4f) / Mathf.Max(v, 1e-9f);
    }

    (float end, Color colour) FogOf(MapAtmosphere a) =>
        (a.FogDistance > 0 ? a.FogDistance / 16f : LevelFogEnd(),
         a.FogOwnColour ? new Color(a.FogR / 255f, a.FogG / 255f, a.FogB / 255f) : _mistLevel ? Colors.White : Colors.Black);

    float FogEndNow => _fogT >= 1 ? _fogToEnd : Mathf.Exp(Mathf.Lerp(Mathf.Log(_fogFromEnd), Mathf.Log(_fogToEnd), _fogT));
    Color FogColourNow => _fogT >= 1 ? _fogToColour : _fogFromColour.Lerp(_fogToColour, _fogT);

    /// <summary>Each frame: the fog of the camera's sector's atmosphere (faded into), the weather round the camera.</summary>
    void AtmosphereFrame(double delta)
    {
        if (!AtmosphereOn || _atmoRoot == null)
            return;
        int s = CameraSectorNow;
        int area = s >= 0 ? AtmosphereIndex(s) : 0;
        var (end, colour) = FogOf(AtmosphereList[area]);
        if (_fogSnap || area != _fogArea || end != _fogToEnd || colour != _fogToColour)
        {
            (_fogFromEnd, _fogFromColour) = (FogEndNow, FogColourNow);
            (_fogToEnd, _fogToColour) = (end, colour);
            _fogFade = Mathf.Max(AtmosphereList[area].Fade, 0f);
            _fogT = _fogSnap || _fogFade <= 0 ? 1 : 0;
            _fogArea = area;
            _fogSnap = false;
            ApplyFog();
        }
        else if (_fogT < 1)
        {
            _fogT = Mathf.Min(1f, _fogT + (float)delta / Mathf.Max(_fogFade, 0.01f));
            ApplyFog();
        }
        // (a fog bank seen from outside only; an area's volume not where a room sharing its place is nearer than its own)
        if (s != _roomsNearOf)
        {
            _roomsNearOf = s;
            _roomsNear = _roomNeighbours == null ? new List<int>() : LightRules.RoomsByNearness(s, _roomColours, i => _roomNeighbours[i]);
        }
        foreach (var (v, a) in _fogBanks)
            if (GodotObject.IsInstanceValid(v))
                v.Visible = a != area;
        foreach (var (v, own, rooms) in _areaRooms)
            if (GodotObject.IsInstanceValid(v))
                v.Visible = LightRules.RoomSeen(_roomsNear, own, rooms) && (!_fogBanks.Any(b => b.volume == v) || v.Visible);
        WeatherFrame();
    }

    void ApplyFog()
    {
        if (!AtmosphereOn)
            return;
        float end = FogEndNow;
        var colour = FogColourNow;
        bool white = colour.Luminance > 0.5f;
        _env.FogEnabled = true;
        _env.FogMode = Godot.Environment.FogModeEnum.Depth;
        _env.FogLightColor = colour;
        _env.FogLightEnergy = 1f;
        _env.FogDensity = white && _mistLevel ? 0.82f : 1f;
        _env.FogDepthBegin = 0f;
        _env.FogDepthEnd = end;
        _env.FogDepthCurve = white ? 1.4f : 0.53f;
        _env.FogSkyAffect = 0f;
        _env.FogAerialPerspective = 0f;
        // (the mist's volumes need the volumetric fog: thin of its own, as the game's the lights glow in)
        _env.VolumetricFogEnabled = true;
        _env.VolumetricFogDensity = 0.18f / end;
        _env.VolumetricFogAlbedo = Colors.White;
        _env.VolumetricFogEmissionEnergy = 0f;
        _env.VolumetricFogAmbientInject = 0f;
        _env.VolumetricFogGIInject = 0f;
        _env.VolumetricFogSkyAffect = 0f;
        _env.VolumetricFogAnisotropy = 0.2f;
        _env.VolumetricFogLength = Mathf.Clamp(Mathf.Max(end, _fogBanks.Count > 0 ? 4096f : 0f), 512f, _mistAreas > 0 ? 2048f : 8192f);
        _env.VolumetricFogDetailSpread = 2f;
        _env.VolumetricFogTemporalReprojectionEnabled = true;
        _env.VolumetricFogTemporalReprojectionAmount = 0.8f;
    }

    // ---------------------------------------------------------------- the ground mist

    int _mistAreas;
    static Shader _mistShader;
    static NoiseTexture3D _mistNoise;
    const float NoMist = 1e6f;
    const int MaxMistCells = 256;

    // (GroundMist's shader at the preview's scale: no portals, the far room's mist not looked for)
    const string MistShaderCode = @"
shader_type fog;
uniform sampler2D floor_map : filter_nearest, repeat_disable;     // r: the mist's bottom, g: its top
uniform vec4 map_rect;
uniform float mist_density;
uniform vec3 mist_color;
uniform sampler3D mist_noise : repeat_enable;
uniform bool solid;             // an area's distance fog: even from its floor to its ceiling, no patches
float cell_layer(ivec2 c, ivec2 size, float y) {
	if (any(lessThan(c, ivec2(0))) || any(greaterThanEqual(c, size)))
		return 0.0;
	vec2 bt = texelFetch(floor_map, c, 0).rg;
	float h = y - bt.x, thick = max(bt.y - bt.x, 0.3);
	return h < -1.0 || h > thick ? 0.0 : 1.0 - smoothstep(0.0, thick, h);
}
void fog() {
	vec3 at = WORLD_POSITION;
	ivec2 size = textureSize(floor_map, 0);
	vec2 p = (at.xz - map_rect.xy) / map_rect.zw * vec2(size) - 0.5;
	ivec2 c = ivec2(floor(p));
	vec2 f = p - vec2(c);
	float y = at.y;
	if (solid) {
		ivec2 cc = clamp(ivec2(floor(p + 0.5)), ivec2(0), size - 1);
		vec2 bt = texelFetch(floor_map, cc, 0).rg;
		DENSITY = y >= bt.x && y <= bt.y ? mist_density : 0.0;
		ALBEDO = vec3(0.0);
		EMISSION = mist_color;
	} else {
		float layer = mix(mix(cell_layer(c, size, y), cell_layer(c + ivec2(1, 0), size, y), f.x),
			mix(cell_layer(c + ivec2(0, 1), size, y), cell_layer(c + ivec2(1, 1), size, y), f.x), f.y);
		vec3 g = at / 32.0;
		vec3 drift = vec3(TIME * 0.11, TIME * 0.02, TIME * 0.07);
		float n = texture(mist_noise, g * 0.045 + drift).r;
		float n2 = texture(mist_noise, g * 0.11 - drift * 0.7).r;
		DENSITY = mist_density * layer * clamp((n * 1.4 + n2 * 0.6) * 1.6 - 1.0, 0.0, 1.2);
		ALBEDO = vec3(0.2);
		EMISSION = mist_color;
	}
}
";

    // the areas with their own distance fog, seen from outside as a bank of it (GroundMist.AddFog): each volume and its
    // atmosphere (hidden while the camera is in it: its fog is the camera's then)
    readonly List<(FogVolume volume, int atmosphere)> _fogBanks = new();
    // each area's volume, its sectors' room colours and those sharing its place (Rooms(): a fog volume knowing no render
    // layers, hidden when one of these is nearer the camera than its own, LightRules.RoomSeen: room 15's mist stood in
    // room 0's place seen from yard 17, sharing none)
    readonly List<(FogVolume volume, HashSet<int> own, HashSet<int> rooms)> _areaRooms = new();
    List<int> _roomsNear = new();
    int _roomsNearOf = -2;

    (HashSet<int> own, HashSet<int> rooms) RoomsOver(List<int> group)
    {
        if (_roomColours == null || _roomOverlaps == null)
            return (null, null);
        HashSet<int> owns = null, rooms = null;
        foreach (int s in group)
        {
            if (s >= _roomColours.Length)
                continue;
            int own = _roomColours[s];
            if (own > 0)
                (owns ??= new HashSet<int>()).Add(own);
            foreach (int o in _roomOverlaps[s])
                if (o < _roomColours.Length && _roomColours[o] > 0 && _roomColours[o] != own)
                    (rooms ??= new HashSet<int>()).Add(_roomColours[o]);
        }
        return (owns, rooms);
    }

    void BuildMist()
    {
        _mistAreas = 0;
        _fogBanks.Clear();
        _areaRooms.Clear();
        _roomsNearOf = -2;
        int n = Doc.Sectors.Count;
        // the fog banks: adjacent sectors of one atmosphere with a fog of its own
        var inBank = new bool[n];
        for (int s0 = 0; s0 < n; s0++)
        {
            if (inBank[s0] || Doc.Sectors[s0].WallNum < 3 || !Doc.SectorShown(s0) || AtmosphereAt(s0).FogDistance <= 0)
                continue;
            int idx = AtmosphereIndex(s0);
            var group = new List<int>();
            var todo = new Stack<int>();
            todo.Push(s0);
            inBank[s0] = true;
            while (todo.Count > 0)
            {
                int c = todo.Pop();
                group.Add(c);
                var sec = Doc.Sectors[c];
                for (int w = sec.WallPtr; w < sec.WallPtr + sec.WallNum && w < Doc.Walls.Count; w++)
                    if (Doc.Walls[w].NextSector is int o and >= 0 && o < n && !inBank[o] && Doc.SectorShown(o) && AtmosphereIndex(o) == idx)
                    {
                        inBank[o] = true;
                        todo.Push(o);
                    }
            }
            if (AddMist(group, AtmosphereList[idx], true) is { } v)
                _fogBanks.Add((v, idx));
        }
        var seen = new bool[n];
        for (int s0 = 0; s0 < n; s0++)
        {
            if (seen[s0] || Doc.Sectors[s0].WallNum < 3 || !Doc.SectorShown(s0) || AtmosphereAt(s0).MistHeight <= 0)
                continue;
            int idx = AtmosphereIndex(s0);
            var group = new List<int>();
            var todo = new Stack<int>();
            todo.Push(s0);
            seen[s0] = true;
            while (todo.Count > 0)
            {
                int c = todo.Pop();
                group.Add(c);
                var sec = Doc.Sectors[c];
                for (int w = sec.WallPtr; w < sec.WallPtr + sec.WallNum && w < Doc.Walls.Count; w++)
                    if (Doc.Walls[w].NextSector is int o and >= 0 && o < n && !seen[o] && Doc.SectorShown(o) && AtmosphereIndex(o) == idx
                        && Math.Abs(Doc.Sectors[o].FloorZ - sec.FloorZ) <= 32 * 256)
                    {
                        seen[o] = true;
                        todo.Push(o);
                    }
            }
            AddMist(group, AtmosphereList[idx], false);
            _mistAreas++;
        }
    }

    // an area's fog volume over its sectors' floors (GroundMist.AddGroups / AddArea, Build units in, preview units out); a
    // fog bank: from their floors to their ceilings, as thick as its fog
    FogVolume AddMist(List<int> group, MapAtmosphere a, bool bank)
    {
        _mistShader ??= new Shader { Code = MistShaderCode };
        _mistNoise ??= new NoiseTexture3D
        {
            Width = 64, Height = 64, Depth = 64, Seamless = true,
            Noise = new FastNoiseLite { NoiseType = FastNoiseLite.NoiseTypeEnum.Perlin, Frequency = 0.04f, FractalOctaves = 3 },
        };
        double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue, lo = double.MaxValue, hi = double.MinValue;
        float height = a.MistHeight / 256f;
        foreach (int s in group)
        {
            var (bx0, by0, bx1, by1) = BuildGeom.SectorBounds(Doc, s);
            (x0, y0, x1, y1) = (Math.Min(x0, bx0), Math.Min(y0, by0), Math.Max(x1, bx1), Math.Max(y1, by1));
            foreach (var (px, py) in new[] { (bx0, by0), (bx1, by1), (bx0, by1), (bx1, by0), ((bx0 + bx1) / 2.0, (by0 + by1) / 2.0) })
            {
                BuildGeom.GetZsOfSlope(Doc, s, px, py, out double cz, out double f);
                lo = Math.Min(lo, -f / 256);
                hi = Math.Max(hi, bank ? -cz / 256 : -f / 256 + height);
            }
        }
        // (Quake units: Build's / 16; the cells 16 units, coarser for a big area)
        double qx0 = x0 / 16, qy0 = y0 / 16, sx = (x1 - x0) / 16, sy = (y1 - y0) / 16;
        double cell = Math.Max(16, Math.Max(sx, sy) / MaxMistCells);
        int nx = Math.Clamp((int)Math.Ceiling(sx / cell) + 1, 2, MaxMistCells), ny = Math.Clamp((int)Math.Ceiling(sy / cell) + 1, 2, MaxMistCells);
        // the image: x along the preview's x (Build x), y along its z (Build y)
        var img = Image.CreateEmpty(nx, ny, false, Image.Format.Rgf);
        for (int i = 0; i < nx; i++)
            for (int j = 0; j < ny; j++)
            {
                double bx = (qx0 + (i + 0.5) * cell) * 16, by = (qy0 + (j + 0.5) * cell) * 16;
                float mlo = NoMist, mhi = NoMist;
                foreach (int s in group)
                    if (BuildGeom.Inside(Doc, s, bx, by))
                    {
                        BuildGeom.GetZsOfSlope(Doc, s, bx, by, out double cz, out double f);
                        float z = (float)(-f / 256);
                        if (mlo >= NoMist || z < mlo)
                            (mlo, mhi) = (z, bank ? (float)(-cz / 256) : z + height);
                    }
                img.SetPixel(i, j, new Color(mlo, mhi, 0));
            }
        var mat = new ShaderMaterial { Shader = _mistShader };
        mat.SetShaderParameter("mist_noise", _mistNoise);
        mat.SetShaderParameter("floor_map", ImageTexture.CreateFromImage(img));
        mat.SetShaderParameter("map_rect", new Vector4((float)qx0, (float)qy0, (float)(nx * cell), (float)(ny * cell)));
        if (bank)
        {
            // (three times its end's worth gone at its end, about as the depth fog; its colour as the fog's)
            mat.SetShaderParameter("solid", true);
            mat.SetShaderParameter("mist_density", 3f / Mathf.Max(FogOf(a).end, 16f));
            // (as set: in linear light, as the game sends it, the preview's bank all but vanished)
            mat.SetShaderParameter("mist_color", FogOf(a).colour);
        }
        else
        {
            // (the game's: 0.18 a Godot unit times r_groundmistdensity 2, its unit 32 of these)
            mat.SetShaderParameter("mist_density", 0.36f / 32f * a.MistDensity);
            var colour = a.MistOwnColour ? new Color(a.MistR / 255f, a.MistG / 255f, a.MistB / 255f) * 0.9f
                : _mistLevel ? new Color(0.9f, 0.9f, 0.9f) : new Color(0.42f, 0.45f, 0.54f);
            mat.SetShaderParameter("mist_color", colour);
        }
        var size = new Vector3((float)(sx + 2 * cell), (float)(hi - lo + 16), (float)(sy + 2 * cell));
        var vol = new FogVolume { Shape = RenderingServer.FogVolumeShape.World, Material = mat, Size = size };
        _atmoRoot.AddChild(vol);
        vol.Position = new Vector3((float)(qx0 + sx / 2), (float)((lo + hi) / 2), (float)(qy0 + sy / 2));
        if (RoomsOver(group) is { rooms: not null } r)
            _areaRooms.Add((vol, r.own, r.rooms));
        return vol;
    }

    // ---------------------------------------------------------------- the weather

    // the grid round the camera (Weather's: 64 x 64 x 32 cells of 32 game units; a cell its kind and its atmosphere)
    const int GX = 64, GY = 64, GZ = 32;
    const float GCell = 32f;
    const byte Solid = 0, Roofed = 1, Open = 2, SkyCell = 3;
    ImageTexture3D _grid;
    readonly byte[][] _gridBuild = new byte[GX][];
    Vector3 _gridCorner, _gridBuildCorner;     // the preview's units: x Build x, z Build y, y up
    int _gridNext = -1;
    bool _gridReady;
    List<int> _gridNear = new();
    ImageTexture _atmosTable;
    Image _atmosImage;
    (float rain, float snow, float leaves) _most;
    readonly List<(string kind, GpuParticles3D node, ShaderMaterial process)> _weatherLayers = new();

    void BuildWeather()
    {
        var list = AtmosphereList;
        _most = (list.Max(a => a.Rain), list.Max(a => a.Snow), list.Max(a => a.Leaves));
        if (_most.rain <= 0 && _most.snow <= 0 && _most.leaves <= 0)
            return;
        if (_grid == null)
        {
            for (int i = 0; i < GX; i++)
                _gridBuild[i] = new byte[GZ * GY * 2];
            var empty = new Godot.Collections.Array<Image>();
            for (int i = 0; i < GX; i++)
                empty.Add(Image.CreateEmpty(GY, GZ, false, Image.Format.Rg8));
            _grid = new ImageTexture3D();
            _grid.Create(Image.Format.Rg8, GY, GZ, GX, false, empty);
        }
        _atmosImage = Image.CreateEmpty(Math.Max(1, list.Count), 2, false, Image.Format.Rgbaf);
        for (int i = 0; i < list.Count; i++)
        {
            float Share(float v, float most) => most > 0 ? Mathf.Clamp(v / most, 0f, 1f) : 0f;
            _atmosImage.SetPixel(i, 0, new Color(Share(list[i].Rain, _most.rain), Share(list[i].Snow, _most.snow), Share(list[i].Leaves, _most.leaves), 0));
        }
        _atmosTable = ImageTexture.CreateFromImage(_atmosImage);
        // (the sky's shade: the commonest sky ceiling's)
        int shade = Doc.Sectors.Where(s => (s.CeilingStat & 1) != 0).GroupBy(s => (int)s.CeilingShade).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault();
        var lit = PreviewMesher.Shade(shade);
        // (the game's counts at its default amounts: rain r_rainamount 0.1, snow 0.45, leaves 1)
        if (_most.rain > 0)
            AddWeather("rain", (int)(18000 * 0.1f * Mathf.Min(_most.rain, 4f)), 1.4f, lit);
        if (_most.snow > 0)
            AddWeather("snow", (int)(14000 * 0.45f * Mathf.Min(_most.snow, 4f)), 9f, lit);
        if (_most.leaves > 0)
            AddWeather("leaves", (int)(1600 * Mathf.Min(_most.leaves, 4f)), 12f, lit);
    }

    void AddWeather(string kind, int amount, float lifetime, Color lit)
    {
        var process = new ShaderMaterial { Shader = WeatherProcess(kind) };
        process.SetShaderParameter("grid", _grid);
        process.SetShaderParameter("atmos", _atmosTable);
        var draw = new ShaderMaterial { Shader = WeatherDraw(kind) };
        draw.SetShaderParameter("lit", new Vector3(lit.R, lit.G, lit.B));
        if (kind == "snow")
            draw.SetShaderParameter("flake", Flake());
        if (kind == "leaves")
            draw.SetShaderParameter("leaf", Leaf());
        var node = new GpuParticles3D
        {
            Amount = Math.Max(1, amount), Lifetime = lifetime, Preprocess = lifetime, LocalCoords = false,
            ProcessMaterial = process, DrawPass1 = new QuadMesh { Size = Vector2.One, Material = draw },
            VisibilityAabb = new Aabb(new Vector3(-2560, -1280, -2560), new Vector3(5120, 2560, 5120)),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Emitting = false,
        };
        _atmoRoot.AddChild(node);
        _weatherLayers.Add((kind, node, process));
    }

    void WeatherFrame()
    {
        if (_weatherLayers.Count == 0)
            return;
        var cam = _cam.GlobalPosition;
        var want = new Vector3(Mathf.Floor(cam.X / GCell), Mathf.Floor(cam.Y / GCell), Mathf.Floor(cam.Z / GCell)) * GCell
            - new Vector3(GX, GZ, GY) * GCell / 2;
        if (_gridNext < 0 && (!_gridReady || new Vector2(want.X - _gridCorner.X, want.Z - _gridCorner.Z).Length() > GX * GCell / 4
            || Mathf.Abs(want.Y - _gridCorner.Y) > GZ * GCell / 4))
            StartGrid(want);
        if (_gridNext >= 0)
            BuildGridSlices(_gridReady ? 4 : GX);
        // each atmosphere's wind now (the preview's units a second)
        var list = AtmosphereList;
        float t = (float)(Time.GetTicksMsec() / 1000.0);
        for (int i = 0; i < list.Count && i < _atmosImage.GetWidth(); i++)
        {
            var (deg, speed) = AtmosphereRules.WindToQuake(list[i].WindAngle, list[i].WindSpeed);
            float gust = 1f + list[i].Gusts * (0.35f * Mathf.Sin(t * 0.37f) + 0.2f * Mathf.Sin(t * 1.13f + 1.7f));
            // (Quake's x east, y north: the preview's x east, z south; r_wind 0.75)
            float ang = Mathf.DegToRad(deg + 12f * Mathf.Sin(t * 0.11f));
            float v = speed * 0.75f * gust;
            _atmosImage.SetPixel(i, 1, new Color(Mathf.Cos(ang) * v, 0, -Mathf.Sin(ang) * v, 0));
        }
        _atmosTable.Update(_atmosImage);
        foreach (var (_, node, _) in _weatherLayers)
        {
            node.GlobalPosition = cam;
            if (node.Emitting != _gridReady)
                node.Emitting = _gridReady;
        }
    }

    void StartGrid(Vector3 corner)
    {
        _gridBuildCorner = corner;
        _gridNext = 0;
        _gridNear.Clear();
        double bx0 = corner.X * 16, by0 = corner.Z * 16, bx1 = (corner.X + GX * GCell) * 16, by1 = (corner.Z + GY * GCell) * 16;
        for (int s = 0; s < Doc.Sectors.Count; s++)
        {
            if (Doc.Sectors[s].WallNum < 3 || !Doc.SectorShown(s))
                continue;
            var (x0, y0, x1, y1) = BuildGeom.SectorBounds(Doc, s);
            if (x1 < bx0 || y1 < by0 || x0 > bx1 || y0 > by1)
                continue;
            _gridNear.Add(s);
        }
    }

    // a slice of the grid at one x (Weather.BuildSlices); done, it is shown
    void BuildGridSlices(int count)
    {
        for (int n = 0; n < count && _gridNext < GX; n++, _gridNext++)
        {
            var cells = _gridBuild[_gridNext];
            Array.Clear(cells);
            float x = _gridBuildCorner.X + (_gridNext + 0.5f) * GCell;
            for (int j = 0; j < GY; j++)
            {
                float z = _gridBuildCorner.Z + (j + 0.5f) * GCell;
                double bx = x * 16, by = z * 16;
                foreach (int s in _gridNear)
                {
                    if (!BuildGeom.Inside(Doc, s, bx, by))
                        continue;
                    BuildGeom.GetZsOfSlope(Doc, s, bx, by, out double c, out double f);
                    float floor = (float)(-f / 256), ceil = (float)(-c / 256);
                    bool sky = (Doc.Sectors[s].CeilingStat & 1) != 0;
                    byte atmo = (byte)Math.Min(AtmosphereIndex(s), 255);
                    int k0 = Math.Max(0, Mathf.FloorToInt((floor - _gridBuildCorner.Y) / GCell));
                    int k1 = Math.Min(GZ - 1, Mathf.FloorToInt((ceil - _gridBuildCorner.Y) / GCell));
                    for (int k = k0; k <= k1; k++)
                    {
                        int ci = (k * GY + j) * 2;
                        byte kind = sky ? Open : Roofed;
                        if (kind >= cells[ci])
                            (cells[ci], cells[ci + 1]) = (kind, atmo);
                    }
                    if (sky)
                        for (int k = Math.Max(k1 + 1, 0); k < GZ; k++)
                            if (cells[(k * GY + j) * 2] == Solid)
                                (cells[(k * GY + j) * 2], cells[(k * GY + j) * 2 + 1]) = (SkyCell, atmo);
                }
            }
        }
        if (_gridNext < GX)
            return;
        _gridNext = -1;
        var slices = new Godot.Collections.Array<Image>();
        for (int i = 0; i < GX; i++)
            slices.Add(Image.CreateFromData(GY, GZ, false, Image.Format.Rg8, _gridBuild[i]));
        _grid.Update(slices);
        _gridCorner = _gridBuildCorner;
        _gridReady = true;
        foreach (var (_, _, process) in _weatherLayers)
        {
            // (the grid's texture: x along the preview's z, y up, z along its x, as the game's)
            process.SetShaderParameter("grid_corner", _gridCorner);
            process.SetShaderParameter("grid_size", new Vector3(GX, GZ, GY) * GCell);
        }
    }

    static readonly Dictionary<string, Shader> _weatherProcess = new(), _weatherDraw = new();

    // Weather's particles at the preview's scale (its Godot unit 32 of these)
    static Shader WeatherProcess(string kind)
    {
        if (_weatherProcess.TryGetValue(kind, out var sh))
            return sh;
        string fall = kind == "rain" ? "14.0" : kind == "snow" ? "2.2" : "1.4";
        string windShare = kind == "rain" ? "1.5" : kind == "snow" ? "1.0" : "1.6";
        string upwind = kind == "rain" ? "0.7" : "0.0";
        string sway = kind == "rain" ? "0.0" : kind == "snow" ? "0.6" : "1.6";
        string share = kind == "rain" ? "r" : kind == "snow" ? "g" : "b";
        sh = new Shader
        {
            Code = @"
shader_type particles;
render_mode disable_velocity;
uniform highp sampler3D grid : filter_nearest, repeat_disable;
uniform highp sampler2D atmos : filter_nearest, repeat_disable;
uniform vec3 grid_corner;
uniform vec3 grid_size;
const float U = 32.0;
const float FALL = " + fall + @";
const float WIND = " + windShare + @";
const float SWAY = " + sway + @";
const float UPWIND = " + upwind + @";
float hash(uint n) {
	n = (n << 13U) ^ n;
	n = n * (n * n * 15731U + 789221U) + 1376312589U;
	return float(n & 0x7fffffffU) / float(0x7fffffff);
}
// the grid's cell at p (x and z of the texture swapped: its x the preview's z)
ivec2 cell2(vec3 p) {
	vec3 q = (p - grid_corner);
	vec3 uvw = vec3(q.z, q.y, q.x) / vec3(grid_size.z, grid_size.y, grid_size.x);
	if (any(lessThan(uvw, vec3(0.0))) || any(greaterThan(uvw, vec3(1.0))))
		return ivec2(0);
	return ivec2(texture(grid, uvw).rg * 255.0 + 0.5);
}
vec3 wind_of(int a) { return texelFetch(atmos, ivec2(a, 1), 0).xyz; }
void start() {
	uint seed = RANDOM_SEED + uint(INDEX) * 7919U + NUMBER * 104729U;
	vec3 c = EMISSION_TRANSFORM[3].xyz;
	vec3 p = c + vec3((hash(seed) - 0.5) * 50.0, hash(seed + 1U) * 17.5 - 1.0, (hash(seed + 2U) - 0.5) * 50.0) * U
		- wind_of(cell2(c).y) * WIND * UPWIND;
	TRANSFORM = mat4(1.0);
	TRANSFORM[3].xyz = p;
	CUSTOM = vec4(hash(seed + 3U), hash(seed + 4U), hash(seed + 5U), 0.0);
	ivec2 kc = cell2(p);
	ACTIVE = kc.x >= 2 && hash(seed + 6U) < texelFetch(atmos, ivec2(kc.y, 0), 0)." + share + @";
}
void process() {
	vec3 p = TRANSFORM[3].xyz;
	float t = TIME * (0.6 + CUSTOM.x) + CUSTOM.y * 6.283;
	vec3 v = vec3(0.0, -FALL * U * (0.8 + 0.4 * CUSTOM.z), 0.0) + wind_of(cell2(p).y) * WIND
		+ vec3(sin(t), 0.0, cos(t * 0.8)) * SWAY * U;
	p += v * DELTA;
	TRANSFORM[3].xyz = p;
	TRANSFORM[1] = vec4(normalize(v), 0.0);
	VELOCITY = v;
	CUSTOM.w += DELTA;
	if (cell2(p).x == 0)
		ACTIVE = false;
}
",
        };
        _weatherProcess[kind] = sh;
        return sh;
    }

    static Shader WeatherDraw(string kind)
    {
        if (_weatherDraw.TryGetValue(kind, out var sh))
            return sh;
        string body = kind switch
        {
            "rain" => @"
render_mode blend_mix, depth_draw_never, cull_disabled, specular_disabled, ambient_light_disabled, shadows_disabled;
uniform vec3 lit;
void vertex() {
	vec3 c = MODEL_MATRIX[3].xyz;
	vec3 v = MODEL_MATRIX[1].xyz;
	vec3 dir = length(v) > 0.001 ? normalize(v) : vec3(0.0, -1.0, 0.0);
	vec3 to_cam = normalize(CAMERA_POSITION_WORLD - c);
	vec3 side = normalize(cross(dir, to_cam));
	vec3 w = c + dir * VERTEX.y * 24.0 + side * VERTEX.x * 0.64;
	POSITION = PROJECTION_MATRIX * VIEW_MATRIX * vec4(w, 1.0);
}
void fragment() {
	float edge = 1.0 - abs(UV.x - 0.5) * 2.0;
	vec3 base = vec3(0.62, 0.66, 0.72) * lit;
	ALBEDO = base;
	EMISSION = base;
	ALPHA = 0.32 * edge * smoothstep(0.0, 0.25, UV.y) * smoothstep(1.0, 0.75, UV.y) * smoothstep(19.0, 80.0, -VERTEX.z);
}
",
            "snow" => @"
render_mode blend_mix, depth_draw_never, cull_disabled, specular_disabled, ambient_light_disabled, shadows_disabled;
uniform vec3 lit;
uniform sampler2D flake : filter_nearest;
void vertex() {
	vec3 c = MODEL_MATRIX[3].xyz;
	vec3 r = INV_VIEW_MATRIX[0].xyz, u = INV_VIEW_MATRIX[1].xyz;
	float s = (0.09 + 0.06 * INSTANCE_CUSTOM.x) * 32.0;
	POSITION = PROJECTION_MATRIX * VIEW_MATRIX * vec4(c + (r * VERTEX.x + u * VERTEX.y) * s, 1.0);
}
void fragment() {
	vec3 base = vec3(0.9, 0.92, 0.95) * lit;
	ALBEDO = base;
	EMISSION = base;
	ALPHA = texture(flake, UV).r * 0.9;
}
",
            _ => @"
render_mode cull_disabled, specular_disabled, ambient_light_disabled, shadows_disabled;
uniform vec3 lit;
uniform sampler2D leaf : source_color, filter_nearest;
void vertex() {
	vec3 c = MODEL_MATRIX[3].xyz;
	float t = TIME * (0.35 + 0.65 * INSTANCE_CUSTOM.y) + INSTANCE_CUSTOM.x * 6.283;
	float a = t + 0.5 * sin(TIME * (1.2 + INSTANCE_CUSTOM.z) + INSTANCE_CUSTOM.y * 6.283), b = t * 0.7 + INSTANCE_CUSTOM.z * 6.283;
	mat3 rx = mat3(vec3(1.0, 0.0, 0.0), vec3(0.0, cos(a), sin(a)), vec3(0.0, -sin(a), cos(a)));
	mat3 ry = mat3(vec3(cos(b), 0.0, -sin(b)), vec3(0.0, 1.0, 0.0), vec3(sin(b), 0.0, cos(b)));
	vec3 w = c + ry * rx * vec3(VERTEX.xy * 9.6, 0.0);
	POSITION = PROJECTION_MATRIX * VIEW_MATRIX * vec4(w, 1.0);
}
void fragment() {
	vec4 t = texture(leaf, UV);
	if (t.a < 0.5) discard;
	ALBEDO = t.rgb;
	EMISSION = t.rgb * lit * 0.8;
}
",
        };
        sh = new Shader { Code = "shader_type spatial;\n" + body };
        _weatherDraw[kind] = sh;
        return sh;
    }

    static ImageTexture _flake, _leaf;

    static ImageTexture Flake()
    {
        if (_flake != null)
            return _flake;
        const int Size = 8;
        var img = Image.CreateEmpty(Size, Size, false, Image.Format.R8);
        for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                float d = new Vector2(x - (Size - 1) / 2f, y - (Size - 1) / 2f).Length() / (Size / 2f);
                img.SetPixel(x, y, new Color(d < 0.55f ? 1f : d < 0.85f ? 0.6f : d < 1.05f ? 0.25f : 0f, 0, 0));
            }
        return _flake = ImageTexture.CreateFromImage(img);
    }

    static ImageTexture Leaf()
    {
        if (_leaf != null)
            return _leaf;
        var img = Image.CreateEmpty(16, 16, false, Image.Format.Rgba8);
        var rng = new RandomNumberGenerator { Seed = 5 };
        for (int y = 0; y < 16; y++)
            for (int x = 0; x < 16; x++)
            {
                float u = (x - 7.5f) / 5f, v = (y - 7f) / 7f;
                bool inside = u * u + v * v * (v < 0 ? 1.6f : 0.9f) < 1f || (Mathf.Abs(x - 7.5f) < 0.7f && y >= 13);
                var c = new Color(0.55f + rng.Randf() * 0.15f, 0.26f + rng.Randf() * 0.12f, 0.08f, inside ? 1f : 0f);
                if (Mathf.Abs(x - 7.5f) < 0.6f && inside)
                    c = c.Darkened(0.35f);
                img.SetPixel(x, y, c);
            }
        return _leaf = ImageTexture.CreateFromImage(img);
    }
}
