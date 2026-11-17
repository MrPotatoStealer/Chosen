using System.Collections.Generic;
using Chosen.Data;
using Chosen.Sim;
using Godot;

namespace Chosen.Render;

/// <summary>
/// Weather (Options > Enhanced, r_weather): rain, snow and autumn leaves as GPU particles over the open parts of the
/// level, blown by one wind (r_wind; the global wind, gusting). Each falls from the sky: the particle shader reads a
/// grid of the space around the camera (32-unit cells, 2048 x 2048 x 1024 units: solid, open under a roof, open under
/// the sky, or the sky over a sky ceiling, from Build's sectors as they are now) and a particle starting anywhere but the
/// sky or open air under it, or moving into a solid cell, is gone: rain stops at roofs and walls, and the wind blows it
/// in through a window or a door. The grid is made again a few slices a frame once the camera has gone a quarter of the
/// way to its edge, the last one kept until then (it is the level's, not the camera's: nothing changes as it moves). The
/// weather and wind are each place's atmosphere's (AreaAtmosphere: a .MAPX's own, else the game's guess for the level):
/// each cell of the grid also holds its sector's atmosphere, and a small table each atmosphere's share of each weather
/// (its amount over the most any has: a layer's particles are the most, each place keeps its share) and its wind now.
/// </summary>
public partial class Weather : Node3D
{
    public static readonly Core.Cvar Mode = Core.Cvars.Register("r_weather", "1",
        "weather: 0 off, 1 the level's (rain, snow, leaves), 2 rain, 3 snow", saved: true);
    public static readonly Core.Cvar Amount = Core.Cvars.Register("r_weatheramount", "1",
        "weather: how much of it falls (0.1 .. 1)", saved: true);
    public static readonly Core.Cvar RainAmount = Core.Cvars.Register("r_rainamount", "0.1",
        "weather: how much rain falls, times r_weatheramount (0.05 .. 1)", saved: true);
    public static readonly Core.Cvar SnowAmount = Core.Cvars.Register("r_snowamount", "0.45",
        "weather: how much snow falls, times r_weatheramount (0.05 .. 2)", saved: true);
    public static readonly Core.Cvar LeavesAmount = Core.Cvars.Register("r_leavesamount", "1",
        "weather: how many leaves blow, times r_weatheramount and the wind (0.05 .. 2)", saved: true);
    public static readonly Core.Cvar Ripples = Core.Cvars.Register("r_rainripples", "1",
        "1: rain makes rings where it lands", saved: true);
    public static readonly Core.Cvar Dust = Core.Cvars.Register("r_dust", "1",
        "1: dust hanging in the air, seen in the light", saved: true);
    public static readonly Core.Cvar Ash = Core.Cvars.Register("r_ash", "1",
        "ash falling: 1 in levels of fire and lava, 2 in any level with sky", saved: true);
    public static readonly Core.Cvar Wind = Core.Cvars.Register("r_wind", "0.75",
        "weather: the wind's strength, times the level's", saved: true);

    const int NX = 64, NY = 64, NZ = 32;     // cells along Quake x, y, z
    const float Cell = 32f;
    const byte Solid = 0, Roofed = 1, Open = 2, Sky = 3;

    World _world;
    // the atmospheres' table (a column each: row 0 their shares of rain, snow and leaves, row 1 their wind now, Godot)
    ImageTexture _atmos;
    Image _atmosImage;
    (float rain, float snow, float leaves) _most;

    // the grid shown and the one being made: its corner (Quake), the slices made so far
    ImageTexture3D _grid;
    readonly Godot.Collections.Array<Image> _slices = new();
    readonly byte[][] _build = new byte[NX][];     // two bytes a cell: its kind, its atmosphere
    Vector3 _shownCorner, _buildCorner;
    int _nextSlice = -1;
    bool _haveGrid;
    readonly List<int> _near = new();        // the sectors over the window being made
    // the highest floor under the sky at each column (Godot y; NoFloor none), for what lands: rain's splashes
    const float NoFloor = -1e6f;
    readonly float[] _floors = new float[NX * NY * 2];     // the floor and its atmosphere
    byte[] _floorBytes, _open;
    ImageTexture _floorTex;

