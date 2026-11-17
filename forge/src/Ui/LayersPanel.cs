// Chosen core: original code.
using System.Globalization;
using System.Linq;
using Godot;

namespace Chosen.Forge.Ui;

/// <summary>
/// The docker's Layers tab (Document/Layers.cs): a row a layer, its box showing or hiding it, its name (the button
/// makes it current: the layer edited, where new sectors go; typed into, renamed), its sectors' count and a button
/// moving the selected sectors into it; New, Delete, From Overlaps (the layers a .MAP gets, made again). Every change
/// goes through the window's commands (recorded, replayed).
/// </summary>
public partial class LayersPanel : VBoxContainer
{
    readonly MainWindow _w;
    VBoxContainer _rows;
    bool _filling;

    public LayersPanel(MainWindow w)
    {
        _w = w;
        Name = "Layers";
    }

    public override void _Ready()
    {
        var tools = new HBoxContainer();
        AddChild(tools);
        foreach (var (text, action, tip) in new[]
        {
            ("New", "layernew", "a new layer, made current"),
            ("Delete", "layerdelete", "the current layer deleted: its sectors go to the first layer"),
            ("From Overlaps", "layersauto", "the layers made again from the map: Blood's linked rooms, then the sectors lying over others by height"),
        })
        {
            var b = new Button { Text = text, TooltipText = tip };
            b.Pressed += () => _w.UiDo(action);
            tools.AddChild(b);
        }
        AddChild(new Label
        {
            Text = "Only the current layer is edited; another shown one is dimmed and locked, a hidden one out of the way.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        });
        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        AddChild(scroll);
        _rows = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        scroll.AddChild(_rows);
        Refresh();
    }

    /// <summary>The rows made again (the map, its layers or the current one changed).</summary>
    public void Refresh()
    {
        if (_rows == null)
            return;
        _filling = true;
        foreach (var c in _rows.GetChildren())
            c.QueueFree();
        var d = _w.Doc;
        if (d != null)
        {
            var counts = new int[d.Layers.Count];
            var sprites = new int[d.Layers.Count];
            for (int s = 0; s < d.Sectors.Count; s++)
                counts[d.LayerOf(s)]++;
            for (int i = 0; i < d.Sprites.Count; i++)
                sprites[d.SpriteLayer(i)]++;
            for (int i = 0; i < d.Layers.Count; i++)
            {
                int layer = i;
                var row = new HBoxContainer();
                var shown = new CheckBox { ButtonPressed = d.Layers[i].Visible, TooltipText = "shown" };
                shown.Toggled += on =>
                {
                    if (!_filling)
                        _w.UiDo($"layershow {layer} {(on ? 1 : 0)}");
                };
                row.AddChild(shown);
                var name = new LineEdit { Text = d.Layers[i].Name, SizeFlagsHorizontal = SizeFlags.ExpandFill, SelectAllOnFocus = true };
                if (i == d.CurrentLayer)
                    name.AddThemeColorOverride("font_color", ForgeTheme.Accent.Lightened(0.3f));
                name.TextSubmitted += t =>
                {
                    if (!_filling && t.Trim().Length > 0 && t != _w.Doc.Layers[layer].Name)
                        _w.UiDo($"layername {layer} {t.Trim()}");
                };
                row.AddChild(name);
                var current = new Button { Text = i == d.CurrentLayer ? "editing" : "edit", ToggleMode = true, ButtonPressed = i == d.CurrentLayer,
                    TooltipText = "make it the layer edited (new sectors go into it)" };
                current.Pressed += () => _w.UiDo($"layercurrent {layer}");
                row.AddChild(current);
                row.AddChild(new Label { Text = counts[i].ToString(CultureInfo.InvariantCulture), CustomMinimumSize = new Vector2(32, 0),
                    HorizontalAlignment = HorizontalAlignment.Right, TooltipText = $"{counts[i]} sectors, {sprites[i]} sprites" });
                var move = new Button { Text = "<", TooltipText = "move the selected sectors (every sprite in them with them) and the selected sprites into this layer; in Lights mode the selected lights (into this layer's room where each is; an attached one with its sprite)" };
                move.Pressed += () => _w.UiDo($"layermove {layer}");
                row.AddChild(move);
                _rows.AddChild(row);
            }
        }
        _filling = false;
    }
}
