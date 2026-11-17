// Chosen core: original code (a gradient over sectors: heights stepping like stairs, or a brightness fading across).
using System;
using System.Collections.Generic;
using System.Linq;
using Chosen.Forge.Document;

namespace Chosen.Forge.Tools;

/// <summary>
/// The Gradient tool: over sectors from the first to the last, a value going from the first's to the last's: the
/// floor's height, the ceiling's, both (the room's height kept: stairs that keep their headroom), or the brightness
/// (shade) of the floors, ceilings and walls. Each sector's place along the way is where its middle lies on the line
/// from the first sector's middle to the last's; Equal steps spreads them evenly in that order (stairs), By distance by
/// how far along they are. Heights can be rounded to whole game units.
/// </summary>
public static class SectorGradient
{
    public static readonly string[] Whats = { "floor", "ceiling", "both", "shade" };
    public static readonly string[] WhatNames = { "Floor height", "Ceiling height", "Floor and ceiling (keep the room's height)", "Brightness (shade)" };

    /// <summary>The shade parts: floors 1, ceilings 2, walls 4.</summary>
    public const int ShadeFloors = 1, ShadeCeilings = 2, ShadeWalls = 4;

    /// <summary>Each sector's place along the way, 0 (the first) to 1 (the last), in their order along it.</summary>
    public static List<(int sector, double t)> Places(MapDocument d, IList<int> sectors, bool equal)
    {
        var l = new List<(int, double)>();
        if (sectors.Count == 0)
            return l;
        if (sectors.Count == 1)
            return new List<(int, double)> { (sectors[0], 0) };
        var a = d.Labels[sectors[0]];
        var b = d.Labels[sectors[^1]];
        var axis = b - a;
        double len2 = axis.LengthSquared();
        // (first and last fixed; those between by where their middles lie along the line, in the order chosen when
        // the first and last share a place)
        double Along(int s, int i) => i == 0 ? 0 : i == sectors.Count - 1 ? 1
            : len2 < 1e-6 ? (double)i / (sectors.Count - 1) : Math.Clamp((d.Labels[s] - a).Dot(axis) / len2, 0, 1);
        var along = sectors.Select((s, i) => (s, t: Along(s, i), i)).OrderBy(x => x.t).ThenBy(x => x.i).ToList();
        for (int k = 0; k < along.Count; k++)
            l.Add((along[k].s, equal ? (double)k / (along.Count - 1) : along[k].t));
        return l;
    }

    /// <summary>The value a sector has now (a floor's or ceiling's height, Build z; a shade: its floor's, else its
    /// ceiling's, else its first wall's).</summary>
    public static int Now(MapDocument d, int s, string what, int parts)
    {
        var sec = d.Sectors[s];
        return what switch
        {
            "ceiling" => sec.CeilingZ,
            "shade" => (parts & ShadeFloors) != 0 ? sec.FloorShade : (parts & ShadeCeilings) != 0 ? sec.CeilingShade
                : sec.WallNum > 0 ? d.Walls[sec.WallPtr].Shade : sec.FloorShade,
            _ => sec.FloorZ,
        };
    }

    /// <summary>The gradient applied (the caller took the undo level): from the first sector's value to the last's
    /// (from, to: heights in Build z, a shade -128..127); round: heights to whole game units (256 Build z). The sectors
    /// changed.</summary>
    public static int Apply(MapDocument d, IList<int> sectors, string what, int from, int to, bool equal, bool round, int parts)
    {
        int changed = 0;
        foreach (var (s, t) in Places(d, sectors, equal))
        {
            if (s < 0 || s >= d.Sectors.Count || !d.SectorEditable(s))
                continue;
            double v = from + (to - from) * t;
            var sec = d.Sectors[s];
            if (what == "shade")
            {
                int sh = Math.Clamp((int)Math.Round(v), -128, 127);
                if ((parts & ShadeFloors) != 0)
                    sec.FloorShade = sh;
                if ((parts & ShadeCeilings) != 0)
                    sec.CeilingShade = sh;
                if ((parts & ShadeWalls) != 0)
                    for (int w = sec.WallPtr; w < sec.WallPtr + sec.WallNum; w++)
                        d.Walls[w].Shade = sh;
                changed++;
                continue;
            }
            int z = round ? (int)Math.Round(v / 256.0) * 256 : (int)Math.Round(v);
            switch (what)
            {
                case "floor":
                    sec.FloorZ = z;
                    break;
                case "ceiling":
                    sec.CeilingZ = z;
                    break;
                default:
                    // both: the floor to the value, the ceiling with it (the room's height kept)
                    sec.CeilingZ += z - sec.FloorZ;
                    sec.FloorZ = z;
                    break;
            }
            changed++;
        }
        return changed;
    }
}
