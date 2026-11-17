// Chosen Blood module (GPL v2): Blood's palette and palette lookups (BLOOD.RFF's PAL and PLU lumps).
namespace Chosen.Shared.Blood.Files;

/// <summary>Blood's palette (BLOOD.PAL: 256 colours, 8 bits a component) and its lookups (PLU: 64 shades × 256).</summary>
public sealed class Palette
{
    /// <summary>The colour index Blood draws transparent in masked tiles and sprites.</summary>
    public const int Transparent = 255;

    public readonly byte[] Rgb;   // 768 bytes

    public Palette(byte[] rgb768) => Rgb = rgb768;

    public static Palette FromRff(Rff blood) =>
        new(blood.Read("BLOOD", "PAL") ?? throw new System.IO.InvalidDataException("no BLOOD.PAL in " + blood.Path));

    public (byte r, byte g, byte b) this[int i] => (Rgb[i * 3], Rgb[i * 3 + 1], Rgb[i * 3 + 2]);

    /// <summary>A tile as RGBA rows (index 255 transparent black when masked, else drawn with its colour); a lookup
    /// (a PLU's first 256 bytes: shade 0) remaps the indices first.</summary>
    public byte[] ToRgba(Tile t, bool masked, byte[] lookup = null)
    {
        var px = new byte[t.Width * t.Height * 4];
        for (int y = 0; y < t.Height; y++)
            for (int x = 0; x < t.Width; x++)
            {
                int i = t.At(x, y), o = (y * t.Width + x) * 4;
                if (masked && i == Transparent)
                    continue;           // (0, 0, 0, 0)
                if (lookup != null)
                    i = lookup[i];
                px[o] = Rgb[i * 3]; px[o + 1] = Rgb[i * 3 + 1]; px[o + 2] = Rgb[i * 3 + 2]; px[o + 3] = 255;
            }
        return px;
    }
}
