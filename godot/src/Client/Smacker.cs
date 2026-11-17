using System;
using System.Collections.Generic;
using System.IO;

namespace Chosen.Client;

/// <summary>
/// A Smacker video decoder (the DOS intro logos and cutscenes), ported from NBlood's libsmackerdec (itself from
/// FFmpeg's smacker.c; LGPL 2.1): Huffman-coded 4x4 blocks of palette indices over the previous frame, a palette
/// that changes per frame. Video only: NBlood plays the sound from the WAV files beside the videos (or SOUNDS.RFF),
/// never the audio inside them, so the audio packets are skipped.
/// </summary>
public sealed class Smacker
{
    public readonly int Width, Height, FrameCount;
    public readonly float FrameRate;
    /// <summary>The current frame: one palette index per pixel, row by row.</summary>
    public readonly byte[] Picture;
    /// <summary>The current palette, 256 RGB triples (8 bits each).</summary>
    public readonly byte[] Palette = new byte[768];
    public int CurrentFrame { get; private set; }

    const int Node = unchecked((int)0x80000000);
    const int MaxAudioTracks = 7;
    const int PaletteFlag = 0x01, RingFrameFlag = 0x01;
    const int BlockMono = 0, BlockFull = 1, BlockSkip = 2, BlockFill = 3;

    static readonly int[] BlockRuns =
    {
        1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30,
        31, 32, 33, 34, 35, 36, 37, 38, 39, 40, 41, 42, 43, 44, 45, 46, 47, 48, 49, 50, 51, 52, 53, 54, 55, 56, 57, 58,
        59, 128, 256, 512, 1024, 2048,
    };

    static readonly byte[] SmkPal =
    {
        0x00, 0x04, 0x08, 0x0C, 0x10, 0x14, 0x18, 0x1C, 0x20, 0x24, 0x28, 0x2C, 0x30, 0x34, 0x38, 0x3C,
        0x41, 0x45, 0x49, 0x4D, 0x51, 0x55, 0x59, 0x5D, 0x61, 0x65, 0x69, 0x6D, 0x71, 0x75, 0x79, 0x7D,
        0x82, 0x86, 0x8A, 0x8E, 0x92, 0x96, 0x9A, 0x9E, 0xA2, 0xA6, 0xAA, 0xAE, 0xB2, 0xB6, 0xBA, 0xBE,
        0xC3, 0xC7, 0xCB, 0xCF, 0xD3, 0xD7, 0xDB, 0xDF, 0xE3, 0xE7, 0xEB, 0xEF, 0xF3, 0xF7, 0xFB, 0xFF,
    };

    readonly byte[] _file;
    readonly bool _ver4;
    readonly uint[] _frameSizes;
    readonly byte[] _frameFlags;
    int _nextPos;
    int[] _mmap, _mclr, _full, _type;
    readonly int[] _mmapLast = new int[3], _mclrLast = new int[3], _fullLast = new int[3], _typeLast = new int[3];

    /// <summary>Opens a .smk file (throws on a file that isn't one).</summary>
    public Smacker(string path)
    {
        _file = File.ReadAllBytes(path);
        int pos = 0;
        string sig = System.Text.Encoding.ASCII.GetString(_file, 0, 4);
        if (sig != "SMK2" && sig != "SMK4")
            throw new InvalidDataException("not a Smacker file: " + path);
        _ver4 = sig == "SMK4";
        pos = 4;
        uint U32() { uint v = BitConverter.ToUInt32(_file, pos); pos += 4; return v; }
        Width = (int)U32();
        Height = (int)U32();
        int frames = (int)U32();
        int frameRate = (int)U32();
        FrameRate = frameRate > 0 ? 1000 / frameRate : frameRate < 0 ? 100000 / -frameRate : 10;
        uint flags = U32();
        if ((flags & RingFrameFlag) != 0)
            frames++;
        pos += 4 * MaxAudioTracks;               // audio sizes
        uint treeSize = U32();
        int mmapSize = (int)U32(), mclrSize = (int)U32(), fullSize = (int)U32(), typeSize = (int)U32();
        pos += 4 * MaxAudioTracks + 4;           // audio flags, reserved
        if (frames <= 0 || frames > 0xFFFFFF)
            throw new InvalidDataException("bad Smacker frame count: " + path);
        FrameCount = frames;
        _frameSizes = new uint[frames];
        _frameFlags = new byte[frames];
        for (int i = 0; i < frames; i++)
            _frameSizes[i] = U32();
        for (int i = 0; i < frames; i++)
            _frameFlags[i] = _file[pos++];
        Picture = new byte[Width * Height];
        var bits = new BitReader(_file, pos, (int)treeSize);
        _mmap = HeaderTree(bits, _mmapLast, mmapSize);
        _mclr = HeaderTree(bits, _mclrLast, mclrSize);
        _full = HeaderTree(bits, _fullLast, fullSize);
        _type = HeaderTree(bits, _typeLast, typeSize);
        _nextPos = pos + (int)treeSize;
    }

