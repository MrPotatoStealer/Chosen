using System.Collections.Generic;
using System.Linq;
using Chosen.Shared.Blood.Map;

namespace Chosen.Data;

/// <summary>
/// The level's own art: the ART files a .MAPX names (its ARTS chunk, Forge's Map Options > Art), over Blood's and the
/// active add-on's (TileArt composes them: Shared.Blood.Files.MapArt), each path relative to the map's folder. Set as
/// a level loads (LevelLoader); a .MAP or a .MAPX naming none: none.
/// </summary>
public static class LevelArt
{
    static string _key = "";

    /// <summary>The level's art sets, in order (empty: Blood's art alone).</summary>
    public static IReadOnlyList<ArtRef> Sets { get; private set; } = new List<ArtRef>();
    /// <summary>The folder the sets' relative paths start from (the map's).</summary>
    public static string Folder { get; private set; } = "";
    /// <summary>The tiles the sets brought (TileArt fills it as it composes): the tile table gets rows for them.</summary>
    public static readonly HashSet<int> Own = new();

    /// <summary>The level's sets (null: none); the tiles read again when they differ from the last level's.</summary>
    public static void Use(IEnumerable<ArtRef> sets, string folder)
    {
        var list = sets?.ToList() ?? new List<ArtRef>();
        // (the files' times too: packed again in Forge, the running game's reload shows the new pictures)
        string key = list.Count == 0 ? "" : folder + "|" + string.Join("|", list.Select(a => (a.Override ? "o:" : "a:") + a.Path + ":"
            + string.Join(",", Chosen.Shared.Blood.Files.MapArt.Files(Chosen.Shared.Blood.Files.MapArt.Resolve(folder, a.Path))
                .Select(f => System.IO.File.GetLastWriteTimeUtc(f).Ticks))));
        if (key == _key)
            return;
        _key = key;
        Sets = list;
        Folder = folder ?? "";
        Own.Clear();
        TileArt.Reload();
        Tiles.Reload();
        TileTextures.Clear();
        Render.Native.NativeWorld.ClearTextures();
        if (list.Count > 0)
            Core.GameConsole.Print($"level art: {list.Count} set{(list.Count == 1 ? "" : "s")} ({string.Join(", ", list.Select(a => a.Path))})");
    }
}
