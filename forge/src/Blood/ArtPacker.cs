// Chosen Blood module (GPL v2): a folder of pictures packed into ART files (a map's own textures).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Chosen.Shared.Blood.Files;
using Godot;

namespace Chosen.Forge.Blood;

/// <summary>
/// Map Options > Art > Pack Pictures: every picture in a folder (PNG, JPEG, BMP, TGA, WebP; by name) matched to Blood's
/// palette (MapArt.Quantize: a pixel less than half opaque see-through) and written as ART files into an output folder,
/// a file for each run of consecutive tile numbers (tiles024.art on: Build reads a file's whole range, so none spans a
/// tile it doesn't mean to replace). The numbers: free tiles (MapArt.Free) in Blood's range (classic: a .MAP for Blood,
/// Refreshed Supply and NBlood) or past it (extended: .MAPX, NBlood and Chosen), the lowest first, all in one run when
/// one is long enough. The output folder's tiles.txt keeps which picture has which number ("tile name [xoffset
/// yoffset]", the offsets moving a sprite's picture), so packing again after a picture changed keeps every number.
/// </summary>
public static class ArtPacker
{
    public static readonly string[] Extensions = { ".png", ".jpg", ".jpeg", ".bmp", ".tga", ".webp" };
    public const string Manifest = "tiles.txt";
    /// <summary>The first ART file's number (Blood's are tiles000..023).</summary>
    public const int FirstFile = 24;

    public sealed class Result
    {
        public readonly List<(int tile, string name, int w, int h)> Tiles = new();
        public readonly List<string> Files = new();
        public readonly List<string> Warnings = new();
        public string Error;
        public int Kept, New;
    }

    /// <summary>The pictures in a folder, by name.</summary>
    public static List<string> Pictures(string folder) => Directory.Exists(folder)
        ? Directory.GetFiles(folder).Where(f => Extensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .OrderBy(f => Path.GetFileName(f).ToLowerInvariant(), StringComparer.Ordinal).ToList()
        : new List<string>();

    /// <summary>Packs the pictures of images into outDir (others: the tiles taken already, Blood's and the map's other
    /// sets); classic: in Blood's range.</summary>
    public static Result Pack(string images, string outDir, bool classic, ArtSet others, Palette pal)
    {
        var r = new Result();
        var pics = Pictures(images);
        if (pics.Count == 0)
        {
            r.Error = $"no pictures in {images} ({string.Join(", ", Extensions)})";
            return r;
        }
        if (pal == null)
        {
            r.Error = "no Blood palette (Edit > Preferences: the Blood folder)";
            return r;
        }
        Directory.CreateDirectory(outDir);
        // the numbers packed before (kept while still free of the others' pictures)
        var old = ReadManifest(Path.Combine(outDir, Manifest), out var oldFiles);
        var mine = new HashSet<int>(old.Values.Select(v => v.tile));
        var taken = new HashSet<int>();
        var number = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in pics)
        {
            string name = Path.GetFileName(p);
            if (old.TryGetValue(name, out var o) && MapArt.Free(others, o.tile, classic, mine) && taken.Add(o.tile))
            {
                number[name] = o.tile;
                r.Kept++;
            }
        }
        var need = pics.Select(Path.GetFileName).Where(n => !number.ContainsKey(n)).ToList();
        var slots = Slots(others, classic, mine, taken, need.Count);
        if (slots.Count < need.Count)
        {
            r.Error = $"{need.Count} pictures need numbers but only {slots.Count} tiles are free {(classic ? $"below {MapArt.BloodTiles} (classic)" : $"from {MapArt.BloodTiles} to {MapArt.MaxTile}")}";
            return r;
        }
        for (int i = 0; i < need.Count; i++)
        {
            number[need[i]] = slots[i];
            r.New++;
        }
        // the pictures as tiles
        var tiles = new Dictionary<int, Tile>();
        foreach (var p in pics)
        {
            string name = Path.GetFileName(p);
            var img = new Image();
            if (img.Load(p) != Error.Ok || img.IsEmpty())
            {
                r.Warnings.Add($"{name}: can't be read (left out)");
                continue;
            }
            if (img.IsCompressed())
                img.Decompress();
            img.Convert(Image.Format.Rgba8);
            int w = img.GetWidth(), h = img.GetHeight();
            if (w > 4096 || h > 4096)
            {
                r.Warnings.Add($"{name}: {w} x {h} is bigger than a tile may be (4096 at most; left out)");
                continue;
            }
            int n = number[name];
            var t = MapArt.Quantize(img.GetData(), w, h, pal, n);
            old.TryGetValue(name, out var o);
            t.Picanm = MapArt.Picanm(o.xofs, o.yofs);
            tiles[n] = t;
            r.Tiles.Add((n, name, w, h));
            if ((h & (h - 1)) != 0)
                r.Warnings.Add($"{name}: {h} high (not a power of two): on a wall it repeats as {Data.TileImages.WallHeight(h)} rows (the rest the next column's)");
        }
        if (tiles.Count == 0)
        {
            r.Error = "no picture could be packed";
            return r;
        }
        // the old files gone (only those this folder's tiles.txt names), the new ones written: one a run
        foreach (var f in oldFiles)
        {
            string path = Path.Combine(outDir, f);
            if (File.Exists(path))
                File.Delete(path);
        }
        var runs = MapArt.Runs(tiles.Keys);
        int fileNo = FirstFile;
        foreach (var (first, last) in runs)
        {
            string file = $"tiles{fileNo++:000}.art";
            File.WriteAllBytes(Path.Combine(outDir, file), MapArt.WriteArt(first, last, tiles));
            r.Files.Add(file);
        }
        if (fileNo > 200)
            r.Warnings.Add($"{r.Files.Count} ART files (Build's ports read tiles000..199.art from a game folder: fewer runs of numbers, fewer files)");
        WriteManifest(Path.Combine(outDir, Manifest), r, old);
        return r;
    }

