// Chosen core: original code.
using System.Collections.Generic;
using Godot;

namespace Chosen.Forge.Config;

/// <summary>The 2D view's colours, by UDB's names (its ColorCollection), close to UDB's defaults; saved in forge.cfg as
/// colors.&lt;name&gt; = #rrggbb.</summary>
public static class ColorSettings
{
    public static Color Background = new(0f, 0f, 0f);
    public static Color Vertices = new(0.25f, 0.55f, 1f);
    public static Color Linedefs = new(1f, 1f, 1f);
    /// <summary>A red wall pair (UDB draws two-sided lines dimmer).</summary>
    public static Color TwoSided = new(0.62f, 0.62f, 0.62f);
    /// <summary>A blocking wall (cstat 1): brighter.</summary>
    public static Color Blocking = new(0.85f, 0.85f, 0.85f);
    public static Color Masked = new(0.35f, 0.85f, 0.85f);
    public static Color OneWay = new(0.85f, 0.55f, 1f);
    public static Color Highlight = new(1f, 0.6f, 0.15f);
    public static Color Selection = new(1f, 0.25f, 0.25f);
    public static Color Indication = new(1f, 1f, 0.35f);
    public static Color Grid = new(0.12f, 0.12f, 0.16f);
    public static Color Grid64 = new(0.18f, 0.18f, 0.3f);
    public static Color Origin = new(0.3f, 0.3f, 0.45f);
    public static Color FirstWall = new(0.4f, 1f, 0.4f);
    /// <summary>A wall that does something when the player uses it (UDB's lines with an action): a push, shot or touch
    /// trigger of its own, or a wall of a sector opened by pushing its walls (a door, a secret door).</summary>
    public static Color Actions = new(0.45f, 0.9f, 0.45f);
    public static Color Label = new(0.8f, 0.8f, 0.8f, 0.85f);
    /// <summary>The lines between linked elements (UDB's event lines: channels, links, markers).</summary>
    public static Color Links = new(1f, 0.85f, 0.3f);

    /// <summary>Sprite marker colours by category (UDB's thing colours).</summary>
    public static readonly Dictionary<string, Color> Things = new()
    {
        ["Decoration"] = new(0.55f, 0.55f, 0.55f),
        ["PlayerStart"] = new(0.3f, 1f, 0.3f),
        ["Marker"] = new(0.65f, 0.4f, 1f),
        ["Switch"] = new(1f, 0.9f, 0.3f),
        ["Weapon"] = new(0.3f, 0.75f, 1f),
        ["Ammo"] = new(0.4f, 0.6f, 0.9f),
        ["Item"] = new(0.3f, 0.9f, 0.9f),
        ["Key"] = new(1f, 0.85f, 0.1f),
        ["Dude"] = new(1f, 0.3f, 0.3f),
        ["Thing"] = new(0.85f, 0.6f, 0.35f),
        ["Trap"] = new(1f, 0.5f, 0.2f),
        ["Generator"] = new(0.9f, 0.4f, 0.8f),
        ["Sound"] = new(0.4f, 0.9f, 0.5f),
        ["Other"] = new(0.7f, 0.7f, 0.7f),
    };

    static readonly (string name, System.Func<Color> get, System.Action<Color> set)[] Named =
    {
        ("background", () => Background, c => Background = c), ("vertices", () => Vertices, c => Vertices = c),
        ("linedefs", () => Linedefs, c => Linedefs = c), ("twosided", () => TwoSided, c => TwoSided = c),
        ("blocking", () => Blocking, c => Blocking = c), ("masked", () => Masked, c => Masked = c),
        ("oneway", () => OneWay, c => OneWay = c), ("highlight", () => Highlight, c => Highlight = c),
        ("selection", () => Selection, c => Selection = c), ("indication", () => Indication, c => Indication = c),
        ("grid", () => Grid, c => Grid = c), ("grid64", () => Grid64, c => Grid64 = c),
        ("firstwall", () => FirstWall, c => FirstWall = c), ("actions", () => Actions, c => Actions = c),
        ("links", () => Links, c => Links = c),
    };

    public static IEnumerable<string> Names
    {
        get
        {
            foreach (var n in Named)
                yield return n.name;
        }
    }

    public static Color Get(string name)
    {
        foreach (var n in Named)
            if (n.name == name)
                return n.get();
        return Colors.White;
    }

    public static void Set(string name, Color c)
    {
        foreach (var n in Named)
            if (n.name == name)
                n.set(c);
    }

    public static void Load(ForgeConfig cfg)
    {
        foreach (var n in Named)
            if (cfg.Get("colors." + n.name) is { Length: > 0 } v && Color.HtmlIsValid(v))
                n.set(Color.FromHtml(v));
    }

    public static void Save(ForgeConfig cfg)
    {
        foreach (var n in Named)
            cfg.Set("colors." + n.name, "#" + n.get().ToHtml(false));
    }
}
