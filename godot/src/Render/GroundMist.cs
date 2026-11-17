using System.Collections.Generic;
using Chosen.Sim;
using Godot;

namespace Chosen.Render;

/// <summary>
/// Ground mist (Options > Enhanced, r_groundmist): the level's env_mist entities (tools/mist brushes over the floors:
/// the converter makes them over the open sectors, or a map places its own) drawn as volumetric fog. Each is a
/// fog volume fixed over its bounds with a map made as the level loads: for each 16-unit cell the bottom and top of its
/// brushes there (the floor and the mist's height, sloped floors too). Its shader puts the density between them, thickest
/// at the bottom, with drifting noise for wisps. Nothing follows the camera. The dynamic lights light it
/// (EnhancedFog.LightEnergy).
/// </summary>
public partial class GroundMist : Node3D
{
    public static readonly Core.Cvar Mode = Core.Cvars.Register("r_groundmist", "1",
        "ground mist over the floors of the level's env_mist areas (the converter puts them over open sectors)", saved: true);
    public static readonly Core.Cvar Density = Core.Cvars.Register("r_groundmistdensity", "2",
        "ground mist: how thick it is", saved: true);

    public static readonly Core.Cvar WaterMist = Core.Cvars.Register("r_watermist", "1",
        "1: mist lying on top of the water", saved: true);

    /// <summary>Is any mist on (the environment's volumetric fog must be, EnhancedFog)?</summary>
    public static bool AnyOn => Mode.Bool || WaterMist.Bool;

    const float NoMist = 1e6f;           // a cell no mist brush covers
    const int MaxCells = 256;            // a map's cells each way at most

    sealed class Area
    {
        public FogVolume Volume;
        public ShaderMaterial Material;
        public float Density = 1;
        public Color? Colour;            // its own light (an atmosphere's colour), else the level's
        public bool Water;               // over water (r_watermist), not the ground
        public int Fog = -1;             // an area's own distance fog seen from outside (its atmosphere; -1: mist)
        public float FogEnd;             // where that fog is complete (Quake units)
        public HashSet<int> Own, Rooms;  // its sectors' room colours and those sharing its place (DynamicLights): hidden
                                         // when one of these is nearer the camera than its own (LightRules.RoomSeen)
        public Aabb Box;                 // Godot
        public bool Shown;
    }

    readonly List<Area> _areas = new();
    static Shader _shader;
    static NoiseTexture3D _noise;
    float _sentDensity = -1, _sentFogDensity = -1;
    Color _sentColour;

