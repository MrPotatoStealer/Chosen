using System.Collections.Generic;
using Chosen.Data;
using Chosen.Sim;
using Godot;
using Chosen.Shared.Blood.Map;

namespace Chosen.Render;

/// <summary>
/// Draws the world's sprites the way Blood3D's client did (sprites.qc, after Build's viewProcessSprites and Polymost):
/// face sprites stand upright facing the camera, wall sprites lie along their angle, floor sprites lie flat; 5- and
/// 8-direction rotations (view types 1 and 2), tile animation, flips, offsets, translucency (Blood's 1/3 and 2/3),
/// and shade: the sprite's plus its sector's, darkening with distance by the map's visibility.
/// </summary>
public partial class SpriteRenderer : Node3D
{
    // shader parameter names, made once: a string argument becomes a new StringName on every call (garbage every frame)
    static readonly StringName ShadeName = "shade", AlphaName = "alpha", FlipName = "flip", TexName = "tex", KindName = "kind",
        FaceXoName = "face_xo", CopyName = "portal_copy", ZClipName = "zclip";
    // the shaders' portal clip (PortalRenderer.Clip) is a material uniform, the materials kept per value: 0 the sprite
    // itself, k a copy seen through portal row k-1, -1 not clipped (reaching through a portal plane: its two parts
    // meet). Not an instance uniform: a copy that lost it would be clipped as the level and vanish behind the portal
    // (Godot's instance uniform slots of freed nodes can land on others; see BuildShading's fx_shade).

    static readonly Core.Cvar VoxelsOn = Core.Cvars.Register("b3d_voxels", "1", "draw voxel models in place of their sprites", saved: true);

    World _world;
    SectorMap _shade;
    // a sprite's nodes: copy 0 the sprite itself, k its copy beyond portal row k-1 (PortalRenderer: drawn moved by -offset
    // where that portal shows the linked room)
    sealed class Drawn
    {
        public MeshInstance3D Quad, Voxel, Flame;      // the quad, a voxel model drawn instead, a lit torch's flame
        public MeshInstance3D Shadow;                  // its shadow on the floor (kViewEffectShadow)
        public int Frame;                              // the last frame it was drawn in (copies)
        public bool Copy;                              // a portal copy: behind the portal face, in the occluder's shadow
    }
    readonly Dictionary<(Sprite s, int copy), Drawn> _drawn = new();
    int _frameNo;
    // what each node was last given: Godot calls cost (a getter makes a wrapper, a setter marshals), and every sprite is
    // placed every frame, so only what changed is sent
    sealed class Sent
    {
        public Material Mat;
        public Mesh Mesh;
        public Transform3D Xf;
        public bool HasXf;
        public float Shade = float.NaN, Alpha = float.NaN;
        public float Glows;
        public Vector2 Flip = new(-1, -1);
        public float Kind = float.NaN, FaceXo = float.NaN;
        public Vector2 ZClip = new(float.NaN, float.NaN);
        public int Visible = -1, Cast = -1;
        public uint Layers;
    }
    readonly Dictionary<GeometryInstance3D, Sent> _sent = new();

    // (the same node asked for several times in a row as it is placed: one dictionary lookup)
    GeometryInstance3D _sentNode;
    Sent _sentLast;

    Sent SentTo(GeometryInstance3D n)
    {
        if (ReferenceEquals(n, _sentNode))
            return _sentLast;
        if (!_sent.TryGetValue(n, out var s))
            _sent[n] = s = new Sent();
        _sentNode = n;
        _sentLast = s;
        return s;
    }

    void SetVisible(GeometryInstance3D n, bool v)
    {
        var s = SentTo(n);
        if (s.Visible != (v ? 1 : 0))
        {
            n.Visible = v;
            s.Visible = v ? 1 : 0;
        }
    }

    // drawing a mirror's copies (the sprites in front of a mirror wall): each placed as it stands, seen from the eye
    // reflected behind the mirror, then reflected in the wall's plane (_mirrorXf, Godot); a face sprite, which the shader
    // turns to the real camera, shows its picture flipped and its x offset the other way
    bool _mirroring;
    Transform3D _mirrorXf = Transform3D.Identity;

    void SetTransform(GeometryInstance3D n, in Transform3D xf)
    {
        var s = SentTo(n);
        var to = _mirroring ? _mirrorXf * xf : xf;
        if (!s.HasXf || s.Xf != to)
        {
            n.Transform = to;
            s.Xf = to;
            s.HasXf = true;
        }
    }

    /// <summary>A face sprite's flip: mirrored in a mirror's copy.</summary>
    Vector2 FaceFlip(bool x, bool y = false) => new(x ^ _mirroring ? 1 : 0, y ? 1 : 0);

    void SetMaterial(GeometryInstance3D n, Material m)
    {
        var s = SentTo(n);
        if (s.Mat != m)
        {
            n.MaterialOverride = m;
            s.Mat = m;
        }
    }

    float _glows;     // the sprite being drawn is a light (BuildShading.GlowTile, a torch lit): 1; a light fixture's (its pale glass glows too): 2
    static readonly StringName GlowsName = "glows", UnlitName = "unlit";

    void SetParams(GeometryInstance3D n, float shade, float alpha, Vector2? flip = null)
    {
        var s = SentTo(n);
        if (s.Shade != shade)
        {
            n.SetInstanceShaderParameter(ShadeName, shade);
            s.Shade = shade;
        }
        if (s.Alpha != alpha)
        {
            n.SetInstanceShaderParameter(AlphaName, alpha);
            s.Alpha = alpha;
        }
        float glows = _glows;
        if (s.Glows != glows)
        {
            n.SetInstanceShaderParameter(GlowsName, glows);
            s.Glows = glows;
        }
        if (flip is Vector2 f && s.Flip != f)
        {
            n.SetInstanceShaderParameter(FlipName, f);
            s.Flip = f;
        }
    }

    /// <summary>The sprite shader's kind (0 face, 1 wall or floor) and a face sprite's x offset (Godot units).</summary>
    void SetZClip(GeometryInstance3D n, Vector2 zclip)
    {
        var s = SentTo(n);
        if (s.ZClip != zclip)
        {
            n.SetInstanceShaderParameter(ZClipName, zclip);
            s.ZClip = zclip;
        }
    }

    static readonly Vector2 NoZClip = new(-1e9f, 1e9f);

    /// <summary>A level from its Build data: the heights (Godot y) a sprite is drawn between, its own sector's floor
    /// and ceiling where it is (Build's classic renderer clips a sprite to them: a z-motion sprite sector keeps its
    /// risers under the floor, E1M1's fire 268 in 59 seen from the yard below); not past a sky, nor past a portal plane
    /// of its own sector (a sprite reaching through it: its parts meet). One reaching towards a portal of another
    /// sector is still clipped to its own (E1M1's fire 711 under 59's floor, tall enough to reach the yard's linked
    /// ceiling).</summary>
    Vector2 ZClipOf(int own, Vector3 org)
    {
        if (_world == null || own < 0 || _world.Board == null || own >= _world.Board.SectorCount)
            return NoZClip;
        var sec = _world.Board.Sectors[own];
        bool floorPortal = false, ceilPortal = false;
        foreach (var l in PortalLinks())
            if (l.Sector == own && l.ConvertedPortal)
            {
                if (l.Dir > 0)
                    floorPortal = true;
                else
                    ceilPortal = true;
            }
        float lo = (sec.FloorStat & 1) != 0 || floorPortal ? -1e9f : (_world.Map.FloorZ(own, org.X, org.Y) - 1) / Coords.QuakeScale;
        float hi = (sec.CeilingStat & 1) != 0 || ceilPortal ? 1e9f : (_world.Map.CeilZ(own, org.X, org.Y) + 1) / Coords.QuakeScale;
        return new Vector2(lo, hi);
    }

    void SetKind(GeometryInstance3D n, float kind, float faceXo)
    {
        if (_mirroring && kind < 0.5f)
            faceXo = -faceXo;
        var s = SentTo(n);
        if (s.Kind != kind)
        {
            n.SetInstanceShaderParameter(KindName, kind);
            s.Kind = kind;
        }
        if (s.FaceXo != faceXo)
        {
            n.SetInstanceShaderParameter(FaceXoName, faceXo);
            s.FaceXo = faceXo;
        }
    }

    // the light-effect sector each sprite is in (World.FxLightShade's search, over every such sector): looked up again
    // when the sprite moves, or every frame while it is a moving sector
    readonly Dictionary<Sprite, (Vector3 at, Sector sec)> _fxSector = new();

    int FxShade(Sprite s)
    {
        var p = s.Origin + new Vector3(0, 0, 0.0625f);
        if (!_fxSector.TryGetValue(s, out var c) || c.at != p || (c.sec != null && c.sec.IsMover))
        {
            _fxSector[s] = c = (p, _world.FxSectorAt(p) ?? _world.FxSectorAt(p - new Vector3(0, 0, 2)));
        }
        return c.sec == null ? 0 : System.Math.Clamp(c.sec.FxSprite, -128, 127);
    }

    // each sprite's Build sector, looked up again when it moves
    readonly Dictionary<Sprite, (Vector3 at, int sec)> _bSector = new();

    /// <summary>The sprite's Build sector now (-1 without Build's sectors), the cache brought up to date: everything
    /// drawing it reads this first (the portal clip read the cache before the shade had refreshed it, so on the first
    /// draw after a link crossing it still took the old room: E1M1's napalm ball, just gone up through the sky link,
    /// was drawn unclipped over the sky for a frame).</summary>
    int BSectorOf(Sprite s)
    {
        if (_world?.Map == null)
            return -1;
        if (!_bSector.TryGetValue(s, out var c) || c.at != s.Origin)
            _bSector[s] = c = (s.Origin, _world.PeekSpriteBSector(s));
        return c.sec;
    }

    /// <summary>viewProcessSprites: the shade of the Build sector the sprite is in, its light effect now included;
    /// null without Build's sectors.</summary>
    int? SectorShade(Sprite s)
    {
        if (_world?.Map == null)
            return null;
        int sec = BSectorOf(s);
        return sec < 0 ? null : _world.SectorSpriteShade(sec);
    }

    /// <summary>Is the sprite in a sector whose floor has palette 1 (its distance fogs to white:
    /// BuildShading.FogFloor)?</summary>
    bool InFog(Sprite s) => BuildShading.FogFloor.Count > 0 && BuildShading.FogFloor.Contains(BSectorOf(s));

