using System.Collections.Generic;
using System.Linq;
using Chosen.Map;
using Chosen.Sim;
using Godot;

namespace Chosen.Render;

/// <summary>
/// Room-over-room portals (Blood's tile 504 link sides): the converter gives a portal side's blood_link slab a
/// tools/portal face towards its room. A link is a shift, so the linked room is drawn in the same pass as everything
/// else: a copy of what lies beyond the linked plane around the opening, moved by the link's offset so it sits right
/// behind the portal face, and shown only where the line from the eye crosses the face (the shaders' clip test,
/// <see cref="Clip"/>). What really lies behind the face (the void under a floor, another room) is hidden there. The
/// static level is copied once (cropped to a box around the opening); movers and stretching walls are mirrored each
/// frame, sprites by the SpriteRenderer. A copy is marked by its vertex colour's blue channel (the portal's row in
/// portal_data plus one; the level's own meshes have 0), sprites by their portal_copy instance uniform.
/// </summary>
public partial class PortalRenderer : Node3D
{
    public const int Slots = 4;                  // portals drawn at once (the nearest in view)
    const int MaxPortals = 64;
    const int MaxTris = 126;                     // footprint triangles a portal keeps (more: its bounds alone)
    /// <summary>portal_data's column holding a portal's offset (Godot) and the Build sector beyond its linked plane.</summary>
    public const int FarColumn = 2 + 2 * MaxTris;
    /// <summary>portal_data's column holding a wall portal's frame (Godot x, z): a point of its wall and the way along it.</summary>
    public const int FrameColumn = FarColumn + 1;
    const int DataWidth = FrameColumn + 1;
    const float Region = 2048f / Coords.QuakeScale;       // how far around and beyond the opening the copy reaches

    /// <summary>The clip test for spatial shaders: <c>if (portal_clipped(world_pos, copy, CAMERA_POSITION_WORLD)) discard;</c>
    /// copy: 0 the level itself (hidden where an active portal's face covers it), k a copy of the far side of portal
    /// row k-1 (only seen through it), -1 never clipped (a sprite reaching through the plane).</summary>
    public static readonly string Clip = @"
// the fragment's world position for light() (dynamic lights' room-over-room regions), set by the lit shaders
varying vec3 light_world;
varying float light_sky;      // does the light from the sky reach here (BuildShading.LightFunction)? 0: its shadows' leaks only
varying float light_lit;      // does any light fall on this? 0: a picture that is itself a light
// the portals drawn this frame, a slot each: (plane y, side, row in portal_data, 1 drawn / 0 unused) and the face's
// bounds (x, z, x, z), so most fragments are decided without reading portal_data
global uniform vec4 portal_slot_0;
global uniform vec4 portal_slot_1;
global uniform vec4 portal_slot_2;
global uniform vec4 portal_slot_3;
global uniform vec4 portal_box_0;
global uniform vec4 portal_box_1;
global uniform vec4 portal_box_2;
global uniform vec4 portal_box_3;
// a wall portal's frame per slot (as its row's FRAMECOLUMN texel)
global uniform vec4 portal_frame_0;
global uniform vec4 portal_frame_1;
global uniform vec4 portal_frame_2;
global uniform vec4 portal_frame_3;
// a row per portal: (plane y, side: 1 a floor portal / -1 a ceiling portal, triangles, 1 of water / 2 joining two rooms
// under water / 3 a wall stack / 4 a mirror / 0), its face's bounds
// (x, z, x, z), then each footprint triangle in two texels (ax, az, bx, bz), (cx, cz, -, -); Godot space. Its last
// texel: the link offset (a copy lies at the far side's place minus it) and the Build sector beyond (-1: unknown); then a
// floor or ceiling portal's place in that sector (x, z in the linked room; its z 1 when known: StackedRooms' walk), a wall
// portal's (w 3, 4) its frame (x, z of a point of its wall, x, z of the way along it): in the frame (along, out from
// the wall into its room, height) its plane is level, y 0 with its room above (side 1), its face's bounds and triangles
// (along, height)
global uniform sampler2D portal_data : filter_nearest;
vec3 portal_local(vec4 f, vec3 p) {
	vec2 d = p.xz - f.xy;
	return vec3(dot(d, f.zw), dot(d, vec2(-f.w, f.z)), p.y);
}
// a 64 x 64 cell mask per portal over its face's bounds (rows of 64 per portal): 1 inside the face, 0 outside, 0.5 on
// its edge (only there the triangles are tested)
global uniform sampler2D portal_mask : filter_nearest;
bool portal_in_tri(vec2 p, vec2 a, vec2 b, vec2 c) {
	float d1 = (p.x - b.x) * (a.y - b.y) - (a.x - b.x) * (p.y - b.y);
	float d2 = (p.x - c.x) * (b.y - c.y) - (b.x - c.x) * (p.y - c.y);
	float d3 = (p.x - a.x) * (c.y - a.y) - (c.x - a.x) * (p.y - a.y);
	return !(((d1 < 0.0) || (d2 < 0.0) || (d3 < 0.0)) && ((d1 > 0.0) || (d2 > 0.0) || (d3 > 0.0)));
}
// does the line from the eye to w cross the face (plane h.x, side h.y, bounds bb) of portal row k, w lying beyond its
// plane (closer to it than lim)?
bool portal_crosses(int k, vec4 h, vec4 bb, vec3 eye, vec3 w, float lim) {
	float de = (eye.y - h.x) * h.y, dw = (w.y - h.x) * h.y;
	if (de <= 0.0 || dw >= lim)
		return false;
	vec3 p = eye + (w - eye) * (de / (de - dw));
	if (p.x < bb.x || p.z < bb.y || p.x > bb.z || p.z > bb.w)
		return false;
	vec2 cell = clamp((p.xz - bb.xy) / max(bb.zw - bb.xy, vec2(1e-6)) * 64.0, vec2(0.0), vec2(63.0));
	float m = texelFetch(portal_mask, ivec2(int(cell.x), k * 64 + int(cell.y)), 0).r;
	if (m < 0.25)
		return false;
	if (m > 0.75)
		return true;
	int n = int(texelFetch(portal_data, ivec2(0, k), 0).z);
	if (n == 0)
		return true;
	for (int i = 0; i < n; i++) {
		vec4 t0 = texelFetch(portal_data, ivec2(2 + 2 * i, k), 0);
		vec4 t1 = texelFetch(portal_data, ivec2(3 + 2 * i, k), 0);
		if (portal_in_tri(p.xz, t0.xy, t0.zw, t1.xy))
			return true;
	}
	return false;
}
// (a water portal, w 2, hides nothing of the level: under a water surface lies the void, or what sinks into it, a
// boat's hull below the river's plane; a wall portal, w 3 or 4, is tested in its frame f)
bool portal_slot(vec4 h, vec4 bb, vec4 f, vec3 eye, vec3 w) {
	if (h.w > 2.5)
		return portal_crosses(int(h.z + 0.5), h, bb, portal_local(f, eye), portal_local(f, w), -0.001);
	return h.w > 0.5 && h.w < 1.5 && portal_crosses(int(h.z + 0.5), h, bb, eye, w, -0.001);
}
bool portal_clipped(vec3 w, float copy, vec3 eye) {
	if (copy < -0.5)
		return false;
	// a copy: through its portal only (what lies on the plane stays: a lid's face seen through it)
	if (copy > 0.5) {
		int k = int(copy + 0.5) - 1;
		vec4 h = texelFetch(portal_data, ivec2(0, k), 0);
		if (h.w > 2.5) {
			vec4 f = texelFetch(portal_data, ivec2(FRAMECOLUMN, k), 0);
			eye = portal_local(f, eye);
			w = portal_local(f, w);
		}
		return !portal_crosses(k, h, texelFetch(portal_data, ivec2(1, k), 0), eye, w, 0.001);
	}
	return portal_slot(portal_slot_0, portal_box_0, portal_frame_0, eye, w) || portal_slot(portal_slot_1, portal_box_1, portal_frame_1, eye, w)
		|| portal_slot(portal_slot_2, portal_box_2, portal_frame_2, eye, w) || portal_slot(portal_slot_3, portal_box_3, portal_frame_3, eye, w);
}
// how far w lies off portal row k's plane (a wall portal's in its frame)
float portal_off_plane(int k, vec3 w) {
	vec4 h = texelFetch(portal_data, ivec2(0, k), 0);
	return h.w > 2.5 ? abs(portal_local(texelFetch(portal_data, ivec2(FRAMECOLUMN, k), 0), w).y) : abs(w.y - h.x);
}
// is w on an active wall portal's face where the eye sees through it (slot h, its bounds bb and frame f)?
bool wall_face_open(vec4 h, vec4 bb, vec4 f, vec3 eye, vec3 w) {
	if (h.w < 2.5)
		return false;
	vec3 lw = portal_local(f, w);
	return abs(lw.y) < 0.02 && portal_crosses(int(h.z + 0.5), h, bb, portal_local(f, eye), lw, 0.02);
}
// a copy's mark in its vertex colour (blue: the portal's row + 1; the level's meshes 0)
float portal_mark(vec4 colour) {
	return floor(colour.b * 255.0 + 0.5);
}
".Replace("FRAMECOLUMN", FrameColumn.ToString());
    /// <summary>The world position of a fragment, for <see cref="Clip"/>.</summary>
    public const string WorldPos = "(INV_VIEW_MATRIX * vec4(VERTEX, 1.0)).xyz";

