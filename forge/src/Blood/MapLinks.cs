// Chosen Blood module (GPL v2): what a Blood map's elements are tied to, for the 2D view's link lines (UDB's event
// lines): channels (an x-object's TX id reaching every x-object with that RX id: NBlood's evSend, 100 and up the
// map's own), a room-over-room link's two markers (warp.cpp: an upper and a lower marker with the same data1), a
// sector's markers (its sliding off and on places, its axis, a teleporter's destination: owned by the sector).
using System.Collections.Generic;
using System.Linq;
using Chosen.Forge.Document;
using Chosen.Forge.View;

namespace Chosen.Forge.Blood;

public static class MapLinks
{
    /// <summary>Every link of the map (a channel sent by several and received by several: each pair).</summary>
    public static List<MapLink> All(MapDocument d)
    {
        var l = new List<MapLink>();
        // channels: who sends and who receives each (Blood's own below 100: the level's end, secrets, the game's)
        var senders = new Dictionary<int, List<Element>>();
        var receivers = new Dictionary<int, List<Element>>();
        void Add(Dictionary<int, List<Element>> to, int c, Element e)
        {
            if (c < 100)
                return;
            if (!to.TryGetValue(c, out var list))
                to[c] = list = new List<Element>();
            list.Add(e);
        }
        for (int s = 0; s < d.Sectors.Count; s++)
            if (d.Sectors[s].Xs is { } x)
            {
                Add(senders, x.TxID, new Element(ElementKind.Sector, s));
                Add(receivers, x.RxID, new Element(ElementKind.Sector, s));
            }
        for (int w = 0; w < d.Walls.Count; w++)
            if (d.Walls[w].Xw is { } x && w < d.LineOf.Length && d.LineOf[w] >= 0)
            {
                Add(senders, x.TxID, new Element(ElementKind.Line, d.LineOf[w]));
                Add(receivers, x.RxID, new Element(ElementKind.Line, d.LineOf[w]));
            }
        for (int i = 0; i < d.Sprites.Count; i++)
            if (d.Sprites[i].Xsp is { } x)
            {
                Add(senders, x.TxID, new Element(ElementKind.Sprite, i));
                foreach (int c in ModernTypes.DataChannels(d.Sprites[i]))
                    Add(senders, c, new Element(ElementKind.Sprite, i));
                Add(receivers, x.RxID, new Element(ElementKind.Sprite, i));
            }
        foreach (var (c, from) in senders)
            if (receivers.TryGetValue(c, out var to))
                foreach (var a in from.Distinct())
                    foreach (var b in to.Distinct())
                        if (a != b)
                            l.Add(new MapLink(a, b, c.ToString(), LinkKind.Channel));
        // a room over a room: its upper marker and its lower one (both ways), with the two sectors
        int n = d.Sectors.Count;
        for (int u = 0; u < d.Sprites.Count; u++)
        {
            var su = d.Sprites[u];
            if (System.Array.IndexOf(AutoLayers.Upper, su.Type) < 0 || su.Xsp == null)
                continue;
            for (int o = 0; o < d.Sprites.Count; o++)
            {
                var so = d.Sprites[o];
                if (System.Array.IndexOf(AutoLayers.Lower, so.Type) < 0 || so.Xsp == null || so.Xsp.Data1 != su.Xsp.Data1)
                    continue;
                l.Add(new MapLink(new Element(ElementKind.Sprite, u), new Element(ElementKind.Sprite, o), $"link {su.Xsp.Data1}",
                    LinkKind.Link, su.SectNum >= 0 && su.SectNum < n ? su.SectNum : -1, so.SectNum >= 0 && so.SectNum < n ? so.SectNum : -1));
            }
        }
        // a sector's markers (status list 10, owned by it)
        for (int i = 0; i < d.Sprites.Count; i++)
        {
            var sp = d.Sprites[i];
            if (sp.StatNum != 10 || sp.Owner < 0 || sp.Owner >= n)
                continue;
            l.Add(new MapLink(new Element(ElementKind.Sector, sp.Owner), new Element(ElementKind.Sprite, i), MarkerName(sp.Type), LinkKind.Marker));
        }
        return l;
    }

    /// <summary>The focus's links and how far along a chain each is (1: the focus's own): with chains, channels
    /// followed from the focus both ways (receivers' own sends onward, senders' senders back), each element once; a
    /// room link or a marker only where the focus is an end (or one of the link's rooms).</summary>
    public static Dictionary<MapLink, int> Focus(List<MapLink> all, HashSet<Element> focus, bool chains)
    {
        var on = new Dictionary<MapLink, int>();
        bool Mine(MapLink l) => focus.Contains(l.From) || focus.Contains(l.To)
            || (l.SectorA >= 0 && focus.Contains(new Element(ElementKind.Sector, l.SectorA)))
            || (l.SectorB >= 0 && focus.Contains(new Element(ElementKind.Sector, l.SectorB)));
        foreach (var l in all)
            if (Mine(l))
                on[l] = 1;
        if (!chains || focus.Count == 0)
            return on;
        var channels = all.Where(l => l.Kind == LinkKind.Channel).ToList();
        var byFrom = channels.ToLookup(l => l.From);
        var byTo = channels.ToLookup(l => l.To);
        foreach (bool forward in new[] { true, false })
        {
            var seen = new HashSet<Element>(focus);
            var frontier = focus.ToList();
            for (int depth = 1; frontier.Count > 0 && depth <= 32; depth++)
            {
                var next = new List<Element>();
                foreach (var e in frontier)
                    foreach (var l in forward ? byFrom[e] : byTo[e])
                    {
                        if (!on.ContainsKey(l))
                            on[l] = depth;
                        var other = forward ? l.To : l.From;
                        if (seen.Add(other))
                            next.Add(other);
                    }
                frontier = next;
            }
        }
        return on;
    }

    static string MarkerName(int type) => type switch
    {
        3 => "off",
        4 => "on",
        5 => "axis",
        8 => "destination",
        _ => BloodNames.Sprite(type),
    };
}
