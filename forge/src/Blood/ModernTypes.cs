// Chosen Blood module (GPL v2): what Forge knows of NoOne's modern features (NBlood's nnexts.cpp, xmapedit): which
// maps ask for them and the channels a modern TX switch sends on.
using System.Collections.Generic;
using Chosen.Shared.Blood.Map;

namespace Chosen.Forge.Blood;

/// <summary>
/// NoOne's modern features as far as the editor needs them: a map is modern by its version (7.1, 7.2: xmapedit's) or
/// by an object sending command 100 on channel 60 or 61 to itself; a random or a sequential TX switch (types 25, 26)
/// sends on the channels in its data fields (data1 and data4 alone: every channel from the one to the other), beside
/// its own TX id.
/// </summary>
public static class ModernTypes
{
    public const int CustomDudeSpawn = 24, RandomTx = 25, SequentialTx = 26, CustomDude = 254, Condition = 501, ConditionFalse = 502;
    /// <summary>The engine's sloped floor sprite: both alignment bits; its slope in its offsets (x the low byte).</summary>
    public const int CstatSlope = 48;

    /// <summary>The map's modern revision (0: a plain Blood map).</summary>
    public static int Revision(BloodMapFile m)
    {
        if (m == null)
            return 0;
        if (m.V7 && (m.Version & 0xff) is 1 or 2)
            return m.Version & 0xff;
        foreach (var s in m.Sprites)
            if (s.Xsp is { } x && x.RxID == x.TxID && x.Command == 100 && x.TxID is 60 or 61)
                return x.TxID == 60 ? 1 : 2;
        return 0;
    }

    public static bool IsModern(BloodMapFile m) => Revision(m) != 0;

    /// <summary>The channels a sprite sends on beside its TX id: a random or sequential TX switch's data fields.</summary>
    public static IEnumerable<int> DataChannels(BSprite s)
    {
        if (s.Xsp is not { } x || (s.Type != RandomTx && s.Type != SequentialTx))
            yield break;
        if (x.Data1 > 0 && x.Data2 <= 0 && x.Data3 <= 0 && x.Data4 > 0)
        {
            // a range (NBlood's txIsRanged)
            int lo = System.Math.Min(x.Data1, x.Data4), hi = System.Math.Max(x.Data1, x.Data4);
            for (int c = lo; c <= hi && c - lo < 1024; c++)
                yield return c;
            yield break;
        }
        if (x.Data1 > 0) yield return x.Data1;
        if (x.Data2 > 0) yield return x.Data2;
        if (x.Data3 > 0) yield return x.Data3;
        if (x.Data4 > 0) yield return x.Data4;
    }

    static readonly Dictionary<(string, int), string> _cdudeNames = new();

    /// <summary>A custom dude's name as its descriptor gives it (the open map's mod folder: CDUDnnnn.CDU, [General]
    /// Name), the file's name when it gives none; null when the folder has no such descriptor.</summary>
    public static string CustomDudeName(int id)
    {
        string folder = Data.ForgeData.ModFolder;
        if (folder == "" || id < 0 || id >= 10000)
            return null;
        if (_cdudeNames.TryGetValue((folder, id), out var known))
            return known;
        string name = null;
        try
        {
            string file = null;
            foreach (var f in System.IO.Directory.GetFiles(folder, $"cdud{id}*.cdu"))
            {
                string b = System.IO.Path.GetFileNameWithoutExtension(f).ToLowerInvariant();
                if (b == $"cdud{id}" || b.StartsWith($"cdud{id}_"))
                {
                    file = f;
                    break;
                }
            }
            if (file != null)
            {
                name = System.IO.Path.GetFileName(file);
                bool general = false;
                foreach (var raw in System.IO.File.ReadLines(file))
                {
                    string line = raw.Trim();
                    if (line.StartsWith('['))
                        general = line.StartsWith("[general]", System.StringComparison.OrdinalIgnoreCase);
                    else if (general && line.StartsWith("name", System.StringComparison.OrdinalIgnoreCase) && line.IndexOf('=') is int eq and > 0
                        && line[..eq].Trim().Equals("name", System.StringComparison.OrdinalIgnoreCase))
                    {
                        string v = line[(eq + 1)..].Trim();
                        if (v != "")
                            name = v;
                        break;
                    }
                }
            }
        }
        catch (System.Exception e) when (e is System.IO.IOException or System.UnauthorizedAccessException)
        {
        }
        _cdudeNames[(folder, id)] = name;
        return name;
    }

    /// <summary>A descriptor's dude's data2 in words (its status bits).</summary>
    public static string CustomDudeFlags(int v)
    {
        if (v == 0)
            return null;
        if (v >= 32)
            return "a version 1 dude's first sequence";
        var l = new List<string>();
        if ((v & 1) != 0) l.Add("awake");
        if ((v & 2) != 0) l.Add("crouching");
        if ((v & 4) != 0) l.Add("no last stand");
        if ((v & 8) != 0) l.Add("cruel");
        if ((v & 16) != 0) l.Add("innocent");
        return string.Join(", ", l);
    }

    /// <summary>A sloped floor sprite's slope (4096: 45 degrees up the way it faces), 0 for any other sprite.</summary>
    public static int Slope(BSprite s) =>
        (s.Cstat & CstatSlope) == CstatSlope ? (short)((s.XOffset & 255) | ((s.YOffset & 255) << 8)) : 0;
}
