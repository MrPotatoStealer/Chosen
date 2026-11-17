using System.IO;
using Godot;

namespace Chosen.Core;

/// <summary>
/// The port was called Blood3D, and Godot keeps a project's settings in a folder named after it
/// (%APPDATA%/Godot/app_userdata/&lt;name&gt;). On the first start as Chosen, the settings are copied over from the old
/// folder: config.cfg (every saved cvar) and driver.cfg (the graphics API; Godot reads it before any of the game runs, so
/// a Vulkan choice takes effect from the second start).
/// </summary>
public static class UserDataMigration
{
    const string OldName = "Blood3D";
    static readonly string[] Files = { "config.cfg", "driver.cfg" };

    /// <summary>Before the config is loaded: copies the old folder's settings when this folder has none yet.</summary>
    public static void MigrateFromBlood3D()
    {
        string dir = OS.GetUserDataDir();
        if (File.Exists(Path.Combine(dir, "config.cfg")))
            return;
        string old = Path.Combine(Path.GetDirectoryName(dir.TrimEnd('/', '\\')) ?? "", OldName);
        if (!File.Exists(Path.Combine(old, "config.cfg")))
            return;
        Directory.CreateDirectory(dir);
        foreach (var f in Files)
        {
            string src = Path.Combine(old, f), dst = Path.Combine(dir, f);
            if (File.Exists(src) && !File.Exists(dst))
                File.Copy(src, dst);
        }
        GD.Print($"settings copied from {old} (the port's old name); a Vulkan choice applies from the next start");
    }
}
