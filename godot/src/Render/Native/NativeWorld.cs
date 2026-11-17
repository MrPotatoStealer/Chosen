using System;
using System.Collections.Generic;
using System.IO;
using Chosen.Sim.Build;
using Godot;

namespace Chosen.Render.Native;

/// <summary>
/// The level's sectors drawn from its Build data as they are now (Board; WorldMesher's faces, WorldShader). Sectors
/// that keep their shape are built once into a mesh per 512-unit square of the map; a sector whose wall points move
/// (TranslateSector, DragPoint) leaves its square for a mesh of its own, built again each drawn frame while it moves
/// (between the last game frame's points and these) and once more where it stops. Heights need no rebuild: the planes go
/// to b3d_planes each game frame (this frame's and the last's), and the shader puts every vertex on its plane; the
/// panning and light-effect shades likewise to b3d_sector and b3d_wallpan. Pictures are Blood's tiles (Data.TileArt: opaque
/// for opaque surfaces, masked with index 255 clear), animated as Build animates them; a surface's palette is
/// applied in the shader (BuildPalette).
/// </summary>
public partial class NativeWorld : Node3D
{
    Board _b;
    WorldMesher _mesher;
    const double ChunkBuild = 8192;      // a square's side, Build units (512 Quake units)

    sealed class Chunk
    {
        public MeshInstance3D Node;
        public ArrayMesh Mesh;
        public readonly List<int> Sectors = new();
        public bool Dirty;
    }

    readonly Dictionary<(int, int, int), Chunk> _chunks = new();
    Chunk[] _chunkOf = Array.Empty<Chunk>();

    sealed class Moving
    {
        public MeshInstance3D Node;
        public ArrayMesh Mesh;
        public int Version = -1;         // the shape version built
        public bool Blending;            // built while it moved: to build again when it has stopped
        public bool EachDraw;            // built for this drawn frame's blend, not for the shader's
        public float Lerp = 1f;          // where between the game frames it was built (moving otherwise than rigidly)
        public int Frame = -1;
        public double EmitMs, UploadMs;  // the last build's times (movingmesh)
        public int Builds;               // builds since the level began
        public readonly WorldMesher.Layout Layout = new();   // its surfaces as last uploaded (updated in place while they stay)
        public bool ShadowStill;         // its moves change no shadow: Shadow casts for it (UpdateShadowStill)
        public bool Candidate;
        public MeshInstance3D Shadow;    // shadows only: it as it was when the still ones were last built together
        public ArrayMesh ShadowMesh;
    }

    readonly Dictionary<int, Moving> _moving = new();
    readonly Dictionary<(int, SurfaceKind), ShaderMaterial> _materials = new();
    // an animated picture: its frames ready and one texture showing them. Its pixels change, not the material: each new texture
    // for a material made Godot rebuild all that uses it, 7.5 times a second for water: slow frames in a steady rhythm wherever
    // water portals are on (CP01's dock: 33 ms in a 5 ms game). Live null: frames of different sizes, set on the material.
    sealed class AnimatedPicture
    {
        public ShaderMaterial Mat;
        public int Tile, Shown = -1;
        public SurfaceKind Kind;
        public ImageTexture Live;
        public Dictionary<int, Image> Frames;
    }
    readonly List<AnimatedPicture> _animated = new();
    static Shader _opaque, _masked, _translucent, _water, _waterMarch, _portalFace, _wallPortal;

    /// <summary>Is a sector's floor (false) or ceiling (true) a room-over-room portal's face (set before Setup)?</summary>
    public Func<int, bool, bool> IsPortal;
    /// <summary>Is a portal face's link one that doesn't move (WorldMesher.IsPortalInPlace; set before Setup)?</summary>
    public Func<int, bool, bool> IsPortalInPlace;
    /// <summary>Is a wall a wall portal's face (WorldMesher.IsWallPortal; set before Setup)?</summary>
    public Func<int, bool> IsWallPortal;
    /// <summary>Is a sector's floor or ceiling a see-through water surface (WorldMesher.IsWater; set before Setup)?</summary>
    public Func<int, bool, bool> IsWater;
    /// <summary>World.NativeSkyFlags (set before Setup).</summary>
    public byte[] SkyFlags;
    /// <summary>Do two sectors share the ground (WorldMesher.SharesGround; set before Setup)?</summary>
    public Func<int, int, bool> SharesGround;
    /// <summary>Each sector's room colour (Shared.Blood.Map.LightRules.RoomColours: rooms sharing a place told apart),
    /// its meshes' render layer (DynamicLights.RoomLayer), so a dynamic light in one room neither lights nor is shadowed
    /// by another sharing its place; null: all 0.</summary>
    public Func<int, int> RoomColour;

    /// <summary>See-through water's rooms under the surface: a sector whose ceiling is drawn at the floor of the sector over
    /// it plus dz (FollowWater).</summary>
    public readonly Dictionary<int, (int near, int dz)> CeilFollows = new();

