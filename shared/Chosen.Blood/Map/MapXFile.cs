// Chosen Blood module (GPL v2): Chosen's .MAPX map format, carrying Blood's records (MapRecords) unencrypted.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Chosen.Shared.Blood.Map;

/// <summary>One of a map's art sets (ARTS): an ART file or a folder of them; Override: its files replace Blood's files
/// of the same name whole (an add-on's art: Death Wish), else its tiles are added to Blood's one by one (new
/// textures: its empty tiles leave Blood's as they are).</summary>
public sealed record ArtRef(string Path, bool Override);

/// <summary>The editor's view of a map, kept in a .MAPX (EDIT chunk): never read by the game.</summary>
public sealed class EditState
{
    public int CentreX, CentreY;
    public float Zoom = 1;
    public int Grid = 64;
    public int Mode;
}

/// <summary>A layer of the editor (LAYR): a name, and whether it shows.</summary>
public sealed class MapLayer
{
    public string Name = "";
    public bool Visible = true;
}

/// <summary>What a .MAPX holds beside the Blood map itself.</summary>
public sealed class MapXExtras
{
    /// <summary>INFO: key=value lines (generator, created, modified, source_crc, title, author, comment), in order.</summary>
    public readonly List<KeyValuePair<string, string>> Info = new();
    /// <summary>SPRX: the sprite extras a .patch gives (model turn, offsets, no model).</summary>
    public readonly List<SpriteExtra> SpriteExtras = new();
    /// <summary>NAME: names or notes per object (kind 0 sector, 1 wall, 2 sprite).</summary>
    public readonly SortedDictionary<(int kind, int index), string> Names = new();
    /// <summary>EDIT: the editor's view, or null.</summary>
    public EditState Edit;
    /// <summary>LAYR: the editor's layers (rooms over rooms kept apart; the game ignores them), the one new sectors
    /// go into, and each sector's layer by index (missing or out of range: 0). Empty: one layer.</summary>
    public readonly List<MapLayer> Layers = new();
    public int CurrentLayer;
    public readonly List<int> SectorLayers = new();
    /// <summary>Each sprite's own layer by index, or -1 (missing too): its sector's.</summary>
    public readonly List<int> SpriteLayers = new();
    /// <summary>The layers are the ones the editor made itself when it opened a .MAP (not saved there: it makes them
    /// again), not the mapper's.</summary>
    public bool LayersAuto;
    /// <summary>ARTS: the map's own art, in the order loaded over Blood's (Files.MapArt): ART files or folders of them,
    /// each path relative to the map's folder (absolute when the map has none yet, or for Forge's Test Map copy).</summary>
    public readonly List<ArtRef> Art = new();
    /// <summary>LITE: the map's dynamic lights (MapLights.cs), when it has its own (HasLights): then they are all the game
    /// gives it, none made from its sprites. A .MAPX without (made before, or from a .MAP whose lights weren't made in
    /// the editor; Forge's Test Map copy of one) gets them from its sprites as a .MAP does.</summary>
    public readonly List<MapLight> Lights = new();
    /// <summary>The map has its own lights (a LITE chunk, written even when it holds none: all deleted).</summary>
    public bool HasLights;
    /// <summary>ATMO: the map's atmospheres (MapAtmosphere.cs: fog, ground mist, weather, wind), the first its own, and
    /// each sector's by index (missing: 0), when it has them (HasAtmosphere): the game then gives it these, none
    /// guessed. A .MAPX without gets the game's guesses as a .MAP does.</summary>
    public readonly List<MapAtmosphere> Atmospheres = new();
    public readonly List<int> SectorAtmospheres = new();
    public bool HasAtmosphere;
    /// <summary>Chunks this version doesn't know, kept and written back.</summary>
    public readonly List<(string fourcc, byte[] payload)> Unknown = new();
    /// <summary>The .MAPX version the file had (MapXFile.Read sets it): below MapXFile.FormatVersion, the map was brought
    /// up to date as it was read (MapXFile's migrations) and is written back at the current version.</summary>
    public int FileVersion = MapXFile.FormatVersion;
    public bool Migrated => FileVersion < MapXFile.FormatVersion;

    public string GetInfo(string key) => Info.FirstOrDefault(kv => kv.Key == key).Value;

    public void SetInfo(string key, string value)
    {
        int i = Info.FindIndex(kv => kv.Key == key);
        if (value == null)
        {
            if (i >= 0)
                Info.RemoveAt(i);
        }
        else if (i >= 0)
            Info[i] = new(key, value);
        else
            Info.Add(new(key, value));
    }
}

