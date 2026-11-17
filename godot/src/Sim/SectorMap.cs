using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Godot;
using Chosen.Shared.Blood.Map;

namespace Chosen.Sim;

/// <summary>
/// The level's Build sectors and walls as the renderers and lookups use them (FromBoard: the board's, refreshed as the
/// game changes it): every sector's outline and floor and ceiling planes, every wall with the sector beyond it, and the
/// sectors sharing ground in plan, in Quake units. The game keeps Build's sector of every sprite and of the camera on
/// it (World.SectorTrack: updatesector, FindSector) and the renderer walks it the way Build draws (StackedRooms).
/// </summary>
public sealed partial class SectorMap
{
    sealed class Sector
    {
        public List<Vector2[]> Rings = new();
        public Vector3 Floor, Ceil;        // z = a x + b y + c, as stored
        public Vector3 Floor0, Ceil0;      // at OFF (a moving plane's travel starts there; else as stored)
        public string FloorMover = "", CeilMover = "";
        public bool FloorSky, CeilSky;     // parallaxed (floorstat / ceilingstat 1)
        public int FloorPic, CeilPic;      // Build's pictures (-1: unknown, a level converted before)
        public int FloorPal, CeilPal;      // Build's palettes (0 also for a level converted before they were written)
        public int SpriteShade = int.MinValue;   // the shade a sprite in it adds (int.MinValue: unknown)
        public Rect2 Box;
        public int Group;                  // its layer of rooms sharing space (Build's impossible space)
        public bool Layered;               // one of the rooms sharing space: seen only in its layer
        public readonly List<int> Overlaps = new();
    }

    sealed class Wall
    {
        public Vector2 P1, P2;
        public int Sector, Next;
        public int NextWall = -1, Cstat, Pic = -1;
        public int Cstat0;                 // as stored (Cstat changes in play: gib walls)
    }

    /// <summary>How many sectors the walk goes through before it gives up (StackedRooms' shader does the same).</summary>
    public const int WalkSteps = 32;

    public int Visibility;
    /// <summary>Sectors overlapping in height where they stack (lower, upper; E1M3's upper corridors reach down into
    /// the rooms under them): Build draws one at a time (StackedRooms).</summary>
    public readonly List<(int lower, int upper)> Stacks = new();
    /// <summary>Sectors sharing ground in plan (a, b with a &lt; b): Build shows whichever its walk reaches.</summary>
    public readonly List<(int a, int b)> Overlaps = new();
    /// <summary>Each sector's neighbours through its two-sided walls.</summary>
    public List<int>[] Neighbours = Array.Empty<List<int>>();
    public int SectorCount => _sectors.Count;
    /// <summary>A level with rooms sharing space (Build's impossible space, the converter's layers).</summary>
    public bool HasLayers;
    public int GroupOf(int s) => s >= 0 && s < _sectors.Count ? _sectors[s].Group : 0;
    /// <summary>One of the rooms sharing space (seen only in its layer)?</summary>
    public bool IsLayered(int s) => s >= 0 && s < _sectors.Count && _sectors[s].Layered;
    /// <summary>The sectors sharing ground with s in plan.</summary>
    public IReadOnlyList<int> OverlapsOf(int s) => s >= 0 && s < _sectors.Count ? _sectors[s].Overlaps : Array.Empty<int>();
    public bool Valid(int s) => s >= 0 && s < _sectors.Count;

    /// <summary>A moving plane's mover ("sectorN floor" / "sectorN ceiling", World.PlaneMover), "" when it stays.</summary>
    public string FloorMoverName(int s) => _sectors[s].FloorMover;
    public string CeilMoverName(int s) => _sectors[s].CeilMover;
    /// <summary>The plane at OFF (z = a x + b y + c): a moving one's mover travels from there.</summary>
    public Vector3 FloorPlane0(int s) => _sectors[s].Floor0;
    public Vector3 CeilPlane0(int s) => _sectors[s].Ceil0;
    /// <summary>The shade a sprite in sector s adds, as stored (int.MinValue: unknown).</summary>
    public int SpriteShade(int s) => _sectors[s].SpriteShade;

    readonly List<int> _layered = new();

    /// <summary>Does (x, y) lie in a room built only in a layer other than this one (the converter's layered
    /// sectors)?</summary>
    public bool InOtherLayer(float x, float y, int layer)
    {
        foreach (int i in LayeredAt(x, y))
            if (_sectors[i].Group != layer && InSector(i, x, y))
                return true;
        return false;
    }

