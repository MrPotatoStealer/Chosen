using System;
using System.Collections.Generic;
using Chosen.Sim;
using Godot;

namespace Chosen.Render;

/// <summary>
/// The Build sectors the camera may see this frame, as Build's renderer finds them: from the camera's sector through
/// the two-sided walls, each narrowing the window of view angles to the part of it the wall spans, not through a wall
/// whose opening is shut (a closed door: the next sector's floor up to its ceiling there). Generous where it can't
/// tell: a wall of a sector moving or near the eye passes the window on whole, a sector's partners sharing ground or
/// stacked over it come along, a link's far room is reached with the whole window, looking steeply up or down the window
/// is all round. Used to skip rebuilding what isn't seen (LiveRenderer's moving groups); an unknown camera sector
/// sees everything.
/// </summary>
public static class SectorVisibility
{
    static bool[] _seen = Array.Empty<bool>();
    static bool[] _direct = Array.Empty<bool>();      // reached by the walk without going through a link
    static bool[] _reach = Array.Empty<bool>();       // within ReachSteps open walls of the camera's, whichever way
    static int[] _steps = Array.Empty<int>();
    static readonly Queue<int> _fill = new();
    const int ReachSteps = 3;
    static float[] _lo = Array.Empty<float>(), _hi = Array.Empty<float>();
    static float[] _dlo = Array.Empty<float>(), _dhi = Array.Empty<float>();    // the window walked directly
    // and up and down (r_skyfaces 0): the slope of the line of sight, height over distance, the same all along a line, as
    // its angle across is. So a line through an opening is within the opening's slopes in every sector beyond
    static float[] _dtlo = Array.Empty<float>(), _dthi = Array.Empty<float>();
    static float[] _wtl = Array.Empty<float>(), _wth = Array.Empty<float>();
    static float _eyeZ;
    static bool _slopes;
    const float Inf = float.PositiveInfinity;
    const float OpeningSlack = 4f;      // Quake units over an opening (its heights between the game frames)
    // a sector waiting to be walked, directly or not: its window is read when it is (widened meanwhile in place, not
    // queued again: DWE3M1's river, hundreds of sectors, walked 2000 times a frame)
    static readonly Queue<(int s, bool direct)> _queue = new();
    static bool[] _queued = Array.Empty<bool>(), _dqueued = Array.Empty<bool>();
    public static int Walked { get; private set; }
    // each wall's look this frame, worked out the first time the walk crosses it (a sector is walked directly and through
    // a link, and again when its window widens): its opening (WallOpen), its ends' angles off the view
    const byte WallShut = 1, WallWhole = 2, WallSpan = 3;
    static byte[] _wallLook = Array.Empty<byte>();
    static float[] _wa1 = Array.Empty<float>(), _wa2 = Array.Empty<float>();
    static int[] _wallStamp = Array.Empty<int>(), _movingStamp = Array.Empty<int>();
    static bool[] _moving = Array.Empty<bool>();
    static int _stamp;
    static List<int>[] _partners;
    // each partner: free to walk to directly (not a link's far room, nor wholly over or under: _linkPairs, _apart), worked
    // out once instead of looked up at every step of the walk
    static bool[][] _partnerFree;
    // and passing its window on as seen directly: only a room stacked over or under it (map.Stacks), not one that merely
    // overlaps it in plan. Build reaches a sector only through its walls (or a link): DWE3M1's black backstage rooms 941
    // and 836, overlapping the lake's open sectors, took their sky's window and their walls stood black over the trees
    static bool[][] _partnerDirect;
    static readonly HashSet<(int, int)> _overlapOnly = new();
    static bool _partnerFreeStale = true;
    static SectorMap _partnersOf;
    static Dictionary<int, List<(int far, Vector2 offset)>> _linkFar;
    // where the eye is, as seen from the rooms reached through links: each sector's offset to it, and the one being walked from.
    // (a far room's walls by the real eye's angles narrowed the walk to nothing: rooms a portal showed counted as not seen, and
    // with the sky faces drawing nothing their copies were cut: a hole in E1M1's chapel from under its link)
    static Vector2[] _offset = System.Array.Empty<Vector2>();
    static Vector2 _walkOffset;
    static readonly HashSet<(int, int)> _linkPairs = new();     // a link's sector and its far room, either way
    // partners one wholly over the other (sharing ground in plan, not heights: a room seen through a link, E1M1's
    // rooftops over its courtyard): the walk sees one from the other only through the link, not directly
    static readonly HashSet<(int, int)> _apart = new();
    static int _linkCount = -1;
    static SectorMap _linkMap;

