using System.Collections.Generic;
using System.Globalization;
using Godot;

namespace Chosen.Render;

/// <summary>
/// Blood's parallax sky, drawn from its panorama like Polymost: the panorama wraps once around the view (Build angle 0
/// = Quake +x, turning towards -y), its rows spaced like its columns at the horizon, linear in the tangent of the
/// elevation; above its top its cloud rows continue as a flat cloud layer, below its bottom the rows fade to their
/// average colour. The level's sky faces (Render.Native: parallax ceilings and floors) draw it from the eye's direction;
/// the same sky fills the background. The sky is shaded by the level's sky shade (the commonest ceiling shade of its
/// parallax sectors: MARROW's night skies at 35 to 45, Blood's own at 0), one for all of it as a background is.
/// Build's way: Polymost's sky covers the walls and floors its walk reaches behind a parallax ceiling but never the
/// sprites (it lies at the far end of the depth range): a tree or a voxel rising past a sky ceiling stays whole
/// (Cryptic Passage's CP09, Death Wish's DWE1M1). Here the sky faces draw after the level without depth of their own,
/// over the level behind them, but not over the pixels sprites and voxels marked (SpriteMark: stencil 3; the sky leaves
/// 2, which blob shadows leave alone: UnderSky).
/// </summary>
public static class BuildSky
{
    /// <summary>0: the faces draw nothing, the background shows, and
    /// what the walk doesn't reach is hidden instead (NativeWorld.SendSeen); 1: they paint the sky over what lies behind them.</summary>
    public static readonly Core.Cvar Faces = Core.Cvars.Register("r_skyfaces", "0",
        "sky: 0 nothing drawn on sky faces, the background shows, the walk hides what it doesn't reach; 1 they paint over what lies behind them (the old way)");

    static BuildSky() => Faces.Changed += () => RenderingServer.GlobalShaderParameterSet("sky_faces", Faces.Float);

    public const string Mapping = @"
global uniform sampler2D sky_pano : source_color, filter_nearest, repeat_enable;
global uniform float sky_horizon;      // panorama row at eye level
global uniform vec2 sky_pano_size;
global uniform vec3 sky_top_colour;
global uniform vec3 sky_bottom_colour;
global uniform float sky_flash;        // lightning
global uniform vec4 sky_haze;          // r_fogsky: the fog's colour, how much of it lies along the horizon

// the fog's colour along the horizon, gone by a third of the way up: the sky itself is never fogged, and far walls faded by
// the fog stood out against it
vec3 build_sky_haze(vec3 c, vec3 g) {
	if (sky_haze.a <= 0.0)
		return c;
	float h = 1.0 - clamp(abs(g.y) / 0.45, 0.0, 1.0);
	return mix(c, sky_haze.rgb, clamp(sky_haze.a * h * h, 0.0, 1.0));
}

vec3 build_sky(vec3 g) {
	vec3 q = vec3(g.z, g.x, g.y);          // Godot direction to Quake
	float yaw = atan(q.y, q.x);
	float hyp = length(q.xy);
	float scale = sky_pano_size.x / (2.0 * PI);
	float tanv = q.z / max(hyp, 1e-6);
	float row = sky_horizon - tanv * scale;
	float u = fract(-yaw / (2.0 * PI));
	float h = sky_pano_size.y - 1.0;
	float v = (clamp(floor(row), 0.0, h) + 0.5) / sky_pano_size.y;
	vec3 c = textureLod(sky_pano, vec2(u, v), 0.0).rgb;
	// Blood's look never sees past the panorama's top; a tilted camera does. There the panorama's cloud rows (its top
	// half) continue as a flat cloud layer overhead, tiled and mirrored (no seams, and no pole where the columns would
	// fan out), blending in before the top row is reached; below its bottom the rows fade to their average colour.
	if (q.z > 0.0) {
		vec2 p = q.xy / q.z * 0.35;
		float band = max(sky_horizon * 0.5, 1.0);
		float cu = fract(p.x * 2.0 * band / sky_pano_size.x);      // as many columns as rows per unit: square texels
		float cr = band * (1.0 - abs(1.0 - 2.0 * fract(p.y)));
		vec3 cloud = textureLod(sky_pano, vec2(cu, (floor(cr) + 0.5) / sky_pano_size.y), 0.0).rgb;
		c = mix(c, cloud, clamp((48.0 - row) / 40.0, 0.0, 1.0));
	}
	c = mix(c, sky_bottom_colour, clamp((row - (h - 32.0)) / 32.0, 0.0, 1.0));
	if (sky_flash > 0.0)
		c = c * (1.0 + sky_flash * 2.5) + vec3(0.05, 0.05, 0.07) * sky_flash;
	return c;
}
";

