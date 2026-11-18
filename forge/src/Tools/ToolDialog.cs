// Chosen core: original code. A tool's options: a small form whose values are remembered (forge.cfg tool.<id>.<key>),
// a preview drawn in the 2D view while it is open, and the window command it applies.
using System;
using System.Collections.Generic;
using Chosen.Forge.Config;
using Chosen.Forge.Ui;
using Godot;

namespace Chosen.Forge.Tools;

public sealed class ToolDialog
{
    readonly MainWindow _w;
    readonly string _id;
    readonly ConfirmationDialog _d;
    GridContainer _g;
    TabContainer _tabs;
    readonly List<Action> _save = new();

    /// <summary>The paths (map coordinates) the tool would make with the options as they are: drawn in the 2D view.</summary>
    public Func<List<Vector2[]>> Preview;
    /// <summary>The tool's change done to a copy of the map (null: done; else why not): the 3D preview shows that copy
    /// while the dialog is open, so the result is seen before it is applied (the 2D view too when the tool has no
    /// Preview paths).</summary>
    public Func<Document.MapDocument, string> Change;
    /// <summary>Told at once of every change (a dialog showing it its own quicker way: the light dialog's lights), and
    /// once the dialog closes.</summary>
    public Action Live, Closed;
    /// <summary>A dialog of settings (not one that builds): its one button Close, and closing it in any way (the window's
    /// X, Esc) applies what was changed (Undo takes it back).</summary>
    public bool ApplyOnClose;
    bool _pending;
    /// <summary>Why the tool can't do it with the options as they are (Change's answer), shown in the dialog as they
    /// change (Make Door on a doorway whose walls are too thin for its leaves only left the preview empty: Forge report
    /// 20261107-105619).</summary>
    Label _why;

    public ToolDialog(MainWindow w, string id, string title, string ok = "Apply")
    {
        _w = w;
        _id = id;
        _d = new ConfirmationDialog { Title = title, OkButtonText = ok };
        // (the form, or its tabs, and under them the line saying why the tool can't)
        _box = new VBoxContainer();
        _d.AddChild(_box);
        _g = Form.Grid(_box);
    }

    readonly VBoxContainer _box;

    /// <summary>The setting a field is remembered as (null: a dialog that remembers nothing, its id null).</summary>
    string Key(string k) => _id == null ? null : $"tool.{_id}.{k}";

    int GetInt(string k, int def) => Key(k) is { } key ? _w.Config.GetInt(key, def) : def;
    bool GetBool(string k, bool def) => Key(k) is { } key ? _w.Config.GetBool(key, def) : def;

    /// <summary>The fields from here on in a tab of this name (the first call puts the form into tabs).</summary>
    public void Tab(string name, Texture2D icon = null)
    {
        if (_tabs == null)
        {
            _tabs = new TabContainer();
            _g.AddSibling(_tabs);
            _g.GetParent().RemoveChild(_g);
            _g.QueueFree();
        }
        var page = new MarginContainer { Name = name };
        page.AddThemeConstantOverride("margin_left", 8);
        page.AddThemeConstantOverride("margin_top", 8);
        page.AddThemeConstantOverride("margin_right", 8);
        page.AddThemeConstantOverride("margin_bottom", 8);
        _tabs.AddChild(page);
        if (icon != null)
            _tabs.SetTabIcon(_tabs.GetTabCount() - 1, icon);
        _g = Form.Grid(page);
    }

    /// <summary>A row of the form: a label and any control (a button).</summary>
    public void Row(string label, Control c) => Form.Row(_g, label, c);

    /// <summary>The previews made again (a field the dialog doesn't know of changed).</summary>
    public void Refresh() => Changed();

    void Changed()
    {
        _w.ToolPreview = Preview?.Invoke();
        _w.View.QueueRedraw();
        Live?.Invoke();
        // the copy shown: made again a moment after the last change (a spin box held runs through many); in the 3D
        // preview, and in the 2D view for a tool that draws no paths of its own there (the gradient's brightness on the
        // floors' pictures)
        if (Change == null || (!_w.PreviewShown && Preview != null) || _pending)
            return;
        _pending = true;
        _w.GetTree().CreateTimer(0.08).Timeout += () =>
        {
            _pending = false;
            if (!GodotObject.IsInstanceValid(_d) || !_d.Visible)
                return;
            var copy = _w.Doc.Copy();
            string why = Change(copy);
            _w.ShowToolResult(why == null ? copy : null);
            if (_why != null)
            {
                _why.Text = why == null ? "" : "Can't: " + why;
                _why.Visible = why != null;
            }
        };
    }

    public SpinBox Int(string key, string label, int def, int min, int max)
    {
        var s = Form.Number(_g, label, Math.Clamp(GetInt(key, def), min, max), min, max);
        s.ValueChanged += _ => Changed();
        if (Key(key) is { } k)
            _save.Add(() => _w.Config.Set(k, (int)s.Value));
        return s;
    }

