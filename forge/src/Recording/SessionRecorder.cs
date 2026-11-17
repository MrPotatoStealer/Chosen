// Chosen core: original code.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Chosen.Forge.Document;
using Godot;

namespace Chosen.Forge.Recording;

/// <summary>
/// Records an editing session so it can be replayed exactly (SessionReplay): user://sessions/&lt;date-time&gt;/events.log,
/// one line an event, "&lt;ms&gt;\t&lt;kind&gt;\t&lt;fields...&gt;":
/// <list type="bullet">
/// <item>state: the settings input depends on (grid, snap, units, highlight range, sprite type, edit-new, hidden sprite
///   categories), written when they change;</item>
/// <item>view: centre and zoom (Build units), before an event when they changed (the snap ranges are pixels);</item>
/// <item>move / btn / key: the view's input in map units (x y), modifiers (s c a), buttons; key: keycode, physical,
///   pressed, echo, the path it came by (v: the view, u: unhandled);</item>
/// <item>leave, do &lt;action&gt; (menus, toolbar, mode buttons), new, open &lt;copy of the file&gt; &lt;patch&gt; &lt;original&gt;;</item>
/// <item>doc &lt;file&gt; &lt;undo|merge&gt; &lt;what&gt;: a dialog's result (the map after it), revert (a new sprite's
///   dialog cancelled);</item>
/// <item>check &lt;fingerprint&gt; &lt;undo name&gt;: the map after every edit; status, save, mark (Report a Problem).</item>
/// </list>
/// Mouse motion is kept back and written before the next other event (only the last position before a click
/// matters to the modes).
/// </summary>
public sealed class SessionRecorder
{
    public static string Root => ProjectSettings.GlobalizePath("user://sessions");
    /// <summary>The tests' own sessions (--uitest, --rebuild): they never push the user's out of the newest 30.</summary>
    public static string TestRoot => ProjectSettings.GlobalizePath("user://test-sessions");
    const int Keep = 30;
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    readonly MainWindow _w;
    readonly StreamWriter _log;
    readonly Stopwatch _clock = Stopwatch.StartNew();
    int _files;
    string _lastState, _lastView, _lastStatus;
    (string line, string view)? _motion;

    public string Folder { get; }

