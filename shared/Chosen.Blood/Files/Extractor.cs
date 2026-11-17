// Chosen Blood module (GPL v2): what the game needs from a Blood install, extracted into its data folder (the runtime
// parts of pipeline/export.py, voxels.py, export_maps.py and export_addon.py, ported).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Chosen.Shared.Blood.Files;

/// <summary>
/// Writes the data folder Chosen reads (fs_blooddata; beside Chosen.exe as data/ in a distribution): from the install
/// (BLOOD.RFF, SOUNDS.RFF, PATCH01.RFF, TILES*.ART, the DATs, BloodEX.kpf, the CD tracks, SC-55 recordings and movies):
/// <list type="bullet">
/// <item>art/tilesNNN.art (copied: the game draws the tiles from them), gfx/ the palettes and palette lookups</item>
/// <item>data/ the tile, SEQ, QAV and sound tables (and their runtime packing), blood.ini, loc_english.txt, rfire.clu,
/// voxels.txt</item>
/// <item>sound/blood/*.wav, sound/cdtracks, sound/midi, sound/sc55; movie/ (and movie/remastered)</item>
/// <item>models/voxels/*.md3; maps/*.map with Refreshed Supply's .patch beside them</item>
/// <item>addons/&lt;id&gt;/ for each add-on chosen: its art, tile tables, episode file, music, cutscenes and maps; an
/// NBlood add-on's (Addon.Mod) also what its resource scripts (.RFS) put into the archives: the animation, weapon
/// animation and sound tables with its own added (data/), its sounds (sound/blood), its palette lookups
/// (gfx/palookup.bin), its voxel models (models/voxels, data/voxels.txt by its VOXEL.DAT), and its custom dude
/// descriptors (data/cdud)</item>
/// </list>
/// Files are written whole (a temporary name, then renamed), so a stopped run leaves no half file.
/// </summary>
public sealed class Extractor
{
    public string BloodDir = "", OutDir = "";
    /// <summary>The add-ons to install (ids or folder names); null: all.</summary>
    public IReadOnlyCollection<string> Addons;
    /// <summary>NBlood add-ons' folders outside the install (Eviction's EVICTION): installed whatever Addons says.</summary>
    public IReadOnlyCollection<string> Mods;
    /// <summary>Progress 0..1 and what is being done.</summary>
    public Action<double, string> Progress;
    public Action<string> Log;
    public Func<bool> Cancelled;

    public static readonly string[] PluNames =
        { "NORMAL", "SATURATE", "BEAST", "TOMMY", "SPIDER3", "GRAY", "GRAYISH", "SPIDER1", "SPIDER2", "FLAME", "COLD", "P1", "P2", "P3", "P4" };
    static readonly string[] ViewPalettes = { "WATER", "BEAST", "SEWER", "INVULN1" };

    /// <summary>The marker the game and the loader read (data/extracted.txt): what this folder was made from, the
    /// extraction's DataVersion and the Chosen version that made it.</summary>
    public const string Marker = "data/extracted.txt";

    /// <summary>The data folder's format: raised whenever the extraction's output changes in a way the game needs (a
    /// folder made with a lower one is extracted again: the game says so). 1: Chosen 0.1.0 (the voxel models' faces
    /// merged).</summary>
    public const int DataVersion = 1;

    /// <summary>The DataVersion a data folder's marker gives (0: none, a folder from before there was one; -1: no
    /// marker at all).</summary>
    public static int FolderDataVersion(string folder)
    {
        string m = Path.Combine(folder, Marker);
        if (!File.Exists(m))
            return -1;
        foreach (var l in File.ReadAllLines(m))
            if (l.StartsWith("data ") && int.TryParse(l[5..].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v))
                return v;
        return 0;
    }

    int _files;
    long _bytes;
    // the base game's tables and files, for an add-on that adds to them
    Rff _blood, _sounds;
    SortedDictionary<int, Seq> _seqs;
    SortedDictionary<int, Qav> _qavs;
    readonly List<Tables.SfxRow> _sfxRows = new();
    readonly Dictionary<string, int> _wavFormat = new();
    byte[] _palookups = Array.Empty<byte>();

    string Out(string rel) => Path.Combine(OutDir, rel.Replace('/', Path.DirectorySeparatorChar));

