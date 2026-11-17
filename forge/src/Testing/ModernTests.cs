// Chosen Blood module (GPL v2): the selftest's modern-map checks (Blood/ModernTypes.cs, Map Analysis' channels, the
// validator's wrapped references, the recorder's late fields).
using System.Collections.Generic;
using System.Linq;
using Chosen.Forge.Document;
using Chosen.Shared.Blood.Map;
using Godot;

namespace Chosen.Forge;

public static partial class HeadlessTests
{
    /// <summary>NoOne's modern maps as the editor knows them: a map modern by its version; a random TX switch's data
    /// fields as channels it sends on (a range from data1 to data4, else each one), so Map Analysis doesn't call a
    /// channel only such a switch sends unsent; a sloped floor sprite's slope out of its offsets; a wall past 8191
    /// whose 14-bit reference wrapped not reported; a field added to a dialog later counted last by the recorder.</summary>
    static int ModernCheck()
    {
        int failures = 0;
        void Check(bool ok, string what)
        {
            GD.Print($"selftest modern: {what}: {(ok ? "ok" : "FAILED")}");
            if (!ok)
                failures++;
        }
        var plain = new BloodMapFile();
        var modern = new BloodMapFile { Version = 0x701 };
        Check(Blood.ModernTypes.Revision(plain) == 0 && Blood.ModernTypes.Revision(modern) == 1 && Blood.ModernTypes.IsModern(modern),
            $"a map of version 7.1 is modern (revision {Blood.ModernTypes.Revision(modern)}), one of 7.0 isn't");

        var ranged = new BSprite { Type = Blood.ModernTypes.RandomTx, Xsp = new XSprite { Data1 = 100, Data4 = 103 } };
        var listed = new BSprite { Type = Blood.ModernTypes.SequentialTx, Xsp = new XSprite { Data1 = 100, Data3 = 105 } };
        var other = new BSprite { Type = 20, Xsp = new XSprite { Data1 = 100, Data4 = 103 } };
        string r = string.Join(",", Blood.ModernTypes.DataChannels(ranged)), l = string.Join(",", Blood.ModernTypes.DataChannels(listed));
        Check(r == "100,101,102,103" && l == "100,105" && !Blood.ModernTypes.DataChannels(other).Any(),
            $"a random TX's data1 100 and data4 103 send on {r}; a sequential TX's 100 and 105 on {l}; a switch's data fields on none");

        var up = new BSprite { Cstat = 48, XOffset = 0, YOffset = 16 };
        var down = new BSprite { Cstat = 48 | 1, XOffset = 0, YOffset = -15 };
        var flat = new BSprite { Cstat = 32, XOffset = 5, YOffset = 16 };
        Check(Blood.ModernTypes.Slope(up) == 4096 && Blood.ModernTypes.Slope(down) == -3840 && Blood.ModernTypes.Slope(flat) == 0,
            $"a sloped floor sprite's slope from its offsets: {Blood.ModernTypes.Slope(up)}, {Blood.ModernTypes.Slope(down)}; a floor sprite's offsets are no slope");

        // E1M1 with a door's channel sent by a random TX's data field alone
        if (FindMap("E1M1") is { } path)
        {
            var d = MapDocument.Open(path);
            int free = Enumerable.Range(600, 400).First(c => !d.Sprites.Any(s => s.Xsp is { } x && (x.TxID == c || x.RxID == c))
                && !d.Sectors.Any(s => s.Xs is { } x && (x.TxID == c || x.RxID == c)) && !d.Walls.Any(w => w.Xw is { } x && (x.TxID == c || x.RxID == c)));
            int listener = Enumerable.Range(0, d.Sprites.Count).First(i => d.Sprites[i].Xsp != null && d.SpriteEditable(i));
            int sender = Enumerable.Range(0, d.Sprites.Count).First(i => i != listener && d.Sprites[i].Xsp != null && d.SpriteEditable(i));
            d.Sprites[listener].Xsp.RxID = free;
            bool before = Blood.MapAnalysis.Run(d).Any(f => f.Text.Contains($"channel {free} is received"));
            d.Sprites[sender].Type = Blood.ModernTypes.RandomTx;
            d.Sprites[sender].Xsp.Data1 = free;
            d.Sprites[sender].Xsp.Data2 = d.Sprites[sender].Xsp.Data3 = d.Sprites[sender].Xsp.Data4 = 0;
            bool after = Blood.MapAnalysis.Run(d).Any(f => f.Text.Contains($"channel {free} is received"));
            bool linked = Blood.MapLinks.All(d).Any(k => k.Kind == View.LinkKind.Channel && k.Label == free.ToString());
            Check(before && !after && linked, $"E1M1: channel {free} with a listener alone is reported ({before}); a random TX sending it by data1: not ({!after}), and linked ({linked})");

            // a wall past 8191: its reference as the file's 14 bits keep it
            var big = new BloodMapFile();
            var issues = MapValidator.Check(d.Map, Limits.NBlood);
            int refsBefore = issues.Count(i => i.Kind == "reference" && i.Object == "wall");
            int w = Enumerable.Range(0, d.Walls.Count).First(i => d.Walls[i].Xw != null);
            int keep = d.Walls[w].Xw.Reference;
            d.Walls[w].Xw.Reference = ((w + 8192) & 16383) - 8192;       // (as stored: the same below 8192)
            bool same = MapValidator.Check(d.Map, Limits.NBlood).Count(i => i.Kind == "reference" && i.Object == "wall") == refsBefore;
            d.Walls[w].Xw.Reference = w + 5;
            bool wrong = MapValidator.Check(d.Map, Limits.NBlood).Count(i => i.Kind == "reference" && i.Object == "wall") == refsBefore + 1;
            d.Walls[w].Xw.Reference = keep;
            Check(same && wrong, $"an x-wall's reference as 14 bits store it passes ({same}); a wrong one is reported ({wrong})");
        }

        var root = new VBoxContainer();
        var a = new LineEdit { Name = "a" };
        var b = new LineEdit { Name = "b" };
        var c = new CheckBox { Name = "c" };
        b.SetMeta(Recording.SessionRecorder.LateField, true);
        root.AddChild(a);
        root.AddChild(b);
        root.AddChild(c);
        string order = string.Join(",", Recording.SessionRecorder.Fields(root).Select(f => (string)f.Name));
        root.Free();
        Check(order == "a,c,b", $"a field added to a dialog later is numbered after the others ({order})");
        return failures;
    }
}
