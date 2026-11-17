using Chosen.Core;
using Chosen.Data;
using Chosen.Sim;
using Chosen.Sim.Collision;
using Godot;

namespace Chosen.Client;

/// <summary>
/// Places the camera at the player's eye like Blood3D's client (view.qc): the interpolated body position plus the
/// view height (zView, with its own inertia), the bob, and Blood's look shear drawn as the pitch that keeps the aim
/// centred (atan(horiz / 128)) with the eye raised by horiz * 10.
/// </summary>
public static class PlayerView
{
    static readonly Cvar VBob = Cvars.Register("b3d_vbob", "1", "vertical view bob", saved: true);
    static readonly Cvar HBob = Cvars.Register("b3d_hbob", "1", "horizontal view bob", saved: true);
    static readonly Cvar SlopeLook = Cvars.Register("b3d_slopelook", "0", "1: the view pitches up/down slopes like Blood", saved: true);

    // the view's own random numbers (the client's, not the game's)
    static readonly System.Random Rand = new();
    static double _screenTilt;
    /// <summary>This frame's shake of the weapon (Build units; the HUD adds it to the sway).</summary>
    public static double ShakeBobX, ShakeBobY;

    static double Random2(double n) => n <= 0 ? 0 : Rand.Next(-(int)n, (int)n);

    /// <summary>viewUpdateShake: explosions (quakeEffect) and the flicker effect shake the view and the weapon.</summary>
    static (double horiz, double angle, double x, double y, double z) Shake(Player p)
    {
        double h = 0, a = 0, x = 0, y = 0, z = 0;
        ShakeBobX = ShakeBobY = 0;
        foreach (var effect in new[] { p.FlickerEffect, p.QuakeEffect })
        {
            if (effect == 0)
                continue;
            double n = Blood.ClipHigh(effect * 8, 2000);
            h += Random2(Blood.Sar(n, 8));
            a += Random2(Blood.Sar(n, 8));
            x += Random2(Blood.Sar(n, 4));
            y += Random2(Blood.Sar(n, 4));
            z += Random2(n);
            ShakeBobX += Random2(n);
            ShakeBobY += Random2(n);
        }
        return (h, a, x, y, z);
    }

    /// <summary>deliriumUpdate: the delirium shroom rocks the view; afterwards it eases back level.</summary>
    static (double turn, double pitch) Delirium(Player p, double clock)
    {
        double n = p.PwUpTime[K.kPwUpDeliriumShroom];
        if (n > 0)
        {
            double tilt1 = 170, tilt2 = 170, pitch = 20;
            double timer = System.Math.Floor(clock) * 2;
            if (n < 512)
            {
                double scl = n / 512;
                tilt1 *= scl;
                tilt2 *= scl;
                pitch *= scl;
            }
            double s2 = Blood.Cos(2 * timer - 512) / 2, s3 = Blood.Cos(3 * timer - 512) / 2;
            double s4 = Blood.Cos(4 * timer - 512) / 2, s5 = Blood.Cos(5 * timer - 512) / 2;
            _screenTilt = Blood.MulScale30(s2 + s3, tilt1);
            return (Blood.MulScale30(s3 + s4, tilt2), Blood.MulScale30(s4 + s5, pitch));
        }
        _screenTilt = _screenTilt > 0 ? System.Math.Max(_screenTilt - 8, 0) : System.Math.Min(_screenTilt + 8, 0);
        return (0, 0);
    }

    // ------------------------------------------------------------------ the eye and room-over-room links (view.qc, links.qc)
    const float LinkViewBand = 72f;       // how far beyond a link plane an eye still counts as in the other room
    public const float NearClip = 0.05f;  // the camera's near plane (Godot units; 1.6 Quake units)
    const float LinkNearClip = 0.03f / Coords.QuakeScale;   // r_nearclip 0.03 close to a link plane
    static bool _viewClamped;

