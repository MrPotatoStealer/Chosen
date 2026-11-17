using System.Collections.Generic;
using Chosen.Data;
using Chosen.Sim;
using Godot;

namespace Chosen.Render;

/// <summary>
/// Atmosphere added to Blood's look (Options > Enhanced, each its own setting, all off by default): the light from the sky
/// (r_moonlight: one directional light with shadows, from the moon where the panorama has one; r_lightning: flashes of it in
/// rain levels, with the sky and thunder), glowing pixels (r_glow: the bright warm colours), the sky moving
/// the view's colour by area (r_areacolor), embers and smoke at fires (r_embers, r_smoke), an explosion's
/// aftermath (r_aftermath). The random numbers here are the drawing's own: the game's are never touched.
/// </summary>
public partial class Atmosphere : Node3D
{
    public static readonly Core.Cvar Moonlight = Core.Cvars.Register("r_moonlight", "1",
        "light from the sky with shadows, from the moon where the sky has one: 0 off .. 2 bright", saved: true);
    public static readonly Core.Cvar MoonFog = Core.Cvars.Register("r_moonfog", "1",
        "the fog lit by the light from the sky where it reaches (a haze, thin shafts): 0 off .. 2 strong", saved: true);
    public static readonly Core.Cvar Lightning = Core.Cvars.Register("r_lightning", "1",
        "lightning: 1 in rain levels, 2 in any level with sky", saved: true);
    public static readonly Core.Cvar Glow = Core.Cvars.Register("r_glow", "0.25",
        "glowing pixels: fire's, lamps' and lava's bright colours stay bright in the dark with a soft glow: 0 off .. 2 strong", saved: true);
    public static readonly Core.Cvar AreaColor = Core.Cvars.Register("r_areacolor", "0.5",
        "the view's colour by what the area is made of (snow cold, sewers green, lava red) and the fog's with it: 0 off .. 2 strong", saved: true);
    public static readonly Core.Cvar Embers = Core.Cvars.Register("r_embers", "1",
        "1: sparks rise from fires", saved: true);
    public static readonly Core.Cvar Smoke = Core.Cvars.Register("r_smoke", "1",
        "1: smoke drifts up from fires", saved: true);
    public static readonly Core.Cvar Aftermath = Core.Cvars.Register("r_aftermath", "1",
        "1: explosions send out a slight round ripple in the view, translucent sprites fade where they meet a wall", saved: true);

    /// <summary>Every effect of Options > Enhanced that goes on and off together (the menu's ALL ON, ALL OFF).</summary>
    static readonly string[] AllCvars =
    {
        "r_waterfog", "r_caustics", "r_waterwarp", "r_watermist", "r_waterripple", "r_watershine", "r_waterreflect",
        "r_moonlight", "r_moonfog", "r_lightflicker", "r_fogsky", "r_lightning", "r_glow", "r_areacolor", "r_embers", "r_smoke", "r_aftermath",
        "r_rainripples", "r_dust", "r_ash",
    };

    // what F10 turns off to show the difference: everything not Blood's own, and what it reads as off
    static readonly (string name, string off)[] Comparison =
    {
        ("r_dynamiclights", "0"), ("r_shadows", "0"), ("r_ao", "0"), ("b3d_shadecontrast", "1"), ("r_moonlight", "0"), ("r_moonfog", "0"), ("r_lightflicker", "0"), ("r_glow", "0"),
        ("r_waterportals", "0"), ("r_waterfog", "0"), ("r_caustics", "0"), ("r_waterwarp", "0"), ("r_watermist", "0"), ("r_waterripple", "0"), ("r_watershine", "0"), ("r_waterreflect", "0"),
        ("r_fog", "0"), ("r_fogblur", "0"), ("r_fogsky", "0"), ("r_groundmist", "0"),
        ("r_weather", "0"), ("r_lightning", "0"), ("r_rainripples", "0"), ("r_ash", "0"),
        ("r_embers", "0"), ("r_smoke", "0"), ("r_dust", "0"), ("r_areacolor", "0"), ("r_aftermath", "0"),
    };

    /// <summary>Are all the enhanced options off for a comparison (F10)?</summary>
    public static bool ComparingOff { get; private set; }

