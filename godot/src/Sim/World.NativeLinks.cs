using System.Collections.Generic;
using Chosen.Data;
using Chosen.Sim.Collision;
using Godot;

namespace Chosen.Sim;

/// <summary>
/// Room over room on a level from its Build data (warp.cpp): each sector's upper and lower link marker (gUpperLink,
/// gLowerLink: kMarkerUpLink / UpWater / UpStack / UpGoo on the floor of the room above, the Low ones on the ceiling of
/// the room below), paired by their data1; CheckLink moves a sprite past its sector's link plane into the other
/// marker's sector, shifted by the markers' difference. The pairs are also the level's links for the renderers
/// (Collision.Links: a floor and a ceiling side each, a portal where Build draws the far room, tile 504).
/// </summary>
public sealed partial class World
{
    Sprite[] _upperLink = System.Array.Empty<Sprite>(), _lowerLink = System.Array.Empty<Sprite>();
    readonly Dictionary<Sprite, Sprite> _linkOwner = new();

    public const int MirrorTile = 504;

    /// <summary>warpInit: the link markers by sector (water, stack and goo markers to their sector's plane), invisible,
    /// paired with the marker of the same data1; the renderers' link sides from the pairs.</summary>
    void NativeWarpInit()
    {
        var b = Board;
        _upperLink = new Sprite[b.SectorCount];
        _lowerLink = new Sprite[b.SectorCount];
        _linkOwner.Clear();
        foreach (var e in Sprites)
        {
            int s = SpriteBSector(e);
            if (s < 0 || e.NoX != 0)
                continue;
            switch (e.Type)
            {
                case K.kMarkerUpLink:
                    _upperLink[s] = e;
                    break;
                case K.kMarkerLowLink:
                    _lowerLink[s] = e;
                    break;
                case K.kMarkerUpWater:
                case K.kMarkerUpStack:
                case K.kMarkerUpGoo:
                    _upperLink[s] = e;
                    e.SetBZ(b.FloorZAt(s, e.BX, e.BY));
                    break;
                case K.kMarkerLowWater:
                case K.kMarkerLowStack:
                case K.kMarkerLowGoo:
                    _lowerLink[s] = e;
                    e.SetBZ(b.CeilZAt(s, e.BX, e.BY));
                    break;
                default:
                    continue;
            }
            e.Cstat |= Cstat.Invisible;
            e.Cstat &= ~(Cstat.Block | Cstat.Hitscan);
            e.Visible = false;
        }
        for (int i = 0; i < b.SectorCount; i++)
        {
            if (_upperLink[i] is not Sprite up)
                continue;
            for (int j = 0; j < b.SectorCount; j++)
                if (_lowerLink[j] is Sprite lo && lo.Data1 == up.Data1)
                {
                    _linkOwner[up] = lo;
                    _linkOwner[lo] = up;
                }
        }
        // the renderers' link sides: each pair a floor side (the room above) and a ceiling side (the room below)
        Collision.Links.Clear();
        for (int i = 0; i < b.SectorCount; i++)
        {
            if (_upperLink[i] is not Sprite up || !_linkOwner.TryGetValue(up, out var lo))
                continue;
            int j = SpriteBSector(lo);
            if (j < 0)
                continue;
            // Build draws the far room through a floor of tile 504 (InitMirrors: IsRorSector), the room below's ceiling
            // forced to it
            bool portal = b.Sectors[i].FloorPicnum == MirrorTile || (b.Sectors[i].FloorStat & 0x180) != 0;
            if (portal)
                b.Sectors[j].CeilingPicnum = MirrorTile;
            var ofs = lo.Origin - up.Origin;
            var upper = new Link
            {
                Dir = 1, Type = up.Type, Id = up.Data1, Sector = i, FarSector = j, Offset = ofs,
                Plane = up.Origin.Z, PrevPlane = up.Origin.Z, Portal = portal, ConvertedPortal = portal, Marker = up,
            };
            var lower = new Link
            {
                Dir = -1, Type = lo.Type, Id = lo.Data1, Sector = j, FarSector = i, Offset = -ofs,
                Plane = lo.Origin.Z, PrevPlane = lo.Origin.Z, Portal = portal, ConvertedPortal = portal, Marker = lo,
            };
            upper.Far = lower;
            lower.Far = upper;
            Collision.Links.Add(upper);
            Collision.Links.Add(lower);
        }
        CollisionWorld.NativeContains = (l, p) => Map.Valid(l.Sector) && Board.Inside(p.X * 16, -p.Y * 16, l.Sector);
        CollisionWorld.NativeBounds = l =>
        {
            var box = Map.SectorBox(l.Sector);
            return new Aabb(new Vector3(box.Position.X, box.Position.Y, l.Plane - 1), new Vector3(box.Size.X, box.Size.Y, 2));
        };
        NativeUpdateLinks();
    }