    sealed class Layer
    {
        public string Kind;
        public GpuParticles3D Node;
        public ShaderMaterial Process, Draw;
        public int Amount;
    }
    readonly List<Layer> _layers = new();
    static readonly StringName WindName = "weather_wind";

    public override void _Ready()
    {
        for (int i = 0; i < NX; i++)
            _build[i] = new byte[NY * NZ * 2];
        var empty = new Godot.Collections.Array<Image>();
        for (int i = 0; i < NX; i++)
            empty.Add(Image.CreateEmpty(NY, NZ, false, Image.Format.Rg8));
        _grid = new ImageTexture3D();
        _grid.Create(Image.Format.Rg8, NY, NZ, NX, false, empty);
        Mode.Changed += Rebuild;
        Ripples.Changed += Rebuild;
        Dust.Changed += Rebuild;
        Ash.Changed += Rebuild;
    }

    public override void _ExitTree()
    {
        Mode.Changed -= Rebuild;
        Ripples.Changed -= Rebuild;
        Dust.Changed -= Rebuild;
        Ash.Changed -= Rebuild;
    }

    /// <summary>A new level (after AreaAtmosphere.Setup): its atmospheres' weather and wind, the grid made again.</summary>
    public void Setup(World world)
    {
        _world = world;
        _open = world.NativeSkyFlags();
        int n = Mathf.Max(1, AreaAtmosphere.List.Count);
        _atmosImage = Image.CreateEmpty(n, 2, false, Image.Format.Rgbaf);
        _atmos = ImageTexture.CreateFromImage(_atmosImage);
        _haveGrid = false;
        _nextSlice = -1;
        Rebuild();
    }

    // the layers the settings ask for
    void Rebuild()
    {
        foreach (var l in _layers)
            l.Node.QueueFree();
        _layers.Clear();
        if (_world == null || _world.Map == null || _atmos == null)
            return;
        // (r_weather 2 and 3: rain or snow everywhere, whatever the atmospheres say)
        _most = Mode.Int switch { 1 => AreaAtmosphere.MostWeather(), 2 => (1f, 0f, 0f), 3 => (0f, 1f, 0f), _ => (0f, 0f, 0f) };
        SendShares();
        if (_most.rain > 0)
            AddLayer("rain", 18000, 1.4f);
        if (_most.snow > 0)
            AddLayer("snow", 14000, 9f);
        if (_most.leaves > 0)
            AddLayer("leaves", 1600, 12f);
        if (_most.rain > 0 && Ripples.Bool)
            AddLayer("splash", 900, 0.35f);
        if (Dust.Bool)
            AddLayer("dust", 1100, 9f);
        if (Ash.Int == 2 && AreaAtmosphere.AnySky || Ash.Int == 1 && FireLevel())
            AddLayer("ash", 3500, 13f);
    }

    // each atmosphere's share of each weather (row 0 of the table)
    void SendShares()
    {
        var list = AreaAtmosphere.List;
        for (int i = 0; i < _atmosImage.GetWidth(); i++)
        {
            var a = i < list.Count ? list[i] : null;
            float Share(float v, float most) => most > 0 ? Mathf.Clamp(v / most, 0f, 1f) : 0f;
            var c = Mode.Int switch
            {
                1 when a != null => new Color(Share(a.Rain, _most.rain), Share(a.Snow, _most.snow), Share(a.Leaves, _most.leaves), 0),
                2 => new Color(1, 0, 0, 0),
                3 => new Color(0, 1, 0, 0),
                _ => new Color(0, 0, 0, 0),
            };
            _atmosImage.SetPixel(i, 0, c);
        }
        _atmos.Update(_atmosImage);
    }

    // lava or a good many fires under open sky
    bool FireLevel()
    {
        if (!AreaAtmosphere.AnySky)
            return false;
        int n = 0;
        foreach (var s in _world.Board.Sectors)
            if (Tiles.SurfType(s.FloorPicnum) == K.kSurfLava || s.FloorPicnum == ProceduralFire.Tile)
                n++;
        return n >= 2;
    }

