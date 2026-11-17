// Chosen core: original code (what the 3D preview draws where, as Build draws a level: from the camera's sector
// through the openings it sees, each sector only within the part of the view its opening covers and behind it).
using System;
using System.Collections.Generic;
using System.Linq;
using Chosen.Forge.Document;
using Chosen.Shared.Engine.Geometry;
using Godot;

namespace Chosen.Forge.View;

public partial class Preview3D
{
    /// <summary>A sector's faces (one mesh: a surface a picture), its sprites.</summary>
    readonly Dictionary<int, MeshInstance3D> _sectorMesh = new();
    readonly List<(Node3D node, int sector)> _spriteNodes = new();

    /// <summary>The sector the camera is in (as the views found it this frame; -1: none). Play: the player's.</summary>
    public int CameraSectorNow { get; private set; } = -1;
    List<(int upper, int lower)> _links = new();

    /// <summary>
    /// The sectors seen and where (Build's portals): from the camera's sector (the one holding it whose floor and
    /// ceiling hold its height), through every red wall facing the camera whose opening (between the higher floor and
    /// the lower ceiling of its two sides; above two skies, open upward) shows on the screen within the opening it was
    /// reached through, into the sector behind; through a Blood link's plane (tile 504) into the linked room. Each
    /// sector seen is drawn only inside the screen rectangle of the openings it was seen through and behind the
    /// opening's plane (its faces' shader: clip_rect, clip_plane), the others not at all: a room over a room, a room
    /// in a room, shows where the game shows it. Outside every sector (flying through a wall): all drawn, unclipped.
    /// </summary>
    void Portals()
    {
        if (Doc == null || _sectorMesh.Count == 0)
            return;
        var size = _vp.Size;
        var full = new Rect2(0, 0, size.X, size.Y);
        var cam = _cam.GlobalPosition;
        double cx = cam.X * 16, cy = cam.Z * 16, cz = -cam.Y * 256;
        int start = CameraSector(cx, cy, cz);
        CameraSectorNow = start;
        // a sector's views: up to Views (a rectangle and a plane each: seen through that many openings apart)
        var seen = new Dictionary<int, List<(Rect2 rect, Plane? plane)>>();
        _seenSectors = null;
        if (start < 0)
        {
            foreach (var (s, mi) in _sectorMesh)
                Clip(mi, new List<(Rect2, Plane?)> { (full, null) });
            foreach (var (node, _) in _spriteNodes)
            {
                node.Visible = true;
                DarkenSprite(node);
            }
            return;
        }
        var queue = new Queue<(int s, Rect2 rect, Plane? plane)>();
        queue.Enqueue((start, full, null));
        var view = _cam.GlobalTransform.AffineInverse();
        // (a bound on the walk: a level's openings seen many times over)
        for (int steps = 0; queue.Count > 0 && steps < 20000; steps++)
        {
            var (s, rect, plane) = queue.Dequeue();
            if (!seen.TryGetValue(s, out var views))
                seen[s] = views = new List<(Rect2, Plane?)>();
            // seen again through the same opening's plane: that view grows; through another: a view of its own (past
            // Views of them, the last takes the rest, its plane dropped). A view grown goes on as it now is (the
            // rectangle round both): the openings beyond are looked through in all of it, as it is drawn, so a view
            // that later lies within it has been looked past already
            int same = views.FindIndex(v => Same(v.plane, plane));
            if (same >= 0)
            {
                if (views[same].rect.Encloses(rect))
                    continue;
                views[same] = (views[same].rect.Merge(rect), plane);
                rect = views[same].rect;
            }
            else if (views.Count < Views)
                views.Add((rect, plane));
            else
            {
                var last = views[^1];
                if (last.plane == null && last.rect.Encloses(rect))
                    continue;
                views[^1] = (last.rect.Merge(rect), null);
                (rect, plane) = views[^1];
            }
            var sec = Doc.Sectors[s];
            for (int w = sec.WallPtr; w < sec.WallPtr + sec.WallNum; w++)
            {
                var wall = Doc.Walls[w];
                int ns = wall.NextSector;
                if (ns < 0 || ns >= Doc.Sectors.Count || !Doc.SectorShown(ns) || !_sectorMesh.ContainsKey(ns) && !SpritesIn(ns))
                    continue;
                var (x1, y1, x2, y2) = BuildGeom.WallEnds(Doc, w);
                double dx = x2 - x1, dy = y2 - y1;
                // the camera on the sector's side (its room on the wall's right as drawn: (-dy, dx))
                double side = (cx - x1) * -dy + (cy - y1) * dx;
                double len = Math.Sqrt(dx * dx + dy * dy);
                bool at = len > 0 && Math.Abs(side) / len < 24 && MapDocument.SegmentDistance(cx, cy, x1, y1, x2, y2) < 24;
                if (side <= 0 && !at)
                    continue;
                var r = at ? rect : Opening(s, ns, x1, y1, x2, y2, view, size, rect);
                if (r is not { } nr)
                    continue;
                // the opening's plane, facing away from the camera: what lies in front of it isn't seen through it
                var a = PreviewMesher.P(x1, y1, 0);
                var n = new Vector3((float)dy, 0, (float)-dx).Normalized();
                queue.Enqueue((ns, nr, at ? plane : new Plane(n, n.Dot(a))));
            }
            // a Blood link: the room at its other end, through the plane of 504
            foreach (var (up, low) in _links)
            {
                int other = up == s && sec.FloorPicnum == PreviewMesher.LinkTile ? low
                    : low == s && sec.CeilingPicnum == PreviewMesher.LinkTile ? up : -1;
                if (other >= 0 && Doc.SectorShown(other))
                    queue.Enqueue((other, rect, null));
            }
        }
        _seenSectors = seen.Keys.ToHashSet();
        foreach (var (s, mi) in _sectorMesh)
        {
            if (seen.TryGetValue(s, out var v))
                Clip(mi, v);
            else
                mi.Visible = false;
        }
        // a sprite where its sector is seen: its place within one of its sector's views (on the screen, behind the
        // opening's plane); not clipped by the pixel (a sprite at an opening's edge shows whole)
        foreach (var (node, s) in _spriteNodes)
        {
            if (s < 0)
            {
                node.Visible = true;
                DarkenSprite(node);
                continue;
            }
            bool vis = false;
            if (seen.TryGetValue(s, out var views) && !_cam.IsPositionBehind(node.GlobalPosition))
            {
                var at = _cam.UnprojectPosition(node.GlobalPosition);
                foreach (var (r, p) in views)
                    if (r.Grow(48).HasPoint(at) && (p is not { } pl || pl.Normal.Dot(node.GlobalPosition) >= pl.D - 0.5f))
                    {
                        vis = true;
                        break;
                    }
            }
            node.Visible = vis;
            if (vis)
                DarkenSprite(node);
        }
    }