    void Write(string rel, byte[] data)
    {
        string p = Out(rel);
        Directory.CreateDirectory(Path.GetDirectoryName(p));
        string tmp = p + ".tmp";
        File.WriteAllBytes(tmp, data);
        File.Move(tmp, p, true);
        _files++;
        _bytes += data.Length;
    }

    void Write(string rel, string text) => Write(rel, new UTF8Encoding(false).GetBytes(text));

    void Copy(string src, string rel)
    {
        string p = Out(rel);
        Directory.CreateDirectory(Path.GetDirectoryName(p));
        // unchanged files (same size and not older) are kept: running the loader again is quick
        var s = new FileInfo(src);
        var d = new FileInfo(p);
        if (d.Exists && d.Length == s.Length && d.LastWriteTimeUtc >= s.LastWriteTimeUtc)
            return;
        File.Copy(src, p + ".tmp", true);
        File.Move(p + ".tmp", p, true);
        _files++;
        _bytes += s.Length;
    }

    void Step(double at, string what)
    {
        if (Cancelled?.Invoke() == true)
            throw new OperationCanceledException();
        Progress?.Invoke(at, what);
    }

    string Find(string name) => BloodInstall.FindFile(BloodDir, name);

    public void Run()
    {
        if (!BloodInstall.IsValid(BloodDir))
            throw new InvalidDataException($"{BloodDir} is not a Blood install (no BLOOD.RFF and TILES000.ART)");
        Directory.CreateDirectory(OutDir);
        var blood = new Rff(Find("BLOOD.RFF"));
        var sounds = Find("SOUNDS.RFF") is { } sp ? new Rff(sp) : null;
        using var kpf = Kpf.Open(Find("BloodEX.kpf"));
        var surface = Assets.Dat(BloodDir, "surface.dat", 8192, false);
        var shade = Assets.Dat(BloodDir, "SHADE.DAT", 8192, false);
        var voxelDat = Assets.Dat(BloodDir, "voxel.dat", 4096, true);

        Step(0.02, "palettes");
        Palettes(blood);
        Step(0.05, "tiles");
        var art = ArtSet.Load(BloodDir);
        foreach (var f in ArtFiles(BloodDir))
            Copy(f, "art/" + Path.GetFileName(f).ToLowerInvariant());
        Write("data/tiles.txt", Tables.TilesTxt(art, surface, shade, voxelDat));
        Write("data/rt_tiles.txt", Tables.RtTiles(art, surface, shade, voxelDat));
        Step(0.15, "animations");
        _blood = blood;
        _sounds = sounds;
        var seqs = _seqs = Assets.Seqs(blood);
        Write("data/seq.txt", Tables.SeqTxt(seqs));
        Write("data/rt_seq.txt", Tables.RtSeq(seqs));
        var qavs = _qavs = Assets.Qavs(blood);
        Write("data/qav.txt", Tables.QavTxt(qavs));
        Write("data/rt_qav.txt", Tables.RtQav(qavs));
        if (blood.Read("RFIRE", "CLU") is { } clu)
            Write("data/rfire.clu", clu);
        Step(0.2, "sounds");
        if (sounds != null)
            Sounds(sounds);
        Step(0.35, "music");
        Music(sounds);
        Step(0.45, "level info");
        if (Find("blood.ini") is { } ini)
            Copy(ini, "data/blood.ini");
        if (kpf?.Read("localization/loc_english.txt") is { } loc)
            Write("data/loc_english.txt", loc);
        Step(0.5, "movies");
        Movies(sounds);
        Step(0.6, "voxel models");
        Write("data/voxels.txt", VoxelModels.Build(blood, art, voxelDat, kpf, (name, md3) => Write($"models/voxels/{name}.md3", md3)));
        Step(0.75, "maps");
        Maps(kpf);
        // (a mod's folder, or the folder it was unpacked into: Addon.ModsIn)
        var named = new List<string>();
        foreach (var m in Mods ?? Array.Empty<string>())
        {
            var found = Files.Addon.ModsIn(m);
            if (found.Count == 0)
                Log?.Invoke($"{m}: not an add-on's folder (no episode file with its maps)");
            named.AddRange(found);
        }
        var addons = Files.Addon.All(BloodDir, named).Where(a => Addons == null || named.Contains(a.Folder, StringComparer.OrdinalIgnoreCase)
            || Addons.Any(x => x.Equals(a.Id, StringComparison.OrdinalIgnoreCase) || x.Equals(a.Name, StringComparison.OrdinalIgnoreCase))).ToList();
        for (int i = 0; i < addons.Count; i++)
        {
            Step(0.85 + 0.14 * i / Math.Max(1, addons.Count), "add-on: " + addons[i].Name);
            Addon(addons[i], surface, shade, voxelDat, kpf);
        }
        Write(Marker, $"from {BloodDir}\nwhen {DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)}\n"
            + $"addons {string.Join(" ", addons.Select(a => a.Id))}\n"
            + $"data {DataVersion}\nchosen {Chosen.Shared.Engine.ChosenVersion.Full}\n");
        Step(1, $"done: {_files} files written, {_bytes / (1024 * 1024)} MB");
    }

