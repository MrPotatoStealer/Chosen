using System.Collections.Generic;
using Chosen.Core;
using Chosen.Data;
using Chosen.Sim;
using Chosen.Shared.Blood.Map;
using Godot;

namespace Chosen.Render;

/// <summary>
/// Dynamic lights (r_dynamiclights, off by default: Blood has none): point lights on top of Blood's sector shading,
/// from explosions (fading), burning things, fire projectiles (flares, fireballs, napalm, tesla), the player's muzzle
/// flash (Blood's flashEffect), lit torches and candles, and light fixtures (lamps, lanterns, fluorescent tubes; gone
/// with the fixture when it is shot, dark with their room's light). A pool of lights goes to the sources nearest the
/// camera each frame; a source keeps its light while it stays in the pool, so a still light's shadow map isn't redrawn.
/// r_shadows lets them cast shadows (world geometry and voxel models).
///
/// Through a room-over-room portal light crosses as a copy: a light near a portal side gets a copy moved by the link's
/// offset (where it would be in the linked room). The linked room is a region (the other side's footprint, beyond its
/// plane) the shaders know (light_region_N): the copy lights only inside it, the light itself only outside it, so a
/// copy, which has no shadows (it sits behind the linked room's floor or ceiling), never shines through the walls of
/// the rooms around it. A light names its region with its specular amount, which the lit shaders don't use otherwise.
/// </summary>
public partial class DynamicLights : Node3D
{
    /// <summary>Off for this run whatever r_dynamiclights says (comparison views: NBlood has no lights).</summary>
    public static bool Suspended;
    public static readonly Cvar Enabled = Cvars.Register("r_dynamiclights", "1", "1: dynamic lights (explosions, fire, muzzle flashes, torches) on top of Blood's shading", saved: true);
    public static readonly Cvar Shadows = Cvars.Register("r_shadows", "1", "1: dynamic lights cast shadows", saved: true);
    const int PoolSize = 24;
    // only lights near the camera cast shadows: each shadowed light redraws all six faces of its map with everything in
    // its reach whenever something there moves (a turning fan, a door), and DWE2M1's lamps 1400 to 4000 units off, barely
    // lighting the view, made 9000 of its 10000 draw calls. Godot's distance fade: the shadow fades out from this far over
    // ShadowFade and its passes are left out beyond; the light itself stays (its own fade begins far past any pool light)
    public static readonly Cvar ShadowDist = Cvars.Register("r_shadowdist", "1500",
        "how far from the camera (Quake units) dynamic lights cast shadows, fading out over the next 400; 0: all", saved: true);
    const float ShadowFade = 400f;
    // and of those, only the most important cast shadows: brightness and reach over distance to the camera (what it adds
    // to the view), the ones casting now kept unless another is clearly more (no flicker between two alike). A light
    // leaving or joining fades its shadow over ShadowSwap seconds.
    public static readonly Cvar ShadowLights = Cvars.Register("r_shadowlights", "8",
        "the most dynamic lights casting shadows at once, the most important to the view (bright, big and near)", saved: true);
    const float ShadowSwap = 0.35f, ShadowKeep = 1.3f;
    readonly float[] _shadowFade = new float[PoolSize];
    readonly (object, int)[] _shadowKey = new (object, int)[PoolSize];
    readonly float[] _shadowScore = new float[PoolSize];
    readonly int[] _shadowOrder = new int[PoolSize];
    readonly bool[] _shadowPick = new bool[PoolSize];
    ulong _shadowTime;
    const float EnergyScale = 2.5f;     // the sources' energies below are relative; this makes them stand out on Blood's dark walls
    const int MaxRegions = 8;

    struct Source
    {
        public Vector3 Pos;        // Quake units
        public Color Color;
        public float Radius;       // Quake units
        public float Energy;
        public float Dist;
        public bool NoShadow;
        public bool Fixture;       // a light fixture's: the fixtures' voxel models cast no shadow for it, nor take its light
        public int Region;         // 0 everywhere; 1..8 only inside region n-1 (a copy); 9..16 not inside region n-9
        public uint Rooms;         // the render layers it reaches (RoomMask: not the rooms sharing its own's place)
        public object Key;         // what it belongs to (a sprite...) and which copy: keeps its light from frame to frame
        public int Sub;
    }

    /// <summary>Render layer 3: light fixtures' voxel models, left out of the fixture lights' shadow casters and of
    /// what they light.</summary>
    public const uint FixtureLayer = 1u << 2;

    /// <summary>The render layers of the rooms sharing a place (Shared.Blood.Map.LightRules.RoomColours): from 10, eight.</summary>
    public const int RoomBit = 10, RoomColourCount = 8;
    static int[] _roomColours;
    static Sim.SectorMap _roomMap;

    /// <summary>A level's rooms sharing a place coloured (Game: before the world's meshes are built, which take each
    /// sector's colour as their render layer).</summary>
    public static void SetRooms(Sim.SectorMap map)
    {
        _roomMap = map;
        _roomColours = map == null ? null
            : LightRules.RoomColours(map.SectorCount, map.OverlapsOf, s => map.Neighbours[s], RoomColourCount);
    }

    static List<int> _roomsNear = new();
    static int _roomsNearOf = -2;
    static int[] _roomsNearColours;

    /// <summary>The room colours nearest sector s first (LightRules.RoomsByNearness; kept while s stays).</summary>
    public static List<int> RoomsByNearness(int s)
    {
        if (s != _roomsNearOf || _roomsNearColours != _roomColours)
        {
            (_roomsNearOf, _roomsNearColours) = (s, _roomColours);
            _roomsNear = _roomMap == null ? new List<int>() : LightRules.RoomsByNearness(s, _roomColours, i => _roomMap.Neighbours[i]);
        }
        return _roomsNear;
    }

    /// <summary>Sector s's room colour (0: it shares no place, or none was left).</summary>
    public static int RoomColourOf(int s) => _roomColours != null && s >= 0 && s < _roomColours.Length ? _roomColours[s] : 0;

    /// <summary>A sector's meshes' render layer by its colour.</summary>
    public static uint RoomLayer(int colour) => LightRules.RoomLayer(colour, RoomBit);

