// Chosen core: original code.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;

namespace Chosen.Forge.Config;

/// <summary>Forge's settings: user://forge.cfg, key = value lines (the Blood folder, recent files, the window, colours,
/// grid, snap).</summary>
public sealed class ForgeConfig
{
    public const string FileName = "user://forge.cfg";
    readonly SortedDictionary<string, string> _v = new(StringComparer.Ordinal);

    public string Get(string key, string def = "") => _v.TryGetValue(key, out var v) ? v : def;
    public int GetInt(string key, int def) => int.TryParse(Get(key), out int v) ? v : def;
    public bool GetBool(string key, bool def) => _v.TryGetValue(key, out var v) ? v == "1" : def;
    public void Set(string key, string value) => _v[key] = value ?? "";
    public void Set(string key, int value) => _v[key] = value.ToString(System.Globalization.CultureInfo.InvariantCulture);
    public void Set(string key, bool value) => _v[key] = value ? "1" : "0";

    public static ForgeConfig Load()
    {
        var c = new ForgeConfig();
        string p = ProjectSettings.GlobalizePath(FileName);
        if (File.Exists(p))
            foreach (var line in File.ReadAllLines(p))
            {
                int eq = line.IndexOf('=');
                if (eq > 0 && !line.StartsWith('#'))
                    c._v[line[..eq].Trim()] = line[(eq + 1)..].Trim();
            }
        return c;
    }

    public void Save()
    {
        // a test run (off the screen, --noinput, a --shot picture; headless: the uitest applying a tool's dialog) leaves
        // the user's settings as they are
        if (Editing.ForgeInput.NoInput || DisplayServer.GetName() == "headless")
            return;
        string p = ProjectSettings.GlobalizePath(FileName);
        Directory.CreateDirectory(Path.GetDirectoryName(p));
        File.WriteAllLines(p, _v.Select(kv => kv.Key + " = " + kv.Value));
    }

    /// <summary>The recent files, newest first (recent.0 ..).</summary>
    public List<string> Recent()
    {
        var l = new List<string>();
        for (int i = 0; i < 10; i++)
            if (Get("recent." + i) is { Length: > 0 } f)
                l.Add(f);
        return l;
    }

    public void AddRecent(string file)
    {
        var l = Recent();
        l.RemoveAll(f => string.Equals(f, file, StringComparison.OrdinalIgnoreCase));
        l.Insert(0, file);
        for (int i = 0; i < 10; i++)
            Set("recent." + i, i < l.Count ? l[i] : "");
    }
}

/// <summary>
/// The keys of Forge's actions: forge/data/keys.cfg (action = key, UDB's action names so its documentation applies),
/// overridden by forge.cfg's keys.&lt;action&gt;. Keys as "Ctrl+Shift+G", "Insert", "Alt+G", "Shift+BracketRight".
/// </summary>
public sealed class KeyMap
{
    readonly Dictionary<string, (Key key, bool ctrl, bool shift, bool alt)> _keys = new();
    readonly Dictionary<string, string> _text = new();

    public IEnumerable<string> Actions => _text.Keys;
    public string Text(string action) => _text.TryGetValue(action, out var t) ? t : "";

    public static KeyMap Load(ForgeConfig cfg)
    {
        var k = new KeyMap();
        // through Godot's files: in an exported Forge res:// is inside its package
        const string p = "res://data/keys.cfg";
        if (Godot.FileAccess.FileExists(p))
            foreach (var line in Godot.FileAccess.GetFileAsString(p).Split('\n'))
            {
                var t = line.Trim();
                int eq = t.IndexOf('=');
                if (eq > 0 && !t.StartsWith('#'))
                    k.Bind(t[..eq].Trim(), t[(eq + 1)..].Trim());
            }
        foreach (var a in k._text.Keys.ToList())
            if (cfg.Get("keys." + a) is { Length: > 0 } over)
                k.Bind(a, over);
        return k;
    }

    public void Bind(string action, string text)
    {
        _text[action] = text;
        bool ctrl = false, shift = false, alt = false;
        Key key = Key.None;
        foreach (var part in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            switch (part.ToLowerInvariant())
            {
                case "ctrl": ctrl = true; break;
                case "shift": shift = true; break;
                case "alt": alt = true; break;
                default: key = OS.FindKeycodeFromString(part); break;
            }
        if (key != Key.None)
            _keys[action] = (key, ctrl, shift, alt);
        else
            _keys.Remove(action);
    }

    /// <summary>An action of the 3D preview only ("visual...", but visualmode itself): in the preview it comes before
    /// the others on the same key, elsewhere it doesn't count.</summary>
    public static bool PreviewOnly(string action) => action.StartsWith("visual", StringComparison.Ordinal) && action != "visualmode";

    /// <summary>The action a key press means, or null (preview: the 3D preview's keys first).</summary>
    public string Match(InputEventKey e, bool preview = false)
    {
        var k = e.Keycode;
        string other = null;
        foreach (var (a, b) in _keys)
            if (b.key == k && b.ctrl == e.CtrlPressed && b.shift == e.ShiftPressed && b.alt == e.AltPressed)
            {
                if (!PreviewOnly(a))
                    other ??= a;
                else if (preview)
                    return a;
            }
        return other;
    }

    /// <summary>A Godot shortcut for a menu item.</summary>
    public Shortcut Shortcut(string action)
    {
        if (!_keys.TryGetValue(action, out var b))
            return null;
        var ev = new InputEventKey { Keycode = b.key, CtrlPressed = b.ctrl, ShiftPressed = b.shift, AltPressed = b.alt };
        return new Shortcut { Events = new Godot.Collections.Array { ev } };
    }
}
