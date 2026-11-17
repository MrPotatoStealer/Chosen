// Chosen core: original code.
using System;
using System.Globalization;
using System.Linq;
using Chosen.Forge.Config;
using Godot;

namespace Chosen.Forge.Ui;

/// <summary>Small helpers for the dialogs' forms: a grid of label and field rows.</summary>
public static class Form
{
    public static GridContainer Grid(Node parent)
    {
        var g = new GridContainer { Columns = 2 };
        g.AddThemeConstantOverride("h_separation", 12);
        parent.AddChild(g);
        return g;
    }

    public static T Row<T>(GridContainer g, string label, T field) where T : Control
    {
        g.AddChild(new Label { Text = label });
        field.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        field.CustomMinimumSize = new Vector2(220, 0);
        g.AddChild(field);
        return field;
    }

    public static SpinBox Number(GridContainer g, string label, double value, double min, double max, bool editable = true)
    {
        var s = new SpinBox { MinValue = min, MaxValue = max, Step = 1, Value = value, Editable = editable, AllowGreater = false, AllowLesser = false };
        return Row(g, label, s);
    }

    public static LineEdit Text(GridContainer g, string label, string value, bool editable = true) =>
        Row(g, label, new LineEdit { Text = value ?? "", Editable = editable });

    public static Label Info(GridContainer g, string label, string value) => Row(g, label, new Label { Text = value });
}

/// <summary>UDB's Map Options: the map header (the start position is set by the player start sprite, shown here only),
/// and the .MAPX's title, author and comment.</summary>
public static partial class MapOptionsDialog
{
    public static void Show(MainWindow w)
    {
        var doc = w.Doc;
        var m = doc.Map;
        var d = new ConfirmationDialog { Title = "Map Options", MinSize = new Vector2I(520, 0) };
        MainWindow.ApplyOnClose(w, d);
        var box = new VBoxContainer();
        d.AddChild(box);
        var g = Form.Grid(box);
        Form.Info(g, "File", doc.Path == "" ? "(not saved yet)" : doc.Path);
        Form.Info(g, "Format", doc.Format == Document.MapFormat.Map ? "Blood .MAP" : "Chosen .MAPX");
        Form.Info(g, "Map version", "0x" + m.Version.ToString("x", CultureInfo.InvariantCulture) + (m.HeaderEncrypted ? ", encrypted header" : ""));
        Form.Info(g, "Revision", m.MapRev.ToString(CultureInfo.InvariantCulture));
        // NoOne's modern features (NBlood, Chosen): asked for by the map's version, 7.1 (xmapedit's way)
        var modern = new CheckBox
        {
            Text = "NBlood's modern features (map version 7.1)", ButtonPressed = Blood.ModernTypes.IsModern(m), Disabled = !m.V7,
            TooltipText = "NoOne's extensions: the modern sprite types (conditions, changers, generators, custom dudes, patrols), the extra x-sprite fields. "
                + "NBlood and Chosen turn them on for a map of version 7.1; Blood itself and Refreshed Supply don't know them",
        };
        modern.SetMeta(Recording.SessionRecorder.LateField, true);
        Form.Row(g, "Modern map", modern);
        Form.Info(g, "Start", $"{Units.Xy(m.StartX)}, {Units.Xy(m.StartY)}, {Units.Z(m.StartZ)}, angle {m.StartAng}, sector {m.StartSect}");
        var vis = Form.Number(g, "Visibility", m.Visibility, 0, 65535);
        var song = Form.Number(g, "Song id", m.SongId, int.MinValue, int.MaxValue);
        var par = Form.Number(g, "Parallax type", m.ParallaxType, 0, 255);
        var sky = Form.Number(g, "Sky tiles (bits)", m.PSkyBits, 0, 8);
        var skyOfs = Form.Text(g, "Sky offsets", string.Join(", ", m.SkyOfs));
        skyOfs.TooltipText = "the tile offset of each of the parallax sky's 2^bits pieces, comma separated";
        var title = Form.Text(g, "Title", doc.Extras.GetInfo("title"));
        var author = Form.Text(g, "Author", doc.Extras.GetInfo("author"));
        var comment = Form.Text(g, "Comment", doc.Extras.GetInfo("comment"));
        box.AddChild(new Label { Text = "Title, author and comment are kept in a .MAPX only.", Modulate = new Color(1, 1, 1, 0.6f) });
        ArtSection(w, box);
        // (closed: applied, ApplyOnClose; an older session's cancel replayed: as it was then)
        d.Canceled += () =>
        {
            if (w.Replaying)
                w.Rec?.DialogCanceled(d, "Map options");
        };
        d.Confirmed += () =>
        {
            w.Rec?.DialogConfirmed(d, "Map options");
            w.Undo.CreateUndo("Map options");
            if (m.V7 && modern.ButtonPressed != Blood.ModernTypes.IsModern(m))
                m.Version = (m.Version & 0xff00) | (modern.ButtonPressed ? 1 : 0);
            m.Visibility = (int)vis.Value;
            m.SongId = (int)song.Value;
            m.ParallaxType = (int)par.Value;
            int bits = (int)sky.Value;
            // one offset a sky piece: those typed, the rest 0
            var typed = skyOfs.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(t => short.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out short v) ? v : (short)0).ToArray();
            var ofs = new short[1 << bits];
            Array.Copy(typed, ofs, Math.Min(ofs.Length, typed.Length));
            m.SkyOfs = ofs;
            m.PSkyBits = bits;
            doc.Extras.SetInfo("title", title.Text == "" ? null : title.Text);
            doc.Extras.SetInfo("author", author.Text == "" ? null : author.Text);
            doc.Extras.SetInfo("comment", comment.Text == "" ? null : comment.Text);
            doc.NotifyChanged();
            w.UndoChanged();
        };
        w.Popup(d);
    }
}

