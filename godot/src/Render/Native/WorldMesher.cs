using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Chosen.Sim.Build;
using Godot;
using Chosen.Shared.Blood.Map;

namespace Chosen.Render.Native;

/// <summary>How a surface is drawn: an opaque picture, a masked one (palette index 255 clear), a translucent one
/// (blended), Blood's parallax sky.</summary>
public enum SurfaceKind { Opaque, Masked, Translucent, Sky, PortalFace, Water, WallPortal }

/// <summary>
/// The faces of Build sectors as they are now (Board), as Build draws them: each sector's floor and ceiling over its
/// loops (PolyFill: Build's scanline fill, even-odd), and for each wall the faces seen from its sector: a one-sided
/// wall floor to ceiling, a two-sided one's lower and upper walls (the next sector's floor and ceiling columns, drawn
/// with this side's wall), a masked or one-way wall across the opening. Pictures mapped as Build maps them (walls by
/// xrepeat and yrepeat pegged to a plane, flats world-aligned or relative to the first wall, flipped and swapped by their
/// stat bits); panning and shade effects are added by the shader from the data textures (WorldData), and so are the
/// heights: every vertex names the plane it lies on (and the planes it is clamped between), so a moving floor or
/// ceiling moves its faces without a rebuild.
/// Vertex data: COLOR r = (127 - shade) / 255, g = the viewer sector's visibility rate / 255, b = a portal copy's mark,
/// a = 1; UV2 = (own Build sector + 1 for the stacked rooms' walk, opacity); CUSTOM0 = the planes of its height (P, L, H:
/// clamp(P, L, H), plane refs 1 + 2 sector + ceiling, 0 none; w: a wall cut to its exact heights in the shader, 1 its top
/// vertex, 2 its bottom one, 0 not); CUSTOM1 = such a wall's other edge's planes (P, L, H), a flat's change of u and v since the last game frame (x, y), and whether and
/// how far the corner moved since then (w: Blend, WorldShader.VertexBlend; a flat's move in CUSTOM2.xy); CUSTOM2 = a wall's picture peg (P, L, H as CUSTOM0) and its v per Quake unit below the peg (0: the
/// v is the mesh's); CUSTOM3 = (the sector whose light effect applies + 1, which: 0 floor, 1 ceiling, 2 walls; the
/// panning source: 1 + wall for a wall, 1 + sector for a flat, negative for a flat; palette).
/// </summary>
public sealed class WorldMesher
{
    public sealed class Bucket
    {
        public readonly List<Vector3> V = new(), N = new();
        public readonly List<Vector2> Uv = new(), Uv2 = new();
        public readonly List<Color> C = new();
        public readonly List<float> C0 = new(), C1 = new(), C2 = new(), C3 = new();
        public bool Sky;
        public int Count => V.Count;

        public void Clear()
        {
            V.Clear(); N.Clear(); Uv.Clear(); Uv2.Clear(); C.Clear(); C0.Clear(); C1.Clear(); C2.Clear(); C3.Clear();
        }
    }

    public readonly Dictionary<(int tile, SurfaceKind kind), Bucket> Buckets = new();
    readonly Board _b;
    /// <summary>Is a sector's floor (false) or ceiling (true) a room-over-room portal's face (tile 504 over a link: the
    /// far room is drawn through it, WorldPortals)?</summary>
    public Func<int, bool, bool> IsPortal;
    /// <summary>Is a portal face's link one that doesn't move (the far room right beyond it, as Forge's Add Floor
    /// makes: a copy of the room before it holds the room beyond too, so the face, seen in a copy, shows it)?</summary>
    public Func<int, bool, bool> IsPortalInPlace;
    /// <summary>Is a wall drawn as a wall portal's face (a wall stack's one-way picture, a mirror: PortalRenderer.WallPortalWalls)?</summary>
    public Func<int, bool> IsWallPortal;
    /// <summary>Is a sector's floor or ceiling a water surface drawn see-through (Enhanced.WaterPortals: its link a
    /// portal, the surface blended over the far room)?</summary>
    public Func<int, bool, bool> IsWater;

    /// <summary>The faces carry how far their corners moved since the last game frame (a sector moving rigidly: built
    /// once a game frame, the shader blends its corners between the two as it blends its planes).</summary>
    public bool Blend;

    /// <summary>Where between the last game frame's wall points (0) and these (1) the faces are built (a sector moving
    /// otherwise, MovesRigidly: built again each drawn frame).</summary>
    public float Lerp = 1f;

    /// <summary>Do two sectors share the ground (SectorMap.Overlaps; set before Setup)?</summary>
    public Func<int, int, bool> SharesGround;

    /// <summary>Is sector s's move since the last game frame rigid (each corner as far from the first as it was)? Then
    /// any blend of its two places is a plain transform of the plane and its floor's fill holds. A floor round a moving
    /// hole or beside a door doesn't: blended, it opened long thin slits (DWE3M1's river round the boat, a yard beside a
    /// door). bugs.py motion holds these to the exact picture.</summary>
    public bool MovesRigidly(int s)
    {
        const float Slack = 2;      // (Build's points are whole units: turning, each is rounded)
        bool first = true;
        Vector2 a = default, a0 = default;
        foreach (var loop in _b.Loops[s])
            foreach (int w in loop)
            {
                var now = new Vector2(_b.Walls[w].X, _b.Walls[w].Y);
                var was = new Vector2(_b.PrevWallX[w], _b.PrevWallY[w]);
                if (first)
                {
                    a = now;
                    a0 = was;
                    first = false;
                }
                else if (Mathf.Abs(now.DistanceTo(a) - was.DistanceTo(a0)) > Slack)
                    return false;
            }
        return true;
    }

    public WorldMesher(Board b) => _b = b;

