// Chosen Blood module (GPL v2): Blood's .MAP format as NBlood's db.cpp (dbLoadMap, dbSaveMap) reads and writes it.
using System;
using System.Collections.Generic;
using System.IO;
using Chosen.Shared.Engine.Geometry;

namespace Chosen.Shared.Blood.Map;

/// <summary>A Build sector as Blood stores it (sectortype, Build units; Xs its x-sector or null).</summary>
public sealed class BSector : IGeomSector
{
    public int WallPtr, WallNum, CeilingZ, FloorZ, CeilingStat, FloorStat, CeilingPicnum, CeilingHeinum, CeilingShade,
        CeilingPal, CeilingXPanning, CeilingYPanning, FloorPicnum, FloorHeinum, FloorShade, FloorPal, FloorXPanning,
        FloorYPanning, Visibility, FogPal, Type, Hitag, Extra;
    public XSector Xs;

    int IGeomSector.WallPtr => WallPtr;
    int IGeomSector.WallNum => WallNum;
    int IGeomSector.CeilingZ => CeilingZ;
    int IGeomSector.FloorZ => FloorZ;
    int IGeomSector.CeilingStat => CeilingStat;
    int IGeomSector.FloorStat => FloorStat;
    int IGeomSector.CeilingHeinum => CeilingHeinum;
    int IGeomSector.FloorHeinum => FloorHeinum;
}

/// <summary>A Build wall (walltype; Xw its x-wall or null).</summary>
public sealed class BWall : IGeomWall
{
    public int X, Y, Point2, NextWall, NextSector, Cstat, Picnum, OverPicnum, Shade, Pal, XRepeat, YRepeat, XPanning,
        YPanning, Type, Hitag, Extra;
    public int Sector = -1;      // the sector whose loop holds it (derived, not stored)
    public XWall Xw;

    int IGeomWall.X => X;
    int IGeomWall.Y => Y;
    int IGeomWall.Point2 => Point2;
    int IGeomWall.NextWall => NextWall;
    int IGeomWall.NextSector => NextSector;
}

/// <summary>A Build sprite (spritetype; Xsp its x-sprite or null).</summary>
public sealed class BSprite
{
    public int X, Y, Z, Cstat, Picnum, Shade, Pal, ClipDist, Blend, XRepeat, YRepeat, XOffset, YOffset, SectNum, StatNum,
        Ang, Owner, YVel, InitType, Type, Flags, Extra;
    /// <summary>Its position in the map (what Blood's code calls its index; NBlood rewrites the stored one on load).</summary>
    public int Index;
    /// <summary>The index field as stored (offset 32), often stale: kept so a map saves back byte for byte.</summary>
    public int FileIndex;
    public XSprite Xsp;
    /// <summary>Refreshed Supply's level fixes for it (defs/levelHacks: angoff, mdxoff, mdyoff, mdzoff, novoxel).</summary>
    public Dictionary<string, string> Patch;
}

/// <summary>
/// A Blood .MAP file (NBlood db.cpp dbLoadMap; pipeline/blood_map.py): the header, sky offsets, sectors, walls and
/// sprites with their x-structures, decrypted (version 7 maps), the CRC checked. <see cref="Parse"/> keeps it exactly
/// as stored (what BloodMapWriter writes back); <see cref="Load"/> also prepares it for the game: Refreshed Supply's
/// level fixes (NAME.patch beside it) and marker references as Blood propagates them.
/// </summary>
public sealed class BloodMapFile : IBuildData
{
    public const int kMapHeaderNew = 0x7474614D;

    public int StartX, StartY, StartZ, StartAng, StartSect, PSkyBits, Visibility, SongId, ParallaxType, MapRev;
    public short[] SkyOfs = Array.Empty<short>();
    public readonly List<BSector> Sectors = new();
    public readonly List<BWall> Walls = new();
    public readonly List<BSprite> Sprites = new();
    public uint Crc;
    public string Name = "";
    public int MarkerFixes;

    /// <summary>The version as stored (0x700 / 0x603...): 7.x maps are encrypted and carry Header2.</summary>
    public int Version = 0x700;
    /// <summary>The 37-byte header was stored encrypted (told by its song id, as dbLoadMap does).</summary>
    public bool HeaderEncrypted = true;
    /// <summary>Version 7's second header (128 bytes, decrypted; null for older maps): the x-structure sizes at 64, 68, 72.</summary>
    public byte[] Header2;
    public int XSpriteSize = 56, XWallSize = 24, XSectorSize = 60;
    /// <summary>Bytes after the CRC, if any (kept as they are).</summary>
    public byte[] Tail = Array.Empty<byte>();

    public bool V7 => (Version & 0xff00) == 0x700;

