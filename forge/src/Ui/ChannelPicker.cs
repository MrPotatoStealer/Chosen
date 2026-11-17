// Chosen core: original code (a channel picker: the map's channels with who listens and sends, the game's own).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Chosen.Forge.Blood;
using Godot;

namespace Chosen.Forge.Ui;

/// <summary>
/// An RX or TX field's Pick...: the channels this map uses, each with what listens to it (RX) and what sends on it
/// (TX), and the game's own (level start, secrets, end level); Next free gives the first channel from 100 nothing
/// uses. Double click or Use takes one.
/// </summary>
public static class ChannelPickerDialog
{
    /// <summary>Who uses a channel in a map, in a few words: "2 listen, 1 sends" (or "nothing else uses it").</summary>
    public static string Summary(Document.MapDocument doc, int c)
    {
        int r = 0, t = 0;
        foreach (var s in doc.Sectors)
            if (s.Xs is { } x)
            {
                r += x.RxID == c ? 1 : 0;
                t += x.TxID == c ? 1 : 0;
            }
        foreach (var wl in doc.Walls)
            if (wl.Xw is { } x)
            {
                r += x.RxID == c ? 1 : 0;
                t += x.TxID == c ? 1 : 0;
            }
        foreach (var sp in doc.Sprites)
            if (sp.Xsp is { } x)
            {
                r += x.RxID == c ? 1 : 0;
                t += x.TxID == c ? 1 : 0;
                foreach (int dc in Blood.ModernTypes.DataChannels(sp))
                    t += dc == c ? 1 : 0;
            }
        return $"{r} listen, {t} send" + (r == 0 || t == 0 ? " (counting this one)" : "");
    }

