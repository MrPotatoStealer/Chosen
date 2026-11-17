// Chosen core: original code. The map's own art (Map Options > Art): ART files or folders of them over Blood's tiles,
// pictures packed into ART files.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Chosen.Forge.Data;
using Chosen.Shared.Blood.Files;
using Chosen.Shared.Blood.Map;
using Godot;

namespace Chosen.Forge;

public partial class MainWindow
{
    /// <summary>The map's art sets as ForgeData shows them: composed again when they differ from the last (an art
    /// command, undo, another map); force: read again (packed anew). The views drawn again when the tiles changed.</summary>
    string _modSaid = "";

    public void SyncMapArt(bool force = false)
    {
        if (Doc == null || !ForgeData.UseMapArt(Doc.Extras.Art, Doc.Folder, force))
            return;
        _preview.ClearMaterials();
        _sprites.Refresh();
        View.MapChanged();
        _preview.MapChanged();
        View.QueueRedraw();
        if (ForgeData.MapArtProblems.Count > 0)
            DisplayStatus("map art: " + string.Join("; ", ForgeData.MapArtProblems));
        else if (ForgeData.ModFolder != "" && ForgeData.ModFolder != _modSaid)
            DisplayStatus($"{Path.GetFileName(Doc.Path)}: with the art of its folder, {Path.GetFileName(ForgeData.ModFolder)} ({ForgeData.ModTiles.Count} tiles of its own)");
        _modSaid = ForgeData.ModFolder;
    }

