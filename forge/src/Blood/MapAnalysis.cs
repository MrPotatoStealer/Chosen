// Chosen Blood module (GPL v2): Map Analysis: Chosen.Blood's map rules and the checks a Blood mapper wants (the kinds
// xmapedit checks for: no player start, channels nobody sends or receives, markers without an owner, zero-length
// walls, two points of a loop at one place).
using System.Collections.Generic;
using System.Linq;
using Chosen.Forge.Document;
using Chosen.Forge.View;
using Chosen.Shared.Blood.Map;
using Chosen.Shared.Engine.Geometry;
using Godot;

namespace Chosen.Forge.Blood;

/// <summary>A finding: how bad, what, and where (the element to centre on); Fix: what Forge can do about it (null:
/// done, else why not), FixText saying what; Repair: the fix only mends the map's data (Fix All and Fix and save make
/// those; a type's need changes the level's design: its Fix is the mapper's choice, one at a time).</summary>
public sealed record Finding(Severity Severity, string Text, ElementKind Kind, int Index, Vector2 At,
    System.Func<MapDocument, string> Fix = null, string FixText = null, bool Repair = false);

public static class MapAnalysis
{
    public static List<Finding> Run(MapDocument d)
    {
        var l = new List<Finding>();
        foreach (var i in MapValidator.Check(d.Map, Limits.NBlood))
        {
            var (kind, index, at) = Where(d, i.Object, i.Index);
            var (fix, fixText) = FixFor(d, i);
            l.Add(new Finding(i.Severity, $"{i.Object} {i.Index}: {i.Message}", kind, index, at, fix, fixText, Repair: fix != null));
        }
        // sky ceilings with a sky of one piece (an empty map's header before Forge gave it Blood's sky): Blood's skies
        // are 16 tiles in a row, so the first alone goes round the view
        if (d.Map.PSkyBits == 0 && Enumerable.Range(0, d.Sectors.Count).FirstOrDefault(s => (d.Sectors[s].CeilingStat & 1) != 0, -1) is int sky and >= 0)
            l.Add(new Finding(Severity.Warning, $"the parallax sky has one piece (Map Options' sky tiles 0): Blood's skies are 16 tiles in a row, "
                + $"so tile {d.Sectors[sky].CeilingPicnum} alone goes round the view (sector {sky}'s ceiling and the other sky ceilings)",
                ElementKind.Sector, sky, d.Labels[sky], doc => { MapDocument.BloodSky(doc.Map); return null; },
                "give the map Blood's sky (16 pieces, offsets 0 to 15, parallax type 2)"));
        // visibility 0: no darkening with distance (Blood's maps have 752..800), so a shade below 0, which only holds that
        // darkening back, does nothing
        if (d.Map.Visibility == 0 && d.Sectors.Count > 0)
            l.Add(new Finding(Severity.Warning, "the map's visibility is 0 (Map Options): nothing darkens with distance, and shades below 0 "
                + "(which only hold that darkening back) look the same as 0; Blood's maps have 752 to 800",
                ElementKind.None, -1, Vector2.Zero, doc => { doc.Map.Visibility = 800; return null; }, "set the visibility to 800, as Blood's maps"));
        // a player start (type 1: single player)
        if (!d.Sprites.Any(s => s.Type == 1))
            l.Add(new Finding(Severity.Warning, "no player start (a sprite of type 1)", ElementKind.None, -1, Vector2.Zero));
        // channels: received with nobody sending, sent with nobody receiving (100 and up: Blood's own below)
        var tx = new HashSet<int>();
        var rx = new Dictionary<int, (ElementKind, int, Vector2)>();
        void Tx(int c)
        {
            if (c >= 100)
                tx.Add(c);
        }
        for (int s = 0; s < d.Sectors.Count; s++)
            if (d.Sectors[s].Xs is { } x)
            {
                Tx(x.TxID);
                if (x.RxID >= 100)
                    rx.TryAdd(x.RxID, (ElementKind.Sector, s, d.Labels[s]));
            }
        for (int w = 0; w < d.Walls.Count; w++)
            if (d.Walls[w].Xw is { } x)
            {
                Tx(x.TxID);
                if (x.RxID >= 100)
                    rx.TryAdd(x.RxID, (ElementKind.Line, d.LineOf[w], new Vector2(d.Walls[w].X, d.Walls[w].Y)));
            }
        for (int i = 0; i < d.Sprites.Count; i++)
            if (d.Sprites[i].Xsp is { } x)
            {
                Tx(x.TxID);
                // (a modern map's random and sequential TX switches send on their data fields' channels)
                foreach (int c in ModernTypes.DataChannels(d.Sprites[i]))
                    Tx(c);
                if (x.RxID >= 100)
                    rx.TryAdd(x.RxID, (ElementKind.Sprite, i, new Vector2(d.Sprites[i].X, d.Sprites[i].Y)));
            }
        foreach (var (c, (kind, index, at)) in rx.OrderBy(kv => kv.Key))
            if (!tx.Contains(c))
                l.Add(new Finding(Severity.Warning, $"channel {c} is received ({kind.ToString().ToLowerInvariant()} {index}) but nothing sends it", kind, index, at));
        foreach (int c in tx.OrderBy(c => c))
            if (!rx.ContainsKey(c))
                l.Add(new Finding(Severity.Warning, $"channel {c} is sent but nothing receives it", ElementKind.None, -1, Vector2.Zero));
        // what types need to work (Blood.Requirements; advice as "maybe")
        foreach (var (kind, index, need) in Requirements.Unmet(d))
        {
            string what = kind switch
            {
                ElementKind.Sector => $"sector {index}" + (d.Sectors[index].Type != 0 ? $" ({BloodNames.Sector(d.Sectors[index].Type)})" : ""),
                ElementKind.Line => $"wall {index}",
                _ => $"sprite {index} ({BloodNames.Sprite(d.Sprites[index].Type)})",
            };
            var at = kind switch
            {
                ElementKind.Sector => d.Labels[index],
                ElementKind.Line => new Vector2(d.Walls[index].X, d.Walls[index].Y),
                _ => new Vector2(d.Sprites[index].X, d.Sprites[index].Y),
            };
            var nf = need.Fix;
            l.Add(new Finding(Severity.Warning, $"{what} {(need.Advice ? "may need" : "needs")} {need.Text}", kind,
                kind == ElementKind.Line ? d.LineOf[index] : index, at, nf == null ? null : doc => { nf(doc); return null; }, need.FixText));
        }
        // markers whose owner isn't a sector with an x-sector (the game deletes them)
        for (int i = 0; i < d.Sprites.Count; i++)
        {
            var s = d.Sprites[i];
            int mi = i;
            if (s.StatNum == 10 && (s.Owner < 0 || s.Owner >= d.Sectors.Count || d.Sectors[s.Owner].Xs == null))
                l.Add(new Finding(Severity.Warning, $"marker sprite {i} has no sector with an x-sector owning it", ElementKind.Sprite, i, new Vector2(s.X, s.Y),
                    doc => { doc.DeleteSprites(new[] { mi }); return null; }, "delete it (Blood does when the level starts)", Repair: true));
        }
        // walls of no length; two points of one loop at one place
        for (int w = 0; w < d.Walls.Count; w++)
        {
            var (ax, ay, bx, by) = BuildGeom.WallEnds(d, w);
            // (its start point taken out: the walls round it joined, the shape as it was)
            int zw = w;
            if (ax == bx && ay == by)
                l.Add(new Finding(Severity.Warning, $"wall {w} has no length", ElementKind.Line, d.LineOf[w], new Vector2(ax, ay),
                    doc => doc.DeletePoint(doc.PointOf[zw]), "take out its start point (the walls on either side joined: the shape stays)", Repair: true));
        }
        for (int p = 0; p < d.Points.Count; p++)
            if (d.PointOnOwnLoop(p))
            {
                // two points of a loop at one place because a wall of no length runs between them: that wall's fix
                int zero = d.Points[p].Walls.FirstOrDefault(w =>
                {
                    var (ax, ay, bx, by) = BuildGeom.WallEnds(d, w);
                    return ax == bx && ay == by;
                }, -1);
                l.Add(new Finding(Severity.Warning, $"vertex {p} at {d.Points[p].X}, {d.Points[p].Y}: two points of one loop there", ElementKind.Point, p,
                    new Vector2(d.Points[p].X, d.Points[p].Y),
                    zero < 0 ? null : doc => doc.DeletePoint(doc.PointOf[zero]),
                    zero < 0 ? null : $"take out the point of wall {zero}, which has no length", Repair: zero >= 0));
            }
        return l;
    }