    static IEnumerable<string> ArtFiles(string folder) => Directory.GetFiles(folder)
        .Where(f => System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(f), @"^tiles\d+\.art$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
        .OrderBy(f => Path.GetFileName(f).ToLowerInvariant(), StringComparer.Ordinal);

    void Palettes(Rff blood)
    {
        Write("gfx/palette.lmp", blood.Read("BLOOD", "PAL")[..768]);
        foreach (var name in ViewPalettes)
            if (blood.Read(name, "PAL") is { } p)
                Write($"gfx/{name.ToLowerInvariant()}.lmp", p[..Math.Min(768, p.Length)]);
        var normal = blood.Read("NORMAL", "PLU");
        normal = normal[..Math.Min(64 * 256, normal.Length)];
        Write("gfx/normal.plu", normal);
        using var ms = new MemoryStream();
        foreach (var name in PluNames)
        {
            var d = blood.Read(name, "PLU");
            d = d == null ? normal : d[..Math.Min(64 * 256, d.Length)];
            ms.Write(d.Length == 64 * 256 ? d : normal);
        }
        _palookups = ms.ToArray();
        Write("gfx/palookup.bin", _palookups);
    }

    void Sounds(Rff sounds)
    {
        var rows = _sfxRows;
        var written = new HashSet<(string, int, int)>();
        void Wav(string fn, byte[] pcm, int fmt, int loop)
        {
            if (written.Add((fn, fmt, loop)))
            {
                Write($"sound/blood/{fn}.wav", Tables.Wav(pcm, Sfx.Rate(fmt), loop));
                _wavFormat[fn] = fmt;
            }
        }
        foreach (var (id, s) in Assets.Sfxs(sounds))
        {
            if (sounds.Find(s.Raw, "RAW") is not { } e)
                continue;
            string name = s.Raw.ToLowerInvariant();
            var pcm = sounds.Read(e);
            Wav(name, pcm, s.Format, -1);
            string fn = name;
            if (s.LoopStart >= 0)
            {
                fn = name + "_loop";
                Wav(fn, pcm, s.Format, s.LoopStart);
            }
            rows.Add(new Tables.SfxRow(id, fn, s.RelVol, s.Pitch, s.PitchRange, s.Format, s.LoopStart));
        }
        Write("data/sfx.txt", Tables.SfxTxt(rows));
        Write("data/rt_sfx.txt", Tables.RtSfx(rows));
        // Refreshed Supply's CD tracks
        for (int n = 2; n < 10; n++)
            if (Find($"blood{n:D2}.ogg") is { } ogg)
                Copy(ogg, $"sound/cdtracks/track{n:D3}.ogg");
    }

    void Music(Rff sounds)
    {
        if (sounds != null)
            foreach (var e in sounds.Entries)
            {
                string t = e.Type.ToUpperInvariant();
                if (t == "MID" || t == "TMB")
                    Write($"sound/midi/{e.Name.ToLowerInvariant()}.{t.ToLowerInvariant()}", sounds.Read(e));
            }
        string sc55 = Path.Combine(BloodDir, "SC-55");
        if (Directory.Exists(sc55))
            foreach (var f in Directory.GetFiles(sc55).Where(f => f.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase)))
                Copy(f, "sound/sc55/" + Path.GetFileName(f).ToLowerInvariant());
    }

