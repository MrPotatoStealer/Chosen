using System;
using Chosen.Core;
using Chosen.Sim;
using Godot;
using Godot.Collections;

namespace Chosen.Render;

/// <summary>
/// Screen-space ambient occlusion as a compositor pass (r_ao: 1 SSAO, 2 GTAO). Godot's own SSAO only darkens ambient
/// light, and the level's Build shading is emission, so this one runs before the opaque pass (after the depth prepass,
/// which writes depth, normals and roughness) and leaves its result in ao_texture, which the opaque Build shaders
/// multiply their emission by (ShaderApply): the dynamic lights and the fog over them are left alone. One compute pass
/// finds each pixel's occlusion from the depth buffer (positions from the scene's inverse projection, normals from
/// neighbouring depths), a second blurs it across pixels of about the same depth into ao_texture.
/// SSAO: a hemisphere of samples about the normal, each occluded when the surface in front of it is nearer.
/// GTAO (Jimenez et al. 2016, after XeGTAO): per slice direction the highest horizon on either side, the visible arc
/// between them integrated with the cosine weight.
/// r_ao_half (the default): the occlusion found for every second pixel each way (a quarter of the work: at 3440x1440
/// the pass took 2 of the frame's 7 ms, report 20261115-161600), the second pass spreading it over the full picture by
/// depth as it blurs (each pixel from the nine found around it, those at about its own depth).
/// </summary>
public partial class AmbientOcclusion : CompositorEffect
{
    /// <summary>What the opaque Build shaders (the level, sprites, voxels) declare: the occlusion of the pixel they draw
    /// (ao_texture: this pass's, made before the opaque pass from the depth prepass).</summary>
    public const string ShaderFunction = @"
global uniform sampler2D ao_texture : filter_nearest;
global uniform float ao_enabled;
global uniform float ao_debug;
float ao_factor(vec2 suv) {
	return ao_enabled > 0.5 ? texture(ao_texture, suv).r : 1.0;
}
";

    /// <summary>What they do with it, after setting EMISSION (Build's shading, the ambient): only that is darkened, not
    /// the dynamic lights (light()) nor the fog Godot puts over it (with the enhanced fog, corners darkened over its glow
    /// looked bad); r_ao_debug shows it alone.</summary>
    public const string ShaderApply = @"
	float ao_v = ao_factor(SCREEN_UV);
	if (ao_debug > 0.5) { ALBEDO = vec3(0.0); EMISSION = vec3(ao_v); } else EMISSION *= ao_v;
";

    public static readonly Cvar Mode = Cvars.Register("r_ao", "2", "ambient occlusion: 0 off, 1 SSAO, 2 GTAO", saved: true);
    public static readonly Cvar Radius = Cvars.Register("r_ao_radius", "24", "ambient occlusion reach (Quake units)", saved: true);
    public static readonly Cvar Intensity = Cvars.Register("r_ao_intensity", "1.5", "ambient occlusion strength (0..2)", saved: true);
    static readonly Cvar Debug = Cvars.Register("r_ao_debug", "0", "1: show the ambient occlusion term alone");
    public static readonly Cvar Half = Cvars.Register("r_ao_half", "1",
        "1: ambient occlusion found at half the resolution and spread by depth (much cheaper, a little softer); 0: for every pixel", saved: true);

    RenderingDevice _rd;
    Rid _aoShader, _aoPipeline, _applyShader, _applyPipeline, _sampler, _aoTex, _aoOut;
    Vector2I _aoSize;
    int _aoScale;
    // what the render thread reads (set from the main thread each frame)
    int _mode, _frame, _scale = 1;
    float _radius, _intensity;
    bool _debug;
    // ao_texture as the shaders see it (main thread), pointed at _aoOut once the render thread made it; the textures a
    // resize replaced, freed a few frames on (the shaders may still use the old one that long)
    readonly Texture2Drd _shaderTex = new();
    bool _ready;
    readonly System.Collections.Generic.List<(Rid rid, int frame)> _toFree = new();
    int _renderFrame;

    public AmbientOcclusion()
    {
        EffectCallbackType = EffectCallbackTypeEnum.PreOpaque;
        AccessResolvedDepth = true;
        // the sky faces are level geometry (Build's parallax ceilings and floors): their roughness 0 marks them (the
        // depth prepass writes normals and roughness when this is set)
        NeedsNormalRoughness = true;
        RenderingServer.CallOnRenderThread(Callable.From(Init));
        // never without a texture (one unset was invalid to every shader declaring it): white until the pass's own
        RenderingServer.GlobalShaderParameterSet("ao_texture", White);
    }