    /// <summary>What Forge can do about a rule's finding (null: nothing it can be sure of: the shape's own problems are
    /// the mapper's to mend).</summary>
    static (System.Func<MapDocument, string> fix, string text) FixFor(MapDocument d, Issue i)
    {
        int n = i.Index;
        switch (i.Kind, i.Object)
        {
            // a sprite of no sector (NBlood stops on it): the one it lies in, a marker its owner's, else the nearest
            case ("sector", "sprite") when i.Severity == Severity.Error && n >= 0 && n < d.Sprites.Count:
                return (doc =>
                {
                    var s = doc.Sprites[n];
                    int was = s.SectNum;
                    s.SectNum = -1;
                    doc.UpdateSpriteSector(n);
                    if (s.SectNum < 0)
                        s.SectNum = s.StatNum == 10 && s.Owner >= 0 && s.Owner < doc.Sectors.Count ? s.Owner : MapPaste.NearestSector(doc, s.X, s.Y);
                    if (s.SectNum >= 0)
                        return null;
                    s.SectNum = was;
                    return "the map has no sector to put it in";
                }, "put it in the sector it lies in (a marker: its owner's; else the nearest)");
            // stored in one sector, lying in another: the one it lies in
            case ("sector", "sprite") when i.Message.Contains("but lies in") && n >= 0 && n < d.Sprites.Count:
                return (doc => { doc.UpdateSpriteSector(n); return null; }, "store it in the sector it lies in");
            case ("reference", "sector") when n >= 0 && n < d.Sectors.Count && d.Sectors[n].Xs != null:
                return (doc => { doc.Sectors[n].Xs.Reference = n; return null; }, "set the reference right");
            case ("reference", "wall") when n >= 0 && n < d.Walls.Count && d.Walls[n].Xw != null:
                return (doc => { doc.Walls[n].Xw.Reference = n; return null; }, "set the reference right");
            case ("reference", "sprite") when n >= 0 && n < d.Sprites.Count && d.Sprites[n].Xsp != null:
                return (doc => { doc.Sprites[n].Xsp.Reference = n; return null; }, "set the reference right");
            case ("marker", "sprite") when n >= 0 && n < d.Sprites.Count:
                return (doc => { doc.DeleteSprites(new[] { n }); return null; }, "delete it (Blood does when the level starts)");
        }
        return (null, null);
    }

