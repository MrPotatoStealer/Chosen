// Chosen Blood module (GPL v2): Build's sector, wall and sprite records as Blood stores them (NBlood db.cpp).
using System;

namespace Chosen.Shared.Blood.Map;

/// <summary>The 40-byte sector, 32-byte wall and 44-byte sprite records, unencrypted: what .MAP stores (encrypted in
/// version 7) and .MAPX stores as they are.</summary>
public static class MapRecords
{
    public const int SectorSize = 40, WallSize = 32, SpriteSize = 44;

    public static BSector ReadSector(byte[] d, int pos) => new()
    {
        WallPtr = S16(d, pos), WallNum = S16(d, pos + 2), CeilingZ = S32(d, pos + 4), FloorZ = S32(d, pos + 8),
        CeilingStat = U16(d, pos + 12), FloorStat = U16(d, pos + 14), CeilingPicnum = S16(d, pos + 16),
        CeilingHeinum = S16(d, pos + 18), CeilingShade = (sbyte)d[pos + 20], CeilingPal = d[pos + 21],
        CeilingXPanning = d[pos + 22], CeilingYPanning = d[pos + 23], FloorPicnum = S16(d, pos + 24),
        FloorHeinum = S16(d, pos + 26), FloorShade = (sbyte)d[pos + 28], FloorPal = d[pos + 29],
        FloorXPanning = d[pos + 30], FloorYPanning = d[pos + 31], Visibility = d[pos + 32], FogPal = d[pos + 33],
        Type = S16(d, pos + 34), Hitag = S16(d, pos + 36), Extra = S16(d, pos + 38),
    };

    public static void WriteSector(BSector s, byte[] r, int at)
    {
        PutI16(r, at, s.WallPtr); PutI16(r, at + 2, s.WallNum); PutI32(r, at + 4, s.CeilingZ); PutI32(r, at + 8, s.FloorZ);
        PutI16(r, at + 12, s.CeilingStat); PutI16(r, at + 14, s.FloorStat); PutI16(r, at + 16, s.CeilingPicnum);
        PutI16(r, at + 18, s.CeilingHeinum); r[at + 20] = (byte)s.CeilingShade; r[at + 21] = (byte)s.CeilingPal;
        r[at + 22] = (byte)s.CeilingXPanning; r[at + 23] = (byte)s.CeilingYPanning; PutI16(r, at + 24, s.FloorPicnum);
        PutI16(r, at + 26, s.FloorHeinum); r[at + 28] = (byte)s.FloorShade; r[at + 29] = (byte)s.FloorPal;
        r[at + 30] = (byte)s.FloorXPanning; r[at + 31] = (byte)s.FloorYPanning; r[at + 32] = (byte)s.Visibility;
        r[at + 33] = (byte)s.FogPal; PutI16(r, at + 34, s.Type); PutI16(r, at + 36, s.Hitag); PutI16(r, at + 38, s.Extra);
    }

    public static BWall ReadWall(byte[] d, int pos) => new()
    {
        X = S32(d, pos), Y = S32(d, pos + 4), Point2 = S16(d, pos + 8), NextWall = S16(d, pos + 10),
        NextSector = S16(d, pos + 12), Cstat = U16(d, pos + 14), Picnum = S16(d, pos + 16), OverPicnum = S16(d, pos + 18),
        Shade = (sbyte)d[pos + 20], Pal = d[pos + 21], XRepeat = d[pos + 22], YRepeat = d[pos + 23],
        XPanning = d[pos + 24], YPanning = d[pos + 25], Type = S16(d, pos + 26), Hitag = S16(d, pos + 28),
        Extra = S16(d, pos + 30),
    };