    static ImageTexture _white;

    static ImageTexture White
    {
        get
        {
            if (_white == null)
            {
                var img = Image.CreateEmpty(1, 1, false, Image.Format.R8);
                img.Fill(Colors.White);
                _white = ImageTexture.CreateFromImage(img);
            }
            return _white;
        }
    }

    /// <summary>The settings for the next frames (main thread).</summary>
    public void Update()
    {
        _mode = (int)Mode.Float;
        Enabled = _mode == 1 || _mode == 2;
        NeedsNormalRoughness = Enabled;      // the sky mark; no extra buffer while it is off
        _radius = Mathf.Max(1f, Radius.Float) / Coords.QuakeScale;
        _intensity = Mathf.Clamp(Intensity.Float, 0f, 2f);
        _debug = Debug.Bool;
        _scale = Half.Bool ? 2 : 1;
        _frame++;
        RenderingServer.GlobalShaderParameterSet("ao_enabled", Enabled && _ready ? 1f : 0f);
        RenderingServer.GlobalShaderParameterSet("ao_debug", _debug ? 1f : 0f);
    }

    // (main thread) the shaders' ao_texture is the render thread's new one
    void Publish(Rid rid)
    {
        _shaderTex.TextureRdRid = rid;
        RenderingServer.GlobalShaderParameterSet("ao_texture", _shaderTex);
        _ready = rid.IsValid;
    }

    void Init()
    {
        _rd = RenderingServer.GetRenderingDevice();
        if (_rd == null)
            return;
        (_aoShader, _aoPipeline) = Compile(AoSource);
        (_applyShader, _applyPipeline) = Compile(ApplySource);
        _sampler = _rd.SamplerCreate(new RDSamplerState
        {
            MinFilter = RenderingDevice.SamplerFilter.Nearest,
            MagFilter = RenderingDevice.SamplerFilter.Nearest,
            RepeatU = RenderingDevice.SamplerRepeatMode.ClampToEdge,
            RepeatV = RenderingDevice.SamplerRepeatMode.ClampToEdge,
        });
    }

    (Rid shader, Rid pipeline) Compile(string code)
    {
        var spirv = _rd.ShaderCompileSpirVFromSource(new RDShaderSource { Language = RenderingDevice.ShaderLanguage.Glsl, SourceCompute = code });
        if (spirv.CompileErrorCompute != "")
        {
            GD.PrintErr("ambient occlusion shader: " + spirv.CompileErrorCompute);
            return (default, default);
        }
        var shader = _rd.ShaderCreateFromSpirV(spirv);
        return (shader, _rd.ComputePipelineCreate(shader));
    }

    public override void _Notification(int what)
    {
        if (what == NotificationPredelete)
            Release();
    }

    /// <summary>Frees the pass's shaders, pipelines, sampler and texture; it draws nothing after. The C# object outlives
    /// the scene when the game quits (the rendering device would report them leaked), so the level frees it as it
    /// leaves the tree.</summary>
    public void Release()
    {
        if (_rd == null)
            return;
        // the shaders off it first (they sampled the freed texture as the game quit: hundreds of errors)
        _ready = false;
        RenderingServer.GlobalShaderParameterSet("ao_enabled", 0f);
        RenderingServer.GlobalShaderParameterSet("ao_texture", White);
        _shaderTex.TextureRdRid = default;
        foreach (var rid in new[] { _aoTex, _aoOut, _aoPipeline, _aoShader, _applyPipeline, _applyShader, _sampler })
            if (rid.IsValid)
                _rd.FreeRid(rid);
        foreach (var (rid, _) in _toFree)
            if (rid.IsValid)
                _rd.FreeRid(rid);
        _toFree.Clear();
        _aoTex = _aoOut = _aoPipeline = _aoShader = _applyPipeline = _applyShader = _sampler = default;
        _rd = null;
    }