    /// <summary>The link sides' planes as they are now (a water, stack or goo link lies on its sector's plane, which may
    /// move: CheckLink takes the plane where the sprite is).</summary>
    void NativeUpdateLinks()
    {
        foreach (var l in Collision.Links)
        {
            l.PrevPlane = l.Plane;
            var m = l.Marker;
            if (m == null)
                continue;
            bool link = l.Type == K.kMarkerUpLink || l.Type == K.kMarkerLowLink;
            if (!link && Map.Valid(l.Sector))
                l.Plane = (float)(-(l.Dir > 0 ? Board.FloorZAt(l.Sector, m.BX, m.BY) : Board.CeilZAt(l.Sector, m.BX, m.BY)) / 256.0);
        }
    }

    /// <summary>CheckLink: a sprite at or past its sector's upper link plane (its marker's z, or the floor there for water,
    /// stack and goo) goes to the lower marker's sector, shifted by the markers' difference and to the far plane; past
    /// the lower link the other way. Returns the type of the marker crossed, 0 if none.</summary>
    int NativeCheckLink(Sprite e)
    {
        var b = Board;
        int nSector = SpriteBSector(e);
        if (nSector < 0)
            return 0;
        var up = _upperLink[nSector];
        var lo = _lowerLink[nSector];
        if (up != null && _linkOwner.TryGetValue(up, out var far1))
        {
            double z = up.Type == K.kMarkerUpLink ? up.BZ : b.FloorZAt(nSector, e.BX, e.BY);
            if (z <= e.BZ)
            {
                int dest = SpriteBSector(far1);
                if (dest < 0)
                    return 0;
                var old = e.Origin;
                double x = e.BX + far1.BX - up.BX, y = e.BY + far1.BY - up.BY;
                double z2 = far1.Type == K.kMarkerLowLink ? far1.BZ : b.CeilZAt(dest, x, y);
                MoveThroughLink(e, x, y, e.BZ + z2 - z, dest, old);
                return up.Type;
            }
        }
        if (lo != null && _linkOwner.TryGetValue(lo, out var far2))
        {
            double z = lo.Type == K.kMarkerLowLink ? lo.BZ : b.CeilZAt(nSector, e.BX, e.BY);
            if (z >= e.BZ)
            {
                int dest = SpriteBSector(far2);
                if (dest < 0)
                    return 0;
                var old = e.Origin;
                double x = e.BX + far2.BX - lo.BX, y = e.BY + far2.BY - lo.BY;
                double z2 = far2.Type == K.kMarkerUpLink ? far2.BZ : b.FloorZAt(dest, x, y);
                MoveThroughLink(e, x, y, e.BZ + z2 - z, dest, old);
                return lo.Type;
            }
        }
        return 0;
    }

    void MoveThroughLink(Sprite e, double x, double y, double z, int dest, Vector3 old)
    {
        e.Origin = B2Q(x, y, z);
        // the position the renderer blends from moves too (viewCorrectSpriteInterpolateOffsets)
        e.PrevOrigin += e.Origin - old;
        if (e is Player p)
        {
            double dz = Q2BZ(e.Origin) - Q2BZ(old);
            p.ZView += dz;
            p.ZWeapon += dz;
        }
        SetSpriteBSector(e, dest);
        UpdateBox(e);
    }

    /// <summary>The far side of a sector's link for a point (x, y, z Build) past its plane: the sector and the point
    /// moved there (CheckLink's int form, for traces and the camera); -1 if it has none crossed.</summary>
    public int NativeCheckLinkPoint(ref double x, ref double y, ref double z, int nSector)
    {
        var b = Board;
        if (b == null || nSector < 0 || nSector >= _upperLink.Length)
            return -1;
        var up = _upperLink[nSector];
        var lo = _lowerLink[nSector];
        if (up != null && _linkOwner.TryGetValue(up, out var far1))
        {
            double z1 = up.Type == K.kMarkerUpLink ? up.BZ : b.FloorZAt(nSector, x, y);
            if (z1 <= z)
            {
                int dest = SpriteBSector(far1);
                x += far1.BX - up.BX;
                y += far1.BY - up.BY;
                double z2 = up.Type == K.kMarkerLowLink ? far1.BZ : b.CeilZAt(dest, x, y);
                z += z2 - z1;
                return dest;
            }
        }
        if (lo != null && _linkOwner.TryGetValue(lo, out var far2))
        {
            double z1 = lo.Type == K.kMarkerLowLink ? lo.BZ : b.CeilZAt(nSector, x, y);
            if (z1 >= z)
            {
                int dest = SpriteBSector(far2);
                x += far2.BX - lo.BX;
                y += far2.BY - lo.BY;
                double z2 = lo.Type == K.kMarkerUpLink ? far2.BZ : b.FloorZAt(dest, x, y);
                z += z2 - z1;
                return dest;
            }
        }
        return -1;
    }

    /// <summary>gUpperLink / gLowerLink of a Build sector (null: none).</summary>
    public Sprite UpperLinkOf(int s) => s >= 0 && s < _upperLink.Length ? _upperLink[s] : null;
    public Sprite LowerLinkOf(int s) => s >= 0 && s < _lowerLink.Length ? _lowerLink[s] : null;
    public Sprite LinkOwnerOf(Sprite m) => m != null && _linkOwner.TryGetValue(m, out var o) ? o : null;
}