    public static readonly Core.Cvar CullCvar = Core.Cvars.Register("r_cullmoving", "1",
        "1: moving sectors the camera can't see (Build's walk through the walls) aren't rebuilt each frame; 0: all are");

    /// <summary>How many sectors the last Update found seen.</summary>
    public static int SeenCount { get; private set; }

        /// <summary>Everything counts as seen (no camera sector, no Build sectors).</summary>
    public static bool All { get; private set; } = true;

    public static bool Seen(int b) => All || (b >= 0 && b < _seen.Length && _seen[b]);

    /// <summary>The view's yaw (Quake) the windows' angles are from.</summary>
    public static float ViewYaw { get; private set; }

    /// <summary>Is sector b reached without going through a link, and through what (angles across off the view's yaw, slopes
    /// of the line of sight)? Only that: not the sectors merely near the camera (DirectlySeen).</summary>
    public static bool Window(int b, out float lo, out float hi, out float tlo, out float thi)
    {
        lo = -10f;
        hi = 10f;
        tlo = -Inf;
        thi = Inf;
        if (All || b < 0 || b >= _direct.Length)
            return true;
        if (!_direct[b])
            return false;
        lo = _dlo[b];
        hi = _dhi[b];
        tlo = _dtlo[b];
        thi = _dthi[b];
        return true;
    }

    /// <summary>Is sector b reached by the walk without going through a link (whose far room a portal shows: what
    /// stands there is seen as its portal copy), or within ReachSteps open walls of the camera's whichever way it
    /// looks (a tall sprite of a sector beside the view's window: DWE3M1's trees off to the side were cut at the sky)?
    /// Its sprites are drawn over the sky (BuildSky.SpriteMark); the others' the sky hides as it hides the level
    /// (E1M1's courtyard sky is a link to its rooftops over it, a dozen sectors away by the walls: a napalm ball gone
    /// through was drawn there and as its copy, side by side).</summary>
    public static bool DirectlySeen(int b) => All || (b >= 0 && b < _reach.Length && (_reach[b] || _direct[b]));

    /// <summary>Can sector b's faces be seen: it, or a neighbour (a shut door or a lift's walls face the next room,
    /// which the walk reaches when it doesn't reach b)?</summary>
    public static bool FacesSeen(int b)
    {
        if (All || Seen(b))
            return true;
        if (_partnersOf == null || b < 0 || b >= _partnersOf.Neighbours.Length)
            return false;
        var nb = _partnersOf.Neighbours[b];
        for (int i = 0; i < nb.Count; i++)
            if (Seen(nb[i]))
                return true;
        return false;
    }