    void Movies(Rff sounds)
    {
        var done = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dir in new[] { Path.Combine(BloodDir, "DOS", "BLOOD121"), Path.Combine(BloodDir, "movie"), BloodDir })
        {
            if (!Directory.Exists(dir))
                continue;
            foreach (var f in Directory.GetFiles(dir))
            {
                string low = Path.GetFileName(f).ToLowerInvariant();
                if ((low.EndsWith(".smk") || (low.StartsWith("cs") && low.EndsWith("822m.wav"))) && done.Add(low))
                    Copy(f, "movie/" + low);
            }
        }
        if (sounds != null)
            foreach (var (id, name) in new[] { (300, "logo.wav"), (301, "gti.wav") })
                if (sounds.Find(id, "WAV") is { } e)
                    Write("movie/" + name, sounds.Read(e));
        string remastered = Path.Combine(BloodDir, "movie");
        if (Directory.Exists(remastered))
            foreach (var f in Directory.GetFiles(remastered))
            {
                string low = Path.GetFileName(f).ToLowerInvariant();
                if (low.StartsWith("cs") && (low.EndsWith(".ogv") || (low.EndsWith(".srt") && !low.Contains('_'))))
                    Copy(f, "movie/remastered/" + low);
            }
    }

    static string PatchText(List<string[]> cmds) => string.Concat(cmds.Select(c => string.Join(" ", c) + "\n"));

    void WriteMap(string folder, string name, byte[] data, IEnumerable<Kpf> kpfs)
    {
        Write($"{folder}/{name.ToUpperInvariant()}.map", data);
        var patch = LevelPatches.For(name, data, kpfs, Log);
        string pp = Out($"{folder}/{name.ToUpperInvariant()}.patch");
        if (patch.Count > 0)
            Write($"{folder}/{name.ToUpperInvariant()}.patch", PatchText(patch));
        else if (File.Exists(pp))
            File.Delete(pp);
    }

    void Maps(Kpf kpf)
    {
        var rffs = new[] { "PATCH01.RFF", "BLOOD.RFF" }.Select(Find).Where(p => p != null).Select(p => new Rff(p)).ToList();
        var names = new List<string>();
        foreach (var r in rffs)
            foreach (var e in r.Entries)
                if (e.Type.Equals("MAP", StringComparison.OrdinalIgnoreCase) && !names.Contains(e.Name.ToUpperInvariant()))
                    names.Add(e.Name.ToUpperInvariant());
        foreach (var n in names)
        {
            var data = rffs.Select(r => r.Find(n, "MAP") is { } e ? r.Read(e) : null).First(d => d != null);
            WriteMap("maps", n, data, new[] { kpf });
        }
        Log?.Invoke($"{names.Count} base maps");
    }

    void Addon(Addon a, int[] surface, int[] shade, int[] voxelDat, Kpf baseKpf)
    {
        string root = "addons/" + a.Id;
        // its art (each file replaces the base game's of its name whole) and the tile table with it in place
        var files = ArtFiles(BloodDir).ToDictionary(f => Path.GetFileName(f).ToLowerInvariant());
        foreach (var f in a.ArtFiles())
        {
            files[Path.GetFileName(f).ToLowerInvariant()] = f;
            Copy(f, $"{root}/art/{Path.GetFileName(f).ToLowerInvariant()}");
        }
        var art = new ArtSet();
        foreach (var k in files.Keys.OrderBy(k => k, StringComparer.Ordinal))
            art.AddFile(File.ReadAllBytes(files[k]));
        if (a.Mod)
        {
            // its own tables (they cover its added tiles) and what its scripts pack into the archives
            surface = Assets.Dat(a.Folder, "surface.dat", MapArt.MaxTile + 1, false) ?? surface;
            shade = Assets.Dat(a.Folder, "shade.dat", MapArt.MaxTile + 1, false) ?? shade;
            voxelDat = Assets.Dat(a.Folder, "voxel.dat", MapArt.MaxTile + 1, true) ?? voxelDat;
            ModFiles(a, root, art, voxelDat, baseKpf);
        }
        Write($"{root}/data/tiles.txt", Tables.TilesTxt(art, surface, shade, voxelDat));
        Write($"{root}/data/rt_tiles.txt", Tables.RtTiles(art, surface, shade, voxelDat));
        if (a.Ini != null)
            Copy(a.Ini, $"{root}/data/blood.ini");
        // (a mod's folder too, "source": ChosenLoader lists it again the next time, pipeline/addons.py starts NBlood with it)
        Write($"{root}/data/addon.txt", $"title {a.Name}\nfolder {a.Name}\nmaps {string.Join(" ", a.Maps.Select(m => m.ToLowerInvariant()))}\n"
            + (a.Mod ? $"source {Path.GetFullPath(a.Folder).TrimEnd('/', '\\')}\n" : ""));
        foreach (var f in Directory.GetFiles(a.Folder))
        {
            string low = Path.GetFileName(f).ToLowerInvariant();
            if (low.StartsWith("blood") && low.EndsWith(".ogg") && int.TryParse(low[5..^4], NumberStyles.None, CultureInfo.InvariantCulture, out int n))
                Copy(f, $"{root}/sound/cdtracks/track{n:D3}.ogg");
            else if (low.EndsWith(".mid"))
                Copy(f, $"{root}/sound/midi/{low}");
            else if (low.EndsWith(".ogv") || (low.EndsWith(".srt") && !low.Contains('_')))
                Copy(f, $"{root}/movie/remastered/{low}");
        }
        string sc55 = Path.Combine(a.Folder, "SC-55");
        if (Directory.Exists(sc55))
            foreach (var f in Directory.GetFiles(sc55).Where(f => f.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase)))
                Copy(f, $"{root}/sound/sc55/{Path.GetFileName(f).ToLowerInvariant()}");
        var kpfs = Directory.GetFiles(a.Folder, "*.kpf").Select(Kpf.Open).Append(baseKpf).ToList();
        try
        {
            int k = 0;
            foreach (var m in a.Maps)
                if (a.MapFile(m) is { } f)
                {
                    WriteMap($"{root}/maps", m, File.ReadAllBytes(f), kpfs);
                    k++;
                }
            Log?.Invoke($"{a.Id}: {k} maps");
        }
        finally
        {
            foreach (var x in kpfs)
                if (x != baseKpf)
                    x?.Dispose();
        }
    }

    /// <summary>What an NBlood add-on's resource scripts add to the archives, as the add-on's own files: the animation
    /// (SEQ), weapon animation (QAV) and sound (SFX) tables with its entries added to the base game's or in place of
    /// them, its samples, the palette lookups it numbers from 15 up, the voxel models its VOXEL.DAT gives its tiles
    /// (its own KVX by id before the base game's), and its custom dude descriptors (CDUDnnnn.CDU).</summary>
    void ModFiles(Addon a, string root, ArtSet art, int[] voxelDat, Kpf kpf)
    {
        var res = ModResources.Load(a.Folder);
        foreach (var p in res.Problems)
            Log?.Invoke($"{a.Id}: {p}");
        var seqs = new SortedDictionary<int, Seq>(_seqs);
        int n = 0;
        foreach (var e in res.OfType("SEQ"))
            if (e.Id >= 0 && Seq.Parse(e.Id, e.Bytes) is { } s)
            {
                seqs[e.Id] = s;
                n++;
            }
        if (n > 0)
        {
            Write($"{root}/data/seq.txt", Tables.SeqTxt(seqs));
            Write($"{root}/data/rt_seq.txt", Tables.RtSeq(seqs));
        }
        var qavs = new SortedDictionary<int, Qav>(_qavs);
        int nq = 0;
        foreach (var e in res.OfType("QAV"))
            if (e.Id >= 0 && Qav.Parse(e.Id, e.Name, e.Bytes) is { } q)
            {
                qavs[e.Id] = q;
                nq++;
            }
        if (nq > 0)
        {
            Write($"{root}/data/qav.txt", Tables.QavTxt(qavs));
            Write($"{root}/data/rt_qav.txt", Tables.RtQav(qavs));
        }
        int ns = ModSounds(root, res);
        // palette lookups by number, from 15 (NBlood's kUserPLUStart); the numbers between as the normal one
        var plus = res.OfType("PLU").Where(e => e.Id >= PluNames.Length && e.Id < 256 && e.Bytes.Length >= 64 * 256).ToList();
        if (plus.Count > 0 && _palookups.Length >= 64 * 256)
        {
            int count = plus.Max(e => e.Id) + 1;
            var all = new byte[count * 64 * 256];
            for (int i = 0; i < count; i++)
                Array.Copy(_palookups, i < PluNames.Length ? i * 64 * 256 : 0, all, i * 64 * 256, 64 * 256);
            foreach (var e in plus)
                Array.Copy(e.Bytes, 0, all, e.Id * 64 * 256, 64 * 256);
            Write($"{root}/gfx/palookup.bin", all);
        }
        // voxel models: the add-on's own (named mod_v<id>) into its folder, the rest are the base game's files
        var kvx = new Dictionary<int, byte[]>();
        foreach (var e in res.OfType("KVX"))
            if (e.Id >= 0)
                kvx[e.Id] = e.Bytes;
        if (kvx.Count > 0 || Assets.Dat(a.Folder, "voxel.dat", 1, true) != null)
            Write($"{root}/data/voxels.txt", VoxelModels.Build(_blood, art, voxelDat, kpf, (name, md3) =>
            {
                if (name.StartsWith(VoxelModels.OwnPrefix))
                    Write($"{root}/models/voxels/{name}.md3", md3);
            }, id => kvx.GetValueOrDefault(id)));
        int nc = 0;
        foreach (var f in Directory.GetFiles(a.Folder).Where(f => f.EndsWith(".cdu", StringComparison.OrdinalIgnoreCase)))
        {
            Copy(f, $"{root}/data/cdud/{Path.GetFileName(f).ToLowerInvariant()}");
            nc++;
        }
        foreach (var e in res.OfType("CDU"))
        {
            Write($"{root}/data/cdud/{(e.Id >= 0 ? $"cdud{e.Id.ToString(CultureInfo.InvariantCulture)}" : e.Name.ToLowerInvariant())}.cdu", e.Bytes);
            nc++;
        }
        Log?.Invoke($"{a.Id}: {n} animations, {ns} sounds, {plus.Count} palette lookups, {kvx.Count} voxels, {nc} custom dudes");
    }

    // the add-on's sounds: its SFX entries over the base game's table, each one's sample its own RAW or SOUNDS.RFF's;
    // a sample another entry already has as a file is not written again unless this one plays it at another rate
    int ModSounds(string root, ModResources res)
    {
        var rows = new SortedDictionary<int, Tables.SfxRow>();
        foreach (var r in _sfxRows)
            rows[r.Id] = r;
        var own = new Dictionary<string, int>();
        int n = 0;
        foreach (var e in res.OfType("SFX"))
        {
            if (e.Id < 0 || Sfx.Parse(e.Id, e.Bytes) is not { } s)
                continue;
            byte[] pcm = res.Find(s.Raw, "RAW")?.Bytes;
            bool mine = pcm != null;
            if (pcm == null && _sounds?.Find(s.Raw, "RAW") is { } be)
                pcm = _sounds.Read(be);
            if (pcm == null)
            {
                Log?.Invoke($"sound {e.Id}: no sample {s.Raw}");
                continue;
            }
            string name = s.Raw.ToLowerInvariant();
            string fn = name;
            bool write = mine;
            if (!mine && (!_wavFormat.TryGetValue(name, out int had) || had != s.Format))
            {
                fn = $"{name}_f{s.Format.ToString(CultureInfo.InvariantCulture)}";
                write = true;
            }
            else if (mine && own.TryGetValue(name, out int f) && f != s.Format)
                fn = $"{name}_f{s.Format.ToString(CultureInfo.InvariantCulture)}";
            if (write)
            {
                own.TryAdd(fn, s.Format);
                Write($"{root}/sound/blood/{fn}.wav", Tables.Wav(pcm, Sfx.Rate(s.Format), -1));
            }
            if (s.LoopStart >= 0)
            {
                fn += "_loop";
                Write($"{root}/sound/blood/{fn}.wav", Tables.Wav(pcm, Sfx.Rate(s.Format), s.LoopStart));
            }
            rows[e.Id] = new Tables.SfxRow(e.Id, fn, s.RelVol, s.Pitch, s.PitchRange, s.Format, s.LoopStart);
            n++;
        }
        if (n > 0)
        {
            Write($"{root}/data/sfx.txt", Tables.SfxTxt(rows.Values));
            Write($"{root}/data/rt_sfx.txt", Tables.RtSfx(rows.Values));
        }
        return n;
    }
}
