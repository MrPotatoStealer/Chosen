using Chosen.Data;
using Godot;

namespace Chosen.Render;

/// <summary>
/// Water beyond Blood's look (Options > Enhanced > Water, each its own setting, all off by default). In the water, from
/// inside or from above through a see-through surface: the fog (r_waterfog) and the moving light (r_caustics) go by how much
/// of the line of sight lies under the surface, which a fragment of a portal's copy can tell from the portal's plane
/// (water_path). The view under water moves as Quake's does (r_waterwarp: the Hud's first pass, and the field of view
/// breathing). On the surface: ripples (r_waterripple) in steps of the picture's own pixels, the sky at a low angle
/// (r_watershine) and the level reflected by a march over the depth buffer (r_waterreflect: a shader of its own, as reading the screen costs even
/// unused). The mist over the surface is GroundMist's (r_watermist).
/// </summary>
public static class WaterFx
{
    public static readonly Core.Cvar Warp = Core.Cvars.Register("r_waterwarp", "1",
        "under water the view moves as Quake's: 0 off .. 2 strong", saved: true);
    public static readonly Core.Cvar Fog = Core.Cvars.Register("r_waterfog", "1",
        "fog in the water, from inside and through a see-through surface: 0 off .. 4 thick", saved: true);
    public static readonly Core.Cvar Caustics = Core.Cvars.Register("r_caustics", "1",
        "1: moving light on what lies under water", saved: true);
    public static readonly Core.Cvar Ripple = Core.Cvars.Register("r_waterripple", "1",
        "1: water surfaces ripple", saved: true);
    public static readonly Core.Cvar Shine = Core.Cvars.Register("r_watershine", "1",
        "1: see-through water is clear looking down and shows the sky at a low angle", saved: true);
    public static readonly Core.Cvar Reflect = Core.Cvars.Register("r_waterreflect", "1",
        "1: see-through water reflects the level at a low angle", saved: true);

    const float HalfAt = 9.4f;      // Godot units of water to half hidden at r_waterfog 1 (300 Quake units)

    static WaterFx()
    {
        Fog.Changed += Send;
        Caustics.Changed += Send;
        Ripple.Changed += Send;
        Shine.Changed += Send;
        Reflect.Changed += Send;
        Send();
    }

    /// <summary>Registers the cvars (the menu and console find them) and sends their shader values.</summary>
    public static void Init() { }

    static void Send()
    {
        RenderingServer.GlobalShaderParameterSet("water_fog", Mathf.Max(Fog.Float, 0f) * Mathf.Log(2f) / HalfAt);
        RenderingServer.GlobalShaderParameterSet("water_caustics", Caustics.Bool ? 1f : 0f);
        RenderingServer.GlobalShaderParameterSet("water_ripple", Ripple.Bool ? 1f : 0f);
        RenderingServer.GlobalShaderParameterSet("water_shine", Shine.Bool || Reflect.Bool ? 1f : 0f);
    }

    /// <summary>The shaders' side: how far along the line of sight a fragment lies in water, and that applied.</summary>
    public const string Functions = @"
global uniform float water_fog;
global uniform vec3 water_fog_color;
global uniform float water_caustics;
global uniform float camera_under_water;
// the water's own clock (seconds, wrapping each hour), never TIME: a shader that reads TIME and can discard is an animated
// material to Godot, and a light with one among its shadow casters redraws its shadow map every frame (every world chunk
// and voxel model was one: each shadowed light redrew its cube's six faces every frame, still or not)
global uniform float water_time;
// the line of sight's length in water, eye to w. A copy through a water portal (its row's w 1): the part past a floor portal, or
// short of a ceiling portal; the level itself, or a copy through a link joining two rooms under water (w 2), with the eye
// under water: all of it.
float water_path(vec3 w, vec3 eye, float copy) {
	if (copy > 0.5) {
		vec4 h = texelFetch(portal_data, ivec2(0, int(copy + 0.5) - 1), 0);
		if (h.w > 1.5 && h.w < 2.5)
			return camera_under_water > 0.5 ? length(w - eye) : 0.0;
		if (h.w < 0.5 || h.w > 2.5)
			return 0.0;
		float de = (eye.y - h.x) * h.y, dw = (w.y - h.x) * h.y;
		if (de <= 0.0 || dw >= 0.0)
			return 0.0;
		float len = length(w - eye), beyond = len * -dw / (de - dw);
		return h.y > 0.0 ? beyond : len - beyond;
	}
	return camera_under_water > 0.5 ? length(w - eye) : 0.0;
}
// two moving lattices, in steps a little bigger than the level's pixels
float water_caustic(vec3 w) {
	vec2 p = floor(w.xz * 8.0) / 8.0;
	float a = sin(p.x * 2.7 + water_time * 0.9 + 1.7 * sin(p.y * 1.9 + water_time * 0.6));
	float b = sin(p.y * 3.1 - water_time * 0.8 + 1.7 * sin(p.x * 2.3 - water_time * 0.5));
	float v = 1.0 - abs(a + b) * 0.5;
	return v * v * v * v;
}
";

