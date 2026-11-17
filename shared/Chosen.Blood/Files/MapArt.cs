// Chosen Blood module (GPL v2): a map's own art (ART files beside it) over Blood's, ART files written, pictures
// matched to Blood's palette.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Chosen.Shared.Blood.Map;

namespace Chosen.Shared.Blood.Files;

/// <summary>
/// A map's own art (the .MAPX's ARTS: Map.ArtRef) over Blood's: Blood's ART files by name, an override set's files
/// replacing those of the same name whole (as an add-on's do: Death Wish's TILES003.ART for Blood's), then each added
/// set's tiles one by one (its empty tiles leaving Blood's as they are). Forge and the game compose it alike.
/// </summary>
public static class MapArt
{
    /// <summary>Blood's tiles: 0..6143 (24 ART files of 256). A .MAP for Blood or Refreshed Supply numbers its
    /// pictures below it.</summary>
    public const int BloodTiles = 6144;
    /// <summary>The highest tile a .MAPX's pictures may have: NBlood's last user tile (MAXUSERTILES - 1: the 256 after
    /// are its own, made while playing).</summary>
    public const int MaxTile = 30463;

    /// <summary>Tiles in Blood's range the games make while playing, never free for a map's art though Blood's ART
    /// leaves them empty: the mirror (504), the lens, tilt and crystal ball buffers (4077..4079) and DOS Blood's
    /// mirror tiles after them (..4095).</summary>
    public static bool Reserved(int tile) => tile == 504 || tile is >= 4077 and <= 4095;

    /// <summary>A set's path as a file path: relative to the map's folder (absolute as it is).</summary>
    public static string Resolve(string mapFolder, string path)
    {
        if (string.IsNullOrEmpty(path))
            return path;
        if (Path.IsPathRooted(path) || string.IsNullOrEmpty(mapFolder))
            return Path.GetFullPath(path);
        return Path.GetFullPath(Path.Combine(mapFolder, path));
    }

    /// <summary>A path as a set's path for a map in mapFolder: relative to it (with '/'), absolute when there's no
    /// folder or no relative way (another drive).</summary>
    public static string Relative(string mapFolder, string fullPath)
    {
        if (string.IsNullOrEmpty(mapFolder))
            return fullPath.Replace('\\', '/');
        string rel = Path.GetRelativePath(mapFolder, fullPath);
        return (Path.IsPathRooted(rel) ? fullPath : rel).Replace('\\', '/');
    }

    /// <summary>The ART files of a set's path: the file itself, or a folder's *.art in name order; none when missing.</summary>
    public static List<string> Files(string fullPath)
    {
        if (File.Exists(fullPath))
            return new List<string> { fullPath };
        if (Directory.Exists(fullPath))
            return Directory.GetFiles(fullPath, "*.art").OrderBy(f => Path.GetFileName(f).ToLowerInvariant(), StringComparer.Ordinal).ToList();
        return new List<string>();
    }