    /// <summary>A sector's ceiling and floor at (x, y) (of the map shown: the moving sectors posed in Off, On, Play).</summary>
    void Zs(int s, double x, double y, out double c, out double f) => BuildGeom.GetZsOfSlope(Doc, s, x, y, out c, out f);

    bool SpritesIn(int s) => _spriteNodes.Any(n => n.sector == s);

    static bool Same(Plane? a, Plane? b) => a is null ? b is null : b is { } pb && a.Value.IsEqualApprox(pb);

    /// <summary>How many views (openings apart) a sector keeps: its shader's clip slots.</summary>
    const int Views = 4;

    static readonly string[] RectNames = Enumerable.Range(0, Views).Select(i => $"clip_rect{i}").ToArray();
    static readonly string[] PlaneNames = Enumerable.Range(0, Views).Select(i => $"clip_plane{i}").ToArray();

    static void Clip(MeshInstance3D mi, List<(Rect2 rect, Plane? plane)> views)
    {
        mi.Visible = true;
        for (int i = 0; i < Views; i++)
        {
            // (an empty slot: a rectangle nothing lies in)
            var (r, p) = i < views.Count ? views[i] : (new Rect2(-10, -10, 0, 0), (Plane?)null);
            mi.SetInstanceShaderParameter(RectNames[i], new Vector4(r.Position.X, r.Position.Y, r.End.X, r.End.Y));
            var pl = p ?? new Plane(Vector3.Up, -1e9f);
            mi.SetInstanceShaderParameter(PlaneNames[i], new Vector4(pl.Normal.X, pl.Normal.Y, pl.Normal.Z, pl.D));
        }
    }