    /// <summary>Over EMISSION and ALBEDO at the end of a lit shader's fragment (light_world set; copy: its portal copy).</summary>
    public static string Apply(string copy) => @"
	if (water_fog + water_caustics > 0.0) {
		float wp = water_path(light_world, CAMERA_POSITION_WORLD, " + copy + @");
		if (wp > 0.0) {
			EMISSION *= 1.0 + 0.9 * water_caustics * water_caustic(light_world);
			float wf = 1.0 - exp(-wp * water_fog);
			EMISSION = mix(EMISSION, water_fog_color, wf);
			ALBEDO *= 1.0 - wf;
		}
	}
";

    /// <summary>A water surface's picture rippled (uv and UV, tile_info in the world shader).</summary>
    public const string RippleCode = @"
	if (water_ripple > 0.5) {
		vec2 g = floor(UV * tile_info.xy) / tile_info.xy;
		uv += 0.018 * vec2(sin(g.y * 11.0 + water_time * 1.3) + sin(g.x * 5.0 + water_time * 0.7), cos(g.x * 9.0 + water_time * 1.1) + cos(g.y * 6.0 - water_time * 0.8));
	}
";

    /// <summary>The level along a reflected line, marched over the depth buffer; alpha 0: it left the screen or found nothing.</summary>
    public const string ReflectFunctions = @"
uniform sampler2D water_screen : hint_screen_texture, filter_nearest, repeat_disable;
uniform sampler2D water_depth : hint_depth_texture, filter_nearest, repeat_disable;
vec4 water_march(vec3 w, vec3 r, mat4 view, mat4 proj, mat4 inv_proj) {
	float t = 0.4;
	for (int i = 0; i < 16; i++) {
		vec4 v = view * vec4(w + r * t, 1.0);
		vec4 c = proj * v;
		if (c.w <= 0.0)
			break;
		vec2 uv = c.xy / c.w * 0.5 + 0.5;
		if (uv.x < 0.0 || uv.y < 0.0 || uv.x > 1.0 || uv.y > 1.0)
			break;
		vec4 s = inv_proj * vec4(uv * 2.0 - 1.0, texture(water_depth, uv).r, 1.0);
		float sz = s.z / s.w;
		if (v.z < sz) {
			if (sz - v.z < t * 0.5 + 0.4)
				return vec4(texture(water_screen, uv).rgb, 1.0);
			break;
		}
		t = t * 1.3 + 0.1;
	}
	return vec4(0.0);
}
";

    /// <summary>The surface at the end of the water shader's fragment: reflecting at a low angle (a is its alpha so far).</summary>
    public static string SurfaceCode(bool march) => @"
	float a = t.a * water_alpha;
	if (water_shine > 0.5 && camera_under_water < 0.5) {
		vec3 v = normalize(CAMERA_POSITION_WORLD - light_world);
		float fres = pow(1.0 - clamp(v.y, 0.0, 1.0), 3.0);
		vec3 r = normalize(vec3(-v.x, abs(v.y), -v.z));
		// (under a roof no sky: only the surface's own colour, darker)
		vec3 seen = v_sky > 0.5 ? (sky_fog > 0.5 ? build_sky_fog(build_sky(r), sky_shade) : build_color(build_sky(r), sky_shade, 0.0)) : EMISSION * 0.45;
		" + (march ? @"vec4 hit = water_march(light_world, r, VIEW_MATRIX, PROJECTION_MATRIX, INV_PROJECTION_MATRIX);
		if (hit.a > 0.5)
			seen = hit.rgb;
		" : "") + @"
		EMISSION = mix(EMISSION, seen, fres);
		ALBEDO *= 1.0 - fres;
		a = mix(a, 1.0, fres);
	}
	ALPHA = a;
";

    static Shader _warp;

