// Chosen core: original code (a map's atmospheres: fog, ground mist, weather and wind by sector; the .MAPX's ATMO).
using System;
using System.Collections.Generic;
using System.Linq;
using Chosen.Shared.Blood.Map;

namespace Chosen.Forge.Document;

/// <summary>
/// The map's atmospheres (Extras.Atmospheres, the first the map's own: the sectors given none have it) and each
/// sector's (Extras.SectorAtmospheres by index). HasAtmosphere: the map has its own (written to the .MAPX; the game then
/// gives it these, none guessed); until then the game guesses (AtmosphereRules.Guess), as Generate Atmosphere does
/// here. Each sector's follows it through every change (Commit), a new one taking its neighbours'.
/// </summary>
public sealed partial class MapDocument
{
    public bool HasAtmosphere => Extras.HasAtmosphere && Extras.Atmospheres.Count > 0;

    /// <summary>The atmospheres (at least the map's own, "Level").</summary>
    public List<MapAtmosphere> Atmospheres
    {
        get
        {
            if (Extras.Atmospheres.Count == 0)
                Extras.Atmospheres.Add(new MapAtmosphere());
            return Extras.Atmospheres;
        }
    }

    /// <summary>Sector s's atmosphere's index (0: the map's own).</summary>
    public int AtmosphereOf(int s) =>
        s >= 0 && s < Extras.SectorAtmospheres.Count && Extras.SectorAtmospheres[s] > 0 && Extras.SectorAtmospheres[s] < Extras.Atmospheres.Count
            ? Extras.SectorAtmospheres[s] : 0;

    public void SetAtmosphere(int s, int a)
    {
        while (Extras.SectorAtmospheres.Count < Sectors.Count)
            Extras.SectorAtmospheres.Add(0);
        if (s >= 0 && s < Extras.SectorAtmospheres.Count)
            Extras.SectorAtmospheres[s] = a;
    }

    /// <summary>The map's atmospheres become its own (a first edit of them): the game's guesses first (what it showed
    /// until then), so nothing changes but the edit.</summary>
    public void OwnAtmosphere()
    {
        if (HasAtmosphere)
            return;
        GenerateAtmosphere();
    }

    /// <summary>A new atmosphere (a copy of another's settings, or the map's own; named so no other has its name); its
    /// index.</summary>
    public int AddAtmosphere(int copyOf = 0, string name = null)
    {
        OwnAtmosphere();
        var a = Atmospheres[Math.Clamp(copyOf, 0, Atmospheres.Count - 1)].Clone();
        a.Name = UniqueAtmosphereName(name ?? "Atmosphere");
        Atmospheres.Add(a);
        return Atmospheres.Count - 1;
    }

    public string UniqueAtmosphereName(string name)
    {
        string n = name;
        for (int k = 2; Atmospheres.Any(a => a.Name == n); k++)
            n = $"{name} {k}";
        return n;
    }

    /// <summary>An atmosphere deleted (not the map's own): its sectors get the map's own, the later ones' indices move down.</summary>
    public bool DeleteAtmosphere(int i)
    {
        if (i <= 0 || i >= Atmospheres.Count)
            return false;
        for (int s = 0; s < Extras.SectorAtmospheres.Count; s++)
        {
            int a = Extras.SectorAtmospheres[s];
            Extras.SectorAtmospheres[s] = a == i ? 0 : a > i ? a - 1 : a;
        }
        Atmospheres.RemoveAt(i);
        return true;
    }

    /// <summary>The atmospheres the game would guess (AtmosphereRules.Guess: the level's, the weather by episode, "Ground
    /// mist" over the open ground) made the map's own, in place of any it had.</summary>
    public void GenerateAtmosphere()
    {
        var (list, sectors) = AtmosphereRules.Guess(Map.Name, AtmosphereRules.AnySky(Sectors), Sectors.Count, GuessMistGroups());
        Extras.Atmospheres.Clear();
        Extras.Atmospheres.AddRange(list);
        Extras.SectorAtmospheres.Clear();
        Extras.SectorAtmospheres.AddRange(sectors);
        Extras.HasAtmosphere = true;
    }