    static readonly StringName DataName = "portal_data", MaskName = "portal_mask";
    const int MaskCells = 64;
    ImageTexture _mask;
    static readonly StringName[] SlotNames = { "portal_slot_0", "portal_slot_1", "portal_slot_2", "portal_slot_3" };
    static readonly StringName[] BoxNames = { "portal_box_0", "portal_box_1", "portal_box_2", "portal_box_3" };
    static readonly StringName[] FrameNames = { "portal_frame_0", "portal_frame_1", "portal_frame_2", "portal_frame_3" };
    readonly Vector4[] _slotsSent = new Vector4[Slots];

    /// <summary>A portal drawn this frame, for the sprites' copies (Quake space).</summary>
    public readonly struct ActivePortal
    {
        public readonly int Mark;              // the copies' mark (row + 1)
        public readonly Vector3 Offset;        // the link offset: linked room = this room + Offset
        public readonly Aabb Region;           // the linked room around the opening, beyond its plane (linked space)
        public readonly float LinkedPlane;     // the plane in the linked room (z)
        public readonly int Dir;               // 1 a floor portal (the linked room below), -1 a ceiling portal
        public readonly Aabb Face;             // the face (this room)
        public readonly int FarSector;         // the Build sector beyond the linked plane (-1: unknown)
        public readonly bool Water;            // a water surface drawn see-through (Enhanced.WaterPortals)
        public readonly IReadOnlyList<Rect2> FaceRects;   // a water portal's face triangles' bounds (Quake x, y), else null
        public readonly bool Wall, Mirror;     // a wall portal (its linked plane NaN), a mirror
        public readonly Vector2 FarAt, Out;    // a wall portal: a point of the far wall and the way out of its room (Godot x, z)
        public readonly Vector2 FarStart;      // a floor or ceiling portal: a place in FarSector (Quake x, y; NaN: unknown)

        public ActivePortal(int mark, Vector3 offset, Aabb region, float linkedPlane, int dir, Aabb face, int farSector,
            bool water = false, IReadOnlyList<Rect2> faceRects = null, bool wall = false, bool mirror = false,
            Vector2 farAt = default, Vector2 outward = default, Vector2? farStart = null)
        {
            FarStart = farStart ?? new Vector2(float.NaN, 0);
            Wall = wall;
            Mirror = mirror;
            FarAt = farAt;
            Out = outward;
            FarSector = farSector;
            Water = water;
            FaceRects = faceRects;
            Mark = mark;
            Offset = offset;
            Region = region;
            LinkedPlane = linkedPlane;
            Dir = dir;
            Face = face;
        }
    }

    sealed class Portal
    {
        public int Row;
        public int Dir;
        public float PlaneY;           // the portal plane (Godot y)
        public Vector3 Offset;         // the link offset (Godot)
        public Vector3 QuakeOffset;
        public int FarSector = -1;     // the Build sector just beyond the linked plane (Build draws the far side from it)
        public Vector2 FarStart = new(float.NaN, 0);     // a place in it (Godot x, z in the linked room; NaN: unknown)
        public Aabb Bounds;            // the face (Godot, global)
        public Aabb Region;            // the copied part of the linked room (Godot, linked space)
        public readonly List<Vector2> Tris = new();      // the face's triangles (Godot x, z), three corners each
        public readonly List<MeshInstance3D> Faces = new();
        public Node3D Copy;            // the copies, moved by -Offset
        public readonly List<(MeshInstance3D src, MeshInstance3D copy, Aabb aabb)> Mirrors = new();
        public readonly Dictionary<MeshInstance3D, int> MirrorVersions = new();   // meshes stamped "version" (LiveRenderer)
        public bool Active;
        public bool Water;             // a water surface drawn see-through (Enhanced.WaterPortals): the level kept behind it
        public int Wall = -1, FarWall = -1;   // a wall portal: its wall, and the wall whose room it shows (a mirror: its own)
        public bool Mirror;
        public Vector2 FrameAt, Along;  // a wall portal's frame (Godot x, z): a point of its wall, the way along it (its room
                                        // on the side of (-Along.y, Along.x))
        public Rect2 Local;            // a wall portal's face bounds in its frame (along, height)
        public int WallSeen = -1;
        public bool IsWall => Wall >= 0;
        public bool Submerged;         // a link between two rooms under water (E4M4's): no surface, water on both sides
        public List<Rect2> Rects;      // its triangles' bounds, Quake x, y (FaceRects)
        public int Layer = -1;        // where rooms share space, the layer its face is tagged with (_g<n>; -1: both)
        public bool OtherLayer;        // this frame, in the other layer's room from the camera: not drawn at all
        public Vector3 SeenFrom = new(float.NaN, 0, 0);      // where Seen last looked from, and what it found
        public bool SeenResult;
        public Vector3 StackFrom = new(float.NaN, 0, 0);     // where StackHidden last looked from, in which sector
        public int StackSector = -1;
        public bool StackResult;
        public Sim.Collision.Link Link;     // a level from its Build data: its link side (its sector's face moves with it)
        public int ShapeSeen = -1, PlaneSeen = -1;
        public bool MaskDirty = true;  // its face mask to be made again (WriteData)
    }

