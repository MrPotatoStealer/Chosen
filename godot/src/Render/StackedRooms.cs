using System.Collections.Generic;
using System.Text.RegularExpressions;
using Godot;
using Chosen.Shared.Blood.Map;

namespace Chosen.Render;

/// <summary>
/// Rooms stacked so that their heights overlap (E1M3: an upper corridor's floor lies below the ceiling of the room
/// under it). Build draws one sector at a time, from the camera's sector outwards through two-sided walls, so in
/// the shared heights it shows whichever room its walk reaches there. The converter names the faces of the shared
/// heights NNNN_v&lt;own&gt;_&lt;other&gt; (blood_above, blood_below, blood_flat); their shader does Build's walk per pixel
/// (Shader: from the camera's sector along the line to just in front of the pixel, as SectorMap.Walk) and drops the
/// pixel where the walk arrives in sector other. So one doorway can show one room and another doorway the other.
/// A portal copy walks from the sector beyond the link, the eye moved by its offset, as Build draws the far side: from
/// where the eye's line comes through the link's plane (the moved eye itself is seldom in that sector: beside a pit it
/// stands over the lower room's next sector, and the walk left the far sector by the wall the line came in through).
/// Where the walk can't tell (more than WalkSteps sectors away), the face goes by the older guess: hidden while the
/// camera is nearer to sector other than to sector own in steps through two-sided walls. A sprite of one room inside the other's footprint is hidden the same way (on the CPU).
/// Rooms sharing space where both are walkable (Build's impossible space, SectorMap's layers) are built once per
/// layer, their faces tagged _g&lt;layer&gt; (any folder): a face of a layer is drawn where the walk reaches a sector of
/// that layer (where it can't tell, the layer of the sector it starts from), and so is a sprite.
/// </summary>
public static class StackedRooms
{
    static Sim.SectorMap _shade;
    static readonly List<(ShaderMaterial mat, int own, int[] others)> _mats = new();
    static readonly Dictionary<string, ShaderMaterial> _bySurface = new();
    static readonly Regex Tag = new(@"_v(\d+)_(\d+)$");
    static readonly StringName HiddenName = "stack_hidden", OwnName = "stack_own", LayerName = "stack_layer";
    const string WallsName = "stack_walls", CameraName = "stack_camera", GroupsName = "stack_groups", DistName = "stack_dist";
    const int DataWidth = 1024;
    public const int WalkSteps = Sim.SectorMap.WalkSteps;
    static ImageTexture _data;

