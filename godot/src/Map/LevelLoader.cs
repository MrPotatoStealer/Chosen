using System.Collections.Generic;
using Godot;
using Chosen.Shared.Blood.Map;

namespace Chosen.Map;

/// <summary>
/// Loads a level from its Blood .MAP or Chosen .MAPX (game/blood3d/maps/&lt;NAME&gt;.map, an add-on's in its folder:
/// pipeline/export_maps.py; Data.Addons.BuildMapPath; or a file anywhere on disk):
/// its Build data (Sim.Build.Board), the sectors and walls the game and renderers look up (Sim.SectorMap.FromBoard),
/// Build's sky and visibility. Loads it again when the file changes on disk (hot reload: save in the editor, see it in
/// the running game).
/// </summary>
public partial class LevelLoader : Node3D
{
    [Signal] public delegate void LevelBuiltEventHandler();

    /// <summary>The level's name as a path (res://maps/&lt;name&gt;.map: bug reports and recordings name it so; the file
    /// itself is the Blood .MAP, BuildPath).</summary>
    public string MapPath { get; private set; } = "";
    /// <summary>The worldspawn's keys (the level's visibility; its weather from World.NativeWeather).</summary>
    public List<MapEntity> Entities { get; private set; } = new();
    /// <summary>The level's Build sectors and walls as the renderers look them up (live: SectorMap.Refresh).</summary>
    public Sim.SectorMap Shade { get; private set; }
    /// <summary>The level's Build data as the game plays it.</summary>
    public Sim.Build.Board Board { get; private set; }
    /// <summary>The .MAP or .MAPX the level is read from.</summary>
    public string BuildPath { get; private set; }
    /// <summary>What Load was given (a level's name as a path, or a file on disk): the reload command loads it again.</summary>
    public string Source { get; private set; } = "";

    WorldEnvironment _env;
    Render.AmbientOcclusion _ao;      // r_ao: the compositor pass on the level's environment
    ulong _mtime;
    double _watchTimer;
    int _readFailures;      // tries of a changed file that couldn't be read (an editor still writing it)

    /// <summary>The level named by path's file name (res://maps/e1m1.map, or just its name), or a .map / .mapx file on
    /// disk (--file, the map command, Forge's Test Map): that file, the level named after it.</summary>
    public void Load(string path)
    {
        Source = path;
        path = path.Replace('\\', '/');
        string name = path.GetFile().GetBaseName();
        string ext = path.GetExtension().ToLowerInvariant();
        bool onDisk = !path.StartsWith("res://") && !path.StartsWith("user://") && (ext == "map" || ext == "mapx")
            && System.IO.File.Exists(path);
        MapPath = path.Contains('/') && !onDisk ? path : $"res://maps/{name.ToLowerInvariant()}.map";
        // one of Refreshed Supply's add-ons' levels: its art, textures, episodes and music first (Data.Addons)
        Data.Addons.Activate(Data.Addons.ForMap(name));
        string bp = onDisk ? path : Data.Addons.BuildMapPath(name);
        if (bp == null)
        {
            Core.GameConsole.Print($"level {name}: no Blood map (pipeline/export_maps.py)");
            return;
        }
        LoadBuild(bp);
    }

