using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Chosen.Sim;
using Godot;

namespace Chosen.Replay;

/// <summary>
/// Checks the C# game against a DarkPlaces bug recording, frame by frame. The whole world runs from the level start
/// with the recorded seed and settings; each frame the player is put in that frame's recorded state, the recorded
/// input runs one game frame, and the player is compared with the next frame's recorded state. Re-syncing the player
/// every frame keeps small differences from piling up (each frame is a test of its own) while the world reacts to
/// the player as it did: doors open when switches are used, sectors trigger when entered.
/// </summary>
public sealed class StrictReplay
{
    readonly Recording _rec;
    readonly World _world;
    readonly Player _p;
    readonly Dictionary<string, int> _idx = new();
    int _cmd;

    public int Compared, Matched;
    public readonly HashSet<int> DumpFrames = new();   // write the world's sprites before these frames (compare with dp_dump.py)
    public string DumpPattern;             // the file for a frame: {0} is the frame number
    public bool TraceRng;                  // print every frame's draws and their callers up to the last dump frame
    public HashSet<int> Probe = new();     // frames to explain: the bodies near the player
    public readonly Dictionary<string, int> FieldMismatches = new();
    public readonly List<string> FirstDiffs = new();

    // per field: absolute tolerance (fields not listed compare exactly)
    static readonly Dictionary<string, double> Tolerance = new()
    {
        ["origin"] = 0.1, ["xvel"] = 2048, ["yvel"] = 2048, ["zvel"] = 2048, ["zView"] = 64, ["zViewVel"] = 512,
        ["zWeapon"] = 64, ["zWeaponVel"] = 512, ["xheight"] = 1, ["ang"] = 1, ["look"] = 1, ["bubbleTime"] = 4,
        ["linkPrevZ"] = 0.1, ["xhealth"] = 1,
    };
    // fields the C# game doesn't simulate yet (not compared)
    static readonly HashSet<string> NotYet = new();

    /// <summary>world: loaded with the recording's map, seed and settings; the player is added here.</summary>
    public StrictReplay(Recording rec, World world)
    {
        _rec = rec;
        _world = world;
        _p = world.AddPlayer();
        World.ApplyCarry(_p, BugRecorder.CarryFromJson(rec.Carry));
        var all = new List<string>(rec.InputFields);
        all.AddRange(rec.StateFields);
        for (int i = 0; i < all.Count; i++)
            _idx[all[i]] = i;
    }

    /// <summary>
    /// What the C# world doesn't simulate yet (monsters walking off or being killed, things blown up) can still stand
    /// where DarkPlaces had none. A blocking sprite the recorded player passes through was gone in DarkPlaces by
    /// then: it is removed from the check at the first frame the recorded player overlaps it.
    /// </summary>
    readonly SortedDictionary<int, List<Sprite>> _dropAt = new();

    List<string> FindSpritesThePlayerPasses()
    {
        var dropped = new List<string>();
        int fo = _idx.TryGetValue("origin", out int oi) ? oi : -1;
        if (fo < 0)
            return dropped;
        double r = _p.ClipDist / 4.0 - 2;
        var origins = new List<Vector3>();
        foreach (var f in _rec.Frames)
            origins.Add(fo < f.Values.Length ? Recording.Vec(f.Values[fo]) : new Vector3(float.NaN, 0, 0));
        foreach (var s in _world.Sprites.ToArray())
        {
            if (s == _p || !s.SolidBlock || s.BoxYaw != 0f)
                continue;
            var lo = s.Origin + s.Mins;
            var hi = s.Origin + s.Maxs;
            for (int i = 0; i < origins.Count; i++)
            {
                var o = origins[i];
                double bz = -o.Z * Blood.ZScale;
                var (top, bottom) = Physics.SpriteExtents(bz, _p.Picnum, _p.YRepeat, _p.Cstat);
                double pzLo = -bottom / Blood.ZScale + 2, pzHi = -top / Blood.ZScale - 2;
                if (o.X + r > lo.X && o.X - r < hi.X && o.Y + r > lo.Y && o.Y - r < hi.Y && pzHi > lo.Z && pzLo < hi.Z)
                {
                    int at = Math.Max(i - 1, 0);
                    if (!_dropAt.TryGetValue(at, out var l))
                        _dropAt[at] = l = new List<Sprite>();
                    l.Add(s);
                    dropped.Add($"{s.ClassName} #{s.Index} (frame {_rec.Frames[at].Number})");
                    break;
                }
            }
        }
        return dropped;
    }

    string V(string[] vals, string field) => _idx.TryGetValue(field, out int i) && i < vals.Length ? vals[i] : null;
    static double D(string s) => s == null ? 0 : double.Parse(s, CultureInfo.InvariantCulture);

