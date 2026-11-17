using System.Collections.Generic;
using Godot;
using Chosen.Shared.Blood.Map;

namespace Chosen.Sim;

/// <summary>
/// Sector light effects (sectorfx.cpp DoSectorLighting, via Blood3D's server/sectorfx.qc): every frame a light-effect
/// sector's shade offset follows its wave (flicker, pulse, strobe...) by its amplitude, scaled by its busy value unless
/// it shades always (light switches ramp busy). The renderer adds the offset to the sector's surfaces (the converter's
/// blood_sectorfx skins), and to the sprites and the weapon in it.
/// </summary>
public sealed partial class World
{
    static readonly int[] Flicker1 =
    {
        0, 0, 1, 0, 1, 1, 1, 1, 0, 0, 0, 0, 1, 0, 1, 0, 1, 1, 0, 1, 0, 0, 1, 1, 0, 1, 1, 0, 1, 0, 0, 1,
        0, 0, 1, 1, 0, 1, 1, 1, 1, 1, 1, 1, 1, 0, 0, 1, 0, 0, 0, 0, 0, 0, 1, 0, 1, 1, 0, 0, 1, 1, 0, 1,
    };
    static readonly int[] Flicker2 =
    {
        1, 2, 4, 2, 3, 4, 3, 2, 0, 0, 1, 2, 4, 3, 2, 0, 2, 1, 0, 1, 0, 2, 3, 4, 3, 2, 1, 1, 2, 0, 0, 1,
        1, 2, 3, 4, 4, 3, 2, 1, 2, 3, 4, 4, 2, 1, 0, 1, 0, 0, 0, 0, 1, 2, 3, 4, 3, 2, 1, 2, 3, 4, 3, 2,
    };
    static readonly int[] Flicker3 =
    {
        4, 4, 4, 4, 3, 4, 4, 4, 4, 4, 4, 2, 4, 3, 4, 4, 4, 4, 2, 1, 3, 3, 3, 4, 3, 4, 4, 4, 4, 4, 2, 4,
        4, 4, 3, 4, 4, 4, 4, 4, 4, 4, 4, 4, 2, 1, 0, 1, 0, 1, 0, 1, 0, 2, 3, 4, 4, 4, 4, 4, 4, 4, 3, 4,
    };
    // flicker4 has 128 entries; only the first 64 are non-zero
    static readonly int[] Flicker4 =
    {
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 4, 0, 0, 3, 0, 1, 0, 1, 0, 4, 4, 4, 4, 4, 2, 0,
        0, 0, 0, 4, 4, 3, 2, 1, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 2, 1, 2, 1, 2, 1, 2, 1, 4, 3, 2,
    };
    static readonly int[] Strobe =
    {
        64, 64, 64, 48, 36, 27, 20, 15, 11, 9, 6, 5, 4, 3, 2, 2, 1, 1, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
    };

    List<Sector> _fxSectors;              // gFxSectors: the light-effect sectors (movers carry none)
    readonly Dictionary<int, Sector> _fxByB = new();   // each Build sector's (0-based) light-effect sector

    /// <summary>sectorfx GetWaveValue(wave, phase, amplitude).</summary>
    static double FxWaveValue(int a, double b, double c)
    {
        int ph = (int)((long)b & 2047);
        switch (a)
        {
            case 0: return c;
            case 1: return (ph >> 10) * c;
            case 2: return Blood.Sar(System.Math.Abs(128 - (ph >> 3)) * c, 7);
            case 3: return Blood.Sar((ph >> 3) * c, 8);
            case 4: return Blood.Sar((255 - (ph >> 3)) * c, 8);
            case 5: return Blood.Sar(c + Blood.MulScale30(c, Blood.Sin(ph)), 1);
            case 6: return Flicker1[ph >> 5] * c;
            case 7: return Blood.Sar(Flicker2[ph >> 5] * c, 2);
            case 8: return Blood.Sar(Flicker3[ph >> 5] * c, 2);
            case 9:
                int i = ph >> 4;
                return i >= 64 ? 0 : Blood.Sar(Flicker4[i] * c, 2);
            case 10: return Blood.Sar(Strobe[ph >> 5] * c, 6);
            case 11:
                if (ph * 4 > 2048)
                    return 0;
                return Blood.Sar(c - Blood.MulScale30(c, Blood.Cos(ph * 4)), 1);
        }
        return 0;
    }