    /// <summary>The world shaders' part: stack_discard(pixel, eye, portal copy) drops a stacked room's face where
    /// Build's walk from the camera's sector reaches a room stacked with its own there. stack_walls: sector s's texel
    /// holds its first wall texel, wall count, first partner texel and partner count; each two-sided wall two texels,
    /// its ends (Quake x y) and the sector beyond; each partner (a sector stacked with s, either way) one, then the
    /// bounds of them all (Quake x y, a little wider: a pixel outside them is in no partner, so no walk can hide it and
    /// none is made: a hall with a small room under one corner walked for every pixel of its floor). One-sided
    /// walls are left out: a line through one ends at a pixel
    /// behind that wall, which the depth test hides whatever the walk says.</summary>
    public static readonly string Shader = @"
global uniform sampler2D stack_walls : filter_nearest;
global uniform float stack_camera;     // the camera's Build sector (-1: none)
// each sector's steps from the camera's through two-sided walls (where the walk can't tell: the nearer room shows)
global uniform sampler2D stack_dist : filter_nearest;
// a face of stacked rooms (StackedRooms): the sector it is seen from (-1: not one)
uniform float stack_own = -1.0;
// the older guess, where the walk can't tell: the camera nearer to the other room
uniform bool stack_hidden = false;
// a face of one layer of rooms sharing space: its layer (-1: not one)
uniform float stack_layer = -1.0;
// the first texel of the sectors' layers in stack_walls (-1: a level without layers)
global uniform float stack_groups;

vec4 stack_texel(int i) { return texelFetch(stack_walls, ivec2(i % DATAWIDTH, i / DATAWIDTH), 0); }

// Build's walk from sector c (holding a) to b through two-sided walls: the sector it reaches b in (-1: too far)
int stack_walk(int c, vec2 a, vec2 b) {
	vec2 d = b - a;
	float tcur = 0.0;
	for (int step = 0; step < WALKSTEPS; step++) {
		vec4 h = stack_texel(c);
		int first = int(h.x), n = int(h.y);
		float best = 2.0;
		int next = -2;
		for (int j = 0; j < n; j++) {
			vec4 seg = stack_texel(first + 2 * j);
			vec2 e = seg.zw - seg.xy;
			float den = d.x * e.y - d.y * e.x;
			if (abs(den) < 1e-9) continue;
			vec2 f = seg.xy - a;
			float t = (f.x * e.y - f.y * e.x) / den, u = (f.x * d.y - f.y * d.x) / den;
			if (t > tcur + 1e-5 && t <= 1.0 && u >= 0.0 && u <= 1.0 && t < best) {
				best = t;
				next = int(stack_texel(first + 2 * j + 1).x);
			}
		}
		if (next == -2) return c;
		c = next;
		tcur = best;
	}
	return -1;
}

bool stack_partner(int s, int r) {
	vec4 h = stack_texel(s);
	for (int j = 0; j < int(h.w); j++)
		if (int(stack_texel(int(h.z) + j).x) == r) return true;
	return false;
}

float stack_steps(int s) { return texelFetch(stack_dist, ivec2(s % DATAWIDTH, s / DATAWIDTH), 0).r; }

// where the walk can't tell (too far): a sector sharing the ground with s fewer steps from the camera's shows instead
bool stack_nearer(int s, vec3 w) {
	vec4 h = stack_texel(s);
	float own = stack_steps(s);
	for (int j = 0; j < int(h.w); j++) {
		int p = int(stack_texel(int(h.z) + j).x);
		if (stack_steps(p) >= own) continue;
#ifdef B3D_PLANES
		// (a level from its Build data: only a sector the pixel lies in, floor to ceiling, shows instead: DWE1M1's
		// cellar 358 under the hills hid them, far past the walk)
		if (!b3d_holds(p, w)) continue;
#endif
		return true;
	}
	return false;
}

// vown: the face's own sector from its vertices (Render.LiveRenderer's sector faces: UV2.x - 1), -1: none
bool stack_discard(vec3 w, vec3 eye, float copy, float vown) {
	bool vertex_own = stack_own < 0.0 && vown > -0.5;
	float own = vertex_own ? floor(vown + 0.5) : stack_own;
	// a face of one layer: the material's, else (a face from Build's sectors) its own sector's when that is one of the
	// rooms sharing space (E4M2's 8 beside the shaft 170: the corner of its wall, the pixel's step back in the shaft,
	// that shares no ground with it, was left: a strip)
	float face_layer = stack_layer;
	if (face_layer < 0.0 && vertex_own && stack_groups >= 0.0) {
		vec4 g = stack_texel(int(stack_groups) + int(own));
		if (g.y > 0.5) face_layer = g.x;
	}
	bool layered = face_layer >= 0.0 && stack_groups >= 0.0;
	// a sector sharing the ground with none: Build shows it wherever its walk gets
	if (vertex_own && !layered && stack_texel(int(own)).w < 0.5) return false;
	if (own < 0.0 && !layered) return false;
	int start = int(stack_camera);
	bool known = true;
	vec3 from = eye;      // where the walk starts, in the sector start
	if (copy > 0.5) {
		// a portal's copy of the far room: Build draws it from the sector beyond the link, the camera moved by the
		// link's offset, so the walk goes there (the copy and the eye moved back to the far side)
		int row = int(copy + 0.5) - 1;
		vec4 far = texelFetch(portal_data, ivec2(FARCOLUMN, row), 0);
		if (far.w < 0.0) known = false;
		else {
			start = int(far.w);
			if (texelFetch(portal_data, ivec2(0, row), 0).w > 3.5) {
				// a mirror's copy: its room reflected in the wall's plane; Build draws it from the eye reflected behind the
				// mirror (mirrorsector), walking in through the mirror into the wall's sector: the pixel and the eye
				// reflected back (the frame: a point of the wall, the way along it, its room on (-along.y, along.x))
				vec4 fr = texelFetch(portal_data, ivec2(FRAMECOLUMN, row), 0);
				vec2 mn = vec2(-fr.w, fr.z);
				w.xz -= 2.0 * dot(w.xz - fr.xy, mn) * mn;
				eye.xz -= 2.0 * dot(eye.xz - fr.xy, mn) * mn;
				from = eye;
			} else {
				// how far the eye and the pixel lie off the link's plane (a wall stack's: in its frame)
				vec4 ph = texelFetch(portal_data, ivec2(0, row), 0);
				vec4 fr = texelFetch(portal_data, ivec2(FRAMECOLUMN, row), 0);
				float de = ph.w > 2.5 ? portal_local(fr, eye).y : eye.y - ph.x;
				float dw = ph.w > 2.5 ? portal_local(fr, w).y : w.y - ph.x;
				w += far.xyz;
				eye += far.xyz;
				from = eye;
				if (de * dw < 0.0) {
					// the walk starts where the line comes through the plane, just past it: in the far sector, or (a
					// floor or ceiling link over several sectors: fr.xy a place in the far sector, fr.z 1) in the one a
					// walk from there along the plane arrives in
					from = eye + (w - eye) * min(de / (de - dw) + 0.0005, 1.0);
					if (ph.w < 2.5 && fr.z > 0.5) {
						int s0 = stack_walk(start, vec2(fr.y, fr.x) * 32.0, vec2(from.z, from.x) * 32.0);
						if (s0 >= 0) start = s0;
					}
				}
			}
		}
	}
	else if (copy != 0.0 || stack_camera < 0.0) known = false;
	if (!known) return stack_own >= 0.0 && stack_hidden;
	// just in front of the pixel (a Quake unit towards the camera): in the room it is drawn from (Godot space is
	// Quake (y, z, x) / 32)
	vec3 q = w + normalize(eye - w) / 32.0;
#ifdef B3D_PLANES
	if (vertex_own && !layered) {
		// outside the bounds of the sectors sharing the ground with its own: the walk can't arrive in one of them
		vec4 oh = stack_texel(int(own));
		vec4 bb = stack_texel(int(oh.z) + int(oh.w));
		vec2 pq = vec2(q.z, q.x) * 32.0, pw = vec2(w.z, w.x) * 32.0;
		if ((pq.x < bb.x || pq.y < bb.y || pq.x > bb.z || pq.y > bb.w) && (pw.x < bb.x || pw.y < bb.y || pw.x > bb.z || pw.y > bb.w))
			return false;
	}
#endif
	int r = stack_walk(start, vec2(from.z, from.x) * 32.0, vec2(q.z, q.x) * 32.0);
	// a face of one layer: where the walk arrives in a sector of the other (too far: the start's)
	if (layered && int(stack_texel(int(stack_groups) + (r >= 0 ? r : start)).x) != int(face_layer)) {
		// a face from Build's sectors: the pixel itself in another layer's too (the step back toward the camera crosses
		// the face's edge where it borders a room of the other layer: DWE1M1's floor 236 beside 239 lost a strip there)
		if (stack_layer >= 0.0) return true;
		int r2 = stack_walk(start, vec2(from.z, from.x) * 32.0, vec2(w.z, w.x) * 32.0);
		if (int(stack_texel(int(stack_groups) + (r2 >= 0 ? r2 : start)).x) != int(face_layer)) return true;
	}
	if (own < 0.0) return false;
	if (r < 0) return vertex_own ? copy < 0.5 && stack_nearer(int(own), w) : stack_hidden;
	// one lower room under several upper ones, or one of several lower sectors under an upper room: whichever of
	// the rooms sharing the ground with the face's own the walk reaches
	if (r == int(own) || !stack_partner(int(own), r)) return false;
	// the step back toward the camera can cross the face's own edge into a partner beside it, where the pixel itself lies
	// in its own sector: that is the face, not the other room's (E1M1's ledge 45 beside the chapel 55: a thin line of sky
	// along its edge)
	return stack_walk(start, vec2(from.z, from.x) * 32.0, vec2(w.z, w.x) * 32.0) != int(own);
}
".Replace("DATAWIDTH", DataWidth.ToString()).Replace("WALKSTEPS", WalkSteps.ToString())
        .Replace("FARCOLUMN", PortalRenderer.FarColumn.ToString()).Replace("FRAMECOLUMN", PortalRenderer.FrameColumn.ToString());
    static int[] _dist = System.Array.Empty<int>();
    static int _camSector = -1;
    static Image _distImage;
    static ImageTexture _distTex;
    // the sectors sharing the ground with each (stacked in height, or sharing it in plan: SectorMap.Overlaps)
    static List<int>[] _partners = System.Array.Empty<List<int>>();

