// Chosen Blood module (GPL v2): which of Blood's record fields are properties (pasted from one item onto another) and
// which are structure (positions, wall links, references), as NBlood's db.h lays them out.
using System.Collections.Generic;
using System.Reflection;
using Chosen.Forge.Document;
using Chosen.Forge.View;
using Chosen.Shared.Blood.Map;

namespace Chosen.Forge.Blood;

public static class Properties
{
    static readonly HashSet<string> SectorKeep = new() { "WallPtr", "WallNum", "Extra" };
    static readonly HashSet<string> WallKeep = new() { "X", "Y", "Point2", "NextWall", "NextSector", "Extra", "Sector" };
    static readonly HashSet<string> SpriteKeep = new() { "X", "Y", "Z", "SectNum", "StatNum", "Owner", "Index", "FileIndex", "Extra", "Ang" };
    static readonly HashSet<string> XKeep = new() { "Reference", "Marker0", "Marker1", "Target", "TargetX", "TargetY", "TargetZ", "BurnSource" };

    static void CopyInts(object from, object to, HashSet<string> keep)
    {
        foreach (var f in from.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
            if (f.FieldType == typeof(int) && !keep.Contains(f.Name))
                f.SetValue(to, f.GetValue(from));
    }

    /// <summary>The properties of item src of a map (a copy taken earlier) onto item dst of the document.</summary>
    public static void Paste(MapDocument doc, BloodMapFile from, ElementKind kind, int src, int dst)
    {
        switch (kind)
        {
            case ElementKind.Sector when src < from.Sectors.Count:
                CopyInts(from.Sectors[src], doc.Sectors[dst], SectorKeep);
                if (from.Sectors[src].Xs is { } xs)
                    CopyInts(xs, doc.EnsureXSector(dst), XKeep);
                break;
            case ElementKind.Sprite when src < from.Sprites.Count:
                CopyInts(from.Sprites[src], doc.Sprites[dst], SpriteKeep);
                if (from.Sprites[src].Xsp is { } xp)
                    CopyInts(xp, doc.EnsureXSprite(dst), XKeep);
                break;
            case ElementKind.Line:
            {
                // the copied line's walls: its front (and back) as the lines were when copied
                int fw = src < doc.Lines.Count ? doc.Lines[src].Front : -1;
                if (fw < 0 || fw >= from.Walls.Count)
                    return;
                var line = doc.Lines[dst];
                Wall(doc, from, fw, line.Front);
                int fb = from.Walls[fw].NextWall;
                if (fb >= 0 && fb < from.Walls.Count && line.Back >= 0)
                    Wall(doc, from, fb, line.Back);
                break;
            }
        }
    }

    /// <summary>Wall src's properties (of a map copied earlier) onto wall dst.</summary>
    public static void PasteWall(MapDocument doc, BloodMapFile from, int src, int dst) => Wall(doc, from, src, dst);

    static void Wall(MapDocument doc, BloodMapFile from, int src, int dst)
    {
        CopyInts(from.Walls[src], doc.Walls[dst], WallKeep);
        if (from.Walls[src].Xw is { } xw)
            CopyInts(xw, doc.EnsureXWall(dst), XKeep);
    }
}