    // IBuildData
    public int SectorCount => Sectors.Count;
    public int WallCount => Walls.Count;
    public IGeomSector Sector(int i) => Sectors[i];
    public IGeomWall Wall(int i) => Walls[i];

    /// <summary>The map as stored (no patch, no marker propagation): for tools and the editor. A .MAP or a .MAPX
    /// (told by its first bytes; a .MAPX's sprite extras are in BSprite.Patch, its other extras dropped:
    /// MapXFile.Read keeps them).</summary>
    public static BloodMapFile Read(string path)
    {
        var data = File.ReadAllBytes(path);
        BloodMapFile m;
        if (MapXFile.IsMapX(data))
        {
            var (map, x) = MapXFile.Read(data);
            m = map;
            m.Lights = x.HasLights ? x.Lights : null;
            if (x.HasAtmosphere && x.Atmospheres.Count > 0)
            {
                m.Atmospheres = x.Atmospheres;
                m.SectorAtmospheres = x.SectorAtmospheres;
            }
        }
        else
            m = Parse(data);
        m.Name = Path.GetFileNameWithoutExtension(path).ToUpperInvariant();
        return m;
    }

    /// <summary>The map as the game plays it: Read, then PrepareForGame with the .patch beside a .MAP (a .MAPX holds
    /// its fixes itself).</summary>
    public static BloodMapFile Load(string path)
    {
        var m = Read(path);
        m.PrepareForGame(m.FromMapX ? null : Path.ChangeExtension(path, ".patch"));
        return m;
    }

    /// <summary>Read from a .MAPX (its patch already in the data).</summary>
    public bool FromMapX;

    /// <summary>A .MAPX's own atmospheres and each sector's (its ATMO chunk), null for a .MAP or a .MAPX without: the
    /// game guesses those (AtmosphereRules.Guess).</summary>
    public List<MapAtmosphere> Atmospheres;
    public List<int> SectorAtmospheres;

    /// <summary>A .MAPX's own dynamic lights as Read gives them (its LITE chunk; empty: none at all), null for a .MAP or
    /// a .MAPX without: the game makes those from the sprites (LightRules), and places a map's own alone.</summary>
    public List<MapLight> Lights;

    /// <summary>What the game does after reading: Refreshed Supply's fixes (if patchPath exists), then marker
    /// references propagated as dbLoadMap does.</summary>
    public void PrepareForGame(string patchPath)
    {
        if (patchPath != null && File.Exists(patchPath))
            ApplyPatch(File.ReadAllLines(patchPath));
        PropagateMarkers();
    }

    internal static void Crypt(byte[] d, int at, int len, int key)
    {
        for (int i = 0; i < len; i++)
            d[at + i] ^= (byte)(key + i);
    }

    static short S16(byte[] d, int at) => MapRecords.S16(d, at);
    static ushort U16(byte[] d, int at) => MapRecords.U16(d, at);
    static int S32(byte[] d, int at) => MapRecords.S32(d, at);

    /// <summary>A header's song id that means it is stored in the clear (dbLoadMap).</summary>
    internal static bool PlainSong(int song) => song == 0 || song == kMapHeaderNew || song == 0x4D617474;