    /// <summary>Does Build sector s share the ground with another (the walk may hide its faces; not an occluder)?</summary>
    public static bool SharesGround(int s) => s >= 0 && s < _partners.Length && _partners[s].Count > 0;
    static readonly Queue<int> _queue = new();

    /// <summary>The Build sector holding p (Quake units; SectorMap.SectorAt), -1 if none or no shading data.</summary>
    public static int SectorAt(Vector3 q, int layer = -1) => _shade?.SectorAt(q, -1, layer) ?? -1;

    /// <summary>The sector beyond a link's linked plane (<see cref="SectorMap.SectorBeyond"/>).</summary>
    public static int SectorBeyond(Vector3 q, int dir, float plane, int layer = -1) =>
        _shade?.SectorBeyond(q, dir, plane, layer) ?? -1;

    /// <summary>Before a level's materials are set up.</summary>
    public static void Clear()
    {
        _mats.Clear();
        _bySurface.Clear();
        _spriteSector.Clear();
        _shade = null;
        _camSector = -1;
        _dist = System.Array.Empty<int>();
        RenderingServer.GlobalShaderParameterSet(CameraName, -1f);
        RenderingServer.GlobalShaderParameterSet(GroupsName, -1f);
    }

    /// <summary>links: the level's (sectors links join without moving, rooms truly over each other, as
    /// Forge's Add Floor makes them, are no partners: the link shows the real room above or below, and through a stack
    /// of them the walk from the middle room hid the top one: report 20261105-143410).</summary>
    public static void Setup(Sim.SectorMap shade, IReadOnlyList<Sim.Collision.Link> links = null)
    {
        _shade = shade;
        _pairs.Clear();
        int n = shade?.SectorCount ?? 0;
        _partners = new List<int>[n];
        for (int i = 0; i < n; i++)
            _partners[i] = new List<int>();
        // (a chain of them too: three floors, the bottom and the top no partners either)
        var group = new int[n];
        for (int i = 0; i < n; i++)
            group[i] = i;
        int Find(int a) => group[a] == a ? a : group[a] = Find(group[a]);
        if (links != null)
            foreach (var l in links)
                if (l.Portal && l.ConvertedPortal && l.Sector >= 0 && l.FarSector >= 0 && l.Sector < n && l.FarSector < n && l.Offset.LengthSquared() < 0.25f)
                    group[Find(l.Sector)] = Find(l.FarSector);
        bool Apart(int a, int b) => a >= 0 && b >= 0 && a < n && b < n && Find(a) == Find(b);
        if (shade != null)
        {
            foreach (var p in shade.Stacks)
            {
                if (Apart(p.lower, p.upper))
                    continue;
                _pairs.Add(p);
                AddPartners(p.lower, p.upper);
            }
            foreach (var (a, b) in shade.Overlaps)
                if (!Apart(a, b))
                    AddPartners(a, b);
        }
        _camSector = -1;
        _dist = new int[n];
        System.Array.Fill(_dist, int.MaxValue);
        _distImage = null;
        RenderingServer.GlobalShaderParameterSet(CameraName, -1f);
        RenderingServer.GlobalShaderParameterSet(GroupsName, -1f);
        if (shade != null && (shade.Stacks.Count > 0 || shade.HasLayers || shade.Overlaps.Count > 0))
        {
            WriteData(shade);
            int h = (n + DataWidth - 1) / DataWidth;
            _distImage = Image.CreateEmpty(DataWidth, System.Math.Max(h, 1), false, Image.Format.Rf);
            _distTex = ImageTexture.CreateFromImage(_distImage);
            RenderingServer.GlobalShaderParameterSet(DistName, _distTex);
        }
    }