    /// <summary>The ground mist's sectors as the game guesses them (AtmosphereRules.GuessMistGroups, from this map: its
    /// links up and down, its moving sectors).</summary>
    public List<List<int>> GuessMistGroups()
    {
        int n = Sectors.Count;
        // (a link: the room with the upper marker sees up through its ceiling into the one with the lower marker, whose
        // floor goes down: Blood's gUpperLink and gLowerLink)
        var floorLink = new HashSet<int>();
        var upLink = new Dictionary<int, int>();
        foreach (var l in Blood.MapLinks.All(this))
            if (l.Kind == View.LinkKind.Link && l.SectorA >= 0 && l.SectorB >= 0)
            {
                upLink[l.SectorA] = l.SectorB;
                floorLink.Add(l.SectorB);
            }
        bool SkyAbove(int s, int depth = 0) =>
            (Sectors[s].CeilingStat & 1) != 0 || depth < 4 && upLink.TryGetValue(s, out int u) && u >= 0 && u < n && SkyAbove(u, depth + 1);
        bool OpenSky(int s) => SkyAbove(s) && (Sectors[s].FloorStat & 1) == 0 && !floorLink.Contains(s);
        bool Moving(int s)
        {
            if (Sectors[s].Xs is not { } xs)
                return false;
            int t = Sectors[s].Type;
            // (path, rotate step, slide, rotate; z motion and the marked slide and rotate when their floor moves)
            if (t is 612 or 613 or 616 or 617)
                return true;
            if (t is 600 or 614 or 615 && xs.OffFloorZ != xs.OnFloorZ)
                return true;
            return xs.BobSpeed != 0 && xs.BobFloor != 0;
        }
        var neighbours = new List<int>[n];
        for (int s = 0; s < n; s++)
        {
            neighbours[s] = new List<int>();
            var sec = Sectors[s];
            for (int w = sec.WallPtr; w < sec.WallPtr + sec.WallNum && w < Walls.Count; w++)
                if (Walls[w].NextSector is int ns and >= 0 && ns < n && !neighbours[s].Contains(ns))
                    neighbours[s].Add(ns);
        }
        return AtmosphereRules.GuessMistGroups(n, OpenSky, s => Sectors[s].FloorZ, s => s < Areas.Length ? Areas[s] : 0, Moving, s => neighbours[s]);
    }

    /// <summary>The atmospheres changed and nothing else (their settings, the sectors given them): the views redraw
    /// (the 3D preview's atmosphere made again without its meshes). AtmosphereOnly is true while Changed runs.</summary>
    public void NotifyAtmosphereChanged()
    {
        Dirty = true;
        AtmosphereOnly = true;
        try
        {
            Changed?.Invoke();
        }
        finally
        {
            AtmosphereOnly = false;
        }
    }

    public bool AtmosphereOnly { get; private set; }

    /// <summary>Each sector's atmosphere by its object, as Commit writes the sectors back.</summary>
    Dictionary<BSector, int> AtmospheresByObject()
    {
        var d = new Dictionary<BSector, int>(ReferenceEqualityComparer.Instance);
        for (int s = 0; s < Sectors.Count; s++)
            d[Sectors[s]] = AtmosphereOf(s);
        return d;
    }

    /// <summary>After Commit (the walls in place): each kept sector's own atmosphere, a new one its neighbours' (the
    /// commonest among those kept: a room split, drawn in or beside one), else the map's own.</summary>
    void RestoreAtmospheres(Dictionary<BSector, int> byObject)
    {
        if (!HasAtmosphere)
        {
            Extras.SectorAtmospheres.Clear();
            return;
        }
        var list = new int[Sectors.Count];
        var kept = new bool[Sectors.Count];
        for (int s = 0; s < Sectors.Count; s++)
            if (byObject.TryGetValue(Sectors[s], out int a))
                (list[s], kept[s]) = (a, true);
        for (int s = 0; s < Sectors.Count; s++)
        {
            if (kept[s])
                continue;
            var votes = new Dictionary<int, int>();
            var sec = Sectors[s];
            for (int w = sec.WallPtr; w < sec.WallPtr + sec.WallNum && w < Walls.Count; w++)
                if (Walls[w].NextSector is int ns and >= 0 && ns < Sectors.Count && kept[ns])
                    votes[list[ns]] = votes.GetValueOrDefault(list[ns]) + 1;
            list[s] = votes.Count == 0 ? 0 : votes.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key).First().Key;
        }
        Extras.SectorAtmospheres.Clear();
        Extras.SectorAtmospheres.AddRange(list);
    }
}
