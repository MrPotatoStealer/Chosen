// Chosen core: original code.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Chosen.Forge.Document;
using Godot;

namespace Chosen.Forge;

/// <summary>Forge's command-line checks, run headless (forge\run.bat --roundtrip / --selftest).</summary>
public static partial class HeadlessTests
{
    /// <summary>The repository's maps (game/blood3d/maps, the add-ons'), or the files given.</summary>
    static List<string> Maps(IEnumerable<string> given)
    {
        var l = given.Where(File.Exists).ToList();
        if (l.Count > 0)
            return l;
        string root = Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), ".."));
        string data = Path.Combine(root, "game", "blood3d");
        if (Directory.Exists(Path.Combine(data, "maps")))
            l.AddRange(Directory.GetFiles(Path.Combine(data, "maps"), "*.map").OrderBy(f => f, StringComparer.OrdinalIgnoreCase));
        if (Directory.Exists(Path.Combine(data, "addons")))
            foreach (var a in Directory.GetDirectories(Path.Combine(data, "addons")).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                if (Directory.Exists(Path.Combine(a, "maps")))
                    l.AddRange(Directory.GetFiles(Path.Combine(a, "maps"), "*.map").OrderBy(f => f, StringComparer.OrdinalIgnoreCase));
        return l;
    }

    /// <summary>--roundtrip &lt;out dir&gt; [maps]: each map opened through MapDocument and saved as .MAP into out dir
    /// (unchanged: no revision bump), compared with the original; and through .MAPX and back. 0 when all are
    /// identical.</summary>
    public static int RoundTrip(string[] args)
    {
        if (args.Length == 0)
        {
            GD.PrintErr("--roundtrip <out dir> [maps...]");
            return 2;
        }
        string outDir = args[0];
        Directory.CreateDirectory(outDir);
        var maps = Maps(args.Skip(1));
        int ok = 0, bad = 0;
        foreach (var f in maps)
        {
            string why = null;
            try
            {
                var orig = File.ReadAllBytes(f);
                var doc = MapDocument.Open(f);
                var asMap = doc.SaveBytes(MapFormat.Map, false, out _);
                File.WriteAllBytes(Path.Combine(outDir, Path.GetFileName(f)), asMap);
                if (!asMap.AsSpan().SequenceEqual(orig))
                    why = "saved .MAP differs";
                else
                {
                    string x = Path.Combine(outDir, Path.GetFileNameWithoutExtension(f) + ".mapx");
                    File.WriteAllBytes(x, doc.SaveBytes(MapFormat.MapX, false, out _));
                    var back = MapDocument.Open(x).SaveBytes(MapFormat.Map, false, out _);
                    if (!back.AsSpan().SequenceEqual(orig))
                        why = "through .MAPX differs";
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
                GD.Print($"{f}: {why}");
            }
        }
        GD.Print($"forge roundtrip: {ok} identical, {bad} different, of {maps.Count} (saved in {outDir})");
        return bad == 0 && maps.Count > 0 ? 0 : 1;
    }
}
