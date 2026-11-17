// Chosen Blood module (GPL v2): the docker's sprite browser (UDB's things browser) over Blood's sprite types.
using System.Collections.Generic;
using System.Linq;
using Chosen.Forge.Data;
using Chosen.Forge.Ui;
using Chosen.Shared.Blood.Types;
using Godot;

namespace Chosen.Forge.Blood;

/// <summary>
/// What new sprites are: Blood's types as pictures under category buttons (All, Enemies, Weapons, Ammo, Items, Keys,
/// Decorations, ...), a search box, the chosen one's picture. Decorations: the pictures Blood's own maps use as plain
/// sprites (type 0: Blood.TileUse; its tiles have no names), placed with their usual size and lie. The pictures' size:
/// the Size slider or Ctrl+wheel over them (ThumbGrid). In Sprites mode a
/// right click on empty space (or Insert) places one; double-clicking one goes to Sprites mode.
/// </summary>
public partial class SpriteBrowser : VBoxContainer
{
    readonly MainWindow _w;
    ItemList _list;
    LineEdit _search;
    TextureRect _pic;
    Label _name;
    CheckBox _modern;
    HFlowContainer _cats;
    readonly ButtonGroup _group = new();
    ThumbGrid _grid;
    bool _filling;

    /// <summary>The categories shown, in order: null is All, Decoration the decorations' pictures.</summary>
    static readonly SpriteCategory?[] Order =
    {
        null, SpriteCategory.Dude, SpriteCategory.Weapon, SpriteCategory.Ammo, SpriteCategory.Item, SpriteCategory.Key,
        SpriteCategory.Decoration, SpriteCategory.Thing, SpriteCategory.Trap, SpriteCategory.Switch,
        SpriteCategory.PlayerStart, SpriteCategory.Marker, SpriteCategory.Sound, SpriteCategory.Generator, SpriteCategory.Other,
    };

    public SpriteBrowser(MainWindow w)
    {
        _w = w;
        Name = "Sprites";
    }

