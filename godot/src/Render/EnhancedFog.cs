using Godot;

namespace Chosen.Render;

/// <summary>
/// Enhanced fog (Options > Enhanced, r_fog) in place of Build's distance fades: Godot's depth fog for the fade (per pixel,
/// on every material as it is drawn, the translucent ones too: water, glass and translucent sprites fog as the walls
/// behind them do) and a thin volumetric fog the dynamic lights scatter in (their glow). With it on, the shaders'
/// distance part of the shade is off (build_visibility 0, BuildShading.RefreshVisibility): the sectors' own shades
/// stay, the fog makes the fade, on Build's curve (FullAt): dark for a Blood level, white for one whose sky has palette 1
/// (Death Wish's mist). A map's worldspawn can set it: _fogcolor (r g b, 0..255) and _fogdistance (Quake
/// units to where it is complete).
/// </summary>
public static class EnhancedFog
{
    public static readonly Core.Cvar Fog = Core.Cvars.Register("r_fog", "1",
        "1: enhanced fog - volumetric fog in place of Build's distance fades (dark, or white in misty levels), lights glowing in it", saved: true);

    public static readonly Core.Cvar Density = Core.Cvars.Register("r_fogdensity", "1",
        "enhanced fog: its density, times the level's (1: Build's fade)", saved: true);

    public static readonly Core.Cvar Glow = Core.Cvars.Register("r_fogglow", "1",
        "enhanced fog: how much the dynamic lights light it (their volumetric fog energy)", saved: true);

    public static readonly Core.Cvar Blur = Core.Cvars.Register("r_fogblur", "1",
        "enhanced fog: blur what the fog hides (far depth of field from where it is a third thick)", saved: true);

    public static readonly Core.Cvar SkyFog = Core.Cvars.Register("r_fogsky", "1",
        "the sky blends into the fog: its veil over the sky, its colour along the horizon: 0 off .. 2 strong", saved: true);

    // the level's: Build's visibility (0: none known), misty (palette 1 sky), and a map's own colour and density
    static int _visibility;
    static bool _mist;
    static Color? _color;
    static float _distance = -1;
    static WorldEnvironment _env;
    static Color _tint = Colors.Black;
    static Color _moon = Colors.Black;
    static float _scatter;

    // the camera's area's fog (AreaAtmosphere: its atmosphere's distance, -1 the level's, and its own colour, null the
    // level's), faded into from the fog as it was over the atmosphere's seconds
    struct AreaFog
    {
        public float Distance;
        public Color? Colour;
    }
    static AreaFog _from = new() { Distance = -1 }, _to = new() { Distance = -1 };
    static float _t = 1, _fade = 1;
    static bool _snap = true;

    /// <summary>The camera's area's fog (AreaAtmosphere.FogOf: where it is complete, Quake units, -1 the level's; its
    /// own colour, null the level's), faded into over fade seconds (at once on a new level).</summary>
    public static void SetArea(float distance, Color? colour, float fade)
    {
        if (!_snap && _to.Distance == distance && _to.Colour == colour)
            return;
        // (from the fog as it is now, mid-way through a fade too)
        _from = new AreaFog { Distance = EndNow(), Colour = ColourNow() };
        _to = new AreaFog { Distance = distance, Colour = colour };
        _fade = Mathf.Max(fade, 0f);
        _t = _snap || _fade <= 0 ? 1 : 0;
        _snap = false;
        Apply();
    }

    /// <summary>Each frame: a fade between areas' fogs on.</summary>
    public static void Update(double delta)
    {
        if (_t >= 1)
            return;
        _t = Mathf.Min(1f, _t + (float)delta / Mathf.Max(_fade, 0.01f));
        Apply();
    }

    // the level's own fog's end and colour, and an area's
    static float LevelEnd()
    {
        if (_distance > 0)
            return _distance;
        float v = (_visibility > 0 ? _visibility : 512) / 1048576f * BuildShading.VisScaleFactor;
        return (_mist ? 1.97f : 4f) / Mathf.Max(v, 1e-9f);
    }

    static Color LevelColour => _color ?? (_mist ? Colors.White : DarkColour);
    static float EndOf(AreaFog f) => f.Distance > 0 ? f.Distance : LevelEnd();
    static Color ColourOf(AreaFog f) => f.Colour ?? LevelColour;
    // (the level's own misty white thinner: Build's whitening reaches 0.867)
    static float DensityOf(AreaFog f) => f.Colour == null && _mist && _color == null ? 0.82f : 1f;
    static float CurveOf(AreaFog f) => ColourOf(f).Luminance > 0.5f ? 1.4f : 0.53f;

    // the fog now (before r_fogdensity): its end blended along its logarithm, its colour, density and curve
    static float EndNow() => _t >= 1 ? EndOf(_to) : Mathf.Exp(Mathf.Lerp(Mathf.Log(EndOf(_from)), Mathf.Log(EndOf(_to)), _t));
    static Color ColourNow() => _t >= 1 ? ColourOf(_to) : ColourOf(_from).Lerp(ColourOf(_to), _t);

