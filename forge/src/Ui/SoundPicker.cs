// Chosen core: original code (a sound browser in the manner of the tile picker).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Chosen.Forge.Blood;
using Godot;

namespace Chosen.Forge.Ui;

/// <summary>
/// Blood's sounds to listen to and pick (Tools > Sounds, a sound field's Pick...): every id with its sample, length,
/// loop and what Blood's own levels use it for (and this map), found by number, range ("100-200"), name or use
/// ("door", "switch", "E1M1"); a sound chosen plays (Play on select), double click or Use takes it.
/// </summary>
public static class SoundPickerDialog
{
    static bool _autoPlay = true;
    static int _show;      // All / In this map / In Blood's levels, while Forge runs

    /// <summary>The browser at sound current; picked: the id used (null: only browsing, the button closes it).</summary>
    public static void Show(MainWindow w, Control from, int current, Action<int> picked)
    {
        var d = new ConfirmationDialog { Title = picked != null ? "Pick a sound" : "Sounds", OkButtonText = picked != null ? "Use" : "Close",
            MinSize = new Vector2I(900, 640) };
        if (picked == null)
            d.GetCancelButton().Visible = false;
        var box = new VBoxContainer();
        d.AddChild(box);

        var row = new HBoxContainer();
        box.AddChild(row);
        row.AddChild(new Label { Text = "Sound" });
        var num = new SpinBox { MinValue = 0, MaxValue = 65535, Value = current, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        row.AddChild(num);
        var play = new Button { Text = "Play", TooltipText = "play the sound (again: stop it)" };
        row.AddChild(play);
        var auto = new CheckBox { Text = "Play on select", ButtonPressed = _autoPlay };
        row.AddChild(auto);
        var about = new Label { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, ClipText = true, CustomMinimumSize = new Vector2(1, 0) };
        row.AddChild(about);

        var filters = new HBoxContainer();
        box.AddChild(filters);
        filters.AddChild(new Label { Text = "Show" });
        var show = new OptionButton();
        show.AddItem("All sounds");
        show.AddItem("In this map");
        show.AddItem("In Blood's levels");
        show.Selected = _show;
        filters.AddChild(show);
        filters.AddChild(new Label { Text = "  Find" });
        var find = new LineEdit { PlaceholderText = "a number, a range (100-200), a name or a use: door, switch, ambient, E1M1",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, ClearButtonEnabled = true };
        filters.AddChild(find);
        var count = new Label();
        filters.AddChild(count);

        var tree = new Tree { SizeFlagsVertical = Control.SizeFlags.ExpandFill, Columns = 4, ColumnTitlesVisible = true, HideRoot = true,
            SelectMode = Tree.SelectModeEnum.Row };
        string[] titles = { "Id", "Sample", "Length", "Used for" };
        for (int c = 0; c < 4; c++)
            tree.SetColumnTitle(c, titles[c]);
        tree.SetColumnExpand(0, false);
        tree.SetColumnCustomMinimumWidth(0, 70);
        tree.SetColumnExpand(1, false);
        tree.SetColumnCustomMinimumWidth(1, 130);
        tree.SetColumnExpand(2, false);
        tree.SetColumnCustomMinimumWidth(2, 90);
        tree.SetColumnExpand(3, true);
        box.AddChild(tree);
        var note = new Label { Text = SoundLibrary.All.Count == 0 ? "No sounds: Edit > Preferences sets the Blood folder (its SOUNDS.RFF)." : "" };
        note.AddThemeColorOverride("font_color", ForgeTheme.Dim);
        box.AddChild(note);

        // this map's uses (what for)
        var here = new Dictionary<int, SortedSet<string>>();
        foreach (var (id, what) in SoundLibrary.UsesIn(w.Doc.Sprites))
        {
            if (!here.TryGetValue(id, out var set))
                here[id] = set = new SortedSet<string>();
            set.Add(what);
        }

        string Used(int id)
        {
            var parts = new List<string>();
            if (here.TryGetValue(id, out var h))
                parts.Add("this map: " + string.Join(", ", h));
            if (SoundLibrary.LevelUses is { } lv && lv.TryGetValue(id, out var u))
                parts.Add(u.ToString());
            return string.Join("  |  ", parts);
        }

        void Describe(int id)
        {
            var text = SoundLibrary.Describe(id);
            about.Text = text == null ? (id == 0 ? "  (none)" : "  (no such sound)") : $"  {text}";
            about.TooltipText = Used(id);
        }

        bool Matches(int id, string f)
        {
            if (f.Length == 0)
                return true;
            int dash = f.IndexOf('-');
            if (dash > 0 && int.TryParse(f[..dash].Trim(), out int lo) && int.TryParse(f[(dash + 1)..].Trim(), out int hi))
                return id >= lo && id <= hi;
            if (id.ToString(CultureInfo.InvariantCulture).Contains(f))
                return true;
            var s = SoundLibrary.Get(id);
            return (s != null && s.Name.Contains(f, StringComparison.OrdinalIgnoreCase)) || Used(id).Contains(f, StringComparison.OrdinalIgnoreCase);
        }

        bool filling = false, usesShown = false;
        void Fill()
        {
            filling = true;
            tree.Clear();
            var root = tree.CreateItem();
            string f = find.Text.Trim();
            usesShown = SoundLibrary.LevelUses != null;
            IEnumerable<int> ids = SoundLibrary.All.Keys;
            if (show.Selected == 1)
                ids = ids.Where(here.ContainsKey);
            else if (show.Selected == 2)
                ids = ids.Where(id => SoundLibrary.LevelUses?.ContainsKey(id) == true);
            int n = 0;
            TreeItem select = null;
            foreach (int id in ids.Where(id => Matches(id, f)))
            {
                var s = SoundLibrary.Get(id);
                var it = tree.CreateItem(root);
                it.SetText(0, id.ToString(CultureInfo.InvariantCulture));
                it.SetText(1, s.Name);
                it.SetText(2, s.Seconds.ToString("0.0", CultureInfo.InvariantCulture) + " s" + (s.Loops ? " loop" : ""));
                string used = Used(id);
                it.SetText(3, used);
                it.SetTooltipText(3, used);
                it.SetMetadata(0, id);
                if (here.ContainsKey(id))
                    for (int c = 0; c < 4; c++)
                        it.SetCustomColor(c, ForgeTheme.Accent.Lightened(0.4f));
                if (id == (int)num.Value)
                    select = it;
                n++;
            }
            count.Text = $"  {n} sounds" + (usesShown ? "" : " (reading Blood's levels...)");
            if (select != null)
            {
                select.Select(0);
                tree.ScrollToItem(select);
            }
            filling = false;
        }

        void PlayIt(int id)
        {
            if (SoundLibrary.Play(w, id))
                play.Text = "Stop";
        }

        // the levels' uses arrive from the background: the list filled again once; the Play button back when done
        var sceneTree = w.GetTree();
        Action tick = () =>
        {
            if (!usesShown && SoundLibrary.LevelUses != null && GodotObject.IsInstanceValid(tree))
            {
                var keep = (int)num.Value;
                Fill();
                Describe(keep);
            }
            if (GodotObject.IsInstanceValid(play) && SoundLibrary.Playing < 0)
                play.Text = "Play";
        };
        sceneTree.ProcessFrame += tick;
        d.TreeExiting += () =>
        {
            sceneTree.ProcessFrame -= tick;
            SoundLibrary.Stop();
        };

        play.Pressed += () =>
        {
            if (SoundLibrary.Playing >= 0)
                SoundLibrary.Stop();
            else
                PlayIt((int)num.Value);
        };
        auto.Toggled += on => _autoPlay = on;
        num.ValueChanged += v => Describe((int)v);
        show.ItemSelected += i =>
        {
            _show = (int)i;
            Fill();
        };
        find.TextChanged += _ => Fill();
        tree.ItemSelected += () =>
        {
            if (filling || tree.GetSelected() is not { } it)
                return;
            int id = (int)it.GetMetadata(0);
            num.Value = id;
            if (auto.ButtonPressed)
                PlayIt(id);
        };
        tree.ItemActivated += () =>
        {
            if (tree.GetSelected() is not { } it)
                return;
            if (picked == null)
            {
                PlayIt((int)it.GetMetadata(0));
                return;
            }
            picked((int)it.GetMetadata(0));
            d.Hide();
            d.QueueFree();
        };
        d.Confirmed += () => picked?.Invoke((int)num.Value);
        Describe(current);
        Fill();
        w.Popup(d, default, from);
        find.CallDeferred(Control.MethodName.GrabFocus);
    }
}
