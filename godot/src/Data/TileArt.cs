using System.Collections.Generic;
using System.IO;
using System.Linq;
using Chosen.Shared.Blood.Files;
using Godot;

namespace Chosen.Data;

/// <summary>
/// Blood's tiles as the game draws them, made from the ART files in the data folder (art/tilesNNN.art; an add-on's
/// art/ files replace the base game's of the same name whole, as NBlood's -game_dir does) through Blood's palette
/// (gfx/palette.lmp) and palette lookups (gfx/palookup.bin): an opaque picture (every index a colour), a masked one
/// (index 255 clear), and a masked one recoloured through a palette lookup's shade-0 row (a sprite's pal 1-14: Tommy
/// cultists' robes, the players' team colours; an add-on's own lookups from 15 up, its gfx/palookup.bin) as Blood
/// draws a sprite with a pal. A level's own art (LevelArt: the ART files its .MAPX names) goes over them.
/// </summary>
public static class TileArt
{
    static ArtSet _art;
    static byte[] _pal, _plu;
    /// <summary>The palette lookups there are (Blood's 15; more with an add-on's own).</summary>
    public static int Palookups
    {
        get
        {
            Load();
            return _plu == null ? 0 : _plu.Length / (64 * 256);
        }
    }

    /// <summary>The art and palettes read again (an add-on activated or left).</summary>
    public static void Reload()
    {
        _art = null;
        _pal = null;
    }

    static readonly object _loading = new();

    // (under a lock, the set shown only once whole: a level's pictures are made on many threads at once,
    // TileTextures.Preload, and the first to ask reads the files)
    static void Load()
    {
        if (_art != null)
            return;
        lock (_loading)
        {
            if (_art == null)
                Read();
        }
    }

    static void Read()
    {
        // base files by name, the active add-on's replacing them
        var files = new SortedDictionary<string, string>(System.StringComparer.Ordinal);
        void Add(string dir)
        {
            if (!Directory.Exists(dir))
                return;
            foreach (var f in Directory.GetFiles(dir, "*.art"))
                files[Path.GetFileName(f).ToLowerInvariant()] = f;
        }
        Add(BloodData.Folder.PathJoin("art"));
        if (Addons.Active is { } a)
            Add(a.Folder.PathJoin("art"));
        ArtSet art;
        if (LevelArt.Sets.Count > 0)
        {
            // the level's own art over them (Shared.Blood.Files.MapArt: override sets by file name, added sets tile by tile)
            art = MapArt.Compose(files.Values, LevelArt.Sets, LevelArt.Folder, out var problems, out var own);
            LevelArt.Own.Clear();
            LevelArt.Own.UnionWith(own);
            foreach (var p in problems)
                Core.GameConsole.Print("level art: " + p);
        }
        else
        {
            art = new ArtSet();
            foreach (var f in files.Values)
                art.AddFile(File.ReadAllBytes(f));
        }
        if (_pal == null)
        {
            string pal = BloodData.Find("gfx/palette.lmp"), plu = BloodData.Find("gfx/palookup.bin");
            _plu = File.Exists(plu) ? File.ReadAllBytes(plu) : null;
            _pal = File.Exists(pal) ? File.ReadAllBytes(pal) : new byte[768];
        }
        _art = art;
    }

    /// <summary>Whether the data folder has the tiles (art/): the game can't draw without them.</summary>
    public static bool Present => Directory.Exists(BloodData.Folder.PathJoin("art"));

    /// <summary>A tile's raw data (size, palette indices column by column), or null.</summary>
    public static Tile Get(int tile)
    {
        Load();
        return _art[tile] is { Empty: false } t ? t : null;
    }

    /// <summary>The palette lookup's shade-0 row for pal (null: none, the tile's own colours).</summary>
    static byte[] Lookup(int pal)
    {
        if (pal <= 0 || _plu == null || _plu.Length < (pal + 1) * 64 * 256)
            return null;
        var row = new byte[256];
        System.Array.Copy(_plu, pal * 64 * 256, row, 0, 256);
        return row;
    }

    /// <summary>A tile's picture: opaque (RGB, index 255 a colour) or masked (RGBA, index 255 clear; recoloured through
    /// pal's lookup); null when the tile has none.</summary>
    public static Image Picture(int tile, bool masked, int pal = 0)
    {
        var t = Get(tile);
        if (t == null)
            return null;
        var lut = masked ? Lookup(pal) : null;
        int w = t.Width, h = t.Height, bpp = masked ? 4 : 3;
        var px = new byte[w * h * bpp];
        for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
            {
                int i = t.Pixels[x * h + y], o = (y * w + x) * bpp;
                if (masked && i == Palette.Transparent)
                    continue;
                if (lut != null)
                    i = lut[i];
                px[o] = _pal[i * 3];
                px[o + 1] = _pal[i * 3 + 1];
                px[o + 2] = _pal[i * 3 + 2];
                if (masked)
                    px[o + 3] = 255;
            }
        return Image.CreateFromData(w, h, false, masked ? Image.Format.Rgba8 : Image.Format.Rgb8, px);
    }

    /// <summary>Is the tile's texel (column x, row y) clear (palette index 255)?</summary>
    public static bool Clear(int tile, int x, int y)
    {
        var t = Get(tile);
        return t != null && x >= 0 && y >= 0 && x < t.Width && y < t.Height && t.Pixels[x * t.Height + y] == Palette.Transparent;
    }

    /// <summary>A voxel model's palette strip for a palette lookup: 16 x 16, index i at (i % 16, i / 16), its colour
    /// through the lookup's shade-0 row (pal 0 and pals without a lookup: the palette itself).</summary>
    public static Image Swatch(int pal)
    {
        Load();
        var lut = Lookup(pal);
        var px = new byte[256 * 3];
        for (int i = 0; i < 256; i++)
        {
            int c = lut != null ? lut[i] : i;
            px[i * 3] = _pal[c * 3];
            px[i * 3 + 1] = _pal[c * 3 + 1];
            px[i * 3 + 2] = _pal[c * 3 + 2];
        }
        return Image.CreateFromData(16, 16, false, Image.Format.Rgb8, px);
    }
}
