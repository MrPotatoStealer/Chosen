using System;
using System.IO;
using Chosen.Data;
using Godot;

namespace Chosen.Render;

/// <summary>
/// Blood's procedural fire (NBlood fire.cpp): tile 2342 is not art but a cellular automaton Blood redraws every 3 ticks
/// of the 120 Hz clock (FireProcess): three seed rows of random heat at the bottom of a 128x132 buffer, each cell the
/// cooled sum of the cells below it, mapped to palette indices through RFIRE.CLU. Run here the same way into a
/// texture every use of tile 2342 draws (TileTextures.Get, world surfaces).
/// </summary>
public static class ProceduralFire
{
    public const int Tile = 2342;
    const int Size = 128, Rows = 132, Damping = 6;

    static readonly byte[] CoolTable = new byte[1024];
    static readonly byte[] FrameBuffer = new byte[17280];
    static readonly byte[][] SeedBuffer = new byte[16][];
    static byte[] _clu;
    static byte[] _palette;
    static Image _image;
    static ImageTexture _texture;
    static long _lastUpdate = long.MinValue;
    static readonly Random Rand = new(Tile);
    static bool _failed;

    /// <summary>The fire's texture (null if Blood's data lacks rfire.clu or the palette).</summary>
    public static ImageTexture Texture
    {
        get
        {
            if (_texture == null && !_failed)
                Init();
            return _texture;
        }
    }

    static void Init()
    {
        var clu = BloodData.DataFile("rfire.clu");
        var pal = BloodData.Folder.PathJoin("gfx/palette.lmp");
        if (!File.Exists(clu) || !File.Exists(pal))
        {
            _failed = true;
            GD.PushWarning("fire: no data/rfire.clu or gfx/palette.lmp (run build_assets.bat)");
            return;
        }
        _clu = File.ReadAllBytes(clu);
        _palette = new byte[768];
        Array.Copy(File.ReadAllBytes(pal), _palette, 768);     // r, g, b by index
        for (int i = 0; i < 1024; i++)                       // BuildCoolTable
            CoolTable[i] = (byte)Math.Max((i - Damping) / 4, 0);
        for (int i = 0; i < 16; i++)                         // InitSeedBuffers
        {
            SeedBuffer[i] = new byte[Size];
            for (int j = 0; j < Size; j += 2)
                SeedBuffer[i][j] = SeedBuffer[i][j + 1] = (byte)Rand.Next(256);
        }
        for (int i = 0; i < 100; i++)                        // FireInit's warm-up
            Step();
        _image = Image.CreateEmpty(Size, Size, true, Image.Format.Rgba8);
        Draw();
        _texture = ImageTexture.CreateFromImage(_image);
    }

    /// <summary>DoFireFrame's simulation: seed rows, then CellularFrame.</summary>
    static void Step()
    {
        var seed = SeedBuffer[Rand.Next(16)];
        for (int i = 0; i < 3; i++)
            Buffer.BlockCopy(seed, 0, FrameBuffer, 16896 + i * Size, Size);
        var f = FrameBuffer;
        for (int p1 = 0; p1 < Size * Rows; p1++)
        {
            int p2 = p1 + Size;
            int sum = f[p2 - 1] + f[p2] + f[p2 + 1] + f[p2 + Size];
            if (f[p2 + Size] > 96)
            {
                p2 += Size;
                sum += f[p2 - 1] + f[p2] + f[p2 + 1] + f[p2 + Size];
                sum >>= 1;
            }
            f[p1] = CoolTable[sum];
        }
    }

    static readonly byte[] Rgba = new byte[Size * Size * 4];

    /// <summary>The buffer's first 128 rows through the colour lookup (Blood's tile is column-major, so the picture is
    /// the buffer as it is: seeds at the bottom, flames rising). Filled here and handed over at once (a SetPixel per
    /// texel, 16384 calls into the engine, took 2 ms every 25 ms).</summary>
    static void Draw()
    {
        for (int i = 0; i < Size * Size; i++)
        {
            int c = _clu[FrameBuffer[i]] * 3, o = i * 4;
            Rgba[o] = _palette[c];
            Rgba[o + 1] = _palette[c + 1];
            Rgba[o + 2] = _palette[c + 2];
            Rgba[o + 3] = 255;
        }
        _image.SetData(Size, Size, false, Image.Format.Rgba8, Rgba);
        _image.GenerateMipmaps();
    }

    /// <summary>FireProcess: a new frame when more than 2 ticks of the 120 Hz clock have passed.</summary>
    public static void Process()
    {
        if (_texture == null)
            return;
        long clock = (long)(Time.GetTicksMsec() * 120 / 1000);
        if (clock < _lastUpdate || _lastUpdate + 2 < clock)
        {
            Step();
            Draw();
            _texture.Update(_image);
            _lastUpdate = clock;
        }
    }
}