    /// <summary>The links drawn as water portals (set before Setup): the room under a surface has its ceiling drawn with the
    /// surface. A surface bobs (E2M1's lake) or rises while the ceiling under it stays in the game; seen through it, the room's
    /// walls then stopped short of the surface and a sliver of nothing showed along the waterline. Only drawn so: Blood's
    /// heights stay as they are. Level surfaces that meet at rest only.</summary>
    public void FollowWater(IEnumerable<Sim.Collision.Link> links, Board b)
    {
        CeilFollows.Clear();
        foreach (var l in links)
        {
            if (!l.Portal || l.ConvertedPortal || l.Dir >= 0 || !b.Valid(l.Sector) || !b.Valid(l.FarSector))
                continue;
            var under = b.Sectors[l.Sector];
            var over = b.Sectors[l.FarSector];
            if ((under.CeilingStat & 2) != 0 || (over.FloorStat & 2) != 0)
                continue;
            // (linked room = this room + Offset, Quake z up)
            int dz = Mathf.RoundToInt(l.Offset.Z * 256f);
            if (Math.Abs(b.BaseFloor[l.FarSector] + dz - b.BaseCeil[l.Sector]) <= 256)
                CeilFollows.TryAdd(l.Sector, (l.FarSector, dz));
        }
    }

    /// <summary>How moving sectors are drawn between the game frames. 0 is the exact picture the motion test
    /// (bugs.py motion) holds the others to.</summary>
    public static readonly Core.Cvar MeshBlend = Core.Cvars.Register("r_meshblend", "1",
        "moving sectors between game frames: 1 blended in the shader where that holds, else built each drawn frame; 0 all built each drawn frame (exact); 2 all blended (wrong on purpose: bugs.py motion --selftest)");

    /// <summary>Counts up when the world's meshes change (a sector leaving its square for a mesh of its own): the
    /// portal copies take them again.</summary>
    public int NodesVersion { get; private set; }

    /// <summary>The world's meshes now (portal copies share them).</summary>
    public IEnumerable<MeshInstance3D> Nodes
    {
        get
        {
            foreach (var c in _chunks.Values)
                yield return c.Node;
            foreach (var m in _moving.Values)
                yield return m.Node;
        }
    }

    /// <summary>The Build sectors in a world mesh (the portal copies take only what a water surface can show).</summary>
    public bool NodeHasSector(MeshInstance3D node, HashSet<int> sectors)
    {
        foreach (var c in _chunks.Values)
            if (c.Node == node)
            {
                foreach (int s in c.Sectors)
                    if (sectors.Contains(s))
                        return true;
                return false;
            }
        foreach (var (s, m) in _moving)
            if (m.Node == node)
                return sectors.Contains(s);
        return true;
    }

    Image _planes, _planesPrev, _sector, _wallpan;
    ImageTexture _planesTex, _planesPrevTex, _sectorTex, _wallpanTex;
    int _rows, _sectorRows, _wallRows;

    /// <summary>The level's light effects and panning, read each frame (null: as the map stores them).</summary>
    public Func<int, Vector3> SectorShadeFx;
    public Func<int, Vector4> SectorPan;
    public Func<int, Vector2> WallPan;

    public int TriangleCount { get; private set; }

    public void Setup(Board b)
    {
        foreach (var c in GetChildren())
        {
            RemoveChild(c);     // (out of the tree at once: the new chunks keep their names)
            c.QueueFree();
        }
        _chunks.Clear();
        _moving.Clear();
        _materials.Clear();
        _animated.Clear();
        _b = b;
        if (b == null)
            return;
        ulong t0 = Time.GetTicksMsec();
        BuildPalette.Ensure();
        _mesher = new WorldMesher(b) { IsPortal = IsPortal, IsPortalInPlace = IsPortalInPlace, IsWallPortal = IsWallPortal, IsWater = IsWater, SharesGround = SharesGround };
        _jobMeshers.Clear();
        NodesVersion++;
        MakeData();
        WritePlanes(true);
        WriteSectorData();
        _chunkOf = new Chunk[b.SectorCount];
        for (int s = 0; s < b.SectorCount; s++)
        {
            var sec = b.Sectors[s];
            if (sec.WallNum < 2)
                continue;
            double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
            foreach (int w in b.SectorWalls(s))
            {
                x0 = Math.Min(x0, b.Walls[w].X); x1 = Math.Max(x1, b.Walls[w].X);
                y0 = Math.Min(y0, b.Walls[w].Y); y1 = Math.Max(y1, b.Walls[w].Y);
            }
            // (a chunk holds one room colour: its render layer is the lights' test)
            int colour = RoomColour?.Invoke(s) ?? 0;
            var key = ((int)Math.Floor((x0 + x1) / 2 / ChunkBuild), (int)Math.Floor((y0 + y1) / 2 / ChunkBuild), colour);
            if (!_chunks.TryGetValue(key, out var ch))
            {
                ch = new Chunk { Mesh = new ArrayMesh() };
                // (shadows from both sides: a wall faces its own room only, so one seen from behind stopped no light, and a
                // light in the next room lit this one through it: E1M1's torches beyond the chapel's wall)
                ch.Node = new MeshInstance3D
                {
                    Mesh = ch.Mesh, Name = colour == 0 ? $"World_{key.Item1}_{key.Item2}" : $"World_{key.Item1}_{key.Item2}_r{colour}",
                    CastShadow = GeometryInstance3D.ShadowCastingSetting.DoubleSided, Layers = DynamicLights.RoomLayer(colour),
                };
                ch.Node.SetInstanceShaderParameter("portal_copy", 0f);
                AddChild(ch.Node);
                _chunks[key] = ch;
            }
            ch.Sectors.Add(s);
            _chunkOf[s] = ch;
        }
        TriangleCount = 0;
        foreach (var ch in _chunks.Values)
            BuildChunk(ch);
        Core.GameConsole.Print($"native world: {b.SectorCount} sectors in {_chunks.Count} meshes, {TriangleCount} triangles, {_materials.Count} materials, {Time.GetTicksMsec() - t0} ms");
    }