    public void Clear()
    {
        foreach (var bk in Buckets.Values)
            bk.Clear();
    }

    Bucket For(int tile, SurfaceKind kind)
    {
        if (!Buckets.TryGetValue((tile, kind), out var bk))
            Buckets[(tile, kind)] = bk = new Bucket { Sky = kind == SurfaceKind.Sky };
        return bk;
    }

    // ---------------------------------------------------------------- coordinates
    /// <summary>Build x, y, z to Godot space (Godot = Quake (y, z, x) / 32; Quake = (x / 16, -y / 16, -z / 256)).</summary>
    public static Vector3 G(double bx, double by, double bz) => new((float)(-by / 512.0), (float)(-bz / 8192.0), (float)(bx / 512.0));

    public static int PlaneRef(int s, bool ceiling) => 1 + 2 * s + (ceiling ? 1 : 0);

    static int Pow2(int n)
    {
        int p = 1;
        while (p < n)
            p <<= 1;
        return p;
    }

    static (int w, int h) TileSize(int tile)
    {
        int w = Data.Tiles.W(tile), h = Data.Tiles.H(tile);
        return (w > 0 ? w : 64, h > 0 ? h : 64);
    }

    // ---------------------------------------------------------------- a sector
    /// <summary>Every face of sector s: its floor, ceiling and the walls seen from it.</summary>
    public void EmitSector(int s)
    {
        if (_b.Sectors[s].WallNum >= 3)
        {
            EmitFlat(s, false);
            EmitFlat(s, true);
        }
        foreach (int w in _b.SectorWalls(s))
            EmitWall(s, w);
    }

    readonly List<Vector2> _pts = new(), _prevPts = new();
    readonly List<List<int>> _loops = new();
    readonly Dictionary<Vector2, Vector2> _prevOf = new();
    readonly Dictionary<Vector2, Vector2[]> _middles = new();
    bool _flatMoved;

    void EmitFlat(int s, bool ceiling)
    {
        var sec = _b.Sectors[s];
        int stat = ceiling ? sec.CeilingStat : sec.FloorStat;
        bool sky = (stat & 1) != 0;
        int tile = ceiling ? sec.CeilingPicnum : sec.FloorPicnum;
        // (a link's floor or ceiling, tile 504, is the portal face: WorldPortals draws through it)
        bool portal = !sky && IsPortal != null && IsPortal(s, ceiling);
        bool water = !sky && !portal && IsWater != null && IsWater(s, ceiling);
        // (a portal face in place: bucket 1, its material drops it in a copy)
        var bk = For(portal ? (IsPortalInPlace != null && IsPortalInPlace(s, ceiling) ? 1 : 0) : tile, sky ? SurfaceKind.Sky : portal ? SurfaceKind.PortalFace : water ? SurfaceKind.Water : SurfaceKind.Opaque);
        _pts.Clear();
        _prevPts.Clear();
        _loops.Clear();
        _prevOf.Clear();
        _middles.Clear();
        _flatMoved = false;
        foreach (var loop in _b.Loops[s])
        {
            var l = new List<int>(loop.Length);
            foreach (int w in loop)
            {
                l.Add(_pts.Count);
                var now = Point(w);
                _pts.Add(now);
                var was = PrevPoint(w);
                _prevPts.Add(was);
                if (now != was)
                    _flatMoved = true;
            }
            _loops.Add(l);
        }
        var tris = PolyFill.Fill(_pts, _loops, _flatMoved ? _middles : null);
        if (tris.Count == 0)
            return;
        var (tw, th) = TileSize(tile);
        var U = new double[3];
        var V = new double[3];
        FlatAxes(s, ceiling, U, V, Point(sec.WallPtr), Point(_b.Walls[sec.WallPtr].Point2));
        double[] U0 = null, V0 = null;
        if (_flatMoved)
        {
            U0 = new double[3];
            V0 = new double[3];
            FlatAxes(s, ceiling, U0, V0, PrevPoint(sec.WallPtr), PrevPoint(_b.Walls[sec.WallPtr].Point2));
        }
        // (a sky island's ceiling is painted at its neighbour's height: SkyOver)
        int over = ceiling && sky ? SkyOver(s) : -1;
        var pl = _b.Plane(over >= 0 ? over : s, ceiling);
        int pref = PlaneRef(over >= 0 ? over : s, ceiling);
        int shade = ceiling ? sec.CeilingShade : sec.FloorShade;
        int pal = ceiling ? sec.CeilingPal : sec.FloorPal;
        var col = new Color(Mathf.Clamp((127 - shade) / 255f, 0f, 1f), ((sec.Visibility + 16) & 255) / 255f, 0f, 1f);
        // the plane's normal (Godot): z = a x + b y + c in Build space; up for a floor, down for a ceiling
        var n = PlaneNormal(pl.a, pl.b, ceiling);
        for (int t = 0; t + 2 < tris.Count; t += 3)
        {
            Vector2 a = tris[t], b = tris[t + 1], c = tris[t + 2];
            // clockwise seen from the front (Godot's front faces)
            if ((G(b.X, b.Y, 0) - G(a.X, a.Y, 0)).Cross(G(c.X, c.Y, 0) - G(a.X, a.Y, 0)).Dot(n) > 0)
                (b, c) = (c, b);
            for (int k = 0; k < 3; k++)
            {
                var p = k == 0 ? a : k == 1 ? b : c;
                double z = pl.a * p.X + pl.b * p.Y + pl.c;
                double u = U[0] * p.X + U[1] * p.Y + U[2], v = V[0] * p.X + V[1] * p.Y + V[2];
                float du = 0, dv = 0, mx = 0, my = 0, moved = 0;
                if (_flatMoved)
                {
                    // where this point of the fill was a game frame ago, and what its picture's coordinates were there
                    var was = FlatPrev(p);
                    mx = p.X - was.X;
                    my = p.Y - was.Y;
                    moved = 1;
                    du = (float)((u - (U0[0] * was.X + U0[1] * was.Y + U0[2])) / tw);
                    dv = (float)((v - (V0[0] * was.X + V0[1] * was.Y + V0[2])) / th);
                }
                AddVertex(bk, G(p.X, p.Y, z), n, new Vector2((float)(u / tw), (float)(v / th)), col, s, 1f,
                    pref, 0, 0, mx, my, 0, 0f, s, (ceiling ? 1 : 0) + 4 * (s + 1), -(s + 1), pal, 0, du, dv, 0, moved);
            }
        }
    }

