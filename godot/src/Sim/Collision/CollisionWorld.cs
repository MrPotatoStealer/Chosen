using System.Collections.Generic;
using Godot;

namespace Chosen.Sim.Collision;

/// <summary>An axis-aligned box that takes part in traces (DarkPlaces' SOLID_SLIDEBOX/BBOX entities: actors, things).</summary>
public sealed class Body
{
    public object Owner;
    public Sprite Sprite;                 // when set, the box is the sprite's (origin, mins, maxs)
    Vector3 _origin, _mins, _maxs;
    public Vector3 Origin { get => Sprite != null ? Sprite.Origin : _origin; set => _origin = value; }
    public Vector3 Mins { get => Sprite != null ? Sprite.Mins : _mins; set => _mins = value; }
    public Vector3 Maxs { get => Sprite != null ? Sprite.Maxs : _maxs; set => _maxs = value; }
    public float Yaw;                     // degrees: the box turns about Origin (wall/floor sprites at odd angles)
    public int Contents = Collision.Contents.Body;
    public bool Enabled = true;

    /// <summary>Can a box in lo..hi (world) touch this body? A turned box is taken as the circle around its turn.</summary>
    public bool MayTouch(Vector3 lo, Vector3 hi)
    {
        var o = Origin;
        Vector3 mn = Mins, mx = Maxs;
        if (Yaw != 0f)
        {
            float r = Mathf.Sqrt(Mathf.Max(mn.X * mn.X, mx.X * mx.X) + Mathf.Max(mn.Y * mn.Y, mx.Y * mx.Y));
            mn = new Vector3(-r, -r, mn.Z);
            mx = new Vector3(r, r, mx.Z);
        }
        return !(lo.X > o.X + mx.X || lo.Y > o.Y + mx.Y || lo.Z > o.Z + mx.Z ||
                 hi.X < o.X + mn.X || hi.Y < o.Y + mn.Y || hi.Z < o.Z + mn.Z);
    }
}

/// <summary>
/// What box traces hit beside Build's walls and sectors (World.BuildClip, World.NativeTrace): the sprites' bodies.
/// tracebox(start, mins, maxs, end, mask, ignore) like DarkPlaces: the nearest hit of them.
/// </summary>
public sealed partial class CollisionWorld
{
    public const float ImpactNudge = 0.03125f;     // DarkPlaces' collision_impactnudge: keep a hair off a surface
    public readonly List<Body> Bodies = new();

    /// <summary>
    /// QuakeC's tracebox / traceline in DarkPlaces 2014 (SV_TraceBox): every body, each traced on its own and combined
    /// (Collision_CombineTraces). Each trace is made 1 unit longer (collision_extendtraceboxlength / tracelinelength)
    /// and scaled back, so a hit within impactnudge past the end still stops it.
    /// </summary>
    public TraceResult Trace(Vector3 start, Vector3 mins, Vector3 maxs, Vector3 end, int mask, object ignore = null,
        HashSet<object> exclude = null)
    {
        if (mins == maxs && mins != Vector3.Zero)
        {
            // SV_TraceBox: a box of no size is a line from the shifted start
            var shifted = Trace(start + mins, Vector3.Zero, Vector3.Zero, end + mins, mask, ignore, exclude);
            shifted.EndPos -= mins;
            return shifted;
        }
        bool point = mins == maxs && start == end;
        var delta = end - start;
        float len = delta.Length(), scale = 1f;
        var extEnd = end;
        if (len > 0)
        {
            scale = (len + 1f) / len;
            extEnd = start + delta * scale;
        }

        var clip = NewTrace();
        // the swept box: bodies whose box it misses are skipped before the full test
        var lo = start.Min(extEnd) + mins - Vector3.One;
        var hi = start.Max(extEnd) + maxs + Vector3.One;
        if ((mask & Contents.Sprites) != 0)
        {
            // like DarkPlaces: the ignored entity's owner and what it owns pass too (a missile and its shooter)
            var ignoreOwner = (ignore as Sprite)?.Owner;
            foreach (var b in Bodies)
            {
                if (!b.Enabled || (b.Contents & mask) == 0 || !b.MayTouch(lo, hi) || b.Owner == ignore ||
                    (exclude != null && exclude.Contains(b.Owner)))
                    continue;
                if (ignore != null && b.Sprite != null && (b.Sprite == ignoreOwner || b.Sprite.Owner == ignore))
                    continue;
                var t = NewTrace();
                TraceBody(ref t, b, start, mins, maxs, extEnd);
                Finish(ref t, scale, point);
                Combine(ref clip, t, b.Owner);
            }
        }
        clip.EndPos = start + delta * clip.Fraction;
        return clip;
    }

    static TraceResult NewTrace() => new() { Fraction = 1f, Texture = "" };

    /// <summary>Collision_ClipExtendFinish: undo the extension; a hit beyond the real end is no hit. A point
    /// inside something is all solid (Collision_TracePointBrushFloat).</summary>
    static void Finish(ref TraceResult t, float scale, bool point)
    {
        if (point && t.StartSolid)
            t.AllSolid = true;
        if (t.Fraction != 1f)
        {
            t.Fraction *= scale;
            if (t.Fraction > 1f)
            {
                t.Normal = Vector3.Zero;
                t.Contents = 0;
                t.Texture = "";
            }
        }
        t.Fraction = Mathf.Clamp(t.Fraction, 0f, 1f);
    }

