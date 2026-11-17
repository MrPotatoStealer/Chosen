using System.IO;
using Godot;

namespace Chosen.Render;

/// <summary>
/// The classic limited palette (NBlood's Polymost "Texture mode: Classic", r_useindexedcolortextures): every texel is
/// looked up as its BLOOD.PAL index and shaded through Blood's shade table (gfx/normal.plu, 64 shades), so the
/// picture keeps the 256 colours and dark areas take the table's tints, as Build draws them. The shade between two
/// table rows blends them (NBlood's r_shadeinterpolate). Off: the shade is a brightness factor on the true colour
/// (BuildShading.ShadeFunction). The shaders' build_color does both; two global textures feed it: palette_lut (64^3,
/// a colour's nearest palette index: the textures hold BLOOD.PAL's 6-bit colours exactly, one per cell) and
/// palette_shade (256 x 64: palette[normal.plu[shade][index]]); palette_bright (256: those that glow, r_glow).
///
/// That is per surface, before what is drawn over it (fog, mist, rain, ambient occlusion, the lights, the blended shade rows):
/// with all of them on a quarter of the pixels were still palette colours. r_useindexedcolortextures 2 adds a last pass over
/// the 3D view and the weapon (Hud, under the tints and the status bar) taking each pixel to its nearest palette colour. In
/// HDR what is over white keeps its brightness, the colour within it taken to the palette.
/// </summary>
public static class ClassicPalette
{
    public static readonly Core.Cvar Enabled = Core.Cvars.Register("r_useindexedcolortextures", "0",
        "classic palette, 1 textures: every texel shaded through Blood's shade table (NBlood's Texture mode: Classic); 2 post process: and all that is drawn over it (fog, mist, rain...) taken to the palette at the end; 0 off: true colour shading",
        saved: true);

    /// <summary>Does the last pass run?</summary>
    public static bool ScreenPass => Enabled.Int == 2 && Build();

    static Shader _screenShader;

