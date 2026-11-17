// Chosen core: original code.
using System;
using Godot;

namespace Chosen.Forge.Editing;

/// <summary>Modifier keys the modes ask about while handling an event (Shift: no grid snap); a replayed session sets
/// them from its recorded events. And whether this run may take the user's mouse and keyboard.</summary>
public static class ForgeInput
{
    public static bool? ShiftOverride, AltOverride, CtrlOverride;
    public static bool Shift => ShiftOverride ?? Input.IsKeyPressed(Key.Shift);
    public static bool Alt => AltOverride ?? Input.IsKeyPressed(Key.Alt);
    public static bool Ctrl => CtrlOverride ?? Input.IsKeyPressed(Key.Ctrl);

    /// <summary>
    /// A test run (--noinput, a window opened off the desktop with --position, a --shot picture): it never takes the
    /// focus (the window opens without it: project.godot's no_focus) nor the mouse (the 3D preview's capture would
    /// clip and move the system's cursor even without focus), so whoever works while it runs keeps both.
    /// </summary>
    public static readonly bool NoInput = Array.IndexOf(OS.GetCmdlineArgs(), "--noinput") >= 0
        || Array.IndexOf(OS.GetCmdlineUserArgs(), "--noinput") >= 0 || Array.IndexOf(OS.GetCmdlineArgs(), "--position") >= 0
        || Array.Exists(OS.GetCmdlineUserArgs(), a => a.StartsWith("--shot="));

    /// <summary>At start: a test run stays without focus (and off the desktop with --position); the editor started
    /// to work in takes it.</summary>
    public static void TakeFocus()
    {
        if (DisplayServer.GetName() == "headless")
            return;
        if (NoInput)
        {
            DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.NoFocus, true);
            if (Array.IndexOf(OS.GetCmdlineArgs(), "--position") >= 0)
                DisplayServer.WindowSetPosition(new Vector2I(-30000, -30000));
            return;
        }
        DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.NoFocus, false);
        DisplayServer.WindowMoveToForeground();
    }

    /// <summary>The mouse captured (the 3D preview's look), unless this is a test run.</summary>
    public static void Capture()
    {
        if (!NoInput && DisplayServer.GetName() != "headless")
            Input.MouseMode = Input.MouseModeEnum.Captured;
    }
}