/// <summary>
/// .MAPX version 1: little-endian; magic "MAPX", u16 version, u16 flags, then chunks to the end (fourcc, u32 payload
/// length, payload). HEAD, SECT, WALL and SPRT carry the .MAP's header and records as Blood stores them, unencrypted,
/// with everything that makes a .MAP of it byte for byte (version, header encryption, header2, x-structure sizes,
/// the bytes after the CRC); INFO, SPRX, NAME, LAYR, ARTS (u16 count; each u8 flags, 1 override, and a path), LITE (the
/// dynamic lights: LightRules.Write), ATMO (the atmospheres: AtmosphereRules.Write), EDIT are
/// Chosen's; the last chunk is "CRC " over everything before it. Its pictures may be numbered up to
/// Files.MapArt.MaxTile (NBlood's user tiles), a .MAP's below 6144 (Blood's).
/// A reader skips chunks it doesn't know (Unknown keeps them).
/// Changing the format: a new chunk, or new fields at the end of a chunk (read only when there are bytes left, as
/// LAYR's sprite layers are), keeps the version: older readers skip or ignore them. Anything an older reader would read
/// wrongly (a field changed or moved, a chunk's layout changed, a value's meaning changed) raises FormatVersion, and
/// the reader keeps reading every older version: a chunk whose layout changed reads by Reader.Version, and what can
/// only be put right once the whole map is read goes into Migrations, run in order from the file's version up. Writing
/// is always the current version. A file newer than its reader is refused, never half read. INFO's saved_by says
/// which Chosen Forge wrote a file; MapXExtras.FileVersion which version it was.
/// </summary>
public static class MapXFile
{
    /// <summary>The format's version, written after the magic (see above for when it changes).</summary>
    public const int FormatVersion = 1;

    /// <summary>The steps that bring a map read from an older file up to date: Migrations[v - 1] takes a map read as
    /// version v to version v + 1 (so there is one per version after the first; none yet). Read checks the count.</summary>
    static readonly Action<BloodMapFile, MapXExtras>[] Migrations =
    {
        // version 1 -> 2: (m, x) => { ... },
    };
    static readonly byte[] Magic = "MAPX"u8.ToArray();

    public static bool IsMapX(byte[] d) => d.Length >= 4 && d[0] == 'M' && d[1] == 'A' && d[2] == 'P' && d[3] == 'X';

    public static (BloodMapFile map, MapXExtras extras) Read(string path)
    {
        var r = Read(File.ReadAllBytes(path));
        r.map.Name = Path.GetFileNameWithoutExtension(path).ToUpperInvariant();
        return r;
    }

