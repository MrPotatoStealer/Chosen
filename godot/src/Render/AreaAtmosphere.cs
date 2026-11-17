using System.Collections.Generic;
using Chosen.Shared.Blood.Map;
using Chosen.Sim;
using Godot;

namespace Chosen.Render;

/// <summary>
/// The level's atmospheres and each sector's (Shared.Blood.Map.MapAtmosphere): a .MAPX's own (its ATMO chunk, placed in
/// Forge's Atmosphere mode), else the game's guesses in the same form (AtmosphereRules.Guess: the level's fog from its
/// visibility, the weather by episode, "Open ground" with the ground mist over the open ground the game always gave it). EnhancedFog takes
/// the camera's sector's distance fog (fading into it), GroundMist each area's mist, Weather each place's weather and
/// wind.
/// </summary>
public static class AreaAtmosphere
{
    static List<MapAtmosphere> _list = new() { new MapAtmosphere() };
    static int[] _sector = System.Array.Empty<int>();

    /// <summary>The level's atmospheres (the first its own).</summary>
    public static IReadOnlyList<MapAtmosphere> List => _list;

    /// <summary>The map has its own (else guessed).</summary>
    public static bool Own { get; private set; }

    /// <summary>A level under open sky anywhere.</summary>
    public static bool AnySky { get; private set; }

    /// <summary>A level's atmospheres: its own, else guessed from its Build data (after World.LoadNative: the links).</summary>
    public static void Setup(World world, string mapName)
    {
        var b = world.Board;
        int n = b.SectorCount;
        AnySky = false;
        for (int s = 0; s < n; s++)
            if ((b.Sectors[s].CeilingStat & 1) != 0 && (b.Sectors[s].FloorStat & 1) == 0)
                AnySky = true;
        List<int> per;
        if (b.File?.Atmospheres is { Count: > 0 } own)
        {
            _list = own;
            per = b.File.SectorAtmospheres ?? new List<int>();
            Own = true;
        }
        else
        {
            (_list, per) = AtmosphereRules.Guess(mapName, AnySky, n, world.NativeMistGroups());
            Own = false;
        }
        _sector = new int[n];
        for (int s = 0; s < n; s++)
            _sector[s] = s < per.Count && per[s] > 0 && per[s] < _list.Count ? per[s] : 0;
    }

    /// <summary>Sector s's atmosphere's index (0: the level's).</summary>
    public static int IndexOf(int s) => s >= 0 && s < _sector.Length ? _sector[s] : 0;

    public static MapAtmosphere Of(int s) => _list[IndexOf(s)];

    /// <summary>Any atmosphere with rain (lightning and thunder come with it).</summary>
    public static bool AnyRain
    {
        get
        {
            foreach (var a in _list)
                if (a.Rain > 0)
                    return true;
            return false;
        }
    }

    /// <summary>The most of each weather any atmosphere has (a layer's particles: the most; each place keeps its share).</summary>
    public static (float rain, float snow, float leaves) MostWeather()
    {
        float r = 0, sn = 0, l = 0;
        foreach (var a in _list)
        {
            r = Mathf.Max(r, a.Rain);
            sn = Mathf.Max(sn, a.Snow);
            l = Mathf.Max(l, a.Leaves);
        }
        return (r, sn, l);
    }

    /// <summary>
    /// The ground mist's areas: adjacent sectors of one atmosphere with mist (MistHeight over 0) at about one height (a
    /// step of 32 Quake units at most, as the game's guess: a yard's beside a roof's is two), each with its atmosphere.
    /// Only sectors not moving: a moving floor's mist stays where it was at load.
    /// </summary>
    public static List<(List<int> sectors, MapAtmosphere atmosphere)> MistAreas(World world)
    {
        var b = world.Board;
        int n = b.SectorCount;
        var areas = new List<(List<int>, MapAtmosphere)>();
        var seen = new bool[n];
        for (int s0 = 0; s0 < n; s0++)
        {
            if (seen[s0] || !b.Valid(s0) || Of(s0).MistHeight <= 0)
                continue;
            int idx = IndexOf(s0);
            var group = new List<int>();
            var todo = new Stack<int>();
            todo.Push(s0);
            seen[s0] = true;
            while (todo.Count > 0)
            {
                int c = todo.Pop();
                group.Add(c);
                foreach (int o in b.Neighbours[c])
                    if (!seen[o] && b.Valid(o) && IndexOf(o) == idx && System.Math.Abs(b.Sectors[o].FloorZ - b.Sectors[c].FloorZ) <= 32 * 256)
                    {
                        seen[o] = true;
                        todo.Push(o);
                    }
            }
            group.Sort();
            areas.Add((group, _list[idx]));
        }
        return areas;
    }

    /// <summary>The areas with their own distance fog (FogDistance over 0): adjacent sectors of one atmosphere, its index,
    /// its fog's end (Quake units) and colour (null: the level's). GroundMist shows each as a bank of fog from outside.</summary>
    public static List<(List<int> sectors, int atmosphere, float end, Color? colour)> FogAreas(World world)
    {
        var b = world.Board;
        int n = b.SectorCount;
        var areas = new List<(List<int>, int, float, Color?)>();
        var seen = new bool[n];
        for (int s0 = 0; s0 < n; s0++)
        {
            if (seen[s0] || !b.Valid(s0) || Of(s0).FogDistance <= 0)
                continue;
            int idx = IndexOf(s0);
            var group = new List<int>();
            var todo = new Stack<int>();
            todo.Push(s0);
            seen[s0] = true;
            while (todo.Count > 0)
            {
                int c = todo.Pop();
                group.Add(c);
                foreach (int o in b.Neighbours[c])
                    if (!seen[o] && b.Valid(o) && IndexOf(o) == idx)
                    {
                        seen[o] = true;
                        todo.Push(o);
                    }
            }
            var (end, colour) = FogOf(_list[idx]);
            areas.Add((group, idx, end, colour));
        }
        return areas;
    }

    /// <summary>An atmosphere's fog: where it is complete (Quake units; -1: the level's, from its visibility) and its own
    /// colour (null: the level's).</summary>
    public static (float distance, Color? colour) FogOf(MapAtmosphere a) =>
        (a.FogDistance > 0 ? a.FogDistance / 16f : -1f, a.FogOwnColour ? new Color(a.FogR / 255f, a.FogG / 255f, a.FogB / 255f) : null);

    /// <summary>An atmosphere's mist colour (null: the level's).</summary>
    public static Color? MistColourOf(MapAtmosphere a) => a.MistOwnColour ? new Color(a.MistR / 255f, a.MistG / 255f, a.MistB / 255f) : null;
}