    /// <summary>Sends every node's parameters again on the next draw (debugging: the spriteresend command).</summary>
    public void Resend()
    {
        _sent.Clear();
        _sentNode = null;
    }

    void Free(GeometryInstance3D n)
    {
        if (n == null)
            return;
        _sent.Remove(n);
        if (ReferenceEquals(n, _sentNode))
            _sentNode = null;
        n.QueueFree();
    }

    void Free(Drawn d)
    {
        Free(d.Quad);
        Free(d.Voxel);
        Free(d.Flame);
        Free(d.Shadow);
    }

    void Hide(GeometryInstance3D n)
    {
        if (n != null)
            SetVisible(n, false);
    }

    /// <summary>A sprite's render layer by its room (its Build sector's colour: DynamicLights.RoomColourOf).</summary>
    static uint RoomLayerOf(Sprite s) =>
        DynamicLights.RoomLayer(DynamicLights.RoomColourOf(StackedRooms.SpriteSectorOf?.Invoke(s) ?? -1));

    MeshInstance3D Quad(Drawn d)
    {
        if (d.Quad == null)
        {
            d.Quad = new MeshInstance3D { Mesh = _quad, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, IgnoreOcclusionCulling = d.Copy };
            AddChild(d.Quad);
        }
        return d.Quad;
    }
    readonly Dictionary<(Texture2D, bool, int), ShaderMaterial> _voxelMaterials = new();
    static Shader _voxelOpaque, _voxelTranslucent, _voxelOpaqueUnder, _voxelTranslucentUnder;
    readonly Dictionary<(Texture2D, bool, int), ShaderMaterial> _voxelUnderMaterials = new();

    static readonly string VoxelShaderBody = PortalRenderer.Clip + @"
uniform sampler2D tex : source_color, filter_nearest, repeat_disable;
instance uniform float shade = 0.0;
instance uniform float alpha = 1.0;
instance uniform float glows = 0.0;      // a torch's or a lamp's picture: its bright colours glow (r_glow); and whatever is fullbright; 2 a light fixture's, its pale glass too
uniform float portal_copy = 0.0;      // the material's (CopyName)
global uniform float build_visibility;
" + BuildShading.ShadeFunction + AmbientOcclusion.ShaderFunction + WaterFx.Functions + @"

void fragment() {
	light_world = " + PortalRenderer.WorldPos + @";
	light_sky = 1.0;
	light_lit = shade <= -64.0 ? 0.0 : 1.0;
	if (portal_clipped(light_world, portal_copy, CAMERA_POSITION_WORLD)) discard;
	vec4 t = texture(tex, UV);
	float dist = -VERTEX.z * 32.0;     // depth, as Build's
	ALBEDO = t.rgb;
	EMISSION = build_color_fog(t.rgb, shade - (shade > 500.0 ? 1000.0 : 0.0), dist * build_visibility * 16.0, shade > 500.0 ? 1.0 : 0.0);
	if (glows > 1.5) EMISSION = build_glow_lamp(t.rgb, EMISSION);
	else if (glows > 0.5 || shade <= -64.0) EMISSION = build_glow(t.rgb, EMISSION);
	if (shade <= -64.0) EMISSION *= max(hdr_highlight, 1.0 + glow_amount);
	ROUGHNESS = fog_roughness(shade - (shade > 500.0 ? 1000.0 : 0.0), dist * build_visibility * 16.0, shade > 500.0 ? 1.0 : 0.0);
	ALPHA_OUT
	WATER_APPLY
}
".Replace("WATER_APPLY", WaterFx.Apply("portal_copy"));
    readonly List<(Sprite s, int copy)> _gone = new();

    /// <summary>The portals drawn this frame (PortalRenderer.Active): the sprites of the linked room near them get
    /// copies.</summary>
    public IReadOnlyList<PortalRenderer.ActivePortal> Portals = System.Array.Empty<PortalRenderer.ActivePortal>();

    /// <summary>The sprite not drawn (the local player's own body).</summary>
    public Sprite ViewSprite;
    readonly Dictionary<(Texture2D, bool, int), ShaderMaterial> _materials = new();
    static QuadMesh _quad;
    static Shader _opaque, _translucent, _opaqueUnder, _translucentUnder;
    readonly Dictionary<(Texture2D, bool, int), ShaderMaterial> _underMaterials = new();
    // the sprite being drawn is in a sector the camera's walk doesn't reach itself (SectorVisibility.DirectlySeen):
    // drawn with the shaders the sky hides (no SpriteMark; translucent ones UnderSky)
    bool _under;

    static readonly string ShaderBody = PortalRenderer.Clip + @"
uniform sampler2D tex : source_color, filter_nearest_mipmap, repeat_disable;
instance uniform float shade = 0.0;      // Build shade: the sprite's plus its sector's
instance uniform float alpha = 1.0;
instance uniform float glows = 0.0;      // a torch's or a lamp's picture: its bright colours glow (r_glow); and whatever is fullbright; 2 a light fixture's, its pale glass too
instance uniform float unlit = 0.0;      // a halo: the glow of a light, not lit by it
instance uniform vec2 flip = vec2(0.0);
// 0: a face sprite, turned to the camera drawing it about the vertical (its node: width along x, height along y,
// face_xo its x offset along the camera's right); 1: a wall or floor sprite, its depth taken lifted off its surface
// towards the camera (depth precision: 0.25 Quake units plus 0.002 of the distance). Only the depth: a sprite moved
// that far left a slit where it meets a surface edge to edge (E2M3: a floor sprite as the ceiling over a doorway,
// beside the next sector's ceiling at its height, showed the sky above through the seam). Done here, the nodes keep
// still while the camera moves.
instance uniform float kind = 0.0;
instance uniform float face_xo = 0.0;
// the heights (Godot y) it is drawn between: its own sector's floor and ceiling (a level from its Build data: Build
// clips a sprite to them)
instance uniform vec2 zclip = vec2(-1e9, 1e9);
uniform float portal_copy = 0.0;      // the material's (CopyName)
global uniform float build_visibility;
varying vec3 depth_at;      // view space: where the depth is taken
" + BuildShading.ShadeFunction + AmbientOcclusion.ShaderFunction + WaterFx.Functions + @"

void vertex() {
	vec3 cam = INV_VIEW_MATRIX[3].xyz;
	vec3 c = MODEL_MATRIX[3].xyz;
	vec3 wp, n;
	if (kind < 0.5) {
		vec3 r = INV_VIEW_MATRIX[0].xyz;
		r.y = 0.0;
		r = dot(r, r) > 1e-8 ? normalize(r) : vec3(1.0, 0.0, 0.0);
		float w = length(MODEL_MATRIX[0].xyz), h = length(MODEL_MATRIX[1].xyz);
		wp = c + r * (face_xo + VERTEX.x * w) + vec3(0.0, VERTEX.y * h, 0.0);
		n = cross(r, vec3(0.0, 1.0, 0.0));
		depth_at = (VIEW_MATRIX * vec4(wp, 1.0)).xyz;
	} else {
		wp = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
		n = normalize(MODEL_MATRIX[2].xyz);
		if (dot(cam - c, n) < 0.0)
			n = -n;
		float lift = 0.25 / 32.0 + length(cam - c) * 0.002;
		// off its surface, or towards the eye if that is nearer: a depth is along the view, and with the view along the surface
		// the first changes nothing (E1M1: ceiling lamps gone looking level, wall sprites as they neared the screen's edge)
		vec3 off = (VIEW_MATRIX * vec4(wp + n * lift, 1.0)).xyz;
		vec3 at = (VIEW_MATRIX * vec4(wp, 1.0)).xyz;
		vec3 toward = at * (1.0 - min(lift / max(length(at), 1e-4), 0.9));
		depth_at = off.z > toward.z ? off : toward;
	}
	VERTEX = (inverse(MODEL_MATRIX) * vec4(wp, 1.0)).xyz;
	NORMAL = normalize(transpose(mat3(MODEL_MATRIX)) * n);
}

void fragment() {
	light_world = " + PortalRenderer.WorldPos + @";
	light_sky = 1.0;
	// (a picture that is itself a light takes none: a lantern or a candle burned out off centre by its own light beside it)
	light_lit = shade <= -64.0 || unlit > 0.5 ? 0.0 : 1.0;
	if (portal_clipped(light_world, portal_copy, CAMERA_POSITION_WORLD)) discard;
	if (light_world.y < zclip.x || light_world.y > zclip.y) discard;
	vec2 uv = UV;
	if (flip.x > 0.5) uv.x = 1.0 - uv.x;
	if (flip.y > 0.5) uv.y = 1.0 - uv.y;
	vec4 t = texture(tex, uv);
	if (t.a < 0.5) discard;
	vec4 dc = PROJECTION_MATRIX * vec4(depth_at, 1.0);
	DEPTH = dc.z / dc.w;
	float dist = -VERTEX.z * 32.0;     // depth, as Build's
	// lit on the side seen: a two-sided face's back gets its normal flipped by the engine, after the vertex turned it to
	// the camera, so a wall or floor sprite seen from behind took no light at all (DWE3M1's ceiling tubes, black in their
	// own glow)
	if (dot(NORMAL, VIEW) < 0.0) NORMAL = -NORMAL;
	ALBEDO = t.rgb;
	EMISSION = build_color_fog(t.rgb, shade - (shade > 500.0 ? 1000.0 : 0.0), dist * build_visibility * 16.0, shade > 500.0 ? 1.0 : 0.0);
	if (glows > 1.5) EMISSION = build_glow_lamp(t.rgb, EMISSION);
	else if (glows > 0.5 || shade <= -64.0) EMISSION = build_glow(t.rgb, EMISSION);
	if (shade <= -64.0) EMISSION *= max(hdr_highlight, 1.0 + glow_amount);
	ROUGHNESS = fog_roughness(shade - (shade > 500.0 ? 1000.0 : 0.0), dist * build_visibility * 16.0, shade > 500.0 ? 1.0 : 0.0);
	ALPHA_OUT
	WATER_APPLY
}
".Replace("WATER_APPLY", WaterFx.Apply("portal_copy"));