    /// <summary>The repairs made (findings whose fix only mends the map's data), one at a time (the map checked again
    /// after each: a fix renumbers, or mends another finding too), each finding tried once; how many were fixed, and
    /// the fixes refused.</summary>
    public static (int fixedCount, List<string> refused) FixAll(MapDocument d)
    {
        var tried = new HashSet<string>();
        var refused = new List<string>();
        int done = 0;
        for (int round = 0; round < 1000; round++)
        {
            var f = Run(d).FirstOrDefault(x => x.Repair && x.Fix != null && !tried.Contains(x.Text));
            if (f == null)
                break;
            tried.Add(f.Text);
            if (f.Fix(d) is { } why)
                refused.Add($"{f.Text}: {why}");
            else
                done++;
            d.NotifyChanged();
        }
        return (done, refused);
    }

    /// <summary>The element a rule's finding is about (a wall as its line) and where it is.</summary>
    static (ElementKind kind, int index, Vector2 at) Where(MapDocument d, string obj, int i) => obj switch
    {
        "sector" when i >= 0 && i < d.Sectors.Count => (ElementKind.Sector, i, d.Labels[i]),
        "wall" when i >= 0 && i < d.Walls.Count => (ElementKind.Line, d.LineOf[i], new Vector2(d.Walls[i].X, d.Walls[i].Y)),
        "sprite" when i >= 0 && i < d.Sprites.Count => (ElementKind.Sprite, i, new Vector2(d.Sprites[i].X, d.Sprites[i].Y)),
        _ => (ElementKind.None, -1, Vector2.Zero),
    };
}