    // the rooms sharing space by the grid cells their boxes cover: asked for every sprite drawn, every frame (a loop
    // over all of them was most of the sprite pass's time in E3M5)
    const float LayerCell = 256f;
    Dictionary<(int, int), List<int>> _layeredGrid;
    static readonly List<int> NoSectors = new();

    List<int> LayeredAt(float x, float y)
    {
        if (_layeredGrid == null)
        {
            _layeredGrid = new Dictionary<(int, int), List<int>>();
            foreach (int i in _layered)
            {
                var b = _sectors[i].Box;
                for (int cx = (int)MathF.Floor(b.Position.X / LayerCell); cx <= (int)MathF.Floor(b.End.X / LayerCell); cx++)
                    for (int cy = (int)MathF.Floor(b.Position.Y / LayerCell); cy <= (int)MathF.Floor(b.End.Y / LayerCell); cy++)
                    {
                        if (!_layeredGrid.TryGetValue((cx, cy), out var l))
                            _layeredGrid[(cx, cy)] = l = new List<int>();
                        l.Add(i);
                    }
            }
        }
        return _layeredGrid.TryGetValue(((int)MathF.Floor(x / LayerCell), (int)MathF.Floor(y / LayerCell)), out var list) ? list : NoSectors;
    }

    /// <summary>Does (x, y) lie in one of the rooms sharing space (either layer)?</summary>
    public bool InLayered(float x, float y)
    {
        foreach (int i in LayeredAt(x, y))
            if (InSector(i, x, y))
                return true;
        return false;
    }

    /// <summary>Is sector i there in layer (-1: any)? The rooms sharing space only in their own.</summary>
    public bool InLayer(int i, int layer) => layer < 0 || i < 0 || !_sectors[i].Layered || _sectors[i].Group == layer;

    /// <summary>The game's whole moving sectors (sliding, turning: their outline here is where Build stores them, not
    /// where they are): the one holding p now in layer (-1: any), else -1 (World.WholeMoverAt). Null: none.</summary>
    public Func<Vector3, int, int> MovingAt;
    /// <summary>Is sector i one of those (its stored outline is no place to look it up)?</summary>
    public Func<int, bool> Moving;

    /// <summary>Does whole moving sector i, its stored outline and column, hold p taken back to where it is stored?</summary>
    public bool HoldsStored(int i, Vector3 on, int layer) =>
        i >= 0 && i < _sectors.Count && InLayer(i, layer) && InSector(i, on.X, on.Y)
        && on.Z >= FloorZ(i, on.X, on.Y) - 1 && on.Z <= CeilZ(i, on.X, on.Y) + 1;
    List<int>[] _sectorWalls = Array.Empty<List<int>>();

    /// <summary>The sector beyond the first two-sided wall of sector sec that the move a-b crosses (Build's clipmove
    /// keeps a sprite's sector that way), -1 if none; at: where it crosses.</summary>
    public int CrossInto(int sec, Vector2 a, Vector2 b, out Vector2 at)
    {
        at = b;
        if (sec < 0 || sec >= _sectorWalls.Length)
            return -1;
        float best = 2;
        int next = -1;
        foreach (int wi in _sectorWalls[sec])
        {
            var w = _walls[wi];
            if (w.Next < 0)
                continue;
            var d = b - a;
            var e = w.P2 - w.P1;
            float den = d.X * e.Y - d.Y * e.X;
            if (Math.Abs(den) < 1e-9f)
                continue;
            var f = w.P1 - a;
            float t = (f.X * e.Y - f.Y * e.X) / den, u = (f.X * d.Y - f.Y * d.X) / den;
            if (t >= 0 && t <= 1 && u >= 0 && u <= 1 && t < best)
            {
                best = t;
                next = w.Next;
                at = a + d * t;
            }
        }
        return next;
    }

