using System;
using System.Collections.Generic;
using Godot;
using Chosen.Shared.Blood.Map;

namespace Chosen.Sim;

/// <summary>
/// Build's sector of every sprite, kept the way Build keeps it (a sprite's sectnum): the same while its sector's
/// outline still holds it, whatever else shares the ground there (rooms over rooms, Build's impossible space), else the
/// sector beyond the two-sided walls its move crossed (clipmove), else a neighbour, else any sector holding it
/// (updatesector); a link moves it to the sector beyond (CheckLink), a teleport looks it up afresh. A level's Build
/// sectors are its SectorMap (maps/&lt;map&gt;.shade.json), their heights as the movers have them now. The camera's
/// sector is its player's (Blood's viewDrawScreen), which the renderer's walk starts from (Render.StackedRooms).
/// Without a SectorMap (a level with no Build sectors) the sector volumes answer as before (SectorAtPoint).
/// </summary>
public sealed partial class World
{
    /// <summary>The level's Build sectors, or null (a level with no Build sectors).</summary>
    public SectorMap Map { get; private set; }

    // per Build sector (0-based): its x-object (the sector's events and effects: Blood's xsector), its moving floor
    // and ceiling, and the sliding or turning mover that carries it whole
    Sector[] _xsector = Array.Empty<Sector>();
    Sector[] _floorMover = Array.Empty<Sector>(), _ceilMover = Array.Empty<Sector>(), _wholeBy = Array.Empty<Sector>();

    /// <summary>After the sectors and movers are spawned: each Build sector's x-object and movers.</summary>
    void IndexMap()
    {
        int n = Map?.SectorCount ?? 0;
        _xsector = new Sector[n];
        _floorMover = new Sector[n];
        _ceilMover = new Sector[n];
        _wholeBy = new Sector[n];
        if (Map == null)
            return;
        foreach (var s in Sectors)
        {
            if (s.BSector <= 0 || s.BSector > n)
                continue;
            int b = s.BSector - 1;
            if (_xsector[b] == null || XRank(s) < XRank(_xsector[b]))
                _xsector[b] = s;
        }
        foreach (var m in _wholeMovers)
            _wholeBy[m.BSector - 1] = m;
        IndexClip();
        _sectSprites.Clear();
        _sectSpritesFrame = -1;
        // the renderer's lookups find a sliding or turning sector where its mover has it now
        Map.Moving = IsWholeMover;
        Map.MovingAt = (p, layer) => WholeMoverAt(p, (sec, on) => Map.HoldsStored(sec, on, layer));
    }

    /// <summary>Which of a Build sector's blood_sector entities is its x-object: its volume, else a z-motion sector's
    /// mover standing for it (the first part, which sends its events), a water mover, any other mover.</summary>
    int XRank(Sector s) =>
        !s.IsMover ? 0 : s.Twin != null && s.ZPart == "floor" ? 4 : IsZMotionVolume(s) ? 1 : IsWaterMover(s) ? 2 : 3;

    /// <summary>Blood's xsector of a Build sector (0-based), or null.</summary>
    public Sector XSectorOf(int b) => b >= 0 && b < _xsector.Length ? _xsector[b] : null;

    // ---------------------------------------------------------------- heights and outlines now
    /// <summary>Build sector b's floor height (Quake z) at q as it is now (the board's planes: SectorMap.Refresh).</summary>
    public float SectorFloorZ(int b, Vector2 q) => Map.FloorZ(b, q.X, q.Y);

    public float SectorCeilZ(int b, Vector2 q) => Map.CeilZ(b, q.X, q.Y);

    /// <summary>Does Build sector b hold p's xy as it is now (Build's inside)? at: p.</summary>
    public bool InSectorNow(int b, Vector3 p, out Vector3 at)
    {
        at = p;
        return Map.InSector(b, p.X, p.Y);
    }

    // how far past its floor or ceiling a sprite still counts as in a sector (one resting on it, sunk a little)

    // ---------------------------------------------------------------- sprites
    /// <summary>A sprite's Build sector (0-based; -1: none known, or no Build sectors), followed as it moves.</summary>
    public int SpriteBSector(Sprite e)
    {
        if (Map == null)
            return -1;
        int was = e.SectKnown ? e.Sect : -1;
        if (!e.SectKnown)
        {
            e.SectKnown = true;
            e.Sect = InitialSector(e);
            e.SectAt = e.Origin;
        }
        else if (e.SectAt != e.Origin)
        {
            e.Sect = UpdateSector(e.Sect, e.SectAt, e.Origin, e.Origin.Z + e.Mins.Z, e.Origin.Z + e.Maxs.Z);
            e.SectAt = e.Origin;
            // a level from its Build data: moved out of every sector, it stays in its own (Blood ignores updatesector's
            // -1), never left to the brushes (there are none)
            if (e.Sect < 0 && was >= 0)
                e.Sect = was;
        }
        if (e.Sect != was)
            SectSpriteMoved(e, was, e.Sect);
        return e.Sect;
    }

