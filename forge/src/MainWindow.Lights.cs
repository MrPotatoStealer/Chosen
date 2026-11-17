// Chosen core: original code. The map's dynamic lights as recorded commands (Lights mode, Tools > Generate Lights).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Chosen.Forge.Config;
using Chosen.Forge.Document;
using Chosen.Forge.Editing;
using Chosen.Forge.View;
using Chosen.Shared.Blood.Map;
using Godot;

namespace Chosen.Forge;

public partial class MainWindow
{
    /// <summary>
    /// "lightset &lt;lights&gt; name=value..." (the light dialog's Apply: Blood.LightDialog.Set) and "generatelights" (the
    /// lights Blood's sprites give, by the game's rules, for the sprites without one: Document.GenerateLights). One undo
    /// step each. True when the action was one of them.
    /// </summary>
    bool LightCommand(string action)
    {
        if (action == "generatelights")
        {
            int added = 0;
            bool had = Doc.HasLights;
            if (Operation("Generate lights", () =>
                {
                    added = Doc.GenerateLights();
                    // (a map that had none of its own now has them, even none: the game then gives it these alone)
                    return added == 0 && had ? "every sprite that gives a light has one already" : null;
                }))
            {
                DisplayStatus($"generate lights: {added} light(s) made from the sprites by the game's rules ({Doc.Lights.Count} in the map); "
                    + "the game gives this map these alone once saved as .MAPX (Lights mode shows and edits them)");
                RefreshLights();
            }
            return true;
        }
        if (action.StartsWith("lightset ", StringComparison.Ordinal))
        {
            LightOperation("Light properties", () => Blood.LightDialog.Set(Doc, action));
            return true;
        }
        if (action.StartsWith("lightmove ", StringComparison.Ordinal))
        {
            LightMove(action);
            return true;
        }
        if (action.StartsWith("lightadd ", StringComparison.Ordinal))
        {
            LightAdd(action);
            return true;
        }
        // "copylights 3,4": the lights copied (the 3D preview's Ctrl+C on them)
        if (action.StartsWith("copylights ", StringComparison.Ordinal))
        {
            var lights = action[11..].Split(',').Select(t => int.TryParse(t, out int i) ? i : -1).Where(i => i >= 0 && i < Doc.Lights.Count).ToList();
            if (Blood.MapPaste.Copy(Doc, Array.Empty<int>(), Array.Empty<int>(), lights) is { } clip)
            {
                _clip = clip;
                DisplayStatus($"copied {clip.What} (Ctrl+V puts them where the crosshair is, as high over the floor as they were)");
            }
            return true;
        }
        return false;
    }

    /// <summary>After a lights edit: the 3D preview's lights made again.</summary>
    void RefreshLights() => _preview?.LightsChanged();