    void AddLayer(string kind, int amount, float lifetime)
    {
        var process = new ShaderMaterial { Shader = ProcessShader(kind) };
        process.SetShaderParameter("grid", _grid);
        process.SetShaderParameter("atmos", _atmos);
        var draw = new ShaderMaterial { Shader = DrawShader(kind) };
        if (kind == "leaves")
            draw.SetShaderParameter("leaf", LeafTexture());
        if (kind is "snow" or "splash" or "ash")
            draw.SetShaderParameter("flake", FlakeTexture());
        if (kind == "splash")
        {
            _floorTex ??= ImageTexture.CreateFromImage(Image.CreateEmpty(NY, NX, false, Image.Format.Rgf));
            process.SetShaderParameter("floors", _floorTex);
        }
        var quad = new QuadMesh { Size = Vector2.One, Material = draw };
        var node = new GpuParticles3D
        {
            Amount = amount,
            Lifetime = lifetime,
            Preprocess = lifetime,
            LocalCoords = false,
            ProcessMaterial = process,
            DrawPass1 = quad,
            VisibilityAabb = new Aabb(new Vector3(-80, -40, -80), new Vector3(160, 80, 160)),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            FixedFps = 0,
            Interpolate = false,
        };
        AddChild(node);
        // (a layer made after the grid, a setting changed while playing: told where it lies, or every particle was out of
        // it and died, and the weather stayed away until the camera had moved far enough for a new grid)
        if (_haveGrid)
            SendGrid(process);
        _layers.Add(new Layer { Kind = kind, Node = node, Process = process, Draw = draw, Amount = amount });
    }

    /// <summary>Each frame after the camera is placed (Quake position): the grid made on, the emitters on the camera,
    /// the wind.</summary>
    public void Update(Vector3 camQ, double time)
    {
        if (_layers.Count == 0)
            return;
        var want = new Vector3(Mathf.Floor(camQ.X / Cell), Mathf.Floor(camQ.Y / Cell), Mathf.Floor(camQ.Z / Cell)) * Cell
            - new Vector3(NX, NY, NZ) * Cell / 2;
        if (_nextSlice < 0 && (!_haveGrid || new Vector2(want.X - _shownCorner.X, want.Y - _shownCorner.Y).Length() > NX * Cell / 4
            || Mathf.Abs(want.Z - _shownCorner.Z) > NZ * Cell / 4))
            StartGrid(want);
        if (_nextSlice >= 0)
            BuildSlices(_haveGrid ? 4 : NX);
        float amount = Mathf.Clamp(Amount.Float, 0.05f, 1f);
        // (each kind as much as the atmosphere with the most of it asks: a place with less keeps its share of them)
        float most(string kind) => kind switch { "rain" or "splash" => _most.rain, "snow" => _most.snow, "leaves" => _most.leaves, _ => 1f };
        foreach (var l in _layers)
        {
            l.Node.GlobalPosition = Coords.QuakeToGodot(camQ);
            // (the count itself: AmountRatio changed while emitting left every particle respawning with the custom
            // process shader; a new count restarts the layer, filled at once by its preprocess)
            float ratio = l.Kind switch
            {
                "rain" or "splash" => amount * Mathf.Clamp(RainAmount.Float, 0.05f, 1f) * (l.Kind == "splash" ? 4f : 1f),
                "snow" => amount * Mathf.Clamp(SnowAmount.Float, 0.05f, 2f),
                "leaves" => amount * Mathf.Clamp(LeavesAmount.Float, 0.05f, 2f),
                _ => 1f,
            } * Mathf.Clamp(most(l.Kind), 0.05f, 4f);
            // the more wind, the more leaves it tears loose (the windiest place with leaves)
            if (l.Kind == "leaves")
                ratio *= Mathf.Clamp(Mathf.Max(Wind.Float, 0f) * LeafWind() / 90f, 0.1f, 4f);
            int count = Mathf.Max(1, Mathf.RoundToInt(l.Amount * ratio));
            if (l.Node.Amount != count)
                l.Node.Amount = count;
            if (l.Node.Emitting != _haveGrid)
                l.Node.Emitting = _haveGrid;
        }
        // each atmosphere's wind, gusting (two slow waves, as much as it says), turning a little; the camera's place's for
        // what starts upwind of it
        var list = AreaAtmosphere.List;
        float t = (float)time;
        for (int i = 0; i < _atmosImage.GetWidth(); i++)
        {
            var w = i < list.Count ? WindOf(list[i], t) : Vector3.Zero;
            _atmosImage.SetPixel(i, 1, new Color(w.X, w.Y, w.Z, 0));
        }
        _atmos.Update(_atmosImage);
        RenderingServer.GlobalShaderParameterSet(WindName, WindOf(AreaAtmosphere.Of(StackedRooms.Sector), t));
    }