    // (the portals' test: what lies behind an active portal's face is the far room's mist, at the far room's place, as the
    // level's copy is; a fog shader knows no camera, so the camera is camera_world, Game, and it takes no varying)
    static string ShaderCode => @"
shader_type fog;
global uniform vec3 camera_world;
" + PortalRenderer.Clip.Replace("varying vec3 light_world;", "").Replace("varying float light_sky;", "").Replace("varying float light_lit;", "") + @"
uniform sampler2D floor_map : filter_nearest, repeat_disable;     // r: the mist's bottom, g: its top (Godot y)
uniform vec4 map_rect;          // Godot x, z of the map's corner, its size along x, z
uniform float mist_density;
uniform vec3 mist_color;        // the light it gives itself
uniform sampler3D mist_noise : repeat_enable;
uniform bool solid;             // an area's distance fog: even from its floor to its ceiling, no patches
// the layer at height y over one cell: thickest at its bottom, none past its top
float cell_layer(ivec2 c, ivec2 size, float y) {
	if (any(lessThan(c, ivec2(0))) || any(greaterThanEqual(c, size)))
		return 0.0;
	vec2 bt = texelFetch(floor_map, c, 0).rg;
	float h = y - bt.x, thick = max(bt.y - bt.x, 0.01);
	return h < -0.03 || h > thick ? 0.0 : 1.0 - smoothstep(0.0, thick, h);
}
// the portal the line of sight to w goes through: its row, or -1
int mist_portal(vec3 w, vec3 eye) {
	if (portal_slot(portal_slot_0, portal_box_0, portal_frame_0, eye, w)) return int(portal_slot_0.z + 0.5);
	if (portal_slot(portal_slot_1, portal_box_1, portal_frame_1, eye, w)) return int(portal_slot_1.z + 0.5);
	if (portal_slot(portal_slot_2, portal_box_2, portal_frame_2, eye, w)) return int(portal_slot_2.z + 0.5);
	if (portal_slot(portal_slot_3, portal_box_3, portal_frame_3, eye, w)) return int(portal_slot_3.z + 0.5);
	return -1;
}
void fog() {
	// through a portal, the mist of the room it shows (E1M1's yard from its rooftops: none, until down in it)
	vec3 at = WORLD_POSITION;
	int through = mist_portal(at, camera_world);
	if (through >= 0)
		at += texelFetch(portal_data, ivec2(" + PortalRenderer.FarColumn + @", through), 0).xyz;
	// the four nearest cells' layers blended, not their heights (blended heights over a step, a yard's beside a roof's,
	// made a sheet of mist up the wall between)
	ivec2 size = textureSize(floor_map, 0);
	vec2 p = (at.xz - map_rect.xy) / map_rect.zw * vec2(size) - 0.5;
	ivec2 c = ivec2(floor(p));
	vec2 f = p - vec2(c);
	float y = at.y;
	if (solid) {
		ivec2 cc = clamp(ivec2(floor(p + 0.5)), ivec2(0), size - 1);
		vec2 bt = texelFetch(floor_map, cc, 0).rg;
		DENSITY = y >= bt.x && y <= bt.y ? mist_density : 0.0;
		ALBEDO = vec3(0.0);
		EMISSION = mist_color;
	} else {
		float layer = mix(mix(cell_layer(c, size, y), cell_layer(c + ivec2(1, 0), size, y), f.x),
			mix(cell_layer(c + ivec2(0, 1), size, y), cell_layer(c + ivec2(1, 1), size, y), f.x), f.y);
		vec3 drift = vec3(TIME * 0.11, TIME * 0.02, TIME * 0.07);
		float n = texture(mist_noise, at * 0.045 + drift).r;
		float n2 = texture(mist_noise, at * 0.11 - drift * 0.7).r;
		// patches and wisps: much of the floor clear
		DENSITY = mist_density * layer * clamp((n * 1.4 + n2 * 0.6) * 1.6 - 1.0, 0.0, 1.2);
		// (it is far thicker than the fog: the lights, tuned for that, lit it through and through)
		ALBEDO = vec3(0.2);
		EMISSION = mist_color;
	}
}
";

    public override void _Ready()
    {
        _shader ??= new Shader { Code = ShaderCode };
        _noise ??= new NoiseTexture3D
        {
            Width = 64, Height = 64, Depth = 64, Seamless = true,
            Noise = new FastNoiseLite { NoiseType = FastNoiseLite.NoiseTypeEnum.Perlin, Frequency = 0.04f, FractalOctaves = 3 },
        };
        Mode.Changed += EnhancedFog.Refresh;
        Mode.Changed += ShowSettings;
        WaterMist.Changed += EnhancedFog.Refresh;
        WaterMist.Changed += ShowSettings;
        Density.Changed += ShowSettings;
    }

    public override void _ExitTree()
    {
        Mode.Changed -= EnhancedFog.Refresh;
        Mode.Changed -= ShowSettings;
        WaterMist.Changed -= EnhancedFog.Refresh;
        WaterMist.Changed -= ShowSettings;
        Density.Changed -= ShowSettings;
    }

    // a setting changed: shown at once (Game's frame doesn't call Update with the menu up)
    void ShowSettings()
    {
        var cam = IsInsideTree() ? GetViewport().GetCamera3D() : null;
        if (cam != null)
            Update(cam);
    }

    // a new level: the last one's fog volumes go
    void Clear()
    {
        foreach (var a in _areas)
            a.Volume.QueueFree();
        _areas.Clear();
        _sentDensity = -1;
    }

