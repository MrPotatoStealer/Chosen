// Chosen core: original code (a map's sectors, walls and sprites as faces for Forge's 3D preview, written from Build's
// map rules: planes over a sector's loops, walls between its planes and its neighbour's, pictures by repeat and panning).
using System;
using System.Collections.Generic;
using System.Linq;
using Chosen.Forge.Data;
using Chosen.Forge.Document;
using Chosen.Shared.Blood.Map;
using Chosen.Shared.Engine.Geometry;
using Godot;

namespace Chosen.Forge.View;

/// <summary>
/// The faces of the 3D preview (UDB's visual mode): each sector's floor and ceiling over its loops (filled band by
/// band between its points' y, even-odd: inner loops are holes; sloped planes by their height at each corner), each
/// wall's faces seen from its sector (a white wall floor to ceiling; a red one's lower and upper parts where the next
/// sector's floor or ceiling steps; a masked or one-way picture across the opening), pictures mapped by repeat and
/// panning and pegged to a plane, shaded by the shade. Positions in game units (16 Build units across, 256 up and
/// down; x east, y up, z south). A parallax ceiling is left open (the background is the sky).
/// </summary>
public sealed class PreviewMesher
{
    public enum Kind { Opaque, Masked, Translucent }

    public sealed class Part
    {
        public readonly List<Vector3> V = new();
        public readonly List<Vector2> Uv = new();
        public readonly List<Color> C = new();
    }

    /// <summary>The faces by picture, kind, seen from one side only (masked), and whether a wall (its picture repeats
    /// as a wall's: TileImages.Wall).</summary>
    public readonly Dictionary<(int tile, Kind kind, bool oneSided, bool wall, int pal, int sector), Part> Parts = new();
    readonly MapDocument _d;

    public PreviewMesher(MapDocument d) => _d = d;

    /// <summary>Game frames Blood's panning surfaces have moved for (Blood.PanMotion); PanOn: the map shown On (a
    /// sector or wall that pans when on pans).</summary>
    public double PanFrames;
    public bool PanOn;

    // (the map as given: the stored one, or the shown copy with the moving sectors posed: Blood.SectorMotion)
    double CeilZ(int s) => _d.Sectors[s].CeilingZ;
    double FloorZ(int s) => _d.Sectors[s].FloorZ;

    void Zs(int s, double x, double y, out double c, out double f) => BuildGeom.GetZsOfSlope(_d, s, x, y, out c, out f);

    double PlaneZ(int s, bool ceiling, double x, double y) => BuildGeom.PlaneZ(_d, s, ceiling, x, y);

    /// <summary>Blood's tile for a plane showing the linked room (and for mirrors).</summary>
    public const int LinkTile = 504;

    /// <summary>A Build position (x, y, z down) in the preview's space.</summary>
    public static Vector3 P(double x, double y, double z) => new((float)(x / 16), (float)(-z / 256), (float)(y / 16));

    /// <summary>Build's shade as a brightness (0 as drawn, 63 near black; negative brighter).</summary>
    /// <summary>A Build shade as a brightness in linear light, as the game draws it (BuildShading.build_shade):
    /// (1 - shade / 64) to the power 2.2 (Blood's shade table darkens the palette's colours, not the light), black from
    /// 64 on, no brighter than 0 below it.</summary>
    public static Color Shade(int shade)
    {
        float c = Mathf.Pow(Mathf.Clamp(1f - shade / 64f, 0f, 1f), 2.2f);
        return new Color(c, c, c);
    }

    /// <summary>A face's shade as its vertex colour carries it to the 3D preview's shader (Preview3DPortals.FaceShader),
    /// which adds Build's darkening with distance before making it a brightness (so a shade below 0, which up close is
    /// no brighter than 0, keeps a face bright further off): r the shade (-128..127 as 0..255), g its sector's own
    /// visibility (0: the map's alone).</summary>
    public static Color ShadeCode(int shade, int sectorVisibility) =>
        new(((Math.Clamp(shade, -128, 127) + 128) / 255f), (sectorVisibility & 255) / 255f, 0f, 1f);