    /// <summary>The sector the camera was in last frame and where it was (map units): kept while it holds the camera,
    /// left through the wall the camera crossed (Build's updatesector), so rooms sharing a place don't swap as it
    /// moves.</summary>
    int _camSector = -1;
    double _camX, _camY;

    /// <summary>The sector the camera is in. The one it was in while that still holds it (its place and its height);
    /// else the sector behind a red wall of that one the camera crossed since the last frame; else the one behind any
    /// red wall of it holding the camera; else any holding it, by height (inside its floor and ceiling, else the
    /// nearest); -1 outside every shown sector.</summary>
    int CameraSector(double x, double y, double z)
    {
        int was = _camSector >= 0 && _camSector < Doc.Sectors.Count && Doc.SectorShown(_camSector) ? _camSector : -1;
        double px = _camX, py = _camY;
        _camX = x;
        _camY = y;
        return _camSector = TrackSector(Doc, Zs, was, px, py, x, y, z);
    }

    public delegate void ZsOf(int s, double x, double y, out double c, out double f);

    /// <summary>CameraSector's choice (static: --selftest): the camera at (x, y, z), last frame in sector was (-1:
    /// none) at (px, py); zs the sectors' ceiling and floor heights.</summary>
    public static int TrackSector(MapDocument Doc, ZsOf zs, int was, double px, double py, double x, double y, double z)
    {
        bool Holds(int s) => Doc.SectorShown(s) && BuildGeom.Inside(Doc, s, x, y);
        double Off(int s)
        {
            zs(s, x, y, out double c, out double f);
            return z < c ? c - z : z > f ? z - f : 0;
        }
        if (was >= 0 && Holds(was) && Off(was) == 0)
            return was;
        if (was >= 0)
        {
            // through the wall crossed, else through any of its red walls into a sector holding the camera
            var sec = Doc.Sectors[was];
            int any = -1;
            for (int w = sec.WallPtr; w < sec.WallPtr + sec.WallNum; w++)
            {
                int ns = Doc.Walls[w].NextSector;
                if (ns < 0 || ns >= Doc.Sectors.Count || !Holds(ns) || Off(ns) > 0)
                    continue;
                var (x1, y1, x2, y2) = BuildGeom.WallEnds(Doc, w);
                if (Crosses(px, py, x, y, x1, y1, x2, y2))
                    return ns;
                if (any < 0)
                    any = ns;
            }
            if (any >= 0)
                return any;
            // flown out through its floor or ceiling while over it: still there
            if (Holds(was) && !Doc.SectorsAt(x, y).Any(s => s != was && Holds(s) && Off(s) == 0))
                return was;
        }
        int best = -1;
        double bd = double.MaxValue;
        foreach (int s in Doc.SectorsAt(x, y))
        {
            if (!Doc.SectorShown(s))
                continue;
            double d = Off(s);
            if (d < bd)
            {
                bd = d;
                best = s;
            }
        }
        return best;
    }