    /// <summary>A sector under the sky standing wholly in others under a higher sky (E1M2's train in its cutting): the sector
    /// to paint its sky at, else -1. Build paints no sky at an opening between two skies, it goes on to the next sector, whose
    /// walls show to their full height; sky faces here paint over all behind them, and at the island's own lower plane they
    /// cut those walls along its outline.</summary>
    int SkyOver(int s)
    {
        int best = -1, bestZ = _b.Sectors[s].CeilingZ;
        if ((_b.Sectors[s].CeilingStat & 3) != 1)
            return -1;
        foreach (int w in _b.SectorWalls(s))
        {
            int ns = _b.Walls[w].NextSector;
            if (ns < 0 || (_b.Sectors[ns].CeilingStat & 3) != 1)
                return -1;
            if (_b.Sectors[ns].CeilingZ < bestZ)
            {
                bestZ = _b.Sectors[ns].CeilingZ;
                best = ns;
            }
        }
        return best;
    }

    /// <summary>A wall's start where the faces are built (between the last game frame's and now: Lerp).</summary>
    Vector2 Point(int w)
    {
        var wall = _b.Walls[w];
        if (Lerp >= 1f)
            return new Vector2(wall.X, wall.Y);
        return new Vector2(_b.PrevWallX[w] + (wall.X - _b.PrevWallX[w]) * Lerp, _b.PrevWallY[w] + (wall.Y - _b.PrevWallY[w]) * Lerp);
    }

    /// <summary>A wall's start a game frame ago (Blend), else where it is.</summary>
    Vector2 PrevPoint(int w) => Blend ? new Vector2(_b.PrevWallX[w], _b.PrevWallY[w]) : Point(w);

    /// <summary>How far a wall's corner moved since the last game frame, for CUSTOM1.w: 12 bits each way, in Build units
    /// (0: not at all, or further than that, drawn where it is).</summary>
    static float PackMove(float dx, float dy)
    {
        int x = Mathf.RoundToInt(dx), y = Mathf.RoundToInt(dy);
        if ((x == 0 && y == 0) || x < -2047 || x > 2047 || y < -2047 || y > 2047)
            return 0;
        return (x + 2048) * 4096 + (y + 2048);
    }

    float WallMove(int w) => Blend ? PackMove(_b.Walls[w].X - _b.PrevWallX[w], _b.Walls[w].Y - _b.PrevWallY[w]) : 0;

    /// <summary>Where a point of the fill was a game frame ago: the fill's points lie along the sector's edges (its bands
    /// cut them), so as far along the same edge; a fan's middle, on none, in the middle of where its outline was (taken
    /// as on the nearest edge, a turning floor's picture slewed: E3M5's door).</summary>
    Vector2 FlatPrev(Vector2 p)
    {
        if (_prevOf.TryGetValue(p, out var found))
            return found;
        if (_middles.TryGetValue(p, out var outline))
        {
            var mean = Vector2.Zero;
            foreach (var q in outline)
                mean += FlatPrev(q);
            return _prevOf[p] = mean / outline.Length;
        }
        float best = float.MaxValue;
        var was = p;
        foreach (var l in _loops)
            for (int i = 0; i < l.Count; i++)
            {
                int ia = l[i], ib = l[(i + 1) % l.Count];
                Vector2 a = _pts[ia], ab = _pts[ib] - a;
                float len = ab.LengthSquared();
                float t = len > 0 ? Mathf.Clamp((p - a).Dot(ab) / len, 0, 1) : 0;
                float d = (a + ab * t - p).LengthSquared();
                if (d < best)
                {
                    best = d;
                    was = _prevPts[ia].Lerp(_prevPts[ib], t);
                }
            }
        _prevOf[p] = was;
        return was;
    }

    static Vector3 PlaneNormal(double a, double b, bool ceiling)
    {
        // Quake z = -(a bx + b by + c)/256 with bx = 16 qx, by = -16 qy: dqz/dqx = -a/16, dqz/dqy = b/16
        var q = new Vector3((float)(a / 16.0), (float)(-b / 16.0), 1f).Normalized();
        var g = new Vector3(q.Y, q.Z, q.X);
        return ceiling ? -g : g;
    }