    /// <summary>A level: the mist over the floors of each area (AreaAtmosphere.MistAreas: adjacent sectors of one
    /// atmosphere, its height, Quake units, density and colour; the game's guess 24 units high, as the converter's env_mist
    /// prisms), and over the water's surfaces.</summary>
    public void SetupNative(Sim.SectorMap map, List<(List<int> sectors, float height, float density, Color? colour)> areas, List<List<int>> water = null,
        List<(List<int> sectors, int atmosphere, float end, Color? colour)> fogs = null)
    {
        Clear();
        foreach (var (g, h, d, c) in areas)
            AddGroups(map, new List<List<int>> { g }, h, d, false, c);
        // (lower and thicker than the ground's: a layer lying on the surface)
        if (water != null)
            AddGroups(map, water, 14, 1.6f, true);
        if (fogs != null)
            foreach (var f in fogs)
                AddFog(map, f.sectors, f.atmosphere, f.end, f.colour);
    }

    /// <summary>The camera's sector's atmosphere (AreaAtmosphere: Game, each frame): an area of its own fog is shown from
    /// outside only (inside, the camera's fog is its fog).</summary>
    public static int CameraAtmosphere = -1;

    /// <summary>The room colours nearest the camera first (DynamicLights.RoomsByNearness: Game, each frame): a fog volume
    /// knows no render layers, so an area's mist or fog is hidden when a room sharing its place is nearer than its own
    /// (in it, or in a sector sharing no place beside it: test.mapx's corridor over a corner of room 0, its fog stood in
    /// that corner; room 15's mist seen from yard 17 in room 0's place, gone as the camera stepped into room 0).</summary>
    public static List<int> CameraRooms = new();

    /// <summary>The room colours of an area's sectors and of the other rooms sharing their place (none: null).</summary>
    static (HashSet<int> own, HashSet<int> rooms) RoomsOver(Sim.SectorMap map, List<int> g)
    {
        HashSet<int> owns = null, rooms = null;
        foreach (int s in g)
        {
            int own = DynamicLights.RoomColourOf(s);
            if (own > 0)
                (owns ??= new HashSet<int>()).Add(own);
            foreach (int o in map.OverlapsOf(s))
                if (DynamicLights.RoomColourOf(o) is var c && c > 0 && c != own)
                    (rooms ??= new HashSet<int>()).Add(c);
        }
        return (owns, rooms);
    }

    /// <summary>An area with its own distance fog (AreaAtmosphere.FogAreas), seen from outside as a bank of it: a fog
    /// volume from its floors to its ceilings, as thick as its fog (nearly opaque at its end).</summary>
    void AddFog(Sim.SectorMap map, List<int> g, int atmosphere, float end, Color? colour)
    {
        Aabb box = default;
        for (int k = 0; k < g.Count; k++)
        {
            var r = map.SectorBox(g[k]);
            var c = r.GetCenter();
            float lo = float.MaxValue, hi = float.MinValue;
            foreach (var p in new[] { r.Position, r.End, new Vector2(r.Position.X, r.End.Y), new Vector2(r.End.X, r.Position.Y), c })
            {
                lo = Mathf.Min(lo, map.FloorZ(g[k], p.X, p.Y));
                hi = Mathf.Max(hi, map.CeilZ(g[k], p.X, p.Y));
            }
            var b = new Aabb(new Vector3(r.Position.X, r.Position.Y, lo), new Vector3(r.Size.X, r.Size.Y, Mathf.Max(hi - lo, 1)));
            box = k == 0 ? b : box.Merge(b);
        }
        AddArea(box, 1, (x, y) =>
        {
            float lo = NoMist, hi = NoMist;
            foreach (int s in g)
                if (map.InSector(s, x, y))
                {
                    float z = map.FloorZ(s, x, y);
                    if (z < lo)
                        (lo, hi) = (z, map.CeilZ(s, x, y));
                }
            return (lo, hi);
        }, false, colour);
        var a = _areas[^1];
        (a.Own, a.Rooms) = RoomsOver(map, g);
        a.Fog = atmosphere;
        a.FogEnd = Mathf.Max(end, 16f);
        a.Material.SetShaderParameter("solid", true);
    }

