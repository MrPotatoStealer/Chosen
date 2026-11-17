// Chosen Blood module (GPL v2): Forge's view of the Blood install (its tiles and palette through Chosen.Blood.Files).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Chosen.Shared.Blood.Files;
using Godot;

namespace Chosen.Forge.Data;

/// <summary>
/// The Blood folder (a preference; found in Steam's or GOG's folders on first start) and what Forge reads from it:
/// the tiles (TILES0NN.ART) and the palette (BLOOD.RFF's BLOOD.PAL). Without one Forge still edits maps: previews show
/// tile numbers. The map's own art (Map Options > Art: the .MAPX's ARTS) goes over Blood's tiles (UseMapArt). A map
/// opened from a mod's or an add-on's folder (one run with NBlood's -game_dir: ART files beside the map, perhaps .RFS
/// resource scripts) gets that folder's too, as the game does: its ART files over Blood's of the same names, its
/// palette lookups, its SHADE.DAT and SURFACE.DAT (ModFolder).
/// </summary>
public static class ForgeData
{
    public static string Folder { get; private set; } = "";
    /// <summary>The tiles shown: Blood's with the map's own art over them.</summary>
    public static ArtSet Tiles { get; private set; }
    /// <summary>Blood's own tiles (the folder's ART files).</summary>
    public static ArtSet BloodTiles { get; private set; }
    static List<string> _bloodFiles = new();
    static string _artKey = "";
    /// <summary>The tiles the map's own art brought (the picker's Map's Art tab).</summary>
    public static HashSet<int> MapTiles { get; private set; } = new();
    /// <summary>What was wrong with the map's art sets when last composed (a set missing...).</summary>
    public static List<string> MapArtProblems { get; private set; } = new();
    /// <summary>The mod (or add-on) folder the open map lies in, whose art and tables are in use; "": none.</summary>
    public static string ModFolder { get; private set; } = "";
    /// <summary>The mod folder's own tiles (a part of MapTiles: the picker's Map's Art tab shows both).</summary>
    public static HashSet<int> ModTiles { get; private set; } = new();
    static byte[][] _bloodLookups = Array.Empty<byte[]>();
    static int[] _bloodShade = Array.Empty<int>(), _bloodSurface = Array.Empty<int>();

    static bool IsArtFile(string f) =>
        System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(f), @"^tiles\d+\.art$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    /// <summary>The folder of a map as a mod's: one that isn't the Blood folder and holds ART files or a mod's episode
    /// file and maps; "" when it is neither.</summary>
    static string ModFolderOf(string mapFolder)
    {
        try
        {
            if (string.IsNullOrEmpty(mapFolder) || !Directory.Exists(mapFolder))
                return "";
            string full = Path.GetFullPath(mapFolder).TrimEnd('/', '\\');
            if (Folder != "" && string.Equals(full, Path.GetFullPath(Folder).TrimEnd('/', '\\'), StringComparison.OrdinalIgnoreCase))
                return "";
            return Directory.GetFiles(full).Any(IsArtFile) || Addon.IsMod(full) ? full : "";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return "";
        }
    }

