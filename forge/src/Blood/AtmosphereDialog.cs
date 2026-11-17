// Chosen core: original code. An atmosphere's settings (Atmosphere mode's dialog) and "atmoset", the command it applies.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Chosen.Forge.Config;
using Chosen.Forge.Document;
using Chosen.Forge.Tools;
using Chosen.Shared.Blood.Map;
using Godot;

namespace Chosen.Forge.Blood;

public static class AtmosphereDialog
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>The dialog over atmosphere i (applied as it closes: one undo step, "atmoset"), shown as it changes in
    /// the 3D preview.</summary>
    public static void Show(MainWindow w, int i)
    {
        var doc = w.Doc;
        if (i < 0 || i >= doc.Atmospheres.Count)
            return;
        var a = doc.Atmospheres[i];
        int count = Enumerable.Range(0, doc.Sectors.Count).Count(s => doc.AtmosphereOf(s) == i);
        var d = new ToolDialog(w, null, i == 0 ? $"Atmosphere: {a.Name} (the map's own)" : $"Atmosphere: {a.Name} ({count} sector{(count == 1 ? "" : "s")})", "Apply")
        {
            ApplyOnClose = true,
        };
        // (the name in the first tab: Tab moves the form into tabs, a row before it went with the old form)
        d.Tab("Fog");
        var name = new LineEdit { Text = a.Name, CustomMinimumSize = new Vector2(200, 0) };
        name.TextChanged += _ => d.Refresh();
        d.Row("Name", name);
        // (shared settings said plainly: Forge report, a green mist for one sector and a pink for another, both given one)
        if (i == 0 || count > 1)
            d.Note(i == 0 ? "The map's own: every sector given no other atmosphere has these settings. To change only some sectors, "
                + "select them in Atmosphere mode, right click: Settings for These Sectors Only."
                : $"Shared: {count} sectors use these settings, a change here changes all of them. To change only some, select "
                + "them in Atmosphere mode, right click: Settings for These Sectors Only.");
        var fog = d.Length(null, "Complete at (0: the map's, from its visibility)", a.FogDistance, 1048576);
        var fogOwn = d.Check(null, "Its own colour (else the level's: dark, white in a misty level)", a.FogOwnColour);
        var fogColour = ColourRow(d, "Colour", a.FogR, a.FogG, a.FogB);
        // (a colour picked is its own: unticked, the pick changed nothing)
        fogColour.ColorChanged += _ => fogOwn.ButtonPressed = true;
        var fade = Number(d, "Fading into it (seconds, as the camera comes in)", a.Fade, 0, 30, 0.1);
        d.Note("The distance fog is the camera's area's: walking into a sector of another atmosphere, the fog becomes its fog "
            + "over the fade. Nearer is thicker: a heavy fog is complete at a few thousand units, a light one far off.");

        d.Tab("Ground mist");
        double perZ = Units.Game ? Units.PerZ : 1;
        var mist = Number(d, $"Height over the floor ({(Units.Game ? "game units" : "Build z")}; 0: none; the game's {(int)Math.Round(AtmosphereRules.GameMistHeight / perZ)})",
            a.MistHeight / perZ, 0, 1048576 / perZ, Units.Game ? 0.25 : 1);
        var mistDensity = Number(d, "Density (1: the game's)", a.MistDensity, 0, 20, 0.05);
        var mistOwn = d.Check(null, "Its own colour (else the level's: a blue grey, white in a misty level)", a.MistOwnColour);
        var mistColour = ColourRow(d, "Colour", a.MistR, a.MistG, a.MistB);
        mistColour.ColorChanged += _ => mistOwn.ButtonPressed = true;
        d.Note("Mist lies on the floors of its sectors, patchy and drifting; adjacent sectors of the same atmosphere at about one "
            + "height are one area.");

        d.Tab("Weather");
        var rain = Number(d, "Rain (0: none, 1: the game's amount)", a.Rain, 0, 4, 0.05);
        var snow = Number(d, "Snow", a.Snow, 0, 4, 0.05);
        var leaves = Number(d, "Leaves", a.Leaves, 0, 4, 0.05);
        double perXy = Units.Game ? Units.PerXy : 1;
        var windAngle = d.Int(null, "Wind blows toward (Build angle: 0 east, 512 south)", a.WindAngle & 2047, 0, 2047);
        var windSpeed = Number(d, $"Wind speed ({Units.Name} a second; the game's rain 1440)", a.WindSpeed / perXy, 0, 65536 / perXy, Units.Game ? 0.25 : 1);
        var gusts = Number(d, "Gusts (0 steady, 1 the game's)", a.Gusts, 0, 4, 0.05);
        d.Note("Weather falls only where the sky is open over the sector, and the wind blows it in at windows and doors.");

        var init = Fields();
        Dictionary<string, string> Fields()
        {
            string Rgb(ColorPickerButton b) => $"{(int)Math.Round(b.Color.R * 255)},{(int)Math.Round(b.Color.G * 255)},{(int)Math.Round(b.Color.B * 255)}";
            string F(double v) => v.ToString("0.###", Inv);
            return new Dictionary<string, string>
            {
                ["name"] = Uri.EscapeDataString(name.Text.Trim()),
                ["fog"] = ((int)ToolDialog.BuildLength(fog)).ToString(Inv),
                ["fogown"] = fogOwn.ButtonPressed ? "1" : "0",
                ["fogcolor"] = Rgb(fogColour),
                ["fade"] = F(fade.Value),
                ["mist"] = ((int)Math.Round(mist.Value * perZ)).ToString(Inv),
                ["mistdensity"] = F(mistDensity.Value),
                ["mistown"] = mistOwn.ButtonPressed ? "1" : "0",
                ["mistcolor"] = Rgb(mistColour),
                ["rain"] = F(rain.Value),
                ["snow"] = F(snow.Value),
                ["leaves"] = F(leaves.Value),
                ["windangle"] = ((int)windAngle.Value).ToString(Inv),
                ["windspeed"] = F(Math.Round(windSpeed.Value * perXy)),
                ["gusts"] = F(gusts.Value),
            };
        }
        string Command()
        {
            var now = Fields();
            var changed = now.Where(kv => init[kv.Key] != kv.Value).Select(kv => $"{kv.Key}={kv.Value}").ToList();
            return changed.Count == 0 ? null : $"atmoset {i} {string.Join(" ", changed)}";
        }
        d.Live = () => w.PreviewAtmosphere(Command());
        d.Closed = () => w.PreviewAtmosphere(null);
        d.Show(Command);

        ColorPickerButton ColourRow(ToolDialog dlg, string label, byte r, byte g, byte b)
        {
            var c = new ColorPickerButton { Color = new Color(r / 255f, g / 255f, b / 255f), EditAlpha = false, CustomMinimumSize = new Vector2(120, 24) };
            dlg.Row(label, c);
            c.ColorChanged += _ => dlg.Refresh();
            return c;
        }
    }

    static SpinBox Number(ToolDialog d, string label, double value, double min, double max, double step)
    {
        var s = new SpinBox { MinValue = min, MaxValue = max, Step = step, Value = Math.Clamp(value, min, max), CustomMinimumSize = new Vector2(120, 0) };
        s.ValueChanged += _ => d.Refresh();
        d.Row(label, s);
        return s;
    }

    /// <summary>"atmoset 2 name=Heavy%20fog fog=4096 fogown=1 fogcolor=200,200,210 fade=1.5 mist=6144 mistdensity=1
    /// mistown=0 mistcolor=r,g,b rain=1 snow=0 leaves=0 windangle=1934 windspeed=1440 gusts=1" (any of the fields): the
    /// atmosphere changed, on the map's list or a copy (the dialog's preview); null when done, else why not.</summary>
    public static string Set(List<MapAtmosphere> list, string command)
    {
        var p = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (p.Length < 2 || p[0] != "atmoset" || !int.TryParse(p[1], out int i))
            return "atmoset <atmosphere> name=value...";
        if (i < 0 || i >= list.Count)
            return $"there is no atmosphere {i}";
        var a = list[i];
        var kv = new Dictionary<string, string>();
        foreach (var f in p.Skip(2))
        {
            int eq = f.IndexOf('=');
            if (eq > 0)
                kv[f[..eq]] = f[(eq + 1)..];
        }
        int I(string k, int def) => kv.TryGetValue(k, out var v) && int.TryParse(v, NumberStyles.Integer, Inv, out int r) ? r : def;
        float F(string k, float def) => kv.TryGetValue(k, out var v) && float.TryParse(v, NumberStyles.Float, Inv, out float r) ? r : def;
        bool Rgb(string k, out byte r, out byte g, out byte b)
        {
            r = g = b = 0;
            if (!kv.TryGetValue(k, out var v) || v.Split(',') is not { Length: 3 } c || !c.All(t => byte.TryParse(t, out _)))
                return false;
            (r, g, b) = (byte.Parse(c[0]), byte.Parse(c[1]), byte.Parse(c[2]));
            return true;
        }
        if (kv.TryGetValue("name", out var nv))
        {
            string n = Uri.UnescapeDataString(nv).Trim();
            if (n.Length == 0)
                return "an atmosphere needs a name";
            if (list.Where((o, k) => k != i && o.Name == n).Any())
                return $"another atmosphere is called {n}";
            a.Name = n;
        }
        a.FogDistance = Math.Max(0, I("fog", a.FogDistance));
        if (kv.ContainsKey("fogown"))
            a.FogOwnColour = I("fogown", 0) != 0;
        if (Rgb("fogcolor", out var fr, out var fg, out var fb))
            (a.FogR, a.FogG, a.FogB) = (fr, fg, fb);
        a.Fade = Math.Clamp(F("fade", a.Fade), 0, 30);
        a.MistHeight = Math.Max(0, I("mist", a.MistHeight));
        a.MistDensity = Math.Clamp(F("mistdensity", a.MistDensity), 0, 20);
        if (kv.ContainsKey("mistown"))
            a.MistOwnColour = I("mistown", 0) != 0;
        if (Rgb("mistcolor", out var mr, out var mg, out var mb))
            (a.MistR, a.MistG, a.MistB) = (mr, mg, mb);
        a.Rain = Math.Clamp(F("rain", a.Rain), 0, 4);
        a.Snow = Math.Clamp(F("snow", a.Snow), 0, 4);
        a.Leaves = Math.Clamp(F("leaves", a.Leaves), 0, 4);
        a.WindAngle = I("windangle", a.WindAngle) & 2047;
        a.WindSpeed = Math.Max(0, F("windspeed", a.WindSpeed));
        a.Gusts = Math.Clamp(F("gusts", a.Gusts), 0, 4);
        return null;
    }
}
