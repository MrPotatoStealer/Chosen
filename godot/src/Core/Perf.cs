using System.Diagnostics;
using System.Text;
using Godot;

namespace Chosen.Core;

/// <summary>
/// The hitch log (cvar r_hitchlog: milliseconds, 0 off): a frame that takes longer prints where its time went, by the
/// sections Game marks, and what was loaded from disk during it (first uses of textures, sounds, voxel models).
/// </summary>
public static class Perf
{
    static readonly Cvar HitchLog = Cvars.Register("r_hitchlog", "0", "print frames slower than this many milliseconds and where the time went (0: off)");

    public static int TextureLoads, SoundLoads, VoxelLoads;

    static readonly Stopwatch _frame = new(), _section = new();
    static readonly StringBuilder _parts = new();
    static int _tex, _snd, _vox, _gc0, _gc1, _gc2;
    static ulong _lastTicks;

    public static bool On => HitchLog.Float > 0;

    static readonly System.Collections.Generic.Dictionary<string, (long ticks, int calls)> _acc = new();

    /// <summary>A start time for <see cref="Add"/> (0 while the hitch log is off).</summary>
    public static long Start() => On ? Stopwatch.GetTimestamp() : 0;

    /// <summary>Adds the time since start to a named counter, printed with the frame's hitch line (time and calls).</summary>
    public static void Add(string name, long start)
    {
        if (start == 0)
            return;
        _acc.TryGetValue(name, out var a);
        _acc[name] = (a.ticks + Stopwatch.GetTimestamp() - start, a.calls + 1);
    }

    /// <summary>Registers the cvar (at startup, before --exec).</summary>
    public static void Init() { }

    static Perf() { }

    public static void BeginFrame()
    {
        if (!On)
            return;
        _parts.Clear();
        _tex = TextureLoads;
        _snd = SoundLoads;
        _vox = VoxelLoads;
        _gc0 = System.GC.CollectionCount(0);
        _gc1 = System.GC.CollectionCount(1);
        _gc2 = System.GC.CollectionCount(2);
        _acc.Clear();
        _frame.Restart();
        _section.Restart();
    }

    /// <summary>Ends a section of the frame.</summary>
    public static void Mark(string name)
    {
        if (!On)
            return;
        _parts.Append(' ').Append(name).Append(' ').Append(_section.Elapsed.TotalMilliseconds.ToString("0.0")).Append(';');
        _section.Restart();
    }

    /// <summary>delta: the engine's time since the last frame (catches time spent outside _Process: rendering, GC).</summary>
    public static void EndFrame(double delta, int gameFrames)
    {
        if (!On)
            return;
        double ms = _frame.Elapsed.TotalMilliseconds;
        if (ms < HitchLog.Float && delta * 1000 < HitchLog.Float)
            return;
        ulong now = Time.GetTicksMsec();
        GD.Print($"hitch at {now / 1000.0:0.00}s: process {ms:0.0} ms, frame delta {delta * 1000:0.0} ms, {gameFrames} game frames;{_parts}" +
            $" loads: {TextureLoads - _tex} textures, {SoundLoads - _snd} sounds, {VoxelLoads - _vox} voxels;" +
            $" GC {System.GC.CollectionCount(0) - _gc0}/{System.GC.CollectionCount(1) - _gc1}/{System.GC.CollectionCount(2) - _gc2};" +
            $" {RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalDrawCallsInFrame)} draw calls;" + Counters());
        _lastTicks = now;
    }

    static string Counters()
    {
        var sb = new StringBuilder();
        foreach (var (k, v) in _acc)
            sb.Append(' ').Append(k).Append(' ').Append((v.ticks * 1000.0 / Stopwatch.Frequency).ToString("0.00")).Append(" ms/").Append(v.calls).Append(';');
        return sb.ToString();
    }
}