    // (a level may have skies of two tiles: each face its own picture; the water reflects the background's)
    static string Local(string code) => code
        .Replace("global uniform sampler2D sky_pano", "uniform sampler2D sky_pano").Replace("global uniform float sky_horizon", "uniform float sky_horizon")
        .Replace("global uniform vec2 sky_pano_size", "uniform vec2 sky_pano_size").Replace("global uniform vec3 sky_top_colour", "uniform vec3 sky_top_colour")
        .Replace("global uniform vec3 sky_bottom_colour", "uniform vec3 sky_bottom_colour");

    static readonly Dictionary<string, (Texture2D tex, float horizon, Color top, Color bottom)> _skies = new();
    static readonly Dictionary<string, (float x, float y, float r)?> _moons = new();

    /// <summary>The moon in the sky set last (panorama column, row, radius), if it has one, and that panorama's size and horizon.</summary>
    public static (float x, float y, float r)? CurrentMoon { get; private set; }
    public static Vector2 CurrentSize { get; private set; }
    /// <summary>The level's sky: its colour at full strength, how far from grey it is (0..1).</summary>
    public static (Color colour, float strength) CurrentTint { get; private set; }
    static readonly Dictionary<string, (Color, float)> _tints = new();

    // what colour a sky is: its bright parts count most (a red sky's clouds, not its black). Measured over the panorama, not a
    // list of levels, so an add-on's skies have theirs too
    static (Color, float) SkyTint(Image img)
    {
        double r = 0, g = 0, b = 0, w = 0;
        for (int y = 0; y < img.GetHeight(); y += 3)
            for (int x = 0; x < img.GetWidth(); x += 5)
            {
                var c = img.GetPixel(x, y);
                double l = c.R * 0.3 + c.G * 0.59 + c.B * 0.11;
                r += c.R * l;
                g += c.G * l;
                b += c.B * l;
                w += l;
            }
        if (w <= 0)
            return (Colors.White, 0f);
        float hi = (float)System.Math.Max(r, System.Math.Max(g, b)), lo = (float)System.Math.Min(r, System.Math.Min(g, b));
        if (hi <= 0)
            return (Colors.White, 0f);
        return (new Color((float)r / hi, (float)g / hi, (float)b / hi), Mathf.SmoothStep(0.55f, 0.85f, (hi - lo) / hi));
    }
    public static float CurrentHorizon { get; private set; }

    /// <summary>A moon: the roundest bright blob over the horizon, compact and filling its box (clouds are neither).</summary>
    static (float x, float y, float r)? FindMoon(Image pano, float horizon)
    {
        int w = pano.GetWidth(), h = Mathf.Min(pano.GetHeight(), (int)horizon);
        if (w <= 0 || h <= 0)
            return null;
        var bright = new bool[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                bright[x + y * w] = pano.GetPixel(x, y).Luminance > 0.55f;
        var seen = new bool[w * h];
        var todo = new Stack<(int, int)>();
        (float x, float y, float r)? best = null;
        float bestScore = 0;
        for (int y0 = 0; y0 < h; y0++)
            for (int x0 = 0; x0 < w; x0++)
            {
                if (!bright[x0 + y0 * w] || seen[x0 + y0 * w])
                    continue;
                int n = 0, xa = x0, xb = x0, ya = y0, yb = y0;
                seen[x0 + y0 * w] = true;
                todo.Push((x0, y0));
                while (todo.Count > 0)
                {
                    var (x, y) = todo.Pop();
                    n++;
                    xa = Mathf.Min(xa, x);
                    xb = Mathf.Max(xb, x);
                    ya = Mathf.Min(ya, y);
                    yb = Mathf.Max(yb, y);
                    foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                    {
                        int nx = x + dx, ny = y + dy;
                        if (nx < 0 || ny < 0 || nx >= w || ny >= h || seen[nx + ny * w] || !bright[nx + ny * w])
                            continue;
                        seen[nx + ny * w] = true;
                        todo.Push((nx, ny));
                    }
                }
                int bw = xb - xa + 1, bh = yb - ya + 1;
                float fill = n / (float)(bw * bh), ratio = Mathf.Max(bw, bh) / (float)Mathf.Min(bw, bh);
                if (bw < 8 || bh < 8 || bw > 90 || bh > 90 || ratio > 1.4f || fill < 0.62f)
                    continue;
                float score = fill * n;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = ((xa + xb) / 2f, (ya + yb) / 2f, (bw + bh) / 4f);
                }
            }
        return best;
    }

