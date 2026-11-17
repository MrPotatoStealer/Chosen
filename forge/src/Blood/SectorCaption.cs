// Chosen Blood module (GPL v2): what a sector does, in words (xmapedit's ExtGetSectorCaption, xmpstub.cpp: its type,
// channels, lock, state and panning; NBlood's x-sector fields for the rest: actor.cpp's sector damage and wind, the
// lighting waves of mapedit.cpp's tLighting).
using System.Collections.Generic;
using Chosen.Forge.Document;

namespace Chosen.Forge.Blood;

public static class SectorCaption
{
    static readonly string[] Waves =
        { "steady", "square", "saw", "ramp up", "ramp down", "sine", "flicker 1", "flicker 2", "flicker 3", "flicker 4", "strobe", "search" };

    static readonly string[] Damages = { "fall", "burn", "bullet", "explode", "drown", "spirit", "tesla" };

    const int DamageSector = 618;

    /// <summary>Sector s's caption: its line ("#12 Z Motion  rx 5  tx 12  On", a locked one "locked") and what it does
    /// ("pan F 40 @ 90°, light sine +20, damage burn, on enter"; empty if nothing); null for a sector that does
    /// nothing (no type, no x-sector worth telling).</summary>
    public static (string head, string effects)? Of(MapDocument d, int s)
    {
        var sec = d.Sectors[s];
        var x = sec.Xs;
        var head = new List<string> { $"#{s}" };
        if (sec.Type != 0)
            head.Add(BloodNames.Sector(sec.Type));
        if (x != null)
        {
            if (x.RxID > 0)
                head.Add($"rx {x.RxID}");
            if (x.TxID > 0)
                head.Add($"tx {x.TxID}");
            if (x.State != 0)
                head.Add("On");
            if (x.Locked != 0)
                head.Add("locked");
        }
        var fx = x == null ? new List<string>() : Effects(sec.Type, x);
        // (enemies kept out alone: most doors have it, nothing to tell)
        if (head.Count == 1 && (fx.Count == 0 || fx is ["no enemies"]))
            return null;
        return (string.Join("  ", head), string.Join(", ", fx));
    }

    static string Deg(int ang) => $"{(ang & 2047) * 360 / 2048.0:0.#}°";

    static List<string> Effects(int type, Chosen.Shared.Blood.Map.XSector x)
    {
        var fx = new List<string>();
        if (x.PanVel != 0 && (x.PanFloor != 0 || x.PanCeiling != 0))
            fx.Add($"pan {(x.PanCeiling != 0 ? "C" : "")}{(x.PanFloor != 0 ? "F" : "")} {x.PanVel} @ {Deg(x.PanAngle)}"
                + (x.PanAlways != 0 ? " always" : ""));
        if (x.Amplitude != 0)
            fx.Add($"light {(x.Wave >= 0 && x.Wave < Waves.Length ? Waves[x.Wave] : $"wave {x.Wave}")} {x.Amplitude:+0;-0}"
                + (x.ShadeAlways != 0 ? " always" : ""));
        // (NBlood's actor.cpp: a Damage sector's type as stored, any other's less one; 0 there: none)
        if (type == DamageSector || x.DamageType > 0)
        {
            int t = System.Math.Clamp(type == DamageSector ? x.DamageType : x.DamageType - 1, 0, Damages.Length - 1);
            fx.Add($"damage {Damages[t]}");
        }
        if (x.Underwater != 0)
            fx.Add("underwater");
        if (x.Depth != 0)
            fx.Add($"depth {x.Depth}");
        if (x.WindVel != 0)
            fx.Add($"wind {x.WindVel} @ {Deg(x.WindAng)}" + (x.WindAlways != 0 ? " always" : ""));
        if (x.BobZRange != 0 && x.BobSpeed != 0)
            fx.Add($"bob {x.BobZRange}" + (x.BobAlways != 0 ? " always" : ""));
        if (x.Crush != 0)
            fx.Add("crush");
        if (x.Key > 0)
            fx.Add($"key {x.Key}");
        var on = new List<string>();
        if (x.Enter != 0) on.Add("enter");
        if (x.Exit != 0) on.Add("exit");
        if (x.Push != 0) on.Add("push");
        if (x.Wallpush != 0) on.Add("wall push");
        if (x.Vector != 0) on.Add("hit");
        if (on.Count > 0)
            fx.Add("on " + string.Join("/", on) + (x.TriggerOnce != 0 ? " once" : ""));
        if (x.DudeLockout != 0)
            fx.Add("no enemies");
        return fx;
    }
}
