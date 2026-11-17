// Chosen Blood module (GPL v2): what the info panel shows of Blood's points, walls, sectors and sprites.
using System;
using Chosen.Forge.Config;
using Chosen.Forge.Document;
using Chosen.Forge.Ui.InfoPanels;
using Chosen.Shared.Engine.Geometry;

namespace Chosen.Forge.Blood;

public static class Panels
{
    // positions and heights in the units shown (Preferences: game units, or Build's)
    static string Z(int z) => Units.Z(z);
    static string Xy(int x, int y) => $"{Units.Xy(x)}, {Units.Xy(y)}";

    public static void Vertex(InfoPanel p, int i)
    {
        var d = p.Doc;
        var pt = d.Points[i];
        var g = p.Column($"Vertex {i}");
        InfoPanel.Row(g, "Position", Xy(pt.X, pt.Y));
        InfoPanel.Row(g, "Walls", string.Join(" ", pt.Walls));
        var sectors = new System.Collections.Generic.SortedSet<int>();
        foreach (int w in pt.Walls)
            if (d.Walls[w].Sector >= 0)
                sectors.Add(d.Walls[w].Sector);
        InfoPanel.Row(g, "Sectors", string.Join(" ", sectors));
    }

    public static void Wall(InfoPanel p, int line)
    {
        var d = p.Doc;
        var l = d.Lines[line];
        var (ax, ay, bx, by) = BuildGeom.WallEnds(d, l.Front);
        double len = Math.Sqrt((double)(bx - ax) * (bx - ax) + (double)(by - ay) * (by - ay));
        int ang = (int)Math.Round(Math.Atan2(by - ay, bx - ax) * 1024 / Math.PI) & 2047;
        var g = p.Column(l.Back >= 0 ? $"Wall {l.Front} / {l.Back}" : $"Wall {l.Front}");
        InfoPanel.Row(g, "Length", Units.Length(len));
        InfoPanel.Row(g, "Angle", $"{ang} ({ang * 360 / 2048.0:0.#}°)");
        InfoPanel.Row(g, "From", Xy(ax, ay));
        InfoPanel.Row(g, "To", Xy(bx, by));
        Side(p, "Front", l.Front);
        if (l.Back >= 0)
            Side(p, "Back", l.Back);
    }

    static void Side(InfoPanel p, string title, int wi)
    {
        var w = p.Doc.Walls[wi];
        var g = p.Column($"{title}: wall {wi}, sector {w.Sector}");
        InfoPanel.Row(g, "Type", $"{w.Type} {BloodNames.Wall(w.Type)}");
        InfoPanel.Row(g, "Shade / pal", $"{w.Shade} / {w.Pal}");
        InfoPanel.Row(g, "Repeat", $"{w.XRepeat} x {w.YRepeat}");
        InfoPanel.Row(g, "Panning", $"{w.XPanning}, {w.YPanning}");
        InfoPanel.Row(g, "Flags", BloodNames.WallCstat(w.Cstat));
        if (w.Xw != null)
            InfoPanel.Row(g, "Channels", BloodNames.Channels(w.Xw.RxID, w.Xw.TxID) is { Length: > 0 } c ? c : "-");
        p.Preview(title, w.Picnum);
        if ((w.Cstat & 16) != 0 || w.NextWall >= 0)
            p.Preview("Over", w.OverPicnum);
    }