    /// <summary>Loads the level's sprite tiles and voxel models now (while the level loads), not on the first frame
    /// they are seen.</summary>
    void Preload()
    {
        // every picture a sprite may show as it stands: its views round (as DrawSprite steps through them), each one's
        // animation frames, a lit torch's next tile (DWE3M1's first frame loaded 17 more, the next ones a few each)
        var tiles = new List<(int, int)>();
        foreach (var s in _world.Sprites)
        {
            if (s.Removed || s.Picnum <= 0 || !Tiles.Valid(s.Picnum))
                continue;
            for (int p0 = s.Picnum; p0 <= s.Picnum + 1; p0++)
            {
                if (!Tiles.Valid(p0) || (p0 != s.Picnum && !(s.StatNum == K.kStatDecoration && s.Type == K.kDecorationTorch)))
                    continue;
                int vt = Tiles.View(p0), views = vt == 1 ? 5 : vt == 2 ? 8 : 1, pic = p0;
                for (int v = 0; v < views && Tiles.Valid(pic); v++, pic += Tiles.AnimNum(pic) + 1)
                {
                    // (animation type 3 runs backwards from the tile)
                    int n = Tiles.AnimType(pic) != 0 ? Tiles.AnimNum(pic) : 0, dir = Tiles.AnimType(pic) == 3 ? -1 : 1;
                    for (int a = 0; a <= n; a++)
                        if (Tiles.Valid(pic + a * dir))
                            tiles.Add((pic + a * dir, s.Pal));
                }
            }
        }
        TileTextures.Preload(tiles);
        // (not their materials: made in another order, coplanar sprites drew differently, DWE3M1's ceiling lamp 11596
        // lost to the ceiling and the glow 11601 under it brightened)
        foreach (var s in _world.Sprites)
        {
            if (s.Removed || s.Picnum <= 0 || !Tiles.Valid(s.Picnum))
                continue;
            if (VoxelsOn.Bool && Voxels.TryGet(s.Picnum, out var info))
            {
                Voxels.Mesh(info.Model);
                Voxels.Palette(s.Pal);
            }
        }
    }

    public void Setup(World world, SectorMap shade)
    {
        _world = world;
        _shade = shade;
        // (a level's materials and pictures go with it: kept, each add-on's new pictures added a set, until the graphics driver
        // ran out of room for them and whatever came next wasn't drawn; Game.StartLevel frees them)
        _voxelMaterials.Clear();
        _voxelUnderMaterials.Clear();
        _materials.Clear();
        _underMaterials.Clear();
        _shadowMaterials.Clear();
        Preload();
        foreach (var d in _drawn.Values)
            Free(d);
        _drawn.Clear();
        _sent.Clear();
        _sentNode = null;
        _fxSector.Clear();
        _bSector.Clear();
        _shadowSector.Clear();
        // both sides: the models are closed, and a mirrored one turns inside out
        _voxelOpaque ??= new Shader { Code = "shader_type spatial;\nrender_mode ambient_light_disabled, specular_disabled, cull_disabled;\n" + BuildSky.SpriteMark + VoxelShaderBody.Replace("ALPHA_OUT", AmbientOcclusion.ShaderApply) + BuildShading.LightFunction };
        _voxelTranslucent ??= new Shader { Code = "shader_type spatial;\nrender_mode ambient_light_disabled, specular_disabled, cull_disabled, blend_mix, depth_draw_never;\n" + VoxelShaderBody.Replace("ALPHA_OUT", "ALPHA = alpha;") + BuildShading.LightFunction };
        _voxelOpaqueUnder ??= new Shader { Code = "shader_type spatial;\nrender_mode ambient_light_disabled, specular_disabled, cull_disabled;\n" + VoxelShaderBody.Replace("ALPHA_OUT", AmbientOcclusion.ShaderApply) + BuildShading.LightFunction };
        _voxelTranslucentUnder ??= new Shader { Code = "shader_type spatial;\nrender_mode ambient_light_disabled, specular_disabled, cull_disabled, blend_mix, depth_draw_never;\n" + BuildSky.UnderSky + VoxelShaderBody.Replace("ALPHA_OUT", "ALPHA = alpha;") + BuildShading.LightFunction };
        _opaqueUnder ??= new Shader { Code = "shader_type spatial;\nrender_mode ambient_light_disabled, specular_disabled, cull_disabled;\n" + ShaderBody.Replace("ALPHA_OUT", AmbientOcclusion.ShaderApply) + BuildShading.LightFunction };
        _translucentUnder ??= new Shader { Code = "shader_type spatial;\nrender_mode ambient_light_disabled, specular_disabled, cull_disabled, blend_mix, depth_draw_never;\n" + BuildSky.UnderSky + "global uniform float soft_sprites;\nuniform sampler2D soft_depth : hint_depth_texture, filter_nearest, repeat_disable;\n" + ShaderBody.Replace("ALPHA_OUT", "float soft = 1.0; if (soft_sprites > 0.5) { vec4 sv = INV_PROJECTION_MATRIX * vec4(SCREEN_UV * 2.0 - 1.0, texture(soft_depth, SCREEN_UV).r, 1.0); soft = clamp((VERTEX.z - sv.z / sv.w) / 0.6, 0.0, 1.0); } ALPHA = alpha * soft;") + BuildShading.LightFunction };
        _quad ??= new QuadMesh { Size = Vector2.One, CustomAabb = new Aabb(new Vector3(-0.5f, -0.5f, -0.5f), Vector3.One) };
        _opaque ??= new Shader { Code = "shader_type spatial;\nrender_mode ambient_light_disabled, specular_disabled, cull_disabled;\n" + BuildSky.SpriteMark + ShaderBody.Replace("ALPHA_OUT", AmbientOcclusion.ShaderApply) + BuildShading.LightFunction };
        _translucent ??= new Shader { Code = "shader_type spatial;\nrender_mode ambient_light_disabled, specular_disabled, cull_disabled, blend_mix, depth_draw_never;\n" + "global uniform float soft_sprites;\nuniform sampler2D soft_depth : hint_depth_texture, filter_nearest, repeat_disable;\n" + ShaderBody.Replace("ALPHA_OUT", "float soft = 1.0; if (soft_sprites > 0.5) { vec4 sv = INV_PROJECTION_MATRIX * vec4(SCREEN_UV * 2.0 - 1.0, texture(soft_depth, SCREEN_UV).r, 1.0); soft = clamp((VERTEX.z - sv.z / sv.w) / 0.6, 0.0, 1.0); } ALPHA = alpha * soft;") + BuildShading.LightFunction };
        // a shadow: the tile's shape in black (pal 5, shade 127), translucent, unlit
        _shadowShader ??= new Shader { Code = "shader_type spatial;\nrender_mode unshaded, cull_disabled, blend_mix, depth_draw_never;\n" + BuildSky.UnderSky + ShaderBody
            .Replace("ALBEDO = t.rgb;", "ALBEDO = vec3(0.0);").Replace("EMISSION = build_color_fog(t.rgb, shade - (shade > 500.0 ? 1000.0 : 0.0), dist * build_visibility * 16.0, shade > 500.0 ? 1.0 : 0.0);", "")
            .Replace("ALPHA_OUT", "ALPHA = alpha;") };
    }

    /// <summary>A sprite's nodes (copy 0: itself), made the first time it is drawn.</summary>
    Drawn NodesOf(Sprite s, int copy)
    {
        if (_drawn.TryGetValue((s, copy), out var d))
            return d;
        _drawn[(s, copy)] = d = new Drawn { Copy = copy != 0 };
        return d;
    }

    ShaderMaterial Material(Texture2D tex, bool translucent, float clip)
    {
        var mats = _under ? _underMaterials : _materials;
        if (!mats.TryGetValue((tex, translucent, (int)clip), out var m))
        {
            m = new ShaderMaterial { Shader = _under ? (translucent ? _translucentUnder : _opaqueUnder) : translucent ? _translucent : _opaque };
            m.SetShaderParameter(TexName, tex);
            m.SetShaderParameter(CopyName, clip);
            if (translucent)
                m.RenderPriority = 1;
            mats[(tex, translucent, (int)clip)] = m;
        }
        return m;
    }

    /// <summary>Is the sprite (itself, not a portal copy) in a sector the camera's walk doesn't reach but through a
    /// link? The sky then hides it as it hides the level there (no SpriteMark).</summary>
    bool Under(Sprite s, int copy) => copy == 0 && !SectorVisibility.All && BSectorOf(s) is int sec && sec >= 0
        && !SectorVisibility.DirectlySeen(sec);

    static Shader _shadowShader;
    readonly Dictionary<(Texture2D, int), ShaderMaterial> _shadowMaterials = new();
    static readonly Core.Cvar SpriteShadows = Core.Cvars.Register("b3d_spriteshadows", "1",
        "Blood's shadows under monsters and things in the air (0: none)", saved: true);
    // the Build sector each shadow-casting sprite is in (its floor is the shadow's), looked up again when it moves
    readonly Dictionary<Sprite, (Vector3 at, int sec)> _shadowSector = new();

    ShaderMaterial ShadowMaterial(Texture2D tex, float clip)
    {
        if (!_shadowMaterials.TryGetValue((tex, (int)clip), out var m))
        {
            m = new ShaderMaterial { Shader = _shadowShader, RenderPriority = 1 };
            m.SetShaderParameter(TexName, tex);
            m.SetShaderParameter(CopyName, clip);
            _shadowMaterials[(tex, (int)clip)] = m;
        }
        return m;
    }

    /// <summary>viewProcessSprites' shadows (kViewEffectShadow): monsters (not the player's own body), and things
    /// that physics moves while in the air (a corpse, being no thing type, always). Only over a floor below the
    /// camera.</summary>
    static bool CastsShadow(Sprite s) =>
        s.StatNum == K.kStatDude
        || s.StatNum == K.kStatThing && (s.HitTag & K.kPhysMove) != 0
           && (s.Type < K.kThingBase || s.Type >= K.kThingMax || s.FlorHit == 0);

    /// <summary>The floor under a sprite for its shadow (Quake z): its Build sector's floor there, or the blocking
    /// floor sprite it stands on (NBlood's fake floors); NaN if it is in no sector.</summary>
    float ShadowFloor(Sprite s, Vector3 org)
    {
        if (s.FlorHitEnt is Sprite fs && (fs.Cstat & Sim.Cstat.AlignMask) == Sim.Cstat.Floor && (fs.Cstat & 1) != 0
            && (fs.Cstat & Sim.Cstat.Invisible) == 0)
            return fs.Origin.Z + 0.0625f;
        if (_shade == null)
            return float.NaN;
        int layer = -1;
        if (!_shadowSector.TryGetValue(s, out var c) || c.at != org)
        {
            int keep = _shadowSector.TryGetValue(s, out var old) ? old.sec
                : s.BSector > 0 && s.BSector <= _shade.SectorCount ? s.BSector - 1 : -1;
            int sec = _shade.SectorAt(org, keep, layer);
            _shadowSector[s] = c = (org, sec >= 0 ? sec : keep);
        }
        return c.sec < 0 ? float.NaN : _shade.FloorZ(c.sec, org.X, org.Y);
    }

