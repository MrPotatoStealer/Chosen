using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace Chosen.Data;

/// <summary>
/// Blood's QAV animations (the weapons in hand). Timing for the game (data/rt_qav.txt, gen_qcdata.py): "Q id ticks
/// dur nframes first" and "F index cb snd" with cb = callback + priority*256 (-1: none). The drawn layers for the
/// client (data/qav.txt): "qav id name nframes ticksPerFrame duration x y", then per frame "frame callback sound
/// priority nlayers" and its layers "picnum x y z stat shade pal angle".
/// </summary>
public static class QavData
{
    public struct Layer
    {
        public int Picnum, X, Y, Z, Stat, Shade, Pal, Angle;
    }

    public sealed class Qav
    {
        public int Id, Ticks, Duration, First, X = 160, Y = 199;
        public string Name = "";
        public readonly List<Layer[]> Frames = new();
        public int FrameCount;
    }

    static readonly Dictionary<int, Qav> _qavs = new();
    static readonly Dictionary<int, (int cb, int snd)> _frames = new();

    /// <summary>Read again (an add-on activated or left).</summary>
    public static void Reload()
    {
        bool had = _qavs.Count > 0;
        _qavs.Clear();
        _frames.Clear();
        if (had)
            Load();
    }

    public static void Load()
    {
        if (_qavs.Count > 0)
            return;
        foreach (var line in File.ReadLines(BloodData.DataFile("rt_qav.txt")))
        {
            var p = line.Split(' ');
            if (p.Length < 4)
                continue;
            int i = int.Parse(p[1]);
            if (p[0] == "Q" && p.Length >= 6)
                _qavs[i] = new Qav { Id = i, Ticks = int.Parse(p[2]), Duration = int.Parse(p[3]), FrameCount = int.Parse(p[4]), First = int.Parse(p[5]) };
            else if (p[0] == "F")
                _frames[i] = (int.Parse(p[2]), int.Parse(p[3]));
        }
        var path = BloodData.DataFile("qav.txt");
        if (!File.Exists(path))
            return;
        Qav cur = null;
        List<Layer> layers = null;
        int left = 0;
        foreach (var line in File.ReadLines(path))
        {
            if (line.StartsWith("//") || line.Length == 0)
                continue;
            var p = line.Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
            if (p[0] == "qav")
            {
                int id = int.Parse(p[1]);
                if (!_qavs.TryGetValue(id, out cur))
                    _qavs[id] = cur = new Qav { Id = id, FrameCount = int.Parse(p[3]), Ticks = int.Parse(p[4]), Duration = int.Parse(p[5]) };
                cur.Name = p[2];
                cur.X = int.Parse(p[6]);
                cur.Y = int.Parse(p[7]);
                continue;
            }
            if (cur == null)
                continue;
            if (p[0] == "frame")
            {
                left = int.Parse(p[4]);
                layers = new List<Layer>(left);
                if (left == 0)
                    cur.Frames.Add(layers.ToArray());
                continue;
            }
            if (layers == null || left <= 0 || p.Length < 8)
                continue;
            layers.Add(new Layer
            {
                Picnum = I(p[0]), X = I(p[1]), Y = I(p[2]), Z = I(p[3]), Stat = I(p[4]), Shade = I(p[5]), Pal = I(p[6]), Angle = I(p[7]),
            });
            if (--left == 0)
                cur.Frames.Add(layers.ToArray());
        }
    }

    static int I(string s) => int.Parse(s, CultureInfo.InvariantCulture);

    public static Qav Get(int id) => _qavs.TryGetValue(id, out var q) ? q : null;
    public static int Ticks(int id) => _qavs.TryGetValue(id, out var q) ? q.Ticks : 0;
    public static int Duration(int id) => _qavs.TryGetValue(id, out var q) ? q.Duration : 0;
    public static int FrameCount(int id) => _qavs.TryGetValue(id, out var q) ? q.FrameCount : 0;
    public static int First(int id) => _qavs.TryGetValue(id, out var q) ? q.First : 0;
    public static int FrameCallback(int fi) => _frames.TryGetValue(fi, out var f) ? f.cb : -1;
    public static int FrameSound(int fi) => _frames.TryGetValue(fi, out var f) ? f.snd : 0;
}
