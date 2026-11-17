// Chosen Blood module (GPL v2): the properties dialogs of Blood's points, walls, sectors and sprites (their fields as
// NBlood's db.h and xmapedit's OBJTYPES.INI name them).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Chosen.Forge.Ui;
using Chosen.Forge.View;
using Chosen.Shared.Blood.Map;
using Chosen.Shared.Blood.Types;
using Godot;

namespace Chosen.Forge.Blood;

public static class EditDialogs
{
    public static readonly (int bit, string name)[] WallBits =
    {
        (1, "blocking"), (64, "hitscan"), (2, "swap bottoms"), (4, "align bottom"), (8, "x-flip"), (256, "y-flip"),
        (16, "masked"), (32, "one-way"), (128, "translucent"), (512, "translucent reversed"),
    };
    public static readonly (int bit, string name)[] PlaneBits =
    {
        (1, "parallax"), (2, "sloped"), (4, "swap xy"), (8, "double smooth"), (16, "x-flip"), (32, "y-flip"),
        (64, "align to first wall"), (128, "masked"), (256, "translucent"),
    };
    public static readonly (int bit, string name)[] SpriteBits =
    {
        (1, "blocking"), (256, "hitscan"), (2, "translucent"), (512, "translucent reversed"), (4, "x-flip"), (8, "y-flip"),
        (16, "wall aligned"), (32, "floor aligned"), (64, "one-sided"), (128, "centred"), (4096, "pushable"),
        (8192, "moves with its sector"), (16384, "moves reversed"), (32768, "invisible"),
    };

    const string SlopeTip = "Slope (heinum) relative to the sector's first wall: 4096 is 45 degrees";

    /// <summary>The tab the next dialog opens on (--edit=sprite:3:1, test pictures); 0 the first.</summary>
    public static int StartTab;

    /// <summary>The dialog for items of a kind; Apply makes one undo step (none when nothing changed).</summary>
    public static void Show(MainWindow w, ElementKind kind, List<int> items, bool isNew = false)
    {
        if (items.Count == 0)
            return;
        var forms = new List<PropertyForm>();
        var d = new ConfirmationDialog { MinSize = new Vector2I(860, 620) };
        MainWindow.ApplyOnClose(w, d);
        var tabs = new TabContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        d.AddChild(tabs);
        Func<int> after = null;
        string what;
        switch (kind)
        {
            case ElementKind.Point:
                what = items.Count == 1 ? $"Vertex {items[0]}" : $"{items.Count} vertices";
                Vertex(w, tabs, items, forms);
                break;
            case ElementKind.Line:
                what = items.Count == 1 ? $"Wall {w.Doc.Lines[items[0]].Front}" : $"{items.Count} walls";
                after = Walls(w, tabs, items, forms);
                break;
            case ElementKind.Sector:
                what = items.Count == 1 ? $"Sector {items[0]}" : $"{items.Count} sectors";
                Sectors(w, tabs, items, forms);
                break;
            default:
                what = items.Count == 1 ? $"Sprite {items[0]}" : $"{items.Count} sprites";
                Sprites(w, tabs, items, forms);
                break;
        }
        d.Title = what;
        if (StartTab > 0 && StartTab < tabs.GetTabCount())
            tabs.CurrentTab = StartTab;
        StartTab = 0;
        bool applied = false;
        d.Confirmed += () =>
        {
            // the session keeps the fields changed (a replay fills the same dialog)
            w.Rec?.DialogConfirmed(d, what);
            applied = true;
            // a new sprite's dialog belongs to its insert's undo step
            if (!isNew)
                w.Undo.CreateUndo("Edit " + what);
            int changes = 0;
            foreach (var f in forms)
            {
                f.Apply();
                changes += f.Changes;
            }
            if (after != null)
                changes += after();
            // a sector type that works with markers gets them (xmapedit's), one that doesn't loses them
            if (kind == ElementKind.Sector)
                foreach (int s in items)
                    changes += SectorMarkers.Fix(w.Doc, s);
            if (changes == 0 && !isNew)
                w.Undo.WithdrawUndo();
            else
                w.Doc.NotifyChanged();
            w.UndoChanged();
            w.DisplayStatus(changes == 0 ? "nothing changed" : $"{what}: {changes} change(s)");
        };
        // a live preview: on every field typed or clicked, the fields applied to a copy of the map (Apply's way) and
        // that copy shown in the 3D preview and the 2D view until the dialog closes
        bool pending = false;
        void Preview()
        {
            if (pending || w.Replaying)
                return;
            pending = true;
            w.GetTree().CreateTimer(0.08).Timeout += () =>
            {
                pending = false;
                if (!GodotObject.IsInstanceValid(d) || !d.Visible)
                    return;
                w.ShowToolResult(w.Doc.TryOn(() =>
                {
                    foreach (var f in forms)
                        f.Apply();
                    after?.Invoke();
                }));
            };
        }
        foreach (var f in forms)
            f.Edited += Preview;
        d.VisibilityChanged += () =>
        {
            if (!d.Visible)
                w.ShowToolResult(null);
        };
        d.Canceled += () =>
        {
            // (closed: applied, ApplyOnClose; an older session's cancel replayed: as it was then)
            if (!w.Replaying)
                return;
            w.Rec?.DialogCanceled(d, what);
            // cancelling a new sprite's dialog takes the sprite back
            if (isNew && !applied)
            {
                w.Undo.Revert();
                w.UndoChanged();
                w.DisplayStatus("insert cancelled");
            }
        };
        w.Popup(d);
    }

