using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Godot;

namespace Chosen.Replay;

/// <summary>
/// A bug report's input recording from the DarkPlaces version (bugs/&lt;id&gt;/recording.rec): every game frame's input
/// and the player's state as the frame began. Header lines name the fields ("input ...", "state ..."), so older
/// recordings with other fields still read. These recordings are the reference the Godot port is checked against.
/// </summary>
public sealed class Recording
{
    public sealed class Frame
    {
        public int Number;
        public string[] Values;          // input fields, then state fields
    }

    public string Map = "";
    public string Start = "map";         // "map" or "load <save>"
    public string Engine = "darkplaces"; // "godot": made by the Godot game (BugRecorder)
    public string Carry;                 // Godot recordings: the inventory the level began with (World.PlayerCarry as JSON)
    public int Seed;
    public readonly Dictionary<string, string> Cvars = new();
    public string[] InputFields = System.Array.Empty<string>();
    public string[] StateFields = System.Array.Empty<string>();
    public readonly List<Frame> Frames = new();
    public readonly List<(int Frame, string Command)> Commands = new();

    public static Recording Load(string path)
    {
        var r = new Recording();
        foreach (var line in File.ReadLines(path))
        {
            if (line.StartsWith("f|") || line.StartsWith("c|"))
            {
                var parts = line.Split('|');
                int frame = int.Parse(parts[1], CultureInfo.InvariantCulture);
                if (line[0] == 'c')
                    r.Commands.Add((frame, line.Substring(parts[0].Length + parts[1].Length + 2)));
                else
                    r.Frames.Add(new Frame { Number = frame, Values = parts[2..] });
                continue;
            }
            int sp = line.IndexOf(' ');
            string key = sp < 0 ? line : line[..sp], rest = sp < 0 ? "" : line[(sp + 1)..];
            switch (key)
            {
                case "map": r.Map = rest; break;
                case "start": r.Start = rest; break;
                case "engine": r.Engine = rest; break;
                case "carry": r.Carry = rest; break;
                case "seed": int.TryParse(rest, out r.Seed); break;
                case "input": r.InputFields = rest.Split(' ', System.StringSplitOptions.RemoveEmptyEntries); break;
                case "state": r.StateFields = rest.Split(' ', System.StringSplitOptions.RemoveEmptyEntries); break;
                case "cvar":
                    int s2 = rest.IndexOf(' ');
                    if (s2 > 0) r.Cvars[rest[..s2]] = rest[(s2 + 1)..];
                    else r.Cvars[rest] = "";
                    break;
            }
        }
        return r;
    }

    public int FieldIndex(string name)
    {
        int i = System.Array.IndexOf(InputFields, name);
        if (i >= 0) return i;
        i = System.Array.IndexOf(StateFields, name);
        return i >= 0 ? InputFields.Length + i : -1;
    }

    public static float Float(string s) => float.Parse(s, CultureInfo.InvariantCulture);

    /// <summary>A recorded vector; a zero vector is stored as a single "0".</summary>
    public static Vector3 Vec(string s)
    {
        var p = s.Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
        if (p.Length < 3)
        {
            float v = p.Length > 0 ? Float(p[0]) : 0;
            return new Vector3(v, v, v);
        }
        return new Vector3(Float(p[0]), Float(p[1]), Float(p[2]));
    }
}
