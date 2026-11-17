// Chosen Blood module (GPL v2): maptool, the command-line checks of Blood's map formats.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Chosen.Shared.Blood.Map;
using Chosen.Shared.Blood.Types;
using Chosen.Shared.Blood.Files;
using Chosen.Shared.Engine.Geometry;

namespace Chosen.MapTool;

static class Program
{
    const string Usage = @"maptool <command> ...
  roundtrip <glob>...   each .MAP written back unchanged, and through .MAPX (.MAP -> .MAPX -> .MAP, .MAPX -> .MAPX):
                        byte for byte, or where it first differs; a .MAPX: written back unchanged
  tomapx <in.map> <out.mapx> [--no-patch] [--lights]   convert, applying the .patch beside the .MAP unless --no-patch;
                        --lights: the dynamic lights its sprites give by the game's rules made the map's own (LITE)
  tomap <in.mapx> <out.map> [--patch] [--limits vanilla|nblood|extended]   convert, listing what a .MAP can't hold;
                        --patch writes the sprite extras as <out>.patch
  dump <file>           the map as text: header, every record and x-field, one line each
  diff <a> <b>          the objects that differ between two maps (by index), field by field
  stats <glob>...       counts per map
  check [--limits vanilla|nblood|extended] <glob>...   the map rules (geometry, references, limits): errors fail
  types                 the type table's counts and a few names
  install               the Blood install found on this computer
  extract <blood dir> <out dir> [--addons all|none|id,id]   the game's data folder from an install (ChosenLoader's work)
  compare <dir a> <dir b> [subfolder...]   the files of b against a: missing, differing (text files ignoring line ends)
  tileuse <glob>[@<art dir>]...   how the maps use each tile (walls, masked walls, floors, ceilings, skies, decoration sprites
                        facing / flat on a wall / on a floor and their usual size, other sprites): forge/data/tileuse.txt,
                        Forge's tile picker tabs and the sprite browser's decorations; glob@art dir: an add-on's maps,
                        the tiles its own ART files hold left out (another picture there than Blood's)
  tiles <blood dir> <data dir> [all|n,n..]   decode tiles through the ART/RFF readers and compare their pixels with
                        the pipeline's textures/blood_masked and textures/blood PNGs
Globs: * in any path part (game/blood3d/addons/*/maps/*.map).";

    static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine(Usage);
            return 2;
        }
        try
        {
            return args[0] switch
            {
                "roundtrip" => RoundTrip(Expand(args.Skip(1))),
                "tomapx" => ToMapX(args.Skip(1).ToList()),
                "tomap" => ToMap(args.Skip(1).ToList()),
                "dump" => Dump(args[1]),
                "diff" => Diff(args[1], args[2]),
                "stats" => Stats(Expand(args.Skip(1))),
                "check" => Check(args.Skip(1).ToList()),
                "types" => Types(),
                "install" => Install(),
                "extract" => Extract(args.Skip(1).ToList()),
                "compare" => Compare(args[1], args[2], args.Skip(3).ToList()),
                "tileuse" => TileUse(args.Skip(1).ToList()),
                "tiles" => Tiles(args[1], args[2], args.Length > 3 ? args[3] : "all"),
                _ => Fail("unknown command " + args[0]),
            };
        }
        catch (Exception e)
        {
            Console.Error.WriteLine(e.Message);
            return 1;
        }
    }

    static int Fail(string msg)
    {
        Console.Error.WriteLine(msg);
        Console.Error.WriteLine(Usage);
        return 2;
    }

    // ---------------------------------------------------------------- globs
    static List<string> Expand(IEnumerable<string> globs)
    {
        var files = new List<string>();
        foreach (var g in globs)
        {
            var parts = g.Replace('\\', '/').Split('/');
            var found = new List<string> { parts[0] == "" ? "/" : parts[0] };
            for (int i = 1; i < parts.Length; i++)
            {
                var next = new List<string>();
                bool last = i == parts.Length - 1;
                foreach (var dir in found)
                {
                    if (!parts[i].Contains('*'))
                    {
                        string p = Path.Combine(dir, parts[i]);
                        if (last ? File.Exists(p) : Directory.Exists(p))
                            next.Add(p);
                        continue;
                    }
                    if (!Directory.Exists(dir))
                        continue;
                    next.AddRange(last ? Directory.GetFiles(dir, parts[i]) : Directory.GetDirectories(dir, parts[i]));
                }
                found = next;
            }
            if (parts.Length == 1 && File.Exists(parts[0]))
                files.Add(parts[0]);
            else
                files.AddRange(found.Where(File.Exists).OrderBy(f => f, StringComparer.OrdinalIgnoreCase));
        }
        return files;
    }

    // ---------------------------------------------------------------- extract / compare
    static int Extract(List<string> a)
    {
        IReadOnlyCollection<string> addons = null;
        int ai = a.IndexOf("--addons");
        if (ai >= 0)
        {
            string v = a[ai + 1];
            addons = v == "all" ? null : v == "none" ? Array.Empty<string>() : v.Split(',');
            a.RemoveRange(ai, 2);
        }
        var mods = new List<string>();
        for (int mi = a.IndexOf("--mod"); mi >= 0 && mi + 1 < a.Count; mi = a.IndexOf("--mod"))
        {
            mods.Add(a[mi + 1]);
            a.RemoveRange(mi, 2);
        }
        if (a.Count != 2)
            return Fail("extract <blood dir> <out dir> [--addons all|none|id,id] [--mod <NBlood add-on's folder>]...");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        string last = "";
        var x = new Extractor
        {
            BloodDir = a[0], OutDir = a[1], Addons = addons, Mods = mods,
            Progress = (f, what) =>
            {
                if (what != last)
                    Console.WriteLine($"{f * 100,5:0}%  {what}  ({sw.Elapsed.TotalSeconds:0.0} s)");
                last = what;
            },
            Log = s => Console.WriteLine("       " + s),
        };
        x.Run();
        return 0;
    }

    static readonly HashSet<string> TextExt = new(StringComparer.OrdinalIgnoreCase) { ".txt", ".ini", ".patch", ".srt", ".cfg" };

    static int Compare(string a, string b, List<string> subs)
    {
        if (subs.Count == 0)
            subs.Add("");
        int same = 0, differ = 0, missing = 0;
        foreach (var sub in subs)
        {
            string db = Path.Combine(b, sub);
            if (!Directory.Exists(db))
            {
                Console.WriteLine($"{db}: no such folder");
                missing++;
                continue;
            }
            foreach (var fb in Directory.GetFiles(db, "*", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal))
            {
                string rel = Path.GetRelativePath(b, fb);
                string fa = Path.Combine(a, rel);
                if (!File.Exists(fa))
                {
                    if (missing++ < 20)
                        Console.WriteLine($"only in {b}: {rel}");
                    continue;
                }
                byte[] x = File.ReadAllBytes(fa), y = File.ReadAllBytes(fb);
                if (TextExt.Contains(Path.GetExtension(fb)))
                {
                    x = x.Where(c => c != 13).ToArray();
                    y = y.Where(c => c != 13).ToArray();
                }
                if (x.AsSpan().SequenceEqual(y))
                    same++;
                else if (differ++ < 30)
                    Console.WriteLine($"differs: {rel} ({x.Length} / {y.Length} bytes){FirstLineDiff(x, y)}");
            }
        }
        Console.WriteLine($"compare: {same} same, {differ} different, {missing} only in {b}");
        return differ == 0 && missing == 0 ? 0 : 1;
    }

    static string FirstLineDiff(byte[] x, byte[] y)
    {
        int n = Math.Min(x.Length, y.Length), at = 0;
        while (at < n && x[at] == y[at])
            at++;
        int ls = at;
        while (ls > 0 && x[ls - 1] != 10)
            ls--;
        string Line(byte[] d)
        {
            int e = ls;
            while (e < d.Length && d[e] != 10 && e - ls < 100)
                e++;
            return System.Text.Encoding.Latin1.GetString(d, ls, Math.Max(0, e - ls));
        }
        return $" at byte {at}: '{Line(x)}' / '{Line(y)}'";
    }

    // ---------------------------------------------------------------- roundtrip
    static int RoundTrip(List<string> files)
    {
        int ok = 0, bad = 0;
        foreach (var f in files)
        {
            byte[] orig = File.ReadAllBytes(f);
            string why;
            try
            {
                if (MapXFile.IsMapX(orig))
                {
                    var (xm, xx) = MapXFile.Read(orig);
                    why = Same(orig, MapXFile.Write(xm, xx), ".mapx -> .mapx");
                }
                else
                {
                    var m = BloodMapFile.Parse(orig);
                    byte[] back = BloodMapWriter.Write(m);
                    why = FirstDifference(orig, back, m);
                    if (why == null)
                    {
                        // through .MAPX: its bytes read back and written as .MAP, and written again as .MAPX
                        byte[] x1 = MapXFile.Write(BloodMapFile.Parse(orig), new MapXExtras());
                        var (m2, e2) = MapXFile.Read(x1);
                        byte[] viaX = BloodMapWriter.Write(m2);
                        why = FirstDifference(orig, viaX, m) is { } d ? ".map -> .mapx -> .map " + d
                            : Same(x1, MapXFile.Write(m2, e2), ".mapx -> .mapx");
                    }
                }
            }
            catch (Exception e)
            {
                why = "error: " + e.Message;
            }
            if (why == null)
                ok++;
            else
            {
                bad++;
                Console.WriteLine($"{f}: {why}");
            }
        }
        Console.WriteLine($"roundtrip: {ok} identical, {bad} different, of {files.Count}");
        return bad == 0 && files.Count > 0 ? 0 : 1;
    }

    static Limits LimitsByName(string n) => n switch
    {
        "nblood" => Limits.NBlood, "extended" => Limits.Extended, "vanilla" => Limits.Vanilla,
        _ => throw new ArgumentException("limits: vanilla, nblood or extended, not " + n),
    };

    static string Same(byte[] a, byte[] b, string what)
    {
        int n = Math.Min(a.Length, b.Length);
        for (int i = 0; i < n; i++)
            if (a[i] != b[i])
                return $"{what}: differs at {i}; sizes {a.Length} / {b.Length}";
        return a.Length == b.Length ? null : $"{what}: sizes {a.Length} / {b.Length}";
    }

    // ---------------------------------------------------------------- tomapx / tomap
    static int ToMapX(List<string> a)
    {
        bool noPatch = a.Remove("--no-patch");
        bool lights = a.Remove("--lights");
        if (a.Count != 2)
            return Fail("tomapx <in.map> <out.mapx> [--no-patch] [--lights]");
        var m = BloodMapFile.Read(a[0]);
        string patch = Path.ChangeExtension(a[0], ".patch");
        bool usePatch = !noPatch && File.Exists(patch);
        var (map, x) = MapConvert.ToMapX(m, usePatch ? File.ReadAllLines(patch) : null, "maptool");
        if (lights)
        {
            x.Lights.AddRange(LightRules.Generate(map));
            x.HasLights = true;
        }
        File.WriteAllBytes(a[1], MapXFile.Write(map, x));
        Console.WriteLine($"{a[1]}: {map.Sectors.Count} sectors, {map.Walls.Count} walls, {map.Sprites.Count} sprites"
            + (usePatch ? $", {Path.GetFileName(patch)} applied ({x.SpriteExtras.Count} sprite extras)" : "")
            + (lights ? $", {x.Lights.Count} dynamic lights" : ""));
        return 0;
    }

    static int ToMap(List<string> a)
    {
        bool writePatch = a.Remove("--patch");
        var limits = Limits.NBlood;
        int li = a.IndexOf("--limits");
        if (li >= 0)
        {
            limits = LimitsByName(a[li + 1]);
            a.RemoveRange(li, 2);
        }
        if (a.Count != 2)
            return Fail("tomap <in.mapx> <out.map> [--patch] [--limits vanilla|nblood|extended]");
        var (m, x) = MapXFile.Read(a[0]);
        var (bytes, dropped) = MapConvert.ToMap(m, x, limits);
        File.WriteAllBytes(a[1], bytes);
        foreach (var d in dropped)
            Console.WriteLine("not in the .MAP: " + d);
        if (writePatch && x.SpriteExtras.Count > 0)
        {
            string patch = Path.ChangeExtension(a[1], ".patch");
            File.WriteAllLines(patch, MapPatch.SpriteLines(x.SpriteExtras));
            Console.WriteLine($"{patch}: {x.SpriteExtras.Count} sprite extras");
        }
        Console.WriteLine($"{a[1]}: written");
        return 0;
    }

    /// <summary>null when equal, else the first differing offset and the region it is in.</summary>
    static string FirstDifference(byte[] a, byte[] b, BloodMapFile m)
    {
        int n = Math.Min(a.Length, b.Length);
        int at = -1;
        for (int i = 0; i < n; i++)
            if (a[i] != b[i])
            {
                at = i;
                break;
            }
        if (at < 0 && a.Length == b.Length)
            return null;
        if (at < 0)
            at = n;
        return $"differs at {at} ({Region(m, at)}); sizes {a.Length} / {b.Length}";
    }

    static string Region(BloodMapFile m, int at)
    {
        int pos = 6;
        if (at < pos) return "signature";
        if (at < (pos += 37)) return "header";
        if (m.V7 && at < (pos += 128)) return "header2";
        if (at < (pos += m.SkyOfs.Length * 2)) return "sky offsets";
        for (int i = 0; i < m.Sectors.Count; i++)
        {
            if (at < (pos += 40)) return $"sector {i}";
            if (m.Sectors[i].Extra > 0 && at < (pos += m.XSectorSize)) return $"x-sector of sector {i}";
        }
        for (int i = 0; i < m.Walls.Count; i++)
        {
            if (at < (pos += 32)) return $"wall {i}";
            if (m.Walls[i].Extra > 0 && at < (pos += m.XWallSize)) return $"x-wall of wall {i}";
        }
        for (int i = 0; i < m.Sprites.Count; i++)
        {
            if (at < (pos += 44)) return $"sprite {i}";
            if (m.Sprites[i].Extra > 0 && at < (pos += m.XSpriteSize)) return $"x-sprite of sprite {i}";
        }
        if (at < pos + 4) return "crc";
        return "after the crc";
    }

    // ---------------------------------------------------------------- dump / stats
    static int Dump(string file) => Dump(BloodMapFile.Read(file), Console.Out);

    static int Diff(string a, string b)
    {
        var la = new StringWriter();
        var lb = new StringWriter();
        Dump(BloodMapFile.Read(a), la);
        Dump(BloodMapFile.Read(b), lb);
        // each line is one object ("sector 12 ..."): key by its first two words, then compare field by field
        static Dictionary<string, string> Index(string text) => text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .ToDictionary(l => string.Join(' ', l.Split(' ').Take(2)), l => l.TrimEnd('\r'));
        var ia = Index(la.ToString());
        var ib = Index(lb.ToString());
        int n = 0;
        foreach (var key in ia.Keys.Union(ib.Keys))
        {
            ia.TryGetValue(key, out var x);
            ib.TryGetValue(key, out var y);
            if (x == y)
                continue;
            n++;
            if (x == null || y == null)
            {
                Console.WriteLine($"{key}: only in {(x == null ? b : a)}");
                continue;
            }
            var fa = x.Split(' ');
            var fb = y.Split(' ');
            var changes = fa.Zip(fb).Where(p => p.First != p.Second && p.First.Contains('=')).Select(p => $"{p.First} -> {p.Second.Split('=')[1]}");
            Console.WriteLine($"{key}: {string.Join(", ", changes)}");
        }
        Console.WriteLine($"diff: {n} objects differ");
        return n == 0 ? 0 : 1;
    }

    static int Dump(BloodMapFile m, TextWriter o)
    {
        o.WriteLine($"version {m.Version:x} headerEncrypted {m.HeaderEncrypted} start {m.StartX} {m.StartY} {m.StartZ} ang {m.StartAng} " +
                    $"sect {m.StartSect} pskybits {m.PSkyBits} visibility {m.Visibility} song {m.SongId} parallax {m.ParallaxType} " +
                    $"maprev {m.MapRev} x sizes {m.XSpriteSize}/{m.XWallSize}/{m.XSectorSize} tail {m.Tail.Length}");
        o.WriteLine("sky " + string.Join(' ', m.SkyOfs));
        for (int i = 0; i < m.Sectors.Count; i++)
            o.WriteLine($"sector {i} {Fields(m.Sectors[i])}{(m.Sectors[i].Xs != null ? " | x " + Fields(m.Sectors[i].Xs) : "")}");
        for (int i = 0; i < m.Walls.Count; i++)
            o.WriteLine($"wall {i} {Fields(m.Walls[i])}{(m.Walls[i].Xw != null ? " | x " + Fields(m.Walls[i].Xw) : "")}");
        for (int i = 0; i < m.Sprites.Count; i++)
            o.WriteLine($"sprite {i} {Fields(m.Sprites[i])}{(m.Sprites[i].Xsp != null ? " | x " + Fields(m.Sprites[i].Xsp) : "")}");
        return 0;
    }

    // every public int field as name=value (a record's or x-structure's)
    static string Fields(object r) =>
        string.Join(' ', r.GetType().GetFields().Where(f => f.FieldType == typeof(int)).Select(f => $"{f.Name}={f.GetValue(r)}"));

    static int Stats(List<string> files)
    {
        foreach (var f in files)
        {
            var m = BloodMapFile.Read(f);
            Console.WriteLine($"{Path.GetFileName(f)}: version {m.Version:x}, {m.Sectors.Count} sectors, {m.Walls.Count} walls, " +
                              $"{m.Sprites.Count} sprites, x {m.Sectors.Count(s => s.Xs != null)}/{m.Walls.Count(w => w.Xw != null)}/" +
                              $"{m.Sprites.Count(s => s.Xsp != null)}, maprev {m.MapRev}");
        }
        return 0;
    }
    /// <summary>How the maps use each tile, a line a tile used: tile, then the count of walls showing it, masked or
    /// one-way walls' over pictures, floors, ceilings (not parallax), parallax ceilings (skies), decoration sprites
    /// (type 0, seen: not invisible) facing the camera, flat on a wall, flat on a floor, sprites of another type; the
    /// maps it is in; and the size (x and y repeat) its decorations have most often (0 0: none).</summary>
    static int TileUse(List<string> args)
    {
        // each glob's maps, with the tiles an add-on's ART files replace (left out of them)
        var files = new List<(string path, List<(int lo, int hi)> skip)>();
        foreach (var a in args)
        {
            int at = a.IndexOf('@');
            var skip = new List<(int, int)>();
            if (at > 0)
                foreach (var art in Directory.GetFiles(a[(at + 1)..], "*.art", SearchOption.TopDirectoryOnly))
                {
                    var h = new byte[16];
                    using (var fs = File.OpenRead(art))
                        fs.ReadExactly(h);
                    skip.Add((BitConverter.ToInt32(h, 8), BitConverter.ToInt32(h, 12)));
                }
            foreach (var f in Expand(new[] { at > 0 ? a[..at] : a }))
                files.Add((f, skip));
        }
        const int K = 9;
        var counts = new SortedDictionary<int, int[]>();
        var maps = new Dictionary<int, HashSet<string>>();
        var sizes = new Dictionary<int, Dictionary<(int, int), int>>();
        void AddAll(int tile, int k, string map)
        {
            if (tile < 0 || tile >= 8192)
                return;
            if (!counts.TryGetValue(tile, out var c))
                counts[tile] = c = new int[K];
            c[k]++;
            if (!maps.TryGetValue(tile, out var m))
                maps[tile] = m = new HashSet<string>();
            m.Add(map);
        }
        foreach (var (f, skip) in files)
        {
            var m = BloodMapFile.Read(f);
            string name = Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetFullPath(f)))) + "/" + Path.GetFileNameWithoutExtension(f).ToUpperInvariant();
            void Add(int tile, int k, string map)
            {
                if (!skip.Any(r => tile >= r.lo && tile <= r.hi))
                    AddAll(tile, k, map);
            }
            foreach (var w in m.Walls)
            {
                Add(w.Picnum, 0, name);
                if ((w.Cstat & (16 | 32)) != 0)
                    Add(w.OverPicnum, 1, name);
            }
            foreach (var sec in m.Sectors)
            {
                Add(sec.FloorPicnum, 2, name);
                Add(sec.CeilingPicnum, (sec.CeilingStat & 1) != 0 ? 4 : 3, name);
            }
            foreach (var sp in m.Sprites)
            {
                // (markers and invisible sprites show nothing)
                if (sp.StatNum == 10 || (sp.Cstat & 32768) != 0)
                    continue;
                if (sp.Type != 0)
                {
                    Add(sp.Picnum, 8, name);
                    continue;
                }
                Add(sp.Picnum, (sp.Cstat & 48) switch { 16 => 6, 32 => 7, _ => 5 }, name);
                if (sp.Picnum is >= 0 and < 8192 && !skip.Any(r => sp.Picnum >= r.lo && sp.Picnum <= r.hi))
                {
                    if (!sizes.TryGetValue(sp.Picnum, out var z))
                        sizes[sp.Picnum] = z = new Dictionary<(int, int), int>();
                    z[(sp.XRepeat, sp.YRepeat)] = z.GetValueOrDefault((sp.XRepeat, sp.YRepeat)) + 1;
                }
            }
        }
        Console.WriteLine($"// how {files.Count} maps (Blood's, its add-ons' outside their own art) use each tile (maptool tileuse): tile wall masked floor ceiling sky "
            + "decoration decoration-on-wall decoration-on-floor sprite maps xrepeat yrepeat");
        foreach (var (t, c) in counts)
        {
            var (xr, yr) = sizes.TryGetValue(t, out var z) ? z.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key).First().Key : (0, 0);
            Console.WriteLine($"{t} {string.Join(" ", c)} {maps[t].Count} {xr} {yr}");
        }
        return 0;
    }

    // ---------------------------------------------------------------- check / types
    static int Check(List<string> args)
    {
        var limits = Limits.Vanilla;
        int li = args.IndexOf("--limits");
        if (li >= 0)
        {
            if (li + 1 >= args.Count)
                return Fail("--limits needs vanilla, nblood or extended");
            limits = LimitsByName(args[li + 1]);
            args.RemoveRange(li, 2);
        }
        var files = Expand(args);
        int errors = 0, warnings = 0, badMaps = 0;
        var kinds = new SortedDictionary<string, int>();
        foreach (var f in files)
        {
            var issues = MapValidator.Check(BloodMapFile.Read(f), limits);
            int e = issues.Count(i => i.Severity == Severity.Error), w = issues.Count - e;
            errors += e;
            warnings += w;
            foreach (var i in issues)
            {
                string k = (i.Severity == Severity.Error ? "error " : "warning ") + i.Kind;
                kinds[k] = kinds.GetValueOrDefault(k) + 1;
            }
            if (e > 0)
            {
                badMaps++;
                Console.WriteLine($"{f}: {e} errors, {w} warnings");
                foreach (var i in issues.Where(i => i.Severity == Severity.Error).Take(5))
                    Console.WriteLine("  " + i);
            }
        }
        foreach (var (k, n) in kinds)
            Console.WriteLine($"  {k}: {n}");
        Console.WriteLine($"check ({limits.Name}): {errors} errors in {badMaps} maps, {warnings} warnings, of {files.Count} maps");
        return errors == 0 && files.Count > 0 ? 0 : 1;
    }

    static int Types()
    {
        var t = TypeTable.Load();
        Console.WriteLine($"{t.Count} types");
        foreach (var (k, n) in new[] { (ObjKind.Sprite, 201), (ObjKind.Sprite, 20), (ObjKind.Wall, 501), (ObjKind.Sector, 600) })
            Console.WriteLine($"  {k} {n}: {t.Name(k, n)} (data1 {t.DataCaption(k, n, 1) ?? "-"}) category {TypeTable.Category(n)}");
        return 0;
    }

    // ---------------------------------------------------------------- install / tiles
    static int Install()
    {
        string found = BloodInstall.Find();
        Console.WriteLine(found ?? "no Blood install found");
        return found != null ? 0 : 1;
    }

    static int Tiles(string bloodDir, string dataDir, string which)
    {
        var art = ArtSet.Load(bloodDir);
        var pal = Palette.FromRff(new Rff(BloodInstall.FindFile(bloodDir, "BLOOD.RFF")));
        var ids = which == "all" ? art.Numbers.OrderBy(n => n).ToList() : which.Split(',').Select(int.Parse).ToList();
        int same = 0, differ = 0, missing = 0, empty = 0;
        foreach (int n in ids)
        {
            var t = art[n];
            if (t == null || t.Empty)
            {
                empty++;
                continue;
            }
            foreach (bool masked in new[] { true, false })
            {
                string png = Path.Combine(dataDir, "textures", masked ? "blood_masked" : "blood", $"{n:D4}.png");
                if (!File.Exists(png))
                {
                    missing++;
                    continue;
                }
                var (w, h, rgba) = PngReader.Read(png);
                var mine = pal.ToRgba(t, masked);
                if (w == t.Width && h == t.Height && rgba.AsSpan().SequenceEqual(mine))
                    same++;
                else
                {
                    differ++;
                    if (differ <= 10)
                        Console.WriteLine($"tile {n} ({(masked ? "masked" : "opaque")}): differs ({w}x{h} vs {t.Width}x{t.Height})");
                }
            }
        }
        Console.WriteLine($"tiles: {same} pictures the same, {differ} different, {missing} with no pipeline PNG, {empty} empty tiles, of {ids.Count} tiles ({art.Count} in the art)");
        return differ == 0 ? 0 : 1;
    }

}
