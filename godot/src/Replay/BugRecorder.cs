using System.Globalization;
using System.IO;
using System.Text;
using Chosen.Sim;
using Godot;

namespace Chosen.Replay;

/// <summary>
/// Records the level as it is played, from its start, in the DarkPlaces recordings' format (qc/server/bugrec.qc), so
/// a bug report made in the Godot game carries a recording.rec that --play, check and inputcheck read like the
/// DarkPlaces ones: every game frame's input and the player's state as the frame began, and the console commands
/// (cheats) before the frame they ran in.
/// </summary>
public sealed class BugRecorder
{
    static readonly string[] InputFields = { "movement", "button0", "button2", "button3", "button4", "button5", "impulse", "v_angle" };
    static readonly string[] StateFields =
    {
        "origin", "xvel", "yvel", "zvel", "hitag", "posture", "zView", "zViewVel", "zWeapon", "zWeaponVel", "linkPrevZ",
        "linkPrevSet", "medium", "xheight", "cantJump", "fallScream", "spin", "ang", "look", "isRunning", "bubbleTime", "xhealth",
    };

    readonly StringBuilder _header = new();
    readonly StringBuilder _body = new();
    int _frame;

    public int Frame => _frame;

    /// <summary>carry: the inventory the level began with (World.PlayerCarry as JSON), or null.</summary>
    public BugRecorder(string map, long seed, World w, string carry)
    {
        _header.Append("map ").Append(map).Append('\n');
        _header.Append("engine godot\n");
        _header.Append("start map\n");
        _header.Append("seed ").Append(seed.ToString(CultureInfo.InvariantCulture)).Append('\n');
        _header.Append("carried ").Append(carry != null ? 1 : 0).Append('\n');
        if (carry != null)
            _header.Append("carry ").Append(carry).Append('\n');
        _header.Append("cvar b3d_skill ").Append(w.Skill).Append('\n');
        _header.Append("cvar b3d_monsters ").Append(w.Monsters ? 1 : 0).Append('\n');
        _header.Append("cvar cl_autoaim ").Append(w.AutoAim).Append('\n');
        _header.Append("cvar b3d_weaponsv10x ").Append(w.WeaponsV10x ? 1 : 0).Append('\n');
        _header.Append("cvar b3d_fixblood ").Append(w.FixBlood ? 1 : 0).Append('\n');
        _header.Append("input ").Append(string.Join(' ', InputFields)).Append('\n');
        _header.Append("state ").Append(string.Join(' ', StateFields)).Append('\n');
    }

    static string N(double v) => v.ToString("R", CultureInfo.InvariantCulture);

    /// <summary>A vector as the recordings write it: a zero vector is a single "0".</summary>
    static string V(double x, double y, double z) => x == 0 && y == 0 && z == 0 ? "0" : $"{N(x)} {N(y)} {N(z)}";

    /// <summary>A console command, run before the next game frame.</summary>
    public void Command(string cmd) => _body.Append("c|").Append(_frame).Append('|').Append(cmd).Append('\n');

    /// <summary>Before a game frame: the player's input (p.Cmd) and state.</summary>
    public void Record(Player p)
    {
        var c = p.Cmd;
        var sb = _body;
        sb.Append("f|").Append(_frame);
        sb.Append('|').Append(V(c.Forward, -c.Strafe, c.Up));
        sb.Append('|').Append(c.Fire ? 1 : 0).Append('|').Append(c.Jump ? 1 : 0).Append('|').Append(c.AltFire ? 1 : 0);
        sb.Append('|').Append(c.Crouch ? 1 : 0).Append('|').Append(c.Use ? 1 : 0).Append('|').Append(c.Impulse);
        sb.Append('|').Append(V(c.Pitch, c.Yaw, 0));
        foreach (var f in StateFields)
        {
            sb.Append('|');
            if (f == "origin")
                sb.Append(V(p.Origin.X, p.Origin.Y, p.Origin.Z));
            else
                sb.Append(N(StrictReplay.Got(p, f, 0)));
        }
        sb.Append('\n');
        _frame++;
    }

    static readonly System.Text.Json.JsonSerializerOptions Json = new() { IncludeFields = true };

    public static string CarryToJson(World.PlayerCarry c) => c == null ? null : System.Text.Json.JsonSerializer.Serialize(c, Json);

    public static World.PlayerCarry CarryFromJson(string s) =>
        string.IsNullOrEmpty(s) ? null : System.Text.Json.JsonSerializer.Deserialize<World.PlayerCarry>(s, Json);

    public void Save(string path, string id)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        using var w = new StreamWriter(path, false, new UTF8Encoding(false));
        w.Write($"# Chosen input recording (Godot): play {id}\n");
        w.Write(_header);
        w.Write(_body);
    }
}
