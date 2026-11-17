namespace Chosen.Render.Native;

/// <summary>
/// The shader of the native world meshes (WorldMesher's vertex data): every vertex's height from the plane it names in
/// b3d_planes (this game frame's and the last's, blended by tick_lerp: a moving floor or ceiling moves without a rebuild),
/// a wall's picture pegged to its plane the same way, the panning and light-effect shades of its sector or wall from
/// b3d_sector and b3d_wallpan, the surface's palette (Build's palookups: BuildPalette) by its vertex; the rest as the
/// level shader (BuildShading: Build's shade and distance darkening, the classic palette, fog to white for palette 1,
/// the stacked rooms' walk, the portal clip, dynamic lights, AO).
/// </summary>
public static class WorldShader
{
    /// <summary>Texels per row of the data textures.</summary>
    public const int DataWidth = 4096;

    public static readonly string Data = @"
// the planes (Quake z = a x + b y + c over Quake x, y): ref r (1 + 2 sector + ceiling) at texel r - 1, this game frame's
// and the last's
global uniform highp sampler2D b3d_planes : filter_nearest;
global uniform highp sampler2D b3d_planes_prev : filter_nearest;
// per sector, two texels: (floor, ceiling, walls) shade offsets of its light effect; (floor x, y, ceiling x, y) panning
global uniform highp sampler2D b3d_sector : filter_nearest;
// per wall: (x, y) panning
global uniform highp sampler2D b3d_wallpan : filter_nearest;
// per sector: (reached by the walk directly, reached at all) (r_skyfaces 0)
global uniform highp sampler2D b3d_seen : filter_nearest;
vec4 b3d_texel(sampler2D t, int i) { return texelFetch(t, ivec2(i % DATAWIDTH, i / DATAWIDTH), 0); }
// per sector: what lines of sight reach it (angle across off b3d_view_yaw, lowest and highest; slope up, lowest and highest)
global uniform highp sampler2D b3d_window : filter_nearest;
global uniform float b3d_view_yaw;
vec4 b3d_window_of(float packed, float copy) {
	int s = int(packed + 0.5) / 4 - 1;
	return s < 0 || copy > 0.5 ? vec4(-10.0, 10.0, -1e6, 1e6) : b3d_texel(b3d_window, s);
}
// (in Godot: x is Quake y, z is Quake x)
bool b3d_outside(vec4 n, vec3 w, vec3 eye) {
	if (n.x < -9.0 && n.z < -9e5 && n.w > 9e5) return false;
	vec3 d = w - eye;
	float t = d.y / max(length(d.xz), 1e-4);
	if (t < n.z - 0.002 || t > n.w + 0.002) return true;
	if (n.x < -9.0) return false;
	float a = mod(atan(d.x, d.z) - b3d_view_yaw + PI, 2.0 * PI) - PI;
	return a < n.x - 0.01 || a > n.y + 0.01;
}
// the camera (Game): a view from elsewhere is a light's shadow pass, where all the level stands: what the walk hides from the
// camera still stops light (with the faces culled there too, light came through E1M1's walls)
global uniform vec3 b3d_eye;
bool b3d_camera_view(vec3 eye) {
	return length(eye - b3d_eye) < 0.01;
}
// a face's sector not reached (CUSTOM3.y: its kind + 4 (sector + 1)): the level's own by the walk directly, a copy's at all
bool b3d_culled(float packed, float copy) {
	int s = int(packed + 0.5) / 4 - 1;
	if (s < 0) return false;
	// (a mirror's copy: its room seen from the eye reflected, which the walk doesn't go round)
	if (copy > 0.5 && texelFetch(portal_data, ivec2(0, int(copy + 0.5) - 1), 0).w > 3.5) return false;
	vec4 n = b3d_texel(b3d_seen, s);
	return (copy > 0.5 ? n.g : n.r) < 0.5;
}
float b3d_plane(int r, vec2 q) {
	if (r <= 0) return 0.0;
	vec4 c = mix(b3d_texel(b3d_planes_prev, r - 1), b3d_texel(b3d_planes, r - 1), tick_lerp);
	return c.x * q.x + c.y * q.y + c.z;
}
// does sector s hold the world point w (Godot) between its floor and ceiling (a Quake unit's slack)?
#define B3D_PLANES
bool b3d_holds(int s, vec3 w) {
	vec2 q = vec2(w.z, w.x) * 32.0;
	float z = w.y * 32.0;
	return z >= b3d_plane(1 + 2 * s, q) - 1.0 && z <= b3d_plane(2 + 2 * s, q) + 1.0;
}
float b3d_height(vec3 refs, vec2 q) {
	float z = b3d_plane(int(refs.x + 0.5), q);
	if (refs.y > 0.5) z = max(z, b3d_plane(int(refs.y + 0.5), q));
	if (refs.z > 0.5) z = min(z, b3d_plane(int(refs.z + 0.5), q));
	return z;
}
// a plane's own height (Build's floorz / ceilingz, not the slope's height anywhere): a wall's picture peg
float b3d_flat(int r) {
	if (r <= 0) return 0.0;
	return mix(b3d_texel(b3d_planes_prev, r - 1).w, b3d_texel(b3d_planes, r - 1).w, tick_lerp);
}
float b3d_peg(vec3 refs) {
	float z = b3d_flat(int(refs.x + 0.5));
	if (refs.y > 0.5) z = max(z, b3d_flat(int(refs.y + 0.5)));
	if (refs.z > 0.5) z = min(z, b3d_flat(int(refs.z + 0.5)));
	return z;
}
".Replace("DATAWIDTH", DataWidth.ToString());

