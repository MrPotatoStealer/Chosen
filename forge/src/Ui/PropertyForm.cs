// Chosen core: original code (UDB's multi-edit dialogs: a field the items disagree on is blank and left alone).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Chosen.Forge.Config;
using Chosen.Forge.Data;
using Godot;

namespace Chosen.Forge.Ui;

/// <summary>
/// The fields of a properties dialog over several items at once: each shows the items' common value, or is blank
/// where they differ; Apply writes only the fields changed (a number typed, a flag clicked).
/// </summary>
public sealed class PropertyForm
{
    readonly List<Action> _apply = new();
    public readonly IReadOnlyList<int> Items;
    /// <summary>Raised by Apply for each item a field was written to (an x-structure made first, for instance).</summary>
    public int Changes { get; private set; }

    public PropertyForm(IReadOnlyList<int> items) => Items = items;

    /// <summary>A field typed in or clicked (the dialog's live preview).</summary>
    public event Action Edited;

    static string Common(IEnumerable<int> values, Func<int, string> show)
    {
        int? first = null;
        foreach (var v in values)
        {
            if (first == null)
                first = v;
            else if (first != v)
                return "";
        }
        return first is { } f ? show(f) : "";
    }

    /// <summary>A number field (blank where the items differ); caption: a note beside it (a data field's meaning); tip:
    /// help shown when the mouse rests on it. "++n" / "--n" add to or take from each item's value (UDB's).</summary>
    public LineEdit Int(GridContainer g, string label, Func<int, int> get, Action<int, int> set, string caption = null, string tip = null) =>
        Number(g, label, get, set, v => v.ToString(CultureInfo.InvariantCulture), Units.ParseInt, caption, tip);

    /// <summary>A horizontal position in the units shown (Preferences: game or Build units).</summary>
    public LineEdit Xy(GridContainer g, string label, Func<int, int> get, Action<int, int> set, string tip = null) =>
        Number(g, label, get, set, v => Units.Xy(v), Units.ParseXy, null, tip ?? $"in {Units.Name}; ++n or --n moves by n");

