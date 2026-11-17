// Chosen core: original code.
using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Chosen.Forge.Editing;

/// <summary>The editing modes and which one is on (UDB's EditingManager): the view's input goes to it.</summary>
public sealed class EditingManager
{
    readonly MainWindow _window;
    public readonly List<EditMode> Modes = new();
    public EditMode Mode { get; private set; }
    /// <summary>The classic mode last on (a draw mode goes back to it).</summary>
    public EditMode LastClassic { get; private set; }
    public event Action ModeChanged;

    public EditingManager(MainWindow window) => _window = window;

    public void Add(EditMode m)
    {
        m.Window = _window;
        Modes.Add(m);
    }

    public T Get<T>() where T : EditMode => Modes.OfType<T>().FirstOrDefault();

    public void Switch(EditMode m)
    {
        if (m == Mode)
            return;
        Mode?.OnDisengage();
        if (Mode is ClassicMode)
            LastClassic = Mode;
        Mode = m;
        // the window's part first (buttons, "<mode> mode"): the mode's own status (a draw mode's help) then stays
        ModeChanged?.Invoke();
        Mode?.OnEngage();
        _window.View.QueueRedraw();
    }

    /// <summary>The mode Forge starts in, as it starts (no classic mode before it): a replay begins here, whatever
    /// the replay before it left on (a right-click on a wall inserted a point or not by the mode).</summary>
    public void Reset()
    {
        Switch(Modes[0]);
        LastClassic = null;
    }

    public void Switch(string action)
    {
        if (Modes.FirstOrDefault(x => x.Action == action) is { } m)
            Switch(m);
    }

    public void Input(InputEvent e)
    {
        if (Mode is ClassicMode c)
            c.HandleInput(e);
        else if (Mode is DrawGeometryMode d)
            d.HandleInput(e);
        else if (Mode is AlignGridMode a)
            a.HandleInput(e);
        else if (Mode is PasteMode p)
            p.HandleInput(e);
    }
}