    public override void _RenderCallback(int effectCallbackType, RenderData renderData)
    {
        if (_rd == null || !_aoPipeline.IsValid || !_applyPipeline.IsValid || (_mode != 1 && _mode != 2))
            return;
        if (renderData.GetRenderSceneBuffers() is not RenderSceneBuffersRD buffers)
            return;
        var sceneData = renderData.GetRenderSceneData();
        var size = buffers.GetInternalSize();
        if (size.X <= 0 || size.Y <= 0)
            return;
        _renderFrame++;
        for (int i = _toFree.Count - 1; i >= 0; i--)
            if (_renderFrame - _toFree[i].frame > 4)
            {
                if (_toFree[i].rid.IsValid)
                    _rd.FreeRid(_toFree[i].rid);
                _toFree.RemoveAt(i);
            }
        int scale = _scale;
        var aoSize = new Vector2I((size.X + scale - 1) / scale, (size.Y + scale - 1) / scale);
        if (!_aoTex.IsValid || _aoSize != size || _aoScale != scale)
        {
            if (_aoTex.IsValid)
                _toFree.Add((_aoTex, _renderFrame));
            if (_aoOut.IsValid)
                _toFree.Add((_aoOut, _renderFrame));
            var format = new RDTextureFormat
            {
                Format = RenderingDevice.DataFormat.R16Sfloat,
                Width = (uint)size.X,
                Height = (uint)size.Y,
                UsageBits = RenderingDevice.TextureUsageBits.StorageBit | RenderingDevice.TextureUsageBits.SamplingBit,
            };
            _aoOut = _rd.TextureCreate(format, new RDTextureView());
            format.Width = (uint)aoSize.X;
            format.Height = (uint)aoSize.Y;
            _aoTex = _rd.TextureCreate(format, new RDTextureView());
            _aoSize = size;
            _aoScale = scale;
            var rid = _aoOut;
            Callable.From(() => Publish(rid)).CallDeferred();
        }
        var sceneBuffer = sceneData.GetUniformBuffer();
        var push = Push(size, scale);
        uint gx = (uint)((size.X + 7) / 8), gy = (uint)((size.Y + 7) / 8);
        uint ax = (uint)((aoSize.X + 7) / 8), ay = (uint)((aoSize.Y + 7) / 8);
        for (uint view = 0; view < buffers.GetViewCount() && view < _sets.Length; view++)
        {
            var depth = buffers.GetDepthLayer(view);
            var normalRough = buffers.GetTextureSlice("forward_clustered", "normal_roughness", view, 0, 1, 1);
            ref var s = ref _sets[view];
            if (s.Out != _aoOut || s.Depth != depth || s.Normal != normalRough || s.Ao != _aoTex || s.Scene != sceneBuffer
                || !_rd.UniformSetIsValid(s.AoSet) || !_rd.UniformSetIsValid(s.ApplySet))
                s = MakeSets(depth, normalRough, sceneBuffer);

            long list = _rd.ComputeListBegin();
            _rd.ComputeListBindComputePipeline(list, _aoPipeline);
            _rd.ComputeListBindUniformSet(list, s.AoSet, 0);
            _rd.ComputeListBindUniformSet(list, s.AoScene, 1);
            _rd.ComputeListSetPushConstant(list, push, (uint)push.Length);
            _rd.ComputeListDispatch(list, ax, ay, 1);
            _rd.ComputeListAddBarrier(list);
            _rd.ComputeListBindComputePipeline(list, _applyPipeline);
            _rd.ComputeListBindUniformSet(list, s.ApplySet, 0);
            _rd.ComputeListBindUniformSet(list, s.ApplyScene, 1);
            _rd.ComputeListSetPushConstant(list, push, (uint)push.Length);
            _rd.ComputeListDispatch(list, gx, gy, 1);
            _rd.ComputeListEnd();
        }
    }

    /// <summary>A view's uniform sets and the textures they were made for.</summary>
    struct Sets
    {
        public Rid Out, Depth, Normal, Ao, Scene;
        public Rid AoSet, AoScene, ApplySet, ApplyScene;
    }

    // made again only when a texture changes (a resize): uniform objects made every frame pile up for the garbage
    // collector, and thousands of them left at quit crash the game as it shuts down
    readonly Sets[] _sets = new Sets[2];

