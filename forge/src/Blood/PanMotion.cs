// Chosen Blood module (GPL v2): Blood's panning floors, ceilings and walls (an x-sector's pan velocity and angle, an
// x-wall's x and y velocities) as NBlood's sectorfx.cpp moves them, for the 3D preview: how far a surface's picture
// has gone after a number of game frames.
using System;
using System.Collections.Generic;
using Chosen.Forge.Document;
using Chosen.Forge.View;

namespace Chosen.Forge.Blood;

public static class PanMotion
{
    /// <summary>Blood moves the panning surfaces once a game frame: 120 tics a second, 4 a frame.</summary>
    public const double FramesPerSecond = 30;

    /// <summary>log2 of a picture's side rounded up (Build's picsiz).</summary>
    static int Bits(int n)
    {
        int b = 0;
        while ((1 << b) < n && b < 15)
            b++;
        return b;
    }

    /// <summary>An x-sector's surfaces pan: its velocity set, and always (pan always), or the sector on (on: shown On;
    /// else its stored state).</summary>
    static bool SectorPans(MapDocument d, int s, bool on) =>
        d.Sectors[s].Xs is { PanVel: > 0 } x && (x.PanAlways != 0 || on || x.State != 0 || x.Busy != 0);

    static bool WallPans(MapDocument d, int w, bool on) =>
        d.Walls[w].Xw is { } x && (x.PanXVel != 0 || x.PanYVel != 0) && (x.PanAlways != 0 || on || x.State != 0 || x.Busy != 0);

    /// <summary>The sectors whose floor, ceiling or walls pan (the preview remakes those as they move).</summary>
    public static List<int> Sectors(MapDocument d, bool on)
    {
        var list = new List<int>();
        for (int s = 0; s < d.Sectors.Count; s++)
        {
            bool pans = SectorPans(d, s, on) && (d.Sectors[s].Xs.PanFloor != 0 || d.Sectors[s].Xs.PanCeiling != 0);
            var sec = d.Sectors[s];
            for (int w = sec.WallPtr; !pans && w < sec.WallPtr + sec.WallNum && w < d.Walls.Count; w++)
                pans = WallPans(d, w, on);
            if (pans)
                list.Add(s);
        }
        return list;
    }

    /// <summary>A floor's or ceiling's panning after frames game frames, in Build's panning units (256 a picture):
    /// each frame the velocity x 1024 x 4 along the pan angle (turned back a quarter for one aligned to its first wall;
    /// a ceiling's mirrored), over the picture's side (halved with double size), the y the other way.</summary>
    public static (double du, double dv) Plane(MapDocument d, int s, bool ceiling, double frames, bool on)
    {
        if (frames == 0 || s < 0 || s >= d.Sectors.Count || !SectorPans(d, s, on))
            return (0, 0);
        var sec = d.Sectors[s];
        var x = sec.Xs;
        if ((ceiling ? x.PanCeiling : x.PanFloor) == 0)
            return (0, 0);
        int stat = ceiling ? sec.CeilingStat : sec.FloorStat;
        int angle = x.PanAngle + 1024;
        if ((stat & 64) != 0)
            angle -= 512;
        if (ceiling)
            angle = -angle;
        double a = angle * Math.PI / 1024;
        var size = PreviewMesher.TileSize(ceiling ? sec.CeilingPicnum : sec.FloorPicnum);
        int dbl = (stat & 8) != 0 ? 1 : 0;
        double speed = x.PanVel * 1024.0 * 4;
        // (px: the panning x 256 plus its fraction)
        double px = speed * Math.Cos(a) / (1 << Math.Max(0, Bits(size.X) - dbl));
        double py = -speed * Math.Sin(a) / (1 << Math.Max(0, Bits(size.Y) - dbl));
        // (a whole picture's turn taken off: the same place, and the numbers stay small)
        return (frames * px / 256 % 256, frames * py / 256 % 256);
    }

    /// <summary>A wall's panning after frames game frames (x in pixels as its xpanning, y in 256ths of the picture as
    /// its ypanning): each frame its velocity x 1024 x 4 over the picture's side.</summary>
    public static (double du, double dv) Wall(MapDocument d, int w, double frames, bool on)
    {
        if (frames == 0 || w < 0 || w >= d.Walls.Count || !WallPans(d, w, on))
            return (0, 0);
        var x = d.Walls[w].Xw;
        var size = PreviewMesher.TileSize(d.Walls[w].Picnum);
        double px = x.PanXVel * 1024.0 * 4 / (1 << Bits(size.X));
        double py = x.PanYVel * 1024.0 * 4 / (1 << Bits(size.Y));
        return (frames * px / 256 % Math.Max(1, size.X), frames * py / 256 % 256);
    }
}
