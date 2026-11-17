// Chosen Blood module (GPL v2): Refreshed Supply's packages (.kpf), add-ons and level fixes (pipeline/addons.py).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace Chosen.Shared.Blood.Files;

/// <summary>A Refreshed Supply package (BloodEX.kpf, an add-on's .kpf): a zip; names matched case-insensitively.</summary>
public sealed class Kpf : IDisposable
{
    readonly ZipArchive _zip;
    readonly Dictionary<string, ZipArchiveEntry> _names = new(StringComparer.OrdinalIgnoreCase);

    public Kpf(string path)
    {
        _zip = ZipFile.OpenRead(path);
        foreach (var e in _zip.Entries)
            _names.TryAdd(e.FullName.Replace('\\', '/'), e);
    }

    public static Kpf Open(string path) => path != null && File.Exists(path) ? new Kpf(path) : null;

    public bool Has(string name) => _names.ContainsKey(name);

    public byte[] Read(string name)
    {
        if (!_names.TryGetValue(name, out var e))
            return null;
        using var s = e.Open();
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }

    /// <summary>A text file (Latin-1, as the scripts read the defs), or null.</summary>
    public string ReadText(string name) => Read(name) is { } b ? Encoding.Latin1.GetString(b) : null;

    public void Dispose() => _zip.Dispose();
}

/// <summary>An add-on: one of Refreshed Supply's (the install's addons/&lt;folder&gt;: Death Wish, Cryptic Passage,
/// MARROW), or an NBlood add-on's folder (a mod: what NBlood's -game_dir is given, Eviction's EVICTION; in the install's
/// folder as its README says, or anywhere and named to the extraction), which may bring resources by script (.RFS), its
/// own SURFACE.DAT and VOXEL.DAT, and custom dude descriptors (.CDU) beside its episode file, maps, art and music.</summary>
public sealed class Addon
{
    public string Folder = "", Name = "", Id = "";
    public string Ini;
    /// <summary>An NBlood add-on's folder (not one of the install's addons/).</summary>
    public bool Mod;
    /// <summary>Its maps in episode order (the .ini's [EpisodeN] MapN), upper case.</summary>
    public readonly List<string> Maps = new();