    // ---- Build's walls and sectors as stored, by index (the game's Build collision: World.BuildClip)
    public int WallCount => _walls.Count;
    /// <summary>Does the level have what Build's collision needs (wall flags; converted since)?</summary>
    public bool HasClipData { get; private set; }
    public IReadOnlyList<int> SectorWalls(int s) => _sectorWalls[s];
    public Vector2 WallP1(int w) => _walls[w].P1;
    public Vector2 WallP2(int w) => _walls[w].P2;
    public int WallSector(int w) => _walls[w].Sector;
    public int WallNext(int w) => _walls[w].Next;
    public int WallNextWall(int w) => _walls[w].NextWall;
    public int WallCstat(int w) => _walls[w].Cstat;
    /// <summary>A wall's flags changed in play (gib walls broken: World.SetupGibWallState).</summary>
    public void SetWallCstat(int w, int cstat) => _walls[w].Cstat = cstat;
    /// <summary>Every wall's flags as stored again (a level (re)starting: the map is kept across restarts).</summary>
    public void ResetWallCstats()
    {
        foreach (var w in _walls)
            w.Cstat = w.Cstat0;
    }
    public int WallPic(int w) => _walls[w].Pic;
    public bool FloorSky(int s) => _sectors[s].FloorSky;
    /// <summary>A sector's bounds as stored (Quake x, y).</summary>
    public Rect2 SectorBox(int s) => _sectors[s].Box;
    public bool CeilSky(int s) => _sectors[s].CeilSky;
    public int FloorPic(int s) => _sectors[s].FloorPic;
    public int CeilPic(int s) => _sectors[s].CeilPic;
    public int FloorPal(int s) => _sectors[s].FloorPal;
    public int CeilPal(int s) => _sectors[s].CeilPal;

    /// <summary>Sector s's walls: their ends and the sector beyond (-1: one-sided).</summary>
    public IEnumerable<(Vector2 p1, Vector2 p2, int next)> WallsOf(int s)
    {
        foreach (int wi in _sectorWalls[s])
            yield return (_walls[wi].P1, _walls[wi].P2, _walls[wi].Next);
    }

    /// <summary>
    /// Build's walk from sector from (holding a) to b: through the two-sided walls the line a-b crosses, the sector it
    /// reaches b in; -1 when it meets a one-sided wall first (or goes on too long). Where rooms stack this is the one
    /// Build shows there, seen from a (StackedRooms; its shader does the same walk per pixel).
    /// </summary>
    public int Walk(int from, Vector2 a, Vector2 b) => Walk(from, a, b, out _);

    /// <summary>Walk, and the sector it ended in (stopped: also the one a one-sided wall stops it in; -1 when it goes
    /// on too long).</summary>
    public int Walk(int from, Vector2 a, Vector2 b, out int stopped)
    {
        stopped = from;
        var d = b - a;
        float tcur = 0;
        int c = from;
        for (int step = 0; step < WalkSteps && c >= 0 && c < _sectorWalls.Length; step++)
        {
            float best = 2;
            int next = -2;
            foreach (int wi in _sectorWalls[c])
            {
                var w = _walls[wi];
                var e = w.P2 - w.P1;
                float den = d.X * e.Y - d.Y * e.X;
                if (Math.Abs(den) < 1e-9f)
                    continue;
                var f = w.P1 - a;
                float t = (f.X * e.Y - f.Y * e.X) / den, u = (f.X * d.Y - f.Y * d.X) / den;
                if (t > tcur + 1e-5f && t <= 1 && u >= 0 && u <= 1 && t < best)
                {
                    best = t;
                    next = w.Next;
                }
            }
            if (next == -2)
                return c;
            if (next < 0)
                return -1;
            c = next;
            stopped = c;
            tcur = best;
        }
        stopped = -1;     // too far: unknown
        return -1;
    }

    readonly List<Sector> _sectors = new();
    readonly List<Wall> _walls = new();

    /// <summary>The Build data it follows (a level loaded from its .MAP: the walls and planes as they are now), or null
    /// (a converted level's .shade.json, as stored).</summary>
    public Build.Board Board { get; private set; }