    public static BloodMapFile Parse(byte[] data)
    {
        if (data.Length < 6 || data[0] != 'B' || data[1] != 'L' || data[2] != 'M' || data[3] != 0x1a)
            throw new InvalidDataException("not a Blood map");
        var d = (byte[])data.Clone();
        var m = new BloodMapFile { Version = U16(d, 4) };
        bool v7 = m.V7;
        int pos = 6;
        // the header (37 bytes), encrypted unless its song id is one of the plain ones
        m.HeaderEncrypted = !PlainSong(S32(d, pos + 22));
        if (m.HeaderEncrypted)
            Crypt(d, pos, 37, kMapHeaderNew);
        m.StartX = S32(d, pos); m.StartY = S32(d, pos + 4); m.StartZ = S32(d, pos + 8); m.StartAng = S16(d, pos + 12);
        m.StartSect = S16(d, pos + 14); m.PSkyBits = S16(d, pos + 16); m.Visibility = S32(d, pos + 18);
        m.SongId = S32(d, pos + 22); m.ParallaxType = d[pos + 26]; m.MapRev = S32(d, pos + 27);
        int numSectors = S16(d, pos + 31), numWalls = S16(d, pos + 33), numSprites = S16(d, pos + 35);
        pos += 37;
        if (v7)
        {
            Crypt(d, pos, 128, numWalls);
            m.Header2 = new byte[128];
            Array.Copy(d, pos, m.Header2, 0, 128);
            m.XSpriteSize = S32(d, pos + 64);
            m.XWallSize = S32(d, pos + 68);
            m.XSectorSize = S32(d, pos + 72);
            // NBlood accepts a stored x-structure smaller than its own (zero-filled), never larger (db.cpp's dassert)
            if (m.XSpriteSize > XSprite.FullSize || m.XWallSize > XWall.FullSize || m.XSectorSize > XSector.FullSize
                || m.XSpriteSize <= 0 || m.XWallSize <= 0 || m.XSectorSize <= 0)
                throw new InvalidDataException($"x-structure sizes {m.XSpriteSize}/{m.XWallSize}/{m.XSectorSize} (at most {XSprite.FullSize}/{XWall.FullSize}/{XSector.FullSize})");
            pos += 128;
        }
        int nsky = 1 << m.PSkyBits;
        if (v7)
            Crypt(d, pos, nsky * 2, nsky * 2);
        m.SkyOfs = new short[nsky];
        for (int i = 0; i < nsky; i++)
            m.SkyOfs[i] = S16(d, pos + i * 2);
        pos += nsky * 2;
        for (int i = 0; i < numSectors; i++)
        {
            if (v7)
                Crypt(d, pos, 40, m.MapRev * 40);
            var s = MapRecords.ReadSector(d, pos);
            pos += 40;
            if (s.Extra > 0)
            {
                s.Xs = XSector.Read(d, pos, m.XSectorSize);
                pos += m.XSectorSize;
            }
            m.Sectors.Add(s);
        }
        for (int i = 0; i < numWalls; i++)
        {
            if (v7)
                Crypt(d, pos, 32, (m.MapRev * 40) | kMapHeaderNew);
            var w = MapRecords.ReadWall(d, pos);
            pos += 32;
            if (w.Extra > 0)
            {
                w.Xw = XWall.Read(d, pos, m.XWallSize);
                pos += m.XWallSize;
            }
            m.Walls.Add(w);
        }
        for (int i = 0; i < numSprites; i++)
        {
            if (v7)
                Crypt(d, pos, 44, (m.MapRev * 44) | kMapHeaderNew);
            var sp = MapRecords.ReadSprite(d, pos, i);
            pos += 44;
            if (sp.Extra > 0)
            {
                sp.Xsp = XSprite.Read(d, pos, m.XSpriteSize);
                pos += m.XSpriteSize;
            }
            m.Sprites.Add(sp);
        }
        m.Crc = BitConverter.ToUInt32(data, pos);
        if (Crc32.Of(data, pos) != m.Crc)
            throw new InvalidDataException("map CRC mismatch");
        if (data.Length > pos + 4)
            m.Tail = data[(pos + 4)..];
        for (int si = 0; si < m.Sectors.Count; si++)
            for (int w = m.Sectors[si].WallPtr; w < m.Sectors[si].WallPtr + m.Sectors[si].WallNum && w < m.Walls.Count; w++)
                m.Walls[w].Sector = si;
        return m;
    }

    /// <summary>PropagateMarkerReferences (dbLoadMap): a sector's markers are the marker sprites it owns, whatever its
    /// x-sector stores (MRW2M3's slide 1112); a marker owned by no sector with an x-sector, or of another type, is
    /// deleted (statnum 1024).</summary>
    void PropagateMarkers()
    {
        foreach (var sp in Sprites)
        {
            if (sp.StatNum != 10)
                continue;
            int o = sp.Owner;
            var x = o >= 0 && o < Sectors.Count ? Sectors[o].Xs : null;
            if (x == null || sp.Type != 3 && sp.Type != 4 && sp.Type != 5 && sp.Type != 8)
            {
                sp.StatNum = 1024;
                continue;
            }
            if (sp.Type == 4)
            {
                if (x.Marker1 != sp.Index)
                {
                    MarkerFixes++;
                    x.Marker1 = sp.Index;
                }
            }
            else if (x.Marker0 != sp.Index)
            {
                MarkerFixes++;
                x.Marker0 = sp.Index;
            }
        }
    }

    /// <summary>Refreshed Supply's fixes (defs/levelHacks): sector heights in the map data, sprites' model keys.</summary>
    public void ApplyPatch(IEnumerable<string> lines) => MapPatch.Parse(lines).Apply(this);
}

/// <summary>CRC-32 (the zlib polynomial), as the map's trailer holds over the bytes before it.</summary>
public static class Crc32
{
    static readonly uint[] Table = Make();

    static uint[] Make()
    {
        var t = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            t[n] = c;
        }
        return t;
    }

    public static uint Of(byte[] d, int len)
    {
        uint c = 0xFFFFFFFFu;
        for (int i = 0; i < len; i++)
            c = Table[(c ^ d[i]) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFFu;
    }
}