    public static Dictionary<string, Dictionary<string, string>> ReadIni(string path)
    {
        var sections = new Dictionary<string, Dictionary<string, string>>();
        Dictionary<string, string> cur = null;
        foreach (var raw in File.ReadLines(path, Encoding.Latin1))
        {
            var line = raw.Split(';', 2)[0].Trim();
            if (line == "")
                continue;
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                string name = line[1..^1].Trim().ToLowerInvariant();
                if (!sections.TryGetValue(name, out cur))
                    sections[name] = cur = new Dictionary<string, string>();
            }
            else if (cur != null && line.Contains('='))
            {
                var kv = line.Split('=', 2);
                cur[kv[0].Trim().ToLowerInvariant()] = kv[1].Trim();
            }
        }
        return sections;
    }

    public Addon(string folder, bool mod = false)
    {
        Mod = mod;
        Folder = folder;
        Name = Path.GetFileName(folder.TrimEnd('/', '\\'));
        Ini = Directory.GetFiles(folder, "*.ini").OrderBy(f => f, StringComparer.Ordinal).FirstOrDefault();
        Id = Ini != null ? Path.GetFileNameWithoutExtension(Ini).ToLowerInvariant() : Name.ToLowerInvariant().Replace(" ", "");
        if (Ini == null)
            return;
        var sec = ReadIni(Ini);
        for (int e = 1; sec.ContainsKey("episode" + e); e++)
            for (int m = 1; sec["episode" + e].TryGetValue("map" + m, out var file); m++)
            {
                string name = Path.GetFileNameWithoutExtension(file.Replace('\\', '/')).ToUpperInvariant();
                if (!Maps.Contains(name))
                    Maps.Add(name);
            }
        // a mod's maps the episodes don't list too (a second version of one, a multiplayer map): played by name
        if (mod)
            foreach (var f in Directory.GetFiles(folder).Where(f => f.EndsWith(".map", StringComparison.OrdinalIgnoreCase))
                .Select(f => Path.GetFileNameWithoutExtension(f).ToUpperInvariant()).OrderBy(n => n, StringComparer.Ordinal))
                if (!Maps.Contains(f))
                    Maps.Add(f);
    }

    /// <summary>Is the folder an NBlood add-on's: an episode file naming maps that are in it?</summary>
    public static bool IsMod(string folder)
    {
        if (!Directory.Exists(folder) || !Directory.GetFiles(folder).Any(f => f.EndsWith(".ini", StringComparison.OrdinalIgnoreCase)))
            return false;
        var a = new Addon(folder);
        return a.Ini != null && a.Maps.Any(m => a.MapFile(m) != null);
    }


    static string Full(string folder) => Path.GetFullPath(folder).TrimEnd('/', '\\');

    /// <summary>The mods a folder the player pointed at holds: the folder itself, else the folders in it that are one
    /// (Eviction's download: EVICTION beside its .bat and its readme). Full paths.</summary>
    public static List<string> ModsIn(string folder)
    {
        var found = new List<string>();
        try
        {
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
                return found;
            if (IsMod(folder))
                found.Add(Full(folder));
            else
                foreach (var d in Directory.GetDirectories(folder).OrderBy(d => Path.GetFileName(d), StringComparer.Ordinal))
                    if (IsMod(d))
                        found.Add(Full(d));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
        return found;
    }

    /// <summary>The mods a data folder was made with before (each add-on's data/addon.txt names its folder, "source"),
    /// those that are still there: ChosenLoader lists them again, so running it after an update keeps them.</summary>
    public static List<string> InstalledMods(string dataDir)
    {
        var found = new List<string>();
        try
        {
            string root = Path.Combine(dataDir ?? "", "addons");
            if (!Directory.Exists(root))
                return found;
            foreach (var dir in Directory.GetDirectories(root).OrderBy(d => Path.GetFileName(d), StringComparer.Ordinal))
            {
                string info = Path.Combine(dir, "data", "addon.txt");
                if (!File.Exists(info))
                    continue;
                foreach (var line in File.ReadLines(info))
                    if (line.StartsWith("source ", StringComparison.Ordinal) && IsMod(line[7..].Trim()))
                        found.Add(Full(line[7..].Trim()));
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
        return found;
    }

    /// <summary>Its file for a map name (any case), or null.</summary>
    public string MapFile(string name) =>
        Directory.GetFiles(Folder).FirstOrDefault(f => Path.GetFileName(f).Equals(name + ".map", StringComparison.OrdinalIgnoreCase));

    public IEnumerable<string> ArtFiles() =>
        Directory.GetFiles(Folder).Where(f => System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(f), @"^tiles\d+\.art$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase));

    /// <summary>The install's add-ons: its addons/ folders, the NBlood add-ons put in its own folder (a folder with an
    /// episode file and its maps), and those named (mods: folders anywhere). One id once, a named one first.</summary>
    public static List<Addon> All(string bloodDir, IEnumerable<string> mods = null)
    {
        var all = new List<Addon>();
        foreach (var m in mods ?? Array.Empty<string>())
            if (IsMod(m))
                all.Add(new Addon(Path.GetFullPath(m).TrimEnd('/', '\\'), true));
        string root = Path.Combine(bloodDir, "addons");
        if (Directory.Exists(root))
            all.AddRange(Directory.GetDirectories(root).OrderBy(d => Path.GetFileName(d), StringComparer.Ordinal).Select(d => new Addon(d)));
        if (Directory.Exists(bloodDir))
            foreach (var d in Directory.GetDirectories(bloodDir).OrderBy(d => Path.GetFileName(d), StringComparer.Ordinal))
                if (!Path.GetFileName(d).Equals("addons", StringComparison.OrdinalIgnoreCase) && IsMod(d))
                    all.Add(new Addon(d, true));
        return all.GroupBy(a => a.Id).Select(g => g.First()).ToList();
    }
}

/// <summary>Refreshed Supply's level fixes (defs/levelHacks/&lt;map&gt;.txt in BloodEX.kpf and an add-on's .kpf).</summary>
public static class LevelPatches
{
    /// <summary>Lines left out, by map: Blood's own content they take away (E1M4's hanging Duke Nukem, sprite 952:
    /// Refreshed Supply draws it flat instead of its voxel model; the original game has it).</summary>
    static readonly Dictionary<string, string[]> Skip = new() { ["E1M4"] = new[] { "sprite 952 novoxel" } };

    /// <summary>A map's fix commands (each its words), [] when it has none or the fix is for another version of the map
    /// (its first line: the CRC-32 stored at the end of the map file it fixes; 0: any). kpfs: searched in order.</summary>
    public static List<string[]> For(string name, byte[] mapData, IEnumerable<Kpf> kpfs, Action<string> log = null)
    {
        uint stored = mapData.Length >= 4 ? BitConverter.ToUInt32(mapData, mapData.Length - 4) : 0;
        foreach (var kpf in kpfs)
        {
            if (kpf == null)
                continue;
            string path = $"defs/levelhacks/{name.ToLowerInvariant()}.txt";
            if (!kpf.Has(path))
                continue;
            var cmds = Read(kpf, path, 0);
            bool crcLine = cmds.Count > 0 && cmds[0].Length == 1 && cmds[0][0].All(char.IsAsciiDigit);
            long crc = crcLine ? long.Parse(cmds[0][0], CultureInfo.InvariantCulture) : 0;
            if (crcLine)
                cmds.RemoveAt(0);
            if (crc != 0 && crc != stored)
            {
                log?.Invoke($"level patch {path}: for another version of the map (CRC {crc}, this map {stored}): not applied");
                return new List<string[]>();
            }
            var skip = Skip.GetValueOrDefault(name.ToUpperInvariant()) ?? Array.Empty<string>();
            return cmds.Where(c => !skip.Contains(string.Join(" ", c))).ToList();
        }
        return new List<string[]>();
    }

    static List<string[]> Read(Kpf kpf, string path, int depth)
    {
        var o = new List<string[]>();
        var bytes = kpf.Read(path);
        var text = new UTF8Encoding(false, false).GetString(bytes);
        foreach (var line in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            var words = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0 || words[0].StartsWith("//"))
                continue;
            if (words[0] == "#include" && words.Length > 1 && depth < 4)
            {
                string inc = words[1].Trim('"').ToLowerInvariant();
                if (kpf.Has(inc))
                    o.AddRange(Read(kpf, inc, depth + 1));
                continue;
            }
            o.Add(words);
        }
        return o;
    }
}
