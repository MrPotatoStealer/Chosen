// Chosen core: original code. A dynamic light's properties (Lights mode's dialog) and "lightset", the command it applies.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Chosen.Forge.Config;
using Chosen.Forge.Document;
using Chosen.Forge.Tools;
using Chosen.Shared.Blood.Map;
using Chosen.Shared.Engine.Geometry;
using Godot;

namespace Chosen.Forge.Blood;

public static class LightDialog
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static readonly string[] KindNames =
    {
        "Point (a light of its own; attached: at the sprite's place)",
        "Flame (a torch's or candle's: lit while it burns)",
        "Light fixture (the lamp's glass: dark when shot or switched off)",
        "Glow (the middle of a picture drawn fullbright)",
    };

    /// <summary>A light's height: a point light's above the floor where it is, an attached one's above its spot on the
    /// sprite (Build z units, up).</summary>
    public static int Height(MapDocument d, int i)
    {
        var l = d.Lights[i];
        if (l.Attached)
            return -l.Z;
        int s = d.LightSector(i);
        if (s < 0)
            return -l.Z;
        BuildGeom.GetZsOfSlope(d, s, l.X, l.Y, out _, out double f);
        return (int)Math.Round(f - l.Z);
    }

    /// <summary>The dialog over lights (one undo step: "lightset"); its fields are the first light's, Apply sends the
    /// ones changed.</summary>
    public static void Show(MainWindow w, List<int> items, bool isNew)
    {
        var d0 = w.Doc;
        items = items.Where(i => i >= 0 && i < d0.Lights.Count).ToList();
        if (items.Count == 0)
            return;
        var l = d0.Lights[items[0]];
        var d = new ToolDialog(w, null, items.Count == 1 ? $"Light {items[0]}" + (isNew ? " (new)" : "") : $"{items.Count} Lights", "Apply")
        {
            ApplyOnClose = true,
        };
        var kind = d.Choice("kind", "Kind", KindNames, (int)l.Kind);
        var sprite = d.IntPick(null, "Attached to sprite (-1: none; it follows it, goes out with it)", l.Sprite, -1, Math.Max(0, d0.Sprites.Count - 1),
            "Nearest", (sb, _) => sb.Value = NearestSprite(w.Doc, items[0]));
        var color = new ColorPickerButton { Color = new Color(l.R / 255f, l.G / 255f, l.B / 255f), EditAlpha = false, CustomMinimumSize = new Vector2(120, 24) };
        d.Row("Colour", color);
        color.ColorChanged += _ => d.Refresh();
        var reach = d.Length("reach", "Reach", l.Radius, 65536);
        var energy = d.Int("energy", "Brightness (percent of a lamp's)", (int)Math.Round(l.Energy * 100), 0, 2000);
        var flicker = d.Int("flicker", "Flicker (percent: 0 steady, a torch's 40)", (int)Math.Round(l.Flicker * 100), 0, 200);
        int h0 = Height(d0, items[0]);
        var height = d.Int(null, $"Height ({(Units.Game ? "game units" : "Build z")}: a point light above the floor, attached above its spot)",
            (int)Math.Round(Units.Game ? h0 / Units.PerZ : h0), -1048576, 1048576);
        var shadows = d.Check(null, "Casts shadows (the nearest few do in the game)", l.Shadows);
        var sector = d.Check(null, "Follows its sector's light (dark when the room's lights are off, flickering with it)", l.SectorLight);
        var tile = d.IntPick(null, "Fixture: shines while its sprite shows tile (-1: any)", l.Tile, -1, Shared.Blood.Files.MapArt.MaxTile, "Pick...",
            (sb, b) => Ui.TilePickerDialog.Show(w, b, sb.Value >= 0 ? (int)sb.Value : 0, t => sb.Value = t));
        var up = d.Int(null, "Fixture: where on its picture (percent: -100 bottom, 100 top)", (int)Math.Round(l.Up * 100), -100, 100);
        d.Note("Drag a light in Lights mode to move it, in 2D or the 3D preview (an attached one moves off its spot and still "
            + "follows its sprite; in 3D the wheel raises it). A map with lights of its own gets only those in the game; "
            + "Tools > Generate Lights adds the ones Blood's sprites give.");
        // the fields as shown, to send only what changed
        var init = Fields();
        Dictionary<string, string> Fields()
        {
            var c = color.Color;
            return new Dictionary<string, string>
            {
                ["kind"] = kind.Selected.ToString(Inv),
                ["sprite"] = ((int)sprite.Value).ToString(Inv),
                ["color"] = $"{(int)Math.Round(c.R * 255)},{(int)Math.Round(c.G * 255)},{(int)Math.Round(c.B * 255)}",
                ["radius"] = ((int)ToolDialog.BuildLength(reach)).ToString(Inv),
                ["energy"] = (energy.Value / 100).ToString(Inv),
                ["flicker"] = (flicker.Value / 100).ToString(Inv),
                ["height"] = ((int)Math.Round(Units.Game ? height.Value * Units.PerZ : height.Value)).ToString(Inv),
                ["shadows"] = shadows.ButtonPressed ? "1" : "0",
                ["sectorlight"] = sector.ButtonPressed ? "1" : "0",
                ["tile"] = ((int)tile.Value).ToString(Inv),
                ["up"] = (up.Value / 100).ToString(Inv),
            };
        }
        string Command()
        {
            var now = Fields();
            var changed = now.Where(kv => isNew && items.Count == 1 || init[kv.Key] != kv.Value).Select(kv => $"{kv.Key}={kv.Value}").ToList();
            return changed.Count == 0 ? null : $"lightset {string.Join(",", items)} {string.Join(" ", changed)}";
        }
        // (shown as it changes, in both views: the lights alone made again, on a copy of the list)
        d.Live = () => w.PreviewLights(Command());
        d.Closed = () => w.PreviewLights(null);
        d.Show(Command);
    }

    /// <summary>The sprite nearest a light's place (in its layer), or -1.</summary>
    static int NearestSprite(MapDocument d, int i)
    {
        if (d.LightPlace(i) is not { } p)
            return -1;
        int best = -1;
        double bd = double.MaxValue;
        for (int s = 0; s < d.Sprites.Count; s++)
        {
            if (!d.SpriteEditable(s) || (d.Sprites[s].Cstat & 32768) != 0 && d.Sprites[s].Type is >= 1 and <= 19)
                continue;
            double dx = d.Sprites[s].X - p.x, dy = d.Sprites[s].Y - p.y, dd = dx * dx + dy * dy;
            if (dd < bd)
                (bd, best) = (dd, s);
        }
        return best;
    }

    /// <summary>"lightset 3,4 kind=0 sprite=-1 color=255,200,140 radius=3200 energy=1 flicker=0 height=4096 shadows=1
    /// sectorlight=0 tile=-1 up=0" (any of the fields): the lights changed; null when done, else why not. An attachment
    /// changed: attached, the light sits on its sprite's spot (its height then above that); let go, it stays where it
    /// shone from.</summary>
    public static string Set(MapDocument d, string command) => Set(d, d.Lights, command);

    /// <summary>Set on a list of lights (the map's, or a copy of it: the dialog's preview), the map giving the sprites
    /// and sectors.</summary>
    public static string Set(MapDocument d, IList<MapLight> lights, string command)
    {
        var p = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (p.Length < 2 || p[0] != "lightset")
            return "lightset <lights> name=value...";
        var items = p[1].Split(',').Select(t => int.TryParse(t, out int i) ? i : -1).Where(i => i >= 0 && i < lights.Count).Distinct().ToList();
        if (items.Count == 0)
            return "no such light";
        var kv = new Dictionary<string, string>();
        foreach (var f in p.Skip(2))
        {
            int eq = f.IndexOf('=');
            if (eq > 0)
                kv[f[..eq]] = f[(eq + 1)..];
        }
        int I(string k, int def) => kv.TryGetValue(k, out var v) && int.TryParse(v, NumberStyles.Integer, Inv, out int r) ? r : def;
        float F(string k, float def) => kv.TryGetValue(k, out var v) && float.TryParse(v, NumberStyles.Float, Inv, out float r) ? r : def;
        if (kv.TryGetValue("sprite", out var sv) && int.TryParse(sv, out int sp) && sp >= d.Sprites.Count)
            return $"there is no sprite {sp}";
        foreach (int i in items)
        {
            var l = lights[i];
            if (kv.ContainsKey("sprite"))
            {
                int to = Math.Max(-1, I("sprite", l.Sprite));
                if (to != l.Sprite)
                {
                    if (to < 0)
                    {
                        // let go: a point light where it shone from
                        if (d.LightPlace(l) is { } at)
                            (l.X, l.Y, l.Z) = ((int)Math.Round(at.x), (int)Math.Round(at.y), (int)Math.Round(at.z));
                        l.Sprite = -1;
                    }
                    else
                    {
                        l.Sprite = to;
                        (l.X, l.Y, l.Z) = (0, 0, 0);
                    }
                }
            }
            if (kv.ContainsKey("kind"))
                l.Kind = (LightKind)Math.Clamp(I("kind", (int)l.Kind), 0, 3);
            if (kv.TryGetValue("color", out var cs) && cs.Split(',') is { Length: 3 } rgb
                && rgb.All(c => byte.TryParse(c, out _)))
                (l.R, l.G, l.B) = (byte.Parse(rgb[0]), byte.Parse(rgb[1]), byte.Parse(rgb[2]));
            l.Radius = Math.Max(0, I("radius", l.Radius));
            l.Energy = Math.Max(0, F("energy", l.Energy));
            l.Flicker = Math.Clamp(F("flicker", l.Flicker), 0, 2);
            if (kv.ContainsKey("shadows"))
                l.Shadows = I("shadows", 1) != 0;
            if (kv.ContainsKey("sectorlight"))
                l.SectorLight = I("sectorlight", 0) != 0;
            l.Tile = Math.Max(-1, I("tile", l.Tile));
            l.Up = Math.Clamp(F("up", l.Up), -1, 1);
            if (kv.ContainsKey("height"))
            {
                int h = I("height", 0);
                int s = l.Attached ? -1 : d.LightSector(l);
                if (s >= 0)
                {
                    BuildGeom.GetZsOfSlope(d, s, l.X, l.Y, out _, out double f);
                    l.Z = (int)Math.Round(f - h);
                }
                else
                    l.Z = -h;
            }
            d.SettleLight(l);
        }
        if (ReferenceEquals(lights, d.Lights))
            d.Extras.HasLights = true;
        return null;
    }
}