    /// <summary>The level's environment (Atmosphere's glow).</summary>
    public static Environment Env => _env?.Environment;

    /// <summary>The fog's colour where it is dark, by area (Atmosphere.AreaColor).</summary>
    public static void SetTint(Color c)
    {
        c = new Color(Mathf.Max(c.R, 0f), Mathf.Max(c.G, 0f), Mathf.Max(c.B, 0f));
        if (c == _tint)
            return;
        _tint = c;
        SendColour();
    }

    /// <summary>What the light from the sky adds to the dark fog where it reaches (Atmosphere, r_moonfog), and how much the fog
    /// brightens towards the moon.</summary>
    public static void SetMoon(Color c, float scatter)
    {
        if (c == _moon && scatter == _scatter)
            return;
        _moon = c;
        _scatter = scatter;
        SendColour();
    }

    static Color DarkColour => new(_tint.R + _moon.R, _tint.G + _moon.G, _tint.B + _moon.B);

    static void SendColour()
    {
        var env = Env;
        if (env == null || !Fog.Bool)
            return;
        env.FogSunScatter = _scatter;
        env.FogLightColor = ColourNow();
        SendHaze();
    }

    // the fog's colour for the sky's horizon (its shader wants it linear, as Godot makes the fog's)
    static void SendHaze()
    {
        var c = ColourNow().SrgbToLinear();
        RenderingServer.GlobalShaderParameterSet("sky_haze", new Vector4(c.R, c.G, c.B, Fog.Bool ? Mathf.Clamp(SkyFog.Float, 0f, 2f) * HorizonHaze : 0f));
    }

    const float HorizonHaze = 0.55f, SkyVeil = 0.3f;

    /// <summary>Raised when the fog's glow changes (the dynamic lights' volumetric fog energy).</summary>
    public static event System.Action GlowChanged;

    static EnhancedFog()
    {
        Fog.Changed += Changed;
        Density.Changed += Apply;
        SkyFog.Changed += Apply;
        Blur.Changed += Apply;
        Glow.Changed += () =>
        {
            Apply();
            GlowChanged?.Invoke();
        };
    }

    static void Changed()
    {
        BuildShading.RefreshVisibility();
        Apply();
        GlowChanged?.Invoke();
    }

    /// <summary>Registers the cvars (the menu and console find them).</summary>
    public static void Init() { }

    /// <summary>The dynamic lights' volumetric fog energy (0 with the fog and the ground mist off: no cost).</summary>
    public static float LightEnergy => Fog.Bool || GroundMist.AnyOn ? Mathf.Max(Glow.Float, 0f) * GlowScale : 0f;

    /// <summary>Is the level misty (white fog: Death Wish's palette-1 sky)?</summary>
    public static bool MistLevel => _color.HasValue ? _color.Value.Luminance > 0.5f : _mist;

    /// <summary>The environment again (the ground mist turned on or off needs its volumetric fog).</summary>
    public static void Refresh()
    {
        Apply();
        GlowChanged?.Invoke();
    }

    // the lights' share in the fog at r_fogglow 1 (the fog is thin: Build's fade reaches half at thousands of units)
    const float GlowScale = 6f;

    /// <summary>A level built: its environment and what its fog is made from.</summary>
    public static void SetLevel(WorldEnvironment env, int visibility, bool mist, Map.MapEntity worldspawn)
    {
        _env = env;
        _visibility = visibility;
        _mist = mist;
        _color = null;
        _distance = -1;
        _from = _to = new AreaFog { Distance = -1 };
        _t = 1;
        _snap = true;
        if (worldspawn != null)
        {
            if (worldspawn.Keys.ContainsKey("_fogcolor"))
            {
                var c = Sim.XObject.ParseVec(worldspawn.Get("_fogcolor"));
                _color = new Color(c.X / 255f, c.Y / 255f, c.Z / 255f);
            }
            if (worldspawn.Keys.ContainsKey("_fogdistance"))
                _distance = (float)Sim.XObject.ParseD(worldspawn.Get("_fogdistance"));
        }
        Apply();
    }

    /// <summary>Is Build's distance fade replaced (the shaders' build_visibility 0)?</summary>
    public static bool ReplacesFade => Fog.Bool;

    /// <summary>
    /// Where the fade is complete (Quake units): Build's to black is the brightness (1 - shade / 64)^2.2 with shade =
    /// distance x visibility x 16, black at 4 / visibility; its palette-1 mist whitens 0.867 x (2 x shade) / 63, linearly
    /// to 0.867 at 1.97 / visibility. A map's _fogdistance instead; r_fogdensity brings it nearer.
    /// </summary>
    static float FullAt() => EndNow() / Mathf.Max(Density.Float, 0.05f);

