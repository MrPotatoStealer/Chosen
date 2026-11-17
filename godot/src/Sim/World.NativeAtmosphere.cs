using System.Collections.Generic;
using Chosen.Data;
using Godot;

namespace Chosen.Sim;

/// <summary>
/// The enhanced atmosphere of a level from its Build data (the old converter's guesses, weather_keys and
/// mist_entities): its weather by episode, and the sectors its ground mist lies in.
/// </summary>
public sealed partial class World
{
    /// <summary>Each sector: under the sky (bit 1: a parallax ceiling, or a link up to a room that is: E1M1's yard under its
    /// rooftops' link), and a room standing over such a sector in plan (bit 2: E1M1's rooftops, drawn through the link and
    /// not really there: the light from the sky goes through them), and too far in for that light (bit 4: more than SkyReach
    /// openings from any sector under the sky, doors counted open: what its shadows let through there is only their leaks,
    /// E1M5's blue edges under a ceiling).</summary>
    public byte[] NativeSkyFlags()
    {
        var b = Board;
        int n = b.SectorCount;
        var up = new Dictionary<int, int>();
        foreach (var l in Collision.Links)
            if (l.Dir < 0)
                up[l.Sector] = l.FarSector;
        bool Open(int s, int depth = 0) =>
            (b.Sectors[s].CeilingStat & 1) != 0 || depth < 4 && up.TryGetValue(s, out int u) && b.Valid(u) && Open(u, depth + 1);
        var flags = new byte[n];
        for (int s = 0; s < n; s++)
            if (b.Valid(s) && Open(s))
                flags[s] |= 1;
        for (int s = 0; s < n; s++)
        {
            if ((flags[s] & 1) == 0 || !b.Valid(s))
                continue;
            var over = Map.OverlapsOf(s);
            for (int i = 0; i < over.Count; i++)
            {
                int o = over[i];
                if (b.Valid(o) && b.Sectors[o].FloorZ < b.Sectors[s].CeilingZ + 256)
                    flags[o] |= 2;
            }
            foreach (var (lo, hi) in Map.Stacks)
                if (lo == s && b.Valid(hi))
                    flags[hi] |= 2;
        }
        foreach (var l in Collision.Links)
            if (l.Dir < 0 && b.Valid(l.FarSector) && b.Valid(l.Sector))
                flags[l.FarSector] |= 2;
        var steps = new int[n];
        var todo = new Queue<int>();
        for (int s = 0; s < n; s++)
        {
            steps[s] = (flags[s] & 1) != 0 ? 0 : -1;
            if (steps[s] == 0)
                todo.Enqueue(s);
        }
        while (todo.Count > 0)
        {
            int s = todo.Dequeue();
            if (steps[s] >= SkyReach)
                continue;
            foreach (int o in b.Neighbours[s])
                if (steps[o] < 0)
                {
                    steps[o] = steps[s] + 1;
                    todo.Enqueue(o);
                }
        }
        for (int s = 0; s < n; s++)
            if (steps[s] < 0)
                flags[s] |= 4;
        SkySteps = steps;
        return flags;
    }

    /// <summary>Each sector's openings from the sky, -1 past SkyReach (set by NativeSkyFlags).</summary>
    public int[] SkySteps { get; private set; }

    const int SkyReach = 2;

    /// <summary>The surfaces of the level's water (r_watermist): groups of adjacent sectors over a water link.</summary>
    public List<List<int>> NativeWaterGroups()
    {
        var water = new HashSet<int>();
        foreach (var l in Collision.Links)
            if (l.Dir > 0 && l.Type == K.kMarkerUpWater && Board.Valid(l.Sector))
                water.Add(l.Sector);
        var groups = new List<List<int>>();
        while (water.Count > 0)
        {
            var e = water.GetEnumerator();
            e.MoveNext();
            var todo = new Stack<int>();
            todo.Push(e.Current);
            water.Remove(e.Current);
            var g = new List<int>();
            while (todo.Count > 0)
            {
                int s = todo.Pop();
                g.Add(s);
                foreach (int w in Board.SectorWalls(s))
                {
                    int n = Board.Walls[w].NextSector;
                    if (n >= 0 && water.Remove(n))
                        todo.Push(n);
                }
            }
            groups.Add(g);
        }
        return groups;
    }

    /// <summary>The ground mist's areas as the game guesses them for a map without its own atmospheres
    /// (Shared.Blood.Map.AtmosphereRules.GuessMistGroups): open to the sky (a parallax ceiling, or a link up to a room open
    /// to it), not a parallax floor (the void) or a link down, not tiny, not raised over all its open neighbours (a roof, a
    /// ledge), not moving.</summary>
    public List<List<int>> NativeMistGroups()
    {
        var b = Board;
        var floorLink = new HashSet<int>();
        var upLink = new Dictionary<int, int>();
        foreach (var l in Collision.Links)
            if (l.Dir > 0)
                floorLink.Add(l.Sector);
            else
                upLink[l.Sector] = l.FarSector;
        bool SkyAbove(int s, int depth = 0) =>
            (b.Sectors[s].CeilingStat & 1) != 0 || depth < 4 && upLink.TryGetValue(s, out int u) && SkyAbove(u, depth + 1);
        bool OpenSky(int s) => SkyAbove(s) && (b.Sectors[s].FloorStat & 1) == 0 && !floorLink.Contains(s);
        bool Moving(int s)
        {
            if (XSectorOf(s) is not { } xs)
                return false;
            if (xs.Type is K.kSectorPath or K.kSectorRotateStep or K.kSectorSlide or K.kSectorRotate)
                return true;
            if (xs.Type is K.kSectorZMotion or K.kSectorSlideMarked or K.kSectorRotateMarked && xs.OffFloorZ != xs.OnFloorZ)
                return true;
            return xs.BobSpeed != 0 && xs.BobFloor != 0;
        }
        double Area(int s)
        {
            double a = 0;
            foreach (var loop in b.Loops[s])
            {
                double r = 0;
                for (int k = 0; k < loop.Length; k++)
                {
                    var w0 = b.Walls[loop[k]];
                    var w1 = b.Walls[w0.Point2];
                    r += (double)w0.X * w1.Y - (double)w1.X * w0.Y;
                }
                a += System.Math.Abs(r) / 2;
            }
            return a;
        }
        return Shared.Blood.Map.AtmosphereRules.GuessMistGroups(b.SectorCount, OpenSky, s => b.Sectors[s].FloorZ, Area, Moving, s => b.Neighbours[s]);
    }
}