    /// <summary>All the enhanced options off, or back as they were. For now only: never saved off.</summary>
    public static void ToggleComparison()
    {
        ComparingOff = !ComparingOff;
        foreach (var (name, off) in Comparison)
        {
            var c = Core.Cvars.Find(name);
            if (c == null)
                continue;
            if (ComparingOff)
                Core.Cvars.Hold(c, off);
            else
                Core.Cvars.Release(c);
        }
    }

    public static void SetAll(bool on)
    {
        foreach (var n in AllCvars)
            Core.Cvars.Find(n)?.Set(on ? "1" : "0");
        if (on)
            Enhanced.WaterPortals.Set("1");
    }

    /// <summary>The view's colour now, for the Hud (r_areacolor).</summary>
    public static Color Grade { get; private set; } = Colors.White;
    /// <summary>Explosions' ripples on the screen, for the Hud: uv, how far out, strength (r_aftermath).</summary>
    public static readonly Vector4[] Spots = new Vector4[4];
    public static bool SpotsOn { get; private set; }
    /// <summary>Thunder (Game: the sound).</summary>
    public static System.Action Thunder;

    World _world;
    bool _hasSky, _rainLevel;
    DirectionalLight3D _sun;
    Vector3 _moonDir = new Vector3(0.4f, 0.8f, 0.45f).Normalized();
    readonly RandomNumberGenerator _rng = new();
    double _now, _nextBolt = -1, _boltAt = -100, _thunderAt = -1;
    Vector3 _boltDir;
    float _sentFlash = -1;
    bool _sentSoft;
    Color _gradeTarget = Colors.White;
    double _nextArea;
    static readonly Color PaleMoon = new(0.62f, 0.68f, 0.86f);
    /// <summary>The colour of the light from this level's sky: pale blue, or its sky's own where that has one (E4M1's red).</summary>
    public static Color MoonColour { get; private set; } = PaleMoon;
    /// <summary>How far that is from the pale moon's, as a tint (GroundMist).</summary>
    public static Color MoonTint => new(MoonColour.R / PaleMoon.R, MoonColour.G / PaleMoon.G, MoonColour.B / PaleMoon.B);
    float _skyTint;
    float _skyNow = -1;      // how much of the light from the sky the place the camera is in has, moving to it over MoonFade
    const float MoonFade = 1.5f;

    public override void _Ready()
    {
        _sun = new DirectionalLight3D
        {
            Visible = false, LightSpecular = 0, ShadowEnabled = true, DirectionalShadowMode = DirectionalLight3D.ShadowMode.Orthogonal,
            DirectionalShadowMaxDistance = 64f, LightVolumetricFogEnergy = 1f, ShadowBlur = 0.25f,
            // (Build's walls and ceilings have no thickness: Godot's default biases put a surface just under a ceiling over it,
            // lit: blue edges along E1M5's rooms)
            ShadowBias = 0.02f, ShadowNormalBias = 0.3f,
        };
        AddChild(_sun);
        Glow.Changed += ApplyGlow;
        Embers.Changed += RefreshFire;
        Smoke.Changed += RefreshFire;
        MakeFire();
        ApplyGlow();
    }

    public override void _ExitTree()
    {
        Glow.Changed -= ApplyGlow;
        Embers.Changed -= RefreshFire;
        Smoke.Changed -= RefreshFire;
    }

    /// <summary>A new level: what its sky and fires are.</summary>
    public void Setup(World world, bool rain, bool sky)
    {
        _world = world;
        _rainLevel = rain;
        _hasSky = sky;
        _nextBolt = -1;
        _skyNow = -1;
        var (tint, strength) = BuildSky.CurrentTint;
        _skyTint = _hasSky ? strength : 0f;
        // (a tint, not the sky's colour itself: all of it turned E3M1's street blood red)
        MoonColour = PaleMoon.Lerp(tint, _skyTint * 0.45f);
        world.NativeSkyFlags();
        _exploded.Clear();
        _live.Clear();
        FindFires();
        ApplyGlow();
        var m = BuildSky.CurrentMoon;
        // (over the panorama: its column and row from the east, turning as Build's angle; a low moon's shadows would miss
        // most of a level, so no lower than 30 degrees)
        Vector3 q;
        if (m.HasValue)
        {
            float yaw = -m.Value.x / Mathf.Max(BuildSky.CurrentSize.X, 1f) * Mathf.Tau;
            float tan = (BuildSky.CurrentHorizon - m.Value.y) / (BuildSky.CurrentSize.X / Mathf.Tau);
            q = new Vector3(Mathf.Cos(yaw), Mathf.Sin(yaw), Mathf.Max(tan, 0.58f));
        }
        else
            q = new Vector3(0.55f, 0.35f, 0.9f);
        _moonDir = Coords.QuakeToGodot(q).Normalized();
    }