    /// <summary>The sectors seen from camQ looking along forward (Quake), with the given half angle of view across.</summary>
    public static void Update(World world, int camSector, Vector3 camQ, Vector3 forward, float halfFovDeg)
    {
        var map = world?.Map;
        if (map == null || !map.Valid(camSector) || !CullCvar.Bool)
        {
            All = true;
            return;
        }
        All = false;
        int n = map.SectorCount;
        if (_seen.Length != n)
        {
            _seen = new bool[n];
            _direct = new bool[n];
            _reach = new bool[n];
            _steps = new int[n];
            _lo = new float[n];
            _hi = new float[n];
            _dlo = new float[n];
            _dhi = new float[n];
            _dtlo = new float[n];
            _dthi = new float[n];
            _queued = new bool[n];
            _dqueued = new bool[n];
            _offset = new Vector2[n];
        }
        Array.Clear(_seen);
        Array.Clear(_direct);
        Array.Clear(_queued);
        Array.Clear(_dqueued);
        Array.Clear(_offset);
        _walkOffset = Vector2.Zero;
        if (_wallStamp.Length != map.WallCount)
        {
            _wallLook = new byte[map.WallCount];
            _wa1 = new float[map.WallCount];
            _wa2 = new float[map.WallCount];
            _wtl = new float[map.WallCount];
            _wth = new float[map.WallCount];
            _wallStamp = new int[map.WallCount];
        }
        if (_movingStamp.Length != n)
        {
            _movingStamp = new int[n];
            _moving = new bool[n];
        }
        if (++_stamp == int.MaxValue)
        {
            _stamp = 1;
            Array.Clear(_wallStamp);
            Array.Clear(_movingStamp);
        }
        int walked = 0;
        Partners(map);
        LinkFar(world);
        if (_partnerFreeStale)
        {
            _partnerFreeStale = false;
            _partnerFree = new bool[_partners.Length][];
            _partnerDirect = new bool[_partners.Length][];
            for (int i = 0; i < _partners.Length; i++)
            {
                var ps = _partners[i];
                _partnerFree[i] = new bool[ps.Count];
                _partnerDirect[i] = new bool[ps.Count];
                for (int k = 0; k < ps.Count; k++)
                {
                    _partnerFree[i][k] = !_linkPairs.Contains((i, ps[k])) && !_apart.Contains((i, ps[k]));
                    _partnerDirect[i][k] = _partnerFree[i][k] && !_overlapOnly.Contains((i, ps[k]));
                }
            }
        }
        float yaw = Mathf.Atan2(forward.Y, forward.X);
        float pitch = Mathf.Asin(Mathf.Clamp(forward.Z / Mathf.Max(forward.Length(), 1e-6f), -1, 1));
        // tilted, the view reaches wider across (and all round looking steeply up or down); a margin for the eye's
        // turn between the game frames
        float half;
        if (Mathf.Abs(pitch) > Mathf.DegToRad(35))
            half = Mathf.Pi;
        else
            half = Mathf.Min(Mathf.Pi, Mathf.Atan(Mathf.Tan(Mathf.DegToRad(Mathf.Min(halfFovDeg, 89))) / Mathf.Cos(pitch)) + Mathf.DegToRad(15));
        var eye = new Vector2(camQ.X, camQ.Y);
        ViewYaw = yaw;
        _eyeZ = camQ.Z;
        _slopes = !BuildSky.Faces.Bool;
        _queue.Clear();
        Reach(world, map, camSector);
        Visit(camSector, -half, half, true, -Inf, Inf);
        while (_queue.Count > 0)
        {
            var (s, direct) = _queue.Dequeue();
            float lo, hi, tlo = -Inf, thi = Inf;
            if (direct)
            {
                _dqueued[s] = false;
                lo = _dlo[s];
                hi = _dhi[s];
                tlo = _dtlo[s];
                thi = _dthi[s];
            }
            else
            {
                _queued[s] = false;
                lo = _lo[s];
                hi = _hi[s];
            }
            walked++;
            _walkOffset = direct ? Vector2.Zero : _offset[s];
            var walls = map.SectorWalls(s);
            for (int wi = 0; wi < walls.Count; wi++)
            {
                int w = walls[wi];
                int nx = map.WallNext(w);
                if (nx < 0 || !map.Valid(nx))
                    continue;
                byte look = WallLook(world, map, s, nx, w, eye, yaw);
                if (look == WallShut)
                    continue;
                if (look == WallWhole && (direct || _walkOffset == Vector2.Zero || MovingNow(world, map, s) || MovingNow(world, map, nx) || CornersMoved(world, w)))
                {
                    Visit(nx, lo, hi, direct, tlo, thi);
                    continue;
                }
                float ntlo = tlo, nthi = thi;
                if (direct && _slopes)
                {
                    ntlo = Mathf.Max(tlo, _wtl[w]);
                    nthi = Mathf.Min(thi, _wth[w]);
                    if (ntlo >= nthi)
                        continue;
                }
                float a1 = _wa1[w], a2 = _wa2[w];
                if (!direct && _walkOffset != Vector2.Zero)
                {
                    // from the eye as this far room sees it (no window to narrow to beside it)
                    var p1 = map.WallP1(w) - eye - _walkOffset;
                    var p2 = map.WallP2(w) - eye - _walkOffset;
                    if (DistToSegmentSq(eye + _walkOffset, map.WallP1(w), map.WallP2(w)) < 24 * 24)
                    {
                        Visit(nx, lo, hi, false, -Inf, Inf);
                        continue;
                    }
                    a1 = Wrap(Mathf.Atan2(p1.Y, p1.X) - yaw);
                    a2 = Wrap(Mathf.Atan2(p2.Y, p2.X) - yaw);
                }
                // the wall's span: the shorter way round between its ends (it can't span half the circle unless the
                // eye is on it, taken above)
                float wlo = Mathf.Min(a1, a2), whi = Mathf.Max(a1, a2);
                if (whi - wlo <= Mathf.Pi)
                    Clip(nx, lo, hi, wlo, whi, direct, ntlo, nthi);
                else
                {
                    // across the back: [whi, pi] and [-pi, wlo]
                    Clip(nx, lo, hi, whi, Mathf.Pi, direct, ntlo, nthi);
                    Clip(nx, lo, hi, -Mathf.Pi, wlo, direct, ntlo, nthi);
                }
            }
        }
        Walked = walked;
        int c = 0;
        foreach (bool b in _seen)
            if (b)
                c++;
        SeenCount = c;
    }