    /// <summary>NBlood's shadow over a floor it draws through (IsRorSector: the game's portal links, tile 504): the
    /// floor of the room below, moved back by the link's offset (floor), drawn as that portal's copy (clip). False if
    /// that portal isn't drawn this frame (nothing to see the shadow through). A plain floor is left as it is.</summary>
    bool ThroughRorFloor(Vector3 org, ref float floor, ref int clip)
    {
        var l = _world.Collision.LinkAtPlane(new Vector3(org.X, org.Y, floor), 1);
        if (l == null || !l.Portal)
            return true;
        int mark = -1;
        foreach (var a in Portals)
            if (a.Dir == 1 && (a.Offset - l.Offset).LengthSquared() < 1f && org.X >= a.Face.Position.X - 1 && org.X <= a.Face.End.X + 1
                && org.Y >= a.Face.Position.Y - 1 && org.Y <= a.Face.End.Y + 1)
            {
                mark = a.Mark;
                break;
            }
        if (mark < 0 || _shade == null)
            return false;
        var below = new Vector3(org.X, org.Y, floor) + l.Offset - new Vector3(0, 0, 1);
        int sec = _shade.SectorAt(below, -1, -1);
        if (sec < 0)
            return false;
        floor = _shade.FloorZ(sec, below.X, below.Y) - l.Offset.Z;
        clip = mark;
        return true;
    }

    /// <summary>kViewEffectShadow: the sprite's tile (pic, its rotation frame's mirroring only) as a face sprite a
    /// quarter as tall, black and translucent, standing on the floor under it, a little further from the camera than
    /// the sprite (Polymost's nudge) so the sprite stays in front.</summary>
    void DrawShadow(Sprite s, Drawn d, int pic, bool flipX, Vector3 org, Vector3 camQ, Vector3 camRight, Vector3 shift,
        int copy)
    {
        if (!SpriteShadows.Bool || !CastsShadow(s))
        {
            Hide(d.Shadow);
            return;
        }
        float floor = ShadowFloor(s, org);
        int yrep = s.YRepeat >> 2;
        var tex = float.IsNaN(floor) || floor > camQ.Z || yrep <= 0 ? null : TileTextures.Get(pic, 0);
        // a floor drawn through (a link side the client draws the room below on): the shadow lies on that room's
        // floor, in its copy behind the portal (NBlood's IsRorSector walk)
        int clip = copy;
        if (tex != null && copy == 0 && !ThroughRorFloor(org, ref floor, ref clip))
            tex = null;
        if (tex == null)
        {
            Hide(d.Shadow);
            return;
        }
        if (d.Shadow == null)
        {
            d.Shadow = new MeshInstance3D { Mesh = _quad, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, IgnoreOcclusionCulling = d.Copy };
            AddChild(d.Shadow);
        }
        float w = Tiles.W(pic) * s.XRepeat / 64f, h = Tiles.H(pic) * yrep / 64f;
        // the tile's own x offset (the sprite's isn't copied), as a face sprite's
        float xo = Tiles.XOfs(pic) * s.XRepeat / 64f;
        if (flipX)
            xo = -xo;
        xo = -xo;
        if ((Tiles.W(pic) & 1) != 0)
            xo += 0.5f * s.XRepeat / 64f;
        // Polymost moves it 2 units along the camera's view, away from it
        var fwd = Vector3.Up.Cross(camRight);
        var away = new Vector3(fwd.Z, fwd.X, 0) * 2f;
        var centre = new Vector3(org.X, org.Y, floor + h / 2f) + away;
        var xf = new Transform3D(new Basis(Vector3.Right * (w / Coords.QuakeScale), Vector3.Up * (h / Coords.QuakeScale),
            Vector3.Back * (w / Coords.QuakeScale)), Coords.QuakeToGodot(centre - shift));
        SetTransform(d.Shadow, xf);
        SetMaterial(d.Shadow, ShadowMaterial(tex, clip));
        SetParams(d.Shadow, 127, 1f / 3f, FaceFlip(flipX));
        SetKind(d.Shadow, 0, xo / Coords.QuakeScale);
        SetVisible(d.Shadow, true);
    }

    /// <summary>Places every sprite for the camera; lerp blends the last two game frames.</summary>
    public void Draw(Camera3D cam, float lerp)
    {
        if (_world == null)
            return;
        var camQ = Coords.GodotToQuake(cam.GlobalPosition);
        var camRight = cam.GlobalBasis.X;
        camRight.Y = 0;
        camRight = camRight.LengthSquared() > 1e-6f ? camRight.Normalized() : Vector3.Right;
        double clock = _world.Clock + lerp * GameClock.TicsPerFrame;
        _frameNo++;
        Frustum(cam);
        foreach (var s in _world.Sprites)
        {
            if (s == ViewSprite || s.Removed)
                continue;
            if (!s.Visible && !_drawn.ContainsKey((s, 0)))
                continue;
            // out of the view (with room for its size and its shadow): left as it is, Godot doesn't draw it; placed
            // again when it comes into view (an open level's thousands of sprites were all placed every frame:
            // Death Wish's DWE1M1, 2700 a frame, 4.7 ms)
            if (s.Visible && OutOfView(s))
            {
                // (moved since it was placed, its picture is where it was, perhaps in view: a napalm ball gone down through
                // E2M1's lake, its place now the far room's, hung on the surface until it blew up)
                if (s.DrawnAt != s.Origin)
                {
                    s.DrawnAt = s.Origin;
                    if (_drawn.TryGetValue((s, 0), out var left))
                    {
                        Hide(left.Quad);
                        Hide(left.Voxel);
                        Hide(left.Flame);
                        Hide(left.Shadow);
                    }
                }
                continue;
            }
            // in a sector the camera's walk doesn't reach: Build draws only the sprites of the sectors it visits (an open
            // level's sprites behind its walls: DWE3M1's river banks, 770 of 1850 placed a frame)
            if (s.Visible && Unseen(s))
            {
                if (_drawn.TryGetValue((s, 0), out var hid))
                {
                    Hide(hid.Quad);
                    Hide(hid.Voxel);
                    Hide(hid.Flame);
                    Hide(hid.Shadow);
                }
                continue;
            }
            s.DrawnAt = s.Origin;
            DrawSprite(s, NodesOf(s, 0), camQ, camRight, lerp, clock, Vector3.Zero, 0, -1);
        }
        // the linked room's sprites near each portal drawn: copies, seen from the eye moved into that room
        foreach (var p in Portals)
        {
            var region = p.Region.Grow(64);
            if (p.Mirror)
            {
                DrawMirrored(p, region, cam, camRight, lerp, clock);
                continue;
            }
            foreach (var s in _world.Sprites)
            {
                if (s == ViewSprite || s.Removed || !s.Visible || !region.HasPoint(s.Origin))
                    continue;
                // a wall stack: only what stands in the room behind its far wall (out of that wall's room the other way)
                if (p.Wall)
                {
                    var g = Coords.QuakeToGodot(s.Origin);
                    if ((new Vector2(g.X, g.Z) - p.FarAt).Dot(p.Out) > 0)
                        continue;
                    var dw = NodesOf(s, p.Mark);
                    dw.Frame = _frameNo;
                    DrawSprite(s, dw, camQ + p.Offset, camRight, lerp, clock, p.Offset, p.Mark, p.FarSector);
                    continue;
                }
                // a flat sprite lying on the linked plane belongs to the room this side (E2M5's pool: translucent water
                // sprites on the floor over the link), drawn there already: copied too, it would be drawn twice
                if ((s.Cstat & (Sim.Cstat.Wall | Sim.Cstat.Floor)) == Sim.Cstat.Floor && Mathf.Abs(s.Origin.Z - p.LinkedPlane) < 1f)
                    continue;
                // only the linked room's (beyond its plane): one of this room standing near the face, where the linked
                // room lies beside it (E1M8's walkway, its floor linked to the room 32 units over and a unit down) got a
                // copy over the floor beside itself, a second skull key
                if ((s.Origin.Z - p.LinkedPlane) * p.Dir > 0.5f)
                    continue;
                // only what can be seen through the face: through a water surface near (from under DWE3M1's river, the
                // whole town above was copied: 2000 sprites and voxel models, 9 million triangles); through any other
                // link at any distance (Eviction's floor link 5 to the street: its region most of the level, a
                // thousand sprites copied a frame, 7.8 ms, nearly none of them seen through its 128 by 448 hole)
                if (!SeenThrough(p, camQ, s.Origin - p.Offset, p.Water ? WaterBox : Mathf.Max(WaterBox, SpriteSize(s)), p.Water ? WaterReach : float.MaxValue))
                    continue;                // and only the far room's own (DWE3M1's underwater rooms lie side by side, each through its own link:
                // a sprite of the next one, copied through this one's offset, lay on the river's surface)
                if (p.Water && p.FarSector >= 0 && BSectorOf(s) is int sec && sec >= 0 && !FarRoom(p.FarSector).Contains(sec))
                    continue;                var d = NodesOf(s, p.Mark);
                d.Frame = _frameNo;
                StackedRooms.ViaPlane = p.LinkedPlane;
                StackedRooms.ViaStart = p.FarStart;
                DrawSprite(s, d, camQ + p.Offset, camRight, lerp, clock, p.Offset, p.Mark, p.FarSector);
                StackedRooms.ViaPlane = float.NaN;
            }
        }
        // nodes of sprites that no longer exist, copies not drawn this frame
        _gone.Clear();
        foreach (var (key, d) in _drawn)
        {
            // (the view sprite's own nodes go; its copies in a mirror stay, hidden below when not drawn)
            if (key.s.Removed || key.s == ViewSprite && key.copy == 0)
                _gone.Add(key);
            else if (key.copy != 0 && d.Frame != _frameNo)
            {
                Hide(d.Quad);
                Hide(d.Voxel);
                Hide(d.Flame);
                Hide(d.Shadow);
            }
        }
        foreach (var key in _gone)
        {
            if (key.copy == 0)
            {
                StackedRooms.Forget(key.s);
                _fxSector.Remove(key.s);
                _bSector.Remove(key.s);
                _shadowSector.Remove(key.s);
            }
            Free(_drawn[key]);
            _drawn.Remove(key);
        }
    }