    readonly List<Portal> _portals = new();
    ImageTexture _data;
    public int Drawn;                  // portals drawn last frame
    /// <summary>The portals drawn this frame (SpriteRenderer copies the sprites near them).</summary>
    public readonly List<ActivePortal> Active = new();

    readonly List<MeshInstance3D> _waterFaces = new();

    // a level from its Build data: the native world whose meshes the copies share, and their version copied
    Native.NativeWorld _native;
    int _nativeVersion = -1;
    Sim.Build.Board _board;

    /// <summary>A level from its Build data: a portal on each link side Build draws the far room through (a floor of
    /// tile 504 and the ceiling under it: Link.Portal), its face the sector's floor or ceiling as built; the copies are
    /// instances of the native world's meshes (shared: what changes in them changes in the copies), moved by -Offset and
    /// marked by their portal_copy.</summary>
    public void SetupNative(IReadOnlyList<Sim.Collision.Link> links, Sim.Build.Board board, Native.NativeWorld world,
        System.Func<int, bool> underwater = null)
    {
        foreach (var f in _waterFaces)
            f.QueueFree();
        _waterFaces.Clear();
        foreach (var p in _portals)
            if (p.Copy != null)
            {
                RemoveChild(p.Copy);     // (out of the tree at once: the new copies keep their names)
                p.Copy.QueueFree();
            }
        _portals.Clear();
        Active.Clear();
        for (int i = 0; i < Slots; i++)
            SetSlot(i, null);
        _native = world;
        _nativeVersion = -1;
        _board = board;
        if (board == null || world == null)
            return;
        foreach (var l in links)
        {
            if (!l.Portal || _portals.Count >= MaxPortals || !board.Valid(l.Sector))
                continue;
            var p = new Portal
            {
                Row = _portals.Count, Dir = l.Dir, Offset = Coords.QuakeToGodot(l.Offset), QuakeOffset = l.Offset,
                FarSector = l.FarSector, Link = l, Water = !l.ConvertedPortal,
            };
            p.Submerged = !p.Water && underwater != null && underwater(l.Sector) && l.Far != null && underwater(l.Far.Sector);
            // (no face yet: a sliding sector stored shut, E2M6's ice floe 1 over its water; kept, not drawn, until it opens)
            NativeFace(p);
            if (p.Water)
                p.Faces.Add(WaterFace(p));
            p.Copy = new Node3D { Name = $"PortalCopy{p.Row}", Position = -p.Offset, Visible = false };
            AddChild(p.Copy);
            _portals.Add(p);
        }
        WallPortals(board);
        if (_portals.Count > 0)
            WriteData();
    }

    /// <summary>
    /// The wall portals, as NBlood's InitMirrors finds them: a wall stack (type 501, overpicnum 504, an x-wall whose data
    /// another such wall has; a red wall, its one-way side the face) shows the room behind that wall, the view shifted
    /// by the far wall's end less this wall's start (DoWallMirrors: not turned); a white wall of picture 504 a mirror of
    /// its own room (the copy reflected in its plane). SectorVisibility walks on into a stack's far room.
    /// </summary>
    void WallPortals(Sim.Build.Board board)
    {
        var walk = new List<(int sector, int far, Vector2 offset)>();
        var byData = StacksByData(board);
        for (int w = 0; w < board.Walls.Length && _portals.Count < MaxPortals; w++)
        {
            var wall = board.Walls[w];
            if (!board.Valid(wall.Sector))
                continue;
            int far = -1;
            bool mirror = false;
            if (IsStackFace(board, w, byData))
                far = byData[wall.Xw.Data].FirstOrDefault(o => o != w, -1);
            else if (IsMirror(board, w))
                (far, mirror) = (w, true);
            if (far < 0 || !board.Valid(board.Walls[far].Sector))
                continue;
            var a = Native.WorldMesher.G(wall.X, wall.Y, 0);
            var fe = board.Walls[board.Walls[far].Point2];
            var off = mirror ? Vector3.Zero : Native.WorldMesher.G(fe.X, fe.Y, 0) - a;
            var p = new Portal
            {
                Row = _portals.Count, Dir = 1, Wall = w, FarWall = far, Mirror = mirror, Offset = off, QuakeOffset = Coords.GodotToQuake(off),
                // (a mirror: its own wall's sector, which Build draws the reflection from through mirrorsector; the
                // stacked-rooms walk unreflects the copy's pixels and the eye and starts there)
                FarSector = board.Walls[far].Sector,
            };
            WallFace(p);
            p.Copy = new Node3D { Name = $"PortalCopy{p.Row}", Visible = false };
            if (mirror)
            {
                // reflected in the wall's plane: x - 2 n (n . (x - a)), n its room's way out of the wall (Godot x, z)
                var n = new Vector3(-p.Along.Y, 0, p.Along.X);
                var basis = new Basis(Vector3.Right - 2 * n.X * n, Vector3.Up, Vector3.Back - 2 * n.Z * n);
                var o = new Vector3(p.FrameAt.X, 0, p.FrameAt.Y);
                p.Copy.Transform = new Transform3D(basis, 2 * n * n.Dot(o));
            }
            else
            {
                p.Copy.Position = -p.Offset;
                walk.Add((wall.Sector, p.FarSector, new Vector2(p.QuakeOffset.X, p.QuakeOffset.Y)));
            }
            AddChild(p.Copy);
            _portals.Add(p);
        }
        SectorVisibility.WallLinks = walk;
    }

    public const int WallStackType = 501, MirrorTile = 504;

    /// <summary>The wall stacks' walls by their x-wall data (type 501 with an x-wall).</summary>
    static Dictionary<int, List<int>> StacksByData(Sim.Build.Board b)
    {
        var byData = new Dictionary<int, List<int>>();
        for (int w = 0; w < b.Walls.Length; w++)
            if (b.Walls[w].Type == WallStackType && b.Walls[w].Xw is { } x && b.Walls[w].Extra > 0)
            {
                if (!byData.TryGetValue(x.Data, out var l))
                    byData[x.Data] = l = new List<int>();
                l.Add(w);
            }
        return byData;
    }