    /// <summary>The walk's walls again where the level's walls moved (a level from its Build data: TranslateSector, DragPoint).</summary>
    public static void RefreshWalls(Sim.SectorMap shade)
    {
        if (shade == null || _shade != shade || _data == null)
            return;
        WriteData(shade);
    }

    static void AddPartners(int a, int b)
    {
        if (a < 0 || b < 0 || a >= _partners.Length || b >= _partners.Length || a == b)
            return;
        if (!_partners[a].Contains(b))
            _partners[a].Add(b);
        if (!_partners[b].Contains(a))
            _partners[b].Add(a);
    }

    /// <summary>stack_walls: every sector's walls and stacked partners for the shaders' walk, then (a level with
    /// layers) every sector's layer and whether it is one of the rooms sharing space (seen only in its layer).</summary>
    static float[] _dataBuf = System.Array.Empty<float>();
    static byte[] _dataBytes = System.Array.Empty<byte>();

    static void WriteData(Sim.SectorMap shade)
    {
        int n = shade.SectorCount, total = n + (shade.HasLayers ? n : 0);
        var partners = _partners;
        for (int i = 0; i < n; i++)
        {
            total += partners[i].Count + 1;
            foreach (var (_, _, next) in shade.WallsOf(i))
                if (next >= 0)
                    total += 2;
        }
        int h = (total + DataWidth - 1) / DataWidth;
        // (written into floats and sent whole: a level from its Build data writes it again each game frame its walls
        // move, a SetPixel per texel cost thousands of engine calls)
        if (_dataBuf.Length != DataWidth * h * 4)
            _dataBuf = new float[DataWidth * h * 4];
        else
            System.Array.Clear(_dataBuf);
        var buf = _dataBuf;
        int at = n;
        void Set(int i, Color c)
        {
            buf[4 * i] = c.R;
            buf[4 * i + 1] = c.G;
            buf[4 * i + 2] = c.B;
            buf[4 * i + 3] = c.A;
        }
        for (int i = 0; i < n; i++)
        {
            int first = at;
            foreach (var (p1, p2, next) in shade.WallsOf(i))
            {
                if (next < 0)
                    continue;
                Set(at++, new Color(p1.X, p1.Y, p2.X, p2.Y));
                Set(at++, new Color(next, 0, 0, 0));
            }
            int walls = (at - first) / 2, firstPartner = at;
            float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
            foreach (int o in partners[i])
            {
                Set(at++, new Color(o, 0, 0, 0));
                foreach (var (p1, p2, _) in shade.WallsOf(o))
                {
                    x0 = Mathf.Min(x0, Mathf.Min(p1.X, p2.X));
                    y0 = Mathf.Min(y0, Mathf.Min(p1.Y, p2.Y));
                    x1 = Mathf.Max(x1, Mathf.Max(p1.X, p2.X));
                    y1 = Mathf.Max(y1, Mathf.Max(p1.Y, p2.Y));
                }
            }
            // the partners' bounds, 2 units wider (none: nothing inside)
            const float Margin = 2;
            Set(at++, partners[i].Count > 0 ? new Color(x0 - Margin, y0 - Margin, x1 + Margin, y1 + Margin) : new Color(1, 1, -1, -1));
            Set(i, new Color(first, walls, firstPartner, partners[i].Count));
        }
        if (shade.HasLayers)
        {
            for (int i = 0; i < n; i++)
                Set(at + i, new Color(shade.GroupOf(i), shade.IsLayered(i) ? 1 : 0, 0, 0));
            RenderingServer.GlobalShaderParameterSet(GroupsName, (float)at);
        }
        if (_dataBytes.Length != buf.Length * 4)
            _dataBytes = new byte[buf.Length * 4];
        System.Buffer.BlockCopy(buf, 0, _dataBytes, 0, _dataBytes.Length);
        var img = Image.CreateFromData(DataWidth, h, false, Image.Format.Rgbaf, _dataBytes);
        if (_data != null && _data.GetWidth() == DataWidth && _data.GetHeight() == h)
            _data.Update(img);      // (the same texture: the global left as it is, setting it again costs every material)
        else
        {
            _data = ImageTexture.CreateFromImage(img);
            RenderingServer.GlobalShaderParameterSet(WallsName, _data);
        }
    }