    public override void _Ready()
    {
        var top = new HBoxContainer();
        AddChild(top);
        _pic = new TextureRect
        {
            CustomMinimumSize = new Vector2(56, 56), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
        };
        top.AddChild(_pic);
        _name = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(1, 0), VerticalAlignment = VerticalAlignment.Center,
        };
        top.AddChild(_name);
        var hint = new Label
        {
            Text = "Sprites mode: right-click empty space (or Insert) to place; right-drag moves it before it lands.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(1, 0),
        };
        hint.AddThemeColorOverride("font_color", ForgeTheme.Dim);
        hint.AddThemeFontSizeOverride("font_size", 11);
        AddChild(hint);
        // the categories: buttons that wrap in the docker's width, one pressed (tabs)
        _cats = new HFlowContainer();
        AddChild(_cats);
        // (a test run's --spritecat=<category>: shown on that one, the setting left alone)
        string current = OS.GetCmdlineUserArgs().FirstOrDefault(x => x.StartsWith("--spritecat="))?[12..] ?? _w.Config.Get("sprite.category", "All");
        foreach (var c in Order)
        {
            var b = new Button
            {
                Text = CategoryName(c), ToggleMode = true, ButtonGroup = _group, Flat = false,
                TooltipText = c == SpriteCategory.Decoration ? "the pictures Blood's own maps use as decorations (plain sprites, type 0)" : "",
            };
            b.AddThemeFontSizeOverride("font_size", 12);
            b.SetMeta("cat", c is { } cc ? (int)cc : -1);
            b.ButtonPressed = b.Text == current;
            b.Pressed += () =>
            {
                _w.Config.Set("sprite.category", b.Text);
                _w.Config.Save();
                Fill();
            };
            _cats.AddChild(b);
        }
        if (_group.GetPressedButton() == null && _cats.GetChildCount() > 0)
            ((Button)_cats.GetChild(0)).ButtonPressed = true;
        _search = new LineEdit { PlaceholderText = "Search (a name, a type or tile number)", ClearButtonEnabled = true };
        _search.TextChanged += _ => Fill();
        AddChild(_search);
        var opts = new HBoxContainer();
        AddChild(opts);
        _modern = new CheckBox { Text = "Modern types", TooltipText = "NoOne's modern types (NBlood's modern maps only)",
            ButtonPressed = _w.Config.GetBool("sprite.modern", false) };
        _modern.Toggled += on => { _w.Config.Set("sprite.modern", on); _w.Config.Save(); Fill(); };
        opts.AddChild(_modern);
        var edit = new CheckBox { Text = "Edit new", TooltipText = "Open a new sprite's properties once it is placed",
            ButtonPressed = _w.EditNewSprite };
        edit.Toggled += on => _w.EditNewSprite = on;
        opts.AddChild(edit);
        _list = new ItemList
        {
            SizeFlagsVertical = SizeFlags.ExpandFill, MaxColumns = 0, IconMode = ItemList.IconModeEnum.Top,
            SameColumnWidth = true, MaxTextLines = 2,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis, SelectMode = ItemList.SelectModeEnum.Single,
        };
        // the pictures, loaded as they come into view; their size the slider's or Ctrl+wheel's
        _grid = new ThumbGrid(_list, TileOfItem, _w.Config, "sprite.thumb", 48, 44);
        opts.AddChild(new Label { Text = "Size" });
        opts.AddChild(_grid.Slider);
        _list.ItemSelected += i =>
        {
            if (!_filling)
                Choose((int)_list.GetItemMetadata((int)i));
        };
        _list.ItemActivated += _ => _w.UiDo("thingsmode");
        AddChild(_list);
        Fill();
        Show(_w.SpriteType, _w.SpriteTile);
    }

    public override void _Process(double delta) => _grid?.Tick();

    // an item's metadata: a type (0 and up), or a decoration's picture as -(tile + 1)
    static int TileOfItem(int meta) => meta < 0 ? -meta - 1 : SpriteTypes.Tile(meta);

    void Choose(int meta)
    {
        if (meta < 0)
        {
            _w.SpriteType = 0;
            _w.SpriteTile = -meta - 1;
        }
        else
        {
            _w.SpriteType = meta;
            _w.SpriteTile = -1;
        }
        Show(_w.SpriteType, _w.SpriteTile);
        _w.DisplayStatus($"new sprites: {_name.Text} (Sprites mode: right-click empty space to place)");
    }

    void Show(int type, int tile)
    {
        bool deco = tile >= 0 && SpriteTypes.Tile(type) < 0;
        int pic = deco ? tile : SpriteTypes.Tile(type);
        _pic.Texture = pic >= 0 ? TileImages.Get(pic) : null;
        _name.Text = deco ? $"Decoration: tile {tile}" : $"{type} {BloodNames.Sprite(type)}";
    }

    void Fill()
    {
        _filling = true;
        try
        {
            FillList();
        }
        finally
        {
            _filling = false;
        }
    }

    SpriteCategory? Current => _group.GetPressedButton() is Button b && (int)b.GetMeta("cat") is var c and >= 0 ? (SpriteCategory)c : null;

    void FillList()
    {
        _list.Clear();
        _grid.Reset();
        string q = _search.Text.Trim();
        var cat = Current;
        int select = -1;
        void Add(int meta, string text, string tip)
        {
            int idx = _list.AddItem(text, _grid.Blank);
            _list.SetItemMetadata(idx, meta);
            _list.SetItemTooltip(idx, tip);
            bool chosen = meta < 0 ? _w.SpriteTile == -meta - 1 && SpriteTypes.Tile(_w.SpriteType) < 0
                : meta == _w.SpriteType && (_w.SpriteTile < 0 || SpriteTypes.Tile(meta) >= 0);
            if (chosen)
                select = idx;
        }
        foreach (var g in SpriteTypes.Placeable(_modern.ButtonPressed))
        {
            if (cat != null && g.Key != cat)
                continue;
            foreach (var t in g)
            {
                string label = $"{t.Type} {t.Name}";
                if (q.Length > 0 && !label.Contains(q, System.StringComparison.OrdinalIgnoreCase))
                    continue;
                string tip = label + (t.Caption.Length > 0 ? "\n" + t.Caption : "")
                    + (t.Type == 0 ? "\n(its picture picked as it is placed; or one of the decorations' pictures)" : "");
                Add(t.Type, t.Name, tip);
            }
        }
        // the decorations' pictures: those Blood's maps use as plain sprites, by number (a set side by side)
        if (cat == SpriteCategory.Decoration || cat == null && q.Length > 0)
            foreach (var (tile, u) in TileUse.All.Where(kv => kv.Value.IsDecoration && (!ForgeData.Ready || TileImages.Exists(kv.Key))).OrderBy(kv => kv.Key))
            {
                string text = $"tile {tile}";
                if (q.Length > 0 && !text.Contains(q, System.StringComparison.OrdinalIgnoreCase) && !(cat == SpriteCategory.Decoration && "decoration".Contains(q, System.StringComparison.OrdinalIgnoreCase)))
                    continue;
                var size = TileImages.Size(tile);
                string lie = u.DecoFloor > u.Deco && u.DecoFloor >= u.DecoWall ? "flat on floors" : u.DecoWall > u.Deco ? "flat on walls" : "facing";
                Add(-(tile + 1), text, $"Decoration: tile {tile}{(size.X > 0 ? $", {size.X} x {size.Y}" : "")}; Blood's maps have it {lie}"
                    + (u.XRepeat > 0 ? $", size {u.XRepeat} x {u.YRepeat}" : "") + "\n" + TileUse.Describe(tile));
            }
        if (select >= 0)
        {
            _list.Select(select);
            _list.EnsureCurrentIsVisible();
        }
        else
            _list.GetVScrollBar().Value = 0;
    }

    static string CategoryName(SpriteCategory? c) => c switch
    {
        null => "All",
        SpriteCategory.PlayerStart => "Player starts",
        SpriteCategory.Dude => "Enemies",
        SpriteCategory.Thing => "Things",
        SpriteCategory.Ammo => "Ammo",
        SpriteCategory.Switch => "Switches",
        SpriteCategory.Decoration => "Decorations",
        SpriteCategory.Other => "Other",
        _ => c + "s",
    };

    /// <summary>The Blood folder changed: the pictures again.</summary>
    public void Refresh()
    {
        Fill();
        Show(_w.SpriteType, _w.SpriteTile);
    }
}
