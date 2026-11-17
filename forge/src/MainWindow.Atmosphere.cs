// Chosen core: original code. The map's atmospheres as recorded commands (Atmosphere mode, its tab, Tools > Generate
// Atmosphere).
using System;
using System.Collections.Generic;
using System.Linq;
using Chosen.Forge.Editing;
using Chosen.Shared.Blood.Map;
using Godot;

namespace Chosen.Forge;

public partial class MainWindow
{
    /// <summary>
    /// "generateatmosphere" (the game's guesses made the map's own), "atmoassign a 3,4,5" (the sectors given atmosphere
    /// a), "atmonew a [3,4,5 [name]]" (a new atmosphere, a copy of a's settings, given to those sectors; its name escaped,
    /// %20, made unique: "Ground mist 2"), "atmodelete a" (its
    /// sectors get the map's own), "atmoset a name=value..." (its settings: Blood.AtmosphereDialog.Set). One undo step
    /// each; the map's atmospheres become its own at the first (the game's guesses first). True when the action was one.
    /// </summary>
    bool AtmosphereCommand(string action)
    {
        var p = action.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (p.Length == 0)
            return false;
        int Arg(int k) => p.Length > k && int.TryParse(p[k], out int v) ? v : -1;
        List<int> Sectors(int k) => p.Length > k
            ? p[k].Split(',').Select(t => int.TryParse(t, out int s) ? s : -1).Where(s => s >= 0 && s < Doc.Sectors.Count && Doc.SectorEditable(s)).Distinct().ToList()
            : new List<int>();
        switch (p[0])
        {
            case "generateatmosphere":
                AtmosphereOperation("Generate atmosphere", () =>
                {
                    Doc.GenerateAtmosphere();
                    return null;
                }, () => $"generate atmosphere: the game's guesses made the map's own ({AtmosphereSummary()}); "
                    + "Atmosphere mode gives sectors others, the Atmosphere tab sets them");
                return true;
            case "atmoassign":
            {
                int a = Arg(1);
                var secs = Sectors(2);
                AtmosphereOperation("Give atmosphere", () =>
                {
                    if (secs.Count == 0)
                        return "no sectors here that can be edited (their layer locked?)";
                    Doc.OwnAtmosphere();
                    if (a < 0 || a >= Doc.Atmospheres.Count)
                        return $"there is no atmosphere {a}";
                    foreach (int s in secs)
                        Doc.SetAtmosphere(s, a);
                    return null;
                }, () => $"{secs.Count} sector{(secs.Count == 1 ? "" : "s")} given atmosphere {Doc.Atmospheres[a].Name}");
                return true;
            }
            case "atmonew":
            {
                int from = Math.Max(0, Arg(1));
                var secs = Sectors(2);
                int made = -1;
                AtmosphereOperation("New atmosphere", () =>
                {
                    made = Doc.AddAtmosphere(Math.Min(from, Doc.Atmospheres.Count - 1),
                        p.Length > 3 && Uri.UnescapeDataString(p[3]).Trim() is { Length: > 0 } n ? n : null);
                    foreach (int s in secs)
                        Doc.SetAtmosphere(s, made);
                    return null;
                }, () => $"atmosphere {Doc.Atmospheres[made].Name} made" + (secs.Count > 0 ? $", given to {secs.Count} sector{(secs.Count == 1 ? "" : "s")}" : ""));
                return true;
            }
            case "atmodelete":
            {
                int a = Arg(1);
                string name = a >= 0 && a < Doc.Atmospheres.Count ? Doc.Atmospheres[a].Name : "?";
                AtmosphereOperation("Delete atmosphere", () =>
                {
                    Doc.OwnAtmosphere();
                    return a == 0 ? "the map's own atmosphere stays (it is every other sector's)" : Doc.DeleteAtmosphere(a) ? null : $"there is no atmosphere {a}";
                }, () => $"atmosphere {name} deleted: its sectors have the map's own");
                return true;
            }
            case "atmoset":
                AtmosphereOperation("Atmosphere settings", () =>
                {
                    Doc.OwnAtmosphere();
                    return Blood.AtmosphereDialog.Set(Doc.Atmospheres, action);
                });
                return true;
        }
        return false;
    }

    /// <summary>The atmospheres in a few words (the status after Generate).</summary>
    string AtmosphereSummary()
    {
        var parts = new List<string>();
        for (int i = 0; i < Doc.Atmospheres.Count; i++)
        {
            var a = Doc.Atmospheres[i];
            int n = Enumerable.Range(0, Doc.Sectors.Count).Count(s => Doc.AtmosphereOf(s) == i);
            var what = new List<string>();
            if (a.Rain > 0) what.Add("rain");
            if (a.Snow > 0) what.Add("snow");
            if (a.Leaves > 0) what.Add("leaves");
            if (a.MistHeight > 0) what.Add("mist");
            parts.Add($"{a.Name}: {n} sectors{(what.Count > 0 ? ", " + string.Join(", ", what) : "")}");
        }
        return string.Join("; ", parts);
    }

