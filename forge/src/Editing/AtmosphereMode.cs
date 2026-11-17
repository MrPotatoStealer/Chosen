// Chosen core: original code. Atmosphere mode: the sectors' atmospheres (fog, ground mist, weather, wind), given by sector.
using System.Collections.Generic;
using System.Linq;
using Chosen.Forge.View;
using Godot;

namespace Chosen.Forge.Editing;

/// <summary>
/// Atmosphere mode: the sectors picked and selected as in Sectors mode (click, drag a box, Tab through rooms over
/// rooms; another layer's left alone), each tinted with its atmosphere's colour (the map's own, "Level", untinted).
/// A right click gives the selected sectors (else the one under the mouse) an atmosphere from a menu, or a new one, or
/// opens the settings of theirs; Delete gives them the map's own. Nothing is moved, joined or deleted here: the
/// geometry stays as it is. The Atmosphere tab lists the atmospheres (MainWindow.Atmosphere.cs).
/// </summary>
public sealed class AtmosphereMode : SectorsMode
{
    public override string Name => "Atmosphere";
    public override string Action => "atmospheremode";

    /// <summary>The colour an atmosphere's sectors are tinted (index 0, the map's own: none).</summary>
    public static Color? Tint(int atmosphere) =>
        atmosphere <= 0 ? null : Color.FromHsv((0.08f + atmosphere * 0.618034f) % 1f, 0.65f, 0.95f, 0.38f);

    public override void OnEngage()
    {
        View.SectorTint = s => Tint(Doc?.AtmosphereOf(s) ?? 0);
        View.TintChanged();
        base.OnEngage();
        Window.DisplayStatus(Doc != null && Doc.HasAtmosphere
            ? "Atmosphere mode: select sectors, right click: their settings only, the settings they share, or the same as another; Delete: the map's own"
            : "Atmosphere mode: the map has no atmospheres of its own yet (the game guesses its fog, mist and weather); "
                + "giving sectors one makes the guesses its own first (Tools > Generate Atmosphere)");
    }

    public override void OnDisengage()
    {
        View.SectorTint = null;
        View.TintChanged();
        base.OnDisengage();
    }

    public override void OnDocumentChanged()
    {
        View.TintChanged();
        base.OnDocumentChanged();
    }

    // nothing dragged: the geometry stays (a drag selects with a box)
    protected override bool CanDrag() => false;

    public override void OnInsert() { }

    /// <summary>Delete: the selected sectors (else the one under the mouse) get the map's own atmosphere.</summary>
    public override void OnDelete()
    {
        var items = Items();
        if (items.Count > 0)
            Window.InputDo($"atmoassign 0 {string.Join(",", items)}");
    }

    /// <summary>A right click: the atmosphere menu for the selected sectors, else the one under the mouse.</summary>
    public override void OnEditEnd(Vector2 map)
    {
        var items = Items();
        if (items.Count > 0)
            Window.AtmosphereMenu(items);
    }

    List<int> Items() => Selection.Count > 0 ? Selection.OrderBy(s => s).ToList()
        : Highlighted is { IsNone: false } h ? new List<int> { h.Index } : new List<int>();
}