    void BuildChunk(Chunk ch)
    {
        _mesher.Clear();
        _mesher.Blend = false;
        _mesher.Lerp = 1f;
        foreach (int s in ch.Sectors)
            if (!_moving.ContainsKey(s))
                _mesher.EmitSector(s);
        foreach (var bk in _mesher.Buckets.Values)
            TriangleCount += bk.Count / 3;
        _mesher.Upload(ch.Mesh, Material);
        ch.Dirty = false;
    }

    // ---------------------------------------------------------------- materials
    Material Material(int tile, SurfaceKind kind)
    {
        if (_materials.TryGetValue((tile, kind), out var m))
            return m;
        if (kind == SurfaceKind.PortalFace)
        {
            _portalFace ??= new Shader { Code = WorldShader.PortalFaceCode() };
            m = new ShaderMaterial { Shader = _portalFace };
            // (bucket 1: a link in place, WorldMesher.IsPortalInPlace)
            if (tile == 1)
                ((ShaderMaterial)m).SetShaderParameter("in_place", true);
            _materials[(tile, kind)] = m;
            return m;
        }
        if (kind == SurfaceKind.WallPortal)
        {
            _wallPortal ??= new Shader { Code = WorldShader.Code("cull_back", false, wallPortal: true) };
            var wm = new ShaderMaterial { Shader = _wallPortal };
            SetTexture(wm, tile, SurfaceKind.Masked);
            _materials[(tile, kind)] = wm;
            return wm;
        }
        if (kind == SurfaceKind.Sky)
        {
            var sky = NativeSky.MaterialFor(tile, _b);
            if (sky != null)
            {
                _materials[(tile, kind)] = sky;
                return sky;
            }
        }
        _opaque ??= new Shader { Code = WorldShader.Code("cull_back", false) };
        _masked ??= new Shader { Code = WorldShader.Code("cull_back", false) };
        _translucent ??= new Shader { Code = WorldShader.Code("cull_back, blend_mix, depth_draw_never", true) };
        // (water: its depth written, the fog's blur took the depth of the room seen through it, BuildShading.SetWater)
        _water ??= new Shader { Code = WorldShader.Code("cull_back, blend_mix, depth_draw_always", true, water: true) };
        _waterMarch ??= new Shader { Code = WorldShader.Code("cull_back, blend_mix, depth_draw_always", true, water: true, waterMarch: true) };
        m = new ShaderMaterial
        {
            Shader = kind switch
            {
                SurfaceKind.Translucent => _translucent, SurfaceKind.Water => WaterFx.Reflect.Bool ? _waterMarch : _water, SurfaceKind.Masked => _masked, _ => _opaque,
            },
        };
        if (kind is SurfaceKind.Translucent or SurfaceKind.Water)
            m.RenderPriority = 1;
        SetTexture(m, tile, kind);
        _materials[(tile, kind)] = m;
        if (Data.Tiles.AnimNum(tile) > 0 && Data.Tiles.AnimType(tile) != 0 || tile == ProceduralFire.Tile)
            _animated.Add(MakeAnimated(m, tile, kind));
        return m;
    }

    AnimatedPicture MakeAnimated(ShaderMaterial m, int tile, SurfaceKind kind)
    {
        var a = new AnimatedPicture { Mat = m, Tile = tile, Kind = kind };
        if (tile == ProceduralFire.Tile)
            return a;
        int num = Data.Tiles.AnimNum(tile);
        var frames = new Dictionary<int, Image>();
        Image first = null;
        for (int k = Data.Tiles.AnimType(tile) == 3 ? -num : 0; k <= num; k++)
        {
            var tex = kind is SurfaceKind.Opaque or SurfaceKind.Water ? Opaque(tile + k) : Data.TileTextures.Get(tile + k);
            var img = tex?.GetImage();
            if (img == null || img.IsEmpty())
                return a;
            first ??= img;
            if (img.GetSize() != first.GetSize() || img.GetFormat() != first.GetFormat() || img.HasMipmaps() != first.HasMipmaps())
                return a;
            frames[tile + k] = img;
        }
        a.Frames = frames;
        a.Live = ImageTexture.CreateFromImage(frames[tile]);
        a.Shown = tile;
        m.SetShaderParameter(BuildShading.AlbedoUniform, a.Live);
        return a;
    }