    /// <summary>What sprites and voxels drawn opaque add to their shaders: their pixels marked so the sky leaves them.</summary>
    public const string SpriteMark = "stencil_mode write, compare_always, 3;\n";
    /// <summary>What blob shadows add: not drawn over the sky (it marks its pixels 2; they lie on floors, whose depth tells).</summary>
    public const string UnderSky = "stencil_mode read, compare_not_equal, 2;\n";

    static Shader _nativeShader, _skyShader;

    /// <summary>The native world's sky faces (Render.Native.WorldMesher's vertex data): Build's way, seen only from the
    /// front (a parallax ceiling from below, Build draws no ceiling from above: from a wall top, the yard's sky ceilings
    /// below the eye drew the sky over the ground), on their planes (b3d_planes), a portal copy marked by its instance.</summary>
    static string NativeCode() => "shader_type spatial;\nrender_mode unshaded, cull_back, fog_disabled, depth_draw_never;\n"
        + "stencil_mode read, write, compare_greater_or_equal, 2;\n"
        + PortalRenderer.Clip + BuildShading.TickLerpUniform + Native.WorldShader.Data + StackedRooms.Shader + Local(Mapping)
        + BuildShading.ShadeFunction + @"
varying float v_portal;
global uniform float sky_shade;
global uniform float sky_fog;
global uniform float sky_faces;
instance uniform float portal_copy = 0.0;
void vertex() {" + Native.WorldShader.VertexBlend + @"
	vec2 q = vec2(VERTEX.z, VERTEX.x) * 32.0;
	if (CUSTOM0.x > 0.5)
		VERTEX.y = b3d_height(CUSTOM0.xyz, q) / 32.0;
	v_portal = portal_copy;
	// (no shadow of its own from the light from the sky: its projection the only orthogonal one)
	POSITION = PROJECTION_MATRIX[3][3] > 0.5 ? vec4(2.0, 2.0, 2.0, 1.0) : PROJECTION_MATRIX * (MODELVIEW_MATRIX * vec4(VERTEX, 1.0));
}
void fragment() {
	vec3 world = " + PortalRenderer.WorldPos + @";
	if (sky_faces < 0.5) discard;
	if (portal_clipped(world, v_portal, CAMERA_POSITION_WORLD)) discard;
	if (stack_discard(world, CAMERA_POSITION_WORLD, v_portal, -1.0)) discard;
	vec3 dir = normalize((INV_VIEW_MATRIX * vec4(VERTEX, 0.0)).xyz);
	ALBEDO = build_sky_haze(sky_fog > 0.5 ? build_sky_fog(build_sky(dir), sky_shade) : build_color(build_sky(dir), sky_shade, 0.0), dir);
	ALPHA = 1.0;
}
";

    /// <summary>A sky made at run time (Render.Native.NativeSky: a panorama from Blood's tiles) under a name, its
    /// material (drawn Build's way) and the background's from <see cref="MakeEnvironment"/>.</summary>
    public static ShaderMaterial Register(string name, Image pano, float horizon, int shade, bool fog)
    {
        string key = Data.Addons.TextureRes + "/sky/" + name;
        _moons[key] = FindMoon(pano, horizon);
        _tints[key] = SkyTint(pano);
        _skies[key] = (ImageTexture.CreateFromImage(pano), horizon, RowAverage(pano, 0), RowAverage(pano, pano.GetHeight() - 1));
        Globals(shade, fog);
        _nativeShader ??= new Shader { Code = NativeCode() };
        var mat = new ShaderMaterial { Shader = _nativeShader, RenderPriority = (int)Material.RenderPriorityMin };
        SetParams(mat, name);
        return mat;
    }

    /// <summary>A native level's sky shade and fog (its sky made before, by an earlier level, sets them again).</summary>
    public static void Globals(int shade, bool fog)
    {
        RenderingServer.GlobalShaderParameterSet("sky_fog", fog ? 1f : 0f);
        RenderingServer.GlobalShaderParameterSet("sky_shade", (float)shade);
    }

