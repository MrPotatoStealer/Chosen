using Godot;

namespace Chosen.Render;

/// <summary>
/// Build's look for the level's drawing (Render.Native.WorldShader, the sprites, the sky): the shade function (one flat
/// shade per surface, darkening with distance by the map's visibility, Blood's shade table and palettes), the dynamic
/// lights' light function, the sectors fogged to white (palette 1), the visibility and contrast settings.
/// </summary>
public static class BuildShading
{
    public const string AlbedoUniform = "albedo_texture";

    /// <summary>The global uniform of the drawn frame's place between the last two game frames (Game).</summary>
    public const string TickLerpUniform = "global uniform float tick_lerp;\n";

    /// <summary>The light function of the lit Build shaders: dynamic lights only (no ambient, no specular).</summary>
    public const string LightFunction = DynamicLights.RegionFunction + @"
global uniform vec3 camera_world;
// dynamic lights (r_dynamiclights; Render/DynamicLights) add on top of Blood's shading, which is the emission; a
// light restricted to a room-over-room region (its specular amount) skips what lies on the wrong side. The light from the sky
// (Atmosphere) has shadows only so far: past them it goes, or it lit every room beyond
void light() {
	if (LIGHT_IS_DIRECTIONAL)
		DIFFUSE_LIGHT += clamp(dot(NORMAL, LIGHT), 0.0, 1.0) * ATTENUATION * LIGHT_COLOR / PI * smoothstep(60.0, 38.0, length(light_world - camera_world)) * light_sky * light_lit;
	else if (!light_skipped(SPECULAR_AMOUNT, light_world))
		DIFFUSE_LIGHT += clamp(dot(NORMAL, LIGHT), 0.0, 1.0) * ATTENUATION * LIGHT_COLOR / PI * light_lit;
}
";
    /// <summary>A level's picture that glows with r_glow: fire, lava, a light fixture's (only their bright colours,
    /// palette_bright). Chosen by what the picture is: by colour alone any bright speck of any picture glowed (E1M1's banners).</summary>
    public static bool GlowTile(int tile) => tile == ProceduralFire.Tile || Data.Tiles.SurfType(tile) == Data.K.kSurfLava || DynamicLights.IsFixtureTile(tile);

    public const string ShadeFunction = @"
global uniform float hdr_highlight;     // HDR output: fullbright sprites this many times over white (HdrOutput); else 1
global uniform float shade_contrast;
// the classic limited palette (ClassicPalette, r_useindexedcolortextures): a colour's palette index, and each index's
// colour at each shade (Blood's shade table)
global uniform float palette_classic;
global uniform highp sampler3D palette_lut : filter_nearest;
global uniform sampler2D palette_shade : filter_nearest;
// r_glow: how far the colours that glow go over white, and which they are
global uniform float glow_amount;
global uniform sampler2D palette_bright : filter_nearest;
vec3 build_shade(float build_s) {
	float f = clamp(1.0 - build_s * max(shade_contrast, 0.0) / 64.0, 0.0, 1.0);
	return vec3(pow(f, 2.2));
}
vec3 pal_to_linear(vec3 c) {
	return mix(c / 12.92, pow((c + 0.055) / 1.055, vec3(2.4)), step(vec3(0.04045), c));
}
vec3 pal_to_srgb(vec3 c) {
	return mix(c * 12.92, 1.055 * pow(c, vec3(1.0 / 2.4)) - 0.055, step(vec3(0.0031308), c));
}
// a texel (linear colour) at Blood shade base (the surface's) plus vis (the distance's): a brightness factor, or with
// the classic palette the shade table's colour as NBlood's Polymost draws it (two rows blended; the distance part
// half a shade lower)
vec3 build_color(vec3 lin, float base, float vis) {
	if (palette_classic < 0.5)
		return lin * build_shade(base + vis);
	ivec3 cell = ivec3(clamp(pal_to_srgb(lin), 0.0, 1.0) * 63.0 + 0.5);
	int idx = int(texelFetch(palette_lut, cell, 0).r * 255.0 + 0.5);
	float sh = clamp((base + clamp(vis - 0.5, 0.0, 64.0)) * max(shade_contrast, 0.0), 0.0, 63.0);
	int s0 = int(floor(sh));
	vec3 a = texelFetch(palette_shade, ivec2(idx, s0), 0).rgb;
	vec3 b = texelFetch(palette_shade, ivec2(idx, min(s0 + 1, 63)), 0).rgb;
	return pal_to_linear(mix(a, b, sh - float(s0)));
}
// a texel of a colour that glows (ClassicPalette), on what gives light: as bright as it is whatever the shade, and more
vec3 build_glow(vec3 lin, vec3 c) {
	if (glow_amount <= 0.0)
		return c;
	ivec3 cell = ivec3(clamp(pal_to_srgb(lin), 0.0, 1.0) * 63.0 + 0.5);
	int idx = int(texelFetch(palette_lut, cell, 0).r * 255.0 + 0.5);
	return texelFetch(palette_bright, ivec2(idx, 0), 0).r > 0.75 ? max(c, lin * (1.0 + glow_amount * 1.4)) : c;
}
// a light fixture's picture (a lamp's glass, a tube): its pale warm and near white colours glow too, and brighter than
// a flame's (the lantern's glass at 1.35 its colour read dull: 2.2 at r_glow 0.25)
vec3 build_glow_lamp(vec3 lin, vec3 c) {
	if (glow_amount <= 0.0)
		return c;
	ivec3 cell = ivec3(clamp(pal_to_srgb(lin), 0.0, 1.0) * 63.0 + 0.5);
	int idx = int(texelFetch(palette_lut, cell, 0).r * 255.0 + 0.5);
	return texelFetch(palette_bright, ivec2(idx, 0), 0).r > 0.25 ? max(c, lin * (1.0 + min(glow_amount, 1.0) * 4.0 + glow_amount * 0.8)) : c;
}
// a face drawn with palette 1 (its picture's: Blood's SATURATE): its shade table runs toward white, not black, and
// NBlood (indexed textures, its default) draws it through that table by its shade and distance as any face, with no
// fog over it (palookupfog[1]'s fog is for filtered textures only): Death Wish's mist. The table as a blend toward
// white in the palette's colours, 0.867 of the way at shade 63 (fitted to SATURATE.PLU's rows); its distance part
// twice the distance darkening's (measured over DWE3M1's comparison views: 1 left the mist too thin far off and,
// with the old fog, too thick close by)
float fog_amount(float base, float vis, float fog) {
	if (fog < 0.5)
		return 0.0;
	float sh = (base + clamp(vis * 2.0 - 0.5, 0.0, 64.0)) * max(shade_contrast, 0.0);
	return 0.867 * clamp(sh, 0.0, 63.0) / 63.0;
}
vec3 build_saturate(vec3 lin, float t) {
	return pal_to_linear(mix(pal_to_srgb(clamp(lin, 0.0, 1.0)), vec3(1.0), t));
}
vec3 build_color_fog(vec3 lin, float base, float vis, float fog) {
	if (fog < 0.5)
		return build_color(lin, base, vis);
	return build_saturate(lin, fog_amount(base, vis, fog));
}
// the roughness the level and sprites write: 1, less where the mist whitens them (AmbientOcclusion takes that share
// of its darkening away: it greyed DWE3M1's misty streets; 0 marks the sky)
float fog_roughness(float base, float vis, float fog) {
	return 1.0 - 0.75 * fog_amount(base, vis, fog) / 0.867;
}
// a palette-1 sky at shade s: through the same table (DWE3M1's sky at 63 nearly white, its clouds faint)
vec3 build_sky_fog(vec3 lin, float s) {
	return build_saturate(lin, 0.867 * clamp(s * max(shade_contrast, 0.0), 0.0, 63.0) / 63.0);
}
// the fog mark in a vertex colour's alpha (FogAlpha: 1 / 0.5 a face of its own / another sector, 0.125 less fogged)
float fog_mark(vec4 colour) {
	return mod(floor(colour.a * 8.0 + 0.5), 4.0) > 2.5 ? 1.0 : 0.0;
}
";