    static int TileShade(int tile) => tile >= 0 && tile < ForgeData.TileShade.Length ? ForgeData.TileShade[tile] : 0;

    /// <summary>A sprite's shade as Blood draws it (view.cpp): its own, plus its sector's floor's (a sky's ceiling's
    /// under a parallax ceiling) with that picture's tile shade, plus its own picture's tile shade (SHADE.DAT).</summary>
    public static int SpriteShade(MapDocument d, Chosen.Shared.Blood.Map.BSprite sp)
    {
        int shade = sp.Shade;
        if (sp.SectNum >= 0 && sp.SectNum < d.Sectors.Count)
        {
            var sec = d.Sectors[sp.SectNum];
            shade += (sec.CeilingStat & 1) != 0 && (sec.FloorStat & 32768) == 0
                ? TileShade(sec.CeilingPicnum) + sec.CeilingShade
                : TileShade(sec.FloorPicnum) + sec.FloorShade;
        }
        shade += TileShade(sp.Picnum);
        return Math.Clamp(shade, -128, 127);
    }

    /// <summary>A tile's size (64 x 64 without a picture).</summary>
    public static Vector2I TileSize(int tile) => Size(tile);

    static Vector2I Size(int tile)
    {
        var s = TileImages.Size(tile);
        return s.X > 0 && s.Y > 0 ? s : new Vector2I(64, 64);
    }

    /// <summary>A sector's faces of one picture and kind (by sector: the preview draws each sector only where it is
    /// seen through the openings, Preview3D.Portals).</summary>
    Part Get(int tile, Kind k, int sector, bool oneSided = false, bool wall = false, int pal = 0)
    {
        if (!Parts.TryGetValue((tile, k, oneSided, wall, pal, sector), out var p))
            Parts[(tile, k, oneSided, wall, pal, sector)] = p = new Part();
        return p;
    }

    public void Build() => Build(Enumerable.Range(0, _d.Sectors.Count));

    /// <summary>Only these sectors' faces (Play: the moving ones and their neighbours, made again each frame).</summary>
    public void Build(IEnumerable<int> sectors)
    {
        foreach (int s in sectors)
        {
            if (s < 0 || s >= _d.Sectors.Count)
                continue;
            var sec = _d.Sectors[s];
            // (a hidden layer's sectors aren't there)
            if (sec.WallNum < 3 || sec.WallPtr < 0 || sec.WallPtr + sec.WallNum > _d.Walls.Count || !_d.SectorShown(s))
                continue;
            Plane(s, false);
            Plane(s, true);
            for (int w = sec.WallPtr; w < sec.WallPtr + sec.WallNum; w++)
                Wall(s, w);
        }
    }

    // ---------------------------------------------------------------- floors and ceilings

    void Plane(int s, bool ceiling)
    {
        var sec = _d.Sectors[s];
        int stat = ceiling ? sec.CeilingStat : sec.FloorStat;
        if (ceiling && (stat & 1) != 0)
            return;
        int tile = ceiling ? sec.CeilingPicnum : sec.FloorPicnum;
        // Blood's linked plane (a room over a room: tile 504 stands for the view of the linked room, never drawn as
        // a picture): left open, the room above or below shows through
        if (tile == LinkTile)
            return;
        var part = Get(tile, Kind.Opaque, s, pal: ceiling ? sec.CeilingPal : sec.FloorPal);
        var color = ShadeCode(ceiling ? sec.CeilingShade : sec.FloorShade, sec.Visibility);
        var (pu, pv) = Blood.PanMotion.Plane(_d, s, ceiling, PanFrames, PanOn);
        var flat = new FlatMap(_d, s, ceiling, Size(tile), pu, pv);
        foreach (var (x, y) in Fill(_d, s))
        {
            part.V.Add(P(x, y, PlaneZ(s, ceiling, x, y)));
            part.Uv.Add(flat.Uv(x, y));
            part.C.Add(color);
        }
    }

