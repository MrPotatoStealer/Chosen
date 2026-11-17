// Chosen core: original code. Rooms over rooms (Blood.RoomLinks): Add Floor and Make Portal as recorded commands.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;

namespace Chosen.Forge;

public partial class MainWindow
{
    /// <summary>
    /// "addfloor &lt;above|below&gt; &lt;same|beside&gt; &lt;layer, -1 new&gt; &lt;plain|water|goo&gt; &lt;height (Build z, 0: each
    /// sector's own)&gt; &lt;sectors...&gt;" and "makeportal &lt;plain|water|goo&gt; &lt;sector&gt; &lt;sector&gt;": one undo step each
    /// (Blood.RoomLinks). True when the action was one of them.
    /// </summary>
    bool LinkCommand(string action)
    {
        var p = action.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        int I(int k) => k < p.Length && int.TryParse(p[k], NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : -1;
        if (p.Length > 0 && p[0] == "addfloor")
        {
            if (p.Length < 7)
            {
                DisplayStatus("addfloor <above|below> <same|beside> <layer> <kind> <height> <sectors...>");
                return true;
            }
            bool above = p[1] != "below", beside = p[2] == "beside";
            int layer = I(3), kind = Blood.RoomLinks.KindOf(p[4]), height = Math.Max(0, I(5));
            var sectors = p.Skip(6).Select(t => int.TryParse(t, out int s) ? s : -1).Where(s => s >= 0).ToList();
            List<int> made = null;
            if (Operation($"Add floor {(above ? "above" : "below")}", () => Blood.RoomLinks.AddFloor(Doc, sectors, above, beside, layer, kind, height, out made)))
            {
                _layers?.Refresh();
                DisplayStatus($"floor {(above ? "above" : "below")}: {made.Count} sector{(made.Count == 1 ? "" : "s")} ({string.Join(", ", made)}) "
                    + $"{(beside ? "beside the map" : "at the same place")} in layer \"{Doc.Layers[Doc.CurrentLayer].Name}\" (now edited), "
                    + $"linked to {string.Join(", ", sectors)} ({Blood.RoomLinks.KindCodes[kind]}); {Blood.RoomLinks.PlanesNote(Doc, kind, above ? made.FirstOrDefault(-1) : sectors.FirstOrDefault(-1))}");
            }
            return true;
        }
        // "makewallportal <line> <line> <front|back>": the first line's side the window is seen from
        if (p.Length > 0 && p[0] == "makewallportal")
        {
            if (p.Length < 4)
            {
                DisplayStatus("makewallportal <line> <line> <front|back>");
                return true;
            }
            int wa = -1, wb = -1;
            if (Operation("Make wall portal", () => Blood.RoomLinks.MakeWallPortal(Doc, I(1), I(2), p[3] == "back", out wa, out wb)))
                DisplayStatus($"wall portal: walls {wa} (sector {Doc.Walls[wa].Sector}) and {wb} (sector {Doc.Walls[wb].Sector}), stack {Doc.Walls[wa].Xw?.Data}: "
                    + "each a window onto the room behind the other (NBlood draws it; the game and the 3D preview too)");
            return true;
        }
        // "makemirror <lines...>": white walls given the mirror picture
        if (p.Length > 0 && p[0] == "makemirror")
        {
            List<int> made = null;
            if (Operation("Make mirror", () => Blood.RoomLinks.MakeMirror(Doc, p.Skip(1).Select(t => int.TryParse(t, out int l) ? l : -1), out made)))
                DisplayStatus($"mirror: wall{(made.Count == 1 ? "" : "s")} {string.Join(", ", made)} given Blood's mirror picture (504)");
            return true;
        }
        // "makedoor <slide|swing> <side 0|1> <thickness> <lip> <inset> <picture> <busy> <wait> <sounds 0|1> <sector> [name=value...]" (Blood.Doors.Command):
        // a doorway made a door (Blood.Doors)
        if (p.Length > 0 && p[0] is "makedoor" or "adjustdoor")
        {
            if (Blood.Doors.Parse(p) is not { } o)
            {
                DisplayStatus("makedoor <slide|swing> <side> <thickness> <lip> <inset> <picture> <busy> <wait> <sounds> <sector> "
                    + "[edge=<tile>] [sounds=<a,b,c,d>] [by=push|channel] [channel=<n>] [key=<0-6>] [once=0|1]");
                return true;
            }
            string made = null;
            bool adjust = p[0] == "adjustdoor";
            if (Operation(adjust ? "Door properties" : "Make door (" + Blood.Doors.KindNames[(int)o.Kind].Split(" (")[0].ToLowerInvariant() + ")",
                () => adjust ? Blood.Doors.Adjust(Doc, I(10), o, out made) : Blood.Doors.Make(Doc, I(10), o, out made)))
                DisplayStatus(made);
            return true;
        }
        if (p.Length > 0 && p[0] == "makeportal")
        {
            if (p.Length < 4)
            {
                DisplayStatus("makeportal <kind> <sector> <sector>");
                return true;
            }
            int kind = Blood.RoomLinks.KindOf(p[1]);
            int up = -1, low = -1;
            if (Operation("Make portal", () => Blood.RoomLinks.MakePortal(Doc, I(2), I(3), kind, out up, out low)))
                DisplayStatus($"portal: sector {up}'s floor linked to sector {low}'s ceiling ({Blood.RoomLinks.KindCodes[kind]}): {Blood.RoomLinks.PlanesNote(Doc, kind, up)}, "
                    + "a marker in each (their offset is the rooms')");
            return true;
        }
        return false;
    }
}