    static void Mark(ShaderMaterial m, int own) => m.SetShaderParameter(OwnName, own);

    /// <summary>A surface's material: its own copy that the camera's side hides, for a tagged face (stacked rooms'
    /// _v&lt;own&gt;_&lt;other&gt;, a layer's _g&lt;layer&gt;).</summary>
    public static Material ForSurface(string surf, Material mat)
    {
        if (mat is not ShaderMaterial sm)
            return mat;
        string bare = Sim.Collision.Contents.StripLayer(surf, out int layer);
        // a face of stacked rooms seen from one side, or a masked or one-way wall of a sector sharing ground (its own
        // sector: hidden where the camera's walk reaches another)
        var m = bare.StartsWith("blood_above/") || bare.StartsWith("blood_below/") || bare.StartsWith("blood_flat/")
            || bare.StartsWith("blood_oneway/") || bare.StartsWith("blood_masked/") ? Tag.Match(bare) : null;
        if (layer < 0 && (m == null || !m.Success))
            return mat;
        if (!_bySurface.TryGetValue(surf, out var own))
        {
            own = (ShaderMaterial)sm.Duplicate();
            _bySurface[surf] = own;
            if (m != null && m.Success)
            {
                var others = new[] { int.Parse(m.Groups[2].Value) };
                _mats.Add((own, int.Parse(m.Groups[1].Value), others));
                Mark(own, int.Parse(m.Groups[1].Value));
            }
            if (layer >= 0)
                own.SetShaderParameter(LayerName, (float)layer);
        }
        return own;
    }

    /// <summary>Marks a material as one layer's (a mover or skin built from a layer's faces keeps its own).</summary>
    public static void SetLayer(ShaderMaterial m, int layer) => m.SetShaderParameter(LayerName, (float)layer);

    /// <summary>A copy of a tagged material (a light-effect skin's own) is hidden with it.</summary>
    public static void Adopt(ShaderMaterial from, ShaderMaterial copy)
    {
        var layer = from.GetShaderParameter(LayerName);
        if (layer.VariantType == Variant.Type.Float && layer.AsSingle() >= 0)
            copy.SetShaderParameter(LayerName, layer);
        foreach (var (mat, own, others) in _mats)
            if (mat == from)
            {
                _mats.Add((copy, own, others));
                Mark(copy, own);
                _camSector = -1;
                return;
            }
    }

    /// <summary>Build sector s's layer when it is one of the rooms sharing space, else -1.</summary>
    public static int LayerOf(int sector) => _shade != null && _shade.IsLayered(sector) ? _shade.GroupOf(sector) : -1;

    /// <summary>Is Build sector s stacked over or under another in height?</summary>
    public static bool IsStacked(int sector)
    {
        if (_shade == null)
            return false;
        foreach (var (lo, hi) in _shade.Stacks)
            if (lo == sector || hi == sector)
                return true;
        return false;
    }

    /// <summary>A moving part of a sector stacked over or under another (E1M3's sliding door in the upper corridor
    /// hung into the room below): its materials are hidden while the camera is nearer to the other room.</summary>
    public static void RegisterMover(IEnumerable<ShaderMaterial> mats, int sector)
    {
        if (_shade == null)
            return;
        var others = new List<int>();
        foreach (var (lo, hi) in _shade.Stacks)
        {
            if (lo == sector)
                others.Add(hi);
            else if (hi == sector)
                others.Add(lo);
        }
        if (others.Count == 0)
            return;
        var arr = others.ToArray();
        foreach (var m in mats)
        {
            _mats.Add((m, sector, arr));
            Mark(m, sector);
        }
        _camSector = -1;      // set them at the next update
    }

    /// <summary>Follows the camera (Quake units; layer: the viewer's, where rooms share space; sector: its Build sector
    /// when the game knows it, the player's view: else followed here): when its sector changes, the steps to every
    /// sector and what the tagged faces show.</summary>
    /// <summary>The camera's Build sector (-1: unknown), as the last Update found it.</summary>
    public static int Sector => _camSector;

    static readonly Core.Cvar WalkOn = Core.Cvars.Register("r_stackwalk", "1",
        "0: the stacked rooms' walk per pixel left out (debugging: what it costs; rooms sharing a place show through each other)");

