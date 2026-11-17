// Chosen core: original code (the layout of UDB's info panels).
using System.Globalization;
using Chosen.Forge.Data;
using Chosen.Forge.Document;
using Chosen.Forge.View;
using Chosen.Forge.Ui;
using Godot;

namespace Chosen.Forge.Ui.InfoPanels;

/// <summary>
/// The info panel at the bottom of the window (UDB's panelinfo): what the highlighted point, wall, sector or sprite
/// is, swapped by the kind highlighted. Each kind is a panel of columns: a title, name/value rows, tile previews.
/// </summary>
public partial class InfoPanel : PanelContainer
{
    HBoxContainer _row;
    public MapDocument Doc;

    public override void _Ready()
    {
        // one height whatever is shown (the tallest, a sprite's or a sector's eight rows, fits; the window's divider can
        // make it taller): the scroll area takes no size from what it holds, so hovering never moves the layout
        CustomMinimumSize = new Vector2(0, 150);
        ClipContents = true;
        var scroll = new ScrollContainer
        {
            VerticalScrollMode = ScrollContainer.ScrollMode.ShowNever,
            HorizontalScrollMode = ScrollContainer.ScrollMode.ShowNever,
        };
        AddChild(scroll);
        _row = new HBoxContainer();
        _row.AddThemeConstantOverride("separation", 16);
        scroll.AddChild(_row);
    }

    public void ShowElement(Element e)
    {
        foreach (var c in _row.GetChildren())
            c.QueueFree();
        if (Doc == null || e.IsNone)
            return;
        switch (e.Kind)
        {
            case ElementKind.Point: Blood.Panels.Vertex(this, e.Index); break;
            case ElementKind.Line: Blood.Panels.Wall(this, e.Index); break;
            case ElementKind.Sector: Blood.Panels.Sector(this, e.Index); break;
            case ElementKind.Sprite: Blood.Panels.Sprite(this, e.Index); break;
            case ElementKind.Light when e.Index < Doc.Lights.Count: Blood.Panels.Light(this, e.Index); break;
        }
    }

    /// <summary>A column: a title and rows of name and value.</summary>
    public GridContainer Column(string title)
    {
        var box = new VBoxContainer();
        var t = new Label { Text = title };
        t.AddThemeColorOverride("font_color", ForgeTheme.Accent.Lightened(0.3f));
        box.AddChild(t);
        var g = new GridContainer { Columns = 2 };
        g.AddThemeConstantOverride("h_separation", 8);
        g.AddThemeConstantOverride("v_separation", -2);
        box.AddChild(g);
        _row.AddChild(box);
        return g;
    }

    public static void Row(GridContainer g, string name, string value)
    {
        var n = new Label { Text = name };
        n.AddThemeColorOverride("font_color", ForgeTheme.Dim);
        n.AddThemeFontSizeOverride("font_size", 12);
        // a fixed width: the panel keeps its layout from one item to the next
        var v = new Label { Text = value, ClipText = true, CustomMinimumSize = new Vector2(170, 0), TooltipText = value, MouseFilter = MouseFilterEnum.Pass };
        v.AddThemeFontSizeOverride("font_size", 12);
        g.AddChild(n);
        g.AddChild(v);
    }

    public static void Row(GridContainer g, string name, int value) => Row(g, name, value.ToString(CultureInfo.InvariantCulture));

    /// <summary>A tile preview with its caption (the number; a grey box when there is no Blood folder).</summary>
    public void Preview(string caption, int tile)
    {
        var box = new VBoxContainer();
        var tex = tile >= 0 ? TileImages.Get(tile) : null;
        Control pic;
        if (tex != null)
            pic = new TextureRect
            {
                Texture = tex, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, CustomMinimumSize = new Vector2(72, 72),
            };
        else
            pic = new ColorRect { Color = new Color(0.2f, 0.2f, 0.2f), CustomMinimumSize = new Vector2(72, 72) };
        box.AddChild(pic);
        var l = new Label { Text = $"{caption} {tile}", HorizontalAlignment = HorizontalAlignment.Center };
        l.AddThemeFontSizeOverride("font_size", 11);
        box.AddChild(l);
        _row.AddChild(box);
    }
}