    public static (BloodMapFile map, MapXExtras extras) Read(byte[] d)
    {
        if (!IsMapX(d) || d.Length < 8)
            throw new InvalidDataException("not a .MAPX");
        int version = BitConverter.ToUInt16(d, 4);
        if (version > FormatVersion)
            throw new InvalidDataException($".MAPX version {version}, saved by a newer Chosen: this one reads up to version {FormatVersion} (update Chosen)");
        if (version < 1)
            throw new InvalidDataException($".MAPX version {version}: not a version there has been");
        if (Migrations.Length != FormatVersion - 1)
            throw new InvalidOperationException($"MapXFile: FormatVersion {FormatVersion} needs {FormatVersion - 1} migrations, there are {Migrations.Length}");
        var m = new BloodMapFile { FromMapX = true };
        var x = new MapXExtras { FileVersion = version };
        bool head = false, sect = false, wall = false, sprt = false, crc = false;
        int pos = 8;
        while (pos < d.Length)
        {
            if (crc)
                throw new InvalidDataException(".MAPX: chunks after its CRC");
            if (pos + 8 > d.Length)
                throw new InvalidDataException(".MAPX: cut short in a chunk header");
            string cc = Encoding.ASCII.GetString(d, pos, 4);
            int len = BitConverter.ToInt32(d, pos + 4);
            int body = pos + 8;
            if (len < 0 || body + len > d.Length)
                throw new InvalidDataException($".MAPX: chunk {cc} runs past the end");
            var c = new Reader(d, body, len, cc, version);
            switch (cc)
            {
                case "INFO":
                    foreach (var line in Encoding.UTF8.GetString(d, body, len).Split('\n'))
                    {
                        int eq = line.IndexOf('=');
                        if (eq > 0)
                            x.Info.Add(new(line[..eq], line[(eq + 1)..]));
                    }
                    break;
                case "HEAD":
                    ReadHead(c, m);
                    head = true;
                    break;
                case "SECT":
                    Need(head, cc);
                    for (int n = c.I32(), i = 0; i < n; i++)
                    {
                        var s = MapRecords.ReadSector(d, c.Take(MapRecords.SectorSize));
                        if (c.U8() != 0)
                            s.Xs = XSector.Read(d, c.Take(m.XSectorSize), m.XSectorSize);
                        m.Sectors.Add(s);
                    }
                    sect = true;
                    break;
                case "WALL":
                    Need(head, cc);
                    for (int n = c.I32(), i = 0; i < n; i++)
                    {
                        var w = MapRecords.ReadWall(d, c.Take(MapRecords.WallSize));
                        if (c.U8() != 0)
                            w.Xw = XWall.Read(d, c.Take(m.XWallSize), m.XWallSize);
                        m.Walls.Add(w);
                    }
                    wall = true;
                    break;
                case "SPRT":
                    Need(head, cc);
                    for (int n = c.I32(), i = 0; i < n; i++)
                    {
                        var sp = MapRecords.ReadSprite(d, c.Take(MapRecords.SpriteSize), i);
                        if (c.U8() != 0)
                            sp.Xsp = XSprite.Read(d, c.Take(m.XSpriteSize), m.XSpriteSize);
                        m.Sprites.Add(sp);
                    }
                    sprt = true;
                    break;
                case "SPRX":
                    for (int n = c.I32(), i = 0; i < n; i++)
                        x.SpriteExtras.Add(new SpriteExtra(c.U16(), c.Str(), c.Str()));
                    break;
                case "NAME":
                    for (int n = c.I32(), i = 0; i < n; i++)
                    {
                        int kind = c.U8(), index = c.U16();
                        x.Names[(kind, index)] = c.Str();
                    }
                    break;
                case "LAYR":
                    for (int n = c.U16(), i = 0; i < n; i++)
                        x.Layers.Add(new MapLayer { Name = c.Str(), Visible = (c.U8() & 1) != 0 });
                    x.CurrentLayer = c.U16();
                    for (int n = c.I32(), i = 0; i < n; i++)
                        x.SectorLayers.Add(c.U16());
                    // (sprites' own layers: 0xFFFF its sector's)
                    if (c.Left >= 4)
                        for (int n = c.I32(), i = 0; i < n; i++)
                            x.SpriteLayers.Add(c.U16() is var l && l == 0xFFFF ? -1 : l);
                    break;
                case "ARTS":
                    for (int n = c.U16(), i = 0; i < n; i++)
                    {
                        int flags = c.U8();
                        x.Art.Add(new ArtRef(c.Str(), (flags & 1) != 0));
                    }
                    break;
                case "LITE":
                    x.Lights.AddRange(LightRules.Read(d, body, len));
                    x.HasLights = true;
                    break;
                case "ATMO":
                {
                    var (list, sectors) = AtmosphereRules.Read(d, body, len);
                    x.Atmospheres.AddRange(list);
                    x.SectorAtmospheres.AddRange(sectors);
                    x.HasAtmosphere = true;
                    break;
                }
                case "EDIT":
                    x.Edit = new EditState { CentreX = c.I32(), CentreY = c.I32(), Zoom = c.F32(), Grid = c.I32(), Mode = c.U8() };
                    break;
                case "CRC ":
                    if (c.U32() != Crc32.Of(d, pos))
                        throw new InvalidDataException(".MAPX CRC mismatch");
                    crc = true;
                    break;
                default:
                    x.Unknown.Add((cc, d[body..(body + len)]));
                    break;
            }
            pos = body + len;
        }
        if (!head || !sect || !wall || !sprt)
            throw new InvalidDataException(".MAPX without its HEAD, SECT, WALL and SPRT chunks");
        if (!crc)
            throw new InvalidDataException(".MAPX without its CRC");
        for (int si = 0; si < m.Sectors.Count; si++)
            for (int w = m.Sectors[si].WallPtr; w < m.Sectors[si].WallPtr + m.Sectors[si].WallNum && w < m.Walls.Count; w++)
                m.Walls[w].Sector = si;
        MapPatch.ApplySprites(m, x.SpriteExtras);
        for (int v = version; v < FormatVersion; v++)
            Migrations[v - 1](m, x);
        return (m, x);
    }

    static void Need(bool head, string cc)
    {
        if (!head)
            throw new InvalidDataException($".MAPX: {cc} before HEAD");
    }