    void PutState(string[] v)
    {
        var p = _p;
        if (V(v, "origin") is string o) p.Origin = Recording.Vec(o);
        p.XVel = D(V(v, "xvel")); p.YVel = D(V(v, "yvel")); p.ZVel = D(V(v, "zvel"));
        p.HitTag = (int)D(V(v, "hitag"));
        p.Posture = (int)D(V(v, "posture"));
        p.ZView = D(V(v, "zView")); p.ZViewVel = D(V(v, "zViewVel"));
        p.ZWeapon = D(V(v, "zWeapon")); p.ZWeaponVel = D(V(v, "zWeaponVel"));
        if (V(v, "linkPrevZ") != null) p.LinkPrevZ = D(V(v, "linkPrevZ"));
        if (V(v, "linkPrevSet") != null) p.LinkPrevSet = D(V(v, "linkPrevSet")) != 0;
        p.Medium = (int)D(V(v, "medium"));
        p.XHeight = D(V(v, "xheight"));
        p.CantJump = D(V(v, "cantJump")) != 0;
        p.FallScream = D(V(v, "fallScream")) != 0;
        p.Spin = D(V(v, "spin"));
        p.Ang = (int)D(V(v, "ang"));
        p.Look = D(V(v, "look"));
        p.IsRunning = D(V(v, "isRunning")) != 0;
        p.BubbleTime = D(V(v, "bubbleTime"));
        p.XHealth = D(V(v, "xhealth"));
    }

    PlayerInput Input(string[] v)
    {
        var m = V(v, "movement") is string ms ? Recording.Vec(ms) : Vector3.Zero;
        var a = V(v, "v_angle") is string ang ? Recording.Vec(ang) : Vector3.Zero;
        return new PlayerInput
        {
            Forward = m.X, Strafe = -m.Y, Up = m.Z,
            Fire = D(V(v, "button0")) != 0, Jump = D(V(v, "button2")) != 0, AltFire = D(V(v, "button3")) != 0,
            Crouch = D(V(v, "button4")) != 0, Use = D(V(v, "button5")) != 0, Impulse = (int)D(V(v, "impulse")),
            Pitch = a.X, Yaw = a.Y,
        };
    }

    double Got(string field, int comp) => Got(_p, field, comp);

    /// <summary>A recorded state field of a player (comp: the component of a vector); NaN for an unknown field.</summary>
    public static double Got(Player p, string field, int comp)
    {
        return field switch
        {
            "origin" => comp == 0 ? p.Origin.X : comp == 1 ? p.Origin.Y : p.Origin.Z,
            "xvel" => p.XVel, "yvel" => p.YVel, "zvel" => p.ZVel, "hitag" => p.HitTag, "posture" => p.Posture,
            "zView" => p.ZView, "zViewVel" => p.ZViewVel, "zWeapon" => p.ZWeapon, "zWeaponVel" => p.ZWeaponVel,
            "linkPrevZ" => p.LinkPrevZ, "linkPrevSet" => p.LinkPrevSet ? 1 : 0, "medium" => p.Medium,
            "xheight" => p.XHeight, "cantJump" => p.CantJump ? 1 : 0, "fallScream" => p.FallScream ? 1 : 0,
            "spin" => p.Spin, "ang" => p.Ang, "look" => p.Look, "isRunning" => p.IsRunning ? 1 : 0,
            "bubbleTime" => p.BubbleTime, "xhealth" => p.XHealth,
            _ => double.NaN,
        };
    }