    void SetTexture(ShaderMaterial m, int tile, SurfaceKind kind)
    {
        var tex = tile == ProceduralFire.Tile && ProceduralFire.Texture != null ? ProceduralFire.Texture
            : kind is SurfaceKind.Opaque or SurfaceKind.Water ? Opaque(tile) : Data.TileTextures.Get(tile);
        m.SetShaderParameter(BuildShading.AlbedoUniform, tex);
        int w = Data.Tiles.W(tile), h = Data.Tiles.H(tile);
        if (w <= 0) w = 64;
        if (h <= 0) h = 64;
        m.SetShaderParameter("tile_info", new Vector4(w, h, Pow2(w), Pow2(h)));
        m.SetShaderParameter("tile_glows", BuildShading.GlowTile(tile) ? 1f : 0f);
        int p2 = Pow2(h);
        m.SetShaderParameter("npot_wall", p2 != h ? new Vector2((float)p2 / h, 1f / w) : Vector2.Zero);
    }

    static int Pow2(int n)
    {
        int p = 1;
        while (p < n)
            p <<= 1;
        return p;
    }

    static readonly Dictionary<int, Texture2D> _opaqueTex = new();

    /// <summary>A tile's picture for an opaque surface (index 255 a colour, not clear).</summary>
    static Texture2D Opaque(int tile)
    {
        if (_opaqueTex.TryGetValue(tile, out var t))
            return t;
        var img = Data.TileArt.Picture(tile, false);
        if (img != null)
        {
            img.GenerateMipmaps();
            t = ImageTexture.CreateFromImage(img);
        }
        else
            t = Data.TileTextures.Get(tile);
        _opaqueTex[tile] = t;
        return t;
    }

    /// <summary>Forget the loaded pictures (an add-on's art in place of the base game's).</summary>
    public static void ClearTextures() => _opaqueTex.Clear();

    // ---------------------------------------------------------------- data textures
    // the data textures' texels (RGBA floats), written here and sent whole (a SetPixel per texel cost thousands of
    // engine calls a game frame)
    float[] _planesBuf, _planesPrevBuf, _sectorBuf, _wallpanBuf;
    readonly Dictionary<int, byte[]> _bytes = new();

    void MakeData()
    {
        int refs = 2 * _b.SectorCount + 2;
        _rows = Math.Max(1, (refs + WorldShader.DataWidth - 1) / WorldShader.DataWidth);
        _planes = Image.CreateEmpty(WorldShader.DataWidth, _rows, false, Image.Format.Rgbaf);
        _planesPrev = Image.CreateEmpty(WorldShader.DataWidth, _rows, false, Image.Format.Rgbaf);
        _sectorRows = Math.Max(1, (2 * _b.SectorCount + WorldShader.DataWidth - 1) / WorldShader.DataWidth);
        _sector = Image.CreateEmpty(WorldShader.DataWidth, _sectorRows, false, Image.Format.Rgbaf);
        _wallRows = Math.Max(1, (_b.WallCount + WorldShader.DataWidth - 1) / WorldShader.DataWidth);
        _wallpan = Image.CreateEmpty(WorldShader.DataWidth, _wallRows, false, Image.Format.Rgbaf);
        _planesBuf = new float[WorldShader.DataWidth * _rows * 4];
        _planesPrevBuf = new float[_planesBuf.Length];
        _sectorBuf = new float[WorldShader.DataWidth * _sectorRows * 4];
        _wallpanBuf = new float[WorldShader.DataWidth * _wallRows * 4];
        _planesTex = ImageTexture.CreateFromImage(_planes);
        _planesPrevTex = ImageTexture.CreateFromImage(_planesPrev);
        _sectorTex = ImageTexture.CreateFromImage(_sector);
        _wallpanTex = ImageTexture.CreateFromImage(_wallpan);
        RenderingServer.GlobalShaderParameterSet("b3d_planes", _planesTex);
        RenderingServer.GlobalShaderParameterSet("b3d_planes_prev", _planesPrevTex);
        RenderingServer.GlobalShaderParameterSet("b3d_sector", _sectorTex);
        RenderingServer.GlobalShaderParameterSet("b3d_wallpan", _wallpanTex);
        int seenRows = Math.Max(1, (_b.SectorCount + WorldShader.DataWidth - 1) / WorldShader.DataWidth);
        _seenBytes = new byte[2 * WorldShader.DataWidth * seenRows];
        Array.Fill(_seenBytes, (byte)255);
        _seen = Image.CreateFromData(WorldShader.DataWidth, seenRows, false, Image.Format.Rg8, _seenBytes);
        _seenTex = ImageTexture.CreateFromImage(_seen);
        _seenAll = true;
        RenderingServer.GlobalShaderParameterSet("b3d_seen", _seenTex);
        _windowBuf = new float[4 * WorldShader.DataWidth * seenRows];
        for (int i = 0; i < _windowBuf.Length; i += 4)
            (_windowBuf[i], _windowBuf[i + 1], _windowBuf[i + 2], _windowBuf[i + 3]) = (-10f, 10f, -1e6f, 1e6f);
        var wb = new byte[_windowBuf.Length * 4];
        Buffer.BlockCopy(_windowBuf, 0, wb, 0, wb.Length);
        _window = Image.CreateFromData(WorldShader.DataWidth, seenRows, false, Image.Format.Rgbaf, wb);
        _windowTex = ImageTexture.CreateFromImage(_window);
        RenderingServer.GlobalShaderParameterSet("b3d_window", _windowTex);
    }

