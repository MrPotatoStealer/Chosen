// Chosen core: original code (the document over Chosen.Blood's map records).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Chosen.Shared.Blood.Map;
using Chosen.Shared.Engine.Geometry;
using Godot;

namespace Chosen.Forge.Document;

public enum MapFormat { Map, MapX }

/// <summary>A point of the map as the editor shows it: the walls starting at one place that move together (a chain
/// of walls joined through their NextWall, as Build's own point dragging finds them). Two rooms stacked with a corner
/// at the same place are two points.</summary>
public sealed class MapPoint
{
    public int X, Y;
    public int[] Walls;
}

/// <summary>A line: a white wall (Back -1), or a red wall and its NextWall as one line with two sides (Front the
/// lower index).</summary>
public readonly record struct MapLine(int Front, int Back);

/// <summary>
/// The map being edited: the Blood map (records as stored), the .MAPX extras, where it came from, and the lookups
/// the view and the modes use (points, lines, sector labels, bounds), rebuilt when it changes.
/// </summary>
public sealed partial class MapDocument : IBuildData
{
    public BloodMapFile Map { get; private set; }
    public MapXExtras Extras { get; private set; }
    /// <summary>The file it was opened from or last saved to ("" for a new map).</summary>
    public string Path { get; private set; } = "";
    public MapFormat Format { get; private set; }
    public bool Dirty { get; set; }
    /// <summary>The file's .patch was applied on opening (its heights are in the data now).</summary>
    public bool PatchApplied { get; private set; }

    public List<BSector> Sectors => Map.Sectors;
    public List<BWall> Walls => Map.Walls;
    public List<BSprite> Sprites => Map.Sprites;

    /// <summary>Raised after the map changed (the lookups are rebuilt first).</summary>
    public event Action Changed;

    // IBuildData
    public int SectorCount => Map.Sectors.Count;
    public int WallCount => Map.Walls.Count;
    public IGeomSector Sector(int i) => Map.Sectors[i];
    public IGeomWall Wall(int i) => Map.Walls[i];

    // lookups
    public int[] PrevWall { get; private set; } = Array.Empty<int>();      // the wall whose Point2 is this one
    public int[] PointOf { get; private set; } = Array.Empty<int>();       // wall -> its start point
    public List<MapPoint> Points { get; } = new();
    public List<MapLine> Lines { get; } = new();
    public int[] LineOf { get; private set; } = Array.Empty<int>();        // wall -> its line
    public Vector2[] Labels { get; private set; } = Array.Empty<Vector2>(); // sector -> where its label goes
    public double[] Areas { get; private set; } = Array.Empty<double>();   // sector -> its area (outer minus inner)
    public Rect2 Bounds { get; private set; }

    MapDocument(BloodMapFile m, MapXExtras x)
    {
        Map = m;
        Extras = x ?? new MapXExtras();
        Rebuild();
    }

    /// <summary>An empty map (version 7, nothing in it), its parallax sky as every one of Blood's maps has it
    /// (BloodSky), its visibility theirs (800).</summary>
    public static MapDocument New()
    {
        // (visibility 800, as Blood's maps have (752..800): the darkening with distance a shade below 0 holds back; 0
        // has none, every face its own shade at any distance)
        var m = new BloodMapFile { Version = 0x700, HeaderEncrypted = true, StartSect = -1, Visibility = 800, MapRev = 0 };
        BloodSky(m);
        var d = new MapDocument(m, null) { Format = MapFormat.MapX };
        d.Extras.SetInfo("generator", "Chosen Forge");
        d.Extras.SetInfo("created", Now());
        return d;
    }

    /// <summary>Blood's parallax sky, as all its maps have it: 16 pieces round the view (sky bits 4), piece i the sky
    /// ceiling's picture plus i (Blood's skies are 16 tiles in a row: 3678.., 2500.., 3491..), parallax type 2.</summary>
    public static void BloodSky(BloodMapFile m)
    {
        m.PSkyBits = 4;
        m.SkyOfs = Enumerable.Range(0, 16).Select(i => (short)i).ToArray();
        m.ParallaxType = 2;
    }

