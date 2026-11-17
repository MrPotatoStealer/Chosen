using System.Collections.Generic;
using System.IO;
using Godot;

namespace Chosen.Data;

/// <summary>
/// Blood's extracted data (pipeline/export.py output: game/blood3d), read at runtime: data tables and tile images.
/// The folder is the cvar fs_blooddata (default: the repository's game/blood3d).
/// </summary>
public static class BloodData
{
    public static readonly Core.Cvar Root = Core.Cvars.Register("fs_blooddata", "", "folder with Blood's extracted data (default ../game/blood3d)");

    /// <summary>The data folder: fs_blooddata, else --data=&lt;folder&gt;, else data/ beside the exe in an exported game
    /// (ChosenLoader fills it), else the repository's game/blood3d.</summary>
    public static string Folder =>
        Root.Value != "" ? Root.Value : _default ??= DefaultFolder();

    static string _default;

    static string DefaultFolder()
    {
        foreach (var a in OS.GetCmdlineUserArgs())
            if (a.StartsWith("--data="))
                return a[7..].Replace('\\', '/');
        if (!OS.HasFeature("editor"))
            return OS.GetExecutablePath().GetBaseDir().PathJoin("data");
        return ProjectSettings.GlobalizePath("res://").PathJoin("../game/blood3d").SimplifyPath();
    }

    /// <summary>What to tell the player when the data folder was made by an older extraction than this game needs (its
    /// marker's DataVersion below Extractor.DataVersion, or no marker: made before there was one), else null.</summary>
    public static string Outdated => _outdated ??= Shared.Blood.Files.Extractor.FolderDataVersion(Folder)
        < Shared.Blood.Files.Extractor.DataVersion
        ? (OS.HasFeature("editor") ? "THE DATA FOLDER IS OUT OF DATE: EXTRACT IT AGAIN (MAPTOOL EXTRACT)"
                                   : "THE DATA FOLDER IS OUT OF DATE: RUN CHOSENLOADER AGAIN")
        : "";

    static string _outdated;

    public static string DataFile(string name) => Find("data/" + name);

    /// <summary>A file of the data folder (a path under it: textures/blood/0123.png), the active add-on's own where it has
    /// one (Addons: as NBlood searches an add-on's folder first).</summary>
    public static string Find(string rel)
    {
        if (Addons.Active is { } a)
        {
            string own = a.Folder.PathJoin(rel);
            if (File.Exists(own))
                return own;
        }
        return Folder.PathJoin(rel);
    }
}

/// <summary>
/// Tile metadata (data/rt_tiles.txt: "tile size ofs misc", packed like gen_qcdata.py): size w + h*1024; offsets
/// (xofs+128) + (yofs+128)*256 + anim*65536; misc view + surface*8 + (shade+128)*256 + voxel*65536 + animtype*131072 +
/// speed*524288. A level's own art (LevelArt) adds or replaces rows for its tiles.
/// </summary>
public static class Tiles
{
    static int[] _size = System.Array.Empty<int>(), _ofs = System.Array.Empty<int>(), _misc = System.Array.Empty<int>();

    /// <summary>Read again (an add-on activated or left: its art's sizes and offsets in place of the base game's).</summary>
    public static void Reload()
    {
        bool had = _size.Length > 0;
        _size = _ofs = _misc = System.Array.Empty<int>();
        if (had)
            Load();
    }

    public static void Load()
    {
        if (_size.Length > 0)
            return;
        var path = BloodData.DataFile("rt_tiles.txt");
        var rows = new List<(int t, int s, int o, int m)>();
        int max = 0;
        foreach (var line in File.ReadLines(path))
        {
            var p = line.Split(' ');
            if (p.Length < 4)
                continue;
            int t = int.Parse(p[0]);
            rows.Add((t, int.Parse(p[1]), int.Parse(p[2]), int.Parse(p[3])));
            max = System.Math.Max(max, t + 1);
        }
        // the level's own art (LevelArt): a row for each of its tiles from the tile itself, a tile it replaces keeping
        // its surface, shade and voxel model (SURFACE.DAT's, SHADE.DAT's: the map's art has none of its own)
        if (LevelArt.Sets.Count > 0)
        {
            TileArt.Get(0);
            var had = new Dictionary<int, int>();
            for (int i = 0; i < rows.Count; i++)
                had[rows[i].t] = i;
            foreach (int t in LevelArt.Own)
            {
                if (TileArt.Get(t) is not { } tile)
                    continue;
                int old = had.TryGetValue(t, out int at) ? rows[at].m : 0;
                var (s, o, m) = Chosen.Shared.Blood.Files.Tables.RtRow(tile, (old >> 3) & 31, old != 0 ? ((old >> 8) & 255) - 128 : 0,
                    ((old >> 16) & 1) != 0);
                var row = (t, (int)s, (int)o, (int)m);
                if (had.TryGetValue(t, out at))
                    rows[at] = row;
                else
                    rows.Add(row);
                max = System.Math.Max(max, t + 1);
            }
        }
        _size = new int[max];
        _ofs = new int[max];
        _misc = new int[max];
        // tiles missing in between read as 0 (offsets -128), as in Blood3D's QuakeC (markers on undefined tiles)
        foreach (var r in rows)
        {
            _size[r.t] = r.s;
            _ofs[r.t] = r.o;
            _misc[r.t] = r.m;
        }
    }