    /// <summary>The Build sectors whose floor / ceiling is drawn with palette 1 (Blood's SATURATE): the sprites in them
    /// take it (Blood's viewApplyFloorPal, its sectors' colour flag); the faces go by their own pictures' (FogPal).</summary>
    public static readonly System.Collections.Generic.HashSet<int> FogFloor = new(), FogCeil = new();

    /// <summary>The fogged sectors from Build's palettes (a level converted since they were written: fpal, cpal) on
    /// top of those whose pictures have palette 1; whether the level's sky has it (most of its sky ceilings).</summary>
    public static bool AddFogFromSectors(Sim.SectorMap map)
    {
        int sky = 0, fogSky = 0;
        for (int i = 0; map != null && i < map.SectorCount; i++)
        {
            if (map.FloorPal(i) == 1)
                FogFloor.Add(i);
            if (map.CeilPal(i) == 1)
                FogCeil.Add(i);
            if (map.CeilSky(i))
            {
                sky++;
                if (map.CeilPal(i) == 1)
                    fogSky++;
            }
        }
        return sky > 0 && fogSky * 2 > sky;
    }

    static readonly Core.Cvar Contrast = Core.Cvars.Register("b3d_shadecontrast", "1",
        "sector contrast: scales Blood's shades (1 as Blood, higher: dark sectors darker)", saved: true);

    static BuildShading()
    {
        void Apply() => RenderingServer.GlobalShaderParameterSet("shade_contrast", Mathf.Clamp(Contrast.Float, 0.5f, 3f));
        Contrast.Changed += Apply;
        Apply();
        ClassicPalette.Init();
        VisScale.Changed += ApplyVisibility;
    }

    /// <summary>Sets the distance shading for the level (0: none).</summary>
    public static void SetVisibility(int visibility)
    {
        _visibility = visibility;
        ApplyVisibility();
    }

    static int _visibility;
    // NBlood's Visibility option (r_ambientlight): the distance darkening divided by it
    static readonly Core.Cvar VisScale = Core.Cvars.Register("r_ambientlight", "1",
        "distance darkening: Blood's visibility divided by this (1 as Blood, higher: less darkening; NBlood's Visibility)", saved: true);

    // shades per Quake unit of distance and unit of sector rate: the map's visibility / 1048576, measured against NBlood's
    // Polymost at six places in E1M1 and E1M2 (DarkPlaces' version used half of that, 524288, and came out too dark)
    static void ApplyVisibility() =>
        RenderingServer.GlobalShaderParameterSet("build_visibility", EnhancedFog.ReplacesFade ? 0f : _visibility * VisScaleFactor / 1048576f);

    /// <summary>The distance fade again (EnhancedFog replaces it, or not).</summary>
    public static void RefreshVisibility() => ApplyVisibility();

    public static float VisScaleFactor => 1f / Mathf.Clamp(VisScale.Float, 0.125f, 1000f);
}