    /// <summary>Runs every frame; returns a report.</summary>
    public string Run(int maxDiffs = 12)
    {
        var dropped = FindSpritesThePlayerPasses();
        var frames = _rec.Frames;
        for (int i = 0; i + 1 < frames.Count; i++)
        {
            var cur = frames[i].Values;
            var next = frames[i + 1].Values;
            // the recorded console commands (cheats) run before their frame
            while (_cmd < _rec.Commands.Count && _rec.Commands[_cmd].Frame <= frames[i].Number)
            {
                var c = _rec.Commands[_cmd++].Command.Split(' ', 2);
                _world.CheatCommand(_p, c[0], c.Length > 1 ? c[1] : "");
            }
            if (DumpFrames.Contains(frames[i].Number) && DumpPattern != null)
                DumpSprites(string.Format(DumpPattern, frames[i].Number), frames[i].Number);
            bool trace = TraceRng && DumpFrames.Count > 0 && frames[i].Number < DumpFrames.Max();
            if (trace)
                Godot.GD.Print($"frame {frames[i].Number}: {_world.RandCalls} draws, seed {_world.SeedState}");
            _world.RandTrace = trace ? new StringBuilder() : null;
            if (_dropAt.TryGetValue(i, out var drop))
                foreach (var s in drop)
                    _world.Delete(s);
            PutState(cur);
            bool probe = Probe.Contains(frames[i].Number);
            _world.Log = probe ? new StringBuilder() : null;
            _p.Cmd = Input(cur);
            _world.Tick();
            if (_world.RandTrace != null && _world.RandTrace.Length > 0)
                Godot.GD.Print($"draws in frame {frames[i].Number}:\n{_world.RandTrace}");
            if (frames[i + 1].Number != frames[i].Number + 1 || D(V(cur, "xhealth")) <= 0)
                continue;          // a gap in the recording; death isn't compared yet
            Compared++;
            bool ok = true;
            var sb = new StringBuilder();
            foreach (var f in _rec.StateFields)
            {
                if (NotYet.Contains(f))
                    continue;
                var want = V(next, f);
                if (want == null)
                    continue;
                double tol = Tolerance.TryGetValue(f, out var t) ? t : 0.0001;
                if (f == "origin")
                {
                    var w = Recording.Vec(want);
                    for (int c = 0; c < 3; c++)
                    {
                        double g = Got(f, c), wv = c == 0 ? w.X : c == 1 ? w.Y : w.Z;
                        if (Math.Abs(g - wv) > tol)
                        {
                            ok = false;
                            Count(f);
                            sb.Append(string.Format(CultureInfo.InvariantCulture, " origin{0} {1:0.###} (want {2:0.###})", "xyz"[c], g, wv));
                        }
                    }
                    continue;
                }
                double gv = Got(f, 0);
                if (double.IsNaN(gv))
                    continue;
                double wd = D(want);
                if (f == "ang")
                {
                    double da = Math.Abs(Blood.AngMask(gv - wd + 1024) - 1024);
                    if (da <= tol) continue;
                }
                else if (Math.Abs(gv - wd) <= tol)
                    continue;
                ok = false;
                Count(f);
                sb.Append(string.Format(CultureInfo.InvariantCulture, " {0} {1:0.###} (want {2})", f, gv, want));
            }
            if (probe)
            {
                var o = Recording.Vec(V(cur, "origin"));
                var pr = new StringBuilder($"probe frame {frames[i].Number} at {o}:{_world.Log} floor {_p.FlorHitEnt}");
                foreach (var s in _world.Sectors)
                    if (s.BusyActive || (s.IsMover && s.XBusy != 0))
                        pr.Append($"\n    sector {s} type {s.Type} busy {s.XBusy} active {s.BusyActive} delta {s.BusyDelta} yaw {s.Yaw:0.##} origin {s.Origin}");
                foreach (var s in _world.Sprites)
                {
                    var d = s.Origin - o;
                    if (s != _p && (s.StatNum == Data.K.kStatDude || s.Type == Data.K.kThingBloodChunks) && new Vector2(d.X, d.Y).Length() < 1024)
                        pr.Append($"\n    DUDE {s.ClassName} #{s.Index} type {s.Type} stat {s.StatNum} health {s.XHealth} state {s.AiState} target {s.XTarget} at {s.Origin} cstat {s.Cstat} solid {s.Solid} pic {s.Picnum} seq {s.SeqId}");
                    else if (s != _p && new Vector2(d.X, d.Y).Length() < 64)
                        pr.Append($"\n    {s.ClassName} #{s.Index} type {s.Type} stat {s.StatNum} pic {s.Picnum} cstat {s.Cstat} at {s.Origin} box {s.Mins}..{s.Maxs} yaw {s.BoxYaw} solid {s.Solid} contents {(s.Body != null ? s.Body.Contents : 0)}");
                }
                FirstDiffs.Insert(0, pr.ToString());
            }
            if (ok)
                Matched++;
            else if (FirstDiffs.Count < maxDiffs)
                FirstDiffs.Add($"frame {frames[i].Number}:{sb}");
        }
        var r = new StringBuilder();
        r.Append(string.Format(CultureInfo.InvariantCulture, "check: {0} of {1} frames match ({2:0.#}%)\n", Matched, Compared,
            Compared > 0 ? 100.0 * Matched / Compared : 0));
        if (dropped.Count > 0)
            r.Append("  sprites left out (the recorded player passes through them): ").Append(string.Join(", ", dropped)).Append('\n');
        if (FieldMismatches.Count > 0)
        {
            r.Append("  mismatches per field:");
            foreach (var kv in FieldMismatches)
                r.Append($" {kv.Key} {kv.Value}");
            r.Append('\n');
        }
        foreach (var d in FirstDiffs)
            r.Append("  ").Append(d).Append('\n');
        return r.ToString();
    }

    /// <summary>The world's sprites in dp_dump.py's format: "sprindex classname stype statnum x,y,z solid cstat picnum
    /// xhealth" (solid: 0 none, 2 trigger-like corpse, 3 slidebox).</summary>
    void DumpSprites(string path, int frame)
    {
        var sb = new StringBuilder($"frame {frame}\nseed {_world.SeedState} draws {_world.RandCalls}\n");
        foreach (var s in _world.Sprites)
        {
            if (s.Removed)
                continue;
            int solid = !s.Solid ? 0 : s.SolidBlock ? 3 : 2;
            sb.Append(string.Format(CultureInfo.InvariantCulture, "{0} {1} {2} {3} {4:0.###},{5:0.###},{6:0.###} {7} {8} {9} {10}\n",
                s.SprIndex, s.ClassName, s.Type, s.StatNum, s.Origin.X, s.Origin.Y, s.Origin.Z, solid, s.Cstat, s.Picnum, s.XHealth, s.Edict));
        }
        System.IO.File.WriteAllText(path, sb.ToString());
    }

    void Count(string f) => FieldMismatches[f] = FieldMismatches.TryGetValue(f, out var n) ? n + 1 : 1;
}
