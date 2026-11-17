using System.Collections.Generic;
using System.Globalization;
using Godot;

namespace Chosen.Core;

/// <summary>A console variable: a named setting, changeable from the console, optionally saved to the config.</summary>
public sealed class Cvar
{
    public readonly string Name, Default, Help;
    public readonly bool Saved;
    public string Value { get; private set; }

    public Cvar(string name, string def, string help, bool saved)
    {
        Name = name;
        Default = Value = def;
        Help = help;
        Saved = saved;
    }

    public float Float => float.TryParse(Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : 0f;
    public int Int => (int)Float;
    public bool Bool => Float != 0f;

    /// <summary>After the value changed (a setting that applies itself: the window mode, vsync).</summary>
    public event System.Action Changed;

    public void Set(string v)
    {
        bool differs = v != Value;
        Value = v;
        Cvars.Changed(this);
        if (differs)
            Changed?.Invoke();
    }
}

/// <summary>Registry of console variables; saved ones persist in user://config.cfg.</summary>
public static class Cvars
{
    static readonly Dictionary<string, Cvar> _all = new();
    static readonly Dictionary<string, string> _loaded = new();
    const string ConfigPath = "user://config.cfg";
    static bool _dirty;
    // set for a moment (a comparison, F10), the config keeping the real value: (the real value, what it was set to)
    static readonly Dictionary<Cvar, (string real, string shown)> _held = new();

    public static IEnumerable<Cvar> All => _all.Values;

    /// <summary>Sets a cvar for now only: the config keeps what it was, whenever the game quits.</summary>
    public static void Hold(Cvar c, string shown)
    {
        if (_held.ContainsKey(c))
            return;
        _held[c] = (c.Value, shown);
        c.Set(shown);
    }

    /// <summary>Back from Hold; one changed since (the menu, the console) keeps that.</summary>
    public static void Release(Cvar c)
    {
        if (!_held.TryGetValue(c, out var h))
            return;
        _held.Remove(c);
        if (c.Value == h.shown)
            c.Set(h.real);
    }

    public static Cvar Register(string name, string def, string help = "", bool saved = false)
    {
        if (_all.TryGetValue(name, out var c))
            return c;
        c = new Cvar(name, def, help, saved);
        _all[name] = c;
        if (_loaded.TryGetValue(name, out var v))
            c.Set(v);
        return c;
    }

    public static Cvar Find(string name) => _all.TryGetValue(name, out var c) ? c : null;

    /// <summary>Registers every cvar the game declares (static Cvar fields), at startup: a class's statics otherwise
    /// run only when it is first used, so a setting would be missing from the console, --exec and the menu until then.</summary>
    public static void RegisterAll()
    {
        foreach (var t in typeof(Cvars).Assembly.GetTypes())
        {
            if (t.ContainsGenericParameters)
                continue;
            foreach (var f in t.GetFields(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic))
            {
                if (f.FieldType != typeof(Cvar))
                    continue;
                System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(t.TypeHandle);
                break;
            }
        }
    }

    internal static void Changed(Cvar c)
    {
        if (c.Saved)
            _dirty = true;
    }

    public static void LoadConfig()
    {
        if (!FileAccess.FileExists(ConfigPath))
            return;
        using var f = FileAccess.Open(ConfigPath, FileAccess.ModeFlags.Read);
        while (!f.EofReached())
        {
            var line = f.GetLine().Trim();
            int sp = line.IndexOf(' ');
            if (sp <= 0)
                continue;
            string name = line[..sp], value = line[(sp + 1)..].Trim().Trim('"');
            _loaded[name] = value;
            Find(name)?.Set(value);
        }
        _dirty = false;
    }

    public static void SaveConfigIfChanged()
    {
        if (!_dirty)
            return;
        using var f = FileAccess.Open(ConfigPath, FileAccess.ModeFlags.Write);
        foreach (var c in _all.Values)
            if (c.Saved)
                f.StoreLine($"{c.Name} \"{(_held.TryGetValue(c, out var h) && c.Value == h.shown ? h.real : c.Value)}\"");
        _dirty = false;
    }
}