    /// <summary>Build's angle (2048 a circle) typed as it is, or in degrees with ° or d ("90°", "++45d").</summary>
    public static Func<int, int> ParseAngle(string text)
    {
        var m = System.Text.RegularExpressions.Regex.Match(text.Trim(), @"^(\+\+|--)?\s*(-?\d+(?:\.\d+)?)\s*(°|d|deg)$");
        if (!m.Success)
            return Units.ParseInt(text) is { } f ? v => f(v) & 2047 : null;
        int build = (int)Math.Round(double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) * 2048 / 360);
        return Units.ParseInt(m.Groups[1].Value + build.ToString(CultureInfo.InvariantCulture)) is { } g ? v => g(v) & 2047 : null;
    }

    /// <summary>A height in the units shown (game units: up is positive; Build: z down).</summary>
    public LineEdit Z(GridContainer g, string label, Func<int, int> get, Action<int, int> set, string tip = null) =>
        Number(g, label, get, set, v => Units.Z(v), Units.ParseZ, null, tip ?? (Units.Game
            ? "in game units, up is positive; ++1 raises by 1, --1 lowers"
            : "Build z: 256 to a game unit, larger is lower; ++n adds n"));

    /// <summary>A number field: shown by show, typed text turned into each item's new value by parse (null: not a
    /// number, left alone).</summary>
    LineEdit Number(GridContainer g, string label, Func<int, int> get, Action<int, int> set, Func<int, string> show,
        Func<string, Func<int, int>> parse, string caption, string tip)
    {
        string shown = Common(Items.Select(get), show);
        var e = new LineEdit { Text = shown, CustomMinimumSize = new Vector2(84, 0), PlaceholderText = shown == "" && Items.Count > 1 ? "(mixed)" : "",
            TooltipText = tip ?? "", SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin };
        g.AddChild(new Label { Text = label, TooltipText = tip ?? "", MouseFilter = tip != null ? Control.MouseFilterEnum.Pass : Control.MouseFilterEnum.Ignore });
        if (caption != null)
        {
            var row = new HBoxContainer();
            row.AddChild(e);
            var cap = new Label { Text = caption, ClipText = true, TooltipText = caption, MouseFilter = Control.MouseFilterEnum.Pass,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(1, 0) };
            cap.AddThemeColorOverride("font_color", ForgeTheme.Dim);
            row.AddChild(cap);
            g.AddChild(row);
        }
        else
            g.AddChild(e);
        e.TextChanged += _ => Edited?.Invoke();
        _apply.Add(() =>
        {
            var t = e.Text.Trim();
            if (t == "" || t == shown || parse(t) is not { } to)
                return;
            foreach (int i in Items)
            {
                int old = get(i), v = to(old);
                if (v != old)
                {
                    set(i, v);
                    Changes++;
                }
            }
        });
        return e;
    }

    /// <summary>Flag checkboxes over a bit field: a flag the items disagree on shows "(mixed)" and is written only when
    /// clicked.</summary>
    /// <returns>Whether a flag was clicked (by its bit).</returns>
    public Func<int, bool> Flags(Container parent, string title, (int bit, string name)[] bits, Func<int, int> get, Action<int, int> set)
    {
        var clicked = new HashSet<int>();
        var box = new VBoxContainer();
        box.AddChild(Heading(title));
        var grid = new GridContainer { Columns = 3 };
        grid.AddThemeConstantOverride("h_separation", 12);
        grid.AddThemeConstantOverride("v_separation", 0);
        box.AddChild(grid);
        parent.AddChild(box);
        foreach (var (bit, name) in bits)
        {
            int on = Items.Count(i => (get(i) & bit) != 0);
            bool mixed = on > 0 && on < Items.Count;
            var c = new CheckBox { Text = name + (mixed ? " (mixed)" : ""), ButtonPressed = on == Items.Count && on > 0 };
            int b = bit;
            c.Toggled += _ =>
            {
                clicked.Add(b);
                Edited?.Invoke();
            };
            grid.AddChild(c);
            _apply.Add(() =>
            {
                if (!clicked.Contains(b))
                    return;
                foreach (int i in Items)
                {
                    int v = get(i), nv = c.ButtonPressed ? v | b : v & ~b;
                    if (nv != v)
                    {
                        set(i, nv);
                        Changes++;
                    }
                }
            });
        }
        return b => clicked.Contains(b);
    }

    /// <summary>Whether the items have an x-structure (triggers, data): a check box ("(mixed)" when some have),
    /// written only when clicked: on makes one where there is none, off removes it. Its apply runs where it is added
    /// (before the x-fields after it).</summary>
    public void Presence(Container parent, string text, Func<int, bool> has, Action<int, bool> set)
    {
        int on = Items.Count(has);
        bool mixed = on > 0 && on < Items.Count;
        var c = new CheckBox { Text = text + (mixed ? " (mixed)" : ""), ButtonPressed = on == Items.Count && on > 0 };
        bool clicked = false;
        c.Toggled += _ =>
        {
            clicked = true;
            Edited?.Invoke();
        };
        parent.AddChild(c);
        _apply.Add(() =>
        {
            if (!clicked)
                return;
            foreach (int i in Items)
                if (has(i) != c.ButtonPressed)
                {
                    set(i, c.ButtonPressed);
                    Changes++;
                }
        });
    }

    /// <summary>A tile field: the number, its preview, a button opening the picker (on kind's tab: TilePickerDialog).</summary>
    public LineEdit Tile(GridContainer g, string label, Func<int, int> get, Action<int, int> set, MainWindow w, string kind = null)
    {
        var e = Int(g, label, get, set);
        var row = new HBoxContainer();
        var pic = new TextureRect { CustomMinimumSize = new Vector2(40, 40), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered };
        void Show()
        {
            pic.Texture = int.TryParse(e.Text, out int n) ? TileImages.Get(n) : null;
        }
        Show();
        e.TextChanged += _ => Show();
        var pick = new Button { Text = "Pick..." };
        pick.Pressed += () => TilePickerDialog.Show(w, pick, int.TryParse(e.Text, out int n) ? n : 0, t =>
        {
            e.Text = t.ToString(CultureInfo.InvariantCulture);
            Show();
        }, kind: kind);
        row.AddChild(pic);
        row.AddChild(pick);
        g.AddChild(new Control());
        g.AddChild(row);
        return e;
    }

    /// <summary>A palette field (Blood's pal: a picture's colours swapped through a lookup) with a button beside it
    /// opening the palette picker, each palette shown on the picture tile gives (the field's picture beside it).</summary>
    public LineEdit Pal(GridContainer g, string label, Func<int, int> get, Action<int, int> set, MainWindow w, Func<int> tile)
    {
        var e = Int(g, label, get, set, tip: "the picture's colours through one of Blood's palette lookups (Pick... shows them on it)");
        // the number and the button in the field's own cell (the grid's columns stay paired)
        int at = e.GetIndex();
        g.RemoveChild(e);
        var row = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        e.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        row.AddChild(e);
        var pick = new Button { Text = "Pick...", TooltipText = "Blood's palettes, each on this picture" };
        pick.Pressed += () => PalettePickerDialog.Show(w, pick, int.TryParse(e.Text, out int n) ? n : -1, tile(), p =>
        {
            e.Text = p.ToString(CultureInfo.InvariantCulture);
            e.EmitSignal(LineEdit.SignalName.TextChanged, e.Text);
        });
        row.AddChild(pick);
        g.AddChild(row);
        g.MoveChild(row, at);
        return e;
    }

    /// <summary>A 0 / 1 field as a check box ("(mixed)" where the items differ; written only when clicked).</summary>
    public CheckBox Check(Container parent, string text, Func<int, int> get, Action<int, int> set, string tip = "")
    {
        int on = Items.Count(i => get(i) != 0);
        bool mixed = on > 0 && on < Items.Count;
        var c = new CheckBox { Text = text + (mixed ? " (mixed)" : ""), ButtonPressed = on == Items.Count && on > 0, TooltipText = tip };
        bool clicked = false;
        c.Toggled += _ =>
        {
            clicked = true;
            Edited?.Invoke();
        };
        parent.AddChild(c);
        _apply.Add(() =>
        {
            if (!clicked)
                return;
            int v = c.ButtonPressed ? 1 : 0;
            foreach (int i in Items)
                if (get(i) != v)
                {
                    set(i, v);
                    Changes++;
                }
        });
        return c;
    }

    /// <summary>A field of bits, each a check box (the bits clicked written into each item's value).</summary>
    public void Bits(GridContainer g, string label, (int bit, string name)[] bits, Func<int, int> get, Action<int, int> set, string tip = "")
    {
        g.AddChild(new Label { Text = label, TooltipText = tip, MouseFilter = Control.MouseFilterEnum.Pass });
        var row = new HFlowContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        g.AddChild(row);
        foreach (var (bit, name) in bits)
        {
            int b = bit;
            Check(row, name, i => (get(i) & b) != 0 ? 1 : 0, (i, on) => set(i, on != 0 ? get(i) | b : get(i) & ~b), tip);
        }
    }

    /// <summary>A field described by a FieldSpec (Blood.XFields): a check box, bits, a number with what it means beside
    /// it (red out of its range) and, by kind, a list of its named values, a sprite type list, the channel picker, the
    /// sound picker or a height in the units shown.</summary>
    public void Field(GridContainer g, Container flags, Blood.FieldSpec spec, Func<int, int> get, Action<int, int> set, MainWindow w,
        Blood.Need need = null)
    {
        // a field its type needs: its label coloured (green met, red missing, yellow advice), the need in its help
        int labelAt = g.GetChildCount();
        void Mark(Control c)
        {
            if (need == null || c == null)
                return;
            var col = need.Met ? new Color(0.55f, 0.9f, 0.55f) : need.Advice ? new Color(1f, 0.8f, 0.35f) : new Color(1f, 0.45f, 0.4f);
            c.AddThemeColorOverride("font_color", col);
            c.TooltipText = $"needed: {need.Text}{(need.Met ? "" : " (missing)")}\n{c.TooltipText}";
            if (c.MouseFilter == Control.MouseFilterEnum.Ignore)
                c.MouseFilter = Control.MouseFilterEnum.Pass;
        }
        switch (spec.Kind)
        {
            case Blood.FieldKind.Flag:
                Mark(Check(flags, spec.Label, get, set, spec.Help));
                return;
            case Blood.FieldKind.Bits:
                Bits(g, spec.Label, spec.Choices, get, set, spec.Help);
                Mark(g.GetChild(labelAt) as Label);
                return;
            case Blood.FieldKind.Sound:
                Sound(g, spec.Label, get, set, spec.Caption ?? "", w).TooltipText = spec.Help;
                Mark(g.GetChild(labelAt) as Label);
                return;
            case Blood.FieldKind.Z:
                Z(g, spec.Label, get, set, spec.Tip);
                Mark(g.GetChild(labelAt) as Label);
                return;
        }
        var e = spec.Kind == Blood.FieldKind.Angle
            ? Number(g, spec.Label, get, set, v => v.ToString(CultureInfo.InvariantCulture), ParseAngle, "", spec.Help + "\ntype 90° (or 90d) for degrees")
            : Int(g, spec.Label, get, set, "", spec.Help);
        var row = (HBoxContainer)e.GetParent();
        row.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        var meaning = (Label)row.GetChild(1);
        meaning.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        OptionButton list = null;
        var choices = spec.Kind switch
        {
            Blood.FieldKind.Choice => spec.Choices,
            Blood.FieldKind.Type => Blood.BloodNames.Types.All(Shared.Blood.Types.ObjKind.Sprite).Where(spec.Types ?? (_ => true))
                .Select(t => (t.Type, t.Name)).Prepend((0, "None")).ToArray(),
            _ => null,
        };
        bool setting = false;
        void Show()
        {
            bool num = int.TryParse(e.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v);
            string text = num ? spec.Describe(v) : "";
            // (a channel: who uses it in this map)
            if (num && spec.Kind == Blood.FieldKind.Channel && v > 0 && v >= spec.Min && v <= spec.Max)
                text = (text.Length > 0 ? text + "; " : "") + ChannelPickerDialog.Summary(w.Doc, v);
            // (the list shows the name already)
            if (num && list != null && list.GetItemIndex(v) >= 0 && v >= spec.Min && v <= spec.Max)
                text = "";
            meaning.Text = text;
            meaning.TooltipText = text;
            bool bad = num && (v < spec.Min || v > spec.Max);
            meaning.AddThemeColorOverride("font_color", bad ? new Color(1f, 0.4f, 0.35f) : ForgeTheme.Dim);
            if (list != null && !setting)
            {
                int idx = num ? list.GetItemIndex(v) : -1;
                list.Select(idx >= 0 ? idx : 0);
            }
        }
        if (choices != null)
        {
            list = new OptionButton { CustomMinimumSize = new Vector2(200, 0), FitToLongestItem = false, ClipText = true, TooltipText = spec.Help };
            list.AddItem("(choose)", int.MinValue);
            foreach (var (v, name) in choices)
                if (list.GetItemIndex(v) < 0)
                    list.AddItem($"{v} {name}", v);
            list.ItemSelected += idx =>
            {
                int v = list.GetItemId((int)idx);
                if (v == int.MinValue)
                    return;
                setting = true;
                e.Text = v.ToString(CultureInfo.InvariantCulture);
                e.EmitSignal(LineEdit.SignalName.TextChanged, e.Text);
                setting = false;
            };
            row.AddChild(list);
            row.MoveChild(list, 1);
        }
        // a data field: what it is for this type (Radius1, Volume...)
        if (spec.Caption != null)
        {
            var cap = new Label { Text = spec.Caption, CustomMinimumSize = new Vector2(90, 0), TooltipText = spec.Help, MouseFilter = Control.MouseFilterEnum.Pass };
            cap.AddThemeColorOverride("font_color", ForgeTheme.Dim);
            row.AddChild(cap);
            row.MoveChild(cap, 1);
        }
        if (spec.Kind == Blood.FieldKind.Channel)
        {
            var pick = new Button { Text = "Pick...", TooltipText = "the channels in this map (who sends and listens), the game's own, the next free one" };
            pick.Pressed += () => ChannelPickerDialog.Show(w, pick, int.TryParse(e.Text, out int n) ? n : 0, c =>
            {
                e.Text = c.ToString(CultureInfo.InvariantCulture);
                e.EmitSignal(LineEdit.SignalName.TextChanged, e.Text);
            });
            row.AddChild(pick);
            row.MoveChild(pick, 1);
        }
        e.TextChanged += _ => Show();
        Show();
        Mark(g.GetChild(labelAt) as Label);
    }

    /// <summary>A sound field (a sprite's data holding a sound id): the number with what it means and the sound's
    /// sample and length beside it, Play (again: stop) and Pick... (the sound browser).</summary>
    public LineEdit Sound(GridContainer g, string label, Func<int, int> get, Action<int, int> set, string caption, MainWindow w)
    {
        // one row: the number, what it is for, Play, Pick..., the sample
        var e = Int(g, label, get, set, caption ?? "");
        var row = (HBoxContainer)e.GetParent();
        row.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        var cap = (Label)row.GetChild(1);
        cap.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
        cap.CustomMinimumSize = new Vector2(90, 0);
        var about = new Label { ClipText = true, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(1, 0),
            MouseFilter = Control.MouseFilterEnum.Pass };
        void Show()
        {
            about.Text = int.TryParse(e.Text, out int n) && n > 0 ? Blood.SoundLibrary.Describe(n) ?? "(no such sound)" : "";
            about.TooltipText = about.Text;
        }
        Show();
        e.TextChanged += _ => Show();
        var play = new Button { Text = "Play", TooltipText = "play this sound (again: stop it)", SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        play.Pressed += () =>
        {
            if (Blood.SoundLibrary.Playing >= 0)
                Blood.SoundLibrary.Stop();
            else if (int.TryParse(e.Text, out int n))
                Blood.SoundLibrary.Play(w, n);
        };
        var pick = new Button { Text = "Pick...", TooltipText = "Blood's sounds: listen, find by name or by what Blood's levels use them for",
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        pick.Pressed += () => SoundPickerDialog.Show(w, pick, int.TryParse(e.Text, out int n) ? n : 0, id =>
        {
            e.Text = id.ToString(CultureInfo.InvariantCulture);
            e.EmitSignal(LineEdit.SignalName.TextChanged, e.Text);
        });
        row.AddChild(play);
        row.AddChild(pick);
        row.AddChild(about);
        return e;
    }

    /// <summary>A type field: the number, and the type's name updated as it is typed; names: type -&gt; name.</summary>
    public LineEdit Type(GridContainer g, string label, Func<int, int> get, Action<int, int> set, Func<int, string> name,
        IEnumerable<(int type, string name)> list = null)
    {
        var e = Int(g, label, get, set);
        var n = new Label { ClipText = true, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(1, 0) };
        OptionButton opt = null;
        void Show()
        {
            bool known = int.TryParse(e.Text, out int t);
            n.Text = known ? name(t) : "";
            // the list shows the type typed (or its name when the list hasn't it)
            if (opt != null)
            {
                int idx = known ? opt.GetItemIndex(t) : -1;
                opt.Select(idx >= 0 ? idx : 0);
                n.Visible = idx < 0;
            }
        }
        e.TextChanged += _ => Show();
        g.AddChild(new Control());
        if (list != null)
        {
            var row = new HBoxContainer();
            opt = new OptionButton { CustomMinimumSize = new Vector2(240, 0), FitToLongestItem = false, ClipText = true };
            opt.AddItem("(choose)", int.MaxValue);
            var types = list.ToList();
            // type 0 (none, or a plain decoration) is a choice too
            if (!types.Any(x => x.type == 0))
                opt.AddItem($"0 {name(0)}", 0);
            foreach (var (t, nm) in types)
                opt.AddItem($"{t} {nm}", t);
            Show();
            opt.ItemSelected += idx =>
            {
                int id = opt.GetItemId((int)idx);
                if (id != int.MaxValue)
                {
                    // as if typed: the name, and whoever follows the type (a sprite's picture)
                    e.Text = id.ToString(CultureInfo.InvariantCulture);
                    e.EmitSignal(LineEdit.SignalName.TextChanged, e.Text);
                }
            };
            row.AddChild(opt);
            row.AddChild(n);
            g.AddChild(row);
        }
        else
        {
            Show();
            g.AddChild(n);
        }
        return e;
    }

    public void Apply()
    {
        Changes = 0;
        foreach (var a in _apply)
            a();
    }

    public static GridContainer Grid(Node parent, int columns = 2)
    {
        var g = new GridContainer { Columns = columns };
        g.AddThemeConstantOverride("h_separation", 8);
        g.AddThemeConstantOverride("v_separation", 2);
        parent.AddChild(g);
        return g;
    }

    /// <summary>A section's title in a dialog.</summary>
    public static Label Heading(string text)
    {
        var l = new Label { Text = text };
        l.AddThemeColorOverride("font_color", ForgeTheme.Accent.Lightened(0.3f));
        return l;
    }
}

/// <summary>
/// The tile picker (UDB's texture browser): every tile of the Blood folder's art as a grid of pictures (the map's own
/// marked), or only the map's; found by number (part of one) or a range ("100-200"); the number box and the picture
/// show the choice; double click or Use takes it. Pictures load as they scroll into view. Without a Blood folder: the
/// map's tile numbers.
/// </summary>
/// <summary>
/// The palette picker: Blood's palette lookups (BLOOD.RFF's PLUs in the game's order, pal 0 NORMAL to 14 P4), each
/// shown on the picture the field belongs to, with how many of the map's surfaces and sprites use it. Double-click or
/// Use takes one.
/// </summary>
public static class PalettePickerDialog
{
    const int Icon = 96;

    public static void Show(MainWindow w, Control from, int current, int tile, Action<int> picked)
    {
        var d = new ConfirmationDialog { Title = "Pick a palette", OkButtonText = "Use", MinSize = new Vector2I(760, 520) };
        var box = new VBoxContainer();
        d.AddChild(box);
        box.AddChild(new Label { Text = TileImages.Exists(tile) ? $"Tile {tile} through each palette" : "No picture to show them on (tile " + tile + ")" });
        var list = new ItemList { SizeFlagsVertical = Control.SizeFlags.ExpandFill, MaxColumns = 0, IconMode = ItemList.IconModeEnum.Top,
            FixedIconSize = new Vector2I(Icon, Icon), SameColumnWidth = true, FixedColumnWidth = Icon + 24 };
        box.AddChild(list);
        // how often each pal is used in the map (floors, ceilings, walls, sprites)
        var uses = new Dictionary<int, int>();
        void Use(int p) => uses[p] = uses.GetValueOrDefault(p) + 1;
        foreach (var s in w.Doc.Sectors)
        {
            Use(s.FloorPal);
            Use(s.CeilingPal);
        }
        foreach (var wl in w.Doc.Walls)
            Use(wl.Pal);
        foreach (var sp in w.Doc.Sprites)
            Use(sp.Pal);
        var names = Chosen.Shared.Blood.Files.Extractor.PluNames;
        var pals = Enumerable.Range(0, names.Length).ToList();
        // one the field has that Blood has no lookup for: shown too (drawn as NORMAL)
        if (current >= names.Length)
            pals.Add(current);
        foreach (int p in pals)
        {
            string name = p < names.Length ? names[p] : "(none)";
            int idx = list.AddItem($"{p} {name}", TileImages.Thumb(tile, Icon, p));
            list.SetItemMetadata(idx, p);
            int n = uses.GetValueOrDefault(p);
            list.SetItemTooltip(idx, $"pal {p}: {name}" + (n > 0 ? $", used {n} time(s) in this map" : ", not used in this map")
                + (p >= names.Length ? " (Blood has no lookup for it: drawn as NORMAL)" : ""));
            if (n > 0)
                list.SetItemCustomFgColor(idx, ForgeTheme.Accent.Lightened(0.4f));
            if (p == current)
                list.Select(idx);
        }
        list.EnsureCurrentIsVisible();
        void Take()
        {
            var sel = list.GetSelectedItems();
            if (sel.Length > 0)
                picked((int)list.GetItemMetadata(sel[0]));
        }
        d.Confirmed += Take;
        list.ItemActivated += i =>
        {
            picked((int)list.GetItemMetadata((int)i));
            d.Hide();
            d.QueueFree();
        };
        w.Popup(d, default, from);
    }
}

public static class TilePickerDialog
{
    // the last tab, material and order chosen, while Forge runs (a field of a kind opens on its own tab)
    static Blood.TileTab _tab = Blood.TileTab.All;
    static int _material, _order;

    /// <summary>A saved place ("x,y,w,h") as it can be used now: its size within the screen it is on (MainWindow.ScreenRoom
    /// when not found on one); its position only while its top left (the title bar) lies on a screen's usable part (dialogs
    /// drawn inside the window: within the window); null: nothing saved.</summary>
    static (Vector2I Size, Vector2I? Position)? SavedPlace(MainWindow w, string text)
    {
        var p = text.Split(',');
        if (p.Length != 4 || !p.All(x => int.TryParse(x, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)))
            return null;
        var v = p.Select(x => int.Parse(x, CultureInfo.InvariantCulture)).ToArray();
        var at = new Vector2I(v[0], v[1]);
        var size = new Vector2I(Math.Max(320, v[2]), Math.Max(240, v[3]));
        var room = w.ScreenRoom();
        Vector2I? pos = null;
        if (w.GetViewport().GuiEmbedSubwindows)
        {
            if (new Rect2I(Vector2I.Zero, room).HasPoint(at + new Vector2I(8, 8)))
                pos = at;
        }
        else
            for (int s = 0; s < DisplayServer.GetScreenCount(); s++)
            {
                var usable = DisplayServer.ScreenGetUsableRect(s);
                if (usable.HasPoint(at + new Vector2I(8, 8)))
                {
                    pos = at;
                    room = new Vector2I(Math.Max(320, usable.Size.X - 32), Math.Max(240, usable.Size.Y - 64));
                    break;
                }
            }
        return (new Vector2I(Math.Min(size.X, room.X), Math.Min(size.Y, room.Y)), pos);
    }

    /// <summary>The picker at tile current; picked: the tile used; preview (optional): each tile looked at as it
    /// changes, -1 when the picker closes (the 3D preview shows it on the surfaces; the picker then stands at the
    /// window's right, out of the way of the crosshair); kind: the picture's kind ("wall", "masked", "floor",
    /// "ceiling", "sky", "sprite"), the tab it opens on. Tabs: All, the tiles Blood's own maps use each way
    /// (Blood.TileUse: its tiles have no names), and the tiles in this map; a material (SURFACE.DAT) and an order
    /// (by number, or most used in Blood's maps first) narrow and sort them.</summary>
    public static void Show(MainWindow w, Control from, int current, Action<int> picked, Action<int> preview = null, string kind = null)
    {
        var d = new ConfirmationDialog { Title = "Pick a tile", OkButtonText = "Use",
            MinSize = preview != null ? new Vector2I(640, 640) : new Vector2I(900, 660) };
        var box = new VBoxContainer();
        d.AddChild(box);
        var row = new HBoxContainer();
        box.AddChild(row);
        row.AddChild(new Label { Text = "Tile" });
        var num = new SpinBox { MinValue = 0, MaxValue = Shared.Blood.Files.MapArt.MaxTile, Value = current, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        row.AddChild(num);
        var pic = new TextureRect { CustomMinimumSize = new Vector2(96, 96), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered };
        row.AddChild(pic);
        // (its two lines broken where they are, clipped: a wrapping label asks a height for one word a line before it is
        // laid out, and the dialog grew past the screen)
        var about = new Label { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, ClipText = true,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis, CustomMinimumSize = new Vector2(1, 0) };
        row.AddChild(about);
        var tabs = new TabBar { ClipTabs = false };
        foreach (var t in Enum.GetValues<Blood.TileTab>())
            tabs.AddTab(Blood.TileUse.TabName(t));
        tabs.SetTabTooltip((int)Blood.TileTab.All, "every tile of the Blood folder's art");
        foreach (var t in new[] { Blood.TileTab.Walls, Blood.TileTab.Floors, Blood.TileTab.Ceilings, Blood.TileTab.Masked, Blood.TileTab.Skies, Blood.TileTab.Decorations })
            tabs.SetTabTooltip((int)t, $"the tiles Blood's maps and its add-ons' use as {Blood.TileUse.TabName(t).ToLowerInvariant()} (Blood's tiles have no names)");
        tabs.SetTabTooltip((int)Blood.TileTab.InMap, "the tiles this map uses");
        tabs.SetTabTooltip((int)Blood.TileTab.MapArt, "the tiles of the map's own art (File > Map Options > Art)");
        tabs.SetTabTooltip((int)Blood.TileTab.Unused, "tiles no map of Blood's or its add-ons uses that look like textures (not an animation frame or a sprite's angle)");
        box.AddChild(tabs);
        var filters = new HBoxContainer();
        box.AddChild(filters);
        filters.AddChild(new Label { Text = "Material" });
        var material = new OptionButton { TooltipText = "Blood's surface type of the tile (SURFACE.DAT: its footsteps and hits)" };
        material.AddItem("Any");
        foreach (var m in Blood.TileUse.Materials.Skip(1))
            material.AddItem(m);
        filters.AddChild(material);
        filters.AddChild(new Label { Text = "  Order" });
        var order = new OptionButton { TooltipText = "by number (a set's tiles side by side), or the ones Blood's maps use most (this tab's way) first" };
        order.AddItem("By number");
        order.AddItem("Most used first");
        filters.AddChild(order);
        filters.AddChild(new Label { Text = "  Find" });
        var find = new LineEdit { PlaceholderText = "a number (or part of one), or a range: 100-200", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, ClearButtonEnabled = true };
        filters.AddChild(find);
        var count = new Label();
        filters.AddChild(count);
        var list = new ItemList { SizeFlagsVertical = Control.SizeFlags.ExpandFill, MaxColumns = 0, IconMode = ItemList.IconModeEnum.Top, SameColumnWidth = true };
        // the pictures, loaded as they come into view; their size the slider's or Ctrl+wheel's
        var grid = new ThumbGrid(list, t => t, w.Config, "picker.thumb", 64, 16);
        filters.AddChild(new Label { Text = "  Size" });
        filters.AddChild(grid.Slider);
        box.AddChild(list);

        var used = new SortedSet<int>();
        var doc = w.Doc;
        foreach (var s in doc.Sectors)
        {
            used.Add(s.FloorPicnum);
            used.Add(s.CeilingPicnum);
        }
        foreach (var wl in doc.Walls)
        {
            used.Add(wl.Picnum);
            if (wl.OverPicnum > 0)
                used.Add(wl.OverPicnum);
        }
        foreach (var sp in doc.Sprites)
            used.Add(sp.Picnum);
        var all = ForgeData.Ready ? ForgeData.Tiles.Numbers.Where(TileImages.Exists).OrderBy(n => n).ToList() : new List<int>();
        var tab = kind != null ? Blood.TileUse.TabFor(kind) : _tab;
        // (no art: only the map's own numbers)
        if (all.Count == 0)
            tab = Blood.TileTab.InMap;
        tabs.CurrentTab = (int)tab;
        material.Selected = _material;
        material.Disabled = ForgeData.Surface.Length == 0;
        order.Selected = _order;

        string Tip(int t)
        {
            var size = TileImages.Size(t);
            var parts = new List<string> { size.X > 0 ? $"{t}: {size.X} x {size.Y}" : t.ToString(CultureInfo.InvariantCulture) };
            if (Blood.TileUse.Material(t) is int m and > 0)
                parts.Add(Blood.TileUse.Materials[m].ToLowerInvariant());
            if (used.Contains(t))
                parts.Add("used in this map");
            string uses = Blood.TileUse.Describe(t);
            return string.Join(", ", parts) + (uses.Length > 0 ? "\n" + uses : "");
        }

        void Describe(int t)
        {
            pic.Texture = TileImages.Get(t);
            about.Text = TileImages.Size(t).X > 0 ? "  " + Tip(t).Replace("\n", "\n  ") : "  (no picture)";
        }

        bool Matches(int t, string f)
        {
            if (f.Length == 0)
                return true;
            int dash = f.IndexOf('-');
            if (dash > 0 && int.TryParse(f[..dash].Trim(), out int lo) && int.TryParse(f[(dash + 1)..].Trim(), out int hi))
                return t >= lo && t <= hi;
            return t.ToString(CultureInfo.InvariantCulture).Contains(f);
        }

        IEnumerable<int> Tiles()
        {
            var tb = (Blood.TileTab)tabs.CurrentTab;
            IEnumerable<int> src = tb switch
            {
                Blood.TileTab.All => all,
                Blood.TileTab.InMap => used,
                Blood.TileTab.MapArt => all.Where(ForgeData.MapTiles.Contains),
                Blood.TileTab.Unused => all.Where(Blood.TileUse.IsUnused),
                _ => Blood.TileUse.All.Keys.Where(t => Blood.TileUse.Count(t, tb) > 0 && (all.Count == 0 || TileImages.Exists(t))).OrderBy(t => t),
            };
            if (material.Selected > 0)
                src = src.Where(t => Blood.TileUse.Material(t) == material.Selected);
            if (order.Selected == 1)
                src = src.OrderByDescending(t => Blood.TileUse.Count(t, tb == Blood.TileTab.InMap ? Blood.TileTab.All : tb)).ThenBy(t => t);
            return src;
        }

        bool filling = false;
        void Fill()
        {
            filling = true;
            list.Clear();
            grid.Reset();
            string f = find.Text.Trim();
            var tiles = Tiles().Where(t => Matches(t, f));
            int select = -1;
            foreach (int t in tiles)
            {
                int idx = list.AddItem(t.ToString(CultureInfo.InvariantCulture), grid.Blank);
                list.SetItemMetadata(idx, t);
                list.SetItemTooltip(idx, Tip(t));
                if (tabs.CurrentTab != (int)Blood.TileTab.InMap && used.Contains(t))
                    list.SetItemCustomFgColor(idx, ForgeTheme.Accent.Lightened(0.4f));
                if (t == (int)num.Value)
                    select = idx;
            }
            count.Text = $"  {list.ItemCount} tiles";
            if (select >= 0)
            {
                list.Select(select);
                list.EnsureCurrentIsVisible();
            }
            else
                list.GetVScrollBar().Value = 0;
            filling = false;
        }

        var tree = w.GetTree();
        Action tick = grid.Tick;
        tree.ProcessFrame += tick;
        d.TreeExiting += () => tree.ProcessFrame -= tick;

        num.ValueChanged += v =>
        {
            Describe((int)v);
            preview?.Invoke((int)v);
            if (filling)
                return;
            for (int i = 0; i < list.ItemCount; i++)
                if ((int)list.GetItemMetadata(i) == (int)v)
                {
                    list.Select(i);
                    list.EnsureCurrentIsVisible();
                    return;
                }
            list.DeselectAll();
        };
        tabs.TabChanged += i =>
        {
            _tab = (Blood.TileTab)(int)i;
            Fill();
        };
        material.ItemSelected += i =>
        {
            _material = (int)i;
            Fill();
        };
        order.ItemSelected += i =>
        {
            _order = (int)i;
            Fill();
        };
        find.TextChanged += _ => Fill();
        list.ItemSelected += idx =>
        {
            if (!filling)
                num.Value = (int)list.GetItemMetadata((int)idx);
        };
        list.ItemActivated += idx =>
        {
            picked((int)list.GetItemMetadata((int)idx));
            d.Hide();
            d.QueueFree();
        };
        d.Confirmed += () => picked((int)num.Value);
        if (preview != null)
            d.VisibilityChanged += () =>
            {
                if (!d.Visible)
                    preview(-1);
            };
        Describe(current);
        Fill();
        w.Popup(d, default, from);
        // where it was and how big when last closed (the 3D preview's picker its own: it stands out of the crosshair's way)
        string placeKey = preview != null ? "picker.rect3d" : "picker.rect";
        bool remember = !w.Replaying && !Editing.ForgeInput.NoInput && DisplayServer.GetName() != "headless";
        var saved = remember ? SavedPlace(w, w.Config.Get(placeKey)) : null;
        if (saved is { } place)
        {
            d.Size = place.Size;
            if (place.Position is { } at)
                d.Position = at;
        }
        if (remember)
            d.VisibilityChanged += () =>
            {
                if (!d.Visible)
                {
                    w.Config.Set(placeKey, $"{d.Position.X},{d.Position.Y},{d.Size.X},{d.Size.Y}");
                    w.Config.Save();
                }
            };
        // the current tile in view again once the dialog has its size (scrolled to before, its rows were others)
        w.GetTree().CreateTimer(0.05).Timeout += () =>
        {
            if (GodotObject.IsInstanceValid(list))
                list.EnsureCurrentIsVisible();
        };
        if (preview != null && !w.Replaying && saved?.Position == null)
            Callable.From(() =>
            {
                if (GodotObject.IsInstanceValid(d))
                    d.Position = w.PopupPosition(new Vector2(w.GetViewport().GetVisibleRect().Size.X - d.Size.X - 16, 60));
            }).CallDeferred();
    }
}