    /// <summary>An edit of the atmospheres alone as one undo step: done (the views redrawn, the tab), else undone and why
    /// said.</summary>
    bool AtmosphereOperation(string what, Func<string> op, Func<string> done = null)
    {
        Fresh(what);
        string error;
        try
        {
            error = op();
        }
        catch (Exception e)
        {
            error = "failed: " + e.Message;
        }
        if (error != null)
        {
            Undo.Revert();
            DisplayStatus($"{what}: {error}");
            UndoChanged();
            return false;
        }
        Doc.NotifyAtmosphereChanged();
        UndoChanged();
        DisplayStatus(done?.Invoke() ?? what + ": done");
        return true;
    }

    /// <summary>The atmosphere dialog's changes shown as they are made (a copy of the atmospheres with the command done on
    /// it, in the 3D preview; null: the map's again).</summary>
    public void PreviewAtmosphere(string command)
    {
        List<MapAtmosphere> shown = null;
        if (command != null)
        {
            shown = Doc.Atmospheres.Select(a => a.Clone()).ToList();
            if (Blood.AtmosphereDialog.Set(shown, command) != null)
                shown = null;
        }
        _preview.AtmospheresShown = shown;
        _preview.AtmosphereChanged();
    }

    /// <summary>The selected sectors: the 2D view's, or the 3D preview's floors and ceilings while it shows.</summary>
    public List<int> SelectedSectors()
    {
        if (_preview.Visible)
            return _preview.Selected.Where(t => t.IsPlane && t.Sector >= 0 && t.Sector < Doc.Sectors.Count).Select(t => t.Sector).Distinct().OrderBy(s => s).ToList();
        return _selection[Chosen.Forge.View.ElementKind.Sector].Where(s => s >= 0 && s < Doc.Sectors.Count).OrderBy(s => s).ToList();
    }

    /// <summary>The settings of atmosphere i (its dialog).</summary>
    public void EditAtmosphere(int i) => Blood.AtmosphereDialog.Show(this, i);

    /// <summary>
    /// Atmosphere mode's right click over sectors: their settings alone (their atmosphere's when they are all its sectors,
    /// else a copy of it given to them: changing it changes no other sector), their atmosphere's (every sector using it),
    /// or another atmosphere given to them (each, the one they have checked).
    /// </summary>
    public void AtmosphereMenu(List<int> sectors, Vector2? at = null)
    {
        sectors = sectors.Where(s => s >= 0 && s < Doc.Sectors.Count && Doc.SectorEditable(s)).ToList();
        // (a replay: what was chosen is its recorded line)
        if (sectors.Count == 0 || Replaying)
            return;
        var have = sectors.Select(Doc.AtmosphereOf).Distinct().ToList();
        string list = string.Join(",", sectors);
        var menu = new PopupMenu();
        string these = sectors.Count == 1 ? "This Sector" : $"These {sectors.Count} Sectors";
        menu.AddSeparator(sectors.Count == 1 ? $"sector {sectors[0]}: {Doc.Atmospheres[Doc.AtmosphereOf(sectors[0])].Name}" : $"{sectors.Count} sectors");
        int Using(int a) => Enumerable.Range(0, Doc.Sectors.Count).Count(s => Doc.AtmosphereOf(s) == a);
        // (their own already: one atmosphere, not the map's, no other sector using it)
        bool alone = Doc.HasAtmosphere && have.Count == 1 && have[0] != 0 && Using(have[0]) == sectors.Count;
        const int Own = 10000, Edit = 10001;
        menu.AddItem($"Settings for {these} Only...", Own);
        menu.SetItemTooltip(menu.GetItemIndex(Own), alone ? "no other sector uses their atmosphere"
            : "a copy of their atmosphere given to them alone: changing it changes no other sector");
        if (have.Count == 1 && !alone)
        {
            int n = Using(have[0]);
            menu.AddItem(have[0] == 0 ? $"Settings of {Doc.Atmospheres[0].Name} (the Map's Own: Every Sector Given None)..."
                : $"Settings of {Doc.Atmospheres[have[0]].Name} (All {n} Sectors Using It)...", Edit);
        }
        menu.AddSeparator("Same as");
        var atmospheres = Doc.HasAtmosphere ? Doc.Atmospheres : new List<MapAtmosphere> { Doc.Atmospheres[0] };
        for (int i = 0; i < atmospheres.Count; i++)
        {
            menu.AddRadioCheckItem($"{atmospheres[i].Name}", i);
            menu.SetItemChecked(menu.GetItemIndex(i), have.Count == 1 && have[0] == i);
            menu.SetItemTooltip(menu.GetItemIndex(i), "give them this atmosphere: they then change with it, and it with them");
        }
        menu.IdPressed += id =>
        {
            if (id == Own)
            {
                if (alone)
                    EditAtmosphere(have[0]);
                else
                {
                    int from = have.Count == 1 ? have[0] : 0;
                    string name = from == 0 ? "Atmosphere" : Doc.Atmospheres[from].Name;
                    UiDo($"atmonew {from} {list} {Uri.EscapeDataString(name)}");
                    EditAtmosphere(Doc.Atmospheres.Count - 1);
                }
            }
            else if (id == Edit)
                EditAtmosphere(have[0]);
            else
                UiDo($"atmoassign {id} {list}");
        };
        menu.PopupHide += () => menu.QueueFree();
        AddChild(menu);
        var pos = at ?? GetViewport().GetMousePosition();
        GetTree().CreateTimer(0.02).Timeout += () =>
        {
            if (!GodotObject.IsInstanceValid(menu))
                return;
            menu.Position = PopupPosition(pos);
            menu.Popup();
        };
    }
}
