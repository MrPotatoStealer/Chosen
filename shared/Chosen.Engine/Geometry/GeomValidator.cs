// Chosen core: original code.
using System.Collections.Generic;

namespace Chosen.Shared.Engine.Geometry;

public enum Severity { Warning, Error }

/// <summary>A problem a check found: what kind, on which object, and a sentence about it.</summary>
public sealed record Issue(Severity Severity, string Kind, string Object, int Index, string Message)
{
    public override string ToString() => $"{(Severity == Severity.Error ? "error" : "warning")} {Kind} {Object} {Index}: {Message}";
}

/// <summary>
/// The rules a level of sectors and wall loops must keep (for the editor's checks after each operation and the map
/// error list): each wall in exactly one sector's range, every loop closed inside its sector's walls, two-sided walls
/// paired both ways with their ends reversed, and each sector with an outer loop.
/// </summary>
public static class GeomValidator
{
    public static List<Issue> Check(IBuildData d)
    {
        var issues = new List<Issue>();
        int nw = d.WallCount, ns = d.SectorCount;
        var owner = new int[nw];
        for (int i = 0; i < nw; i++)
            owner[i] = -1;
        for (int s = 0; s < ns; s++)
        {
            var sec = d.Sector(s);
            if (sec.WallNum < 1 || sec.WallPtr < 0 || sec.WallPtr + sec.WallNum > nw)
            {
                issues.Add(new(Severity.Error, "walls", "sector", s, $"wall range {sec.WallPtr}+{sec.WallNum} is not a range of the {nw} walls"));
                continue;
            }
            // (a sector of fewer than 3 walls has no area; shipped maps have them: E6M7's 144, MRW2M6)
            if (sec.WallNum < 3)
                issues.Add(new(Severity.Warning, "walls", "sector", s, $"only {sec.WallNum} walls"));
            for (int w = sec.WallPtr; w < sec.WallPtr + sec.WallNum; w++)
            {
                if (owner[w] >= 0)
                    issues.Add(new(Severity.Error, "walls", "wall", w, $"in the ranges of sectors {owner[w]} and {s}"));
                owner[w] = s;
            }
        }
        // (walls left in no sector are ignored by the game; Death Wish's DWE4M1 has 36)
        for (int w = 0; w < nw; w++)
            if (owner[w] < 0)
                issues.Add(new(Severity.Warning, "walls", "wall", w, "in no sector's range"));
        if (issues.Exists(i => i.Severity == Severity.Error))
            return issues;          // the rest assumes the ranges

        for (int s = 0; s < ns; s++)
        {
            var sec = d.Sector(s);
            if (sec.WallNum < 3)
                continue;           // warned above; no loop to check (DWE4M1's one-wall sector 547)
            int end = sec.WallPtr + sec.WallNum;
            bool loopsOk = true;
            for (int w = sec.WallPtr; w < end; w++)
            {
                int p2 = d.Wall(w).Point2;
                if (p2 < sec.WallPtr || p2 >= end)
                {
                    issues.Add(new(Severity.Error, "loop", "wall", w, $"point2 {p2} is outside sector {s}'s walls {sec.WallPtr}..{end - 1}"));
                    loopsOk = false;
                }
            }
            if (!loopsOk)
                continue;
            // every wall reached exactly once by following point2 from the loop starts
            var hits = new int[sec.WallNum];
            for (int w = sec.WallPtr; w < end; w++)
                hits[d.Wall(w).Point2 - sec.WallPtr]++;
            for (int k = 0; k < sec.WallNum; k++)
                if (hits[k] != 1)
                {
                    issues.Add(new(Severity.Error, "loop", "wall", sec.WallPtr + k, $"{hits[k]} walls of sector {s} lead to it (a loop must be closed: exactly 1)"));
                    loopsOk = false;
                }
            if (!loopsOk)
                continue;
            var loops = BuildGeom.Loops(d, s);
            int outer = 0;
            foreach (var l in loops)
            {
                if (l.Count < 3)
                    issues.Add(new(Severity.Warning, "loop", "sector", s, $"a loop of {l.Count} walls (from wall {l[0]})"));
                if (BuildGeom.LoopIsOuter(d, l))
                    outer++;
            }
            if (outer == 0)
                issues.Add(new(Severity.Warning, "winding", "sector", s, "no loop runs clockwise (no outer loop)"));
        }

        for (int w = 0; w < nw; w++)
        {
            var a = d.Wall(w);
            if ((a.NextWall < 0) != (a.NextSector < 0))
            {
                issues.Add(new(Severity.Error, "pair", "wall", w, $"nextwall {a.NextWall} with nextsector {a.NextSector} (both or neither must be set)"));
                continue;
            }
            if (a.NextWall < 0)
                continue;
            if (a.NextWall >= nw || a.NextSector >= ns)
            {
                issues.Add(new(Severity.Error, "pair", "wall", w, $"nextwall {a.NextWall} / nextsector {a.NextSector} out of range"));
                continue;
            }
            var b = d.Wall(a.NextWall);
            // one-way pairs (several walls facing one; shipped maps have them where sectors overlap: E3M5's wall 264,
            // Death Wish's DWE1M9, DWE2M4, DWE3M11...) play in Blood: warnings, not errors
            if (b.NextWall != w)
                issues.Add(new(Severity.Warning, "pair", "wall", w, $"its nextwall {a.NextWall} points back to {b.NextWall}"));
            if (owner[a.NextWall] != a.NextSector)
                issues.Add(new(Severity.Warning, "pair", "wall", w, $"nextsector {a.NextSector} but its nextwall {a.NextWall} is in sector {owner[a.NextWall]}"));
            var (ax1, ay1, ax2, ay2) = BuildGeom.WallEnds(d, w);
            var (bx1, by1, bx2, by2) = BuildGeom.WallEnds(d, a.NextWall);
            if (ax1 != bx2 || ay1 != by2 || ax2 != bx1 || ay2 != by1)
                issues.Add(new(Severity.Warning, "pair", "wall", w, $"its ends ({ax1},{ay1})-({ax2},{ay2}) are not nextwall {a.NextWall}'s reversed ({bx1},{by1})-({bx2},{by2})"));
        }
        return issues;
    }
}
