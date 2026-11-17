// Chosen core: original code. The selftest's dynamic lights (Document/Lights.cs, Chosen.Blood's MapLights.cs).
using System;
using System.IO;
using System.Linq;
using Chosen.Forge.Document;
using Chosen.Shared.Blood.Map;
using Chosen.Shared.Engine.Geometry;
using Godot;

namespace Chosen.Forge;

public static partial class HeadlessTests
{
    /// <summary>Dynamic lights: E1M1's made from its sprites by the game's rules (a torch, a fixture, none twice); kept in a
    /// .MAPX's LITE chunk field for field, the game's reader giving them (none: null, the game's own; an emptied list:
    /// empty); following their sprites when sprites before them go, gone with theirs; an attached light above its sprite,
    /// moved off its spot and following it; "lightset" letting one go where it shone, its height over the floor; a .MAP
    /// saved says it drops them.</summary>
    static int LightCheck()
    {
        int failures = 0;
        void Check(bool ok, string what)
        {
            GD.Print($"selftest lights: {what}: {(ok ? "ok" : "FAILED")}");
            if (!ok)
                failures++;
        }
        string path = FindMap("E1M1");
        if (path == null)
            return 0;
        var d = MapDocument.Open(path);
        int expected = LightRules.Generate(d.Map).Count;
        int torches = d.Sprites.Count(s => s.Type is 30 or 32);
        bool noneYet = !d.HasLights && d.Lights.Count == 0;
        int made = d.GenerateLights();
        int again = d.GenerateLights();
        Check(noneYet && made == expected && made > 0 && again == 0 && d.HasLights
            && d.Lights.Count(l => l.Kind == LightKind.Torch) == torches && d.Lights.Any(l => l.Kind == LightKind.Fixture),
            $"E1M1's from its sprites: {made} ({torches} flames, {d.Lights.Count(l => l.Kind == LightKind.Fixture)} fixtures, "
            + $"{d.Lights.Count(l => l.Kind == LightKind.Glow)} glows), none twice ({again})");

        // a point light of its own, every field set, through the .MAPX and back
        var p = new MapLight { Kind = LightKind.Point, X = 100, Y = -200, Z = -3000, R = 10, G = 20, B = 30, Radius = 4321, Energy = 1.25f,
            Flicker = 0.5f, Up = -0.25f, Tile = 77, Shadows = false, SectorLight = true };
        int pi = d.AddLight(p);
        var (_, x) = MapXFile.Read(d.Snapshot());
        var q = x.Lights[pi];
        Check(x.HasLights && x.Lights.Count == d.Lights.Count && q.Kind == p.Kind && q.Sprite == -1 && (q.X, q.Y, q.Z) == (p.X, p.Y, p.Z)
            && (q.R, q.G, q.B) == (p.R, p.G, p.B) && q.Radius == p.Radius && q.Energy == p.Energy && q.Flicker == p.Flicker && q.Up == p.Up
            && q.Tile == p.Tile && q.Shadows == p.Shadows && q.SectorLight == p.SectorLight
            && x.Lights.Zip(d.Lights).All(z => z.First.Sprite == z.Second.Sprite && z.First.Kind == z.Second.Kind),
            $"the .MAPX's LITE chunk keeps {x.Lights.Count} lights field for field");

        // the game's reader: a .MAPX with its own lights gives them; without (never made): null, the game makes its own;
        // all deleted: empty (none at all)
        string tmp = Path.Combine(Path.GetTempPath(), "forge-selftest-lights.mapx");
        File.WriteAllBytes(tmp, d.SaveBytes(MapFormat.MapX, false, out _));
        int read = BloodMapFile.Read(tmp).Lights?.Count ?? -1;
        var plain = MapDocument.Open(path);
        File.WriteAllBytes(tmp, plain.SaveBytes(MapFormat.MapX, false, out _));
        bool without = BloodMapFile.Read(tmp).Lights == null;
        var emptied = MapDocument.Open(path);
        emptied.GenerateLights();
        emptied.DeleteLights(Enumerable.Range(0, emptied.Lights.Count));
        File.WriteAllBytes(tmp, emptied.SaveBytes(MapFormat.MapX, false, out _));
        var none = BloodMapFile.Read(tmp).Lights;
        File.Delete(tmp);
        Check(read == d.Lights.Count && without && none is { Count: 0 },
            $"the game reads a .MAPX's own ({read}), none of its own as null ({without}), all deleted as none ({none?.Count.ToString() ?? "null"})");

        // sprites deleted: the lights after them follow, a light on a deleted sprite goes with it
        int li = d.Lights.FindIndex(l => l.Attached && l.Sprite > 5);
        int sprite = d.Lights[li].Sprite;
        int before = d.Lights.Count;
        d.DeleteSprites(new[] { 0 });
        bool shifted = d.Lights[li].Sprite == sprite - 1;
        d.DeleteSprites(new[] { sprite - 1 });
        Check(shifted && d.Lights.Count == before - 1 && d.Lights.All(l => !l.Attached || l.Sprite < d.Sprites.Count),
            $"sprites deleted: light {li} followed its sprite {sprite} to {sprite - 1} ({shifted}), went with it ({d.Lights.Count} of {before})");

        // an attached flame: above its sprite; moved off its spot it follows the sprite
        int ti = d.Lights.FindIndex(l => l.Kind == LightKind.Torch && l.Attached);
        var t = d.Lights[ti];
        var tsp = d.Sprites[t.Sprite];
        var at = d.LightPlace(ti).Value;
        d.MoveLight(ti, (int)at.x + 64, (int)at.y);
        var moved = d.LightPlace(ti).Value;
        tsp.X += 512;
        var followed = d.LightPlace(ti).Value;
        tsp.X -= 512;
        Check(at.z < tsp.Z && t.X == 64 && Math.Abs(moved.x - (at.x + 64)) < 1 && Math.Abs(followed.x - (moved.x + 512)) < 1,
            $"a flame on sprite {t.Sprite}: {tsp.Z - at.z:0} above it, dragged 64 along x (offset {t.X}), following its sprite moved 512");

        // lightset: let go, a point light where it shone; its height above the floor where it is
        string why = Blood.LightDialog.Set(d, $"lightset {ti} sprite=-1");
        var free = d.Lights[ti];
        bool stayed = !free.Attached && Math.Abs(free.X - moved.x) < 1 && Math.Abs(free.Z - moved.z) < 1;
        string why2 = Blood.LightDialog.Set(d, $"lightset {ti} height=1024 color=1,2,3 shadows=0");
        int s = d.LightSector(ti);
        double floor = 0;
        if (s >= 0)
            BuildGeom.GetZsOfSlope(d, s, free.X, free.Y, out _, out floor);
        Check(why == null && why2 == null && stayed && s >= 0 && Math.Abs(floor - free.Z - 1024) < 1 && (free.R, free.G, free.B) == (1, 2, 3) && !free.Shadows,
            $"lightset: let go where it shone ({stayed}), 1024 above the floor of sector {s} (z {free.Z}, floor {floor:0}), its colour and shadows");

        // copied and pasted: a sprite's attached light comes with it (on the new sprite, its offset kept); a point light
        // copied alone lands as high over the floor there; a sector's own point light comes with the sector
        int ai = d.Lights.FindIndex(l => l.Attached && l.Sprite >= 0);
        var al = d.Lights[ai];
        int asp = al.Sprite;
        var aat = d.LightPlace(ai).Value;
        var clip = Blood.MapPaste.Copy(d, Array.Empty<int>(), new[] { asp });
        int lc0 = d.Lights.Count, sc0 = d.Sprites.Count;
        var (_, newSprites, pwhy) = Blood.MapPaste.Paste(d, clip, 256, 0, out var newLights);
        var nl = newLights.Count == 1 ? d.Lights[newLights[0]] : null;
        var nat = nl != null ? d.LightPlace(newLights[0]) : null;
        bool withSprite = pwhy == null && newSprites.Count == 1 && d.Sprites.Count == sc0 + 1 && d.Lights.Count == lc0 + 1 && nl != null
            && nl.Sprite == newSprites[0] && (nl.X, nl.Y, nl.Z, nl.Kind) == (al.X, al.Y, al.Z, al.Kind)
            && nat is { } na && Math.Abs(na.x - aat.x - 256) < 1 && Math.Abs(na.y - aat.y) < 1;
        int h0 = Blood.LightDialog.Height(d, ti);
        var pclip = Blood.MapPaste.Copy(d, Array.Empty<int>(), Array.Empty<int>(), new[] { ti });
        var (_, _, pwhy2) = Blood.MapPaste.Paste(d, pclip, 128, 0, out var pl2);
        bool alone = pwhy2 == null && pclip.Lights.Count == 1 && pl2.Count == 1 && !d.Lights[pl2[0]].Attached
            && Math.Abs(Blood.LightDialog.Height(d, pl2[0]) - h0) <= 1 && d.Lights[pl2[0]].X == d.Lights[ti].X + 128;
        bool bySector = s >= 0 && Blood.MapPaste.Copy(d, new[] { s }, Array.Empty<int>()) is { } sclip
            && sclip.Lights.Any(c => !c.Light.Attached && c.Light.X == d.Lights[ti].X && c.Light.Y == d.Lights[ti].Y);
        Check(withSprite && alone && bySector,
            $"copy and paste: sprite {asp}'s light came with it {withSprite} ({pwhy}), a point light alone {alone} ({pwhy2}; {h0} over the floor), "
            + $"with its sector {bySector}");

        // rooms of two layers drawn over each other at the same heights: a point light there is the edited layer's
        // (clicked in 2D and 3D), whichever room was made first; the other layer edited, that one's
        {
            var o = MapDocument.New();
            var rect = new System.Collections.Generic.List<Godot.Vector2I> { new(0, 0), new(2048, 0), new(2048, 2048), new(0, 2048) };
            int other = o.MakeSector(rect);
            int layerB = o.AddLayer("B");
            int mine = o.MakeSector(rect, out string owhy, over: true);
            if (mine >= 0)
            {
                o.SetLayer(other, layerB);
                o.SetLayer(mine, 0);
                o.CurrentLayer = 0;
                o.NotifyChanged();
            }
            int ol = mine >= 0 ? o.AddLight(new MapLight { X = 1024, Y = 1024, Z = -4096 }) : -1;
            int inMine = ol >= 0 ? o.LightSector(ol) : -2;
            bool editable = ol >= 0 && o.LightEditable(ol);
            // placed (settled) there: it keeps that room whichever layer is edited, and the file keeps it for the game
            o.SettleLight(ol);
            o.CurrentLayer = layerB;
            int stays = ol >= 0 ? o.LightSector(ol) : -2;
            bool lockedInB = ol >= 0 && !o.LightEditable(ol);
            var (_, ox) = MapXFile.Read(MapXFile.Write(o.Map, o.Extras));
            bool saved = ox.Lights.Count == 1 && ox.Lights[0].Sector == mine;
            Check(mine >= 0 && o.SectorsAt(1024, 1024).Count == 2 && inMine == mine && editable && stays == mine && lockedInB && saved,
                $"two layers' rooms over each other ({other} in layer B, {mine} in layer 0 {owhy}): the light in {inMine} while layer 0 is edited "
                + $"(editable {editable}); placed there, still in {stays} while B is (locked {lockedInB}), saved with its room {saved}");

            // moved to layer B (Move to Layer in Lights mode): into B's room there, edited with B, saved so
            o.CurrentLayer = 0;
            var (movedB, _, leftB) = ol >= 0 ? o.MoveLightsToLayer(new[] { ol }, layerB) : (0, 0, 1);
            int inB = ol >= 0 ? o.LightSector(ol) : -2;
            bool lockedIn0 = ol >= 0 && !o.LightEditable(ol);
            o.CurrentLayer = layerB;
            bool editableInB = ol >= 0 && o.LightEditable(ol);
            var (_, ox2) = MapXFile.Read(MapXFile.Write(o.Map, o.Extras));
            bool savedB = ox2.Lights.Count == 1 && ox2.Lights[0].Sector == other;
            // a layer with no room there: left where it was
            int layerC = o.AddLayer("C");
            var (moved2, _, left2) = ol >= 0 ? o.MoveLightsToLayer(new[] { ol }, layerC) : (1, 0, 0);
            Check(movedB == 1 && leftB == 0 && inB == other && lockedIn0 && editableInB && savedB && moved2 == 0 && left2 == 1 && o.LightSector(ol) == other,
                $"a light moved to layer B: moved {movedB} (left {leftB}), in {inB} (B's room {other}), locked while layer 0 is edited {lockedIn0}, "
                + $"edited with B {editableInB}, saved with B's room {savedB}; to an empty layer: moved {moved2}, left {left2}, still in {o.LightSector(ol)}");
        }

        // a room of another layer laid over a corner of one only (no middle of either's fill triangles inside the
        // other): still sharing its place, so neither's lights reach the other (Forge report 20261108-150121: test.mapx's
        // corridor 14 over room 0's corner, lit by room 0's light)
        {
            var o = MapDocument.New();
            int a = o.MakeSector(new System.Collections.Generic.List<Godot.Vector2I> { new(0, 0), new(2048, 0), new(2048, 2048), new(0, 2048) });
            int layerB = o.AddLayer("B");
            o.CurrentLayer = layerB;
            int b = o.MakeSector(new System.Collections.Generic.List<Godot.Vector2I> { new(1792, 1024), new(8000, 1024), new(8000, 4000), new(1024, 4000) },
                out string bwhy, over: true);
            var (over, _) = b >= 0 ? Blood.AutoLayers.Rooms(o) : (null, null);
            var colours = over == null ? null : LightRules.RoomColours(o.Sectors.Count, s => over[s], s => System.Array.Empty<int>(), 16);
            bool shares = over != null && over[a].Contains(b) && over[b].Contains(a);
            bool apart = colours != null && colours[a] > 0 && colours[b] > 0 && colours[a] != colours[b];
            Check(shares && apart, $"a room over another's corner ({b} over {a} {bwhy}): sharing its place {shares}, told apart {apart}");
        }

        // a .MAP keeps none: saying so
        d.SaveBytes(MapFormat.Map, false, out var dropped);
        Check(dropped.Any(n => n.Contains("dynamic light")), "saved as .MAP: the lights said to be dropped");
        return failures;
    }
}
