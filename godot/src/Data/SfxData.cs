using System.Collections.Generic;
using System.IO;

namespace Chosen.Data;

/// <summary>
/// Blood's sounds (data/rt_sfx.txt: "id file relvol pitch prange loop", like Blood3D's data.qc sfx_* buffers): the
/// file a sound id plays and how. Ids without a file play nothing (sfx_slot -1).
/// </summary>
public static class SfxData
{
    public readonly struct Sfx
    {
        public readonly string File;
        public readonly int RelVol, Pitch, PitchRange;
        public readonly bool Loop;

        public Sfx(string file, int relVol, int pitch, int pitchRange, bool loop)
        {
            File = file;
            RelVol = relVol;
            Pitch = pitch;
            PitchRange = pitchRange;
            Loop = loop;
        }
    }

    static Dictionary<int, Sfx> _sfx;

    /// <summary>Read again when next asked (an add-on activated or left: its sounds added to the base game's).</summary>
    public static void Reload() => _sfx = null;

    public static void Load()
    {
        if (_sfx != null)
            return;
        _sfx = new Dictionary<int, Sfx>();
        var path = BloodData.DataFile("rt_sfx.txt");
        if (!File.Exists(path))
            return;
        foreach (var line in File.ReadLines(path))
        {
            var p = line.Split(' ');
            if (p.Length < 6 || !int.TryParse(p[0], out int id) || p[1] == "")
                continue;
            _sfx[id] = new Sfx(p[1], int.Parse(p[2]), int.Parse(p[3]), int.Parse(p[4]), p[5] != "0");
        }
    }

    public static bool HasFile(int id)
    {
        Load();
        return _sfx.ContainsKey(id);
    }

    public static bool TryGet(int id, out Sfx sfx)
    {
        Load();
        return _sfx.TryGetValue(id, out sfx);
    }
}