    /// <summary>The render layers a light in Build sector s reaches: not the rooms sharing its place.</summary>
    static uint RoomMaskOf(int s) => _roomMap == null ? 0xFFFFFFFFu : LightRules.RoomMask(s, _roomColours, _roomMap.OverlapsOf, RoomBit);

    /// <summary>Does a sprite give a fixture's light (its voxel model is left out of that light's shadow casters)?</summary>
    public static bool IsFixtureSprite(Sprite s) => _fixtureSprites.Contains(s);
    static readonly HashSet<Sprite> _fixtureSprites = new();

    /// <summary>Is a picture a light fixture's (Render: its bright colours glow, r_glow)?</summary>
    public static bool IsFixtureTile(int tile) => LightRules.Fixtures.ContainsKey(tile);

    /// <summary>A light the map places (the converter's 'light' entities: torches and candles, light fixtures,
    /// decorations Blood draws fullbright; or one a mapper put there): what it gives, and the sprite it belongs to
    /// (lightOf = the sprite's lightId), which it follows and goes out with; without one it shines from its origin.</summary>
    sealed class MapLight
    {
        public string Kind = "";        // torch, fixture, glow; point: a .MAPX's light of its own (Sprite null)
        // a .MAPX's light (LightRules: the map's LITE chunk): its offset from the spot on its sprite, or its place
        // (a point), Quake units; its shadows, its sector's light effect; the game's rules for a .MAP's lights
        public bool Placed, NoShadow, SectorLight;
        public Vector3 Offset, Fixed;
        public Color Color;
        public float Radius, Energy, Flicker, Up, ZOffset;
        public int Tile, Seed;
        public Sprite Sprite;
        // its light-effect sector (World.FxLightSector, a search over the sectors), looked up again when it moves
        public Vector3 FxAt = new(float.NaN, 0, 0);
        public Sector FxSec;
        // the offsets of the links it reaches through (ThroughPortals), worked out again when it moves or the links change
        public Vector3 LinksAt = new(float.NaN, 0, 0);
        public int LinksVersion = -1;
        public float LinksReach;
        public Vector3[] LinkOffsets = System.Array.Empty<Vector3>();
        // KeepInRoom's last answer along the floor: looked for again when the place asked about moves
        public Vector2 KeptFrom = new(float.NaN, 0), Kept;
        // a .MAPX point light's room as the map gives it (-1: the sector holding its place); the render layers it
        // reaches (RoomsOf), worked out once
        public int BSector = -1;
        public uint Rooms;
        public bool RoomsSet;
    }
    readonly List<MapLight> _mapLights = new();

    /// <summary>The light test for the lit shaders' light(): a light restricted to a link region (see the class).</summary>
    public const string RegionFunction = @"
global uniform vec4 light_region_0;
global uniform vec4 light_region_1;
global uniform vec4 light_region_2;
global uniform vec4 light_region_3;
global uniform vec4 light_region_4;
global uniform vec4 light_region_5;
global uniform vec4 light_region_6;
global uniform vec4 light_region_7;
global uniform vec4 light_region_planes_0;
global uniform vec4 light_region_planes_1;
bool in_light_region(vec3 w, int i) {
	vec4 b = i == 0 ? light_region_0 : i == 1 ? light_region_1 : i == 2 ? light_region_2 : i == 3 ? light_region_3
		: i == 4 ? light_region_4 : i == 5 ? light_region_5 : i == 6 ? light_region_6 : light_region_7;
	vec4 pl = i < 4 ? light_region_planes_0 : light_region_planes_1;
	int j = i % 4;
	float p = j == 0 ? pl.x : j == 1 ? pl.y : j == 2 ? pl.z : pl.w;
	if (w.x < b.x || w.x > b.z || w.z < b.y || w.z > b.w) return false;
	return (w.y - (abs(p) - 1000.0)) * sign(p) > -0.03125;
}
bool light_skipped(float specular_amount, vec3 w) {
	int k = int(round(specular_amount * 0.5));
	if (k <= 0) return false;
	return in_light_region(w, (k - 1) % 8) != (k <= 8);
}
";

    public static readonly Core.Cvar FlickerAmount = Core.Cvars.Register("r_lightflicker", "1",
        "how much flames' lights flicker: 0 steady .. 2 strong", saved: true);
    /// <summary>The user's taste for how far the dynamic lights reach (report 20261115-161749: Eviction's cinema, its
    /// floor lamps small pools): every light's radius times this, the map's and the game's own (shots, explosions).</summary>
    public static readonly Core.Cvar RangeScale = Core.Cvars.Register("r_lightrange", "1",
        "how far dynamic lights reach: 1 as they are .. 3 three times as far", saved: true);
    static float Reach => Mathf.Clamp(RangeScale.Float, 0.25f, 4f);

    readonly List<Vector3> _testLights = new();

    /// <summary>A fixed white test light (the light command), or clear them.</summary>
    public void AddTestLight(Vector3 pos) => _testLights.Add(pos);
    public void ClearTestLights() => _testLights.Clear();

    readonly List<OmniLight3D> _pool = new();
    readonly List<Source> _sources = new();
    readonly Dictionary<Sprite, double> _born = new();       // when an explosion was first seen (fading)
    readonly HashSet<Sprite> _seen = new();
    readonly Dictionary<(object, int), int> _assigned = new();     // a source's light last frame
    readonly Dictionary<(object, int), int> _assignedNext = new();
    readonly (object, int)[] _poolKeys = new (object, int)[PoolSize];
    readonly Dictionary<Sim.Collision.Link, int> _regionOf = new();
    int _regionsVersion;
    // what each light of the pool was last given: a Godot getter makes a call too, and most of it doesn't change
    struct PoolSent
    {
        public bool Visible, Shadow;
        public Vector3 Pos;
        public Color Color;
        public float Energy, Range, Specular, Fog, ShadowOpacity;
        public uint Mask;
        public int Mode;
    }
    readonly PoolSent[] _sent = new PoolSent[PoolSize];
    readonly int[] _slotOf = new int[PoolSize];
    readonly bool[] _used = new bool[PoolSize];
    // squared distance past which a map light can't be among the nearest PoolSize next frame (from last frame's order)
    float _farCull = float.MaxValue;
    static readonly object PlayerFlash = new(), TestKey = new();
    World _world;
    double _now;

