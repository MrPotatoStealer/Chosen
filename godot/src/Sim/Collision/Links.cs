using System;
using System.Collections.Generic;
using Godot;

namespace Chosen.Sim.Collision;

/// <summary>
/// One side of a room-over-room link (Blood's link markers): its sector's footprint at the plane. dir 1: the floor of the room above the plane; -1: the ceiling of the room below. Things
/// crossing the plane inside the footprint move by Offset into the other room.
/// </summary>
public sealed class Link
{
    public int Dir;
    public float Plane;          // plane z (Quake)
    public float PrevPlane;      // at the previous frame's checks (a plane on a mover moves before things do)
    public Vector3 Offset;       // added to a position crossing this side
    public int Type;             // Blood marker type of the side (kMarkerUpLink, kMarkerUpWater, ...)
    public int Id;
    public bool Portal;          // the client draws the other room on it
    public bool ConvertedPortal; // a portal as converted (Blood's tile 504); Portal also when the client draws water through
    public int Sector = -1, FarSector = -1;   // its Build sector and the one beyond (-1: unknown; a level converted before)
    // a side on a moving floor/ceiling follows it, as Blood's CheckLink uses the sector's planes as they are now
    // (World.UpdateLinkPlanes): that z-mover, the other side, and Plane and Offset.Z as converted
    public Sector Mover;
    public Link Far;
    public float BasePlane, BaseOffsetZ;
    /// <summary>Its marker sprite (a level from its Build data: World.NativeLinks), else null.</summary>
    public Sprite Marker;
    public Aabb Bounds => CollisionWorld.NativeBounds?.Invoke(this) ?? default;

    /// <summary>The plane as converted (before a mover moved it), or plane when not known yet.</summary>
    public float BasePlaneOr(float plane) => BasePlane != 0 ? BasePlane : plane;
}

public sealed partial class CollisionWorld
{
    public readonly List<Link> Links = new();

    /// <summary>A level from its Build data: a link side's footprint is its sector as it is now (World.NativeLinks).</summary>
    public static System.Func<Link, Vector3, bool> NativeContains;
    public static System.Func<Link, Aabb> NativeBounds;

    /// <summary>linkContains: is (x, y) of p inside this side's footprint?</summary>
    public static bool LinkContains(Link l, Vector3 p) => NativeContains != null && NativeContains(l, p);

    /// <summary>The position to draw something at between two frames; a jump of more than 96 units snaps. Through a
    /// room-over-room link it keeps moving in the new room: the crossing moved the previous position too
    /// (World.CheckLinkCross).</summary>
    public Vector3 Interpolate(Vector3 prev, Vector3 cur, float lerp)
    {
        if (prev == cur || (cur - prev).Length() > 96)
            return cur;
        return prev.Lerp(cur, lerp);
    }

    /// <summary>linkAtPlane: the side whose plane a vertical trace stopped on (dir 1 going down onto a floor, -1 up).</summary>
    public Link LinkAtPlane(Vector3 p, int dir)
    {
        foreach (var l in Links)
        {
            if (l.Dir != dir)
                continue;
            // the plane, or the side's own surface (the floor or ceiling it lies on): a link marker can sit off the
            // floor (E2M7's pit, link 33: its marker a unit above), and Blood's GetZRange follows the link whatever
            float surf = dir > 0 ? l.Bounds.End.Z : l.Bounds.Position.Z;
            if (Mathf.Abs(p.Z - l.Plane) > 0.6f && Mathf.Abs(p.Z - surf) > 0.6f)
                continue;
            if (LinkContains(l, p))
                return l;
        }
        return null;
    }

    /// <summary>
    /// traceFollowLinks: a vertical box trace (dir 1 down, -1 up) that stopped on a link plane continues in the
    /// linked room (Blood's GetZRange follows gUpperLink/gLowerLink). Returns the offset of the room it ended in.
    /// </summary>
    public Vector3 TraceFollowLinks(ref TraceResult tr, Vector3 mins, Vector3 maxs, int dir, int mask, object self,
        HashSet<object> exclude = null)
    {
        var shift = Vector3.Zero;
        for (int i = 0; i < 3; i++)
        {
            if (tr.Fraction >= 1 || tr.StartSolid)
                break;
            var l = LinkAtPlane(tr.EndPos, dir);
            if (l == null)
                break;
            shift += l.Offset;
            // on in the other room from the plane (the trace may have stopped on the floor a little off it: a live
            // floor tilting under a bobbing link, E2M6's water strip; from there it would start above that room)
            var p = new Vector3(tr.EndPos.X, tr.EndPos.Y, l.Plane) + l.Offset;
            p.Z -= dir * 0.1f;
            tr = TraceZ(p, mins, maxs, -8192 * dir, mask, self, exclude);
            // a box reaching into a solid there (E2M6's underwater strip, its floor tilting up to its ceiling at the
            // edge): narrowed as GetZRange's own search is
            for (float s = 0.5f; s >= 0.03f && tr.StartSolid; s *= 0.5f)
                tr = TraceZ(p, mins * s, maxs * s, -8192 * dir, mask, self, exclude);
        }
        return shift;
    }

    /// <summary>linkClampBox: keep a box from reaching through a link plane (the part beyond is in the other room).</summary>
    public (Vector3 mn, Vector3 mx) LinkClampBox(Vector3 org, Vector3 mn, Vector3 mx)
    {
        foreach (var l in Links)
        {
            float rel = l.Plane - org.Z;
            if (l.Dir < 0)
            {
                // ceiling link above: the head goes up into the other room
                if (rel < 0 || rel >= mx.Z || !LinkContains(l, org))
                    continue;
                mx.Z = Mathf.Max(rel - 0.05f, mn.Z + 0.5f);
            }
            else
            {
                if (rel > 0 || rel <= mn.Z || !LinkContains(l, org))
                    continue;
                mn.Z = Mathf.Min(rel + 0.05f, mx.Z - 0.5f);
            }
        }
        return (mn, mx);
    }

    /// <summary>linkPathCross: the first link plane the origin crosses moving from start to end, inside its
    /// footprint; frac is where along the move the origin is a hair short of it.</summary>
    public Link LinkPathCross(Vector3 start, Vector3 end, out float frac)
    {
        Link found = null;
        frac = 1;
        foreach (var l in Links)
        {
            if (l.Dir > 0)
            {
                if (!(start.Z >= l.Plane && end.Z < l.Plane))
                    continue;
            }
            else if (!(start.Z <= l.Plane && end.Z > l.Plane))
                continue;
            float f = Mathf.Max(0, (start.Z - (l.Plane + l.Dir * 0.05f)) / (start.Z - end.Z));
            if (found != null && f >= frac)
                continue;
            if (!LinkContains(l, start + (end - start) * f))
                continue;
            found = l;
            frac = f;
        }
        return found;
    }
}