    void AddGroups(Sim.SectorMap map, List<List<int>> groups, float height, float density, bool water, Color? colour = null)
    {
        foreach (var g in groups)
        {
            Aabb box = default;
            for (int k = 0; k < g.Count; k++)
            {
                var r = map.SectorBox(g[k]);
                var c = r.GetCenter();
                float lo = float.MaxValue, hi = float.MinValue;
                foreach (var p in new[] { r.Position, r.End, new Vector2(r.Position.X, r.End.Y), new Vector2(r.End.X, r.Position.Y), c })
                {
                    float z = map.FloorZ(g[k], p.X, p.Y);
                    lo = Mathf.Min(lo, z);
                    hi = Mathf.Max(hi, z + height);
                }
                var b = new Aabb(new Vector3(r.Position.X, r.Position.Y, lo), new Vector3(r.Size.X, r.Size.Y, hi - lo));
                box = k == 0 ? b : box.Merge(b);
            }
            AddArea(box, density, (x, y) =>
            {
                float lo = NoMist, hi = NoMist;
                foreach (int s in g)
                    if (map.InSector(s, x, y))
                    {
                        float z = map.FloorZ(s, x, y);
                        if (z < lo)
                            (lo, hi) = (z, z + height);
                    }
                return (lo, hi);
            }, water, colour);
            (_areas[^1].Own, _areas[^1].Rooms) = RoomsOver(map, g);
        }
    }

    // an area over box (Quake): its floor map (the mist's bottom and top at each cell, NoMist where there is none)
    void AddArea(Aabb box, float density, System.Func<float, float, (float lo, float hi)> span, bool water = false, Color? colour = null)
    {
        // the map over the area (Quake x, y): 16-unit cells, coarser for a big one
        float cell = Mathf.Max(16f, Mathf.Max(box.Size.X, box.Size.Y) / MaxCells);
        int nx = Mathf.Clamp(Mathf.CeilToInt(box.Size.X / cell) + 1, 2, MaxCells), ny = Mathf.Clamp(Mathf.CeilToInt(box.Size.Y / cell) + 1, 2, MaxCells);
        // the image: x along Godot x (Quake y), y along Godot z (Quake x)
        var img = Image.CreateEmpty(ny, nx, false, Image.Format.Rgf);
        for (int i = 0; i < nx; i++)
            for (int j = 0; j < ny; j++)
            {
                float x = box.Position.X + (i + 0.5f) * cell, y = box.Position.Y + (j + 0.5f) * cell;
                var (lo, hi) = span(x, y);
                img.SetPixel(j, i, lo >= NoMist ? new Color(NoMist, NoMist, 0) : new Color(lo / Coords.QuakeScale, hi / Coords.QuakeScale, 0));
            }
        var mat = new ShaderMaterial { Shader = _shader };
        mat.SetShaderParameter("mist_noise", _noise);
        mat.SetShaderParameter("floor_map", ImageTexture.CreateFromImage(img));
        // map_rect: Godot x of its corner is Quake y, Godot z Quake x
        mat.SetShaderParameter("map_rect", new Vector4(box.Position.Y / Coords.QuakeScale, box.Position.X / Coords.QuakeScale,
            ny * cell / Coords.QuakeScale, nx * cell / Coords.QuakeScale));
        var centre = Coords.QuakeToGodot(box.GetCenter());
        var size = Coords.QuakeToGodot(box.Size + new Vector3(2 * cell, 2 * cell, 16));
        // the whole world, not a box: the floor map keeps it to its area, and a big box, its corners behind the camera,
        // was left out whole at some angles (E3M1's street: looking up a little, all the mist gone with the floor still seen)
        var vol = new FogVolume
        {
            Shape = RenderingServer.FogVolumeShape.World, Material = mat, Visible = false,
            Size = new Vector3(Mathf.Abs(size.X), Mathf.Abs(size.Y), Mathf.Abs(size.Z)),
        };
        AddChild(vol);
        vol.GlobalPosition = centre;
        var half = new Vector3(Mathf.Abs(size.X), Mathf.Abs(size.Y), Mathf.Abs(size.Z)) / 2;
        _areas.Add(new Area { Volume = vol, Material = mat, Density = density, Water = water, Colour = colour, Box = new Aabb(centre - half, half * 2) });
    }