    /// <summary>The console's fires: how many, and the first few.</summary>
    public void ListFires()
    {
        Core.GameConsole.Print($"{_fires.Count} fires");
        for (int i = 0; i < _fires.Count && i < 12; i++)
            Core.GameConsole.Print($"fire {_fires[i].ClassName} pic {_fires[i].Picnum} at {_fires[i].Origin} sector {_world.PeekSpriteBSector(_fires[i], false)}");
    }

    /// <summary>Lightning now (the console's lightning, tests).</summary>
    public void Bolt() => StartBolt();

    void StartBolt()
    {
        _boltAt = _now;
        _thunderAt = _now + _rng.RandfRange(0.25f, 2.2f);
        _nextBolt = _now + _rng.RandfRange(9f, 28f);
        float a = _rng.RandfRange(0f, Mathf.Tau);
        _boltDir = new Vector3(Mathf.Cos(a) * 0.55f, 0.85f, Mathf.Sin(a) * 0.55f).Normalized();
    }

    float Flash()
    {
        float t = (float)(_now - _boltAt);
        if (t < 0f || t > 1.2f)
            return 0f;
        // a strike and two after-flickers
        float f = Mathf.Exp(-t * 11f);
        if (t > 0.13f)
            f += 0.7f * Mathf.Exp(-(t - 0.13f) * 8f);
        if (t > 0.34f)
            f += 0.45f * Mathf.Exp(-(t - 0.34f) * 7f);
        return Mathf.Min(f, 1f);
    }

    /// <summary>Each frame, the camera placed and the sectors it sees found.</summary>
    public void Update(Camera3D cam, Vector3 camQ, int camSector, double delta)
    {
        _now = Time.GetTicksMsec() / 1000.0;
        UpdateSky(camSector, delta);
        UpdateGrade(camSector, delta);
        UpdateFire(camQ);
        UpdateExplosions(cam);
    }

    /// <summary>The light from the sky the camera's place has: all under it, less by each opening away, none further in.</summary>
    float SkyAt(int sector)
    {
        var steps = _world?.SkySteps;
        if (steps == null || sector < 0 || sector >= steps.Length)
            return 1f;
        return steps[sector] switch { 0 => 1f, 1 => 0.8f, 2 => 0.6f, _ => 0f };
    }

    void UpdateSky(int camSector, double delta)
    {
        bool bolts = Lightning.Int == 2 && _hasSky || Lightning.Int == 1 && _rainLevel;
        if (bolts)
        {
            if (_nextBolt < 0)
                _nextBolt = _now + _rng.RandfRange(3f, 10f);
            else if (_now >= _nextBolt)
                StartBolt();
        }
        if (_thunderAt > 0 && _now >= _thunderAt)
        {
            _thunderAt = -1;
            Thunder?.Invoke();
        }
        float flash = Flash();
        if (flash != _sentFlash)
        {
            _sentFlash = flash;
            RenderingServer.GlobalShaderParameterSet("sky_flash", flash);
        }
        // (over a second and a half, going out or in: it doesn't pop on at a doorway; with none, no shadow pass deep indoors)
        float want = SkyAt(camSector);
        _skyNow = _skyNow < 0 ? want : Mathf.MoveToward(_skyNow, want, (float)delta * 1f / MoonFade);
        float sky = Mathf.SmoothStep(0f, 1f, _skyNow);
        float moon = _hasSky ? Mathf.Clamp(Moonlight.Float, 0f, 2f) * 0.9f * sky : 0f;
        float energy = moon + flash * 7f;
        bool on = energy > 0.001f;
        // the fog in it: the dark fade a haze of its colour, a flash lighting it, brighter towards the moon; and more of it in the
        // volumetric fog (thin, tuned for the dynamic lights' 6 times), which shows its shadows
        float haze = Mathf.Clamp(MoonFog.Float, 0f, 2f);
        EnhancedFog.SetMoon(MoonColour * (moon * (MoonHaze + SkyHaze * _skyTint) + flash * FlashHaze) * haze, on ? MoonScatter * haze : 0f);
        _sun.LightVolumetricFogEnergy = flash > 0.02f ? 1f : 1f + MoonShafts * haze;
        if (_sun.Visible != on)
            _sun.Visible = on;
        if (!on)
            return;
        // (a flash's own direction and colour over the moon's)
        var dir = flash > 0.02f ? _boltDir : _moonDir;
        _sun.LightColor = flash > 0.02f ? new Color(0.78f, 0.84f, 1f) : MoonColour;
        _sun.LightEnergy = energy;
        _sun.GlobalTransform = new Transform3D(Basis.LookingAt(-dir, Mathf.Abs(dir.Y) > 0.98f ? Vector3.Forward : Vector3.Up), Vector3.Zero);
    }