    /// <summary>A mirror's copies (DoWallMirrors' viewProcessSprites from the reflected eye): every sprite in front of
    /// the mirror near it, the player's own body too (Blood draws the mirrors before it hides the view sprite), each
    /// placed for the eye reflected behind the mirror and then reflected in its plane.</summary>
    void DrawMirrored(PortalRenderer.ActivePortal p, Aabb region, Camera3D cam, Vector3 camRight, float lerp, double clock)
    {
        // the reflection in the wall's plane (Godot x, z): x - 2 n (n . (x - at)), n the way out into its room
        var n = new Vector3(p.Out.X, 0, p.Out.Y);
        var at = new Vector3(p.FarAt.X, 0, p.FarAt.Y);
        _mirrorAt = p.FarAt;
        _mirrorOut = p.Out;
        _mirrorXf = new Transform3D(new Basis(Vector3.Right - 2 * n.X * n, Vector3.Up, Vector3.Back - 2 * n.Z * n), 2 * n * n.Dot(at));
        var eyeQ = Coords.GodotToQuake(_mirrorXf * cam.GlobalPosition);
        var right = _mirrorXf.Basis * camRight;
        _mirroring = true;
        foreach (var s in _world.Sprites)
        {
            if (s.Removed || !s.Visible || !region.HasPoint(s.Origin))
                continue;
            var g = Coords.QuakeToGodot(s.Origin);
            if ((new Vector2(g.X, g.Z) - p.FarAt).Dot(p.Out) <= 0)
                continue;
            var d = NodesOf(s, p.Mark);
            d.Frame = _frameNo;
            DrawSprite(s, d, eyeQ, right, lerp, clock, Vector3.Zero, p.Mark, p.FarSector);
        }
        _mirroring = false;
    }

    // the mirror being drawn: a point of its wall and the way out into its room (Godot x, z)
    Vector2 _mirrorAt, _mirrorOut;

    /// <summary>Where the stacked-rooms walk for a sprite in a mirror starts (Quake): the eye reflected behind the
    /// mirror (eyeQ) lies outside the mirror's sector, and from there the walk's first wall is the mirror itself, a
    /// white wall, which ended it unknown (the sprite then drawn: a zombie lying in a room sharing space with the room
    /// beyond showed in the mirror, report 20261105-220910). Build walks in from mirrorsector through the mirror: the
    /// point where the line from that eye to the sprite crosses the mirror, a little into the room.</summary>
    Vector3 MirrorEntry(Vector3 eyeQ, Vector3 orgQ)
    {
        var e3 = Coords.QuakeToGodot(eyeQ);
        var s3 = Coords.QuakeToGodot(orgQ);
        Vector2 e = new(e3.X, e3.Z), s = new(s3.X, s3.Z);
        float de = (e - _mirrorAt).Dot(_mirrorOut), ds = (s - _mirrorAt).Dot(_mirrorOut);
        if (de >= 0 || ds <= 0)
            return eyeQ;
        var m = e + (s - e) * (de / (de - ds)) + _mirrorOut * (0.5f / Coords.QuakeScale);
        return Coords.GodotToQuake(new Vector3(m.X, e3.Y, m.Y));
    }

    /// <summary>Does a sprite's quad (centre and half height, Quake) reach through a portal plane near its face (its
    /// centre on the room's side or at most beyond units past the plane, its far end beyond)? Then it is drawn
    /// unclipped: its parts meet across the plane.</summary>
    bool ReachesThrough(Vector3 centre, float halfHeight, float beyond, int sector = -1)
    {
        foreach (var l in PortalLinks())
        {
            // (the sprite itself in the room beyond this link: it doesn't reach through from this side)
            if (sector >= 0 && l.FarSector == sector)
                continue;
            // (nor through a water surface drawn see-through: a swimmer under it is seen through it, its top past its
            // room's ceiling clipped there, not standing over the water: E3M3's hand, drawn whole on the surface)
            if (!l.ConvertedPortal)
                continue;
            var b = l.Bounds;
            if (centre.X < b.Position.X - 32 || centre.X > b.End.X + 32 || centre.Y < b.Position.Y - 32 || centre.Y > b.End.Y + 32)
                continue;
            float near = (centre.Z - l.Plane) * l.Dir;
            if (near > -beyond && near < halfHeight && !FarPlane(l))
                return true;
        }
        return false;
    }

    /// <summary>Is link l's plane the far side of a portal the camera looks through (its linked plane)? A sprite
    /// reaching through it stands in the room that portal shows: its copy draws it, and itself is clipped as the
    /// level there (E1M1's napalm ball over its rooftops' linked floor was drawn there and as its copy, side by side).</summary>
    bool FarPlane(Sim.Collision.Link l)
    {
        foreach (var p in Portals)
            if (p.Dir == -l.Dir && Mathf.Abs(p.LinkedPlane - l.Plane) < 0.5f)
                return true;
        return false;
    }

    /// <summary>A flat sprite of a portal's own Build sector lying a little past its plane (MARROW's MRW01: the
    /// translucent water sprites of 341 four units under its linked floor): Build draws a sector's sprites over its
    /// floor's link view whatever their height, so it isn't clipped; unless a portal copies it too (beyond a linked
    /// plane near its own: E3M3's sprite bridge over link 50, offset 2 units), which then draws it there.</summary>
    bool UnderOwnPortal(Sprite s, Vector3 centre)
    {
        int sec = -1;
        foreach (var l in PortalLinks())
        {
            if (l.Sector < 0 || !l.ConvertedPortal)
                continue;
            float past = (l.Plane - centre.Z) * l.Dir;
            if (past <= 0 || past > 16)
                continue;
            var b = l.Bounds;
            if (centre.X < b.Position.X || centre.X > b.End.X || centre.Y < b.Position.Y || centre.Y > b.End.Y)
                continue;
            if (sec == -1)
                sec = _world.PeekSpriteBSector(s);
            if (sec != l.Sector)
                continue;
            foreach (var p in Portals)
                if (p.Region.Grow(64).HasPoint(s.Origin) && (s.Origin.Z - p.LinkedPlane) * p.Dir <= 0.5f)
                    return false;
            return true;
        }
        return false;
    }

    // the camera's view this frame (Godot space): its side planes, normals outwards
    readonly Plane[] _view = new Plane[6];
    int _viewPlanes;
    Vector3 _viewEye;
    const float ViewNear = 512f / Coords.QuakeScale;     // always placed this near the eye (shadows cast into view)

    void Frustum(Camera3D cam)
    {
        _viewEye = cam.GlobalPosition;
        var f = cam.GetFrustum();
        _viewPlanes = Mathf.Min(f.Count, _view.Length);
        for (int i = 0; i < _viewPlanes; i++)
            _view[i] = f[i];
        ((Godot.Collections.Array)f).Dispose();      // freed at once (left to the finalizer, it contends with the main thread)
    }

    static readonly Core.Cvar CullSprites = Core.Cvars.Register("r_cullsprites", "1",
        "1: sprites in sectors the camera's walk through the walls doesn't reach aren't drawn (as Build); 0: all are");

    bool Unseen(Sprite s) => CullSprites.Bool && !SectorVisibility.All && BSectorOf(s) is int sec && sec >= 0
        && !SectorVisibility.Seen(sec);

    /// <summary>Is a sprite wholly out of the camera's view: its sphere (its tile's size at its repeat, a voxel's or a
    /// shadow's reach and a margin) behind any of the view's planes, and it isn't near the eye (a monster just behind
    /// the camera casts its shadow into view with r_shadows)?</summary>
    const int FlamePic = 2101;

    /// <summary>A sprite's picture's larger side at its repeat (Quake units; 64 without a picture).</summary>
    static float SpriteSize(Sprite s)
    {
        int pic = s.Picnum;
        return pic > 0 && Tiles.Valid(pic) ? Mathf.Max(Tiles.W(pic) * s.XRepeat, Tiles.H(pic) * s.YRepeat) / 64f : 64f;
    }

    bool OutOfView(Sprite s)
    {
        if (_viewPlanes == 0)
            return false;
        float r = (SpriteSize(s) + 96f) / Coords.QuakeScale;
        var c = Coords.QuakeToGodot(s.Origin);
        if ((c - _viewEye).LengthSquared() < ViewNear * ViewNear)
            return false;
        for (int i = 0; i < _viewPlanes; i++)
            if (_view[i].DistanceTo(c) > r)
                return true;
        return false;
    }

    // the level's portal links (most links aren't: the two checks above ran over all of them for every sprite drawn)
    readonly List<Sim.Collision.Link> _portalLinks = new();
    List<Sim.Collision.Link> _portalLinksOf;
    int _portalLinksCount = -1;

    /// <summary>Can a sprite's copy (at, this room's side of the portal; Quake) be seen from the eye through a portal's
    /// face, within reach? A box round it (box each way) projected from the eye onto the plane must overlap the face's
    /// bounds.</summary>
    static bool SeenThrough(PortalRenderer.ActivePortal p, Vector3 eye, Vector3 at, float box, float reach)
    {
        if (reach < float.MaxValue && new Vector2(at.X - eye.X, at.Y - eye.Y).LengthSquared() > reach * reach)
            return false;
        float plane = p.Dir > 0 ? p.Face.End.Z : p.Face.Position.Z;
        float lo = at.Z - box, hi = at.Z + box;
        // its part beyond the plane (below a floor portal, above a ceiling portal)
        if (p.Dir > 0)
            hi = Mathf.Min(hi, plane);
        else
            lo = Mathf.Max(lo, plane);
        if (lo > hi)
            return false;
        float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
        for (int k = 0; k < 8; k++)
        {
            var c = new Vector3(at.X + ((k & 1) != 0 ? box : -box), at.Y + ((k & 2) != 0 ? box : -box), (k & 4) != 0 ? hi : lo);
            float den = c.Z - eye.Z;
            if (Mathf.Abs(den) < 1e-3f)
                return true;
            float t = (plane - eye.Z) / den;
            if (t < 0)
                return true;        // a corner level with or behind the eye: keep it
            float x = eye.X + (c.X - eye.X) * t, y = eye.Y + (c.Y - eye.Y) * t;
            x0 = Mathf.Min(x0, x); x1 = Mathf.Max(x1, x);
            y0 = Mathf.Min(y0, y); y1 = Mathf.Max(y1, y);
        }
        if (!(x1 >= p.Face.Position.X && x0 <= p.Face.End.X && y1 >= p.Face.Position.Y && y0 <= p.Face.End.Y))
            return false;
        // the face's own pieces (a river's bounds hold half the town)
        if (p.FaceRects == null)
            return true;
        var seen = new Rect2(x0, y0, x1 - x0, y1 - y0);
        foreach (var r in p.FaceRects)
            if (r.Intersects(seen, true))
                return true;
        return false;
    }

