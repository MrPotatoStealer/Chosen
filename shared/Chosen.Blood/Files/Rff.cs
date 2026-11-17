// Chosen Blood module (GPL v2): Blood's RFF archives as NBlood's resource.cpp reads them (pipeline/rff.py).
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Chosen.Shared.Blood.Files;

/// <summary>An entry of an RFF archive: its name and type (BLOOD + PAL), id, place and flags.</summary>
public sealed record RffEntry(string Name, string Type, int Id, int Offset, int Size, int Flags);

/// <summary>
/// An RFF archive (BLOOD.RFF, SOUNDS.RFF): the directory at its end, XOR-encrypted in version 3.x files, and each
/// entry's data, whose first 256 bytes are XOR-encrypted when its flag 16 is set.
/// </summary>
public sealed class Rff
{
    const int DictCrypt = 16;
    readonly byte[] _blob;
    public readonly List<RffEntry> Entries = new();
    public string Path { get; }

    public Rff(string path) : this(File.ReadAllBytes(path)) => Path = path;

    public Rff(byte[] blob)
    {
        _blob = blob;
        if (blob.Length < 16 || blob[0] != 'R' || blob[1] != 'F' || blob[2] != 'F' || blob[3] != 0x1a)
            throw new InvalidDataException("not an RFF archive");
        int version = BitConverter.ToInt16(blob, 4);
        int offset = BitConverter.ToInt32(blob, 8), count = BitConverter.ToInt32(blob, 12);
        var dir = new byte[count * 48];
        Array.Copy(blob, offset, dir, 0, dir.Length);
        if ((version & 0xff00) == 0x300)
            Crypt(dir, 0, dir.Length, (offset + (version & 0xff) * offset) & 0xFFFF);
        for (int i = 0; i < count; i++)
        {
            int e = i * 48;
            Entries.Add(new RffEntry(Str(dir, e + 36, 8), Str(dir, e + 33, 3), BitConverter.ToInt32(dir, e + 44),
                BitConverter.ToInt32(dir, e + 16), BitConverter.ToInt32(dir, e + 20), dir[e + 32]));
        }
    }

    static string Str(byte[] d, int at, int max)
    {
        int n = 0;
        while (n < max && d[at + n] != 0)
            n++;
        return Encoding.Latin1.GetString(d, at, n);
    }

    static void Crypt(byte[] d, int at, int len, int key)
    {
        for (int i = 0; i < len; i++)
            d[at + i] ^= (byte)((((key + i) & 0xFFFF) >> 1) & 0xFF);
    }

    public byte[] Read(RffEntry e)
    {
        var data = new byte[e.Size];
        Array.Copy(_blob, e.Offset, data, 0, e.Size);
        if ((e.Flags & DictCrypt) != 0)
            Crypt(data, 0, Math.Min(0x100, data.Length), 0);
        return data;
    }

    /// <summary>The first entry of that name and type (case-insensitive), or null.</summary>
    public RffEntry Find(string name, string type) =>
        Entries.Find(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && e.Type.Equals(type, StringComparison.OrdinalIgnoreCase));

    public RffEntry Find(int id, string type) =>
        Entries.Find(e => e.Id == id && e.Type.Equals(type, StringComparison.OrdinalIgnoreCase));

    public byte[] Read(string name, string type) => Find(name, type) is { } e ? Read(e) : null;
}