    public static void Update(Vector3 camQ, int layer = -1, int sector = -1)
    {
        if (_shade == null)
            return;
        if (!WalkOn.Bool)
        {
            RenderingServer.GlobalShaderParameterSet(CameraName, -1f);
            _camSector = -1;
            return;
        }
        _camLayer = layer;
        int sec = sector >= 0 && _forcedSector < 0 ? sector : CameraSector(camQ);
        // in a sliding or turning sector away from where it is stored: the walk (the walls as stored) starts from the
        // room around it as stored (DWE3M1's boat on its way: walked from its stored outline back at the dock, all
        // but what lay through that was hidden, the river and its shore white)
        if (sec >= 0 && _shade.Moving != null && _shade.Moving(sec) && !_shade.HoldsStored(sec, camQ, _camLayer))
        {
            int st = _shade.StoredAt(camQ, _camLayer);
            if (st >= 0)
                sec = st;
        }
        _camPos = camQ;
        if (sec < 0 || sec == _camSector)
            return;
        _camSector = sec;
        if (_mats.Count == 0 && _shade.Stacks.Count == 0 && !_shade.HasLayers && _shade.Overlaps.Count == 0)
            return;
        RenderingServer.GlobalShaderParameterSet(CameraName, (float)sec);
        System.Array.Fill(_dist, int.MaxValue);
        _dist[sec] = 0;
        _queue.Clear();
        _queue.Enqueue(sec);
        while (_queue.Count > 0)
        {
            int s = _queue.Dequeue();
            foreach (int n in _shade.Neighbours[s])
                if (_dist[n] == int.MaxValue)
                {
                    _dist[n] = _dist[s] + 1;
                    _queue.Enqueue(n);
                }
        }
        foreach (var (mat, own, others) in _mats)
        {
            bool hidden = false;
            foreach (int o in others)
                hidden |= OtherSide(own, o);
            mat.SetShaderParameter(HiddenName, hidden);
        }
        if (_distImage != null)
        {
            int n = _dist.Length;
            for (int i = 0; i < n; i++)
                _distImage.SetPixel(i % DataWidth, i / DataWidth, new Color(_dist[i] == int.MaxValue ? 1e9f : _dist[i], 0, 0));
            _distTex.Update(_distImage);
        }
    }

    /// <summary>The camera's sector and each stacked pair's side (debugging: the stacks command).</summary>
    public static IEnumerable<string> Describe()
    {
        yield return $"camera sector {_camSector}, {_mats.Count} tagged materials";
        if (_shade == null)
            yield break;
        foreach (var (lo, hi) in _shade.Stacks)
            yield return $"  lower {lo} (steps {Steps(lo)}) upper {hi} (steps {Steps(hi)}): {(OtherSide(hi, lo) ? "lower" : "upper")} side";
        for (int s = 0; s < _partners.Length; s++)
            if (_partners[s].Count > 0)
                yield return $"  sector {s} shares the ground with {string.Join(" ", _partners[s])}";
    }

    static string Steps(int s) => s < _dist.Length && _dist[s] != int.MaxValue ? _dist[s].ToString() : "-";

    static Vector3 _camPos;
    static int _camLayer = -1, _forcedSector = -1;

    /// <summary>The camera's sector for the next update, given (a comparison view's: rooms sharing space, stacked
    /// rooms), then followed as it moves.</summary>
    public static void ForceSector(int sector) => _forcedSector = sector;

    /// <summary>
    /// The camera's Build sector as Build keeps a moving sprite's: the same while its outline still holds the camera
    /// (whatever else stacks there), else the one beyond the two-sided walls the move crossed. A jump (a teleport, a
    /// link, a new view) looks it up afresh: the sector whose column holds the camera, the highest floor first.
    /// </summary>
    static int CameraSector(Vector3 p)
    {
        if (_forcedSector >= 0)
        {
            int f = _forcedSector;
            _forcedSector = -1;
            return f;
        }
        // a sliding or turning sector (a train car, a turning cabin) where its mover has it now; leaving one, a fresh
        // look: its stored outline is no place to walk from
        if (_shade.MovingAt != null)
        {
            int mv = _shade.MovingAt(p, _camLayer);
            if (mv >= 0)
                return mv;
            if (_camSector >= 0 && _shade.Moving(_camSector))
                return _shade.SectorAt(p, -1, _camLayer);
        }
        if (_camSector < 0 || p.DistanceTo(_camPos) > 64)
            return _shade.SectorAt(p, -1, _camLayer);
        var a = new Vector2(_camPos.X, _camPos.Y);
        var b = new Vector2(p.X, p.Y);
        int sec = _camSector;
        for (int i = 0; i < 8 && !_shade.InSector(sec, b.X, b.Y); i++)
        {
            int next = _shade.CrossInto(sec, a, b, out var at);
            if (next < 0 || _shade.Moving != null && _shade.Moving(next))
                return _shade.SectorAt(p, sec, _camLayer);
            sec = next;
            a = at + (b - at) * 0.001f;
        }
        return sec;
    }