    /// <summary>Comparison views (Game's --views) and regression shots: flickering light effects stay at their base
    /// shades, as NBlood's -b3dviews; steady ones (wave 0) are on.</summary>
    public static bool FreezeLightFx;

    /// <summary>sectorFxProcess (DoSectorLighting): every light-effect sector's shade offset this frame.</summary>
    void SectorFxProcess()
    {
        // every sector with an amplitude (NBlood InitSectorFX), moving ones too: a sliding lid's sector brightens as
        // it opens (MoverRenderer gives a mover's surfaces its shade)
        if (_fxSectors == null)
        {
            _fxSectors = Sectors.FindAll(s => s.FxAmp != 0);
            _fxByB.Clear();
            foreach (var s in _fxSectors)
                if (s.BSector > 0)
                    _fxByB.TryAdd(s.BSector - 1, s);
        }
        foreach (var s in _fxSectors)
        {
            int nShade = 0;
            // frozen (comparison and regression shots): no flicker, but a steady light (wave 0) still lights its sector
            // (E1M1's secret shelf, lit while open, as NBlood's -b3dviews draws it)
            if ((s.FxAlways != 0 || s.XBusy != 0) && (!FreezeLightFx || s.FxWave == 0))
            {
                double amp = s.FxAmp;
                if (s.FxAlways == 0 && s.XBusy != 0)
                    amp = Blood.MulScale16(amp, s.XBusy);
                nShade = (int)FxWaveValue(s.FxWave, s.FxPhase * 8 + (double)s.FxFreq * FrameClock, amp);
            }
            s.FxShade = nShade;
            // viewProcessSprites: a sprite adds its sector's ceiling shade under a sky, else the floor's
            s.FxSprite = s.FxSky != 0 ? (s.FxCeil != 0 ? nShade : 0) : (s.FxFloor != 0 ? nShade : 0);
            s.FxWeapon = s.FxFloor != 0 ? nShade : 0;     // the weapon always takes the floor's
        }
    }

    /// <summary>Build sector b's (0-based) shade offset for the sprites in it this frame (its light effect).</summary>
    public int FxSpriteShadeOf(int b) => _fxByB.TryGetValue(b, out var s) ? s.FxSprite : 0;

    /// <summary>fxSectorAt: the light-effect sector whose column holds p (Quake units), or null. Free of side effects,
    /// so the renderer may ask.</summary>
    public Sector FxSectorAt(Vector3 p)
    {
        if (_fxSectors == null)
            return null;
        int s = SectorOfPointB(Q2BX(p), Q2BY(p), Q2BZ(p));
        return s >= 0 && _fxByB.TryGetValue(s, out var fs) ? fs : null;
    }

    /// <summary>How much of its light effect the sector at p has on (0..1; 1 without an effect): a room whose light
    /// switch adds its light (a negative amplitude) is 1 while on, 0 while off, in between while it fades or flickers;
    /// a darkening effect the other way round. Dynamic lights of light fixtures follow it.</summary>
    public float FxLightLevel(Vector3 p) => FxLevelOf(FxLightSector(p));

    /// <summary>Are the light-effect sectors known yet (from the first game frame on: before it none are found)?</summary>
    public bool FxReady => _fxSectors != null;

    /// <summary>The light-effect sector whose level counts at p (a point a hair over a ceiling light looks just below it
    /// too): the search FxLightLevel makes, for a caller keeping the answer while p stays.</summary>
    public Sector FxLightSector(Vector3 p) =>
        FxSectorAt(p + new Vector3(0, 0, 0.0625f)) ?? FxSectorAt(p - new Vector3(0, 0, 2));

    /// <summary>How much of its light effect a sector has on now (0..1; 1 without one).</summary>
    public static float FxLevelOf(Sector s)
    {
        if (s == null || s.FxAmp == 0)
            return 1;
        float f = System.Math.Clamp((float)s.FxShade / s.FxAmp, 0f, 1f);
        return s.FxAmp < 0 ? f : 1 - f;
    }

    /// <summary>fxLightShade: the shade offset a sprite (or, weapon, the player's weapon) takes from the light effect at
    /// p; a point a hair above a ceiling light looks just below it too.</summary>
    public int FxLightShade(Vector3 p, bool weapon)
    {
        var s = FxSectorAt(p) ?? FxSectorAt(p - new Vector3(0, 0, 2));
        if (s == null)
            return 0;
        return System.Math.Clamp(weapon ? s.FxWeapon : s.FxSprite, -128, 127);
    }
}
