// Chosen Blood module (GPL v2): Blood's SEQ, QAV and SFX resources and its DAT tables, as NBlood (source/blood/src)
// reads them (pipeline/blood_assets.py).
using System;
using System.Collections.Generic;
using System.IO;

namespace Chosen.Shared.Blood.Files;

/// <summary>A SEQ frame (seq.h SEQFRAME's bit fields).</summary>
public sealed class SeqFrame
{
    public int Tile, Transparent, Transparent2, Blockable, Hittable, XRepeat, YRepeat, Shade, Pal, Trigger, Smoke, Autoaim,
        Pushable, PlaySound, Invisible, XFlip, YFlip;
    // NoOne's extensions: the frame's sound picked from that many after the sequence's, a step sound of the floor
    public int SoundRange, SurfaceSound;

    /// <param name="extended">a sequence newer than version 3.0: its frames' tile numbers and palettes have high bits
    /// (tile2, pal2; a 3.0 file's are not to be read, seq.cpp)</param>
    public SeqFrame(uint a, uint b, bool extended = false)
    {
        Tile = (int)(a & 0xFFF);
        if (extended)
            Tile |= (int)(((b >> 21) & 0xF) << 12);
        Transparent = (int)((a >> 12) & 1);
        Transparent2 = (int)((a >> 13) & 1);
        Blockable = (int)((a >> 14) & 1);
        Hittable = (int)((a >> 15) & 1);
        XRepeat = (int)((a >> 16) & 0xFF);
        YRepeat = (int)((a >> 24) & 0xFF);
        Shade = (sbyte)(b & 0xFF);
        Pal = (int)((b >> 8) & 0x1F);
        if (extended)
            Pal |= (int)(((b >> 30) & 3) << 5);
        SoundRange = (int)((b >> 25) & 0xF);
        SurfaceSound = (int)((b >> 29) & 1);
        Trigger = (int)((b >> 13) & 1);
        Smoke = (int)((b >> 14) & 1);
        Autoaim = (int)((b >> 15) & 1);
        Pushable = (int)((b >> 16) & 1);
        PlaySound = (int)((b >> 17) & 1);
        Invisible = (int)((b >> 18) & 1);
        XFlip = (int)((b >> 19) & 1);
        YFlip = (int)((b >> 20) & 1);
    }

    /// <summary>The flags as the game's tables pack them (1 trans, 2 trans2, 4 blockable ... 2048 yflip).</summary>
    public int Flags => Transparent | Transparent2 << 1 | Blockable << 2 | Hittable << 3 | Trigger << 4 | Smoke << 5
        | Autoaim << 6 | Pushable << 7 | PlaySound << 8 | Invisible << 9 | XFlip << 10 | YFlip << 11;
}

public sealed class Seq
{
    public int Id, Version, NFrames, Ticks, Sound, Flags;
    public readonly List<SeqFrame> Frames = new();

    /// <summary>A SEQ resource, or null when it isn't one (or is cut short).</summary>
    public static Seq Parse(int id, byte[] d)
    {
        if (d.Length < 16 || d[0] != 'S' || d[1] != 'E' || d[2] != 'Q' || d[3] != 0x1a)
            return null;
        var s = new Seq
        {
            Id = id, Version = BitConverter.ToInt16(d, 4), NFrames = BitConverter.ToInt16(d, 6), Ticks = BitConverter.ToInt16(d, 8),
            Sound = BitConverter.ToInt16(d, 10), Flags = BitConverter.ToInt32(d, 12),
        };
        if (s.NFrames < 0 || 16 + s.NFrames * 8 > d.Length)
            return null;
        for (int i = 0; i < s.NFrames; i++)
            s.Frames.Add(new SeqFrame(BitConverter.ToUInt32(d, 16 + i * 8), BitConverter.ToUInt32(d, 20 + i * 8), (s.Version & 0xff) != 0));
        return s;
    }
}

public sealed class QavTile
{
    public int Picnum, X, Y, Z, Stat, Shade, Pal, Angle;
}

public sealed class QavFrame
{
    public int Callback, Sound, Priority;
    public readonly QavTile[] Tiles = new QavTile[8];
}

public sealed class Qav
{
    public int Id, NFrames, Ticks, Duration, X, Y;
    public string Name = "";
    public readonly List<QavFrame> Frames = new();