    // wall w of sector s into nx this frame: shut, passing the window on whole (a sector moving, or the eye by the wall),
    // or narrowing it to its span (its ends' angles in _wa1, _wa2)
    static byte WallLook(World world, SectorMap map, int s, int nx, int w, Vector2 eye, float yaw)
    {
        if (_wallStamp[w] == _stamp)
            return _wallLook[w];
        _wallStamp[w] = _stamp;
        var p1 = map.WallP1(w);
        var p2 = map.WallP2(w);
        byte look;
        if (Shut(world, s, nx, p1, p2))
            look = WallShut;
        else if (MovingNow(world, map, s) || MovingNow(world, map, nx) || CornersMoved(world, w) || DistToSegmentSq(eye, p1, p2) < 24 * 24)
            look = WallWhole;
        else
        {
            look = WallSpan;
            _wa1[w] = Wrap(Mathf.Atan2(p1.Y - eye.Y, p1.X - eye.X) - yaw);
            _wa2[w] = Wrap(Mathf.Atan2(p2.Y - eye.Y, p2.X - eye.X) - yaw);
            if (_slopes)
                OpeningSlopes(world, s, nx, w, p1, p2, eye);
        }
        _wallLook[w] = look;
        return look;
    }

    // what lines of sight pass through wall w's opening: over a level height the steepest is at the nearest point of the
    // wall going up, the farthest going down. Between two sky ceilings (floors) it runs on into the sky (Shut).
    static void OpeningSlopes(World world, int s, int n, int w, Vector2 p1, Vector2 p2, Vector2 eye)
    {
        var map = world.Map;
        float near = Mathf.Sqrt(DistToSegmentSq(eye, p1, p2)), far = Mathf.Sqrt(Mathf.Max((p1 - eye).LengthSquared(), (p2 - eye).LengthSquared()));
        near = Mathf.Max(near, 1f);
        if (map.CeilSky(s) && map.CeilSky(n))
            _wth[w] = Inf;
        else
        {
            float top = Mathf.Max(Mathf.Min(world.SectorCeilZ(s, p1), world.SectorCeilZ(n, p1)),
                Mathf.Min(world.SectorCeilZ(s, p2), world.SectorCeilZ(n, p2))) + OpeningSlack - _eyeZ;
            _wth[w] = top / (top > 0 ? near : far);
        }
        if (map.FloorSky(s) && map.FloorSky(n))
            _wtl[w] = -Inf;
        else
        {
            float bottom = Mathf.Min(Mathf.Max(world.SectorFloorZ(s, p1), world.SectorFloorZ(n, p1)),
                Mathf.Max(world.SectorFloorZ(s, p2), world.SectorFloorZ(n, p2))) - OpeningSlack - _eyeZ;
            _wtl[w] = bottom / (bottom < 0 ? near : far);
        }
    }

    static bool MovingNow(World world, SectorMap map, int s)
    {
        if (_movingStamp[s] != _stamp)
        {
            _movingStamp[s] = _stamp;
            _moving[s] = Moving(world, map, s);
        }
        return _moving[s];
    }

    static void Clip(int s, float lo, float hi, float wlo, float whi, bool direct, float tlo, float thi)
    {
        float a = Mathf.Max(lo, wlo), b = Mathf.Min(hi, whi);
        if (a < b)
            Visit(s, a, b, direct, tlo, thi);
    }