    /// <summary>A flat's texel coordinates as linear forms over Build x, y (u = U0 x + U1 y + U2), without its panning
    /// (the shader adds it): Build's flat mapping (polymost), world-aligned or relative to the first wall (stat 64), 8
    /// units a texel with stat 8 (16 else), swapped (4) and flipped (16, 32).</summary>
    void FlatAxes(int s, bool ceiling, double[] U, double[] V, Vector2 w0, Vector2 w1)
    {
        var sec = _b.Sectors[s];
        int stat = ceiling ? sec.CeilingStat : sec.FloorStat;
        double scale = (stat & 8) != 0 ? 8 : 16;
        double ox = 0, oy = 0, ubx = 1, uby = 0, vbx = 0, vby = 1;
        if ((stat & 64) != 0)
        {
            // (the first wall's two points given: then and now for a moving sector, whose picture blends with its corners;
            // its picture from where it is now slid a game frame's way off them and back each frame: E2M1's boat)
            double dx = w1.X - w0.X, dy = w1.Y - w0.Y;
            double ln = Math.Sqrt(dx * dx + dy * dy);
            if (ln == 0)
                ln = 1;
            ubx = dx / ln; uby = dy / ln;
            vbx = -dy / ln; vby = dx / ln;
            ox = w0.X; oy = w0.Y;
            stat ^= (stat & 4) == 0 ? 32 : 16;
        }
        U[0] = ubx / scale; U[1] = uby / scale; U[2] = -(ox * ubx + oy * uby) / scale;
        V[0] = vbx / scale; V[1] = vby / scale; V[2] = -(ox * vbx + oy * vby) / scale;
        // Build's v runs against its y (polymost)
        for (int i = 0; i < 3; i++)
            V[i] = -V[i];
        if ((stat & 4) != 0)
            for (int i = 0; i < 3; i++)
                (U[i], V[i]) = (V[i], U[i]);
        if ((stat & 16) != 0)
            for (int i = 0; i < 3; i++)
                U[i] = -U[i];
        if ((stat & 32) != 0)
            for (int i = 0; i < 3; i++)
                V[i] = -V[i];
    }