    /// <summary>The console's moonstat.</summary>
    public string MoonStatus(int sector) => $"sector {sector} steps {(_world?.SkySteps != null && sector >= 0 && sector < _world.SkySteps.Length ? _world.SkySteps[sector] : -9)} sky now {_skyNow:0.00} energy {_sun.LightEnergy:0.00} visible {_sun.Visible} colour {MoonColour.R:0.00} {MoonColour.G:0.00} {MoonColour.B:0.00} tint {_skyTint:0.00}";

    const float SkyHaze = 0.05f;      // a coloured sky's fog: more of it, the colour shows at a distance
    const float MoonHaze = 0.03f, FlashHaze = 0.05f, MoonScatter = 0.05f, MoonShafts = 3f;

    void ApplyGlow()
    {
        float g = Mathf.Clamp(Glow.Float, 0f, 2f);
        if (g > 0f)
            ClassicPalette.Build();
        RenderingServer.GlobalShaderParameterSet("glow_amount", g);
        var env = EnhancedFog.Env;
        if (env == null)
            return;
        // (only what is over white: a wall's picture never blooms)
        env.GlowEnabled = g > 0f;
        env.GlowHdrThreshold = 1f;
        env.GlowHdrScale = 1.5f;
        env.GlowIntensity = 0.55f * g;
        env.GlowBloom = 0f;
        env.GlowBlendMode = Environment.GlowBlendModeEnum.Additive;
        for (int i = 1; i <= 7; i++)
            env.SetGlowLevel(i - 1, i is 2 or 3 or 4 ? 1f : 0f);
    }

    // ---------------------------------------------------------------- the view's colour by area
    void UpdateGrade(int s, double delta)
    {
        float amount = Mathf.Clamp(AreaColor.Float, 0f, 2f);
        if (amount <= 0f || _world?.Board == null)
        {
            if (Grade != Colors.White)
            {
                Grade = Colors.White;
                EnhancedFog.SetTint(Colors.Black);
            }
            return;
        }
        if (_now >= _nextArea)
        {
            _nextArea = _now + 0.25;
            _gradeTarget = AreaOf(s);
        }
        var want = Colors.White.Lerp(_gradeTarget, amount);
        want = new Color(Mathf.Max(want.R, 0.2f), Mathf.Max(want.G, 0.2f), Mathf.Max(want.B, 0.2f));
        // (over a second and a half, walking from one to the next)
        Grade = Grade.Lerp(want, 1f - Mathf.Exp(-(float)delta * 2f));
        EnhancedFog.SetTint(new Color(Grade.R * Grade.R, Grade.G * Grade.G, Grade.B * Grade.B) * 0.05f - new Color(0.03f, 0.03f, 0.03f, 0));
    }