    public static void Sector(InfoPanel p, int s)
    {
        var d = p.Doc;
        var sec = d.Sectors[s];
        var g = p.Column($"Sector {s}");
        InfoPanel.Row(g, "Type", $"{sec.Type} {BloodNames.Sector(sec.Type)}");
        InfoPanel.Row(g, "Floor", Z(sec.FloorZ));
        InfoPanel.Row(g, "Ceiling", Z(sec.CeilingZ));
        InfoPanel.Row(g, "Height", Units.Height(sec.FloorZ - sec.CeilingZ));
        InfoPanel.Row(g, "Walls", $"{sec.WallNum} from {sec.WallPtr} (first)");
        InfoPanel.Row(g, "Visibility", sec.Visibility);
        if (sec.Xs != null)
            InfoPanel.Row(g, "Channels", BloodNames.Channels(sec.Xs.RxID, sec.Xs.TxID) is { Length: > 0 } c ? c : "-");
        var f = p.Column("Floor");
        InfoPanel.Row(f, "Shade / pal", $"{sec.FloorShade} / {sec.FloorPal}");
        InfoPanel.Row(f, "Slope", sec.FloorHeinum.ToString());
        InfoPanel.Row(f, "Flags", BloodNames.PlaneStat(sec.FloorStat));
        p.Preview("Floor", sec.FloorPicnum);
        var c2 = p.Column("Ceiling");
        InfoPanel.Row(c2, "Shade / pal", $"{sec.CeilingShade} / {sec.CeilingPal}");
        InfoPanel.Row(c2, "Slope", sec.CeilingHeinum.ToString());
        InfoPanel.Row(c2, "Flags", BloodNames.PlaneStat(sec.CeilingStat));
        p.Preview("Ceiling", sec.CeilingPicnum);
    }

    /// <summary>A dynamic light (Lights mode).</summary>
    public static void Light(InfoPanel p, int i)
    {
        var l = p.Doc.Lights[i];
        var g = p.Column($"Light {i}");
        InfoPanel.Row(g, "Kind", LightDialog.KindNames[(int)l.Kind].Split(" (")[0]);
        InfoPanel.Row(g, "Attached to", l.Attached ? $"sprite {l.Sprite}" + (l.Sprite < p.Doc.Sprites.Count ? $" ({BloodNames.Sprite(p.Doc.Sprites[l.Sprite].Type)})" : " (gone)") : "-");
        if (p.Doc.LightPlace(i) is { } at)
            InfoPanel.Row(g, "Shines from", $"{Xy((int)System.Math.Round(at.x), (int)System.Math.Round(at.y))}, {Z((int)System.Math.Round(at.z))}");
        InfoPanel.Row(g, "Colour", $"{l.R} {l.G} {l.B}");
        InfoPanel.Row(g, "Reach", Units.Xy(l.Radius));
        InfoPanel.Row(g, "Brightness", $"{l.Energy * 100:0}%" + (l.Flicker > 0 ? $", flicker {l.Flicker * 100:0}%" : ""));
        InfoPanel.Row(g, "Shadows", l.Shadows ? "yes" : "no");
        if (l.SectorLight)
            InfoPanel.Row(g, "Sector light", "follows it");
    }

    public static void Sprite(InfoPanel p, int i)
    {
        var sp = p.Doc.Sprites[i];
        var g = p.Column($"Sprite {i}");
        InfoPanel.Row(g, "Type", $"{sp.Type} {BloodNames.Sprite(sp.Type)}");
        InfoPanel.Row(g, "Position", $"{Xy(sp.X, sp.Y)}, {Z(sp.Z)}");
        InfoPanel.Row(g, "Angle", $"{sp.Ang} ({sp.Ang * 360 / 2048.0:0.#}°)");
        InfoPanel.Row(g, "Sector", sp.SectNum);
        InfoPanel.Row(g, "Shade / pal", $"{sp.Shade} / {sp.Pal}");
        InfoPanel.Row(g, "Repeat", $"{sp.XRepeat} x {sp.YRepeat}");
        InfoPanel.Row(g, "Flags", BloodNames.SpriteCstat(sp.Cstat));
        if (sp.Xsp is { } x)
        {
            var xg = p.Column("X-sprite");
            InfoPanel.Row(xg, "Channels", BloodNames.Channels(x.RxID, x.TxID) is { Length: > 0 } c ? c : "-");
            int[] data = { x.Data1, x.Data2, x.Data3, x.Data4 };
            var sounds = SoundLibrary.Fields(sp.Type);
            for (int n = 1; n <= 4; n++)
                InfoPanel.Row(xg, BloodNames.SpriteData(sp.Type, n) ?? $"Data {n}",
                    Array.IndexOf(sounds, n) >= 0 && data[n - 1] > 0 && SoundLibrary.Get(data[n - 1]) is { } snd
                        ? $"{data[n - 1]} ({snd.Name})" : data[n - 1].ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        p.Preview("Tile", sp.Picnum);
    }
}