    /// <summary>A level's sectors and walls from its Build data, following it as it changes (Refresh).</summary>
    public static SectorMap FromBoard(Build.Board b)
    {
        var d = new SectorMap { Visibility = b.File.Visibility, Board = b };
        for (int i = 0; i < b.SectorCount; i++)
        {
            var bs = b.Sectors[i];
            d._sectors.Add(new Sector
            {
                FloorSky = (bs.FloorStat & 1) != 0, CeilSky = (bs.CeilingStat & 1) != 0,
                FloorPic = bs.FloorPicnum, CeilPic = bs.CeilingPicnum, FloorPal = bs.FloorPal, CeilPal = bs.CeilingPal,
                SpriteShade = SectorSpriteShade(bs),
            });
        }
        for (int w = 0; w < b.WallCount; w++)
        {
            var bw = b.Walls[w];
            d._walls.Add(new Wall
            {
                Sector = bw.Sector, Next = bw.NextSector, NextWall = bw.NextWall, Cstat = bw.Cstat, Cstat0 = bw.Cstat,
                Pic = bw.Picnum,
            });
        }
        d.HasClipData = true;
        d._sectorWalls = new List<int>[d._sectors.Count];
        for (int i = 0; i < d._sectorWalls.Length; i++)
            d._sectorWalls[i] = new List<int>();
        for (int i = 0; i < d._walls.Count; i++)
            if (d._walls[i].Sector >= 0 && d._walls[i].Sector < d._sectors.Count)
                d._sectorWalls[d._walls[i].Sector].Add(i);
        d.Neighbours = new List<int>[d._sectors.Count];
        for (int i = 0; i < d.Neighbours.Length; i++)
            d.Neighbours[i] = new List<int>(b.Neighbours[i]);
        for (int i = 0; i < d._sectors.Count; i++)
        {
            d.RefreshShape(i);
            d.RefreshPlanes(i);
        }
        return d;
    }

    /// <summary>The sectors sharing ground found again from the level as it is now (after trInit has put the movers at
    /// their starting state), those Blood moves whole or by their marked walls left out: a door panel stored open lies
    /// over the next room, which is not Build's impossible space (E1M1's chapel door: the closed panel was hidden by the
    /// stacked rooms' walk).</summary>
    public void FindOverlaps(Func<int, bool> moves)
    {
        Overlaps.Clear();
        Stacks.Clear();
        foreach (var s in _sectors)
            s.Overlaps.Clear();
        _moves = moves;
        FindOverlaps();
        _moves = null;
    }

    Func<int, bool> _moves;

    /// <summary>The shade a sprite in a Build sector adds (viewProcessSprites: its ceiling's under a parallax sky, else
    /// its floor's), each with its tile's own shade.</summary>
    static int SectorSpriteShade(BSector s) => (s.CeilingStat & 1) != 0 && (s.FloorStat & 32768) == 0
        ? s.CeilingShade + Data.Tiles.Shade(s.CeilingPicnum) : s.FloorShade + Data.Tiles.Shade(s.FloorPicnum);

    int[] _shapeSeen = Array.Empty<int>(), _planeSeen = Array.Empty<int>();

    /// <summary>After a game frame: the sectors whose walls moved take their outlines again, those whose planes moved
    /// their planes (a level from its Build data).</summary>
    public void Refresh()
    {
        if (Board == null)
            return;
        if (_shapeSeen.Length != _sectors.Count)
        {
            _shapeSeen = new int[_sectors.Count];
            _planeSeen = new int[_sectors.Count];
        }
        for (int i = 0; i < _sectors.Count; i++)
        {
            if (Board.ShapeVersion[i] != _shapeSeen[i])
            {
                _shapeSeen[i] = Board.ShapeVersion[i];
                RefreshShape(i);
                RefreshPlanes(i);     // a slope turns with its first wall
                _layeredGrid = null;
            }
            else if (Board.PlaneVersion[i] != _planeSeen[i])
                RefreshPlanes(i);
            _planeSeen[i] = Board.PlaneVersion[i];
        }
    }

    void RefreshShape(int i)
    {
        var b = Board;
        var sec = _sectors[i];
        sec.Rings.Clear();
        float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
        foreach (var loop in b.Loops[i])
        {
            var pts = new Vector2[loop.Length];
            for (int k = 0; k < loop.Length; k++)
            {
                var bw = b.Walls[loop[k]];
                var q = new Vector2(bw.X / 16f, -bw.Y / 16f);
                pts[k] = q;
                x0 = Math.Min(x0, q.X); y0 = Math.Min(y0, q.Y); x1 = Math.Max(x1, q.X); y1 = Math.Max(y1, q.Y);
            }
            sec.Rings.Add(pts);
        }
        sec.Box = new Rect2(x0, y0, x1 - x0, y1 - y0);
        foreach (int w in _sectorWalls[i])
        {
            var bw = b.Walls[w];
            var b2 = b.Walls[bw.Point2];
            _walls[w].P1 = new Vector2(bw.X / 16f, -bw.Y / 16f);
            _walls[w].P2 = new Vector2(b2.X / 16f, -b2.Y / 16f);
        }
    }

    void RefreshPlanes(int i)
    {
        _sectors[i].Floor = _sectors[i].Floor0 = QuakePlane(Board.Plane(i, false));
        _sectors[i].Ceil = _sectors[i].Ceil0 = QuakePlane(Board.Plane(i, true));
    }