    // ---------------------------------------------------------------- bits and Huffman tables
    sealed class BitReader
    {
        readonly byte[] _data;
        readonly int _start, _bits;
        int _pos;
        public BitReader(byte[] data, int start, int size) { _data = data; _start = start; _bits = size * 8; }
        public int Position => _pos;
        public int GetBit()
        {
            int i = _start + (_pos >> 3);
            int b = _pos < _bits && i < _data.Length ? (_data[i] >> (_pos & 7)) & 1 : 0;
            _pos++;
            return b;
        }
        public int GetBits(int n)
        {
            int v = 0;
            for (int i = 0; i < n; i++)
                v |= GetBit() << i;
            return v;
        }
    }

    sealed class Huff
    {
        public int Length, MaxLength, Current;
        public uint[] Bits;
        public int[] Lengths, Values;
        public Huff(int length)
        {
            Length = length;
            Bits = new uint[length];
            Lengths = new int[length];
            Values = new int[length];
        }
    }

    /// <summary>VLC_InitTable / VLC_GetCodeBits: codes by length (bits read least significant first).</summary>
    sealed class Vlc
    {
        readonly List<Dictionary<uint, int>> _byLength = new();
        public bool Empty => _byLength.Count == 0;
        public Vlc(Huff h)
        {
            for (int i = 0; i < h.MaxLength; i++)
                _byLength.Add(new Dictionary<uint, int>());
            for (int i = 0; i < h.Current; i++)
                if (h.Lengths[i] > 0)
                    _byLength[h.Lengths[i] - 1].TryAdd(h.Bits[i], i);
        }
        public Vlc() { }
        public int Get(BitReader bits)
        {
            uint code = 0;
            for (int i = 0; i < _byLength.Count; i++)
            {
                code |= (uint)bits.GetBit() << i;
                if (_byLength[i].TryGetValue(code, out int sym))
                    return sym;
            }
            return 0;
        }
    }

    static void DecodeTree(BitReader bits, Huff hc, uint prefix, int length)
    {
        if (bits.GetBit() == 0)
        {
            if (hc.Current >= 256)
                throw new InvalidDataException("Smacker tree too big");
            hc.Bits[hc.Current] = length != 0 ? prefix : 0;
            hc.Lengths[hc.Current] = length;
            hc.Values[hc.Current] = bits.GetBits(8);
            hc.Current++;
            if (hc.MaxLength < length)
                hc.MaxLength = length;
            return;
        }
        length++;
        DecodeTree(bits, hc, prefix, length);
        DecodeTree(bits, hc, prefix | (1u << (length - 1)), length);
    }

    sealed class BigTreeCtx
    {
        public Vlc V1 = new(), V2 = new();
        public int[] Recode1 = new int[256], Recode2 = new int[256];
        public readonly int[] Escapes = new int[3];
        public int[] Last;
    }

    static int DecodeBigTree(BitReader bits, Huff hc, BigTreeCtx ctx)
    {
        if (bits.GetBit() == 0)
        {
            if (hc.Current >= hc.Length)
                throw new InvalidDataException("Smacker big tree too big");
            int i1 = ctx.V1.Empty ? 0 : ctx.V1.Get(bits);
            int i2 = ctx.V2.Empty ? 0 : ctx.V2.Get(bits);
            int val = ctx.Recode1[i1] | (ctx.Recode2[i2] << 8);
            if (val == ctx.Escapes[0]) { ctx.Last[0] = hc.Current; val = 0; }
            else if (val == ctx.Escapes[1]) { ctx.Last[1] = hc.Current; val = 0; }
            else if (val == ctx.Escapes[2]) { ctx.Last[2] = hc.Current; val = 0; }
            hc.Values[hc.Current++] = val;
            return 1;
        }
        int t = hc.Current++;
        int r = DecodeBigTree(bits, hc, ctx);
        hc.Values[t] = Node | r;
        r++;
        r += DecodeBigTree(bits, hc, ctx);
        return r;
    }

