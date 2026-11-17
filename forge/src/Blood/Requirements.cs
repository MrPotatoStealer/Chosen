// Chosen Blood module (GPL v2): what Blood's sector, wall and sprite types need to work (NBlood's triggers.cpp, db.cpp's
// PropagateMarkerReferences, asound.cpp's ambient sounds, sfx sectors' SectorStartSound: a turning sector without its
// axis marker, a switch with no TX, an ambient sound whose radii are the wrong way round do nothing in the game).
using System;
using System.Collections.Generic;
using System.Linq;
using Chosen.Forge.Document;
using Chosen.Shared.Blood.Map;
using Chosen.Shared.Engine.Geometry;

namespace Chosen.Forge.Blood;

/// <summary>One thing a type needs: met or not, in words, the x-field it is about (the dialog marks it), whether it
/// is only advice (works without, but likely not as meant), and a fix Forge can make.</summary>
public sealed record Need(bool Met, string Text, string Field = null, bool Advice = false, string FixText = null,
    Func<MapDocument, int> Fix = null);

public static class Requirements
{
    static readonly HashSet<int> Movers = new() { 600, 602, 612, 613, 614, 615, 616, 617 };

    /// <summary>Who sends and receives each channel (sectors, walls, sprites).</summary>
    public sealed class Channels
    {
        public readonly Dictionary<int, int> Tx = new(), Rx = new();

        public Channels(MapDocument d)
        {
            void Add(int rx, int tx)
            {
                if (rx > 0)
                    Rx[rx] = Rx.GetValueOrDefault(rx) + 1;
                if (tx > 0)
                    Tx[tx] = Tx.GetValueOrDefault(tx) + 1;
            }
            foreach (var s in d.Sectors)
                if (s.Xs is { } x)
                    Add(x.RxID, x.TxID);
            foreach (var w in d.Walls)
                if (w.Xw is { } x)
                    Add(x.RxID, x.TxID);
            foreach (var sp in d.Sprites)
                if (sp.Xsp is { } x)
                {
                    Add(x.RxID, x.TxID);
                    foreach (int c in ModernTypes.DataChannels(sp))
                        Add(0, c);
                }
        }

        /// <summary>Something sends on channel c (the game's own below 100: it does).</summary>
        public bool Sent(int c) => c > 0 && (c < 100 || Tx.ContainsKey(c));
        public bool Received(int c) => c > 0 && (c < 100 || Rx.ContainsKey(c));
    }

    static void ChannelNeeds(List<Need> l, Channels c, int rx, int tx)
    {
        if (rx >= 100)
            l.Add(new Need(c.Sent(rx), $"something sending channel {rx} (its RX): else it never fires", "RxID", Advice: true));
        if (tx >= 100)
            l.Add(new Need(c.Received(tx), $"something listening to channel {tx} (its TX)", "TxID", Advice: true));
    }

    static string Name(int markerType) => markerType switch
    {
        3 => "an off marker (type 3: where it is off)",
        4 => "an on marker (type 4: where it is on)",
        5 => "an axis marker (type 5: what it turns about)",
        8 => "a destination marker (type 8: where it sends you)",
        _ => $"a marker of type {markerType}",
    };

    // ---------------------------------------------------------------- sectors