    Sets MakeSets(Rid depth, Rid normalRough, Rid sceneBuffer)
    {
        var scene = new RDUniform { UniformType = RenderingDevice.UniformType.UniformBuffer, Binding = 0 };
        scene.AddId(sceneBuffer);
        var aoRaw = new RDUniform { UniformType = RenderingDevice.UniformType.Image, Binding = 1 };
        aoRaw.AddId(_aoTex);
        var outU = new RDUniform { UniformType = RenderingDevice.UniformType.Image, Binding = 2 };
        outU.AddId(_aoOut);
        return new Sets
        {
            Out = _aoOut, Depth = depth, Normal = normalRough, Ao = _aoTex, Scene = sceneBuffer,
            AoSet = UniformSetCacheRD.GetCache(_aoShader, 0,
                new Array<RDUniform> { SampledUniform(0, depth), aoRaw, SampledUniform(2, normalRough) }),
            AoScene = UniformSetCacheRD.GetCache(_aoShader, 1, new Array<RDUniform> { scene }),
            ApplySet = UniformSetCacheRD.GetCache(_applyShader, 0,
                new Array<RDUniform> { SampledUniform(0, _aoTex), SampledUniform(1, depth), outU, SampledUniform(3, normalRough) }),
            ApplyScene = UniformSetCacheRD.GetCache(_applyShader, 1, new Array<RDUniform> { scene }),
        };
    }

    RDUniform SampledUniform(int binding, Rid texture)
    {
        var u = new RDUniform { UniformType = RenderingDevice.UniformType.SamplerWithTexture, Binding = binding };
        u.AddId(_sampler);
        u.AddId(texture);
        return u;
    }

    byte[] Push(Vector2I size, int scale)
    {
        float[] p = { size.X, size.Y, _radius, _intensity, _mode, _frame % 64, _debug ? 1 : 0, scale };
        var b = new byte[p.Length * 4];
        Buffer.BlockCopy(p, 0, b, 0, b.Length);
        return b;
    }

    // ---------------------------------------------------------------- shaders
    const string Common = @"
#version 450
layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;
// the scene's uniform buffer: its first members (Godot's SceneData: the projection drawn with and its inverse)
layout(set = 1, binding = 0, std140) uniform SceneDataBlock {
	mat4 projection;
	mat4 inv_projection;
} scene;
layout(push_constant, std430) uniform Params {
	vec2 size;
	float radius;        // view units (Godot)
	float intensity;
	float mode;          // 1 SSAO, 2 GTAO
	float frame;
	float debug;
	float scale;         // 1: the occlusion found for every pixel, 2: for every second one each way
} p;
const float PI = 3.14159265;

// a sky face: roughness 0 (BuildSky), stored scaled by 127/255, mirrored about 1 for dynamic objects; the level is 1
bool is_sky(float stored) {
	return stored < 0.1 || stored > 0.9;
}
";

