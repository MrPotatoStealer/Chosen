// Chosen core: original code (Play: the level run by Blood's events, Blood.EventSim, in the 2D view or the 3D
// preview).
using System;
using System.Collections.Generic;
using System.Linq;
using Chosen.Forge.Blood;
using Chosen.Forge.Document;

namespace Chosen.Forge.View;

/// <summary>
/// Play: the level run from its start by Blood's events (Blood.EventSim: the level-start channel, switches, timers,
/// links, the busy sectors, lighting, bob, slopes) in the shown map (a copy of the stored one); a player's doings are
/// the camera's (Preview3D: entering sectors, E using what it aims at).
/// </summary>
public sealed class MoverPlay
{
    public readonly MapDocument Stored;
    public readonly EventSim Sim;

    /// <summary>shown: a fresh copy of stored, run from the level's start.</summary>
    public MoverPlay(MapDocument stored, MapDocument shown)
    {
        Stored = stored;
        Sim = new EventSim(stored, shown);
        Sim.Start();
    }

    /// <summary>The clock on by delta seconds: the sectors drawn differently since (moved, lit, tilted).</summary>
    public List<int> Step(MapDocument shown, double delta) => Sim.Step(delta).ToList();

    /// <summary>What moving sectors change: themselves, the neighbours sharing their walls and points, and the
    /// sprites in them.</summary>
    public (HashSet<int> sectors, List<int> sprites) Touched(List<int> moved)
    {
        var sectors = new HashSet<int>(moved);
        foreach (int s in moved)
        {
            var sec = Stored.Sectors[s];
            for (int w = sec.WallPtr; w < sec.WallPtr + sec.WallNum; w++)
            {
                if (Stored.Walls[w].NextSector >= 0)
                    sectors.Add(Stored.Walls[w].NextSector);
                if (w < Stored.PointOf.Length && Stored.PointOf[w] is int p && p >= 0)
                    foreach (int o in Stored.Points[p].Walls)
                        sectors.Add(Stored.Walls[o].Sector);
            }
        }
        var set = new HashSet<int>(moved);
        var sprites = Enumerable.Range(0, Stored.Sprites.Count).Where(i => set.Contains(Stored.Sprites[i].SectNum)).ToList();
        return (sectors, sprites);
    }
}

public partial class Preview3D
{
    /// <summary>Play moved some sectors (Doc posed already): their faces and sprites made again.</summary>
    public void Moved(MoverPlay play, List<int> moved)
    {
        if (!Visible || Doc == null || moved.Count == 0)
            return;
        var (sectors, sprites) = play.Touched(moved);
        // (what pans changes as sectors go on and off)
        _panSectors = Blood.PanMotion.Sectors(Doc, PanOn);
        Remake(sectors, sprites);
    }
}