/// <summary>Map Options' Art: the map's own ART files over Blood's (MainWindow.Art.cs: each button a recorded "mapart"
/// command, done at once, an undo step; not waiting for Apply).</summary>
public static partial class MapOptionsDialog
{
    static void ArtSection(MainWindow w, VBoxContainer box)
    {
        box.AddChild(new HSeparator());
        box.AddChild(new Label { Text = "Art: ART files (or folders of them) over Blood's tiles, later ones over earlier" });
        var list = new ItemList { CustomMinimumSize = new Vector2(0, 110), SelectMode = ItemList.SelectModeEnum.Single };
        box.AddChild(list);
        var says = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(480, 0), Modulate = new Color(1, 1, 1, 0.75f) };
        int Selected() => list.GetSelectedItems() is { Length: > 0 } s ? s[0] : -1;
        void Fill(int select = -1)
        {
            list.Clear();
            var doc = w.Doc;
            foreach (var a in doc.Extras.Art)
            {
                string full = Shared.Blood.Files.MapArt.Resolve(doc.Folder, a.Path);
                int files = Shared.Blood.Files.MapArt.Files(full).Count;
                list.AddItem($"{a.Path}   {(a.Override ? "override (replaces Blood's files of the same names)" : "added (its tiles over Blood's)")}"
                    + (files == 0 ? "   MISSING" : $"   {files} file{(files == 1 ? "" : "s")}"));
            }
            if (select >= 0 && select < list.ItemCount)
                list.Select(select);
            says.Text = (doc.Extras.Art.Count == 0 ? "Blood's art only." : $"{Data.ForgeData.MapTiles.Count} tiles of the map's own (the tile picker's Map's Art tab).")
                + (Data.ForgeData.MapArtProblems.Count > 0 ? " " + string.Join("; ", Data.ForgeData.MapArtProblems) : "")
                + (doc.Format == Document.MapFormat.Map ? " A .MAP doesn't keep this list (save as .MAPX): ship the ART files with it." : "");
        }
        void Run(string cmd, int select = -1)
        {
            w.UiDo(cmd);
            Fill(select);
        }
        var buttons = new HFlowContainer();
        box.AddChild(buttons);
        Button B(string text, string tip, Action a)
        {
            var b = new Button { Text = text, TooltipText = tip };
            b.Pressed += a;
            buttons.AddChild(b);
            return b;
        }
        void Browse(bool folder, Action<string> picked)
        {
            var fd = new FileDialog
            {
                FileMode = folder ? FileDialog.FileModeEnum.OpenDir : FileDialog.FileModeEnum.OpenFile,
                Access = FileDialog.AccessEnum.Filesystem, UseNativeDialog = true, Title = folder ? "A folder of ART files" : "An ART file",
            };
            if (!folder)
                fd.Filters = new[] { "*.art ; ART files" };
            if (w.Doc.Folder != "")
                fd.CurrentDir = w.Doc.Folder;
            fd.FileSelected += p => picked(p);
            fd.DirSelected += p => picked(p);
            w.AddChild(fd);
            fd.PopupCentered(new Vector2I(800, 500));
        }
        B("Add ART File...", "an ART file whose tiles go over Blood's (Override: it replaces Blood's file of the same name)",
            () => Browse(false, p => Run("mapart add add " + p, w.Doc.Extras.Art.Count)));
        B("Add Folder...", "every ART file in a folder (an add-on's art: Death Wish's, set it to Override)",
            () => Browse(true, p => Run("mapart add add " + p, w.Doc.Extras.Art.Count)));
        B("Pack Pictures...", "PNG, JPEG, BMP, TGA or WebP pictures packed into ART files (Blood's palette), added as a set",
            () => PackDialog(w, () => Fill(w.Doc.Extras.Art.Count - 1)));
        B("Override / Added", "override: its files replace Blood's of the same names whole (an add-on's art); added: its tiles go over Blood's one by one",
            () =>
            {
                int i = Selected();
                if (i >= 0)
                    Run($"mapart kind {i} {(w.Doc.Extras.Art[i].Override ? "add" : "override")}", i);
            });
        B("Up", "earlier: under the sets after it", () =>
        {
            int i = Selected();
            if (i > 0)
                Run($"mapart move {i} -1", i - 1);
        });
        B("Down", "later: over the sets before it", () =>
        {
            int i = Selected();
            if (i >= 0 && i < w.Doc.Extras.Art.Count - 1)
                Run($"mapart move {i} 1", i + 1);
        });
        B("Remove", "the set out of the list (its files stay)", () =>
        {
            int i = Selected();
            if (i >= 0)
                Run($"mapart remove {i}");
        });
        B("Reload", "the ART files read again (changed outside Forge)", () => Run("mapart reload", Selected()));
        box.AddChild(says);
        Fill();
    }

    /// <summary>Pack Pictures: a folder of pictures, the folder for the ART files (beside the map: other maps can add
    /// it too), the numbers (free tiles in Blood's range for a .MAP, past it for a .MAPX).</summary>
    static void PackDialog(MainWindow w, Action done)
    {
        var doc = w.Doc;
        var d = new ConfirmationDialog { Title = "Pack Pictures into ART Files", OkButtonText = "Pack", MinSize = new Vector2I(620, 0) };
        var box = new VBoxContainer();
        d.AddChild(box);
        var g = Form.Grid(box);
        LineEdit FolderRow(string label, string value)
        {
            g.AddChild(new Label { Text = label });
            var row = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            var e = new LineEdit { Text = value, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(380, 0) };
            row.AddChild(e);
            var b = new Button { Text = "Browse..." };
            b.Pressed += () =>
            {
                var fd = new FileDialog { FileMode = FileDialog.FileModeEnum.OpenDir, Access = FileDialog.AccessEnum.Filesystem, UseNativeDialog = true };
                if (e.Text != "" && System.IO.Directory.Exists(e.Text))
                    fd.CurrentDir = e.Text;
                else if (doc.Folder != "")
                    fd.CurrentDir = doc.Folder;
                fd.DirSelected += p => e.Text = p;
                w.AddChild(fd);
                fd.PopupCentered(new Vector2I(800, 500));
            };
            row.AddChild(b);
            g.AddChild(row);
            return e;
        }
        var pics = FolderRow("Pictures", "");
        var outDir = FolderRow("ART files into", doc.Folder != "" ? System.IO.Path.Combine(doc.Folder, "art") : "");
        // (the out folder follows the pictures' name while not typed)
        bool typed = false;
        outDir.TextChanged += _ => typed = true;
        pics.TextChanged += t =>
        {
            if (!typed && t.Trim() != "")
            {
                string name = System.IO.Path.GetFileName(t.TrimEnd('\\', '/'));
                string baseDir = doc.Folder != "" ? doc.Folder : System.IO.Path.GetDirectoryName(t.TrimEnd('\\', '/')) ?? "";
                outDir.SetBlockSignals(true);
                outDir.Text = System.IO.Path.Combine(baseDir, name + "-art");
                outDir.SetBlockSignals(false);
            }
        };
        var numbers = Form.Row(g, "Tile numbers", new OptionButton
        {
            TooltipText = $"classic: free tiles in Blood's range (below {Shared.Blood.Files.MapArt.BloodTiles}): a .MAP for Blood, Refreshed Supply and NBlood; "
                + $"extended: from {Shared.Blood.Files.MapArt.BloodTiles} on (to {Shared.Blood.Files.MapArt.MaxTile}): a .MAPX (NBlood and Chosen draw them)",
        });
        numbers.AddItem($"Classic: free tiles below {Shared.Blood.Files.MapArt.BloodTiles} (a .MAP)", 0);
        numbers.AddItem($"Extended: {Shared.Blood.Files.MapArt.BloodTiles} and up (a .MAPX)", 1);
        numbers.Select(doc.Format == Document.MapFormat.Map ? 0 : 1);
        box.AddChild(new Label
        {
            Text = "Each picture is matched to Blood's palette (less than half opaque: see-through). The folder's tiles.txt keeps each "
                + "picture's number: pack again after changing a picture and the walls showing it keep it.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(580, 0), Modulate = new Color(1, 1, 1, 0.7f),
        });
        d.Confirmed += () =>
        {
            if (pics.Text.Trim() == "" || outDir.Text.Trim() == "")
            {
                w.DisplayStatus("pack pictures: a pictures folder and a folder for the ART files");
                return;
            }
            w.UiDo($"mapart pack {(numbers.Selected == 0 ? "classic" : "extended")}|{pics.Text.Trim()}|{outDir.Text.Trim()}");
            if (w.LastPack is { } r)
            {
                if (r.Error != null)
                    w.Alert("Pack Pictures", r.Error);
                else if (r.Warnings.Count > 0)
                    w.Alert("Pack Pictures", $"Packed {r.Tiles} pictures into {string.Join(", ", r.Files)}.\n\n" + string.Join("\n", r.Warnings.Take(20))
                        + (r.Warnings.Count > 20 ? $"\n... and {r.Warnings.Count - 20} more" : ""));
            }
            done();
        };
        w.Popup(d);
    }
}