    /// <summary>Is wall w a wall stack's face (NBlood: type 501, overpicnum 504, a red wall, another of its data)?</summary>
    static bool IsStackFace(Sim.Build.Board b, int w, Dictionary<int, List<int>> byData)
    {
        var wall = b.Walls[w];
        return wall.Type == WallStackType && wall.OverPicnum == MirrorTile && wall.Extra > 0 && wall.Xw != null && wall.NextSector >= 0
            && byData.TryGetValue(wall.Xw.Data, out var l) && l.Count > 1;
    }

    /// <summary>Is wall w a mirror (a white wall of Blood's mirror picture, 504)?</summary>
    static bool IsMirror(Sim.Build.Board b, int w) => b.Walls[w].Picnum == MirrorTile && b.Walls[w].NextSector < 0 && b.Walls[w].Type != WallStackType;

    /// <summary>The walls drawn as a wall portal's face (a stack's window, a mirror): WorldMesher.IsWallPortal.</summary>
    public static HashSet<int> WallPortalWalls(Sim.Build.Board b)
    {
        var set = new HashSet<int>();
        if (b == null)
            return set;
        var byData = StacksByData(b);
        for (int w = 0; w < b.Walls.Length; w++)
            if (b.Valid(b.Walls[w].Sector) && (IsStackFace(b, w, byData) || IsMirror(b, w)))
                set.Add(w);
        return set;
    }

    int WallStamp(Portal p)
    {
        var wall = _board.Walls[p.Wall];
        int s = wall.Sector, n = wall.NextSector;
        return _board.ShapeVersion[s] * 7 + _board.PlaneVersion[s] + (n >= 0 ? _board.ShapeVersion[n] * 13 + _board.PlaneVersion[n] * 3 : 0);
    }

    /// <summary>A wall portal's face: its frame (a point of the wall and the way along it, its room on the left of that
    /// way), the opening as it is now (a stack: between the higher floor and the lower ceiling of its two sectors at each
    /// end; a mirror: its sector's floor to ceiling), its triangles and bounds (along, height), the face's box and the
    /// region copied (around the far wall, in the linked space).</summary>
    bool WallFace(Portal p)
    {
        var b = _board;
        var wall = b.Walls[p.Wall];
        var end = b.Walls[wall.Point2];
        p.WallSeen = WallStamp(p);
        var a = Native.WorldMesher.G(wall.X, wall.Y, 0);
        var e = Native.WorldMesher.G(end.X, end.Y, 0);
        var along = new Vector2(e.X - a.X, e.Z - a.Z);
        float len = along.Length();
        p.Tris.Clear();
        p.Rects = null;
        p.PlaneY = 0;
        if (len < 1e-5f)
            return false;
        along /= len;
        // the frame's normal points into the wall's room: Build winds every sector's walls with its inside on their
        // left ((-dy, dx)), which is (-along.y, along.x) here, so the wall's own start and way give it (a point probed
        // a few units out missed the room where it is thinner: E1M3's mirrors stand in 32-unit slivers)
        p.FrameAt = new Vector2(a.X, a.Z);
        p.Along = along;
        float Al(Vector3 g) => (new Vector2(g.X, g.Z) - p.FrameAt).Dot(p.Along);
        static float H(double z) => (float)(-z / 8192.0);
        int s = wall.Sector, n = wall.NextSector;
        (float bot, float top) At(Chosen.Shared.Blood.Map.BWall pt)
        {
            double f = b.FloorZAt(s, pt.X, pt.Y), c = b.CeilZAt(s, pt.X, pt.Y);
            if (n >= 0 && !p.Mirror)
            {
                f = System.Math.Min(f, b.FloorZAt(n, pt.X, pt.Y));
                c = System.Math.Max(c, b.CeilZAt(n, pt.X, pt.Y));
            }
            return (H(f), System.Math.Max(H(f), H(c)));
        }
        var (b1, t1) = At(wall);
        var (b2, t2) = At(end);
        if (t1 - b1 < 1e-4f && t2 - b2 < 1e-4f)
            return false;
        float u1 = Al(a), u2 = Al(e);
        var q1 = new Vector2(u1, b1);
        var q2 = new Vector2(u2, b2);
        var q3 = new Vector2(u2, t2);
        var q4 = new Vector2(u1, t1);
        p.Tris.AddRange(new[] { q1, q2, q3, q1, q3, q4 });
        float minU = System.Math.Min(u1, u2), maxU = System.Math.Max(u1, u2);
        float minH = System.Math.Min(b1, b2), maxH = System.Math.Max(t1, t2);
        p.Local = new Rect2(minU, minH, maxU - minU, maxH - minH);
        var lo = new Vector3(System.Math.Min(a.X, e.X), minH, System.Math.Min(a.Z, e.Z));
        var hi = new Vector3(System.Math.Max(a.X, e.X), maxH, System.Math.Max(a.Z, e.Z));
        p.Bounds = new Aabb(lo, hi - lo).Grow(0.01f);
        p.Region = new Aabb(p.Bounds.Position + p.Offset, p.Bounds.Size).Grow(Region);
        return true;
    }

    /// <summary>The rectangle a portal's face mask covers and its slot's box: a wall portal's face bounds in its frame
    /// (along, height), a level one's in x, z.</summary>
    static Rect2 MaskRect(Portal p) => p.IsWall ? p.Local
        : new Rect2(p.Bounds.Position.X, p.Bounds.Position.Z, p.Bounds.Size.X, p.Bounds.Size.Z);

    /// <summary>A point in a wall portal's frame: (along, out into its room, height).</summary>
    static Vector3 Local(Portal p, Vector3 g)
    {
        var d = new Vector2(g.X, g.Z) - p.FrameAt;
        return new Vector3(d.Dot(p.Along), d.Dot(new Vector2(-p.Along.Y, p.Along.X)), g.Y);
    }

    /// <summary>A native portal's face: its sector's floor or ceiling as it is now (Build draws the far room through its
    /// tile 504 wherever its link marker sits, the copy shifted by the markers' difference), and the region copied.</summary>
    bool NativeFace(Portal p)
    {
        var board = _board;
        var l = p.Link;
        p.ShapeSeen = board.ShapeVersion[l.Sector];
        p.PlaneSeen = PlaneStamp(p);
        p.PlaneY = PlaneYOf(p);
        var pts = new List<Vector2>();
        var loops = new List<List<int>>();
        foreach (var loop in board.Loops[l.Sector])
        {
            var li = new List<int>();
            foreach (int w in loop)
            {
                li.Add(pts.Count);
                pts.Add(new Vector2(board.Walls[w].X, board.Walls[w].Y));
            }
            loops.Add(li);
        }
        p.Tris.Clear();
        p.Rects = null;
        bool any = false;
        foreach (var t in PolyFill.Fill(pts, loops))
        {
            var g = new Vector3(-t.Y / 512f, p.PlaneY, t.X / 512f);
            p.Tris.Add(new Vector2(g.X, g.Z));
            var bx = new Aabb(g, Vector3.Zero);
            p.Bounds = any ? p.Bounds.Merge(bx) : bx;
            any = true;
        }
        if (!any)
            return false;
        float linked = p.PlaneY + p.Offset.Y;
        var fb = new Aabb(p.Bounds.Position + p.Offset, p.Bounds.Size);
        float lo = p.Dir > 0 ? linked - Region : linked, hi = p.Dir > 0 ? linked : linked + Region;
        p.Region = new Aabb(new Vector3(fb.Position.X - Region, lo, fb.Position.Z - Region),
            new Vector3(fb.Size.X + 2 * Region, hi - lo, fb.Size.Z + 2 * Region));
        // the copy's walk starts in the sector just beyond the face's first triangle (the far marker's own may lie
        // elsewhere: E3M2's link 1, its copy's rooms hidden by a walk from the wrong one)
        var tri = (p.Tris[0] + p.Tris[1] + p.Tris[2]) / 3f;
        var far = Coords.GodotToQuake(new Vector3(tri.X, p.PlaneY, tri.Y) + p.Offset);
        int fs = StackedRooms.SectorBeyond(far + new Vector3(0, 0, p.Dir > 0 ? -1 : 1), p.Dir, far.Z);
        p.FarSector = fs >= 0 ? fs : l.FarSector;
        p.FarStart = fs >= 0 ? new Vector2(tri.X + p.Offset.X, tri.Y + p.Offset.Z) : new Vector2(float.NaN, 0);
        return true;
    }