    public static void WriteWall(BWall w, byte[] r, int at)
    {
        PutI32(r, at, w.X); PutI32(r, at + 4, w.Y); PutI16(r, at + 8, w.Point2); PutI16(r, at + 10, w.NextWall);
        PutI16(r, at + 12, w.NextSector); PutI16(r, at + 14, w.Cstat); PutI16(r, at + 16, w.Picnum);
        PutI16(r, at + 18, w.OverPicnum); r[at + 20] = (byte)w.Shade; r[at + 21] = (byte)w.Pal; r[at + 22] = (byte)w.XRepeat;
        r[at + 23] = (byte)w.YRepeat; r[at + 24] = (byte)w.XPanning; r[at + 25] = (byte)w.YPanning;
        PutI16(r, at + 26, w.Type); PutI16(r, at + 28, w.Hitag); PutI16(r, at + 30, w.Extra);
    }

    /// <summary>A sprite record; index: its position in the map (the stored index field goes to FileIndex).</summary>
    public static BSprite ReadSprite(byte[] d, int pos, int index) => new()
    {
        X = S32(d, pos), Y = S32(d, pos + 4), Z = S32(d, pos + 8), Cstat = U16(d, pos + 12), Picnum = S16(d, pos + 14),
        Shade = (sbyte)d[pos + 16], Pal = d[pos + 17], ClipDist = d[pos + 18], Blend = d[pos + 19],
        XRepeat = d[pos + 20], YRepeat = d[pos + 21], XOffset = (sbyte)d[pos + 22], YOffset = (sbyte)d[pos + 23],
        SectNum = S16(d, pos + 24), StatNum = S16(d, pos + 26), Ang = S16(d, pos + 28), Owner = S16(d, pos + 30),
        Index = index, FileIndex = S16(d, pos + 32), YVel = S16(d, pos + 34), InitType = S16(d, pos + 36),
        Type = S16(d, pos + 38), Flags = S16(d, pos + 40), Extra = S16(d, pos + 42),
    };

    public static void WriteSprite(BSprite sp, byte[] r, int at)
    {
        PutI32(r, at, sp.X); PutI32(r, at + 4, sp.Y); PutI32(r, at + 8, sp.Z); PutI16(r, at + 12, sp.Cstat);
        PutI16(r, at + 14, sp.Picnum); r[at + 16] = (byte)sp.Shade; r[at + 17] = (byte)sp.Pal; r[at + 18] = (byte)sp.ClipDist;
        r[at + 19] = (byte)sp.Blend; r[at + 20] = (byte)sp.XRepeat; r[at + 21] = (byte)sp.YRepeat;
        r[at + 22] = (byte)sp.XOffset; r[at + 23] = (byte)sp.YOffset; PutI16(r, at + 24, sp.SectNum);
        PutI16(r, at + 26, sp.StatNum); PutI16(r, at + 28, sp.Ang); PutI16(r, at + 30, sp.Owner);
        PutI16(r, at + 32, sp.FileIndex); PutI16(r, at + 34, sp.YVel); PutI16(r, at + 36, sp.InitType);
        PutI16(r, at + 38, sp.Type); PutI16(r, at + 40, sp.Flags); PutI16(r, at + 42, sp.Extra);
    }

    /// <summary>An x-structure as the map stores it (size bytes): its record, or zeros for a missing one (an Extra with
    /// no x).</summary>
    internal static byte[] XBytes((int size, Action<byte[], int> write)? x, int size, string what)
    {
        var b = new byte[size];
        if (x is { } v)
        {
            if (v.size != size)
                throw new System.IO.InvalidDataException($"a {what} x-structure of {v.size} bytes in a map storing {size}");
            v.write(b, 0);
        }
        return b;
    }

    internal static short S16(byte[] d, int at) => BitConverter.ToInt16(d, at);
    internal static ushort U16(byte[] d, int at) => BitConverter.ToUInt16(d, at);
    internal static int S32(byte[] d, int at) => BitConverter.ToInt32(d, at);
    internal static void PutI16(byte[] d, int at, int v) { d[at] = (byte)v; d[at + 1] = (byte)(v >> 8); }
    internal static void PutI32(byte[] d, int at, int v) { d[at] = (byte)v; d[at + 1] = (byte)(v >> 8); d[at + 2] = (byte)(v >> 16); d[at + 3] = (byte)(v >> 24); }
}
