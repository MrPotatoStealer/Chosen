using System.Collections.Generic;
using System.IO;
using Chosen.Sim.Build;
using Godot;

namespace Chosen.Render.Native;

/// <summary>
/// Blood's parallax sky from its tiles at run time (pipeline/export.py's sky_panorama and sky_horizon): sixteen strips
/// of the sky tile side by side (or one wide tile), the horizon at the row Build draws at eye level for the panning most
/// of the level's sky ceilings use; drawn by BuildSky (Build's way: after the level, over what lies behind the sky faces).
/// </summary>
public static class NativeSky
{
    static readonly Dictionary<string, ShaderMaterial> _made = new();

    public static string Name(int tile, int ypan) => $"{tile:D4}" + (ypan != 0 ? $"_y{ypan}" : "");

    /// <summary>The sky material for a level's sky tile; null if its tiles aren't there.</summary>
    public static ShaderMaterial MaterialFor(int tile, Board b)
    {
        var (ypan, shade, fog) = SkyOf(b, tile);
        string name = Name(tile, ypan);
        if (_made.TryGetValue(name + Data.Addons.TextureRes, out var m))
        {
            // (the environment's and the sky globals' too: a level after another's sky)
            Current = name;
            BuildSky.Globals(shade, fog);
            return m;
        }
        var pano = Panorama(tile);
        if (pano == null)
            return null;
        m = BuildSky.Register(name, pano, Horizon(pano.GetHeight(), ypan), shade, fog);
        _made[name + Data.Addons.TextureRes] = m;
        Current = name;
        return m;
    }

    /// <summary>The name of the last sky made (the background's: BuildSky.MakeEnvironment).</summary>
    public static string Current = "";

    /// <summary>The panning most of the level's sky ceilings of this tile use, their commonest shade, and whether most
    /// have palette 1 (shaded toward white).</summary>
    static (int ypan, int shade, bool fog) SkyOf(Board b, int tile)
    {
        var pans = new Dictionary<int, int>();
        var shades = new Dictionary<int, int>();
        int n = 0, fog = 0;
        foreach (var s in b.Sectors)
        {
            if ((s.CeilingStat & 1) == 0 || s.CeilingPicnum != tile)
                continue;
            n++;
            pans[s.CeilingYPanning] = pans.GetValueOrDefault(s.CeilingYPanning) + 1;
            shades[s.CeilingShade] = shades.GetValueOrDefault(s.CeilingShade) + 1;
            if (s.CeilingPal == 1)
                fog++;
        }
        int ypan = 0, best = -1;
        foreach (var (k, v) in pans)
            if (v > best || v == best && k < ypan)
                (ypan, best) = (k, v);
        int shade = 0;
        best = -1;
        foreach (var (k, v) in shades)
            if (v > best)
                (shade, best) = (k, v);
        return (ypan, shade, n > 0 && fog * 2 > n);
    }

    /// <summary>sky_horizon: the panorama row at eye level, Polymost's tilesizy / 2 plus the panning in 1/256 of the
    /// height rounded up to a power of two (doubled when that isn't the height).</summary>
    static float Horizon(int h, int ypan)
    {
        int i = 1;
        while (i < h)
            i <<= 1;
        if (i != h)
            i += i;
        return h / 2f + (ypan >= 128 ? ypan - 256 : ypan) * i / 256f;
    }

    static Image Tile(int t) => Data.TileArt.Picture(t, false);

    static Image Panorama(int start)
    {
        var first = Tile(start);
        if (first == null)
            return null;
        if (first.GetWidth() >= 512)
            return first;
        int w = first.GetWidth(), h = first.GetHeight();
        var pano = Image.CreateEmpty(w * 16, h, false, Image.Format.Rgb8);
        for (int i = 0; i < 16; i++)
        {
            var strip = i == 0 ? first : Tile(start + i);
            if (strip == null || strip.GetWidth() != w || strip.GetHeight() != h)
                return first;
            pano.BlitRect(strip, new Rect2I(0, 0, w, h), new Vector2I(i * w, 0));
        }
        return pano;
    }
}