    static void ReadHead(Reader c, BloodMapFile m)
    {
        m.StartX = c.I32(); m.StartY = c.I32(); m.StartZ = c.I32(); m.StartAng = c.I16(); m.StartSect = c.I16();
        m.PSkyBits = c.I16(); m.Visibility = c.I32(); m.SongId = c.I32(); m.ParallaxType = c.U8(); m.MapRev = c.I32();
        m.Version = c.U16(); m.HeaderEncrypted = c.U8() != 0;
        if (c.U8() != 0)
        {
            m.Header2 = new byte[128];
            Array.Copy(c.D, c.Take(128), m.Header2, 0, 128);
        }
        m.XSpriteSize = c.U16(); m.XWallSize = c.U16(); m.XSectorSize = c.U16();
        if (m.XSpriteSize > XSprite.FullSize || m.XWallSize > XWall.FullSize || m.XSectorSize > XSector.FullSize
            || m.XSpriteSize <= 0 || m.XWallSize <= 0 || m.XSectorSize <= 0)
            throw new InvalidDataException($".MAPX x-structure sizes {m.XSpriteSize}/{m.XWallSize}/{m.XSectorSize}");
        int nsky = c.U16();
        m.SkyOfs = new short[nsky];
        for (int i = 0; i < nsky; i++)
            m.SkyOfs[i] = (short)c.I16();
        int tail = c.I32(), at = c.Take(tail);
        m.Tail = c.D[at..(at + tail)];
    }

    /// <summary>The .MAPX bytes of a map and its extras (extras null: none).</summary>
    public static byte[] Write(BloodMapFile m, MapXExtras x = null)
    {
        x ??= new MapXExtras();
        using var ms = new MemoryStream();
        var bw = new BinaryWriter(ms);
        bw.Write(Magic);
        bw.Write((ushort)FormatVersion);
        bw.Write((ushort)0);

        if (x.Info.Count > 0)
            Chunk(bw, "INFO", w => w.Write(Encoding.UTF8.GetBytes(string.Join("\n", x.Info.Select(kv => kv.Key + "=" + kv.Value)))));
        Chunk(bw, "HEAD", w =>
        {
            w.Write(m.StartX); w.Write(m.StartY); w.Write(m.StartZ); w.Write((short)m.StartAng); w.Write((short)m.StartSect);
            w.Write((short)m.PSkyBits); w.Write(m.Visibility); w.Write(m.SongId); w.Write((byte)m.ParallaxType); w.Write(m.MapRev);
            w.Write((ushort)m.Version); w.Write((byte)(m.HeaderEncrypted ? 1 : 0));
            w.Write((byte)(m.Header2 != null ? 1 : 0));
            if (m.Header2 != null)
                w.Write(m.Header2, 0, 128);
            w.Write((ushort)m.XSpriteSize); w.Write((ushort)m.XWallSize); w.Write((ushort)m.XSectorSize);
            w.Write((ushort)m.SkyOfs.Length);
            foreach (var o in m.SkyOfs)
                w.Write(o);
            w.Write(m.Tail.Length);
            w.Write(m.Tail);
        });
        Chunk(bw, "SECT", w =>
        {
            w.Write(m.Sectors.Count);
            var r = new byte[MapRecords.SectorSize];
            foreach (var s in m.Sectors)
            {
                MapRecords.WriteSector(s, r, 0);
                w.Write(r);
                WriteX(w, s.Xs == null ? null : (s.Xs.Size, s.Xs.Write), m.XSectorSize, "sector");
            }
        });
        Chunk(bw, "WALL", w =>
        {
            w.Write(m.Walls.Count);
            var r = new byte[MapRecords.WallSize];
            foreach (var wl in m.Walls)
            {
                MapRecords.WriteWall(wl, r, 0);
                w.Write(r);
                WriteX(w, wl.Xw == null ? null : (wl.Xw.Size, wl.Xw.Write), m.XWallSize, "wall");
            }
        });
        Chunk(bw, "SPRT", w =>
        {
            w.Write(m.Sprites.Count);
            var r = new byte[MapRecords.SpriteSize];
            foreach (var sp in m.Sprites)
            {
                MapRecords.WriteSprite(sp, r, 0);
                w.Write(r);
                WriteX(w, sp.Xsp == null ? null : (sp.Xsp.Size, sp.Xsp.Write), m.XSpriteSize, "sprite");
            }
        });
        if (x.SpriteExtras.Count > 0)
            Chunk(bw, "SPRX", w =>
            {
                w.Write(x.SpriteExtras.Count);
                foreach (var e in x.SpriteExtras)
                {
                    w.Write((ushort)e.Sprite);
                    Str(w, e.Key);
                    Str(w, e.Value);
                }
            });
        if (x.Names.Count > 0)
            Chunk(bw, "NAME", w =>
            {
                w.Write(x.Names.Count);
                foreach (var ((kind, index), name) in x.Names)
                {
                    w.Write((byte)kind);
                    w.Write((ushort)index);
                    Str(w, name);
                }
            });
        if (x.Layers.Count > 1)
            Chunk(bw, "LAYR", w =>
            {
                w.Write((ushort)x.Layers.Count);
                foreach (var l in x.Layers)
                {
                    Str(w, l.Name);
                    w.Write((byte)(l.Visible ? 1 : 0));
                }
                w.Write((ushort)x.CurrentLayer);
                // one a sector, always (a sector missing from the list is in layer 0): the same layers, the same bytes
                w.Write(m.Sectors.Count);
                for (int i = 0; i < m.Sectors.Count; i++)
                    w.Write((ushort)(i < x.SectorLayers.Count ? x.SectorLayers[i] : 0));
                w.Write(m.Sprites.Count);
                for (int i = 0; i < m.Sprites.Count; i++)
                    w.Write((ushort)(i < x.SpriteLayers.Count && x.SpriteLayers[i] >= 0 ? x.SpriteLayers[i] : 0xFFFF));
            });
        if (x.Art.Count > 0)
            Chunk(bw, "ARTS", w =>
            {
                w.Write((ushort)x.Art.Count);
                foreach (var a in x.Art)
                {
                    w.Write((byte)(a.Override ? 1 : 0));
                    Str(w, a.Path);
                }
            });
        if (x.HasLights || x.Lights.Count > 0)
            Chunk(bw, "LITE", w => LightRules.Write(w, x.Lights));
        if (x.HasAtmosphere && x.Atmospheres.Count > 0)
            Chunk(bw, "ATMO", w => AtmosphereRules.Write(w, x.Atmospheres, x.SectorAtmospheres, m.Sectors.Count));
        if (x.Edit is { } e)
            Chunk(bw, "EDIT", w => { w.Write(e.CentreX); w.Write(e.CentreY); w.Write(e.Zoom); w.Write(e.Grid); w.Write((byte)e.Mode); });
        foreach (var (cc, payload) in x.Unknown)
            Chunk(bw, cc, w => w.Write(payload));
        bw.Flush();
        uint crc = Crc32.Of(ms.GetBuffer(), (int)ms.Length);
        Chunk(bw, "CRC ", w => w.Write(crc));
        bw.Flush();
        return ms.ToArray();
    }

