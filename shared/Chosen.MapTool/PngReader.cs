// Chosen core: original code (a minimal PNG decoder for maptool's comparisons).
using System;
using System.IO;
using System.IO.Compression;

namespace Chosen.MapTool;

/// <summary>Decodes 8-bit RGB / RGBA PNGs (what the pipeline writes) to RGBA rows.</summary>
static class PngReader
{
    public static (int w, int h, byte[] rgba) Read(string path)
    {
        var d = File.ReadAllBytes(path);
        int pos = 8, w = 0, h = 0, type = 0;
        using var idat = new MemoryStream();
        while (pos + 8 <= d.Length)
        {
            int len = (d[pos] << 24) | (d[pos + 1] << 16) | (d[pos + 2] << 8) | d[pos + 3];
            string kind = System.Text.Encoding.ASCII.GetString(d, pos + 4, 4);
            int body = pos + 8;
            if (kind == "IHDR")
            {
                w = (d[body] << 24) | (d[body + 1] << 16) | (d[body + 2] << 8) | d[body + 3];
                h = (d[body + 4] << 24) | (d[body + 5] << 16) | (d[body + 6] << 8) | d[body + 7];
                if (d[body + 8] != 8 || (d[body + 9] != 2 && d[body + 9] != 6) || d[body + 12] != 0)
                    throw new InvalidDataException($"{path}: only 8-bit RGB/RGBA, not interlaced");
                type = d[body + 9];
            }
            else if (kind == "IDAT")
                idat.Write(d, body, len);
            else if (kind == "IEND")
                break;
            pos = body + len + 4;
        }
        int bpp = type == 6 ? 4 : 3, stride = w * bpp;
        var raw = new byte[(stride + 1) * h];
        idat.Position = 0;
        using (var z = new ZLibStream(idat, CompressionMode.Decompress))
        {
            int got = 0, n;
            while (got < raw.Length && (n = z.Read(raw, got, raw.Length - got)) > 0)
                got += n;
        }
        var px = new byte[stride * h];
        for (int y = 0; y < h; y++)
        {
            int f = raw[y * (stride + 1)], src = y * (stride + 1) + 1, dst = y * stride;
            for (int x = 0; x < stride; x++)
            {
                int a = x >= bpp ? px[dst + x - bpp] : 0, b = y > 0 ? px[dst - stride + x] : 0;
                int c = x >= bpp && y > 0 ? px[dst - stride + x - bpp] : 0, v = raw[src + x];
                px[dst + x] = (byte)(f switch
                {
                    0 => v,
                    1 => v + a,
                    2 => v + b,
                    3 => v + ((a + b) >> 1),
                    4 => v + Paeth(a, b, c),
                    _ => throw new InvalidDataException($"{path}: filter {f}"),
                });
            }
        }
        if (bpp == 4)
            return (w, h, px);
        var rgba = new byte[w * h * 4];
        for (int i = 0, j = 0; i < px.Length; i += 3, j += 4)
        {
            rgba[j] = px[i]; rgba[j + 1] = px[i + 1]; rgba[j + 2] = px[i + 2]; rgba[j + 3] = 255;
        }
        return (w, h, rgba);
    }

    static int Paeth(int a, int b, int c)
    {
        int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }
}