    // the plane as drawn: a ceiling under see-through water is at the surface (NativeWorld.FollowWater)
    float PlaneYOf(Portal p)
    {
        var sec = _board.Sectors[p.Link.Sector];
        int z = p.Dir > 0 ? sec.FloorZ : sec.CeilingZ;
        if (p.Dir < 0 && _native.CeilFollows.TryGetValue(p.Link.Sector, out var f))
            z = _board.Sectors[f.near].FloorZ + f.dz;
        return -z / 256f / Coords.QuakeScale;
    }

    int PlaneStamp(Portal p) => _board.PlaneVersion[p.Link.Sector]
        + (p.Dir < 0 && _native.CeilFollows.TryGetValue(p.Link.Sector, out var f) ? _board.PlaneVersion[f.near] : 0);

    /// <summary>The native portals whose sector moved: their faces again (and the shaders' data).</summary>
    void NativeFaces()
    {
        bool changed = false, reshaped = false;
        foreach (var p in _portals)
        {
            if (p.IsWall)
            {
                // a wall portal: its opening again when its sectors moved or changed shape (a door over the window)
                if (WallStamp(p) == p.WallSeen)
                    continue;
                bool hadFace = p.Tris.Count > 0;
                if (WallFace(p) && !hadFace)
                    _nativeVersion = -1;
                p.MaskDirty = true;
                p.SeenFrom = new Vector3(float.NaN, 0, 0);
                changed = reshaped = true;
                continue;
            }
            int s = p.Link.Sector;
            int planes = PlaneStamp(p);
            if (_board.ShapeVersion[s] == p.ShapeSeen && planes == p.PlaneSeen)
                continue;
            if (_board.ShapeVersion[s] == p.ShapeSeen)
            {
                // only its plane moved (E2M1's bobbing water, every game frame): the face and its black face moved up
                // or down, the triangles and the mask as they are
                float y0 = p.PlaneY;
                p.PlaneSeen = planes;
                p.PlaneY = PlaneYOf(p);
                float dy = p.PlaneY - y0;
                p.Bounds = new Aabb(p.Bounds.Position + new Vector3(0, dy, 0), p.Bounds.Size);
                p.Region = new Aabb(p.Region.Position + new Vector3(0, dy, 0), p.Region.Size);
                if (p.Water && p.Faces.Count > 0)
                    p.Faces[0].Position += new Vector3(0, dy, 0);
            }
            else
            {
                bool had = p.Tris.Count > 0;
                // (a face for the first time: the copies, made around the region it had then, made again)
                if (NativeFace(p) && !had)
                    _nativeVersion = -1;
                p.MaskDirty = true;
                if (p.Water && p.Faces.Count > 0)
                {
                    // (its black face under the reshaped surface: the mesh made again, the node kept)
                    var fresh = WaterFace(p);
                    p.Faces[0].Mesh = fresh.Mesh;
                    p.Faces[0].Position = Vector3.Zero;
                    _waterFaces.Remove(fresh);
                    fresh.QueueFree();
                }
                reshaped = true;
            }
            p.SeenFrom = new Vector3(float.NaN, 0, 0);
            changed = true;
        }
        if (changed)
        {
            WriteData(reshaped);
            for (int i = 0; i < Slots; i++)
                _slotsSent[i] = new Vector4(float.NaN, 0, 0, 0);
        }
    }

    /// <summary>The copies of the native world's meshes near each portal's region, again whenever those meshes change.</summary>
    void NativeCopies()
    {
        if (_native == null)
            return;
        NativeFaces();
        if (_native.NodesVersion == _nativeVersion)
            return;
        _nativeVersion = _native.NodesVersion;
        foreach (var p in _portals)
        {
            foreach (var c in p.Copy.GetChildren())
                c.QueueFree();
            var near = p.Region.Grow(Region);
            // through a water surface only the room under it can be seen: the meshes of the rest of the level around it,
            // drawn again for each surface and all clipped away, cost more than the room itself (DWE3M1's town round its
            // river, E2M1's shore round its lake)
            var room = p.Water ? WaterRoom(p) : null;
            foreach (var n in _native.Nodes)
            {
                if (n.Mesh == null || !(n.GlobalTransform * n.Mesh.GetAabb()).Intersects(near))
                    continue;
                if (room != null && !_native.NodeHasSector(n, room))
                    continue;
                var copy = new MeshInstance3D
                {
                    Mesh = n.Mesh, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, IgnoreOcclusionCulling = true,
                    ExtraCullMargin = n.ExtraCullMargin, Layers = n.Layers,
                };
                copy.SetInstanceShaderParameter("portal_copy", p.Row + 1f);
                p.Copy.AddChild(copy);
            }
        }
    }

    /// <summary>The sectors a water portal can show: all those joined to its far room through the walls' openings (not
    /// stopping at the rooms of the other links: what lies beyond them shows through them, and left out it was a hole with
    /// the sky in it, E3M3's flooded corridor).</summary>
    HashSet<int> WaterRoom(Portal p)
    {
        var room = new HashSet<int>();
        int start = p.FarSector >= 0 ? p.FarSector : p.Link.FarSector;
        if (start < 0 || !_board.Valid(start))
            return null;
        var todo = new Queue<int>();
        room.Add(start);
        todo.Enqueue(start);
        while (todo.Count > 0)
        {
            int s = todo.Dequeue();
            foreach (int w in _board.SectorWalls(s))
            {
                int nx = _board.Walls[w].NextSector;
                if (nx >= 0 && _board.Valid(nx) && room.Add(nx))
                    todo.Enqueue(nx);
            }
        }
        return room;
    }