    public override void _Ready()
    {
        for (int i = 0; i < PoolSize; i++)
        {
            var l = new OmniLight3D { Visible = false, LightSpecular = 0, ShadowEnabled = false, OmniShadowMode = OmniLight3D.ShadowMode.Cube, OmniAttenuation = 1.5f };
            AddChild(l);
            ApplyShadowDist(l);
            _sent[_pool.Count] = new PoolSent { Pos = new Vector3(float.NaN, 0, 0), Energy = float.NaN, Range = float.NaN, Specular = 0, Fog = float.NaN, Mode = -1, ShadowOpacity = float.NaN };
            _pool.Add(l);
        }
        ShadowDist.Changed += ApplyShadowDists;
        // how much they light the enhanced fog (none with it off)
        EnhancedFog.GlowChanged += ApplyFogGlow;
        ApplyFogGlow();
    }

    void ApplyFogGlow()
    {
        _fogGlow = EnhancedFog.LightEnergy;
    }
    float _fogGlow;

    public override void _ExitTree()
    {
        EnhancedFog.GlowChanged -= ApplyFogGlow;
        ShadowDist.Changed -= ApplyShadowDists;
    }

    void ApplyShadowDists()
    {
        foreach (var l in _pool)
            ApplyShadowDist(l);
    }

    static void ApplyShadowDist(OmniLight3D l)
    {
        float d = ShadowDist.Float;
        l.DistanceFadeEnabled = d > 0;
        l.DistanceFadeBegin = 1e6f;
        l.DistanceFadeShadow = d / Coords.QuakeScale;
        l.DistanceFadeLength = ShadowFade / Coords.QuakeScale;
    }

    public void Setup(World world)
    {
        _world = world;
        _born.Clear();
        _assigned.Clear();
        SetupRegions();
        SetupSpriteLights();
    }

    // (the light fixtures by the tile drawn, the torches', candles' and glows' lights: Chosen.Blood's LightRules, which
    // the editor's Generate Lights follows too)

    /// <summary>A level from its Build data: a .MAP's lights from its sprites by the game's rules (LightRules: a torch
    /// or candle, a light fixture by its tile, a decoration Blood draws fullbright); a .MAPX's are the ones it places
    /// (SetupPlacedLights), none made from its sprites.</summary>
    void SetupSpriteLights()
    {
        _bursts.Clear();
        _mapLights.Clear();
        _fixtureSprites.Clear();
        if (_world.Board?.File.Lights is { } placed)
        {
            SetupPlacedLights(placed);
            GameConsole.Print($"{_mapLights.Count} map lights (the map's own, of {placed.Count})");
            return;
        }
        foreach (var s in _world.Sprites)
        {
            if (s.Removed || s is Player)
                continue;
            MapLight l = null;
            if (s.ClassName is "decoration_torch" or "decoration_candle")
            {
                bool torch = s.ClassName == "decoration_torch";
                l = new MapLight
                {
                    Kind = "torch", ZOffset = torch ? 24 : 8, Color = Color.Color8(255, 160, 70),
                    Radius = torch ? LightRules.TorchRadius : LightRules.CandleRadius,
                    Energy = torch ? LightRules.TorchEnergy : LightRules.CandleEnergy, Flicker = torch ? LightRules.TorchFlicker : LightRules.CandleFlicker,
                };
                // (as a fixture's: its own model doesn't shadow its light, at its top)
                _fixtureSprites.Add(s);
            }
            else if (LightRules.Fixtures.TryGetValue(s.Picnum, out var f))
            {
                l = new MapLight { Kind = "fixture", Tile = s.Picnum, Color = Color.Color8(f.r, f.g, f.b), Radius = f.radius, Energy = f.energy, Up = f.up, Flicker = f.flicker };
                _fixtureSprites.Add(s);
            }
            else if (s.StatNum == Data.K.kStatDecoration && s.Shade <= -64 && s.Picnum > 0)
                l = new MapLight { Kind = "glow", Color = Color.Color8(255, 190, 110), Radius = LightRules.GlowRadius, Energy = LightRules.GlowEnergy, Flicker = LightRules.GlowFlicker };
            if (l == null)
                continue;
            l.Sprite = s;
            l.Seed = s.Edict;
            _mapLights.Add(l);
        }
        GameConsole.Print($"{_mapLights.Count} map lights (from its sprites)");
    }

    /// <summary>A .MAPX's lights (its LITE chunk: Forge places them, or makes them from the sprites by the same rules): one
    /// attached to a sprite follows it and goes out with it as a .MAP's does, shining from its kind's spot moved by its
    /// offset; a point light shines from its place. Each says whether it casts shadows and follows its sector's light.</summary>
    void SetupPlacedLights(List<Chosen.Shared.Blood.Map.MapLight> placed)
    {
        int seed = 1;
        foreach (var p in placed)
        {
            Sprite s = null;
            if (p.Attached)
            {
                s = _world.SpriteOfB(p.Sprite);
                // (a sprite the game doesn't spawn, or gone: monsters off took it)
                if (s == null || s.Removed)
                    continue;
            }
            var l = new MapLight
            {
                Placed = true, Sprite = s, NoShadow = !p.Shadows, SectorLight = p.SectorLight,
                Kind = p.Kind switch { LightKind.Torch => "torch", LightKind.Fixture => "fixture", LightKind.Glow => "glow", _ => "point" },
                Color = Color.Color8(p.R, p.G, p.B), Radius = p.Radius / 16f, Energy = p.Energy, Flicker = p.Flicker, Up = p.Up,
                Tile = p.Tile, ZOffset = s != null && s.Type == LightRules.TorchType ? 24 : 8, Seed = s?.Edict ?? 100000 + seed,
                Offset = World.B2Q(p.X, p.Y, p.Z),
                Fixed = World.B2Q(p.X, p.Y, p.Z),
                BSector = p.Attached ? -1 : p.Sector,
            };
            seed++;
            if (s != null && p.Kind is LightKind.Torch or LightKind.Fixture)
                _fixtureSprites.Add(s);
            _mapLights.Add(l);
        }
    }

    /// <summary>The links drawn as portals changed (Enhanced.WaterPortals): their light regions again.</summary>
    public void PortalsChanged()
    {
        if (_world != null)
            SetupRegions();
    }