    /// <summary>Opens a .MAP or .MAPX. A .MAP with a .patch beside it (Refreshed Supply's fixes): applyPatch says
    /// whether to take them into the map (the heights into its data, the sprite keys into the extras).</summary>
    public static MapDocument Open(string path, Func<string, bool> applyPatch = null)
    {
        var data = File.ReadAllBytes(path);
        MapDocument d;
        if (MapXFile.IsMapX(data))
        {
            var (m, x) = MapXFile.Read(data);
            d = new MapDocument(m, x) { Format = MapFormat.MapX };
        }
        else
        {
            var m = BloodMapFile.Parse(data);
            d = new MapDocument(m, null) { Format = MapFormat.Map };
            string patch = System.IO.Path.ChangeExtension(path, ".patch");
            if (File.Exists(patch) && applyPatch != null && applyPatch(patch))
            {
                var (_, x) = MapConvert.ToMapX(m, File.ReadAllLines(patch), "Chosen Forge");
                d.Extras = x;
                d.PatchApplied = true;
                d.Dirty = true;
                d.Rebuild();
            }
        }
        d.Map.Name = System.IO.Path.GetFileNameWithoutExtension(path).ToUpperInvariant();
        d.Path = path;
        // (point lights saved before they kept their room: given the one the layer edited has there, for good)
        foreach (var l in d.Lights)
            if (!l.Attached && l.Sector < 0)
                d.SettleLight(l);
        return d;
    }