    /// <summary>The sectors within ReachSteps open two-sided walls (partners free) of sector c, no link crossed (_reach).</summary>
    static void Reach(World world, SectorMap map, int c)
    {
        Array.Clear(_reach);
        _fill.Clear();
        _reach[c] = true;
        _steps[c] = 0;
        _fill.Enqueue(c);
        while (_fill.Count > 0)
        {
            int s = _fill.Dequeue();
            var ps = _partners[s];
            var free = _partnerFree[s];
            for (int i = 0; i < ps.Count; i++)
            {
                int o = ps[i];
                if (!_reach[o] && free[i])
                {
                    _reach[o] = true;
                    _steps[o] = _steps[s];
                    _fill.Enqueue(o);
                }
            }
            if (_steps[s] >= ReachSteps)
                continue;
            var walls = map.SectorWalls(s);
            for (int wi = 0; wi < walls.Count; wi++)
            {
                int w = walls[wi];
                int nx = map.WallNext(w);
                if (nx < 0 || !map.Valid(nx) || _reach[nx] || Shut(world, s, nx, map.WallP1(w), map.WallP2(w)))
                    continue;
                _reach[nx] = true;
                _steps[nx] = _steps[s] + 1;
                _fill.Enqueue(nx);
            }
        }
    }

    static void Visit(int s, float lo, float hi, bool direct, float tlo, float thi)
    {
        // the window walked directly is its own: a sector also a link's far room is seen all round through it, and that
        // took in what it saw directly (DWE3M1's river, the far room of the links under it: only a sliver of it walked
        // on directly, the bank's trees beyond counted as seen through the link and the sky cut them)
        if (direct)
        {
            if (_direct[s] && lo >= _dlo[s] && hi <= _dhi[s] && tlo >= _dtlo[s] && thi <= _dthi[s])
                return;
            if (_direct[s])
            {
                lo = Mathf.Min(lo, _dlo[s]);
                hi = Mathf.Max(hi, _dhi[s]);
                tlo = Mathf.Min(tlo, _dtlo[s]);
                thi = Mathf.Max(thi, _dthi[s]);
            }
            _direct[s] = true;
            _dlo[s] = lo;
            _dhi[s] = hi;
            _dtlo[s] = tlo;
            _dthi[s] = thi;
        }
        else if (_seen[s])
        {
            if (lo >= _lo[s] && hi <= _hi[s])
                return;
            lo = Mathf.Min(lo, _lo[s]);
            hi = Mathf.Max(hi, _hi[s]);
        }
        if (!direct)
            _offset[s] = _walkOffset;
        _lo[s] = _seen[s] ? Mathf.Min(lo, _lo[s]) : lo;
        _hi[s] = _seen[s] ? Mathf.Max(hi, _hi[s]) : hi;
        _seen[s] = true;
        if (direct ? !_dqueued[s] : !_queued[s])
        {
            if (direct)
                _dqueued[s] = true;
            else
                _queued[s] = true;
            _queue.Enqueue((s, direct));
        }
        var partners = _partners[s];
        for (int i = 0; i < partners.Count; i++)
        {
            // (a room stacked over this one through a link is seen through it: E1M1's rooftops 90 over its courtyard)
            int o = partners[i];
            Visit(o, lo, hi, direct && _partnerDirect[s][i], tlo, thi);
        }
        if (_linkFar.TryGetValue(s, out var far))
            for (int i = 0; i < far.Count; i++)
            {
                int f = far[i].far;
                if (!_seen[f] || _lo[f] > -Mathf.Pi || _hi[f] < Mathf.Pi)
                {
                    var from = _walkOffset;
                    _walkOffset = from + far[i].offset;
                    Visit(f, -Mathf.Pi, Mathf.Pi, false, -Inf, Inf);
                    _walkOffset = from;
                }
            }
    }

    // a wall with a corner moved since the last game frame: it is drawn between the two, not where it is now, so what it lets
    // through can't be measured from here. Its sector needn't be moving (E3M5's door drags the corners of the sector round it:
    // measured from where they are now, a strip was cut from the room behind it, wider the earlier in the blend)
    static bool CornersMoved(World world, int w)
    {
        var b = world.Board;
        if (b == null || w < 0 || w >= b.Walls.Length)
            return false;
        int w2 = b.Walls[w].Point2;
        return b.Walls[w].X != b.PrevWallX[w] || b.Walls[w].Y != b.PrevWallY[w] || b.Walls[w2].X != b.PrevWallX[w2] || b.Walls[w2].Y != b.PrevWallY[w2];
    }

    // a sector moving whole (its walls aren't where they are stored)
    static bool Moving(World world, SectorMap map, int s) => map.Moving?.Invoke(s) ?? false;