    /// <summary>The map's lights this frame: a sprite's as long as it gives it (a torch lit, a fixture showing its
    /// tile, in its sector's light), placed on it where it is now; a placed one at its origin.</summary>
    void CollectMapLights(Player player, Vector3 eye, float lerp)
    {
        foreach (var l in _mapLights)
        {
            var s = l.Sprite;
            // a .MAPX's point light: from its place, as lit as its sector's light effect when it follows it
            if (s == null)
            {
                if (TooFar(l, l.Fixed, eye))
                    continue;
                float lv = l.SectorLight ? FxLevel(l, l.Fixed) : 1;
                if (lv > 0.01f)
                    Add(l, l.Fixed, l.Color, l.Radius, l.Energy * lv * Flicker(l.Flicker, l.Seed), eye, noShadow: l.NoShadow, rooms: RoomsOf(l));
                continue;
            }
            // far from the eye, and from it through any link: not among the nearest, so none of the rest is worked out
            // (an open level's hundreds of lights, each searching for its sector every frame: DWE3M1, 1.8 ms)
            if (TooFar(l, s.Origin, eye))
                continue;
            if (s.Removed || s == player || s.BurnTime > 0)
                continue;
            var pos = s.PrevOrigin.Lerp(s.Origin, lerp);
            if (l.Kind == "torch")
            {
                if (s.Visible && (!s.HasX || s.NoX != 0 || s.XState != 0))
                    Add(KeyOf(l, s), Placed(l, s, FlameSpot(s, pos, l)), l.Color, l.Radius, l.Energy * Flicker(l.Flicker, l.Seed), eye, fixture: true, noShadow: l.NoShadow, rooms: RoomsOf(l));
                continue;
            }
            if (!s.Visible || (s.Cstat & Cstat.Invisible) != 0 || s.OutsideHome)
                continue;
            int pic = DrawnTile(s);
            // a .MAPX's point light on a sprite: from the sprite's place moved by its offset, while it shows
            if (l.Kind == "point")
            {
                float level = l.SectorLight ? FxLevel(l, s.Origin) : 1;
                if (level > 0.01f)
                    Add(KeyOf(l, s), Placed(l, s, pos), l.Color, l.Radius, l.Energy * level * Flicker(l.Flicker, l.Seed), eye, noShadow: l.NoShadow, rooms: RoomsOf(l));
                continue;
            }
            if (l.Kind == "fixture")
            {
                if (l.Tile >= 0 && pic != l.Tile)
                    continue;      // shot (its broken frames), or a light switch showing its off tile
                // one Blood draws in its sector's light (not fullbright) is as lit as the sector's light effect: dark
                // with the room's lights switched off, flickering with a flickering room (a .MAPX's: when it says so)
                float level = s.Shade + Tiles.Shade(pic) <= -64 || (l.Placed && !l.SectorLight) ? 1 : FxLevel(l, s.Origin);
                if (level > 0.01f)
                    Add(KeyOf(l, s), Placed(l, s, FixtureSpot(s, pos, pic, l.Up, eye)), l.Color, l.Radius, l.Energy * level * Flicker(l.Flicker, l.Seed), eye, fixture: true, noShadow: l.NoShadow, rooms: RoomsOf(l));
                continue;
            }
            // a decoration Blood draws fullbright: a soft glow from its middle
            if (pic > 0)
            {
                float level = l.Placed && l.SectorLight ? FxLevel(l, s.Origin) : 1;
                if (level > 0.01f)
                    Add(KeyOf(l, s), Placed(l, s, pos + new Vector3(0, 0, Tiles.H(pic) * s.YRepeat / 128f)), l.Color, l.Radius, l.Energy * level * Flicker(l.Flicker, l.Seed), eye, noShadow: l.NoShadow, rooms: RoomsOf(l));
            }
        }
    }

    /// <summary>The render layers a map light reaches (once): not the rooms sharing the place of its room (its sprite's
    /// sector; a point light's as the map gives it while that holds its place, else the sector holding it).</summary>
    uint RoomsOf(MapLight l)
    {
        if (l.RoomsSet)
            return l.Rooms;
        l.RoomsSet = true;
        var map = _world.Map;
        if (map == null || _roomColours == null)
            return l.Rooms = 0xFFFFFFFFu;
        int sec;
        if (l.Sprite != null)
            sec = _world.SpriteBSector(l.Sprite);
        else if (l.BSector >= 0 && l.BSector < map.SectorCount && map.InSector(l.BSector, l.Fixed.X, l.Fixed.Y))
            sec = l.BSector;
        else
            sec = map.SectorAt(l.Fixed);
        return l.Rooms = RoomMaskOf(sec);
    }

    /// <summary>What keeps a light's slot from frame to frame: its sprite (a .MAP's: one light a sprite), or the light
    /// itself (a .MAPX may give a sprite more than one).</summary>
    static object KeyOf(MapLight l, Sprite s) => l.Placed ? l : s;

    /// <summary>An attached light's place: its kind's spot, kept in the sprite's room (KeepInRoom); a .MAPX's moved by
    /// its offset (one moved off the spot: where the mapper put it, not kept in).</summary>
    Vector3 Placed(MapLight l, Sprite s, Vector3 spot) =>
        l.Placed && l.Offset != Vector3.Zero ? spot + l.Offset : KeepInRoom(l, s, spot);

    /// <summary>Where a torch's or candle's light is: at its flame, the top of its picture as the sprite renderer places it (a
    /// height fixed over its middle had a candle's light glowing from its base).</summary>
    Vector3 FlameSpot(Sprite s, Vector3 pos, MapLight l)
    {
        bool torch = s.ClassName == "decoration_torch";
        int pic = torch && Tiles.Valid(s.Picnum + 1) ? s.Picnum + 1 : s.Picnum;
        if (pic <= 0 || !Tiles.Valid(pic))
            return pos + new Vector3(0, 0, l.ZOffset);
        pos.Z += (Tiles.H(pic) / 2 + Tiles.YOfs(pic) + s.YOffset) * s.YRepeat / 64f + (torch ? 4f : 0f);
        return pos;
    }

    // (a flame's light flickers in strength only and stays where it is: one moving a few units as it flickered made Godot
    // redraw its shadow map every frame, a cube's six faces with everything in its reach: Eviction's street fire, report
    // 20261117-113226; a light's strength changing redraws nothing)