    static ScrollContainer Tab(TabContainer tabs, string name)
    {
        var s = new ScrollContainer { Name = name, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        var v = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        v.AddThemeConstantOverride("separation", 4);
        s.AddChild(v);
        tabs.AddChild(s);
        return s;
    }

    static VBoxContainer Body(ScrollContainer s) => (VBoxContainer)s.GetChild(0);

    /// <summary>The tile a picture field shows (the palette picker's), else the first item's.</summary>
    static int PicOf(LineEdit picture, int first) => int.TryParse(picture.Text, out int n) ? n : first;

    /// <summary>The x-structure's fields not shown above, as numbers (an item without one gets it on the first
    /// change).</summary>
    /// <summary>An x-structure's fields (Blood.XFields: every int field, none left out) by group: each group's numbers
    /// in a grid (label, value with what it means, a list or a picker), its check boxes after them; the fields the
    /// game sets as it runs last. data: a data field's meaning for the items' type (null: as listed).</summary>
    static void XSection(PropertyForm f, Container parent, FieldSpec[] specs, Func<int, object> getX, Func<int, object> ensureX, MainWindow w,
        Func<FieldSpec, FieldSpec> data = null, List<Need> needs = null, Action fix = null)
    {
        // what its type needs (Blood.Requirements), met or missing, a fix where Forge can make one
        if (needs is { Count: > 0 })
        {
            parent.AddChild(PropertyForm.Heading("What it needs"));
            foreach (var n in needs)
            {
                var row = new HBoxContainer();
                var mark = new Label { Text = n.Met ? "ok" : n.Advice ? "?" : "missing", CustomMinimumSize = new Vector2(64, 0) };
                mark.AddThemeColorOverride("font_color", n.Met ? new Color(0.55f, 0.9f, 0.55f) : n.Advice ? new Color(1f, 0.8f, 0.35f) : new Color(1f, 0.45f, 0.4f));
                row.AddChild(mark);
                row.AddChild(new Label { Text = n.Text, AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
                if (!n.Met && n.Fix != null && fix != null)
                {
                    var b = new Button { Text = n.FixText ?? "Fix", TooltipText = "applies the dialog, makes it, and opens the dialog again" };
                    b.Pressed += fix;
                    row.AddChild(b);
                }
                parent.AddChild(row);
            }
        }
        foreach (var group in XFields.Groups)
        {
            var inGroup = specs.Where(sp => sp.Group == group).Select(sp => data?.Invoke(sp) ?? sp).ToList();
            if (inGroup.Count == 0)
                continue;
            parent.AddChild(PropertyForm.Heading(group));
            var g = PropertyForm.Grid(parent, 2);
            var flags = new HFlowContainer();
            flags.AddThemeConstantOverride("h_separation", 12);
            parent.AddChild(flags);
            foreach (var spec in inGroup)
            {
                var field = XType(specs).GetField(spec.Field);
                var need = needs?.Where(n => n.Field == spec.Field).OrderBy(n => n.Met ? 1 : 0).FirstOrDefault();
                f.Field(g, flags, spec, i => getX(i) is { } x ? (int)field.GetValue(x) : 0, (i, v) => field.SetValue(ensureX(i), v), w, need);
            }
        }
    }

    /// <summary>A Fix button's work: the dialog applied (a recorded dialog), then "fixneeds" (a recorded command: the
    /// fix as an undo step of its own, the dialog opened again on the same tab).</summary>
    static Action FixFor(MainWindow w, TabContainer tabs, string kind, int index) => () =>
    {
        int tab = tabs.CurrentTab;
        if (tabs.GetParent() is AcceptDialog dlg)
            dlg.GetOkButton().EmitSignal(BaseButton.SignalName.Pressed);
        w.UiDo($"fixneeds {kind} {index} {tab}");
    };

    static Type XType(FieldSpec[] specs) => specs == XFields.Sector ? typeof(XSector) : specs == XFields.Wall ? typeof(XWall) : typeof(XSprite);

    // ---------------------------------------------------------------- vertices
    static void Vertex(MainWindow w, TabContainer tabs, List<int> items, List<PropertyForm> forms)
    {
        var doc = w.Doc;
        var f = new PropertyForm(items);
        forms.Add(f);
        var b = Body(Tab(tabs, "Vertex"));
        var g = PropertyForm.Grid(b);
        f.Xy(g, "X", i => doc.Points[i].X, (i, v) => doc.MovePoint(i, v, doc.Points[i].Y));
        f.Xy(g, "Y", i => doc.Points[i].Y, (i, v) => doc.MovePoint(i, doc.Points[i].X, v));
        b.AddChild(new Label { Text = "A vertex is every wall starting there that moves with it (the walls of the rooms on both sides)." });
    }

    // ---------------------------------------------------------------- walls
    static Func<int> Walls(MainWindow w, TabContainer tabs, List<int> lines, List<PropertyForm> forms)
    {
        var doc = w.Doc;
        var fronts = lines.Select(l => doc.Lines[l].Front).ToList();
        var backs = lines.Select(l => doc.Lines[l].Back).Where(x => x >= 0).ToList();
        CheckBox both = null;
        Func<int, bool> frontTouched = null, ignored = null;
        Side(w, tabs, "Front", fronts, forms, ref both, ref frontTouched);
        CheckBox dummy = null;
        if (backs.Count > 0)
            Side(w, tabs, "Back", backs, forms, ref dummy, ref ignored);
        return () =>
        {
            // the front's blocking and hitscan flags, where they were clicked, onto the walls behind
            int mask = (frontTouched?.Invoke(1) == true ? 1 : 0) | (frontTouched?.Invoke(64) == true ? 64 : 0);
            if (both == null || !both.ButtonPressed || mask == 0)
                return 0;
            int changes = 0;
            foreach (int l in lines)
            {
                var line = doc.Lines[l];
                if (line.Back < 0)
                    continue;
                var fw = doc.Walls[line.Front];
                var bw = doc.Walls[line.Back];
                int nc = (bw.Cstat & ~mask) | (fw.Cstat & mask);
                if (nc != bw.Cstat)
                {
                    bw.Cstat = nc;
                    changes++;
                }
            }
            return changes;
        };
    }

    static void Side(MainWindow w, TabContainer tabs, string name, List<int> walls, List<PropertyForm> forms, ref CheckBox both,
        ref Func<int, bool> touched)
    {
        var doc = w.Doc;
        var f = new PropertyForm(walls);
        forms.Add(f);
        var b = Body(Tab(tabs, name));
        var g = PropertyForm.Grid(b, 4);
        BWall W(int i) => doc.Walls[i];
        var wpic = f.Tile(g, "Picture", i => W(i).Picnum, (i, v) => W(i).Picnum = v, w, "wall");
        f.Tile(g, "Masked picture", i => W(i).OverPicnum, (i, v) => W(i).OverPicnum = v, w, "masked");
        f.Int(g, "Shade", i => W(i).Shade, (i, v) => W(i).Shade = Math.Clamp(v, -128, 127));
        f.Pal(g, "Palette", i => W(i).Pal, (i, v) => W(i).Pal = v & 255, w, () => PicOf(wpic, W(f.Items[0]).Picnum));
        f.Int(g, "X repeat", i => W(i).XRepeat, (i, v) => W(i).XRepeat = v & 255);
        f.Int(g, "Y repeat", i => W(i).YRepeat, (i, v) => W(i).YRepeat = v & 255);
        f.Int(g, "X panning", i => W(i).XPanning, (i, v) => W(i).XPanning = v & 255);
        f.Int(g, "Y panning", i => W(i).YPanning, (i, v) => W(i).YPanning = v & 255);
        f.Type(g, "Type", i => W(i).Type, (i, v) => W(i).Type = v, BloodNames.Wall,
            BloodNames.Types.All(ObjKind.Wall).Select(t => (t.Type, t.Name)));
        f.Int(g, "Hitag", i => W(i).Hitag, (i, v) => W(i).Hitag = v);
        touched = f.Flags(b, "Flags", WallBits, i => W(i).Cstat, (i, v) => W(i).Cstat = v);
        if (name == "Front")
        {
            both = new CheckBox { Text = "Blocking and hitscan flags on both sides", ButtonPressed = true };
            b.AddChild(both);
        }
        b.AddChild(PropertyForm.Heading("X-wall (triggers)"));
        f.Presence(b, "Has an x-wall", i => W(i).Xw != null, (i, on) => { if (on) doc.EnsureXWall(i); else doc.RemoveXWall(i); });
        XWall X(int i) => W(i).Xw;
        XWall E(int i) => doc.EnsureXWall(i);
        int wt = W(walls[0]).Type;
        bool oneWallType = walls.All(i => W(i).Type == wt);
        XSection(f, b, XFields.Wall, i => X(i), i => E(i), w,
            sp => sp.Field == "Data" && oneWallType ? XFields.XData(Shared.Blood.Types.ObjKind.Wall, wt) : null,
            walls.Count == 1 ? Requirements.Wall(doc, walls[0]) : null, walls.Count == 1 ? FixFor(w, tabs, "wall", walls[0]) : null);
    }

    // ---------------------------------------------------------------- sectors
    static void Sectors(MainWindow w, TabContainer tabs, List<int> items, List<PropertyForm> forms)
    {
        var doc = w.Doc;
        var f = new PropertyForm(items);
        forms.Add(f);
        BSector S(int i) => doc.Sectors[i];

        var fb = Body(Tab(tabs, "Floor"));
        var fg = PropertyForm.Grid(fb, 4);
        var fpic = f.Tile(fg, "Picture", i => S(i).FloorPicnum, (i, v) => S(i).FloorPicnum = v, w, "floor");
        f.Z(fg, "Height", i => S(i).FloorZ, (i, v) => S(i).FloorZ = v);
        f.Int(fg, "Slope", i => S(i).FloorHeinum, (i, v) => S(i).FloorHeinum = Math.Clamp(v, -32768, 32767), tip: SlopeTip);
        f.Int(fg, "Shade", i => S(i).FloorShade, (i, v) => S(i).FloorShade = Math.Clamp(v, -128, 127));
        f.Pal(fg, "Palette", i => S(i).FloorPal, (i, v) => S(i).FloorPal = v & 255, w, () => PicOf(fpic, S(f.Items[0]).FloorPicnum));
        f.Int(fg, "X panning", i => S(i).FloorXPanning, (i, v) => S(i).FloorXPanning = v & 255);
        f.Int(fg, "Y panning", i => S(i).FloorYPanning, (i, v) => S(i).FloorYPanning = v & 255);
        f.Flags(fb, "Floor flags", PlaneBits, i => S(i).FloorStat, (i, v) => S(i).FloorStat = v);

        var cb = Body(Tab(tabs, "Ceiling"));
        var cg = PropertyForm.Grid(cb, 4);
        var cpic = f.Tile(cg, "Picture", i => S(i).CeilingPicnum, (i, v) => S(i).CeilingPicnum = v, w,
            items.All(i => (S(i).CeilingStat & 1) != 0) ? "sky" : "ceiling");
        f.Z(cg, "Height", i => S(i).CeilingZ, (i, v) => S(i).CeilingZ = v);
        f.Int(cg, "Slope", i => S(i).CeilingHeinum, (i, v) => S(i).CeilingHeinum = Math.Clamp(v, -32768, 32767), tip: SlopeTip);
        f.Int(cg, "Shade", i => S(i).CeilingShade, (i, v) => S(i).CeilingShade = Math.Clamp(v, -128, 127));
        f.Pal(cg, "Palette", i => S(i).CeilingPal, (i, v) => S(i).CeilingPal = v & 255, w, () => PicOf(cpic, S(f.Items[0]).CeilingPicnum));
        f.Int(cg, "X panning", i => S(i).CeilingXPanning, (i, v) => S(i).CeilingXPanning = v & 255);
        f.Int(cg, "Y panning", i => S(i).CeilingYPanning, (i, v) => S(i).CeilingYPanning = v & 255);
        f.Flags(cb, "Ceiling flags", PlaneBits, i => S(i).CeilingStat, (i, v) => S(i).CeilingStat = v);

        var eb = Body(Tab(tabs, "Effects"));
        var eg = PropertyForm.Grid(eb, 4);
        f.Type(eg, "Type", i => S(i).Type, (i, v) => S(i).Type = v, BloodNames.Sector,
            BloodNames.Types.All(ObjKind.Sector).Select(t => (t.Type, t.Name)));
        f.Int(eg, "Visibility", i => S(i).Visibility, (i, v) => S(i).Visibility = v & 255);
        f.Int(eg, "Hitag", i => S(i).Hitag, (i, v) => S(i).Hitag = v);
        eb.AddChild(PropertyForm.Heading("X-sector (triggers, motion, lighting)"));
        f.Presence(eb, "Has an x-sector", i => S(i).Xs != null, (i, on) => { if (on) doc.EnsureXSector(i); else doc.RemoveXSector(i); });
        XSector X(int i) => S(i).Xs;
        XSector E(int i) => doc.EnsureXSector(i);
        int st = S(items[0]).Type;
        bool oneSectorType = items.All(i => S(i).Type == st);
        XSection(f, eb, XFields.Sector, i => X(i), i => E(i), w,
            sp => sp.Field == "Data" && oneSectorType ? XFields.XData(Shared.Blood.Types.ObjKind.Sector, st) : null,
            items.Count == 1 ? Requirements.Sector(doc, items[0]) : null, items.Count == 1 ? FixFor(w, tabs, "sector", items[0]) : null);
    }

    // ---------------------------------------------------------------- sprites
    static void Sprites(MainWindow w, TabContainer tabs, List<int> items, List<PropertyForm> forms)
    {
        var doc = w.Doc;
        var f = new PropertyForm(items);
        forms.Add(f);
        BSprite S(int i) => doc.Sprites[i];
        var b = Body(Tab(tabs, "Sprite"));
        var g = PropertyForm.Grid(b, 4);
        // a new type: the status list Blood keeps it in, and the picture field its usual tile (xmapedit's auto-adjust)
        var type = f.Type(g, "Type", i => S(i).Type, (i, v) =>
            {
                S(i).Type = v;
                S(i).StatNum = TypeTable.StatFor(v);
            }, BloodNames.Sprite, BloodNames.Types.All(ObjKind.Sprite).Select(t => (t.Type, t.Name)));
        var pic = f.Tile(g, "Picture", i => S(i).Picnum, (i, v) => S(i).Picnum = v, w, "sprite");
        type.TextChanged += text =>
        {
            if (int.TryParse(text, out int t) && SpriteTypes.Tile(t) is var tile and >= 0)
            {
                pic.Text = tile.ToString(System.Globalization.CultureInfo.InvariantCulture);
                pic.EmitSignal(LineEdit.SignalName.TextChanged, pic.Text);
            }
        };
        f.Xy(g, "X", i => S(i).X, (i, v) => doc.MoveSprite(i, v, S(i).Y));
        f.Xy(g, "Y", i => S(i).Y, (i, v) => doc.MoveSprite(i, S(i).X, v));
        // a new height may put it in another of the rooms sharing its place
        f.Z(g, "Z", i => S(i).Z, (i, v) =>
        {
            S(i).Z = v;
            doc.UpdateSpriteSector(i);
        });
        f.Field(g, null, new FieldSpec { Field = "Ang", Label = "Angle", Kind = FieldKind.Angle, Min = 0, Max = 2047, Meaning = XFields.Compass,
            Tip = "Build's angle: 2048 a circle, 512 a quarter turn (0 east, 512 south, 1024 west, 1536 north); an axis marker's: how far its sector turns" },
            i => S(i).Ang, (i, v) => S(i).Ang = v & 2047, w);
        f.Int(g, "Shade", i => S(i).Shade, (i, v) => S(i).Shade = Math.Clamp(v, -128, 127));
        f.Pal(g, "Palette", i => S(i).Pal, (i, v) => S(i).Pal = v & 255, w, () => PicOf(pic, S(f.Items[0]).Picnum));
        f.Int(g, "X repeat", i => S(i).XRepeat, (i, v) => S(i).XRepeat = v & 255);
        f.Int(g, "Y repeat", i => S(i).YRepeat, (i, v) => S(i).YRepeat = v & 255);
        f.Int(g, "Clip distance", i => S(i).ClipDist, (i, v) => S(i).ClipDist = v & 255);
        f.Int(g, "X offset", i => S(i).XOffset, (i, v) => S(i).XOffset = Math.Clamp(v, -128, 127), tip: "the picture moved across, in texels");
        f.Int(g, "Y offset", i => S(i).YOffset, (i, v) => S(i).YOffset = Math.Clamp(v, -128, 127), tip: "the picture moved up and down, in texels");
        var slopeField = f.Int(g, "Slope", i => ModernTypes.Slope(S(i)), (i, v) =>
        {
            // (the engine keeps a sloped floor sprite's slope in its offsets: x the low byte, y the high one)
            var s = S(i);
            v = Math.Clamp(v, -32768, 32767);
            if (v != 0)
            {
                s.Cstat |= ModernTypes.CstatSlope;
                s.XOffset = (sbyte)(v & 255);
                s.YOffset = (sbyte)((v >> 8) & 255);
            }
            else if ((s.Cstat & ModernTypes.CstatSlope) == ModernTypes.CstatSlope)
            {
                s.Cstat = (s.Cstat & ~ModernTypes.CstatSlope) | 32;
                s.XOffset = s.YOffset = 0;
            }
        }, tip: "a sloped floor sprite (NBlood, Chosen: wall aligned and floor aligned both): how steeply it rises the way it faces, 4096 for 45 degrees; "
            + "it takes the place of the offsets. 0: none");
        slopeField?.SetMeta(Recording.SessionRecorder.LateField, true);
        f.Int(g, "Hitag", i => S(i).Flags, (i, v) => S(i).Flags = v, tip: "Blood's sprite flags (hitag)");
        f.Int(g, "Status", i => S(i).StatNum, (i, v) => S(i).StatNum = v, tip: "Blood's status list: 0 decoration, 3 item, 4 thing, 6 enemy, 10 sector marker, 11 trap, 12 ambient sound, 16 path marker (set with the type)");
        f.Flags(b, "Flags", SpriteBits, i => S(i).Cstat, (i, v) => S(i).Cstat = v);

        var xb = Body(Tab(tabs, "X-sprite"));
        f.Presence(xb, "Has an x-sprite", i => S(i).Xsp != null, (i, on) => { if (on) doc.EnsureXSprite(i); else doc.RemoveXSprite(i); });
        XSprite X(int i) => S(i).Xsp;
        XSprite E(int i) => doc.EnsureXSprite(i);
        int t0 = S(items[0]).Type;
        bool oneType = items.All(i => S(i).Type == t0);
        // (a data field's meaning for the type: a sound, an enemy, a radius...)
        XSection(f, xb, XFields.Sprite, i => X(i), i => E(i), w,
            sp => oneType && sp.Field.StartsWith("Data", StringComparison.Ordinal) && sp.Field.Length == 5 ? XFields.SpriteData(t0, sp.Field[4] - '0') : null,
            items.Count == 1 ? Requirements.Sprite(doc, items[0]) : null, items.Count == 1 ? FixFor(w, tabs, "sprite", items[0]) : null);
    }
}