    Image _window;
    ImageTexture _windowTex;
    float[] _windowBuf;

    Image _seen;
    ImageTexture _seenTex;
    byte[] _seenBytes;
    bool _seenAll;

    /// <summary>The water's shader again (r_waterreflect: the one reading the screen only with it on).</summary>
    public void RefreshWater()
    {
        foreach (var ((_, kind), m) in _materials)
            if (kind == SurfaceKind.Water)
                m.Shader = WaterFx.Reflect.Bool ? _waterMarch : _water;
    }

    /// <summary>r_skyfaces 0: the sectors the walk reached this frame, for the shaders (else all).</summary>
    public void SendSeen()
    {
        if (_b == null || _seen == null)
            return;
        bool all = BuildSky.Faces.Bool || SectorVisibility.All;
        if (all && _seenAll)
            return;
        _seenAll = all;
        for (int s = 0; s < _b.SectorCount; s++)
        {
            bool direct = SectorVisibility.Window(s, out float lo, out float hi, out float tlo, out float thi);
            _seenBytes[2 * s] = all || direct ? (byte)255 : (byte)0;
            _seenBytes[2 * s + 1] = all || SectorVisibility.Seen(s) ? (byte)255 : (byte)0;
            // (reached with all of the view: none, so the shader needn't work it out)
            bool whole = all || lo <= -Mathf.Pi && hi >= Mathf.Pi;
            _windowBuf[4 * s] = whole ? -10f : lo;
            _windowBuf[4 * s + 1] = whole ? 10f : hi;
            _windowBuf[4 * s + 2] = all ? -1e6f : Mathf.Max(tlo, -1e6f);
            _windowBuf[4 * s + 3] = all ? 1e6f : Mathf.Min(thi, 1e6f);
        }
        _seen.SetData(_seen.GetWidth(), _seen.GetHeight(), false, Image.Format.Rg8, _seenBytes);
        _seenTex.Update(_seen);
        Send(_window, _windowTex, _windowBuf);
        RenderingServer.GlobalShaderParameterSet("b3d_view_yaw", SectorVisibility.ViewYaw);
    }

    static void Set(float[] buf, int i, Color c)
    {
        buf[4 * i] = c.R;
        buf[4 * i + 1] = c.G;
        buf[4 * i + 2] = c.B;
        buf[4 * i + 3] = c.A;
    }

    void Send(Image img, ImageTexture tex, float[] buf)
    {
        int n = buf.Length * 4;
        if (!_bytes.TryGetValue(n, out var bytes))
            _bytes[n] = bytes = new byte[n];
        Buffer.BlockCopy(buf, 0, bytes, 0, n);
        img.SetData(img.GetWidth(), img.GetHeight(), false, Image.Format.Rgbaf, bytes);
        tex.Update(img);
    }

    /// <summary>A plane in Quake units: z = a x + b y + c over Quake x, y (Build's plane, x = 16 qx, y = -16 qy, z = -256 qz).</summary>
    /// The fourth value: the plane's own height (Build's floorz / ceilingz in Quake z), what a wall's picture is pegged to
    /// (a slope's height at the wall would skew it: E2M6's sloped lift floor 18).
    static Color QuakePlane((double a, double b, double c) p, int z) =>
        new((float)(-p.a * 16 / 256), (float)(p.b * 16 / 256), (float)(-p.c / 256), -z / 256f);

    /// <summary>The planes now into b3d_planes, as the last game frame began into b3d_planes_prev (both: the same now).
    /// From the board's snapshot, not the last one sent: a drawn frame two game frames on (a hitch) would blend two
    /// frames' motion of a floor over one, while the sprites on it and the walls blend one (E2M1's bobbing boat).</summary>
    void WritePlanes(bool both)
    {
        for (int s = 0; s < _b.SectorCount; s++)
        {
            Set(_planesBuf, 2 * s, QuakePlane(_b.Plane(s, false), _b.Sectors[s].FloorZ));
            Set(_planesBuf, 2 * s + 1, QuakePlane(_b.Plane(s, true), _b.Sectors[s].CeilingZ));
            if (!both)
            {
                Set(_planesPrevBuf, 2 * s, QuakePlane(_b.PrevPlane(s, false), _b.PrevFloorZ[s]));
                Set(_planesPrevBuf, 2 * s + 1, QuakePlane(_b.PrevPlane(s, true), _b.PrevCeilZ[s]));
            }
        }
        // (a wall's picture stays pegged to the ceiling's own height: only its edge follows the surface)
        foreach (var (s, f) in CeilFollows)
        {
            Set(_planesBuf, 2 * s + 1, QuakePlane((0, 0, _b.Sectors[f.near].FloorZ + f.dz), _b.Sectors[s].CeilingZ));
            if (!both)
                Set(_planesPrevBuf, 2 * s + 1, QuakePlane((0, 0, _b.PrevFloorZ[f.near] + f.dz), _b.PrevCeilZ[s]));
        }
        if (both)
            Array.Copy(_planesBuf, _planesPrevBuf, _planesBuf.Length);
        Send(_planes, _planesTex, _planesBuf);
        Send(_planesPrev, _planesPrevTex, _planesPrevBuf);
    }