    /// <summary>A sprite's Build sector as SpriteBSector would give it, without keeping it (the renderer's shading:
    /// asking at other times than the game would could walk a different way and change the game). lookUp: a sprite the
    /// game hasn't placed yet (no map sector) is looked for over every sector; false: -1.</summary>
    public int PeekSpriteBSector(Sprite e, bool lookUp = true)
    {
        if (Map == null)
            return -1;
        if (e.SectKnown)
        {
            if (e.SectAt == e.Origin || e.Sect < 0)
                return e.Sect;
            int now = UpdateSector(e.Sect, e.SectAt, e.Origin, e.Origin.Z + e.Mins.Z, e.Origin.Z + e.Maxs.Z);
            return now < 0 ? e.Sect : now;
        }
        if (e.BSector > 0 && Map.Valid(e.BSector - 1))
            return e.BSector - 1;
        return lookUp ? FindSectorZ(-1, e.Origin, e.Origin.Z + e.Mins.Z, e.Origin.Z + e.Maxs.Z) : -1;
    }

    /// <summary>The sector's shade a sprite in it adds (viewProcessSprites: its ceiling's under a sky, else its floor's,
    /// with the surface tile's shade) and its light effect now; null without one known.</summary>
    public int? SectorSpriteShade(int b)
    {
        if (Map == null || !Map.Valid(b) || Map.SpriteShade(b) == int.MinValue)
            return null;
        return Map.SpriteShade(b) + FxSpriteShadeOf(b);
    }

    /// <summary>The sector holding point to, reached from sector from at point at (the walls crossed between them, as a
    /// move would): the eye's from its player's (bob, interpolation can put it a few units over a wall).</summary>
    public int SectorFrom(int from, Vector3 at, Vector3 to)
    {
        if (Map == null || from < 0)
            return from;
        // through the red walls the line from at to to crosses, into the sector holding to: where rooms share a place
        // (two sectors over the same ground at the same heights), the first neighbour holding the point can be the
        // other room (the eye a few units behind the player as it steps back over a wall: one frame walked from the
        // wrong room, a flicker; report 20261103-125459)
        if (Board is { } b && Board.Valid(from))
        {
            double ax = Q2BX(at), ay = Q2BY(at), bx = Q2BX(to), by = Q2BY(to), bz = Q2BZ(to);
            int cur = from, came = -1;
            for (int step = 0; step < 8; step++)
            {
                if (b.InsideZ(bx, by, bz, cur))
                    return cur;
                int next = -1;
                foreach (int w in b.SectorWalls(cur))
                {
                    var wall = b.Walls[w];
                    if (wall.NextSector < 0 || w == came)
                        continue;
                    var p2 = b.Walls[wall.Point2];
                    if (Crosses(ax, ay, bx, by, wall.X, wall.Y, p2.X, p2.Y))
                    {
                        next = wall.NextSector;
                        came = wall.NextWall;
                        break;
                    }
                }
                if (next < 0)
                    break;
                cur = next;
            }
        }
        return UpdateSector(from, at, to, to.Z, to.Z);
    }

    /// <summary>Does the line (ax, ay)-(bx, by) cross the segment (cx, cy)-(dx, dy)?</summary>
    static bool Crosses(double ax, double ay, double bx, double by, double cx, double cy, double dx, double dy)
    {
        static double Side(double ox, double oy, double px, double py, double qx, double qy) => (px - ox) * (qy - oy) - (py - oy) * (qx - ox);
        double s1 = Side(cx, cy, dx, dy, ax, ay), s2 = Side(cx, cy, dx, dy, bx, by);
        double s3 = Side(ax, ay, bx, by, cx, cy), s4 = Side(ax, ay, bx, by, dx, dy);
        return (s1 > 0) != (s2 > 0) && (s3 > 0) != (s4 > 0);
    }

    /// <summary>Puts a sprite in a Build sector (ChangeSpriteSect: a link's far side).</summary>
    public void SetSpriteBSector(Sprite e, int b)
    {
        if (Map == null || !Map.Valid(b))
            return;
        int was = e.SectKnown ? e.Sect : -1;
        e.SectKnown = true;
        e.Sect = b;
        e.SectAt = e.Origin;
        SectSpriteMoved(e, was, b);
    }