    /// <summary>A sprite's light kept in the sprite's room: where it sits on the picture can be past the wall the sprite hangs
    /// on or the ceiling, and with shadows from both sides of a wall a light there lights nothing of its room.</summary>
    Vector3 KeepInRoom(MapLight l, Sprite s, Vector3 p)
    {
        const float Margin = 4, Reach = 48;
        var map = _world.Map;
        int b = map != null ? _world.SpriteBSector(s) : -1;
        if (b < 0)
            return p;
        var xy = new Vector2(p.X, p.Y);
        if (xy != l.KeptFrom)
        {
            l.KeptFrom = xy;
            l.Kept = xy;
            if (!map.InSector(b, xy.X, xy.Y) && map.NearestInside(b, xy, 2 * Margin, Reach, out var inside))
                l.Kept = inside;
        }
        float lo = map.FloorZ(b, l.Kept.X, l.Kept.Y), hi = map.CeilZ(b, l.Kept.X, l.Kept.Y);
        float z = hi - lo > 2 * Margin ? Mathf.Clamp(p.Z, lo + Margin, hi - Margin) : p.Z;
        return new Vector3(l.Kept.X, l.Kept.Y, z);
    }

    bool TooFar(MapLight l, Vector3 at, Vector3 eye)
    {
        if (at.DistanceSquaredTo(eye) <= _farCull)
            return false;
        float reach = l.Radius * Reach;
        if (l.LinksAt != at || l.LinksVersion != _regionsVersion || l.LinksReach != reach)
        {
            l.LinksAt = at;
            l.LinksVersion = _regionsVersion;
            l.LinksReach = reach;
            l.LinkOffsets = OffsetsThrough(at, reach);
        }
        foreach (var off in l.LinkOffsets)
            if ((at + off).DistanceSquaredTo(eye) <= _farCull)
                return false;
        return true;
    }

    // the offsets of the portal links a light at this place can reach through: ThroughPortals' test, with a margin for
    // where on its sprite it sits
    Vector3[] OffsetsThrough(Vector3 at, float radius)
    {
        const float Margin = 48;
        List<Vector3> found = null;
        foreach (var kv in _regionOf)
        {
            var link = kv.Key;
            var b = link.Bounds;
            float above = (at.Z - link.Plane) * link.Dir;
            if (above <= -GapBand - Margin || above >= radius + Margin)
                continue;
            float up = Mathf.Max(above, 0);
            float dx = Mathf.Max(0, Mathf.Max(b.Position.X - at.X, at.X - b.End.X));
            float dy = Mathf.Max(0, Mathf.Max(b.Position.Y - at.Y, at.Y - b.End.Y));
            if (dx * dx + dy * dy + up * up < (radius + Margin) * (radius + Margin))
                (found ??= new List<Vector3>()).Add(link.Offset);
        }
        return found == null ? System.Array.Empty<Vector3>() : found.ToArray();
    }

    float FxLevel(MapLight l, Vector3 at)
    {
        // (a frame drawn before the first game frame finds no sector yet: not kept, or a light switched off stays on)
        if (!_world.FxReady)
            return 1;
        if (l.FxAt != at || (l.FxSec != null && l.FxSec.IsMover))
        {
            l.FxAt = at;
            l.FxSec = _world.FxLightSector(at);
        }
        return World.FxLevelOf(l.FxSec);
    }

    /// <summary>One region per portal side (up to eight): the linked room a light on that side sends its copy to, the
    /// other side's footprint (a little wider, for the walls on its edge) beyond its plane.</summary>
    void SetupRegions()
    {
        _regionOf.Clear();
        _regionsVersion++;
        var planes = new float[MaxRegions];
        for (int i = 0; i < MaxRegions; i++)
            RenderingServer.GlobalShaderParameterSet("light_region_" + i, new Vector4(1, 1, -1, -1));     // empty
        foreach (var l in _world.Collision.Links)
        {
            if (!l.Portal || _regionOf.Count >= MaxRegions)
                continue;
            int i = _regionOf.Count;
            _regionOf[l] = i;
            const float Margin = 16;
            var b = l.Bounds;
            float x0 = b.Position.X + l.Offset.X - Margin, x1 = b.End.X + l.Offset.X + Margin;
            float y0 = b.Position.Y + l.Offset.Y - Margin, y1 = b.End.Y + l.Offset.Y + Margin;
            // Godot x = Quake y, Godot z = Quake x
            RenderingServer.GlobalShaderParameterSet("light_region_" + i, new Vector4(y0, x0, y1, x1) / Coords.QuakeScale);
            // the linked room lies beyond the other side's plane, away from this side's room
            planes[i] = -l.Dir * (1000 + (l.Plane + l.Offset.Z) / Coords.QuakeScale);
        }
        RenderingServer.GlobalShaderParameterSet("light_region_planes_0", new Vector4(planes[0], planes[1], planes[2], planes[3]));
        RenderingServer.GlobalShaderParameterSet("light_region_planes_1", new Vector4(planes[4], planes[5], planes[6], planes[7]));
    }

    static Color Rgb(int r, int g, int b) => Color.Color8((byte)r, (byte)g, (byte)b);

    void Add(object key, Vector3 pos, Color c, float radius, float energy, Vector3 eye, bool fixture = false, bool noShadow = false,
        uint rooms = 0xFFFFFFFFu)
    {
        // a sprite of a stacked room hidden from the camera's side lights nothing there either (E2M5's torch in 136,
        // whose floor lies below the ceiling of corridor 694 and room 691 under it: its light glowed on their ceiling);
        // a .MAPX's light on a sprite is keyed by itself, its sprite the one hidden
        var owner = key as Sprite ?? (key as MapLight)?.Sprite;
        if (owner != null && StackedRooms.SpriteHidden(owner, owner.Origin))
            return;
        _sources.Add(new Source { Key = key, Pos = pos, Color = c, Radius = radius * Reach, Energy = energy, Dist = pos.DistanceSquaredTo(eye), Fixture = fixture, NoShadow = noShadow, Rooms = rooms });
    }

    /// <summary>The lights in use (debugging: the lights command).</summary>
    public IEnumerable<string> Describe()
    {
        foreach (var l in _pool)
            if (l.Visible)
                yield return $"light at {Coords.GodotToQuake(l.GlobalPosition)} range {l.OmniRange * Coords.QuakeScale:0} energy {l.LightEnergy:0.0} colour {l.LightColor} shadow {l.ShadowEnabled} ({l.ShadowOpacity:0.00}) region {l.LightSpecular:0}";
    }