    public static int Count => _size.Length;
    static bool In(int t) => t >= 0 && t < _size.Length;
    public static bool Valid(int t) => In(t) && _size[t] > 0;
    public static int W(int t) => In(t) ? _size[t] & 1023 : 0;
    public static int H(int t) => In(t) ? _size[t] >> 10 : 0;
    public static int XOfs(int t) => In(t) ? (_ofs[t] & 255) - 128 : 0;
    public static int YOfs(int t) => In(t) ? ((_ofs[t] >> 8) & 255) - 128 : 0;
    public static int AnimNum(int t) => In(t) ? (_ofs[t] >> 16) & 63 : 0;
    public static int View(int t) => In(t) ? _misc[t] & 7 : 0;
    public static int SurfType(int t) => In(t) ? (_misc[t] >> 3) & 31 : 0;
    public static int Shade(int t) => In(t) ? ((_misc[t] >> 8) & 255) - 128 : 0;
    public static bool HasVoxel(int t) => In(t) && ((_misc[t] >> 16) & 1) != 0;
    public static int AnimType(int t) => In(t) ? (_misc[t] >> 17) & 3 : 0;
    public static int AnimSpeed(int t) => In(t) ? (_misc[t] >> 19) & 15 : 0;
}

/// <summary>Tile images with palette index 255 clear, recoloured by a sprite's pal (TileArt: from the data folder's ART files).</summary>
public static class TileTextures
{
    static readonly Dictionary<int, Texture2D> _cache = new();

    static readonly Dictionary<int, Texture2D> _faded = new();

    /// <summary>A tile drawn translucent (rotatesprite's levels: thirds 1 or 2 of full opacity): its picture with the
    /// alpha baked in. Drawing the plain picture with a see-through tint instead turned a clear column at a tile's
    /// edge black (the credits' scrolling text).</summary>
    public static Texture2D GetFaded(int tile, int pal, int thirds)
    {
        int key = (tile * 32 + (pal & 31)) * 4 + thirds;
        if (_faded.TryGetValue(key, out var tex))
            return tex;
        tex = null;
        if (Get(tile, pal) is ImageTexture src && src.GetImage() is Image img)
        {
            img = (Image)img.Duplicate();
            img.ClearMipmaps();
            if (img.GetFormat() != Image.Format.Rgba8)
                img.Convert(Image.Format.Rgba8);
            var data = img.GetData();
            for (int i = 3; i < data.Length; i += 4)
                data[i] = (byte)(data[i] * thirds / 3);
            img = Image.CreateFromData(img.GetWidth(), img.GetHeight(), false, Image.Format.Rgba8, data);
            tex = ImageTexture.CreateFromImage(img);
        }
        _faded[key] = tex;
        return tex;
    }

    /// <summary>Forget the loaded images (an add-on activated or left).</summary>
    public static void Clear()
    {
        _cache.Clear();
        _faded.Clear();
    }

    public static Texture2D Get(int tile, int pal = 0)
    {
        if (tile == Render.ProceduralFire.Tile && Render.ProceduralFire.Texture is Texture2D fire)
            return fire;        // Blood's procedural fire, drawn live
        int key = tile * 32 + (pal & 31);
        if (_cache.TryGetValue(key, out var tex))
            return tex;
        var img = Decode(tile, pal);
        if (img != null)
            Core.Perf.TextureLoads++;
        tex = img == null ? null : ImageTexture.CreateFromImage(img);
        _cache[key] = tex;
        return tex;
    }

    // a tile's picture in palette pal, with its mipmaps; null if there is none
    static Image Decode(int tile, int pal)
    {
        var img = TileArt.Picture(tile, true, pal);
        img?.GenerateMipmaps();
        return img;
    }

    /// <summary>Loads these tiles not loaded yet, their pictures read and mipmapped in parallel (a level's sprites as it
    /// loads, not on the first frames they are seen).</summary>
    public static void Preload(IEnumerable<(int tile, int pal)> tiles)
    {
        var want = new List<(int tile, int pal, int key)>();
        var seen = new HashSet<int>();
        foreach (var (tile, pal) in tiles)
        {
            int key = tile * 32 + (pal & 31);
            if (tile == Render.ProceduralFire.Tile || _cache.ContainsKey(key) || !seen.Add(key))
                continue;
            want.Add((tile, pal, key));
        }
        var imgs = new Image[want.Count];
        System.Threading.Tasks.Parallel.For(0, want.Count, i => imgs[i] = Decode(want[i].tile, want[i].pal));
        for (int i = 0; i < want.Count; i++)
            _cache[want[i].key] = imgs[i] == null ? null : ImageTexture.CreateFromImage(imgs[i]);
    }
}