    /// <summary>Whether the move (ax, ay)-(bx, by) crosses the segment (cx, cy)-(dx, dy).</summary>
    static bool Crosses(double ax, double ay, double bx, double by, double cx, double cy, double dx, double dy)
    {
        static double Side(double ox, double oy, double px, double py, double qx, double qy) => (px - ox) * (qy - oy) - (py - oy) * (qx - ox);
        double s1 = Side(cx, cy, dx, dy, ax, ay), s2 = Side(cx, cy, dx, dy, bx, by);
        double s3 = Side(ax, ay, bx, by, cx, cy), s4 = Side(ax, ay, bx, by, dx, dy);
        return (s1 > 0) != (s2 > 0) && (s3 > 0) != (s4 > 0);
    }

    /// <summary>The screen rectangle of the opening of wall (x1, y1)-(x2, y2) between sectors s and ns, within rect;
    /// null where it is shut (a closed door), behind the camera or outside rect.</summary>
    Rect2? Opening(int s, int ns, double x1, double y1, double x2, double y2, Transform3D view, Vector2I size, Rect2 rect)
    {
        Zs(s, x1, y1, out double c1, out double f1);
        Zs(s, x2, y2, out double c2, out double f2);
        Zs(ns, x1, y1, out double nc1, out double nf1);
        Zs(ns, x2, y2, out double nc2, out double nf2);
        var a = Doc.Sectors[s];
        var b = Doc.Sectors[ns];
        // (two skies: open upward; two parallax floors: open downward)
        bool skyUp = (a.CeilingStat & 1) != 0 && (b.CeilingStat & 1) != 0, skyDown = (a.FloorStat & 1) != 0 && (b.FloorStat & 1) != 0;
        double t1 = skyUp ? -1e7 : Math.Max(c1, nc1), t2 = skyUp ? -1e7 : Math.Max(c2, nc2);
        double b1 = skyDown ? 1e7 : Math.Min(f1, nf1), b2 = skyDown ? 1e7 : Math.Min(f2, nf2);
        if (t1 >= b1 && t2 >= b2)
            return null;
        // the opening's corners in the camera's space, cut at the near plane, then onto the screen
        var poly = new List<Vector3>
        {
            view * PreviewMesher.P(x1, y1, t1), view * PreviewMesher.P(x2, y2, t2),
            view * PreviewMesher.P(x2, y2, b2), view * PreviewMesher.P(x1, y1, b1),
        };
        float near = -_cam.Near;
        var cut = new List<Vector3>();
        for (int i = 0; i < poly.Count; i++)
        {
            var p = poly[i];
            var q = poly[(i + 1) % poly.Count];
            bool pin = p.Z <= near, qin = q.Z <= near;
            if (pin)
                cut.Add(p);
            if (pin != qin)
                cut.Add(p + (q - p) * ((near - p.Z) / (q.Z - p.Z)));
        }
        if (cut.Count < 3)
            return null;
        var world = _cam.GlobalTransform;
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        foreach (var p in cut)
        {
            var sp = _cam.UnprojectPosition(world * p);
            minX = Math.Min(minX, sp.X);
            minY = Math.Min(minY, sp.Y);
            maxX = Math.Max(maxX, sp.X);
            maxY = Math.Max(maxY, sp.Y);
        }
        // (a pixel more each way: the GPU's edge of the opening's faces and this one's needn't round alike)
        var r = new Rect2(minX - 1, minY - 1, maxX - minX + 2, maxY - minY + 2).Intersection(rect);
        return r.Size.X > 0.5f && r.Size.Y > 0.5f ? r : null;
    }

    // ---------------------------------------------------------------- the faces' shader

    static readonly Dictionary<(PreviewMesher.Kind, bool oneSided), Shader> _shaders = new();

