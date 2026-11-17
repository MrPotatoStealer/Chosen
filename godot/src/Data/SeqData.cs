using System.Collections.Generic;
using System.IO;

namespace Chosen.Data;

/// <summary>
/// Blood's SEQ animations (data/rt_seq.txt, gen_qcdata.py): "S id info first sound" with info = nframes +
/// ticksPerFrame*256 + flags*65536, and "F index a b c" per frame with a = tile + flags*8192, b = xrepeat +
/// yrepeat*256 + flips*65536, c = shade+128 + pal*256 + soundRange*8192 + surfaceSound*131072 + (pal >> 5)*262144 (the
/// last three NoOne's extensions: a mod's sequences).
/// </summary>
public static class SeqData
{
    public const int FlagLoop = 1, FlagRemove = 2;
    public const int FrameTrans = 1, FrameTrans2 = 2, FrameBlockable = 4, FrameHittable = 8, FrameTrigger = 16,
        FrameSmoke = 32, FrameAutoAim = 64, FramePushable = 128, FramePlaySound = 256, FrameInvisible = 512;

    static readonly Dictionary<int, (int info, int first, int sound)> _seqs = new();
    static readonly Dictionary<int, (int a, int b, int c)> _frames = new();

    /// <summary>Read again (an add-on activated or left: its animations added to the base game's).</summary>
    public static void Reload()
    {
        bool had = _seqs.Count > 0;
        _seqs.Clear();
        _frames.Clear();
        if (had)
            Load();
    }

    public static void Load()
    {
        if (_seqs.Count > 0)
            return;
        foreach (var line in File.ReadLines(BloodData.DataFile("rt_seq.txt")))
        {
            var p = line.Split(' ');
            if (p.Length < 5)
                continue;
            int i = int.Parse(p[1]);
            if (p[0] == "S")
                _seqs[i] = (int.Parse(p[2]), int.Parse(p[3]), int.Parse(p[4]));
            else
                _frames[i] = (int.Parse(p[2]), int.Parse(p[3]), int.Parse(p[4]));
        }
    }

    public static bool Exists(int id) => _seqs.ContainsKey(id);
    public static int FrameCount(int id) => _seqs.TryGetValue(id, out var s) ? s.info & 255 : 0;
    public static int TicksPerFrame(int id) => _seqs.TryGetValue(id, out var s) ? (s.info >> 8) & 255 : 0;
    public static int Flags(int id) => _seqs.TryGetValue(id, out var s) ? (s.info >> 16) & 255 : 0;
    public static int SoundId(int id) => _seqs.TryGetValue(id, out var s) ? s.sound : 0;

    static (int a, int b, int c) Frame(int id, int frame) =>
        _seqs.TryGetValue(id, out var s) && _frames.TryGetValue(s.first + frame, out var f) ? f : (0, 0, 128);

    /// <summary>A frame's sprite repeats (0: keep the sprite's).</summary>
    public static (int x, int y) FrameRepeat(int id, int frame)
    {
        var f = Frame(id, frame);
        return (f.b & 255, (f.b >> 8) & 255);
    }

    /// <summary>The tile a sequence's frame shows.</summary>
    public static int FrameTile(int id, int frame) => Frame(id, frame).a & 8191;
    public static int FrameFlags(int id, int frame) => (Frame(id, frame).a >> 13) & 1023;
    public static int FrameFlip(int id, int frame) => (Frame(id, frame).b >> 16) & 3;
    public static int FrameShade(int id, int frame) => (Frame(id, frame).c & 255) - 128;
    public static int FramePal(int id, int frame)
    {
        int c = Frame(id, frame).c;
        return ((c >> 8) & 31) | (((c >> 18) & 3) << 5);
    }

    /// <summary>The frame's sound is one of that many from the sequence's (0, 1: of two).</summary>
    public static int FrameSoundRange(int id, int frame) => (Frame(id, frame).c >> 13) & 15;
    /// <summary>The frame plays the step sound of the floor a moving sprite stands on.</summary>
    public static bool FrameSurfaceSound(int id, int frame) => ((Frame(id, frame).c >> 17) & 1) != 0;

    /// <summary>Has one of the sequence's frames the trigger flag (the callback's frame)?</summary>
    public static bool HasTrigger(int id)
    {
        int n = FrameCount(id);
        for (int i = 0; i < n; i++)
            if ((FrameFlags(id, i) & FrameTrigger) != 0)
                return true;
        return false;
    }

    /// <summary>The sequence's last frame given the trigger flag (a custom dude's attack animation without one: until
    /// the tables are read again).</summary>
    public static void SetLastFrameTrigger(int id)
    {
        if (!_seqs.TryGetValue(id, out var s) || (s.info & 255) == 0)
            return;
        int k = s.first + (s.info & 255) - 1;
        if (_frames.TryGetValue(k, out var f))
            _frames[k] = (f.a | (FrameTrigger << 13), f.b, f.c);
    }
}
