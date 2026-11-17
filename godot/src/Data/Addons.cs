using System.Collections.Generic;
using System.IO;
using Godot;

namespace Chosen.Data;

/// <summary>
/// Refreshed Supply's add-ons (Death Wish, Cryptic Passage, MARROW: install_addon.bat, pipeline/export_addon.py): each
/// a data folder, &lt;data&gt;/addons/&lt;id&gt;, holding what the add-on brings over the base game: the images of the tiles
/// its art files replace, its tile table (rt_tiles.txt), its episode file (data/blood.ini), its music and cutscenes, its
/// maps (pipeline/export_maps.py); an NBlood add-on's (Eviction) also the animation and sound tables with its own
/// added, its sounds, palette lookups, voxel models and custom dudes. The game reads through <see cref="BloodData.Find"/>,
/// the active add-on's file first, as NBlood's -game_dir searches the add-on's folder first. A level of an add-on
/// activates it as it loads (LevelLoader), a base game level the base game; the menu's ADD-ONS picks one for its
/// episodes.
/// </summary>
public static class Addons
{
    public sealed class Addon
    {
        public string Id = "", Title = "", Folder = "";
        public readonly HashSet<string> Maps = new();
    }

    static List<Addon> _all;

    /// <summary>The add-on whose files come first (null: the base game).</summary>
    public static Addon Active { get; private set; }

    /// <summary>Every exported add-on (those with a data/addon.txt), by title.</summary>
    public static IReadOnlyList<Addon> All
    {
        get
        {
            if (_all != null)
                return _all;
            _all = new List<Addon>();
            string root = BloodData.Folder.PathJoin("addons");
            if (!Directory.Exists(root))
                return _all;
            foreach (var dir in Directory.GetDirectories(root))
            {
                string info = Path.Combine(dir, "data", "addon.txt");
                if (!File.Exists(info))
                    continue;
                var a = new Addon { Id = Path.GetFileName(dir), Folder = dir.Replace('\\', '/') };
                foreach (var line in File.ReadLines(info))
                {
                    int sp = line.IndexOf(' ');
                    if (sp < 0)
                        continue;
                    string key = line[..sp], val = line[(sp + 1)..].Trim();
                    if (key == "title")
                        a.Title = val;
                    else if (key == "maps")
                        foreach (var m in val.Split(' ', System.StringSplitOptions.RemoveEmptyEntries))
                            a.Maps.Add(m.ToLowerInvariant());
                }
                if (a.Title == "")
                    a.Title = a.Id;
                _all.Add(a);
            }
            _all.Sort((x, y) => string.CompareOrdinal(x.Title, y.Title));
            return _all;
        }
    }

    /// <summary>The add-on a map belongs to (its .ini lists it), null for the base game's.</summary>
    public static Addon ForMap(string map)
    {
        map = Path.GetFileNameWithoutExtension(map ?? "").ToLowerInvariant();
        foreach (var a in All)
            if (a.Maps.Contains(map))
                return a;
        // a level no add-on lists, played with one all the same (--addon: Forge's Test Map of a map made in a mod's folder)
        return Forced == "" ? null : ById(Forced);
    }

    /// <summary>The add-on (its id) a level that no add-on lists is played with; "": the base game alone.</summary>
    public static string Forced = "";

    public static Addon ById(string id)
    {
        foreach (var a in All)
            if (a.Id == id)
                return a;
        return null;
    }

    /// <summary>Make an add-on's files come first (null: the base game's only): its tile table, tile images, episodes and
    /// music from now on.</summary>
    public static void Activate(Addon a)
    {
        if (a == Active)
            return;
        Active = a;
        Tiles.Reload();
        TileArt.Reload();
        TileTextures.Clear();
        Render.Native.NativeWorld.ClearTextures();
        Render.BuildPalette.Reload();
        Render.Voxels.Reload();
        SeqData.Reload();
        QavData.Reload();
        SfxData.Reload();
        LevelInfo.Reload();
        Core.GameConsole.Print(a != null ? $"add-on: {a.Title}" : "add-on: none (Blood)");
    }

    /// <summary>A level's name as a path, res://maps/&lt;name&gt;.map (bug reports and recordings name levels so; the level
    /// itself is read from its Blood .MAP, BuildMapPath).</summary>
    public static string MapRes(string name)
    {
        name = Path.GetFileNameWithoutExtension(name ?? "").ToLowerInvariant();
        return $"res://maps/{name}.map";
    }

    /// <summary>fs_mapx: a folder whose &lt;NAME&gt;.mapx the game takes over the level's .map (the .MAPX check:
    /// bugs.py regress --mapx).</summary>
    public static readonly Core.Cvar MapXFolder = Core.Cvars.Register("fs_mapx", "",
        "a folder whose <NAME>.mapx files are played instead of the levels' .map (testing)");

    /// <summary>A level's map file (pipeline/export_maps.py): fs_mapx's &lt;NAME&gt;.mapx when that is set and has one, else
    /// an add-on's in &lt;data&gt;/addons/&lt;id&gt;/maps, the base game's in &lt;data&gt;/maps, as .map, else as .mapx; null if
    /// there is none.</summary>
    public static string BuildMapPath(string name)
    {
        name = Path.GetFileNameWithoutExtension(name ?? "").ToUpperInvariant();
        if (MapXFolder.Value != "")
        {
            string x = Path.Combine(MapXFolder.Value, name + ".mapx").Replace('\\', '/');
            if (File.Exists(x))
                return x;
        }
        foreach (var ext in new[] { ".map", ".mapx" })
        {
            if (ForMap(name) is { } a && File.Exists(a.Folder.PathJoin("maps").PathJoin(name + ext)))
                return a.Folder.PathJoin("maps").PathJoin(name + ext);
            string p = BloodData.Folder.PathJoin("maps").PathJoin(name + ext);
            if (File.Exists(p))
                return p;
        }
        return null;
    }

    /// <summary>A name for the active add-on's textures (the native sky's key: an add-on's art may replace its tiles).</summary>
    public static string TextureRes => Active != null ? $"res://mods/{Active.Id}/textures" : "res://textures";
}