    /// <summary>DecodeHeaderTrees' part for one table (a 0 bit: an empty table).</summary>
    static int[] HeaderTree(BitReader bits, int[] last, int size)
    {
        if (bits.GetBit() == 0)
        {
            last[0] = last[1] = last[2] = 1;
            return new int[2];
        }
        var tmp1 = new Huff(256);
        var tmp2 = new Huff(256);
        var ctx = new BigTreeCtx();
        if (bits.GetBit() != 0)
        {
            DecodeTree(bits, tmp1, 0, 0);
            bits.GetBit();
            ctx.V1 = new Vlc(tmp1);
        }
        if (bits.GetBit() != 0)
        {
            DecodeTree(bits, tmp2, 0, 0);
            bits.GetBit();
            ctx.V2 = new Vlc(tmp2);
        }
        for (int i = 0; i < 3; i++)
        {
            ctx.Escapes[i] = bits.GetBits(8);
            ctx.Escapes[i] |= bits.GetBits(8) << 8;
        }
        last[0] = last[1] = last[2] = -1;
        ctx.Recode1 = tmp1.Values;
        ctx.Recode2 = tmp2.Values;
        ctx.Last = last;
        var huff = new Huff(((size + 3) >> 2) + 3);
        DecodeBigTree(bits, huff, ctx);
        bits.GetBit();
        for (int i = 0; i < 3; i++)
            if (last[i] == -1)
                last[i] = huff.Current++;
        return huff.Values;
    }

    static int GetCode(BitReader bits, int[] recode, int[] last)
    {
        int t = 0;
        while ((recode[t] & Node) != 0)
        {
            if (bits.GetBit() != 0)
                t += recode[t] & ~Node;
            t++;
        }
        int v = recode[t];
        if (v != recode[last[0]])
        {
            recode[last[2]] = recode[last[1]];
            recode[last[1]] = recode[last[0]];
            recode[last[0]] = v;
        }
        return v;
    }

    static void LastReset(int[] recode, int[] last) => recode[last[0]] = recode[last[1]] = recode[last[2]] = 0;

    // ---------------------------------------------------------------- frames
    /// <summary>Decodes the next frame into Picture and Palette; false after the last.</summary>
    public bool NextFrame()
    {
        if (CurrentFrame >= FrameCount)
            return false;
        int pos = _nextPos;
        int frameSize = (int)(_frameSizes[CurrentFrame] & ~3u);
        int flag = _frameFlags[CurrentFrame];
        if ((flag & PaletteFlag) != 0)
        {
            int size = _file[pos++] * 4 - 1;
            frameSize -= size + 1;
            int end = pos + size;
            var old = (byte[])Palette.Clone();
            int p = 0, sz = 0;
            while (sz < 256 && pos < _file.Length)
            {
                int t = _file[pos++];
                if ((t & 0x80) != 0)
                {
                    sz += (t & 0x7F) + 1;
                    p += ((t & 0x7F) + 1) * 3;
                }
                else if ((t & 0x40) != 0)
                {
                    int off = _file[pos++] * 3;
                    int j = (t & 0x3F) + 1;
                    while (j-- > 0 && sz < 256)
                    {
                        Palette[p++] = old[off];
                        Palette[p++] = old[off + 1];
                        Palette[p++] = old[off + 2];
                        sz++;
                        off += 3;
                    }
                }
                else
                {
                    Palette[p++] = SmkPal[t];
                    Palette[p++] = SmkPal[_file[pos++] & 0x3F];
                    Palette[p++] = SmkPal[_file[pos++] & 0x3F];
                    sz++;
                }
            }
            pos = end;
        }
        flag >>= 1;
        for (int i = 0; i < MaxAudioTracks; i++, flag >>= 1)
        {
            if ((flag & 1) == 0)
                continue;
            int size = (int)BitConverter.ToUInt32(_file, pos);
            pos += size;                 // the packet (its length word included): the sound plays from the WAV
            frameSize -= size;
        }
        if (frameSize > 0)
            DecodeFrame(pos, frameSize);
        CurrentFrame++;
        _nextPos = pos + Math.Max(frameSize, 0);
        return true;
    }

