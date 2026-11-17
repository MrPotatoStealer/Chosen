// Chosen Blood module (GPL v2): the game's data tables made from Blood's resources (pipeline/export.py's tiles, seq,
// qav and sfx tables and gen_qcdata.py's runtime packing of them), written line for line as those scripts write them.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Chosen.Shared.Blood.Files;

public static class Tables
{
    static string I(long v) => v.ToString(CultureInfo.InvariantCulture);

    static string Line(params long[] v) => string.Join(" ", v.Select(I)) + "\n";

    /// <summary>data/tiles.txt: every tile with a picture: size, offsets, animation, view, and SURFACE.DAT / SHADE.DAT /
    /// VOXEL.DAT's entries (0, 0, -1 where the file or entry is missing).</summary>
    public static string TilesTxt(ArtSet art, int[] surface, int[] shade, int[] voxel)
    {
        var sb = new StringBuilder("// tile w h xofs yofs anim animtype speed view surface shade voxel\n");
        foreach (int n in art.Numbers.OrderBy(n => n))
        {
            var t = art[n];
            if (t.Width <= 0 || t.Height <= 0)
                continue;
            sb.Append(Line(n, t.Width, t.Height, t.XOffset, t.YOffset, t.AnimFrames, t.AnimType, t.AnimSpeed, t.View,
                surface != null && n < surface.Length ? surface[n] : 0, shade != null && n < shade.Length ? shade[n] : 0,
                voxel != null && n < voxel.Length ? voxel[n] : -1));
        }
        return sb.ToString();
    }

    /// <summary>data/rt_tiles.txt: "tile size ofs misc" packed (Data/BloodData.cs Tiles reads it).</summary>
    public static string RtTiles(ArtSet art, int[] surface, int[] shade, int[] voxel)
    {
        var rows = art.Numbers.Where(n => art[n].Width > 0 && art[n].Height > 0).OrderBy(n => n).ToList();
        var sb = new StringBuilder();
        foreach (int n in rows)
        {
            var t = art[n];
            int surf = surface != null && n < surface.Length ? surface[n] : 0;
            int sh = shade != null && n < shade.Length ? shade[n] : 0;
            int vox = voxel != null && n < voxel.Length ? voxel[n] : -1;
            var (size, ofs, misc) = RtRow(t, surf, sh, vox >= 0);
            if (size == 0)
                continue;
            sb.Append(Line(n, size, ofs, misc));
        }
        return sb.ToString();
    }

    /// <summary>One tile's rt_tiles.txt numbers: size, offsets, misc (the game's Tiles; a map's own art adds rows so).</summary>
    public static (long size, long ofs, long misc) RtRow(Tile t, int surface, int shade, bool voxel)
    {
        long size = t.Width + (long)t.Height * 1024;
        long ofs = (t.XOffset + 128) + (t.YOffset + 128) * 256L + t.AnimFrames * 65536L;
        long misc = t.View + (surface & 31) * 8L + ((shade + 128) & 255) * 256L + (voxel ? 1 : 0) * 65536L
            + (t.AnimType & 3) * 131072L + (t.AnimSpeed & 15) * 524288L;
        return (size, ofs, misc);
    }

    /// <summary>data/seq.txt (the SEQ animations, by id).</summary>
    public static string SeqTxt(SortedDictionary<int, Seq> seqs)
    {
        var sb = new StringBuilder(
            "// seq <id> <nframes> <ticksPerFrame> <soundId> <flags>\n" +
            "// then per frame: <tile> <flags> <xrepeat> <yrepeat> <shade> <pal>\n" +
            "// frame flags: 1 trans 2 trans2 4 blockable 8 hittable 16 trigger 32 smoke 64 autoaim\n" +
            "//              128 pushable 256 playSound 512 invisible 1024 xflip 2048 yflip\n");
        foreach (var (id, s) in seqs)
        {
            sb.Append("seq ").Append(Line(id, s.NFrames, s.Ticks, s.Sound, s.Flags));
            foreach (var f in s.Frames)
                sb.Append(Line(f.Tile, f.Flags, f.XRepeat, f.YRepeat, f.Shade, f.Pal));
        }
        return sb.ToString();
    }

    /// <summary>data/rt_seq.txt: "S id info first sound" per SEQ, "F n a b c" per frame (Data/SeqData.cs): a the tile
    /// (13 bits) and the flags, b the repeats and flips, c the shade, the palette's low five bits, the sound range (from
    /// 8192), the step sound (131072) and the palette's high bits (from 262144).</summary>
    public static string RtSeq(SortedDictionary<int, Seq> seqs)
    {
        var s1 = new StringBuilder();
        var s2 = new StringBuilder();
        int n = 0;
        foreach (var (id, s) in seqs)
        {
            s1.Append("S ").Append(Line(id, s.Frames.Count + (s.Ticks & 255) * 256L + (s.Flags & 255) * 65536L, n, s.Sound));
            foreach (var f in s.Frames)
            {
                int flags = f.Flags;
                s2.Append("F ").Append(Line(n, f.Tile + (flags & 1023) * 8192L, f.XRepeat + f.YRepeat * 256L + ((flags >> 10) & 3) * 65536L,
                    ((f.Shade + 128) & 255) + (f.Pal & 31) * 256L + (f.SoundRange & 15) * 8192L + (f.SurfaceSound & 1) * 131072L
                    + (f.Pal >> 5) * 262144L));
                n++;
            }
        }
        return s1.Append(s2).ToString();
    }