    /// <summary>Collision_CombineTraces: the nearest hit wins (only a hit with a plane); a start inside anything
    /// makes the whole trace start (or be all) solid.</summary>
    static void Combine(ref TraceResult clip, in TraceResult t, object touch)
    {
        if (t.AllSolid)
            clip.AllSolid = true;
        if (t.StartSolid)
        {
            clip.StartSolid = true;
            if (clip.Fraction == 1f)
                clip.Entity = touch;
        }
        if (t.Fraction < clip.Fraction && t.Normal.LengthSquared() > 0f)
        {
            clip.Fraction = t.Fraction;
            clip.Normal = t.Normal;
            clip.Entity = touch;
            clip.Contents = t.Contents;
            clip.Texture = t.Texture;
        }
    }

    /// <summary>
    /// A trace straight up or down by dz (GetZRange's floor and ceiling: 8192). It tries the first 256 units first:
    /// a long trace tests every brush in the column, where the floor or ceiling is mostly near. The long one runs
    /// when the short one hits nothing or starts inside something (its end then matters too).
    /// </summary>
    public TraceResult TraceZ(Vector3 start, Vector3 mins, Vector3 maxs, float dz, int mask, object ignore = null,
        HashSet<object> exclude = null)
    {
        const float Near = 256f;
        if (Mathf.Abs(dz) > Near)
        {
            var near = Trace(start, mins, maxs, start + new Vector3(0, 0, Mathf.Sign(dz) * Near), mask, ignore, exclude);
            if (near.Fraction < 1f && !near.StartSolid)
                return near;
        }
        return Trace(start, mins, maxs, start + new Vector3(0, 0, dz), mask, ignore, exclude);
    }

    public TraceResult TraceLine(Vector3 start, Vector3 end, int mask, object ignore = null) =>
        Trace(start, Vector3.Zero, Vector3.Zero, end, mask, ignore);

    /// <summary>Is this trace result's entity a body (a sprite box), not the world or a brush entity?</summary>
    public bool IsBody(object ent) => ent is Sprite;

    /// <summary>
    /// A box against a body box: the body grows by the moving box (Minkowski sum) and the trace is a line, like
    /// DarkPlaces' Collision_ClipTrace_Box (Collision_TraceLineBrushFloat: touching a side is inside). A body turned
    /// by a yaw is traced as a brush (Collision_TraceBrushBrushFloat: touching is outside).
    /// </summary>
    static void TraceBody(ref TraceResult tr, Body b, Vector3 start, Vector3 mins, Vector3 maxs, Vector3 end)
    {
        if (b.Yaw != 0f)
        {
            // trace in the box's frame (the moving box keeps its axes, as DarkPlaces' boxangle does)
            float a = Mathf.DegToRad(b.Yaw), c = Mathf.Cos(a), s = Mathf.Sin(a);
            Vector3 ToBox(Vector3 w)
            {
                var d = w - b.Origin;
                return new Vector3(d.X * c + d.Y * s, -d.X * s + d.Y * c, d.Z);
            }
            float before = tr.Fraction;
            TraceBox(ref tr, b.Mins, b.Maxs, ToBox(start), mins, maxs, ToBox(end), mins != maxs, b.Contents);
            if (tr.Fraction < before)
            {
                var n = tr.Normal;
                tr.Normal = new Vector3(n.X * c - n.Y * s, n.X * s + n.Y * c, n.Z);
            }
            return;
        }
        TraceBox(ref tr, b.Origin + b.Mins, b.Origin + b.Maxs, start, mins, maxs, end, false, b.Contents);
    }

    /// <summary>The box (boxMins..boxMaxs) against the moving box: DarkPlaces' plane clipping  over the six sides of the grown box.</summary>
    static void TraceBox(ref TraceResult tr, Vector3 boxMins, Vector3 boxMaxs, Vector3 start, Vector3 mins, Vector3 maxs,
        Vector3 end, bool touchIsOutside, int contents)
    {
        var lo = boxMins - maxs;
        var hi = boxMaxs - mins;
        float enter = -1f, leave = 1f, enter2 = -1f;
        int hitAxis = -1;
        float hitSign = 0;
        for (int axis = 0; axis < 3; axis++)
        {
            for (int side = 0; side < 2; side++)
            {
                // plane: outward normal +axis at hi, -axis at lo
                float sgn = side == 0 ? 1f : -1f;
                float dist = side == 0 ? hi[axis] : -lo[axis];
                float d1 = sgn * start[axis] - dist, d2 = sgn * end[axis] - dist;
                bool outside = d1 > 0 || (touchIsOutside && d1 == 0);
                if (d1 > d2)
                {
                    if (d2 > 0)
                        return;
                    if (outside)
                    {
                        float imove = 1f / (d1 - d2), f = d1 * imove;
                        if (enter < f)
                        {
                            enter = f;
                            if (enter > leave)
                                return;
                            enter2 = (d1 - ImpactNudge) * imove;
                            if (enter2 >= tr.Fraction)
                                return;
                            hitAxis = axis;
                            hitSign = sgn;
                        }
                    }
                }
                else
                {
                    if (outside)
                        return;
                    if (d2 > 0)
                    {
                        float f = d1 / (d1 - d2);
                        if (leave > f)
                        {
                            leave = f;
                            if (enter > leave)
                                return;
                        }
                    }
                }
            }
        }
        if (enter > -1)
        {
            tr.Fraction = Mathf.Clamp(enter2, 0f, 1f);
            var n = Vector3.Zero;
            n[hitAxis] = hitSign;
            tr.Normal = n;
            tr.Contents = contents;
            tr.Texture = "";
        }
        else
        {
            tr.StartSolid = true;
            if (leave < 1)
                tr.AllSolid = true;
            tr.Normal = Vector3.Zero;
        }
    }
}