    /// <summary>Where a sprite starts: the map's sector for a map sprite; one made during play from its owner's,
    /// walked over to where it is; else the sector whose column holds it.</summary>
    int InitialSector(Sprite e)
    {
        if (e.BSector > 0 && Map.Valid(e.BSector - 1))
            return e.BSector - 1;
        float lo = e.Origin.Z + e.Mins.Z, hi = e.Origin.Z + e.Maxs.Z;
        if (e.Owner != null && e.Owner != e && !e.Owner.Removed && e.Owner.SectKnown && e.Owner.Sect >= 0)
        {
            int from = SpriteBSector(e.Owner);
            if (from >= 0)
                return UpdateSector(from, e.Owner.Origin, e.Origin, lo, hi);
        }
        int found = FindSectorZ(-1, e.Origin, lo, hi);
        // a level from its Build data: a sprite always has a sector in Blood; one put outside them all (a teleport out
        // of bounds: a bug spot) takes the nearest
        return found >= 0 ? found : Map.NearestSector(e.Origin.X, e.Origin.Y);
    }

    /// <summary>
    /// updatesector as Blood's clipmove keeps a sprite's: cur while its outline holds to (unless to is out of its column
    /// and a sector sharing the ground there holds it: a flying camera or a noclip player going down into the room
    /// below); else beyond the two-sided walls the move from-to crossed; else a neighbour holding it; else any.
    /// lo..hi: the sprite's span. Stays cur when nothing holds it (Build: -1, the caller keeping the last).
    /// </summary>
    int UpdateSector(int cur, Vector3 from, Vector3 to, float lo, float hi)
    {
        // Build's updatesectorz from the sector it was in (its column holding z), else updatesector
        double bx = Q2BX(to), by = Q2BY(to), bz = Q2BZ(to);
        // still within its sector's outline but past its linked floor (or ceiling): Blood keeps it there (its sector
        // changes only as it moves across walls) until CheckLink takes it to the far room. A room right under the link
        // (Forge's Add Floor) took it first, by height, and the link never moved it: no water (Forge report
        // 20261105-151058)
        if (cur >= 0 && cur < _upperLink.Length && (_upperLink[cur] != null || _lowerLink[cur] != null) && Board.Inside(bx, by, cur))
        {
            // (the planes as CheckLink takes them)
            if (_upperLink[cur] is { } up && bz >= (up.Type == Chosen.Data.K.kMarkerUpLink ? up.BZ : Board.FloorZAt(cur, bx, by)))
                return cur;
            if (_lowerLink[cur] is { } low && bz <= (low.Type == Chosen.Data.K.kMarkerLowLink ? low.BZ : Board.CeilZAt(cur, bx, by)))
                return cur;
        }
        int s = Board.UpdateSectorZ(bx, by, bz, cur);
        if (s >= 0)
            return s;
        s = Board.UpdateSector(bx, by, cur);
        return s >= 0 ? s : cur;
    }

    /// <summary>
    /// The sector holding p whose column reaches lo..hi (updatesectorz over every sector): a whole moving sector where
    /// its mover has it now first, then of the others the highest floor under p (rooms over rooms); any holding p's xy
    /// when none reaches the span; -1 if none. prefer: kept when it is one of them.
    /// </summary>
    public int FindSectorZ(int prefer, Vector3 p, float lo, float hi)
    {
        if (Map == null)
            return -1;
        double bx = Q2BX(p), by = Q2BY(p), bz = Q2BZ(p);
        int s = Board.UpdateSectorZ(bx, by, bz, prefer);
        return s >= 0 ? s : Board.UpdateSector(bx, by, prefer);
    }

    /// <summary>
    /// Blood's FindSector(x, y, z, &amp;nSector): the sector holding the point and its z, looked for in start first, then
    /// its neighbours, then every sector; -1 if none does (the probe of a dude's move finds no room there).
    /// </summary>
    public int FindSector(int start, Vector3 p)
    {
        return FindSectorB(Q2BX(p), Q2BY(p), Q2BZ(p), start);
    }

    /// <summary>The sector a link moves a sprite into (CheckLink: the other marker's), where it is now.</summary>
    int LinkFarSector(Collision.Link l, Sprite e)
    {
        float lo = e.Origin.Z + e.Mins.Z, hi = e.Origin.Z + e.Maxs.Z;
        if (Map != null && Map.Valid(l.FarSector))
            return UpdateSector(l.FarSector, e.Origin, e.Origin, lo, hi);
        return FindSectorZ(-1, e.Origin, lo, hi);
    }
}