    /// <summary>data/qav.txt (the QAV weapon and HUD animations, by id; layers with a picture).</summary>
    public static string QavTxt(SortedDictionary<int, Qav> qavs)
    {
        var sb = new StringBuilder(
            "// qav <id> <name> <nframes> <ticksPerFrame> <duration> <x> <y>\n" +
            "// frame <callback> <sound> <priority> <nlayers>, then layers: <picnum> <x> <y> <z> <stat> <shade> <pal> <angle>\n");
        foreach (var (id, q) in qavs)
        {
            sb.Append($"qav {I(id)} {q.Name} ").Append(Line(q.NFrames, q.Ticks, q.Duration, q.X, q.Y));
            foreach (var f in q.Frames)
            {
                var layers = f.Tiles.Where(t => t.Picnum > 0).ToList();
                sb.Append("frame ").Append(Line(f.Callback, f.Sound, f.Priority, layers.Count));
                foreach (var t in layers)
                    sb.Append(Line(t.Picnum, t.X, t.Y, t.Z, t.Stat, t.Shade, t.Pal, t.Angle));
            }
        }
        return sb.ToString();
    }

    /// <summary>data/rt_qav.txt: "Q id ticks duration nframes first" per QAV, "F n callback sound" per frame (a callback of
    /// none stored as 0; the priority times 256 added).</summary>
    public static string RtQav(SortedDictionary<int, Qav> qavs)
    {
        var s1 = new StringBuilder();
        var s2 = new StringBuilder();
        int n = 0;
        foreach (var (id, q) in qavs)
        {
            s1.Append("Q ").Append(Line(id, q.Ticks, q.Duration, q.Frames.Count, n));
            foreach (var f in q.Frames)
            {
                s2.Append("F ").Append(Line(n, Math.Max(f.Callback, 0) + 256L * (f.Priority & 255), f.Sound));
                n++;
            }
        }
        return s1.Append(s2).ToString();
    }

    /// <summary>A sound's row of data/sfx.txt and data/rt_sfx.txt: its id, its file (the raw's name, _loop for the looped
    /// copy), and its numbers.</summary>
    public sealed record SfxRow(int Id, string File, int RelVol, int Pitch, int PitchRange, int Format, int LoopStart);

    public static string SfxTxt(IEnumerable<SfxRow> rows)
    {
        var sb = new StringBuilder("// <id> <raw> <relvol> <pitch> <pitchrange> <format> <loopstart>\n");
        foreach (var r in rows)
            sb.Append($"{I(r.Id)} {r.File} ").Append(Line(r.RelVol, r.Pitch, r.PitchRange, r.Format, r.LoopStart));
        return sb.ToString();
    }

    public static string RtSfx(IEnumerable<SfxRow> rows)
    {
        var sb = new StringBuilder();
        foreach (var r in rows.OrderBy(r => r.Id))
            sb.Append($"{I(r.Id)} blood/{r.File}.wav ").Append(Line(r.RelVol, r.Pitch, r.PitchRange, r.LoopStart >= 0 ? 1 : 0));
        return sb.ToString();
    }

    /// <summary>A Blood sample as a WAV (8-bit mono; a looped one with a cue chunk at its loop point), as export.py writes
    /// it.</summary>
    public static byte[] Wav(byte[] pcm, int rate, int loopStart)
    {
        using var ms = new System.IO.MemoryStream();
        var w = new System.IO.BinaryWriter(ms);
        var chunks = new System.IO.MemoryStream();
        var c = new System.IO.BinaryWriter(chunks);
        c.Write("fmt "u8);
        c.Write(16);
        c.Write((ushort)1); c.Write((ushort)1); c.Write(rate); c.Write(rate); c.Write((ushort)1); c.Write((ushort)8);
        c.Write("data"u8);
        c.Write(pcm.Length);
        c.Write(pcm);
        if ((pcm.Length & 1) != 0)
            c.Write((byte)128);
        if (loopStart >= 0)
        {
            c.Write("cue "u8);
            c.Write(28);
            c.Write(1);
            c.Write(1); c.Write(loopStart); c.Write("data"u8); c.Write(0); c.Write(0); c.Write(loopStart);
        }
        c.Flush();
        w.Write("RIFF"u8);
        w.Write(4 + (int)chunks.Length);
        w.Write("WAVE"u8);
        w.Write(chunks.ToArray());
        w.Flush();
        return ms.ToArray();
    }
}