    /// <summary>A sector's area as triangles (three points each, in order): filled band by band between its points'
    /// y, even-odd (inner loops are holes). The 3D preview's planes, the 2D view's textured floors.</summary>
    public static List<(double x, double y)> Fill(MapDocument d, int s)
    {
        var tris = new List<(double x, double y)>();
        var edges = new List<(double x1, double y1, double x2, double y2)>();
        var ys = new SortedSet<double>();
        foreach (var loop in BuildGeom.Loops(d, s))
            foreach (int w in loop)
            {
                var (x1, y1, x2, y2) = BuildGeom.WallEnds(d, w);
                edges.Add((x1, y1, x2, y2));
                ys.Add(y1);
                ys.Add(y2);
            }
        var yl = ys.ToList();
        var cross = new List<(double xm, double xa, double xb)>();
        for (int i = 0; i + 1 < yl.Count; i++)
        {
            double ya = yl[i], yb = yl[i + 1], ym = (ya + yb) * 0.5;
            cross.Clear();
            foreach (var e in edges)
            {
                if ((e.y1 <= ym) == (e.y2 <= ym))
                    continue;
                double X(double y) => e.x1 + (e.x2 - e.x1) * (y - e.y1) / (e.y2 - e.y1);
                cross.Add((X(ym), X(ya), X(yb)));
            }
            cross.Sort((a, b) => a.xm.CompareTo(b.xm));
            for (int k = 0; k + 1 < cross.Count; k += 2)
            {
                var l = cross[k];
                var r = cross[k + 1];
                var q = new[] { (l.xa, ya), (r.xa, ya), (r.xb, yb), (l.xb, yb) };
                foreach (int c in new[] { 0, 1, 2, 0, 2, 3 })
                    tris.Add(q[c]);
            }
        }
        return tris;
    }

    /// <summary>A floor's or ceiling's picture: world-aligned (or to the sector's first wall), a texel 16 Build units
    /// (8 smoothed double), swapped, flipped, panned.</summary>
    public readonly struct FlatMap
    {
        readonly int _stat;
        readonly double _ox, _oy, _ux, _uy, _texel, _panU, _panV;
        readonly Vector2I _size;

        /// <summary>du, dv: panning added (Build's units, 256 a picture: Blood's panning floors as they move).</summary>
        public FlatMap(MapDocument d, int s, bool ceiling, Vector2I size, double du = 0, double dv = 0)
        {
            var sec = d.Sectors[s];
            _stat = ceiling ? sec.CeilingStat : sec.FloorStat;
            _size = size;
            _texel = (_stat & 8) != 0 ? 8 : 16;
            _panU = ((ceiling ? sec.CeilingXPanning : sec.FloorXPanning) + du) / 256.0;
            _panV = ((ceiling ? sec.CeilingYPanning : sec.FloorYPanning) + dv) / 256.0;
            _ox = _oy = 0;
            _ux = 1;
            _uy = 0;
            if ((_stat & 64) != 0)
            {
                // relative to the first wall: u along it from its start
                var (x1, y1, x2, y2) = BuildGeom.WallEnds(d, sec.WallPtr);
                double len = Math.Sqrt((double)(x2 - x1) * (x2 - x1) + (double)(y2 - y1) * (y2 - y1));
                if (len > 0)
                {
                    _ox = x1;
                    _oy = y1;
                    _ux = (x2 - x1) / len;
                    _uy = (y2 - y1) / len;
                }
            }
        }

        public Vector2 Uv(double x, double y)
        {
            double dx = x - _ox, dy = y - _oy;
            double u = dx * _ux + dy * _uy, v = -dx * _uy + dy * _ux;
            if ((_stat & 4) != 0)
                (u, v) = (v, u);
            if ((_stat & 16) != 0)
                u = -u;
            if ((_stat & 32) != 0)
                v = -v;
            return new Vector2((float)(u / (_texel * _size.X) + _panU), (float)(v / (_texel * _size.Y) + _panV));
        }
    }