    // the colour of a sector by its floor's and walls' surface
    Color AreaOf(int s)
    {
        var b = _world.Board;
        if (!b.Valid(s))
            return Colors.White;
        if (WaterFx.CameraUnder)
            return new Color(0.82f, 0.93f, 1f);
        int[] weight = new int[K.kSurfMax];
        void Add(int tile, int n)
        {
            int k = Tiles.SurfType(tile);
            if (k > 0 && k < weight.Length)
                weight[k] += n;
        }
        Add(b.Sectors[s].FloorPicnum, 4);
        foreach (int w in b.SectorWalls(s))
            Add(b.Walls[w].Picnum, 1);
        int best = 0;
        for (int k = 1; k < weight.Length; k++)
            if (weight[k] > weight[best])
                best = k;
        return best switch
        {
            K.kSurfSnow or K.kSurfIce => new Color(0.84f, 0.91f, 1f),
            K.kSurfGoo => new Color(0.82f, 1f, 0.76f),
            K.kSurfLava => new Color(1f, 0.8f, 0.64f),
            K.kSurfFlesh => new Color(1f, 0.78f, 0.78f),
            K.kSurfWater => new Color(0.84f, 0.95f, 1f),
            K.kSurfWood => new Color(1f, 0.94f, 0.84f),
            K.kSurfMetal => new Color(0.9f, 0.95f, 1f),
            K.kSurfPlant or K.kSurfLeaves => new Color(0.93f, 1f, 0.85f),
            K.kSurfDirt or K.kSurfClay => new Color(1f, 0.95f, 0.88f),
            _ => Colors.White,
        };
    }

    // ---------------------------------------------------------------- embers and smoke at fires
    const int MaxFires = 24;
    const int FlamePic = 2101;
    readonly List<Sprite> _fires = new();
    readonly Vector4[] _fireAt = new Vector4[MaxFires];
    GpuParticles3D _embers, _smoke;
    ShaderMaterial _emberProcess, _smokeProcess;
    double _nextFires;

    static string FireProcess(string rise, string sway, string spread) => @"
shader_type particles;
render_mode disable_velocity;
uniform vec4 fires[" + MaxFires + @"];     // x, y, z (Godot), 1: burning
uniform int fire_count = 0;
uniform float fire_share = 0.0;      // the share of the particles at work: the same few per fire, however many there are
float hash(uint n) {
	n = (n << 13U) ^ n;
	n = n * (n * n * 15731U + 789221U) + 1376312589U;
	return float(n & 0x7fffffffU) / float(0x7fffffff);
}
void start() {
	uint seed = RANDOM_SEED + uint(INDEX) * 7919U + NUMBER * 104729U;
	int e = fire_count > 0 ? int(hash(seed) * float(fire_count)) % fire_count : 0;
	ACTIVE = fire_count > 0 && fires[e].w > 0.5 && hash(seed + 7U) < fire_share;
	vec3 p = fires[e].xyz + vec3((hash(seed + 1U) - 0.5) * " + spread + @", hash(seed + 2U) * 0.3, (hash(seed + 3U) - 0.5) * " + spread + @");
	TRANSFORM = mat4(1.0);
	TRANSFORM[3].xyz = p;
	CUSTOM = vec4(hash(seed + 4U), hash(seed + 5U), hash(seed + 6U), 0.0);
}
void process() {
	float t = TIME * (0.8 + CUSTOM.x) + CUSTOM.y * 6.283;
	TRANSFORM[3].xyz += vec3(sin(t) * " + sway + @", " + rise + @" * (0.7 + 0.6 * CUSTOM.z), cos(t * 0.8) * " + sway + @") * DELTA;
	CUSTOM.w += DELTA / LIFETIME;
}
";

    const string EmberDraw = @"
shader_type spatial;
render_mode unshaded, blend_add, depth_draw_never, cull_disabled, shadows_disabled, fog_disabled;
global uniform float hdr_highlight;
varying flat vec4 v_c;
void vertex() {
	v_c = INSTANCE_CUSTOM;
	vec3 c = MODEL_MATRIX[3].xyz;
	vec3 r = INV_VIEW_MATRIX[0].xyz, u = INV_VIEW_MATRIX[1].xyz;
	float s = 0.03 + 0.03 * INSTANCE_CUSTOM.x;
	POSITION = PROJECTION_MATRIX * VIEW_MATRIX * vec4(c + (r * VERTEX.x + u * VERTEX.y) * s, 1.0);
}
void fragment() {
	float life = v_c.w;
	// (white hot to dull red; flickers out)
	vec3 c = mix(vec3(1.0, 0.65, 0.2), vec3(0.8, 0.12, 0.02), life);
	ALBEDO = c * max(hdr_highlight, 1.0) * (1.0 - life);
}
";

