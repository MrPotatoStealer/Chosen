// Chosen core: original code. The context menu (Space, in the 2D view and the 3D preview): the tools that apply to
// what is selected, each a small piece that ends in a window command (recorded and replayed as any other).
using System;
using System.Collections.Generic;
using System.Linq;
using Chosen.Forge.Document;
using Chosen.Forge.View;
using Godot;

namespace Chosen.Forge.Tools;

/// <summary>What a tool works on: the 2D view's selection (else what is highlighted), or the 3D preview's targets
/// (its selection when what the crosshair is on is in it, else that alone).</summary>
public sealed class ToolContext
{
    public MainWindow Window;
    public MapDocument Doc => Window.Doc;
    public bool In3D;
    public List<int> Points = new(), Lines = new(), Sectors = new(), Sprites = new();
    public List<VisualTarget> Targets = new();
    /// <summary>The 3D preview: what the crosshair is on (null: nothing).</summary>
    public VisualTarget? UnderCrosshair;
    /// <summary>Lights mode (2D, or the 3D preview's): the menu has the lights' tools, not the surfaces' or sprites'.</summary>
    public bool LightsMode;
    /// <summary>The lights it works on (Lights mode: the selection, else the one under the mouse or crosshair).</summary>
    public List<int> Lights = new();

    /// <summary>The lines of the 3D targets' walls, or the 2D lines.</summary>
    public IEnumerable<int> AllLines => In3D
        ? Targets.Where(t => t.IsWall && t.Wall < Doc.LineOf.Length).Select(t => Doc.LineOf[t.Wall]).Distinct()
        : Lines;

    public bool Any => In3D ? Targets.Count > 0 : Points.Count + Lines.Count + Sectors.Count + Sprites.Count + Lights.Count > 0;

    /// <summary>What it holds, for the menu's title.</summary>
    public string Describe()
    {
        if (LightsMode)
            return Lights.Count == 1 ? $"light {Lights[0]}" : Lights.Count > 1 ? $"{Lights.Count} lights"
                : UnderCrosshair is { } u ? Window.DescribeTarget(u) : "Lights mode";
        if (In3D)
            return Targets.Count == 1 ? Window.DescribeTarget(Targets[0]) : $"{Targets.Count} selected";
        var parts = new List<string>();
        void Add(int n, string one) { if (n > 0) parts.Add(n == 1 ? "1 " + one : $"{n} {one}s"); }
        Add(Points.Count, "point");
        Add(Lines.Count, "wall");
        Add(Sectors.Count, "sector");
        Add(Sprites.Count, "sprite");
        return parts.Count == 0 ? "nothing selected" : string.Join(", ", parts);
    }
}

/// <summary>A tool in the context menu: its group (the menu's sections, in the order tools are registered), whether
/// it applies, what it does; Action: the keys.cfg action whose key the menu shows beside it; Shape: it changes the
/// map's shape (left out while the moving sectors are shown Off, On or playing).</summary>
public sealed record ContextTool(string Group, string Name, Func<ToolContext, bool> Applies, Action<ToolContext> Run,
    string Action = null, bool Shape = false);

public static class ContextTools
{
    static readonly List<ContextTool> _all = new();

    public static IReadOnlyList<ContextTool> All
    {
        get
        {
            if (_all.Count == 0)
                BuiltinTools.Register();
            return _all;
        }
    }

    public static void Register(ContextTool t) => _all.Add(t);

    /// <summary>The menu at a window position for context c (read once laid out: the mouse cursor; nothing applies:
    /// a status line says so); closed: after it closes without opening a dialog.</summary>
    public static void Show(ToolContext c, Func<Vector2> at, Action closed = null)
    {
        var w = c.Window;
        var tools = All.Where(t => (!t.Shape || !w.Posed) && t.Applies(c)).ToList();
        if (tools.Count == 0)
        {
            w.DisplayStatus($"context menu: no tool for {c.Describe()}");
            closed?.Invoke();
            return;
        }
        var menu = new PopupMenu();
        menu.AddSeparator(c.Describe());
        string group = null;
        for (int i = 0; i < tools.Count; i++)
        {
            if (group != null && tools[i].Group != group)
                menu.AddSeparator();
            group = tools[i].Group;
            menu.AddItem(tools[i].Name, i);
            if (tools[i].Action != null && w.Keys.Shortcut(tools[i].Action) is { } sc)
                menu.SetItemShortcut(menu.GetItemIndex(i), sc);
        }
        bool ran = false;
        menu.IdPressed += id =>
        {
            ran = true;
            tools[(int)id].Run(c);
        };
        // (after IdPressed: a tool that opened a dialog says when it is done itself)
        menu.PopupHide += () =>
        {
            menu.QueueFree();
            Callable.From(() => { if (!ran || !w.DialogOpen) closed?.Invoke(); }).CallDeferred();
        };
        w.AddChild(menu);
        // (once laid out: a view just shown has no size yet)
        w.GetTree().CreateTimer(0.05).Timeout += () =>
        {
            if (!GodotObject.IsInstanceValid(menu))
                return;
            menu.Position = w.PopupPosition(at());
            menu.Popup();
        };
    }
}
