// Chosen Blood module (GPL v2): the markers a sector type works with (xmapedit's markerDB in aadjust.cpp; NBlood's
// kSector* and kMarker* types, db.cpp's PropagateMarkerReferences: a marker belongs to the sector that owns it).
using System.Collections.Generic;
using System.Linq;
using Chosen.Forge.Document;

namespace Chosen.Forge.Blood;

public static class SectorMarkers
{
    /// <summary>The marker types a sector type needs (0: none): rotating ones an axis, a teleporter its destination,
    /// sliding ones where they are off and on.</summary>
    public static (int first, int second) For(int sectorType) => sectorType switch
    {
        613 or 615 or 617 => (5, 0),
        604 => (8, 0),
        614 or 616 => (3, 4),
        _ => (0, 0),
    };

    /// <summary>
    /// Sector s's markers as its type needs them: a missing one made (in the sector, the second beside the first;
    /// status list 10, owned by the sector, an x-sector made for it, invisible), one its type doesn't use deleted. The
    /// x-sector's marker references follow (Blood finds them from the owners when it loads). How many changed.
    /// </summary>
    public static int Fix(MapDocument doc, int s)
    {
        var sec = doc.Sectors[s];
        var (m0, m1) = For(sec.Type);
        var want = new[] { m0, m1 }.Where(t => t != 0).ToList();
        int changes = 0;
        // markers it owns that its type doesn't use
        var gone = Enumerable.Range(0, doc.Sprites.Count)
            .Where(i => doc.Sprites[i].StatNum == 10 && doc.Sprites[i].Owner == s && !want.Contains(doc.Sprites[i].Type)).ToList();
        if (gone.Count > 0)
        {
            doc.DeleteSprites(gone);
            changes += gone.Count;
        }
        if (want.Count == 0)
            return changes;
        var xs = doc.EnsureXSector(s);
        var at = doc.Labels.Length > s ? doc.Labels[s] : new Godot.Vector2(doc.Walls[sec.WallPtr].X, doc.Walls[sec.WallPtr].Y);
        for (int k = 0; k < want.Count; k++)
        {
            int i = Enumerable.Range(0, doc.Sprites.Count).FirstOrDefault(j => doc.Sprites[j].StatNum == 10 && doc.Sprites[j].Owner == s
                && doc.Sprites[j].Type == want[k], -1);
            if (i < 0)
            {
                i = doc.AddSprite((int)at.X + k * 64, (int)at.Y, want[k], s);
                SpriteTypes.Apply(doc, i, want[k]);
                var m = doc.Sprites[i];
                m.StatNum = 10;
                m.Owner = s;
                m.Cstat |= 32768 | 128;
                m.Cstat &= ~(1 | 256);
                changes++;
            }
            if (k == 0)
                xs.Marker0 = i;
            else
                xs.Marker1 = i;
        }
        return changes;
    }
}