    SessionRecorder(MainWindow w, string folder)
    {
        _w = w;
        Folder = folder;
        _log = new StreamWriter(Path.Combine(folder, "events.log"), false, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
        Line("session", "1", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", Inv), $"{w.GetWindow().Size.X}x{w.GetWindow().Size.Y}");
        ListenForErrors();
    }

    /// <summary>A new session folder in root (the oldest beyond the newest 30 removed: one still open, a Forge
    /// running, is left); null when it can't be made.</summary>
    public static SessionRecorder Start(MainWindow w, string root = null)
    {
        root ??= Root;
        try
        {
            Directory.CreateDirectory(root);
            foreach (var old in Directory.GetDirectories(root).OrderByDescending(d => d).Skip(Keep - 1))
            {
                try
                {
                    Directory.Delete(old, true);
                }
                catch (Exception)
                {
                    // in use (another Forge is recording into it): kept
                }
            }
            string name = DateTime.Now.ToString("yyyyMMdd-HHmmss", Inv);
            string folder = Path.Combine(root, name);
            for (int i = 2; Directory.Exists(folder); i++)
                folder = Path.Combine(root, $"{name}-{i}");
            Directory.CreateDirectory(folder);
            return new SessionRecorder(w, folder);
        }
        catch (Exception e)
        {
            GD.PrintErr("session recording off: " + e.Message);
            return null;
        }
    }

    public static string F(double v) => Math.Round(v, 3).ToString("0.###", Inv);
    static string Clean(string s) => (s ?? "").Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' ');

    /// <summary>A line as it is (the pending motion first).</summary>
    public void Line(string kind, params string[] fields)
    {
        FlushMotion();
        Write(kind, fields);
    }

    void Write(string kind, string[] fields)
    {
        var sb = new StringBuilder();
        sb.Append(_clock.ElapsedMilliseconds.ToString(Inv)).Append('\t').Append(kind);
        foreach (var f in fields)
            sb.Append('\t').Append(Clean(f));
        Emit(sb.ToString());
    }

    void FlushMotion()
    {
        if (_motion is not { } m)
            return;
        _motion = null;
        WriteView(m.view);
        Emit(m.line);
    }

    string ViewNow() => $"{F(_w.View.Centre.X)}\t{F(_w.View.Centre.Y)}\t{_w.View.Zoom.ToString("R", Inv)}";

    void WriteView(string view)
    {
        if (view == _lastView)
            return;
        _lastView = view;
        Emit($"{_clock.ElapsedMilliseconds.ToString(Inv)}\tview\t{view}");
    }

    /// <summary>The settings input depends on, written when they changed.</summary>
    void State()
    {
        string s = _w.RecordedState();
        if (s == _lastState)
            return;
        _lastState = s;
        Line("state", s);
    }

    static string Mods(InputEventWithModifiers e) => (e.ShiftPressed ? "s" : "") + (e.CtrlPressed ? "c" : "") + (e.AltPressed ? "a" : "") is { Length: > 0 } m ? m : "-";

    /// <summary>An input event: from the view (path v) or a key no control took (u).</summary>
    public void Input(InputEvent e, string path)
    {
        State();
        var v = _w.View;
        switch (e)
        {
            case InputEventMouseMotion mm:
            {
                var m = v.ScreenToMap(mm.Position);
                string line = $"{_clock.ElapsedMilliseconds.ToString(Inv)}\tmove\t{F(m.X)}\t{F(m.Y)}\t{Mods(mm)}\t{(int)mm.ButtonMask}";
                // only the last motion before another event matters; it is written with the view it was made in
                if (_motion is { } old && old.view != ViewNow())
                    FlushMotion();
                _motion = (line, ViewNow());
                return;
            }
            case InputEventMouseButton mb:
            {
                FlushMotion();
                WriteView(ViewNow());
                var m = v.ScreenToMap(mb.Position);
                Write("btn", new[] { ((int)mb.ButtonIndex).ToString(Inv), mb.Pressed ? "1" : "0", F(m.X), F(m.Y), Mods(mb),
                    ((int)mb.ButtonMask).ToString(Inv), mb.DoubleClick ? "1" : "0", F(mb.Factor) });
                return;
            }
            case InputEventKey k:
                FlushMotion();
                WriteView(ViewNow());
                Write("key", new[] { path, ((long)k.Keycode).ToString(Inv), ((long)k.PhysicalKeycode).ToString(Inv), k.Pressed ? "1" : "0",
                    k.Echo ? "1" : "0", Mods(k), ((long)k.Unicode).ToString(Inv) });
                return;
        }
    }

    public void Leave() => Line("leave");

    /// <summary>A command from a menu, the toolbar or a mode button.</summary>
    public void Do(string action)
    {
        State();
        FlushMotion();
        WriteView(ViewNow());
        Write("do", new[] { action });
    }

    public void NewMap() => Line("new");

    /// <summary>A map file opened: the file as it was (and its patch when applied) copied into the session first.</summary>
    public void Opened(string path, bool patch)
    {
        string copy = $"map-{++_files:00}-original{Path.GetExtension(path)}";
        try
        {
            File.Copy(path, Path.Combine(Folder, copy), true);
            string p = Path.ChangeExtension(path, ".patch");
            if (patch && File.Exists(p))
                File.Copy(p, Path.Combine(Folder, Path.ChangeExtension(copy, ".patch")), true);
        }
        catch (Exception e)
        {
            GD.PrintErr("session: can't copy the map: " + e.Message);
        }
        Line("open", copy, patch ? "1" : "0", path);
    }

    /// <summary>The map after a dialog changed it (undo: as its own undo step; merge: part of the step before).</summary>
    public void Document(MapDocument doc, string how, string what)
    {
        string file = $"doc-{++_files:00}.mapx";
        File.WriteAllBytes(Path.Combine(Folder, file), doc.Snapshot());
        Line("doc", file, how, what);
    }

    public void Revert() => Line("revert");

    // ---------------------------------------------------------------- dialogs: the fields changed, not their result

    /// <summary>A dialog's fields in tree order (text boxes, check boxes, spin boxes; not those of a window opened
    /// from it): the same dialog built again has them in the same order.</summary>
    public static List<Control> Fields(Node root)
    {
        var l = new List<Control>();
        // (a field added to a dialog later is marked LateField: counted after the others wherever it stands, so the
        // sessions recorded before it keep their fields' numbers)
        var late = new List<Control>();
        void Walk(Node n)
        {
            foreach (var c in n.GetChildren())
            {
                if (c is Window)
                    continue;
                if (c is LineEdit or CheckBox or SpinBox)
                    (c.HasMeta(LateField) ? late : l).Add((Control)c);
                if (c is not SpinBox)
                    Walk(c);
            }
        }
        Walk(root);
        l.AddRange(late);
        return l;
    }

    /// <summary>The meta a field added to an existing dialog carries (Fields).</summary>
    public const string LateField = "rec_late";

    public static string ValueOf(Control c) => c switch
    {
        LineEdit e => e.Text,
        CheckBox b => b.ButtonPressed ? "1" : "0",
        SpinBox s => s.Value.ToString("R", Inv),
        _ => "",
    };

    /// <summary>A field set as if typed or clicked (its signals fire: a type's name, a flag marked clicked).</summary>
    public static void SetValue(Control c, string v)
    {
        switch (c)
        {
            case LineEdit e:
                e.Text = v;
                e.EmitSignal(LineEdit.SignalName.TextChanged, v);
                break;
            case CheckBox b:
                b.ButtonPressed = v == "1";
                break;
            case SpinBox s:
                s.Value = double.Parse(v, Inv);
                break;
        }
    }

    readonly Dictionary<ulong, string[]> _opened = new();

    /// <summary>A dialog shown: its fields as they start.</summary>
    public void DialogOpened(Window d) => _opened[d.GetInstanceId()] = Fields(d).Select(ValueOf).ToArray();

    /// <summary>A dialog applied: the fields changed since it opened ("i=value", escaped), written before what it does.</summary>
    public void DialogConfirmed(Window d, string what)
    {
        var f = Fields(d);
        _opened.TryGetValue(d.GetInstanceId(), out var start);
        var changed = new List<string>();
        for (int i = 0; i < f.Count; i++)
        {
            string v = ValueOf(f[i]);
            if (start == null || i >= start.Length || start[i] != v)
                changed.Add($"{i}={Uri.EscapeDataString(v)}");
        }
        _opened.Remove(d.GetInstanceId());
        Line("dlg", what, string.Join(";", changed));
    }

    public void DialogCanceled(Window d, string what)
    {
        _opened.Remove(d.GetInstanceId());
        Line("dlgcancel", what);
    }

    /// <summary>The map's fingerprint after an edit.</summary>
    public void Check(MapDocument doc, string undoName) => Line("check", Fingerprint(doc), undoName ?? "");

    public void Status(string text)
    {
        if (text == _lastStatus)
            return;
        _lastStatus = text;
        Line("status", text);
    }

    public void Mark(string text) => Line("mark", text);

    /// <summary>FNV-1a over the map's .MAPX bytes but its INFO chunk (the dates it was made and saved, the
    /// generator) and the CRC over all of it: what the map is, not when.</summary>
    public static string Fingerprint(MapDocument doc)
    {
        var d = doc.Snapshot();
        ulong h = 14695981039346656037;
        void Hash(int from, int to)
        {
            for (int i = from; i < to; i++)
            {
                h ^= d[i];
                h *= 1099511628211;
            }
        }
        // MAPX, u16 version, u16 flags, then chunks: fourcc, u32 length, the data
        Hash(0, Math.Min(8, d.Length));
        int p = 8;
        while (p + 8 <= d.Length)
        {
            int len = (int)BitConverter.ToUInt32(d, p + 4);
            int end = Math.Min(d.Length, p + 8 + len);
            if (Encoding.ASCII.GetString(d, p, 4) is not ("INFO" or "CRC "))
                Hash(p, end);
            p = end;
        }
        return h.ToString("x16", Inv);
    }

    public void Close()
    {
        FlushMotion();
        Write("end", Array.Empty<string>());
        if (_errors != null)
            OS.RemoveLogger(_errors);
        lock (_log)
        {
            _closed = true;
            _log.Dispose();
        }
    }

    bool _closed;
    ErrorLog _errors;

    /// <summary>A line written (the error logger writes from other threads too).</summary>
    void Emit(string line)
    {
        lock (_log)
        {
            if (!_closed)
                _log.WriteLine(line);
        }
    }

    /// <summary>An error or warning Godot or a script reported (a C# exception in a callback too): an "error" line, so
    /// a report shows what went wrong where; a replay says where the recording had one.</summary>
    internal void Error(string type, string what, string where) =>
        Emit($"{_clock.ElapsedMilliseconds.ToString(Inv)}\terror\t{type}\t{Clean(what)}\t{Clean(where)}");

    void ListenForErrors()
    {
        _errors = new ErrorLog(this);
        OS.AddLogger(_errors);
    }
}

/// <summary>Godot's error stream into a session's events.log.</summary>
public partial class ErrorLog : Logger
{
    readonly SessionRecorder _r;

    public ErrorLog() { }

    public ErrorLog(SessionRecorder r) => _r = r;

    public override void _LogError(string function, string file, int line, string code, string rationale, bool editorNotify, int errorType,
        Godot.Collections.Array<ScriptBacktrace> scriptBacktraces)
    {
        string type = errorType switch { 1 => "warning", 2 => "script", 3 => "shader", _ => "error" };
        _r?.Error(type, string.IsNullOrEmpty(rationale) ? code : rationale, $"{file}:{line} {function}");
    }
}
