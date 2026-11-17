// Chosen core: original code. The texture settings of what the 3D preview points at (a floor, a ceiling, a wall's
// part, a sprite), written from Build's map rules: which record holds a part's picture, panning, repeats and bits.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Chosen.Forge.Document;
using Chosen.Forge.View;
using Chosen.Shared.Blood.Map;
using Chosen.Shared.Engine.Geometry;

namespace Chosen.Forge.Tools;

public static class TextureOps
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>The wall whose picture, panning, shade, palette, y flip and align bottom a wall part shows: a lower
    /// part with swapped bottoms (cstat 2) the wall behind, else the wall itself.</summary>
    public static BWall Look(MapDocument d, VisualTarget t)
    {
        var w = d.Walls[t.Wall];
        if (t.Part == VisualPart.Lower && (w.Cstat & 2) != 0 && w.NextWall >= 0 && w.NextWall < d.Walls.Count)
            return d.Walls[w.NextWall];
        return w;
    }

    static int Wrap(int v) => ((v % 256) + 256) % 256;
    static int Byte(int v, int min = 0, int max = 255) => Math.Clamp(v, min, max);

    /// <summary>Panning (Build's xpanning: pixels; ypanning: 1/256 of the picture's height) moved by dx, dy; a
    /// floor's or ceiling's (1/256 of the picture across); a sprite's offset (pixels).</summary>
    public static void Pan(MapDocument d, VisualTarget t, int dx, int dy)
    {
        switch (t.Part)
        {
            case VisualPart.Floor:
            {
                var s = d.Sectors[t.Sector];
                s.FloorXPanning = Wrap(s.FloorXPanning + dx);
                s.FloorYPanning = Wrap(s.FloorYPanning + dy);
                break;
            }
            case VisualPart.Ceiling:
            {
                var s = d.Sectors[t.Sector];
                s.CeilingXPanning = Wrap(s.CeilingXPanning + dx);
                s.CeilingYPanning = Wrap(s.CeilingYPanning + dy);
                break;
            }
            case VisualPart.Sprite:
            {
                var sp = d.Sprites[t.Sprite];
                sp.XOffset = Byte(sp.XOffset + dx, -128, 127);
                sp.YOffset = Byte(sp.YOffset + dy, -128, 127);
                break;
            }
            default:
            {
                var w = Look(d, t);
                w.XPanning = Wrap(w.XPanning + dx);
                w.YPanning = Wrap(w.YPanning + dy);
                break;
            }
        }
    }

    /// <summary>Repeats (a wall's: xrepeat * 8 pixels along it, a pixel 2048 / yrepeat z high; a sprite's size) changed
    /// by dx, dy (1..255). A floor or ceiling has none: false.</summary>
    public static bool Repeat(MapDocument d, VisualTarget t, int dx, int dy)
    {
        if (t.IsPlane)
            return false;
        if (t.IsSprite)
        {
            var sp = d.Sprites[t.Sprite];
            sp.XRepeat = Byte(sp.XRepeat + dx, 1);
            sp.YRepeat = Byte(sp.YRepeat + dy, 1);
            return true;
        }
        // (the repeats are the wall's own, whichever wall's picture the part shows)
        var w = d.Walls[t.Wall];
        w.XRepeat = Byte(w.XRepeat + dx, 1);
        w.YRepeat = Byte(w.YRepeat + dy, 1);
        return true;
    }

    /// <summary>The shades a key goes between: Blood keeps -128..127, but 63 is black and below 0 is as bright as 0
    /// up close (a negative shade only keeps a surface bright farther off, against the distance's darkening).</summary>
    public const int ShadeLight = -64, ShadeDark = 63;

    /// <summary>Shade changed by ds, darker up, no further than ShadeLight..ShadeDark (a value already beyond kept,
    /// or brought back).</summary>
    public static void Shade(MapDocument d, VisualTarget t, int ds)
    {
        static int Step(int old, int ds) => Math.Clamp(old + ds, Math.Min(old, ShadeLight), Math.Max(old, ShadeDark));
        switch (t.Part)
        {
            case VisualPart.Floor: d.Sectors[t.Sector].FloorShade = Step(d.Sectors[t.Sector].FloorShade, ds); break;
            case VisualPart.Ceiling: d.Sectors[t.Sector].CeilingShade = Step(d.Sectors[t.Sector].CeilingShade, ds); break;
            case VisualPart.Sprite: d.Sprites[t.Sprite].Shade = Step(d.Sprites[t.Sprite].Shade, ds); break;
            default:
            {
                var w = Look(d, t);
                w.Shade = Step(w.Shade, ds);
                break;
            }
        }
    }

    /// <summary>The flags a target can toggle: their command names and captions.</summary>
    public static readonly (string name, string caption)[] Flags =
    {
        ("flipx", "Flip X"), ("flipy", "Flip Y"), ("bottom", "Align to bottom"), ("relative", "Relative to the first wall"),
        ("swapxy", "Swap X and Y"), ("expand", "Double size"),
    };

    /// <summary>The record and bit a flag of a target is (null: the target has no such flag): walls: x flip cstat 8
    /// (the wall's), y flip 256 and align bottom 4 (the wall whose picture the part shows); floors and ceilings: stat
    /// 16 x flip, 32 y flip, 4 swap x and y, 8 double size, 64 relative; sprites: cstat 4 x flip, 8 y flip.</summary>
    static (Func<int> get, Action<int> set, int bit)? FlagOf(MapDocument d, VisualTarget t, string flag)
    {
        switch (t.Part)
        {
            case VisualPart.Floor:
            case VisualPart.Ceiling:
            {
                var s = d.Sectors[t.Sector];
                bool c = t.Part == VisualPart.Ceiling;
                int bit = flag switch { "flipx" => 16, "flipy" => 32, "swapxy" => 4, "expand" => 8, "relative" => 64, _ => 0 };
                if (bit == 0)
                    return null;
                return (() => c ? s.CeilingStat : s.FloorStat, v => { if (c) s.CeilingStat = v; else s.FloorStat = v; }, bit);
            }
            case VisualPart.Sprite:
            {
                var sp = d.Sprites[t.Sprite];
                int bit = flag switch { "flipx" => 4, "flipy" => 8, _ => 0 };
                if (bit == 0)
                    return null;
                return (() => sp.Cstat, v => sp.Cstat = v, bit);
            }
            default:
            {
                var w = flag == "flipx" ? d.Walls[t.Wall] : Look(d, t);
                int bit = flag switch { "flipx" => 8, "flipy" => 256, "bottom" => 4, _ => 0 };
                if (bit == 0)
                    return null;
                return (() => w.Cstat, v => w.Cstat = v, bit);
            }
        }
    }

    public static bool? GetFlag(MapDocument d, VisualTarget t, string flag) =>
        FlagOf(d, t, flag) is { } f ? (f.get() & f.bit) != 0 : null;

    /// <summary>A flag set or cleared; false: the target has no such flag.</summary>
    public static bool SetFlag(MapDocument d, VisualTarget t, string flag, bool on)
    {
        if (FlagOf(d, t, flag) is not { } f)
            return false;
        f.set(on ? f.get() | f.bit : f.get() & ~f.bit);
        return true;
    }

    /// <summary>Panning and offsets to 0 and the flips off; a wall's repeats to one picture pixel per 16 Build units
    /// along it (Build's default: xrepeat its length / 128) and yrepeat 8; a sprite's size to 64.</summary>
    public static void Reset(MapDocument d, VisualTarget t)
    {
        switch (t.Part)
        {
            case VisualPart.Floor: d.Sectors[t.Sector].FloorXPanning = d.Sectors[t.Sector].FloorYPanning = 0; break;
            case VisualPart.Ceiling: d.Sectors[t.Sector].CeilingXPanning = d.Sectors[t.Sector].CeilingYPanning = 0; break;
            case VisualPart.Sprite:
            {
                var sp = d.Sprites[t.Sprite];
                sp.XOffset = sp.YOffset = 0;
                sp.XRepeat = sp.YRepeat = 64;
                break;
            }
            default:
            {
                var look = Look(d, t);
                look.XPanning = look.YPanning = 0;
                var w = d.Walls[t.Wall];
                var (x1, y1, x2, y2) = BuildGeom.WallEnds(d, t.Wall);
                double len = Math.Sqrt((double)(x2 - x1) * (x2 - x1) + (double)(y2 - y1) * (y2 - y1));
                w.XRepeat = (int)Math.Clamp(Math.Round(len / 128.0), 1, 255);
                w.YRepeat = 8;
                break;
            }
        }
        foreach (var flag in new[] { "flipx", "flipy" })
            SetFlag(d, t, flag, false);
    }

    /// <summary>The fields the Texture Settings dialog edits for a target: name to value.</summary>
    public static Dictionary<string, int> Fields(MapDocument d, VisualTarget t)
    {
        var f = new Dictionary<string, int>();
        switch (t.Part)
        {
            case VisualPart.Floor:
            case VisualPart.Ceiling:
            {
                var s = d.Sectors[t.Sector];
                bool c = t.Part == VisualPart.Ceiling;
                f["tile"] = c ? s.CeilingPicnum : s.FloorPicnum;
                f["panx"] = c ? s.CeilingXPanning : s.FloorXPanning;
                f["pany"] = c ? s.CeilingYPanning : s.FloorYPanning;
                f["shade"] = c ? s.CeilingShade : s.FloorShade;
                f["pal"] = c ? s.CeilingPal : s.FloorPal;
                break;
            }
            case VisualPart.Sprite:
            {
                var sp = d.Sprites[t.Sprite];
                f["tile"] = sp.Picnum;
                f["panx"] = sp.XOffset;
                f["pany"] = sp.YOffset;
                f["repx"] = sp.XRepeat;
                f["repy"] = sp.YRepeat;
                f["shade"] = sp.Shade;
                f["pal"] = sp.Pal;
                break;
            }
            default:
            {
                var look = Look(d, t);
                var w = d.Walls[t.Wall];
                f["tile"] = t.Part == VisualPart.Middle && w.NextSector >= 0 ? w.OverPicnum : look.Picnum;
                f["panx"] = look.XPanning;
                f["pany"] = look.YPanning;
                f["repx"] = w.XRepeat;
                f["repy"] = w.YRepeat;
                f["shade"] = look.Shade;
                f["pal"] = look.Pal;
                if (w.NextSector >= 0)
                    foreach (var (key, v) in Mask(w))
                        f[key] = v;
                break;
            }
        }
        foreach (var (name, _) in Flags)
            if (GetFlag(d, t, name) is { } on)
                f[name] = on ? 1 : 0;
        foreach (var sc in Scrolling(t))
            f[sc.key] = Scroll(d, t, sc.key);
        return f;
    }

    // ---------------------------------------------------------------- a two-sided wall's opening (masked walls)

    /// <summary>The settings of a two-sided wall's opening (Texture Settings' Masked wall rows): "middle" 0 open, 1
    /// masked (cstat 16, both sides: its masked picture seen from either), 2 one-way (32, this side: a solid picture
    /// seen from here); "seethrough" 0 solid, 1 translucent (128), 2 translucent reversed (128 | 512); "block" (1) and
    /// "hitscan" (64): the player and shots stopped by it.</summary>
    public static readonly string[] MaskKeys = { "middle", "seethrough", "block", "hitscan" };

    static IEnumerable<(string key, int v)> Mask(BWall w)
    {
        yield return ("middle", (w.Cstat & 32) != 0 ? 2 : (w.Cstat & 16) != 0 ? 1 : 0);
        yield return ("seethrough", (w.Cstat & 128) == 0 ? 0 : (w.Cstat & 512) != 0 ? 2 : 1);
        yield return ("block", (w.Cstat & 1) != 0 ? 1 : 0);
        yield return ("hitscan", (w.Cstat & 64) != 0 ? 1 : 0);
    }

    /// <summary>An opening's setting written as xmapedit writes it: both sides, but one-way (this side; the other side
    /// open). Masked turned on: one-way off, the other side's masked picture this one's and x-flipped against this
    /// side's (it reads the same from there); one-way turned on: masked off and, with no masked picture yet, the wall's
    /// own.</summary>
    static void SetMask(MapDocument d, int wi, string key, int v)
    {
        var w = d.Walls[wi];
        if (w.NextSector < 0 || w.NextWall < 0 || w.NextWall >= d.Walls.Count)
            return;
        var o = d.Walls[w.NextWall];
        void Both(int bits, bool on)
        {
            w.Cstat = on ? w.Cstat | bits : w.Cstat & ~bits;
            o.Cstat = on ? o.Cstat | bits : o.Cstat & ~bits;
        }
        switch (key)
        {
            case "middle":
                if (v == 1 && (w.Cstat & 16) == 0)
                {
                    Both(32, false);
                    Both(16, true);
                    o.OverPicnum = w.OverPicnum;
                    o.Cstat = (w.Cstat & 8) == 0 ? o.Cstat | 8 : o.Cstat & ~8;
                }
                else if (v == 2 && (w.Cstat & 32) == 0)
                {
                    Both(16, false);
                    o.Cstat &= ~32;
                    w.Cstat |= 32;
                    if (w.OverPicnum <= 0)
                        w.OverPicnum = w.Picnum;
                }
                else if (v == 0)
                    Both(16 | 32, false);
                break;
            case "seethrough":
                Both(128 | 512, false);
                if (v >= 1)
                    Both(v == 2 ? 128 | 512 : 128, true);
                break;
            case "block":
                Both(1, v != 0);
                break;
            case "hitscan":
                Both(64, v != 0);
                break;
        }
    }

    // ---------------------------------------------------------------- scrolling (Blood's x-structures)

    /// <summary>Blood's scrolling settings of a kind of surface (Texture Settings' Scrolling rows): a wall's speeds
    /// across and up (its x-wall), a floor's or ceiling's on / off, speed and angle (shared by the sector's floor and
    /// ceiling), always (not only while on), and a floor's drag (what stands on it carried along).</summary>
    public static (string key, string label, int min, int max, bool flag, string tip)[] Scrolling(VisualTarget t) => t.Part switch
    {
        VisualPart.Floor => new[]
        {
            ("panfloor", "Floor scrolls", 0, 1, true, "the floor's picture scrolls (Blood's Pan floor)"),
            ("panvel", "Scroll speed", 0, 255, false, "how fast (the sector's floor and ceiling share it)"),
            ("panangle", "Scroll angle", 0, 2047, false, "the way it scrolls (Build's angle: 0 east, 512 south; shared with the ceiling)"),
            ("panalways", "Scroll always", 0, 1, true, "all the time, not only while the sector is on"),
            ("drag", "Drag", 0, 1, true, "carries what stands on it along (a conveyor)"),
        },
        VisualPart.Ceiling => new[]
        {
            ("panceiling", "Ceiling scrolls", 0, 1, true, "the ceiling's picture scrolls (Blood's Pan ceiling)"),
            ("panvel", "Scroll speed", 0, 255, false, "how fast (the sector's floor and ceiling share it)"),
            ("panangle", "Scroll angle", 0, 2047, false, "the way it scrolls (Build's angle: 0 east, 512 south; shared with the floor)"),
            ("panalways", "Scroll always", 0, 1, true, "all the time, not only while the sector is on"),
        },
        VisualPart.Sprite => Array.Empty<(string, string, int, int, bool, string)>(),
        _ => new[]
        {
            ("panxvel", "Scroll speed across", -128, 127, false, "how fast its picture scrolls sideways (negative: the other way)"),
            ("panyvel", "Scroll speed up", -128, 127, false, "how fast its picture scrolls up and down (negative: the other way)"),
            ("panalways", "Scroll always", 0, 1, true, "all the time, not only while the wall is on"),
        },
    };

    /// <summary>A scrolling setting's value (0 without an x-structure).</summary>
    public static int Scroll(MapDocument d, VisualTarget t, string key)
    {
        if (t.IsWall)
        {
            var x = d.Walls[t.Wall].Xw;
            return x == null ? 0 : key switch { "panxvel" => x.PanXVel, "panyvel" => x.PanYVel, "panalways" => x.PanAlways, _ => 0 };
        }
        if (!t.IsPlane)
            return 0;
        var xs = d.Sectors[t.Sector].Xs;
        return xs == null ? 0 : key switch
        {
            "panfloor" => xs.PanFloor, "panceiling" => xs.PanCeiling, "panvel" => xs.PanVel, "panangle" => xs.PanAngle,
            "panalways" => xs.PanAlways, "drag" => xs.Drag, _ => 0,
        };
    }

    /// <summary>A scrolling setting written (the x-structure made only when the value changes).</summary>
    static void SetScroll(MapDocument d, VisualTarget t, string key, int v)
    {
        var spec = Scrolling(t).FirstOrDefault(s => s.key == key);
        if (spec.key == null)
            return;
        v = Math.Clamp(v, spec.min, spec.max);
        if (Scroll(d, t, key) == v)
            return;
        if (t.IsWall)
        {
            var x = d.EnsureXWall(t.Wall);
            switch (key)
            {
                case "panxvel": x.PanXVel = v; break;
                case "panyvel": x.PanYVel = v; break;
                case "panalways": x.PanAlways = v; break;
            }
            return;
        }
        var xs = d.EnsureXSector(t.Sector);
        switch (key)
        {
            case "panfloor": xs.PanFloor = v; break;
            case "panceiling": xs.PanCeiling = v; break;
            case "panvel": xs.PanVel = v; break;
            case "panangle": xs.PanAngle = v; break;
            case "panalways": xs.PanAlways = v; break;
            case "drag": xs.Drag = v; break;
        }
    }

    /// <summary>The picture keys of Texture Settings, one a kind of surface, each set only on its own kind: a wall's
    /// picture (the part's: a lower part with swapped bottoms the wall behind's), a two-sided wall's masked picture,
    /// a floor's, a ceiling's, a sprite's.</summary>
    public static readonly (string key, string label)[] PictureKeys =
    {
        ("tilewall", "Wall picture"), ("tileover", "Masked picture"), ("tilefloor", "Floor picture"),
        ("tileceiling", "Ceiling picture"), ("tilesprite", "Sprite picture"),
    };

    /// <summary>A picture key's value on a target, null when the target hasn't that kind of picture.</summary>
    public static int? Picture(MapDocument d, VisualTarget t, string key) => key switch
    {
        "tilewall" when t.IsWall => Look(d, t).Picnum,
        "tileover" when t.IsWall && d.Walls[t.Wall].NextSector >= 0 => d.Walls[t.Wall].OverPicnum,
        "tilefloor" when t.Part == VisualPart.Floor => d.Sectors[t.Sector].FloorPicnum,
        "tileceiling" when t.Part == VisualPart.Ceiling => d.Sectors[t.Sector].CeilingPicnum,
        "tilesprite" when t.IsSprite => d.Sprites[t.Sprite].Picnum,
        _ => null,
    };

    /// <summary>A target's kind as Texture Settings' tabs prefix their keys: w (a wall's part), f, c, s.</summary>
    public static string KindPrefix(VisualTarget t) => t.Part switch
    {
        VisualPart.Floor => "f",
        VisualPart.Ceiling => "c",
        VisualPart.Sprite => "s",
        _ => "w",
    };

    /// <summary>The fields for a target: those without a prefix, and those of its kind ("w.panx": a wall's) or of its
    /// kind and picture ("w275.panx": a wall showing tile 275, read before anything is changed) with the prefix taken
    /// off (Texture Settings' tabs, each a kind of surface and a picture).</summary>
    public static IReadOnlyDictionary<string, int> ForKind(MapDocument d, VisualTarget t, IReadOnlyDictionary<string, int> f)
    {
        if (!f.Keys.Any(k => k.Contains('.')))
            return f;
        string kind = KindPrefix(t), withTile = kind + Tile(d, t).ToString(Inv);
        var own = new Dictionary<string, int>();
        foreach (var (k, v) in f)
        {
            int dot = k.IndexOf('.');
            if (dot < 0)
                own[k] = v;
            else if (k[..dot] == kind || k[..dot] == withTile)
                own[k[(dot + 1)..]] = v;
        }
        return own;
    }

    /// <summary>Ctrl+C's clip: what Ctrl+V puts on a surface of the same kind (the picture and all its settings:
    /// panning, repeats, shade, palette, flags), its kind (KindPrefix: a floor and a ceiling count as one) and a wall's
    /// length (a wall it goes onto takes the x repeat that keeps the picture's size along it).</summary>
    public static Dictionary<string, int> Clip(MapDocument d, VisualTarget t)
    {
        var f = Fields(d, t);
        f["kind"] = t.IsWall ? 0 : t.IsPlane ? 1 : 2;
        // (an opening's settings, masked or not, stay with its wall: Texture Settings sets them)
        foreach (var key in MaskKeys)
            f.Remove(key);
        if (t.IsWall)
            f["len"] = (int)Math.Round(WallLength(d, t.Wall));
        return f;
    }

    static double WallLength(MapDocument d, int w)
    {
        var (x1, y1, x2, y2) = BuildGeom.WallEnds(d, w);
        return Math.Sqrt((double)(x2 - x1) * (x2 - x1) + (double)(y2 - y1) * (y2 - y1));
    }

    /// <summary>A clip (Clip) put on a target: of the same kind, the picture and all its settings (a wall's x repeat
    /// scaled to its length); another kind, the picture alone.</summary>
    public static void Paste(MapDocument d, VisualTarget t, IReadOnlyDictionary<string, int> clip)
    {
        int kind = t.IsWall ? 0 : t.IsPlane ? 1 : 2;
        if (!clip.TryGetValue("kind", out int from) || from != kind)
        {
            if (clip.TryGetValue("tile", out int tile))
                SetFields(d, t, new Dictionary<string, int> { ["tile"] = tile });
            return;
        }
        var f = clip.Where(kv => kv.Key is not ("kind" or "len")).ToDictionary(kv => kv.Key, kv => kv.Value);
        if (t.IsWall && clip.TryGetValue("len", out int len) && len > 0 && f.TryGetValue("repx", out int rx))
            f["repx"] = Math.Clamp((int)Math.Round(rx * WallLength(d, t.Wall) / len), 1, 255);
        SetFields(d, t, f);
    }

    /// <summary>The picture key of what a target shows (a two-sided wall's middle: its masked picture).</summary>
    public static string PictureKey(MapDocument d, VisualTarget t) => t.Part switch
    {
        VisualPart.Floor => "tilefloor",
        VisualPart.Ceiling => "tileceiling",
        VisualPart.Sprite => "tilesprite",
        VisualPart.Middle when d.Walls[t.Wall].NextSector >= 0 => "tileover",
        _ => "tilewall",
    };

    /// <summary>Fields set on a target (those it has; "tile": its picture where the preview reads it; the picture
    /// keys (PictureKeys): only on their own kind).</summary>
    public static void SetFields(MapDocument d, VisualTarget t, IReadOnlyDictionary<string, int> f)
    {
        f = ForKind(d, t, f);
        foreach (var sc in Scrolling(t))
            if (f.TryGetValue(sc.key, out int sv))
                SetScroll(d, t, sc.key, sv);
        int Get(string k, int now) => f.TryGetValue(k, out int v) ? v : now;
        foreach (var (key, _) in PictureKeys)
        {
            if (!f.TryGetValue(key, out int pic) || Picture(d, t, key) == null)
                continue;
            pic = Byte(pic, 0, 32767);
            switch (key)
            {
                case "tilewall": Look(d, t).Picnum = pic; break;
                case "tileover": d.Walls[t.Wall].OverPicnum = pic; break;
                case "tilefloor": d.Sectors[t.Sector].FloorPicnum = pic; break;
                case "tileceiling": d.Sectors[t.Sector].CeilingPicnum = pic; break;
                case "tilesprite": Blood.SpriteExtents.SetPicture(d.Sprites[t.Sprite], pic); break;
            }
        }
        switch (t.Part)
        {
            case VisualPart.Floor:
            case VisualPart.Ceiling:
            {
                var s = d.Sectors[t.Sector];
                if (t.Part == VisualPart.Ceiling)
                {
                    s.CeilingPicnum = Byte(Get("tile", s.CeilingPicnum), 0, 32767);
                    s.CeilingXPanning = Wrap(Get("panx", s.CeilingXPanning));
                    s.CeilingYPanning = Wrap(Get("pany", s.CeilingYPanning));
                    s.CeilingShade = Byte(Get("shade", s.CeilingShade), -128, 127);
                    s.CeilingPal = Byte(Get("pal", s.CeilingPal));
                }
                else
                {
                    s.FloorPicnum = Byte(Get("tile", s.FloorPicnum), 0, 32767);
                    s.FloorXPanning = Wrap(Get("panx", s.FloorXPanning));
                    s.FloorYPanning = Wrap(Get("pany", s.FloorYPanning));
                    s.FloorShade = Byte(Get("shade", s.FloorShade), -128, 127);
                    s.FloorPal = Byte(Get("pal", s.FloorPal));
                }
                break;
            }
            case VisualPart.Sprite:
            {
                var sp = d.Sprites[t.Sprite];
                if (f.ContainsKey("tile"))
                    Blood.SpriteExtents.SetPicture(sp, Byte(f["tile"], 0, 32767));
                sp.XOffset = Byte(Get("panx", sp.XOffset), -128, 127);
                sp.YOffset = Byte(Get("pany", sp.YOffset), -128, 127);
                sp.XRepeat = Byte(Get("repx", sp.XRepeat), 1);
                sp.YRepeat = Byte(Get("repy", sp.YRepeat), 1);
                sp.Shade = Byte(Get("shade", sp.Shade), -128, 127);
                sp.Pal = Byte(Get("pal", sp.Pal));
                break;
            }
            default:
            {
                var look = Look(d, t);
                var w = d.Walls[t.Wall];
                if (t.Part == VisualPart.Middle && w.NextSector >= 0)
                    w.OverPicnum = Byte(Get("tile", w.OverPicnum), 0, 32767);
                else
                    look.Picnum = Byte(Get("tile", look.Picnum), 0, 32767);
                look.XPanning = Wrap(Get("panx", look.XPanning));
                look.YPanning = Wrap(Get("pany", look.YPanning));
                w.XRepeat = Byte(Get("repx", w.XRepeat), 1);
                w.YRepeat = Byte(Get("repy", w.YRepeat), 1);
                look.Shade = Byte(Get("shade", look.Shade), -128, 127);
                look.Pal = Byte(Get("pal", look.Pal));
                break;
            }
        }
        foreach (var (name, _) in Flags)
            if (f.TryGetValue(name, out int on))
                SetFlag(d, t, name, on != 0);
        if (t.IsWall && d.Walls[t.Wall].NextSector >= 0)
        {
            foreach (var key in MaskKeys)
                if (f.TryGetValue(key, out int mv))
                    SetMask(d, t.Wall, key, mv);
            // a masked opening's picture on both sides (the other side shows it too)
            var mw = d.Walls[t.Wall];
            if ((mw.Cstat & 16) != 0 && mw.NextWall >= 0 && (f.ContainsKey("tileover") || t.Part == VisualPart.Middle && f.ContainsKey("tile")))
                d.Walls[mw.NextWall].OverPicnum = mw.OverPicnum;
        }
    }

    /// <summary>The picture a target shows (a lower part with swapped bottoms: the wall behind's; a masked middle its mask).</summary>
    public static int Tile(MapDocument d, VisualTarget t)
    {
        switch (t.Part)
        {
            case VisualPart.Floor: return d.Sectors[t.Sector].FloorPicnum;
            case VisualPart.Ceiling: return d.Sectors[t.Sector].CeilingPicnum;
            case VisualPart.Sprite: return d.Sprites[t.Sprite].Picnum;
        }
        var w = d.Walls[t.Wall];
        return t.Part == VisualPart.Middle && w.NextSector >= 0 ? w.OverPicnum : Look(d, t).Picnum;
    }

    /// <summary>The z a wall part's picture is measured from (Build's: a white wall's ceiling, its floor aligned to the
    /// bottom; an upper part the next ceiling, its own aligned to the bottom; a lower part the next floor, its own
    /// ceiling aligned to the bottom (the bit of the wall whose picture it shows); a masked middle the lower ceiling,
    /// the higher floor aligned to the bottom).</summary>
    public static double ZRef(MapDocument d, VisualTarget t)
    {
        var w = d.Walls[t.Wall];
        var sec = d.Sectors[w.Sector];
        int ns = w.NextSector;
        if (ns < 0 || ns >= d.Sectors.Count)
            return (w.Cstat & 4) != 0 ? sec.FloorZ : sec.CeilingZ;
        var nsec = d.Sectors[ns];
        return t.Part switch
        {
            VisualPart.Upper => (w.Cstat & 4) != 0 ? sec.CeilingZ : nsec.CeilingZ,
            VisualPart.Lower => (Look(d, t).Cstat & 4) != 0 ? sec.CeilingZ : nsec.FloorZ,
            _ => (w.Cstat & 4) != 0 ? Math.Min(sec.FloorZ, nsec.FloorZ) : Math.Max(sec.CeilingZ, nsec.CeilingZ),
        };
    }

    static double Length(MapDocument d, int w)
    {
        var (x1, y1, x2, y2) = BuildGeom.WallEnds(d, w);
        return Math.Sqrt((double)(x2 - x1) * (x2 - x1) + (double)(y2 - y1) * (y2 - y1));
    }

    static int Mod(int v, int m) => ((v % m) + m) % m;

    /// <summary>
    /// Auto-align (UDB's, by Build's rules): from the start wall part along the walls joined at its corners, both ways
    /// (into the neighbouring sectors' walls at a corner too), each wall whose picture is the start's (the part of it
    /// showing it; a masked or one-way picture across an opening only along such pictures, a wall's only along wall
    /// parts) aligned to the one before (a masked picture's offsets being its wall's own, the wall's picture beside
    /// the opening moves with it): x: its x repeat keeping the start's pixels per unit of length, its panning
    /// carrying on where the one before ends (x flip as the start's); y: its y repeat the start's and its panning so a
    /// picture row lies at the same height (y flip as the start's). limit: only these walls (a selection); null: any
    /// with the picture. The start is left as it is. The number of parts aligned.
    /// </summary>
    // ---------------------------------------------------------------- floors and ceilings

    /// <summary>The flat bits that lay a floor's or ceiling's picture out: swap x and y (4), double size (8), the flips
    /// (16, 32), relative to the first wall (64).</summary>
    const int Layout = 4 | 8 | 16 | 32 | 64;

    static int StatOf(BSector s, bool ceiling) => ceiling ? s.CeilingStat : s.FloorStat;
    static void SetStat(BSector s, bool ceiling, int v) { if (ceiling) s.CeilingStat = v; else s.FloorStat = v; }
    static int PicOf(BSector s, bool ceiling) => ceiling ? s.CeilingPicnum : s.FloorPicnum;

    /// <summary>
    /// Ctrl+A on a floor or ceiling: its picture carried on into the sectors joined to it by red walls whose floor (or
    /// ceiling) has the same picture (not a parallaxed one), each in turn from the ones it touches, so no seam shows
    /// between them: each keeps its own relative-to-the-first-wall bit and takes the start's double size, the swap and
    /// flips that turn its picture the way the start's runs, and the panning that puts it where the start's lies. One
    /// whose picture can't be turned so (relative to a first wall that runs another way: Build turns a floor's picture
    /// only with its first wall, and only by quarter turns otherwise) is left. limit: only these
    /// sectors (a selection); null: any. The start is left as it is. The number aligned, and those left.
    /// </summary>
    public static (int aligned, int left) AlignPlanes(MapDocument d, VisualTarget start, HashSet<int> limit = null)
    {
        if (!start.IsPlane || start.Sector < 0 || start.Sector >= d.Sectors.Count)
            return (0, 0);
        bool ceiling = start.Part == VisualPart.Ceiling;
        int pic = PicOf(d.Sectors[start.Sector], ceiling);
        if ((StatOf(d.Sectors[start.Sector], ceiling) & 1) != 0)
            return (0, 0);
        var size = PreviewMesher.TileSize(pic);
        int aligned = 0, left = 0;
        var done = new HashSet<int> { start.Sector };
        var queue = new Queue<int>();
        queue.Enqueue(start.Sector);
        while (queue.Count > 0)
        {
            int a = queue.Dequeue();
            var sa = d.Sectors[a];
            int statA = StatOf(sa, ceiling);
            for (int w = sa.WallPtr; w < sa.WallPtr + sa.WallNum; w++)
            {
                int b = d.Walls[w].NextSector;
                if (b < 0 || b >= d.Sectors.Count || !done.Add(b))
                    continue;
                var sb = d.Sectors[b];
                if (PicOf(sb, ceiling) != pic || (StatOf(sb, ceiling) & 1) != 0 || (limit != null && !limit.Contains(b)) || !d.SectorEditable(b))
                    continue;
                // b keeps its own relative bit (a moving sector's floor aligned to its first wall stays so); the swap
                // and flips that turn its picture the way a's runs (the start's own first, as they are when both are
                // relative to first walls running the same way), a's double size; none: its first wall runs another way
                int oldStat = StatOf(sb, ceiling);
                int keep = (oldStat & ~Layout) | (oldStat & 64) | (statA & 8);
                var faA = new PreviewMesher.FlatMap(d, a, ceiling, size);
                var q = d.Walls[sb.WallPtr];
                var (ax0, ax1, ay1) = (faA.Uv(q.X, q.Y), faA.Uv(q.X + 1024, q.Y), faA.Uv(q.X, q.Y + 1024));
                int? found = null;
                foreach (int turn in new[] { statA & (4 | 16 | 32) }.Concat(Enumerable.Range(0, 8).Select(i => (i & 1) * 4 | (i & 2) * 8 | (i & 4) * 8)))
                {
                    SetStat(sb, ceiling, keep | turn);
                    var fb = new PreviewMesher.FlatMap(d, b, ceiling, size);
                    var (b0, bx1, by1) = (fb.Uv(q.X, q.Y), fb.Uv(q.X + 1024, q.Y), fb.Uv(q.X, q.Y + 1024));
                    if (((bx1 - b0) - (ax1 - ax0)).Length() < 1e-3f && ((by1 - b0) - (ay1 - ax0)).Length() < 1e-3f)
                    {
                        found = turn;
                        break;
                    }
                }
                if (found == null)
                {
                    SetStat(sb, ceiling, oldStat);
                    left++;
                    continue;
                }
                SetStat(sb, ceiling, keep | found.Value);
                // the panning that puts b's picture where a's lies: both read at a point (b's first point)
                var p = d.Walls[sb.WallPtr];
                var uvA = new PreviewMesher.FlatMap(d, a, ceiling, size).Uv(p.X, p.Y);
                if (ceiling)
                    sb.CeilingXPanning = sb.CeilingYPanning = 0;
                else
                    sb.FloorXPanning = sb.FloorYPanning = 0;
                var uvB = new PreviewMesher.FlatMap(d, b, ceiling, size).Uv(p.X, p.Y);
                int px = Wrap((int)Math.Round((uvA.X - uvB.X) * 256)), py = Wrap((int)Math.Round((uvA.Y - uvB.Y) * 256));
                if (ceiling)
                    (sb.CeilingXPanning, sb.CeilingYPanning) = (px, py);
                else
                    (sb.FloorXPanning, sb.FloorYPanning) = (px, py);
                aligned++;
                queue.Enqueue(b);
            }
        }
        return (aligned, left);
    }

    /// <summary>
    /// A floor or ceiling aligned to a wall of its sector: that wall made the sector's first wall and the plane
    /// relative to it, its panning 0, its flips and swap off (the picture's top edge along the wall, from its start).
    /// A sloped sector's slope turns about its first wall: refused there (why) unless the wall lies along the old one.
    /// </summary>
    public static string AlignPlaneToWall(MapDocument d, int wall, bool ceiling)
    {
        if (wall < 0 || wall >= d.Walls.Count)
            return "no wall";
        int s = d.Walls[wall].Sector;
        if (s < 0)
            return "the wall is in no sector";
        var sec = d.Sectors[s];
        bool sloped = (sec.FloorStat & 2) != 0 && sec.FloorHeinum != 0 || (sec.CeilingStat & 2) != 0 && sec.CeilingHeinum != 0;
        if (sloped && wall != sec.WallPtr)
        {
            var (ax, ay, bx, by) = BuildGeom.WallEnds(d, sec.WallPtr);
            var (cx, cy, ex, ey) = BuildGeom.WallEnds(d, wall);
            if ((long)(bx - ax) * (ey - cy) - (long)(by - ay) * (ex - cx) != 0)
                return $"sector {s} is sloped: its slope turns about its first wall (make the wall first by hand, Shift+F, to turn it)";
        }
        if (!d.SetFirstWall(s, wall))
            return "not the sector's wall";
        sec = d.Sectors[s];
        SetStat(sec, ceiling, (StatOf(sec, ceiling) & ~(4 | 16 | 32)) | 64);
        if (ceiling)
            sec.CeilingXPanning = sec.CeilingYPanning = 0;
        else
            sec.FloorXPanning = sec.FloorYPanning = 0;
        return null;
    }

    /// <summary>A two-sided wall's middle: the picture across its opening (masked or one-way).</summary>
    public static bool IsOpening(MapDocument d, VisualTarget t) =>
        t.Part == VisualPart.Middle && t.Wall >= 0 && t.Wall < d.Walls.Count && d.Walls[t.Wall].NextSector >= 0;

    public static int AutoAlign(MapDocument d, VisualTarget start, bool x, bool y, HashSet<int> limit = null)
    {
        if (!start.IsWall || start.Wall < 0 || start.Wall >= d.Walls.Count)
            return 0;
        // a masked picture along the masked openings next to it, a wall's picture along the walls' (not into an
        // opening showing the same tile, nor out of one)
        bool opening = IsOpening(d, start);
        int pic = Tile(d, start);
        var size = PreviewMesher.TileSize(pic);
        int width = Math.Max(1, size.X), height = Math.Max(1, Data.TileImages.WallHeight(size.Y));
        // the walls ending at each point (the walls starting at one are its chain)
        var endsAt = new Dictionary<int, List<int>>();
        for (int w = 0; w < d.Walls.Count; w++)
        {
            int p2 = d.Walls[w].Point2;
            if (p2 < 0 || p2 >= d.Walls.Count || d.PointOf[p2] < 0)
                continue;
            int e = d.PointOf[p2];
            (endsAt.TryGetValue(e, out var l) ? l : endsAt[e] = new List<int>()).Add(w);
        }
        var seen = new HashSet<int> { start.Wall };
        var todo = new Queue<(VisualTarget from, bool right)>();
        todo.Enqueue((start, true));
        todo.Enqueue((start, false));
        int done = 0;
        while (todo.Count > 0)
        {
            var (from, right) = todo.Dequeue();
            var fw = d.Walls[from.Wall];
            IEnumerable<int> next;
            if (right)
            {
                int p2 = fw.Point2;
                next = p2 >= 0 && p2 < d.Walls.Count && d.PointOf[p2] >= 0 ? d.Points[d.PointOf[p2]].Walls : Array.Empty<int>();
            }
            else
                next = d.PointOf[from.Wall] >= 0 && endsAt.TryGetValue(d.PointOf[from.Wall], out var l) ? l : new List<int>();
            foreach (int c in next.ToList())
            {
                if (c == from.Wall || c == fw.NextWall || seen.Contains(c) || (limit != null && !limit.Contains(c)))
                    continue;
                var part = Preview3D.PartsOf(d, c).Where(p => Tile(d, p) == pic && IsOpening(d, p) == opening).Select(p => (VisualTarget?)p).FirstOrDefault();
                if (part is not { } to)
                    continue;
                seen.Add(c);
                AlignTo(d, from, to, right, x, y, width, height);
                done++;
                todo.Enqueue((to, right));
            }
        }
        return done;
    }

    /// <summary>Part to aligned to part from (to follows from when right, else comes before it).</summary>
    static void AlignTo(MapDocument d, VisualTarget from, VisualTarget to, bool right, bool x, bool y, int width, int height)
    {
        var fw = d.Walls[from.Wall];
        var tw = d.Walls[to.Wall];
        var fl = Look(d, from);
        var tl = Look(d, to);
        if (x)
        {
            double lf = Length(d, from.Wall), lt = Length(d, to.Wall);
            if (lf > 0)
                tw.XRepeat = (int)Math.Clamp(Math.Round(fw.XRepeat * lt / lf), 1, 255);
            bool flip = (fw.Cstat & 8) != 0;
            tw.Cstat = flip ? tw.Cstat | 8 : tw.Cstat & ~8;
            // u runs xpanning to xpanning + xrepeat * 8 along a wall (flipped: back from xrepeat * 8 + xpanning)
            int xp = right
                ? flip ? fl.XPanning - tw.XRepeat * 8 : fl.XPanning + fw.XRepeat * 8
                : flip ? fl.XPanning + fw.XRepeat * 8 : fl.XPanning - tw.XRepeat * 8;
            tl.XPanning = Mod(Mod(xp, width), 256);
        }
        if (y)
        {
            tw.YRepeat = fw.YRepeat;
            tl.Cstat = (fl.Cstat & 256) != 0 ? tl.Cstat | 256 : tl.Cstat & ~256;
            // a row at z: (z - zref) * yrepeat / 2048 + ypanning * height / 256 pixels down the picture, the same on both
            double dz = ZRef(d, to) - ZRef(d, from);
            tl.YPanning = Mod(fl.YPanning + (int)Math.Round(dz * fw.YRepeat / (8.0 * height)), 256);
        }
    }

    /// <summary>"name=value,name=value" (the settings command's form).</summary>
    public static string Encode(IReadOnlyDictionary<string, int> f) => string.Join(",", f.Select(kv => $"{kv.Key}={kv.Value.ToString(Inv)}"));

    public static Dictionary<string, int> Decode(string s)
    {
        var f = new Dictionary<string, int>();
        foreach (var kv in s.Split(',', StringSplitOptions.RemoveEmptyEntries))
            if (kv.IndexOf('=') is var eq and > 0 && int.TryParse(kv[(eq + 1)..], NumberStyles.Integer, Inv, out int v))
                f[kv[..eq]] = v;
        return f;
    }
}