    // an atmosphere's wind now (Godot units a second)
    static Vector3 WindOf(Shared.Blood.Map.MapAtmosphere a, float time)
    {
        var (deg, speedQ) = Shared.Blood.Map.AtmosphereRules.WindToQuake(a.WindAngle, a.WindSpeed);
        float gust = 1f + a.Gusts * (0.35f * Mathf.Sin(time * 0.37f) + 0.2f * Mathf.Sin(time * 1.13f + 1.7f));
        float angle = Mathf.DegToRad(deg + 12f * Mathf.Sin(time * 0.11f));
        float speed = speedQ * Mathf.Max(Wind.Float, 0f) * gust;
        return Coords.QuakeToGodot(new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0) * speed);
    }

    // the wind of the windiest atmosphere with leaves (Quake units a second)
    static float LeafWind()
    {
        float most = 0;
        foreach (var a in AreaAtmosphere.List)
            if (a.Leaves > 0)
                most = Mathf.Max(most, a.WindSpeed / 16f);
        return most;
    }

    void StartGrid(Vector3 corner)
    {
        _buildCorner = corner;
        _nextSlice = 0;
        // the sectors over the window (each column then looks at these only)
        _near.Clear();
        var map = _world.Map;
        for (int b = 0; b < map.SectorCount; b++)
        {
            var box = map.SectorBox(b);
            if (box.End.X < corner.X || box.End.Y < corner.Y || box.Position.X > corner.X + NX * Cell || box.Position.Y > corner.Y + NY * Cell)
                continue;
            _near.Add(b);
        }
    }

    // slices of the grid (each a column of cells along Quake y and z at one x); done, it replaces the shown one
    void BuildSlices(int count)
    {
        for (int n = 0; n < count && _nextSlice < NX; n++, _nextSlice++)
        {
            var cells = _build[_nextSlice];
            System.Array.Clear(cells);
            for (int j = 0; j < NY; j++)
            {
                _floors[(_nextSlice * NY + j) * 2] = NoFloor;
                _floors[(_nextSlice * NY + j) * 2 + 1] = 0;
            }
            float x = _buildCorner.X + (_nextSlice + 0.5f) * Cell;
            for (int j = 0; j < NY; j++)
            {
                float y = _buildCorner.Y + (j + 0.5f) * Cell;
                var q = new Vector3(x, y, 0);
                foreach (int b in _near)
                {
                    if (!_world.InSectorNow(b, q, out _))
                        continue;
                    var p = new Vector2(x, y);
                    float floor = _world.SectorFloorZ(b, p), ceil = _world.SectorCeilZ(b, p);
                    bool sky = _world.Map.CeilSky(b);
                    byte atmo = (byte)Mathf.Min(AreaAtmosphere.IndexOf(b), 255);
                    int k0 = Mathf.Max(0, Mathf.FloorToInt((floor - _buildCorner.Z) / Cell));
                    int k1 = Mathf.Min(NZ - 1, Mathf.FloorToInt((ceil - _buildCorner.Z) / Cell));
                    for (int k = k0; k <= k1; k++)
                    {
                        int c = (k * NY + j) * 2;
                        byte kind = sky ? Open : Roofed;
                        if (kind >= cells[c])
                        {
                            cells[c] = kind;
                            cells[c + 1] = atmo;
                        }
                    }
                    // (what lands: the highest floor open to the sky no higher than the camera's reach)
                    int fi = (_nextSlice * NY + j) * 2;
                    if (_open != null && b < _open.Length && (_open[b] & 3) == 1 && floor < _buildCorner.Z + (NZ / 2 + 3) * Cell
                        && floor / Coords.QuakeScale > _floors[fi])
                    {
                        _floors[fi] = floor / Coords.QuakeScale;
                        _floors[fi + 1] = atmo;
                    }
                    if (sky)
                        for (int k = Mathf.Max(k1 + 1, 0); k < NZ; k++)
                            if (cells[(k * NY + j) * 2] == Solid)
                            {
                                cells[(k * NY + j) * 2] = Sky;
                                cells[(k * NY + j) * 2 + 1] = atmo;
                            }
                }
            }
        }
        if (_nextSlice < NX)
            return;
        _nextSlice = -1;
        _slices.Clear();
        for (int i = 0; i < NX; i++)
            _slices.Add(Image.CreateFromData(NY, NZ, false, Image.Format.Rg8, _build[i]));
        _grid.Update(_slices);
        if (_floorTex != null)
        {
            _floorBytes ??= new byte[_floors.Length * 4];
            System.Buffer.BlockCopy(_floors, 0, _floorBytes, 0, _floorBytes.Length);
            _floorTex.Update(Image.CreateFromData(NY, NX, false, Image.Format.Rgf, _floorBytes));
        }
        _shownCorner = _buildCorner;
        _haveGrid = true;
        foreach (var l in _layers)
            SendGrid(l.Process);
    }

    // where the grid lies (Godot): its corner and size, along Godot x (Quake y), y (Quake z), z (Quake x)
    void SendGrid(ShaderMaterial process)
    {
        process.SetShaderParameter("grid_corner", Coords.QuakeToGodot(_shownCorner));
        process.SetShaderParameter("grid_size", new Vector3(NY, NZ, NX) * Cell / Coords.QuakeScale);
    }

    // ---------------------------------------------------------------- shaders
    static readonly Dictionary<string, Shader> _process = new(), _draw = new();

    static Shader ProcessShader(string kind)
    {
        if (_process.TryGetValue(kind, out var sh))
            return sh;
        if (kind == "splash")
        {
            sh = new Shader { Code = @"
shader_type particles;
render_mode disable_velocity;
uniform highp sampler2D floors : filter_nearest, repeat_disable;     // r: the floor, g: its atmosphere
uniform highp sampler2D atmos : filter_nearest, repeat_disable;
uniform vec3 grid_corner;
uniform vec3 grid_size;
float hash(uint n) {
	n = (n << 13U) ^ n;
	n = n * (n * n * 15731U + 789221U) + 1376312589U;
	return float(n & 0x7fffffffU) / float(0x7fffffff);
}
void start() {
	uint seed = RANDOM_SEED + uint(INDEX) * 7919U + NUMBER * 104729U;
	vec3 c = EMISSION_TRANSFORM[3].xyz;
	vec3 p = c + vec3((hash(seed) - 0.5) * 36.0, 0.0, (hash(seed + 1U) - 0.5) * 36.0);
	vec3 uvw = (p - grid_corner) / grid_size;
	float y = -1e6, share = 0.0;
	if (uvw.x >= 0.0 && uvw.z >= 0.0 && uvw.x <= 1.0 && uvw.z <= 1.0) {
		vec2 f = texture(floors, uvw.xz).rg;
		y = f.r;
		share = texelFetch(atmos, ivec2(int(f.g + 0.5), 0), 0).r;
	}
	// (only where it rains, as much as it does there)
	ACTIVE = y > -1e5 && abs(y - c.y) < 14.0 && hash(seed + 5U) < share;
	p.y = y + 0.02;
	TRANSFORM = mat4(1.0);
	TRANSFORM[3].xyz = p;
	CUSTOM = vec4(hash(seed + 2U), hash(seed + 3U), hash(seed + 4U), 0.0);
}
void process() {
	CUSTOM.w += DELTA / LIFETIME;
}
" };
            _process[kind] = sh;
            return sh;
        }
        // (Godot units: 1 = 32 Quake units) fall speed, the wind's share, sway
        string fall = kind == "rain" ? "14.0" : kind == "snow" ? "2.2" : kind == "ash" ? "0.7" : kind == "dust" ? "0.03" : "1.4";
        // (rain falls fast: at the level's wind a slant of some 17 degrees, 40 at the most the slider gives)
        string windShare = kind == "rain" ? "1.5" : kind is "snow" or "ash" ? "1.0" : kind == "dust" ? "0.0" : "1.6";
        // starting this many seconds of wind upwind, what falls arrives over the camera (only rain: its life is short, and
        // the wind would otherwise blow the area it falls in away from the camera)
        string upwind = kind == "rain" ? "0.7" : "0.0";
        string sway = kind == "rain" ? "0.0" : kind is "snow" or "ash" ? "0.6" : kind == "dust" ? "0.05" : "1.6";
        // (dust hangs all round the camera, roofed or not; what falls starts over it, in the open)
        string zone = kind == "dust" ? "hash(seed + 1U) * 18.0 - 9.0" : "hash(seed + 1U) * 17.5 - 1.0";
        string across = kind == "dust" ? "26.0" : "50.0";
        string active = kind == "dust" ? "k == 1 || k == 2" : "k >= 2";
        // its share of each place (the atmospheres' table's row 0: rain, snow, leaves; the rest everywhere)
        string share = kind switch { "rain" => "r", "snow" => "g", "leaves" => "b", _ => "" };
        sh = new Shader
        {
            Code = @"
shader_type particles;
render_mode disable_velocity;
global uniform vec3 weather_wind;      // the camera's place's, Godot units a second
uniform highp sampler3D grid : filter_nearest, repeat_disable;     // r: the cell's kind, g: its atmosphere
uniform highp sampler2D atmos : filter_nearest, repeat_disable;    // a column an atmosphere: row 0 its shares, row 1 its wind
uniform vec3 grid_corner;
uniform vec3 grid_size;
const float FALL = " + fall + @";
const float WIND = " + windShare + @";
const float SWAY = " + sway + @";
const float UPWIND = " + upwind + @";

float hash(uint n) {
	n = (n << 13U) ^ n;
	n = n * (n * n * 15731U + 789221U) + 1376312589U;
	return float(n & 0x7fffffffU) / float(0x7fffffff);
}

// the grid's cell at p: its kind (0 solid, 1 open under a roof, 2 open under the sky, 3 the sky; outside the grid:
// solid) and its atmosphere
ivec2 cell2(vec3 p) {
	vec3 uvw = (p - grid_corner) / grid_size;
	if (any(lessThan(uvw, vec3(0.0))) || any(greaterThan(uvw, vec3(1.0))))
		return ivec2(0);
	return ivec2(texture(grid, uvw).rg * 255.0 + 0.5);
}
int cell(vec3 p) { return cell2(p).x; }
vec3 wind_of(int a) { return texelFetch(atmos, ivec2(a, 1), 0).xyz; }

void start() {
	uint seed = RANDOM_SEED + uint(INDEX) * 7919U + NUMBER * 104729U;
	// anywhere over a square 1600 Quake units across round the camera, from just under it to 560 over it
	vec3 c = EMISSION_TRANSFORM[3].xyz;
	vec3 p = c + vec3((hash(seed) - 0.5) * " + across + @", " + zone + @", (hash(seed + 2U) - 0.5) * " + across + @")
		- weather_wind * WIND * UPWIND;
	TRANSFORM = mat4(1.0);
	TRANSFORM[3].xyz = p;
	CUSTOM = vec4(hash(seed + 3U), hash(seed + 4U), hash(seed + 5U), 0.0);
	ivec2 kc = cell2(p);
	int k = kc.x;
	ACTIVE = " + active + @";      // (what falls: only in the sky or the open air under it)
	// (where this weather falls, as much as it does there)
" + (share == "" ? "" : @"	if (ACTIVE)
		ACTIVE = hash(seed + 6U) < texelFetch(atmos, ivec2(kc.y, 0), 0)." + share + @";
") + @"
}

void process() {
	vec3 p = TRANSFORM[3].xyz;
	float t = TIME * (0.6 + CUSTOM.x) + CUSTOM.y * 6.283;
	// (the wind of the place it is in)
	vec3 v = vec3(0.0, -FALL * (0.8 + 0.4 * CUSTOM.z), 0.0) + wind_of(cell2(p).y) * WIND
		+ vec3(sin(t), 0.0, cos(t * 0.8)) * SWAY;
	p += v * DELTA;
	TRANSFORM[3].xyz = p;
	// its direction as the transform's y (rain draws a streak along it)
	TRANSFORM[1] = vec4(normalize(v), 0.0);
	VELOCITY = v;
	CUSTOM.w += DELTA;
	if (cell(p) == 0)
		ACTIVE = false;
}
",
        };
        _process[kind] = sh;
        return sh;
    }

    static Shader DrawShader(string kind)
    {
        if (_draw.TryGetValue(kind, out var sh))
            return sh;
        string body = kind switch
        {
            // a streak along its velocity, turned to the camera about it
            "rain" => @"
render_mode blend_mix, depth_draw_never, cull_disabled, specular_disabled, ambient_light_disabled, shadows_disabled;
global uniform float sky_shade;
" + BuildShading.ShadeFunction + @"
void vertex() {
	vec3 c = MODEL_MATRIX[3].xyz;
	vec3 v = MODEL_MATRIX[1].xyz;
	vec3 dir = length(v) > 0.001 ? normalize(v) : vec3(0.0, -1.0, 0.0);
	vec3 to_cam = normalize(CAMERA_POSITION_WORLD - c);
	vec3 side = normalize(cross(dir, to_cam));
	vec3 w = c + dir * VERTEX.y * 0.75 + side * VERTEX.x * 0.02;
	POSITION = PROJECTION_MATRIX * VIEW_MATRIX * vec4(w, 1.0);
}
void fragment() {
	float edge = 1.0 - abs(UV.x - 0.5) * 2.0;
	vec3 base = vec3(0.62, 0.66, 0.72) * build_shade(sky_shade);
	ALBEDO = base;
	EMISSION = base;
	// (faded right by the camera: a streak there filled the screen)
	ALPHA = 0.32 * edge * smoothstep(0.0, 0.25, UV.y) * smoothstep(1.0, 0.75, UV.y) * smoothstep(0.6, 2.5, -VERTEX.z);
}
",
            // a flake of a few big pixels turned to the camera
            "snow" => @"
render_mode blend_mix, depth_draw_never, cull_disabled, specular_disabled, ambient_light_disabled, shadows_disabled;
global uniform float sky_shade;
uniform sampler2D flake : filter_nearest;
" + BuildShading.ShadeFunction + @"
void vertex() {
	vec3 c = MODEL_MATRIX[3].xyz;
	vec3 r = INV_VIEW_MATRIX[0].xyz, u = INV_VIEW_MATRIX[1].xyz;
	float s = 0.09 + 0.06 * INSTANCE_CUSTOM.x;
	POSITION = PROJECTION_MATRIX * VIEW_MATRIX * vec4(c + (r * VERTEX.x + u * VERTEX.y) * s, 1.0);
}
void fragment() {
	vec3 base = vec3(0.9, 0.92, 0.95) * build_shade(sky_shade);
	ALBEDO = base;
	EMISSION = base;
	ALPHA = texture(flake, UV).r * 0.9;
}
",
            // a ring on the ground, spreading
            "splash" => @"
render_mode blend_mix, depth_draw_never, cull_disabled, specular_disabled, ambient_light_disabled, shadows_disabled;
global uniform float sky_shade;
uniform sampler2D flake : filter_nearest;
" + BuildShading.ShadeFunction + @"
varying flat float v_life;
void vertex() {
	v_life = INSTANCE_CUSTOM.w;
	vec3 c = MODEL_MATRIX[3].xyz;
	float s = 0.06 + 0.28 * INSTANCE_CUSTOM.w;
	POSITION = PROJECTION_MATRIX * VIEW_MATRIX * vec4(c + vec3(VERTEX.x, 0.0, VERTEX.y) * s, 1.0);
}
void fragment() {
	vec3 base = vec3(0.7, 0.74, 0.8) * build_shade(sky_shade);
	ALBEDO = base;
	EMISSION = base;
	float f = texture(flake, UV).r;
	// (hollow as it spreads)
	ALPHA = (f < 0.5 + 0.5 * step(v_life, 0.4) ? f : 0.0) * 0.8 * (1.0 - v_life) * smoothstep(0.6, 2.5, -VERTEX.z);
}
",
            // ash: a dark flake, now and then one still burning
            "ash" => @"
render_mode blend_mix, depth_draw_never, cull_disabled, specular_disabled, ambient_light_disabled, shadows_disabled;
global uniform float sky_shade;
uniform sampler2D flake : filter_nearest;
" + BuildShading.ShadeFunction + @"
varying flat float v_hot;
void vertex() {
	v_hot = INSTANCE_CUSTOM.y;
	vec3 c = MODEL_MATRIX[3].xyz;
	vec3 r = INV_VIEW_MATRIX[0].xyz, u = INV_VIEW_MATRIX[1].xyz;
	float s = 0.05 + 0.05 * INSTANCE_CUSTOM.x;
	POSITION = PROJECTION_MATRIX * VIEW_MATRIX * vec4(c + (r * VERTEX.x + u * VERTEX.y) * s, 1.0);
}
void fragment() {
	bool hot = v_hot > 0.9;
	vec3 base = hot ? vec3(1.0, 0.42, 0.1) : vec3(0.2, 0.19, 0.18) * build_shade(sky_shade);
	ALBEDO = base;
	EMISSION = base;
	ALPHA = texture(flake, UV).r * 0.9;
}
",
            // dust: hardly there but where a light falls on it
            "dust" => @"
render_mode blend_mix, depth_draw_never, cull_disabled, specular_disabled, ambient_light_disabled, shadows_disabled;
void vertex() {
	vec3 c = MODEL_MATRIX[3].xyz;
	vec3 r = INV_VIEW_MATRIX[0].xyz, u = INV_VIEW_MATRIX[1].xyz;
	float s = 0.018 + 0.016 * INSTANCE_CUSTOM.x;
	vec3 w = c + (r * VERTEX.x + u * VERTEX.y) * s;
	VERTEX = (inverse(MODEL_MATRIX) * vec4(w, 1.0)).xyz;
	NORMAL = (transpose(mat3(MODEL_MATRIX)) * normalize(CAMERA_POSITION_WORLD - w));
}
void fragment() {
	ALBEDO = vec3(0.9, 0.85, 0.75);
	EMISSION = vec3(0.012);
	ALPHA = 0.8 * smoothstep(1.0, 2.5, -VERTEX.z) * (1.0 - smoothstep(9.0, 13.0, -VERTEX.z));
}
void light() {
	DIFFUSE_LIGHT += ATTENUATION * LIGHT_COLOR / PI;
}
",
            // a leaf tumbling: turned about two axes as it falls
            _ => @"
render_mode cull_disabled, specular_disabled, ambient_light_disabled, shadows_disabled;
global uniform float sky_shade;
uniform sampler2D leaf : source_color, filter_nearest;
" + BuildShading.ShadeFunction + @"
void vertex() {
	vec3 c = MODEL_MATRIX[3].xyz;
	// its own slow turn, from what it was given at its start (from its place, which the wind moves, it spun the faster the
	// harder it blew), with a flutter
	float t = TIME * (0.35 + 0.65 * INSTANCE_CUSTOM.y) + INSTANCE_CUSTOM.x * 6.283;
	float a = t + 0.5 * sin(TIME * (1.2 + INSTANCE_CUSTOM.z) + INSTANCE_CUSTOM.y * 6.283), b = t * 0.7 + INSTANCE_CUSTOM.z * 6.283;
	mat3 rx = mat3(vec3(1.0, 0.0, 0.0), vec3(0.0, cos(a), sin(a)), vec3(0.0, -sin(a), cos(a)));
	mat3 ry = mat3(vec3(cos(b), 0.0, -sin(b)), vec3(0.0, 1.0, 0.0), vec3(sin(b), 0.0, cos(b)));
	vec3 w = c + ry * rx * vec3(VERTEX.xy * 0.3, 0.0);
	POSITION = PROJECTION_MATRIX * VIEW_MATRIX * vec4(w, 1.0);
}
void fragment() {
	vec4 t = texture(leaf, UV);
	if (t.a < 0.5) discard;
	ALBEDO = t.rgb;
	EMISSION = t.rgb * build_shade(sky_shade) * 0.8;
}
",
        };
        sh = new Shader { Code = "shader_type spatial;\n" + body };
        _draw[kind] = sh;
        return sh;
    }

    static ImageTexture _flakeTex, _leafTex;

    public static ImageTexture FlakeTexture()
    {
        if (_flakeTex != null)
            return _flakeTex;
        const int Size = 8;
        var img = Image.CreateEmpty(Size, Size, false, Image.Format.R8);
        for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                float d = new Vector2(x - (Size - 1) / 2f, y - (Size - 1) / 2f).Length() / (Size / 2f);
                img.SetPixel(x, y, new Color(d < 0.55f ? 1f : d < 0.85f ? 0.6f : d < 1.05f ? 0.25f : 0f, 0, 0));
            }
        return _flakeTex = ImageTexture.CreateFromImage(img);
    }

    static ImageTexture LeafTexture()
    {
        if (_leafTex != null)
            return _leafTex;
        // a leaf: an oval with a point and a stalk, in an autumn brown-orange (Blood's palette's warm browns)
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
        return _leafTex = ImageTexture.CreateFromImage(img);
    }
}