    void WriteSectorData()
    {
        for (int s = 0; s < _b.SectorCount; s++)
        {
            var sec = _b.Sectors[s];
            var fx = SectorShadeFx?.Invoke(s) ?? Vector3.Zero;
            var pan = SectorPan?.Invoke(s) ?? new Vector4(sec.FloorXPanning, sec.FloorYPanning, sec.CeilingXPanning, sec.CeilingYPanning);
            // (w: under the sky, plus 2 standing over a sector that is)
            Set(_sectorBuf, 2 * s, new Color(fx.X, fx.Y, fx.Z, SkyFlags != null && s < SkyFlags.Length ? SkyFlags[s] : (sec.CeilingStat & 1) != 0 ? 1 : 0));
            Set(_sectorBuf, 2 * s + 1, new Color(pan.X, pan.Y, pan.Z, pan.W));
        }
        Send(_sector, _sectorTex, _sectorBuf);
        for (int w = 0; w < _b.WallCount; w++)
        {
            var p = WallPan?.Invoke(w) ?? new Vector2(_b.Walls[w].XPanning, _b.Walls[w].YPanning);
            Set(_wallpanBuf, w, new Color(p.X, p.Y, 0, 0));
        }
        Send(_wallpan, _wallpanTex, _wallpanBuf);
    }

    // ---------------------------------------------------------------- each frame
    int _frame = -1;

    /// <summary>A new game frame (frame: its number): the planes now and before, the moving sectors.</summary>
    public void GameFrame(int frame)
    {
        if (_b == null || frame == _frame)
            return;
        _frame = frame;
        WritePlanes(false);
        WriteSectorData();
        foreach (int s in _b.Reshaped)
            if (!_moving.ContainsKey(s))
                StartMoving(s);
        // a modern map's changers: a sector's pictures, palettes, shades or flags changed where it stands
        if (_b.Relooked.Count > 0)
        {
            foreach (int s in _b.Relooked)
                SectorLookChanged(s);
            _b.Relooked.Clear();
        }
    }

    /// <summary>What is drawn of a sector changed without it moving (Board.TouchLook): its mesh is built again.</summary>
    public void SectorLookChanged(int s)
    {
        if (_b == null || s < 0 || s >= _b.SectorCount)
            return;
        if (_moving.TryGetValue(s, out var mv))
            mv.Version = -1;
        else if (_chunkOf[s] is { } ch)
            ch.Dirty = true;
    }

    /// <summary>A wall's flags changed (a gib wall broken or whole again: its masked picture shown or not): the sectors on
    /// both its sides built again.</summary>
    public void WallChanged(int w)
    {
        if (_b == null || w < 0 || w >= _b.WallCount)
            return;
        foreach (int s in new[] { _b.Walls[w].Sector, _b.Walls[w].NextSector })
        {
            if (s < 0 || s >= _b.SectorCount)
                continue;
            if (_moving.TryGetValue(s, out var mv))
                mv.Version = -1;
            else if (_chunkOf[s] is { } ch)
                ch.Dirty = true;
        }
    }

    void StartMoving(int s)
    {
        var mv = new Moving { Mesh = new ArrayMesh() };
        // (its mesh's box is where it is now: a game frame's move back stays within the margin)
        mv.Node = new MeshInstance3D { Mesh = mv.Mesh, Name = $"Moving_{s}", ExtraCullMargin = 4f, CastShadow = GeometryInstance3D.ShadowCastingSetting.DoubleSided,
            Layers = DynamicLights.RoomLayer(RoomColour?.Invoke(s) ?? 0) };
        mv.Node.SetInstanceShaderParameter("portal_copy", 0f);
        AddChild(mv.Node);
        NodesVersion++;
        _moving[s] = mv;
        if (_chunkOf[s] is { } ch)
            ch.Dirty = true;
    }

    /// <summary>The moving sectors' meshes (debugging, movingmesh): each one's state and last build's times.</summary>
    public IEnumerable<string> DescribeMoving()
    {
        if (_b == null)
            yield break;
        foreach (var (s, mv) in _moving)
        {
            bool still = _b.Reshaped.Contains(s);
            bool rigid = still && _mesher.MovesRigidly(s);
            yield return $"moving {s}: {(still ? rigid ? "rigid" : "EACH DRAW" : "stopped")} seen {SectorVisibility.FacesSeen(s)} builds {mv.Builds} emit {mv.EmitMs:0.000} ms upload {mv.UploadMs:0.000} ms{(mv.ShadowStill ? " shadow still" : "")}";
        }
    }