    void DecodeFrame(int start, int frameSize)
    {
        LastReset(_mmap, _mmapLast);
        LastReset(_mclr, _mclrLast);
        LastReset(_full, _fullLast);
        LastReset(_type, _typeLast);
        int bw = Width >> 2, bh = Height >> 2, blocks = bw * bh, stride = Width;
        var bits = new BitReader(_file, start, frameSize);
        int blk = 0;
        while (blk < blocks)
        {
            int type = GetCode(bits, _type, _typeLast);
            int run = BlockRuns[(type >> 2) & 0x3F];
            switch (type & 3)
            {
                case BlockMono:
                    while (run-- > 0 && blk < blocks)
                    {
                        int clr = GetCode(bits, _mclr, _mclrLast);
                        int map = GetCode(bits, _mmap, _mmapLast);
                        int o = (blk / bw) * (stride * 4) + (blk % bw) * 4;
                        byte hi = (byte)(clr >> 8), lo = (byte)clr;
                        for (int i = 0; i < 4; i++)
                        {
                            Picture[o] = (map & 1) != 0 ? hi : lo;
                            Picture[o + 1] = (map & 2) != 0 ? hi : lo;
                            Picture[o + 2] = (map & 4) != 0 ? hi : lo;
                            Picture[o + 3] = (map & 8) != 0 ? hi : lo;
                            map >>= 4;
                            o += stride;
                        }
                        blk++;
                    }
                    break;
                case BlockFull:
                {
                    int mode = 0;
                    if (_ver4)
                    {
                        if (bits.GetBit() != 0) mode = 1;
                        else if (bits.GetBit() != 0) mode = 2;
                    }
                    while (run-- > 0 && blk < blocks)
                    {
                        int o = (blk / bw) * (stride * 4) + (blk % bw) * 4;
                        switch (mode)
                        {
                            case 0:
                                for (int i = 0; i < 4; i++)
                                {
                                    int pix = GetCode(bits, _full, _fullLast);
                                    Picture[o + 2] = (byte)pix;
                                    Picture[o + 3] = (byte)(pix >> 8);
                                    pix = GetCode(bits, _full, _fullLast);
                                    Picture[o] = (byte)pix;
                                    Picture[o + 1] = (byte)(pix >> 8);
                                    o += stride;
                                }
                                break;
                            case 1:
                                for (int half = 0; half < 2; half++)
                                {
                                    int pix = GetCode(bits, _full, _fullLast);
                                    for (int r = 0; r < 2; r++)
                                    {
                                        Picture[o] = Picture[o + 1] = (byte)pix;
                                        Picture[o + 2] = Picture[o + 3] = (byte)(pix >> 8);
                                        o += stride;
                                    }
                                }
                                break;
                            case 2:
                                for (int i = 0; i < 2; i++)
                                {
                                    int pix2 = GetCode(bits, _full, _fullLast);
                                    int pix1 = GetCode(bits, _full, _fullLast);
                                    for (int r = 0; r < 2; r++)
                                    {
                                        Picture[o] = (byte)pix1;
                                        Picture[o + 1] = (byte)(pix1 >> 8);
                                        Picture[o + 2] = (byte)pix2;
                                        Picture[o + 3] = (byte)(pix2 >> 8);
                                        o += stride;
                                    }
                                }
                                break;
                        }
                        blk++;
                    }
                    break;
                }
                case BlockSkip:
                    while (run-- > 0 && blk < blocks)
                        blk++;
                    break;
                case BlockFill:
                {
                    byte col = (byte)(type >> 8);
                    while (run-- > 0 && blk < blocks)
                    {
                        int o = (blk / bw) * (stride * 4) + (blk % bw) * 4;
                        for (int i = 0; i < 4; i++)
                        {
                            Picture[o] = Picture[o + 1] = Picture[o + 2] = Picture[o + 3] = col;
                            o += stride;
                        }
                        blk++;
                    }
                    break;
                }
            }
        }
    }
}
