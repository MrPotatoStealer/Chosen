using System.IO;
using Godot;

namespace Chosen.Render;

/// <summary>
/// Build's palookups for the native world (gfx/palookup.bin, pipeline/export.py: Blood's 15 PLUs, and an add-on's own
/// from 15 up in its gfx/palookup.bin, 64 shades x 256
/// palette indices each): a surface drawn with palette p takes each texel's palette index (ClassicPalette's palette_lut)
/// through PLU p, as Build recolours walls, floors and ceilings; the classic palette mode shades through the PLU's rows,
/// true colour takes its shade-0 row and darkens it as BuildShading does. One global texture, palette_plu (256 wide,
/// 64 rows a lookup: palette[plu[p][s][i]] at row 64 p + s).
/// </summary>
public static class BuildPalette
{
    static bool _built;

    /// <summary>Made again when next needed (an add-on activated or left: its own lookups).</summary>
    public static void Reload()
    {
        if (!_built)
            return;
        _built = false;
        Ensure();
    }

    public const string ShaderFunction = @"
global uniform sampler2D palette_plu : filter_nearest;
// a texel (linear colour) of a surface with palette pal at shade base plus vis (the distance's): Build's palookup
vec3 build_color_pal(vec3 lin, int pal, float base, float vis) {
	if (pal <= 0 || pal >= textureSize(palette_plu, 0).y / 64)
		return build_color(lin, base, vis);
	ivec3 cell = ivec3(clamp(pal_to_srgb(lin), 0.0, 1.0) * 63.0 + 0.5);
	int idx = int(texelFetch(palette_lut, cell, 0).r * 255.0 + 0.5);
	if (palette_classic < 0.5)
		return pal_to_linear(texelFetch(palette_plu, ivec2(idx, pal * 64), 0).rgb) * build_shade(base + vis);
	float sh = clamp((base + clamp(vis - 0.5, 0.0, 64.0)) * max(shade_contrast, 0.0), 0.0, 63.0);
	int s0 = int(floor(sh));
	vec3 a = texelFetch(palette_plu, ivec2(idx, pal * 64 + s0), 0).rgb;
	vec3 b = texelFetch(palette_plu, ivec2(idx, pal * 64 + min(s0 + 1, 63)), 0).rgb;
	return pal_to_linear(mix(a, b, sh - float(s0)));
}
";

    /// <summary>palette_plu and palette_lut set (once).</summary>
    public static void Ensure()
    {
        if (_built)
            return;
        _built = true;
        ClassicPalette.Build();
        string palPath = Data.BloodData.Find("gfx/palette.lmp"), pluPath = Data.BloodData.Find("gfx/palookup.bin");
        if (!File.Exists(palPath) || !File.Exists(pluPath))
        {
            GD.PushWarning($"palookups: no {pluPath} (run pipeline/export.py)");
            return;
        }
        byte[] pal = File.ReadAllBytes(palPath), plu = File.ReadAllBytes(pluPath);
        int n = plu.Length / (64 * 256);
        var px = new byte[256 * n * 64 * 3];
        for (int k = 0; k < n * 64 * 256; k++)
        {
            int c = plu[k] * 3;
            px[k * 3] = pal[c];
            px[k * 3 + 1] = pal[c + 1];
            px[k * 3 + 2] = pal[c + 2];
        }
        var img = Image.CreateFromData(256, n * 64, false, Image.Format.Rgb8, px);
        RenderingServer.GlobalShaderParameterSet("palette_plu", ImageTexture.CreateFromImage(img));
    }
}