    /// <summary>count free tile numbers (not taken): the lowest run long enough for all, else the lowest free ones.</summary>
    static List<int> Slots(ArtSet others, bool classic, HashSet<int> mine, HashSet<int> taken, int count)
    {
        var free = new List<int>();
        if (count == 0)
            return free;
        int lo = classic ? 0 : MapArt.BloodTiles, hi = classic ? MapArt.BloodTiles - 1 : MapArt.MaxTile;
        for (int t = lo; t <= hi; t++)
            if (!taken.Contains(t) && MapArt.Free(others, t, classic, mine))
                free.Add(t);
        // one run holding them all
        foreach (var (first, last) in MapArt.Runs(free))
            if (last - first + 1 >= count)
                return Enumerable.Range(first, count).ToList();
        return free.Take(count).ToList();
    }

    /// <summary>tiles.txt: picture name -> its tile and offsets, and the ART files written (a "files" line).</summary>
    public static Dictionary<string, (int tile, int xofs, int yofs)> ReadManifest(string path, out List<string> files)
    {
        var m = new Dictionary<string, (int, int, int)>(StringComparer.OrdinalIgnoreCase);
        files = new List<string>();
        if (!File.Exists(path))
            return m;
        foreach (var raw in File.ReadAllLines(path))
        {
            string line = raw.Trim();
            if (line == "" || line.StartsWith('#'))
                continue;
            if (line.StartsWith("files ", StringComparison.Ordinal))
            {
                // (only ART file names: never a path out of the folder)
                files.AddRange(line[6..].Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Where(f => f == Path.GetFileName(f) && f.EndsWith(".art", StringComparison.OrdinalIgnoreCase)));
                continue;
            }
            // tile, the name (it may hold spaces: the offsets are the last two numbers when there are three or more parts)
            var p = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (p.Length < 2 || !int.TryParse(p[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int tile))
                continue;
            int xo = 0, yo = 0, end = p.Length;
            if (p.Length >= 4 && int.TryParse(p[^2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int a)
                && int.TryParse(p[^1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int b))
            {
                (xo, yo) = (a, b);
                end -= 2;
            }
            m[string.Join(' ', p[1..end])] = (tile, xo, yo);
        }
        return m;
    }

    static void WriteManifest(string path, Result r, Dictionary<string, (int tile, int xofs, int yofs)> old)
    {
        var sb = new StringBuilder();
        sb.Append("# Chosen Forge: the tile number of each picture packed here (kept when packed again: Map Options > Art).\n");
        sb.Append("# tile picture [xoffset yoffset]: the offsets move a sprite's picture (pixels), edit them and pack again.\n");
        sb.Append("files ").Append(string.Join(' ', r.Files)).Append('\n');
        foreach (var (tile, name, _, _) in r.Tiles.OrderBy(t => t.tile))
        {
            sb.Append(tile.ToString(CultureInfo.InvariantCulture)).Append(' ').Append(name);
            if (old.TryGetValue(name, out var o) && (o.xofs != 0 || o.yofs != 0))
                sb.Append(' ').Append(o.xofs.ToString(CultureInfo.InvariantCulture)).Append(' ').Append(o.yofs.ToString(CultureInfo.InvariantCulture));
            sb.Append('\n');
        }
        File.WriteAllText(path, sb.ToString());
    }
}