    /// <summary>A room-over-room portal's face (tile 504 over a link): nothing where its portal is drawn this frame (the
    /// copy of the far room shows), black where it isn't (more portals in view than PortalRenderer.Slots).</summary>
    /// <summary>The top of a world shader's vertex(): a sector moving rigidly: its corner, built where it is now
    /// (WorldMesher.Blend), drawn between the last game frame and now, a flat's picture with it (its planes blend in
    /// b3d_plane).</summary>
    public const string VertexBlend = @"
	if (CUSTOM1.w > 0.5) {
		float back = 1.0 - tick_lerp;
		vec2 moved;
		if (int(CUSTOM3.y + 0.5) % 4 < 2) {
			moved = CUSTOM2.xy;
			UV -= CUSTOM1.xy * back;
		} else {
			int packed = int(CUSTOM1.w + 0.5);
			moved = vec2(float((packed >> 12) - 2048), float((packed & 4095) - 2048));
		}
		VERTEX.x += moved.y / 512.0 * back;
		VERTEX.z -= moved.x / 512.0 * back;
	}
";

    public static string PortalFaceCode() => @"
shader_type spatial;" + PortalRenderer.Clip + @"
render_mode unshaded, cull_back, fog_disabled;
" + BuildShading.TickLerpUniform + Data + StackedRooms.Shader + @"
instance uniform float portal_copy = 0.0;
// its link doesn't move (WorldMesher.IsPortalInPlace): in a copy the room beyond it is there, right behind it
uniform bool in_place = false;
varying float v_portal;
varying float v_own;
varying flat vec4 v_window;
bool face_open(vec4 h, vec4 bb, vec3 eye, vec3 w) {
	return h.w > 0.5 && h.w < 1.5 && abs(w.y - h.x) < 0.02 && portal_crosses(int(h.z + 0.5), h, bb, eye, w, 0.02);
}
void vertex() {" + VertexBlend + @"
	vec2 q = vec2(VERTEX.z, VERTEX.x) * 32.0;
	if (CUSTOM0.x > 0.5)
		VERTEX.y = b3d_height(CUSTOM0.xyz, q) / 32.0;
	v_portal = portal_copy;
	v_own = UV2.x - 1.0;
	v_window = b3d_window_of(CUSTOM3.y, portal_copy);
	POSITION = b3d_camera_view(INV_VIEW_MATRIX[3].xyz) && b3d_culled(CUSTOM3.y, portal_copy) ? vec4(2.0, 2.0, 2.0, 1.0) : PROJECTION_MATRIX * (MODELVIEW_MATRIX * vec4(VERTEX, 1.0));
}
void fragment() {
	vec3 w = " + PortalRenderer.WorldPos + @";
	light_world = w;
	light_sky = 1.0;
	light_lit = 1.0;
	if (portal_clipped(w, v_portal, CAMERA_POSITION_WORLD)) discard;
	vec3 eye = CAMERA_POSITION_WORLD;
	if (b3d_camera_view(eye) && b3d_outside(v_window, w, eye)) discard;
	// a copy's portal face on the plane of the portal it is seen through: the near room's own face (a link with no
	// height between its rooms, E2M7's 221-223: the copy of the floor face lay over the far room, drawn nearer)
	if (v_portal > 0.5 && portal_off_plane(int(v_portal + 0.5) - 1, w) < 0.02) discard;
	// another link's face in a copy is black (Build draws links within links; the copies don't), but for a link in place:
	// the room beyond it is in the copy where it really is (three floors stacked: report 20261105-143410)
	if (v_portal > 0.5 && in_place) discard;
	// the stacked rooms' walk as the world's (a copy holds the near room too where the rooms share space: E3M2's link
	// 1, the upper room's face in the copy of the lower one under it)
	if (stack_discard(w, eye, v_portal, v_own)) discard;
	if (v_portal < 0.5 && (face_open(portal_slot_0, portal_box_0, eye, w) || face_open(portal_slot_1, portal_box_1, eye, w)
		|| face_open(portal_slot_2, portal_box_2, eye, w) || face_open(portal_slot_3, portal_box_3, eye, w))) discard;
	ALBEDO = vec3(0.0);
}
";