    public static List<Need> Sector(MapDocument d, int s, Channels ch = null)
    {
        var l = new List<Need>();
        var sec = d.Sectors[s];
        var x = sec.Xs;
        if (sec.Type == 0 && x == null)
            return l;
        ch ??= new Channels(d);
        // markers (owned by the sector: Blood finds them from their owners as it loads)
        var (m0, m1) = SectorMarkers.For(sec.Type);
        foreach (var (mt, field) in new[] { (m0, "Marker0"), (m1, "Marker1") })
            if (mt != 0)
            {
                bool has = d.Sprites.Any(sp => sp.StatNum == 10 && sp.Owner == s && sp.Type == mt);
                l.Add(new Need(has, Name(mt), field, FixText: "Make it", Fix: doc => SectorMarkers.Fix(doc, s)));
            }
        // how far it turns or slides: a turning sector by its axis marker's angle (a step-turning one each step), a
        // sliding one from its off marker to its on marker (TranslateSector: from marker 1's place and angle to marker
        // 2's); the same, and it doesn't move
        int Marker(int type) => Enumerable.Range(0, d.Sprites.Count).FirstOrDefault(i => d.Sprites[i].StatNum == 10 && d.Sprites[i].Owner == s
            && d.Sprites[i].Type == type, -1);
        if (sec.Type is 613 or 615 or 617 && Marker(5) is var axis and >= 0)
            l.Add(new Need(d.Sprites[axis].Ang != 0,
                $"an angle on its axis marker (sprite {axis}): how far it turns{(sec.Type == 613 ? " each step" : "")}, 512 a quarter turn (0: it doesn't turn)",
                FixText: "Set 90°", Fix: doc =>
                {
                    doc.Sprites[axis].Ang = 512;
                    return 1;
                }));
        if (sec.Type is 614 or 616 && Marker(3) is var off and >= 0 && Marker(4) is var on and >= 0)
        {
            var (a0, a1) = (d.Sprites[off], d.Sprites[on]);
            l.Add(new Need(a0.X != a1.X || a0.Y != a1.Y || a0.Ang != a1.Ang,
                $"its on marker (sprite {on}) somewhere else than its off marker (sprite {off}): it slides from one to the other"));
        }
        // a turning or sliding sector's floor or ceiling aligned to its first wall: the picture is laid from that wall's
        // two points, which Blood rounds to whole units each frame as it turns them; a short first wall tips the
        // picture by up to 1 / its length, so across a long sector it wobbles (400 long on 3376: half a texel). Its
        // longest wall first keeps it still (a sloped sector's slopes turn about the first wall: no fix offered there)
        if (sec.Type is >= 613 and <= 617 && ((sec.FloorStat | sec.CeilingStat) & 64) != 0)
        {
            double Len(int w)
            {
                var (ax, ay, bx, by) = BuildGeom.WallEnds(d, w);
                return Math.Sqrt((double)(bx - ax) * (bx - ax) + (double)(by - ay) * (by - ay));
            }
            var outer = BuildGeom.OuterLoop(d, s);
            if (outer.Count > 0)
            {
                int longest = outer.OrderByDescending(Len).First();
                // (sloped: the flag with a slope; a parallaxed ceiling often has the flag and none)
                bool sloped = (sec.FloorStat & 2) != 0 && sec.FloorHeinum != 0 || (sec.CeilingStat & 2) != 0 && sec.CeilingHeinum != 0;
                l.Add(new Need(Len(sec.WallPtr) * 2 >= Len(longest),
                    $"a long first wall (wall {sec.WallPtr} is {Len(sec.WallPtr):0}, wall {longest} {Len(longest):0}): its picture aligned to the first wall "
                    + "wobbles as it moves when that wall is short (Blood rounds its points each frame)", Advice: true,
                    FixText: sloped ? null : $"Make wall {longest} first",
                    Fix: sloped ? null : doc => doc.SetFirstWall(s, longest) ? 1 : 0));
            }
        }
        if (x == null)
        {
            if (sec.Type != 0)
                l.Add(new Need(false, "an x-sector (its triggers and timing)", null, FixText: "Add one", Fix: doc =>
                {
                    doc.EnsureXSector(s);
                    return 1;
                }));
            return l;
        }
        ChannelNeeds(l, ch, x.RxID, x.TxID);
        if (Movers.Contains(sec.Type) || sec.Type == 604)
        {
            bool trigger = x.Enter != 0 || x.Exit != 0 || x.Push != 0 || x.Wallpush != 0 || x.Vector != 0;
            bool selfMoving = x.State != 0 && (x.ReTriggerA != 0 || x.ReTriggerB != 0) || sec.Type == 612 && x.State != 0;
            l.Add(new Need(trigger || x.RxID > 0 || selfMoving,
                "something to set it off: an RX channel, or Entered / Left / Used / Wall used / Shot", "RxID"));
        }
        if (sec.Type is 600 or 602)
            l.Add(new Need(x.OffFloorZ != x.OnFloorZ || x.OffCeilZ != x.OnCeilZ, "its off and on heights apart (else it doesn't move)", "OnFloorZ"));
        if (Movers.Contains(sec.Type) && sec.Type != 612)
            l.Add(new Need(x.BusyTimeA > 0 || x.BusyTimeB > 0, "a busy time (0: it jumps at once)", "BusyTimeA", Advice: true));
        if (sec.Type == 614)
        {
            bool marked = Enumerable.Range(sec.WallPtr, sec.WallNum).Any(w => (d.Walls[w].Cstat & (16384 | 32768)) != 0)
                || d.Sprites.Any(sp => sp.SectNum == s && (sp.Cstat & (8192 | 16384)) != 0);
            l.Add(new Need(marked, "walls or sprites marked to move with it (a wall's flags 16384 / 32768, a sprite's 8192 / 16384)",
                Advice: true));
        }
        if (sec.Type == 612)
            l.Add(new Need(x.Data > 0 && d.Sprites.Any(sp => sp.Type == 15 && sp.Xsp?.Data1 == x.Data),
                $"a path marker (type 15) with id {x.Data} (its Data 1) to start at", "Data"));
        if (sec.Type == 619)
        {
            l.Add(new Need(x.Data > 0, "the sprite type it counts (Data)", "Data"));
            l.Add(new Need(x.TxID > 0, "a TX channel to send when they are all gone", "TxID"));
        }
        // effects missing their other half
        if (x.Wave != 0 || x.Amplitude != 0)
        {
            l.Add(new Need(x.ShadeFloor != 0 || x.ShadeCeiling != 0 || x.ShadeWalls != 0, "what it lights: Light floor, ceiling or walls", "ShadeFloor"));
            l.Add(new Need(x.Amplitude != 0, "a light amplitude (0: no change)", "Amplitude", Advice: true));
            if (x.Wave != 0)
                l.Add(new Need(x.Freq != 0, "a light frequency (0: it doesn't change)", "Freq", Advice: true));
        }
        if (x.PanVel != 0)
            l.Add(new Need(x.PanFloor != 0 || x.PanCeiling != 0 || x.Drag != 0, "what scrolls: Pan floor or ceiling", "PanFloor"));
        if (x.BobZRange != 0)
        {
            l.Add(new Need(x.BobSpeed != 0, "a bob speed", "BobSpeed"));
            l.Add(new Need(x.BobFloor != 0 || x.BobCeiling != 0, "what bobs: Bob floor or ceiling", "BobFloor"));
        }
        return l;
    }