    /// <summary>The mod folder's palette lookups (its .RFS scripts' PLU resources, by number), SHADE.DAT and
    /// SURFACE.DAT over Blood's; none: Blood's own again.</summary>
    static void UseModTables(string mod)
    {
        Lookups = _bloodLookups;
        TileShade = _bloodShade;
        Surface = _bloodSurface;
        if (mod == "")
            return;
        try
        {
            TileShade = Assets.Dat(mod, "SHADE.DAT", MapArt.MaxTile + 1, false) ?? _bloodShade;
            Surface = Assets.Dat(mod, "SURFACE.DAT", MapArt.MaxTile + 1, false) ?? _bloodSurface;
            var plus = ModResources.Load(mod).OfType("PLU").Where(e => e.Id > 0 && e.Id < 256 && e.Bytes.Length >= 256).ToList();
            if (plus.Count > 0)
            {
                var all = new byte[Math.Max(_bloodLookups.Length, plus.Max(e => e.Id) + 1)][];
                Array.Copy(_bloodLookups, all, _bloodLookups.Length);
                foreach (var e in plus)
                    all[e.Id] = e.Bytes[..256];
                Lookups = all;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or InvalidDataException)
        {
            MapArtProblems.Add($"{Path.GetFileName(mod)}: {e.Message}");
        }
    }

    // the map's sets with its mod folder's art before them (an override set: files of Blood's names replace them whole)
    static List<Shared.Blood.Map.ArtRef> WithMod(IEnumerable<Shared.Blood.Map.ArtRef> sets, string mod)
    {
        var all = new List<Shared.Blood.Map.ArtRef>();
        if (mod != "" && Directory.GetFiles(mod).Any(IsArtFile))
            all.Add(new Shared.Blood.Map.ArtRef(mod, true));
        if (sets != null)
            all.AddRange(sets);
        return all;
    }

    /// <summary>The map's art sets over Blood's tiles (relative paths from mapFolder); true when the tiles changed
    /// (composed again: the same sets and files keep them; force: read again, a set packed anew).</summary>
    public static bool UseMapArt(IReadOnlyList<Shared.Blood.Map.ArtRef> sets, string mapFolder, bool force = false)
    {
        if (BloodTiles == null)
            return false;
        string mod = ModFolderOf(mapFolder);
        string key = (sets == null || sets.Count == 0 ? "" : mapFolder + "|" + string.Join("|", sets.Select(a => (a.Override ? "o:" : "a:") + a.Path)))
            + (mod == "" ? "" : "|mod:" + mod);
        if (key == _artKey && !force)
            return false;
        _artKey = key;
        MapArtProblems = new List<string>();
        ModFolder = mod;
        ModTiles = new HashSet<int>();
        if (key == "")
        {
            Tiles = BloodTiles;
            MapTiles = new HashSet<int>();
        }
        else
        {
            var all = WithMod(sets, mod);
            Tiles = MapArt.Compose(_bloodFiles, all, mapFolder, out var problems, out var own);
            MapArtProblems = problems;
            MapTiles = own;
            if (mod != "" && all.Count > 0 && all[0].Path == mod)
            {
                // which of them are the mod's own (for the status line)
                MapArt.Compose(_bloodFiles, all.Take(1), mapFolder, out _, out var modOwn);
                ModTiles = modOwn;
            }
        }
        UseModTables(mod);
        TileImages.Clear();
        return true;
    }

    /// <summary>Blood's tiles with the map's sets but one over them (the pictures packed into a set again: the numbers
    /// the others take aren't free).</summary>
    public static ArtSet TilesWithout(IReadOnlyList<Shared.Blood.Map.ArtRef> sets, string mapFolder, int leaveOut)
    {
        var rest = WithMod(sets.Where((_, i) => i != leaveOut), ModFolderOf(mapFolder));
        return rest.Count == 0 ? BloodTiles : MapArt.Compose(_bloodFiles, rest, mapFolder, out _, out _);
    }
    public static Palette Palette { get; private set; }
    /// <summary>Blood's palette lookups by pal (BLOOD.RFF's PLUs in the game's order: NORMAL, SATURATE, BEAST...): the
    /// shade-0 row of each, colour index to colour index; null where there is none.</summary>
    public static byte[][] Lookups { get; private set; } = Array.Empty<byte[]>();
    /// <summary>SHADE.DAT: each tile's shade, added to a sprite's (Blood's view.cpp); empty without the file.</summary>
    public static int[] TileShade { get; private set; } = Array.Empty<int>();
    /// <summary>SURFACE.DAT: each tile's surface type (Blood.TileUse.Materials), empty without it.</summary>
    public static int[] Surface { get; private set; } = Array.Empty<int>();
    public static string Error { get; private set; } = "";

    public static bool Ready => Tiles != null && Palette != null;

    /// <summary>Loads the tiles and palette of a Blood folder; false (and Error) if it isn't one.</summary>
    public static bool Use(string folder)
    {
        Tiles = BloodTiles = null;
        Palette = null;
        TileImages.Clear();
        _artKey = "";
        MapTiles = new HashSet<int>();
        ModTiles = new HashSet<int>();
        ModFolder = "";
        Folder = folder ?? "";
        if (!BloodInstall.IsValid(folder))
        {
            Error = string.IsNullOrEmpty(folder) ? "no Blood folder chosen" : folder + " holds no BLOOD.RFF and TILES000.ART";
            return false;
        }
        try
        {
            Tiles = BloodTiles = ArtSet.Load(folder);
            _bloodFiles = Directory.GetFiles(folder)
                .Where(f => System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(f), @"^tiles\d+\.art$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                .OrderBy(f => Path.GetFileName(f).ToLowerInvariant(), StringComparer.Ordinal).ToList();
            var rff = new Rff(BloodInstall.FindFile(folder, "BLOOD.RFF"));
            Palette = Palette.FromRff(rff);
            Lookups = _bloodLookups = Extractor.PluNames.Select(n => rff.Read(n, "PLU") is { Length: >= 256 } d ? d[..256] : null).ToArray();
            TileShade = _bloodShade = Assets.Dat(folder, "SHADE.DAT", 8192, false) ?? Array.Empty<int>();
            Surface = _bloodSurface = Assets.Dat(folder, "SURFACE.DAT", 8192, false) ?? Array.Empty<int>();
            Error = "";
            return true;
        }
        catch (Exception e)
        {
            Tiles = BloodTiles = null;
            Palette = null;
            Error = e.Message;
            return false;
        }
    }
}

/// <summary>Tile pictures as textures for the previews, made on first use and kept.</summary>
public static class TileImages
{
    static readonly Dictionary<(int, int), ImageTexture> _cache = new();

    public static void Clear()
    {
        _cache.Clear();
        _thumbs.Clear();
        _walls.Clear();
    }

    /// <summary>Tile n's picture (palette index 255 transparent), or null when there is no Blood folder or no such
    /// tile.</summary>
    public static ImageTexture Get(int n, int pal = 0)
    {
        if (_cache.TryGetValue((n, pal), out var t))
            return t;
        t = ImageOf(n, pal) is { } img ? ImageTexture.CreateFromImage(img) : null;
        _cache[(n, pal)] = t;
        return t;
    }

    /// <summary>Tile n as an image (not kept), or null; pal: drawn through that palette lookup (Blood's pal: a wall
    /// or a sprite recoloured, a secret door's hint).</summary>
    public static Image ImageOf(int n, int pal = 0)
    {
        if (!ForgeData.Ready || ForgeData.Tiles[n] is not { Empty: false } tile)
            return null;
        var lut = pal > 0 && pal < ForgeData.Lookups.Length ? ForgeData.Lookups[pal] : null;
        var rgba = ForgeData.Palette.ToRgba(tile, true, lut);
        return Image.CreateFromData(tile.Width, tile.Height, false, Image.Format.Rgba8, rgba);
    }

    static readonly Dictionary<(int, int), ImageTexture> _walls = new();

    /// <summary>
    /// Tile n as a wall shows it (the 3D preview): a height that isn't a power of two repeats at the next power of two,
    /// the rows past the picture going on into the next column's (Build keeps a tile column by column: a wall reading
    /// past a column's end reads the next one; NBlood's emulation of it). Other tiles: as Get.
    /// </summary>
    public static ImageTexture Wall(int n, int pal = 0)
    {
        if (_walls.TryGetValue((n, pal), out var t))
            return t;
        t = Get(n, pal);
        if (t != null && ImageOf(n, pal) is { } img)
        {
            int w = img.GetWidth(), h = img.GetHeight(), h2 = WallHeight(h);
            if (h2 != h)
            {
                var pad = Image.CreateEmpty(w, h2, false, Image.Format.Rgba8);
                for (int x = 0; x < w; x++)
                    for (int r = 0; r < h2; r++)
                        pad.SetPixel(x, r, img.GetPixel((x + r / h) % w, r % h));
                t = ImageTexture.CreateFromImage(pad);
            }
        }
        _walls[(n, pal)] = t;
        return t;
    }

    /// <summary>The rows a wall repeats a picture of this height at: the next power of two.</summary>
    public static int WallHeight(int h)
    {
        int p = 1;
        while (p < h)
            p <<= 1;
        return p;
    }

    /// <summary>Whether tile n has a picture.</summary>
    public static bool Exists(int n) => ForgeData.Ready && ForgeData.Tiles[n] is { Empty: false };

    static readonly Dictionary<(int, int, int), ImageTexture> _thumbs = new();

    /// <summary>Tile n fitted into a square of size pixels (its aspect kept, centred): list icons of one height; pal:
    /// through that palette lookup (the palette picker).</summary>
    public static ImageTexture Thumb(int n, int size, int pal = 0)
    {
        if (_thumbs.TryGetValue((n, size, pal), out var t))
            return t;
        t = null;
        // made from the tile's pixels (a picker shows thousands: no full-size texture each)
        if (_cache.TryGetValue((n, 0), out var full) && full == null)
            t = null;
        else if (ImageOf(n, pal) is { } img)
        {
            float k = (float)size / Math.Max(img.GetWidth(), img.GetHeight());
            int w = Math.Max(1, (int)Math.Round(img.GetWidth() * k)), h = Math.Max(1, (int)Math.Round(img.GetHeight() * k));
            // (a small tile made bigger keeps its pixels sharp)
            img.Resize(w, h, k > 1 ? Image.Interpolation.Nearest : Image.Interpolation.Bilinear);
            var sq = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
            sq.BlitRect(img, new Rect2I(0, 0, w, h), new Vector2I((size - w) / 2, (size - h) / 2));
            t = ImageTexture.CreateFromImage(sq);
        }
        _thumbs[(n, size, pal)] = t;
        return t;
    }

    /// <summary>Tile n's size in pixels (0 x 0 without one).</summary>
    public static Vector2I Size(int n) =>
        ForgeData.Tiles?[n] is { } t ? new Vector2I(Math.Max(t.Width, 0), Math.Max(t.Height, 0)) : Vector2I.Zero;
}