    /// <summary>A number with a button beside it (Pick...: a picker that sets it); the row's box holds both (and
    /// anything more added to it: a picture of the tile). key null: not remembered (a channel: the next door's own).</summary>
    public SpinBox IntPick(string key, string label, int def, int min, int max, string button, Action<SpinBox, Button> pressed)
    {
        var row = new HBoxContainer();
        var s = new SpinBox { MinValue = min, MaxValue = max, Step = 1, Value = Math.Clamp(key == null ? def : GetInt(key, def), min, max),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        var b = new Button { Text = button };
        row.AddChild(s);
        row.AddChild(b);
        Form.Row(_g, label, row);
        s.ValueChanged += _ => Changed();
        b.Pressed += () => pressed(s, b);
        if (key != null && Key(key) is { } k)
            _save.Add(() => _w.Config.Set(k, (int)s.Value));
        return s;
    }

    public OptionButton Choice(string key, string label, string[] names, int def)
    {
        var o = new OptionButton();
        foreach (var n in names)
            o.AddItem(n);
        o.Selected = Math.Clamp(GetInt(key, def), 0, names.Length - 1);
        o.ItemSelected += _ => Changed();
        Form.Row(_g, label, o);
        if (Key(key) is { } k)
            _save.Add(() => _w.Config.Set(k, o.Selected));
        return o;
    }

    public CheckBox Check(string key, string label, bool def)
    {
        var c = new CheckBox { ButtonPressed = GetBool(key, def) };
        c.Toggled += _ => Changed();
        Form.Row(_g, label, c);
        if (Key(key) is { } k)
            _save.Add(() => _w.Config.Set(k, c.ButtonPressed));
        return c;
    }

    /// <summary>A length in the units shown (def: Build units, when none is remembered); Build units back from BuildLength.</summary>
    public SpinBox Length(string key, string label, double def, double max = 65536)
    {
        double build = Key(key) != null && double.TryParse(_w.Config.Get(Key(key)), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : def;
        double scale = Units.Game ? Units.PerXy : 1;
        var s = Form.Number(_g, $"{label} ({Units.Name})", build / scale, 0, max / scale);
        s.Step = Units.Game ? 0.25 : 1;
        s.ValueChanged += _ => Changed();
        if (Key(key) is { } k)
            _save.Add(() => _w.Config.Set(k, BuildLength(s).ToString(System.Globalization.CultureInfo.InvariantCulture)));
        return s;
    }

    public static double BuildLength(SpinBox s) => Math.Round(Units.Game ? s.Value * Units.PerXy : s.Value);

    public void Note(string text)
    {
        // (wrapped: a long one ran the dialog off the screen, the atmosphere's)
        var l = Form.Info(_g, "", text);
        l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        l.CustomMinimumSize = new Vector2(420, 0);
    }

    /// <summary>A tool's rules for what is selected, one a line: those kept in green, the others in red with what to
    /// change (problem); the dialog's button off unless all are kept (Make Portal on two walls: RoomLinks.WallPortalRules).</summary>
    public void Rules(IEnumerable<(string Rule, string Problem)> rules)
    {
        bool all = true;
        foreach (var (rule, problem) in rules)
        {
            all &= problem == null;
            var l = Form.Info(_g, problem == null ? "Met" : "Not met", problem == null ? rule : $"{rule}.\n{char.ToUpper(problem[0])}{problem[1..]}.");
            l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            l.CustomMinimumSize = new Vector2(420, 0);
            l.AddThemeColorOverride("font_color", problem == null ? new Color(0.55f, 0.85f, 0.55f) : new Color(1f, 0.55f, 0.45f));
        }
        _d.GetOkButton().Disabled = !all;
    }

    /// <summary>Shown; Apply: the values remembered and command()'s window command done (null: nothing).</summary>
    public void Show(Func<string> command)
    {
        if (Change != null)
        {
            _why = new Label();
            _box.AddChild(_why);
            _why.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            _why.CustomMinimumSize = new Vector2(420, 0);
            _why.AddThemeColorOverride("font_color", new Color(1f, 0.55f, 0.45f));
            _why.Visible = false;
        }
        if (ApplyOnClose)
            MainWindow.ApplyOnClose(_w, _d);
        _d.Confirmed += () =>
        {
            foreach (var s in _save)
                s();
            if (_save.Count > 0)
                _w.Config.Save();
            if (command() is { } c)
                _w.UiDo(c);
        };
        _d.VisibilityChanged += () =>
        {
            if (!_d.Visible)
            {
                _w.ToolPreview = null;
                _w.View.QueueRedraw();
                _w.ShowToolResult(null);
                Closed?.Invoke();
            }
        };
        _w.Popup(_d);
        // (in a corner of the view, out of the way of what it changes: the 3D preview's middle, the 2D view's cursor)
        if (!_w.Replaying)
            Callable.From(() =>
            {
                if (GodotObject.IsInstanceValid(_d))
                    _d.Position = _w.PopupPosition(_w.ShownView.GlobalPosition + new Vector2(16, 40));
            }).CallDeferred();
        Changed();
    }
}
