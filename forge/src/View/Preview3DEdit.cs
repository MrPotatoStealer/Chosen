// Chosen core: original code (the 3D preview's editing, as UDB's visual mode: what the crosshair is on highlighted,
// clicked to select, the wheel raising and lowering floors and ceilings).
using System;
using System.Collections.Generic;
using System.Linq;
using Chosen.Forge.Config;
using Chosen.Forge.Document;
using Chosen.Shared.Engine.Geometry;
using Godot;

namespace Chosen.Forge.View;

/// <summary>What the preview's crosshair is on: a sector's floor or ceiling, a wall's part (its upper, lower or whole
/// wall; a masked middle), a sprite, or a dynamic light (its bulb, in Lights mode).</summary>
public enum VisualPart { Floor, Ceiling, Upper, Lower, Middle, Sprite, Light }

public readonly record struct VisualTarget(VisualPart Part, int Sector, int Wall, int Sprite = -1, int Light = -1)
{
    public bool IsPlane => Part is VisualPart.Floor or VisualPart.Ceiling;
    public bool IsWall => Part is VisualPart.Upper or VisualPart.Lower or VisualPart.Middle;
    public bool IsSprite => Part == VisualPart.Sprite;
    public bool IsLight => Part == VisualPart.Light;
}

public partial class Preview3D
{
    /// <summary>The target under the crosshair (null: nothing, or outside every sector).</summary>
    public VisualTarget? Target { get; private set; }
    /// <summary>Where the crosshair's ray meets the target (Build's x, y, z), while there is one.</summary>
    public (double x, double y, double z) HitPoint { get; private set; }

    /// <summary>The camera's place (Build's x, y, z).</summary>
    public (double x, double y, double z) CameraBuild => (_cam.GlobalPosition.X * 16, _cam.GlobalPosition.Z * 16, -_cam.GlobalPosition.Y * 256);

    /// <summary>A tool's result is shown (a changed copy of the map): no target, outline or tint.</summary>
    public bool MarksHidden
    {
        get => _marksHidden;
        set
        {
            _marksHidden = value;
            DrawMarks();
        }
    }
    bool _marksHidden;
    /// <summary>The selected targets (clicked), kept until the map is another or the preview left.</summary>
    public readonly HashSet<VisualTarget> Selected = new();
    /// <summary>The target changed (the info panel shows its sector or wall).</summary>
    public Action<VisualTarget?> TargetChanged;
    /// <summary>The wheel on the selection (else the target): the planes to raise (Build z, up negative), the step.</summary>
    public Action<List<VisualTarget>, int> Raise;
    /// <summary>A click on a target: the window toggles it (a command, recorded: ToggleSelected).</summary>
    public Action<VisualTarget> Click;

    /// <summary>
    /// Slope mode (H): Build's slopes as they are, a plane hinged on the sector's first wall, which only Shift+click
    /// changes. Aimed at a floor or ceiling, the plane is drawn (SlopeFrame: a rectangle from the first wall, green, to
    /// the far edge through the sector's farthest corner, cyan: Tools.SlopeOps.FarLine, as wide as the sector reaches
    /// along the wall), filled where it slices the room and outlined over everything; aimed anywhere on it (through
    /// walls too: the far edge mostly lies outside the room), its half under the crosshair is the handle. A click locks
    /// onto it (kept wherever the crosshair goes) and the wheel moves it: the far half tilts the plane about the first
    /// wall, the first wall's half raises that side with the far edge kept. Shift+click on any of the sector's walls
    /// makes it the first wall. Clicks select nothing in this mode. (Forge report 20261105-181409-182821: the edges
    /// were thin lines to aim at, the far one mostly behind the walls.)
    /// </summary>
    public bool SlopeMode
    {
        get => _slopeMode;
        set
        {
            _slopeMode = value;
            Edge = null;
            Handle = null;
            Locked = null;
            Shown = null;
            DrawMarks();
        }
    }
    bool _slopeMode;
    /// <summary>The plane slope mode shows (the locked one, else the one aimed at; kept while the crosshair stays on its
    /// drawn plane).</summary>
    public VisualTarget? Shown { get; private set; }
    /// <summary>The plane aimed at in slope mode, locked or not (its handles and walls answer the crosshair).</summary>
    VisualTarget? _aimed;

    /// <summary>A floor's or ceiling's slope as drawn: the first wall's start (Build x, y), the way along it and the way
    /// into the sector (unit), the rectangle's extent along (u) and across (v: the first wall at 0), and the far edge's
    /// v (the farthest corner's, either side).</summary>
    readonly record struct SlopeFrame(double Ax, double Ay, double Tx, double Ty, double Nx, double Ny,
        double ULo, double UHi, double VLo, double VHi, double VFar)
    {
        public (double x, double y) At(double u, double v) => (Ax + Tx * u + Nx * v, Ay + Ty * u + Ny * v);
        public (double u, double v) Of(double x, double y) => ((x - Ax) * Tx + (y - Ay) * Ty, (x - Ax) * Nx + (y - Ay) * Ny);
        /// <summary>The far half (nearer the far edge than the first wall).</summary>
        public bool Far(double v) => Math.Abs(v - VFar) < Math.Abs(v);
        /// <summary>The strips along its two edges that win over what lies in front (drawn over everything).</summary>
        public double Band => Math.Min(Math.Max(0.2 * (VHi - VLo), 128), (VHi - VLo) / 2);
        public double Margin => 32 + 0.04 * Math.Max(UHi - ULo, VHi - VLo);
    }