    /// <summary>A QAV resource (204 bytes a frame, 8 layers of 24), or null when cut short.</summary>
    public static Qav Parse(int id, string name, byte[] d)
    {
        if (d.Length < 28)
            return null;
        var q = new Qav
        {
            Id = id, Name = name, NFrames = BitConverter.ToInt32(d, 8), Ticks = BitConverter.ToInt32(d, 12),
            Duration = BitConverter.ToInt32(d, 16), X = BitConverter.ToInt32(d, 20), Y = BitConverter.ToInt32(d, 24),
        };
        for (int i = 0; i < q.NFrames; i++)
        {
            int b = 0x24 + i * 204;
            if (b + 12 + 8 * 24 > d.Length)
                return null;
            var fr = new QavFrame { Callback = BitConverter.ToInt32(d, b), Sound = BitConverter.ToInt32(d, b + 4), Priority = d[b + 8] };
            for (int t = 0; t < 8; t++)
            {
                int tb = b + 12 + t * 24;
                fr.Tiles[t] = new QavTile
                {
                    Picnum = BitConverter.ToInt32(d, tb), X = BitConverter.ToInt32(d, tb + 4), Y = BitConverter.ToInt32(d, tb + 8),
                    Z = BitConverter.ToInt32(d, tb + 12), Stat = BitConverter.ToInt32(d, tb + 16), Shade = (sbyte)d[tb + 20],
                    Pal = d[tb + 21], Angle = BitConverter.ToUInt16(d, tb + 22),
                };
            }
            q.Frames.Add(fr);
        }
        return q;
    }
}

public sealed class Sfx
{
    public int Id, RelVol, Pitch, PitchRange, Format, LoopStart;
    public string Raw = "";

    static readonly int[] Rates = { 11025, 11025, 11025, 11025, 11025, 22050, 22050, 22050, 22050, 44100, 44100, 44100, 44100 };

    /// <summary>The sample rate of a sound format (Blood's 0..12; else 11025).</summary>
    public static int Rate(int format) => format >= 0 && format < 13 ? Rates[format] : 11025;

    public static Sfx Parse(int id, byte[] d)
    {
        if (d.Length < 20)
            return null;
        var s = new Sfx
        {
            Id = id, RelVol = BitConverter.ToInt32(d, 0), Pitch = BitConverter.ToInt32(d, 4), PitchRange = BitConverter.ToInt32(d, 8),
            Format = BitConverter.ToInt32(d, 12), LoopStart = BitConverter.ToInt32(d, 16),
        };
        int end = Math.Min(d.Length, 29), n = 20;
        while (n < end && d[n] != 0)
            n++;
        s.Raw = System.Text.Encoding.Latin1.GetString(d, 20, n - 20);
        return s;
    }
}

public static class Assets
{
    /// <summary>Every resource of a type, parsed, by id (a later entry of the same id wins; unreadable ones skipped).</summary>
    public static SortedDictionary<int, T> ByType<T>(Rff rff, string type, Func<RffEntry, byte[], T> parse) where T : class
    {
        var d = new SortedDictionary<int, T>();
        foreach (var e in rff.Entries)
            if (e.Type.Equals(type, StringComparison.OrdinalIgnoreCase) && parse(e, rff.Read(e)) is { } v)
                d[e.Id] = v;
        return d;
    }

    public static SortedDictionary<int, Seq> Seqs(Rff rff) => ByType(rff, "SEQ", (e, d) => Seq.Parse(e.Id, d));
    public static SortedDictionary<int, Qav> Qavs(Rff rff) => ByType(rff, "QAV", (e, d) => Qav.Parse(e.Id, e.Name, d));
    public static SortedDictionary<int, Sfx> Sfxs(Rff rff) => ByType(rff, "SFX", (e, d) => Sfx.Parse(e.Id, d));

    /// <summary>A DAT table of the install (surface.dat: signed bytes; voxel.dat: int16), or null without the file.</summary>
    public static int[] Dat(string folder, string name, int count, bool int16)
    {
        string p = BloodInstall.FindFile(folder, name);
        if (p == null)
            return null;
        var d = File.ReadAllBytes(p);
        int size = int16 ? 2 : 1;
        count = Math.Min(count, d.Length / size);
        var v = new int[count];
        for (int i = 0; i < count; i++)
            v[i] = int16 ? BitConverter.ToInt16(d, i * 2) : (sbyte)d[i];
        return v;
    }
}
