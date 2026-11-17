// Chosen Blood module (GPL v2): Blood's .MAP format as NBlood's db.cpp (dbSaveMap) writes it.
using System;
using System.IO;

namespace Chosen.Shared.Blood.Map;

/// <summary>
/// Writes a <see cref="BloodMapFile"/> as a Blood .MAP: the inverse of <see cref="BloodMapFile.Parse"/>, so a map read
/// and written unchanged comes out byte for byte (the version, the header's encryption, Header2, every record and
/// x-structure as stored, the bytes after the CRC). Unlike NBlood's own dbSaveMap it keeps what that rewrites
/// (MapRev unless bumped, header2's text, fogpal and blend, stale sprite index fields, x-structure sizes).
/// </summary>
public static class BloodMapWriter
{
    /// <summary>The file's bytes. bumpRevision: MapRev + 1 first (a save from the editor, as mapedit does; the
    /// records' encryption keys follow it).</summary>
    public static byte[] Write(BloodMapFile m, bool bumpRevision = false)
    {
        // the revision this file gets: m.MapRev changes only once the file is made (a failed save leaves it)
        int rev = m.MapRev + (bumpRevision ? 1 : 0);
        bool v7 = m.V7;
        int nsky = 1 << m.PSkyBits;
        if (m.SkyOfs.Length != nsky)
            throw new InvalidDataException($"{m.SkyOfs.Length} sky offsets for psky bits {m.PSkyBits} ({nsky} expected)");
        using var ms = new MemoryStream();
        var bw = new BinaryWriter(ms);
        bw.Write((byte)'B'); bw.Write((byte)'L'); bw.Write((byte)'M'); bw.Write((byte)0x1a);
        bw.Write((ushort)m.Version);

        var h = new byte[37];
        PutI32(h, 0, m.StartX); PutI32(h, 4, m.StartY); PutI32(h, 8, m.StartZ); PutI16(h, 12, m.StartAng);
        PutI16(h, 14, m.StartSect); PutI16(h, 16, m.PSkyBits); PutI32(h, 18, m.Visibility); PutI32(h, 22, m.SongId);
        h[26] = (byte)m.ParallaxType; PutI32(h, 27, rev);
        PutI16(h, 31, m.Sectors.Count); PutI16(h, 33, m.Walls.Count); PutI16(h, 35, m.Sprites.Count);
        if (m.HeaderEncrypted)
            BloodMapFile.Crypt(h, 0, 37, BloodMapFile.kMapHeaderNew);
        // the reader tells an encrypted header by its song id as stored: the stored one must say the right thing
        if (BloodMapFile.PlainSong(BitConverter.ToInt32(h, 22)) == m.HeaderEncrypted)
            throw new InvalidDataException(m.HeaderEncrypted
                ? $"song id {m.SongId:x} encrypts to one that marks a plain header"
                : $"song id {m.SongId:x} can't be stored in a plain header (it would read as encrypted)");
        bw.Write(h);

        if (v7)
        {
            var h2 = new byte[128];
            if (m.Header2 != null)
                Array.Copy(m.Header2, h2, 128);
            PutI32(h2, 64, m.XSpriteSize); PutI32(h2, 68, m.XWallSize); PutI32(h2, 72, m.XSectorSize);
            BloodMapFile.Crypt(h2, 0, 128, m.Walls.Count);
            bw.Write(h2);
        }

        var sky = new byte[nsky * 2];
        for (int i = 0; i < nsky; i++)
            PutI16(sky, i * 2, m.SkyOfs[i]);
        if (v7)
            BloodMapFile.Crypt(sky, 0, sky.Length, nsky * 2);
        bw.Write(sky);

        foreach (var s in m.Sectors)
        {
            var r = new byte[40];
            MapRecords.WriteSector(s, r, 0);
            if (v7)
                BloodMapFile.Crypt(r, 0, 40, rev * 40);
            bw.Write(r);
            if (s.Extra > 0)
                bw.Write(MapRecords.XBytes(s.Xs == null ? null : (s.Xs.Size, s.Xs.Write), m.XSectorSize, "sector"));
        }
        foreach (var w in m.Walls)
        {
            var r = new byte[32];
            MapRecords.WriteWall(w, r, 0);
            if (v7)
                BloodMapFile.Crypt(r, 0, 32, (rev * 40) | BloodMapFile.kMapHeaderNew);
            bw.Write(r);
            if (w.Extra > 0)
                bw.Write(MapRecords.XBytes(w.Xw == null ? null : (w.Xw.Size, w.Xw.Write), m.XWallSize, "wall"));
        }
        foreach (var sp in m.Sprites)
        {
            var r = new byte[44];
            MapRecords.WriteSprite(sp, r, 0);
            if (v7)
                BloodMapFile.Crypt(r, 0, 44, (rev * 44) | BloodMapFile.kMapHeaderNew);
            bw.Write(r);
            if (sp.Extra > 0)
                bw.Write(MapRecords.XBytes(sp.Xsp == null ? null : (sp.Xsp.Size, sp.Xsp.Write), m.XSpriteSize, "sprite"));
        }
        bw.Flush();
        var body = ms.ToArray();
        uint crc = Crc32.Of(body, body.Length);
        var file = new byte[body.Length + 4 + m.Tail.Length];
        Array.Copy(body, file, body.Length);
        BitConverter.GetBytes(crc).CopyTo(file, body.Length);
        Array.Copy(m.Tail, 0, file, body.Length + 4, m.Tail.Length);
        m.MapRev = rev;
        return file;
    }

    static void PutI16(byte[] d, int at, int v) => MapRecords.PutI16(d, at, v);
    static void PutI32(byte[] d, int at, int v) => MapRecords.PutI32(d, at, v);
}