    const string SmokeDraw = @"
shader_type spatial;
render_mode unshaded, blend_mix, depth_draw_never, cull_disabled, shadows_disabled;
uniform sampler2D flake : filter_nearest;
varying flat float v_life;
void vertex() {
	v_life = INSTANCE_CUSTOM.w;
	vec3 c = MODEL_MATRIX[3].xyz;
	vec3 r = INV_VIEW_MATRIX[0].xyz, u = INV_VIEW_MATRIX[1].xyz;
	float s = 0.18 + 0.75 * INSTANCE_CUSTOM.w;
	POSITION = PROJECTION_MATRIX * VIEW_MATRIX * vec4(c + (r * VERTEX.x + u * VERTEX.y) * s, 1.0);
}
void fragment() {
	float life = v_life;
	ALBEDO = vec3(0.17, 0.16, 0.15);
	ALPHA = texture(flake, UV).r * 0.16 * smoothstep(0.0, 0.15, life) * (1.0 - life);
}
";

    // (one node each, both placed at the camera: at the same distance which went first changed from frame to frame, the embers
    // and smoke flickering over each other; so by priority: the smoke, then the embers, over the level's see-through surfaces)
    GpuParticles3D FireNode(ShaderMaterial process, string draw, int amount, float life, Texture2D flake, int priority)
    {
        var mat = new ShaderMaterial { Shader = new Shader { Code = draw }, RenderPriority = priority };
        if (flake != null)
            mat.SetShaderParameter("flake", flake);
        var n = new GpuParticles3D
        {
            Amount = amount, Lifetime = life, LocalCoords = false, ProcessMaterial = process, Emitting = false,
            DrawPass1 = new QuadMesh { Size = Vector2.One, Material = mat },
            VisibilityAabb = new Aabb(new Vector3(-80, -40, -80), new Vector3(160, 80, 160)),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(n);
        return n;
    }

    void MakeFire()
    {
        _emberProcess = new ShaderMaterial { Shader = new Shader { Code = FireProcess("0.8", "0.25", "0.4") } };
        _smokeProcess = new ShaderMaterial { Shader = new Shader { Code = FireProcess("0.55", "0.15", "0.3") } };
        // (some 5 embers and 6 puffs alive at each fire)
        _embers = FireNode(_emberProcess, EmberDraw, MaxFires * 5, 2.5f, null, 3);
        _smoke = FireNode(_smokeProcess, SmokeDraw, MaxFires * 6, 4.5f, Weather.FlakeTexture(), 2);
    }

    void RefreshFire() => _nextFires = 0;

    void FindFires()
    {
        _fires.Clear();
        _nextFires = 0;
        if (_world == null)
            return;
        foreach (var s in _world.Sprites)
            if (!s.Removed && IsFire(s))
                _fires.Add(s);
    }

    static bool IsFire(Sprite s) => s.ClassName == "decoration_torch"
        || s.Picnum >= FlamePic && s.Picnum <= FlamePic + Tiles.AnimNum(FlamePic);

    // the nearest fires the camera may see, each a place the particles start from
    void UpdateFire(Vector3 camQ)
    {
        bool embers = Embers.Bool, smoke = Smoke.Bool;
        if (_embers.Emitting != embers)
            _embers.Emitting = embers;
        if (_smoke.Emitting != smoke)
            _smoke.Emitting = smoke;
        if (!embers && !smoke || _now < _nextFires)
            return;
        _nextFires = _now + 0.25;
        var pos = Coords.QuakeToGodot(camQ);
        _embers.GlobalPosition = _smoke.GlobalPosition = pos;
        var near = new List<(float d, Sprite s)>();
        foreach (var s in _fires)
        {
            if (s.Removed)
                continue;
            // (a torch put out is another picture)
            if (s.ClassName == "decoration_torch" && s.HasX && s.XState == 0)
                continue;
            float d = (s.Origin - camQ).LengthSquared();
            if (d > 1200f * 1200f)
                continue;
            int b = _world.PeekSpriteBSector(s, false);
            if (b >= 0 && !SectorVisibility.Seen(b))
                continue;
            near.Add((d, s));
        }
        near.Sort((a, b) => a.d.CompareTo(b.d));
        int n = Mathf.Min(near.Count, MaxFires);
        for (int i = 0; i < MaxFires; i++)
            _fireAt[i] = i < n ? ToFire(near[i].s) : Vector4.Zero;
        foreach (var m in new[] { _emberProcess, _smokeProcess })
        {
            m.SetShaderParameter("fires", _fireAt);
            m.SetShaderParameter("fire_count", n);
            m.SetShaderParameter("fire_share", (float)n / MaxFires);
        }
    }

    static Vector4 ToFire(Sprite s)
    {
        var g = Coords.QuakeToGodot(s.Origin + new Vector3(0, 0, s.ClassName == "decoration_torch" ? 24f : 12f));
        return new Vector4(g.X, g.Y, g.Z, 1f);
    }

    // ---------------------------------------------------------------- explosions
    const float RippleLife = 0.45f;
    readonly HashSet<Sprite> _exploded = new(), _stillHere = new();
    readonly List<(Vector3 at, float size, double t0)> _live = new();

    void UpdateExplosions(Camera3D cam)
    {
        bool on = Aftermath.Bool;
        if (on != _sentSoft)
        {
            _sentSoft = on;
            RenderingServer.GlobalShaderParameterSet("soft_sprites", on ? 1f : 0f);
        }
        if (!on || _world == null)
        {
            SpotsOn = false;
            _live.Clear();
            return;
        }
        _stillHere.Clear();
        foreach (var s in _world.Sprites)
        {
            if (s.Removed || s.StatNum != K.kStatExplosion)
                continue;
            _stillHere.Add(s);
            if (_exploded.Add(s))
                Explode(s);
        }
        _exploded.IntersectWith(_stillHere);
        // the ripples on the screen
        System.Array.Clear(Spots);
        int spot = 0;
        for (int i = _live.Count - 1; i >= 0; i--)
        {
            var e = _live[i];
            float t = (float)(_now - e.t0);
            if (t > RippleLife)
            {
                _live.RemoveAt(i);
                continue;
            }
            if (spot < Spots.Length && !cam.IsPositionBehind(e.at))
            {
                var size = GetViewport().GetVisibleRect().Size;
                var c = cam.UnprojectPosition(e.at);
                var edge = cam.UnprojectPosition(e.at + cam.GlobalBasis.X * e.size);
                float radius = Mathf.Clamp((edge - c).Length() / size.Y, 0.04f, 0.6f);
                // out fast, slowing; fading as it goes
                float a = t / RippleLife, grown = 1f - (1f - a) * (1f - a);
                Spots[spot++] = new Vector4(c.X / size.X, c.Y / size.Y, radius * Mathf.Lerp(0.15f, 1f, grown), 0.006f * (1f - a) * Mathf.Min(radius * 8f, 1f));
            }
        }
        SpotsOn = spot > 0;
    }

    void Explode(Sprite s) => Explode(s.Origin, _world.PeekSpriteBSector(s, false), Mathf.Max(s.XRepeat, 32));

    /// <summary>An explosion's aftermath in front of the camera (the console's boom; tests).</summary>
    public void Boom(Camera3D cam, int sector)
    {
        // (level with the eye: the sector really there, as an explosion's own)
        var f = -cam.GlobalBasis.Z;
        f.Y = 0f;
        var q = Coords.GodotToQuake(cam.GlobalPosition + f.Normalized() * 5f);
        int at = _world.SectorOfPointB(q.X * 16, -q.Y * 16, -q.Z * 256, sector);
        if (at < 0)
            at = sector;
        Explode(new Vector3(q.X, q.Y, _world.Map.Valid(at) ? _world.SectorFloorZ(at, new Vector2(q.X, q.Y)) + 8f : q.Z), at, 64);
    }

    void Explode(Vector3 origin, int b, int repeat)
    {
        // (one in a room out of sight sends none through the walls)
        if (b >= 0 && !SectorVisibility.Seen(b))
            return;
        _live.Add((Coords.QuakeToGodot(origin + new Vector3(0, 0, 16)), 5f * repeat / 64f, _now));
    }
}