    const string AoSource = Common + @"
layout(set = 0, binding = 0) uniform sampler2D depth_tex;
layout(r16f, set = 0, binding = 1) uniform restrict writeonly image2D ao_image;
layout(set = 0, binding = 2) uniform sampler2D normal_rough_tex;

vec3 view_at(vec2 uv) {
	float d = textureLod(depth_tex, uv, 0.0).r;
	vec4 v = scene.inv_projection * vec4(uv * 2.0 - 1.0, d, 1.0);
	return v.xyz / v.w;
}

vec2 uv_of(vec3 v) {
	vec4 c = scene.projection * vec4(v, 1.0);
	return c.xy / c.w * 0.5 + 0.5;
}

// interleaved gradient noise (Jimenez), moved each frame: the blur and the frames smooth it
float ign(vec2 px, float k) {
	px += k * 5.588238;
	return fract(52.9829189 * fract(dot(px, vec2(0.06711056, 0.00583715))));
}

vec3 normal_at(vec2 uv, vec3 c, vec2 texel) {
	vec3 l = view_at(uv - vec2(texel.x, 0.0)), r = view_at(uv + vec2(texel.x, 0.0));
	vec3 u = view_at(uv - vec2(0.0, texel.y)), d = view_at(uv + vec2(0.0, texel.y));
	vec3 dx = abs(r.z - c.z) < abs(c.z - l.z) ? r - c : c - l;
	vec3 dy = abs(d.z - c.z) < abs(c.z - u.z) ? d - c : c - u;
	vec3 n = normalize(cross(dy, dx));
	return dot(n, -c) < 0.0 ? -n : n;
}

float ssao(vec2 uv, vec3 c, vec3 n, vec2 px) {
	float a = ign(px, p.frame) * 2.0 * PI;
	vec3 t = normalize(abs(n.y) < 0.99 ? cross(n, vec3(0.0, 1.0, 0.0)) : cross(n, vec3(1.0, 0.0, 0.0)));
	vec3 b = cross(n, t);
	vec3 t2 = t * cos(a) + b * sin(a), b2 = cross(n, t2);
	const int N = 16;
	float occ = 0.0;
	for (int i = 0; i < N; i++) {
		// a spiral of directions over the hemisphere, nearer samples denser
		float fi = (float(i) + 0.5) / float(N);
		float phi = float(i) * 2.39996323;
		float ct = sqrt(1.0 - fi), st = sqrt(fi);
		vec3 h = t2 * (cos(phi) * st) + b2 * (sin(phi) * st) + n * ct;
		float s = mix(0.1, 1.0, fi * fi);
		vec3 sp = c + h * (p.radius * s);
		vec2 suv = uv_of(sp);
		if (any(lessThan(suv, vec2(0.0))) || any(greaterThan(suv, vec2(1.0))))
			continue;
		float sz = view_at(suv).z;
		float range = smoothstep(0.0, 1.0, p.radius / max(abs(c.z - sz), 1e-4));
		occ += (sz >= sp.z + 0.02 * p.radius ? 1.0 : 0.0) * range;
	}
	return 1.0 - occ / float(N);
}

float gtao(vec2 uv, vec3 c, vec3 n, vec2 px) {
	vec3 v = normalize(-c);
	float depth = max(-c.z, 1e-3);
	// the radius in pixels at this depth
	float rpx = p.radius * abs(scene.projection[1][1]) * 0.5 * p.size.y / depth;
	if (rpx < 1.0)
		return 1.0;
	const int SLICES = 3, STEPS = 6;
	float noise_slice = ign(px, p.frame), noise_step = ign(px + vec2(13.0, 7.0), p.frame);
	float falloff_range = 0.615 * p.radius, falloff_from = p.radius * (1.0 - 0.615);
	float falloff_mul = -1.0 / falloff_range, falloff_add = falloff_from / falloff_range + 1.0;
	float vis = 0.0;
	for (int sl = 0; sl < SLICES; sl++) {
		float phi = (float(sl) + noise_slice) * PI / float(SLICES);
		vec2 omega = vec2(cos(phi), -sin(phi));           // screen: y down
		vec3 dir = vec3(cos(phi), sin(phi), 0.0);         // view: y up
		vec3 ortho = dir - dot(dir, v) * v;
		vec3 axis = normalize(cross(ortho, v));
		vec3 pn = n - axis * dot(n, axis);
		float pn_len = length(pn);
		if (pn_len < 1e-4)
			continue;
		float cos_n = clamp(dot(pn, v) / pn_len, 0.0, 1.0);
		float nang = sign(dot(ortho, pn)) * acos(cos_n);
		float low0 = cos(nang + PI * 0.5), low1 = cos(nang - PI * 0.5);
		float h0c = low0, h1c = low1;
		for (int st = 0; st < STEPS; st++) {
			float s = (float(st) + noise_step) / float(STEPS);
			s *= s;
			vec2 off = omega * (s * rpx + 1.0) / p.size;
			vec3 d0 = view_at(uv + off) - c, d1 = view_at(uv - off) - c;
			float l0 = length(d0), l1 = length(d1);
			float c0 = dot(d0 / max(l0, 1e-5), v), c1 = dot(d1 / max(l1, 1e-5), v);
			c0 = mix(low0, c0, clamp(l0 * falloff_mul + falloff_add, 0.0, 1.0));
			c1 = mix(low1, c1, clamp(l1 * falloff_mul + falloff_add, 0.0, 1.0));
			h0c = max(h0c, c0);
			h1c = max(h1c, c1);
		}
		float h0 = -acos(clamp(h1c, -1.0, 1.0)), h1 = acos(clamp(h0c, -1.0, 1.0));
		h0 = nang + clamp(h0 - nang, -PI * 0.5, PI * 0.5);
		h1 = nang + clamp(h1 - nang, -PI * 0.5, PI * 0.5);
		float a0 = (cos_n + 2.0 * h0 * sin(nang) - cos(2.0 * h0 - nang)) * 0.25;
		float a1 = (cos_n + 2.0 * h1 * sin(nang) - cos(2.0 * h1 - nang)) * 0.25;
		vis += pn_len * (a0 + a1);
	}
	return clamp(vis / float(SLICES), 0.0, 1.0);
}

void main() {
	// (ip: the occlusion's texel, fp: the picture's pixel it is found for)
	ivec2 ip = ivec2(gl_GlobalInvocationID.xy);
	ivec2 fp = ip * int(p.scale + 0.5);
	if (fp.x >= int(p.size.x) || fp.y >= int(p.size.y))
		return;
	vec2 texel = 1.0 / p.size;
	vec2 uv = (vec2(fp) + 0.5) * texel;
	float d = textureLod(depth_tex, uv, 0.0).r;
	float ao = 1.0;
	// reversed depth: 0 is the far plane (the background sky); the sky faces are marked by their roughness
	if (d > 0.0 && !is_sky(textureLod(normal_rough_tex, uv, 0.0).a)) {
		vec3 c = view_at(uv);
		vec3 n = normal_at(uv, c, texel);
		ao = p.mode > 1.5 ? gtao(uv, c, n, vec2(ip)) : ssao(uv, c, n, vec2(ip));
	}
	imageStore(ao_image, ip, vec4(ao, 0.0, 0.0, 1.0));
}
";