    /// <summary>
    /// Blood's art (baseFiles: its ART files, name order; an add-on's replacing them already) with a map's sets over it
    /// (resolved against mapFolder). Problems: sets missing or unreadable (left out). Also which tiles came from the
    /// map's sets (ownTiles).
    /// </summary>
    public static ArtSet Compose(IEnumerable<string> baseFiles, IEnumerable<ArtRef> sets, string mapFolder,
        out List<string> problems, out HashSet<int> ownTiles)
    {
        problems = new List<string>();
        ownTiles = new HashSet<int>();
        var byName = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var f in baseFiles)
            byName[Path.GetFileName(f).ToLowerInvariant()] = f;
        var overridden = new HashSet<string>();
        var added = new List<string>();
        foreach (var s in sets ?? Enumerable.Empty<ArtRef>())
        {
            string full = Resolve(mapFolder, s.Path);
            var files = Files(full);
            if (files.Count == 0)
            {
                problems.Add($"{s.Path}: no ART file there");
                continue;
            }
            foreach (var f in files)
            {
                if (s.Override)
                {
                    byName[Path.GetFileName(f).ToLowerInvariant()] = f;
                    overridden.Add(f);
                }
                else
                    added.Add(f);
            }
        }
        var art = new ArtSet();
        foreach (var f in byName.Values)
        {
            try
            {
                var d = File.ReadAllBytes(f);
                var range = art.AddFile(d);
                if (overridden.Contains(f))
                    ownTiles.UnionWith(range.Where(n => art[n] is { Empty: false }));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or InvalidDataException)
            {
                problems.Add($"{Path.GetFileName(f)}: {e.Message}");
            }
        }
        foreach (var f in added)
        {
            try
            {
                var extra = new ArtSet();
                extra.AddFile(File.ReadAllBytes(f));
                foreach (int n in extra.Numbers)
                    if (extra[n] is { Empty: false } t)
                    {
                        art.Put(t);
                        ownTiles.Add(n);
                    }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or InvalidDataException)
            {
                problems.Add($"{Path.GetFileName(f)}: {e.Message}");
            }
        }
        return art;
    }

    /// <summary>A tile number free for a map's own picture: no picture in art (except those of tiles mine: a set's
    /// own, packed again), not reserved; in Blood's range (classic) or past it (extended, up to MaxTile).</summary>
    public static bool Free(ArtSet art, int tile, bool classic, ISet<int> mine = null)
    {
        if (classic ? tile < 0 || tile >= BloodTiles : tile < BloodTiles || tile > MaxTile)
            return false;
        if (Reserved(tile))
            return false;
        return mine?.Contains(tile) == true || art[tile] is not { Empty: false };
    }

    // ---------------------------------------------------------------- writing ART files

    /// <summary>
    /// An ART file (version 1) of the tiles from first to last (tiles: by number; a number missing an empty tile):
    /// the header (version, tile count, first, last), then each tile's width, height and picanm, then the pixels
    /// column by column. One contiguous range a file: Build reads every tile of a file's range, empty ones too, so a
    /// file never spans a tile it doesn't mean to replace.
    /// </summary>
    public static byte[] WriteArt(int first, int last, IReadOnlyDictionary<int, Tile> tiles)
    {
        if (last < first)
            throw new ArgumentException("an ART file of no tiles");
        int n = last - first + 1;
        using var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        w.Write(1);
        w.Write(n);
        w.Write(first);
        w.Write(last);
        Tile T(int i) => tiles.TryGetValue(first + i, out var t) && t is { Empty: false } ? t : null;
        for (int i = 0; i < n; i++)
            w.Write((short)(T(i)?.Width ?? 0));
        for (int i = 0; i < n; i++)
            w.Write((short)(T(i)?.Height ?? 0));
        for (int i = 0; i < n; i++)
            w.Write(T(i)?.Picanm ?? 0u);
        for (int i = 0; i < n; i++)
            if (T(i) is { } t)
                w.Write(t.Pixels, 0, t.Width * t.Height);
        w.Flush();
        return ms.ToArray();
    }

    /// <summary>The runs of consecutive numbers in tiles (each run one ART file).</summary>
    public static List<(int first, int last)> Runs(IEnumerable<int> tiles)
    {
        var runs = new List<(int, int)>();
        foreach (int t in tiles.Distinct().OrderBy(t => t))
        {
            if (runs.Count > 0 && runs[^1].Item2 == t - 1)
                runs[^1] = (runs[^1].Item1, t);
            else
                runs.Add((t, t));
        }
        return runs;
    }

    /// <summary>A picanm with these offsets (the picture's middle moved by x, y pixels: sprites) and animation.</summary>
    public static uint Picanm(int xOffset, int yOffset, int frames = 0, int animType = 0, int speed = 0, int view = 0) =>
        (uint)(frames & 63) | (uint)(animType & 3) << 6 | (uint)(byte)(sbyte)Math.Clamp(xOffset, -128, 127) << 8
        | (uint)(byte)(sbyte)Math.Clamp(yOffset, -128, 127) << 16 | (uint)(speed & 15) << 24 | (uint)(view & 7) << 28;

    // ---------------------------------------------------------------- matching pictures to the palette

    /// <summary>
    /// A picture (RGBA rows, top to bottom) as a tile of Blood's palette: each pixel the nearest of the palette's
    /// colours 0..254 (a weighted RGB distance: green counts most, as the eye sees it; one lookup a colour), a pixel
    /// less than half opaque the clear index 255. Columns, as an ART tile keeps them.
    /// </summary>
    public static Tile Quantize(byte[] rgba, int width, int height, Palette pal, int number)
    {
        var px = new byte[width * height];
        var memo = new Dictionary<int, byte>();
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int o = (y * width + x) * 4;
                byte idx;
                if (rgba[o + 3] < 128)
                    idx = Palette.Transparent;
                else
                {
                    int key = rgba[o] << 16 | rgba[o + 1] << 8 | rgba[o + 2];
                    if (!memo.TryGetValue(key, out idx))
                        memo[key] = idx = Nearest(pal, rgba[o], rgba[o + 1], rgba[o + 2]);
                }
                px[x * height + y] = idx;
            }
        return new Tile { Number = number, Width = width, Height = height, Pixels = px };
    }

    /// <summary>The palette's colour (0..254: 255 is the clear one) nearest r, g, b.</summary>
    public static byte Nearest(Palette pal, int r, int g, int b)
    {
        int best = 0;
        long bd = long.MaxValue;
        for (int i = 0; i < 255; i++)
        {
            var (pr, pg, pb) = pal[i];
            long dr = pr - r, dg = pg - g, db = pb - b;
            long d = 3 * dr * dr + 4 * dg * dg + 2 * db * db;
            if (d < bd)
            {
                bd = d;
                best = i;
                if (d == 0)
                    break;
            }
        }
        return (byte)best;
    }
}
