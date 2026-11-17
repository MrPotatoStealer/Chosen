using System;
using System.IO;
using Godot;

namespace Chosen.Render;

/// <summary>
/// Blood's screen palettes (UpdateDacs / scrSetPalette): underwater, in goo or the death mask, Blood draws the whole
/// screen through another palette: 1 WATER, 2 BEAST (a liquid that is neither), 3 SEWER (goo), 4 INVULN1, each the same
/// 256 entries as BLOOD.PAL recoloured. Drawn in colour here, each is a colour matrix fitted to map BLOOD.PAL's
/// colours onto the palette's (least squares; within about 2 of 255 on average for water and sewer), applied to
/// the screen in one pass (Hud: over the 3D view, weapon and status bar).
/// </summary>
public static class ViewPalette
{
    static readonly string[] Files = { null, "water", "beast", "sewer", "invuln1" };
    static readonly Projection?[] _matrices = new Projection?[Files.Length];
    static Shader _shader;

    /// <summary>The pass's shader: the screen through mat (rows: r g b out of r g b 1).</summary>
    public static Shader Shader => _shader ??= new Shader { Code = @"
shader_type canvas_item;
uniform sampler2D screen : hint_screen_texture, filter_nearest;
uniform mat4 pal = mat4(1.0);
global uniform float hdr_linear_screen;     // HDR output: the screen reads linear, the palettes' matrix is fitted in sRGB
vec3 to_srgb(vec3 v) {
	v = clamp(v, 0.0, 1.0);
	return mix(v * 12.92, 1.055 * pow(v, vec3(1.0 / 2.4)) - 0.055, step(vec3(0.0031308), v));
}
vec3 to_linear(vec3 v) {
	return mix(v / 12.92, pow((v + 0.055) / 1.055, vec3(2.4)), step(vec3(0.04045), v));
}
void fragment() {
	vec3 c = texture(screen, SCREEN_UV).rgb;
	if (hdr_linear_screen > 0.5)
		c = to_srgb(c);
	c = clamp((pal * vec4(c, 1.0)).rgb, 0.0, 1.0);
	COLOR = vec4(hdr_linear_screen > 0.5 ? to_linear(c) : c, 1.0);
}
" };

    /// <summary>Screen palette n's matrix (identity: none, or its file missing; gfx/&lt;name&gt;.lmp from build_assets).</summary>
    public static Projection Matrix(int n)
    {
        if (n <= 0 || n >= Files.Length)
            return Projection.Identity;
        return _matrices[n] ??= Fit(n);
    }

    static Projection Fit(int n)
    {
        string dir = Data.BloodData.Folder.PathJoin("gfx");
        string src = dir.PathJoin("palette.lmp"), dst = dir.PathJoin(Files[n] + ".lmp");
        if (!File.Exists(src) || !File.Exists(dst))
        {
            GD.PushWarning($"view palette {n}: no {dst} (run build_assets.bat)");
            return Projection.Identity;
        }
        byte[] a = File.ReadAllBytes(src), b = File.ReadAllBytes(dst);
        if (a.Length < 768 || b.Length < 768)
            return Projection.Identity;
        // least squares per output channel: [r g b 1] x = out, from the normal equations (4 x 4)
        var ata = new double[4, 4];
        var atb = new double[4, 3];
        for (int i = 0; i < 256; i++)
        {
            double[] x = { a[i * 3] / 255.0, a[i * 3 + 1] / 255.0, a[i * 3 + 2] / 255.0, 1 };
            for (int r = 0; r < 4; r++)
            {
                for (int c = 0; c < 4; c++)
                    ata[r, c] += x[r] * x[c];
                for (int k = 0; k < 3; k++)
                    atb[r, k] += x[r] * b[i * 3 + k] / 255.0;
            }
        }
        var m = Solve(ata, atb);
        // a Projection's columns: out = col0 * r + col1 * g + col2 * b + col3
        return new Projection(
            new Vector4((float)m[0, 0], (float)m[0, 1], (float)m[0, 2], 0),
            new Vector4((float)m[1, 0], (float)m[1, 1], (float)m[1, 2], 0),
            new Vector4((float)m[2, 0], (float)m[2, 1], (float)m[2, 2], 0),
            new Vector4((float)m[3, 0], (float)m[3, 1], (float)m[3, 2], 1));
    }

    /// <summary>Solves a x = b (4 x 4, three right-hand sides) by Gaussian elimination with partial pivoting.</summary>
    static double[,] Solve(double[,] a, double[,] b)
    {
        int n = 4, k = b.GetLength(1);
        for (int col = 0; col < n; col++)
        {
            int piv = col;
            for (int r = col + 1; r < n; r++)
                if (Math.Abs(a[r, col]) > Math.Abs(a[piv, col]))
                    piv = r;
            for (int c = 0; c < n; c++)
                (a[col, c], a[piv, c]) = (a[piv, c], a[col, c]);
            for (int c = 0; c < k; c++)
                (b[col, c], b[piv, c]) = (b[piv, c], b[col, c]);
            for (int r = 0; r < n; r++)
            {
                if (r == col || a[col, col] == 0)
                    continue;
                double f = a[r, col] / a[col, col];
                for (int c = 0; c < n; c++)
                    a[r, c] -= f * a[col, c];
                for (int c = 0; c < k; c++)
                    b[r, c] -= f * b[col, c];
            }
        }
        var x = new double[n, k];
        for (int r = 0; r < n; r++)
            for (int c = 0; c < k; c++)
                x[r, c] = a[r, r] != 0 ? b[r, c] / a[r, r] : 0;
        return x;
    }
}