    /// <summary>viewClampHeight: the eye stays half a unit under the ceiling (over the floor) of the body's room, unless
    /// a room-over-room link continues there: standing up under a low ceiling does not put the eye above it.</summary>
    static Vector3 ViewClampHeight(World w, Vector3 cam, Vector3 body)
    {
        _viewClamped = false;
        // viewDrawScreen: the eye a unit's sixteenth (256) inside its sector's floor and ceiling, unless a link
        // continues there
        int ns = w.SectorOfPointB(cam.X * 16, -cam.Y * 16, -body.Z * 256, w.Me != null ? w.PeekSpriteBSector(w.Me, false) : -1);
        if (ns < 0)
            return cam;
        w.Board.GetZsOfSlope(ns, cam.X * 16, -cam.Y * 16, out double cz, out double fz);
        double z = -cam.Z * 256;
        if (z > fz - 256 && w.UpperLinkOf(ns) == null)
        {
            z = fz - 256;
            _viewClamped = true;
        }
        if (z < cz + 256 && w.LowerLinkOf(ns) == null)
        {
            z = cz + 256;
            _viewClamped = true;
        }
        cam.Z = (float)(-z / 256);
        return cam;
    }

    /// <summary>
    /// linkViewOrigin: Blood moves a sprite across a link when its feet cross the plane, so a player rising into a
    /// ceiling link has the eye beyond the plane for a moment (and one that fell through a floor link has it above
    /// the lower room's ceiling): an eye just beyond a plane, inside its footprint, with the body on the near side,
    /// views from the other room. feet: the body's latest position (the interpolated one can lag behind a crossing).
    /// With Build sectors, Blood's viewDrawScreen: the eye past a link plane of the body's sector views from the room
    /// beyond, wherever the feet are (E3M3's flooded stairs: a swimmer the step lifted above the water plane, not yet
    /// moved up through it, had the eye drawn in the void over the water for a frame).
    /// </summary>
    static Vector3 LinkViewOrigin(World w, Player p, Vector3 eye, Vector3 feet)
    {
        EyeLink = null;
        int bs = w.Map != null ? w.PeekSpriteBSector(p, false) : -1;
        bool known = false;
        if (bs >= 0)
            foreach (var l in w.Collision.Links)
            {
                if (l.Sector < 0)
                    continue;
                known = true;
                if (l.Sector != bs)
                    continue;
                if (l.Dir < 0 ? eye.Z > l.Plane : eye.Z < l.Plane)
                {
                    EyeLink = l;
                    return eye + l.Offset;
                }
            }
        if (known)
            return eye;
        foreach (var l in w.Collision.Links)
        {
            bool beyond = l.Dir < 0
                ? eye.Z > l.Plane && eye.Z < l.Plane + LinkViewBand && feet.Z < l.Plane
                : eye.Z < l.Plane && eye.Z > l.Plane - LinkViewBand && feet.Z > l.Plane;
            if (beyond && CollisionWorld.LinkContains(l, eye) && (CollisionWorld.LinkContains(l, feet) || InSiblingLink(w, l, feet)))
            {
                EyeLink = l;
                return eye + l.Offset;
            }
        }
        return eye;
    }

    /// <summary>Is p in another side of the same surface as l (its plane, its way, its offset)? A surface split into one
    /// link per sector (E3M3's flooded stairs, a link a step): the eye, interpolated, trails the body by a frame's move,
    /// and at a step's edge the two were over different links: no view from the room above, the void past the plane
    /// drawn for a frame.</summary>
    static bool InSiblingLink(World w, Sim.Collision.Link l, Vector3 p)
    {
        foreach (var o in w.Collision.Links)
            if (o != l && o.Dir == l.Dir && Mathf.Abs(o.Plane - l.Plane) < 0.5f
                && (o.Offset - l.Offset).LengthSquared() < 1f && CollisionWorld.LinkContains(o, p))
                return true;
        return false;
    }

    /// <summary>The link the last view looked through (its eye past the plane), or null.</summary>
    public static Sim.Collision.Link EyeLink;

    /// <summary>The view's Build sector (Blood's viewDrawScreen: the player sprite's sectnum, then CheckLink to the
    /// sector beyond a link the eye is past), -1 if none known.</summary>
    public static int EyeSector(World w, Player p)
    {
        int s = w.PeekSpriteBSector(p, false);
        if (EyeLink != null && w.Map != null && w.Map.Valid(EyeLink.FarSector))
            return EyeLink.FarSector;
        // where the eye is drawn (interpolated, bobbed): from the player's sector across the walls between them
        return w.SectorFrom(s, p.Origin, Eye);
    }

    /// <summary>The last view's eye (Quake units).</summary>
    public static Vector3 Eye;