    // ---------------------------------------------------------------- walls
    void EmitWall(int s, int w)
    {
        var wall = _b.Walls[w];
        var q1 = Point(w);
        var q2 = Point(wall.Point2);
        _move1 = WallMove(w);
        _move2 = WallMove(wall.Point2);
        double x1 = q1.X, y1 = q1.Y, x2 = q2.X, y2 = q2.Y;
        double dx = x2 - x1, dy = y2 - y1;
        double len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 1e-6)
            return;
        var sec = _b.Sectors[s];
        var rate = ((sec.Visibility + 16) & 255) / 255f;
        // into the sector: (-dy, dx) in Build x, y (clipmove's facing side); Quake (-dy, -dx), Godot (-dx, 0, -dy)
        var n = new Vector3((float)(-dx / len), 0f, (float)(-dy / len));
        int ownF = PlaneRef(s, false), ownC = PlaneRef(s, true);
        int ns = wall.NextSector;
        // the wall of an opening counts as the next room's, for the stacked rooms' walk: not where this room shares the
        // ground with that one, where the walk reaching this room would hide its own wall (E1M1's chapel 55 beside 45: the
        // low wall under 45's floor gone, the sky through it)
        int openOwn = ns >= 0 && SharesGround != null && SharesGround(s, ns) ? s : ns;
        if (ns < 0 || wall.NextWall < 0)
        {
            int peg = (wall.Cstat & 4) != 0 ? ownF : ownC;
            WallQuad(For(wall.Picnum, IsWallPortal != null && IsWallPortal(w) ? SurfaceKind.WallPortal : SurfaceKind.Opaque), s, w, x1, y1, x2, y2, n, wall.Picnum, wall, wall.XPanning,
                wall.Shade, wall.Pal, rate, s, 1f,
                ownC, 0, 0, ownF, 0, ownC, peg, 0, 0);
            return;
        }
        var nsec = _b.Sectors[ns];
        var nw = _b.Walls[wall.NextWall];
        int nF = PlaneRef(ns, false), nC = PlaneRef(ns, true);
        // the upper wall: the next sector's ceiling column (both skies: Build draws none, the sky shows)
        if ((sec.CeilingStat & nsec.CeilingStat & 1) != 0)
        {
            // (none round a sky island: its sky is level with its neighbour's)
            if (SkyOver(s) < 0 && SkyOver(ns) < 0)
                WallQuad(For(sec.CeilingPicnum, SurfaceKind.Sky), s, w, x1, y1, x2, y2, n, 0, wall, 0, 0, 0, rate, openOwn, 1f,
                    ownC, 0, 0, nC, ownF, ownC, 0, 0, 0);
        }
        else
        {
            int peg = (wall.Cstat & 4) != 0 ? ownC : nC;
            WallQuad(For(wall.Picnum, SurfaceKind.Opaque), s, w, x1, y1, x2, y2, n, wall.Picnum, wall, wall.XPanning,
                wall.Shade, wall.Pal, rate, openOwn, 1f,
                ownC, 0, 0, nC, ownF, ownC, peg, 0, 0);
        }
        // the lower wall: the next sector's floor column, with the other side's picture where swapped (cstat 2)
        if ((sec.FloorStat & nsec.FloorStat & 1) != 0)
            WallQuad(For(sec.FloorPicnum, SurfaceKind.Sky), s, w, x1, y1, x2, y2, n, 0, wall, 0, 0, 0, rate, openOwn, 1f,
                nF, ownF, ownC, ownF, 0, 0, 0, 0, 0);
        else
        {
            var src = (wall.Cstat & 2) != 0 ? nw : wall;
            int peg = (wall.Cstat & 4) != 0 ? ownC : nF;
            WallQuad(For(src.Picnum, SurfaceKind.Opaque), s, (wall.Cstat & 2) != 0 ? wall.NextWall : w, x1, y1, x2, y2, n,
                src.Picnum, wall, src.XPanning, src.Shade, src.Pal, rate, openOwn, 1f,
                nF, ownF, ownC, ownF, 0, 0, peg, 0, 0);
        }
        // a masked wall seen from this side (its own wall masked: Build draws its picture, mapping, palette, shade and
        // translucency), across the opening
        if ((wall.Cstat & 16) != 0)
        {
            var kind = IsWallPortal != null && IsWallPortal(w) ? SurfaceKind.WallPortal : (wall.Cstat & 128) != 0 ? SurfaceKind.Translucent : SurfaceKind.Masked;
            float alpha = (wall.Cstat & 128) == 0 ? 1f : (wall.Cstat & 512) != 0 ? 2f / 3f : 1f / 3f;
            bool bottom = (wall.Cstat & 4) != 0;
            WallQuad(For(wall.OverPicnum, kind), s, w, x1, y1, x2, y2, n, wall.OverPicnum, wall, wall.XPanning, wall.Shade,
                wall.Pal, rate, s, alpha,
                ownC, 0, nC, ownF, nF, 0,
                bottom ? ownF : ownC, bottom ? nF : 0, bottom ? 0 : nC);
        }
        else if ((wall.Cstat & 32) != 0)
        {
            // a one-way wall: its overpicnum across the opening, from this side only, pegged to its own ceiling with
            // cstat 4, else to the next sector's
            WallQuad(For(wall.OverPicnum, IsWallPortal != null && IsWallPortal(w) ? SurfaceKind.WallPortal : SurfaceKind.Masked), s, w, x1, y1, x2, y2, n, wall.OverPicnum, wall, wall.XPanning,
                wall.Shade, wall.Pal, rate, s, 1f,
                ownC, 0, nC, ownF, nF, 0,
                (wall.Cstat & 4) != 0 ? ownC : nC, 0, 0);
        }
    }

    float _move1, _move2;      // the wall's two ends' moves since the last game frame (PackMove)

    /// <summary>One wall face between a top height (tP clamped to tL..tH, plane refs in Quake z: clamp(P, L, H)) and a
    /// bottom one, its picture pegged to the peg height (pP, pL, pH), mapped by the wall's xrepeat, yrepeat and flips;
    /// u from the face's start (wall w's start), the panning by panWall in the shader.</summary>
    void WallQuad(Bucket bk, int viewer, int fxWall, double x1, double y1, double x2, double y2, Vector3 n, int tile,
        BWall map, int xpan, int shade, int pal, float rate, int own, float alpha,
        int tP, int tL, int tH, int bP, int bL, int bH, int pP, int pL, int pH)
    {
        var (tw, th) = TileSize(tile);
        int xrep = map.XRepeat == 0 ? 1 : map.XRepeat, yrep = map.YRepeat == 0 ? 1 : map.YRepeat;
        bool xflip = (map.Cstat & 8) != 0, yflip = (map.Cstat & 256) != 0;
        float u1 = xflip ? xrep * 8f : 0f, u2 = xflip ? 0f : xrep * 8f;
        // v per Quake unit below the peg (texels: yrepeat / 8 a unit)
        float k = (yflip ? -1f : 1f) * yrep / (8f * th);
        int panWall = fxWall;
        int fxSector = _b.Walls[fxWall].Sector;
        var col = new Color(Mathf.Clamp((127 - shade) / 255f, 0f, 1f), rate, 0f, 1f);
        // heights now (the shader takes them from the planes; these are for culling and the occluder)
        double zt = Clamped(tP, tL, tH, x1, y1), zb = Clamped(bP, bL, bH, x1, y1);
        double zt2 = Clamped(tP, tL, tH, x2, y2), zb2 = Clamped(bP, bL, bH, x2, y2);
        var p1t = G(x1, y1, zt);
        var p2t = G(x2, y2, zt2);
        var p2b = G(x2, y2, zb2);
        var p1b = G(x1, y1, zb);
        // a wall between planes that may slope (and cross along it: clamp of two slopes bends where they meet, a straight
        // edge between the ends left a hole, E2M6's underwater strip 185 under its aligned floor) is drawn over the
        // bounds and cut to the exact heights in the shader (mode 1 top, 2 bottom; CUSTOM1 the other edge's planes)
        bool exact = !bk.Sky && (Slopes(tP) || Slopes(tL) || Slopes(tH) || Slopes(bP) || Slopes(bL) || Slopes(bH));
        void V(Vector3 p, float u, bool top, float moved)
        {
            AddVertex(bk, p, n, new Vector2(u / tw, 0f), col, own, alpha,
                top ? tP : bP, top ? tL : bL, top ? tH : bH, pP, pL, pH, k, fxSector, 2 + 4 * (viewer + 1), panWall + 1, pal,
                exact ? (top ? 1 : 2) : 0, top ? bP : tP, top ? bL : tL, top ? bH : tH, moved);
        }
        // two triangles clockwise seen from the front (into the viewer's sector); the corners' heights now can meet in
        // a point (the shader's planes decide), so the winding comes from the wall's direction alone
        var along = G(x2, y2, 0) - G(x1, y1, 0);
        bool cw = along.Cross(new Vector3(0, -1, 0)).Dot(n) < 0;
        if (cw)
        {
            V(p1t, u1, true, _move1); V(p2t, u2, true, _move2); V(p2b, u2, false, _move2);
            V(p1t, u1, true, _move1); V(p2b, u2, false, _move2); V(p1b, u1, false, _move1);
        }
        else
        {
            V(p1t, u1, true, _move1); V(p2b, u2, false, _move2); V(p2t, u2, true, _move2);
            V(p1t, u1, true, _move1); V(p1b, u1, false, _move1); V(p2b, u2, false, _move2);
        }
    }

    /// <summary>May a plane slope (its slope bit, or AlignSlopes tilting it: the sector's filler names a wall)?</summary>
    bool Slopes(int pref)
    {
        if (pref <= 0)
            return false;
        var sec = _b.Sectors[(pref - 1) >> 1];
        bool c = ((pref - 1) & 1) != 0;
        return ((c ? sec.CeilingStat : sec.FloorStat) & 2) != 0 || sec.FogPal != 0;
    }

    /// <summary>Build z of clamp(P, L, H) (plane refs, heights in Quake z: up) at Build (x, y).</summary>
    double Clamped(int p, int l, int h, double x, double y)
    {
        double q = -PlaneZ(p, x, y);
        if (l != 0)
            q = Math.Max(q, -PlaneZ(l, x, y));
        if (h != 0)
            q = Math.Min(q, -PlaneZ(h, x, y));
        return -q;
    }

    double PlaneZ(int pref, double x, double y)
    {
        int s = (pref - 1) >> 1;
        bool c = ((pref - 1) & 1) != 0;
        var pl = _b.Plane(s, c);
        return pl.a * x + pl.b * y + pl.c;
    }

    void AddVertex(Bucket bk, Vector3 p, Vector3 n, Vector2 uv, Color col, int own, float alpha,
        int hp, int hl, int hh, float pp, float pl, int ph, float k, int fxSector, int fxKind, int pan, int pal,
        int mode = 0, float op = 0, float ol = 0, float oh = 0, float moved = 0)
    {
        bk.V.Add(p);
        bk.N.Add(n);
        bk.Uv.Add(uv);
        bk.C.Add(col);
        bk.Uv2.Add(new Vector2(own + 1, alpha));
        bk.C0.Add(hp); bk.C0.Add(hl); bk.C0.Add(hh); bk.C0.Add(mode);
        bk.C1.Add(op); bk.C1.Add(ol); bk.C1.Add(oh); bk.C1.Add(moved);
        bk.C2.Add(pp); bk.C2.Add(pl); bk.C2.Add(ph); bk.C2.Add(k);
        bk.C3.Add(fxSector + 1); bk.C3.Add(fxKind); bk.C3.Add(pan); bk.C3.Add(pal);
    }

    /// <summary>The vertex format of these meshes: CUSTOM0..3 RGBA float.</summary>
    public static readonly Mesh.ArrayFormat Format = (Mesh.ArrayFormat)(
        ((long)Mesh.ArrayCustomFormat.RgbaFloat << (int)Mesh.ArrayFormat.FormatCustom0Shift)
        | ((long)Mesh.ArrayCustomFormat.RgbaFloat << (int)Mesh.ArrayFormat.FormatCustom1Shift)
        | ((long)Mesh.ArrayCustomFormat.RgbaFloat << (int)Mesh.ArrayFormat.FormatCustom2Shift)
        | ((long)Mesh.ArrayCustomFormat.RgbaFloat << (int)Mesh.ArrayFormat.FormatCustom3Shift));

    /// <summary>The buckets as surfaces of a mesh (cleared first), each with the material the callback gives.</summary>
    public void Upload(ArrayMesh mesh, Func<int, SurfaceKind, Material> material)
    {
        mesh.ClearSurfaces();
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        foreach (var ((tile, kind), bk) in Buckets)
        {
            if (bk.Count == 0)
                continue;
            arrays[(int)Mesh.ArrayType.Vertex] = CollectionsMarshal.AsSpan(bk.V);
            arrays[(int)Mesh.ArrayType.Normal] = CollectionsMarshal.AsSpan(bk.N);
            arrays[(int)Mesh.ArrayType.TexUV] = CollectionsMarshal.AsSpan(bk.Uv);
            arrays[(int)Mesh.ArrayType.TexUV2] = CollectionsMarshal.AsSpan(bk.Uv2);
            arrays[(int)Mesh.ArrayType.Color] = CollectionsMarshal.AsSpan(bk.C);
            arrays[(int)Mesh.ArrayType.Custom0] = CollectionsMarshal.AsSpan(bk.C0);
            arrays[(int)Mesh.ArrayType.Custom1] = CollectionsMarshal.AsSpan(bk.C1);
            arrays[(int)Mesh.ArrayType.Custom2] = CollectionsMarshal.AsSpan(bk.C2);
            arrays[(int)Mesh.ArrayType.Custom3] = CollectionsMarshal.AsSpan(bk.C3);
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays, null, null, Format);
            mesh.SurfaceSetMaterial(mesh.GetSurfaceCount() - 1, material(tile, kind));
        }
    }

    // ---------------------------------------------------------------- moving meshes updated in place
    /// <summary>A moving sector's mesh as last uploaded: its surfaces' buckets and vertex counts, and the byte buffers its
    /// in-place updates reuse.</summary>
    public sealed class Layout
    {
        public readonly List<((int, SurfaceKind) key, int count)> Surfaces = new();
        public readonly List<(byte[] vertex, byte[] attribute)> Buffers = new();
        public readonly List<Offsets> Formats = new();
        // each vertex's normal as its bytes were last written (NaN: not yet): only changed ones encoded again
        public readonly List<Vector3[]> Normals = new();
        public Aabb Bounds;      // the custom box last set (set again only when it changes)
    }

    /// <summary>Where each channel goes in a surface's buffers (Godot's, for its format and vertex count), asked once when
    /// the surface is made: asking at each update was ten calls into Godot a surface.</summary>
    public readonly struct Offsets
    {
        public readonly int Vs, As, V, N, C, U, U2, C0, C1, C2, C3;

        public Offsets(Mesh.ArrayFormat fmt, int n)
        {
            var rf = (RenderingServer.ArrayFormat)fmt;
            int O(Mesh.ArrayType t) => (int)RenderingServer.MeshSurfaceGetFormatOffset(rf, n, (int)t);
            Vs = (int)RenderingServer.MeshSurfaceGetFormatVertexStride(rf, n);
            As = (int)RenderingServer.MeshSurfaceGetFormatAttributeStride(rf, n);
            V = O(Mesh.ArrayType.Vertex);
            N = O(Mesh.ArrayType.Normal);
            C = O(Mesh.ArrayType.Color);
            U = O(Mesh.ArrayType.TexUV);
            U2 = O(Mesh.ArrayType.TexUV2);
            C0 = O(Mesh.ArrayType.Custom0);
            C1 = O(Mesh.ArrayType.Custom1);
            C2 = O(Mesh.ArrayType.Custom2);
            C3 = O(Mesh.ArrayType.Custom3);
        }

        // the vertex buffer: the positions at their stride, then after all of them each vertex's normal and tangent
        public int VertexBytes(int n) => Math.Max(Vs * n, N + NormalStride * n);
    }

    /// <summary>r_meshupdate: a moving mesh whose surfaces keep their vertex counts gets its data written into the
    /// buffers it has (ArrayMesh.SurfaceUpdate*Region) instead of new surfaces: making them (Godot's arrays to bytes, new
    /// GPU buffers, the material set again) was two thirds of DWE2M1's 5.5 ms of moving meshes a frame.</summary>
    public static readonly Core.Cvar MeshUpdate = Core.Cvars.Register("r_meshupdate", "1",
        "1: moving meshes whose vertex counts stay are updated in place (0: built anew each time)");
    // the packing checked against Godot's own on the first full uploads' surfaces (PackingChecks of them, all their
    // normals and channels): in place once one matched, off for good at the first that doesn't
    const int PackingChecks = 64;
    static int _packingChecked;
    static bool _packingOff;
    static bool PackingOk => _packingChecked > 0 && !_packingOff;

    /// <summary>Upload, in place when the mesh's surfaces are those of the last upload (layout) with the same counts.</summary>
    public void Upload(ArrayMesh mesh, Func<int, SurfaceKind, Material> material, Layout layout)
    {
        Prepare(layout);
        Commit(mesh, material, layout);
    }

    bool _prepared;
    Aabb _bounds;

    /// <summary>The buckets packed into the layout's buffers when they fit it (no Godot calls: any thread, one mesher and
    /// layout to each); Commit then hands them to Godot.</summary>
    public void Prepare(Layout layout)
    {
        _prepared = layout != null && PackingOk && MeshUpdate.Bool && SameLayout(layout);
        if (!_prepared)
            return;
        for (int i = 0; i < layout.Surfaces.Count; i++)
        {
            var (vb, ab) = layout.Buffers[i];
            Pack(Buckets[layout.Surfaces[i].key], layout.Formats[i], vb, ab, layout.Normals[i]);
        }
        _bounds = Bounds();
    }

    /// <summary>On the main thread after Prepare: the packed buffers written into the mesh's surfaces, else the mesh built
    /// anew (and the layout taken from it).</summary>
    public void Commit(ArrayMesh mesh, Func<int, SurfaceKind, Material> material, Layout layout)
    {
        long tp = Core.Perf.Start();
        if (_prepared)
        {
            Core.Perf.Add("mv.inplace", tp);
            _prepared = false;
            for (int i = 0; i < layout.Buffers.Count; i++)
            {
                var (vb, ab) = layout.Buffers[i];
                mesh.SurfaceUpdateVertexRegion(i, 0, vb);
                mesh.SurfaceUpdateAttributeRegion(i, 0, ab);
            }
            if (layout.Bounds != _bounds)
            {
                mesh.CustomAabb = _bounds;
                layout.Bounds = _bounds;
            }
            Core.Perf.Add("mv.regions", tp);
            return;
        }
        Upload(mesh, material);
        Core.Perf.Add("mv.full", tp);
        if (layout == null)
            return;
        layout.Bounds = Bounds();
        mesh.CustomAabb = layout.Bounds;
        layout.Surfaces.Clear();
        layout.Buffers.Clear();
        layout.Formats.Clear();
        layout.Normals.Clear();
        int s = 0;
        foreach (var ((tile, kind), bk) in Buckets)
        {
            if (bk.Count == 0)
                continue;
            var fmt = mesh.SurfaceGetFormat(s);
            int n = bk.Count;
            var off = new Offsets(fmt, n);
            var vb = new byte[off.VertexBytes(n)];
            var ab = new byte[off.As * n];
            layout.Surfaces.Add(((tile, kind), n));
            layout.Buffers.Add((vb, ab));
            layout.Formats.Add(off);
            var last = new Vector3[n];
            System.Array.Fill(last, new Vector3(float.NaN, 0, 0));
            layout.Normals.Add(last);
            if (!_packingOff && _packingChecked < PackingChecks)
                CheckPacking(mesh, s, bk, fmt, off, vb, ab, last);
            s++;
        }
    }

    // the same surfaces with the same counts, in whatever order this mesher keeps its buckets (each job's mesher has
    // its own: a sector built by another mesher than last time listed them otherwise and was built anew)
    bool SameLayout(Layout layout)
    {
        int used = 0;
        foreach (var bk in Buckets.Values)
            if (bk.Count > 0)
                used++;
        if (used != layout.Surfaces.Count)
            return false;
        foreach (var (key, count) in layout.Surfaces)
            if (!Buckets.TryGetValue(key, out var bk) || bk.Count != count)
                return false;
        return true;
    }

    Aabb Bounds()
    {
        var lo = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
        var hi = -lo;
        foreach (var bk in Buckets.Values)
            foreach (var v in bk.V)
            {
                lo = lo.Min(v);
                hi = hi.Max(v);
            }
        return lo.X > hi.X ? new Aabb() : new Aabb(lo, hi - lo);
    }

    // a normal and a tangent; without tangents Godot makes one from the normal (MissingTangent)
    const int NormalStride = 8;

    // Godot's tangent for a vertex given none (mesh_create_surface_data_from_arrays): (n.z, -n.x, n.y) across the
    // normal, packed by octahedron_tangent_encode with a positive sign
    static (ushort x, ushort y) MissingTangent(Vector3 n)
    {
        var t = new Vector3(n.Z, -n.X, n.Y).Cross(n.Normalized()).Normalized();
        var o = t.OctahedronEncode();
        float y = Math.Max(o.Y, 1f / 32767f) * 0.5f + 0.5f;
        return ((ushort)Math.Clamp(o.X * 65535f, 0f, 65535f), (ushort)Math.Clamp(y * 65535f, 0f, 65535f));
    }

    // the bucket's vertices in Godot's layout for its surface format: the positions as floats (interleaved at the vertex
    // stride), then a block of normals, octahedral in two 16-bit values, each followed by Godot's made-up tangent; colour
    // as bytes, UVs and the custom channels as floats in the attribute buffer
    static void Pack(Bucket bk, Offsets f, byte[] vb, byte[] ab, Vector3[] last)
    {
        int n = bk.Count;
        int vs = f.Vs, ast = f.As, oV = f.V, oN = f.N, oC = f.C, oU = f.U, oU2 = f.U2, o0 = f.C0, o1 = f.C1, o2 = f.C2, o3 = f.C3;
        var vspan = vb.AsSpan();
        var aspan = ab.AsSpan();
        var v = CollectionsMarshal.AsSpan(bk.V);
        var nr = CollectionsMarshal.AsSpan(bk.N);
        var c = CollectionsMarshal.AsSpan(bk.C);
        var uv = CollectionsMarshal.AsSpan(bk.Uv);
        var uv2 = CollectionsMarshal.AsSpan(bk.Uv2);
        var c0 = CollectionsMarshal.AsSpan(bk.C0);
        var c1 = CollectionsMarshal.AsSpan(bk.C1);
        var c2 = CollectionsMarshal.AsSpan(bk.C2);
        var c3 = CollectionsMarshal.AsSpan(bk.C3);
        for (int i = 0; i < n; i++)
        {
            int b = i * vs;
            MemoryMarshal.Write(vspan.Slice(b + oV), in v[i]);
            // (the normal and tangent only when the normal changed: the buffer keeps the last ones)
            if (last[i] != nr[i])
            {
                last[i] = nr[i];
                var o = nr[i].OctahedronEncode();
                ushort ox = (ushort)Math.Clamp(o.X * 65535f, 0f, 65535f), oy = (ushort)Math.Clamp(o.Y * 65535f, 0f, 65535f);
                int nb = oN + i * NormalStride;
                MemoryMarshal.Write(vspan.Slice(nb), in ox);
                MemoryMarshal.Write(vspan.Slice(nb + 2), in oy);
                var (tx, ty) = MissingTangent(nr[i]);
                MemoryMarshal.Write(vspan.Slice(nb + 4), in tx);
                MemoryMarshal.Write(vspan.Slice(nb + 6), in ty);
            }
            int a = i * ast;
            var col = c[i];
            aspan[a + oC] = (byte)Math.Clamp(col.R * 255f, 0f, 255f);
            aspan[a + oC + 1] = (byte)Math.Clamp(col.G * 255f, 0f, 255f);
            aspan[a + oC + 2] = (byte)Math.Clamp(col.B * 255f, 0f, 255f);
            aspan[a + oC + 3] = (byte)Math.Clamp(col.A * 255f, 0f, 255f);
            MemoryMarshal.Write(aspan.Slice(a + oU), in uv[i]);
            MemoryMarshal.Write(aspan.Slice(a + oU2), in uv2[i]);
            MemoryMarshal.Write(aspan.Slice(a + o0), in MemoryMarshal.AsRef<Quad>(MemoryMarshal.AsBytes(c0.Slice(i * 4, 4))));
            MemoryMarshal.Write(aspan.Slice(a + o1), in MemoryMarshal.AsRef<Quad>(MemoryMarshal.AsBytes(c1.Slice(i * 4, 4))));
            MemoryMarshal.Write(aspan.Slice(a + o2), in MemoryMarshal.AsRef<Quad>(MemoryMarshal.AsBytes(c2.Slice(i * 4, 4))));
            MemoryMarshal.Write(aspan.Slice(a + o3), in MemoryMarshal.AsRef<Quad>(MemoryMarshal.AsBytes(c3.Slice(i * 4, 4))));
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    struct Quad { public float A, B, C, D; }

    // a full upload: Godot's bytes for the surface beside this packing of the same data; the in-place updates only while
    // they are the same
    static void CheckPacking(ArrayMesh mesh, int surface, Bucket bk, Mesh.ArrayFormat fmt, Offsets off, byte[] vb, byte[] ab, Vector3[] last)
    {
        Pack(bk, off, vb, ab, last);
        var d = RenderingServer.MeshGetSurface(mesh.GetRid(), surface);
        var gv = d.TryGetValue("vertex_data", out var x) ? x.AsByteArray() : null;
        var ga = d.TryGetValue("attribute_data", out var y) ? y.AsByteArray() : null;
        int badV = Mismatch(gv, vb), badA = Mismatch(ga, ab);
        if (badV >= 0 || badA >= 0)
        {
            _packingOff = true;
            GD.Print($"moving meshes: built anew (packing differs from Godot's: vertex byte {badV} of {gv?.Length}/{vb.Length}, attribute byte {badA} of {ga?.Length}/{ab.Length}, format {(long)fmt})");
            return;
        }
        if (++_packingChecked == PackingChecks)
            GD.Print($"moving meshes: updated in place (packing checked on {PackingChecks} surfaces)");
    }

    static int Mismatch(byte[] godot, byte[] mine)
    {
        if (godot == null || godot.Length != mine.Length)
            return int.MaxValue;
        for (int i = 0; i < mine.Length; i++)
            if (godot[i] != mine[i])
                return i;
        return -1;
    }
}