    /// <summary>The Hud's first pass, over the 3D view and under the weapon: the view moved, under water and in the round
    /// ripple an explosion sends out.</summary>
    public static Shader WarpShader => _warp ??= new Shader { Code = @"
shader_type canvas_item;
uniform sampler2D screen : hint_screen_texture, filter_nearest, repeat_disable;
uniform float warp = 0.0;
// up to four explosions' ripples: where (screen uv), how far out it is now (a share of the height), how strong
uniform vec4 spots[4];
void fragment() {
	vec2 uv = SCREEN_UV;
	if (warp > 0.0) {
		// (zoomed by the swing, so no edge of the picture comes in)
		float a = warp * 0.007;
		uv = 0.5 + (uv - 0.5) * (1.0 - 2.0 * a);
		uv += a * vec2(sin(SCREEN_UV.y * 17.0 + TIME * 2.4), cos(SCREEN_UV.x * 13.0 + TIME * 1.9));
	}
	float aspect = SCREEN_PIXEL_SIZE.y / SCREEN_PIXEL_SIZE.x;
	for (int i = 0; i < 4; i++) {
		if (spots[i].w <= 0.0)
			continue;
		vec2 d = SCREEN_UV - spots[i].xy;
		d.x *= aspect;
		float len = max(length(d), 1e-4), r = len / spots[i].z;
		// a band pushing outwards, soft on both sides
		float band = smoothstep(0.55, 1.0, r) * (1.0 - smoothstep(1.0, 1.25, r));
		uv -= d / len * vec2(1.0 / aspect, 1.0) * spots[i].w * band;
	}
	COLOR = vec4(texture(screen, uv).rgb, 1.0);
}
" };

    /// <summary>Is the eye in water this frame?</summary>
    public static bool CameraUnder { get; private set; }
    static float _sentUnder = -1f;
    static Color _surface = new(0.02f, 0.05f, 0.09f);
    static Color _sentColour;
    static int _sentMedium = -1;

    /// <summary>A level's links set: the colour of its water, from the commonest surface's picture and shade.</summary>
    public static void SetLevel(System.Collections.Generic.IEnumerable<Sim.Collision.Link> links, Sim.Build.Board b)
    {
        _surface = new Color(0.02f, 0.05f, 0.09f);
        _sentMedium = -1;
        var count = new System.Collections.Generic.Dictionary<int, int>();
        int shade = 0;
        foreach (var l in links)
        {
            if (l.Dir <= 0 || !Enhanced.IsWaterLink(l.Type) || !b.Valid(l.Sector))
                continue;
            int tile = b.Sectors[l.Sector].FloorPicnum;
            count.TryGetValue(tile, out int n);
            count[tile] = n + 1;
            shade = b.Sectors[l.Sector].FloorShade;
        }
        int best = -1, bestN = 0;
        foreach (var (tile, n) in count)
            if (n > bestN)
                (best, bestN) = (tile, n);
        if (best < 0)
            return;
        var img = TileTextures.Get(best)?.GetImage();
        if (img == null || img.IsEmpty())
            return;
        if (img.IsCompressed())
            img.Decompress();
        float r = 0, g = 0, bl = 0;
        int w = img.GetWidth(), h = img.GetHeight(), seen = 0;
        for (int y = 0; y < h; y += System.Math.Max(1, h / 16))
            for (int x = 0; x < w; x += System.Math.Max(1, w / 16))
            {
                var c = img.GetPixel(x, y).SrgbToLinear();
                r += c.R;
                g += c.G;
                bl += c.B;
                seen++;
            }
        // (the picture's mean, at the surface's shade, a little deeper)
        float k = Mathf.Pow(Mathf.Clamp(1f - shade / 64f, 0f, 1f), 2.2f) * 0.5f / Mathf.Max(seen, 1);
        _surface = new Color(r * k, g * k, bl * k);
    }

    /// <summary>Each frame, the eye placed: in water or not (medium: Player's), and how the view breathes (a share of the
    /// field of view).</summary>
    public static float Update(bool under, int medium)
    {
        CameraUnder = under;
        RenderingServer.GlobalShaderParameterSet("water_time", (float)(Time.GetTicksMsec() % 3600000 / 1000.0));
        float u = under ? 1f : 0f;
        if (u != _sentUnder)
        {
            _sentUnder = u;
            RenderingServer.GlobalShaderParameterSet("camera_under_water", u);
        }
        if (medium != _sentMedium || _surface != _sentColour)
        {
            _sentMedium = medium;
            _sentColour = _surface;
            var colour = medium == Sim.Player.MediumGoo ? new Color(0.03f, 0.06f, 0.015f) : _surface;
            RenderingServer.GlobalShaderParameterSet("water_fog_color", new Vector3(colour.R, colour.G, colour.B));
        }
        return under ? Mathf.Max(Warp.Float, 0f) * 0.012f * Mathf.Sin((float)(Time.GetTicksMsec() / 1000.0) * 1.6f) : 0f;
    }
}