    /// <summary>The last pass: the screen, each pixel its nearest palette colour.</summary>
    public static Shader ScreenShader => _screenShader ??= new Shader { Code = @"
shader_type canvas_item;
uniform sampler2D screen : hint_screen_texture, filter_nearest;
global uniform highp sampler3D palette_lut : filter_nearest;
global uniform sampler2D palette_shade : filter_nearest;
global uniform float hdr_linear_screen;     // HDR output: the screen reads linear, and over 1 for what is over white
vec3 to_srgb(vec3 v) {
	return mix(v * 12.92, 1.055 * pow(v, vec3(1.0 / 2.4)) - 0.055, step(vec3(0.0031308), v));
}
vec3 to_linear(vec3 v) {
	return mix(v / 12.92, pow((v + 0.055) / 1.055, vec3(2.4)), step(vec3(0.04045), v));
}
void fragment() {
	vec3 c = texture(screen, SCREEN_UV).rgb;
	float over = 1.0;
	if (hdr_linear_screen > 0.5) {
		over = max(max(c.r, max(c.g, c.b)), 1.0);
		c = to_srgb(clamp(c / over, 0.0, 1.0));
	}
	ivec3 cell = ivec3(clamp(c, 0.0, 1.0) * 63.0 + 0.5);
	int idx = int(texelFetch(palette_lut, cell, 0).r * 255.0 + 0.5);
	vec3 p = texelFetch(palette_shade, ivec2(idx, 0), 0).rgb;
	COLOR = vec4(hdr_linear_screen > 0.5 ? to_linear(p) * over : p, 1.0);
}
" };

    const int Cells = 64;
    static bool _built, _failed;

    public static void Init()
    {
        Enabled.Changed += Apply;
        Apply();
    }

    static void Apply() =>
        RenderingServer.GlobalShaderParameterSet("palette_classic", Enabled.Bool && Build() ? 1f : 0f);

    public static bool Build()
    {
        if (_built || _failed)
            return _built;
        string dir = Data.BloodData.Folder.PathJoin("gfx");
        string palPath = dir.PathJoin("palette.lmp"), pluPath = dir.PathJoin("normal.plu");
        if (!File.Exists(palPath) || !File.Exists(pluPath))
        {
            GD.PushWarning($"classic palette: no {pluPath} (run build_assets.bat)");
            _failed = true;
            return false;
        }
        byte[] pal = File.ReadAllBytes(palPath), plu = File.ReadAllBytes(pluPath);
        if (pal.Length < 768 || plu.Length < 64 * 256)
        {
            _failed = true;
            return false;
        }
        // the shade table: row s, column i = the colour index i takes at shade s
        var shade = Image.CreateEmpty(256, 64, false, Image.Format.Rgb8);
        for (int s = 0; s < 64; s++)
            for (int i = 0; i < 256; i++)
            {
                int c = plu[s * 256 + i] * 3;
                shade.SetPixel(i, s, Color.Color8(pal[c], pal[c + 1], pal[c + 2]));
            }
        // the colours that glow on what gives light (Atmosphere.Glow, BuildShading.GlowTile): Blood's shade table darkens all
        // of them alike, so by what they look like: the bright oranges and yellows of fire, lamps and lava, and reds only at
        // their hottest (blood stays out): 255. On a light fixture's picture also its pale warm and near white colours, the
        // lantern's glass and the tubes (build_glow_lamp): 128.
        var brightData = new byte[256];
        for (int i = 0; i < 256; i++)
        {
            var c = Color.Color8(pal[i * 3], pal[i * 3 + 1], pal[i * 3 + 2]);
            float hue = c.H * 360f;
            if (c.S > 0.5f && c.V > 0.7f && (hue >= 20f && hue <= 62f || hue < 20f && c.V > 0.92f))
                brightData[i] = 255;
            else if (c.V > 0.62f && (c.S > 0.12f && hue >= 15f && hue <= 65f) || c.S <= 0.12f && c.V > 0.85f)
                brightData[i] = 128;
        }
        RenderingServer.GlobalShaderParameterSet("palette_bright", ImageTexture.CreateFromImage(Image.CreateFromData(256, 1, false, Image.Format.R8, brightData)));
        // colour -> palette index: each palette colour owns its cell, every other cell takes the nearest colour
        // (mipmapped and blended texels)
        var index = new byte[Cells * Cells * Cells];
        var owned = new bool[index.Length];
        for (int i = 0; i < 256; i++)
        {
            int k = Cell(pal[i * 3]) + Cell(pal[i * 3 + 1]) * Cells + Cell(pal[i * 3 + 2]) * Cells * Cells;
            if (!owned[k])
            {
                owned[k] = true;
                index[k] = (byte)i;
            }
        }
        var images = new Godot.Collections.Array<Image>();
        for (int b = 0; b < Cells; b++)
        {
            var data = new byte[Cells * Cells];
            for (int g = 0; g < Cells; g++)
                for (int r = 0; r < Cells; r++)
                {
                    int k = r + g * Cells + b * Cells * Cells;
                    if (!owned[k])
                    {
                        int cr = r * 255 / (Cells - 1), cg = g * 255 / (Cells - 1), cb = b * 255 / (Cells - 1);
                        int best = 0, bestD = int.MaxValue;
                        for (int i = 0; i < 256; i++)
                        {
                            int dr = pal[i * 3] - cr, dg = pal[i * 3 + 1] - cg, db = pal[i * 3 + 2] - cb;
                            int d = dr * dr + dg * dg + db * db;
                            if (d < bestD)
                            {
                                bestD = d;
                                best = i;
                            }
                        }
                        index[k] = (byte)best;
                    }
                    data[r + g * Cells] = index[k];
                }
            images.Add(Image.CreateFromData(Cells, Cells, false, Image.Format.R8, data));
        }
        var lut = new ImageTexture3D();
        lut.Create(Image.Format.R8, Cells, Cells, Cells, false, images);
        RenderingServer.GlobalShaderParameterSet("palette_lut", lut);
        RenderingServer.GlobalShaderParameterSet("palette_shade", ImageTexture.CreateFromImage(shade));
        _built = true;
        return true;
    }

    static int Cell(byte v) => (v * (Cells - 1) + 127) / 255;
}