    /// <summary>A Build plane (z = a x + b y + c, Build units) in Quake units.</summary>
    static Vector3 QuakePlane((double a, double b, double c) p) =>
        new((float)(-p.a * 16 / 256), (float)(p.b * 16 / 256), (float)(-p.c / 256));

    /// <summary>The sectors sharing ground in plan (their outlines overlap: Build's rooms over rooms and impossible
    /// space): Overlaps, and of those the ones whose columns overlap in height Stacks (lower, upper by their floors).</summary>
    void FindOverlaps()
    {
        int n = _sectors.Count;
        const float Cell = 256;
        var grid = new Dictionary<(int, int), List<int>>();
        for (int i = 0; i < n; i++)
        {
            var bx = _sectors[i].Box;
            if (_sectors[i].Rings.Count == 0 || _moves != null && _moves(i))
                continue;
            for (int cx = (int)MathF.Floor(bx.Position.X / Cell); cx <= (int)MathF.Floor(bx.End.X / Cell); cx++)
                for (int cy = (int)MathF.Floor(bx.Position.Y / Cell); cy <= (int)MathF.Floor(bx.End.Y / Cell); cy++)
                {
                    if (!grid.TryGetValue((cx, cy), out var l))
                        grid[(cx, cy)] = l = new List<int>();
                    l.Add(i);
                }
        }
        var seen = new HashSet<(int, int)>();
        foreach (var l in grid.Values)
            for (int x = 0; x < l.Count; x++)
                for (int y = x + 1; y < l.Count; y++)
                {
                    int a = Math.Min(l[x], l[y]), c = Math.Max(l[x], l[y]);
                    if (!seen.Add((a, c)) || !_sectors[a].Box.Intersects(_sectors[c].Box) || !OutlinesOverlap(a, c))
                        continue;
                    Overlaps.Add((a, c));
                    _sectors[a].Overlaps.Add(c);
                    _sectors[c].Overlaps.Add(a);
                }
        if (_moves == null)
            return;
        // a moving sector inside a stack shares the ground too: one over a room that a neighbour of it (across a
        // wall) shares the ground with (E1M3's sliding door 159 between 158 and 160 over 31: its walls showed under
        // 31's ceiling). A door panel stored open over the next room has no such neighbour.
        var add = new List<(int, int)>();
        for (int m = 0; m < n; m++)
        {
            if (_sectors[m].Rings.Count == 0 || !_moves(m))
                continue;
            var others = new HashSet<int>();
            foreach (int nb in Neighbours[m])
                if (!_moves(nb))
                    foreach (int o in _sectors[nb].Overlaps)
                        if (o != m && !_moves(o) && !Neighbours[m].Contains(o))
                            others.Add(o);
            foreach (int o in others)
                if (_sectors[m].Box.Intersects(_sectors[o].Box) && OutlinesOverlap(m, o))
                    add.Add((Math.Min(m, o), Math.Max(m, o)));
        }
        foreach (var (a, c) in add)
        {
            Overlaps.Add((a, c));
            _sectors[a].Overlaps.Add(c);
            _sectors[c].Overlaps.Add(a);
        }
    }

    /// <summary>Do two sectors' areas overlap (more than touching)? A point of one's area inside the other (each
    /// outline's wall middles stepped a little inward, and the corners), or edges crossing.</summary>
    bool OutlinesOverlap(int a, int b)
    {
        foreach (var (p, q) in new[] { (a, b), (b, a) })
            foreach (var ring in _sectors[p].Rings)
                for (int k = 0; k < ring.Length; k++)
                {
                    var p1 = ring[k];
                    var p2 = ring[(k + 1) % ring.Length];
                    var d = p2 - p1;
                    float len = d.Length();
                    if (len < 1e-4f)
                        continue;
                    var mid = (p1 + p2) * 0.5f;
                    var nrm = new Vector2(-d.Y, d.X) / len * 0.25f;
                    foreach (var t in new[] { mid + nrm, mid - nrm })
                        if (InSector(p, t.X, t.Y) && InSector(q, t.X, t.Y))
                            return true;
                }
        foreach (var ra in _sectors[a].Rings)
            for (int i = 0; i < ra.Length; i++)
                foreach (var rb in _sectors[b].Rings)
                    for (int j = 0; j < rb.Length; j++)
                        if (ProperCross(ra[i], ra[(i + 1) % ra.Length], rb[j], rb[(j + 1) % rb.Length]))
                            return true;
        return false;
    }