    /// <summary>The portals drawn this frame (their far rooms' mist is seen through them).</summary>
    public IReadOnlyList<PortalRenderer.ActivePortal> Portals = System.Array.Empty<PortalRenderer.ActivePortal>();

    // in view at its own place, or as seen through a portal
    bool AnyInView(Aabb b)
    {
        if (InView(b))
            return true;
        foreach (var p in Portals)
            if (!p.Water && InView(new Aabb(b.Position - Coords.QuakeToGodot(p.Offset), b.Size)))
                return true;
        return false;
    }

    bool InView(Aabb b)
    {
        foreach (var pl in _view)
        {
            // the box's corner furthest along the plane's normal the wrong way (the planes face outwards)
            var c = new Vector3(pl.Normal.X > 0 ? b.Position.X : b.End.X, pl.Normal.Y > 0 ? b.Position.Y : b.End.Y,
                pl.Normal.Z > 0 ? b.Position.Z : b.End.Z);
            if (pl.DistanceTo(c) > 0)
                return false;
        }
        return true;
    }

    readonly List<Plane> _view = new();

    /// <summary>Each frame: shown or not, and its look (the misty level's white, else a dim blue grey; the menu's
    /// density). An area wholly out of the camera's view isn't shown: each costs the fog's whole grid, in view or not.</summary>
    public void Update(Camera3D cam)
    {
        // (an area's own fog shows with the fog on, the mist off or not)
        bool show = (AnyOn || EnhancedFog.Fog.Bool) && _areas.Count > 0;
        _view.Clear();
        if (show)
        {
            var planes = cam.GetFrustum();
            for (int i = 0; i < planes.Count; i++)
                _view.Add(planes[i]);
            ((Godot.Collections.Array)planes).Dispose();      // freed at once (left to the finalizer, it contends with the main thread)
        }
        foreach (var a in _areas)
        {
            bool vis = show && (a.Fog >= 0 ? EnhancedFog.Fog.Bool && a.Fog != CameraAtmosphere : a.Water ? WaterMist.Bool : Mode.Bool)
                && Chosen.Shared.Blood.Map.LightRules.RoomSeen(CameraRooms, a.Own, a.Rooms) && AnyInView(a.Box);
            if (vis != a.Shown)
            {
                a.Shown = vis;
                a.Volume.Visible = vis;
            }
        }
        if (!show)
            return;
        // the light it gives itself (a fog volume's emission takes far more than a surface's to show: these by eye, the
        // misty level's white over DWE3M1's river, a dim blue grey over E1M1's graveyard)
        var t = Atmosphere.MoonTint;
        var colour = EnhancedFog.MistLevel ? new Color(0.9f, 0.9f, 0.9f) : new Color(Mathf.Min(0.42f * t.R, 1f), Mathf.Min(0.45f * t.G, 1f), Mathf.Min(0.54f * t.B, 1f));
        float k = 0.18f * Mathf.Max(Density.Float, 0f);
        float fogDensity = EnhancedFog.Density.Float;
        if (k == _sentDensity && colour == _sentColour && fogDensity == _sentFogDensity)
            return;
        (_sentDensity, _sentColour, _sentFogDensity) = (k, colour, fogDensity);
        foreach (var a in _areas)
        {
            // (an area's fog: three times its end's worth gone at its end, about as the depth fog; dark or the level's
            // white, else its own colour; r_fogdensity brings it nearer as it does the fog)
            if (a.Fog >= 0)
            {
                a.Material.SetShaderParameter("mist_density", 3f / a.FogEnd * Coords.QuakeScale * Mathf.Max(EnhancedFog.Density.Float, 0.05f));
                a.Material.SetShaderParameter("mist_color", (a.Colour ?? (EnhancedFog.MistLevel ? Colors.White : Colors.Black)).SrgbToLinear());
                continue;
            }
            a.Material.SetShaderParameter("mist_density", k * a.Density);
            // (an atmosphere's own colour as bright as the level's blue grey looks: a fog volume's light takes far more to show)
            a.Material.SetShaderParameter("mist_color", a.Colour is { } own ? own * 0.9f : colour);
        }
    }
}
