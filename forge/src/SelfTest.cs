// Chosen core: original code.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Chosen.Forge.Document;
using Chosen.Forge.Editing;
using Chosen.Shared.Blood.Map;
using Chosen.Shared.Engine.Geometry;
using Godot;

namespace Chosen.Forge;

public static partial class HeadlessTests
{
    static int Errors(MapDocument d) =>
        MapValidator.Check(d.Map, Limits.Extended).Count(i => i.Severity == Severity.Error);

    /// <summary>The structural warnings an operation must not add (loops, red pairs, winding).</summary>
    static int StructureWarnings(MapDocument d) =>
        MapValidator.Check(d.Map, Limits.Extended).Count(i => i.Severity == Severity.Warning && (i.Kind == "loop" || i.Kind == "pair" || i.Kind == "winding"));

    static string FindMap(string name) =>
        Maps(Array.Empty<string>()).FirstOrDefault(f => Path.GetFileNameWithoutExtension(f).Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// --selftest [map]: a scripted session through MapDocument on E1M1 (points and sprites moved, sprites added and
    /// deleted, first walls set, x-walls edited), the map rules checked after each step (no error more than the map
    /// had), then everything undone (and redone and undone again): the map must save byte for byte as opened.
    /// </summary>
    public static int SelfTest(string[] args)
    {
        string path = args.FirstOrDefault(File.Exists) ?? FindMap("E1M1");
        if (path == null)
        {
            GD.PrintErr("selftest: no E1M1.map (pipeline/export_maps.py)");
            return 2;
        }
        int failures = Editing(path) + Geometry(args) + Preserve() + NewMap() + Across() + LayerCheck() + CameraTrack() + SpriteToLayer() + LinkCheck() + MotionCheck() + AlignCheck() + CaptionCheck() + PlayCheck() + SoundCheck() + FieldCheck() + NeedsCheck() + GridCheck() + PasteCheck() + DissolveCheck() + FixCheck() + FloorAlignCheck() + JoinTouchingCheck() + SeparateCheck() + SkyCheck() + TileUseCheck() + GradientCheck() + SlopeEdgeCheck() + MapArtCheck() + RoomLinksCheck() + WallPasteCheck() + WallPortalCheck() + InLineCheck() + DoorCheck() + LightCheck() + AtmosphereCheck() + ModernCheck() + MapXVersionCheck();
        GD.Print(failures == 0 ? "selftest: passed" : $"selftest: {failures} failure(s)");
        return failures == 0 ? 0 : 1;
    }

    static int Editing(string path)
    {
        var orig = File.ReadAllBytes(path);
        var doc = MapDocument.Open(path);
        var undo = new UndoManager(doc, 10000);
        int baseErrors = Errors(doc), failures = 0, steps = 0;
        var rng = new Random(12345);
        void Step(string what, Action a)
        {
            undo.CreateUndo(what);
            a();
            doc.NotifyChanged();
            steps++;
            int e = Errors(doc);
            if (e > baseErrors)
            {
                failures++;
                GD.Print($"selftest: {what}: {e} map rule errors (the map had {baseErrors})");
                foreach (var i in MapValidator.Check(doc.Map, Limits.Extended).Where(i => i.Severity == Severity.Error).Take(5))
                    GD.Print("  " + i.Message);
            }
        }
        for (int k = 0; k < 50; k++)
        {
            int p = rng.Next(doc.Points.Count);
            int dx = rng.Next(3) - 1, dy = rng.Next(3) - 1;
            Step($"move point {p}", () => doc.MovePoint(p, doc.Points[p].X + dx * 64, doc.Points[p].Y + dy * 64));
        }
        for (int k = 0; k < 20; k++)
        {
            int s = rng.Next(doc.Sprites.Count);
            Step($"move sprite {s}", () => doc.MoveSprite(s, doc.Sprites[s].X + 64, doc.Sprites[s].Y - 64));
        }
        for (int k = 0; k < 10; k++)
        {
            int sec = rng.Next(doc.Sectors.Count);
            var at = doc.Labels[sec];
            Step($"add a sprite in sector {sec}", () => doc.AddSprite((int)at.X, (int)at.Y, 0, sec));
        }
        for (int k = 0; k < 10; k++)
        {
            // (not a marker: a sector's markers deleted leave it pointing at none, which the rules report)
            var plain = Enumerable.Range(0, doc.Sprites.Count).Where(i => doc.Sprites[i].StatNum != 10).ToList();
            int s = plain[rng.Next(plain.Count)];
            Step($"delete sprite {s}", () => doc.DeleteSprites(new[] { s }));
        }
        for (int k = 0; k < 10; k++)
        {
            int sec = rng.Next(doc.Sectors.Count);
            var loops = BuildGeom.Loops(doc, sec);
            if (loops.Count == 0)
                continue;
            int w = loops[0][rng.Next(loops[0].Count)];
            var wall = doc.Walls[w];
            Step($"first wall of sector {sec}: {w}", () =>
            {
                if (!doc.SetFirstWall(sec, w))
                    GD.Print($"selftest: SetFirstWall({sec}, {w}) refused");
            });
            if (doc.Walls[doc.Sectors[sec].WallPtr] != wall)
            {
                failures++;
                GD.Print($"selftest: wall {w} isn't sector {sec}'s first wall after SetFirstWall");
            }
        }
        for (int k = 0; k < 10; k++)
        {
            int w = rng.Next(doc.Walls.Count);
            Step($"x-wall of wall {w}", () =>
            {
                var x = doc.EnsureXWall(w);
                x.TxID = 100 + k;
                x.Command = 1;
            });
        }
        // undo all, redo all, undo all: back to the file
        int undone = 0;
        while (undo.PerformUndo())
            undone++;
        int redone = 0;
        while (undo.PerformRedo())
            redone++;
        if (Errors(doc) > baseErrors)
        {
            failures++;
            GD.Print("selftest: map rule errors after redoing everything");
        }
        while (undo.PerformUndo())
        { }
        var back = doc.SaveBytes(MapFormat.Map, false, out _);
        bool same = back.AsSpan().SequenceEqual(orig);
        if (!same)
            failures++;
        GD.Print($"selftest editing ({Path.GetFileName(path)}): {steps} steps, {undone} undone, {redone} redone, "
            + (same ? "saved byte for byte as opened" : "SAVED DIFFERENT FROM THE FILE") + $", {failures} failure(s)");
        return failures;
    }

    /// <summary>
    /// The geometry fuzz: on E1M1, E2M1 and DWE1M1 (rooms sharing space), 200 random operations each (a point inserted
    /// on a wall; inserted and deleted again; a small room drawn in a sector and joined back; a convex sector split
    /// along a chord; a sector deleted), the map rules after each (no error more than the map had); the fuzzed map
    /// saved into tools/forge_fuzz (.map and .mapx, for maptool check and the game); everything undone: byte for
    /// byte as opened.
    /// </summary>
    static int Geometry(string[] args)
    {
        int failures = 0;
        string outDir = Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), "..", "tools", "forge_fuzz"));
        Directory.CreateDirectory(outDir);
        foreach (var name in new[] { "E1M1", "E2M1", "DWE1M1" })
        {
            string path = FindMap(name);
            if (path == null)
            {
                GD.Print($"selftest geometry: no {name}.map, skipped");
                continue;
            }
            failures += Fuzz(path, outDir, name.Sum(ch => ch) * 7919);
        }
        return failures;
    }

    static int Fuzz(string path, string outDir, int seed)
    {
        var orig = File.ReadAllBytes(path);
        var doc = MapDocument.Open(path);
        var undo = new UndoManager(doc, 100000);
        var rng = new Random(seed);
        int baseErrors = Errors(doc), failures = 0;
        int baseWarnings = StructureWarnings(doc);
        string[] names = { "insert point", "insert and delete point", "room drawn and joined", "split", "delete sector",
            "neighbours joined", "split with a pillar" };
        var counts = new int[names.Length];
        var refused = new int[names.Length];
        for (int k = 0; k < 200; k++)
        {
            int op = rng.Next(names.Length);
            bool mayRefuse = op >= 5;      // joining neighbours may make two rooms; a chord may cross the pillar
            undo.CreateUndo(names[op]);
            string why = null;
            bool skipped = false;
            switch (op)
            {
                case 0:
                case 1:
                {
                    int w = rng.Next(doc.Walls.Count);
                    var (ax, ay, bx, by) = BuildGeom.WallEnds(doc, w);
                    int mx = (ax + bx) / 2, my = (ay + by) / 2;
                    if ((mx == ax && my == ay) || (mx == bx && my == by))
                    {
                        skipped = true;
                        break;
                    }
                    if (!doc.InsertPoint(w, mx, my))
                    {
                        why = "InsertPoint refused";
                        break;
                    }
                    if (op == 1)
                    {
                        int nw = Enumerable.Range(0, doc.Walls.Count).First(i => doc.Walls[i].X == mx && doc.Walls[i].Y == my);
                        why = doc.DeletePoint(doc.PointOf[nw]);
                    }
                    break;
                }
                case 2:
                {
                    int s = rng.Next(doc.Sectors.Count);
                    var c = doc.Labels[s];
                    int cx = (int)Math.Round(c.X / 16) * 16, cy = (int)Math.Round(c.Y / 16) * 16;
                    var rect = new List<Vector2I> { new(cx - 16, cy - 16), new(cx + 16, cy - 16), new(cx + 16, cy + 16), new(cx - 16, cy + 16) };
                    if (!RoomFits(doc, s, rect))
                    {
                        skipped = true;
                        break;
                    }
                    int n = doc.InsertInnerLoop(s, rect);
                    why = n < 0 ? "InsertInnerLoop refused" : doc.JoinSectors(s, n);
                    break;
                }
                case 3:
                {
                    var convex = Enumerable.Range(0, doc.Sectors.Count).Where(i => Convex(doc, i)).ToList();
                    if (convex.Count == 0)
                    {
                        skipped = true;
                        break;
                    }
                    int s = convex[rng.Next(convex.Count)];
                    var loop = BuildGeom.Loops(doc, s)[0];
                    var a = doc.Walls[loop[0]];
                    var b = doc.Walls[loop[loop.Count / 2]];
                    var (_, err) = doc.SplitSector(s, new List<Vector2I> { new(a.X, a.Y), new(b.X, b.Y) });
                    why = err;
                    break;
                }
                case 4:
                    doc.DeleteSector(rng.Next(doc.Sectors.Count));
                    break;
                case 5:
                {
                    var red = Enumerable.Range(0, doc.Walls.Count).Where(i => doc.Walls[i].NextSector >= 0 && doc.Walls[i].NextSector != doc.Walls[i].Sector
                        && doc.Walls[i].NextWall >= 0 && doc.Walls[doc.Walls[i].NextWall].NextWall == i).ToList();
                    if (red.Count == 0)
                    {
                        skipped = true;
                        break;
                    }
                    int w = red[rng.Next(red.Count)];
                    why = doc.JoinSectors(doc.Walls[w].Sector, doc.Walls[w].NextSector);
                    break;
                }
                case 6:
                {
                    var holed = Enumerable.Range(0, doc.Sectors.Count).Where(i => BuildGeom.Loops(doc, i).Count >= 2 && BuildGeom.Loops(doc, i)[0].Count >= 4).ToList();
                    if (holed.Count == 0)
                    {
                        skipped = true;
                        break;
                    }
                    int s = holed[rng.Next(holed.Count)];
                    var loop = BuildGeom.Loops(doc, s)[0];
                    var a = doc.Walls[loop[0]];
                    var b = doc.Walls[loop[loop.Count / 2]];
                    why = doc.SplitSector(s, new List<Vector2I> { new(a.X, a.Y), new(b.X, b.Y) }).error;
                    break;
                }
            }
            if (skipped || why != null)
            {
                undo.Revert();
                if (why != null)
                {
                    refused[op]++;
                    if (!mayRefuse)
                    {
                        GD.Print($"selftest geometry {Path.GetFileName(path)} step {k}: {names[op]}: {why}");
                        failures++;
                    }
                }
                continue;
            }
            doc.NotifyChanged();
            counts[op]++;
            int e = Errors(doc), sw = StructureWarnings(doc);
            if (e > baseErrors || sw > baseWarnings)
            {
                failures++;
                GD.Print($"selftest geometry {Path.GetFileName(path)} step {k}: {names[op]}: {e} map rule errors (the map had {baseErrors}), "
                    + $"{sw} loop/pair/winding warnings (it had {baseWarnings})");
                foreach (var i in MapValidator.Check(doc.Map, Limits.Extended).Where(i => i.Severity == Severity.Error
                    || i.Kind == "loop" || i.Kind == "pair" || i.Kind == "winding").Take(6))
                    GD.Print("  " + i.Message);
                undo.Revert();
                doc.NotifyChanged();
            }
        }
        string stem = Path.Combine(outDir, Path.GetFileNameWithoutExtension(path).ToUpperInvariant());
        File.WriteAllBytes(stem + ".mapx", doc.SaveBytes(MapFormat.MapX, false, out _));
        File.WriteAllBytes(stem + ".map", doc.SaveBytes(MapFormat.Map, false, out _));
        while (undo.PerformUndo())
        { }
        bool same = doc.SaveBytes(MapFormat.Map, false, out _).AsSpan().SequenceEqual(orig);
        if (!same)
            failures++;
        GD.Print($"selftest geometry ({Path.GetFileName(path)}): " + string.Join(", ", names.Select((n, i) => $"{n} {counts[i]}" + (refused[i] > 0 ? $" ({refused[i]} refused)" : "")))
            + $"; undone: {(same ? "byte for byte as opened" : "DIFFERENT FROM THE FILE")}; saved {stem}.map/.mapx; {failures} failure(s)");
        return failures;
    }

    /// <summary>
    /// What no operation may touch beyond its own walls: one-way links (E3M5's wall 264 and others), walls in no
    /// sector's range (DWE4M1's), NextSector values as stored. A point inserted on one wall must leave every other
    /// wall's link as it was (the same wall object, the same sector) and keep every wall.
    /// </summary>
    static int Preserve()
    {
        int failures = 0;
        foreach (var name in new[] { "E3M5", "DWE4M1", "DWE1M9" })
        {
            string path = FindMap(name);
            if (path == null)
                continue;
            var doc = MapDocument.Open(path);
            var before = doc.Walls.Select(w => (w, next: w.NextWall >= 0 && w.NextWall < doc.Walls.Count ? doc.Walls[w.NextWall] : null,
                raw: w.NextWall, ns: w.NextSector)).ToList();
            int oneWay = before.Count(b => b.next != null && b.next.NextWall != doc.Walls.IndexOf(b.w));
            int orphans = doc.Walls.Count(w => w.Sector < 0);
            // a white wall of a sector (not a link of anything) gets a point
            int target = Enumerable.Range(0, doc.Walls.Count).First(i => doc.Walls[i].NextWall < 0 && doc.Walls[i].Sector >= 0
                && before.All(b => !ReferenceEquals(b.next, doc.Walls[i])));
            var (ax, ay, bx, by) = BuildGeom.WallEnds(doc, target);
            var tw = doc.Walls[target];
            if (!doc.InsertPoint(target, (ax + bx) / 2, (ay + by) / 2))
                continue;
            int changed = 0, lost = 0;
            var now = new HashSet<BWall>(doc.Walls, ReferenceEqualityComparer.Instance);
            foreach (var b in before)
            {
                if (ReferenceEquals(b.w, tw))
                    continue;
                if (!now.Contains(b.w))
                {
                    lost++;
                    continue;
                }
                var next = b.w.NextWall >= 0 && b.w.NextWall < doc.Walls.Count ? doc.Walls[b.w.NextWall] : null;
                bool same = b.next == null ? b.w.NextWall == b.raw : ReferenceEquals(next, b.next);
                if (!same || b.w.NextSector != b.ns)
                    changed++;
            }
            int orphansNow = doc.Walls.Count(w => w.Sector < 0);
            bool ok = changed == 0 && lost == 0 && orphansNow == orphans;
            if (!ok)
                failures++;
            GD.Print($"selftest preserve ({name}): {oneWay} one-way links, {orphans} walls in no sector; after a point on wall {target}: "
                + $"{changed} other links changed, {lost} walls lost, {orphansNow} in no sector{(ok ? "" : ": FAILED")}");
        }
        return failures;
    }

    /// <summary>
    /// A level from nothing, as a mapper makes one: File > New, a rectangle drawn (a sector), a room drawn inside it,
    /// a player start inserted; saved as tools/forge_fuzz/NEWMAP.mapx and .map (for the game: --file=...).
    /// </summary>
    static int NewMap()
    {
        var doc = MapDocument.New();
        int s = doc.MakeSector(new List<Vector2I> { new(-2048, -2048), new(2048, -2048), new(2048, 2048), new(-2048, 2048) });
        int r = s >= 0 ? doc.InsertInnerLoop(s, new List<Vector2I> { new(512, -512), new(1024, -512), new(1024, 512), new(512, 512) }) : -1;
        if (r >= 0)
            doc.Sectors[r].FloorZ -= 256 * 16;        // a step 16 units up
        int p = doc.AddSprite(-1024, 0, 1);
        doc.NotifyChanged();
        int e = Errors(doc);
        string outDir = Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), "..", "tools", "forge_fuzz"));
        Directory.CreateDirectory(outDir);
        File.WriteAllBytes(Path.Combine(outDir, "NEWMAP.mapx"), doc.SaveBytes(MapFormat.MapX, false, out _));
        File.WriteAllBytes(Path.Combine(outDir, "NEWMAP.map"), doc.SaveBytes(MapFormat.Map, false, out _));
        bool ok = s >= 0 && r >= 0 && p >= 0 && doc.Sprites[p].SectNum == s && e == 0;
        GD.Print($"selftest new map: sector {s}, room {r}, player start {p} in sector {doc.Sprites[p].SectNum}, {e} map rule errors"
            + (ok ? "" : ": FAILED"));
        return ok ? 0 : 1;
    }

    /// <summary>
    /// Shapes drawn over sectors (MakeSectorAcross, UDB's drawing): over a corner, across a red wall, round a room,
    /// inside along the walls, over a corner with an island beside it. Each must give the sectors expected, no map
    /// error or loop warning, the area kept (the parts add up), the sprite in the part holding it.
    /// </summary>
    static int Across()
    {
        static List<Vector2I> Rect(int x1, int y1, int x2, int y2) => new() { new(x1, y1), new(x2, y1), new(x2, y2), new(x1, y2) };
        int failures = 0;
        void Case(string what, Func<MapDocument, (int sectors, double area)> setup, List<Vector2I> drawn, Vector2I sprite)
        {
            var doc = MapDocument.New();
            var (expect, area) = setup(doc);
            int sp = doc.AddSprite(sprite.X, sprite.Y, 0);
            doc.NotifyChanged();
            var made = doc.MakeSectorAcross(drawn, out string why);
            doc.NotifyChanged();
            double total = doc.Areas.Sum();
            int home = BuildGeom.UpdateSector(doc, sprite.X, sprite.Y, -1);
            bool ok = made != null && doc.Sectors.Count == expect && Errors(doc) == 0 && StructureWarnings(doc) == 0
                && (area < 0 || Math.Abs(total - area) < 1) && doc.Sprites[sp].SectNum == home && home >= 0;
            GD.Print($"selftest across: {what}: {doc.Sectors.Count} sectors (expected {expect}), area {total} ({area}), "
                + $"{Errors(doc)} errors, {StructureWarnings(doc)} loop warnings, sprite in {doc.Sprites[sp].SectNum} ({home})"
                + (why != null ? $" \"{why}\"" : "") + (ok ? "" : ": FAILED"));
            if (!ok)
                failures++;
        }
        const double S = 4096.0 * 4096;
        Case("over a corner", d =>
        {
            d.MakeSector(Rect(-2048, -2048, 2048, 2048));
            return (3, S + 1024.0 * 1024 * 3);
        }, Rect(1024, 1024, 3072, 3072), new(1500, 1500));
        Case("across a red wall", d =>
        {
            d.MakeSector(Rect(-2048, -2048, 0, 2048));
            d.MakeSectorJoined(Rect(0, -2048, 2048, 2048));
            return (4, S);
        }, Rect(-1024, -1024, 1024, 1024), new(-512, 0));
        Case("round a room", d =>
        {
            int a = d.MakeSector(Rect(-2048, -2048, 2048, 2048));
            d.InsertInnerLoop(a, Rect(256, 256, 768, 768));
            return (4, S + 3072.0 * 3072 - 2048.0 * 2048);
        }, Rect(0, 0, 3072, 3072), new(512, 512));
        Case("inside, along two walls", d =>
        {
            d.MakeSector(Rect(-2048, -2048, 2048, 2048));
            return (2, S);
        }, Rect(-2048, -2048, 0, 0), new(-1024, -1024));
        Case("over a corner, an island beside", d =>
        {
            d.MakeSector(Rect(-2048, -2048, 2048, 2048));
            d.MakeSector(Rect(2560, 2560, 2816, 2816));
            return (4, S + 1024.0 * 1024 * 3);
        }, Rect(1024, 1024, 3072, 3072), new(2600, 2600));
        {
            // a white wall collapsed (Walls mode's Delete): of a room joined to another, the wall across from the red one
            var doc = MapDocument.New();
            doc.MakeSector(Rect(-2048, -2048, 0, 2048));
            doc.MakeSectorJoined(Rect(0, -2048, 2048, 2048));
            doc.NotifyChanged();
            int w = Enumerable.Range(doc.Sectors[1].WallPtr, doc.Sectors[1].WallNum)
                .First(x => doc.Walls[x].X == 2048 && doc.Walls[doc.Walls[x].Point2].X == 2048);
            string why = doc.CollapseWall(w);
            doc.NotifyChanged();
            bool ok = why == null && doc.Sectors[1].WallNum == 3 && doc.Walls.Any(x => x.X == 2048 && x.Y == 0)
                && Errors(doc) == 0 && StructureWarnings(doc) == 0;
            GD.Print($"selftest delete: a white wall collapsed: {doc.Sectors[1].WallNum} walls, {Errors(doc)} errors{(why != null ? $" \"{why}\"" : "")}"
                + (ok ? "" : ": FAILED"));
            if (!ok)
                failures++;
        }
        {
            // a point of a triangle filling a room's notch deleted (a report's map): the triangle goes, the room's wall
            // runs across and faces the sector beyond the triangle (3 sectors become 2)
            var doc = MapDocument.New();
            doc.MakeSector(new List<Vector2I> { new(-2072, -2400), new(928, -2405), new(928, -864), new(3055, -864), new(3080, 1816), new(-2032, 1824) });
            doc.MakeSectorJoined(new List<Vector2I> { new(928, -2405), new(3055, -864), new(928, -864) });
            doc.MakeSectorJoined(new List<Vector2I> { new(4000, -864), new(3055, -864), new(928, -2405), new(3264, -3648) });
            doc.NotifyChanged();
            int before = doc.Sectors.Count;
            int p = Enumerable.Range(0, doc.Points.Count).First(i => doc.Points[i].X == 928 && doc.Points[i].Y == -864);
            string why = doc.DeletePoint(p);
            doc.NotifyChanged();
            bool across = Enumerable.Range(doc.Sectors[0].WallPtr, doc.Sectors[0].WallNum).Any(w =>
                doc.Walls[w].X == 928 && doc.Walls[w].Y == -2405 && doc.Walls[doc.Walls[w].Point2].X == 3055 && doc.Walls[w].NextSector >= 0);
            bool ok = why == null && before == 3 && doc.Sectors.Count == 2 && across && Errors(doc) == 0 && StructureWarnings(doc) == 0;
            GD.Print($"selftest delete: a triangle's point: {before} sectors to {doc.Sectors.Count}, the wall across red: {across}, "
                + $"{Errors(doc)} errors{(why != null ? $" \"{why}\"" : "")}" + (ok ? "" : ": FAILED"));
            if (!ok)
                failures++;
        }
        Case("through a corner, crossing at an angle", d =>
        {
            d.MakeSector(Rect(-2048, -2048, 2048, 2048));
            return (3, -1);
        }, new List<Vector2I> { new(1280, 2176), new(2304, 1152), new(3328, 2176), new(2304, 3200) }, new(2000, 1900));
        return failures;
    }

    /// <summary>
    /// The 3D preview's camera sector where rooms share a place (TrackSector, report 20261103-124810): rooms A and B
    /// joined, C over their red wall at the same heights. Flown from A through the red wall: B, not C; on in B: B;
    /// from C across the same place: C; put there afresh: one of them by height.
    /// </summary>
    static int CameraTrack()
    {
        static List<Vector2I> Rect(int x1, int y1, int x2, int y2) => new() { new(x1, y1), new(x2, y1), new(x2, y2), new(x1, y2) };
        var d = MapDocument.New();
        int a = d.MakeSector(Rect(-2048, -2048, 0, 2048));
        d.NotifyChanged();
        d.MakeSectorJoined(Rect(0, -2048, 2048, 2048));
        d.NotifyChanged();
        int c = d.MakeSector(Rect(-512, -512, 512, 512), out _, over: true);
        d.NotifyChanged();
        int b = BuildGeom.UpdateSector(d, 1500, 0, -1);
        View.Preview3D.ZsOf zs = (int s, double x, double y, out double ce, out double f) => BuildGeom.GetZsOfSlope(d, s, x, y, out ce, out f);
        double z = (d.Sectors[a].CeilingZ + d.Sectors[a].FloorZ) / 2.0;
        int failures = 0;
        void Case(string what, int was, double px, double py, double x, double y, int expect)
        {
            int got = View.Preview3D.TrackSector(d, zs, was, px, py, x, y, z);
            bool ok = got == expect;
            GD.Print($"selftest camera sector: {what}: {got} (expected {expect})" + (ok ? "" : ": FAILED"));
            if (!ok)
                failures++;
        }
        bool joined = d.Sectors.Count == 3 && b != a && b != c && d.SectorsAt(100, 0).Count == 2;
        GD.Print($"selftest camera sector: rooms {a}, {b} joined, {c} over their wall" + (joined ? "" : ": FAILED (setup)"));
        if (!joined)
            return 1;
        Case("from A through the red wall into B and C's place", a, -1000, 0, 100, 0, b);
        Case("on in B", b, 100, 0, 300, 0, b);
        Case("from C across the red wall", c, -100, 0, 100, 0, c);
        Case("from A, not through C's walls", a, -1000, 0, -100, 0, a);
        int fresh = View.Preview3D.TrackSector(d, zs, -1, 100, 0, 100, 0, z);
        Case("put there afresh", -1, 100, 0, 100, 0, fresh == b || fresh == c ? fresh : -2);
        return failures;
    }

    /// <summary>
    /// A sprite moved to another layer on its own goes into that layer's sector holding it (report 20261103-124810: a
    /// player start moved to the room below must start the player there), onto its floor when outside its heights.
    /// </summary>
    static int SpriteToLayer()
    {
        static List<Vector2I> Rect(int x1, int y1, int x2, int y2) => new() { new(x1, y1), new(x2, y1), new(x2, y2), new(x1, y2) };
        var d = MapDocument.New();
        int below = d.MakeSector(Rect(-2048, -2048, 2048, 2048));
        d.NotifyChanged();
        d.CurrentLayer = d.AddLayer("Above");
        int above = d.MakeSector(Rect(-512, -512, 512, 512), out _, over: true);
        d.Sectors[above].FloorZ -= 70000;
        d.Sectors[above].CeilingZ -= 70000;
        d.NotifyChanged();
        int sp = d.AddSprite(0, 0, 1, below);
        d.SetSpriteLayer(sp, 0);
        d.MoveToLayer(Array.Empty<int>(), new[] { sp }, 1);
        bool up = d.Sprites[sp].SectNum == above && d.SpriteLayer(sp) == 1 && d.Sprites[sp].Z == d.Sectors[above].FloorZ;
        d.MoveToLayer(Array.Empty<int>(), new[] { sp }, 0);
        bool down = d.Sprites[sp].SectNum == below && d.SpriteLayer(sp) == 0 && d.Sprites[sp].Z == d.Sectors[below].FloorZ;
        bool ok = above >= 0 && d.LayerOf(above) == 1 && d.LayerOf(below) == 0 && up && down;
        GD.Print($"selftest sprite to layer: into the room above ({up}), back below ({down})" + (ok ? "" : ": FAILED"));
        return ok ? 0 : 1;
    }

    /// <summary>
    /// The layers a .MAP gets (Blood.AutoLayers): on Blood's levels no layer may hold two sectors sharing a place;
    /// saved as .MAPX and read back they must be the same.
    /// </summary>
    /// <summary>
    /// The 2D view's link lines (Blood.MapLinks) on E1M1 and E1M3: E1M1's sliding door (sector 4, type 614) receives
    /// channel 100 from the walls that open it and owns its off and on markers; every channel link joins a sender and a
    /// receiver of that channel; E1M3's rooms over rooms give link-marker pairs.
    /// </summary>
    static int LinkCheck()
    {
        int failures = 0;
        void Check(bool ok, string what)
        {
            GD.Print($"selftest links: {what}" + (ok ? "" : ": FAILED"));
            if (!ok)
                failures++;
        }
        if (FindMap("E1M1") is { } p1)
        {
            var d = MapDocument.Open(p1);
            var links = Blood.MapLinks.All(d);
            var door = new View.Element(View.ElementKind.Sector, 4);
            int senders = links.Count(l => l.To == door && l.Kind == View.LinkKind.Channel && l.Label == "100");
            var markers = links.Where(l => l.From == door && l.Kind == View.LinkKind.Marker).Select(l => l.Label).OrderBy(t => t).ToList();
            bool channels = links.Where(l => l.Kind == View.LinkKind.Channel).All(l => TxOf(d, l.From) == int.Parse(l.Label) && RxOf(d, l.To) == int.Parse(l.Label));
            Check(senders > 0 && string.Join(",", markers) == "off,on" && channels,
                $"E1M1: {links.Count} links; the door (sector 4) sent channel 100 by {senders}, its markers {string.Join(", ", markers)}; every channel link a sender and a receiver ({channels})");
        }
        if (FindMap("E1M3") is { } p3)
        {
            var d = MapDocument.Open(p3);
            var links = Blood.MapLinks.All(d);
            int pairs = links.Count(l => l.Kind == View.LinkKind.Link && l.SectorA >= 0 && l.SectorB >= 0);
            Check(pairs > 0 && pairs == Blood.AutoLayers.Links(d).Count, $"E1M3: {pairs} link-marker pairs (AutoLayers.Links: {Blood.AutoLayers.Links(d).Count})");
        }
        return failures;
    }

    /// <summary>
    /// The moving sectors' Off and On places (Blood.SectorMotion, NBlood's trInit and TranslateSector) on E1M1: the
    /// sliding door (sector 4, 614, its on marker 896 units west of its off marker) stored ON: at On every wall as
    /// stored, at Off its marked walls 896 units east (16384) or west (32768); the lift (sector 6, 600) at its off and
    /// on floor heights; the stored map untouched.
    /// </summary>
    static int MotionCheck()
    {
        if (FindMap("E1M1") is not { } path)
            return 0;
        var d = MapDocument.Open(path);
        var before = d.Snapshot();
        var on = d.Copy();
        Blood.SectorMotion.Pose(d, on, _ => 1.0);
        var off = d.Copy();
        Blood.SectorMotion.Pose(d, off, _ => 0.0);
        var door = d.Sectors[4];
        int movedRight = 0, movedLeft = 0, wrong = 0, onWrong = 0;
        for (int w = door.WallPtr; w < door.WallPtr + door.WallNum; w++)
        {
            int dx = off.Walls[w].X - d.Walls[w].X, dy = off.Walls[w].Y - d.Walls[w].Y;
            if (on.Walls[w].X != d.Walls[w].X || on.Walls[w].Y != d.Walls[w].Y)
                onWrong++;
            if (dy != 0 || (dx != 0 && Math.Abs(Math.Abs(dx) - 896) > 1))
                wrong++;
            else if (dx > 0)
                movedRight++;
            else if (dx < 0)
                movedLeft++;
        }
        bool lift = off.Sectors[6].FloorZ == -45056 && on.Sectors[6].FloorZ == -12288;
        bool movers = Blood.SectorMotion.IsMover(d, 4) && Blood.SectorMotion.IsMover(d, 6) && !Blood.SectorMotion.IsMover(d, 1);
        bool untouched = d.Snapshot().AsSpan().SequenceEqual(before);
        bool ok = onWrong == 0 && wrong == 0 && movedRight > 0 && movedLeft > 0 && lift && movers && untouched;
        GD.Print($"selftest motion: E1M1 door: at On {onWrong} walls off their stored place, at Off {movedRight} east and {movedLeft} west "
            + $"by 896, {wrong} wrong; the lift's floor off {off.Sectors[6].FloorZ} on {on.Sectors[6].FloorZ}; movers found ({movers}); "
            + $"the map untouched ({untouched}); {Blood.SectorMotion.Movers(d).Count} movers" + (ok ? "" : ": FAILED"));
        return ok ? 0 : 1;
    }

    /// <summary>
    /// Auto-align against Blood's own maps (aligned in Build's editors): for each white wall whose next wall has the
    /// same picture, x repeat density, flips and y repeat, the next one aligned to it as the auto-align does; how often
    /// that gives the panning the map has (x and y counted apart). A measure, failing only when nearly nothing agrees
    /// (a wrong formula); needs the Blood folder for the pictures' sizes.
    /// </summary>
    /// <summary>Play (Blood.EventSim) on the two reported places: E1M8's corridor, five sliding strips each started
    /// by a toggle switch the level's start turns on, back off after 1.2 .. 2.8 s, then sliding back and forth for ever
    /// (retrigger A and B): a wave, each a step behind the one before, not all at once; E2M6's ice
    /// block, sector 151 rising when entered, linking its busy to the sliding sectors 152 and 175, its floor tilting to
    /// meet 152's (its align wall), staying up (trigger once).</summary>
    static int PlayCheck()
    {
        int fails = 0;
        static void Run(Blood.EventSim sim, double seconds)
        {
            for (double t = 0; t < seconds - 1e-9; t += 0.05)
                sim.Step(Math.Min(0.05, seconds - t));
        }
        if (FindMap("E1M8") is { } p8)
        {
            var d = MapDocument.Open(p8);
            var sim = new Blood.EventSim(d, d.Copy());
            sim.Start();
            int[] strips = { 32, 34, 36, 61, 62 };
            Run(sim, 1.4);
            var early = strips.Select(sim.SectorBusy).ToArray();
            Run(sim, 4.6);
            var late = strips.Select(sim.SectorBusy).ToArray();
            Run(sim, 0.5);
            var later = strips.Select(sim.SectorBusy).ToArray();
            // (still going after 6 s, each at its own place in the wave)
            bool ok = early[0] > 0 && early.Skip(1).All(b => b == 0) && late.Distinct().Count() >= 4
                && late.Zip(later).All(p => p.First != p.Second);
            GD.Print($"selftest play: E1M8 corridor busy at 1.4 s {string.Join(",", early)}, at 6 s {string.Join(",", late)}, "
                + $"at 6.5 s {string.Join(",", later)}" + (ok ? "" : ": FAILED"));
            if (!ok)
                fails++;
        }
        if (FindMap("E2M6") is { } p6)
        {
            var d = MapDocument.Open(p6);
            var shown = d.Copy();
            var sim = new Blood.EventSim(d, shown);
            sim.Start();
            sim.PlayerMoved(-1, 151);
            Run(sim, 4.5);
            int b151 = sim.SectorBusy(151), b152 = sim.SectorBusy(152);
            int tilt = shown.Sectors[151].FloorHeinum, floor = shown.Sectors[151].FloorZ;
            Run(sim, 12);
            int after = sim.SectorBusy(151);
            bool ok = b151 > 16384 && b151 < 49152 && b152 == b151 && tilt != 0 && floor < d.Sectors[151].FloorZ && after == 65536;
            GD.Print($"selftest play: E2M6 ice block entered: at 4.5 s busy 151 {b151}, 152 {b152}, floor {floor}, slope {tilt}; "
                + $"at 16.5 s busy {after}" + (ok ? "" : ": FAILED"));
            if (!ok)
                fails++;
        }
        if (FindMap("E1M1") is { } p1)
        {
            // E1M1's wall crack (sprite 87): broken (E), gone, its channel 111 raising sector 64's ceiling (the wall opens)
            var d = MapDocument.Open(p1);
            var shown = d.Copy();
            var sim = new Blood.EventSim(d, shown);
            sim.Start();
            string used = sim.Use(-1, -1, 87, -1);
            Run(sim, 1);
            int busy = sim.SectorBusy(64);
            bool ok = used == "sprite 87 broken" && !shown.SpriteShown(87) && busy == 65536 && shown.Sectors[64].CeilingZ == d.Sectors[64].Xs.OnCeilZ;
            GD.Print($"selftest play: E1M1 crack broken: {used}, shown {shown.SpriteShown(87)}, sector 64 busy {busy} ceiling {shown.Sectors[64].CeilingZ}"
                + (ok ? "" : ": FAILED"));
            if (!ok)
                fails++;
            // the TNT barrel beside it (sprite 88) blown up instead: the crack in its reach broken with it
            shown = d.Copy();
            sim = new Blood.EventSim(d, shown);
            sim.Start();
            used = sim.Use(-1, -1, 88, -1);
            Run(sim, 1);
            ok = used == "sprite 88 broken" && !shown.SpriteShown(88) && !shown.SpriteShown(87) && sim.SectorBusy(64) == 65536;
            GD.Print($"selftest play: E1M1 barrel blown up: {used}, barrel shown {shown.SpriteShown(88)}, crack shown {shown.SpriteShown(87)}, "
                + $"sector 64 busy {sim.SectorBusy(64)}" + (ok ? "" : ": FAILED"));
            if (!ok)
                fails++;
        }
        return fails == 0 ? 0 : 1;
    }

    /// <summary>The grid's frame (View.MapView2D.GridOrigin, GridAngle: G's axis locks and snapping go by it): a grid
    /// turned 30 degrees and moved snaps a point near one of its crossings onto it, and its axes are a turned square.</summary>
    /// <summary>Copy and paste (Blood.MapPaste): a room (a door) inside a big sector copied and pasted elsewhere in
    /// that sector comes back a room in it (its walls red to it, the map valid), not a sector over it; pasted again
    /// out in empty space, a sector of its own.</summary>
    /// <summary>Walls copied and pasted (Blood.MapPaste.CopyWalls, PasteWalls: a drawing made beforehand): two rooms side
    /// by side and a tall room to the left; its long top wall pasted across both rooms splits each (the ends outside
    /// them cut off), the new walls its picture on both sides; its right wall then pasted down across the left room
    /// splits the two halves it runs through; a wall pasted to end inside a room splits nothing and says so.</summary>
    static int WallPasteCheck()
    {
        static List<Vector2I> Rect(int x1, int y1, int x2, int y2) => new() { new(x1, y1), new(x2, y1), new(x2, y2), new(x1, y2) };
        var d = MapDocument.New();
        d.MakeSector(Rect(0, 0, 4096, 4096));
        d.NotifyChanged();
        d.MakeSectorJoined(Rect(4096, 0, 8192, 4096), out _);
        d.NotifyChanged();
        int tall = d.MakeSector(Rect(-16384, -2048, -4096, 6144));
        d.NotifyChanged();
        int Line(int x1, int y1, int x2, int y2) => Enumerable.Range(0, d.Lines.Count).First(l =>
        {
            var (ax, ay, bx, by) = BuildGeom.WallEnds(d, d.Lines[l].Front);
            return (ax, ay, bx, by) == (x1, y1, x2, y2) || (ax, ay, bx, by) == (x2, y2, x1, y1);
        });
        int top = Line(-16384, -2048, -4096, -2048), right = Line(-4096, -2048, -4096, 6144);
        d.Walls[d.Lines[top].Front].Picnum = 123;
        var clip = Blood.MapPaste.CopyWalls(d, new[] { top });
        // (to y 2048, x -2048 .. 10240: across both rooms, out of them at each end)
        var (walls, made, note) = Blood.MapPaste.PasteWalls(d, clip, 14336, 4096);
        d.NotifyChanged();
        bool across = made == 2 && d.Sectors.Count == 5 && walls.Count == 4 && walls.All(w => d.Walls[w].Picnum == 123) && Errors(d) == 0;
        var clip2 = Blood.MapPaste.CopyWalls(d, new[] { right });
        var (walls2, made2, note2) = Blood.MapPaste.PasteWalls(d, clip2, 6144, 0);
        d.NotifyChanged();
        bool down = made2 == 2 && d.Sectors.Count == 7 && walls2.Count == 4 && Errors(d) == 0;
        // (x 7168, y -7168 .. 1024: in through the right room's top wall, ending inside its upper part)
        var (walls3, made3, note3) = Blood.MapPaste.PasteWalls(d, clip2, 11264, -5120);
        d.NotifyChanged();
        bool dangling = made3 == 0 && walls3.Count == 0 && note3 != null && d.Sectors.Count == 7 && Errors(d) == 0
            && !d.Points.Any(p => p.X == 7168 && p.Y == 0);
        bool ok = across && down && dangling;
        GD.Print($"selftest wall paste: across {made} split, {d.Sectors.Count} sectors, walls {walls.Count} ({note}) {across}; "
            + $"down {made2} split, walls {walls2.Count} ({note2}) {down}; ending in a room: {made3} ({note3}) {dangling}" + (ok ? "" : ": FAILED"));
        return ok ? 0 : 1;
    }

    /// <summary>Make Portal on two walls (Blood.RoomLinks.MakeWallPortal: NBlood's wall stack): rooms A and B, each with
    /// a closet behind a wall; seen from A, A's side of its wall and B's side of the other (running the other way) become
    /// type 501 with one stack number, the one-way picture 504, blocking; walls of different lengths are refused; Make
    /// Mirror gives a white wall the picture 504 and leaves a red one.</summary>
    static int WallPortalCheck()
    {
        static List<Vector2I> Rect(int x1, int y1, int x2, int y2) => new() { new(x1, y1), new(x2, y1), new(x2, y2), new(x1, y2) };
        var d = MapDocument.New();
        int a = d.MakeSector(Rect(0, 0, 4096, 4096));
        d.NotifyChanged();
        d.MakeSectorJoined(Rect(4096, 0, 4608, 4096), out _);
        d.NotifyChanged();
        int b = d.MakeSector(Rect(16384, 0, 20480, 4096));
        d.NotifyChanged();
        d.MakeSectorJoined(Rect(15872, 1024, 16384, 3072), out _);
        d.NotifyChanged();
        int Line(int x1, int y1, int x2, int y2) => Enumerable.Range(0, d.Lines.Count).First(l =>
        {
            var (ax, ay, bx, by) = BuildGeom.WallEnds(d, d.Lines[l].Front);
            return (ax, ay, bx, by) == (x1, y1, x2, y2) || (ax, ay, bx, by) == (x2, y2, x1, y1);
        });
        int la = Line(4096, 0, 4096, 4096), lshort = Line(16384, 1024, 16384, 3072);
        // (B's closet wall is shorter: refused)
        string refused = Blood.RoomLinks.MakeWallPortal(d, la, lshort, d.Walls[d.Lines[la].Front].Sector != a, out _, out _);
        // (the rules Make Portal's dialog lists: A's outer wall, white and 4096 long, and B's closet wall, 2048: red and
        // length not met, parallel and not a portal met; Forge report 20261117-183624)
        var rules = Blood.RoomLinks.WallPortalRules(d, Line(0, 0, 0, 4096), lshort);
        bool listed = rules.Count == 4 && rules[0].Problem != null && rules[1].Problem != null && rules[2].Problem == null && rules[3].Problem == null;
        // (B's own wall beside the closet: (16384, 3072) .. (16384, 4096) is 1024 long; a closet the same length below it)
        d.MakeSectorJoined(Rect(15872, 0, 16384, 1024), out _);
        d.NotifyChanged();
        int la2 = Enumerable.Range(0, d.Lines.Count).First(l =>
        {
            var (ax, ay, bx, by) = BuildGeom.WallEnds(d, d.Lines[l].Front);
            return ax == 4096 && bx == 4096 && d.Lines[l].Back >= 0;
        });
        // (A's wall split to 1024 long: the closet joined at y 0 .. 1024 only)
        d.InsertPoint(d.Lines[la2].Front, 4096, 1024);
        d.NotifyChanged();
        int lpa = Line(4096, 0, 4096, 1024), lb = Line(16384, 0, 16384, 1024);
        bool fromBack = d.Walls[d.Lines[lpa].Front].Sector != a;
        string why = Blood.RoomLinks.MakeWallPortal(d, lpa, lb, fromBack, out int wa, out int wb);
        d.NotifyChanged();
        bool made = why == null && d.Walls[wa].Sector == a && d.Walls[wb].Sector == b
            && d.Walls[wa].Type == 501 && d.Walls[wb].Type == 501 && d.Walls[wa].Xw?.Data is > 0 && d.Walls[wa].Xw.Data == d.Walls[wb].Xw?.Data
            && d.Walls[wa].OverPicnum == 504 && (d.Walls[wa].Cstat & 33) == 33 && Errors(d) == 0;
        string mwhy = Blood.RoomLinks.MakeMirror(d, new[] { Line(0, 0, 0, 4096), lb }, out var mirrors);
        bool mirror = mwhy == null && mirrors.Count == 1 && d.Walls[mirrors[0]].Picnum == 504 && d.Walls[d.Lines[lb].Front].Picnum != 504;
        bool ok = refused != null && listed && made && mirror;
        GD.Print($"selftest wall portal: different lengths refused ({refused}); rules listed {listed} ("
            + string.Join("; ", rules.Select(r => r.Problem ?? "met")) + $"); made ({why ?? "ok"}): walls {wa} in {(wa >= 0 ? d.Walls[wa].Sector : -1)}, "
            + $"{wb} in {(wb >= 0 ? d.Walls[wb].Sector : -1)} {made}; mirror {string.Join(",", mirrors)} {mirror}" + (ok ? "" : ": FAILED"));
        return ok ? 0 : 1;
    }

    /// <summary>Make Door (Blood.Doors): two rooms joined by a doorway 2048 wide and 512 deep; a sliding pair stored open
    /// (each leaf's edge the lip in from its jamb) closes with the leaves meeting in the middle (SectorMotion.Pose at
    /// OFF); a swinging pair in the far room closes across the opening, each leaf between the inset and the inset less its
    /// thickness from the threshold; a room that isn't a doorway is refused.</summary>
    static int DoorCheck()
    {
        static List<Vector2I> Rect(int x1, int y1, int x2, int y2) => new() { new(x1, y1), new(x2, y1), new(x2, y2), new(x1, y2) };
        MapDocument Rooms(out int doorway, out int far, int deep = 512)
        {
            var d = MapDocument.New();
            d.MakeSector(Rect(0, 0, 4096, 4096));
            d.NotifyChanged();
            doorway = d.MakeSectorJoined(Rect(1024, 4096, 3072, 4096 + deep), out _);
            d.NotifyChanged();
            far = d.MakeSectorJoined(Rect(0, 4096 + deep, 4096, 8192 + deep), out _);
            d.NotifyChanged();
            return d;
        }
        // the use key on a door's pushing wall (EventSim.Use, as the game's ActionScan): how far open the door is after
        // 3 s (busy, 65536 open), each sector given
        int[] Pushed(MapDocument d, int from, params int[] doors)
        {
            int w = Enumerable.Range(0, d.Walls.Count).FirstOrDefault(i => d.Walls[i].Xw is { TriggerPush: not 0 }, -1);
            var sim = new Blood.EventSim(d, d.Copy());
            sim.Start();
            if (w < 0 || sim.Use(w, -1, -1, from) == null)
                return doors.Select(_ => -1).ToArray();
            for (double t = 0; t < 3; t += 0.05)
                sim.Step(0.05);
            return doors.Select(sim.SectorBusy).ToArray();
        }
        // the x's of the walls of sector s running across the doorway's depth (y 4096..4608), as shown at OFF
        List<int> Edges(MapDocument shown, int s) => Enumerable.Range(shown.Sectors[s].WallPtr, shown.Sectors[s].WallNum)
            .Select(w => BuildGeom.WallEnds(shown, w)).Where(e => e.x1 == e.x2 && e.y1 != e.y2 && e.y1 > 4096 && e.y1 < 4608 && e.x1 > 1024 && e.x1 < 3072)
            .Select(e => e.x1).OrderBy(x => x).ToList();
        // sliding
        var a = Rooms(out int door, out _);
        string swhy = Blood.Doors.Make(a, door, new Blood.Doors.Options(Blood.Doors.DoorKind.SlidePair, 0, 0, 64, 256, -1, -1, 10, 0, null), out _);
        a.NotifyChanged();
        var openEdges = Edges(a, door);
        var closedA = a.Copy();
        Blood.SectorMotion.Pose(a, closedA, s => s == door ? 0 : null);
        var closedEdges = Edges(closedA, door);
        var slideUse = Pushed(a, door, door);
        bool slide = swhy == null && slideUse[0] == 65536 && a.Sectors[door].Type == 614 && a.Sectors[door].Xs is { RxID: >= 100 } && Errors(a) == 0
            && openEdges.SequenceEqual(new[] { 1088, 3008 }) && closedEdges.SequenceEqual(new[] { 2048, 2048 })
            && a.Sprites.Count(sp => sp.Type == 709) == 1;
        // swinging, into the far room
        var b = Rooms(out door, out int room);
        int side = Array.IndexOf(Blood.Doors.Sides(b, door), room);
        int before = b.Sectors.Count;
        string wwhy = Blood.Doors.Make(b, door, new Blood.Doors.Options(Blood.Doors.DoorKind.SwingPair, side, 128, 64, 256, -1, -1, 10, 0, null), out _);
        b.NotifyChanged();
        var leaves = Enumerable.Range(before, b.Sectors.Count - before).ToList();
        var closedB = b.Copy();
        Blood.SectorMotion.Pose(b, closedB, s => leaves.Contains(s) ? 0 : null);
        bool across = leaves.Count == 2 && leaves.All(l => Enumerable.Range(closedB.Sectors[l].WallPtr, closedB.Sectors[l].WallNum)
            .All(w => closedB.Walls[w].Y >= 4608 + 128 - 2 && closedB.Walls[w].Y <= 4608 + 256 + 2));
        var swingUse = leaves.Count == 2 ? Pushed(b, room, leaves[0], leaves[1]) : new[] { -1, -1 };
        bool swing = wwhy == null && swingUse.All(u => u == 65536) && leaves.All(l => b.Sectors[l].Type == 617 && b.Sectors[l].Xs?.Marker0 is int m && m >= 0
            && Math.Abs(b.Sprites[m].Ang) == 512) && across && Errors(b) == 0;
        // a room where a leaf slides into the wall: in the doorway's layer it blocks it; in another (a room over a room
        // at the same place) it doesn't; a swinging pair made while another layer is the one edited
        var e = Rooms(out door, out room);
        int upper = e.AddLayer("upper");
        e.CurrentLayer = 0;
        int inWall = e.MakeSector(Rect(200, 4200, 900, 4500));
        e.NotifyChanged();
        e.SetLayer(inWall, 0);
        string blocked = Blood.Doors.Make(e, door, new Blood.Doors.Options(Blood.Doors.DoorKind.SlidePair, 0, 0, 64, 256, -1, -1, 10, 0, null), out _);
        e.SetLayer(inWall, upper);
        e.CurrentLayer = upper;
        string over = Blood.Doors.Make(e, door, new Blood.Doors.Options(Blood.Doors.DoorKind.SlidePair, 0, 0, 64, 256, -1, -1, 10, 0, null), out _);
        var f = Rooms(out door, out room);
        f.CurrentLayer = f.AddLayer("other");
        string swingOther = Blood.Doors.Make(f, door, new Blood.Doors.Options(Blood.Doors.DoorKind.SwingPair, Array.IndexOf(Blood.Doors.Sides(f, door), room), 128, 64, 256, -1, -1, 10, 0, null), out _);
        bool layers = inWall >= 0 && blocked != null && over == null && swingOther == null;
        // refused: a room with no thresholds
        var c = Rooms(out _, out int notDoor);
        string refused = Blood.Doors.Make(c, notDoor, new Blood.Doors.Options(Blood.Doors.DoorKind.SlidePair, 0, 0, 64, 256, -1, -1, 10, 0, null), out _);
        // the use key on each wall given in turn, 3 s apart: how far open the doors are at the end
        int[] PushedEach(MapDocument d, int from, IEnumerable<int> walls, params int[] doors)
        {
            var sim = new Blood.EventSim(d, d.Copy());
            sim.Start();
            foreach (int w in walls)
            {
                if (sim.Use(w, -1, -1, from) == null)
                    return doors.Select(_ => -1).ToArray();
                for (double t = 0; t < 3; t += 0.05)
                    sim.Step(0.05);
            }
            return doors.Select(sim.SectorBusy).ToArray();
        }
        // the options: its own pictures and sounds, a key, used once (pushed again, on another face too: still open,
        // where the plain sliding pair closes)
        var g = Rooms(out door, out _);
        string owhy = Blood.Doors.Make(g, door, new Blood.Doors.Options(Blood.Doors.DoorKind.SlidePair, 0, 0, 64, 256, 300, 301, 10, 20, new[] { 103, 0, 103, 0 }, Key: 2, Once: true), out _);
        g.NotifyChanged();
        var pushers = Enumerable.Range(0, g.Walls.Count).Where(i => g.Walls[i].Xw is { TriggerPush: not 0 }).ToList();
        bool marked = owhy == null && pushers.Count == 4 && pushers.All(i => g.Walls[i].Xw is { Key: 2, TriggerOnce: 1, Command: 1 } && g.Walls[i].Picnum == 300)
            && g.Walls.Count(wl => wl.Picnum == 301) == 2 && g.Sprites.FirstOrDefault(sp => sp.Type == 709)?.Xsp is { Data1: 103, Data2: 0, Data3: 103, Data4: 0 }
            && g.Sectors[door].Xs?.WaitTimeA == 0;
        var onceUse = pushers.Count == 4 ? PushedEach(g, door, new[] { pushers[0], pushers[1], pushers[0] }, door) : new[] { -1 };
        var aPushers = Enumerable.Range(0, a.Walls.Count).Where(i => a.Walls[i].Xw is { TriggerPush: not 0 }).ToList();
        var againUse = aPushers.Count == 4 ? PushedEach(a, door, new[] { aPushers[0], aPushers[1] }, door) : new[] { -1 };
        // opened only by its channel: no wall of it pushes, no sounds; a switch (a wall of the far room) sending on it opens it
        var h = Rooms(out door, out int hroom);
        string cwhy = Blood.Doors.Make(h, door, new Blood.Doors.Options(Blood.Doors.DoorKind.SlidePair, 0, 0, 64, 256, -1, -1, 10, 0, new int[4], Push: false, Channel: 150), out _);
        h.NotifyChanged();
        bool channelOnly = cwhy == null && h.Walls.All(wl => wl.Xw is not { TriggerPush: not 0 }) && h.Sectors[door].Xs?.RxID == 150
            && !h.Sprites.Any(sp => sp.Type == 709);
        int sw = Enumerable.Range(h.Sectors[hroom].WallPtr, h.Sectors[hroom].WallNum).First(i => h.Walls[i].NextSector < 0);
        var swx = h.EnsureXWall(sw);
        (swx.TxID, swx.Command, swx.TriggerPush, swx.TriggerOn, swx.TriggerOff) = (150, 3, 1, 1, 1);
        var switchUse = PushedEach(h, hroom, new[] { sw }, door);
        bool options = marked && onceUse[0] == 65536 && againUse[0] == 0 && channelOnly && switchUse[0] == 65536;

        // Door Properties: a door read back; its settings changed in place (no wall more or less); its lip changed (made
        // again, the settings kept); made a swinging pair (the doorway plain again, found from a leaf and from the
        // doorway); its inset changed, then a sliding pair again (the leaves gone, the room whole); then opened only by
        // its channel; and a sliding sector not Make Door's: its settings change, its shape is refused
        static int Markers(MapDocument d) => d.Sprites.Count(sp => sp.StatNum == 10);
        static int SoundsIn(MapDocument d) => d.Sprites.Count(sp => sp.Type == Blood.Doors.SoundSector);
        var q = Rooms(out int qd, out int qfar);
        Blood.Doors.Make(q, qd, new Blood.Doors.Options(Blood.Doors.DoorKind.SlidePair, 0, 0, 64, 256, -1, -1, 10, 0, null), out _);
        q.NotifyChanged();
        var r1 = Blood.Doors.Find(q, qd);
        bool readBack = r1 is { Swing: false, NotShaped: null } && r1.Now.Thickness == 256 && r1.Now.Lip == 64 && r1.Now.Face == Blood.Doors.SlideFace
            && r1.Now.Edge == Blood.Doors.SlideEdge && r1.Now.SoundIds.SequenceEqual(Blood.Doors.SlideSounds) && r1.Now.Push && r1.Pushers.Count == 4 && r1.Now.Channel >= 100;
        int qWalls = q.Walls.Count;
        string e1 = r1 == null ? "not found" : Blood.Doors.Adjust(q, qd, r1.Now with { Face = 300, Sounds = new[] { 103, 0, 103, 0 }, Key = 3, WaitTime = 30, BusyTime = 20 }, out _);
        q.NotifyChanged();
        var r2 = Blood.Doors.Find(q, qd);
        bool inPlace = e1 == null && q.Walls.Count == qWalls && r2 != null && r2.Now.Face == 300 && r2.Now.Edge == Blood.Doors.SlideEdge
            && r2.Now.SoundIds.SequenceEqual(new[] { 103, 0, 103, 0 }) && r2.Now.Key == 3 && r2.Now.WaitTime == 30 && r2.Now.BusyTime == 20
            && r2.Now.Channel == r1.Now.Channel && SoundsIn(q) == 1 && Errors(q) == 0;
        string e2 = r2 == null ? "not found" : Blood.Doors.Adjust(q, qd, r2.Now with { Lip = 128 }, out _);
        q.NotifyChanged();
        var r3 = Blood.Doors.Find(q, qd);
        bool relipped = e2 == null && r3 is { NotShaped: null } && r3.Now.Lip == 128 && r3.Now.Face == 300 && r3.Now.Key == 3 && r3.Now.Channel == r1.Now.Channel
            && SoundsIn(q) == 1 && Markers(q) == 2 && Errors(q) == 0 && Pushed(q, qd, qd)[0] == 65536;
        int qSide = r3 == null ? -1 : Array.IndexOf(Blood.Doors.SidesOf(q, r3) ?? Array.Empty<int>(), qfar);
        string e3 = r3 == null ? "not found" : Blood.Doors.Adjust(q, qd, r3.Now with { Kind = Blood.Doors.DoorKind.SwingPair, Side = qSide, Thickness = 128, Inset = 256,
            Face = Blood.Doors.SwingFace, Edge = Blood.Doors.SwingEdge }, out _);
        q.NotifyChanged();
        var qLeaves = Enumerable.Range(0, q.Sectors.Count).Where(i => q.Sectors[i].Type == Blood.Doors.Rotate).ToList();
        var fromLeaf = qLeaves.Count == 2 ? Blood.Doors.Find(q, qLeaves[1]) : null;
        var fromDoorway = Blood.Doors.Find(q, qd);
        bool toSwing = e3 == null && q.Sectors[qd].Type == 0 && q.Sectors[qd].WallNum == 4 && q.Sectors[qd].Xs == null && fromLeaf is { Swing: true, NotShaped: null }
            && fromDoorway != null && fromDoorway.Leaves.SequenceEqual(fromLeaf.Leaves) && fromLeaf.Doorway == qd && fromLeaf.Now.Side == qSide
            && fromLeaf.Now.Inset == 256 && fromLeaf.Now.Thickness == 128 && fromLeaf.Now.Channel == r1.Now.Channel && fromLeaf.Now.Key == 3
            && SoundsIn(q) == 1 && Markers(q) == 2 && Errors(q) == 0 && Pushed(q, qfar, qLeaves[0], qLeaves[1]).All(u => u == 65536);
        string e4 = fromLeaf == null ? "not found" : Blood.Doors.Adjust(q, qLeaves[0], fromLeaf.Now with { Inset = 320 }, out _);
        q.NotifyChanged();
        var r5 = Blood.Doors.Find(q, qd);
        string e5 = r5 == null ? "not found" : Blood.Doors.Adjust(q, qd, r5.Now with { Kind = Blood.Doors.DoorKind.SlidePair, Thickness = 0, Lip = 64,
            Face = Blood.Doors.SlideFace, Edge = Blood.Doors.SlideEdge }, out _);
        q.NotifyChanged();
        var r6 = Blood.Doors.Find(q, qd);
        bool backToSlide = e4 == null && r5?.Now.Inset == 320 && e5 == null && q.Sectors.Count == 3 && r6 is { Swing: false, NotShaped: null }
            && r6.Now.Thickness == 256 && Markers(q) == 2 && SoundsIn(q) == 1 && Errors(q) == 0 && Pushed(q, qd, qd)[0] == 65536;
        string e6 = r6 == null ? "not found" : Blood.Doors.Adjust(q, qd, r6.Now with { Push = false }, out _);
        q.NotifyChanged();
        bool qChannelOnly = e6 == null && Blood.Doors.Find(q, qd)?.Now.Push == false && q.Walls.All(wl => wl.Xw is not { TriggerPush: not 0 });
        var hand = Rooms(out int hd, out _);
        hand.Sectors[hd].Type = Blood.Doors.SlideMarked;
        hand.EnsureXSector(hd).RxID = 120;
        var rh = Blood.Doors.Find(hand, hd);
        string eh1 = rh == null ? "not found" : Blood.Doors.Adjust(hand, hd, rh.Now with { BusyTime = 33 }, out _);
        string eh2 = rh == null ? "not found" : Blood.Doors.Adjust(hand, hd, rh.Now with { Lip = 99 }, out _);
        bool notOurs = rh is { NotShaped: not null } && eh1 == null && hand.Sectors[hd].Xs.BusyTimeA == 33 && eh2 != null;
        // Blood's own (E1M1): its sliding door (sector 4) and swinging pair (50 and 51) found and read back; a setting
        // changed where it is (no wall more or less)
        bool blood = true;
        string bloodSays = "no E1M1";
        if (FindMap("E1M1") is { } e1m1)
        {
            var m = MapDocument.Open(e1m1);
            var s4 = Blood.Doors.Find(m, 4);
            var s50 = Blood.Doors.Find(m, 50);
            int mWalls = m.Walls.Count;
            string busyWhy = s4 == null ? "not found" : Blood.Doors.Adjust(m, 4, s4.Now with { BusyTime = s4.Now.BusyTime + 1 }, out _);
            blood = s4 is { Swing: false } && s50 is { Swing: true, Leaves.Length: 2 } && s50.Leaves.Contains(51) && busyWhy == null && m.Walls.Count == mWalls
                && m.Sectors[4].Xs.BusyTimeA == s4.Now.BusyTime + 1 && Errors(m) == 0;
            bloodSays = (s4 == null ? "sector 4 not found" : $"sector 4 sliding, channel {s4.Now.Channel}, {s4.Faces.Count} faces, pushed by {s4.Pushers.Count} walls"
                    + (s4.SectorPush ? " and its sector" : "") + $", shape: {s4.NotShaped ?? "Make Door's"}")
                + (s50 == null ? "; sector 50 not found" : $"; sectors {string.Join(" and ", s50.Leaves)} swinging, channel {s50.Now.Channel}, shape: {s50.NotShaped ?? "Make Door's"}")
                + $"; its opening time changed ({busyWhy ?? "ok"})";
        }
        bool props = readBack && inPlace && relipped && toSwing && backToSlide && qChannelOnly && notOurs && blood;
        GD.Print($"selftest door properties: read back {readBack}; settings in place ({e1 ?? "ok"}) {inPlace}; lip changed, made again ({e2 ?? "ok"}) {relipped}; "
            + $"made a swinging pair ({e3 ?? "ok"}) {toSwing}; its inset changed ({e4 ?? "ok"}) and a sliding pair again ({e5 ?? "ok"}) {backToSlide}; "
            + $"only its channel ({e6 ?? "ok"}) {qChannelOnly}; a sliding sector not Make Door's: settings ({eh1 ?? "ok"}), its shape refused ({eh2}) {notOurs}; "
            + $"Blood's own: {bloodSays} {blood}" + (props ? "" : ": FAILED"));

        // the other kinds: doors lifting up, lowering down and parting (the doorway itself, 600: stored closed, open at its
        // heights, pushed through the room's walls facing it); one swinging leaf (hinged right, then left: made again); a
        // pivot (615) in a square doorway (refused in a shallow one), closed across the opening, made a lifting door
        Blood.Doors.Options Of(Blood.Doors.DoorKind k, int side = 0, int thick = 0, int hinge = 0) =>
            new(k, side, thick, 64, 256, -1, -1, 10, 0, null, Hinge: hinge);
        // the use key on wall w from sector from: how far open sector door is after 3 s
        int UsedOn(MapDocument d, int w, int from, int door)
        {
            var sim = new Blood.EventSim(d, d.Copy());
            sim.Start();
            if (sim.Use(w, -1, -1, from) == null)
                return -1;
            for (double t = 0; t < 3; t += 0.05)
                sim.Step(0.05);
            return sim.SectorBusy(door);
        }
        var kindsSaid = new List<string>();
        bool kinds = true;
        foreach (var k in new[] { Blood.Doors.DoorKind.LiftUp, Blood.Doors.DoorKind.LiftDown, Blood.Doors.DoorKind.LiftSplit })
        {
            var ld = Rooms(out int lw, out _);
            int openC = ld.Sectors[lw].CeilingZ, openF = ld.Sectors[lw].FloorZ;
            string lwhy = Blood.Doors.Make(ld, lw, Of(k), out _);
            ld.NotifyChanged();
            var lsec = ld.Sectors[lw];
            // (a room's wall facing it: room 0's, the threshold's other side)
            int facing = Enumerable.Range(0, ld.Walls.Count).First(i => ld.Walls[i].Sector == 0 && ld.Walls[i].NextSector == lw);
            int used = lwhy == null ? UsedOn(ld, facing, 0, lw) : -1;
            var found = Blood.Doors.Find(ld, lw);
            // (its jambs held still: aligned on the bottom, the floor, unless the floor is what moves)
            var jambs = Enumerable.Range(lsec.WallPtr, lsec.WallNum).Where(i => ld.Walls[i].NextSector < 0).ToList();
            bool still = jambs.Count == 2 && jambs.All(i => ((ld.Walls[i].Cstat & 4) != 0) == (k != Blood.Doors.DoorKind.LiftDown));
            bool ok1 = lwhy == null && lsec.Type == Blood.Doors.ZMotion && lsec.CeilingZ == lsec.FloorZ && lsec.CeilingZ == (k == Blood.Doors.DoorKind.LiftUp ? openF : k == Blood.Doors.DoorKind.LiftDown ? openC : (int)Math.Round((openC + openF) / 2.0))
                && lsec.Xs.OnCeilZ == openC && lsec.Xs.OnFloorZ == openF && lsec.Xs.Wallpush == 1 && used == 65536
                && still && found is { NotShaped: null } && found.Kind == k && ld.Walls[facing].Picnum == Blood.Doors.DefaultFace(k) && Errors(ld) == 0;
            kinds &= ok1;
            kindsSaid.Add($"{k} ({lwhy ?? "ok"}) closed at {lsec.CeilingZ} (open {openC}..{openF}), pushed open {used}, jambs held still {still}, read back {found?.Kind} {ok1}");
        }
        var sg = Rooms(out int sd, out int sfar);
        int sSide = Array.IndexOf(Blood.Doors.Sides(sg, sd), sfar);
        int sBefore = sg.Sectors.Count;
        string swy = Blood.Doors.Make(sg, sd, Of(Blood.Doors.DoorKind.SwingSingle, sSide, 128, 1), out _);
        sg.NotifyChanged();
        int leaf1 = sg.Sectors.Count - 1;
        var sFound = sg.Sectors.Count == sBefore + 1 ? Blood.Doors.Find(sg, leaf1) : null;
        var sFromDoorway = Blood.Doors.Find(sg, sd);
        var sClosed = sg.Copy();
        Blood.SectorMotion.Pose(sg, sClosed, x => x == leaf1 ? 0 : null);
        bool sAcross = sg.Sectors.Count == sBefore + 1 && Enumerable.Range(sClosed.Sectors[leaf1].WallPtr, 4)
            .All(w => sClosed.Walls[w].Y >= 4608 + 128 - 2 && sClosed.Walls[w].Y <= 4608 + 256 + 2);
        int sUsed = swy == null ? Pushed(sg, sfar, leaf1)[0] : -1;
        string sw2 = sFound == null ? "not found" : Blood.Doors.Adjust(sg, leaf1, sFound.Now with { Hinge = 0 }, out _);
        sg.NotifyChanged();
        var sFound2 = Blood.Doors.Find(sg, sd);
        bool single = swy == null && sFound is { Kind: Blood.Doors.DoorKind.SwingSingle, NotShaped: null } && sFound.Now.Hinge == 1 && sFound.Leaves.Length == 1
            && sFromDoorway != null && sFromDoorway.Leaves.SequenceEqual(sFound.Leaves) && sAcross && sUsed == 65536
            && sw2 == null && sFound2 is { Kind: Blood.Doors.DoorKind.SwingSingle, NotShaped: null } && sFound2.Now.Hinge == 0 && sg.Sectors.Count == sBefore + 1 && Errors(sg) == 0;
        kinds &= single;
        kindsSaid.Add($"one swinging leaf ({swy ?? "ok"}) hinged right {sFound?.Now.Hinge}, closed across {sAcross}, pushed open {sUsed}, "
            + $"hinged left again ({sw2 ?? "ok"}) {sFound2?.Now.Hinge} {single}");
        var pShallow = Rooms(out int psd, out _);
        string pRefused = Blood.Doors.Make(pShallow, psd, Of(Blood.Doors.DoorKind.Pivot), out _);
        var pv = Rooms(out int pd, out _, 2048);
        string pwhy = Blood.Doors.Make(pv, pd, Of(Blood.Doors.DoorKind.Pivot), out _);
        pv.NotifyChanged();
        var pLoops = BuildGeom.Loops(pv, pd);
        var pClosed = pv.Copy();
        Blood.SectorMotion.Pose(pv, pClosed, x => x == pd ? 0 : null);
        // (closed across the opening: the leaf's corners within its half thickness of the doorway's middle line)
        bool pAcross = pLoops.Count == 2 && pLoops[1].All(w => Math.Abs(pClosed.Walls[w].Y - (4096 + 1024)) <= 33);
        bool pOpenAlong = pLoops.Count == 2 && pLoops[1].All(w => Math.Abs(pv.Walls[w].X - 2048) <= 33);
        int pUsed = pwhy == null ? Pushed(pv, pd, pd)[0] : -1;
        bool pTurns = pv.Sectors[pd].Type == Blood.Doors.RotateMarked;
        var pFound = Blood.Doors.Find(pv, pd);
        string pw2 = pFound == null ? "not found" : Blood.Doors.Adjust(pv, pd, pFound.Now with { Kind = Blood.Doors.DoorKind.LiftUp, Face = -1, Edge = -1 }, out _);
        pv.NotifyChanged();
        bool pivot = pRefused != null && pwhy == null && pTurns && pAcross && pOpenAlong && pUsed == 65536
            && pFound is { Kind: Blood.Doors.DoorKind.Pivot, NotShaped: null } && pFound.Now.Thickness == 64
            && pw2 == null && BuildGeom.Loops(pv, pd).Count == 1 && pv.Sectors[pd].Type == Blood.Doors.ZMotion && Errors(pv) == 0;
        kinds &= pivot;
        kindsSaid.Add($"a pivot: in a shallow doorway refused ({pRefused}); in a square one ({pwhy ?? "ok"}) closed across {pAcross}, open along {pOpenAlong}, "
            + $"pushed open {pUsed}, read back {pFound?.Kind} {pFound?.Now.Thickness} thick, made a lifting door ({pw2 ?? "ok"}: {BuildGeom.Loops(pv, pd).Count} loops, type {pv.Sectors[pd].Type}, {Errors(pv)} errors) {pivot}");
        // a pivot in that thin doorway with Carve: the room it turns in cut out of both rooms and joined to the doorway
        // (as many sectors as before), as deep as its corners' circle (2016 long, 64 thick: 2033), closed across, pushed open
        var pc = Rooms(out int pcd, out _);
        int pcSectors = pc.Sectors.Count;
        string pcwhy = Blood.Doors.Make(pc, pcd, Of(Blood.Doors.DoorKind.Pivot) with { Carve = true }, out _);
        pc.NotifyChanged();
        var pcLoops = BuildGeom.Loops(pc, pcd);
        var pcClosed = pc.Copy();
        Blood.SectorMotion.Pose(pc, pcClosed, x => x == pcd ? 0 : null);
        bool pcAcross = pcLoops.Count == 2 && pcLoops[1].All(w => Math.Abs(pcClosed.Walls[w].Y - (4096 + 256)) <= 33);
        int pcDeep = pcLoops.Count > 0 ? pcLoops[0].Max(w => pc.Walls[w].Y) - pcLoops[0].Min(w => pc.Walls[w].Y) : 0;
        int pcUsed = pcwhy == null ? Pushed(pc, pcd, pcd)[0] : -1;
        var pcFound = Blood.Doors.Find(pc, pcd);
        bool carved = pcwhy == null && pc.Sectors.Count == pcSectors && pcAcross && pcDeep >= 2033 && pcUsed == 65536
            && pcFound is { Kind: Blood.Doors.DoorKind.Pivot, NotShaped: not null } && Errors(pc) == 0;
        kinds &= carved;
        kindsSaid.Add($"a pivot in a thin doorway with Carve ({pcwhy ?? "ok"}): {pc.Sectors.Count} sectors (was {pcSectors}), its room {pcDeep} deep, "
            + $"closed across {pcAcross}, pushed open {pcUsed}, its shape kept ({pcFound?.NotShaped}) {carved}");
        GD.Print("selftest door kinds: " + string.Join("; ", kindsSaid) + (kinds ? "" : ": FAILED"));
        bool ok = slide && swing && refused != null && layers && options && props && kinds;
        GD.Print($"selftest doors: sliding ({swhy ?? "ok"}) open edges at {string.Join(",", openEdges)}, closed at {string.Join(",", closedEdges)}, "
            + $"pushed open {string.Join(",", slideUse)}, errors {Errors(a)} {slide}; swinging ({wwhy ?? "ok"}) leaves {string.Join(",", leaves)} closed across the opening {across}, pushed open {string.Join(",", swingUse)}, errors {Errors(b)} {swing}; "
            + $"a room refused ({refused}); a room in the wall blocks it in the doorway's layer ({blocked}), in another not ({over ?? "made"}), "
            + $"a swinging pair with another layer edited ({swingOther ?? "made"}) {layers}; "
            + $"its own pictures, sounds, key, used once ({owhy ?? "made"}) {marked}, pushed three times {onceUse[0]} (reusable twice: {againUse[0]}), "
            + $"opened only by its channel ({cwhy ?? "made"}) {channelOnly}, by a switch {switchUse[0]} {options}" + (ok ? "" : ": FAILED"));
        return ok ? 0 : 1;
    }

    /// <summary>A double-click on a wall in Walls mode (WallsMode.InLine): a room's top wall split in three, the middle
    /// one double-clicked takes the other two and stops at the corners (a turn of 90 degrees).</summary>
    static int InLineCheck()
    {
        var d = MapDocument.New();
        d.MakeSector(new List<Vector2I> { new(0, 0), new(4096, 0), new(4096, 4096), new(0, 4096) });
        d.NotifyChanged();
        int Line(int x1, int y1, int x2, int y2) => Enumerable.Range(0, d.Lines.Count).First(l =>
        {
            var (ax, ay, bx, by) = BuildGeom.WallEnds(d, d.Lines[l].Front);
            return (ax, ay, bx, by) == (x1, y1, x2, y2) || (ax, ay, bx, by) == (x2, y2, x1, y1);
        });
        d.InsertPoint(d.Lines[Line(0, 0, 4096, 0)].Front, 1024, 0);
        d.NotifyChanged();
        d.InsertPoint(d.Lines[Line(1024, 0, 4096, 0)].Front, 2048, 0);
        d.NotifyChanged();
        var got = Chosen.Forge.Editing.WallsMode.InLine(d, Line(1024, 0, 2048, 0));
        var want = new HashSet<int> { Line(0, 0, 1024, 0), Line(2048, 0, 4096, 0) };
        bool ok = got.Count == 2 && want.SetEquals(got);
        GD.Print($"selftest wall line: the middle of three in line takes {string.Join(",", got)} (want {string.Join(",", want)})" + (ok ? "" : ": FAILED"));
        return ok ? 0 : 1;
    }

    static int PasteCheck()
    {
        static List<Vector2I> Rect(int x1, int y1, int x2, int y2) => new() { new(x1, y1), new(x2, y1), new(x2, y2), new(x1, y2) };
        var d = MapDocument.New();
        int big = d.MakeSector(Rect(-4096, -4096, 4096, 4096));
        int door = d.InsertInnerLoop(big, Rect(-512, -128, 512, 128));
        d.NotifyChanged();
        var clip = Blood.MapPaste.Copy(d, new[] { door }, new int[0]);
        var (inside, _, why) = Blood.MapPaste.Paste(d, clip, 0, 2048);
        d.NotifyChanged();
        int n = inside?.FirstOrDefault(-1) ?? -1;
        bool room = n >= 0 && Enumerable.Range(d.Sectors[n].WallPtr, d.Sectors[n].WallNum).All(w => d.Walls[w].NextSector == big) && Errors(d) == 0;
        var (outside, _, why2) = Blood.MapPaste.Paste(d, clip, 16384, 0);
        d.NotifyChanged();
        int m = outside?.FirstOrDefault(-1) ?? -1;
        bool alone = m >= 0 && Enumerable.Range(d.Sectors[m].WallPtr, d.Sectors[m].WallNum).All(w => d.Walls[w].NextSector == -1) && Errors(d) == 0;
        // its marker beside it, in the big sector: pasted where the door fits but the marker lands outside every sector,
        // the marker still names a sector (its door's: NBlood stops on a sprite of none)
        int mk = d.AddSprite(3000, 0, 5, big);
        d.Sprites[mk].StatNum = 10;
        d.Sprites[mk].Owner = door;
        d.NotifyChanged();
        var clip2 = Blood.MapPaste.Copy(d, new[] { door }, new int[0]);
        var (beside, besideSprites, why3) = Blood.MapPaste.Paste(d, clip2, 2048, -2048);
        d.NotifyChanged();
        int nd = beside?.FirstOrDefault(-1) ?? -1;
        int nm = besideSprites?.FirstOrDefault(i => d.Sprites[i].Type == 5, -1) ?? -1;
        bool marker = nd >= 0 && nm >= 0 && d.Sprites[nm].X == 5048 && d.Sprites[nm].SectNum == nd && d.Sprites[nm].Owner == nd && Errors(d) == 0;
        // the walls round a room take the look of those round it where it was copied (a coffin's sides are its room's
        // walls): the door's room's walls facing it given picture 77, shade 5, flipped; the door copied into another
        // room (its walls a plain 0): those facing the pasted door 77, shade 5, flipped, the rest of that room as it was
        int other = d.MakeSector(Rect(-4096, 12288, 4096, 20480));
        d.NotifyChanged();
        for (int w = d.Sectors[door].WallPtr; w < d.Sectors[door].WallPtr + d.Sectors[door].WallNum; w++)
        {
            var o = d.Walls[d.Walls[w].NextWall];
            o.Picnum = 77;
            o.Shade = 5;
            o.Cstat |= 8;
        }
        d.NotifyChanged();
        var clip3 = Blood.MapPaste.Copy(d, new[] { door }, new int[0]);
        var (lookd, _, why4) = Blood.MapPaste.Paste(d, clip3, 0, 16384);
        d.NotifyChanged();
        int ld = lookd?.FirstOrDefault(-1) ?? -1;
        var facing = ld < 0 ? new List<int>() : Enumerable.Range(d.Sectors[ld].WallPtr, d.Sectors[ld].WallNum).Select(w => d.Walls[w].NextWall).ToList();
        bool looks = ld >= 0 && facing.Count == 4 && facing.All(w => w >= 0 && d.Walls[w].Sector == other && d.Walls[w].Picnum == 77
            && d.Walls[w].Shade == 5 && (d.Walls[w].Cstat & 8) != 0)
            && Enumerable.Range(d.Sectors[other].WallPtr, d.Sectors[other].WallNum).Where(w => !facing.Contains(w)).All(w => d.Walls[w].Picnum == 0)
            && Errors(d) == 0;
        // pasted inside a room against its wall (the door's edge along that room's south wall): cut out of the room and
        // joined to it by red walls (it was laid over the room, joined to nothing: unseen, walked through)
        var (against, _, why5) = Blood.MapPaste.Paste(d, clip3, 0, 20480 - 128);
        d.NotifyChanged();
        int ag = against?.FirstOrDefault(-1) ?? -1;
        var agWalls = ag < 0 ? new List<int>() : Enumerable.Range(d.Sectors[ag].WallPtr, d.Sectors[ag].WallNum).ToList();
        bool cut = ag >= 0 && agWalls.Count(w => d.Walls[w].NextSector == other) >= 3 && agWalls.Any(w => d.Walls[w].NextSector < 0) && Errors(d) == 0;
        bool ok = room && alone && marker && looks && cut;
        GD.Print($"selftest paste: a door pasted inside its room a room in it {room} ({why ?? "ok"}), out in empty space a sector alone {alone} ({why2 ?? "ok"}); "
            + $"its marker pasted outside the sectors in the door's sector {marker} ({why3 ?? "ok"}, sector {(nm >= 0 ? d.Sprites[nm].SectNum : -2)}); "
            + $"into another room, the walls round it with the look of those round it where copied {looks} ({why4 ?? "ok"}); "
            + $"against that room's wall from inside, cut out of it and joined {cut} ({why5 ?? "ok"}, walls {string.Join(",", agWalls.Select(w => d.Walls[w].NextSector))}); "
            + $"{Errors(d)} map errors" + (ok ? "" : ": FAILED"));
        return ok ? 0 : 1;
    }

    /// <summary>Dissolve (Backspace in Sectors mode): a room in a big sector dissolved goes into the big one (its
    /// neighbour by the most wall), no hole left: one sector of one loop, the map valid.</summary>
    static int DissolveCheck()
    {
        static List<Vector2I> Rect(int x1, int y1, int x2, int y2) => new() { new(x1, y1), new(x2, y1), new(x2, y2), new(x1, y2) };
        var d = MapDocument.New();
        int big = d.MakeSector(Rect(-4096, -4096, 4096, 4096));
        int room = d.InsertInnerLoop(big, Rect(-512, -512, 512, 512));
        d.NotifyChanged();
        int into = Chosen.Forge.Editing.SectorsMode.DissolveInto(d, room);
        string why = d.JoinSectors(into, room);
        d.NotifyChanged();
        bool ok = into == big && why == null && d.Sectors.Count == 1 && BuildGeom.Loops(d, 0).Count == 1 && d.Sectors[0].WallNum == 4 && Errors(d) == 0;
        GD.Print($"selftest dissolve: a room into sector {into} ({why ?? "ok"}): {d.Sectors.Count} sector(s), {BuildGeom.Loops(d, 0).Count} loop(s), "
            + $"{d.Sectors[0].WallNum} walls, {Errors(d)} map errors" + (ok ? "" : ": FAILED"));
        return ok ? 0 : 1;
    }

    /// <summary>Map Analysis' Fix All: a sprite of no sector (NBlood stops on it), a marker nobody owns, an x-sprite's
    /// wrong reference: all fixed, the rules then finding no error and none of those.</summary>
    static int FixCheck()
    {
        static List<Vector2I> Rect(int x1, int y1, int x2, int y2) => new() { new(x1, y1), new(x2, y1), new(x2, y2), new(x1, y2) };
        var d = MapDocument.New();
        int big = d.MakeSector(Rect(-4096, -4096, 4096, 4096));
        d.NotifyChanged();
        int lost = d.AddSprite(100, 100, 1, big);
        d.Sprites[lost].SectNum = -1;
        int orphan = d.AddSprite(200, 200, 5, big);
        d.Sprites[orphan].StatNum = 10;
        d.Sprites[orphan].Owner = 77;
        int wrong = d.AddSprite(300, 300, 20, big);
        d.EnsureXSprite(wrong).Reference = 99;
        d.NotifyChanged();
        int before = Blood.MapAnalysis.Run(d).Count(f => f.Repair);
        var (n, refused) = Blood.MapAnalysis.FixAll(d);
        var left = Blood.MapAnalysis.Run(d);
        bool ok = before >= 3 && refused.Count == 0 && Errors(d) == 0 && d.Sprites.Count == 2 && d.Sprites[lost].SectNum == big
            && !left.Any(f => f.Repair);
        GD.Print($"selftest fix all: {before} fixable, {n} fixed, {refused.Count} refused; {Errors(d)} map errors, {d.Sprites.Count} sprites, "
            + $"{left.Count(f => f.Repair)} repairs left" + (ok ? "" : ": FAILED"));
        return ok ? 0 : 1;
    }

    /// <summary>Floor alignment (the 3D preview's Ctrl+A on a floor, Align Floor to This Wall): two sectors side by
    /// side with one picture, the first panned, carried on into the second (the picture the same at points on both
    /// sides of the wall between them), plain and relative to parallel first walls; then the second aligned to one of
    /// its walls: that wall first, relative, the picture's corner at the wall's start.</summary>
    static int FloorAlignCheck()
    {
        static List<Vector2I> Rect(int x1, int y1, int x2, int y2) => new() { new(x1, y1), new(x2, y1), new(x2, y2), new(x1, y2) };
        var d = MapDocument.New();
        int a = d.MakeSector(Rect(0, 0, 2048, 2048));
        int b = d.MakeSectorJoined(Rect(2048, 0, 4096, 2048), out _);
        d.NotifyChanged();
        const int pic = 275;
        var size = View.PreviewMesher.TileSize(pic);
        foreach (int s0 in new[] { a, b })
            d.Sectors[s0].FloorPicnum = pic;
        bool Same(Vector2 p, Vector2 q) => Math.Abs(Mathf.PosMod(p.X - q.X + 0.5f, 1f) - 0.5f) < 0.01f && Math.Abs(Mathf.PosMod(p.Y - q.Y + 0.5f, 1f) - 0.5f) < 0.01f;
        // the picture at points either side of the shared wall (x 2048), and a way into b
        bool Seamless()
        {
            var fa = new View.PreviewMesher.FlatMap(d, a, false, size);
            var fb = new View.PreviewMesher.FlatMap(d, b, false, size);
            return Same(fa.Uv(2048, 700), fb.Uv(2048, 700)) && Same(fa.Uv(3000, 1500), fb.Uv(3000, 1500));
        }
        d.Sectors[a].FloorXPanning = 37;
        d.Sectors[a].FloorYPanning = 90;
        d.Sectors[a].FloorStat |= 16;
        var start = new View.VisualTarget(View.VisualPart.Floor, a, -1);
        var (n1, _) = Tools.TextureOps.AlignPlanes(d, start);
        bool plain = n1 == 1 && Seamless() && (d.Sectors[b].FloorStat & 16) != 0;
        // each keeps its own relative bit: a relative (first walls both along x), b not; then b relative to a first
        // wall a quarter turn from a's (its picture turned to a's by the swap and a flip), a not
        d.Sectors[a].FloorStat |= 64;
        d.Sectors[b].FloorStat &= ~64;
        var (n2, left2) = Tools.TextureOps.AlignPlanes(d, start);
        bool relative = n2 == 1 && left2 == 0 && Seamless() && (d.Sectors[b].FloorStat & 64) == 0;
        d.Sectors[a].FloorStat &= ~64;
        d.SetFirstWall(b, d.Sectors[b].WallPtr + 1);
        d.Sectors[b].FloorStat |= 64;
        var (n3, left3) = Tools.TextureOps.AlignPlanes(d, start);
        relative &= n3 == 1 && left3 == 0 && Seamless() && (d.Sectors[b].FloorStat & 64) != 0 && (d.Sectors[a].FloorStat & 64) == 0;
        // b along its third wall: that wall first, relative, the picture's corner at its start
        int w = d.Sectors[b].WallPtr + 2;
        var (wx, wy, _, _) = Chosen.Shared.Engine.Geometry.BuildGeom.WallEnds(d, w);
        string why = Tools.TextureOps.AlignPlaneToWall(d, w, false);
        var fw = new View.PreviewMesher.FlatMap(d, b, false, size);
        var (fx, fy, _, _) = Chosen.Shared.Engine.Geometry.BuildGeom.WallEnds(d, d.Sectors[b].WallPtr);
        bool toWall = why == null && (fx, fy) == (wx, wy) && (d.Sectors[b].FloorStat & 64) != 0 && Same(fw.Uv(wx, wy), Vector2.Zero) && Errors(d) == 0;
        bool ok = plain && relative && toWall;
        GD.Print($"selftest floor align: carried on {plain}, each its own relative bit {relative}, to a wall {toWall} ({why ?? "ok"})"
            + (ok ? "" : ": FAILED"));
        return ok ? 0 : 1;
    }

    /// <summary>A sector moved against another (a drag, G): sector b made apart from a, its points moved so its west
    /// wall lies along the middle of a's east wall; JoinTouching splits a's wall where b's ends fall and joins the
    /// middle part to b's into a red pair (their points merged), the map without errors.</summary>
    static int JoinTouchingCheck()
    {
        static List<Vector2I> Box(int x1, int y1, int x2, int y2) => new() { new(x1, y1), new(x2, y1), new(x2, y2), new(x1, y2) };
        var d = MapDocument.New();
        int a = d.MakeSector(Box(0, 0, 1024, 1024));
        int b = d.MakeSector(Box(4096, 256, 5120, 768));
        d.NotifyChanged();
        var pts = Enumerable.Range(d.Sectors[b].WallPtr, d.Sectors[b].WallNum).Select(w => d.PointOf[w]).Distinct().ToList();
        foreach (int p in pts)
            d.MovePoint(p, d.Points[p].X - 3072, d.Points[p].Y);
        d.NotifyChanged();
        int joined = d.JoinTouching(new HashSet<int> { b });
        d.NotifyChanged();
        int red = d.Walls.Count(w => w.NextWall >= 0);
        bool ok = joined == 1 && red == 2 && d.Sectors[a].WallNum == 6 && Errors(d) == 0;
        GD.Print($"selftest join touching: {joined} joined, {red} red walls, sector {a} {d.Sectors[a].WallNum} walls (split twice), {Errors(d)} errors"
            + (ok ? "" : ": FAILED"));
        return ok ? 0 : 1;
    }

    /// <summary>Separate Sector: two sectors joined by a red wall, the second separated (its red walls white on both
    /// sides, its points its own: moving them leaves the first alone), no map errors; JoinTouching joins them again.</summary>
    static int SeparateCheck()
    {
        static List<Vector2I> Box(int x1, int y1, int x2, int y2) => new() { new(x1, y1), new(x2, y1), new(x2, y2), new(x1, y2) };
        var d = MapDocument.New();
        int a = d.MakeSector(Box(0, 0, 1024, 1024));
        int b = d.MakeSectorJoined(Box(1024, 0, 2048, 1024), out _);
        d.NotifyChanged();
        bool joinedFirst = d.Walls.Count(w => w.NextWall >= 0) == 2;
        int cut = d.SeparateSectors(new[] { b });
        d.NotifyChanged();
        bool white = d.Walls.All(w => w.NextWall < 0);
        // b's points its own: none of them holds a wall of a
        bool own = Enumerable.Range(d.Sectors[b].WallPtr, d.Sectors[b].WallNum)
            .All(w => d.Points[d.PointOf[w]].Walls.All(o => d.Walls[o].Sector == b));
        int errors = Errors(d);
        int again = d.JoinTouching(new HashSet<int> { b });
        d.NotifyChanged();
        bool ok = joinedFirst && cut == 1 && white && own && errors == 0 && again == 1 && Errors(d) == 0;
        GD.Print($"selftest separate: {cut} wall cut, all white {white}, its points its own {own}, {errors} errors; joined again {again}"
            + (ok ? "" : ": FAILED"));
        return ok ? 0 : 1;
    }

    /// <summary>The sky: a new map has Blood's (16 pieces, offsets 0..15, parallax type 2); a sky ceiling under a sky
    /// of one piece is a Map Analysis warning whose fix gives it Blood's, and the map still writes and reads back.</summary>
    static int SkyCheck()
    {
        static List<Vector2I> Box(int x1, int y1, int x2, int y2) => new() { new(x1, y1), new(x2, y1), new(x2, y2), new(x1, y2) };
        var d = MapDocument.New();
        bool fresh = d.Map.PSkyBits == 4 && d.Map.SkyOfs.SequenceEqual(Enumerable.Range(0, 16).Select(i => (short)i)) && d.Map.ParallaxType == 2;
        int a = d.MakeSector(Box(0, 0, 1024, 1024));
        d.Sectors[a].CeilingStat |= 1;
        d.Sectors[a].CeilingPicnum = 3678;
        d.NotifyChanged();
        bool quietNew = !Blood.MapAnalysis.Run(d).Any(f => f.Text.Contains("parallax sky"));
        d.Map.PSkyBits = 0;
        d.Map.SkyOfs = new short[1];
        d.Map.ParallaxType = 0;
        var found = Blood.MapAnalysis.Run(d).FirstOrDefault(f => f.Text.Contains("parallax sky"));
        bool warned = found != null && found.Fix != null && found.Index == a;
        if (warned)
            found.Fix(d);
        bool mended = d.Map.PSkyBits == 4 && d.Map.SkyOfs.Length == 16 && d.Map.SkyOfs[15] == 15 && d.Map.ParallaxType == 2
            && !Blood.MapAnalysis.Run(d).Any(f => f.Text.Contains("parallax sky"));
        var back = BloodMapFile.Parse(BloodMapWriter.Write(d.Map));
        bool kept = back.PSkyBits == 4 && back.SkyOfs.SequenceEqual(d.Map.SkyOfs) && back.ParallaxType == 2;
        bool ok = fresh && quietNew && warned && mended && kept;
        GD.Print($"selftest sky: new map Blood's {fresh}, no warning {quietNew}; one piece warned {warned}, fixed {mended}, written and read {kept}"
            + (ok ? "" : ": FAILED"));
        return ok ? 0 : 1;
    }

    /// <summary>The tile picker's tabs and the sprite browser's decorations (Blood.TileUse, data/tileuse.txt: Blood's and its add-ons' maps): the
    /// table loaded; the skies Blood's three; walls, floors and ceilings each hundreds of tiles; a wall texture put up
    /// once as a sprite not a decoration; the tab a kind opens on.</summary>
    static int TileUseCheck()
    {
        var all = Blood.TileUse.All;
        var skies = all.Keys.Where(t => Blood.TileUse.Count(t, Blood.TileTab.Skies) > 0).OrderBy(t => t).ToList();
        int walls = all.Keys.Count(t => Blood.TileUse.Count(t, Blood.TileTab.Walls) > 0);
        int floors = all.Keys.Count(t => Blood.TileUse.Count(t, Blood.TileTab.Floors) > 0);
        int ceilings = all.Keys.Count(t => Blood.TileUse.Count(t, Blood.TileTab.Ceilings) > 0);
        int decos = all.Values.Count(u => u.IsDecoration);
        bool tile0 = all.TryGetValue(0, out var u0) && u0.Decoration > 0 && !u0.IsDecoration;
        bool tabs = Blood.TileUse.TabFor("sky") == Blood.TileTab.Skies && Blood.TileUse.TabFor("masked") == Blood.TileTab.Masked
            && Blood.TileUse.TabFor(null) == Blood.TileTab.All;
        bool ok = all.Count > 1000 && skies.SequenceEqual(new[] { 2500, 3491, 3678 }) && walls > 300 && floors > 150 && ceilings > 100
            && decos > 200 && tile0 && tabs && Blood.TileUse.Describe(2500).Contains("skies");
        GD.Print($"selftest tile use: {all.Count} tiles, skies {string.Join(",", skies)}, {walls} wall / {floors} floor / {ceilings} ceiling tiles, "
            + $"{decos} decorations (tile 0 not one {tile0}), tabs {tabs}" + (ok ? "" : ": FAILED"));
        return ok ? 0 : 1;
    }

    /// <summary>The Gradient tool (Tools.SectorGradient): five rooms in a row, given out of order (the first and the
    /// last at the ends): floors in equal steps by where they lie (stairs), whole game units; a brightness by distance on
    /// floors and walls only; floor and ceiling together keeping each room's height; the rooms stay sound.</summary>
    static int GradientCheck()
    {
        static List<Vector2I> Box(int x1, int y1, int x2, int y2) => new() { new(x1, y1), new(x2, y1), new(x2, y2), new(x1, y2) };
        var d = MapDocument.New();
        var rooms = new List<int> { d.MakeSector(Box(0, 0, 1024, 1024)) };
        for (int i = 1; i < 5; i++)
            rooms.Add(d.MakeSectorJoined(Box(i * 1024, 0, (i + 1) * 1024, 1024), out _));
        d.NotifyChanged();
        // the first, then those between shuffled, then the last
        var order = new List<int> { rooms[0], rooms[3], rooms[1], rooms[2], rooms[4] };
        Chosen.Forge.Tools.SectorGradient.Apply(d, order, "floor", 0, -4100, true, true, 0);
        var floors = rooms.Select(r => d.Sectors[r].FloorZ).ToList();
        bool stairs = floors.SequenceEqual(new[] { 0, -1024, -2048, -3072, -4096 });
        int wallShade0 = d.Walls[d.Sectors[rooms[2]].WallPtr].Shade, ceilShade0 = d.Sectors[rooms[2]].CeilingShade;
        Chosen.Forge.Tools.SectorGradient.Apply(d, order, "shade", 0, 40, false, false,
            Chosen.Forge.Tools.SectorGradient.ShadeFloors | Chosen.Forge.Tools.SectorGradient.ShadeWalls);
        var mid = d.Sectors[rooms[2]];
        bool shade = mid.FloorShade == 20 && d.Walls[mid.WallPtr].Shade == 20 && mid.CeilingShade == ceilShade0 && d.Sectors[rooms[4]].FloorShade == 40;
        var heights = rooms.Select(r => d.Sectors[r].FloorZ - d.Sectors[r].CeilingZ).ToList();
        Chosen.Forge.Tools.SectorGradient.Apply(d, order, "both", -2048, 0, true, false, 0);
        bool both = rooms.Select(r => d.Sectors[r].FloorZ - d.Sectors[r].CeilingZ).SequenceEqual(heights) && d.Sectors[rooms[0]].FloorZ == -2048
            && d.Sectors[rooms[4]].FloorZ == 0;
        int errors = Errors(d);
        bool ok = stairs && shade && both && errors == 0;
        GD.Print($"selftest gradient: stairs {string.Join(",", floors)} ({stairs}), brightness by distance {shade} (wall {wallShade0} -> {d.Walls[mid.WallPtr].Shade}), "
            + $"floors and ceilings keeping the height {both}, {errors} errors" + (ok ? "" : ": FAILED"));
        return ok ? 0 : 1;
    }

    /// <summary>Slope edges (Tools.SlopeOps): a square room's floor, its south edge raised a step twice: the edge at
    /// that height, the far side where it was (the plane through both, the edge the first wall); lowered back: flat (the
    /// slope bit off). A ceiling's edge from another wall of a room already sloped the other way: that edge raised, the
    /// far corner kept. The rooms stay sound.</summary>
    static int SlopeEdgeCheck()
    {
        static List<Vector2I> Box(int x1, int y1, int x2, int y2) => new() { new(x1, y1), new(x2, y1), new(x2, y2), new(x1, y2) };
        var d = MapDocument.New();
        int s = d.MakeSector(Box(0, 0, 2048, 2048));
        d.NotifyChanged();
        double Z(double x, double y, bool c = false) => BuildGeom.PlaneZ(d, s, c, x, y);
        // the south edge (y 2048): its wall from (2048, 2048) to (0, 2048) or the other way
        int South() => Enumerable.Range(d.Sectors[s].WallPtr, d.Sectors[s].WallNum).First(w => BuildGeom.WallEnds(d, w) is var e && e.Item2 == 2048 && e.Item4 == 2048);
        string r1 = Chosen.Forge.Tools.SlopeOps.RaiseEdge(d, s, false, South(), -1024);
        string r2 = Chosen.Forge.Tools.SlopeOps.RaiseEdge(d, s, false, South(), -1024);
        bool up = r1 == null && r2 == null && Math.Abs(Z(0, 2048) + 2048) < 2 && Math.Abs(Z(2048, 2048) + 2048) < 2
            && Math.Abs(Z(0, 0)) < 8 && Math.Abs(Z(2048, 0)) < 8 && Math.Abs(Z(1024, 1024) + 1024) < 8
            && d.Sectors[s].WallPtr == South() && (d.Sectors[s].FloorStat & 2) != 0;
        string r3 = Chosen.Forge.Tools.SlopeOps.RaiseEdge(d, s, false, South(), 2048);
        bool flat = r3 == null && d.Sectors[s].FloorHeinum == 0 && (d.Sectors[s].FloorStat & 2) == 0 && d.Sectors[s].FloorZ == 0;
        // the ceiling: sloped by its south edge, then its west edge raised: the west edge at its new height, the corner
        // farthest from it (east) kept
        Chosen.Forge.Tools.SlopeOps.RaiseEdge(d, s, true, South(), 2048);
        int West() => Enumerable.Range(d.Sectors[s].WallPtr, d.Sectors[s].WallNum).First(w => BuildGeom.WallEnds(d, w) is var e && e.Item1 == 0 && e.Item3 == 0);
        var far = Chosen.Forge.Tools.SlopeOps.FarCorner(d, s, West());
        double farZ = Z(far.x, far.y, true);
        var (wx1, wy1, wx2, wy2) = BuildGeom.WallEnds(d, West());
        double westZ = (Z(wx1, wy1, true) + Z(wx2, wy2, true)) / 2;
        string r4 = Chosen.Forge.Tools.SlopeOps.RaiseEdge(d, s, true, West(), -512);
        var (vx1, vy1, vx2, vy2) = BuildGeom.WallEnds(d, d.Sectors[s].WallPtr);
        bool ceil = r4 == null && Math.Abs(Z(far.x, far.y, true) - farZ) < 8 && Math.Abs(Z(vx1, vy1, true) - (westZ - 512)) < 2
            && Math.Abs(Z(vx2, vy2, true) - (westZ - 512)) < 2;
        // the (flat) floor tilted about its west edge (made the first wall, the hinge): the west side kept, the east
        // side up a step
        string r5 = Chosen.Forge.Tools.SlopeOps.Tilt(d, s, false, West(), -1024);
        bool tilt = r5 == null && Math.Abs(Z(0, 0)) < 2 && Math.Abs(Z(0, 2048)) < 2 && Math.Abs(Z(2048, 0) + 1024) < 8
            && Math.Abs(Z(2048, 2048) + 1024) < 8 && d.Sectors[s].WallPtr == West();
        int errors = Errors(d);
        bool ok = up && flat && ceil && tilt && errors == 0;
        GD.Print($"selftest slope edges: raised twice {up} (edge {Z(0, 2048):0}, far side {Z(0, 0):0}), lowered back flat {flat}, "
            + $"a ceiling sloped the other way tilted about its far corner {ceil}, a floor tilted about its west edge {tilt} (east {Z(2048, 0):0}), {errors} errors" + (ok ? "" : ": FAILED"));
        return ok ? 0 : 1;
    }

    /// <summary>The .MAPX's version (MapXFile.FormatVersion, its migrations): a map saved carries the current version and
    /// reads back as it, not migrated, with saved_by naming this Forge; a file claiming a newer version is refused as
    /// one from a newer Chosen, and version 0 as no version at all.</summary>
    static int MapXVersionCheck()
    {
        var d = MapDocument.New();
        d.MakeSector(new List<Vector2I> { new(0, 0), new(1024, 0), new(1024, 1024), new(0, 1024) });
        var bytes = d.SaveBytes(MapFormat.MapX, false, out _);
        int written = BitConverter.ToUInt16(bytes, 4);
        var (_, x) = MapXFile.Read(bytes);
        string savedBy = x.GetInfo("saved_by") ?? "";
        string Refusal(int version)
        {
            var b = (byte[])bytes.Clone();
            BitConverter.GetBytes((ushort)version).CopyTo(b, 4);
            try
            {
                MapXFile.Read(b);
                return "(read)";
            }
            catch (InvalidDataException e)
            {
                return e.Message;
            }
        }
        string newer = Refusal(MapXFile.FormatVersion + 1), zero = Refusal(0);
        bool ok = written == MapXFile.FormatVersion && x.FileVersion == MapXFile.FormatVersion && !x.Migrated
            && savedBy.StartsWith("Chosen Forge " + Shared.Engine.ChosenVersion.Number) && newer.Contains("newer") && zero.Contains("not a version");
        GD.Print($"selftest mapx version: written {written}, read {x.FileVersion} (migrated {x.Migrated}), saved_by \"{savedBy}\"; "
            + $"version {MapXFile.FormatVersion + 1}: \"{newer}\"; version 0: \"{zero}\": {(ok ? "ok" : "FAILED")}");
        return ok ? 0 : 1;
    }

    /// <summary>The map's own art (Shared.Blood.Files.MapArt, Blood.ArtPacker, ForgeData.UseMapArt): three pictures
    /// packed classic into free tiles below 6144 in one run (a see-through pixel 255, a red one a red colour), packed
    /// again after one changed: the same numbers; extended: from 6144; added over Blood's: its tiles shown, Blood's
    /// kept; an override file replacing Blood's tiles000.art whole (its other tiles gone), the same file added: they
    /// stay; the .MAPX keeps the list, a .MAP says it can't and warns of a picture past 6143; paths rebased.</summary>
    static int MapArtCheck()
    {
        if ((Data.ForgeData.Tiles?.Count ?? 0) == 0 && !Data.ForgeData.Use(Shared.Blood.Files.BloodInstall.Find() ?? ""))
        {
            GD.Print("selftest map art: no Blood folder, skipped");
            return 0;
        }
        string root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "chosen-forge-selftest-art");
        if (System.IO.Directory.Exists(root))
            System.IO.Directory.Delete(root, true);
        string pics = System.IO.Path.Combine(root, "pics"), outC = System.IO.Path.Combine(root, "classic"), outE = System.IO.Path.Combine(root, "extended");
        System.IO.Directory.CreateDirectory(pics);
        void Pic(string name, int w, int h, Func<int, int, Color> c)
        {
            var img = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    img.SetPixel(x, y, c(x, y));
            img.SavePng(System.IO.Path.Combine(pics, name));
        }
        Pic("a_red.png", 64, 64, (x, y) => new Color(0.9f, 0.05f, 0.05f));
        Pic("b_hole.png", 32, 48, (x, y) => x < 16 ? new Color(0, 0, 0, 0) : new Color(0.2f, 0.8f, 0.2f));
        Pic("c_ramp.png", 128, 128, (x, y) => new Color(x / 128f, y / 128f, 0.5f));
        var blood = Data.ForgeData.BloodTiles;
        var pal = Data.ForgeData.Palette;
        var r1 = Blood.ArtPacker.Pack(pics, outC, true, blood, pal);
        var nums = r1.Tiles.ToDictionary(t => t.name, t => t.tile);
        bool classic = r1.Error == null && r1.Tiles.Count == 3 && r1.Files.Count == 1 && nums.Values.All(n => n < Shared.Blood.Files.MapArt.BloodTiles && blood[n] is not { Empty: false })
            && nums.Values.Max() - nums.Values.Min() == 2;
        // packed again, a picture changed: the same numbers
        Pic("b_hole.png", 32, 48, (x, y) => x < 16 ? new Color(0, 0, 0, 0) : new Color(0.1f, 0.1f, 0.9f));
        var r2 = Blood.ArtPacker.Pack(pics, outC, true, blood, pal);
        bool kept = r2.Error == null && r2.Kept == 3 && r2.New == 0 && r2.Tiles.All(t => nums[t.name] == t.tile);
        var r3 = Blood.ArtPacker.Pack(pics, outE, false, blood, pal);
        bool extended = r3.Error == null && r3.Tiles.All(t => t.tile >= Shared.Blood.Files.MapArt.BloodTiles);
        // added over Blood's: the pictures there, Blood's tile 0 as it was; the colours
        Data.ForgeData.UseMapArt(new List<Shared.Blood.Map.ArtRef> { new("classic", false) }, root);
        var red = Data.ForgeData.Tiles[nums["a_red.png"]];
        var hole = Data.ForgeData.Tiles[nums["b_hole.png"]];
        var (rr, rg, rb) = red != null ? pal[red.At(5, 5)] : ((byte)0, (byte)0, (byte)0);
        bool added = red is { Width: 64, Height: 64 } && hole is { Width: 32, Height: 48 } && hole.At(3, 3) == Shared.Blood.Files.Palette.Transparent
            && hole.At(20, 3) != Shared.Blood.Files.Palette.Transparent && rr > 150 && rg < 80 && rb < 80
            && Data.ForgeData.Tiles[0]?.Width == blood[0]?.Width && Data.ForgeData.MapTiles.Count == 3;
        // an override tiles000.art of tile 0 alone: Blood's file replaced whole (tile 1 gone); added instead: tile 1 kept
        string ov = System.IO.Path.Combine(root, "override");
        System.IO.Directory.CreateDirectory(ov);
        var t0 = Shared.Blood.Files.MapArt.Quantize(new byte[16 * 16 * 4].Select((_, i) => (byte)(i % 4 == 3 ? 255 : 200)).ToArray(), 16, 16, pal, 0);
        System.IO.File.WriteAllBytes(System.IO.Path.Combine(ov, "TILES000.ART"), Shared.Blood.Files.MapArt.WriteArt(0, 0, new Dictionary<int, Shared.Blood.Files.Tile> { [0] = t0 }));
        Data.ForgeData.UseMapArt(new List<Shared.Blood.Map.ArtRef> { new("override", true) }, root);
        bool over = Data.ForgeData.Tiles[0] is { Width: 16 } && Data.ForgeData.Tiles[1] is not { Empty: false } && blood[1] is { Empty: false };
        Data.ForgeData.UseMapArt(new List<Shared.Blood.Map.ArtRef> { new("override", false) }, root);
        over &= Data.ForgeData.Tiles[0] is { Width: 16 } && Data.ForgeData.Tiles[1] is { Empty: false };
        Data.ForgeData.UseMapArt(null, "");
        over &= Data.ForgeData.Tiles == blood;
        // the .MAPX keeps the list; a .MAP says so and warns of a picture past Blood's tiles
        var d = MapDocument.New();
        d.MakeSector(new List<Vector2I> { new(0, 0), new(1024, 0), new(1024, 1024), new(0, 1024) });
        d.Walls[0].Picnum = 6200;
        d.Extras.Art.Add(new("classic", false));
        d.Extras.Art.Add(new("override", true));
        var back = Shared.Blood.Map.MapXFile.Read(d.SaveBytes(MapFormat.MapX, false, out _)).extras.Art;
        d.SaveBytes(MapFormat.Map, false, out var dropped);
        bool file = back.SequenceEqual(d.Extras.Art) && dropped.Any(s => s.Contains("art list")) && dropped.Any(s => s.Contains("6144 and up"));
        // paths: relative to the map, rebased when it moves
        string mapA = System.IO.Path.Combine(root, "maps", "a");
        d.RebaseArt(root, mapA);
        bool rebased = d.Extras.Art[0].Path == "../../classic" && Shared.Blood.Files.MapArt.Resolve(mapA, d.Extras.Art[0].Path) == System.IO.Path.GetFullPath(outC);
        bool ok = classic && kept && extended && added && over && file && rebased;
        GD.Print($"selftest map art: packed classic {classic} (tiles {string.Join(",", nums.Values.OrderBy(n => n))}), again the same numbers {kept}, "
            + $"extended {extended} ({string.Join(",", r3.Tiles.Select(t => t.tile))}), added over Blood's {added} (red {rr},{rg},{rb}), override / added {over}, "
            + $".MAPX list and .MAP warnings {file}, rebased {rebased}" + (r1.Error != null ? $" ({r1.Error})" : "") + (ok ? "" : ": FAILED"));
        try
        {
            System.IO.Directory.Delete(root, true);
        }
        catch (System.IO.IOException)
        {
        }
        return ok ? 0 : 1;
    }

    /// <summary>Rooms over rooms (Blood.RoomLinks): a room's floor added above at the same place on a new layer (the
    /// same outline, its floor the room's ceiling, the room's height, joined to nothing, linked: stack markers 11 / 12
    /// with one number at the planes' heights, the planes 504; Forge's link reading finds it); again above: refused (one
    /// link a ceiling); below beside the map (moved east, linked across the offset); two rooms apart made a portal (the
    /// higher floor's above). The map valid.</summary>
    static int RoomLinksCheck()
    {
        static List<Vector2I> Box(int x1, int y1, int x2, int y2) => new() { new(x1, y1), new(x2, y1), new(x2, y2), new(x1, y2) };
        var d = MapDocument.New();
        int room = d.MakeSector(Box(0, 0, 2048, 2048));
        d.Sectors[room].CeilingZ = -16384;
        d.Sectors[room].FloorZ = 0;
        int roomCeiling = d.Sectors[room].CeilingPicnum;
        d.NotifyChanged();
        string w1 = Blood.RoomLinks.AddFloor(d, new List<int> { room }, true, false, -1, 0, 0, out var made);
        d.NotifyChanged();
        int up = made.FirstOrDefault(-1);
        var links = Blood.AutoLayers.Links(d);
        bool Marker(int s, int type, int z) => d.Sprites.Any(sp => sp.SectNum == s && sp.Type == type && sp.Z == z && sp.Xsp != null);
        bool above = w1 == null && up >= 0 && BuildGeom.SectorBounds(d, up) == BuildGeom.SectorBounds(d, room)
            && d.Sectors[up].FloorZ == -16384 && d.Sectors[up].CeilingZ == -32768 && d.LayerOf(up) != d.LayerOf(room)
            && Enumerable.Range(d.Sectors[up].WallPtr, d.Sectors[up].WallNum).All(w => d.Walls[w].NextSector < 0)
            && d.Sectors[up].FloorPicnum == 504 && d.Sectors[room].CeilingPicnum == 504
            && Marker(up, 11, -16384) && Marker(room, 12, -16384) && links.Contains((up, room));
        string w2 = Blood.RoomLinks.AddFloor(d, new List<int> { room }, true, false, -1, 0, 0, out _);
        bool refused = w2 != null;
        // below, beside the map: east of everything, linked across that offset
        int other = d.MakeSector(Box(0, 4096, 1024, 5120));
        d.NotifyChanged();
        // (the same place on its own layer: refused, it would lie on it)
        string w0 = Blood.RoomLinks.AddFloor(d, new List<int> { other }, false, false, d.LayerOf(other), 0, 0, out _);
        bool sameLayer = w0 != null && d.Sprites.All(sp => sp.SectNum != other);
        int otherFloor = d.Sectors[other].FloorPicnum;
        string w3 = Blood.RoomLinks.AddFloor(d, new List<int> { other }, false, true, d.CurrentLayer, 1, 8192, out var made2);
        d.NotifyChanged();
        int low = made2.FirstOrDefault(-1);
        links = Blood.AutoLayers.Links(d);
        var ob = BuildGeom.SectorBounds(d, other);
        var lb = low >= 0 ? BuildGeom.SectorBounds(d, low) : default;
        bool below = w3 == null && low >= 0 && lb.minX > 2048 && lb.minY == ob.minY && lb.maxX - lb.minX == ob.maxX - ob.minX
            && d.Sectors[low].CeilingZ == d.Sectors[other].FloorZ && d.Sectors[low].FloorZ == d.Sectors[other].FloorZ + 8192
            && Marker(other, 9, d.Sectors[other].FloorZ) && Marker(low, 10, d.Sectors[low].CeilingZ) && links.Contains((other, low))
            // (water as Blood's: the surface water picture 2915, the room under it underwater)
            && d.Sectors[other].FloorPicnum == 2915 && d.Sectors[low].CeilingPicnum == 2915 && d.Sectors[low].Xs?.Underwater == 1;
        // a portal between two rooms apart: the higher floor's room above
        int a = d.MakeSector(Box(-8192, 0, -6144, 2048)), b = d.MakeSector(Box(-8192, 8192, -6144, 10240));
        d.Sectors[a].FloorZ = 16384;
        d.Sectors[a].CeilingZ = 0;
        d.Sectors[b].FloorZ = -2048;
        d.Sectors[b].CeilingZ = -18432;
        d.NotifyChanged();
        string w4 = Blood.RoomLinks.MakePortal(d, a, b, 0, out int pu, out int pl);
        d.NotifyChanged();
        bool portal = w4 == null && pu == b && pl == a && Blood.AutoLayers.Links(d).Contains((b, a)) && d.Sectors[b].FloorPicnum == 504 && d.Sectors[a].CeilingPicnum == 504;
        int errors = Errors(d);
        // a linked room deleted: its markers and their partners go, the other room's plane given a picture back
        int sprites = d.Sprites.Count;
        d.DeleteSector(Math.Max(up, low));
        int up2 = up > low ? -1 : up, low2 = low > up ? -1 : low;
        if (up2 >= 0)
            d.DeleteSector(up2);
        if (low2 >= 0)
            d.DeleteSector(low2);
        d.NotifyChanged();
        // (the sectors after a deleted one are a number lower)
        int After(int s0) => s0 - (s0 > up ? 1 : 0) - (s0 > low ? 1 : 0);
        room = After(room);
        other = After(other);
        int[] linkTypes = { 9, 10, 11, 12 };
        bool unlinked = d.Sprites.Count == sprites - 4 && !d.Sprites.Any(sp => (sp.SectNum == room || sp.SectNum == other) && linkTypes.Contains(sp.Type))
            && d.Sectors[room].CeilingPicnum == roomCeiling && d.Sectors[other].FloorPicnum == otherFloor && Blood.AutoLayers.Links(d).Count == 1;
        if (!unlinked)
            GD.Print($"  unlink: sprites {d.Sprites.Count} of {sprites}, room ceiling {d.Sectors[room].CeilingPicnum} (was {roomCeiling}), other floor {d.Sectors[other].FloorPicnum} (was {otherFloor}), links {Blood.AutoLayers.Links(d).Count}, up {up} low {low} room {room} other {other}");
        // water under a room underwater already: no surface between them (E4M4's way: stack markers, 504, both underwater)
        var d2 = MapDocument.New();
        int wet = d2.MakeSector(Box(0, 0, 2048, 2048));
        d2.Sectors[wet].CeilingZ = -16384;
        d2.Sectors[wet].FloorZ = 0;
        d2.EnsureXSector(wet).Underwater = 1;
        d2.NotifyChanged();
        string w5 = Blood.RoomLinks.AddFloor(d2, new List<int> { wet }, false, true, d2.CurrentLayer, 1, 0, out var made3);
        d2.NotifyChanged();
        int deeper = made3.FirstOrDefault(-1);
        bool underUnder = w5 == null && deeper >= 0 && d2.Sectors[wet].FloorPicnum == 504 && d2.Sectors[deeper].CeilingPicnum == 504
            && d2.Sprites.Any(sp => sp.SectNum == wet && sp.Type == 11) && d2.Sprites.Any(sp => sp.SectNum == deeper && sp.Type == 12)
            && d2.Sectors[deeper].Xs?.Underwater == 1 && d2.Sectors[wet].Xs?.Underwater == 1;
        bool ok = above && refused && sameLayer && below && portal && unlinked && underUnder && errors == 0;
        GD.Print($"selftest room links: a floor above at the same place on its own layer, linked {above} ({w1 ?? "ok"}), again refused {refused} ({w2}), "
            + $"the same place on its own layer refused {sameLayer} ({w0}), below beside the map (water), linked across the offset {below} ({w3 ?? "ok"}), "
            + $"two rooms made a portal {portal} ({w4 ?? "ok"}), the copies deleted, their links with them {unlinked}, "
            + $"water under a room underwater linked with no surface between {underUnder} ({w5 ?? "ok"}), {errors} errors" + (ok ? "" : ": FAILED"));
        return ok ? 0 : 1;
    }

    static int GridCheck()
    {
        var v = new View.MapView2D { GridSize = 64, Snap = true, GridAngle = Mathf.Pi / 6, GridOrigin = new Vector2(100, 37) };
        var crossing = v.FromGrid(new Vector2(64 * 3, -64 * 2));
        var snapped = v.SnapToGrid(crossing + v.GridAxisX * 20 - v.GridAxisY * 15);
        bool ok = snapped.DistanceTo(crossing.Round()) < 1.01f && Mathf.Abs(v.GridAxisX.Dot(v.GridAxisY)) < 1e-5f
            && v.ToGrid(crossing).DistanceTo(new Vector2(192, -128)) < 0.01f;
        var plain = new View.MapView2D { GridSize = 64, Snap = true };
        ok &= plain.SnapToGrid(new Vector2(100, -37)) == new Vector2(128, -64);
        v.Free();
        plain.Free();
        GD.Print($"selftest grid: a grid turned 30° and moved: {crossing.Round()} snapped from near it to {snapped}" + (ok ? "" : ": FAILED"));
        return ok ? 0 : 1;
    }

    /// <summary>What types need (Blood.Requirements, the dialogs' "What it needs", Map Analysis): a sector made a
    /// Rotate sector with no markers needs its axis and Fix makes it; a switch with no TX needs one; and over Blood's
    /// own levels (which work) the needs missing, by kind: few, or a rule is wrong.</summary>
    static int NeedsCheck()
    {
        int fails = 0;
        if (FindMap("E1M1") is { } p1)
        {
            var d = MapDocument.Open(p1);
            // a plain sector (no x-sector, no markers) made a Rotate sector
            int s = Enumerable.Range(0, d.Sectors.Count).First(i => d.Sectors[i].Type == 0 && d.Sectors[i].Xs == null);
            d.Sectors[s].Type = 617;
            var before = Blood.Requirements.Sector(d, s);
            bool axisMissing = before.Any(n => !n.Met && n.Text.StartsWith("an axis marker", StringComparison.Ordinal) && n.Fix != null);
            foreach (var n in before.Where(n => !n.Met && n.Fix != null))
                n.Fix(d);
            var after = Blood.Requirements.Sector(d, s);
            bool axisMet = after.Any(n => n.Met && n.Text.StartsWith("an axis marker", StringComparison.Ordinal));
            bool triggerMissing = after.Any(n => !n.Met && n.Text.StartsWith("something to set it off", StringComparison.Ordinal));
            // (the axis Fix made stands at angle 0: it turns nothing; its Fix turns it a quarter)
            bool angleMissing = after.Any(n => !n.Met && n.Text.StartsWith("an angle on its axis marker", StringComparison.Ordinal) && n.Fix != null);
            foreach (var n in after.Where(n => !n.Met && n.Text.StartsWith("an angle", StringComparison.Ordinal)))
                n.Fix(d);
            bool angleMet = Blood.Requirements.Sector(d, s).Any(n => n.Met && n.Text.StartsWith("an angle on its axis marker", StringComparison.Ordinal));
            bool ok = axisMissing && axisMet && triggerMissing && angleMissing && angleMet;
            GD.Print($"selftest needs: sector {s} as Rotate: axis missing {axisMissing}, made by Fix {axisMet}, still needs a trigger {triggerMissing}, its axis an angle {angleMissing} (set by Fix {angleMet})"
                + (ok ? "" : ": FAILED"));
            if (!ok)
                fails++;
        }
        var counts = new Dictionary<string, int>();
        int maps = 0;
        foreach (var path in Maps(Array.Empty<string>()).Where(f => !f.Contains("addons", StringComparison.OrdinalIgnoreCase)))
        {
            MapDocument d;
            try
            {
                d = MapDocument.Open(path);
            }
            catch (Exception)
            {
                continue;
            }
            maps++;
            foreach (var (kind, index, need) in Blood.Requirements.Unmet(d).Where(u => !u.need.Advice))
            {
                string key = (kind == View.ElementKind.Sprite ? $"sprite type {d.Sprites[index].Type}" : kind == View.ElementKind.Sector
                    ? $"sector type {d.Sectors[index].Type}" : "wall") + ": " + System.Text.RegularExpressions.Regex.Replace(need.Text, @"\d+", "N");
                counts[key] = counts.GetValueOrDefault(key) + 1;
            }
        }
        GD.Print($"selftest needs: over {maps} of Blood's levels, missing (not advice): {counts.Values.Sum()}");
        foreach (var (k, n) in counts.OrderByDescending(kv => kv.Value).Take(8))
            GD.Print($"selftest needs:   {n} x {k}");
        return fails == 0 ? 0 : 1;
    }

    /// <summary>The x-fields' descriptions (Blood.XFields, the dialogs' x-tabs): every int field of each x-structure
    /// described once (none left out of the dialog), labels unique in a tab (the dialog driver finds fields by them),
    /// ranges from Blood's bit widths (Command 0..255, a busy time 0..4095, a light amplitude -128..127).</summary>
    static int FieldCheck()
    {
        int fails = 0;
        foreach (var (t, specs) in new[] { (typeof(XSector), Blood.XFields.Sector), (typeof(XWall), Blood.XFields.Wall), (typeof(XSprite), Blood.XFields.Sprite) })
        {
            var fields = t.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
                .Where(f => f.FieldType == typeof(int) && f.Name != "Reference").Select(f => f.Name).OrderBy(n => n).ToList();
            var described = specs.Select(sp => sp.Field).OrderBy(n => n).ToList();
            var labels = specs.Where(sp => sp.Kind != Blood.FieldKind.Bits).Select(sp => sp.Label)
                .Concat(specs.Where(sp => sp.Kind == Blood.FieldKind.Bits).SelectMany(sp => sp.Choices.Select(c => c.name))).ToList();
            var twice = labels.GroupBy(l => l).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            bool ok = fields.SequenceEqual(described) && twice.Count == 0;
            GD.Print($"selftest fields: {t.Name}: {specs.Length} described" + (ok ? "" : $": FAILED (missing {string.Join(",", fields.Except(described))}, "
                + $"twice {string.Join(",", described.GroupBy(x => x).Where(g => g.Count() > 1).Select(g => g.Key))}, labels twice {string.Join(",", twice)})"));
            if (!ok)
                fails++;
        }
        string R(Type t, string f) => Blood.XFields.Range(t, f) is var (lo, hi) ? $"{lo}..{hi}" : "";
        string ranges = $"Command {R(typeof(XSector), "Command")}, BusyTimeA {R(typeof(XSector), "BusyTimeA")}, Amplitude {R(typeof(XSector), "Amplitude")}, "
            + $"Data1 {R(typeof(XSprite), "Data1")}, Data4 {R(typeof(XSprite), "Data4")}, RxID {R(typeof(XWall), "RxID")}";
        bool rok = ranges == "Command 0..255, BusyTimeA 0..4095, Amplitude -128..127, Data1 -32768..32767, Data4 0..65535, RxID 0..1023";
        // an ambient sound's data: radius, radius, sound, volume
        string amb = string.Join(" ", Enumerable.Range(1, 4).Select(n => Blood.XFields.SpriteData(710, n)).Select(sp => $"{sp.Caption}:{sp.Kind}"));
        rok &= amb == "Radius1:Number Radius2:Number Sound:Sound Volume:Number" && Blood.XFields.SpriteData(710, 4).Describe(127) == "100%";
        GD.Print($"selftest fields: ranges {ranges}; ambient {amb}" + (rok ? "" : ": FAILED"));
        return fails + (rok ? 0 : 1) == 0 ? 0 : 1;
    }

    /// <summary>The sounds (Tools > Sounds, a sound field's Pick...): SOUNDS.RFF read (sound 103 a sliding door's
    /// doorslid, its samples made Godot's), the sound fields of a Sector SFX, a switch, an ambient sound, and Blood's
    /// levels' uses read (sound 103 used by Sector SFX).</summary>
    static int SoundCheck()
    {
        if (Data.ForgeData.Folder.Length == 0 || Blood.SoundLibrary.All.Count == 0)
        {
            GD.Print("selftest sounds: no Blood folder, skipped");
            return 0;
        }
        var s = Blood.SoundLibrary.Get(103);
        var st = Blood.SoundLibrary.Stream(103);
        string fields = string.Join(" ", new[] { 709, 20, 710, 417, 0 }.Select(t => $"{t}:{string.Join(",", Blood.SoundLibrary.Fields(t))}"));
        var sw = System.Diagnostics.Stopwatch.StartNew();
        IReadOnlyDictionary<int, Blood.SoundLibrary.Use> uses = null;
        while ((uses = Blood.SoundLibrary.LevelUses) == null && sw.Elapsed.TotalSeconds < 60)
            System.Threading.Thread.Sleep(50);
        string use = uses != null && uses.TryGetValue(103, out var u) ? u.ToString() : "-";
        bool ok = s?.Name == "doorslid" && st != null && st.Data.Length > 1000 && fields == "709:1,2,3,4 20:1,2 710:3 417:4 0:"
            && use.Contains("Sector SFX");
        GD.Print($"selftest sounds: {Blood.SoundLibrary.All.Count} sounds, 103 {Blood.SoundLibrary.Describe(103)}, fields {fields}, "
            + $"levels read in {sw.Elapsed.TotalSeconds:0.0} s ({uses?.Count ?? 0} sounds used), 103 used for {use}" + (ok ? "" : ": FAILED"));
        return ok ? 0 : 1;
    }

    /// <summary>The sector captions (View > Show Sector Effects) of E1M1's lit, triggered rooms as xmapedit tells them.</summary>
    static int CaptionCheck()
    {
        if (FindMap("E1M1") is not { } path)
            return 0;
        var d = MapDocument.Open(path);
        int fails = 0, shown = 0;
        for (int s = 0; s < d.Sectors.Count; s++)
            if (Blood.SectorCaption.Of(d, s) != null)
                shown++;
        foreach (var (s, head, fx) in new[]
        {
            (88, "#88  tx 2", "light flicker 2 +4 always, on enter once"),
            (66, "#66  rx 124", "light steady -42"),
        })
        {
            var c = Blood.SectorCaption.Of(d, s);
            if (c is not { } got || got.head != head || got.effects != fx)
            {
                GD.Print($"selftest caption: sector {s}: \"{c?.head}\" / \"{c?.effects}\", expected \"{head}\" / \"{fx}\": FAILED");
                fails++;
            }
        }
        GD.Print($"selftest caption: E1M1: {shown} of {d.Sectors.Count} sectors captioned" + (fails == 0 ? "" : ": FAILED"));
        // the link chain of E1M1's sector 18 (entered: channel 156 to two switches, each sending on to its sound)
        var all = Blood.MapLinks.All(d);
        var chain = Blood.MapLinks.Focus(all, new HashSet<View.Element> { new(View.ElementKind.Sector, 18) }, true)
            .Where(p => p.Key.Kind == View.LinkKind.Channel).ToList();
        string Sp(View.Element e) => e.Kind == View.ElementKind.Sprite ? $"s{e.Index}" : $"{e.Kind}{e.Index}";
        var chainText = string.Join(" ", chain.OrderBy(p => p.Value).ThenBy(p => p.Key.Label)
            .Select(p => $"{Sp(p.Key.From)}>{Sp(p.Key.To)}@{p.Key.Label}:{p.Value}"));
        const string want = "Sector18>s507@156:1 Sector18>s508@156:1 s508>s506@158:2 s507>s505@159:2";
        bool chainOk = chainText == want;
        GD.Print($"selftest chain: E1M1 sector 18: {chainText}" + (chainOk ? "" : $", expected {want}: FAILED"));
        if (!chainOk)
            fails++;
        return fails == 0 ? 0 : 1;
    }

    static int AlignCheck()
    {
        if ((Data.ForgeData.Tiles?.Count ?? 0) == 0 && !Data.ForgeData.Use(Shared.Blood.Files.BloodInstall.Find() ?? ""))
        {
            GD.Print("selftest align: no Blood folder, skipped");
            return 0;
        }
        int xPairs = 0, xSame = 0, yPairs = 0, ySame = 0;
        foreach (var name in new[] { "E1M1", "E1M2", "E1M3", "E2M1", "E3M1" })
        {
            if (FindMap(name) is not { } path)
                continue;
            var d = MapDocument.Open(path);
            for (int w = 0; w < d.Walls.Count; w++)
            {
                var a = d.Walls[w];
                int n = a.Point2;
                if (a.NextSector >= 0 || n < 0 || n >= d.Walls.Count)
                    continue;
                var b = d.Walls[n];
                if (b.NextSector >= 0 || b.Picnum != a.Picnum || ((a.Cstat ^ b.Cstat) & (8 | 256 | 4)) != 0 || a.YRepeat != b.YRepeat)
                    continue;
                // (only the next wall changes: its fields kept and put back)
                var (xp, yp, xr0, yr0, cs) = (b.XPanning, b.YPanning, b.XRepeat, b.YRepeat, b.Cstat);
                var start = new View.VisualTarget(View.VisualPart.Middle, a.Sector, w);
                Tools.TextureOps.AutoAlign(d, start, true, true, new HashSet<int> { w, n });
                if (b.XRepeat == xr0)
                {
                    xPairs++;
                    if (b.XPanning == xp)
                        xSame++;
                }
                yPairs++;
                if (b.YPanning == yp)
                    ySame++;
                (b.XPanning, b.YPanning, b.XRepeat, b.YRepeat, b.Cstat) = (xp, yp, xr0, yr0, cs);
            }
        }
        double xr = xPairs == 0 ? 1 : (double)xSame / xPairs, yr = yPairs == 0 ? 1 : (double)ySame / yPairs;
        bool ok = xr > 0.25 && yr > 0.25;
        GD.Print($"selftest align: next walls aligned as the maps have them: x {xSame} of {xPairs} ({xr:P0}), y {ySame} of {yPairs} ({yr:P0})"
            + (ok ? "" : ": FAILED"));
        return ok ? 0 : 1;
    }

    static int TxOf(MapDocument d, View.Element e) => e.Kind switch
    {
        View.ElementKind.Sector => d.Sectors[e.Index].Xs?.TxID ?? -1,
        View.ElementKind.Line => d.Walls[d.Lines[e.Index].Front].Xw?.TxID ?? (d.Lines[e.Index].Back >= 0 ? d.Walls[d.Lines[e.Index].Back].Xw?.TxID ?? -1 : -1),
        View.ElementKind.Sprite => d.Sprites[e.Index].Xsp?.TxID ?? -1,
        _ => -1,
    };

    static int RxOf(MapDocument d, View.Element e) => e.Kind switch
    {
        View.ElementKind.Sector => d.Sectors[e.Index].Xs?.RxID ?? -1,
        View.ElementKind.Line => d.Walls[d.Lines[e.Index].Front].Xw?.RxID ?? (d.Lines[e.Index].Back >= 0 ? d.Walls[d.Lines[e.Index].Back].Xw?.RxID ?? -1 : -1),
        View.ElementKind.Sprite => d.Sprites[e.Index].Xsp?.RxID ?? -1,
        _ => -1,
    };

    static int LayerCheck()
    {
        int failures = 0;
        foreach (var name in new[] { "E1M1", "E2M1", "DM1", "DM2" })
        {
            string path = FindMap(name);
            if (path == null)
                continue;
            var doc = MapDocument.Open(path);
            var clock = System.Diagnostics.Stopwatch.StartNew();
            Blood.AutoLayers.Make(doc);
            long ms = clock.ElapsedMilliseconds;
            int clash = 0;
            for (int a = 0; a < doc.Sectors.Count; a++)
                for (int b = a + 1; b < doc.Sectors.Count; b++)
                    if (doc.LayerOf(a) == doc.LayerOf(b) && Blood.AutoLayers.Overlap(doc, a, b))
                        clash++;
            var (_, x) = MapXFile.Read(doc.SaveBytes(MapFormat.MapX, false, out _));
            // (one layer: no LAYR chunk)
            bool kept = doc.Layers.Count == 1 ? x.Layers.Count == 0
                : x.Layers.Select(l => l.Name).SequenceEqual(doc.Layers.Select(l => l.Name))
                  && x.SectorLayers.SequenceEqual(Enumerable.Range(0, doc.Sectors.Count).Select(doc.LayerOf));
            bool ok = clash == 0 && kept;
            GD.Print($"selftest layers {name}: {Blood.AutoLayers.Summary(doc)} ({ms} ms); {clash} sharing a place in one layer; "
                + $".MAPX keeps them: {kept}" + (ok ? "" : ": FAILED"));
            if (!ok)
                failures++;
        }
        return failures;
    }

    /// <summary>A convex sector of one loop and 4 to 12 walls.</summary>
    static bool Convex(MapDocument d, int s)
    {
        var loops = BuildGeom.Loops(d, s);
        if (loops.Count != 1 || loops[0].Count < 4 || loops[0].Count > 12)
            return false;
        var l = loops[0];
        for (int i = 0; i < l.Count; i++)
        {
            var (ax, ay, bx, by) = BuildGeom.WallEnds(d, l[i]);
            var (_, _, cx, cy) = BuildGeom.WallEnds(d, l[(i + 1) % l.Count]);
            long cross = (long)(bx - ax) * (cy - by) - (long)(by - ay) * (cx - bx);
            if (cross <= 0)
                return false;
        }
        return true;
    }

    /// <summary>Does the room lie inside sector s, clear of all walls (none crosses or touches it)?</summary>
    static bool RoomFits(MapDocument d, int s, List<Vector2I> rect)
    {
        if (!rect.All(p => BuildGeom.Inside(d, s, p.X, p.Y)))
            return false;
        int x0 = rect[0].X - 1, y0 = rect[0].Y - 1, x1 = rect[2].X + 1, y1 = rect[2].Y + 1;
        for (int w = 0; w < d.Walls.Count; w++)
        {
            var (ax, ay, bx, by) = BuildGeom.WallEnds(d, w);
            if (Math.Max(ax, bx) < x0 || Math.Min(ax, bx) > x1 || Math.Max(ay, by) < y0 || Math.Min(ay, by) > y1)
                continue;
            return false;
        }
        // nothing else shares the place (a room of another sector stacked there)
        return d.SectorsAt(rect[0].X, rect[0].Y).Count == 1;
    }
}