    static bool ProperCross(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
    {
        float d1 = Cross(c, d, a), d2 = Cross(c, d, b), d3 = Cross(a, b, c), d4 = Cross(a, b, d);
        return (d1 > 1e-4f && d2 < -1e-4f || d1 < -1e-4f && d2 > 1e-4f) && (d3 > 1e-4f && d4 < -1e-4f || d3 < -1e-4f && d4 > 1e-4f);
    }

    static float Cross(Vector2 o, Vector2 a, Vector2 b) => (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);

    public static SectorMap Load(string path)
    {
        if (!File.Exists(path))
            return null;
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var root = doc.RootElement;
        var d = new SectorMap { Visibility = root.GetProperty("visibility").GetInt32() };
        foreach (var s in root.GetProperty("sectors").EnumerateArray())
        {
            var sec = new Sector
            {
                Floor = Plane(s.GetProperty("floor")),
                Ceil = Plane(s.GetProperty("ceil")),
            };
            sec.Floor0 = s.TryGetProperty("floor0", out var f0) ? Plane(f0) : sec.Floor;
            sec.Ceil0 = s.TryGetProperty("ceil0", out var c0) ? Plane(c0) : sec.Ceil;
            sec.FloorMover = s.TryGetProperty("fmv", out var fm) ? fm.GetString() : "";
            sec.CeilMover = s.TryGetProperty("cmv", out var cm) ? cm.GetString() : "";
            sec.Group = s.TryGetProperty("group", out var g) ? g.GetInt32() : 0;
            sec.FloorSky = s.TryGetProperty("floorsky", out var fs) && fs.GetInt32() != 0;
            sec.CeilSky = s.TryGetProperty("ceilsky", out var cs) && cs.GetInt32() != 0;
            sec.FloorPic = s.TryGetProperty("fpic", out var fp) ? fp.GetInt32() : -1;
            sec.CeilPic = s.TryGetProperty("cpic", out var cp) ? cp.GetInt32() : -1;
            sec.FloorPal = s.TryGetProperty("fpal", out var fpl) ? fpl.GetInt32() : 0;
            sec.CeilPal = s.TryGetProperty("cpal", out var cpl) ? cpl.GetInt32() : 0;
            if (s.TryGetProperty("spriteshade", out var ss))
                sec.SpriteShade = ss.GetInt32();
            float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
            foreach (var r in s.GetProperty("rings").EnumerateArray())
            {
                var pts = new List<Vector2>();
                foreach (var p in r.EnumerateArray())
                {
                    var q = new Vector2(p[0].GetSingle(), p[1].GetSingle());
                    pts.Add(q);
                    x0 = Math.Min(x0, q.X); y0 = Math.Min(y0, q.Y); x1 = Math.Max(x1, q.X); y1 = Math.Max(y1, q.Y);
                }
                sec.Rings.Add(pts.ToArray());
            }
            sec.Box = new Rect2(x0, y0, x1 - x0, y1 - y0);
            d._sectors.Add(sec);
        }
        foreach (var w in root.GetProperty("walls").EnumerateArray())
        {
            var wall = new Wall
            {
                P1 = new Vector2(w.GetProperty("p1")[0].GetSingle(), w.GetProperty("p1")[1].GetSingle()),
                P2 = new Vector2(w.GetProperty("p2")[0].GetSingle(), w.GetProperty("p2")[1].GetSingle()),
                Sector = w.GetProperty("sector").GetInt32(),
                Next = w.TryGetProperty("next", out var nx) ? nx.GetInt32() : -1,
                NextWall = w.TryGetProperty("nw", out var nw) ? nw.GetInt32() : -1,
                Cstat = w.TryGetProperty("cstat", out var wc) ? wc.GetInt32() : 0,
                Pic = w.TryGetProperty("pic", out var wp) ? wp.GetInt32() : -1,
            };
            wall.Cstat0 = wall.Cstat;
            d._walls.Add(wall);
        }
        d.HasClipData = d._walls.Count > 0 && root.GetProperty("walls")[0].TryGetProperty("nw", out _);
        d._sectorWalls = new List<int>[d._sectors.Count];
        for (int i = 0; i < d._sectorWalls.Length; i++)
            d._sectorWalls[i] = new List<int>();
        for (int i = 0; i < d._walls.Count; i++)
            if (d._walls[i].Sector >= 0 && d._walls[i].Sector < d._sectors.Count)
                d._sectorWalls[d._walls[i].Sector].Add(i);
        d.Neighbours = new List<int>[d._sectors.Count];
        for (int i = 0; i < d.Neighbours.Length; i++)
            d.Neighbours[i] = new List<int>();
        foreach (var w in d._walls)
            if (w.Sector >= 0 && w.Sector < d._sectors.Count && w.Next >= 0 && w.Next < d._sectors.Count && !d.Neighbours[w.Sector].Contains(w.Next))
                d.Neighbours[w.Sector].Add(w.Next);
        if (root.TryGetProperty("stacks", out var stacks))
            foreach (var st in stacks.EnumerateArray())
                d.Stacks.Add((st[0].GetInt32(), st[1].GetInt32()));
        if (root.TryGetProperty("overlaps", out var overlaps))
            foreach (var o in overlaps.EnumerateArray())
            {
                int a = o[0].GetInt32(), b = o[1].GetInt32();
                if (!d.Valid(a) || !d.Valid(b))
                    continue;
                d.Overlaps.Add((a, b));
                d._sectors[a].Overlaps.Add(b);
                d._sectors[b].Overlaps.Add(a);
            }
        if (root.TryGetProperty("layered", out var layered))
            foreach (var l in layered.EnumerateArray())
                if (l.GetInt32() >= 0 && l.GetInt32() < d._sectors.Count)
                {
                    d._sectors[l.GetInt32()].Layered = true;
                    d._layered.Add(l.GetInt32());
                    d.HasLayers = true;
                }
        return d;
    }

    /// <summary>Is (x, y) inside sector i's outline (Build's inside: even-odd over its loops)?</summary>
    public bool InSector(int i, float x, float y)
    {
        var s = _sectors[i];
        if (x < s.Box.Position.X || y < s.Box.Position.Y || x > s.Box.End.X || y > s.Box.End.Y)
            return false;
        int n = 0;
        foreach (var r in s.Rings)
            if (InRing(x, y, r))
                n++;
        return (n & 1) != 0;
    }

    /// <summary>A place inside sector i, margin from the wall nearest p, if that wall is within reach of p.</summary>
    public bool NearestInside(int i, Vector2 p, float margin, float reach, out Vector2 inside)
    {
        inside = p;
        float best = reach;
        Vector2 at = p, along = default;
        bool found = false;
        foreach (var ring in _sectors[i].Rings)
            for (int k = 0, j = ring.Length - 1; k < ring.Length; j = k++)
            {
                var q = Geo2.ClosestPointToSegment(p, ring[j], ring[k]);
                float d = q.DistanceTo(p);
                if (d < best)
                {
                    (best, at, along, found) = (d, q, ring[k] - ring[j], true);
                }
            }
        if (!found || along.LengthSquared() < 1e-6f)
            return false;
        var n = new Vector2(along.Y, -along.X).Normalized();
        foreach (var candidate in new[] { at + n * margin, at - n * margin })
            if (InSector(i, candidate.X, candidate.Y))
            {
                inside = candidate;
                return true;
            }
        return false;
    }

    /// <summary>The sector whose outline is nearest (x, y) (holding it: distance 0), -1 if there are none.</summary>
    public int NearestSector(float x, float y)
    {
        int best = -1;
        float bestD = float.MaxValue;
        var p = new Vector2(x, y);
        for (int i = 0; i < _sectors.Count; i++)
        {
            if (InSector(i, x, y))
                return i;
            foreach (var ring in _sectors[i].Rings)
                for (int k = 0, j = ring.Length - 1; k < ring.Length; j = k++)
                {
                    float d = Geo2.ClosestPointToSegment(p, ring[j], ring[k]).DistanceTo(p);
                    if (d < bestD)
                        (best, bestD) = (i, d);
                }
        }
        return best;
    }

    /// <summary>Is (x, y) inside sector i's outline or within r of it (a sprite on its wall line)?</summary>
    public bool NearSector(int i, float x, float y, float r)
    {
        var s = _sectors[i];
        if (x < s.Box.Position.X - r || y < s.Box.Position.Y - r || x > s.Box.End.X + r || y > s.Box.End.Y + r)
            return false;
        if (InSector(i, x, y))
            return true;
        var p = new Vector2(x, y);
        foreach (var ring in s.Rings)
            for (int k = 0, j = ring.Length - 1; k < ring.Length; j = k++)
                if (Geo2.ClosestPointToSegment(p, ring[j], ring[k]).DistanceTo(p) <= r)
                    return true;
        return false;
    }

    /// <summary>The planes as stored (the game's heights as they are now: World.SectorFloorZ).</summary>
    public float FloorZ(int i, float x, float y) => Zof(_sectors[i].Floor, x, y);
    public float CeilZ(int i, float x, float y) => Zof(_sectors[i].Ceil, x, y);

    /// <summary>The sector whose outline and floor-to-ceiling column hold p (Quake units): keep, when it still
    /// does (stacked rooms share heights); else the one with the highest floor under p; -1 if none. layer: of rooms
    /// sharing space, the sectors there in it (-1: any).</summary>
    public int SectorAt(Vector3 p, int keep = -1, int layer = -1)
    {
        // a sliding or turning sector holding p where it is now (a train car over a platform, a turning cabin) first;
        // one's outline as stored is no place to keep or find it
        if (MovingAt != null)
        {
            int mv = MovingAt(p, layer);
            if (mv >= 0)
                return mv;
            if (keep >= 0 && Moving(keep))
                keep = -1;
        }
        if (keep >= 0 && keep < _sectors.Count && InSector(keep, p.X, p.Y) && InLayer(keep, layer)
            && p.Z >= FloorZ(keep, p.X, p.Y) - 1 && p.Z <= CeilZ(keep, p.X, p.Y) + 1)
            return keep;
        return StoredAt(p, layer);
    }

    /// <summary>The sector whose outline and column as stored hold p, the highest floor first, not a whole moving one;
    /// -1 if none (where the walk over the walls as stored can start: StackedRooms).</summary>
    public int StoredAt(Vector3 p, int layer = -1)
    {
        int best = -1;
        float bestFloor = float.MinValue;
        foreach (int i in SectorsAt(p.X, p.Y))
        {
            if (!InLayer(i, layer) || Moving != null && Moving(i))
                continue;
            float f = FloorZ(i, p.X, p.Y);
            if (p.Z >= f - 1 && p.Z <= CeilZ(i, p.X, p.Y) + 1 && f > bestFloor)
            {
                best = i;
                bestFloor = f;
            }
        }
        return best;
    }

    /// <summary>The sector just beyond a link's linked plane (at height <paramref name="plane"/>, p a unit past it): of
    /// the sectors holding p, the one whose ceiling (dir &gt; 0, looking down) or floor (looking up) lies nearest the
    /// plane. A room stacked over the far room can hold p too (E3M2's 310 reaches down past the ceiling of 309, link 1's
    /// far room), and the highest floor would pick it.</summary>
    public int SectorBeyond(Vector3 p, int dir, float plane, int layer = -1)
    {
        int best = -1;
        float bestGap = float.MaxValue;
        foreach (int i in SectorsAt(p.X, p.Y))
        {
            if (!InLayer(i, layer) || Moving != null && Moving(i))
                continue;
            float f = FloorZ(i, p.X, p.Y), c = CeilZ(i, p.X, p.Y);
            if (p.Z < f - 1 || p.Z > c + 1)
                continue;
            float gap = Mathf.Abs((dir > 0 ? c : f) - plane);
            if (gap < bestGap)
            {
                best = i;
                bestGap = gap;
            }
        }
        return best;
    }

    static Vector3 Plane(JsonElement e) => new(e[0].GetSingle(), e[1].GetSingle(), e[2].GetSingle());
    public static float Zof(Vector3 plane, float x, float y) => plane.X * x + plane.Y * y + plane.Z;

    static bool InRing(float x, float y, Vector2[] r)
    {
        bool inside = false;
        for (int i = 0, j = r.Length - 1; i < r.Length; j = i++)
            if ((r[i].Y > y) != (r[j].Y > y) && x < (r[j].X - r[i].X) * (y - r[i].Y) / (r[j].Y - r[i].Y) + r[i].X)
                inside = !inside;
        return inside;
    }

    /// <summary>The sectors whose outline holds (x, y), as stored.</summary>
    public IEnumerable<int> SectorsAt(float x, float y)
    {
        for (int i = 0; i < _sectors.Count; i++)
        {
            var s = _sectors[i];
            if (x < s.Box.Position.X || y < s.Box.Position.Y || x > s.Box.End.X || y > s.Box.End.Y)
                continue;
            int n = 0;
            foreach (var r in s.Rings)
                if (InRing(x, y, r))
                    n++;
            if ((n & 1) != 0)
                yield return i;
        }
    }
}