    // ---------------------------------------------------------------- moving sectors whose moves change no shadow
    // A caster whose mesh changes makes Godot redraw the shadow map of every shadowed light around it, every frame it
    // changes: Eviction's turning sector 420 (type 617, flush with the room 1059 round it: same floor and ceiling, its
    // walls drawing nothing) and that room, rebuilt each drawn frame round it, made the street's eight shadowed lights
    // redraw every frame, 4000 of the 8700 draw calls (report 20261117-113226). Moving so, flush, the two together are the
    // same surfaces wherever it has turned to: their drawn meshes cast none, and a shadows-only copy of each, all the
    // still ones built at the same moment (their outlines meet), casts for them and changes only when that set does.
    int _stillFrame = -1;
    public static readonly Core.Cvar StillShadows = Core.Cvars.Register("r_stillshadows", "1",
        "1: a moving sector whose moves change no shadow (turning flush with the floor round it) casts from a still copy, so the lights near it keep their shadow maps");

    /// <summary>Did wall w of sector s move this game frame (either end)?</summary>
    bool WallMoved(int w)
    {
        var wl = _b.Walls[w];
        int p2 = wl.Point2;
        return wl.X != _b.PrevWallX[w] || wl.Y != _b.PrevWallY[w]
            || p2 >= 0 && p2 < _b.WallCount && (_b.Walls[p2].X != _b.PrevWallX[p2] || _b.Walls[p2].Y != _b.PrevWallY[p2]);
    }

    /// <summary>Is wall w of sector s flush: two-sided, nothing drawn on it (no masked or one-way picture, no portal), the
    /// sector beyond with the same level unsloped floor and ceiling of the same kind?</summary>
    bool Flush(int s, int w)
    {
        var wl = _b.Walls[w];
        int n = wl.NextSector;
        if (n < 0 || !_b.Valid(n) || (wl.Cstat & 48) != 0 || IsWallPortal?.Invoke(w) == true)
            return false;
        if (wl.NextWall >= 0 && wl.NextWall < _b.WallCount && ((_b.Walls[wl.NextWall].Cstat & 48) != 0 || IsWallPortal?.Invoke(wl.NextWall) == true))
            return false;
        var a = _b.Sectors[s];
        var b = _b.Sectors[n];
        if (a.FloorZ != b.FloorZ || a.CeilingZ != b.CeilingZ || ((a.FloorStat | b.FloorStat | a.CeilingStat | b.CeilingStat) & 2) != 0
            || ((a.FloorStat ^ b.FloorStat) & 1) != 0 || ((a.CeilingStat ^ b.CeilingStat) & 1) != 0)
            return false;
        if (CeilFollows.ContainsKey(s) || CeilFollows.ContainsKey(n))
            return false;
        foreach (bool ceiling in new[] { false, true })
            if ((IsPortal?.Invoke(s, ceiling) ?? false) != (IsPortal?.Invoke(n, ceiling) ?? false)
                || (IsWater?.Invoke(s, ceiling) ?? false) != (IsWater?.Invoke(n, ceiling) ?? false))
                return false;
        return true;
    }

    /// <summary>Did sector s move this game frame only so that no shadow changes: its planes still, every wall that moved
    /// flush?</summary>
    bool MovesFlush(int s)
    {
        var sec = _b.Sectors[s];
        if (sec.FloorZ != _b.PrevFloorZ[s] || sec.CeilingZ != _b.PrevCeilZ[s])
            return false;
        foreach (int w in _b.SectorWalls(s))
            if (WallMoved(w) && !Flush(s, w))
                return false;
        return true;
    }

    /// <summary>Once a game frame: which moving sectors are shadow still (sticky while they don't move; given up by a move
    /// that isn't flush, and by a neighbour across a wall that moved giving it up: its live mesh would leave a gap against
    /// the still copy). When that set changes, every still one's copy is built again, together.</summary>
    void UpdateShadowStill()
    {
        if (_frame == _stillFrame)
            return;
        _stillFrame = _frame;
        bool on = StillShadows.Bool;
        foreach (var (s, mv) in _moving)
            mv.Candidate = on && (_b.Reshaped.Contains(s) ? MovesFlush(s) : mv.ShadowStill);
        for (bool changed = true; changed;)
        {
            changed = false;
            foreach (int s in _b.Reshaped)
            {
                if (!_moving.TryGetValue(s, out var mv) || !mv.Candidate)
                    continue;
                foreach (int w in _b.SectorWalls(s))
                {
                    int n = _b.Walls[w].NextSector;
                    if (n >= 0 && WallMoved(w) && _moving.TryGetValue(n, out var nm) && !nm.Candidate)
                    {
                        mv.Candidate = false;
                        changed = true;
                        break;
                    }
                }
            }
        }
        bool rebuild = false;
        foreach (var mv in _moving.Values)
        {
            if (mv.Candidate == mv.ShadowStill)
                continue;
            mv.ShadowStill = mv.Candidate;
            mv.Node.CastShadow = mv.ShadowStill ? GeometryInstance3D.ShadowCastingSetting.Off : GeometryInstance3D.ShadowCastingSetting.DoubleSided;
            rebuild = true;
        }
        if (!rebuild)
            return;
        foreach (var (s, mv) in _moving)
        {
            if (!mv.ShadowStill)
            {
                if (mv.Shadow != null)
                    mv.Shadow.Visible = false;
                continue;
            }
            if (mv.Shadow == null)
            {
                mv.ShadowMesh = new ArrayMesh();
                mv.Shadow = new MeshInstance3D { Mesh = mv.ShadowMesh, Name = $"MovingShadow_{s}", CastShadow = GeometryInstance3D.ShadowCastingSetting.ShadowsOnly, Layers = mv.Node.Layers };
                mv.Shadow.SetInstanceShaderParameter("portal_copy", 0f);
                AddChild(mv.Shadow);
            }
            _mesher.Clear();
            _mesher.Blend = false;
            _mesher.Lerp = 1f;
            _mesher.EmitSector(s);
            _mesher.Upload(mv.ShadowMesh, Material);
            mv.Shadow.Visible = true;
        }
    }