    // the opening between sectors s and n along a wall shut at both ends (Quake z up); between two sky ceilings (floors)
    // Build draws no upper (lower) wall, the opening runs on into the sky (DWE3M1's trees on the bank over the river's
    // sky were cut at it, their ground higher than the river's sky ceiling)
    static bool Shut(World world, int s, int n, Vector2 p1, Vector2 p2)
    {
        var map = world.Map;
        bool skyUp = map.CeilSky(s) && map.CeilSky(n), skyDown = map.FloorSky(s) && map.FloorSky(n);
        if (skyUp && skyDown)
            return false;
        bool ShutAt(Vector2 q) => (skyUp ? float.MaxValue : Mathf.Min(world.SectorCeilZ(s, q), world.SectorCeilZ(n, q)))
            - (skyDown ? float.MinValue : Mathf.Max(world.SectorFloorZ(s, q), world.SectorFloorZ(n, q))) <= 0;
        return ShutAt(p1) && ShutAt(p2);
    }

    static float Wrap(float a)
    {
        while (a > Mathf.Pi)
            a -= Mathf.Tau;
        while (a < -Mathf.Pi)
            a += Mathf.Tau;
        return a;
    }

    static float DistToSegmentSq(Vector2 p, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        float l = ab.LengthSquared();
        float t = l > 0 ? Mathf.Clamp((p - a).Dot(ab) / l, 0, 1) : 0;
        return (a + ab * t - p).LengthSquared();
    }

    // each sector's partners: the sectors sharing ground with it and those stacked over or under it
    static void Partners(SectorMap map)
    {
        if (_partnersOf == map && _partners != null)
            return;
        _partnersOf = map;
        _partnerFreeStale = true;
        _partners = new List<int>[map.SectorCount];
        _apart.Clear();
        _overlapOnly.Clear();
        for (int i = 0; i < _partners.Length; i++)
        {
            _partners[i] = new List<int>(map.OverlapsOf(i));
            foreach (int o in _partners[i])
            {
                if (Apart(map, i, o) || Apart(map, o, i))
                    _apart.Add((i, o));
                _overlapOnly.Add((i, o));
            }
        }
        foreach (var (lo, hi) in map.Stacks)
            if (map.Valid(lo) && map.Valid(hi))
            {
                _partners[lo].Add(hi);
                _partners[hi].Add(lo);
                _overlapOnly.Remove((lo, hi));
                _overlapOnly.Remove((hi, lo));
            }
    }

    // sector a's ceiling below sector b's floor, as stored (each at its first wall's corner)
    static bool Apart(SectorMap map, int a, int b)
    {
        if (map.SectorWalls(a).Count == 0 || map.SectorWalls(b).Count == 0)
            return false;
        var pa = map.WallP1(map.SectorWalls(a)[0]);
        var pb = map.WallP1(map.SectorWalls(b)[0]);
        return map.CeilZ(a, pa.X, pa.Y) < map.FloorZ(b, pb.X, pb.Y) - 1;
    }

    /// <summary>The wall stacks (PortalRenderer.WallPortals): a sector, the room its window shows, the view's shift
    /// (Quake x, y). The walk goes on into that room as through a link.</summary>
    public static List<(int sector, int far, Vector2 offset)> WallLinks
    {
        get => _wallLinks;
        set
        {
            _wallLinks = value ?? new();
            _linkFar = null;
        }
    }
    static List<(int sector, int far, Vector2 offset)> _wallLinks = new();

    // each linked sector's rooms beyond (room over room, water; a wall stack's window)
    static void LinkFar(World world)
    {
        var links = world.Collision.Links;
        if (_linkFar != null && _linkCount == links.Count && _linkMap == world.Map)
            return;
        _linkCount = links.Count;
        _linkMap = world.Map;
        _partnerFreeStale = true;
        _linkFar = new Dictionary<int, List<(int, Vector2)>>();
        _linkPairs.Clear();
        foreach (var l in links)
            if (l.Sector >= 0 && l.FarSector >= 0)
            {
                if (!_linkFar.TryGetValue(l.Sector, out var far))
                    _linkFar[l.Sector] = far = new List<(int, Vector2)>();
                far.Add((l.FarSector, new Vector2(l.Offset.X, l.Offset.Y)));
                _linkPairs.Add((l.Sector, l.FarSector));
                _linkPairs.Add((l.FarSector, l.Sector));
            }
        foreach (var (s, f, off) in _wallLinks)
            if (s >= 0 && f >= 0)
            {
                if (!_linkFar.TryGetValue(s, out var far))
                    _linkFar[s] = far = new List<(int, Vector2)>();
                far.Add((f, off));
            }
    }
}