    /// <summary>A portal's face triangles' bounds in Quake x, y (worked out once).</summary>
    static List<Rect2> FaceRects(Portal p)
    {
        if (p.Rects != null)
            return p.Rects;
        p.Rects = new List<Rect2>();
        for (int t = 0; t + 2 < p.Tris.Count; t += 3)
        {
            var r = new Rect2();
            for (int k = 0; k < 3; k++)
            {
                var q = Coords.GodotToQuake(new Vector3(p.Tris[t + k].X, p.PlaneY, p.Tris[t + k].Y));
                var pt = new Rect2(q.X, q.Y, 0, 0);
                r = k == 0 ? pt : r.Merge(pt);
            }
            p.Rects.Add(r);
        }
        return p.Rects;
    }
    static Material _waterFaceMaterial;

    /// <summary>A water portal's black face, for when it isn't drawn (more than Slots in view): its triangles a
    /// hair beyond the plane, under the water surface blended over it.</summary>
    MeshInstance3D WaterFace(Portal p)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        float y = p.PlaneY - p.Dir * 0.05f / Coords.QuakeScale;
        for (int t = 0; t + 2 < p.Tris.Count; t += 3)
            for (int k = 0; k < 3; k++)
                st.AddVertex(new Vector3(p.Tris[t + k].X, y, p.Tris[t + k].Y));
        _waterFaceMaterial ??= new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = Colors.Black, CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        var mi = new MeshInstance3D { Mesh = p.Tris.Count > 0 ? st.Commit() : new ArrayMesh(), MaterialOverride = _waterFaceMaterial, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Visible = false };
        AddChild(mi);
        _waterFaces.Add(mi);
        return mi;
    }

    /// <summary>portal_data: each portal's plane, side, face bounds and triangles.</summary>
    float[] _dataBuf;
    byte[] _dataBytes;

    /// <summary>portal_data, and with mask the face masks (only when a face's shape changed: its plane moving leaves
    /// them). The textures are sent whole and set as globals once (setting a global again costs every material).</summary>
    void WriteData(bool mask = true)
    {
        _dataBuf ??= new float[DataWidth * MaxPortals * 4];
        System.Array.Clear(_dataBuf);
        void Set(int x, int y, Color c)
        {
            int i = 4 * (y * DataWidth + x);
            _dataBuf[i] = c.R;
            _dataBuf[i + 1] = c.G;
            _dataBuf[i + 2] = c.B;
            _dataBuf[i + 3] = c.A;
        }
        foreach (var p in _portals)
        {
            int tris = p.Tris.Count / 3;
            int n = tris <= MaxTris ? tris : 0;
            var mr = MaskRect(p);
            Set(0, p.Row, new Color(p.PlaneY, p.Dir, n, p.IsWall ? (p.Mirror ? 4 : 3) : p.Water ? 1 : p.Submerged ? 2 : 0));
            Set(1, p.Row, new Color(mr.Position.X, mr.Position.Y, mr.End.X, mr.End.Y));
            Set(FarColumn, p.Row, new Color(p.Offset.X, p.Offset.Y, p.Offset.Z, p.FarSector));
            if (p.IsWall)
                Set(FrameColumn, p.Row, new Color(p.FrameAt.X, p.FrameAt.Y, p.Along.X, p.Along.Y));
            else if (!float.IsNaN(p.FarStart.X))
                Set(FrameColumn, p.Row, new Color(p.FarStart.X, p.FarStart.Y, 1, 0));
            for (int t = 0; t < n; t++)
            {
                Vector2 a = p.Tris[t * 3], b = p.Tris[t * 3 + 1], c = p.Tris[t * 3 + 2];
                Set(2 + 2 * t, p.Row, new Color(a.X, a.Y, b.X, b.Y));
                Set(3 + 2 * t, p.Row, new Color(c.X, c.Y, 0, 0));
            }
        }
        _dataBytes ??= new byte[_dataBuf.Length * 4];
        System.Buffer.BlockCopy(_dataBuf, 0, _dataBytes, 0, _dataBytes.Length);
        var img = Image.CreateFromData(DataWidth, MaxPortals, false, Image.Format.Rgbaf, _dataBytes);
        if (_data == null)
        {
            _data = ImageTexture.CreateFromImage(img);
            RenderingServer.GlobalShaderParameterSet(DataName, _data);
        }
        else
            _data.Update(img);
        if (!mask && _mask != null)
            return;
        // each portal's cell mask over its bounds: 3 x 3 samples a cell, all inside the face: inside, none and no
        // corner of the face in it: outside, else the edge. Only the portals whose face changed shape (E2M1's sliding
        // ice floes, water links, every game frame), their triangles rasterised over the samples
        _maskBytes ??= new byte[MaskCells * MaskCells * MaxPortals];
        foreach (var p in _portals)
            if (p.MaskDirty)
            {
                MaskRows(p, _maskBytes);
                p.MaskDirty = false;
            }
        var maskImg = Image.CreateFromData(MaskCells, MaskCells * MaxPortals, false, Image.Format.R8, _maskBytes);
        if (_mask == null)
        {
            _mask = ImageTexture.CreateFromImage(maskImg);
            RenderingServer.GlobalShaderParameterSet(MaskName, _mask);
        }
        else
            _mask.Update(maskImg);
    }

    byte[] _maskBytes;
    const int Samples = 2 * MaskCells + 1;
    readonly bool[] _inside = new bool[Samples * Samples];
    readonly bool[] _corner = new bool[MaskCells * MaskCells];

    /// <summary>A portal's rows of the face mask.</summary>
    void MaskRows(Portal p, byte[] bytes)
    {
        var r = MaskRect(p);
        var b = new Aabb(new Vector3(r.Position.X, 0, r.Position.Y), new Vector3(r.Size.X, 0, r.Size.Y));
        float cw = Mathf.Max(b.Size.X / MaskCells, 1e-6f), ch = Mathf.Max(b.Size.Z / MaskCells, 1e-6f);
        float sw = cw / 2, sh = ch / 2;
        System.Array.Clear(_inside);
        System.Array.Clear(_corner);
        for (int t = 0; t + 2 < p.Tris.Count; t += 3)
        {
            Vector2 a = p.Tris[t], c1 = p.Tris[t + 1], c2 = p.Tris[t + 2];
            float minX = Mathf.Min(a.X, Mathf.Min(c1.X, c2.X)), maxX = Mathf.Max(a.X, Mathf.Max(c1.X, c2.X));
            float minZ = Mathf.Min(a.Y, Mathf.Min(c1.Y, c2.Y)), maxZ = Mathf.Max(a.Y, Mathf.Max(c1.Y, c2.Y));
            int i0 = Mathf.Clamp(Mathf.CeilToInt((minX - b.Position.X) / sw), 0, Samples - 1);
            int i1 = Mathf.Clamp(Mathf.FloorToInt((maxX - b.Position.X) / sw), 0, Samples - 1);
            int j0 = Mathf.Clamp(Mathf.CeilToInt((minZ - b.Position.Z) / sh), 0, Samples - 1);
            int j1 = Mathf.Clamp(Mathf.FloorToInt((maxZ - b.Position.Z) / sh), 0, Samples - 1);
            for (int j = j0; j <= j1; j++)
                for (int i = i0; i <= i1; i++)
                    if (!_inside[j * Samples + i] && InTri(new Vector2(b.Position.X + i * sw, b.Position.Z + j * sh), a, c1, c2))
                        _inside[j * Samples + i] = true;
            foreach (var v in new[] { a, c1, c2 })
            {
                int cx = Mathf.Clamp(Mathf.FloorToInt((v.X - b.Position.X) / cw), 0, MaskCells - 1);
                int cz = Mathf.Clamp(Mathf.FloorToInt((v.Y - b.Position.Z) / ch), 0, MaskCells - 1);
                _corner[cz * MaskCells + cx] = true;
            }
        }
        for (int y = 0; y < MaskCells; y++)
            for (int x = 0; x < MaskCells; x++)
            {
                int inside = 0;
                for (int j = 0; j < 3; j++)
                    for (int i = 0; i < 3; i++)
                        if (_inside[(2 * y + j) * Samples + 2 * x + i])
                            inside++;
                float val = inside == 9 ? 1f : inside == 0 && !_corner[y * MaskCells + x] ? 0f : 0.5f;
                bytes[(p.Row * MaskCells + y) * MaskCells + x] = (byte)Mathf.RoundToInt(val * 255);
            }
    }

    static bool InTri(Vector2 q, Vector2 a, Vector2 b, Vector2 c)
    {
        float d1 = (q.X - b.X) * (a.Y - b.Y) - (a.X - b.X) * (q.Y - b.Y);
        float d2 = (q.X - c.X) * (b.Y - c.Y) - (b.X - c.X) * (q.Y - c.Y);
        float d3 = (q.X - a.X) * (c.Y - a.Y) - (c.X - a.X) * (q.Y - a.Y);
        bool neg = d1 < 0 || d2 < 0 || d3 < 0, pos = d1 > 0 || d2 > 0 || d3 > 0;
        return !(neg && pos);
    }

    static bool InFace(Portal p, Vector2 q)
    {
        for (int t = 0; t + 2 < p.Tris.Count; t += 3)
        {
            Vector2 a = p.Tris[t], b = p.Tris[t + 1], c = p.Tris[t + 2];
            float d1 = (q.X - b.X) * (a.Y - b.Y) - (a.X - b.X) * (q.Y - b.Y);
            float d2 = (q.X - c.X) * (b.Y - c.Y) - (b.X - c.X) * (q.Y - c.Y);
            float d3 = (q.X - a.X) * (c.Y - a.Y) - (c.X - a.X) * (q.Y - a.Y);
            bool neg = d1 < 0 || d2 < 0 || d3 < 0, pos = d1 > 0 || d2 > 0 || d3 > 0;
            if (!(neg && pos))
                return true;
        }
        return false;
    }

    public int Count => _portals.Count;

    /// <summary>Each portal: plane, side, offset, whether drawn, its copy (debugging: the portals command).</summary>
    public IEnumerable<string> Describe()
    {
        foreach (var p in _portals)
        {
            int tris = 0;
            foreach (var c in p.Copy.GetChildren())
                if (c is MeshInstance3D mi && mi.Mesh is ArrayMesh am)
                    for (int s = 0; s < am.GetSurfaceCount(); s++)
                        tris += am.SurfaceGetArrayLen(s) / 3;
            yield return $"portal {p.Row}: plane {p.PlaneY * Coords.QuakeScale:0.#} dir {p.Dir} offset {p.QuakeOffset} " +
                $"face {p.Tris.Count / 3} triangles, active {p.Active}, copy {p.Copy.GetChildCount()} meshes {tris} triangles, " +
                $"{p.Mirrors.Count} mirrored, copy visible {p.Copy.IsVisibleInTree()}, far sector {p.FarSector}" + (p.Link != null ? $" (link sector {p.Link.Sector}, marker far {p.Link.FarSector})" : "")
                + (p.IsWall ? $" ({(p.Mirror ? "mirror" : "wall stack")} on wall {p.Wall} showing wall {p.FarWall}'s room: frame at {p.FrameAt}, along {p.Along}, face {p.Local}, bounds {p.Bounds})" : "");
        }
    }

    /// <summary>Is the line between two points (Godot space) clear of solid walls? (Game: the level's collision.)</summary>
    public System.Func<Vector3, Vector3, bool> Sees;

    // a portal within this distance (Godot units) is drawn whenever it is in view
    const float NearAlways = 384f / Coords.QuakeScale;

    /// <summary>
    /// Is the portal face in sight of the eye, not just inside the view cone behind walls (E1M3's cellar ceiling,
    /// rooms away)? Lines from the eye to points over the face, any of them clear of the static world (a shut door
    /// doesn't count, so the portal is never missing when it opens). Near the face it is always drawn.
    /// </summary>
    bool Seen(Portal p, Vector3 eye)
    {
        if (Sees == null)
            return true;
        // looked at again only once the eye has moved a little (4 Quake units): nine traces a portal each frame cost
        if (p.SeenFrom.DistanceSquaredTo(eye) < 0.125f * 0.125f)
            return p.SeenResult;
        p.SeenFrom = eye;
        return p.SeenResult = SeenNow(p, eye);
    }

    /// <summary>A level from its Build data: is the portal's face, its sector sharing the ground with others, hidden
    /// wherever it lies (each face triangle's middle), Build's walk from the camera's sector arriving there in a sector
    /// sharing it (StackedRooms.SectorFaceHidden, as the face's own shader drops it)? Then it is neither drawn nor black,
    /// as the other layer's portal (E3M1's shaft 87 over the bar 381: from the bar its floor link showed the pit 85
    /// under it through the bar's railing). Looked at again once the eye moved or the camera's sector changed.</summary>
    bool StackHidden(Portal p, Vector3 eye)
    {
        if (p.Link == null || !StackedRooms.SharesGround(p.Link.Sector) || p.Tris.Count == 0)
            return false;
        if (p.StackFrom.DistanceSquaredTo(eye) < 0.125f * 0.125f && p.StackSector == StackedRooms.Sector)
            return p.StackResult;
        p.StackFrom = eye;
        p.StackSector = StackedRooms.Sector;
        p.StackResult = true;
        for (int t = 0; t + 2 < p.Tris.Count && t < 3 * 16; t += 3)
        {
            var c = (p.Tris[t] + p.Tris[t + 1] + p.Tris[t + 2]) / 3f;
            if (!StackedRooms.SectorFaceHidden(p.Link.Sector, Coords.GodotToQuake(new Vector3(c.X, p.PlaneY, c.Y)), out _))
            {
                p.StackResult = false;
                break;
            }
        }
        return p.StackResult;
    }

    bool SeenNow(Portal p, Vector3 eye)
    {
        if (p.IsWall)
        {
            // a wall portal: points over its face a unit out into its room
            var bw = p.Bounds;
            var cl = new Vector3(Mathf.Clamp(eye.X, bw.Position.X, bw.End.X), Mathf.Clamp(eye.Y, bw.Position.Y, bw.End.Y), Mathf.Clamp(eye.Z, bw.Position.Z, bw.End.Z));
            if (cl.DistanceTo(eye) < NearAlways)
                return true;
            var nrm = new Vector2(-p.Along.Y, p.Along.X) / Coords.QuakeScale;
            for (int i = 0; i < 3; i++)
                for (int j = 0; j < 3; j++)
                {
                    float u = Mathf.Lerp(p.Local.Position.X, p.Local.End.X, 0.15f + 0.35f * ((i + 1) % 3));
                    float h = Mathf.Lerp(p.Local.Position.Y, p.Local.End.Y, 0.15f + 0.35f * ((j + 1) % 3));
                    var g = p.FrameAt + p.Along * u + nrm;
                    if (Sees(eye, new Vector3(g.X, h, g.Y)))
                        return true;
                }
            return false;
        }
        var b = p.Bounds;
        float y = p.PlaneY + (p.Dir > 0 ? 1f : -1f) / Coords.QuakeScale;      // just off the face, on its room's side
        float cx = Mathf.Clamp(eye.X, b.Position.X, b.End.X), cz = Mathf.Clamp(eye.Z, b.Position.Z, b.End.Z);
        if (new Vector3(cx, p.PlaneY, cz).DistanceTo(eye) < NearAlways)
            return true;
        for (int i = 0; i < 3; i++)
            for (int j = 0; j < 3; j++)
            {
                float u = 0.15f + 0.35f * ((i + 1) % 3), v = 0.15f + 0.35f * ((j + 1) % 3);     // the centre first
                var pt = new Vector3(Mathf.Lerp(b.Position.X, b.End.X, u), y, Mathf.Lerp(b.Position.Z, b.End.Z, v));
                if (Sees(eye, pt))
                    return true;
            }
        return false;
    }

    Plane[] _frustum = System.Array.Empty<Plane>();
    readonly List<(float dist, Portal p)> _visible = new();

    static bool InFrustum(Plane[] frustum, Aabb b)
    {
        foreach (var pl in frustum)
        {
            // the box corner furthest along the plane's normal (the frustum's planes face outwards)
            var c = new Vector3(pl.Normal.X > 0 ? b.Position.X : b.End.X, pl.Normal.Y > 0 ? b.Position.Y : b.End.Y,
                pl.Normal.Z > 0 ? b.Position.Z : b.End.Z);
            if (pl.DistanceTo(c) > 0)
                return false;
        }
        return true;
    }

    /// <summary>After the main camera has moved for the frame: the nearest portals in view get their copies drawn,
    /// the others their black face.</summary>
    public void Draw(Camera3D main)
    {
        Active.Clear();
        Drawn = 0;
        if (_portals.Count == 0)
            return;
        NativeCopies();
        var eye = main.GlobalPosition;
        // the frustum's planes, the Godot array freed at once (left to the finalizer, it contends with the main thread)
        var planes = main.GetFrustum();
        if (_frustum.Length != planes.Count)
            _frustum = new Plane[planes.Count];
        for (int i = 0; i < _frustum.Length; i++)
            _frustum[i] = planes[i];
        ((Godot.Collections.Array)planes).Dispose();
        _visible.Clear();
        foreach (var p in _portals)
        {
            // where rooms share space, a portal of the other layer's room is neither drawn nor black (as its room's
            // faces, StackedRooms.LayerHidden)
            p.OtherLayer = p.Layer >= 0 && StackedRooms.LayerHidden(p.Layer, Coords.GodotToQuake(p.Bounds.GetCenter()))
                || StackHidden(p, eye);
            if (p.OtherLayer || p.Tris.Count == 0)
                continue;
            // the face is seen from its room's side only: above a floor portal, below a ceiling portal
            bool side = p.IsWall ? Local(p, eye).Y > 0 : p.Dir > 0 ? eye.Y > p.PlaneY : eye.Y < p.PlaneY;
            if (side && InFrustum(_frustum, p.Bounds) && Seen(p, eye))
                _visible.Add((p.Bounds.GetCenter().DistanceSquaredTo(eye), p));
        }
        _visible.Sort((a, b) => a.dist.CompareTo(b.dist));
        foreach (var p in _portals)
            p.Active = false;
        for (int i = 0; i < Slots; i++)
            SetSlot(i, i < _visible.Count ? _visible[i].p : null);
        for (int i = 0; i < _visible.Count && i < Slots; i++)
        {
            var p = _visible[i].p;
            p.Active = true;
            Active.Add(new ActivePortal(p.Row + 1, p.QuakeOffset, ToQuake(p.Region),
                p.IsWall ? float.NaN : Coords.GodotToQuake(new Vector3(0, p.PlaneY + p.Offset.Y, 0)).Z, p.Dir, ToQuake(p.Bounds), p.FarSector, p.Water,
                p.Water ? FaceRects(p) : null, p.IsWall, p.Mirror, p.FrameAt + new Vector2(p.Offset.X, p.Offset.Z), new Vector2(-p.Along.Y, p.Along.X),
                p.IsWall || float.IsNaN(p.FarStart.X) ? null : QuakeXY(p.FarStart)));
        }
        foreach (var p in _portals)
        {
            if (p.Copy.Visible != p.Active)
                p.Copy.Visible = p.Active;
            if (!p.Water)
                continue;
            bool black = !p.Active && !p.OtherLayer;     // a portal in view without a copy (more than Slots at once)
            foreach (var f in p.Faces)
                if (f.Visible != black)
                    f.Visible = black;
        }
        Drawn = System.Math.Min(_visible.Count, Slots);
    }

    /// <summary>A shader slot: the portal drawn in it (null: unused). Sent only when it changes.</summary>
    void SetSlot(int i, Portal p)
    {
        var v = p == null ? Vector4.Zero : new Vector4(p.PlaneY, p.Dir, p.Row, p.IsWall ? (p.Mirror ? 4 : 3) : p.Water ? 2 : 1);
        if (v == _slotsSent[i] && p != null)
            return;
        _slotsSent[i] = v;
        RenderingServer.GlobalShaderParameterSet(SlotNames[i], v);
        if (p != null)
        {
            var r = MaskRect(p);
            RenderingServer.GlobalShaderParameterSet(BoxNames[i], new Vector4(r.Position.X, r.Position.Y, r.End.X, r.End.Y));
            if (p.IsWall)
                RenderingServer.GlobalShaderParameterSet(FrameNames[i], new Vector4(p.FrameAt.X, p.FrameAt.Y, p.Along.X, p.Along.Y));
        }
    }

    static Vector2 QuakeXY(Vector2 xz)
    {
        var q = Coords.GodotToQuake(new Vector3(xz.X, 0, xz.Y));
        return new Vector2(q.X, q.Y);
    }

    static Aabb ToQuake(Aabb g) => new Aabb(Coords.GodotToQuake(g.Position), Vector3.Zero).Expand(Coords.GodotToQuake(g.End));
}