    /// <summary>Copies of the lights near a portal side, moved into the linked room by the link's offset: a light in
    /// the room above a floor portal also lights the room below around the opening, and the other way round; only
    /// lights whose reach covers part of the opening.</summary>
    const float GapBand = 24;      // Quake units below a link plane that still count as its room

    void ThroughPortals(Vector3 eye)
    {
        int n = _sources.Count;
        foreach (var l in _world.Collision.Links)
        {
            if (!l.Portal || !_regionOf.TryGetValue(l, out int region))
                continue;
            var b = l.Bounds;
            for (int i = 0; i < n; i++)
            {
                var s = _sources[i];
                // on this side's room: above a floor side (dir 1), below a ceiling side (dir -1); a light in the band
                // between a room's ceiling plane and the linked room's floor plane (E1M1's yard: 32 and 48) counts on
                // both sides, so nothing falls between the two
                float above = (s.Pos.Z - l.Plane) * l.Dir;
                if (above <= -GapBand || above >= s.Radius)
                    continue;
                // within reach of the opening
                float up = Mathf.Max(above, 0);
                float dx = Mathf.Max(0, Mathf.Max(b.Position.X - s.Pos.X, s.Pos.X - b.End.X));
                float dy = Mathf.Max(0, Mathf.Max(b.Position.Y - s.Pos.Y, s.Pos.Y - b.End.Y));
                float reach = Mathf.Sqrt(dx * dx + dy * dy + up * up);
                if (reach >= s.Radius)
                    continue;
                var c = s;
                c.Pos = s.Pos + l.Offset;
                c.NoShadow = true;
                // (in the linked room, kept to its region: the rooms sharing the light's own place mean nothing there)
                c.Rooms = 0xFFFFFFFFu;
                c.Region = 1 + region;
                c.Sub = 1 + region;
                c.Dist = c.Pos.DistanceSquaredTo(eye);
                _sources.Add(c);
                // the light itself stays out of the linked room (from its own side it would reach it at the wrong
                // place); one in the band between the planes lights both
                if (above > 0 && s.Region == 0)
                {
                    s.Region = 9 + region;
                    _sources[i] = s;
                }
            }
        }
    }

    /// <summary>A smooth flicker about 1, from value noise at a flame's pace, its own per seed (r_lightflicker).</summary>
    float Flicker(float amount, int seed)
    {
        amount *= Mathf.Clamp(FlickerAmount.Float, 0f, 2f);
        if (amount <= 0)
            return 1;
        return Mathf.Max(0f, 1 + amount * (0.5f - 0.65f * Noise(_now * 6, seed) - 0.35f * Noise(_now * 15, seed + 101)) * 1.6f);
    }

    static float Noise(double t, int seed)
    {
        long i = (long)System.Math.Floor(t);
        float f = (float)(t - i);
        f = f * f * (3 - 2 * f);
        float a = Hash(i, seed), b = Hash(i + 1, seed);
        return a + (b - a) * f;
    }

    static float Hash(long i, int seed)
    {
        uint h = unchecked((uint)(i * 374761393L + seed * 668265263L));
        h = unchecked((h ^ (h >> 13)) * 1274126177u);
        return (h ^ (h >> 16)) / (float)uint.MaxValue;
    }

    // (the light fixtures by tile, their colour, reach, energy and place on the picture: the converter's
    // FIXTURE_LIGHTS, written into each fixture's light entity)

    /// <summary>The tile a sprite is drawn with: a switch's on/off frame (SpriteRenderer.AnimSel) for unrotated tiles.</summary>
    static int DrawnTile(Sprite s) => Tiles.View(s.Picnum) == 0 ? s.Picnum + SpriteRenderer.AnimSel(s) : s.Picnum;

    /// <summary>Where a fixture's light sits: on a face sprite's picture (its centre, as the sprite renderer places it,
    /// moved by up half-heights), a few units off a wall or floor sprite on its drawn side (a two-sided one: the side
    /// the camera is on), so it isn't inside the surface the fixture hangs on.</summary>
    static Vector3 FixtureSpot(Sprite s, Vector3 pos, int pic, float up, Vector3 eye)
    {
        const float Off = 8;
        int align = s.Cstat & Cstat.AlignMask;
        bool oneSided = (s.Cstat & Cstat.OneSided) != 0;
        if (align == Cstat.Floor)
        {
            // a one-sided floor sprite faces up, or down when y-flipped
            bool above = oneSided ? (s.Cstat & Cstat.YFlip) == 0 : eye.Z >= pos.Z;
            return pos + new Vector3(0, 0, above ? Off : -Off);
        }
        float half = Tiles.H(pic) * s.YRepeat / 128f;
        pos.Z += (Tiles.YOfs(pic) + s.YOffset) * s.YRepeat / 64f + up * half;
        if (align == Cstat.Wall)
        {
            float a = BuildMath.AngToRad(s.Ang - 512);
            var fwd = new Vector3(-Mathf.Sin(a), -Mathf.Cos(a), 0);
            pos += (oneSided || (eye - pos).Dot(fwd) >= 0 ? fwd : -fwd) * Off;
        }
        return pos;
    }