    static string Now() => DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>The file's bytes in a format; bumpRevision: MapRev + 1 (a save, as Build's editors do). For .MAP also
    /// what it can't hold (dropped).</summary>
    public byte[] SaveBytes(MapFormat format, bool bumpRevision, out List<string> dropped)
    {
        dropped = new List<string>();
        NoXIsMinusOne();
        if (format == MapFormat.Map)
        {
            var (bytes, d) = MapConvert.ToMap(Map, Extras, Limits.NBlood, bumpRevision);
            dropped = d;
            return bytes;
        }
        if (bumpRevision)
            Map.MapRev++;
        if (Extras.GetInfo("generator") == null)
            Extras.SetInfo("generator", "Chosen Forge");
        Extras.SetInfo("modified", Now());
        // which Forge saved it (the format's own version is MapXFile.FormatVersion): a problem with a file traces back
        Extras.SetInfo("saved_by", "Chosen Forge " + Chosen.Shared.Engine.ChosenVersion.Full);
        return MapXFile.Write(Map, Extras);
    }

    /// <summary>
    /// Records without an x-structure stored as Blood stores them: Extra -1. (Records Forge made had 0: Blood's own
    /// reader and NBlood take only Extra > 0 for an x-structure, but Refreshed Supply looks one up for 0 and crashes on
    /// the null it finds.)
    /// </summary>
    void NoXIsMinusOne()
    {
        foreach (var s in Sectors)
            if (s.Xs == null && s.Extra == 0)
                s.Extra = -1;
        foreach (var w in Walls)
            if (w.Xw == null && w.Extra == 0)
                w.Extra = -1;
        foreach (var sp in Sprites)
            if (sp.Xsp == null && sp.Extra == 0)
                sp.Extra = -1;
    }

    /// <summary>Writes the map to path (through a temporary file: a game watching it never reads half of it); the
    /// document then belongs to that file and format.</summary>
    public List<string> Save(string path, MapFormat format)
    {
        // the map's art named from its new folder (saved elsewhere: the same files)
        RebaseArt(Folder, System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path)) ?? "");
        var bytes = SaveBytes(format, true, out var dropped);
        string tmp = path + ".tmp";
        File.WriteAllBytes(tmp, bytes);
        File.Move(tmp, path, true);
        Path = path;
        Format = format;
        Map.Name = System.IO.Path.GetFileNameWithoutExtension(path).ToUpperInvariant();
        Dirty = false;
        return dropped;
    }

    /// <summary>The folder the map's file is in ("" for a new map): its art sets' paths start there.</summary>
    public string Folder => Path == "" ? "" : System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Path)) ?? "";

    /// <summary>The art sets' paths, relative to from (absolute when ""), made relative to to.</summary>
    public void RebaseArt(string from, string to)
    {
        if (from == to)
            return;
        for (int i = 0; i < Extras.Art.Count; i++)
        {
            var a = Extras.Art[i];
            Extras.Art[i] = a with { Path = Shared.Blood.Files.MapArt.Relative(to, Shared.Blood.Files.MapArt.Resolve(from, a.Path)) };
        }
    }

    /// <summary>The .MAPX bytes of a copy played elsewhere (Test Map's in the temporary folder): the art sets' paths
    /// absolute.</summary>
    public byte[] TestBytes()
    {
        var keep = Extras.Art.ToList();
        RebaseArt(Folder, "");
        try
        {
            return SaveBytes(MapFormat.MapX, false, out _);
        }
        finally
        {
            Extras.Art.Clear();
            Extras.Art.AddRange(keep);
        }
    }

    /// <summary>The map changed: lookups rebuilt, Changed raised.</summary>
    public void NotifyChanged()
    {
        Dirty = true;
        Rebuild();
        Changed?.Invoke();
    }

    void Rebuild()
    {
        int nw = Walls.Count;
        foreach (var w in Walls)
            w.Sector = -1;
        for (int s = 0; s < Sectors.Count; s++)
            for (int w = Sectors[s].WallPtr; w >= 0 && w < Sectors[s].WallPtr + Sectors[s].WallNum && w < nw; w++)
                Walls[w].Sector = s;
        PrevWall = new int[nw];
        Array.Fill(PrevWall, -1);
        for (int w = 0; w < nw; w++)
        {
            int p2 = Walls[w].Point2;
            if (p2 >= 0 && p2 < nw)
                PrevWall[p2] = w;
        }
        BuildPoints();
        BuildLines();
        BuildLabels();
    }

    // points: the walls at one place joined through NextWall (the wall before one ends where it starts: that wall's
    // NextWall starts there too; a wall's NextWall ends where it starts: the NextWall's Point2 starts there)
    void BuildPoints()
    {
        int nw = Walls.Count;
        Points.Clear();
        PointOf = new int[nw];
        Array.Fill(PointOf, -1);
        var stack = new Stack<int>();
        var chain = new List<int>();
        for (int w = 0; w < nw; w++)
        {
            if (PointOf[w] >= 0)
                continue;
            int x = Walls[w].X, y = Walls[w].Y, id = Points.Count;
            chain.Clear();
            stack.Push(w);
            PointOf[w] = id;
            while (stack.Count > 0)
            {
                int c = stack.Pop();
                chain.Add(c);
                int prev = PrevWall[c];
                if (prev >= 0)
                    Visit(Walls[prev].NextWall);
                int next = Walls[c].NextWall;
                if (next >= 0 && next < nw)
                    Visit(Walls[next].Point2);
            }
            chain.Sort();
            Points.Add(new MapPoint { X = x, Y = y, Walls = chain.ToArray() });

            void Visit(int o)
            {
                if (o < 0 || o >= nw || PointOf[o] >= 0 || Walls[o].X != x || Walls[o].Y != y)
                    return;
                PointOf[o] = id;
                stack.Push(o);
            }
        }
    }

    void BuildLines()
    {
        int nw = Walls.Count;
        Lines.Clear();
        LineOf = new int[nw];
        Array.Fill(LineOf, -1);
        for (int w = 0; w < nw; w++)
        {
            if (LineOf[w] >= 0)
                continue;
            int o = Walls[w].NextWall;
            bool pair = o >= 0 && o < nw && o != w && Walls[o].NextWall == w && LineOf[o] < 0;
            LineOf[w] = Lines.Count;
            if (pair)
                LineOf[o] = Lines.Count;
            Lines.Add(new MapLine(w, pair ? o : -1));
        }
    }

    // a label inside each sector: the middle of the widest span of its loops across the row through its bounds' centre
    void BuildLabels()
    {
        int ns = Sectors.Count;
        Labels = new Vector2[ns];
        Areas = new double[ns];
        float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
        var cross = new List<double>();
        for (int s = 0; s < ns; s++)
        {
            var sec = Sectors[s];
            if (sec.WallNum <= 0 || sec.WallPtr < 0 || sec.WallPtr + sec.WallNum > Walls.Count)
                continue;
            var (minX, minY, maxX, maxY) = BuildGeom.SectorBounds(this, s);
            x0 = Math.Min(x0, minX); y0 = Math.Min(y0, minY); x1 = Math.Max(x1, maxX); y1 = Math.Max(y1, maxY);
            double area = 0;
            foreach (var loop in BuildGeom.Loops(this, s))
                area += BuildGeom.LoopArea(this, loop);
            Areas[s] = Math.Abs(area);
            double row = (minY + maxY) * 0.5 + 0.5;
            cross.Clear();
            for (int w = sec.WallPtr; w < sec.WallPtr + sec.WallNum; w++)
            {
                var (ax, ay, bx, by) = BuildGeom.WallEnds(this, w);
                if ((ay > row) != (by > row))
                    cross.Add(ax + (row - ay) * (bx - ax) / (by - ay));
            }
            cross.Sort();
            double best = -1, at = 0;
            for (int i = 0; i + 1 < cross.Count; i += 2)
                if (cross[i + 1] - cross[i] > best)
                {
                    best = cross[i + 1] - cross[i];
                    at = (cross[i] + cross[i + 1]) * 0.5;
                }
            if (best >= 0)
                Labels[s] = new Vector2((float)at, (float)row);
            else
            {
                double sx = 0, sy = 0;
                for (int w = sec.WallPtr; w < sec.WallPtr + sec.WallNum; w++)
                {
                    sx += Walls[w].X;
                    sy += Walls[w].Y;
                }
                Labels[s] = new Vector2((float)(sx / sec.WallNum), (float)(sy / sec.WallNum));
            }
        }
        // the walls' bounds (maps keep sprites far outside the level: spawn markers, unused things); sprites only
        // when there are no walls
        if (x0 > x1)
            foreach (var sp in Sprites)
            {
                x0 = Math.Min(x0, sp.X); y0 = Math.Min(y0, sp.Y); x1 = Math.Max(x1, sp.X); y1 = Math.Max(y1, sp.Y);
            }
        // (an empty map: 8192 units round the middle, room for a few rooms of a usual size)
        Bounds = x0 <= x1 ? new Rect2(x0, y0, x1 - x0, y1 - y0) : new Rect2(-4096, -4096, 8192, 8192);
    }

    // ---------------------------------------------------------------- queries (map units)

    /// <summary>The point nearest to (x, y) within range, or -1.</summary>
    public int PointNear(double x, double y, double range, Func<int, bool> ok = null)
    {
        int best = -1;
        double bd = range * range;
        for (int i = 0; i < Points.Count; i++)
        {
            if (ok != null && !ok(i))
                continue;
            double dx = Points[i].X - x, dy = Points[i].Y - y, d = dx * dx + dy * dy;
            if (d <= bd)
            {
                bd = d;
                best = i;
            }
        }
        return best;
    }

    /// <summary>The line nearest to (x, y) within range, or -1.</summary>
    public int LineNear(double x, double y, double range, Func<MapLine, bool> ok = null)
    {
        int best = -1;
        double bd = range;
        for (int i = 0; i < Lines.Count; i++)
        {
            if (ok != null && !ok(Lines[i]))
                continue;
            var (ax, ay, bx, by) = BuildGeom.WallEnds(this, Lines[i].Front);
            double d = SegmentDistance(x, y, ax, ay, bx, by);
            if (d <= bd)
            {
                bd = d;
                best = i;
            }
        }
        return best;
    }

    public static double SegmentDistance(double px, double py, double ax, double ay, double bx, double by)
    {
        double dx = bx - ax, dy = by - ay, len = dx * dx + dy * dy;
        double t = len > 0 ? Math.Clamp(((px - ax) * dx + (py - ay) * dy) / len, 0, 1) : 0;
        double cx = ax + t * dx - px, cy = ay + t * dy - py;
        return Math.Sqrt(cx * cx + cy * cy);
    }

    /// <summary>The sectors holding (x, y), smallest first (rooms stacked or overlapping share places).</summary>
    public List<int> SectorsAt(double x, double y)
    {
        var l = new List<int>();
        for (int s = 0; s < Sectors.Count; s++)
        {
            var sec = Sectors[s];
            if (sec.WallNum <= 0 || sec.WallPtr < 0 || sec.WallPtr + sec.WallNum > Walls.Count)
                continue;
            if (BuildGeom.Inside(this, s, x, y))
                l.Add(s);
        }
        l.Sort((a, b) => Areas[a].CompareTo(Areas[b]));
        return l;
    }

    /// <summary>The sprite nearest to (x, y) within range (or whose marker holds it), or -1.</summary>
    public int SpriteNear(double x, double y, double range, Func<int, bool> skip = null)
    {
        int best = -1;
        double bd = range * range;
        for (int i = 0; i < Sprites.Count; i++)
        {
            if (skip != null && skip(i))
                continue;
            double dx = Sprites[i].X - x, dy = Sprites[i].Y - y, d = dx * dx + dy * dy;
            if (d <= bd)
            {
                bd = d;
                best = i;
            }
        }
        return best;
    }

    /// <summary>The sectors on each side of a line: the front wall's and the back wall's (-1 for none).</summary>
    public (int front, int back) LineSectors(int line)
    {
        var l = Lines[line];
        return (Walls[l.Front].Sector, l.Back >= 0 ? Walls[l.Back].Sector : -1);
    }

    public int Count(Func<BSector, bool> f) => Sectors.Count(f);
}
