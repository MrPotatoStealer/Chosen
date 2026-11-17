// Chosen core: original code.
using System;
using System.Globalization;

namespace Chosen.Forge.Config;

/// <summary>
/// The units Forge shows and takes (Preferences > Editing): game units (the default: Chosen's, 16 Build units across
/// and 256 up and down, heights growing upward) or Build units (what the .MAP stores and NBlood and xmapedit show: z
/// growing downward). The map always keeps Build's integers; only what is shown and typed changes. A typed value
/// may be relative, as in UDB: "++8" adds 8 to each item's value, "--8" takes 8 off.
/// </summary>
public static class Units
{
    public static bool Game = true;
    public const double PerXy = 16, PerZ = 256;
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string Name => Game ? "game units" : "Build units";

    public static string Fmt(double v) => Math.Round(v, 4).ToString("0.####", Inv);

    /// <summary>A horizontal position or distance (Build units in).</summary>
    public static string Xy(double build) => Game ? Fmt(build / PerXy) : Fmt(Math.Round(build));

    /// <summary>A height (Build z in: down positive); game units go up.</summary>
    public static string Z(double buildZ) => Game ? Fmt(-buildZ / PerZ) : Fmt(Math.Round(buildZ));

    /// <summary>A vertical extent (Build z units in, positive), as a positive number of the units shown.</summary>
    public static string Height(double buildZ) => Game ? Fmt(buildZ / PerZ) : Fmt(Math.Round(buildZ));

    /// <summary>A length to show by a drawn line: two decimals at most.</summary>
    public static string Length(double build) => Game ? Fmt(Math.Round(build / PerXy, 2)) : Fmt(Math.Round(build));

    /// <summary>What typed text does to a horizontal value (Build units), or null when it isn't a number.</summary>
    public static Func<int, int> ParseXy(string text) => Parse(text, Game ? PerXy : 1);

    /// <summary>What typed text does to a height (Build z), or null when it isn't a number.</summary>
    public static Func<int, int> ParseZ(string text) => Parse(text, Game ? -PerZ : 1);

    /// <summary>What typed text does to a plain number (relative values too), or null.</summary>
    public static Func<int, int> ParseInt(string text)
    {
        var f = Parse(text, 1);
        if (f == null)
            return null;
        // whole numbers only
        string t = text.Trim();
        return double.TryParse(t.StartsWith("++") || t.StartsWith("--") ? t[2..] : t, NumberStyles.Float, Inv, out double v)
            && v == Math.Floor(v) ? f : null;
    }

    /// <summary>The typed value times scale, in Build's integers: absolute, or "++n" / "--n" added to the old value.</summary>
    static Func<int, int> Parse(string text, double scale)
    {
        string t = text.Trim();
        int sign = t.StartsWith("++") ? 1 : t.StartsWith("--") ? -1 : 0;
        if (sign != 0)
            t = t[2..].Trim();
        if (!double.TryParse(t, NumberStyles.Float, Inv, out double v))
            return null;
        int d = (int)Math.Round(v * scale);
        if (sign == 0)
            return _ => d;
        return old => old + sign * d;
    }
}