    static void WriteX(BinaryWriter w, (int size, Action<byte[], int> write)? xs, int size, string what)
    {
        w.Write((byte)(xs != null ? 1 : 0));
        if (xs != null)
            w.Write(MapRecords.XBytes(xs, size, what));
    }

    static void Chunk(BinaryWriter bw, string fourcc, Action<BinaryWriter> body)
    {
        if (fourcc.Length != 4)
            throw new ArgumentException($"chunk id '{fourcc}' is not 4 characters");
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, Encoding.UTF8, true))
            body(w);
        bw.Write(Encoding.ASCII.GetBytes(fourcc));
        bw.Write((int)ms.Length);
        bw.Write(ms.GetBuffer(), 0, (int)ms.Length);
    }

    static void Str(BinaryWriter w, string s)
    {
        var b = Encoding.UTF8.GetBytes(s ?? "");
        w.Write((ushort)b.Length);
        w.Write(b);
    }

    /// <summary>Reads a chunk's payload, failing when a read would run past it.</summary>
    sealed class Reader
    {
        public readonly byte[] D;
        public int Pos;
        /// <summary>The file's .MAPX version: a chunk whose layout changed reads the old layout by it.</summary>
        public readonly int Version;
        readonly int _end;
        readonly string _cc;

        public Reader(byte[] d, int at, int len, string cc, int version)
        {
            D = d;
            Pos = at;
            _end = at + len;
            _cc = cc;
            Version = version;
        }

        /// <summary>Takes n bytes, returning where they start.</summary>
        public int Left => _end - Pos;

        public int Take(int n)
        {
            if (n < 0 || Pos + n > _end)
                throw new InvalidDataException($".MAPX: chunk {_cc} is too short");
            int at = Pos;
            Pos += n;
            return at;
        }

        public int U8() => D[Take(1)];
        public int U16() => BitConverter.ToUInt16(D, Take(2));
        public int I16() => BitConverter.ToInt16(D, Take(2));
        public int I32() => BitConverter.ToInt32(D, Take(4));
        public uint U32() => BitConverter.ToUInt32(D, Take(4));
        public float F32() => BitConverter.ToSingle(D, Take(4));
        public string Str()
        {
            int n = U16();
            return Encoding.UTF8.GetString(D, Take(n), n);
        }
    }
}
