// Chosen core: original code. The selftest's atmospheres (Document/Atmospheres.cs, Chosen.Blood's MapAtmosphere.cs).
using System;
using System.Collections.Generic;
using System.Linq;
using Chosen.Forge.Document;
using Chosen.Shared.Blood.Map;
using Godot;

namespace Chosen.Forge;

public static partial class HeadlessTests
{
    /// <summary>Atmospheres: E1M1's guessed as the game guesses (rain and leaves, ground mist over its open ground);
    /// kept in a .MAPX's ATMO chunk field for field, the game's reader giving them (none: null, the game's guesses);
    /// "atmoset" (a name with spaces); a deleted one's sectors the map's own, later ones moved down; each sector's
    /// following it when a sector before it goes, a split's new half its neighbour's; a .MAP saved says it drops them.</summary>
    static int AtmosphereCheck()
    {
        int failures = 0;
        void Check(bool ok, string what)
        {
            GD.Print($"selftest atmosphere: {what}: {(ok ? "ok" : "FAILED")}");
            if (!ok)
                failures++;
        }
        string path = FindMap("E1M1");
        if (path == null)
            return 0;
        var d = MapDocument.Open(path);
        bool noneYet = !d.HasAtmosphere;
        d.GenerateAtmosphere();
        var level = d.Atmospheres[0];
        int mist = Enumerable.Range(0, d.Sectors.Count).Count(s => d.AtmosphereOf(s) == 1);
        bool open = Enumerable.Range(0, d.Sectors.Count).Where(s => d.AtmosphereOf(s) == 1).All(s => (d.Sectors[s].CeilingStat & 1) != 0
            || Blood.MapLinks.All(d).Any(l => l.Kind == View.LinkKind.Link && l.SectorA == s));
        Check(noneYet && d.HasAtmosphere && d.Atmospheres.Count == 2 && level.Rain == 1 && level.Leaves == 1 && level.Snow == 0
            && d.Atmospheres[1].MistHeight == AtmosphereRules.GameMistHeight && mist > 0 && open,
            $"E1M1's guessed: {d.Atmospheres.Count} atmospheres (rain {level.Rain}, leaves {level.Leaves}), ground mist over {mist} open sectors ({open})");

        // its own: a new one with settings, given to a sector; through the .MAPX and back, and as the game reads it
        int heavy = d.AddAtmosphere(0, "Heavy fog");
        string why = Blood.AtmosphereDialog.Set(d.Atmospheres, $"atmoset {heavy} name=Heavy%20fog%20here fog=3000 fogown=1 fogcolor=150,170,190 "
            + "mist=4096 mistdensity=2.5 rain=0 snow=1.5 windangle=300 windspeed=2000 gusts=0.5 fade=3");
        int target = Enumerable.Range(0, d.Sectors.Count).First(s => d.AtmosphereOf(s) == 0);
        d.SetAtmosphere(target, heavy);
        var a = d.Atmospheres[heavy];
        bool set = why == null && a.Name == "Heavy fog here" && a.FogDistance == 3000 && a.FogOwnColour && a.FogG == 170 && a.MistHeight == 4096
            && Math.Abs(a.MistDensity - 2.5f) < 1e-4 && a.Snow == 1.5f && a.Rain == 0 && a.WindAngle == 300 && a.WindSpeed == 2000 && a.Fade == 3;
        var bytes = MapXFile.Write(d.Map, d.Extras);
        var (_, x) = MapXFile.Read(bytes);
        bool kept = x.HasAtmosphere && x.Atmospheres.Count == d.Atmospheres.Count && x.Atmospheres.Zip(d.Atmospheres).All(p => p.First.SameAs(p.Second))
            && Enumerable.Range(0, d.Sectors.Count).All(s => (s < x.SectorAtmospheres.Count ? x.SectorAtmospheres[s] : 0) == d.AtmosphereOf(s));
        string tmp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "forge_atmo_test.mapx");
        System.IO.File.WriteAllBytes(tmp, bytes);
        var game = BloodMapFile.Read(tmp);
        var plain = MapDocument.Open(path);
        System.IO.File.WriteAllBytes(tmp, MapXFile.Write(plain.Map, plain.Extras));
        var gamePlain = BloodMapFile.Read(tmp);
        System.IO.File.Delete(tmp);
        bool read = game.Atmospheres?.Count == d.Atmospheres.Count && game.SectorAtmospheres?[target] == heavy && gamePlain.Atmospheres == null;
        Check(set && kept && read, $"\"atmoset\" ({why ?? "done"}: {a.Name}), kept in ATMO field for field {kept}, read by the game {read} (none: its guesses)");

        // Settings for These Sectors Only: a copy named after the one copied, changed without changing it (a green mist for
        // one sector and a pink for another, both given one: Forge report)
        var heavyBefore = d.Atmospheres[heavy].Clone();
        int copy = d.AddAtmosphere(heavy, "Heavy fog here");
        Blood.AtmosphereDialog.Set(d.Atmospheres, $"atmoset {copy} mistown=1 mistcolor=0,242,32");
        var orig = d.Atmospheres[heavy];
        bool alone = d.Atmospheres[copy].Name == "Heavy fog here 2" && d.Atmospheres[copy].MistHeight == 4096 && d.Atmospheres[copy].MistG == 242
            && orig.SameAs(heavyBefore);
        d.DeleteAtmosphere(copy);
        Check(alone, $"a sector's own settings: a copy ({d.Atmospheres.Count} atmospheres after), named \"Heavy fog here 2\" {alone}, the one copied unchanged");

        // a sector before the heavy one gone: its atmosphere follows it; one deleted: its sectors the map's own, later ones down
        int before = Enumerable.Range(0, target).LastOrDefault(s => d.Sectors[s].WallNum >= 3, -1);
        bool follows = true;
        if (before >= 0)
        {
            d.DeleteSector(before);
            follows = d.AtmosphereOf(target - 1) == heavy;
            target--;
        }
        int third = d.AddAtmosphere(0, "Third");
        int other = Enumerable.Range(0, d.Sectors.Count).First(s => d.AtmosphereOf(s) == 0);
        d.SetAtmosphere(other, third);
        bool deleted = d.DeleteAtmosphere(heavy) && d.AtmosphereOf(target) == 0 && d.AtmosphereOf(other) == third - 1 && d.Atmospheres.Count == third
            && !d.DeleteAtmosphere(0);
        Check(follows && deleted, $"a sector before one gone: it keeps its atmosphere {follows}; one deleted: its sectors the map's own, the later one moved down {deleted}");

        // a split: the new half takes its neighbour's (the half kept)
        {
            var o = MapDocument.New();
            int s = o.MakeSector(new List<Vector2I> { new(0, 0), new(2048, 0), new(2048, 2048), new(0, 2048) });
            o.OwnAtmosphere();
            int f = o.AddAtmosphere(0, "Split fog");
            o.SetAtmosphere(s, f);
            var (half, err) = o.SplitSector(s, new List<Vector2I> { new(0, 0), new(2048, 2048) });
            bool both = err == null && o.Sectors.Count == 2 && o.AtmosphereOf(0) == f && o.AtmosphereOf(1) == f;
            Check(both, $"a sector split ({err ?? "done"}): both halves keep its atmosphere ({o.AtmosphereOf(0)}, {(o.Sectors.Count > 1 ? o.AtmosphereOf(1) : -1)})");
        }

        // a .MAP keeps none: saying so
        d.SaveBytes(MapFormat.Map, false, out var dropped);
        Check(dropped.Any(n => n.Contains("atmosphere")), "saved as .MAP: the atmospheres said to be dropped");
        return failures;
    }
}