    // ---------------------------------------------------------------- walls

    void Wall(int s, int w)
    {
        var wall = _d.Walls[w];
        var sec = _d.Sectors[s];
        var (x1, y1, x2, y2) = BuildGeom.WallEnds(_d, w);
        if (x1 == x2 && y1 == y2)
            return;
        Zs(s, x1, y1, out double c1, out double f1);
        Zs(s, x2, y2, out double c2, out double f2);
        int ns = wall.NextSector;
        if (ns < 0 || ns >= _d.Sectors.Count)
        {
            // floor to ceiling, pegged to the ceiling (to the floor: align bottom)
            double peg = (wall.Cstat & 4) != 0 ? FloorZ(s) : CeilZ(s);
            Face(w, wall.Picnum, Kind.Opaque, x1, y1, x2, y2, c1, c2, f1, f2, peg);
            return;
        }
        var nsec = _d.Sectors[ns];
        Zs(ns, x1, y1, out double nc1, out double nf1);
        Zs(ns, x2, y2, out double nc2, out double nf2);
        // the lower part: where the next floor is above this one (swap bottoms: the wall behind's picture, shade,
        // panning, alignment and y flip; this wall's repeats and x flip)
        if (nf1 < f1 || nf2 < f2)
        {
            var look = (wall.Cstat & 2) != 0 && wall.NextWall >= 0 && wall.NextWall < _d.Walls.Count ? _d.Walls[wall.NextWall] : wall;
            double peg = (look.Cstat & 4) != 0 ? CeilZ(s) : FloorZ(ns);
            // (from the next floor down to this one: Face cuts it where the two cross, the next floor above at one end only)
            Face(w, look.Picnum, Kind.Opaque, x1, y1, x2, y2, nf1, nf2, f1, f2, peg, look);
        }
        // the upper part: where the next ceiling is below this one (not between two skies)
        bool sky = (sec.CeilingStat & 1) != 0 && (nsec.CeilingStat & 1) != 0;
        if (!sky && (nc1 > c1 || nc2 > c2))
        {
            double peg = (wall.Cstat & 4) != 0 ? CeilZ(s) : CeilZ(ns);
            Face(w, wall.Picnum, Kind.Opaque, x1, y1, x2, y2, c1, c2, nc1, nc2, peg);
        }
        // a masked (or one-way) picture across the opening
        if ((wall.Cstat & (16 | 32)) != 0)
        {
            // both show the overpicnum; a masked one pegged to the lower ceiling (cstat 4: the higher floor), a one-way
            // one to the next sector's ceiling (cstat 4: its own)
            int pic = wall.OverPicnum;
            bool masked = (wall.Cstat & 16) != 0;
            var kind = masked && (wall.Cstat & (128 | 512)) != 0 ? Kind.Translucent : Kind.Masked;
            double t1 = Math.Max(c1, nc1), t2 = Math.Max(c2, nc2), b1 = Math.Min(f1, nf1), b2 = Math.Min(f2, nf2);
            double peg = masked
                ? (wall.Cstat & 4) != 0 ? Math.Min(FloorZ(s), FloorZ(ns)) : Math.Max(CeilZ(s), CeilZ(ns))
                : (wall.Cstat & 4) != 0 ? CeilZ(s) : CeilZ(ns);
            // (seen from this side only: the wall behind draws its own picture for the other side, as Build does)
            Face(w, pic, kind, x1, y1, x2, y2, t1, t2, b1, b2, peg, null, oneSided: true);
        }
    }

