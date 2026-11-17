// Chosen core: original code.
using System.Globalization;
using System.Linq;
using Chosen.Forge.Editing;
using Godot;

namespace Chosen.Forge.Ui;

/// <summary>
/// The docker's Atmosphere tab (Document/Atmospheres.cs): a row an atmosphere, its colour (its sectors' tint in
/// Atmosphere mode), its name, its sectors' count, a button giving it to the selected sectors, one opening its
/// settings, one deleting it; New (a copy of the map's own), Generate (the game's guesses). Every change goes through
/// the window's commands (recorded, replayed).
/// </summary>
public partial class AtmospherePanel : VBoxContainer
{
    readonly MainWindow _w;
    VBoxContainer _rows;
    Label _state;

    public AtmospherePanel(MainWindow w)
    {
        _w = w;
        Name = "Atmosphere";
    }

    public override void _Ready()
    {
        var tools = new HBoxContainer();
        AddChild(tools);
        var add = new Button { Text = "New", TooltipText = "a new atmosphere, a copy of the map's own (its settings then opened)" };
        add.Pressed += () =>
        {
            _w.UiDo("atmonew 0");
            _w.EditAtmosphere(_w.Doc.Atmospheres.Count - 1);
        };
        tools.AddChild(add);
        var gen = new Button { Text = "Generate", TooltipText = "the game's guesses made the map's own, in place of these: its fog, the weather by episode, ground mist over the open ground" };
        gen.Pressed += () => _w.UiDo("generateatmosphere");
        tools.AddChild(gen);
        _state = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        AddChild(_state);
        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        AddChild(scroll);
        _rows = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        scroll.AddChild(_rows);
        Refresh();
    }

    /// <summary>The rows made again (the map or its atmospheres changed).</summary>
    public void Refresh()
    {
        if (_rows == null)
            return;
        foreach (var c in _rows.GetChildren())
            c.QueueFree();
        var d = _w.Doc;
        if (d == null)
            return;
        _state.Text = d.HasAtmosphere
            ? "An atmosphere is a set of settings sectors share: changing one changes every sector using it. Atmosphere mode: "
              + "select sectors, right click: Settings for These Sectors Only (their own), or Same as another."
            : "The map has none of its own: the game guesses (shown here). Giving sectors one, or Generate, makes the guesses its own first.";
        var counts = new int[d.Atmospheres.Count];
        for (int s = 0; s < d.Sectors.Count; s++)
            counts[d.AtmosphereOf(s)]++;
        for (int i = 0; i < d.Atmospheres.Count; i++)
        {
            int a = i;
            var row = new HBoxContainer();
            var swatch = new ColorRect
            {
                Color = AtmosphereMode.Tint(i) is { } t ? new Color(t.R, t.G, t.B) : new Color(0.3f, 0.3f, 0.3f),
                CustomMinimumSize = new Vector2(14, 14), SizeFlagsVertical = SizeFlags.ShrinkCenter,
                TooltipText = i == 0 ? "the map's own: not tinted" : "its sectors' tint in Atmosphere mode",
            };
            row.AddChild(swatch);
            row.AddChild(new Label { Text = d.Atmospheres[i].Name, SizeFlagsHorizontal = SizeFlags.ExpandFill, ClipText = true,
                TooltipText = Describe(d.Atmospheres[i]) });
            row.AddChild(new Label { Text = counts[i].ToString(CultureInfo.InvariantCulture), CustomMinimumSize = new Vector2(32, 0),
                HorizontalAlignment = HorizontalAlignment.Right, TooltipText = $"{counts[i]} sectors" });
            var give = new Button { Text = "<", TooltipText = "give it to the selected sectors" };
            give.Pressed += () =>
            {
                var secs = _w.SelectedSectors();
                if (secs.Count == 0)
                    _w.DisplayStatus("give atmosphere: select sectors first (Atmosphere or Sectors mode)");
                else
                    _w.UiDo($"atmoassign {a} {string.Join(",", secs)}");
            };
            row.AddChild(give);
            var edit = new Button { Text = "...", TooltipText = i == 0 ? "its settings: every sector given no other atmosphere"
                : $"its settings: all {counts[i]} sectors using it change with them" };
            edit.Pressed += () => _w.EditAtmosphere(a);
            row.AddChild(edit);
            var del = new Button { Text = "x", TooltipText = i == 0 ? "the map's own stays" : "delete it: its sectors get the map's own", Disabled = i == 0 };
            del.Pressed += () => _w.UiDo($"atmodelete {a}");
            row.AddChild(del);
            _rows.AddChild(row);
        }
    }

    static string Describe(Shared.Blood.Map.MapAtmosphere a)
    {
        var parts = new System.Collections.Generic.List<string>
        {
            a.FogDistance > 0 ? $"fog complete at {a.FogDistance}" : "the map's fog",
        };
        if (a.MistHeight > 0) parts.Add($"mist {a.MistHeight} high");
        if (a.Rain > 0) parts.Add($"rain {a.Rain:0.##}");
        if (a.Snow > 0) parts.Add($"snow {a.Snow:0.##}");
        if (a.Leaves > 0) parts.Add($"leaves {a.Leaves:0.##}");
        if (a.WindSpeed > 0) parts.Add($"wind {a.WindSpeed:0} toward {a.WindAngle}");
        return string.Join(", ", parts);
    }
}