    const string ApplySource = Common + @"
layout(set = 0, binding = 0) uniform sampler2D ao_tex;
layout(set = 0, binding = 1) uniform sampler2D depth_tex;
layout(r16f, set = 0, binding = 2) uniform restrict writeonly image2D out_image;
layout(set = 0, binding = 3) uniform sampler2D normal_rough_tex;

float view_z(vec2 uv) {
	float d = textureLod(depth_tex, uv, 0.0).r;
	vec4 v = scene.inv_projection * vec4(uv * 2.0 - 1.0, d, 1.0);
	return v.z / v.w;
}

void main() {
	ivec2 ip = ivec2(gl_GlobalInvocationID.xy);
	if (ip.x >= int(p.size.x) || ip.y >= int(p.size.y))
		return;
	vec2 texel = 1.0 / p.size;
	vec2 uv = (vec2(ip) + 0.5) * texel;
	if (is_sky(textureLod(normal_rough_tex, uv, 0.0).a) || textureLod(depth_tex, uv, 0.0).r <= 0.0) {
		imageStore(out_image, ip, vec4(1.0));      // the sky as drawn
		return;
	}
	float zc = view_z(uv);
	float sum = 0.0, wsum = 0.0;
	if (p.scale < 1.5) {
		// a 5x5 blur over pixels at about the same depth (the noise and the slices' steps smoothed, edges kept)
		for (int y = -2; y <= 2; y++)
			for (int x = -2; x <= 2; x++) {
				vec2 suv = uv + vec2(x, y) * texel;
				float w = exp(-abs(view_z(suv) - zc) / max(0.05 * abs(zc), 0.02) * 4.0);
				sum += textureLod(ao_tex, suv, 0.0).r * w;
				wsum += w;
			}
	} else {
		// found for every second pixel each way: those within four pixels of this one (three or four each way), by
		// their depth being about this one's: the same blur, and the spread over the full picture, in one. The weights
		// are a 3x3 box of them about each of the nearest (the noise averages out exactly over such a box; weights
		// falling off with distance left its hatching), a pixel between two taking both boxes: 1 1 1, or 1 2 2 1
		ivec2 q0 = (ip - 2) >> 1, last = (ivec2(p.size) - 1) / 2;
		bvec2 odd = equal(ip & 1, ivec2(1));
		for (int y = 0; y < 4; y++)
			for (int x = 0; x < 4; x++) {
				ivec2 q = q0 + ivec2(x, y);
				ivec2 off = abs(q * 2 - ip);
				float wx = odd.x ? (off.x <= 1 ? 2.0 : 1.0) : (off.x <= 2 ? 1.0 : 0.0);
				float wy = odd.y ? (off.y <= 1 ? 2.0 : 1.0) : (off.y <= 2 ? 1.0 : 0.0);
				float ws = wx * wy;
				if (ws <= 0.0)
					continue;
				q = clamp(q, ivec2(0), last);
				vec2 suv = (vec2(q * 2) + 0.5) * texel;
				float w = exp(-abs(view_z(suv) - zc) / max(0.05 * abs(zc), 0.02) * 4.0) * ws;
				sum += texelFetch(ao_tex, q, 0).r * w;
				wsum += w;
			}
	}
	float ao = sum / max(wsum, 1e-4);
	// a surface the fog whitens (roughness under 1: fog_roughness) keeps that much less of it
	float st = textureLod(normal_rough_tex, uv, 0.0).a;
	float rough = min(st, 1.0 - st) * 255.0 / 127.0;
	float clear = clamp((rough - 0.25) / 0.75, 0.0, 1.0);
	ao = clamp(mix(1.0, ao, p.intensity * clear), 0.0, 1.0);
	imageStore(out_image, ip, vec4(ao, 0.0, 0.0, 1.0));
}
";
}
