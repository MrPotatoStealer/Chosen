using System.Collections.Generic;
using Chosen.Core;
using Godot;

namespace Chosen.Render;

/// <summary>
/// The graphics API (Options > Video > Renderer): Direct3D 12, which has HDR output on Windows, or Vulkan, which in Godot has
/// none there but can run faster. Godot picks the driver before any of the game runs, from its project settings, so the choice
/// is a settings file of ours it reads over project.godot (application/config/project_settings_override), and takes effect at
/// the next start. Should Vulkan fail to start, Godot falls back to Direct3D 12 by itself. --rendering-driver overrides it all.
/// </summary>
public static class RenderDriver
{
    public static readonly Cvar Choice = Cvars.Register("r_driver", "0", "graphics API at the next start: 0 Direct3D 12 (has HDR), 1 Vulkan", saved: true);

    const string OverridePath = "user://driver.cfg";

    static string Wanted => Choice.Int == 1 ? "vulkan" : "d3d12";

    static string Name(string driver) => driver == "vulkan" ? "VULKAN" : "DIRECT3D 12";

    /// <summary>The driver this run is on differs from the one chosen.</summary>
    public static bool Pending => RenderingServer.GetCurrentRenderingDriverName() != Wanted;

    /// <summary>What the options page shows.</summary>
    public static string Status => Pending ? $"PRESS TO RESTART ON {Name(Wanted)}"
        : Wanted == "vulkan" ? "RUNNING ON VULKAN, NO HDR" : "RUNNING ON DIRECT3D 12";

    /// <summary>At startup, the settings loaded: the file follows the setting from here on.</summary>
    public static void Init()
    {
        Write();
        Choice.Changed += Write;
    }

    static void Write()
    {
        string text = $"[rendering]\n\nrendering_device/driver.windows=\"{Wanted}\"\n";
        if (FileAccess.FileExists(OverridePath) && FileAccess.GetFileAsString(OverridePath) == text)
            return;
        using var f = FileAccess.Open(OverridePath, FileAccess.ModeFlags.Write);
        f?.StoreString(text);
    }

    /// <summary>Starts the game again as it was started, on the chosen driver (nothing to do if already on it).</summary>
    public static void Restart()
    {
        if (!Pending)
            return;
        Cvars.SaveConfigIfChanged();
        var args = new List<string>(OS.GetCmdlineArgs());
        var user = OS.GetCmdlineUserArgs();
        if (user.Length > 0)
        {
            args.Add("--");
            args.AddRange(user);
        }
        OS.SetRestartOnExit(true, args.ToArray());
        ((SceneTree)Engine.GetMainLoop()).Quit();
    }
}
