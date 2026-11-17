using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace Chosen.Data;

/// <summary>
/// Blood's level info (blood.ini, copied into the data folder by pipeline/export.py): the episodes and their maps,
/// each map's CD track (Refreshed Supply's sound/cdtracks/trackNNN.ogg), and the menu's music.
/// </summary>
public static class LevelInfo
{
    public sealed class Episode
    {
        public string Title = "";
        public bool BloodBathOnly;
        public readonly List<string> Maps = new();
        // the cutscenes (CutSceneA on entering level CutALevel, CutSceneB after the last; NBlood's levelPlayIntroScene /
        // levelPlayEndScene): the video's name without folder or extension (cs1), and its WAV's
        public string CutA = "", CutWavA = "", CutB = "", CutWavB = "";
        public int CutALevel;          // 0-based
    }

    public static readonly List<Episode> Episodes = new();
    static readonly Dictionary<string, Dictionary<string, string>> _sections = new();
    static bool _loaded;

    // the English titles of Refreshed Supply's string codes
    static readonly Dictionary<string, string> Titles = new()
    {
        ["$ble1"] = "THE WAY OF ALL FLESH", ["$ble2"] = "EVEN DEATH MAY DIE", ["$ble3"] = "FAREWELL TO ARMS",
        ["$ble4"] = "DEAD OF WINTER", ["$ble5"] = "BLOODBATH", ["$ble6"] = "POST MORTEM",
    };

    /// <summary>Read again (an add-on activated or left: its episode file, data/blood.ini, in place of Blood's).</summary>
    public static void Reload()
    {
        _loaded = false;
        Episodes.Clear();
        _sections.Clear();
    }

    static Dictionary<string, string> _strings;

    /// <summary>A title by its code ($ble1, $dw_ep1, $cp01): Refreshed Supply's English strings (data/loc_english.txt,
    /// pipeline/export.py export_strings), else the built-in table, else the code itself.</summary>
    public static string Title(string code)
    {
        if (_strings == null)
        {
            _strings = new Dictionary<string, string>();
            var path = Path.Combine(BloodData.Folder, "data", "loc_english.txt");
            if (File.Exists(path))
                foreach (var raw in File.ReadLines(path))
                {
                    int eq = raw.IndexOf('=');
                    if (!raw.StartsWith('$') || eq < 0)
                        continue;
                    string v = raw[(eq + 1)..].Trim();
                    if (v.Length >= 2 && v[0] == '"' && v[^1] == '"')
                        _strings[raw[..eq].Trim().ToLowerInvariant()] = v[1..^1];
                }
        }
        string key = code.Trim().ToLowerInvariant();
        if (_strings.TryGetValue(key, out var s))
            return s;
        return Titles.TryGetValue(key, out var en) ? en : code.TrimStart('$');
    }

    static void Load()
    {
        if (_loaded)
            return;
        _loaded = true;
        var path = BloodData.DataFile("blood.ini");
        if (!File.Exists(path))
            return;
        Dictionary<string, string> cur = null;
        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            int sc = line.IndexOf(';');
            if (sc >= 0)
                line = line[..sc].Trim();
            if (line == "")
                continue;
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                cur = new Dictionary<string, string>();
                _sections[line[1..^1].ToLowerInvariant()] = cur;
                continue;
            }
            int eq = line.IndexOf('=');
            if (cur != null && eq > 0)
                cur[line[..eq].Trim().ToLowerInvariant()] = line[(eq + 1)..].Trim();
        }
        for (int e = 1; _sections.TryGetValue($"episode{e}", out var sec); e++)
        {
            var ep = new Episode
            {
                Title = sec.TryGetValue("title", out var t) ? Title(t).ToUpperInvariant() : $"EPISODE {e}",
                BloodBathOnly = sec.TryGetValue("bloodbathonly", out var bb) && bb != "0",
            };
            for (int m = 1; sec.TryGetValue($"map{m}", out var map); m++)
                ep.Maps.Add(map.ToLowerInvariant());
            static string Name(Dictionary<string, string> s, string key) =>
                s.TryGetValue(key, out var v) ? System.IO.Path.GetFileNameWithoutExtension(v.Replace('\\', '/')).ToLowerInvariant() : "";
            ep.CutA = Name(sec, "cutscenea");
            ep.CutWavA = Name(sec, "cutwava");
            ep.CutB = Name(sec, "cutsceneb");
            ep.CutWavB = Name(sec, "cutwavb");
            ep.CutALevel = sec.TryGetValue("cutscenealevel", out var cl) && int.TryParse(cl, out int ci) && ci > 0 ? ci - 1 : 0;
            Episodes.Add(ep);
        }
    }

    public static IReadOnlyList<Episode> AllEpisodes
    {
        get { Load(); return Episodes; }
    }

    static string Get(string section, string key)
    {
        Load();
        return _sections.TryGetValue(section.ToLowerInvariant(), out var s) && s.TryGetValue(key, out var v) ? v : null;
    }

    /// <summary>The episode holding a map and the map's level index in it (-1, -1: none).</summary>
    public static (int episode, int level) Find(string map)
    {
        Load();
        map = map.ToLowerInvariant();
        for (int e = 0; e < Episodes.Count; e++)
        {
            int l = Episodes[e].Maps.IndexOf(map);
            if (l >= 0)
                return (e, l);
        }
        return (-1, -1);
    }

    /// <summary>levelGetNextLevels / levelEndLevel: the map after this one (EndingA, or EndingB for a secret exit; 1-based
    /// levels of the episode), "" when the episode ends there, null when the map isn't in blood.ini.</summary>
    public static string NextMap(string map, bool secret)
    {
        var (e, _) = Find(map);
        if (e < 0)
            return null;
        if (!int.TryParse(Get(map, secret ? "endingb" : "endinga"), out int n) || n <= 0 || n > Episodes[e].Maps.Count)
            return "";
        return Episodes[e].Maps[n - 1];
    }

    /// <summary>The map that is level n (0-based) of the episode holding a map, or null.</summary>
    public static string MapOfEpisode(string map, int n)
    {
        var (e, _) = Find(map);
        return e >= 0 && n >= 0 && n < Episodes[e].Maps.Count ? Episodes[e].Maps[n] : null;
    }

    /// <summary>A map's level message n (blood.ini's MessageN: trTextOver), null if none.</summary>
    public static string Message(string map, int n) => Get(map, "message" + n);

    /// <summary>A map's MIDI song (blood.ini's Song: a SOUNDS.RFF MID resource, lower case; "": none).</summary>
    public static string Song(string map) => (Get(map, "song") ?? "").Trim().ToLowerInvariant();

    /// <summary>The menu's MIDI song.</summary>
    public static string MenuSong() => (Get("MenuSounds", "song") ?? "").Trim().ToLowerInvariant();

    /// <summary>A map's CD track (0: none).</summary>
    public static int Track(string map) => int.TryParse(Get(map, "track"), out int t) ? t : 0;

    /// <summary>The menu's CD track and where in it the music starts (seconds).</summary>
    public static (int track, float start) MenuTrack()
    {
        int.TryParse(Get("MenuSounds", "track"), out int t);
        float.TryParse(Get("MenuSounds", "trackstarttime") ?? "0", NumberStyles.Float, CultureInfo.InvariantCulture, out float s);
        return (t, s);
    }
}