    /// <summary>The faces' shader (the picture by its shade as its emission, from the vertex colour, darkened with distance
    /// by the map's visibility as Build does: vis_rate; full_bright: neither; the dynamic lights lighting the picture over
    /// it: Preview3DLights), discarding what lies
    /// outside the sector's view: clip_rect (screen pixels), clip_plane (kept where dot(n, p) >= d).</summary>
    static Shader FaceShader(PreviewMesher.Kind kind, bool oneSided)
    {
        if (_shaders.TryGetValue((kind, oneSided), out var sh))
            return sh;
        // (lit: the dynamic lights add to the picture over its shade, as the game's; nothing else lights it)
        string modes = "ambient_light_disabled, specular_disabled, " + (oneSided ? "cull_back" : "cull_disabled") + (kind == PreviewMesher.Kind.Translucent ? ", blend_mix, depth_draw_never" : "");
        string alpha = kind switch
        {
            PreviewMesher.Kind.Masked => "if (c.a < 0.5) discard;",
            PreviewMesher.Kind.Translucent => "if (c.a < 0.05) discard; ALPHA = c.a * 0.66;",
            _ => "",
        };
        sh = new Shader
        {
            Code = $@"shader_type spatial;
render_mode {modes};
uniform sampler2D albedo : source_color, filter_nearest_mipmap, repeat_enable;
uniform vec4 tint = vec4(1.0);
// Build's darkening with distance: shades a unit of depth (the map's visibility / 65536, as the game's), a sector's
// own visibility v scaling it by (v + 16) / 16 (Build's mulscale4); 0: none (a map of visibility 0)
uniform float vis_rate = 0.0;
// View > Full Brightness: every face as bright as shade 0 up close, nothing darkened with distance
uniform float full_bright = 0.0;
instance uniform vec4 clip_rect0 = vec4(-1e9, -1e9, 1e9, 1e9);
instance uniform vec4 clip_plane0 = vec4(0.0, 1.0, 0.0, -1e9);
instance uniform vec4 clip_rect1 = vec4(-10.0, -10.0, -10.0, -10.0);
instance uniform vec4 clip_plane1 = vec4(0.0, 1.0, 0.0, -1e9);
instance uniform vec4 clip_rect2 = vec4(-10.0, -10.0, -10.0, -10.0);
instance uniform vec4 clip_plane2 = vec4(0.0, 1.0, 0.0, -1e9);
instance uniform vec4 clip_rect3 = vec4(-10.0, -10.0, -10.0, -10.0);
instance uniform vec4 clip_plane3 = vec4(0.0, 1.0, 0.0, -1e9);
varying vec3 world_pos;
void vertex() {{
    world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}}
// seen through view i: inside its rectangle, behind its opening's plane
bool seen(vec4 r, vec4 p, vec2 f) {{
    return f.x >= r.x && f.y >= r.y && f.x <= r.z && f.y <= r.w && dot(p.xyz, world_pos) >= p.w - 0.05;
}}
void fragment() {{
    vec2 f = FRAGCOORD.xy;
    if (!seen(clip_rect0, clip_plane0, f) && !seen(clip_rect1, clip_plane1, f) && !seen(clip_rect2, clip_plane2, f)
        && !seen(clip_rect3, clip_plane3, f))
        discard;
    // the face's shade (PreviewMesher.ShadeCode) plus the distance's, as a brightness (Build's 64 shades to black)
    float shade = floor(COLOR.r * 255.0 + 0.5) - 128.0;
    float sv = floor(COLOR.g * 255.0 + 0.5);
    float rate = vis_rate * (sv > 0.5 ? mod(sv + 16.0, 256.0) / 16.0 : 1.0);
    float s = shade + max(-VERTEX.z, 0.0) * rate;
    float b = mix(pow(clamp(1.0 - s / 64.0, 0.0, 1.0), 2.2), 1.0, full_bright);
    vec4 t = texture(albedo, UV) * tint;
    vec4 c = t * vec4(vec3(b), COLOR.a);
    {alpha}
    // Build's shading as the face's own light; the lights on the picture itself (DynamicLights: the game's)
    ALBEDO = t.rgb;
    EMISSION = c.rgb;
}}",
        };
        _shaders[(kind, oneSided)] = sh;
        return sh;
    }
}