/// <summary>Preferences: the Blood folder, the colours, the keys.</summary>
public static class PreferencesDialog
{
    public static void Show(MainWindow w)
    {
        var d = new AcceptDialog { Title = "Preferences", OkButtonText = "Close", MinSize = new Vector2I(640, 520) };
        var tabs = new TabContainer();
        d.AddChild(tabs);

        // the Blood folder
        var data = new VBoxContainer { Name = "Blood folder" };
        tabs.AddChild(data);
        data.AddChild(new Label { Text = "The Blood install Forge reads its tiles and palette from (BLOOD.RFF, TILES000.ART...):" });
        var row = new HBoxContainer();
        data.AddChild(row);
        var path = new LineEdit { Text = w.Config.Get("blood.folder"), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        row.AddChild(path);
        var browse = new Button { Text = "Browse..." };
        row.AddChild(browse);
        var find = new Button { Text = "Find" };
        row.AddChild(find);
        var state = new Label { Text = Data.ForgeData.Ready ? $"{Data.ForgeData.Tiles.Count} tiles" : Data.ForgeData.Error };
        data.AddChild(state);
        void Use(string f)
        {
            path.Text = f;
            w.UseBloodFolder(f);
            state.Text = Data.ForgeData.Ready ? $"{Data.ForgeData.Tiles.Count} tiles" : Data.ForgeData.Error;
        }
        path.TextSubmitted += Use;
        find.Pressed += () => Use(Shared.Blood.Files.BloodInstall.Find() ?? "");
        browse.Pressed += () =>
        {
            var fd = new FileDialog { FileMode = FileDialog.FileModeEnum.OpenDir, Access = FileDialog.AccessEnum.Filesystem, UseNativeDialog = true };
            fd.DirSelected += Use;
            w.AddChild(fd);
            fd.PopupCentered(new Vector2I(800, 500));
        };

        // editing
        var editing = new VBoxContainer { Name = "Editing" };
        tabs.AddChild(editing);
        var eg = Form.Grid(editing);
        var range = Form.Number(eg, "Highlight range (pixels)", Editing.ClassicMode.HighlightRange, 2, 50);
        range.ValueChanged += v =>
        {
            Editing.ClassicMode.HighlightRange = (float)v;
            w.Config.Set("highlightrange", (int)v);
            w.Config.Save();
        };
        var units = Form.Row(eg, "Units", new OptionButton { TooltipText = "What positions and heights are shown and typed in: "
            + "game units (Chosen's; 16 Build units across, 256 up and down, heights going up) or Build units (as the .MAP "
            + "stores them, NBlood and xmapedit show them). The map is the same either way." });
        units.AddItem("Game units", 0);
        units.AddItem("Build units", 1);
        units.Select(Units.Game ? 0 : 1);
        var grid = Form.Row(eg, "Grid size", new OptionButton());
        void FillGrid()
        {
            grid.Clear();
            for (int s = 1, k = 0; s <= 8192; s *= 2, k++)
            {
                grid.AddItem(Units.Xy(s), s);
                if (s == w.View.GridSize)
                    grid.Select(k);
            }
        }
        FillGrid();
        grid.ItemSelected += idx => w.SetGrid(grid.GetItemId((int)idx));
        units.ItemSelected += idx =>
        {
            w.SetUnits(idx == 0);
            FillGrid();
        };

        // colours
        var colours = new ScrollContainer { Name = "Colours" };
        tabs.AddChild(colours);
        var cg = new GridContainer { Columns = 2 };
        colours.AddChild(cg);
        foreach (var name in ColorSettings.Names)
        {
            cg.AddChild(new Label { Text = name });
            var pick = new ColorPickerButton { Color = ColorSettings.Get(name), CustomMinimumSize = new Vector2(120, 24), EditAlpha = false };
            string n = name;
            pick.ColorChanged += c =>
            {
                ColorSettings.Set(n, c);
                w.ColoursChanged();
            };
            cg.AddChild(pick);
        }

        // keys
        var keys = new ScrollContainer { Name = "Keys" };
        tabs.AddChild(keys);
        var kg = new GridContainer { Columns = 2 };
        keys.AddChild(kg);
        foreach (var a in w.Keys.Actions)
        {
            kg.AddChild(new Label { Text = a });
            var e = new LineEdit { Text = w.Keys.Text(a), CustomMinimumSize = new Vector2(200, 0), TooltipText = "e.g. Ctrl+Shift+G (Enter to apply)" };
            string action = a;
            e.TextSubmitted += t =>
            {
                w.Keys.Bind(action, t);
                w.Config.Set("keys." + action, t);
                w.Config.Save();
            };
            kg.AddChild(e);
        }
        w.Popup(d);
    }
}

/// <summary>View > Go To Coordinates: centres the view on a map position (in the units shown).</summary>
public static class GoToDialog
{
    public static void Show(MainWindow w)
    {
        var d = new ConfirmationDialog { Title = "Go To Coordinates", OkButtonText = "Go" };
        var g = Form.Grid(d);
        var c = w.View.Centre;
        var x = Form.Text(g, $"X ({Units.Name})", Units.Xy(c.X));
        var y = Form.Text(g, $"Y ({Units.Name})", Units.Xy(c.Y));
        d.Confirmed += () =>
        {
            if (Units.ParseXy(x.Text) is { } fx && Units.ParseXy(y.Text) is { } fy)
                w.View.CentreOn(new Vector2(fx((int)c.X), fy((int)c.Y)));
        };
        w.Popup(d);
    }
}