    const float WaterReach = 1024, WaterBox = 64;

    /// <summary>The links drawn as portals changed (Enhanced.WaterPortals): looked for again.</summary>
    public void PortalsChanged()
    {
        _portalLinksCount = -1;
        _farRooms.Clear();
    }

    // the sectors of a link's far room: its far sector and those reached from it through two-sided walls, not into
    // another link's far room
    readonly Dictionary<int, HashSet<int>> _farRooms = new();
    SectorMap _farRoomsMap;

    HashSet<int> FarRoom(int far)
    {
        var map = _world.Map;
        if (_farRoomsMap != map)
        {
            _farRooms.Clear();
            _farRoomsMap = map;
        }
        if (_farRooms.TryGetValue(far, out var room))
            return room;
        room = new HashSet<int> { far };
        _farRooms[far] = room;
        if (map == null)
            return room;
        var others = new HashSet<int>();
        foreach (var l in _world.Collision.Links)
            if (l.FarSector >= 0 && l.FarSector != far)
                others.Add(l.FarSector);
        var todo = new Queue<int>();
        todo.Enqueue(far);
        while (todo.Count > 0 && room.Count < 4096)
            foreach (int n in map.Neighbours[todo.Dequeue()])
                if (!others.Contains(n) && room.Add(n))
                    todo.Enqueue(n);
        return room;
    }

    List<Sim.Collision.Link> PortalLinks()
    {
        var all = _world.Collision.Links;
        if (_portalLinksOf != all || _portalLinksCount != all.Count)
        {
            _portalLinks.Clear();
            foreach (var l in all)
                if (l.Portal)
                    _portalLinks.Add(l);
            (_portalLinksOf, _portalLinksCount) = (all, all.Count);
        }
        return _portalLinks;
    }

    static Vector3 G(Vector3 q) => new(q.Y, q.Z, q.X);          // Quake direction to Godot

    /// <summary>The drawn sprites, voxel models and portal copies whose box covers a screen pixel (debugging).</summary>
    public IEnumerable<string> At(Camera3D cam, Vector2 px)
    {
        foreach (var ((s, copy), d) in _drawn)
            foreach (var (kind, n) in new[] { ("sprite", d.Quad), ("voxel", d.Voxel), ("flame", d.Flame) })
            {
                if (n == null || !n.IsVisibleInTree())
                    continue;
                string what = copy == 0 ? kind : $"{kind} (copy for portal {copy - 1})";
                var box = n.GlobalTransform * n.GetAabb();
                Vector2 lo = new(float.MaxValue, float.MaxValue), hi = new(float.MinValue, float.MinValue);
                bool behind = false;
                for (int i = 0; i < 8; i++)
                {
                    var c = box.GetEndpoint(i);
                    if (cam.IsPositionBehind(c))
                    {
                        behind = true;
                        break;
                    }
                    var p = cam.UnprojectPosition(c);
                    lo = lo.Min(p);
                    hi = hi.Max(p);
                }
                // a box reaching behind the camera (a big voxel model close by) may cover any pixel: listed too
                if (behind && box.GetCenter().DistanceTo(cam.GlobalPosition) < 8 || !behind && px.X >= lo.X && px.X <= hi.X && px.Y >= lo.Y && px.Y <= hi.Y)
                {
                    var st = SentTo(n);
                    yield return $"{what} {s.ClassName}#{s.Edict} type {s.Type} pic {s.Picnum} org {s.Origin} cstat {s.Cstat} sector {StackedRooms.SpriteSectorOf?.Invoke(s)}" +
                        $" (shade {st.Shade} alpha {st.Alpha} kind {st.Kind} at {n.GlobalPosition})";
                }
            }
    }

    /// <summary>Blood's view-time animation selectors (viewProcessSprites; the DarkPlaces server's spriteAnimSel): a
    /// switch shows its next tile while on (a light fixture broken, a lever pulled), a combo switch the tile of its
    /// value.</summary>
    public static int AnimSel(Sprite s)
    {
        // (a modern map's conditions show their state as switches do)
        if (s.Type == K.kSwitchToggle || s.Type == K.kSwitchOneWay || s.Type == Sim.KM.kModernCondition || s.Type == Sim.KM.kModernConditionFalse)
            return s.XState != 0 ? 1 : 0;
        if (s.Type == K.kSwitchCombo)
            return Mathf.Clamp(s.Data1, 0, 15);
        return s.Anim;
    }

    void HideVoxel(Drawn d) => Hide(d.Voxel);

    /// <summary>
    /// Blood3D's drawVoxel (Build's drawvox placement): voxel x runs along the sprite's facing, y to its left, z up; a
    /// voxel is xrepeat / 64 units across (a wall sprite's 1.25 times that) and yrepeat / 64 up. Blood's voxels stand
    /// on their pivot unless the sprite is y-centred, unflipped; Refreshed Supply's are centred on their tile and flip
    /// with the sprite. Spinning voxels turn at the clock times 8. False: the sprite is drawn instead.
    /// </summary>
    bool DrawVoxel(Sprite s, Drawn d, int pic, Vector3 org, int bang, int cstat, float shade, bool translucent, float alpha,
        double clock, float copy)
    {
        if (!VoxelsOn.Bool || s.NoVoxel != 0 || !Voxels.TryGet(pic, out var info))
        {
            HideVoxel(d);
            return false;
        }
        bool extra = (info.Flags & Voxels.Extra) != 0;
        if (extra && (cstat & Sim.Cstat.Floor) != 0)
        {
            HideVoxel(d);
            return false;      // floor sprites stay flat
        }
        var mesh = Voxels.Mesh(info.Model);
        var pal = Voxels.Palette(s.Pal);
        if (mesh == null || pal == null)
        {
            HideVoxel(d);
            return false;
        }
        float xs = s.XRepeat / 64f, zs = s.YRepeat / 64f, xflip = 1, yflip = 1;
        if (extra)
        {
            if ((cstat & Sim.Cstat.XFlip) != 0) xflip = -1;
            if ((cstat & Sim.Cstat.YFlip) != 0) yflip = -1;
            if ((cstat & Sim.Cstat.AlignMask) == Sim.Cstat.Wall)
                xs *= 1.25f;
            if ((info.Flags & Voxels.RandomAngle) != 0)
            {
                // a fixed angle per sprite from its entity slot
                double a0 = System.Math.Sin(s.Edict * 12.9898) * 43758.5453;
                bang = (int)System.Math.Floor((a0 - System.Math.Floor(a0)) * 2048);
            }
            if ((info.Flags & Voxels.Rotate) != 0)
                bang = BuildMath.AngMask(bang + (int)System.Math.Floor(clock) * 8);
        }
        else if (Tiles.View(pic) == 7)
            bang = BuildMath.AngMask((int)System.Math.Floor(clock) * 8);
        // a level's fix for this sprite's model (Refreshed Supply's levelHacks: a turn and a world offset)
        bang = BuildMath.AngMask(bang + s.VoxAngOff);
        org += new Vector3(s.VoxXOff / 16f, -s.VoxYOff / 16f, -s.VoxZOff / 256f);
        float a = bang * Mathf.Pi / 1024;
        var fwd = new Vector3(Mathf.Cos(a), -Mathf.Sin(a), 0);
        var left = new Vector3(-fwd.Y, fwd.X, 0);
        org -= left * (s.XOffset * xflip * xs * 0.8f);
        org.Z += (Tiles.YOfs(pic) + s.YOffset) * zs;
        if ((cstat & Sim.Cstat.YCenter) != 0 && !extra)
            org.Z -= info.ZPivot * zs;
        else if ((cstat & Sim.Cstat.YCenter) == 0 && extra)
            org.Z += info.ZSize / 2f * zs;      // (not centred: standing on its z, as the picture would)
        if (yflip < 0)
            org.Z += (2 * info.ZPivot - info.ZSize) * zs;    // upside down within the same height
        var node = d.Voxel;
        if (node == null)
        {
            // with r_shadows (a copy casts none: its light copies come from the linked room)
            d.Voxel = node = new MeshInstance3D { CastShadow = GeometryInstance3D.ShadowCastingSetting.On, IgnoreOcclusionCulling = d.Copy };
            AddChild(node);
        }
        // only what changed: a shadow caster that changes makes Godot redraw the shadow maps of the lights around it,
        // which a still scene otherwise keeps from frame to frame (r_shadows)
        var sent = SentTo(node);
        if (sent.Mesh != mesh)
        {
            node.Mesh = mesh;
            sent.Mesh = mesh;
        }
        // a light fixture's model (the lamp post's lantern) doesn't shadow its own light, which sits inside it
        // (an object casts for a light when its layers meet the light's caster mask: only that layer, which all cameras see)
        uint layers = DynamicLights.IsFixtureSprite(s) ? DynamicLights.FixtureLayer : RoomLayerOf(s);
        if (sent.Layers != layers)
        {
            node.Layers = layers;
            sent.Layers = layers;
        }
        // spinning models (pickups) cast none: they would redraw every shadow map near them each frame
        bool spins = extra ? (info.Flags & Voxels.Rotate) != 0 : Tiles.View(pic) == 7;
        var cast = spins || copy > 0 ? GeometryInstance3D.ShadowCastingSetting.Off : GeometryInstance3D.ShadowCastingSetting.On;
        if (sent.Cast != (int)cast)
        {
            node.CastShadow = cast;
            sent.Cast = (int)cast;
        }
        // model x along the facing, y to the left, z up (Godot axes of the mesh: x = model y, y = z, z = x)
        var xf = new Transform3D(new Basis(G(left) * (xs * xflip), G(new Vector3(0, 0, 1)) * (zs * yflip), G(fwd) * xs), Coords.QuakeToGodot(org));
        SetTransform(node, xf);
        var vmats = _under ? _voxelUnderMaterials : _voxelMaterials;
        if (!vmats.TryGetValue((pal, translucent, (int)copy), out var m))
        {
            m = new ShaderMaterial { Shader = _under ? (translucent ? _voxelTranslucentUnder : _voxelOpaqueUnder) : translucent ? _voxelTranslucent : _voxelOpaque };
            m.SetShaderParameter(TexName, pal);
            m.SetShaderParameter(CopyName, copy);
            if (translucent)
                m.RenderPriority = 1;
            vmats[(pal, translucent, (int)copy)] = m;
        }
        SetMaterial(node, m);
        SetParams(node, shade, alpha);
        SetVisible(node, true);
        return true;
    }

