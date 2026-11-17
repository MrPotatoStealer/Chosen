// Chosen Blood module (GPL v2): Build's ART tile files as Blood ships them (pipeline/blood_assets.py).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Chosen.Shared.Blood.Files;

/// <summary>A tile: its size, picanm (animation, offsets, Blood's view type) and its palette indices, stored column by
/// column as in the ART file.</summary>
public sealed class Tile
{
    public int Number, Width, Height;
    public uint Picanm;
    public byte[] Pixels = Array.Empty<byte>();

    public int AnimFrames => (int)(Picanm & 63);
    public int AnimType => (int)((Picanm >> 6) & 3);
    public int XOffset => (sbyte)((Picanm >> 8) & 255);
    public int YOffset => (sbyte)((Picanm >> 16) & 255);
    public int AnimSpeed => (int)((Picanm >> 24) & 15);
    /// <summary>Blood's view type for sprites (picanm bits 28-30): 0 single, 1-5 rotations, 6/7 voxel.</summary>
    public int View => (int)((Picanm >> 28) & 7);

    public bool Empty => Width <= 0 || Height <= 0;

    /// <summary>The palette index at (x, y), row-major view of the column-major data.</summary>
    public byte At(int x, int y) => Pixels[x * Height + y];

    /// <summary>All indices row by row (width × height).</summary>
    public byte[] Rows()
    {
        var r = new byte[Width * Height];
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
                r[y * Width + x] = Pixels[x * Height + y];
        return r;
    }
}

/// <summary>The tiles of a set of ART files (TILES000.ART..), by number.</summary>
public sealed class ArtSet
{
    readonly Dictionary<int, Tile> _tiles = new();

    public int Count => _tiles.Count;
    public IEnumerable<int> Numbers => _tiles.Keys;

    public Tile this[int n] => _tiles.TryGetValue(n, out var t) ? t : null;

    /// <summary>Every tilesNNN.art in folder (names matched case-insensitively), in name order.</summary>
    public static ArtSet Load(string folder)
    {
        var set = new ArtSet();
        var files = Directory.GetFiles(folder)
            .Where(f => System.Text.RegularExpressions.Regex.IsMatch(System.IO.Path.GetFileName(f), @"^tiles\d+\.art$",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            .OrderBy(f => System.IO.Path.GetFileName(f).ToLowerInvariant());
        foreach (var f in files)
            set.AddFile(File.ReadAllBytes(f));
        return set;
    }

    /// <summary>One ART file's tiles (a later file's tile of the same number replaces the earlier one's); the numbers
    /// of its range. A file cut short or with a range past MapArt.MaxTile + 256: InvalidDataException.</summary>
    public List<int> AddFile(byte[] d)
    {
        if (d.Length < 16)
            throw new InvalidDataException("not an ART file (too short)");
        int start = BitConverter.ToInt32(d, 8), end = BitConverter.ToInt32(d, 12);
        int n = end - start + 1;
        if (start < 0 || n <= 0 || end > MapArt.MaxTile + 256 || 16 + (long)n * 8 > d.Length)
            throw new InvalidDataException($"not an ART file (tiles {start}..{end})");
        int sx = 16, sy = 16 + n * 2, pa = 16 + n * 4, off = 16 + n * 8;
        var range = new List<int>(n);
        for (int i = 0; i < n; i++)
        {
            int w = BitConverter.ToInt16(d, sx + i * 2), h = BitConverter.ToInt16(d, sy + i * 2);
            int size = w > 0 && h > 0 ? w * h : 0;
            if (off + size > d.Length)
                throw new InvalidDataException($"ART file cut short at tile {start + i}");
            var t = new Tile { Number = start + i, Width = w, Height = h, Picanm = BitConverter.ToUInt32(d, pa + i * 4), Pixels = new byte[size] };
            Array.Copy(d, off, t.Pixels, 0, size);
            off += size;
            _tiles[t.Number] = t;
            range.Add(t.Number);
        }
        return range;
    }

    /// <summary>A tile put in (replacing the one of its number).</summary>
    public void Put(Tile t) => _tiles[t.Number] = t;
}