    static bool LoadSky(string name)
    {
        // the level's textures (an add-on's in its own folder: its art may replace the sky's tiles)
        string dir = Data.Addons.TextureRes + "/sky";
        if (_skies.ContainsKey(dir + "/" + name))
            return true;
        // read straight from the PNG (no import step; Godot could otherwise compress it for the GPU)
        string png = ProjectSettings.GlobalizePath($"{dir}/{name}.png");
        if (!System.IO.File.Exists(png))
            return false;
        var img = Image.LoadFromFile(png);
        var tex = ImageTexture.CreateFromImage(img);
        float horizon = tex.GetHeight() / 2f;
        string txt = $"{dir}/{name}.txt";
        if (FileAccess.FileExists(txt))
            horizon = float.Parse(FileAccess.GetFileAsString(txt).Trim(), CultureInfo.InvariantCulture);
        _skies[dir + "/" + name] = (tex, horizon, RowAverage(img, 0), RowAverage(img, img.GetHeight() - 1));
        _tints[dir + "/" + name] = SkyTint(img);
        return true;
    }

    static Color RowAverage(Image img, int y)
    {
        float r = 0, g = 0, b = 0;
        int w = img.GetWidth();
        for (int x = 0; x < w; x++)
        {
            var c = img.GetPixel(x, y);
            r += c.R; g += c.G; b += c.B;
        }
        return new Color(r / w, g / w, b / w);
    }

    // (globals: only one sky at a time, and the water reflects it)
    static void SetParams(ShaderMaterial m, string name)
    {
        var s = _skies[Data.Addons.TextureRes + "/sky/" + name];
        m.SetShaderParameter("sky_pano", s.tex);
        m.SetShaderParameter("sky_horizon", s.horizon);
        m.SetShaderParameter("sky_pano_size", new Vector2(s.tex.GetWidth(), s.tex.GetHeight()));
        m.SetShaderParameter("sky_top_colour", new Vector3(s.top.R, s.top.G, s.top.B));
        m.SetShaderParameter("sky_bottom_colour", new Vector3(s.bottom.R, s.bottom.G, s.bottom.B));
        _moons.TryGetValue(Data.Addons.TextureRes + "/sky/" + name, out var moon);
        CurrentMoon = moon;
        CurrentTint = _tints.TryGetValue(Data.Addons.TextureRes + "/sky/" + name, out var tint) ? tint : (Colors.White, 0f);
        CurrentSize = new Vector2(s.tex.GetWidth(), s.tex.GetHeight());
        CurrentHorizon = s.horizon;
        RenderingServer.GlobalShaderParameterSet("sky_pano", s.tex);
        RenderingServer.GlobalShaderParameterSet("sky_horizon", s.horizon);
        RenderingServer.GlobalShaderParameterSet("sky_pano_size", new Vector2(s.tex.GetWidth(), s.tex.GetHeight()));
        RenderingServer.GlobalShaderParameterSet("sky_top_colour", new Vector3(s.top.R, s.top.G, s.top.B));
        RenderingServer.GlobalShaderParameterSet("sky_bottom_colour", new Vector3(s.bottom.R, s.bottom.G, s.bottom.B));
    }

    /// <summary>The background sky (an Environment) for a sky name; null if it isn't there.</summary>
    public static Environment MakeEnvironment(string name)
    {
        if (name == "" || !LoadSky(name))
            return null;
        _skyShader ??= new Shader
        {
            Code = "shader_type sky;\n" + Local(Mapping) + BuildShading.ShadeFunction
                + "\nglobal uniform float sky_shade;\nglobal uniform float sky_fog;\nvoid sky() {\n\tCOLOR = build_sky_haze(sky_fog > 0.5 ? build_sky_fog(build_sky(EYEDIR), sky_shade) : build_color(build_sky(EYEDIR), sky_shade, 0.0), EYEDIR);\n}\n",
        };
        var mat = new ShaderMaterial { Shader = _skyShader };
        SetParams(mat, name);
        return new Environment
        {
            BackgroundMode = Environment.BGMode.Sky,
            Sky = new Sky { SkyMaterial = mat, ProcessMode = Sky.ProcessModeEnum.Realtime },
        };
    }
}