    /// <summary>A sprite's nodes placed for the camera at camQ (Quake); a copy (copy &gt; 0, PortalRenderer's mark) is the
    /// sprite as seen from the eye moved into the linked room (camQ), drawn moved back by shift; Build draws that room
    /// from farSector (the sector beyond the link).</summary>
    void DrawSprite(Sprite s, Drawn d, Vector3 camQ, Vector3 camRight, float lerp, double clock, Vector3 shift, int copy,
        int farSector)
    {
        Hide(d.Flame);
        int pic = s.Picnum;
        if (!s.Visible || s.OutsideHome || pic <= 0 || !Tiles.Valid(pic) || s.XRepeat <= 0 || s.YRepeat <= 0)
        {
            Hide(d.Quad);
            HideVoxel(d);
            Hide(d.Shadow);
            return;
        }
        var node = Quad(d);
        // a lit torch (viewProcessSprites): its next tile, with a flame on top
        bool torch = s.StatNum == K.kStatDecoration && s.Type == K.kDecorationTorch && (!s.HasX || s.NoX != 0 || s.XState == 1);
        if (torch && Tiles.Valid(pic + 1))
            pic++;
        var org = _world.Collision.Interpolate(s.PrevOrigin, s.Origin, lerp);
        if (copy == 0 ? StackedRooms.SpriteHidden(s, org) : farSector >= 0 && StackedRooms.SpriteHidden(s, org, farSector, _mirroring ? MirrorEntry(camQ, org) : camQ, _mirroring))
        {
            SetVisible(node, false);
            HideVoxel(d);
            Hide(d.Shadow);
            return;
        }
        // its room's render layer: where rooms share a place, a dynamic light of another room keeps off it
        // (DynamicLights.RoomLayer, as the world's meshes)
        uint roomLayer = RoomLayerOf(s);
        var sentQ = SentTo(node);
        if (sentQ.Layers != roomLayer)
        {
            node.Layers = roomLayer;
            sentQ.Layers = roomLayer;
        }
        int dAng = ((s.Ang - s.PrevAng + 1024) & 2047) - 1024;
        int bang = BuildMath.AngMask(s.PrevAng + (int)Mathf.Round(dAng * lerp));
        int cstat = s.Cstat;
        // a sequence frame's flips (kHitagFlipX/Y) add to the sprite's
        if ((s.HitTag & K.kHitagFlipX) != 0) cstat |= Sim.Cstat.XFlip;
        if ((s.HitTag & K.kHitagFlipY) != 0) cstat |= Sim.Cstat.YFlip;

        // rotation frames / animation selectors (viewProcessSprites)
        int vt = Tiles.View(pic), nAnim = 0;
        if (vt == 0)
            nAnim = AnimSel(s);
        else if (vt == 1 || vt == 2)
        {
            var rv = BuildMath.RotateVector(camQ.X * 16 - org.X * 16, -camQ.Y * 16 + org.Y * 16, 128 - bang);
            nAnim = BuildMath.GetOctant(rv.X, rv.Y);
            if (vt == 1)
            {
                if (nAnim <= 4)
                    cstat &= ~Sim.Cstat.XFlip;
                else
                {
                    nAnim = 8 - nAnim;
                    cstat |= Sim.Cstat.XFlip;
                }
            }
        }
        for (; nAnim > 0; nAnim--)
            pic += Tiles.AnimNum(pic) + 1;
        pic += BuildMath.AnimateOffs(pic, clock);
        if (!Tiles.Valid(pic))
        {
            SetVisible(node, false);
            Hide(d.Shadow);
            return;
        }
        var tex = TileTextures.Get(pic, s.Pal);
        if (tex == null)
        {
            SetVisible(node, false);
            Hide(d.Shadow);
            return;
        }
        // its shadow: the frame drawn, mirrored only as a rotation frame is (the shadow has none of the sprite's cstat)
        DrawShadow(s, d, pic, vt == 1 && (cstat & Sim.Cstat.XFlip) != 0, org, camQ, camRight, shift, copy);

        float w = Tiles.W(pic) * s.XRepeat / 64f, h = Tiles.H(pic) * s.YRepeat / 64f;
        float xo = (Tiles.XOfs(pic) + s.XOffset) * s.XRepeat / 64f;
        bool fx = (cstat & Sim.Cstat.XFlip) != 0, fy = (cstat & Sim.Cstat.YFlip) != 0;
        if (fx)
            xo = -xo;
        int align = cstat & Sim.Cstat.AlignMask;
        // (a sloped floor sprite: a floor sprite tilted up the way it faces, its slope 4096 for 45 degrees)
        bool flat = align == Sim.Cstat.Floor || align == Sim.Cstat.Slope;
        Vector3 centre, xAxis, yAxis;
        float faceXo = 0;      // a face sprite's x offset along the camera's right (the shader turns it)
        if (flat)
        {
            float a = BuildMath.AngToRad(bang);
            var fwd = new Vector3(Mathf.Cos(a), -Mathf.Sin(a), 0);
            var right = new Vector3(fwd.Y, -fwd.X, 0);
            float tan = align == Sim.Cstat.Slope ? s.Slope / 4096f : 0f;
            // one-sided: seen only from above, or from below when y-flipped
            if ((cstat & Sim.Cstat.OneSided) != 0 && (camQ.Z < org.Z + tan * (camQ - org).Dot(fwd)) == !fy)
            {
                SetVisible(node, false);
                return;
            }
            if (tan != 0)
                fwd = (fwd + new Vector3(0, 0, tan)) / Mathf.Sqrt(1 + tan * tan);
            var c = org;      // the shader lifts it off the floor (depth precision)
            c += right * xo - fwd * ((Tiles.YOfs(pic) + s.YOffset) * s.YRepeat / 64f);
            centre = c;
            xAxis = G(right) * w;
            yAxis = G(fwd) * h;
        }
        else
        {
            Vector3 rightG;
            if (align == Sim.Cstat.Wall)
            {
                float a = BuildMath.AngToRad(bang - 512);
                var right = new Vector3(Mathf.Cos(a), -Mathf.Sin(a), 0);
                xo = -xo;
                var fwd = new Vector3(right.Y, -right.X, 0);
                float side = (camQ - org).Dot(fwd);
                if ((cstat & Sim.Cstat.OneSided) != 0 && side <= 0)
                {
                    SetVisible(node, false);
                    return;
                }
                rightG = G(right);      // the shader lifts it off the wall towards the camera
            }
            else
            {
                // face sprite (Polymost): the x offset moves it left on screen, an odd-width tile sits half a texel right
                rightG = camRight;
                xo = -xo;
                if ((Tiles.W(pic) & 1) != 0)
                    xo += 0.5f * s.XRepeat / 64f;
            }
            // (the picture's centre on its z, cstat 128, as every sprite of Blood's own maps has it; else its bottom:
            // a mod's map made with xmapedit can store either. Eviction's fan in its vent box, bug 20261115-161220)
            int rise = (cstat & Sim.Cstat.YCenter) != 0 ? Tiles.H(pic) / 2 : Tiles.H(pic);
            float topz = org.Z + (rise + Tiles.YOfs(pic) + s.YOffset) * s.YRepeat / 64f;
            if (torch)
                DrawTorchFlame(s, d, pic, new Vector3(org.X, org.Y, topz) - shift, clock, copy);
            else if (s.StatNum == K.kStatDecoration && s.Type == K.kDecorationCandle && (!s.HasX || s.NoX != 0 || s.XState == 1))
                DrawCandleHalo(d, new Vector3(org.X, org.Y, topz) - shift, cstat, copy);
            else if (s.StatNum == K.kStatProjectile && s.Type == K.kMissileTeslaRegular)
                DrawTeslaHalo(s, d, org - shift, org - camQ, copy);
            else if (s.DudeFlag4 != 0 && s.StatNum == K.kStatDude && s.Data3 > 0 && s.Data3 <= Sim.World.PatrolSpotMax
                && s.AiState is { Type: >= Sim.World.kAiStatePatrolBase and < Sim.World.kAiStatePatrolMax })
                DrawSpotProgress(s, d, new Vector3(org.X, org.Y, topz) - shift, copy);
            centre = org;
            if (align == Sim.Cstat.Wall)
            {
                var rightQ = new Vector3(rightG.Z, rightG.X, 0);
                centre += rightQ * xo;
                xAxis = rightG * w;
            }
            else
            {
                // a face sprite: turned to the camera by the shader, its node fixed (x along Godot x, a box w deep so
                // the culling box holds it whichever way it turns)
                faceXo = xo / Coords.QuakeScale;
                xAxis = Vector3.Right * w;
            }
            centre.Z = topz - h / 2f;
            yAxis = Vector3.Up * h;
        }
        bool face = !flat && align != Sim.Cstat.Wall;
        var zAxis = face ? Vector3.Back * (w / Coords.QuakeScale) : xAxis.Cross(yAxis).Normalized() * 0.01f;
        var nodeXf = new Transform3D(new Basis(xAxis / Coords.QuakeScale, yAxis / Coords.QuakeScale, zAxis), Coords.QuakeToGodot(centre - shift));
        // reaching through a portal plane (in its own room): not clipped, its two parts meet
        // a flat one only when lying on the plane: 8 units past it (E3M3's sprite bridge over link 50) it and its copy, moved
        // by the link's 2 unit offset, were both drawn unclipped and fought
        int own = copy == 0 ? BSectorOf(s) : -1;
        // (a mirror's copy always clipped to the mirror: reflected, it stands behind the wall, nowhere near its own links)
        float clip = !_mirroring && (flat ? ReachesThrough(centre, 1, 1, own) || copy == 0 && UnderOwnPortal(s, centre)
            : ReachesThrough(centre, h / 2, 24, own)) ? -1 : copy;

        int shade = s.Shade + Tiles.Shade(pic);
        // HDR: a flame is a light whatever its sector's shade (MARROW's fires, placed as decorations, dull beside a napalm
        // ball; Blood itself draws them at the shade)
        if (HdrOutput.Highlighting && pic >= FlamePic && pic <= FlamePic + Tiles.AnimNum(FlamePic))
            shade = -128;
        // its Build sector's shade now (light effects, switched lights), else the floor's under it
        float total = shade + (SectorShade(s) ?? (s.SecShade == int.MinValue ? 16 : s.SecShade) + FxShade(s));
        if (shade <= -64)
            total = shade;      // fullbright sprites (shade -128) ignore their sector
        // a candle (viewProcessSprites): fullbright while lit, shade -8 when out
        if (s.StatNum == K.kStatDecoration && s.Type == K.kDecorationCandle)
            total = !s.HasX || s.NoX != 0 || s.XState == 1 ? -128 : -8;
        if (InFog(s))
            total += 1000;      // fogged to white (its shaders take the shade 1000 over as the mark)
        _under = Under(s, copy);
        // (the sky faces drawing nothing, what the walk reaches only through a link has only its copy: nothing of it stays behind
        // where it was last placed, a thrown bundle hung where it went through E1M1's sky link)
        if (_under && !BuildSky.Faces.Bool)
        {
            SetVisible(node, false);
            HideVoxel(d);
            Hide(d.Flame);
            Hide(d.Shadow);
            return;
        }
        bool translucent = (cstat & Sim.Cstat.Trans) != 0;
        float alpha = translucent ? ((cstat & Sim.Cstat.Trans2) != 0 ? 2f / 3f : 1f / 3f) : 1f;
        _glows = DynamicLights.IsFixtureTile(pic) ? 2f
            : pic >= FlamePic && pic <= FlamePic + Tiles.AnimNum(FlamePic) || s.ClassName == "decoration_torch" && (!s.HasX || s.NoX != 0 || s.XState != 0) ? 1f : 0f;
        // a voxel tile (view 6/7) by the sprite's own tile; Refreshed Supply's voxels by the tile it shows now
        bool voxel = DrawVoxel(s, d, vt == 6 || vt == 7 ? s.Picnum : pic, org - shift, bang, cstat, total, translucent, alpha, clock, clip);
        if (voxel)
        {
            _glows = 0f;
            SetVisible(node, false);
            return;
        }
        SetTransform(node, nodeXf);
        SetMaterial(node, Material(tex, translucent, clip));
        SetParams(node, total, alpha, face ? FaceFlip(fx, fy) : new Vector2(fx ? 1 : 0, fy ? 1 : 0));
        _glows = 0f;
        SetKind(node, face ? 0 : 1, faceXo);
        SetZClip(node, copy == 0 ? ZClipOf(own, org) : NoZClip);
        SetVisible(node, true);
    }

