using System;
using System.Globalization;
using Chosen.Sim;
using Godot;

namespace Chosen.Replay;

/// <summary>
/// Plays a DarkPlaces bug recording's inputs (and console commands) into the C# game: the world runs from the level
/// start on its own, only the input comes from the recording. Unlike StrictReplay nothing is re-synced, so it shows
/// how long the two games stay together, and you can watch the result.
/// </summary>
public sealed class InputReplay
{
    readonly Recording _rec;
    readonly int _move, _b0, _b2, _b3, _b4, _b5, _imp, _ang, _origin;
    int _index;
    int _cmd;
    public double MaxError, ErrorSum;
    public int FirstDivergence = -1;      // the first frame the player is more than 1 unit off
    public int TraceFrom = -1, TraceTo = -1;   // print the player against the recording for these frames
    public Action<string> OnCommand;           // each recorded command as it runs (a new recording takes it over)

    public InputReplay(Recording rec)
    {
        _rec = rec;
        _move = rec.FieldIndex("movement");
        _b0 = rec.FieldIndex("button0");
        _b2 = rec.FieldIndex("button2");
        _b3 = rec.FieldIndex("button3");
        _b4 = rec.FieldIndex("button4");
        _b5 = rec.FieldIndex("button5");
        _imp = rec.FieldIndex("impulse");
        _ang = rec.FieldIndex("v_angle");
        _origin = rec.FieldIndex("origin");
    }

    public bool Finished => _index >= _rec.Frames.Count;
    public int FrameNumber => _index < _rec.Frames.Count ? _rec.Frames[_index].Number : -1;
    public int Compared;

    static double D(string[] v, int i) => i >= 0 && i < v.Length ? double.Parse(v[i], CultureInfo.InvariantCulture) : 0;

    /// <summary>A frame's recorded input.</summary>
    public PlayerInput Input(int index)
    {
        var v = _rec.Frames[index].Values;
        var m = _move >= 0 ? Recording.Vec(v[_move]) : Vector3.Zero;
        var a = _ang >= 0 ? Recording.Vec(v[_ang]) : Vector3.Zero;
        return new PlayerInput
        {
            Forward = m.X, Strafe = -m.Y, Up = m.Z,
            Fire = D(v, _b0) != 0, Jump = D(v, _b2) != 0, AltFire = D(v, _b3) != 0,
            Crouch = D(v, _b4) != 0, Use = D(v, _b5) != 0, Impulse = (int)D(v, _imp),
            Pitch = a.X, Yaw = a.Y,
        };
    }

    /// <summary>Before the next game frame: run its recorded commands and set the player's input.</summary>
    public void Prepare(World world, Player p, Action<string> runCommand = null)
    {
        if (Finished)
            return;
        int frame = _rec.Frames[_index].Number;
        while (_cmd < _rec.Commands.Count && _rec.Commands[_cmd].Frame <= frame)
        {
            OnCommand?.Invoke(_rec.Commands[_cmd].Command);
            var c = _rec.Commands[_cmd++].Command.Split(' ', 2);
            if (!world.CheatCommand(p, c[0], c.Length > 1 ? c[1] : ""))
                runCommand?.Invoke(_rec.Commands[_cmd - 1].Command);
        }
        p.Cmd = Input(_index);
    }

    /// <summary>After the game frame: compare the player with the next recorded state, then move on.</summary>
    public void Finish(Player p)
    {
        _index++;
        if (!Finished && _origin < 0)
        {
            // a made-up recording (pipeline/walktest.py): where the player is, nothing to compare with
            int f = _rec.Frames[_index].Number;
            if (f >= TraceFrom && f <= TraceTo)
                GD.Print(string.Format(CultureInfo.InvariantCulture, "frame {0}: {1:0.###},{2:0.###},{3:0.###} vel {4},{5},{6} hit {7} floor {8} sect {9} medium {10} posture {11}",
                    f, p.Origin.X, p.Origin.Y, p.Origin.Z, p.XVel, p.YVel, p.ZVel, p.Hit, p.FlorHit, p.Sect, p.Medium, p.Posture));
        }
        if (Finished || _origin < 0)
            return;
        var want = Recording.Vec(_rec.Frames[_index].Values[_origin]);
        double err = (p.Origin - want).Length();
        int n = _rec.Frames[_index].Number;
        if (n >= TraceFrom && n <= TraceTo)
            GD.Print(string.Format(CultureInfo.InvariantCulture, "frame {0}: {1:0.###},{2:0.###},{3:0.###} want {4:0.###},{5:0.###},{6:0.###} (off {7:0.###}) vel {8},{9},{10} posture {11} medium {12} pic {13} floor {14} sect {15} on {16}",
                n, p.Origin.X, p.Origin.Y, p.Origin.Z, want.X, want.Y, want.Z, err, p.XVel, p.YVel, p.ZVel, p.Posture, p.Medium, p.Picnum, p.FlorHit,
                p.Sect, p.FlorHitEnt is Sim.Sector fs ? $"s{fs.B}" : p.FlorHitEnt is Sim.Sprite fsp ? "sprite" : "-"));
        Compared++;
        ErrorSum += err;
        MaxError = Math.Max(MaxError, err);
        if (FirstDivergence < 0 && err > 1)
            FirstDivergence = _rec.Frames[_index].Number;
    }

    public string Report() => string.Format(CultureInfo.InvariantCulture,
        "input replay: {0} frames, first off by more than 1 unit at frame {1}, max error {2:0.#}, mean {3:0.##}",
        Compared, FirstDivergence, MaxError, Compared > 0 ? ErrorSum / Compared : 0);
}