    /// <summary>linkViewNear: the near plane would cut through a portal the eye is about to pass (or a ceiling the
    /// eye is held under): close to a link plane clip much nearer, and keep the eye a hair (0.2) off the plane.</summary>
    static Vector3 LinkViewNear(World w, Vector3 eye, out float near)
    {
        float nearest = 999;
        foreach (var l in w.Collision.Links)
        {
            float d = eye.Z - l.Plane;
            if (Mathf.Abs(d) >= 4 || Mathf.Abs(d) >= Mathf.Abs(nearest) || !CollisionWorld.LinkContains(l, eye))
                continue;
            nearest = d;
        }
        near = nearest != 999 || _viewClamped ? LinkNearClip : NearClip;
        if (nearest != 999 && Mathf.Abs(nearest) < 0.2f)
            eye.Z += nearest < 0 ? -(0.2f + nearest) : 0.2f - nearest;
        return eye;
    }

    /// <summary>clock: the game's 120 Hz clock, interpolated.</summary>
    public static void Apply(Camera3D cam, World w, Player p, PlayerControls c, float lerp, double clock)
    {
        var o = w.Collision.Interpolate(p.PrevOrigin, p.Origin, lerp);
        double zRel = Mathf.Lerp((float)(p.PrevZView - p.PrevBZ), (float)(p.ZView - p.BZ), lerp);
        double cZ = -o.Z * Blood.ZScale + zRel;
        double bobH = Mathf.Lerp((float)p.PrevBobHeight, (float)p.BobHeight, lerp);
        double bobW = Mathf.Lerp((float)p.PrevBobWidth, (float)p.BobWidth, lerp);
        double yaw, horiz;
        if (p.XHealth > 0 && p.Spin >= 0)
        {
            yaw = c.Yaw;
            double pitch = c.Pitch > 180 ? c.Pitch - 360 : c.Pitch;
            double look = Blood.ClipRange(-pitch * 2048 / 360, -347, 289);
            horiz = 100 * System.Math.Tan(look * System.Math.PI / 1024);
        }
        else
        {
            yaw = -p.Ang * 360.0 / 2048;
            horiz = p.Horiz;
        }
        double cA = Blood.AngMask(-yaw * 2048 / 360);
        var shake = Shake(p);
        horiz += shake.horiz;
        cA = Blood.AngMask(cA + shake.angle);
        var delirium = Delirium(p, clock);
        cA = Blood.AngMask(cA + delirium.turn);
        horiz += delirium.pitch;
        double cX = o.X * Blood.XYScale + shake.x, cY = -o.Y * Blood.XYScale + shake.y;
        cZ += shake.z;
        horiz += Blood.MulScale30(1073741824 - Blood.Cos(p.TiltEffect * 4), 30);
        if (HBob.Bool)
        {
            cX -= Blood.Sar(Blood.MulScale30(bobW, Blood.Sin(cA)), 4);
            cY += Blood.Sar(Blood.MulScale30(bobW, Blood.Cos(cA)), 4);
        }
        if (VBob.Bool)
            cZ += bobH;
        if (SlopeLook.Bool)
            horiz += p.SlopeHoriz;
        cZ += horiz * 10;
        var eye = new Vector3((float)(cX / Blood.XYScale), (float)(-cY / Blood.XYScale), (float)(-cZ / Blood.ZScale));
        eye = ViewClampHeight(w, eye, o);
        // an eye past a room-over-room plane the body has not crossed yet views from the other room
        eye = LinkViewNear(w, LinkViewOrigin(w, p, eye, p.Origin), out float near);
        cam.Near = near;
        Eye = eye;
        float camPitch = -Mathf.RadToDeg(Mathf.Atan2((float)horiz, 128f));
        float camYaw = (float)(-cA * 360 / 2048);
        var g = Coords.QuakeToGodot(eye);
        cam.GlobalPosition = g;
        cam.LookAt(g + Coords.QuakeAnglesToGodotDir(camPitch, camYaw), Vector3.Up);
        if (p.XHealth <= 0)
            cam.RotateObjectLocal(Vector3.Forward, Mathf.DegToRad(Mathf.Min((float)p.DeathTime, 120) / 4));
        else if (_screenTilt != 0)
            cam.RotateObjectLocal(Vector3.Forward, Mathf.DegToRad((float)(_screenTilt * 360 / 2048)));
    }
}