    /// <summary>A wall portal's face (WorldMesher.IsWallPortal: a wall stack's window, a mirror): open where its portal is
    /// drawn and the eye looks through it (the copy behind it shows), else black; in a copy its own portal's face, on the
    /// plane the copy is seen through, open too.</summary>
    const string WallPortalFace = @"
	if (v_portal < 0.5 && (wall_face_open(portal_slot_0, portal_box_0, portal_frame_0, CAMERA_POSITION_WORLD, light_world)
		|| wall_face_open(portal_slot_1, portal_box_1, portal_frame_1, CAMERA_POSITION_WORLD, light_world)
		|| wall_face_open(portal_slot_2, portal_box_2, portal_frame_2, CAMERA_POSITION_WORLD, light_world)
		|| wall_face_open(portal_slot_3, portal_box_3, portal_frame_3, CAMERA_POSITION_WORLD, light_world))) discard;
	if (v_portal > 0.5 && portal_off_plane(int(v_portal + 0.5) - 1, light_world) < 0.02) discard;
	ALBEDO = vec3(0.0);
	EMISSION = vec3(0.0);
	ROUGHNESS = 1.0;
";

    public static string Code(string renderMode, bool translucent, bool water = false, bool waterMarch = false, bool wallPortal = false) => @"
shader_type spatial;" + PortalRenderer.Clip + @"
render_mode ambient_light_disabled, specular_disabled, " + renderMode + @";
" + (water ? "global uniform float water_alpha;\nglobal uniform float water_ripple;\nglobal uniform float water_shine;\nglobal uniform float sky_shade;\nglobal uniform float sky_fog;\nvarying flat float v_sky;\n" + BuildSky.Mapping + (waterMarch ? WaterFx.ReflectFunctions : "") : "") + @"

uniform sampler2D albedo_texture : source_color, filter_nearest_mipmap, repeat_enable;
// the tile: width, height, and their next powers of two (panning is in 1/256 of those)
uniform vec4 tile_info = vec4(64.0, 64.0, 64.0, 64.0);
" + BuildShading.TickLerpUniform + Data + @"
global uniform float build_visibility;
" + BuildShading.ShadeFunction + BuildPalette.ShaderFunction + AmbientOcclusion.ShaderFunction + StackedRooms.Shader + WaterFx.Functions + @"
// a wall picture whose height isn't a power of two repeats as classic Build draws it (BuildShading.NpotWalls)
uniform vec2 npot_wall = vec2(0.0);
// a picture of fire, lava or a lamp (BuildShading.GlowTile): its bright colours glow (r_glow)
uniform float tile_glows = 0.0;
// a room-over-room copy of the world (WorldPortals): the portal's row + 1; 0 the level itself (every instance sets it)
instance uniform float portal_copy = 0.0;

varying float v_shade;
varying float v_rate;
varying float v_portal;
varying float v_own;
varying float v_wall;
varying float v_alpha;
varying flat vec4 v_window;
varying flat int v_pal;
// a wall cut to its exact heights (WorldMesher: planes that may slope): its top's and bottom's planes, 0: not cut
varying flat vec3 v_top;
varying flat vec3 v_bottom;
varying flat float v_cut;
varying flat float v_sky_reach;

void vertex() {" + VertexBlend + @"
	vec2 q = vec2(VERTEX.z, VERTEX.x) * 32.0;     // Quake x, y
	v_cut = 0.0;
	if (CUSTOM0.w > 0.5) {
		// drawn over its bounds: the top no lower than max(P, L) (convex: the edge between the ends lies over it), the
		// bottom no higher than min(P, H); the fragments outside the exact heights are cut
		bool top = CUSTOM0.w < 1.5;
		v_top = top ? CUSTOM0.xyz : CUSTOM1.xyz;
		v_bottom = top ? CUSTOM1.xyz : CUSTOM0.xyz;
		v_cut = 1.0;
	}
	if (CUSTOM0.x > 0.5) {
		float qz = v_cut > 0.5 ? (CUSTOM0.w < 1.5 ? b3d_height(vec3(CUSTOM0.xy, 0.0), q) : b3d_height(vec3(CUSTOM0.x, 0.0, CUSTOM0.z), q))
			: b3d_height(CUSTOM0.xyz, q);
		VERTEX.y = qz / 32.0;
		if (abs(CUSTOM2.w) > 0.0)
			UV.y = CUSTOM2.w * (b3d_peg(CUSTOM2.xyz) - qz);
	}
	int sec = int(CUSTOM3.x + 0.5) - 1;
	int which = int(CUSTOM3.y + 0.5) % 4;
	float v_over = 0.0;
	v_sky_reach = 1.0;
	float pan = CUSTOM3.z;
	if (pan > 0.5) {
		vec4 wp = b3d_texel(b3d_wallpan, int(pan + 0.5) - 1);
		UV += vec2(wp.x / tile_info.x, wp.y * tile_info.w / (256.0 * tile_info.y));
	} else if (pan < -0.5) {
		vec4 sp = b3d_texel(b3d_sector, 2 * (int(-pan + 0.5) - 1) + 1);
		UV += (which == 1 ? sp.zw : sp.xy) * tile_info.zw / (256.0 * tile_info.xy);
	}
	float fx = 0.0;
	if (sec >= 0) {
		vec4 f = b3d_texel(b3d_sector, 2 * sec);
		fx = which == 0 ? f.x : which == 1 ? f.y : f.z;
		v_over = mod(f.w, 4.0) > 1.5 ? 1.0 : 0.0;
		v_sky_reach = f.w > 3.5 ? 0.0 : 1.0;
		" + (water ? "v_sky = mod(f.w, 2.0);" : "") + @"
	}
	v_shade = 127.0 - COLOR.r * 255.0 + fx;
	v_rate = COLOR.g * 255.0;
	v_portal = portal_copy;
	v_own = UV2.x - 1.0;
	v_alpha = UV2.y;
	v_wall = abs(CUSTOM2.w) > 0.0 ? 1.0 : 0.0;
	v_pal = int(CUSTOM3.w + 0.5);
	v_window = b3d_window_of(CUSTOM3.y, portal_copy);
	vec4 clip = PROJECTION_MATRIX * (MODELVIEW_MATRIX * vec4(VERTEX, 1.0));
	clip.z *= (v_portal > 0.5 ? 0.9996 : 1.0);
	// (the light from the sky's shadows, its projection the only orthogonal one: a room over the open sky casts none)
	POSITION = b3d_camera_view(INV_VIEW_MATRIX[3].xyz) && b3d_culled(CUSTOM3.y, portal_copy) || v_over > 0.5 && PROJECTION_MATRIX[3][3] > 0.5 ? vec4(2.0, 2.0, 2.0, 1.0) : clip;
}

void fragment() {
	light_world = " + PortalRenderer.WorldPos + @";
	light_sky = v_sky_reach;
	light_lit = 1.0;
	if (portal_clipped(light_world, v_portal, CAMERA_POSITION_WORLD)) discard;
	if (stack_discard(light_world, CAMERA_POSITION_WORLD, v_portal, v_own)) discard;
	if (b3d_camera_view(CAMERA_POSITION_WORLD) && b3d_outside(v_window, light_world, CAMERA_POSITION_WORLD)) discard;
	if (v_cut > 0.5) {
		// (in the copy's own place: a portal copy is moved by its node)
		vec3 mw = (inverse(MODEL_MATRIX) * vec4(light_world, 1.0)).xyz;
		vec2 fq = vec2(mw.z, mw.x) * 32.0;
		float fz = mw.y * 32.0;
		if (fz > b3d_height(v_top, fq) + 0.05 || fz < b3d_height(v_bottom, fq) - 0.05) discard;
	}
	" + (wallPortal ? WallPortalFace : @"
	vec2 uv = UV;
	if (npot_wall.x > 1.0 && v_wall > 0.5) {
		float m = mod(uv.y, npot_wall.x);
		uv = vec2(uv.x + floor(m) * npot_wall.y, floor(uv.y / npot_wall.x) + m);
	}
	" + (water ? WaterFx.RippleCode : "") + @"
	vec4 t = texture(albedo_texture, uv);
	if (t.a < " + (translucent ? "0.05" : "0.5") + @") discard;
	float dist = -VERTEX.z * 32.0;
	float vis = dist * build_visibility * v_rate;
	float fog = v_pal == 1 ? 1.0 : 0.0;
	vec3 c = fog > 0.5 ? build_color_fog(t.rgb, v_shade, vis, 1.0) : build_color_pal(t.rgb, v_pal, v_shade, vis);
	ALBEDO = t.rgb;
	if (tile_glows > 0.5)
		c = build_glow(t.rgb, c);
	EMISSION = c;
	ROUGHNESS = fog_roughness(v_shade, vis, fog);
	// (occlusion under the water's fog: it fades with the distance as all else there, the fog's own colour never darkened)
	" + (water || translucent ? "" : AmbientOcclusion.ShaderApply) + WaterFx.Apply("v_portal") + @"
	" + (water ? WaterFx.SurfaceCode(waterMarch) : translucent ? "ALPHA = t.a * v_alpha;" : "")) + @"
}
" + BuildShading.LightFunction;
}