    public static void Show(MainWindow w, Control from, int current, Action<int> picked)
    {
        var doc = w.Doc;
        var rx = new Dictionary<int, List<string>>();
        var tx = new Dictionary<int, List<string>>();
        void Add(Dictionary<int, List<string>> d, int c, string who)
        {
            if (c <= 0)
                return;
            if (!d.TryGetValue(c, out var l))
                d[c] = l = new List<string>();
            l.Add(who);
        }
        for (int s = 0; s < doc.Sectors.Count; s++)
            if (doc.Sectors[s].Xs is { } x)
            {
                string who = $"sector {s}" + (doc.Sectors[s].Type != 0 ? $" ({BloodNames.Sector(doc.Sectors[s].Type)})" : "");
                Add(rx, x.RxID, who);
                Add(tx, x.TxID, who);
            }
        for (int i = 0; i < doc.Walls.Count; i++)
            if (doc.Walls[i].Xw is { } x)
            {
                string who = $"wall {i}" + (doc.Walls[i].Type != 0 ? $" ({BloodNames.Wall(doc.Walls[i].Type)})" : "");
                Add(rx, x.RxID, who);
                Add(tx, x.TxID, who);
            }
        for (int i = 0; i < doc.Sprites.Count; i++)
            if (doc.Sprites[i].Xsp is { } x)
            {
                string who = $"sprite {i} ({BloodNames.Sprite(doc.Sprites[i].Type)})";
                Add(rx, x.RxID, who);
                Add(tx, x.TxID, who);
                foreach (int dc in Blood.ModernTypes.DataChannels(doc.Sprites[i]))
                    Add(tx, dc, who + " (a data field)");
            }
        int NextFree()
        {
            for (int c = 100; c < 1024; c++)
                if (!rx.ContainsKey(c) && !tx.ContainsKey(c))
                    return c;
            return 0;
        }

        var d = new ConfirmationDialog { Title = "Pick a channel", OkButtonText = "Use", MinSize = new Vector2I(820, 560) };
        var box = new VBoxContainer();
        d.AddChild(box);
        var row = new HBoxContainer();
        box.AddChild(row);
        row.AddChild(new Label { Text = "Channel" });
        var num = new SpinBox { MinValue = 0, MaxValue = 1023, Value = current, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        row.AddChild(num);
        var free = new Button { Text = $"Next free ({NextFree()})", TooltipText = "the first channel from 100 that nothing in this map uses" };
        row.AddChild(free);
        var about = new Label { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, ClipText = true, CustomMinimumSize = new Vector2(1, 0) };
        row.AddChild(about);
        var filters = new HBoxContainer();
        box.AddChild(filters);
        filters.AddChild(new Label { Text = "Find" });
        var find = new LineEdit { PlaceholderText = "a channel, or what uses it: sector 12, switch, door", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            ClearButtonEnabled = true };
        filters.AddChild(find);

        var tree = new Tree { SizeFlagsVertical = Control.SizeFlags.ExpandFill, Columns = 3, ColumnTitlesVisible = true, HideRoot = true,
            SelectMode = Tree.SelectModeEnum.Row };
        tree.SetColumnTitle(0, "Channel");
        tree.SetColumnTitle(1, "Listens (RX)");
        tree.SetColumnTitle(2, "Sends (TX)");
        tree.SetColumnExpand(0, false);
        tree.SetColumnCustomMinimumWidth(0, 220);
        box.AddChild(tree);

        string Name(int c) => XFields.ReservedChannel(c) is { } r ? $"{c}  {r}" : c.ToString(CultureInfo.InvariantCulture);
        string List(Dictionary<int, List<string>> dd, int c) => dd.TryGetValue(c, out var l)
            ? (l.Count <= 4 ? string.Join(", ", l) : $"{string.Join(", ", l.Take(4))} +{l.Count - 4}") : "";

        void Describe(int c)
        {
            int r = rx.TryGetValue(c, out var a) ? a.Count : 0, t = tx.TryGetValue(c, out var b) ? b.Count : 0;
            about.Text = $"  {(XFields.ReservedChannel(c) is { } res ? res + "; " : "")}" + (r + t == 0 ? "unused in this map" : $"{r} listen, {t} send");
        }

        bool filling = false;
        void Fill()
        {
            filling = true;
            tree.Clear();
            var root = tree.CreateItem();
            string f = find.Text.Trim();
            var channels = new SortedSet<int>(rx.Keys.Concat(tx.Keys));
            // the game's own channels too (but "reserved by the game", unnamed)
            for (int c = 1; c < 100; c++)
                if (XFields.ReservedChannel(c) is { } r && r != "reserved by the game")
                    channels.Add(c);
            TreeItem select = null;
            foreach (int c in channels)
            {
                string a = List(rx, c), b = List(tx, c), name = Name(c);
                if (f.Length > 0 && !name.Contains(f, StringComparison.OrdinalIgnoreCase) && !a.Contains(f, StringComparison.OrdinalIgnoreCase)
                    && !b.Contains(f, StringComparison.OrdinalIgnoreCase))
                    continue;
                var it = tree.CreateItem(root);
                it.SetText(0, name);
                it.SetText(1, a);
                it.SetTooltipText(1, rx.TryGetValue(c, out var la) ? string.Join("\n", la) : "");
                it.SetText(2, b);
                it.SetTooltipText(2, tx.TryGetValue(c, out var lb) ? string.Join("\n", lb) : "");
                it.SetMetadata(0, c);
                if (c < 100)
                    for (int col = 0; col < 3; col++)
                        it.SetCustomColor(col, ForgeTheme.Dim);
                else if (!rx.ContainsKey(c) || !tx.ContainsKey(c))
                    // (sent and nothing listens, or listened to and nothing sends)
                    it.SetCustomColor(0, new Color(1f, 0.7f, 0.35f));
                if (c == (int)num.Value)
                    select = it;
            }
            if (select != null)
            {
                select.Select(0);
                tree.ScrollToItem(select);
            }
            filling = false;
        }

        num.ValueChanged += v => Describe((int)v);
        free.Pressed += () => num.Value = NextFree();
        find.TextChanged += _ => Fill();
        tree.ItemSelected += () =>
        {
            if (!filling && tree.GetSelected() is { } it)
                num.Value = (int)it.GetMetadata(0);
        };
        tree.ItemActivated += () =>
        {
            if (tree.GetSelected() is not { } it)
                return;
            picked((int)it.GetMetadata(0));
            d.Hide();
            d.QueueFree();
        };
        d.Confirmed += () => picked((int)num.Value);
        Describe(current);
        Fill();
        w.Popup(d, default, from);
    }
}