    /// <summary>The art a .MAPX names (its ARTS: Data.LevelArt) over Blood's, before anything reads a tile; a .MAP or a
    /// .MAPX naming none: Blood's alone.</summary>
    static void UseLevelArt(string buildPath)
    {
        IEnumerable<ArtRef> sets = null;
        string folder = "";
        try
        {
            var head = new byte[4];
            using (var f = System.IO.File.OpenRead(buildPath))
                f.ReadExactly(head);
            if (MapXFile.IsMapX(head))
            {
                sets = MapXFile.Read(buildPath).extras.Art;
                folder = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(buildPath)) ?? "";
            }
        }
        catch (System.Exception e) when (e is System.IO.IOException or System.IO.InvalidDataException or System.UnauthorizedAccessException)
        {
            // (the map's own reading says what's wrong with it)
        }
        Data.LevelArt.Use(sets, folder);
    }

    /// <summary>The level's Build data as stored again (a restart: the game changed the last one as it played).</summary>
    public void FreshBoard()
    {
        if (BuildPath == null)
            return;
        Board = new Sim.Build.Board(BloodMapFile.Load(BuildPath));
        Shade = Sim.SectorMap.FromBoard(Board);
        Render.StackedRooms.Clear();
    }

    void LoadBuild(string buildPath, BloodMapFile file = null)
    {
        ulong t = Time.GetTicksMsec();
        BuildPath = buildPath;
        UseLevelArt(buildPath);
        Board = new Sim.Build.Board(file ?? BloodMapFile.Load(buildPath));
        Shade = Sim.SectorMap.FromBoard(Board);
        var ws = new MapEntity();
        ws.Keys["classname"] = "worldspawn";
        ws.Keys["bloodVisibility"] = Board.File.Visibility.ToString();
        Entities = new List<MapEntity> { ws };
        Render.StackedRooms.Clear();
        Render.BuildShading.FogFloor.Clear();
        Render.BuildShading.FogCeil.Clear();
        bool skyFog = Render.BuildShading.AddFogFromSectors(Shade);
        int vis = Board.File.Visibility;
        Render.BuildShading.SetVisibility(vis);
        // the sky: the tile most of the parallax ceilings have
        var counts = new Dictionary<int, int>();
        foreach (var s in Board.Sectors)
            if ((s.CeilingStat & 1) != 0)
                counts[s.CeilingPicnum] = counts.GetValueOrDefault(s.CeilingPicnum) + 1;
        int skyTile = -1, best = 0;
        foreach (var (k, v) in counts)
            if (v > best)
                (skyTile, best) = (k, v);
        string sky = "";
        if (skyTile >= 0 && Render.Native.NativeSky.MaterialFor(skyTile, Board) != null)
            sky = Render.Native.NativeSky.Current;
        _env ??= new WorldEnvironment();
        if (_env.GetParent() == null)
            AddChild(_env);
        _env.Environment = Render.BuildSky.MakeEnvironment(sky) ?? new Environment { BackgroundMode = Environment.BGMode.Color, BackgroundColor = Colors.Black };
        Render.EnhancedFog.SetLevel(_env, vis, skyFog, ws);
        _ao ??= new Render.AmbientOcclusion();
        _ao.Update();
        _env.Compositor = new Compositor { CompositorEffects = new Godot.Collections.Array<CompositorEffect> { _ao } };
        _mtime = FileAccess.GetModifiedTime(buildPath);
        Core.GameConsole.Print($"level {buildPath}: loaded in {(Time.GetTicksMsec() - t) / 1000.0:0.0} s, {Board.SectorCount} sectors, {Board.WallCount} walls, {Board.File.Sprites.Count} sprites");
        EmitSignal(SignalName.LevelBuilt);
    }

    public override void _ExitTree()
    {
        // the game quitting: the compositor pass's GPU resources go before the rendering device does
        if (_env != null)
            _env.Compositor = null;
        _ao?.Release();
        _ao = null;
    }

    public override void _Process(double delta)
    {
        _ao?.Update();
        if (BuildPath == null)
            return;
        _watchTimer += delta;
        if (_watchTimer < 0.5)
            return;
        _watchTimer = 0;
        ulong m = FileAccess.GetModifiedTime(BuildPath);
        if (m != 0 && m != _mtime)
        {
            // read first: a file still being written (an editor saving in place) fails to read; it is tried again on
            // the next ticks (for 5 s), then not until it changes again
            BloodMapFile file;
            try
            {
                file = BloodMapFile.Load(BuildPath);
            }
            catch (System.Exception e)
            {
                if (_readFailures++ == 0)
                    Core.GameConsole.Print($"level changed on disk, but it can't be read yet ({e.Message}): kept as it was");
                if (_readFailures >= 10)
                {
                    _mtime = m;
                    _readFailures = 0;
                }
                return;
            }
            _readFailures = 0;
            Core.GameConsole.Print("level changed on disk, loading it again");
            LoadBuild(BuildPath, file);
        }
    }
}