    /// <summary>Each drawn frame: the sources this frame, the nearest get the lights.</summary>
    public void Draw(Player player, Vector3 eye, float lerp)
    {
        bool on = Enabled.Bool && !Suspended && _world != null;
        _sources.Clear();
        if (on)
        {
            Collect(player, eye, lerp);
            for (int i = 0; i < _testLights.Count; i++)
            {
                Add(TestKey, _testLights[i], Rgb(255, 240, 220), 300, 2f, eye);
                var t = _sources[^1];
                t.Sub = -1 - i;
                _sources[^1] = t;
            }
            ThroughPortals(eye);
        }
        _sources.Sort((a, b) => a.Dist.CompareTo(b.Dist));
        int n = Mathf.Min(_sources.Count, _pool.Count);
        // next frame's map lights: well past the first left out now (the eye moves a few units a frame; after a warp with
        // too few found, all of them once more)
        if (_sources.Count > _pool.Count)
        {
            float far = Mathf.Sqrt(_sources[_pool.Count].Dist) * 1.25f + 192;
            _farCull = far * far;
        }
        else
            _farCull = float.MaxValue;
        // more sources than lights: the farthest in use fade out towards the nearest left out, so one that changes
        // places with it does so at zero energy instead of popping
        float cut = _sources.Count > _pool.Count ? Mathf.Sqrt(_sources[_pool.Count].Dist) : float.MaxValue;

        // a source keeps last frame's light; the others take the free ones
        System.Array.Fill(_poolKeys, default);
        _assignedNext.Clear();
        var slotOf = _slotOf;
        for (int i = 0; i < n; i++)
        {
            var key = (_sources[i].Key, _sources[i].Sub);
            slotOf[i] = -1;
            if (_sources[i].Key != null && _assigned.TryGetValue(key, out int slot) && _poolKeys[slot].Item1 == null)
            {
                slotOf[i] = slot;
                _poolKeys[slot] = key;
            }
        }
        int free = 0;
        for (int i = 0; i < n; i++)
        {
            if (slotOf[i] < 0)
            {
                while (_poolKeys[free].Item1 != null)
                    free++;
                slotOf[i] = free;
                _poolKeys[free] = (_sources[i].Key ?? TestKey, _sources[i].Sub);
            }
            if (_sources[i].Key != null)
                _assignedNext[(_sources[i].Key, _sources[i].Sub)] = slotOf[i];
        }
        _assigned.Clear();
        foreach (var kv in _assignedNext)
            _assigned[kv.Key] = kv.Value;

        bool shadows = Shadows.Bool;
        ulong now = Time.GetTicksMsec();
        float dt = _shadowTime == 0 ? 1f : Mathf.Min((now - _shadowTime) / 1000f, 1f);
        _shadowTime = now;
        PickShadows(n, slotOf, shadows);
        var used = _used;
        System.Array.Clear(used);
        for (int i = 0; i < n; i++)
        {
            var s = _sources[i];
            var l = _pool[slotOf[i]];
            ref var was = ref _sent[slotOf[i]];
            used[slotOf[i]] = true;
            // only what changed: setting a light's range, shadow or caster mask (even to the same value) or moving it
            // makes Godot redraw its shadow map, and a still light's map is otherwise kept from frame to frame
            if (!was.Visible)
            {
                l.Visible = true;
                was.Visible = true;
            }
            var pos = Coords.QuakeToGodot(s.Pos);
            if (was.Pos != pos)
            {
                l.Position = pos;
                was.Pos = pos;
            }
            if (was.Color != s.Color)
            {
                l.LightColor = s.Color;
                was.Color = s.Color;
            }
            float energy = s.Energy * EnergyScale * Mathf.Clamp((cut - Mathf.Sqrt(s.Dist)) / (0.25f * cut), 0, 1);
            if (was.Energy != energy)
            {
                l.LightEnergy = energy;
                was.Energy = energy;
            }
            float range = s.Radius / Coords.QuakeScale;
            if (was.Range != range)
            {
                l.OmniRange = range;
                was.Range = range;
            }
            if (was.Specular != s.Region)
            {
                l.LightSpecular = s.Region;
                was.Specular = s.Region;
            }
            // the fog's glow: not a link's copy, kept to its region by the shaders of what it lights but not in the fog, and
            // with no shadow: it glowed through the walls (E1M1's chapel, a yard torch's copy); nor a light kept to its
            // room where rooms share a place (the fog and the ground mist know no render layers: it glowed in the other
            // room's mist)
            float fog = s.Region is >= 1 and <= 8 || s.Rooms != 0xFFFFFFFFu ? 0f : _fogGlow;
            if (was.Fog != fog)
            {
                l.LightVolumetricFogEnergy = fog;
                was.Fog = fog;
            }
            // (a slot taken by another light starts its shadow from nothing)
            int sl = slotOf[i];
            var skey = (s.Key ?? TestKey, s.Sub);
            if (!_shadowKey[sl].Equals(skey))
            {
                _shadowKey[sl] = skey;
                _shadowFade[sl] = 0;
            }
            _shadowFade[sl] = Mathf.MoveToward(_shadowFade[sl], _shadowPick[i] ? 1f : 0f, dt / ShadowSwap);
            bool shadow = _shadowFade[sl] > 0;
            if (was.Shadow != shadow)
            {
                l.ShadowEnabled = shadow;
                was.Shadow = shadow;
            }
            if (shadow && was.ShadowOpacity != _shadowFade[sl])
            {
                l.ShadowOpacity = _shadowFade[sl];
                was.ShadowOpacity = _shadowFade[sl];
            }
            // a fixture's light neither lights nor is shadowed by the fixtures' models: it sits a few units behind the
            // lantern's glass, so the pane facing it burned bright and the next stayed dull (E1M1's lamp posts; NBlood
            // draws the lantern evenly). Other lights (the moon, weapons, explosions) still light them.
            // and a light in a room sharing its place with others (impossible space, rooms stacked in height, a .MAPX's
            // layers) neither lights nor is shadowed by them (RoomsOf)
            uint mask = (s.Fixture ? ~FixtureLayer : 0xFFFFFFFFu) & s.Rooms;
            if (was.Mask != mask)
            {
                l.ShadowCasterMask = mask;
                l.LightCullMask = mask;
                was.Mask = mask;
            }
            // a moving light (explosion, projectile, muzzle flash) redraws its shadow map every frame: two passes
            // (dual paraboloid) instead of a cube's six; still fixtures keep the cleaner cube, drawn once
            var mode = s.Fixture ? OmniLight3D.ShadowMode.Cube : OmniLight3D.ShadowMode.DualParaboloid;
            if (was.Mode != (int)mode)
            {
                l.OmniShadowMode = mode;
                was.Mode = (int)mode;
            }
        }
        for (int i = 0; i < _pool.Count; i++)
            if (!used[i] && _sent[i].Visible)
            {
                _pool[i].Visible = false;
                _sent[i].Visible = false;
                _shadowKey[i] = default;
                _shadowFade[i] = 0;
            }
    }

