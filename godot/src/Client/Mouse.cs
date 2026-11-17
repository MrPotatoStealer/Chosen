using System;
using Godot;

namespace Chosen.Client;

/// <summary>
/// The mouse taken for play (captured: hidden, held in the window) or given back. A test run (--noinput, or a window
/// opened off the desktop with --position by the pipeline's launchers) never takes the system's cursor: Godot's capture
/// on Windows clips the cursor to the window and moves it there even without focus, which takes the mouse from whoever
/// is working while the test runs. The game still sees itself as captured then (play goes on as it would).
/// </summary>
public static class Mouse
{
    public static readonly bool NoInput = Array.IndexOf(OS.GetCmdlineArgs(), "--noinput") >= 0
        || Array.IndexOf(OS.GetCmdlineArgs(), "--position") >= 0 || Array.IndexOf(OS.GetCmdlineUserArgs(), "--noinput") >= 0;

    static Input.MouseModeEnum _mode = Input.MouseModeEnum.Visible;

    /// <summary>The mode the game asked for (in a test run, not the system's).</summary>
    public static Input.MouseModeEnum Mode => NoInput ? _mode : Input.MouseMode;

    public static bool Captured => Mode == Input.MouseModeEnum.Captured;

    public static void Set(Input.MouseModeEnum m)
    {
        _mode = m;
        if (!NoInput)
            Input.MouseMode = m;
    }

    public static void Capture() => Set(Input.MouseModeEnum.Captured);
}
