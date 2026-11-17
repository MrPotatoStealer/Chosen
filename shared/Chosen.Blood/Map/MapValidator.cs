// Chosen Blood module (GPL v2): Blood's map rules (NBlood db.h limits, dbLoadMap's references).
using System.Collections.Generic;
using Chosen.Shared.Engine.Geometry;

namespace Chosen.Shared.Blood.Map;

/// <summary>How many of each object a map may hold (NBlood build.h MAXSECTORSV7 / MAXWALLSV7 / MAXSPRITESV7, db.h's
/// kMaxXSprites / kMaxXWalls / kMaxXSectors for Blood 1.21).</summary>
public sealed record Limits(string Name, int Sectors, int Walls, int Sprites, int XSprites, int XWalls, int XSectors)
{
    public static readonly Limits Vanilla = new("vanilla Blood", 1024, 8192, 4096, 2048, 512, 512);
    /// <summary>NBlood reads Build's version 8 limits (build.h MAXSECTORSV8 / MAXWALLSV8 / MAXSPRITESV8): MARROW's maps need them.</summary>
    public static readonly Limits NBlood = new("NBlood", 4096, 16384, 16384, 16384, 16384, 4096);   // db.h kMaxXSectors 4096
    /// <summary>Chosen's own format (.MAPX): no fixed arrays; the editor still warns past these.</summary>
    public static readonly Limits Extended = new("Chosen", 65536, 262144, 65536, 65536, 65536, 65536);
}

/// <summary>Blood's rules on top of the geometry's (GeomValidator): x-structure references, sprites' sectors, markers,
/// the start sector, the limits.</summary>
public static class MapValidator
{
    /// <summary>An index as the map file's 14-bit signed reference field keeps it (past 8191 it wraps: a map of NBlood's
    /// limits has walls and sprites beyond; the games set the references themselves as they load).</summary>
    static int Stored14(int i) => ((i + 8192) & 16383) - 8192;

    public static List<Issue> Check(BloodMapFile m, Limits limits)
    {
        var issues = GeomValidator.Check(m);
        int ns = m.Sectors.Count;

        void Over(string what, int n, int max)
        {
            if (n > max)
                issues.Add(new(Severity.Error, "limit", "map", 0, $"{n} {what} (at most {max} in {limits.Name})"));
        }
        Over("sectors", ns, limits.Sectors);
        Over("walls", m.Walls.Count, limits.Walls);
        Over("sprites", m.Sprites.Count, limits.Sprites);
        int xs = 0, xw = 0, xsp = 0;

        if (m.StartSect < 0 || m.StartSect >= ns)
            issues.Add(new(Severity.Warning, "start", "map", 0, $"the start sector {m.StartSect} is not a sector"));   // MRW2M13 (the game finds it from the start position)

        for (int i = 0; i < ns; i++)
            if (m.Sectors[i].Xs is { } x)
            {
                xs++;
                if (x.Reference != i && x.Reference != Stored14(i))
                    issues.Add(new(Severity.Warning, "reference", "sector", i, $"its x-sector's reference is {x.Reference}"));
            }
        for (int i = 0; i < m.Walls.Count; i++)
            if (m.Walls[i].Xw is { } x)
            {
                xw++;
                if (x.Reference != i && x.Reference != Stored14(i))
                    issues.Add(new(Severity.Warning, "reference", "wall", i, $"its x-wall's reference is {x.Reference}"));
            }
        for (int i = 0; i < m.Sprites.Count; i++)
        {
            var sp = m.Sprites[i];
            if (sp.Xsp is { } x)
            {
                xsp++;
                if (x.Reference != i && x.Reference != Stored14(i))
                    issues.Add(new(Severity.Warning, "reference", "sprite", i, $"its x-sprite's reference is {x.Reference}"));
            }
            if (sp.StatNum >= 1024)
                continue;           // removed from the game (dbLoadMap skips it)
            if (sp.SectNum < 0 || sp.SectNum >= ns)
            {
                issues.Add(new(Severity.Error, "sector", "sprite", i, $"sector {sp.SectNum} is not a sector"));
                continue;
            }
            if (sp.StatNum == 10)
            {
                // a sector's marker: owned by a sector with an x-sector (else dbLoadMap deletes it)
                if (sp.Owner < 0 || sp.Owner >= ns || m.Sectors[sp.Owner].Xs == null)
                    issues.Add(new(Severity.Warning, "marker", "sprite", i, $"a marker owned by {sp.Owner}, not a sector with an x-sector: Blood deletes it"));
                continue;           // markers sit where the sector's movement needs them, often outside it
            }
            if (!BuildGeom.Inside(m, sp.SectNum, sp.X, sp.Y) && BuildGeom.UpdateSector(m, sp.X, sp.Y, sp.SectNum) is int at && at != sp.SectNum)
                issues.Add(new(Severity.Warning, "sector", "sprite", i, at < 0
                    ? $"outside every sector (stored in {sp.SectNum})"
                    : $"stored in sector {sp.SectNum} but lies in {at}"));
        }
        Over("x-sectors", xs, limits.XSectors);
        Over("x-walls", xw, limits.XWalls);
        Over("x-sprites", xsp, limits.XSprites);
        return issues;
    }
}