    // this drawn frame's moving sectors to build: each built and packed on a worker with its own mesher (they only read
    // the level), then handed to Godot here (r_meshjobs; DWE2M1: 30 a frame)
    readonly List<(int s, Moving mv, bool rigid, bool eachDraw, float lerp, bool stillMoving)> _jobs = new();
    readonly List<WorldMesher> _jobMeshers = new();
    public static readonly Core.Cvar MeshJobs = Core.Cvars.Register("r_meshjobs", "1",
        "1: the moving sectors' meshes are built on worker threads (0: one after another on the main thread)");

    void BuildJob(int i)
    {
        var j = _jobs[i];
        var m = _jobMeshers[i];
        long t = System.Diagnostics.Stopwatch.GetTimestamp();
        m.Clear();
        m.Blend = j.rigid;
        m.Lerp = j.lerp;
        m.EmitSector(j.s);
        m.Prepare(j.mv.Layout);
        j.mv.EmitMs = (System.Diagnostics.Stopwatch.GetTimestamp() - t) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
    }

    /// <summary>Each drawn frame (lerp: its place between the last two game frames).</summary>
    public void Draw(float lerp, double clock)
    {
        if (_b == null)
            return;
        foreach (var ch in _chunks.Values)
            if (ch.Dirty)
                BuildChunk(ch);
        UpdateShadowStill();
        foreach (var (s, mv) in _moving)
        {
            bool moved = _b.ShapeVersion[s] != mv.Version;
            // moving rigidly, once a game frame (the shader blends between them, WorldShader.VertexBlend: E2M1 lake's floes,
            // each built again every drawn frame, took a frame's 2 ms); moving otherwise (the floor round them, beside a
            // door), each drawn frame as far as it has got; and once more when it has stopped
            bool stillMoving = _b.Reshaped.Contains(s);
            int blend = MeshBlend.Int;
            bool rigid = stillMoving && (blend == 2 || blend == 1 && _mesher.MovesRigidly(s));
            bool eachDraw = stillMoving && !rigid;
            if (!moved && !eachDraw && mv.EachDraw == eachDraw && !(mv.Blending && mv.Frame != _frame))
                continue;
            float l = eachDraw ? lerp : 1f;
            if (!moved && eachDraw && mv.EachDraw && mv.Frame == _frame && mv.Lerp == l)
                continue;
            // not seen from the camera (Build's walk, through the links too): built again once it is, by the above
            if (!SectorVisibility.FacesSeen(s))
                continue;
            _jobs.Add((s, mv, rigid, eachDraw, l, stillMoving));
        }
        if (_jobs.Count > 0)
        {
            while (_jobMeshers.Count < _jobs.Count)
                _jobMeshers.Add(new WorldMesher(_b) { IsPortal = IsPortal, IsPortalInPlace = IsPortalInPlace, IsWallPortal = IsWallPortal, IsWater = IsWater, SharesGround = SharesGround });
            long t0 = Core.Perf.Start();
            // (the slowest last time first: one big floor round the moving parts, DWE2M1's 866, takes as long as the rest)
            _jobs.Sort((a, b) => b.mv.EmitMs.CompareTo(a.mv.EmitMs));
            if (MeshJobs.Bool && _jobs.Count > 1)
                System.Threading.Tasks.Parallel.For(0, _jobs.Count, BuildJob);
            else
                for (int i = 0; i < _jobs.Count; i++)
                    BuildJob(i);
            long t1 = Core.Perf.Start();
            Core.Perf.Add("mv.build", t0);
            for (int i = 0; i < _jobs.Count; i++)
            {
                var (s, mv, _, eachDraw, l, stillMoving) = _jobs[i];
                long tc = System.Diagnostics.Stopwatch.GetTimestamp();
                _jobMeshers[i].Commit(mv.Mesh, Material, mv.Layout);
                mv.UploadMs = (System.Diagnostics.Stopwatch.GetTimestamp() - tc) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                mv.Builds++;
                mv.Blending = stillMoving;
                mv.EachDraw = eachDraw;
                mv.Lerp = l;
                mv.Frame = _frame;
                mv.Version = _b.ShapeVersion[s];
            }
            Core.Perf.Add("mv.commit", t1);
            _jobs.Clear();
        }
        foreach (var a in _animated)
        {
            int t = a.Tile == ProceduralFire.Tile ? a.Tile : a.Tile + Sim.BuildMath.AnimateOffs(a.Tile, clock);
            if (a.Shown == t)
                continue;
            a.Shown = t;
            if (a.Live != null)
                a.Live.Update(a.Frames[t]);
            else
                SetTexture(a.Mat, t, a.Kind);
        }
    }
}