    /// <summary>Does the camera look from sector other's side rather than own's? From either of them, from its own;
    /// from another room stacked with one of them in the same role (another upper room over the same lower room:
    /// E1M6's stairs over the great hall, beside the walkway over it), from the side of that role; else from the
    /// one fewer steps through two-sided walls away.</summary>
    static bool OtherSide(int own, int other)
    {
        int c = _camSector;
        if (c == own)
            return false;
        if (c == other)
            return true;
        if (_pairs.Contains((other, own)))         // own is the upper room
        {
            if (_pairs.Contains((other, c)))
                return false;
            if (_pairs.Contains((c, own)))
                return true;
        }
        else if (_pairs.Contains((own, other)))    // own is the lower room
        {
            if (_pairs.Contains((c, other)))
                return false;
            if (_pairs.Contains((own, c)))
                return true;
        }
        return own < _dist.Length && other < _dist.Length && _dist[other] < _dist[own];
    }

    static readonly HashSet<(int lower, int upper)> _pairs = new();
    const float SpriteReach = 8;     // how near the room it stacks with a sprite counts as in it (Quake units)

    // each sprite's Build sector, followed as it moves (kept while its column still holds it)
    static readonly Dictionary<Sim.Sprite, (Vector3 at, int sec)> _spriteSector = new();

    /// <summary>A sprite (at Quake position p) of a stacked room, inside the room it stacks with, while the camera
    /// looks from that other room (the upper corridor's lamps and gibs showed through the ceiling of the room under
    /// it). Its room is its Build sector: the map's for what is placed there (a lamp on a low ceiling inside the
    /// shared heights is the lower room's), then the sector whose column holds it as it moves.</summary>
    public static bool SpriteHidden(Sim.Sprite s, Vector3 p) => SpriteHidden(s, p, _camSector, _camPos);

    /// <summary>The floor or ceiling link a copy's sprites are being drawn through: its plane in the linked room (Quake
    /// z; NaN: none) and a place in the sector beyond it (Quake x, y; NaN: unknown).</summary>
    public static float ViaPlane = float.NaN;
    public static Vector2 ViaStart = new(float.NaN, 0);

    /// <summary>A sprite's Build sector as the game keeps it (Sim.World.PeekSpriteBSector), -1 if unknown.</summary>
    public static System.Func<Sim.Sprite, int> SpriteSectorOf;

    /// <summary>SpriteHidden for Build's walk from sector from, the eye at eye (Quake): a portal's copy of the far room
    /// walks from the sector beyond the link, the eye moved by its offset. mirror: a mirror's copy (the camera's own
    /// visibility says nothing there).</summary>
    public static bool SpriteHidden(Sim.Sprite s, Vector3 p, int from, Vector3 eye, bool mirror = false)
    {
        if (_shade == null || from < 0 || (_shade.Stacks.Count == 0 && _shade.Overlaps.Count == 0 && !_shade.HasLayers))
            return false;
        // a floor or ceiling link's copy: the walk from where the eye's line comes through the linked plane, as the
        // shader's stack_discard (ViaPlane, ViaStart: SpriteRenderer sets them for the portal it draws through)
        if (!mirror && !float.IsNaN(ViaPlane) && (eye.Z - ViaPlane) * (p.Z - ViaPlane) < 0)
        {
            eye += (p - eye) * System.Math.Min((eye.Z - ViaPlane) / (eye.Z - p.Z) + 0.0005f, 1f);
            if (!float.IsNaN(ViaStart.X))
            {
                int s0 = _shade.Walk(from, ViaStart, new Vector2(eye.X, eye.Y));
                if (s0 >= 0)
                    from = s0;
            }
        }
        // (the sprites' world has no layers of its own: a sprite counts as in layer 0 where rooms share space)
        int layer = _shade.HasLayers ? 0 : -1;
        if (layer >= 0)
        {
            // where rooms share space: hidden where Build's walk from the camera reaches the other layer's room; and
            // a sprite just behind a wall of the other layer's room (E2M2's life seed in 263, 4 units past 81's wall,
            // poked through it), where the walk stops in that room: Build never reaches the sprite's sector
            var a = new Vector2(eye.X, eye.Y);
            var b = new Vector2(p.X, p.Y);
            var toward = a - b;
            bool inOther = _shade.InOtherLayer(p.X, p.Y, layer);
            if (inOther || _shade.InLayered(p.X, p.Y))
            {
                int r = _shade.Walk(from, a, toward.Length() > 1 ? b + toward.Normalized() : b, out int stopped);
                if (inOther && _shade.GroupOf(r >= 0 ? r : from) != layer)
                    return true;
                if (stopped >= 0 && _shade.IsLayered(stopped) && _shade.GroupOf(stopped) != layer)
                    return true;
            }
        }
        if (_shade.Stacks.Count == 0 && _shade.Overlaps.Count == 0)
            return false;
        int sec;
        bool seen = _spriteSector.TryGetValue(s, out var known);
        int tracked = SpriteSectorOf?.Invoke(s) ?? -1;
        if (tracked >= 0)
            sec = tracked;        // the game's: Build's sector of it (World.SpriteBSector)
        else if (seen && known.at == p)
            sec = known.sec;      // not moved since
        else if (s.MoveWithEnt != null && s.MoveWithEnt.BSector > 0 && s.MoveWithEnt.BSector <= _shade.SectorCount)
        {
            // TranslateSector keeps a carried sprite in its sector wherever the turn takes it (E2M5's revolving
            // bookcase: its emblem passes over the room sharing its space, 21, and stays the bookcase's)
            sec = s.MoveWithEnt.BSector - 1;
            _spriteSector[s] = (p, sec);
        }
        else
        {
            sec = seen ? known.sec : s.BSector > 0 && s.BSector <= _shade.SectorCount ? s.BSector - 1 : -1;
            int now = _shade.SectorAt(p, sec, layer);
            if (now >= 0)
                sec = now;
            _spriteSector[s] = (p, sec);
        }
        if (sec < 0 || sec >= _partners.Length)
            return false;
        // a sector the camera can't see this frame (SectorVisibility, which counts the rooms sharing ground or stacked
        // as seen together): behind the walls, nothing to decide (most of the sprites, each a walk from the camera); a
        // mirror's copy looks back into the room behind the camera, which it needn't see (report 20261105-220910: a zombie
        // lying in 16, sharing ground with 0 beyond the mirror's room, showed in the mirror)
        if (!mirror && !SectorVisibility.Seen(sec))
            return false;
        int walked = -2;
        foreach (int other in _partners[sec])
        {
            // (or on its wall line: E1M5's candle of 132 hangs on the barrel room 203's rack wall, half of it in 203)
            if (!_shade.NearSector(other, p.X, p.Y, SpriteReach))
                continue;
            // Build's walk from the camera to just in front of the sprite: hidden where it reaches a room stacked
            // with the sprite's
            if (walked == -2)
            {
                var a = new Vector2(eye.X, eye.Y);
                var b = new Vector2(p.X, p.Y);
                var toward = a - b;
                walked = _shade.Walk(from, a, toward.Length() > 1 ? b + toward.Normalized() : b);
                if (walked >= 0)
                    return walked != sec && _partners[sec].Contains(walked);
            }
            if (OtherSide(sec, other))
                return true;
        }
        return false;
    }