    /// <summary>
    /// kViewEffectTorchHigh: a lit torch's flame, Blood's tile 2101 (animated, fullbright) centred on the torch's top
    /// and scaled by the torch tile's width (xrepeat = width * the torch's xrepeat / 32).
    /// </summary>
    void DrawTorchFlame(Sprite s, Drawn d, int torchPic, Vector3 top, double clock, int copy)
    {
        const int FlamePic = 2101;
        int pic = FlamePic + BuildMath.AnimateOffs(FlamePic, clock);
        var tex = Tiles.Valid(pic) ? TileTextures.Get(pic, 0) : null;
        if (tex == null)
            return;
        var node = d.Flame;
        if (node == null)
        {
            d.Flame = node = new MeshInstance3D { Mesh = _quad, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, IgnoreOcclusionCulling = d.Copy };
            AddChild(node);
        }
        float scale = Tiles.W(torchPic) * s.XRepeat / 32f / 64f;
        float w = Tiles.W(pic) * scale, h = Tiles.H(pic) * scale;
        float xo = -Tiles.XOfs(pic) * scale;
        if ((Tiles.W(pic) & 1) != 0)
            xo += 0.5f * scale;
        var centre = top + new Vector3(0, 0, Tiles.YOfs(pic) * scale);
        SetTransform(node, new Transform3D(new Basis(Vector3.Right * (w / Coords.QuakeScale), Vector3.Up * (h / Coords.QuakeScale),
            Vector3.Back * (w / Coords.QuakeScale)), Coords.QuakeToGodot(centre)));
        SetMaterial(node, Material(tex, false, copy));
        SetParams(node, -128f, 1f, FaceFlip(false));
        SetKind(node, 0, xo / Coords.QuakeScale);
        SetVisible(node, true);
    }

    /// <summary>
    /// kViewEffectTesla: a tesla bolt's halo, Blood's tile 2135 at the bolt's repeat, centred on it (cstat 128),
    /// translucent (2), shade -128, put 2 units further from the camera (viewInsertTSprite's Polymost nudge along the
    /// view) so the bolt shows in front of it. Drawn in the bolt's flame node.
    /// </summary>
    void DrawTeslaHalo(Sprite s, Drawn d, Vector3 org, Vector3 fromCam, int copy)
    {
        const int HaloPic = 2135;
        var tex = Tiles.Valid(HaloPic) ? TileTextures.Get(HaloPic, 0) : null;
        if (tex == null)
            return;
        var node = d.Flame;
        if (node == null)
        {
            d.Flame = node = new MeshInstance3D { Mesh = _quad, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, IgnoreOcclusionCulling = d.Copy };
            AddChild(node);
        }
        float sx = s.XRepeat / 64f, sy = s.YRepeat / 64f;
        float w = Tiles.W(HaloPic) * sx, h = Tiles.H(HaloPic) * sy;
        float xo = -Tiles.XOfs(HaloPic) * sx;
        if ((Tiles.W(HaloPic) & 1) != 0)
            xo += 0.5f * sx;
        var away = new Vector3(fromCam.X, fromCam.Y, 0);
        away = away.LengthSquared() > 1e-6f ? away.Normalized() * 2 : Vector3.Zero;
        var centre = org + away + new Vector3(0, 0, Tiles.YOfs(HaloPic) * sy);
        SetTransform(node, new Transform3D(new Basis(Vector3.Right * (w / Coords.QuakeScale), Vector3.Up * (h / Coords.QuakeScale),
            Vector3.Back * (w / Coords.QuakeScale)), Coords.QuakeToGodot(centre)));
        SetMaterial(node, Material(tex, true, copy));
        SetParams(node, -128f, 1f / 3f, FaceFlip(false));
        node.SetInstanceShaderParameter(UnlitName, 1f);
        SetKind(node, 0, xo / Coords.QuakeScale);
        SetVisible(node, true);
    }

    /// <summary>
    /// kViewEffectPhase: a lit candle's halo, Blood's tile 626 at repeat 24, translucent with the candle's cstat, shade
    /// 26 (added after viewProcessSprites shades the sprites: no sector shade), standing on the candle's top (centred
    /// there for a centred candle). Drawn in the candle's flame node (a candle has no torch flame).
    /// </summary>
    void DrawCandleHalo(Drawn d, Vector3 top, int cstat, int copy)
    {
        const int HaloPic = 626;
        var tex = Tiles.Valid(HaloPic) ? TileTextures.Get(HaloPic, 0) : null;
        if (tex == null)
            return;
        var node = d.Flame;
        if (node == null)
        {
            d.Flame = node = new MeshInstance3D { Mesh = _quad, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, IgnoreOcclusionCulling = d.Copy };
            AddChild(node);
        }
        const float scale = 24 / 64f;
        float w = Tiles.W(HaloPic) * scale, h = Tiles.H(HaloPic) * scale;
        float xo = -Tiles.XOfs(HaloPic) * scale;
        if ((Tiles.W(HaloPic) & 1) != 0)
            xo += 0.5f * scale;
        var centre = top + new Vector3(0, 0, Tiles.YOfs(HaloPic) * scale + ((cstat & Sim.Cstat.YCenter) != 0 ? 0 : h / 2));
        SetTransform(node, new Transform3D(new Basis(Vector3.Right * (w / Coords.QuakeScale), Vector3.Up * (h / Coords.QuakeScale),
            Vector3.Back * (w / Coords.QuakeScale)), Coords.QuakeToGodot(centre)));
        SetMaterial(node, Material(tex, true, copy));
        SetParams(node, 26f, (cstat & Sim.Cstat.Trans2) != 0 ? 2f / 3f : 1f / 3f, FaceFlip(false));
        node.SetInstanceShaderParameter(UnlitName, 1f);
        SetKind(node, 0, xo / Coords.QuakeScale);
        SetVisible(node, true);
    }

    /// <summary>
    /// kViewEffectSpotProgress (a modern map's patrolling dude noticing a player, in stealth mode): a bar over its head,
    /// Blood's tile 2203 as wide as it has spotted the player (its data3 of 512), fullbright, blue, yellow from half,
    /// plain from three quarters (palettes 10, 6, 0).
    /// </summary>
    void DrawSpotProgress(Sprite s, Drawn d, Vector3 top, int copy)
    {
        const int BarPic = 2203;
        int perc = 100 * s.Data3 / Sim.World.PatrolSpotMax, xrep = 94 * s.Data3 / Sim.World.PatrolSpotMax;
        var tex = Tiles.Valid(BarPic) && xrep > 0 ? TileTextures.Get(BarPic, perc >= 75 ? 0 : perc >= 50 ? 6 : 10) : null;
        if (tex == null)
            return;
        var node = d.Flame;
        if (node == null)
        {
            d.Flame = node = new MeshInstance3D { Mesh = _quad, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, IgnoreOcclusionCulling = d.Copy };
            AddChild(node);
        }
        float w = Tiles.W(BarPic) * xrep / 64f, h = Tiles.H(BarPic) * 20 / 64f;
        // (2048 Build z over its top: 8 units)
        var centre = top + new Vector3(0, 0, 8 + h / 2);
        SetTransform(node, new Transform3D(new Basis(Vector3.Right * (w / Coords.QuakeScale), Vector3.Up * (h / Coords.QuakeScale),
            Vector3.Back * (w / Coords.QuakeScale)), Coords.QuakeToGodot(centre)));
        SetMaterial(node, Material(tex, false, copy));
        SetParams(node, -128f, 1f, FaceFlip(false));
        node.SetInstanceShaderParameter(UnlitName, 1f);
        SetKind(node, 0, 0);
        SetVisible(node, true);
    }
}