    /// <summary>
    /// "mapart ..." (Map Options > Art; recorded, one undo step each):
    /// <c>add &lt;add|override&gt; &lt;path&gt;</c> an ART file or a folder of them (as typed: absolute, or from the map's
    /// folder), <c>remove &lt;i&gt;</c>, <c>kind &lt;i&gt; &lt;add|override&gt;</c>, <c>move &lt;i&gt; &lt;-1|1&gt;</c> (the
    /// order they go over Blood's: later over earlier), <c>pack &lt;classic|extended&gt;|&lt;pictures folder&gt;|&lt;out
    /// folder&gt;</c> (Blood.ArtPacker; the out folder added as a set when it isn't one), <c>reload</c> (the files read
    /// again).
    /// </summary>
    void MapArtCommand(string args)
    {
        var art = Doc.Extras.Art;
        string verb = args.Split(' ', 2)[0], rest = args.Length > verb.Length ? args[(verb.Length + 1)..].Trim() : "";
        int Index(string s) => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i) && i >= 0 && i < art.Count ? i : -1;
        void Changed(string what)
        {
            Doc.NotifyChanged();
            UndoChanged();
            SyncMapArt(true);
            if (ForgeData.MapArtProblems.Count == 0)
                DisplayStatus($"map art: {what} ({ForgeData.MapTiles.Count} tiles of the map's own)");
        }
        switch (verb)
        {
            case "add":
            {
                var p = rest.Split(' ', 2);
                if (p.Length < 2 || p[0] is not ("add" or "override") || p[1].Trim() == "")
                {
                    DisplayStatus("mapart add <add|override> <ART file or folder>");
                    return;
                }
                string full = MapArt.Resolve(Doc.Folder, p[1].Trim());
                if (MapArt.Files(full).Count == 0)
                {
                    DisplayStatus($"map art: no ART file at {full}");
                    return;
                }
                string rel = MapArt.Relative(Doc.Folder, full);
                if (art.Any(a => MapArt.Resolve(Doc.Folder, a.Path) == full))
                {
                    DisplayStatus($"map art: {rel} is in the list already");
                    return;
                }
                Undo.CreateUndo("Add map art");
                art.Add(new ArtRef(rel, p[0] == "override"));
                Changed($"{rel} added ({(p[0] == "override" ? "replacing Blood's files of the same names" : "its tiles over Blood's")})");
                return;
            }
            case "remove":
            {
                int i = Index(rest);
                if (i < 0)
                    return;
                Undo.CreateUndo("Remove map art");
                string was = art[i].Path;
                art.RemoveAt(i);
                Changed($"{was} removed");
                return;
            }
            case "kind":
            {
                var p = rest.Split(' ');
                int i = Index(p[0]);
                if (i < 0 || p.Length < 2 || p[1] is not ("add" or "override"))
                    return;
                Undo.CreateUndo("Map art kind");
                art[i] = art[i] with { Override = p[1] == "override" };
                Changed($"{art[i].Path}: {(art[i].Override ? "override" : "added")}");
                return;
            }
            case "move":
            {
                var p = rest.Split(' ');
                int i = Index(p[0]);
                int j = p.Length > 1 && int.TryParse(p[1], out int dir) ? i + Math.Sign(dir) : -1;
                if (i < 0 || j < 0 || j >= art.Count)
                    return;
                Undo.CreateUndo("Map art order");
                (art[i], art[j]) = (art[j], art[i]);
                Changed($"{art[j].Path} moved {(j > i ? "later (over more)" : "earlier")}");
                return;
            }
            case "reload":
                SyncMapArt(true);
                DisplayStatus($"map art read again: {ForgeData.MapTiles.Count} tiles of the map's own"
                    + (ForgeData.MapArtProblems.Count > 0 ? "; " + string.Join("; ", ForgeData.MapArtProblems) : ""));
                return;
            case "pack":
                PackArt(rest);
                return;
        }
        DisplayStatus("mapart add|remove|kind|move|pack|reload ...");
    }

    /// <summary>The last pack's result, for Map Options to show (and the tests).</summary>
    public ArtPackerResultView LastPack { get; private set; }

    public sealed record ArtPackerResultView(string Folder, int Tiles, int Kept, int New, List<string> Files, List<string> Warnings, string Error);

    void PackArt(string rest)
    {
        var p = rest.Split('|');
        if (p.Length < 3 || p[0] is not ("classic" or "extended"))
        {
            DisplayStatus("mapart pack <classic|extended>|<pictures folder>|<out folder>");
            return;
        }
        string images = MapArt.Resolve(Doc.Folder, p[1].Trim()), outDir = MapArt.Resolve(Doc.Folder, p[2].Trim());
        if (!ForgeData.Ready)
        {
            LastPack = new ArtPackerResultView(outDir, 0, 0, 0, new(), new(), "no Blood folder (Edit > Preferences)");
            DisplayStatus("map art: " + LastPack.Error);
            return;
        }
        var art = Doc.Extras.Art;
        int self = art.FindIndex(a => MapArt.Resolve(Doc.Folder, a.Path).TrimEnd('\\', '/') == outDir.TrimEnd('\\', '/'));
        // the numbers Blood's tiles and the map's other sets take
        var others = ForgeData.TilesWithout(art, Doc.Folder, self);
        Blood.ArtPacker.Result r;
        try
        {
            r = Blood.ArtPacker.Pack(images, outDir, p[0] == "classic", others, ForgeData.Palette);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            r = new Blood.ArtPacker.Result { Error = e.Message };
        }
        LastPack = new ArtPackerResultView(outDir, r.Tiles.Count, r.Kept, r.New, r.Files, r.Warnings, r.Error);
        if (r.Error != null)
        {
            DisplayStatus("map art: " + r.Error);
            return;
        }
        string range = r.Tiles.Count == 0 ? "" : $"tiles {r.Tiles.Min(t => t.tile)}..{r.Tiles.Max(t => t.tile)}";
        string says = $"packed {r.Tiles.Count} pictures into {r.Files.Count} ART file{(r.Files.Count == 1 ? "" : "s")} ({range}; {r.Kept} kept their numbers, {r.New} new)"
            + (r.Warnings.Count > 0 ? $"; {r.Warnings.Count} warning{(r.Warnings.Count == 1 ? "" : "s")}: {r.Warnings[0]}" : "");
        if (self < 0)
        {
            Undo.CreateUndo("Pack map art");
            art.Add(new ArtRef(MapArt.Relative(Doc.Folder, outDir), false));
            Doc.NotifyChanged();
            UndoChanged();
        }
        SyncMapArt(true);
        DisplayStatus("map art: " + says);
    }
}