    /// <summary>Where rooms share space: is a face of the given layer (-1: of both) at p (Quake) hidden, Build's walk
    /// from the camera's sector arriving there in a sector of the other layer (too far: the start's), as the shader's
    /// stack_discard hides a tagged face. (A link's portal: E3M1's shaft 87, layer 1, over the bar 381: from the bar
    /// its floor portal showed the pit 85 under it.)</summary>
    public static bool LayerHidden(int layer, Vector3 p)
    {
        if (layer < 0 || _shade == null || !_shade.HasLayers || _camSector < 0)
            return false;
        var a = new Vector2(_camPos.X, _camPos.Y);
        var b = new Vector2(p.X, p.Y);
        var toward = a - b;
        int r = _shade.Walk(_camSector, a, toward.Length() > 1 ? b + toward.Normalized() : b);
        return _shade.GroupOf(r >= 0 ? r : _camSector) != layer;
    }

    /// <summary>stack_discard on the CPU for a level face (debugging: pickshade skips what the shader drops): surface
    /// surf's pixel at p (Quake), seen from the camera.</summary>
    public static bool FaceHidden(string surf, Vector3 p)
    {
        var m = Tag.Match(surf);
        if (_shade == null || _camSector < 0 || !m.Success)
            return false;
        int own = int.Parse(m.Groups[1].Value);
        var a = new Vector2(_camPos.X, _camPos.Y);
        var b = new Vector2(p.X, p.Y);
        var toward = a - b;
        int r = _shade.Walk(_camSector, a, toward.Length() > 1 ? b + toward.Normalized() : b);
        if (r < 0)
            return OtherSide(own, int.Parse(m.Groups[2].Value));
        return r != own && (_pairs.Contains((own, r)) || _pairs.Contains((r, own)));
    }

    /// <summary>stack_discard on the CPU for a face the sectors draw (its own Build sector, LiveRenderer.EmitOwn; debugging:
    /// pickshade skips what the shader drops). why: where the walk from the camera's sector ended.</summary>
    public static bool SectorFaceHidden(int own, Vector3 p, out string why)
    {
        why = "";
        if (_shade == null || _camSector < 0 || own < 0 || own >= _partners.Length)
            return false;
        var a = new Vector2(_camPos.X, _camPos.Y);
        var b = new Vector2(p.X, p.Y);
        var toward = a - b;
        int r = _shade.Walk(_camSector, a, toward.Length() > 1 ? b + toward.Normalized() : b);
        why = $"walk from {_camSector} ends in {r}";
        if (_shade.HasLayers && _shade.IsLayered(own) && _shade.GroupOf(r >= 0 ? r : _camSector) != _shade.GroupOf(own))
        {
            int r2 = _shade.Walk(_camSector, a, b);
            why += $", to the pixel in {r2}";
            if (_shade.GroupOf(r2 >= 0 ? r2 : _camSector) != _shade.GroupOf(own))
                return true;
        }
        if (_partners[own].Count == 0)
            return false;
        if (r < 0)
            return _partners[own].Exists(o => _dist[o] < _dist[own]);
        return r != own && _partners[own].Contains(r);
    }

    /// <summary>A sprite gone from the level.</summary>
    public static void Forget(Sim.Sprite s) => _spriteSector.Remove(s);
}