    /// <summary>An edit of the lights alone as one undo step (its own: no run merged into it): done (the selection
    /// kept, the lights' views redrawn), else undone and why said.</summary>
    bool LightOperation(string what, Func<string> op)
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
        Doc.NotifyLightsChanged();
        UndoChanged();
        DisplayStatus(what + ": done");
        return true;
    }

    /// <summary>"lightmove dx dy dz 3,4 [run]": lights moved (Build units, z down: a point light's place, an attached
    /// one's offset from its spot); run: a run of them on the same lights one undo step (the 3D preview's arrows), else
    /// a step of its own (a bulb held and let go).</summary>
    void LightMove(string action)
    {
        var p = action.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (p.Length < 5 || !int.TryParse(p[1], out int dx) || !int.TryParse(p[2], out int dy) || !int.TryParse(p[3], out int dz))
            return;
        var items = p[4].Split(',').Select(t => int.TryParse(t, out int i) ? i : -1)
            .Where(i => i >= 0 && i < Doc.Lights.Count && Doc.LightEditable(i)).Distinct().ToList();
        if (items.Count == 0)
        {
            DisplayStatus("move lights: none here that can be edited (their layer locked?)");
            return;
        }
        string name = items.Count == 1 ? "Move light" : $"Move {items.Count} lights";
        if (p.Length > 5 && p[5] == "run")
            MergeUndo("lightmove " + string.Join(",", items.OrderBy(i => i)), name);
        else
            Fresh(name);
        foreach (int i in items)
        {
            var l = Doc.Lights[i];
            l.X += dx;
            l.Y += dy;
            l.Z += dz;
            Doc.SettleLight(l);
        }
        Doc.NotifyLightsChanged();
        UndoChanged();
        var at = Doc.LightPlace(items[0]);
        DisplayStatus($"light {items[0]}" + (at is { } a ? $" at {Units.Xy(a.x)}, {Units.Xy(a.y)}, z {Units.Z(a.z)}" : "")
            + (items.Count > 1 ? $" (and {items.Count - 1} more)" : ""));
    }

    /// <summary>"lightadd x y z": a point light placed there (Build units; the 3D preview's Insert in Lights mode), one
    /// undo step, selected.</summary>
    void LightAdd(string action)
    {
        var p = action.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (p.Length < 4 || !int.TryParse(p[1], out int x) || !int.TryParse(p[2], out int y) || !int.TryParse(p[3], out int z))
            return;
        Fresh("Place light");
        int i = LightsMode.NewLight(Doc, x, y);
        Doc.Lights[i].Z = z;
        Doc.SettleLight(i);
        Doc.NotifyLightsChanged();
        UndoChanged();
        foreach (var s in _selection.Values)
            s.Clear();
        _selection[ElementKind.Light].Add(i);
        SelectionChanged();
        if (_preview.Visible)
            _preview.SetSelected(new[] { Preview3D.LightTarget(Doc, i) });
        DisplayStatus($"placed light {i} (right click it for its settings; hold it to move it, the wheel raises it)");
    }

    /// <summary>The light dialog's changes shown as they are made (a copy of the lights with the command done on it, in
    /// both views; null: the map's again).</summary>
    public void PreviewLights(string command)
    {
        List<MapLight> list = null;
        if (command != null && Doc != null)
        {
            list = Doc.Lights.Select(l => l.Clone()).ToList();
            if (Blood.LightDialog.Set(Doc, list, command) != null)
                list = null;
        }
        if (list == null && _preview.LightsShown == null && View.LightsShown == null)
            return;
        _preview.LightsShown = list;
        View.LightsShown = list;
        if (_preview.Visible)
            _preview.RefreshLights();
        View.QueueRedraw();
    }

    /// <summary>The 3D preview's right click: on lights their properties (the dialog changes them as it is used), else
    /// Texture Settings.</summary>
    void PreviewMenu(List<VisualTarget> targets)
    {
        // (Atmosphere mode: the atmosphere menu for the sectors of what is aimed at: a floor's or ceiling's, a wall's, a
        // sprite's)
        if (Editing.Mode is AtmosphereMode)
        {
            var sectors = targets.Select(t => t.IsPlane ? t.Sector : t.IsSprite && t.Sprite < Doc.Sprites.Count ? Doc.Sprites[t.Sprite].SectNum
                    : t.Wall >= 0 && t.Wall < Doc.Walls.Count ? Doc.Walls[t.Wall].Sector : -1)
                .Where(s => s >= 0 && s < Doc.Sectors.Count).Distinct().ToList();
            if (Input.MouseMode == Input.MouseModeEnum.Captured)
                Input.MouseMode = Input.MouseModeEnum.Visible;
            AtmosphereMenu(sectors, _preview.GlobalPosition + _preview.Size / 2);
            return;
        }
        var lights = targets.Where(t => t.IsLight && t.Light < Doc.Lights.Count).Select(t => t.Light).Distinct().ToList();
        if (lights.Count == 0)
        {
            TextureSettings(targets.Where(t => !t.IsLight).ToList());
            return;
        }
        if (Input.MouseMode == Input.MouseModeEnum.Captured)
            Input.MouseMode = Input.MouseModeEnum.Visible;
        EditProperties(ElementKind.Light, lights);
    }

    /// <summary>The point the crosshair is on, drawn back toward the camera by back game units (off the surface aimed at,
    /// into the room: no more than half way), Build units.</summary>
    (int x, int y, int z) CrosshairPoint(double back)
    {
        var (hx, hy, hz) = _preview.HitPoint;
        var (cx, cy, cz) = _preview.CameraBuild;
        double t = _preview.HitDistance;
        double f = t > 0 ? (t - Math.Min(back, t / 2)) / t : 1;
        return ((int)Math.Round(cx + (hx - cx) * f), (int)Math.Round(cy + (hy - cy) * f), (int)Math.Round(cz + (hz - cz) * f));
    }

    /// <summary>Insert in the 3D preview in Lights mode: a point light where the crosshair is (on a floor or ceiling half
    /// way up the room there, else a little off what it is on).</summary>
    public void PlaceLightAtCrosshair()
    {
        if (_preview.Target is not { } t || t.IsLight)
        {
            DisplayStatus("place light: aim at a floor, a ceiling, a wall or a sprite");
            return;
        }
        var (x, y, z) = CrosshairPoint(8);
        if (t.IsPlane)
        {
            (x, y, _) = CrosshairPoint(0);
            z = LightsMode.MiddleHeight(Doc, x, y);
        }
        UiDo(string.Format(CultureInfo.InvariantCulture, "lightadd {0} {1} {2}", x, y, z));
    }

    /// <summary>Ctrl+C in the 3D preview on lights (the selection's, else the one aimed at): they are copied as Lights
    /// mode copies them. False: no lights there (the picture is copied).</summary>
    bool PreviewLightsCopied()
    {
        var lights = PreviewTargets(false, true).Where(t => t.IsLight).Select(t => t.Light).ToList();
        if (lights.Count == 0)
            return false;
        UiDo("copylights " + string.Join(",", lights.OrderBy(i => i)));
        return true;
    }

    /// <summary>Ctrl+V in the 3D preview with lights alone copied: put where the crosshair is (the first light there,
    /// the others as they lay round it; each as high over the floor as it was). False: something else copied.</summary>
    /// <summary>Lights alone copied (Ctrl+V in the 3D preview puts them at the crosshair).</summary>
    public bool LightsCopied => _clip is { Lines.Count: 0, Sectors.Count: 0, Sprites.Count: 0, Lights.Count: > 0 };

    /// <summary>The context menu's Paste Lights Here: as Ctrl+V.</summary>
    public void PasteLightsAtCrosshair() => PreviewLightsPasted();

    bool PreviewLightsPasted()
    {
        if (!LightsCopied)
            return false;
        if (_preview.Target is not { } t)
        {
            DisplayStatus("paste lights: aim where they go");
            return true;
        }
        var (x, y, _) = CrosshairPoint(t.IsPlane ? 0 : 8);
        UiDo($"pastehere {x - _clip.Ref.X} {y - _clip.Ref.Y}");
        return true;
    }

    /// <summary>
    /// A dialog of settings (properties, Texture Settings, a light's, Map Options; not a tool that builds): one button,
    /// Close, and closing it in any way (Close, the window's X, Esc: Godot's cancel) applies what was changed as Close
    /// does (one undo step: Undo takes it back if it was wrong). The session records it as applied; a replay of an older
    /// session's cancel cancels.
    /// </summary>
    public static void ApplyOnClose(MainWindow w, ConfirmationDialog d)
    {
        d.OkButtonText = "Close";
        d.GetCancelButton().Visible = false;
        d.Canceled += () =>
        {
            if (!w.Replaying)
                d.EmitSignal(AcceptDialog.SignalName.Confirmed);
        };
    }

    /// <summary>Lights mode on or off: the 3D preview draws each light's bulb while it is on (its help line says how
    /// they are worked).</summary>
    public void LightMarkers(bool on)
    {
        if (_preview == null || _preview.LightMarkers == on)
            return;
        _preview.LightMarkers = on;
        _preview.ShowLightsHelp(on);
        _preview.LightsChanged();
    }

    /// <summary>K in the 3D preview: Lights mode on (slope edges off: their clicks are theirs), or off back to the mode
    /// before it; a recorded mode switch, the preview kept.</summary>
    void ToggleLightsMode3D()
    {
        if (Editing.Mode is LightsMode)
        {
            var back = Editing.LastClassic is { } m && m is not LightsMode ? m : Editing.Modes[0];
            UiDo(back.Action);
            DisplayStatus($"{back.Name} mode (the lights' bulbs gone from the 3D preview; K: Lights mode again)");
            return;
        }
        if (_preview.SlopeMode)
            _preview.SlopeMode = false;
        UiDo("lightsmode");
        DisplayStatus(Doc.HasLights || Doc.Lights.Count > 0
            ? "Lights mode: aim at a bulb: click selects, hold it to move it (wheel: nearer / farther), wheel raises, arrows move, right click: its settings, I: place a light, Delete, Ctrl+C / Ctrl+V; K: back"
            : "Lights mode: the map has no lights of its own yet (Tools > Generate Lights makes the ones its sprites give; I places one); K: back");
    }

    /// <summary>A .MAP just opened whose sprites give lights: asked whether to make them here (as the game makes them on
    /// loading a .MAP), so a .MAPX saved from it keeps them and they can be moved and changed. Not in a replay or a test
    /// picture (the answer is the session's "generatelights" line).</summary>
    void AskGenerateLights()
    {
        if (Replaying || _shotRun || Doc == null || Doc.Format != MapFormat.Map || Doc.HasLights
            || DisplayServer.GetName() == "headless")
            return;
        int n = LightRules.Generate(Doc.Map).Count;
        if (n == 0)
            return;
        var c = new ConfirmationDialog
        {
            Title = "Dynamic lights",
            DialogText = $"The game gives this .MAP {n} dynamic lights from its sprites (torches, candles, lamps, glowing decorations) as it loads.\n"
                + "Make them here as lights of the map's own? Then they show in Lights mode and the 3D preview, can be moved, changed\n"
                + "and added to, and a .MAPX saved from it keeps them (the game then gives it those alone). A .MAP saved keeps none:\n"
                + "the game makes them again from the sprites.",
            OkButtonText = "Make the lights", CancelButtonText = "Not now (Tools > Generate Lights)",
        };
        c.Confirmed += () => UiDo("generatelights");
        Popup(c);
    }
}