    static void Apply()
    {
        var env = _env?.Environment;
        if (env == null)
            return;
        bool on = Fog.Bool;
        env.FogEnabled = on;
        env.VolumetricFogEnabled = (on && Glow.Float > 0) || GroundMist.AnyOn;
        SendHaze();
        if (!on && GroundMist.AnyOn)
        {
            // the ground mist alone: its volume's density only
            env.VolumetricFogDensity = 0f;
            env.VolumetricFogAlbedo = Colors.White;
            env.VolumetricFogEmissionEnergy = 0f;
            env.VolumetricFogAmbientInject = 0f;
            env.VolumetricFogGIInject = 0f;
            env.VolumetricFogSkyAffect = Mathf.Clamp(SkyFog.Float * SkyVeil, 0f, 1f);
            env.VolumetricFogLength = 2048f / Sim.Coords.QuakeScale;
            env.VolumetricFogDetailSpread = 2f;
            env.VolumetricFogTemporalReprojectionEnabled = true;
            env.VolumetricFogTemporalReprojectionAmount = 0.8f;
        }
        if (on)
        {
            float end = FullAt();
            var colour = ColourNow();
            // the fade: depth fog, per pixel on every material (translucent ones too), Build's curve: its fade to black
            // (in linear light, as the shaders darken) halves at a quarter of the way (the curve's power fitted there and
            // at 0.6: 0.53); the mist is a blend toward white in the palette's colours, linear to 0.867: in linear light
            // that is about its power 1.4 (0.2 -> 0.11, 0.5 -> 0.38, 0.867 -> 0.82; linear, near things were washed out)
            env.FogMode = Environment.FogModeEnum.Depth;
            env.FogLightColor = colour;
            env.FogLightEnergy = 1f;
            env.FogDensity = _t >= 1 ? DensityOf(_to) : Mathf.Lerp(DensityOf(_from), DensityOf(_to), _t);
            env.FogDepthBegin = 0f;
            env.FogDepthEnd = end / Sim.Coords.QuakeScale;
            env.FogDepthCurve = _t >= 1 ? CurveOf(_to) : Mathf.Lerp(CurveOf(_from), CurveOf(_to), _t);
            env.FogSkyAffect = 0f;       // Build's sky is never faded (a misty level's is pale already)
            env.FogSunScatter = _scatter;
            env.FogAerialPerspective = 0f;
            // the lights' glow: a thin volumetric fog (a sixth of the light gone at the fade's end, no light of its own),
            // the dynamic lights scattering in it
            env.VolumetricFogDensity = 0.18f / end * Sim.Coords.QuakeScale;
            env.VolumetricFogAlbedo = Colors.White;
            env.VolumetricFogEmissionEnergy = 0f;
            env.VolumetricFogAmbientInject = 0f;
            env.VolumetricFogGIInject = 0f;
            env.VolumetricFogSkyAffect = Mathf.Clamp(SkyFog.Float * SkyVeil, 0f, 1f);
            env.VolumetricFogAnisotropy = 0.2f;
            // (with the ground mist no further than 2048: its thin layer needs the depth slices near)
            env.VolumetricFogLength = Mathf.Clamp(end, 512f, GroundMist.AnyOn ? 2048f : 8192f) / Sim.Coords.QuakeScale;
            env.VolumetricFogDetailSpread = 2f;
            env.VolumetricFogTemporalReprojectionEnabled = true;
            env.VolumetricFogTemporalReprojectionAmount = 0.8f;
        }
        ApplyBlur(on);
    }

    static bool _blurSet;

    static void ApplyBlur(bool on)
    {
        if (_env == null)
            return;
        if (!on || !Blur.Bool)
        {
            if (_env.CameraAttributes is CameraAttributesPractical cur)
                cur.DofBlurFarEnabled = false;
            return;
        }
        // (the cheapest blur: this slight one over the far fog looks the same with it, and Godot's default, a hexagon
        // of medium quality, took 0.7 ms of a 7 ms frame at 3440x1440: report 20261115-161600)
        if (!_blurSet)
        {
            _blurSet = true;
            RenderingServer.CameraAttributesSetDofBlurBokehShape(RenderingServer.DofBokehShape.Box);
            RenderingServer.CameraAttributesSetDofBlurQuality(RenderingServer.DofBlurQuality.Low, false);
        }
        var ca = _env.CameraAttributes as CameraAttributesPractical ?? new CameraAttributesPractical();
        // from a third of the way to where the fade is complete, fully blurred at nine tenths (from a fifth, DWE3M1's
        // mist blurred the water under the camera)
        float end = FullAt(), from = 0.33f * end, to = 0.9f * end;
        ca.DofBlurFarEnabled = true;
        ca.DofBlurFarDistance = from / Sim.Coords.QuakeScale;
        ca.DofBlurFarTransition = (to - from) / Sim.Coords.QuakeScale;
        ca.DofBlurAmount = 0.06f;
        _env.CameraAttributes = ca;
    }
}
