using Chosen.Core;
using Godot;
using Chosen.Shared.Blood.Map;

namespace Chosen.Sim.Build;

/// <summary>Console commands for the Build layer.</summary>
public static class BuildDebug
{
    public static void Register()
    {
        GameConsole.Register("bmapcheck", "bmapcheck <map|all>", "read Blood .MAP files (Sim.Build) and print a summary of each",
            a =>
            {
                var names = new System.Collections.Generic.List<string>();
                if (a.Length > 1 && a[1] != "all")
                    names.Add(a[1]);
                else
                {
                    foreach (var f in System.IO.Directory.GetFiles(Data.BloodData.Folder.PathJoin("maps"), "*.map"))
                        names.Add(f);
                    foreach (var ad in Data.Addons.All)
                        if (System.IO.Directory.Exists(ad.Folder.PathJoin("maps")))
                            foreach (var f in System.IO.Directory.GetFiles(ad.Folder.PathJoin("maps"), "*.map"))
                                names.Add(f);
                }
                foreach (var n in names)
                {
                    string path = System.IO.File.Exists(n) ? n : Data.Addons.BuildMapPath(n);
                    try
                    {
                        var m = BloodMapFile.Load(path);
                        long sum = 0;
                        int xs = 0, xw = 0, xsp = 0;
                        foreach (var s in m.Sectors)
                            if (s.Xs != null) { xs++; sum += s.Xs.BusyTimeA + s.Xs.OffFloorZ % 1000 + s.Xs.BobSpeed + s.Xs.Marker0; }
                        foreach (var w in m.Walls)
                            if (w.Xw != null) { xw++; sum += w.Xw.TxID + w.Xw.PanXVel; }
                        foreach (var sp in m.Sprites)
                        {
                            sum += sp.Type + sp.Ang;
                            if (sp.Xsp != null) { xsp++; sum += sp.Xsp.Data1 + sp.Xsp.TargetX % 1000 + sp.Xsp.LSkill; }
                        }
                        GameConsole.Print($"BMAP {m.Name} sectors {m.Sectors.Count} walls {m.Walls.Count} sprites {m.Sprites.Count} x {xs} {xw} {xsp} vis {m.Visibility} sum {sum}");
                        // the map rules (shared library: Chosen.Shared.Blood.Map.MapValidator) on the map as stored
                        var issues = MapValidator.Check(BloodMapFile.Read(path), Limits.NBlood);
                        int errors = issues.FindAll(i => i.Severity == Chosen.Shared.Engine.Geometry.Severity.Error).Count;
                        GameConsole.Print($"BMAP {m.Name} check: {errors} errors, {issues.Count - errors} warnings");
                        foreach (var i in issues.FindAll(i => i.Severity == Chosen.Shared.Engine.Geometry.Severity.Error).GetRange(0, System.Math.Min(errors, 5)))
                            GameConsole.Print("  " + i);
                    }
                    catch (System.Exception e)
                    {
                        GameConsole.Print($"BMAP {n}: {e.Message}");
                    }
                }
            });
        GameConsole.Register("geomcheck", "geomcheck <map|all>",
            "compare the game's Build geometry (Board) with the shared library's (Chosen.Shared.Engine BuildGeom): inside over a grid per sector, slope heights at its walls' points",
            a =>
            {
                var paths = MapPaths(a.Length > 1 ? a[1] : "all");
                long points = 0, insideDiff = 0, zPoints = 0, zDiff = 0;
                double zWorst = 0;
                string worstAt = "";
                foreach (var path in paths)
                {
                    var file = BloodMapFile.Load(path);
                    var b = new Board(file);
                    long mi = 0, mz = 0;
                    for (int s = 0; s < file.Sectors.Count; s++)
                    {
                        if (file.Sectors[s].WallNum < 3)
                            continue;
                        var (x0, y0, x1, y1) = Chosen.Shared.Engine.Geometry.BuildGeom.SectorBounds(file, s);
                        // a grid over the bounds, off the integer lattice (walls run between integer points)
                        for (int i = 0; i < 10; i++)
                            for (int j = 0; j < 10; j++)
                            {
                                double x = x0 + (x1 - x0) * (i + 0.37) / 10, y = y0 + (y1 - y0) * (j + 0.61) / 10;
                                points++;
                                if (b.Inside(x, y, s) != Chosen.Shared.Engine.Geometry.BuildGeom.Inside(file, s, x, y))
                                    mi++;
                            }
                        var sec = file.Sectors[s];
                        for (int w = sec.WallPtr; w < sec.WallPtr + sec.WallNum; w++)
                        {
                            double x = file.Walls[w].X, y = file.Walls[w].Y;
                            b.GetZsOfSlope(s, x, y, out double c1, out double f1);
                            Chosen.Shared.Engine.Geometry.BuildGeom.GetZsOfSlope(file, s, x, y, out double c2, out double f2);
                            zPoints++;
                            double d = System.Math.Max(System.Math.Abs(c1 - c2), System.Math.Abs(f1 - f2));
                            if (d > zWorst)
                            {
                                zWorst = d;
                                var bw = b.Walls[w];
                                worstAt = $"{file.Name} sector {s} wall {w} ({x},{y}) board ceil {c1} floor {f1} geom ceil {c2} floor {f2}; file first wall {file.Walls[sec.WallPtr].X},{file.Walls[sec.WallPtr].Y} board {b.Walls[sec.WallPtr].X},{b.Walls[sec.WallPtr].Y}; heinum c {sec.CeilingHeinum} f {sec.FloorHeinum} stat c {sec.CeilingStat} f {sec.FloorStat}; board heinum f {b.Sectors[s].FloorHeinum} z f {b.Sectors[s].FloorZ} file z f {sec.FloorZ}";
                            }
                            if (d > 2)
                                mz++;
                        }
                    }
                    insideDiff += mi;
                    zDiff += mz;
                    if (mi > 0 || mz > 0)
                        GameConsole.Print($"GEOM {file.Name}: inside differs at {mi} points, slope height (over 2 z) at {mz} wall points");
                }
                GameConsole.Print($"GEOM {paths.Count} maps: inside differs at {insideDiff} of {points} points; slope height over 2 z at {zDiff} of {zPoints} wall points (worst {zWorst:0.##} z: {worstAt})");
            });
    }

    /// <summary>A map's path, or every base and add-on map for "all".</summary>
    static System.Collections.Generic.List<string> MapPaths(string which)
    {
        var paths = new System.Collections.Generic.List<string>();
        if (which != "all")
        {
            paths.Add(System.IO.File.Exists(which) ? which : Data.Addons.BuildMapPath(which));
            return paths;
        }
        paths.AddRange(System.IO.Directory.GetFiles(Data.BloodData.Folder.PathJoin("maps"), "*.map"));
        foreach (var ad in Data.Addons.All)
            if (System.IO.Directory.Exists(ad.Folder.PathJoin("maps")))
                paths.AddRange(System.IO.Directory.GetFiles(ad.Folder.PathJoin("maps"), "*.map"));
        return paths;
    }
}
