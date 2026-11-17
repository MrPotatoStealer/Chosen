// Chosen core: original code. The context menu's own tools (ContextTools): each says where it applies and ends in a
// window command.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Chosen.Forge.Config;
using Chosen.Forge.Document;
using Chosen.Forge.View;

namespace Chosen.Forge.Tools;

public static class BuiltinTools
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static void Register()
    {
        // the 3D preview's surfaces
        // a sprite of the docker's type at the crosshair (on a wall: wall aligned); in Lights mode a light
        ContextTools.Register(new("Place", "Place Sprite Here", c => c.In3D && !c.LightsMode && c.UnderCrosshair != null,
            c => c.Window.PlaceAtCrosshair(), "visualinsert"));
        ContextTools.Register(new("Place", "Place Light Here", c => c.LightsMode && c.In3D && c.UnderCrosshair is { IsLight: false },
            c => c.Window.PlaceLightAtCrosshair(), "visualinsert"));
        ContextTools.Register(new("Place", "Place Light Here", c => c.LightsMode && !c.In3D,
            c => c.Window.Do("insertitem"), "insertitem"));
        // Lights mode's: the lights' settings, copied, pasted where the crosshair is, deleted
        ContextTools.Register(new("Lights", "Light Properties...", c => c.LightsMode && c.Lights.Count > 0,
            c => c.Window.EditProperties(View.ElementKind.Light, c.Lights)));
        ContextTools.Register(new("Lights", "Copy Lights", c => c.LightsMode && c.In3D && c.Lights.Count > 0,
            c => c.Window.UiDo("copylights " + string.Join(",", c.Lights)), "copyselection"));
        ContextTools.Register(new("Lights", "Paste Lights Here", c => c.LightsMode && c.In3D && c.Window.LightsCopied && c.UnderCrosshair is { IsLight: false },
            c => c.Window.PasteLightsAtCrosshair(), "pasteselection"));
        ContextTools.Register(new("Lights", "Delete Lights", c => c.LightsMode && c.In3D && c.Lights.Count > 0,
            c => c.Window.UiDo("visualdelete " + string.Join(" ", c.Lights.Select(i => "l" + i))), "visualdelete"));
        ContextTools.Register(new("Texture", "Change Texture...", c => c.In3D && c.Targets.Count > 0,
            c => c.Window.PickTexture(c.Targets)));
        ContextTools.Register(new("Texture", "Copy Texture", c => c.In3D && !c.LightsMode && c.UnderCrosshair != null,
            c => c.Window.UiDo("visualcopy " + Preview3D.Code(c.UnderCrosshair.Value)), "copyselection"));
        ContextTools.Register(new("Texture", "Paste Texture", c => c.In3D && c.Targets.Count > 0 && c.Window.TextureClip >= 0,
            c => c.Window.PasteTexture(c.Targets), "pasteselection"));
        ContextTools.Register(new("Texture", "Texture Settings...", c => c.In3D && c.Targets.Count > 0,
            c => c.Window.TextureSettings(c.Targets), "visualtexturesettings"));
        // (the window's actions: on the preview's targets as they are when picked)
        bool Wall(ToolContext c) => c.In3D && (c.UnderCrosshair is { IsWall: true } || c.Targets.Any(t => t.IsWall));
        ContextTools.Register(new("Align", "Auto Align Textures", Wall, c => c.Window.Do("visualautoalign"), "visualautoalign"));
        // a floor or ceiling: its picture carried on into the sectors next to it; a wall: its sector's floor or
        // ceiling laid along it (its first wall, relative)
        bool Plane(ToolContext c) => c.In3D && c.UnderCrosshair is { IsPlane: true };
        ContextTools.Register(new("Align", "Auto Align Floors / Ceilings", Plane, c => c.Window.Do("visualautoalign"), "visualautoalign"));
        bool AimWall(ToolContext c) => c.In3D && c.UnderCrosshair is { IsWall: true };
        ContextTools.Register(new("Align", "Align Floor to This Wall", AimWall,
            c => c.Window.UiDo("visualtex floortowall " + Preview3D.Code(c.UnderCrosshair.Value)), Shape: true));
        ContextTools.Register(new("Align", "Align Ceiling to This Wall", AimWall,
            c => c.Window.UiDo("visualtex ceilingtowall " + Preview3D.Code(c.UnderCrosshair.Value)), Shape: true));
        // the same in 2D: the selected sectors' floors or ceilings carried on from the first (the one under the mouse
        // when it is one of them); a wall's sector's floor or ceiling along it (the wall's front: its own side)
        bool Sectors2D(ToolContext c) => !c.In3D && c.Sectors.Count > 1;
        string PlaneCodes(ToolContext c, char kind)
        {
            var order = c.Sectors.ToList();
            if (c.Window.Editing.Mode is Editing.ClassicMode { Highlighted: { Kind: ElementKind.Sector } h } && order.Remove(h.Index))
                order.Insert(0, h.Index);
            return string.Join(" ", order.Select(i => $"{kind}{i}"));
        }
        ContextTools.Register(new("Align", "Auto Align Floors", Sectors2D, c => c.Window.UiDo("visualtex alignplanes " + PlaneCodes(c, 'f'))));
        ContextTools.Register(new("Align", "Auto Align Ceilings", Sectors2D, c => c.Window.UiDo("visualtex alignplanes " + PlaneCodes(c, 'c'))));
        bool OneLine(ToolContext c) => !c.In3D && c.Lines.Count == 1 && c.Doc.Lines[c.Lines[0]].Front >= 0;
        ContextTools.Register(new("Align", "Align Floor to This Wall", OneLine,
            c => c.Window.UiDo($"visualtex floortowall w{c.Doc.Lines[c.Lines[0]].Front}m"), Shape: true));
        ContextTools.Register(new("Align", "Align Ceiling to This Wall", OneLine,
            c => c.Window.UiDo($"visualtex ceilingtowall w{c.Doc.Lines[c.Lines[0]].Front}m"), Shape: true));
        ContextTools.Register(new("Align", "Auto Align X", Wall, c => c.Window.Do("visualautoalignx"), "visualautoalignx"));
        ContextTools.Register(new("Align", "Auto Align Y", Wall, c => c.Window.Do("visualautoaligny"), "visualautoaligny"));
        ContextTools.Register(new("Align", "Slope Edges On / Off", c => c.In3D && !c.LightsMode, c => c.Window.Do("visualslopeedges"), "visualslopeedges"));
        ContextTools.Register(new("Align", "Flip", c => c.In3D && c.Targets.Count > 0, c => c.Window.Do("visualflip"), "visualflip"));
        ContextTools.Register(new("Align", "Reset Texture", c => c.In3D && c.Targets.Count > 0, c => c.Window.Do("visualresettexture"),
            "visualresettexture"));
        ContextTools.Register(new("Align", "Copy Offsets", c => c.In3D && !c.LightsMode && c.UnderCrosshair != null, c => c.Window.Do("visualcopyoffsets"),
            "visualcopyoffsets"));
        ContextTools.Register(new("Align", "Paste Offsets", c => c.In3D && c.Targets.Count > 0, c => c.Window.Do("visualpasteoffsets"),
            "visualpasteoffsets"));

        // walls (the 2D view's lines, the 3D preview's walls)
        ContextTools.Register(new("Walls", "Split Walls", c => c.AllLines.Any(),
            c => c.Window.UiDo("splitlines " + string.Join(" ", c.AllLines)), Shape: true));
        ContextTools.Register(new("Walls", "Curve Walls...", c => c.AllLines.Any(), Curve, Shape: true));
        // wall portals and mirrors (Blood.RoomLinks: NBlood's wall stacks, Blood's mirrors)
        ContextTools.Register(new("Walls", "Make Portal...", c => c.AllLines.Count() == 2, MakeWallPortal, Shape: true));
        ContextTools.Register(new("Walls", "Make Mirror", c => c.AllLines.Any(l => l < c.Doc.Lines.Count && c.Doc.Lines[l].Back < 0),
            c => c.Window.UiDo("makemirror " + string.Join(" ", c.AllLines)), Shape: true));

        // sprites (2D or 3D) standing as Blood's maps have them: centred, their bottom on the floor
        static IEnumerable<int> Sprites(ToolContext c) => c.In3D ? c.Targets.Where(t => t.IsSprite).Select(t => t.Sprite) : c.Sprites;
        ContextTools.Register(new("Sprites", "Stand on Floor", c => Sprites(c).Any(),
            c => c.Window.UiDo("standonfloor " + string.Join(" ", Sprites(c)))));

        // anything selected in 2D: its points (a wall's ends, a sector's corners) or sprites to the grid
        ContextTools.Register(new("Grid", "Snap to Grid", c => !c.In3D && c.Any, c => c.Window.SnapToGrid(c), "snaptogrid", true));

        // sectors (the 2D view's selection)
        ContextTools.Register(new("Sectors", "Gradient...", c => GradientSectors(c).Count >= 2, Gradient, "gradienttool"));
        // rooms over rooms (Blood.RoomLinks)
        ContextTools.Register(new("Sectors", "Add Floor...", c => LinkSectors(c).Count > 0, AddFloor, Shape: true));
        ContextTools.Register(new("Sectors", "Make Portal...", c => LinkSectors(c).Count == 2, MakePortal, Shape: true));
        // (one sector: the dialog says why when it isn't a doorway)
        ContextTools.Register(new("Sectors", "Make Door...", c => LinkSectors(c) is { Count: 1 } l && Blood.Doors.Find(c.Doc, l[0]) == null, MakeDoor, Shape: true));
        ContextTools.Register(new("Sectors", "Door Properties...", c => LinkSectors(c) is { Count: 1 } l && Blood.Doors.Find(c.Doc, l[0]) != null, DoorProperties, Shape: true));
        ContextTools.Register(new("Sectors", "Join Sectors", c => !c.In3D && c.Sectors.Count > 1
            && c.Window.SelectionOf(ElementKind.Sector).Count > 1, c => c.Window.UiDo("joinsectors"), "joinsectors", true));
        ContextTools.Register(new("Sectors", "Merge Sectors", c => !c.In3D && c.Sectors.Count > 1
            && c.Window.SelectionOf(ElementKind.Sector).Count > 1, c => c.Window.UiDo("mergesectors"), "mergesectors", true));
        // a sector (a room) taken away into the one next to it (round it): no hole left
        ContextTools.Register(new("Sectors", "Separate Sector", c => !c.In3D && c.Sectors.Count > 0 && c.Window.Editing.Mode is Editing.SectorsMode
            && c.Sectors.Any(s => s < c.Doc.Sectors.Count && Editing.SectorsMode.Joined(c.Doc, s, c.Sectors)), c => c.Window.UiDo("separatesectors"), "separatesectors", true));
        ContextTools.Register(new("Sectors", "Dissolve Sector", c => !c.In3D && c.Sectors.Count > 0 && c.Window.Editing.Mode is Editing.SectorsMode
            && c.Sectors.Any(s => s < c.Doc.Sectors.Count && Editing.SectorsMode.DissolveInto(c.Doc, s) >= 0), c => c.Window.UiDo("dissolveitem"), "dissolveitem", true));

        // the items themselves
        ContextTools.Register(new("Items", "Properties...", c => c.In3D ? c.Targets.Count > 0 : c.Lines.Count + c.Sectors.Count + c.Sprites.Count > 0,
            Properties));
        ContextTools.Register(new("Items", "Delete", c => !c.In3D && c.Any, c => c.Window.UiDo("deleteitem"), "deleteitem", true));
    }

    static void Properties(ToolContext c)
    {
        var w = c.Window;
        if (c.In3D)
        {
            w.PreviewProperties(c.Targets);
            return;
        }
        if (c.Lines.Count > 0)
            w.UiProperties(ElementKind.Line, c.Lines);
        else if (c.Sectors.Count > 0)
            w.UiProperties(ElementKind.Sector, c.Sectors);
        else if (c.Sprites.Count > 0)
            w.UiProperties(ElementKind.Sprite, c.Sprites);
    }

    /// <summary>The sectors a gradient goes over, in the order chosen (the first where it starts, the last where it
    /// ends): the 2D view's selected sectors, the 3D preview's selected floors' and ceilings'.</summary>
    static List<int> GradientSectors(ToolContext c) => (c.In3D
        ? c.Targets.Where(t => t.IsPlane).Select(t => t.Sector)
        : c.Sectors).Where(s => s >= 0 && s < c.Doc.Sectors.Count).Distinct().ToList();

    /// <summary>The Gradient tool (SectorGradient): heights stepping over the sectors (stairs) or a brightness fading
    /// across them, from the first sector's value to the last's (Reverse swaps them).</summary>
    public static void Gradient(ToolContext c)
    {
        var w = c.Window;
        var doc = c.Doc;
        var sectors = GradientSectors(c);
        if (sectors.Count < 2)
        {
            w.DisplayStatus("gradient: select two sectors or more (the first is where it starts, the last where it ends)");
            return;
        }
        var d = new ToolDialog(w, "gradient", $"Gradient over {sectors.Count} Sectors");
        var what = d.Choice("what", "What", SectorGradient.WhatNames, 0);
        Godot.SpinBox Value(string label)
        {
            var s = new Godot.SpinBox { MinValue = -1 << 23, MaxValue = 1 << 23, AllowGreater = true, AllowLesser = true,
                SizeFlagsHorizontal = Godot.Control.SizeFlags.ExpandFill };
            d.Row(label, s);
            s.ValueChanged += _ => d.Refresh();
            return s;
        }
        var from = Value($"From (sector {sectors[0]})");
        var to = Value($"To (sector {sectors[^1]})");
        var reverse = new Godot.Button { Text = "Reverse", TooltipText = "the last sector's value to the first and the first's to the last" };
        d.Row("", reverse);
        var spacing = d.Choice("spacing", "Spacing", new[] { "Equal steps (stairs)", "By distance" }, 0);
        var round = d.Check("round", "Round heights to whole game units", true);
        var floors = d.Check("floors", "Brightness of floors", true);
        var ceilings = d.Check("ceilings", "Brightness of ceilings", true);
        var walls = d.Check("walls", "Brightness of walls", true);
        d.Note("those between the first and the last by where they lie on the way from one to the other");
        string W() => SectorGradient.Whats[what.Selected];
        bool Shade() => W() == "shade";
        int Parts() => (floors.ButtonPressed ? SectorGradient.ShadeFloors : 0) | (ceilings.ButtonPressed ? SectorGradient.ShadeCeilings : 0)
            | (walls.ButtonPressed ? SectorGradient.ShadeWalls : 0);
        // heights shown in the units chosen (game units: up positive), a shade as it is
        double Show(int v) => Shade() ? v : Units.Game ? -v / (double)Units.PerZ : v;
        int Build(Godot.SpinBox s) => Shade() ? (int)Math.Round(s.Value) : Units.Game ? (int)Math.Round(-s.Value * Units.PerZ) : (int)Math.Round(s.Value);
        void Load()
        {
            bool shade = Shade();
            foreach (var s in new[] { from, to })
            {
                s.Step = shade || !Units.Game ? 1 : 1.0 / Units.PerZ;
                s.Suffix = shade ? "shade" : Units.Name;
            }
            from.Value = Show(SectorGradient.Now(doc, sectors[0], W(), Parts()));
            to.Value = Show(SectorGradient.Now(doc, sectors[^1], W(), Parts()));
            round.Disabled = shade;
            floors.Disabled = ceilings.Disabled = walls.Disabled = !shade;
        }
        what.ItemSelected += _ => Load();
        reverse.Pressed += () =>
        {
            double f = from.Value;
            from.Value = to.Value;
            to.Value = f;
            d.Refresh();
        };
        Load();
        d.Change = copy =>
        {
            SectorGradient.Apply(copy, sectors, W(), Build(from), Build(to), spacing.Selected == 0, round.ButtonPressed, Parts());
            return null;
        };
        d.Show(() => string.Join(" ", "sectorgradient", W(), Build(from), Build(to), spacing.Selected == 0 ? 1 : 0, round.ButtonPressed ? 1 : 0,
            Parts(), string.Join(" ", sectors)));
    }

    /// <summary>The sectors Add Floor and Make Portal work on: the 2D view's selected sectors, the 3D preview's selected
    /// floors' and ceilings'.</summary>
    static List<int> LinkSectors(ToolContext c) => (c.In3D ? c.Targets.Where(t => t.IsPlane).Select(t => t.Sector) : c.Sectors)
        .Where(s => s >= 0 && s < c.Doc.Sectors.Count).Distinct().ToList();

    /// <summary>Add Floor (Blood.RoomLinks.AddFloor): above or below, at the same place on a layer (a new one or one
    /// there) or beside the map, the link's kind, the new room's height.</summary>
    public static void AddFloor(ToolContext c)
    {
        var w = c.Window;
        var doc = c.Doc;
        var sectors = LinkSectors(c);
        // (no id: nothing remembered, each time Above, the same place, a new layer, plain, the same height: a water or goo
        // link left from the last time made the selected room underwater under a floor added above it)
        var d = new ToolDialog(w, null, sectors.Count == 1 ? $"Add Floor to Sector {sectors[0]}" : $"Add Floor to {sectors.Count} Sectors", "Add");
        var dir = d.Choice("dir", "Where", new[] { "Above (its floor this ceiling)", "Below (its ceiling this floor)" }, 0);
        var place = d.Choice("place", "Place", new[] { "The same place, on a layer", "Beside the map (in empty space)" }, 0);
        // (the layer: 0 a new one, then the map's layers; the same place needs one apart from these sectors')
        var layer = d.Choice("layer", "Layer", new[] { "A new layer" }.Concat(doc.Layers.Select(l => l.Name)).ToArray(), 0);
        var kind = d.Choice("kind", "Link", Blood.RoomLinks.KindNames, 0);
        int max = Units.Game ? 1024 : 1024 * (int)Units.PerZ;
        var height = d.Int("height", Units.Game ? "Height (game units, 0: the same)" : "Height (Build z, 0: the same)", 0, 0, max);
        d.Note("an exact copy of each sector (walls and settings, not its sprites), linked to it: Blood draws and moves across the plane between them (tile 504)");
        d.Show(() =>
        {
            int h = (int)height.Value * (Units.Game ? (int)Units.PerZ : 1);
            return string.Join(" ", "addfloor", dir.Selected == 1 ? "below" : "above", place.Selected == 1 ? "beside" : "same", layer.Selected - 1,
                Blood.RoomLinks.KindCodes[Math.Max(0, kind.Selected)], h, string.Join(" ", sectors));
        });
    }

    /// <summary>Make Door (Blood.Doors): a doorway made a sliding or swinging pair of leaves, as E1M1's; the 3D preview
    /// shows the result while the dialog is open (stored open, as Blood stores doors).</summary>
    public static void MakeDoor(ToolContext c) => DoorDialog(c, null);

    /// <summary>Door Properties (Blood.Doors.Find, Adjust): the door under it in Make Door's dialog, filled in from the
    /// map (nothing remembered); its settings change in place, a changed shape makes it again from its doorway (Make
    /// Door's shape only: the shape of Blood's own doors stays).</summary>
    public static void DoorProperties(ToolContext c) => DoorDialog(c, Blood.Doors.Find(c.Doc, LinkSectors(c)[0]));

    static void DoorDialog(ToolContext c, Blood.Doors.Door door)
    {
        var w = c.Window;
        var doc = c.Doc;
        int s = LinkSectors(c)[0];
        var now = door?.Now;
        var sides = door != null ? Blood.Doors.SidesOf(doc, door) : Blood.Doors.Sides(doc, s);
        // (a door's own: nothing remembered, each field as the door has it)
        var d = door == null ? new ToolDialog(w, "door", $"Make Door in Sector {s}", "Make")
            : new ToolDialog(w, null, $"Door Properties: {door.What}", "Apply");
        d.Tab("Door");
        var kind = d.Choice("kind", "Door", Blood.Doors.KindNames, (int)(now?.Kind ?? 0));
        Blood.Doors.DoorKind Kind() => (Blood.Doors.DoorKind)Math.Max(0, kind.Selected);
        // (each room named with the way it lies from the doorway; Reverse: the other)
        var ways = Blood.Doors.SideWays(doc, door?.Doorway ?? s) is { Length: 2 } wl ? wl : new[] { "", "" };
        var side = d.Choice("side", "Swings into", sides != null
            ? new[] { $"Sector {sides[0]} ({ways[0]})", $"Sector {sides[1]} ({ways[1]})" } : new[] { "(not a doorway)" }, now?.Side ?? 0);
        var reverse = new Godot.Button { Text = "Reverse", TooltipText = "the leaves swing into the room on the other side" };
        d.Row("", reverse);
        reverse.Pressed += () =>
        {
            if (side.ItemCount < 2)
                return;
            int other = 1 - side.Selected;
            side.Select(other);
            side.EmitSignal(Godot.OptionButton.SignalName.ItemSelected, other);
        };
        var hinge = d.Choice("hinge", "Hinged at (one leaf)", new[] { "The left jamb", "The right jamb" }, now?.Hinge ?? 0);
        var carve = d.Check("carve", "Carve the room a pivot turns in (a thin doorway: cut out of the rooms either side)", now?.Carve ?? false);
        var thick = d.Length("thick", "Leaf thickness (0: half the doorway's depth sliding, 128 swinging, 64 a pivot)", now?.Thickness ?? 0, 4096);
        var lip = d.Length("lip", "Lip (sliding: how far open leaves stay out of the walls)", now?.Lip ?? 64, 4096);
        var inset = d.Length("inset", "Hinge inset (swinging: from the threshold into the room)", now?.Inset ?? 256, 4096);
        var busy = d.Int("busy", "Opening time (tenths of a second)", now?.BusyTime ?? 10, 1, 4095);
        var wait = d.Int("wait", "Closes by itself after (tenths of a second; 0: when pushed again)", now?.WaitTime ?? 0, 0, 4095);
        bool shaped = door == null || door.NotShaped == null;
        if (door == null)
            d.Note("left and right as seen from the doorway, looking into the room a leaf swings into; a pivot turns on its own axis "
                + "inside the doorway: draw the doorway square (as deep as it is wide), or Carve the room it turns in; the map stores a door open "
                + "as Blood stores them, a lifting one closed (the toolbar's Off and On show them, P plays them)");
        else if (shaped)
            d.Note("a changed kind, thickness, lip, inset or side makes the door again from its doorway; the rest changes where it is");
        else
            d.Note($"its shape stays ({door.NotShaped}): the rest changes where it is");

        // the leaves' pictures, each with what it looks like (-1: E1M1's for the kind)
        d.Tab("Look");
        int maxTile = Shared.Blood.Files.MapArt.MaxTile;
        var face = d.IntPick("face", "Faces (tile; -1: E1M1's)", now?.Face ?? -1, -1, maxTile, "Pick...", (sb, b) =>
            Ui.TilePickerDialog.Show(w, b, sb.Value >= 0 ? (int)sb.Value : Blood.Doors.DefaultFace(Kind()), t => sb.Value = t, kind: "wall"));
        var edge = d.IntPick("edge", "Edges (tile; -1: E1M1's)", now?.Edge ?? -1, -1, maxTile, "Pick...", (sb, b) =>
            Ui.TilePickerDialog.Show(w, b, sb.Value >= 0 ? (int)sb.Value : Blood.Doors.DefaultEdge(Kind()), t => sb.Value = t, kind: "wall"));
        Godot.TextureRect Thumb(Godot.SpinBox sb)
        {
            var t = new Godot.TextureRect { CustomMinimumSize = new Godot.Vector2(40, 40), ExpandMode = Godot.TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = Godot.TextureRect.StretchModeEnum.KeepAspectCentered };
            sb.GetParent().AddChild(t);
            return t;
        }
        var faceThumb = Thumb(face);
        var edgeThumb = Thumb(edge);
        d.Note("faces: the leaves' broad sides (the picture once across each), a lifting door's the rooms' walls facing it; "
            + "edges: the leaves' ends, a lifting door's bottom (its ceiling) or top (lowering: its floor)");

        // the sound sprite's four (a set as Blood's levels have them, or each picked)
        d.Tab("Sounds");
        var setNames = new List<string> { "E1M1's for the kind of door" };
        setNames.AddRange(Blood.Doors.SoundSets.Select(x => x.Name));
        setNames.Add("None");
        setNames.Add("Your own (below)");
        int none = setNames.Count - 2, own = setNames.Count - 1;
        // (a door's own: the set its sounds are, else none or your own)
        int setNow = now == null ? 0 : !now.HasSounds ? none
            : Array.FindIndex(Blood.Doors.SoundSets, x => x.Ids.SequenceEqual(now.SoundIds)) is var si and >= 0 ? si + 1 : own;
        var set = d.Choice("soundset", "Sounds", setNames.ToArray(), setNow);
        var ids = new[] { "Opening starts", "Opening stops", "Closing starts", "Closing stops" }.Select((label, k) =>
            d.IntPick($"sound{k}", label, now?.SoundIds[k] ?? 0, 0, 65535, "Pick...", (sb, b) => Ui.SoundPickerDialog.Show(w, b, (int)sb.Value, id => sb.Value = id))).ToArray();
        d.Note("a sound sprite in the door (709) plays them as it starts and stops moving (Pick...: hear them)");

        // what opens it
        d.Tab("Opening");
        var by = d.Choice("by", "Opened by", new[] { "The use key on the door", "Only its channel (a switch or a trigger elsewhere)" }, now == null || now.Push ? 0 : 1);
        var channel = d.IntPick(null, door == null ? "Channel (0: the first free from 100)" : "Channel", now?.Channel ?? 0, 0, 1023, "Pick...", (sb, b) =>
            Ui.ChannelPickerDialog.Show(w, b, (int)sb.Value, ch => sb.Value = ch));
        var key = d.Choice("key", "Needs", Blood.XFields.Keys.Where(k => k.Item1 <= 6).Select(k => k.Item1 == 0 ? "No key" : "The " + k.Item2.ToLowerInvariant()).ToArray(), Math.Clamp(now?.Key ?? 0, 0, 6));
        var once = d.Check("once", "Used once (opens and stays open)", now?.Once ?? false);
        d.Note("anything sending On, Off or Toggle on its channel opens and closes it (the use key's walls send on it too)");

        int[] SetIds() => set.Selected == 0 ? Blood.Doors.DefaultSounds(Kind())
            : set.Selected <= Blood.Doors.SoundSets.Length ? Blood.Doors.SoundSets[set.Selected - 1].Ids
            : set.Selected == none ? new int[4] : null;
        bool filling = false;
        void Fill()
        {
            if (SetIds() is not { } v)
                return;
            filling = true;
            for (int k = 0; k < 4; k++)
                ids[k].Value = v[k];
            filling = false;
        }
        // (a sound changed by hand: your own)
        foreach (var sb in ids)
            sb.ValueChanged += _ =>
            {
                if (!filling)
                    set.Selected = own;
            };
        void Thumbs()
        {
            var o = Opts();
            faceThumb.Texture = Data.TileImages.Get(o.FacePicture);
            edgeThumb.Texture = Data.TileImages.Get(o.EdgePicture);
        }
        void Enable()
        {
            var k = Kind();
            kind.Disabled = !shaped;
            side.Disabled = !Blood.Doors.Swings(k) || !shaped;
            reverse.Disabled = side.Disabled || side.ItemCount < 2;
            hinge.Disabled = k != Blood.Doors.DoorKind.SwingSingle || !shaped;
            carve.Disabled = k != Blood.Doors.DoorKind.Pivot || !shaped;
            thick.Editable = Blood.Doors.HasLeaves(k) && shaped;
            lip.Editable = k == Blood.Doors.DoorKind.SlidePair && shaped;
            inset.Editable = Blood.Doors.Swings(k) && shaped;
            key.Disabled = once.Disabled = by.Selected == 1;
            wait.Editable = !(once.ButtonPressed && by.Selected == 0);
        }
        // (a door's own pictures given as tiles: E1M1's for the kind it was become the other kind's)
        var was = Kind();
        kind.ItemSelected += _ =>
        {
            if (door != null && was != Kind())
            {
                if ((int)face.Value == Blood.Doors.DefaultFace(was))
                    face.Value = Blood.Doors.DefaultFace(Kind());
                if ((int)edge.Value == Blood.Doors.DefaultEdge(was))
                    edge.Value = Blood.Doors.DefaultEdge(Kind());
            }
            was = Kind();
            Enable();
            Fill();
            Thumbs();
        };
        set.ItemSelected += _ => Fill();
        by.ItemSelected += _ => Enable();
        once.Toggled += _ => Enable();
        face.ValueChanged += _ => Thumbs();
        edge.ValueChanged += _ => Thumbs();
        Blood.Doors.Options Opts() => new(Kind(), side.Selected, (int)ToolDialog.BuildLength(thick), (int)ToolDialog.BuildLength(lip),
            (int)ToolDialog.BuildLength(inset), (int)face.Value, (int)edge.Value, (int)busy.Value, (int)wait.Value,
            ids.Select(x => (int)x.Value).ToArray(), by.Selected == 0, (int)channel.Value, key.Selected, once.ButtonPressed, Math.Max(0, hinge.Selected), carve.ButtonPressed);
        Fill();
        Enable();
        Thumbs();
        if (door == null)
        {
            d.Change = copy => Blood.Doors.Make(copy, s, Opts(), out _);
            d.Show(() => Blood.Doors.Command(Opts(), s));
        }
        else
        {
            d.Change = copy => Blood.Doors.Adjust(copy, s, Opts(), out _);
            d.Show(() => Blood.Doors.Command(Opts(), s, "adjustdoor"));
        }
    }

    /// <summary>Make Portal (Blood.RoomLinks.MakePortal): two sectors linked, the higher floor's above, the link's kind.</summary>
    public static void MakePortal(ToolContext c)
    {
        var w = c.Window;
        var sectors = LinkSectors(c);
        var d = new ToolDialog(w, null, $"Make Portal: Sectors {sectors[0]} and {sectors[1]}", "Link");
        var kind = d.Choice("kind", "Link", Blood.RoomLinks.KindNames, 0);
        d.Note("the sector whose floor is higher goes above: its floor and the other's ceiling become the plane between them (504)");
        d.Show(() => $"makeportal {Blood.RoomLinks.KindCodes[Math.Max(0, kind.Selected)]} {sectors[0]} {sectors[1]}");
    }

    /// <summary>Make Portal on two walls (Blood.RoomLinks.MakeWallPortal): which side of the first the window is seen
    /// from (the bigger room's at first: what lies behind a window is often a small room).</summary>
    public static void MakeWallPortal(ToolContext c)
    {
        var w = c.Window;
        var doc = c.Doc;
        var lines = c.AllLines.ToList();
        var (f, b) = doc.Lines[lines[0]];
        int sf = doc.Walls[f].Sector, sb = b >= 0 ? doc.Walls[b].Sector : -1;
        double Area(int s)
        {
            if (s < 0)
                return 0;
            var (x0, y0, x1, y1) = Chosen.Shared.Engine.Geometry.BuildGeom.SectorBounds(doc, s);
            return (double)(x1 - x0) * (y1 - y0);
        }
        var d = new ToolDialog(w, null, $"Make Portal: Walls {lines[0]} and {lines[1]}", "Link");
        var side = d.Choice("side", $"Wall {lines[0]} seen from", new[] { $"Sector {sf}", sb >= 0 ? $"Sector {sb}" : "(nothing behind it)" }, Area(sb) > Area(sf) ? 1 : 0);
        d.Note("NBlood's wall stack: each wall a window (its one-way picture 504) showing the room behind the other wall, shifted by their offset, not turned or scaled: two red walls, the same length and parallel; both then block moves and shots");
        d.Show(() => $"makewallportal {lines[0]} {lines[1]} {(side.Selected == 1 ? "back" : "front")}");
    }

    /// <summary>Curve Walls (UDB's Curve Linedefs): points, shape, depth, which way, a connected run as one.</summary>
    static void Curve(ToolContext c)
    {
        var w = c.Window;
        var doc = c.Doc;
        var lines = c.AllLines.ToList();
        var f = doc.Lines[lines[0]].Front;
        var (x1, y1, x2, y2) = Chosen.Shared.Engine.Geometry.BuildGeom.WallEnds(doc, f);
        double len = Math.Sqrt((double)(x2 - x1) * (x2 - x1) + (double)(y2 - y1) * (y2 - y1));

        var d = new ToolDialog(w, "curve", lines.Count == 1 ? "Curve Wall" : $"Curve {lines.Count} Walls");
        var count = d.Int("points", "Points", 8, 1, 128);
        var shape = d.Choice("shape", "Shape", Curves.Names, 0);
        var depth = d.Length("depth", "Depth", Math.Max(16, Math.Round(len / 4)));
        var reverse = d.Check("reverse", "Reverse (bend the other way)", false);
        var asOne = d.Check("asone", "Bend connected walls as one", true);
        d.Note("Points: new points per wall, or along a run bent as one");
        string Args() => string.Join(" ", (int)count.Value, Curves.Codes[shape.Selected],
            ToolDialog.BuildLength(depth).ToString(Inv), reverse.ButtonPressed ? 1 : 0, asOne.ButtonPressed ? 1 : 0);
        CurvePlan Plan(MapDocument on) => on.PlanCurve(lines, (CurveShape)shape.Selected, (int)count.Value,
            ToolDialog.BuildLength(depth), reverse.ButtonPressed, asOne.ButtonPressed);
        d.Preview = () => Plan(doc).Paths;
        // the 3D preview: the map as it would be (a copy curved the same way)
        d.Change = copy => copy.ApplyCurve(Plan(copy));
        d.Show(() => $"curvelines {Args()} " + string.Join(" ", lines));
    }

    /// <summary>"curvelines count shape depth reverse asone line...": the arguments, or null.</summary>
    public static (int count, CurveShape shape, double depth, bool reverse, bool asOne, List<int> lines)? ParseCurve(string action)
    {
        var p = action.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (p.Length < 7 || !int.TryParse(p[1], out int count) || Curves.Parse(p[2]) is not { } shape
            || !double.TryParse(p[3], NumberStyles.Float, Inv, out double depth))
            return null;
        var lines = p.Skip(6).Select(s => int.TryParse(s, out int l) ? l : -1).Where(l => l >= 0).ToList();
        return (Math.Clamp(count, 1, 1024), shape, depth, p[4] == "1", p[5] == "1", lines);
    }
}