    /// <summary>Plane p's slope frame, null where it has none (no sector, or no corner off its first wall's line).</summary>
    SlopeFrame? FrameOf(VisualTarget p)
    {
        if (!p.IsPlane || p.Sector < 0 || p.Sector >= Doc.Sectors.Count || Doc.Sectors[p.Sector].WallNum < 3)
            return null;
        var sec = Doc.Sectors[p.Sector];
        var (ax, ay, bx, by) = BuildGeom.WallEnds(Doc, sec.WallPtr);
        double len = Math.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay));
        if (len < 1)
            return null;
        // (into the sector: the wall's right as drawn)
        double tx = (bx - ax) / len, ty = (by - ay) / len, nx = -ty, ny = tx;
        double ulo = 0, uhi = 0, vlo = 0, vhi = 0, vfar = 0;
        for (int w = sec.WallPtr; w < sec.WallPtr + sec.WallNum; w++)
        {
            double u = (Doc.Walls[w].X - ax) * tx + (Doc.Walls[w].Y - ay) * ty, v = (Doc.Walls[w].X - ax) * nx + (Doc.Walls[w].Y - ay) * ny;
            ulo = Math.Min(ulo, u);
            uhi = Math.Max(uhi, u);
            vlo = Math.Min(vlo, v);
            vhi = Math.Max(vhi, v);
            if (Math.Abs(v) > Math.Abs(vfar))
                vfar = v;
        }
        return Math.Abs(vfar) < 1 ? null : new SlopeFrame(ax, ay, tx, ty, nx, ny, ulo, uhi, vlo, vhi, vfar);
    }

    /// <summary>Where the ray (Build space: from c along d) meets plane p's drawn rectangle (its margin round it):
    /// the point, how far along the ray, and the frame's u, v there; null where it misses.</summary>
    (double x, double y, double z, double t, double u, double v)? FrameHit(VisualTarget p, SlopeFrame f,
        double cx, double cy, double cz, double dx, double dy, double dz)
    {
        var (a, b, c) = PlaneOf(p.Sector, p.Part == VisualPart.Ceiling);
        double den = dz - a * dx - b * dy;
        if (Math.Abs(den) < 1e-9)
            return null;
        double t = (a * cx + b * cy + c - cz) / den;
        if (t <= 0)
            return null;
        double x = cx + dx * t, y = cy + dy * t;
        var (u, v) = f.Of(x, y);
        double m = f.Margin;
        if (u < f.ULo - m || u > f.UHi + m || v < f.VLo - m || v > f.VHi + m)
            return null;
        return (x, y, cz + dz * t, t, u, v);
    }

    static double SlopeDegrees(MapDocument d, VisualTarget p)
    {
        var s = d.Sectors[p.Sector];
        bool ceiling = p.Part == VisualPart.Ceiling;
        int stat = ceiling ? s.CeilingStat : s.FloorStat, heinum = ceiling ? s.CeilingHeinum : s.FloorHeinum;
        // (Build: heinum 4096 a rise of one across one)
        return (stat & 2) == 0 ? 0 : Math.Atan(heinum / 4096.0) * 180 / Math.PI;
    }
    /// <summary>The wall nearest the crosshair in slope mode (for Shift+click: made the first wall): the plane (a floor
    /// or ceiling target) and its wall.</summary>
    public (VisualTarget plane, int wall)? Edge { get; private set; }
    /// <summary>The handle nearest the crosshair in slope mode: the plane, and far (the far edge) or not (the first
    /// wall).</summary>
    public (VisualTarget plane, bool far)? Handle { get; private set; }
    /// <summary>The handle clicked in slope mode (the wheel moves it).</summary>
    public (VisualTarget plane, bool far)? Locked { get; set; }
    /// <summary>The wheel on the locked handle: its plane, its first wall, the step (Build z, up negative), far (the
    /// plane tilted about the first wall) or not (the first wall's side moved, the far edge kept).</summary>
    public Action<VisualTarget, int, int, bool> SlopeEdge;
    /// <summary>Shift+click on a wall in slope mode: the window makes it the plane's first wall (a command).</summary>
    public Action<VisualTarget, int> FirstWall;
    /// <summary>A word for the status line (what a slope click did).</summary>
    public Action<string> Says;

    static string PlaneName(VisualTarget p) => $"sector {p.Sector}'s {(p.Part == VisualPart.Ceiling ? "ceiling" : "floor")}";

    /// <summary>A click in slope mode: the handle aimed at locked (again: unlocked); Shift: the wall aimed at made the
    /// first wall, the far edge locked; nothing aimed at: unlocked.</summary>
    public void SlopeClick(bool shift)
    {
        if (shift)
        {
            if (Edge is { } e)
            {
                FirstWall?.Invoke(e.plane, e.wall);
                Locked = (e.plane, true);
                Says?.Invoke($"slope: {PlaneName(e.plane)}: that wall is its first wall now ({SlopeDegrees(Doc, e.plane):0.#}°); the far edge (cyan) locked: the wheel tilts the plane about the first wall");
            }
            DrawMarks();
            return;
        }
        if (Handle is not { } h)
        {
            if (Locked != null)
                Says?.Invoke("slope: unlocked (aim at a floor or ceiling and click its plane: the far half to tilt it, the first wall's half to raise that side)");
            Locked = null;
        }
        else if (Locked == h)
        {
            Locked = null;
            Says?.Invoke("slope: unlocked");
        }
        else
        {
            Locked = h;
            Says?.Invoke(h.far
                ? $"slope: {PlaneName(h.plane)} ({SlopeDegrees(Doc, h.plane):0.#}°): far edge locked: the wheel tilts the plane about its first wall (green); click again: unlock; Shift+click a wall: the first wall"
                : $"slope: {PlaneName(h.plane)} ({SlopeDegrees(Doc, h.plane):0.#}°): first wall locked: the wheel raises or lowers its side, the far edge kept; click again: unlock");
        }
        DrawMarks();
    }

    /// <summary>A right click: the targets its menu works on (the selection when the target is in it, else the
    /// target).</summary>
    public Action<List<VisualTarget>> Menu;

    /// <summary>A target selected, or no longer.</summary>
    public void ToggleSelected(VisualTarget t)
    {
        if (!Selected.Remove(t))
            Selected.Add(t);
        DrawMarks();
    }

    /// <summary>The selection replaced (the 2D view's, synced).</summary>
    public void SetSelected(IEnumerable<VisualTarget> targets)
    {
        Selected.Clear();
        Selected.UnionWith(targets);
        DrawMarks();
    }

    /// <summary>A target as a command's word: f12, c12 (a floor, a ceiling), w40u, w40l, w40m (a wall's part).</summary>
    public static string Code(VisualTarget t) => t.Part switch
    {
        VisualPart.Floor => $"f{t.Sector}",
        VisualPart.Ceiling => $"c{t.Sector}",
        VisualPart.Upper => $"w{t.Wall}u",
        VisualPart.Lower => $"w{t.Wall}l",
        VisualPart.Sprite => $"s{t.Sprite}",
        VisualPart.Light => $"l{t.Light}",
        _ => $"w{t.Wall}m",
    };

    public static VisualTarget? Parse(MapDocument d, string code)
    {
        if (code.Length < 2 || !int.TryParse(code[1..].TrimEnd('u', 'l', 'm'), out int n) || n < 0)
            return null;
        switch (code[0])
        {
            case 'f' when n < d.Sectors.Count: return new VisualTarget(VisualPart.Floor, n, -1);
            case 'c' when n < d.Sectors.Count: return new VisualTarget(VisualPart.Ceiling, n, -1);
            case 's' when n < d.Sprites.Count: return new VisualTarget(VisualPart.Sprite, d.Sprites[n].SectNum, -1, n);
            case 'l' when n < d.Lights.Count: return LightTarget(d, n);
            case 'w' when n < d.Walls.Count:
                return new VisualTarget(code.EndsWith('u') ? VisualPart.Upper : code.EndsWith('l') ? VisualPart.Lower : VisualPart.Middle,
                    d.Walls[n].Sector, n);
        }
        return null;
    }

    /// <summary>Light i as a target (no sector: the same target wherever it is moved).</summary>
    public static VisualTarget LightTarget(MapDocument d, int i) => new(VisualPart.Light, -1, -1, -1, i);

    /// <summary>A wall's parts there are (to select a whole wall: a line selected in 2D): a white wall its middle; a red
    /// one its upper and lower parts where the next sector's planes leave them, its middle when masked or one-way.</summary>
    public static IEnumerable<VisualTarget> PartsOf(MapDocument d, int w)
    {
        if (w < 0 || w >= d.Walls.Count)
            yield break;
        var wall = d.Walls[w];
        int s = wall.Sector, ns = wall.NextSector;
        if (ns < 0 || ns >= d.Sectors.Count)
        {
            yield return new VisualTarget(VisualPart.Middle, s, w);
            yield break;
        }
        var (x1, y1, x2, y2) = BuildGeom.WallEnds(d, w);
        bool upper = false, lower = false;
        foreach (var (x, y) in new[] { (x1, y1), (x2, y2) })
        {
            BuildGeom.GetZsOfSlope(d, s, x, y, out double c, out double f);
            BuildGeom.GetZsOfSlope(d, ns, x, y, out double nc, out double nf);
            upper |= nc > c;
            lower |= nf < f;
        }
        if (upper && !((d.Sectors[s].CeilingStat & 1) != 0 && (d.Sectors[ns].CeilingStat & 1) != 0))
            yield return new VisualTarget(VisualPart.Upper, s, w);
        if (lower)
            yield return new VisualTarget(VisualPart.Lower, s, w);
        if ((wall.Cstat & (16 | 32)) != 0)
            yield return new VisualTarget(VisualPart.Middle, s, w);
    }

    MeshInstance3D _marks, _tint, _plane;
    ImmediateMesh _markMesh, _tintMesh, _planeMesh;
    Control _cross;

    /// <summary>The sectors Portals found seen this frame (the target is picked among them).</summary>
    HashSet<int> _seenSectors;

    void MakeMarks()
    {
        _markMesh = new ImmediateMesh();
        var mat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, NoDepthTest = true, VertexColorUseAsAlbedo = true,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        };
        _marks = new MeshInstance3D { Mesh = _markMesh, MaterialOverride = mat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        // the selection: its surfaces tinted red, where they are seen (depth tested, a hair off them toward the room)
        _tintMesh = new ImmediateMesh();
        var tint = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            AlbedoColor = new Color(1f, 0.3f, 0.25f, 0.07f), CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        _tint = new MeshInstance3D { Mesh = _tintMesh, MaterialOverride = tint, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        // slope mode's plane: filled where it slices the room (depth tested, both sides; its outline is a mark)
        _planeMesh = new ImmediateMesh();
        var plane = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            VertexColorUseAsAlbedo = true, CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        _plane = new MeshInstance3D { Mesh = _planeMesh, MaterialOverride = plane, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        _cross = new CrossHair { MouseFilter = MouseFilterEnum.Ignore };
        _cross.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_cross);
    }

    sealed partial class CrossHair : Control
    {
        public override void _Draw()
        {
            var c = Size * 0.5f;
            var col = new Color(1, 1, 1, 0.8f);
            DrawLine(c - new Vector2(7, 0), c + new Vector2(8, 0), col, 1);
            DrawLine(c - new Vector2(0, 7), c + new Vector2(0, 8), col, 1);
        }
    }

    // ---------------------------------------------------------------- picking

    /// <summary>The target under the crosshair: the nearest of the seen sectors' floors and ceilings (from their side)
    /// and walls (from their sector's side: the part between the planes there).</summary>
    void Pick()
    {
        VisualTarget? best = null;
        if (Doc != null && _sectorMesh.Count > 0)
        {
            var o = _cam.GlobalPosition;
            var g = -_cam.GlobalTransform.Basis.Z;
            // Build space: x, y as the map's, z down, 16 z a unit
            double cx = o.X * 16, cy = o.Z * 16, cz = -o.Y * 256, dx = g.X * 16, dy = g.Z * 16, dz = -g.Y * 256;
            double bt = double.MaxValue;
            IEnumerable<int> sectors = _seenSectors ?? (IEnumerable<int>)Enumerable.Range(0, Doc.Sectors.Count);
            foreach (int s in sectors)
            {
                if (s < 0 || s >= Doc.Sectors.Count || !Doc.SectorShown(s))
                    continue;
                var sec = Doc.Sectors[s];
                if (sec.WallNum < 3)
                    continue;
                foreach (bool ceiling in new[] { false, true })
                {
                    var (a, b, c) = PlaneOf(s, ceiling);
                    double den = dz - a * dx - b * dy;
                    if (Math.Abs(den) < 1e-9)
                        continue;
                    double t = (a * cx + b * cy + c - cz) / den;
                    if (t <= 0 || t >= bt)
                        continue;
                    // (seen from inside the sector: a floor from above, a ceiling from below)
                    if (ceiling ? den > 0 : den < 0)
                        continue;
                    double hx = cx + dx * t, hy = cy + dy * t;
                    if (!BuildGeom.Inside(Doc, s, hx, hy))
                        continue;
                    bt = t;
                    _hitT = t;
                    best = new VisualTarget(ceiling ? VisualPart.Ceiling : VisualPart.Floor, s, -1);
                }
                for (int w = sec.WallPtr; w < sec.WallPtr + sec.WallNum; w++)
                {
                    var (x1, y1, x2, y2) = BuildGeom.WallEnds(Doc, w);
                    double ex = x2 - x1, ey = y2 - y1;
                    // the camera on the wall's sector side (its room on the wall's right as drawn)
                    if ((cx - x1) * -ey + (cy - y1) * ex <= 0)
                        continue;
                    double den = dx * ey - dy * ex;
                    if (Math.Abs(den) < 1e-12)
                        continue;
                    double t = ((x1 - cx) * ey - (y1 - cy) * ex) / den;
                    double u = ((x1 - cx) * dy - (y1 - cy) * dx) / den;
                    if (t <= 0 || t >= bt || u < 0 || u > 1)
                        continue;
                    double hx = cx + dx * t, hy = cy + dy * t, hz = cz + dz * t;
                    BuildGeom.GetZsOfSlope(Doc, s, hx, hy, out double ceil, out double floor);
                    int ns = Doc.Walls[w].NextSector;
                    VisualPart? part = null;
                    if (ns < 0 || ns >= Doc.Sectors.Count)
                    {
                        if (hz >= ceil && hz <= floor)
                            part = VisualPart.Middle;
                    }
                    else
                    {
                        BuildGeom.GetZsOfSlope(Doc, ns, hx, hy, out double nc, out double nf);
                        bool skies = (sec.CeilingStat & 1) != 0 && (Doc.Sectors[ns].CeilingStat & 1) != 0;
                        if (!skies && hz >= ceil && hz < nc)
                            part = VisualPart.Upper;
                        else if (hz > nf && hz <= floor)
                            part = VisualPart.Lower;
                        // the picture across the opening: masked, or one-way (seen from this side: the side this loop
                        // looks from)
                        else if ((Doc.Walls[w].Cstat & (16 | 32)) != 0 && hz >= Math.Max(ceil, nc) && hz <= Math.Min(floor, nf))
                            part = VisualPart.Middle;
                    }
                    if (part is not { } p)
                        continue;
                    bt = t;
                    _hitT = t;
                    best = new VisualTarget(p, s, w);
                }
            }
        }
        // the sprites drawn, nearer than what was found (their rectangles: a picture's clear pixels count)
        if (Doc != null)
        {
            var o = _cam.GlobalPosition;
            var g = -_cam.GlobalTransform.Basis.Z;
            double bt = best != null ? _hitT : double.MaxValue;
            foreach (var (i, node) in _spriteNodeOf)
            {
                if (node is not Sprite3D s3 || !s3.Visible || !SpriteQuad(s3, out var c, out var right, out var up))
                    continue;
                var n = right.Cross(up);
                float den = n.Dot(g);
                if (Mathf.Abs(den) < 1e-6f)
                    continue;
                float t = n.Dot(c - o) / den;
                if (t <= 0 || t >= bt)
                    continue;
                var h = o + g * t - c;
                if (Mathf.Abs(h.Dot(right) / right.LengthSquared()) > 0.5f || Mathf.Abs(h.Dot(up) / up.LengthSquared()) > 0.5f)
                    continue;
                bt = t;
                _hitT = t;
                best = new VisualTarget(VisualPart.Sprite, Doc.Sprites[i].SectNum, -1, i);
            }
            // the lights' bulbs (Lights mode): drawn over the sprites, they win over what the crosshair is on unless a
            // wall, floor or ceiling hides them (a little round each: they are small)
            if (PickLight(o, g) is { } pl)
            {
                _hitT = pl.t;
                best = LightTarget(Doc, pl.light);
            }
        }
        // where the crosshair's ray met it (Build's x, y, z: the same t in both spaces)
        if (best != null)
        {
            var o = _cam.GlobalPosition;
            var g = -_cam.GlobalTransform.Basis.Z;
            HitPoint = (o.X * 16 + g.X * 16 * _hitT, o.Z * 16 + g.Z * 16 * _hitT, -o.Y * 256 - g.Y * 256 * _hitT);
        }
        Edge = null;
        Handle = null;
        if (SlopeMode && Doc != null)
            PickSlope(best);
        else
            Shown = _aimed = null;
        if (best != Target)
        {
            Target = best;
            TargetChanged?.Invoke(best);
        }
        DrawMarks();
    }

    /// <summary>How far along the ray (the same in Build's space and the preview's) the map's target was hit.</summary>
    double _hitT;

    /// <summary>How far the crosshair's target is (game units).</summary>
    public double HitDistance => _hitT;

    /// <summary>
    /// Slope mode's aim (best: what the crosshair is on in the map): the plane shown keeps the aim where the crosshair
    /// is on a strip along its first wall or far edge (drawn over everything: they mostly lie past the room's walls);
    /// else a floor or ceiling under the crosshair is aimed at; else the plane shown while the crosshair stays on it.
    /// The handle is the half of the aimed plane under the crosshair, Edge its wall nearest there (Shift+click).
    /// </summary>
    void PickSlope(VisualTarget? best)
    {
        var o = _cam.GlobalPosition;
        var g = -_cam.GlobalTransform.Basis.Z;
        double cx = o.X * 16, cy = o.Z * 16, cz = -o.Y * 256, dx = g.X * 16, dy = g.Z * 16, dz = -g.Y * 256;
        (VisualTarget p, SlopeFrame f, double x, double y, double v)? aim = null;
        if (Locked is { } lk && FrameOf(lk.plane) == null)
            Locked = null;
        var keep = Locked?.plane ?? _aimed ?? Shown;
        if (keep is { } k && FrameOf(k) is { } kf && FrameHit(k, kf, cx, cy, cz, dx, dy, dz) is { } kh
            && (Math.Abs(kh.v) <= kf.Band || Math.Abs(kh.v - kf.VFar) <= kf.Band))
            aim = (k, kf, kh.x, kh.y, kh.v);
        else if (best is { IsPlane: true } bp && FrameOf(bp) is { } bf)
            aim = (bp, bf, HitPoint.x, HitPoint.y, bf.Of(HitPoint.x, HitPoint.y).v);
        else if (keep is { } k2 && FrameOf(k2) is { } kf2 && FrameHit(k2, kf2, cx, cy, cz, dx, dy, dz) is { } kh2)
            aim = (k2, kf2, kh2.x, kh2.y, kh2.v);
        _aimed = aim?.p;
        Shown = Locked?.plane ?? _aimed;
        if (aim is { } a)
        {
            Handle = (a.p, a.f.Far(a.v));
            if (Tools.SlopeOps.NearestEdge(Doc, a.p.Sector, a.x, a.y) is int ew and >= 0)
                Edge = (a.p, ew);
        }
    }

    /// <summary>A sprite's picture as a rectangle: its middle and its half-extents' axes scaled to the whole width
    /// and height (right, up); a facing sprite turned to the camera about the vertical, as drawn.</summary>
    bool SpriteQuad(Sprite3D s3, out Vector3 c, out Vector3 right, out Vector3 up)
    {
        c = right = up = Vector3.Zero;
        if (s3.Texture == null)
            return false;
        var size = s3.Texture.GetSize() * s3.PixelSize;
        var b = s3.GlobalTransform.Basis;
        if (s3.Billboard == BaseMaterial3D.BillboardModeEnum.FixedY)
        {
            var cr = _cam.GlobalTransform.Basis.X;
            cr.Y = 0;
            if (cr.LengthSquared() < 1e-6f)
                return false;
            right = cr.Normalized() * size.X * s3.Scale.X;
            up = Vector3.Up * size.Y * s3.Scale.Y;
        }
        else
        {
            right = b.X * size.X;
            up = b.Y * size.Y;
        }
        // (the offset in pixels along the picture's own axes)
        c = s3.GlobalPosition + right / size.X * s3.Offset.X * s3.PixelSize + up / size.Y * s3.Offset.Y * s3.PixelSize;
        return true;
    }

    /// <summary>Sector s's floor or ceiling as z = a x + b y + c (Build space; a slope is a plane).</summary>
    (double a, double b, double c) PlaneOf(int s, bool ceiling)
    {
        var w = Doc.Walls[Doc.Sectors[s].WallPtr];
        double x0 = w.X, y0 = w.Y;
        double z0 = BuildGeom.PlaneZ(Doc, s, ceiling, x0, y0);
        double a = (BuildGeom.PlaneZ(Doc, s, ceiling, x0 + 1024, y0) - z0) / 1024;
        double b = (BuildGeom.PlaneZ(Doc, s, ceiling, x0, y0 + 1024) - z0) / 1024;
        return (a, b, z0 - a * x0 - b * y0);
    }

    // ---------------------------------------------------------------- the marks

    /// <summary>The target outlined in the highlight colour (over everything), the selection's surfaces tinted red.</summary>
    void DrawMarks()
    {
        TintBulbs();
        _markMesh.ClearSurfaces();
        _tintMesh.ClearSurfaces();
        _planeMesh.ClearSurfaces();
        if (Doc == null || MarksHidden || (Target == null && Selected.Count == 0 && Locked == null && Shown == null))
            return;
        if (_marks.GetParent() == null)
            _world.AddChild(_marks);
        if (_tint.GetParent() == null)
            _world.AddChild(_tint);
        if (_plane.GetParent() == null)
            _world.AddChild(_plane);
        if (SlopeMode)
        {
            _markMesh.SurfaceBegin(Mesh.PrimitiveType.Lines);
            _planeMesh.SurfaceBegin(Mesh.PrimitiveType.Triangles);
            // (an empty surface isn't allowed, nor fewer vertices than a triangle)
            var (lines, tris) = SlopeMarks();
            if (lines == 0)
            {
                _markMesh.SurfaceAddVertex(Vector3.Zero);
                _markMesh.SurfaceAddVertex(Vector3.Zero);
            }
            if (tris == 0)
                for (int k = 0; k < 3; k++)
                    _planeMesh.SurfaceAddVertex(Vector3.Zero);
            _markMesh.SurfaceEnd();
            _planeMesh.SurfaceEnd();
        }
        else if (Target != null || Selected.Any(s => s.IsLight))
        {
            _markMesh.SurfaceBegin(Mesh.PrimitiveType.Lines);
            // (Lights mode: only a light outlined, the rest isn't clicked)
            int n = Target is { } tg && (!LightMarkers || tg.IsLight) ? Outline(tg, ColorSettings.Highlight) : 0;
            // (the lights selected ringed: their bulbs are too small to tint)
            foreach (var s in Selected)
                if (s.IsLight)
                    n += LightRing(s.Light, ColorSettings.Selection, 1.95f);
            // (an empty surface isn't allowed: a target with nothing to outline, a sprite with no picture)
            if (n == 0)
            {
                _markMesh.SurfaceAddVertex(Vector3.Zero);
                _markMesh.SurfaceAddVertex(Vector3.Zero);
            }
            _markMesh.SurfaceEnd();
        }
        if (Selected.Count > 0)
        {
            _tintMesh.SurfaceBegin(Mesh.PrimitiveType.Triangles);
            int n = 0;
            foreach (var t in Selected)
                n += Tint(t);
            // (an empty surface isn't allowed, nor fewer vertices than a triangle: one of nothing)
            if (n == 0)
                for (int k = 0; k < 3; k++)
                    _tintMesh.SurfaceAddVertex(Vector3.Zero);
            _tintMesh.SurfaceEnd();
        }
    }

    /// <summary>Slope mode's marks: the plane shown (locked, else aimed at) drawn as the plane it is, from its first wall
    /// (green) to its far edge (cyan) as wide as the sector reaches along the wall: filled where it slices the room
    /// (depth tested; the half the wheel would move brighter), its outline, its edges (the locked one thick, the one
    /// aimed at less so), lines across it at even heights and an arrow from the first wall to the far edge over
    /// everything; its sector outlined dim; another plane aimed at while one is locked, its edges dim; the wall
    /// Shift+click would make first white (brighter while Shift is held). How many line and triangle vertices.</summary>
    /// <summary>Slope mode's locked half and edge (clicked: what the wheel moves), apart from the one only aimed at.</summary>
    static readonly Color LockedColour = new(1f, 0.3f, 0.25f);

    (int lines, int tris) SlopeMarks()
    {
        int n = 0, nt = 0;
        var hot = new Color(0.25f, 1f, 1f);
        void L(Vector3 a, Vector3 b, Color c)
        {
            _markMesh.SurfaceSetColor(c);
            _markMesh.SurfaceAddVertex(a);
            _markMesh.SurfaceSetColor(c);
            _markMesh.SurfaceAddVertex(b);
            n += 2;
        }
        void T(Vector3 a, Vector3 b, Vector3 c, Color col)
        {
            foreach (var p in new[] { a, b, c })
            {
                _planeMesh.SurfaceSetColor(col);
                _planeMesh.SurfaceAddVertex(p);
            }
            nt += 3;
        }
        var thick = new[] { Vector3.Zero, new Vector3(0, 0.04f, 0), new Vector3(0, -0.04f, 0), new Vector3(0, 0.08f, 0) };
        // a point of plane p's frame f (u, v) at the plane's height there, lifted a hair into the room
        Vector3 At(VisualTarget p, SlopeFrame f, double u, double v, double lift = 0)
        {
            bool ceiling = p.Part == VisualPart.Ceiling;
            var (x, y) = f.At(u, v);
            return PreviewMesher.P(x, y, BuildGeom.PlaneZ(Doc, p.Sector, ceiling, x, y) + (ceiling ? lift : -lift));
        }
        void Line(Vector3 a, Vector3 b, Color c, int thickness)
        {
            for (int k = 0; k < Math.Min(thickness, thick.Length); k++)
                L(a + thick[k], b + thick[k], c);
        }
        // a plane's two edges: the first wall green, the far edge cyan, across the whole frame
        void Edges(VisualTarget p, SlopeFrame f, bool dim)
        {
            foreach (bool far in new[] { false, true })
            {
                bool locked = Locked is { } lk && lk.plane == p && lk.far == far;
                bool aimed = Handle is { } ha && ha.plane == p && ha.far == far;
                var c = far ? hot : ColorSettings.FirstWall;
                if (dim && !locked)
                    c = c.Darkened(0.5f);
                double v = far ? f.VFar : 0;
                // (the locked edge red: the one the wheel moves; aimed at only, its own colour brighter)
                Line(At(p, f, f.ULo, v), At(p, f, f.UHi, v), locked ? LockedColour : aimed ? c.Lightened(0.2f) : c.Darkened(0.25f), locked ? 4 : aimed ? 2 : 1);
            }
        }
        if (Shown is { } sp && FrameOf(sp) is { } f)
        {
            n += Outline(sp, ColorSettings.Highlight.Darkened(0.75f));
            // the fill: the first wall's half and the far half; the locked one (the wheel moves it) red, the one aimed at
            // brighter in its own colour
            double mid = f.VFar / 2;
            double vNear = f.VFar > 0 ? f.VLo : f.VHi, vFar = f.VFar > 0 ? f.VHi : f.VLo;
            foreach (bool far in new[] { false, true })
            {
                bool locked = Locked is { } lk && lk.plane == sp && lk.far == far;
                bool aimed = Handle is { } ha && ha.plane == sp && ha.far == far;
                var c = locked ? LockedColour : far ? hot : ColorSettings.FirstWall;
                c.A = locked ? 0.3f : aimed ? 0.2f : 0.07f;
                double v0 = far ? mid : vNear, v1 = far ? vFar : mid;
                // (cut in strips along the wall: the corners' heights are the plane's, a long quad stays flat anyway)
                var a = At(sp, f, f.ULo, v0, 16);
                var b = At(sp, f, f.UHi, v0, 16);
                var cc = At(sp, f, f.UHi, v1, 16);
                var d = At(sp, f, f.ULo, v1, 16);
                T(a, b, cc, c);
                T(a, cc, d, c);
            }
            // its outline, lines across it at even steps from the first wall, its edges
            var dimLine = hot.Darkened(0.5f);
            dimLine.A = 0.7f;
            Line(At(sp, f, f.ULo, f.VLo), At(sp, f, f.UHi, f.VLo), dimLine, 1);
            Line(At(sp, f, f.ULo, f.VHi), At(sp, f, f.UHi, f.VHi), dimLine, 1);
            Line(At(sp, f, f.ULo, f.VLo), At(sp, f, f.ULo, f.VHi), dimLine, 1);
            Line(At(sp, f, f.UHi, f.VLo), At(sp, f, f.UHi, f.VHi), dimLine, 1);
            var faint = dimLine;
            faint.A = 0.35f;
            for (int k = 1; k < 4; k++)
                Line(At(sp, f, f.ULo, f.VFar * k / 4), At(sp, f, f.UHi, f.VFar * k / 4), faint, 1);
            Edges(sp, f, false);
            // the arrow: the way the plane tilts, from the first wall's middle to the far edge
            double um = (f.ULo + f.UHi) / 2, head = Math.Min(Math.Abs(f.VFar) * 0.15, (f.UHi - f.ULo) * 0.25);
            double sgn = Math.Sign(f.VFar);
            var arrow = new Color(1f, 0.85f, 0.3f);
            Line(At(sp, f, um, 0), At(sp, f, um, f.VFar), arrow, 2);
            Line(At(sp, f, um, f.VFar), At(sp, f, um - head, f.VFar - sgn * head), arrow, 2);
            Line(At(sp, f, um, f.VFar), At(sp, f, um + head, f.VFar - sgn * head), arrow, 2);
        }
        else if (Target is { } tg)
            n += Outline(tg, ColorSettings.Highlight.Darkened(0.75f));
        // another plane aimed at while one is locked: its edges dim
        if (_aimed is { } other && Shown is { } s0 && other != s0 && FrameOf(other) is { } of)
            Edges(other, of, true);
        // the wall Shift+click would make first (not the first wall already): white, brighter with Shift held
        if (Edge is { } e && e.plane.Sector >= 0 && e.plane.Sector < Doc.Sectors.Count && e.wall != Doc.Sectors[e.plane.Sector].WallPtr)
        {
            bool shift = Input.IsKeyPressed(Key.Shift);
            bool ceiling = e.plane.Part == VisualPart.Ceiling;
            var (x1, y1, x2, y2) = BuildGeom.WallEnds(Doc, e.wall);
            Line(PreviewMesher.P(x1, y1, BuildGeom.PlaneZ(Doc, e.plane.Sector, ceiling, x1, y1)),
                PreviewMesher.P(x2, y2, BuildGeom.PlaneZ(Doc, e.plane.Sector, ceiling, x2, y2)),
                new Color(1, 1, 1, shift ? 1f : 0.6f), shift ? 3 : 1);
        }
        return (n, nt);
    }

    /// <summary>A selected surface's triangles for the tint: a floor or ceiling its sector's area at its height, a
    /// wall's part its quad; lifted a hair into the room (no fighting with the face). How many vertices.</summary>
    int Tint(VisualTarget t)
    {
        int n = 0;
        void V(Vector3 p)
        {
            _tintMesh.SurfaceAddVertex(p);
            n++;
        }
        if (t.IsSprite)
        {
            if (SpriteCorners(t.Sprite) is { } q)
            {
                V(q[0]); V(q[1]); V(q[2]);
                V(q[0]); V(q[2]); V(q[3]);
            }
            return n;
        }
        if (t.Sector < 0 || t.Sector >= Doc.Sectors.Count || !Doc.SectorShown(t.Sector))
            return 0;
        if (t.IsPlane)
        {
            bool ceiling = t.Part == VisualPart.Ceiling;
            // (16 Build z: a sixteenth of a game unit into the room)
            double off = ceiling ? 16 : -16;
            foreach (var (x, y) in PreviewMesher.Fill(Doc, t.Sector))
                V(PreviewMesher.P(x, y, BuildGeom.PlaneZ(Doc, t.Sector, ceiling, x, y) + off));
            return n;
        }
        if (t.Wall < 0 || t.Wall >= Doc.Walls.Count)
            return 0;
        var (ax, ay, bx, by) = BuildGeom.WallEnds(Doc, t.Wall);
        var (at, ab) = WallSpan(t, ax, ay);
        var (bt, bb) = WallSpan(t, bx, by);
        // (a Build unit toward the wall's own sector: its right as drawn)
        double len = Math.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay));
        double nx = len > 0 ? -(by - ay) / len : 0, ny = len > 0 ? (bx - ax) / len : 0;
        var p1 = PreviewMesher.P(ax + nx, ay + ny, at);
        var p2 = PreviewMesher.P(bx + nx, by + ny, bt);
        var p3 = PreviewMesher.P(bx + nx, by + ny, bb);
        var p4 = PreviewMesher.P(ax + nx, ay + ny, ab);
        V(p1); V(p2); V(p3);
        V(p1); V(p3); V(p4);
        return n;
    }

    /// <summary>A wall part's top and bottom at a point of it (Build z): a whole wall its sector's ceiling to floor,
    /// an upper part the ceiling to the next sector's, a lower part the next sector's floor to the floor, a masked
    /// middle the opening.</summary>
    (double top, double bottom) WallSpan(VisualTarget t, double x, double y)
    {
        BuildGeom.GetZsOfSlope(Doc, t.Sector, x, y, out double ce, out double fl);
        int ns = Doc.Walls[t.Wall].NextSector;
        if (ns < 0 || ns >= Doc.Sectors.Count)
            return (ce, fl);
        BuildGeom.GetZsOfSlope(Doc, ns, x, y, out double nc, out double nf);
        return t.Part switch
        {
            VisualPart.Upper => (ce, nc),
            VisualPart.Lower => (nf, fl),
            _ => (Math.Max(ce, nc), Math.Min(fl, nf)),
        };
    }

    /// <summary>A sprite's rectangle's corners (as drawn), or null.</summary>
    Vector3[] SpriteCorners(int i)
    {
        if (!_spriteNodeOf.TryGetValue(i, out var node) || node is not Sprite3D s3 || !SpriteQuad(s3, out var c, out var r, out var u))
            return null;
        r *= 0.5f;
        u *= 0.5f;
        return new[] { c - r + u, c + r + u, c + r - u, c - r - u };
    }

    /// <summary>A target's outline in colour c; how many vertices.</summary>
    int Outline(VisualTarget t, Color c)
    {
        int n = 0;
        void L(Vector3 a, Vector3 b)
        {
            _markMesh.SurfaceSetColor(c);
            _markMesh.SurfaceAddVertex(a);
            _markMesh.SurfaceSetColor(c);
            _markMesh.SurfaceAddVertex(b);
            n += 2;
        }
        if (t.IsSprite)
        {
            if (SpriteCorners(t.Sprite) is { } q)
                for (int k = 0; k < 4; k++)
                    L(q[k], q[(k + 1) % 4]);
            return n;
        }
        if (t.IsLight)
            return LightRing(t.Light, c, 1.7f);
        if (t.Sector < 0 || t.Sector >= Doc.Sectors.Count)
            return n;
        var sec = Doc.Sectors[t.Sector];
        if (t.IsPlane)
        {
            bool ceiling = t.Part == VisualPart.Ceiling;
            for (int w = sec.WallPtr; w < sec.WallPtr + sec.WallNum; w++)
            {
                var (x1, y1, x2, y2) = BuildGeom.WallEnds(Doc, w);
                L(PreviewMesher.P(x1, y1, BuildGeom.PlaneZ(Doc, t.Sector, ceiling, x1, y1)),
                    PreviewMesher.P(x2, y2, BuildGeom.PlaneZ(Doc, t.Sector, ceiling, x2, y2)));
            }
            return n;
        }
        if (t.Wall < 0 || t.Wall >= Doc.Walls.Count)
            return n;
        var (ax, ay, bx, by) = BuildGeom.WallEnds(Doc, t.Wall);
        var (at, ab) = WallSpan(t, ax, ay);
        var (bt, bb) = WallSpan(t, bx, by);
        var p1 = PreviewMesher.P(ax, ay, at);
        var p2 = PreviewMesher.P(bx, by, bt);
        var p3 = PreviewMesher.P(bx, by, bb);
        var p4 = PreviewMesher.P(ax, ay, ab);
        L(p1, p2);
        L(p2, p3);
        L(p3, p4);
        L(p4, p1);
        return n;
    }

    // ---------------------------------------------------------------- input

    /// <summary>C (clearselection): nothing selected.</summary>
    public void ClearSelection()
    {
        Selected.Clear();
        DrawMarks();
    }

    /// <summary>A click or the wheel while the mouse is captured: true when used (internal: the UI test presses with it).</summary>
    internal bool EditInput(InputEventMouseButton mb)
    {
        // a light held let go: put where it was moved, or (not moved) a click on it
        if (!mb.Pressed)
        {
            if (mb.ButtonIndex != MouseButton.Left || _held == null)
                return false;
            EndHold();
            return true;
        }
        // the wheel on a light held: pushed away, pulled near (Shift a little, Ctrl more)
        if (_held != null && mb.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
        {
            float by = mb.ShiftPressed ? 1 : mb.CtrlPressed ? 16 : 4;
            PushHeld(mb.ButtonIndex == MouseButton.WheelUp ? by : -by);
            return true;
        }
        switch (mb.ButtonIndex)
        {
            case MouseButton.Left:
                // slope mode: a click locks onto an edge (Shift: the hinge), never selects
                if (SlopeMode)
                {
                    SlopeClick(mb.ShiftPressed);
                    return true;
                }
                // a light's bulb: held (moved with the crosshair while the button is down; let go unmoved, selected)
                if (Target is { IsLight: true } lt)
                {
                    StartHold(lt);
                    return true;
                }
                // (Lights mode: only the lights are clicked)
                if (LightMarkers)
                    return true;
                // (UDB: a click selects what the crosshair is on, again: no longer; through the window, recorded)
                if (Target is { } t)
                {
                    if (Click != null)
                        Click(t);
                    else
                        ToggleSelected(t);
                }
                return true;
            case MouseButton.Right:
                // (Lights mode: only a light's settings)
                if (Target is { } rt && Menu != null && (!LightMarkers || rt.IsLight))
                    Menu(Selected.Contains(rt) ? Selected.ToList() : new List<VisualTarget> { rt });
                return true;
            case MouseButton.WheelUp:
            case MouseButton.WheelDown:
            {
                // slope mode: the locked handle moved (the same steps), wherever the crosshair is, the first wall kept;
                // nothing locked: nothing moves (the aim would drift onto another handle)
                if (SlopeMode)
                {
                    if (Locked is { } l && l.plane.Sector >= 0 && l.plane.Sector < Doc.Sectors.Count && Doc.Sectors[l.plane.Sector].WallNum >= 3)
                    {
                        int es = mb.ShiftPressed ? 256 : mb.CtrlPressed ? 4096 : 1024;
                        SlopeEdge?.Invoke(l.plane, Doc.Sectors[l.plane.Sector].WallPtr, mb.ButtonIndex == MouseButton.WheelUp ? -es : es, l.far);
                    }
                    else
                    {
                        Locked = null;
                        Says?.Invoke("slope: click a floor's or ceiling's plane first (its far half, cyan: tilt; its first wall's half, green: raise that side); Shift+click a wall: make it the first wall");
                    }
                    return true;
                }
                var on = Selected.Count > 0 ? Selected.ToList() : Target is { } tg ? new List<VisualTarget> { tg } : new List<VisualTarget>();
                // (Lights mode: the lights alone)
                if (LightMarkers)
                    on = on.Where(t => t.IsLight).ToList();
                var planes = on.Select(PlaneFor).Where(p => p != null).Select(p => p.Value).Distinct().ToList();
                if (planes.Count == 0)
                    return true;
                // a notch Build's PgUp step (1024 z: 4 game units); Shift one game unit, Ctrl four steps (sprites too)
                int step = mb.ShiftPressed ? 256 : mb.CtrlPressed ? 4096 : 1024;
                Raise?.Invoke(planes, mb.ButtonIndex == MouseButton.WheelUp ? -step : step);
                return true;
            }
        }
        return false;
    }

    /// <summary>What a target's wheel moves: a floor or ceiling, a sprite, a light (up and down); a wall nothing (UDB's
    /// default: its height by sidedef off).</summary>
    static VisualTarget? PlaneFor(VisualTarget t) => t.IsPlane || t.IsSprite || t.IsLight ? t : null;
}