    /// <summary>A wall face from (x1, y1) to (x2, y2), top t and bottom b at each end (Build z), its picture xrepeat * 8
    /// texels along it, a texel 2048 / yrepeat z high from peg (the game: yrepeat / 8 texels a game unit), panned and flipped by the wall's bits.</summary>
    /// <summary>look: the wall whose picture, shade, panning and y flip it shows (swapped bottoms: the wall behind);
    /// oneSided: only its front shows (a masked picture: each side of a red wall draws its own).</summary>
    void Face(int w, int tile, Kind kind, double x1, double y1, double x2, double y2, double t1, double t2, double b1, double b2, double peg,
        BWall look = null, bool oneSided = false)
    {
        if (t1 >= b1 && t2 >= b2)
            return;
        var wall = _d.Walls[w];
        look ??= wall;
        var size = Size(tile);
        // (a height that isn't a power of two repeats at the next one: TileImages.Wall)
        size = new Vector2I(size.X, TileImages.WallHeight(size.Y));
        // Blood's panning walls as they move (the wall whose panning it shows)
        var (pu, pv) = Blood.PanMotion.Wall(_d, look == wall ? w : wall.NextWall, PanFrames, PanOn);
        float xpan = (float)(look.XPanning + pu), ypan = (float)(look.YPanning + pv);
        // Build's u in pixels: xpanning + t * xrepeat * 8; x-flipped: xrepeat * 8 + 2 * xpanning less that
        float u0 = xpan / size.X, u1 = u0 + wall.XRepeat * 8f / size.X;
        if ((wall.Cstat & 8) != 0)
            (u0, u1) = ((wall.XRepeat * 8f + xpan) / size.X, xpan / size.X);
        float yr = Math.Max(1, wall.YRepeat);
        float V(double z)
        {
            float v = (float)((z - peg) * yr / 2048.0 / size.Y + ypan / 256.0);
            return (look.Cstat & 256) != 0 ? -v : v;
        }
        var part = Get(tile, kind, wall.Sector, oneSided, wall: true, pal: look.Pal);
        // (the wall's sector's visibility: Build darkens a wall by the sector it is drawn from)
        var color = ShadeCode(look.Shade, _d.Sectors[wall.Sector].Visibility);
        double d1 = b1 - t1, d2 = b2 - t2;
        if (d1 <= 0 || d2 <= 0)
        {
            // open at one end only (a sloped floor or ceiling crossing the other side's): Build draws the columns where
            // the top is above the bottom, so the face ends where the two lines cross (a triangle, not a sliver along
            // the whole wall standing above the lower plane)
            double s = d1 / (d1 - d2);
            double xc = x1 + s * (x2 - x1), yc = y1 + s * (y2 - y1), zc = t1 + s * (t2 - t1);
            var x = (P(xc, yc, zc), new Vector2(u0 + (float)s * (u1 - u0), V(zc)));
            var tri = d1 > 0
                ? new[] { (P(x1, y1, t1), new Vector2(u0, V(t1))), x, (P(x1, y1, b1), new Vector2(u0, V(b1))) }
                : new[] { x, (P(x2, y2, t2), new Vector2(u1, V(t2))), (P(x2, y2, b2), new Vector2(u1, V(b2))) };
            foreach (var (p, uv) in tri)
            {
                part.V.Add(p);
                part.Uv.Add(uv);
                part.C.Add(color);
            }
            return;
        }
        var a = (P(x1, y1, t1), new Vector2(u0, V(t1)));
        var b = (P(x2, y2, t2), new Vector2(u1, V(t2)));
        var c = (P(x2, y2, b2), new Vector2(u1, V(b2)));
        var d = (P(x1, y1, b1), new Vector2(u0, V(b1)));
        foreach (var (p, uv) in new[] { a, b, c, a, c, d })
        {
            part.V.Add(p);
            part.Uv.Add(uv);
            part.C.Add(color);
        }
    }
}