    // which of the n sources in use cast shadows this frame (_shadowPick): the r_shadowlights most important, a light
    // casting now counting ShadowKeep times as much
    void PickShadows(int n, int[] slotOf, bool shadows)
    {
        int max = shadows ? Mathf.Clamp(ShadowLights.Int, 0, PoolSize) : 0;
        int c = 0;
        for (int i = 0; i < n; i++)
        {
            _shadowPick[i] = false;
            var s = _sources[i];
            if (s.NoShadow || max == 0)
                continue;
            // what it adds to the view: energy and reach (its lit area) over the squared distance from the eye, no nearer
            // than half its reach (a light around the camera is as important as one beside it)
            float near = s.Radius * 0.5f;
            float score = s.Energy * s.Radius * s.Radius / Mathf.Max(s.Dist, near * near);
            int sl = slotOf[i];
            if (_shadowFade[sl] > 0 && _shadowKey[sl].Equals((s.Key ?? TestKey, s.Sub)))
                score *= ShadowKeep;
            _shadowScore[i] = score;
            _shadowOrder[c++] = i;
        }
        if (c <= max)
        {
            for (int k = 0; k < c; k++)
                _shadowPick[_shadowOrder[k]] = true;
            return;
        }
        System.Array.Sort(_shadowOrder, 0, c, Comparer<int>.Create((a, b) => _shadowScore[b].CompareTo(_shadowScore[a])));
        for (int k = 0; k < max; k++)
            _shadowPick[_shadowOrder[k]] = true;
    }

    // a napalm ball's burst: a wide area lit at once, dying away slowly, long after the explosion's own picture
    const float BurstRadius = 1100f, BurstEnergy = 3.5f, BurstRise = 0.05f, BurstFall = 1.8f;
    const int MaxBursts = 3;
    readonly List<Burst> _bursts = new();

    sealed class Burst
    {
        public Vector3 Pos;
        public double T0;
    }

    public void AddBurst(Vector3 pos)
    {
        // (a ball's pieces burst side by side: one light, or they'd sum up and take the pool)
        foreach (var b in _bursts)
            if (_now - b.T0 < 0.3 && b.Pos.DistanceSquaredTo(pos) < 256f * 256f)
                return;
        if (_bursts.Count >= MaxBursts)
            _bursts.RemoveAt(0);
        _bursts.Add(new Burst { Pos = pos, T0 = _now });
    }

    void CollectBursts(Vector3 eye)
    {
        for (int i = _bursts.Count - 1; i >= 0; i--)
        {
            var b = _bursts[i];
            float t = (float)(_now - b.T0);
            if (t >= BurstRise + BurstFall)
            {
                _bursts.RemoveAt(i);
                continue;
            }
            float rest = 1f - Mathf.Max(t - BurstRise, 0f) / BurstFall;
            float f = t < BurstRise ? t / BurstRise : rest * rest;
            int before = _sources.Count;
            Add(b, b.Pos, Rgb(255, 170, 80), BurstRadius, BurstEnergy * f, eye);
            // (it lights more than any lamp: among the nearest, far as it may be)
            if (_sources.Count > before)
            {
                var s = _sources[before];
                s.Dist *= 0.01f;
                _sources[before] = s;
            }
        }
    }

    void Collect(Player player, Vector3 eye, float lerp)
    {
        _now = Time.GetTicksMsec() / 1000.0;
        _seen.Clear();
        foreach (var s in _world.Sprites)
        {
            if (s.Removed || s == player)
                continue;
            var pos = s.PrevOrigin.Lerp(s.Origin, lerp);
            int seed = s.Edict;
            if (s.StatNum == K.kStatExplosion)
            {
                // an explosion flashes and fades over half a second
                _seen.Add(s);
                if (!_born.TryGetValue(s, out double t0))
                {
                    _born[s] = t0 = _now;
                    if (s.Type == K.kExplosionNapalm)
                        AddBurst(pos + new Vector3(0, 0, 16));
                }
                if (s.Type == K.kExplosionNapalm)
                    continue;
                float f = 1 - (float)((_now - t0) / 0.6);
                if (f > 0)
                    Add(s, pos + new Vector3(0, 0, 16), Rgb(255, 170, 80), 320 * Mathf.Max(s.XRepeat, 32) / 64f, 3f * f, eye);
                continue;
            }
            if (s.StatNum == K.kStatProjectile)
            {
                switch (s.Type)
                {
                    case K.kMissileFlareRegular:
                    case K.kMissileFlareAlt:
                        Add(s, pos, Rgb(255, 80, 40), 220, 2f * Flicker(0.2f, seed), eye);
                        break;
                    case K.kMissileTeslaRegular:
                    case K.kMissileTeslaAlt:
                        Add(s, pos, Rgb(120, 160, 255), 200, 2f, eye);
                        break;
                    case K.kMissileFireball:
                    case K.kMissileFireballNapalm:
                    case K.kMissileFireballCerberus:
                    case K.kMissileFireballTchernobog:
                    case K.kMissileFlameSpray:
                    case K.kMissileFlameHound:
                        Add(s, pos, Rgb(255, 140, 50), 200, 2f * Flicker(0.2f, seed), eye);
                        break;
                    case K.kMissileLifeLeechRegular:
                        // (the staff's ball is fire, not green)
                        Add(s, pos, Rgb(255, 140, 50), 160, 1.5f * Flicker(0.2f, seed), eye);
                        break;
                }
                continue;
            }
            if (s.BurnTime > 0 && s.Visible)
                Add(s, pos + new Vector3(0, 0, 24), Rgb(255, 130, 40), 180, 1.6f * Flicker(0.3f, seed), eye);
        }
        CollectBursts(eye);
        // torches, candles, light fixtures and fullbright decorations: the map's light entities
        CollectMapLights(player, eye, lerp);
        // explosions that are gone stop fading
        if (_born.Count > _seen.Count)
        {
            var gone = new List<Sprite>();
            foreach (var k in _born.Keys)
                if (!_seen.Contains(k))
                    gone.Add(k);
            foreach (var k in gone)
                _born.Remove(k);
        }
        // the muzzle flash of the player's weapon (Blood's flashEffect, set while a shot is fired)
        if (player != null && player.FlashEffect > 0)
        {
            float a = Mathf.DegToRad((float)Blood.AngToYaw(player.Ang));
            var fwd = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0);
            Add(PlayerFlash, eye + fwd * 24, Rgb(255, 210, 150), 260, 2.5f, eye);
        }
    }
}