    // ---------------------------------------------------------------- walls

    public static List<Need> Wall(MapDocument d, int w, Channels ch = null)
    {
        var l = new List<Need>();
        var wall = d.Walls[w];
        if (wall.Xw is not { } x)
            return l;
        ch ??= new Channels(d);
        ChannelNeeds(l, ch, x.RxID, x.TxID);
        if ((x.TriggerPush != 0 || x.TriggerVector != 0 || x.TriggerTouch != 0) && wall.Type == 0)
            l.Add(new Need(x.TxID > 0 || (wall.NextSector >= 0 && d.Sectors[wall.NextSector].Xs is { Wallpush: not 0 }),
                "a TX channel to send when it is used, shot or touched", "TxID", Advice: true));
        return l;
    }

    // ---------------------------------------------------------------- sprites

    static readonly (int a, int b)[] LinkPairs = { (6, 7), (9, 10), (11, 12), (13, 14) };

    public static List<Need> Sprite(MapDocument d, int i, Channels ch = null)
    {
        var l = new List<Need>();
        var sp = d.Sprites[i];
        var x = sp.Xsp;
        ch ??= new Channels(d);
        if (x != null)
            ChannelNeeds(l, ch, x.RxID, x.TxID);
        int t = sp.Type;
        bool Triggered() => x != null && (x.Push != 0 || x.Touch != 0 || x.Vector != 0 || x.Impact != 0 || x.Sight != 0 || x.Proximity != 0
            || ch.Sent(x.RxID));
        bool Started() => x != null && (x.State != 0 || x.RxID > 0);
        Need NoX(string what) => new(false, $"an x-sprite ({what})", null, FixText: "Add one", Fix: doc =>
        {
            doc.EnsureXSprite(i);
            return 1;
        });
        switch (t)
        {
            case >= 20 and <= 23:
                if (x == null)
                {
                    l.Add(NoX("what it switches"));
                    break;
                }
                l.Add(new Need(x.TxID > 0, "a TX channel: what it switches (else it only clicks)", "TxID", Advice: true));
                l.Add(new Need(Triggered() || x.RxID > 0, "a way to use it: Used (the player's Space), Touched, Shot... or an RX channel", "Push"));
                // (Max 0: a count that never goes round, reaching Goal once)
                if (t == 22 && x.Data3 > 0)
                    l.Add(new Need(x.Data2 < x.Data3, "a Goal (Data 2) below Max (Data 3): the count goes round at Max", "Data2"));
                break;
            case 710:
                if (x == null)
                {
                    l.Add(NoX("its sound and radii"));
                    break;
                }
                l.Add(new Need(x.Data3 > 0 && (SoundLibrary.All.Count == 0 || SoundLibrary.Get(x.Data3) != null), "a sound (Data 3)", "Data3"));
                l.Add(new Need(x.Data1 < x.Data2, "Radius1 (Data 1) less than Radius2 (Data 2): else Blood skips it", "Data2"));
                l.Add(new Need(x.Data4 > 0, "a volume (Data 4)", "Data4"));
                l.Add(new Need(Started(), "On (State), or an RX channel turning it on: it plays only while on", "State"));
                break;
            case 709:
                if (x == null)
                {
                    l.Add(NoX("its sounds"));
                    break;
                }
                l.Add(new Need(x.Data1 > 0 || x.Data2 > 0 || x.Data3 > 0 || x.Data4 > 0, "a sound for it to play (Data 1..4)", "Data1"));
                l.Add(new Need(sp.SectNum >= 0 && sp.SectNum < d.Sectors.Count && d.Sectors[sp.SectNum].Xs != null,
                    "a sector with an x-sector (a door, a lift...): it plays as its sector goes on and off"));
                l.Add(new Need(sp.StatNum == 0, "status list 0 (decoration): Blood plays only those", Advice: true));
                break;
            case >= 700 and <= 708:
                if (x == null)
                {
                    l.Add(NoX("its timing"));
                    break;
                }
                l.Add(new Need(x.State != x.RestState || x.RxID > 0, "On (State) unlike Rests on, or an RX channel: to start it", "State"));
                l.Add(new Need(x.BusyTime > 0, "a busy time: how often it repeats (0: once)", "BusyTime", Advice: true));
                if (t == 708)
                    l.Add(new Need(x.Data2 > 0, "a sound (Data 2)", "Data2"));
                break;
            case 18:
                if (x == null)
                {
                    l.Add(NoX("what it spawns"));
                    break;
                }
                l.Add(new Need(x.Data1 > 0 || x.Data2 > 0 || x.Data3 > 0 || x.Data4 > 0, "an enemy to spawn (Data 1..4)", "Data1"));
                l.Add(new Need(x.RxID > 0, "an RX channel: when to spawn", "RxID"));
                break;
            case 19:
                l.Add(new Need(x != null && x.RxID > 0, "an RX channel: when to shake", "RxID"));
                break;
            case 1 or 2:
                if (x == null)
                {
                    l.Add(NoX("which player"));
                    break;
                }
                int others = d.Sprites.Count(o => o != sp && o.Type == t && o.Xsp is { } ox && ox.Data1 == x.Data1);
                l.Add(new Need(others == 0, $"the only {(t == 1 ? "player" : "bloodbath")} start for player {x.Data1 + 1} (Data 1)", "Data1",
                    Advice: t == 2));
                break;
            case 15:
                if (x == null)
                    break;
                l.Add(new Need(d.Sprites.Any(o => o.Type == 15 && o.Xsp is { } ox && ox.Data1 == x.Data2), $"a path marker with id {x.Data2} after it (Next Id)",
                    "Data2", Advice: true));
                break;
        }
        foreach (var (a, b) in LinkPairs)
            if (t == a || t == b)
            {
                int other = t == a ? b : a;
                int id = x?.Data1 ?? 0;
                l.Add(new Need(x != null && d.Sprites.Any(o => o.Type == other && o.Xsp?.Data1 == id),
                    $"its pair: a {BloodNames.Sprite(other)} with the same id ({id}, Data 1)", "Data1"));
            }
        return l;
    }

    /// <summary>Every unmet need of the map (advice too, marked), where it is.</summary>
    public static IEnumerable<(View.ElementKind kind, int index, Need need)> Unmet(MapDocument d)
    {
        var ch = new Channels(d);
        for (int s = 0; s < d.Sectors.Count; s++)
            foreach (var n in Sector(d, s, ch).Where(n => !n.Met))
                yield return (View.ElementKind.Sector, s, n);
        for (int w = 0; w < d.Walls.Count; w++)
            foreach (var n in Wall(d, w, ch).Where(n => !n.Met))
                yield return (View.ElementKind.Line, w, n);
        for (int i = 0; i < d.Sprites.Count; i++)
            foreach (var n in Sprite(d, i, ch).Where(n => !n.Met))
                yield return (View.ElementKind.Sprite, i, n);
    }
}
