// Chosen core: original code.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Chosen.Forge.Config;
using Chosen.Forge.Document;
using Chosen.Forge.Editing;
using Godot;

namespace Chosen.Forge;

/// <summary>
/// --uitest: the editing modes driven as a mapper drives them, by mouse and key events through the window (not the
/// document's operations): a rectangle drawn (a sector), one inside it (a room), a line across (a split), a refused
/// drawing (its reason stays in the status bar), right-click auto-draw in Vertices mode, a sprite placed by right-drag
/// in Sprites mode, the view panned with Space held. The user's settings are put back. Exit code 0: passed.
/// </summary>
public partial class MainWindow
{
    async Task<int> UiTest()
    {
        int failures = 0;
        void Check(bool ok, string what)
        {
            GD.Print($"uitest: {what}: {(ok ? "ok" : "FAILED")}");
            if (!ok)
                failures++;
        }
        // the settings file as it was, written back at the end (the test sets the sprite type, the grid...)
        string cfgPath = ProjectSettings.GlobalizePath(Chosen.Forge.Config.ForgeConfig.FileName);
        byte[] cfgBefore = System.IO.File.Exists(cfgPath) ? System.IO.File.ReadAllBytes(cfgPath) : null;
        try
        {
            SetDocument(MapDocument.New());
            View.GridSize = 64;
            View.Snap = true;
            await Frames(3);
            View.Zoom = Mathf.Min(View.Size.X, View.Size.Y) / 9000f;
            View.CentreOn(Vector2.Zero);
            await Frames(1);

            // a rectangle outside everything: a sector
            UiDo("drawrectanglemode");
            await Click(MouseButton.Left, new(-2048, -2048));
            await Click(MouseButton.Left, new(2048, 2048));
            Check(Doc.Sectors.Count == 1 && Editing.Mode is SectorsMode or VerticesMode or WallsMode or SpritesMode,
                $"rectangle: a sector ({Doc.Sectors.Count} sectors, back in {Editing.Mode?.Name})");
            // a new map's start (sector -1) moved into the first sector drawn: no "start sector" warning
            Check(Doc.Map.StartSect == 0 && !Shared.Blood.Map.MapValidator.Check(Doc.Map, Shared.Blood.Map.Limits.Extended).Any(i => i.Kind == "start"),
                $"the map's start is in a sector (sector {Doc.Map.StartSect})");

            // a rectangle against it, sharing part of its east wall: a sector joined to it by a red wall
            UiDo("drawrectanglemode");
            await Click(MouseButton.Left, new(2048, -2048));
            await Click(MouseButton.Left, new(3072, -1024));
            bool joined = Doc.Sectors.Count == 2 && Enumerable.Range(Doc.Sectors[1].WallPtr, Doc.Sectors[1].WallNum)
                .Any(w => Doc.Walls[w].NextSector == 0 && Doc.Walls[Doc.Walls[w].NextWall].NextWall == w);
            Check(joined && MapErrors() == 0, $"rectangle against a sector: joined by a red wall ({Doc.Sectors.Count} sectors, "
                + $"{MapErrors()} map errors; status \"{_status.Text}\")");

            // a rectangle inside it: a room
            UiDo("drawrectanglemode");
            await Click(MouseButton.Left, new(512, -512));
            await Click(MouseButton.Left, new(1024, 512));
            Check(Doc.Sectors.Count == 3, $"rectangle inside a sector: a room ({Doc.Sectors.Count} sectors; status \"{_status.Text}\")");

            // the sector dragged: the room in it goes along (and the sector against it stretches); undone
            int roomX = Doc.Walls[Doc.Sectors[2].WallPtr].X, outerX = Doc.Walls[Doc.Sectors[0].WallPtr].X;
            UiDo("sectorsmode");
            await Move(new(-1024, 0));
            await Press(MouseButton.Left, new(-1024, 0), true);
            await Move(new(-896, 0));
            await Move(new(-768, 0));
            await Press(MouseButton.Left, new(-768, 0), false);
            int dRoom = Doc.Walls[Doc.Sectors[2].WallPtr].X - roomX, dOuter = Doc.Walls[Doc.Sectors[0].WallPtr].X - outerX;
            Check(dOuter == 256 && dRoom == 256 && MapErrors() == 0, $"sector dragged 256 east: its room went along (sector {dOuter}, room {dRoom}; "
                + $"{MapErrors()} map errors; status \"{_status.Text}\")");
            UiDo("undo");
            Check(Doc.Walls[Doc.Sectors[2].WallPtr].X == roomX, "and undone");

            // a double-click on the sector: it and the room inside it selected (not the sector beside it), Shift too
            // (the sector beside it, selected first, kept); Ctrl+double-click: both out again
            UiDo("clearselection");
            await DoubleClick(new(-1024, 0));
            var sel = new HashSet<int>(SelectionOf(Chosen.Forge.View.ElementKind.Sector));
            bool withRoom = sel.SetEquals(new[] { 0, 2 });
            UiDo("clearselection");
            await Click(MouseButton.Left, new(2560, -1536));
            await DoubleClick(new(-1024, 0), "s");
            bool dblKept = new HashSet<int>(SelectionOf(Chosen.Forge.View.ElementKind.Sector)).SetEquals(new[] { 0, 1, 2 });
            await DoubleClick(new(-1024, 0), "c");
            bool outAgain = new HashSet<int>(SelectionOf(Chosen.Forge.View.ElementKind.Sector)).SetEquals(new[] { 1 });
            UiDo("clearselection");
            Check(withRoom && dblKept && outAgain, $"double-click on a sector: it and its room selected {withRoom} ({string.Join(",", sel)}), "
                + $"Shift: added to what was selected {dblKept}, Ctrl: taken out {outAgain} (status \"{_status.Text}\")");

            // a line across the outer sector, above the room: a split (right click finishes)
            UiDo("drawlinesmode");
            await Click(MouseButton.Left, new(-2048, -1024));
            await Click(MouseButton.Left, new(2048, -1024));
            await Click(MouseButton.Right, new(2048, -1024));
            Check(Doc.Sectors.Count == 4, $"line across a sector: a split ({Doc.Sectors.Count} sectors; status \"{_status.Text}\")");

            // a rectangle partly over a sector (UDB's drawing): the sector cut along it, the part outside a new sector,
            // joined by red walls; undone (the steps below count the sectors as before)
            UiDo("drawrectanglemode");
            await Click(MouseButton.Left, new(1536, 1536));
            await Click(MouseButton.Left, new(2560, 2560));
            bool cutJoined = Doc.Sectors.Count == 6 && Enumerable.Range(Doc.Sectors[5].WallPtr, Doc.Sectors[5].WallNum)
                .Any(w => Doc.Walls[w].NextSector == 4);
            Check(cutJoined && MapErrors() == 0, $"rectangle partly over a sector: cut, the rest a sector joined to it ({Doc.Sectors.Count} sectors, "
                + $"{MapErrors()} map errors; status \"{_status.Text}\")");
            UiDo("undo");
            Check(Doc.Sectors.Count == 4, $"and undone ({Doc.Sectors.Count} sectors)");

            // Vertices mode: a right click on empty space starts drawing there; three more clicks close a sector
            UiDo("verticesmode");
            await Move(new(3072, 0));
            await Press(MouseButton.Right, new(3072, 0), true);
            await Press(MouseButton.Right, new(3072, 0), false);
            Check(Editing.Mode is DrawGeometryMode, $"right click on empty space: drawing ({Editing.Mode?.Name})");
            await Click(MouseButton.Left, new(3584, 0));
            await Click(MouseButton.Left, new(3584, 512));
            await Click(MouseButton.Left, new(3072, 0));
            Check(Doc.Sectors.Count == 5, $"auto-drawn triangle: a sector ({Doc.Sectors.Count} sectors; status \"{_status.Text}\")");

            // an extension: a line from the triangle's east wall out and back onto it (right click finishes): a new sector
            // against that stretch of wall, joined by a red wall (zoomed in: the snap range is pixels)
            float oldZoom = View.Zoom;
            View.Zoom = 0.5f;
            View.CentreOn(new Vector2(3712, 256));
            await Frames(1);
            UiDo("drawlinesmode");
            await Click(MouseButton.Left, new(3584, 128));
            await Click(MouseButton.Left, new(3840, 128));
            await Click(MouseButton.Left, new(3840, 384));
            await Click(MouseButton.Left, new(3584, 384));
            await Click(MouseButton.Right, new(3584, 384));
            bool extended = Doc.Sectors.Count == 6 && Enumerable.Range(Doc.Sectors[5].WallPtr, Doc.Sectors[5].WallNum)
                .Any(w => Doc.Walls[w].NextSector == 4);
            Check(extended && MapErrors() == 0, $"line out of a room and back: an extension ({Doc.Sectors.Count} sectors, "
                + $"{MapErrors()} map errors; status \"{_status.Text}\")");

            // Vertices mode: over a wall's middle (no point there) the place for one shows; a right-drag from it puts a
            // point in and drags it out (the wall split); selected and deleted, the walls join again
            UiDo("verticesmode");
            int walls5 = Doc.Sectors[5].WallNum, undos = Undo.UndoCount;
            await Move(new(3840, 256));
            await Press(MouseButton.Right, new(3840, 256), true);
            await Move(new(3900, 256));
            await Move(new(3968, 256));
            await Press(MouseButton.Right, new(3968, 256), false);
            bool split = Doc.Sectors[5].WallNum == walls5 + 1 && Doc.Points.Any(p => p.X == 3968 && p.Y == 256);
            Check(split && MapErrors() == 0 && Undo.UndoCount == undos + 1, $"right-drag from a wall: a point put in and dragged, one undo step "
                + $"({Doc.Sectors[5].WallNum - walls5} walls more, {Undo.UndoCount - undos} undo steps, {MapErrors()} map errors; status \"{_status.Text}\")");
            await Click(MouseButton.Left, new(3968, 256));
            UiDo("deleteitem");
            Check(Doc.Sectors[5].WallNum == walls5 && !Doc.Points.Any(p => p.X == 3968 && p.Y == 256) && MapErrors() == 0,
                $"the selected point deleted, its walls joined ({Doc.Sectors[5].WallNum} walls; status \"{_status.Text}\")");
            // Walls mode: Delete on a white wall collapses it to its middle (the square a triangle)
            UiDo("linedefsmode");
            await Move(new(3840, 300));
            UiDo("deleteitem");
            Check(Doc.Sectors[5].WallNum == walls5 - 1 && Doc.Points.Any(p => p.X == 3840 && p.Y == 256) && MapErrors() == 0,
                $"a white wall deleted: collapsed to its middle ({Doc.Sectors[5].WallNum} walls, {MapErrors()} map errors; status \"{_status.Text}\")");
            // the view modes (UDB's): floors filled, the next one (ceilings), back to lines only
            UiDo("viewmodefloors");
            await Frames(1);
            bool floors = View.Mode == Chosen.Forge.View.ViewMode.Floors;
            UiDo("nextviewmode");
            await Frames(1);
            bool ceilings = View.Mode == Chosen.Forge.View.ViewMode.Ceilings;
            UiDo("viewmodenormal");
            Check(floors && ceilings && View.Mode == Chosen.Forge.View.ViewMode.Wireframe, $"view modes: floors, the next (ceilings), wireframe ({View.Mode})");
            // Full Brightness (UDB's Toggle Full Brightness, B): both views, the menu checked, and back
            bool wasBright = View.FullBright;
            UiDo("togglebrightness");
            await Frames(1);
            var fbItem = _menuItems["togglebrightness"];
            bool brightOn = View.FullBright != wasBright && _preview.FullBright == View.FullBright
                && fbItem.menu.IsItemChecked(fbItem.menu.GetItemIndex(fbItem.id)) == View.FullBright;
            UiDo("togglebrightness");
            await Frames(1);
            Check(brightOn && View.FullBright == wasBright && _preview.FullBright == wasBright
                && fbItem.menu.IsItemChecked(fbItem.menu.GetItemIndex(fbItem.id)) == wasBright,
                $"full brightness: 2D and 3D toggled together, the menu checked, and back ({View.FullBright}; status \"{_status.Text}\")");
            View.Zoom = oldZoom;
            View.CentreOn(Vector2.Zero);
            await Frames(1);

            // Sprites mode: right-press on empty space places a sprite of the browser's type, the drag moves it
            SpriteType = 1;
            EditNewSprite = false;
            UiDo("thingsmode");
            await Move(new(-1024, 1024));
            await Press(MouseButton.Right, new(-1024, 1024), true);
            await Move(new(-1000, 1100));
            await Move(new(-960, 1152));
            await Press(MouseButton.Right, new(-960, 1152), false);
            var sp = Doc.Sprites.Count == 1 ? Doc.Sprites[0] : null;
            Check(sp != null && sp.X == -960 && sp.Y == 1152 && sp.Type == 1 && sp.Picnum == 2522 && sp.SectNum >= 0,
                sp == null ? $"sprite placed ({Doc.Sprites.Count} sprites)"
                    : $"sprite placed: type {sp.Type} tile {sp.Picnum} at {sp.X},{sp.Y} in sector {sp.SectNum}");
            // Insert places another at the cursor
            await Move(new(-1536, 1536));
            UiDo("insertitem");
            Check(Doc.Sprites.Count == 2 && Doc.Sprites[1].X == -1536 && Doc.Sprites[1].Y == 1536,
                $"Insert: a sprite at the cursor ({Doc.Sprites.Count} sprites, the new one at {Doc.Sprites[^1].X},{Doc.Sprites[^1].Y})");
            // the browser's Decorations: a picture Blood's maps lay flat on floors, placed with its usual size and lie
            // (Blood.TileUse), undone
            int decoTile = Blood.TileUse.All.Where(kv => kv.Value.IsDecoration && kv.Value.DecoFloor > kv.Value.Deco
                && kv.Value.DecoFloor >= kv.Value.DecoWall && kv.Value.XRepeat > 0 && kv.Value.XRepeat != 64 && Data.TileImages.Exists(kv.Key))
                .Select(kv => kv.Key).DefaultIfEmpty(-1).First();
            bool decoOk = false;
            string decoSays = "no flat decoration found in data/tileuse.txt";
            if (decoTile >= 0)
            {
                var du = Blood.TileUse.Of(decoTile);
                SpriteType = 0;
                SpriteTile = decoTile;
                await Move(new(-1280, 1280));
                UiDo("insertitem");
                var ds0 = Doc.Sprites.Count == 3 ? Doc.Sprites[2] : null;
                decoOk = ds0 != null && ds0.Type == 0 && ds0.Picnum == decoTile && ds0.XRepeat == du.XRepeat && ds0.YRepeat == du.YRepeat
                    && (ds0.Cstat & 48) == 32;
                decoSays = ds0 == null ? $"{Doc.Sprites.Count} sprites" : $"tile {ds0.Picnum} size {ds0.XRepeat} x {ds0.YRepeat} (Blood's {du.XRepeat} x {du.YRepeat}), cstat {ds0.Cstat}";
                if (Doc.Sprites.Count == 3)
                    UiDo("undo");
                SpriteTile = -1;
                SpriteType = 1;
            }
            Check(decoOk && Doc.Sprites.Count == 2, $"a decoration from the sprite browser: {decoSays}, flat on the floor; undone");

            // the Gradient tool's command over three sectors: their floors in equal steps from the first's to the
            // last's (the middle one by where it lies), undone
            if (Doc.Sectors.Count >= 3)
            {
                var gBefore = Enumerable.Range(0, 3).Select(i => Doc.Sectors[i].FloorZ).ToList();
                UiDo("sectorgradient floor 0 -2048 1 1 0 0 1 2");
                var gAfter = Enumerable.Range(0, 3).Select(i => Doc.Sectors[i].FloorZ).ToList();
                UiDo("undo");
                bool stepped = gAfter[0] == 0 && gAfter[2] == -2048 && gAfter.OrderBy(z => z).SequenceEqual(new[] { -2048, -1024, 0 });
                Check(stepped && Enumerable.Range(0, 3).Select(i => Doc.Sectors[i].FloorZ).SequenceEqual(gBefore),
                    $"the Gradient tool: sectors 0, 1, 2's floors {string.Join(", ", gAfter)} (from {string.Join(", ", gBefore)}); undone");
            }

            // the tile picker on a wall's tab: its pictures made as they come into view; the Size slider at 128 makes them
            // again at that size (every item in view its own picture), closed
            Ui.TilePickerDialog.Show(this, View, 275, _ => { }, kind: "wall");
            await Frames(2);
            var pdl = LastDialog;
            var plist = pdl?.FindChildren("*", nameof(ItemList), true, false).OfType<ItemList>().FirstOrDefault();
            var pslider = pdl?.FindChildren("*", nameof(HSlider), true, false).OfType<HSlider>().FirstOrDefault();
            string pickSays = "no picker";
            bool pickOk = false;
            if (plist != null && pslider != null)
            {
                pslider.Value = 128;
                await Frames(12);
                float top = (float)plist.GetVScrollBar().Value, bottom = top + plist.Size.Y;
                int inView = 0, shown = 0;
                for (int i = 0; i < plist.ItemCount; i++)
                {
                    var rc = plist.GetItemRect(i);
                    if (rc.End.Y < top || rc.Position.Y > bottom)
                        continue;
                    inView++;
                    int t = (int)plist.GetItemMetadata(i);
                    if (plist.GetItemIcon(i) is { } ic && ic == Data.TileImages.Thumb(t, 128))
                        shown++;
                }
                // the selected tile (275) kept in view through the change, about the middle
                var selItems = plist.GetSelectedItems();
                var src = selItems.Length > 0 ? plist.GetItemRect(selItems[0]) : new Rect2();
                float mid = src.GetCenter().Y - top;
                bool focused = selItems.Length > 0 && (int)plist.GetItemMetadata(selItems[0]) == 275 && mid > plist.Size.Y * 0.25f && mid < plist.Size.Y * 0.75f;
                pickOk = plist.FixedIconSize == new Vector2I(128, 128) && inView > 4 && shown == inView && focused;
                pickSays = $"{plist.ItemCount} wall tiles, icons {plist.FixedIconSize.X} px, {shown} of {inView} in view with their picture; "
                    + $"the selected tile {(selItems.Length > 0 ? (int)plist.GetItemMetadata(selItems[0]) : -1)} at {mid:0} of {plist.Size.Y:0} px down (dialog {pdl.Size}, min {pdl.MinSize}, max {pdl.MaxSize}, list {plist.Size})";
                pdl.Hide();
                pdl.QueueFree();
                await Frames(1);
            }
            Check(pickOk, $"the tile picker's pictures: {pickSays}");

            // heights typed in game units, up positive: "++1" raises each floor by one game unit (256 Build z), "2.5" sets it
            bool oldUnits = Units.Game;
            Units.Game = true;
            Undo.CreateUndo("floors");
            int z0 = Doc.Sectors[0].FloorZ, z1 = Doc.Sectors[1].FloorZ;
            int Typed(string text)
            {
                var form = new Ui.PropertyForm(new List<int> { 0, 1 });
                var grid = new GridContainer();
                form.Z(grid, "Height", i => Doc.Sectors[i].FloorZ, (i, v) => Doc.Sectors[i].FloorZ = v).Text = text;
                form.Apply();
                grid.Free();
                return form.Changes;
            }
            Typed("++1");
            bool raised = Doc.Sectors[0].FloorZ == z0 - 256 && Doc.Sectors[1].FloorZ == z1 - 256;
            Typed("2.5");
            bool set = Doc.Sectors[0].FloorZ == -640 && Doc.Sectors[1].FloorZ == -640;
            Doc.NotifyChanged();
            // as a dialog's result would be recorded
            Rec?.Document(Doc, "undo", "floors");
            UndoChanged();
            Check(raised && set && Units.Z(-640) == "2.5" && Units.Xy(18944) == "1184" && Units.Xy(8) == "0.5",
                $"game units: ++1 raised the floors {(raised ? "by 256 Build z" : "WRONG")}, 2.5 set them to z {Doc.Sectors[0].FloorZ}; "
                + $"18944 shows as {Units.Xy(18944)}");
            Units.Game = oldUnits;

            // a properties dialog as a user fills it: a right click on a sector in Sectors mode, its floor typed, Apply
            UiDo("sectorsmode");
            var inside = new Vector2(-1536, 1800);
            int ds = Doc.SectorsAt(inside.X, inside.Y).FirstOrDefault(-1);
            await Move(inside);
            await Press(MouseButton.Right, inside, true);
            await Press(MouseButton.Right, inside, false);
            var dlg = LastDialog;
            bool opened = dlg != null && IsInstanceValid(dlg) && dlg.Visible;
            int palItems = 0;
            string palText = "";
            if (opened)
            {
                Testing.DialogDriver.Set(dlg, "Floor", "Height", "8");
                // the palette picker beside the floor's palette: pal 3 double-clicked
                var palField = (LineEdit)Testing.DialogDriver.Field(dlg, "Floor", "Palette");
                if (palField.GetParent().GetChildren().OfType<Button>().FirstOrDefault() is { } pick)
                {
                    pick.EmitSignal(BaseButton.SignalName.Pressed);
                    await Frames(1);
                    if (LastDialog != dlg && LastDialog?.FindChildren("*", nameof(ItemList), true, false).FirstOrDefault() is ItemList pals)
                    {
                        palItems = pals.ItemCount;
                        pals.EmitSignal(ItemList.SignalName.ItemActivated, 3);
                        await Frames(1);
                    }
                }
                palText = palField.Text;
                Testing.DialogDriver.Ok(dlg);
                await Frames(2);
            }
            Check(opened && ds >= 0 && Doc.Sectors[ds].FloorZ == -2048,
                $"sector {ds}'s dialog: floor typed 8, now z {(ds >= 0 ? Doc.Sectors[ds].FloorZ : 0)} (status \"{_status.Text}\")");
            Check(palItems >= 15 && palText == "3" && ds >= 0 && Doc.Sectors[ds].FloorPal == 3,
                $"the palette picker: {palItems} palettes, pal 3 picked into the field (\"{palText}\"), floor pal {(ds >= 0 ? Doc.Sectors[ds].FloorPal : -1)}");

            // R turns what the mouse is on: sector ds turned a quarter about its middle by the mouse going round it, a
            // click keeps it (one undo level: every point a quarter turn round the middle), undone
            if (ds >= 0)
            {
                UiDo("clearselection");
                await Move(inside);
                var (bx0, by0, bx1, by1) = Chosen.Shared.Engine.Geometry.BuildGeom.SectorBounds(Doc, ds);
                var placesR = Enumerable.Range(Doc.Sectors[ds].WallPtr, Doc.Sectors[ds].WallNum).Select(wl => (Doc.Walls[wl].X, Doc.Walls[wl].Y)).ToList();
                int undosR = Undo.UndoCount;
                SendKey(Godot.Key.R, true);
                SendKey(Godot.Key.R, false);
                await Frames(1);
                bool turning = (Editing.Mode as ClassicMode)?.Rotating == true;
                // (the middle as the mode takes it: the bounds of what turns, here the sector alone or with rooms in it)
                var mid = new Vector2((float)Math.Round((bx0 + bx1) / 2.0), (float)Math.Round((by0 + by1) / 2.0));
                var arm = inside - mid;
                await Move(mid + new Vector2(-arm.Y, arm.X) * 0.7f + arm * 0.3f);
                await Move(mid + new Vector2(-arm.Y, arm.X));
                await Click(MouseButton.Left, mid + new Vector2(-arm.Y, arm.X));
                var afterR = Enumerable.Range(Doc.Sectors[ds].WallPtr, Doc.Sectors[ds].WallNum).Select(wl => (Doc.Walls[wl].X, Doc.Walls[wl].Y)).ToList();
                // a quarter turn (Build's: clockwise as the view shows it, y down): (x, y) about the middle to (-y, x)
                bool quarter = placesR.Zip(afterR).All(pq => Math.Abs(pq.Second.X - (mid.X - (pq.First.Y - mid.Y))) <= 1
                    && Math.Abs(pq.Second.Y - (mid.Y + (pq.First.X - mid.X))) <= 1);
                bool oneStep = Undo.UndoCount == undosR + 1;
                UiDo("undo");
                bool back = Enumerable.Range(Doc.Sectors[ds].WallPtr, Doc.Sectors[ds].WallNum).Select(wl => (Doc.Walls[wl].X, Doc.Walls[wl].Y)).SequenceEqual(placesR);
                // the same with the sector selected and the mouse on empty space (nothing highlighted): Esc puts it back
                UiDo("clearselection");
                UiDo($"select sector {ds}");
                await Move(new Vector2(-6000, -6000));
                SendKey(Godot.Key.R, true);
                SendKey(Godot.Key.R, false);
                await Frames(1);
                bool selTurning = (Editing.Mode as ClassicMode)?.Rotating == true;
                await Move(new Vector2(-6000, 6000));
                SendKey(Godot.Key.Escape, true);
                SendKey(Godot.Key.Escape, false);
                await Frames(1);
                bool selBack = Enumerable.Range(Doc.Sectors[ds].WallPtr, Doc.Sectors[ds].WallNum).Select(wl => (Doc.Walls[wl].X, Doc.Walls[wl].Y)).SequenceEqual(placesR)
                    && Undo.UndoCount == undosR && (Editing.Mode as ClassicMode)?.Rotating == false;
                UiDo("clearselection");
                Check(selTurning && selBack, $"R on a selected sector with the mouse on nothing: turning {selTurning}, Esc put it back {selBack}");

                // Shift+S on the selected sector, its right side dragged past the left as far again: mirrored across the
                // left side (x to 2 left - x), its loops turned round (no map errors), one undo step, undone
                {
                    UiDo($"select sector {ds}");
                    int undosS = Undo.UndoCount;
                    var pointsS = Enumerable.Range(Doc.Sectors[ds].WallPtr, Doc.Sectors[ds].WallNum).Select(wl => (Doc.Walls[wl].X, Doc.Walls[wl].Y)).ToHashSet();
                    UiDo("scaleselection");
                    await Frames(1);
                    var handle = new Vector2((float)bx1, (float)Math.Round((by0 + by1) / 2.0));
                    await Move(handle);
                    await Press(MouseButton.Left, handle, true);
                    var past = new Vector2((float)(2 * bx0 - bx1), handle.Y);
                    await Move((handle + past) / 2);
                    await Move(past);
                    await Press(MouseButton.Left, past, false);
                    SendKey(Godot.Key.Enter, true);
                    SendKey(Godot.Key.Enter, false);
                    await Frames(1);
                    var mirrored = Enumerable.Range(Doc.Sectors[ds].WallPtr, Doc.Sectors[ds].WallNum).Select(wl => (Doc.Walls[wl].X, Doc.Walls[wl].Y)).ToHashSet();
                    bool across = mirrored.SetEquals(pointsS.Select(p => ((int)(2 * bx0 - p.X), p.Y)));
                    // (each loop of it: one outer loop running clockwise, the rest the other way, as Build needs)
                    var loopsS = Chosen.Shared.Engine.Geometry.BuildGeom.Loops(Doc, ds).ToList();
                    int errorsS = MapErrors() + (loopsS.Count(l => Chosen.Shared.Engine.Geometry.BuildGeom.LoopIsOuter(Doc, l)) == 1 ? 0 : 1);
                    bool oneS = Undo.UndoCount == undosS + 1;
                    UiDo("undo");
                    bool backS = Enumerable.Range(Doc.Sectors[ds].WallPtr, Doc.Sectors[ds].WallNum).Select(wl => (Doc.Walls[wl].X, Doc.Walls[wl].Y)).ToHashSet().SetEquals(pointsS);
                    UiDo("clearselection");
                    Check(across && errorsS == 0 && oneS && backS, $"Shift+S dragged through itself mirrors sector {ds}: across its left side {across}, "
                        + $"{errorsS} map or winding errors (its loops turned round), one undo step {oneS}, undone {backS} (status \"{_status.Text}\")");
                }

                // Walls mode (sector ds's walls selected) and Vertices mode (its points selected): a quarter turn about their
                // middle with the mouse on nothing, Enter keeps it, undone
                foreach (var (mode, kind) in new[] { ("linedefsmode", Chosen.Forge.View.ElementKind.Line), ("verticesmode", Chosen.Forge.View.ElementKind.Point) })
                {
                    UiDo(mode);
                    UiDo("clearselection");
                    var walls = Enumerable.Range(Doc.Sectors[ds].WallPtr, Doc.Sectors[ds].WallNum).ToList();
                    bool lines = kind == Chosen.Forge.View.ElementKind.Line;
                    UiDo($"select {(lines ? "line" : "point")} {string.Join(",", walls.Select(wl => lines ? Doc.LineOf[wl] : Doc.PointOf[wl]))}");
                    int undosW = Undo.UndoCount;
                    var start = mid + new Vector2(0, -8000);
                    await Move(start);
                    SendKey(Godot.Key.R, true);
                    SendKey(Godot.Key.R, false);
                    await Frames(1);
                    await Move(mid + new Vector2(5600, -5600));
                    await Move(mid + new Vector2(8000, 0));
                    SendKey(Godot.Key.Enter, true);
                    SendKey(Godot.Key.Enter, false);
                    await Frames(1);
                    var turned = walls.Select(wl => (Doc.Walls[wl].X, Doc.Walls[wl].Y)).ToList();
                    bool q = placesR.Zip(turned).All(pq => Math.Abs(pq.Second.X - (mid.X - (pq.First.Y - mid.Y))) <= 1
                        && Math.Abs(pq.Second.Y - (mid.Y + (pq.First.X - mid.X))) <= 1);
                    bool one = Undo.UndoCount == undosW + 1;
                    UiDo("undo");
                    UiDo("clearselection");
                    bool undone = walls.Select(wl => (Doc.Walls[wl].X, Doc.Walls[wl].Y)).SequenceEqual(placesR);
                    Check(q && one && undone, $"R in {mode} on sector {ds}'s {walls.Count} {(kind == Chosen.Forge.View.ElementKind.Line ? "walls" : "vertices")}: "
                        + $"a quarter turn {q} (status \"{_status.Text}\"), one undo level {one}, undone {undone}");
                }
                UiDo("sectorsmode");

                // the drawing tools, in empty space east of the map: a circle (16 sides) and a 4 x 4 grid (16 sectors, one
                // undo level); the grid aligned to a turned wall and a rectangle drawn along it; each undone
                {
                    int undosD = Undo.UndoCount, sectorsD = Doc.Sectors.Count;
                    // (the view over the empty space east of the map: a click outside the view reaches nothing)
                    var (keepCentre, keepZoom) = (View.Centre, View.Zoom);
                    View.Zoom = 0.04f;
                    View.CentreOn(new Vector2(11000, 8000));
                    await Frames(1);
                    UiDo("drawcirclemode");
                    await Click(MouseButton.Left, new Vector2(6144, 6144));
                    await Click(MouseButton.Left, new Vector2(8192, 8192));
                    bool circle = Doc.Sectors.Count == sectorsD + 1 && Doc.Sectors[^1].WallNum == 16 && Undo.UndoCount == undosD + 1;
                    var cw = Doc.Walls[Doc.Sectors[^1].WallPtr];
                    UiDo("undo");
                    // a circle with Alt: square (a box 2048 x 1024 clicked, 2048 x 2048 drawn)
                    UiDo("drawcirclemode");
                    await Click(MouseButton.Left, new Vector2(6144, 6144));
                    await Click(MouseButton.Left, new Vector2(8192, 7168), "a");
                    var (ax0, ay0, ax1, ay1) = Chosen.Shared.Engine.Geometry.BuildGeom.SectorBounds(Doc, Doc.Sectors.Count - 1);
                    bool round = Doc.Sectors.Count == sectorsD + 1 && Math.Abs((ax1 - ax0) - (ay1 - ay0)) <= 2 && ay1 - ay0 > 2000;
                    UiDo("undo");
                    // a rectangle with Shift+Alt: a square round its first click (that corner 1024 x 512 off it)
                    UiDo("drawrectanglemode");
                    await Click(MouseButton.Left, new Vector2(7168, 7168));
                    await Click(MouseButton.Left, new Vector2(8192, 7680), "sa");
                    var (qx0, qy0, qx1, qy1) = Chosen.Shared.Engine.Geometry.BuildGeom.SectorBounds(Doc, Doc.Sectors.Count - 1);
                    bool centred = Doc.Sectors.Count == sectorsD + 1 && (qx0, qy0, qx1, qy1) == (6144, 6144, 8192, 8192);
                    UiDo("undo");
                    // a grid with W: a row more (4 x 5), S takes it back
                    UiDo("drawgridmode");
                    SendKey(Godot.Key.W, true);
                    SendKey(Godot.Key.W, false);
                    await Frames(1);
                    await Click(MouseButton.Left, new Vector2(10240, 6144));
                    await Click(MouseButton.Left, new Vector2(12288, 8192));
                    int cells = Doc.Sectors.Count - sectorsD;
                    bool grid = cells == 20 && Undo.UndoCount == undosD + 1 && MapErrors() == 0
                        && Doc.Sectors.Skip(sectorsD).All(sc => sc.WallNum == 4);
                    UiDo("drawgridmode");
                    SendKey(Godot.Key.S, true);
                    SendKey(Godot.Key.S, false);
                    await Frames(1);
                    SendKey(Godot.Key.Escape, true);
                    SendKey(Godot.Key.Escape, false);
                    await Frames(1);
                    // the grid along one of the cells' walls turned 30 degrees: a wall drawn at that angle first
                    UiDo("undo");
                    UiDo("drawlinesmode");
                    var t0 = new Vector2(14336, 6144);
                    var t1 = t0 + new Vector2(Mathf.Cos(Mathf.Pi / 6), Mathf.Sin(Mathf.Pi / 6)) * 2048;
                    var t2 = t0 + new Vector2(-Mathf.Sin(Mathf.Pi / 6), Mathf.Cos(Mathf.Pi / 6)) * 2048;
                    UiDo("snapgrid off");
                    await Click(MouseButton.Left, t0);
                    await Click(MouseButton.Left, t1);
                    await Click(MouseButton.Left, t2);
                    await Click(MouseButton.Left, t0);
                    UiDo("snapgrid on");
                    int tri = Doc.Sectors.Count - 1;
                    string triStatus = _status.Text;
                    int edge = Enumerable.Range(Doc.Sectors[tri].WallPtr, Doc.Sectors[tri].WallNum).FirstOrDefault(wl => Math.Abs(Doc.Walls[wl].X - (int)Math.Round(t0.X)) <= 1
                        && Math.Abs(Doc.Walls[wl].Y - (int)Math.Round(t0.Y)) <= 1, -1);
                    if (edge < 0)
                        GD.Print($"uitest: the turned triangle: {Doc.Sectors.Count - sectorsD} new sectors, the last {Doc.Sectors[tri].WallNum} walls from "
                            + $"({Doc.Walls[Doc.Sectors[tri].WallPtr].X}, {Doc.Walls[Doc.Sectors[tri].WallPtr].Y}); status \"{triStatus}\"");
                    edge = Math.Max(edge, Doc.Sectors[tri].WallPtr);
                    UiDo($"aligngrid line {Doc.LineOf[edge]}");
                    float ang = View.GridAngle;
                    UiDo("drawrectanglemode");
                    var r0 = View.FromGrid(new Vector2(0, 2560));
                    var r1 = View.FromGrid(new Vector2(1024, 3584));
                    await Click(MouseButton.Left, r0);
                    await Click(MouseButton.Left, r1);
                    var rs = Doc.Sectors[^1];
                    var (qa, qb) = (Doc.Walls[rs.WallPtr], Doc.Walls[rs.WallPtr + 1]);
                    float side = Mathf.Atan2(qb.Y - qa.Y, qb.X - qa.X);
                    bool along = rs.WallNum == 4 && Math.Abs(Mathf.Wrap(side - ang, -Mathf.Pi / 2, Mathf.Pi / 2)) < 0.02f;
                    UiDo("aligngrid reset");
                    UiDo("undo");
                    UiDo("undo");
                    bool reset = View.GridAngle == 0 && View.GridOrigin == Vector2.Zero && Doc.Sectors.Count == sectorsD && Undo.UndoCount == undosD;
                    UiDo("sectorsmode");
                    View.Zoom = keepZoom;
                    View.CentreOn(keepCentre);
                    await Frames(1);
                    Check(circle && round && centred && grid && along && reset, $"drawing: a circle of 16 sides {circle}, Alt: round {round} "
                        + $"({ax1 - ax0} x {ay1 - ay0}); a rectangle with Shift+Alt a square round its first click {centred} ({qx0}, {qy0}, {qx1}, {qy1}); "
                        + $"a grid (W: 4 x 5) of {cells} cells in one undo level {grid}; "
                        + $"the grid along a wall turned {Mathf.RadToDeg(ang):0.#}°, a rectangle along it {along}; undone, the grid back {reset}");
                }

                // Ctrl+C on sector ds (and what lies in it), Ctrl+V, a click in empty space east of the map: the same shape,
                // floor, walls and sprites there, the map's rules no worse, one undo level, undone
                {
                    UiDo("sectorsmode");
                    UiDo($"select sector {ds}");
                    int undosC = Undo.UndoCount, sectorsC = Doc.Sectors.Count, spritesC = Doc.Sprites.Count, errorsC = MapErrors();
                    void Ctrl(Godot.Key k)
                    {
                        Input.ParseInputEvent(new InputEventKey { Keycode = k, PhysicalKeycode = k, Pressed = true, CtrlPressed = true });
                        Input.ParseInputEvent(new InputEventKey { Keycode = k, PhysicalKeycode = k, Pressed = false, CtrlPressed = true });
                    }
                    await Move(inside);
                    Ctrl(Godot.Key.C);
                    await Frames(1);
                    string copied = _status.Text;
                    int inDs = Doc.Sprites.Count(sp => sp.SectNum == ds);
                    var (keepC, keepZ) = (View.Centre, View.Zoom);
                    View.Zoom = 0.04f;
                    View.CentreOn(new Vector2(11000, 8000));
                    await Frames(1);
                    Ctrl(Godot.Key.V);
                    await Frames(1);
                    bool placing = Editing.Mode is PasteMode;
                    var to = new Vector2(10240, 6144);
                    await Move(to + new Vector2(300, 0));
                    await Click(MouseButton.Left, to);
                    int made = Doc.Sectors.Count - sectorsC;
                    int ns = sectorsC;
                    bool same = made >= 1 && Doc.Sectors[ns].WallNum == Doc.Sectors[ds].WallNum && Doc.Sectors[ns].FloorZ == Doc.Sectors[ds].FloorZ
                        && Doc.Sectors[ns].FloorPicnum == Doc.Sectors[ds].FloorPicnum && Doc.Sprites.Count(sp => sp.SectNum == ns) == inDs
                        && MapErrors() <= errorsC && Undo.UndoCount == undosC + 1;
                    var (cx0, cy0, _, _) = Chosen.Shared.Engine.Geometry.BuildGeom.SectorBounds(Doc, ns);
                    var (ox0, oy0, _, _) = Chosen.Shared.Engine.Geometry.BuildGeom.SectorBounds(Doc, ds);
                    UiDo("undo");
                    UiDo("clearselection");
                    View.Zoom = keepZ;
                    View.CentreOn(keepC);
                    await Frames(1);
                    bool backC = Doc.Sectors.Count == sectorsC && Doc.Sprites.Count == spritesC && Undo.UndoCount == undosC;
                    Check(placing && same && backC, $"copy and paste sector {ds} ({copied}): placing {placing}, {made} sector(s) made, the same shape, floor and "
                        + $"{inDs} sprites {same}, moved by {cx0 - ox0}, {cy0 - oy0}; undone {backC} (status \"{_status.Text}\")");
                }

                // R with its pivot moved (P): sector ds turned a quarter about a point east of it, Enter keeps it, undone
                {
                    UiDo("clearselection");
                    await Move(inside);
                    int undosP = Undo.UndoCount;
                    SendKey(Godot.Key.R, true);
                    SendKey(Godot.Key.R, false);
                    await Frames(1);
                    var pivot = new Vector2(2048, 1024);
                    await Move(pivot);
                    SendKey(Godot.Key.P, true);
                    SendKey(Godot.Key.P, false);
                    await Frames(1);
                    await Move(pivot + new Vector2(2048, 0));
                    await Move(pivot + new Vector2(1448, 1448));
                    await Move(pivot + new Vector2(0, 2048));
                    SendKey(Godot.Key.Enter, true);
                    SendKey(Godot.Key.Enter, false);
                    await Frames(1);
                    var turnedP = Enumerable.Range(Doc.Sectors[ds].WallPtr, Doc.Sectors[ds].WallNum).Select(wl => (Doc.Walls[wl].X, Doc.Walls[wl].Y)).ToList();
                    bool aboutPivot = placesR.Zip(turnedP).All(pq => Math.Abs(pq.Second.X - (pivot.X - (pq.First.Y - pivot.Y))) <= 1
                        && Math.Abs(pq.Second.Y - (pivot.Y + (pq.First.X - pivot.X))) <= 1) && Undo.UndoCount == undosP + 1;
                    UiDo("undo");
                    Check(aboutPivot, $"R with P: sector {ds} a quarter turn about ({pivot.X}, {pivot.Y}) {aboutPivot} (status \"{_status.Text}\"), undone");
                }

                // Shift+S on sector ds: its box's far corner dragged to twice the size (evenly from the near corner), Enter
                // keeps it, undone
                {
                    UiDo("clearselection");
                    await Move(inside);
                    int undosS = Undo.UndoCount;
                    Input.ParseInputEvent(new InputEventKey { Keycode = Godot.Key.S, PhysicalKeycode = Godot.Key.S, Pressed = true, ShiftPressed = true });
                    Input.ParseInputEvent(new InputEventKey { Keycode = Godot.Key.S, PhysicalKeycode = Godot.Key.S, Pressed = false, ShiftPressed = true });
                    await Frames(1);
                    bool scaling = (Editing.Mode as ClassicMode)?.Scaling == true;
                    var (sx0, sy0, sx1, sy1) = Chosen.Shared.Engine.Geometry.BuildGeom.SectorBounds(Doc, ds);
                    var far = new Vector2(sx1, sy1);
                    var twice = new Vector2(sx0 + 2 * (sx1 - sx0), sy0 + 2 * (sy1 - sy0));
                    await Move(far);
                    await Press(MouseButton.Left, far, true);
                    await Move((far + twice) / 2);
                    await Move(twice);
                    await Press(MouseButton.Left, twice, false);
                    SendKey(Godot.Key.Enter, true);
                    SendKey(Godot.Key.Enter, false);
                    await Frames(1);
                    var (nx0, ny0, nx1, ny1) = Chosen.Shared.Engine.Geometry.BuildGeom.SectorBounds(Doc, ds);
                    bool doubled = scaling && nx0 == sx0 && ny0 == sy0 && nx1 - nx0 == 2 * (sx1 - sx0) && ny1 - ny0 == 2 * (sy1 - sy0) && Undo.UndoCount == undosS + 1;
                    UiDo("undo");
                    Check(doubled, $"Shift+S on sector {ds}: {sx1 - sx0} x {sy1 - sy0} dragged by its corner to {nx1 - nx0} x {ny1 - ny0} from the same corner "
                        + $"{doubled} (status \"{_status.Text}\"), undone");
                }

                // G with the falloff (O): one of sector ds's points moved, its neighbours (joined by walls, within the
                // radius) moved part of the way, the far ones not; Esc puts all back
                {
                    UiDo("verticesmode");
                    UiDo("clearselection");
                    int pf = Doc.PointOf[Doc.Sectors[ds].WallPtr];
                    var pp = new Vector2(Doc.Points[pf].X, Doc.Points[pf].Y);
                    var beforeF = Enumerable.Range(0, Doc.Points.Count).Select(q => (Doc.Points[q].X, Doc.Points[q].Y)).ToList();
                    await Move(pp);
                    SendKey(Godot.Key.G, true);
                    SendKey(Godot.Key.G, false);
                    await Frames(1);
                    SendKey(Godot.Key.O, true);
                    SendKey(Godot.Key.O, false);
                    await Frames(1);
                    bool fall = (Editing.Mode as ClassicMode)?.Falloff == true;
                    // the radius up by the wheel past the sector's neighbouring corners (1024 x 1.25^7: about 4900)
                    for (int k = 0; k < 7; k++)
                    {
                        var wg = Screen(pp);
                        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.WheelUp, Pressed = true, Position = wg, GlobalPosition = wg });
                        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.WheelUp, Pressed = false, Position = wg, GlobalPosition = wg });
                        await Frames(1);
                    }
                    // Shift+wheel: the next curve, back again
                    var curves = new List<string> { (Editing.Mode as ClassicMode)?.FalloffCurve };
                    foreach (var wb in new[] { MouseButton.WheelUp, MouseButton.WheelDown })
                    {
                        var wg = Screen(pp);
                        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = wb, Pressed = true, Position = wg, GlobalPosition = wg, ShiftPressed = true });
                        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = wb, Pressed = false, Position = wg, GlobalPosition = wg, ShiftPressed = true });
                        await Frames(1);
                        curves.Add((Editing.Mode as ClassicMode)?.FalloffCurve);
                    }
                    bool curveWheel = curves[0] != curves[1] && curves[0] == curves[2];
                    await Move(pp + new Vector2(0, -256));
                    await Move(pp + new Vector2(0, -512));
                    var now = Enumerable.Range(0, Doc.Points.Count).Select(q => (Doc.Points[q].X, Doc.Points[q].Y)).ToList();
                    int full = 0, part = 0;
                    for (int q = 0; q < now.Count; q++)
                    {
                        int dyq = now[q].Y - beforeF[q].Y;
                        if (q == pf)
                            full += dyq == -512 && now[q].X == beforeF[q].X ? 1 : 0;
                        else if (dyq < 0 && dyq > -512)
                            part++;
                    }
                    SendKey(Godot.Key.Escape, true);
                    SendKey(Godot.Key.Escape, false);
                    await Frames(1);
                    bool backF = Enumerable.Range(0, Doc.Points.Count).Select(q => (Doc.Points[q].X, Doc.Points[q].Y)).SequenceEqual(beforeF);
                    UiDo("sectorsmode");
                    Check(fall && full == 1 && part > 0 && backF && curveWheel, $"G with falloff: Shift+wheel the curve {string.Join(" > ", curves)} {curveWheel}; "
                        + $"the point moved 512 up {full == 1}, {part} joined points part of the way; "
                        + $"Esc put them back {backF} (status \"{_status.Text}\")");
                }

                // G moves what the mouse is on: X locks it to the grid's X axis (the mouse's Y ignored), on the grid; a click
                // keeps it (one undo level), undone; then Y locked and a right click puts it back
                {
                    UiDo("clearselection");
                    await Move(inside);
                    int undosG = Undo.UndoCount;
                    SendKey(Godot.Key.G, true);
                    SendKey(Godot.Key.G, false);
                    await Frames(1);
                    bool moving = (Editing.Mode as ClassicMode)?.Moving == true;
                    SendKey(Godot.Key.X, true);
                    SendKey(Godot.Key.X, false);
                    await Frames(1);
                    // (the mouse 3.4 grid steps across and 2.6 down: on X alone, snapped: 3 steps across)
                    var by = new Vector2(View.GridSize * 3.4f, View.GridSize * 2.6f);
                    await Move(inside + by * 0.5f);
                    await Move(inside + by);
                    await Click(MouseButton.Left, inside + by);
                    var movedX = Enumerable.Range(Doc.Sectors[ds].WallPtr, Doc.Sectors[ds].WallNum).Select(wl => (Doc.Walls[wl].X, Doc.Walls[wl].Y)).ToList();
                    int dx = movedX[0].X - placesR[0].X, dy = movedX[0].Y - placesR[0].Y;
                    bool alongX = placesR.Zip(movedX).All(pq => pq.Second.X - pq.First.X == dx && pq.Second.Y == pq.First.Y)
                        && dx == 3 * View.GridSize && Undo.UndoCount == undosG + 1;
                    UiDo("undo");
                    bool undoneG = Enumerable.Range(Doc.Sectors[ds].WallPtr, Doc.Sectors[ds].WallNum).Select(wl => (Doc.Walls[wl].X, Doc.Walls[wl].Y)).SequenceEqual(placesR);
                    Check(moving && alongX && undoneG, $"G on sector {ds}, X locked: moving {moving}, moved {dx}, {dy} (3 grid steps across, none down: {alongX}; "
                        + $"status \"{_status.Text}\"), undone {undoneG}");
                    await Move(inside);
                    SendKey(Godot.Key.G, true);
                    SendKey(Godot.Key.G, false);
                    await Frames(1);
                    SendKey(Godot.Key.Y, true);
                    SendKey(Godot.Key.Y, false);
                    await Frames(1);
                    await Move(inside + by);
                    bool alongY = Doc.Walls[Doc.Sectors[ds].WallPtr].X == placesR[0].X && Doc.Walls[Doc.Sectors[ds].WallPtr].Y == placesR[0].Y + 3 * View.GridSize;
                    await Press(MouseButton.Right, inside + by, true);
                    await Press(MouseButton.Right, inside + by, false);
                    bool putBack = Enumerable.Range(Doc.Sectors[ds].WallPtr, Doc.Sectors[ds].WallNum).Select(wl => (Doc.Walls[wl].X, Doc.Walls[wl].Y)).SequenceEqual(placesR)
                        && Undo.UndoCount == undosG && (Editing.Mode as ClassicMode)?.Moving == false;
                    Check(alongY && putBack, $"G on sector {ds}, Y locked: 3 grid steps down only {alongY}; a right click put it back {putBack}");
                }
                Check(turning && quarter && oneStep && back, $"R on sector {ds}: turning {turning}, a quarter turn about its middle {quarter} "
                    + $"(status \"{_status.Text}\"), one undo level {oneStep}, undone {back}");
            }

            // layers: a new one (current); a rectangle drawn over a Level sector there is a sector of its own over it
            // (the Level sector locked: not cut, no room made in it); Level hidden: its sectors aren't picked
            int below = Doc.SectorsAt(-1000, 500).FirstOrDefault(-1), belowWalls = below >= 0 ? Doc.Sectors[below].WallNum : 0;
            int sectorsBefore = Doc.Sectors.Count;
            UiDo("layernew");
            UiDo("drawrectanglemode");
            await Click(MouseButton.Left, new(-1536, 0));
            await Click(MouseButton.Left, new(-512, 1024));
            bool over = Doc.Sectors.Count == sectorsBefore + 1 && Doc.LayerOf(Doc.Sectors.Count - 1) == 1 && Doc.LayerOf(below) == 0
                && Doc.Sectors[below].WallNum == belowWalls;
            Check(over, $"layers: a rectangle in a new layer over a Level sector is its own sector ({Doc.Sectors.Count - sectorsBefore} new, "
                + $"in layer {Doc.LayerOf(Doc.Sectors.Count - 1)}; the one below {belowWalls} walls, now {Doc.Sectors[below].WallNum}; status \"{_status.Text}\")");
            UiDo("layershow 0 0");
            UiDo("sectorsmode");
            await Move(new(-1800, 1800));
            bool unpicked = (Editing.Mode as ClassicMode)?.Highlighted.IsNone == true;
            UiDo("layershow 0 1");
            UiDo("layercurrent 0");
            await Move(new(-1800, 1800));
            bool picked = (Editing.Mode as ClassicMode)?.Highlighted.IsNone == false;
            Check(unpicked && picked, $"layers: a hidden layer's sector isn't picked ({unpicked}), shown and current again it is ({picked})");
            // a selected sprite moved into the other layer (<): no longer picked in Level; a sector moved takes the
            // sprite in it with it; both undone
            UiDo("thingsmode");
            await Click(MouseButton.Left, new(-1536, 1536));
            UiDo("layermove 1");
            await Move(new(-1536, 1536));
            bool spriteMoved = Doc.SpriteLayer(1) == 1 && (Editing.Mode as ClassicMode)?.Highlighted.IsNone == true;
            int home = Doc.Sprites[0].SectNum;
            UiDo("sectorsmode");
            var label = Doc.Labels[home];
            await Click(MouseButton.Left, label);
            UiDo("layermove 1");
            bool withSprite = Doc.LayerOf(home) == 1 && Doc.SpriteLayer(0) == 1;
            UiDo("undo");
            UiDo("undo");
            Check(spriteMoved && withSprite && Doc.SpriteLayer(0) == 0 && Doc.SpriteLayer(1) == 0 && Doc.LayerOf(home) == 0,
                $"layers: a sprite moved alone ({spriteMoved}), a sector moved with its sprite ({withSprite}), both undone (status \"{_status.Text}\")");
            // in Lights mode the selected light moved into the other layer (<): into that layer's room there; undone
            {
                int lightsBefore = Doc.Lights.Count, top = sectorsBefore;
                Chosen.Shared.Engine.Geometry.BuildGeom.GetZsOfSlope(Doc, below, -1024, 512, out double lc, out double lf);
                UiDo("lightsmode");
                UiDo($"lightadd -1024 512 {(int)((lc + lf) / 2)}");
                int nl = Doc.Lights.Count - 1;
                bool inLevel = Doc.Lights.Count == lightsBefore + 1 && Doc.LightSector(nl) == below;
                UiDo("layermove 1");
                bool inTop = Doc.LightSector(nl) == top && Doc.LayerOf(top) == 1 && !Doc.LightEditable(nl);
                string moveStatus = _status.Text;
                UiDo("undo");
                bool undone = Doc.Lights.Count == lightsBefore + 1 && Doc.LightSector(nl) == below;
                UiDo("undo");
                UiDo("sectorsmode");
                Check(inLevel && inTop && undone && Doc.Lights.Count == lightsBefore,
                    $"layers: a light placed in Level's {below} ({inLevel}) moved to layer 1's {top} ({inTop}, status \"{moveStatus}\"), undone ({undone})");
            }
            // Atmosphere mode: a sector clicked (selected, tinted once it has one), given a new atmosphere (the guesses made
            // the map's own first), Delete gives it back the map's own; both undone (no atmospheres of its own again)
            {
                bool hadOwn = Doc.HasAtmosphere;
                int atmoUndos = Undo.UndoCount;
                UiDo("atmospheremode");
                bool tinting = View.SectorTint != null;
                await Click(MouseButton.Left, Doc.Labels[below]);
                bool atmoPicked = SelectedSectors().Contains(below);
                UiDo($"atmonew 0 {below}");
                int made = Doc.AtmosphereOf(below);
                bool given = Doc.HasAtmosphere && made > 0 && Doc.Atmospheres[made].Name.StartsWith("Atmosphere");
                SendKey(Godot.Key.Delete, true);
                SendKey(Godot.Key.Delete, false);
                await Frames(1);
                bool back = Doc.AtmosphereOf(below) == 0 && Doc.Sectors.Count > below;
                UiDo("undo");
                UiDo("undo");
                bool undone = Doc.HasAtmosphere == hadOwn && Undo.UndoCount == atmoUndos;
                UiDo("sectorsmode");
                // (the sector selection is Sectors mode's too: left selected, the next click took it off)
                UiDo("clearselection");
                bool untinted = View.SectorTint == null;
                Check(tinting && atmoPicked && given && back && undone && untinted,
                    $"atmosphere: sector {below} picked ({atmoPicked}, tinted {tinting}), given atmosphere {made} ({given}), Delete: the map's own ({back}), "
                    + $"both undone ({undone}), the tint gone in Sectors mode ({untinted})");
            }

            // Q: the 3D preview (built from the map: meshes and sprites), Q again: back to 2D
            await Move(new Vector2(-1024, 1024));
            SendKey(Godot.Key.Q, true);
            SendKey(Godot.Key.Q, false);
            await Frames(2);
            bool in3d = _preview.Visible && !View.Visible && _preview.MeshCount > 0;
            int meshes = _preview.MeshCount;
            SendKey(Godot.Key.Q, true);
            SendKey(Godot.Key.Q, false);
            await Frames(2);
            Check(in3d && !_preview.Visible && View.Visible, $"Q: the 3D preview ({meshes} meshes and sprites) and back to 2D");

            // the selections in step (View > Sync Selection): a sector selected in 2D is its floor and ceiling in the
            // preview; the preview's clicks (their commands) select in 2D: both taken off, the sector no longer selected
            UiDo("sectorsmode");
            int ss = Doc.SectorsAt(-1024, 1024).FirstOrDefault(-1);
            await Click(MouseButton.Left, Doc.Labels[ss]);
            SendKey(Godot.Key.Q, true);
            SendKey(Godot.Key.Q, false);
            await Frames(2);
            bool to3d = _preview.Selected.Contains(new Chosen.Forge.View.VisualTarget(Chosen.Forge.View.VisualPart.Floor, ss, -1))
                && _preview.Selected.Contains(new Chosen.Forge.View.VisualTarget(Chosen.Forge.View.VisualPart.Ceiling, ss, -1));
            UiDo($"visualselect c{ss}");
            bool still = SelectionOf(Chosen.Forge.View.ElementKind.Sector).Contains(ss);
            UiDo($"visualselect f{ss}");
            bool gone = !SelectionOf(Chosen.Forge.View.ElementKind.Sector).Contains(ss);
            SendKey(Godot.Key.Q, true);
            SendKey(Godot.Key.Q, false);
            await Frames(2);
            Check(to3d && still && gone && View.Visible,
                $"sync selection: sector {ss} selected in 2D is its floor and ceiling in 3D ({to3d}); its ceiling taken off, still selected in 2D ({still}); its floor too, no longer ({gone})");

            // the preview's menu (its command): a floor's picture changed; the wheel on a sprite: up; both undone
            int zs0 = Doc.SectorsAt(-1024, 1024).FirstOrDefault(-1);
            if (zs0 >= 0 && Doc.Sprites.Count > 0)
            {
                int pic = Doc.Sectors[zs0].FloorPicnum, sz = Doc.Sprites[0].Z;
                UiDo($"visualtile 7 f{zs0}");
                bool tiled = Doc.Sectors[zs0].FloorPicnum == 7;
                UiDo("visualz -1024 s0");
                bool lifted = Doc.Sprites[0].Z == sz - 1024;
                UiDo("undo");
                UiDo("undo");
                Check(tiled && lifted && Doc.Sectors[zs0].FloorPicnum == pic && Doc.Sprites[0].Z == sz,
                    $"the preview's menu: sector {zs0}'s floor picture 7 ({tiled}); the wheel on sprite 0 lifts it ({lifted}); both undone");
            }

            // Ctrl+C in the preview (its command): the floor's picture copied for Ctrl+V
            if (zs0 >= 0)
            {
                UiDo($"visualcopy f{zs0}");
                Check(TextureClip == Doc.Sectors[zs0].FloorPicnum, $"Ctrl+C in the preview: sector {zs0}'s floor picture copied ({TextureClip})");

                // Ctrl+C / Ctrl+V take a surface's settings with its picture: a wall's shade, palette and panning put on
                // another wall (its x repeat scaled to its length), the floor given the picture alone; undone
                int cw0 = Doc.Sectors[zs0].WallPtr, cw1 = Doc.Walls[cw0].Point2;
                var cp0 = Chosen.Forge.View.Preview3D.PartsOf(Doc, cw0).First();
                var cp1 = Chosen.Forge.View.Preview3D.PartsOf(Doc, cw1).First();
                var l0 = Chosen.Forge.Tools.TextureOps.Look(Doc, cp0);
                int undosC = Undo.UndoCount;
                UiDo($"visualtex set shade=9,pal=4,panx=24,repx=16 {Chosen.Forge.View.Preview3D.Code(cp0)}");
                UiDo($"visualcopy {Chosen.Forge.View.Preview3D.Code(cp0)}");
                PasteTexture(new List<Chosen.Forge.View.VisualTarget> { cp1, new(Chosen.Forge.View.VisualPart.Floor, zs0, -1) });
                var l1 = Chosen.Forge.Tools.TextureOps.Look(Doc, cp1);
                double WallLen(int wl)
                {
                    var (x1, y1, x2, y2) = Chosen.Shared.Engine.Geometry.BuildGeom.WallEnds(Doc, wl);
                    return Math.Sqrt((double)(x2 - x1) * (x2 - x1) + (double)(y2 - y1) * (y2 - y1));
                }
                double len0 = WallLen(cw0), len1 = WallLen(cw1);
                int wantRx = Math.Clamp((int)Math.Round(16 * len1 / Math.Round(len0)), 1, 255);
                int gotRx = Doc.Walls[cw1].XRepeat, gotShade = l1.Shade, gotPal = l1.Pal, gotPan = l1.XPanning;
                bool settings = gotShade == 9 && gotPal == 4 && gotPan == 24 && gotRx == wantRx;
                bool floorPic = Doc.Sectors[zs0].FloorPicnum == l0.Picnum && Doc.Sectors[zs0].FloorShade != 9;
                while (Undo.UndoCount > undosC)
                    UiDo("undo");
                Check(settings && floorPic, $"Ctrl+V with the settings: wall {cw1} shade {gotShade}, pal {gotPal}, panning {gotPan}, x repeat "
                    + $"{gotRx} (want {wantRx} for its length); the floor the picture alone {floorPic}");

                // Texture Settings over a wall and a floor: a tab each, the floor's shade typed in its tab goes onto the
                // floor alone
                int fShade0 = Doc.Sectors[zs0].FloorShade, wShade0 = Chosen.Forge.Tools.TextureOps.Look(Doc, cp0).Shade;
                TextureSettings(new List<Chosen.Forge.View.VisualTarget> { cp0, new(Chosen.Forge.View.VisualPart.Floor, zs0, -1) });
                await Frames(1);
                var tdlg = LastDialog;
                int tabCount = tdlg?.FindChildren("*", nameof(TabContainer), true, false).OfType<TabContainer>().FirstOrDefault()?.GetTabCount() ?? 0;
                bool tabbed = false, scrolls = false;
                bool hadXs = Doc.Sectors[zs0].Xs != null;
                if (tabCount == 2)
                {
                    string ftab = "Floors (1)";
                    Testing.DialogDriver.Set(tdlg, ftab, "Shade (darker up)", "11");
                    // Blood's scrolling in the floor's tab: its x-sector made, speed and Pan floor set
                    Testing.DialogDriver.Set(tdlg, ftab, "Scroll speed", "30");
                    // (Texture Settings' check boxes have their label beside them in the grid)
                    if (tdlg.FindChildren("*", nameof(Label), true, false).OfType<Label>().FirstOrDefault(l => l.Text == "Floor scrolls") is { } fl
                        && fl.GetParent().GetChild(fl.GetIndex() + 1) is CheckBox fc)
                        fc.ButtonPressed = true;
                    Testing.DialogDriver.Ok(tdlg);
                    await Frames(2);
                    var xs = Doc.Sectors[zs0].Xs;
                    scrolls = xs != null && xs.PanVel == 30 && xs.PanFloor == 1;
                    tabbed = Doc.Sectors[zs0].FloorShade == 11 && Chosen.Forge.Tools.TextureOps.Look(Doc, cp0).Shade == wShade0;
                    if (Doc.Sectors[zs0].FloorShade != fShade0)
                        UiDo("undo");
                }
                Check(tabbed && scrolls && Doc.Sectors[zs0].FloorShade == fShade0 && (Doc.Sectors[zs0].Xs != null) == hadXs,
                    $"Texture Settings over a wall and a floor: {tabCount} tabs, the Floors tab's shade on the floor alone {tabbed}, its scrolling set {scrolls} (x-sector {(hadXs ? "had one" : "made")}), undone");

                // two walls with different pictures: one form (no tabs), the wall picture saying Multiple; the shade
                // typed goes onto both, the pictures left as they were
                int tw1p = Chosen.Forge.Tools.TextureOps.Look(Doc, cp1).Picnum;
                UiDo($"visualtile {tw1p + 1} {Chosen.Forge.View.Preview3D.Code(cp1)}");
                TextureSettings(new List<Chosen.Forge.View.VisualTarget> { cp0, cp1 });
                await Frames(2);
                var pdlg = LastDialog;
                int picTabs = pdlg?.FindChildren("*", nameof(TabContainer), true, false).OfType<TabContainer>().FirstOrDefault()?.GetTabCount() ?? 0;
                string picText = "";
                bool both = false;
                if (pdlg != null)
                {
                    picText = (Testing.DialogDriver.Field(pdlg, null, "Wall picture") as SpinBox)?.GetLineEdit().Text ?? "";
                    Testing.DialogDriver.Set(pdlg, null, "Shade (darker up)", "5");
                    Testing.DialogDriver.Ok(pdlg);
                    await Frames(2);
                    var la = Chosen.Forge.Tools.TextureOps.Look(Doc, cp0);
                    var lb = Chosen.Forge.Tools.TextureOps.Look(Doc, cp1);
                    both = la.Shade == 5 && lb.Shade == 5 && lb.Picnum == tw1p + 1 && la.Picnum != lb.Picnum;
                    UiDo("undo");
                }
                UiDo("undo");
                Check(picTabs == 0 && picText == "Multiple" && both && Chosen.Forge.Tools.TextureOps.Look(Doc, cp1).Picnum == tw1p,
                    $"Texture Settings over two walls of different pictures: {picTabs} tabs, the picture says \"{picText}\", the shade on both and the pictures kept {both}");

                // the context menu's Properties... over a floor: recorded ("properties sector n"), so the replay at the
                // end opens the dialog the change is made in; its floor height typed, undone
                int fz0 = Doc.Sectors[zs0].FloorZ;
                var propsTool = Chosen.Forge.Tools.ContextTools.All.First(t => t.Name == "Properties...");
                propsTool.Run(new Chosen.Forge.Tools.ToolContext { Window = this, In3D = true,
                    Targets = new List<Chosen.Forge.View.VisualTarget> { new(Chosen.Forge.View.VisualPart.Floor, zs0, -1) } });
                await Frames(2);
                var sdlg = LastDialog;
                string sTitle = sdlg != null && IsInstanceValid(sdlg) && sdlg.Visible ? sdlg.Title : "none";
                bool sOpen = sTitle == $"Sector {zs0}";
                int fz1 = fz0;
                if (sOpen)
                {
                    // (closed by its window's X, not a button: a settings dialog applies as it closes; its one button Close)
                    sOpen &= sdlg is ConfirmationDialog { OkButtonText: "Close" } cdl && !cdl.GetCancelButton().Visible;
                    Testing.DialogDriver.Set(sdlg, "Floor", "Height", Units.Z(fz0 - 1024));
                    sdlg.Hide();
                    sdlg.EmitSignal(AcceptDialog.SignalName.Canceled);
                    await Frames(2);
                    fz1 = Doc.Sectors[zs0].FloorZ;
                    if (fz1 != fz0)
                        UiDo("undo");
                }
                // two red walls selected, their openings set at once from Texture Settings' Masked wall rows (their
                // command, as the dialog writes it for a selection): masked with a picture, translucent, blocking, both
                // sides of each (the other side's picture the same, x-flipped against this side's); a copy of the masked
                // opening leaves its settings out (they stay with the wall); undone
                var reds = Enumerable.Range(0, Doc.Walls.Count).Where(i => Doc.Walls[i].NextWall >= 0 && Doc.Walls[i].Sector >= 0).ToList();
                int rw = reds.FirstOrDefault(-1);
                int rw2 = rw < 0 ? -1 : reds.FirstOrDefault(i => i != rw && i != Doc.Walls[rw].NextWall, -1);
                bool maskSet = false;
                string maskSays = $"red walls {rw}, {rw2}";
                if (rw >= 0 && rw2 >= 0)
                {
                    int undosM = Undo.UndoCount;
                    var up = new Chosen.Forge.View.VisualTarget(Chosen.Forge.View.VisualPart.Upper, Doc.Walls[rw].Sector, rw);
                    var low = new Chosen.Forge.View.VisualTarget(Chosen.Forge.View.VisualPart.Lower, Doc.Walls[rw2].Sector, rw2);
                    UiDo($"visualtex set w.tileover=300,w.middle=1,w.seethrough=1,w.block=1 {Chosen.Forge.View.Preview3D.Code(up)} {Chosen.Forge.View.Preview3D.Code(low)}");
                    bool Masked(int wi)
                    {
                        var mw = Doc.Walls[wi];
                        var mo = Doc.Walls[mw.NextWall];
                        return (mw.Cstat & (16 | 128 | 1)) == (16 | 128 | 1) && (mo.Cstat & (16 | 128 | 1)) == (16 | 128 | 1)
                            && mw.OverPicnum == 300 && mo.OverPicnum == 300 && ((mw.Cstat ^ mo.Cstat) & 8) != 0 && (mw.Cstat & 32) == 0;
                    }
                    var mclip = Chosen.Forge.Tools.TextureOps.Clip(Doc, new Chosen.Forge.View.VisualTarget(Chosen.Forge.View.VisualPart.Middle, Doc.Walls[rw].Sector, rw));
                    bool clipPlain = Chosen.Forge.Tools.TextureOps.MaskKeys.All(k => !mclip.ContainsKey(k)) && mclip.GetValueOrDefault("kind") == 0;
                    // auto-align from the first wall's masked picture: along the masked openings (the second wall's), the
                    // status saying the walls' own pictures beside the openings move with them
                    bool maskedBoth = Masked(rw) && Masked(rw2);
                    var midA = new Chosen.Forge.View.VisualTarget(Chosen.Forge.View.VisualPart.Middle, Doc.Walls[rw].Sector, rw);
                    UiDo($"visualtex align xy {Chosen.Forge.View.Preview3D.Code(midA)}");
                    string alignSays = _status.Text;
                    bool alignedMasks = alignSays.Contains("masked opening(s) aligned") && alignSays.Contains("Blood keeps one set");
                    maskSet = maskedBoth && clipPlain && alignedMasks;
                    maskSays = $"auto-align: \"{alignSays}\"; ";
                    maskSays += $"wall {rw}: cstat {Doc.Walls[rw].Cstat} / other side {Doc.Walls[Doc.Walls[rw].NextWall].Cstat}; wall {rw2}: cstat "
                        + $"{Doc.Walls[rw2].Cstat} / {Doc.Walls[Doc.Walls[rw2].NextWall].Cstat}; masked pictures {Doc.Walls[rw].OverPicnum}, {Doc.Walls[rw2].OverPicnum}; "
                        + $"a copy of the opening without its settings {clipPlain}";
                    while (Undo.UndoCount > undosM)
                        UiDo("undo");
                }
                Check(maskSet && rw >= 0 && (Doc.Walls[rw].Cstat & 16) == 0 && (Doc.Walls[rw2].Cstat & 16) == 0,
                    $"two red walls masked at once from Texture Settings: {maskSays}; undone");

                // in the 3D preview: a one-way opening is aimed at from its side, the crosshair on it; undone
                if (rw >= 0)
                {
                    int undosA = Undo.UndoCount;
                    var up = new Chosen.Forge.View.VisualTarget(Chosen.Forge.View.VisualPart.Upper, Doc.Walls[rw].Sector, rw);
                    UiDo($"visualtex set w.tileover=300,w.middle=2 {Chosen.Forge.View.Preview3D.Code(up)}");
                    SendKey(Godot.Key.Q, true);
                    SendKey(Godot.Key.Q, false);
                    await Frames(2);
                    // the camera 768 units in front of the wall's middle on its sector's side, at the opening's middle
                    // height, facing it
                    var (x1, y1, x2, y2) = Chosen.Shared.Engine.Geometry.BuildGeom.WallEnds(Doc, rw);
                    double mx = (x1 + x2) / 2.0, my = (y1 + y2) / 2.0, len = Math.Sqrt((x2 - x1) * (double)(x2 - x1) + (y2 - y1) * (double)(y2 - y1));
                    double cx = mx - (y2 - y1) / len * 768, cy = my + (x2 - x1) / len * 768;
                    Chosen.Shared.Engine.Geometry.BuildGeom.GetZsOfSlope(Doc, Doc.Walls[rw].Sector, mx, my, out double c0, out double f0);
                    Chosen.Shared.Engine.Geometry.BuildGeom.GetZsOfSlope(Doc, Doc.Walls[rw].NextSector, mx, my, out double c1, out double f1);
                    double z = (Math.Max(c0, c1) + Math.Min(f0, f1)) / 2;
                    float ang = (float)(Math.Atan2(my - cy, mx - cx) * 1024 / Math.PI);
                    _preview.SetCamera(Chosen.Forge.View.PreviewMesher.P(cx, cy, z), Chosen.Forge.View.Preview3D.YawOf(ang), 0);
                    await Frames(3);
                    var onIt = _preview.Target;
                    bool aimedOneWay = onIt is { Part: Chosen.Forge.View.VisualPart.Middle } ot && ot.Wall == rw;
                    while (Undo.UndoCount > undosA)
                        UiDo("undo");
                    SendKey(Godot.Key.Q, true);
                    SendKey(Godot.Key.Q, false);
                    await Frames(2);
                    Check(aimedOneWay && (Doc.Walls[rw].Cstat & 32) == 0,
                        $"a one-way opening aimed at: the crosshair on {(onIt is { } a1 ? Chosen.Forge.View.Preview3D.Code(a1) : "nothing")} (wall {rw}'s); undone");
                }

                Check(sOpen && fz1 == fz0 - 1024 && Doc.Sectors[zs0].FloorZ == fz0,
                    $"the context menu's Properties... over floor {zs0}: dialog \"{sTitle}\", the floor raised to {fz1} (from {fz0}), undone (status \"{_status.Text}\")");
            }

            // the preview's texture keys (their commands): a wall panned twice (one undo level), its x repeat raised, its
            // shade set; the next wall auto-aligned to it (x: its panning carries on where the first ends); a dialog's
            // fields tried on a copy (the map itself untouched); all undone
            if (zs0 >= 0)
            {
                int tw0 = Doc.Sectors[zs0].WallPtr, tw1 = Doc.Walls[tw0].Point2;
                var part0 = Chosen.Forge.View.Preview3D.PartsOf(Doc, tw0).First();
                string code = Chosen.Forge.View.Preview3D.Code(part0);
                var look = Chosen.Forge.Tools.TextureOps.Look(Doc, part0);
                int undos1 = Undo.UndoCount, px0 = look.XPanning, rx0 = Doc.Walls[tw0].XRepeat;
                int sign = (Doc.Walls[tw0].Cstat & 8) != 0 ? -1 : 1;
                UiDo($"visualtex pan 1 0 {code}");
                UiDo($"visualtex pan 1 0 {code}");
                bool panned = Chosen.Forge.Tools.TextureOps.Look(Doc, part0).XPanning == ((px0 + 2 * sign) % 256 + 256) % 256 && Undo.UndoCount == undos1 + 1;
                UiDo($"visualtex repeat 1 0 {code}");
                bool repeated = Doc.Walls[tw0].XRepeat == Math.Min(255, rx0 + 1);
                UiDo($"visualtex set shade=5 {code}");
                bool shaded = Chosen.Forge.Tools.TextureOps.Look(Doc, part0).Shade == 5;
                // the next wall given the first's picture, then aligned
                var part1 = Chosen.Forge.View.Preview3D.PartsOf(Doc, tw1).First();
                UiDo($"visualtile {Chosen.Forge.Tools.TextureOps.Tile(Doc, part0)} {Chosen.Forge.View.Preview3D.Code(part1)}");
                UiDo($"visualtex align x {code}");
                var w0r = Doc.Walls[tw0];
                int width = Math.Max(1, Chosen.Forge.View.PreviewMesher.TileSize(Chosen.Forge.Tools.TextureOps.Tile(Doc, part0)).X);
                int expect = (((Chosen.Forge.Tools.TextureOps.Look(Doc, part0).XPanning + ((w0r.Cstat & 8) != 0 ? -Doc.Walls[tw1].XRepeat * 8 : w0r.XRepeat * 8)) % width) + width) % width % 256;
                bool aligned = Chosen.Forge.Tools.TextureOps.Look(Doc, part1).XPanning == expect;
                bool dirty = Doc.Dirty;
                var tried = Doc.TryOn(() => Doc.Walls[tw0].Shade = 20);
                bool tryOn = tried != null && tried.Walls[tw0].Shade == 20 && Doc.Walls[tw0].Shade != 20 && Doc.Dirty == dirty;
                int texLevels = Undo.UndoCount - undos1;
                for (int i = 0; i < texLevels; i++)
                    UiDo("undo");
                Check(panned && repeated && shaded && aligned && tryOn && Undo.UndoCount == undos1 && Chosen.Forge.Tools.TextureOps.Look(Doc, part0).XPanning == px0,
                    $"texture keys: panned twice as one undo level ({panned}), x repeat ({repeated}), shade set ({shaded}); the next wall auto-aligned ({aligned}); a dialog tried on a copy ({tryOn}); {texLevels} levels undone");
            }

            // the context menu's wall tools (their commands): a wall split at its middle; two walls side by side curved
            // each on its own, then as one (their corner moved onto the curve); each one undo step, the map rules kept
            if (zs0 >= 0)
            {
                int w0 = Doc.Sectors[zs0].WallPtr, w1 = Doc.Walls[w0].Point2;
                int l0 = Doc.LineOf[w0], l1 = Doc.LineOf[w1];
                int Sides(int l) => Doc.Lines[l].Back >= 0 ? 2 : 1;
                int walls = Doc.Walls.Count, errs = MapErrors(), undos0 = Undo.UndoCount;
                var corner = new Vector2I(Doc.Walls[w1].X, Doc.Walls[w1].Y);
                UiDo($"splitlines {l0}");
                bool split1 = Doc.Walls.Count == walls + Sides(l0) && Undo.UndoCount == undos0 + 1;
                UiDo("undo");
                UiDo($"curvelines 4 arc 64 0 0 {l0} {l1}");
                bool each = Doc.Walls.Count == walls + 4 * (Sides(l0) + Sides(l1)) && MapErrors() <= errs && Undo.UndoCount == undos0 + 1;
                UiDo("undo");
                UiDo($"curvelines 3 parabola 64 1 1 {l0} {l1}");
                bool cornerMoved = !Doc.Walls.Any(x => x.X == corner.X && x.Y == corner.Y);
                // (3 points along the run, in the wall each falls in: one or two walls each)
                bool one = Doc.Walls.Count >= walls + 3 && Doc.Walls.Count <= walls + 6;
                bool oneOk = one && cornerMoved && MapErrors() <= errs && Undo.UndoCount == undos0 + 1;
                UiDo("undo");
                Check(split1 && each && oneOk && Doc.Walls.Count == walls && Undo.UndoCount == undos0,
                    $"wall tools: split ({split1}), two walls curved each on its own ({each}), as one with their corner moved ({oneOk}), all undone (status \"{_status.Text}\")");
            }

            // Snap to Grid (its command): a sprite to the nearest crossing of a 128 grid, a sector's corners to a 48 grid;
            // each one undo step, undone
            if (Doc.Sprites.Count > 0 && zs0 >= 0)
            {
                int undosG = Undo.UndoCount, sx = Doc.Sprites[0].X, sy = Doc.Sprites[0].Y;
                UiDo("snapgrid 128 sprite 0");
                int ex = (int)(Math.Round(sx / 128.0, MidpointRounding.AwayFromZero) * 128), ey = (int)(Math.Round(sy / 128.0, MidpointRounding.AwayFromZero) * 128);
                bool spriteSnapped = Doc.Sprites[0].X == ex && Doc.Sprites[0].Y == ey && Undo.UndoCount == undosG + (ex != sx || ey != sy ? 1 : 0);
                UiDo("undo");
                int undosS = Undo.UndoCount;
                UiDo($"snapgrid 48 sector {zs0}");
                var sec = Doc.Sectors[zs0];
                bool cornersSnapped = Enumerable.Range(sec.WallPtr, sec.WallNum).All(w => Doc.Walls[w].X % 48 == 0 && Doc.Walls[w].Y % 48 == 0)
                    && Undo.UndoCount == undosS + 1;
                UiDo("undo");
                Check(spriteSnapped && cornersSnapped && Doc.Sprites[0].X == sx && Undo.UndoCount == undosG,
                    $"snap to grid: sprite 0 to {ex}, {ey} ({spriteSnapped}); sector {zs0}'s corners to a 48 grid ({cornersSnapped}); undone (status \"{_status.Text}\")");
            }

            // the preview's arrows on a sprite (their command): moved twice across the map as one undo level; the shade
            // keys stop at black (63)
            if (Doc.Sprites.Count > 0)
            {
                int undosM = Undo.UndoCount, mx = Doc.Sprites[0].X, sh = Doc.Sprites[0].Shade;
                UiDo("visualmove 64 0 s0");
                UiDo("visualmove 64 0 s0");
                bool moved2 = Doc.Sprites[0].X == mx + 128 && Undo.UndoCount == undosM + 1;
                UiDo("visualtex set shade=60 s0");
                UiDo("visualtex shade 8 s0");
                bool stopped = Doc.Sprites[0].Shade == 63;
                UiDo("undo");
                UiDo("undo");
                UiDo("undo");
                Check(moved2 && stopped && Doc.Sprites[0].X == mx && Doc.Sprites[0].Shade == sh && Undo.UndoCount == undosM,
                    $"arrows on a sprite: moved 128 as one undo level ({moved2}); shade 60 + 8 stops at 63 ({stopped}); undone");

                // a held arrow (the key's repeats) goes on nudging the sprite selected in the preview, into the same undo
                // level; a held Space (or any other key) doesn't repeat
                UiDo("visualselect s0");
                var (hx, hy) = (Doc.Sprites[0].X, Doc.Sprites[0].Y);
                var held = new InputEventKey { Keycode = Key.Right, PhysicalKeycode = Key.Right, Pressed = true };
                PreviewKey(held);
                held.Echo = true;
                PreviewKey(held);
                PreviewKey(held);
                bool repeated = Math.Abs(Doc.Sprites[0].X - hx) + Math.Abs(Doc.Sprites[0].Y - hy) == 3 * View.GridSize
                    && Undo.UndoCount == undosM + 1;
                bool spaceHeld = !PreviewKey(new InputEventKey { Keycode = Key.Space, PhysicalKeycode = Key.Space, Pressed = true, Echo = true });
                UiDo("undo");
                UiDo("clearselection");
                Check(repeated && spaceHeld && Doc.Sprites[0].X == hx && Doc.Sprites[0].Y == hy && Undo.UndoCount == undosM,
                    $"a held arrow: the sprite nudged three times as one undo level ({repeated}); a held Space ignored ({spaceHeld}); undone");
            }

            // placing in the preview (its command): a player start on a floor (standing on it), one on a wall (wall
            // aligned, turned as given); each one undo step, undone
            if (zs0 >= 0)
            {
                int undosP = Undo.UndoCount, count = Doc.Sprites.Count;
                var (px0, py0, px1, py1) = Chosen.Shared.Engine.Geometry.BuildGeom.SectorBounds(Doc, zs0);
                int cxp = (px0 + px1) / 2, cyp = (py0 + py1) / 2;
                UiDo($"visualplace 1 floor {cxp} {cyp} 0 {zs0} 512");
                var placed = Doc.Sprites[^1];
                // (centred, Blood's bottom on the floor: where it settles in the game)
                bool onFloor = Doc.Sprites.Count == count + 1 && Chosen.Forge.Blood.SpriteExtents.Of(placed).bottom == Doc.Sectors[zs0].FloorZ
                    && placed.Ang == 512 && (placed.Cstat & 48) == 0 && (placed.Cstat & 128) != 0;
                UiDo($"visualplace 1 wall {cxp} {cyp} {Doc.Sectors[zs0].FloorZ - 4096} {zs0} 1024");
                var decal = Doc.Sprites[^1];
                bool onWall = Doc.Sprites.Count == count + 2 && (decal.Cstat & 48) == 16 && decal.Ang == 1024
                    && decal.Z <= Doc.Sectors[zs0].FloorZ && decal.Z > Doc.Sectors[zs0].CeilingZ;
                bool levels2 = Undo.UndoCount == undosP + 2;
                // the wall sprite along its wall and up (each sprite its own move), then both deleted (one step)
                int di = Doc.Sprites.Count - 1, dz0 = decal.Z, dy0 = decal.Y;
                UiDo($"visualmove 0,16,-256:s{di}");
                bool slid = Doc.Sprites[di].Y == dy0 + 16 && Doc.Sprites[di].Z == dz0 - 256;
                UiDo($"visualdelete s{di - 1} s{di}");
                bool deleted = Doc.Sprites.Count == count;
                UiDo("undo");
                UiDo("undo");
                UiDo("undo");
                UiDo("undo");
                Check(onFloor && onWall && levels2 && slid && deleted && Doc.Sprites.Count == count && Undo.UndoCount == undosP,
                    $"placing in the preview: on the floor ({onFloor}), on a wall wall aligned ({onWall}), one undo step each ({levels2}); the wall one moved along and up ({slid}); both deleted ({deleted}); undone");
            }

            // the preview's wheel (its command): a floor raised two notches in Edit (one undo level); in Off a sector that
            // doesn't move kept; one undo takes both notches back
            int zs = Doc.SectorsAt(-1024, 1024).FirstOrDefault(-1);
            int fz = zs >= 0 ? Doc.Sectors[zs].FloorZ : 0;
            int levels = Undo.UndoCount;
            UiDo($"visualz -1024 f{zs}");
            UiDo($"visualz -1024 f{zs}");
            bool wheelUp = zs >= 0 && Doc.Sectors[zs].FloorZ == fz - 2048 && Undo.UndoCount == levels + 1;
            UiDo("showoff");
            UiDo($"visualz -1024 f{zs}");
            bool kept = zs >= 0 && Doc.Sectors[zs].FloorZ == fz - 2048;
            UiDo("showedit");
            UiDo("undo");
            Check(wheelUp && kept && zs >= 0 && Doc.Sectors[zs].FloorZ == fz,
                $"the preview's wheel: sector {zs}'s floor raised two notches in Edit as one undo level ({wheelUp}), kept in Off ({kept}), undone at once (status \"{_status.Text}\")");

            // slope mode in the 3D preview: H on, the camera over a floor looking down at it: a handle under the
            // crosshair, a click locks onto it; Shift+click's first wall; the far edge's wheel tilts about it, the first
            // wall's raises its side; undone; H off
            {
                SendKey(Godot.Key.Q, true);
                SendKey(Godot.Key.Q, false);
                await Frames(2);
                int ss0 = Doc.SectorsAt(-1024, 1024).FirstOrDefault(-1);
                string slopeSays = "no sector";
                bool sloped = false;
                if (ss0 >= 0)
                {
                    Do("visualslopeedges");
                    var lab = Doc.Labels[ss0];
                    double slFz = Doc.Sectors[ss0].FloorZ;
                    _preview.SetCamera(Chosen.Forge.View.PreviewMesher.P(lab.X, lab.Y, slFz - 6144), Chosen.Forge.View.Preview3D.YawOf(0), Mathf.DegToRad(-60));
                    await Frames(3);
                    var handle = _preview.Handle;
                    int undosS = Undo.UndoCount;
                    int selBefore = _preview.Selected.Count;
                    if (handle is { } hd && hd.plane.Sector == ss0 && hd.plane.Part == Chosen.Forge.View.VisualPart.Floor)
                    {
                        static double PZ(Chosen.Forge.Document.MapDocument d, int s, double x, double y) => Chosen.Shared.Engine.Geometry.BuildGeom.PlaneZ(d, s, false, x, y);
                        var plane = hd.plane;
                        // the plane aimed at is the one drawn (from its first wall to its far edge)
                        bool shownPlane = _preview.Shown == plane;
                        // a click locks onto the handle aimed at (no selection); the aim moved off: still locked
                        _preview.SlopeClick(false);
                        bool locked = _preview.Locked == hd && _preview.Selected.Count == selBefore;
                        _preview.SetCamera(Chosen.Forge.View.PreviewMesher.P(lab.X + 4096, lab.Y + 4096, slFz - 6144), Chosen.Forge.View.Preview3D.YawOf(90), Mathf.DegToRad(-30));
                        await Frames(3);
                        locked &= _preview.Locked == hd;
                        // Shift+click on another wall: the first wall; the far edge locked
                        int first0 = Doc.Sectors[ss0].WallPtr;
                        int other = first0 + 1;
                        var (hx1, hy1, hx2, hy2) = Chosen.Shared.Engine.Geometry.BuildGeom.WallEnds(Doc, other);
                        _preview.FirstWall(plane, other);
                        _preview.Locked = (plane, true);
                        bool isFirst = Chosen.Shared.Engine.Geometry.BuildGeom.WallEnds(Doc, Doc.Sectors[ss0].WallPtr) == (hx1, hy1, hx2, hy2);
                        // the far edge's wheel: the plane tilts about the first wall (kept, and still the first wall)
                        var hfar = Chosen.Forge.Tools.SlopeOps.FarCorner(Doc, ss0, Doc.Sectors[ss0].WallPtr);
                        _preview.SlopeEdge(plane, Doc.Sectors[ss0].WallPtr, -1024, true);
                        _preview.SlopeEdge(plane, Doc.Sectors[ss0].WallPtr, -1024, true);
                        bool tilted = isFirst && Math.Abs(PZ(Doc, ss0, hx1, hy1) - slFz) < 2 && Math.Abs(PZ(Doc, ss0, hx2, hy2) - slFz) < 2
                            && Math.Abs(PZ(Doc, ss0, hfar.x, hfar.y) - (slFz - 2048)) < 8 && (Doc.Sectors[ss0].FloorStat & 2) != 0
                            && Chosen.Shared.Engine.Geometry.BuildGeom.WallEnds(Doc, Doc.Sectors[ss0].WallPtr) == (hx1, hy1, hx2, hy2);
                        // the first wall's wheel: its side up, the far edge kept, the first wall the same
                        _preview.Locked = (plane, false);
                        _preview.SlopeEdge(plane, Doc.Sectors[ss0].WallPtr, -512, false);
                        bool slRaised = Math.Abs(PZ(Doc, ss0, hx1, hy1) - (slFz - 512)) < 2 && Math.Abs(PZ(Doc, ss0, hfar.x, hfar.y) - (slFz - 2048)) < 8
                            && Chosen.Shared.Engine.Geometry.BuildGeom.WallEnds(Doc, Doc.Sectors[ss0].WallPtr) == (hx1, hy1, hx2, hy2);
                        sloped = shownPlane && locked && tilted && slRaised;
                        slopeSays = $"sector {ss0}: its plane drawn {shownPlane}, a handle locked {locked}; first wall ({hx1},{hy1})-({hx2},{hy2}) set {isFirst}, tilted about it {tilted} "
                            + $"(far {PZ(Doc, ss0, hfar.x, hfar.y):0}), its side raised {slRaised} ({PZ(Doc, ss0, hx1, hy1):0}, from {slFz:0}); \"{_status.Text}\"";
                        while (Undo.UndoCount > undosS)
                            UiDo("undo");
                    }
                    else
                        slopeSays = $"handle under the crosshair: {(handle is { } x ? $"{Chosen.Forge.View.Preview3D.Code(x.plane)} {(x.far ? "far edge" : "first wall")}" : "none")}";
                    Do("visualslopeedges");
                }
                SendKey(Godot.Key.Q, true);
                SendKey(Godot.Key.Q, false);
                await Frames(2);
                Check(sloped && !_preview.SlopeMode && ss0 >= 0 && (Doc.Sectors[ss0].FloorStat & 2) == 0,
                    $"slope edges: {slopeSays}; undone, off again");
            }

            // what the cursor is over never changes the layout: the view and the info panel keep their sizes
            var viewSize = View.Size;
            var infoSize = _info.Size;
            var sizes = new List<string>();
            foreach (var (mode, spot) in new (string, Vector2)[]
            {
                ("thingsmode", new(-960, 1152)), ("sectorsmode", new(0, 1536)), ("linedefsmode", new(-2048, 1536)),
                ("verticesmode", new(-2048, 2048)), ("verticesmode", new(6000, 6000)), ("thingsmode", new(-1536, 1536)),
            })
            {
                Do(mode);
                await Move(spot);
                await Frames(2);
                if (View.Size != viewSize || _info.Size != infoSize)
                    sizes.Add($"{mode} at {spot}: view {View.Size}, info {_info.Size}");
            }
            Check(sizes.Count == 0, sizes.Count == 0 ? $"hovering keeps the layout (view {viewSize}, info panel {infoSize})"
                : "hovering moved the layout: " + string.Join("; ", sizes) + $" (was view {viewSize}, info {infoSize})");

            // Lights mode as a user works it: a right click in a room places a point light half way up it and opens its
            // dialog (its brightness made 150%, Apply), a drag moves it, Delete takes it away; all undone (the map without
            // lights of its own again)
            {
                int lights0 = Doc.Lights.Count, undosL = Undo.UndoCount;
                bool had0 = Doc.HasLights;
                UiDo("lightsmode");
                var lat = new Vector2(0, 1536);
                await Move(lat);
                await Press(MouseButton.Right, lat, true);
                await Press(MouseButton.Right, lat, false);
                await Frames(2);
                int li = Doc.Lights.Count - 1;
                var ldlg = LastDialog;
                bool placed = Doc.Lights.Count == lights0 + 1 && Doc.HasLights && li >= 0 && !Doc.Lights[li].Attached
                    && Doc.LightPlace(li) is { } lp0 && Math.Abs(lp0.x - lat.X) <= View.GridSize && Math.Abs(lp0.y - lat.Y) <= View.GridSize;
                bool lOpen = ldlg != null && IsInstanceValid(ldlg) && ldlg.Visible && ldlg.Title == $"Light {li} (new)";
                bool lApplied = false, lMoved = false, lGone = false;
                string lSays = "", l3 = "not tried";
                if (placed && lOpen)
                {
                    Testing.DialogDriver.Set(ldlg, null, "Brightness (percent of a lamp's)", "150");
                    Testing.DialogDriver.Ok(ldlg);
                    await Frames(2);
                    lSays = _status.Text;
                    lApplied = Math.Abs(Doc.Lights[li].Energy - 1.5f) < 0.001f;
                    var from = Doc.LightPlace(li).Value;
                    var grab = new Vector2((float)from.x, (float)from.y);
                    await Move(grab);
                    await Press(MouseButton.Left, grab, true);
                    await Move(grab + new Vector2(128, 0));
                    await Move(grab + new Vector2(256, 0));
                    await Press(MouseButton.Left, grab + new Vector2(256, 0), false);
                    await Frames(1);
                    var to = Doc.LightPlace(li).Value;
                    lMoved = Math.Abs(to.x - (from.x + 256)) <= View.GridSize && Math.Abs(to.y - from.y) < 1;
                    l3 = await LightsIn3D(li);
                    await Move(new Vector2((float)to.x, (float)to.y));
                    UiDo("select light " + li);
                    int beforeDelete = Doc.Lights.Count;
                    UiDo("deleteitem");
                    await Frames(1);
                    lGone = Doc.Lights.Count == beforeDelete - 1;
                }
                while (Undo.UndoCount > undosL)
                    UiDo("undo");
                await Frames(1);
                UiDo("verticesmode");
                Check(placed && lOpen && lApplied && lMoved && lGone && Doc.Lights.Count == lights0 && Doc.HasLights == had0,
                    $"Lights mode: a right click placed a point light (True: {placed}), its dialog open {lOpen}, brightness 150% applied {lApplied} "
                    + $"(\"{lSays}\"), dragged 256 {lMoved}, deleted {lGone}; undone to {Doc.Lights.Count} (own lights {Doc.HasLights})");
                Check(l3 == "ok", "Lights mode's copy and paste, and the light in the 3D preview (picked, selected, held and moved, "
                    + $"raised by the wheel, its dialog shown as it changes, copied and pasted there): {l3}");
            }

            // Make Door's dialog as a user fills it, on a doorway between two rooms made for it out of the way: the Sounds
            // tab's set chosen (its four sounds filled in), a face picture typed on Look, a key and used once on Opening,
            // Make: the door as asked; undone with its rooms (each field set: the dialog remembers the user's own)
            {
                static List<Vector2I> R(int x1, int y1, int x2, int y2) => new() { new(x1, y1), new(x2, y1), new(x2, y2), new(x1, y2) };
                const int ox = 50176, oy = 50176;
                int doorRooms0 = Doc.Sectors.Count;
                Undo.CreateUndo("door rooms");
                Doc.MakeSector(R(ox, oy, ox + 4096, oy + 4096));
                Doc.NotifyChanged();
                int dws = Doc.MakeSectorJoined(R(ox + 1024, oy + 4096, ox + 3072, oy + 4608), out _);
                Doc.NotifyChanged();
                Doc.MakeSectorJoined(R(ox, oy + 4608, ox + 4096, oy + 8704), out _);
                Doc.NotifyChanged();
                Rec?.Document(Doc, "undo", "door rooms");
                UndoChanged();
                Chosen.Forge.Tools.ContextTools.All.First(t => t.Name == "Make Door...").Run(
                    new Chosen.Forge.Tools.ToolContext { Window = this, Sectors = new List<int> { dws } });
                await Frames(2);
                var ddlg = LastDialog;
                bool dOpen = ddlg != null && IsInstanceValid(ddlg) && ddlg.Visible && ddlg.Title == $"Make Door in Sector {dws}";
                string filled = "", dSays = "", drawn = "the result not shown in the 2D view", pFilled = "", pSays = "";
                bool pOpen = false, remade = false, reversed = false;
                bool made = false;
                if (dOpen)
                {
                    var tabs = ddlg.FindChildren("*", nameof(TabContainer), true, false).OfType<TabContainer>().First();
                    // the control on a tab's row whose label starts so (a number with a Pick... button: its spin box)
                    Control Row(string tab, string label)
                    {
                        var page = tabs.GetChildren().First(c => c.Name == tab);
                        var l = page.FindChildren("*", nameof(Label), true, false).OfType<Label>().First(x => x.Text.StartsWith(label) && x.GetParent() is GridContainer);
                        var next = (Control)l.GetParent().GetChild(l.GetIndex() + 1);
                        return next is HBoxContainer h ? h.GetChildren().OfType<SpinBox>().First() : next;
                    }
                    void Pick(string tab, string label, int item)
                    {
                        var o = (OptionButton)Row(tab, label);
                        o.Select(item);
                        o.EmitSignal(OptionButton.SignalName.ItemSelected, item);
                    }
                    void Num(string tab, string label, double v) => ((SpinBox)Row(tab, label)).Value = v;
                    // (a swinging pair: Reverse swings it into the other room)
                    Pick("Door", "Door", 1);
                    var sideBox = (OptionButton)Row("Door", "Swings into");
                    var rev = tabs.GetChildren().First(c => c.Name == "Door").FindChildren("*", nameof(Button), true, false).OfType<Button>().First(x => x.Text == "Reverse");
                    int sideWas = sideBox.Selected;
                    bool revOn = !rev.Disabled;
                    rev.EmitSignal(BaseButton.SignalName.Pressed);
                    reversed = revOn && sideBox.Selected == 1 - sideWas && new[] { sideBox.GetItemText(0), sideBox.GetItemText(1) }.OrderBy(x => x.Contains("(north)") ? 0 : 1).ToArray() is var sw && sw[0].Contains("(north)") && sw[1].Contains("(south)");
                    Pick("Door", "Door", 0);
                    reversed &= rev.Disabled;
                    Num("Door", "Leaf thickness", 0);
                    Num("Door", "Lip", Units.Game ? 64.0 / Units.PerXy : 64);
                    Num("Door", "Closes by itself", 0);
                    Pick("Sounds", "Sounds", 3);
                    filled = string.Join(",", new[] { "Opening starts", "Opening stops", "Closing starts", "Closing stops" }.Select(n => ((SpinBox)Row("Sounds", n)).Value));
                    Num("Look", "Faces", 300);
                    Num("Look", "Edges", -1);
                    Pick("Opening", "Opened by", 0);
                    Num("Opening", "Channel", 0);
                    Pick("Opening", "Needs", 2);
                    ((CheckBox)Row("Opening", "Used once")).ButtonPressed = true;
                    // the 2D view draws the door as it would be while the dialog is open: the copy's own sound sprite
                    // asked of the copy (its colour was asked of the map: index out of range, the view's drawing failed)
                    await ToSignal(GetTree().CreateTimer(0.3), SceneTreeTimer.SignalName.Timeout);
                    if (View.Doc != Doc && View.Doc.Sprites.Count > Doc.Sprites.Count)
                        try
                        {
                            int last = View.Doc.Sprites.Count - 1;
                            View.SpriteColor(last);
                            View.SpriteHidden?.Invoke(last);
                            View.SectorLabel(View.Doc.Sectors.Count - 1);
                            View.SectorCaption(View.Doc.Sectors.Count - 1);
                            View.Links();
                            drawn = $"drawn with its {View.Doc.Sprites.Count} sprites (the map {Doc.Sprites.Count})";
                        }
                        catch (Exception e)
                        {
                            drawn = "FAILED: " + e.Message;
                        }
                    Testing.DialogDriver.Ok(ddlg);
                    await Frames(2);
                    dSays = _status.Text;
                    var pushers = Enumerable.Range(0, Doc.Walls.Count).Where(i => Doc.Walls[i].Xw is { TriggerPush: not 0 } && Doc.Walls[i].Sector == dws).ToList();
                    made = Doc.Sectors[dws].Type == Blood.Doors.SlideMarked && pushers.Count == 4
                        && pushers.All(i => Doc.Walls[i].Picnum == 300 && Doc.Walls[i].Xw is { Key: 2, TriggerOnce: 1, Command: 1 })
                        && Doc.Sprites.Any(sp => sp.Type == Blood.Doors.SoundSector && sp.SectNum == dws && sp.Xsp is { Data1: 103, Data2: 0, Data3: 103, Data4: 0 });
                    // Door Properties on it (the menu has it in place of Make Door): filled in from the door, its lip
                    // changed, Apply: made again with that lip, the rest as it was; undone
                    if (made)
                    {
                        var pc = new Chosen.Forge.Tools.ToolContext { Window = this, Sectors = new List<int> { dws } };
                        var props = Chosen.Forge.Tools.ContextTools.All.First(t => t.Name == "Door Properties...");
                        bool listed = props.Applies(pc) && !Chosen.Forge.Tools.ContextTools.All.First(t => t.Name == "Make Door...").Applies(pc);
                        props.Run(pc);
                        await Frames(2);
                        var propsDlg = LastDialog;
                        pOpen = listed && propsDlg != null && IsInstanceValid(propsDlg) && propsDlg.Visible && propsDlg != ddlg && propsDlg.Title.StartsWith("Door Properties");
                        if (pOpen)
                        {
                            tabs = propsDlg.FindChildren("*", nameof(TabContainer), true, false).OfType<TabContainer>().First();
                            pFilled = $"face {((SpinBox)Row("Look", "Faces")).Value}, key {((OptionButton)Row("Opening", "Needs")).Selected}, "
                                + $"once {((CheckBox)Row("Opening", "Used once")).ButtonPressed}, sounds {((OptionButton)Row("Sounds", "Sounds")).Selected}, "
                                + $"lip {Chosen.Forge.Tools.ToolDialog.BuildLength((SpinBox)Row("Door", "Lip"))}";
                            Num("Door", "Lip", Units.Game ? 128.0 / Units.PerXy : 128);
                            Testing.DialogDriver.Ok(propsDlg);
                            await Frames(2);
                            pSays = _status.Text;
                            var after = Blood.Doors.Find(Doc, dws);
                            remade = after is { NotShaped: null } && after.Now.Lip == 128 && after.Now.Face == 300 && after.Now.Key == 2 && after.Now.Once
                                && after.Now.SoundIds.SequenceEqual(new[] { 103, 0, 103, 0 });
                            if (remade)
                                UiDo("undo");
                        }
                        UiDo("undo");
                    }
                }
                UiDo("undo");
                Check(dOpen && reversed && filled == "103,0,103,0" && drawn.StartsWith("drawn") && made && Doc.Sectors.Count == doorRooms0,
                    $"Make Door's dialog: open {dOpen}, Reverse swings a pair into the other room (north, south) {reversed}, the set \"Sliding door\" filled in {filled}, {drawn}, made with face 300, the eye key, used once {made} "
                    + $"(\"{dSays}\"); undone to {Doc.Sectors.Count} sectors");
                Check(pOpen && pFilled == "face 300, key 2, once True, sounds 3, lip 64" && remade,
                    $"Door Properties on it: open {pOpen}, filled in from the door ({pFilled}), the lip made 128: made again with the rest kept {remade} (\"{pSays}\")");
            }

            // Space held pans the view
            var before = View.Centre;
            await Move(new(0, 0));
            SendKey(Godot.Key.Space, true);
            await Frames(1);
            var at = View.MapToScreen(Vector2.Zero);
            await MoveScreen(at + new Vector2(120, 0));
            SendKey(Godot.Key.Space, false);
            await Frames(1);
            float moved = before.X - View.Centre.X;
            Check(Mathf.Abs(moved * View.Zoom - 120) < 2, $"Space held pans ({moved * View.Zoom:0} px of 120)");
            Check(Doc.Sprites.Count == 2 && Doc.Sectors.Count == 7 && MapErrors() == 0, $"nothing edited while panning; {MapErrors()} map errors");

            // undo goes back step by step to the empty map
            int steps = 0;
            while (Undo.UndoCount > 0 && steps < 100)
            {
                UiDo("undo");
                steps++;
            }
            Check(Doc.Sectors.Count == 0 && Doc.Sprites.Count == 0, $"undone in {steps} steps to an empty map");

            // the session just recorded replays exactly: every check of the map after an edit the same
            string folder = Rec?.Folder;
            Rec?.Close();
            Rec = null;
            int checks = folder != null ? File.ReadAllLines(Path.Combine(folder, "events.log")).Count(l => l.Contains("\tcheck\t")) : 0;
            int differ = folder != null ? await Replay(folder, verbose: false) : -1;
            Check(differ == 0 && checks > 10, $"the recorded session replays exactly ({checks} checks, {differ} differ; {folder})");
        }
        catch (Exception e)
        {
            GD.PrintErr("uitest: " + e);
            failures++;
        }
        finally
        {
            if (cfgBefore != null)
                System.IO.File.WriteAllBytes(cfgPath, cfgBefore);
            else
                System.IO.File.Delete(cfgPath);
        }
        GD.Print(failures == 0 ? "uitest: passed" : $"uitest: {failures} failure(s)");
        return failures == 0 ? 0 : 1;
    }

    /// <summary>Light li (a point light; Lights mode on, the 2D view showing) copied and pasted 512 east in 2D (a light
    /// like it, as high over the floor, selected); then in the 3D preview: the crosshair on its bulb, a click selecting
    /// it (2D too), held while the camera turns: moved with the crosshair (one undo step), the wheel raising it, its
    /// dialog from a right click shown as it changes (the map's light the same until Apply; cancelled: the map's again),
    /// Ctrl+C on it and Ctrl+V aimed at the floor: a light as high over that floor. "ok", else what went wrong; the
    /// caller undoes it all.</summary>
    async Task<string> LightsIn3D(int li)
    {
        UiDo("select light " + li);
        UiDo("copyselection");
        int n0 = Doc.Lights.Count;
        var p0 = Doc.LightPlace(li).Value;
        UiDo("pastehere 512 0");
        if (Doc.Lights.Count != n0 + 1)
            return $"2D paste made {Doc.Lights.Count - n0} lights (\"{_status.Text}\")";
        var pp = Doc.LightPlace(n0).Value;
        if (Math.Abs(pp.x - p0.x - 512) > 0.5 || Math.Abs(pp.y - p0.y) > 0.5 || Math.Abs(pp.z - p0.z) > 0.5
            || Math.Abs(Doc.Lights[n0].Energy - Doc.Lights[li].Energy) > 1e-4 || !SelectionOf(Chosen.Forge.View.ElementKind.Light).Contains(n0))
            return $"2D paste: at {pp} (copied at {p0}), energy {Doc.Lights[n0].Energy}, selected {SelectionOf(Chosen.Forge.View.ElementKind.Light).Contains(n0)}";
        ClearSelection();
        SendKey(Godot.Key.Q, true);
        SendKey(Godot.Key.Q, false);
        await Frames(2);
        try
        {
            // K in the preview: Lights mode off (back to the mode before) and on again, the preview kept
            PreviewKey(new InputEventKey { Keycode = Godot.Key.K, Pressed = true });
            await Frames(1);
            bool kOff = Editing.Mode is not LightsMode && !_preview.LightMarkers && _preview.Visible;
            string kWas = Editing.Mode.Name;
            PreviewKey(new InputEventKey { Keycode = Godot.Key.K, Pressed = true });
            await Frames(1);
            if (!kOff || Editing.Mode is not LightsMode || !_preview.LightMarkers || !_preview.Visible)
                return $"3D: K took Lights mode off {kOff} (to {kWas}) and on again {Editing.Mode is LightsMode} (bulbs {_preview.LightMarkers})";
            var eye = Chosen.Forge.View.PreviewMesher.P(p0.x - 256, p0.y, p0.z);
            _preview.SetCamera(eye, Chosen.Forge.View.Preview3D.YawOf(0), 0);
            await Frames(3);
            var lt = Chosen.Forge.View.Preview3D.LightTarget(Doc, li);
            if (_preview.Target != lt)
                return $"3D: the crosshair on {(_preview.Target is { } a ? Chosen.Forge.View.Preview3D.Code(a) : "nothing")}, not light {li}'s bulb";
            var press = new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true };
            var release = new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false };
            _preview.EditInput(press);
            await Frames(1);
            _preview.EditInput(release);
            await Frames(1);
            if (!_preview.Selected.Contains(lt) || !SelectionOf(Chosen.Forge.View.ElementKind.Light).Contains(li))
                return "3D: a click on the bulb didn't select it";
            int u0 = Undo.UndoCount;
            string before = _preview.Target is { } tb ? Chosen.Forge.View.Preview3D.Code(tb) : "nothing";
            _preview.EditInput(press);
            bool held = _preview.Holding;
            _preview.SetCamera(eye, Chosen.Forge.View.Preview3D.YawOf(64), 0);
            await Frames(3);
            held &= _preview.Holding;
            _preview.EditInput(release);
            await Frames(2);
            var p1 = Doc.LightPlace(li).Value;
            double wantY = p0.y + 256 * Math.Sin(64 * Math.PI / 1024);
            if (Undo.UndoCount != u0 + 1 || Math.Abs(p1.y - wantY) > 3 || Math.Abs(p1.z - p0.z) > 16)
                return $"3D: held while turning, it is at {p1} (from {p0}, y {wantY:0} wanted; {Undo.UndoCount - u0} undo steps; pressed on {before}, held {held}; \"{_status.Text}\")";
            int z0 = Doc.Lights[li].Z;
            _preview.EditInput(new InputEventMouseButton { ButtonIndex = MouseButton.WheelUp, Pressed = true });
            await Frames(2);
            if (Doc.Lights[li].Z != z0 - 1024)
                return $"3D: the wheel took it to z {Doc.Lights[li].Z} (from {z0})";
            // its dialog (the crosshair still on it: it moved with it, the wheel raised it out of the way: aimed again)
            var eye2 = Chosen.Forge.View.PreviewMesher.P(p0.x - 256, p0.y, p0.z - 1024);
            _preview.SetCamera(eye2, Chosen.Forge.View.Preview3D.YawOf(64), 0);
            await Frames(3);
            if (_preview.Target != lt)
                return $"3D: aimed at it again, the crosshair on {(_preview.Target is { } b ? Chosen.Forge.View.Preview3D.Code(b) : "nothing")}";
            _preview.EditInput(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true });
            await Frames(2);
            var dlg = LastDialog;
            if (dlg == null || !IsInstanceValid(dlg) || !dlg.Visible || dlg.Title != $"Light {li}")
                return $"3D: a right click on it opened {(dlg != null && IsInstanceValid(dlg) ? $"\"{dlg.Title}\"" : "nothing")}";
            Testing.DialogDriver.Set(dlg, null, "Brightness (percent of a lamp's)", "300");
            await Frames(1);
            bool live = _preview.LightsShown is { } ls && ls.Count > li && Math.Abs(ls[li].Energy - 3f) < 1e-4
                && Math.Abs(Doc.Lights[li].Energy - 1.5f) < 1e-4;
            // closed by its window's X (Godot: hidden, then cancelled): applied all the same, one undo step
            int uClose = Undo.UndoCount;
            dlg.Hide();
            dlg.EmitSignal(AcceptDialog.SignalName.Canceled);
            await Frames(2);
            if (!live || _preview.LightsShown != null || Math.Abs(Doc.Lights[li].Energy - 3f) > 1e-4 || Undo.UndoCount != uClose + 1)
                return $"3D: the dialog's brightness shown as it changed {live}, applied as it closed {Doc.Lights[li].Energy} "
                    + $"({Undo.UndoCount - uClose} undo steps), the preview the map's again {_preview.LightsShown == null}";
            ResumePreview();
            // Ctrl+C on it, Ctrl+V aimed at the floor ahead
            int h0 = Blood.LightDialog.Height(Doc, li), n1 = Doc.Lights.Count;
            PreviewKey(new InputEventKey { Keycode = Godot.Key.C, CtrlPressed = true, Pressed = true });
            _preview.SetCamera(eye, Chosen.Forge.View.Preview3D.YawOf(0), Mathf.DegToRad(-50));
            await Frames(3);
            if (_preview.Target is not { IsPlane: true } floorT)
                return $"3D: aimed down, the crosshair on {(_preview.Target is { } c ? Chosen.Forge.View.Preview3D.Code(c) : "nothing")}";
            // (Lights mode: a click on the floor selects nothing)
            int sel0 = _preview.Selected.Count;
            _preview.EditInput(press);
            _preview.EditInput(release);
            await Frames(1);
            if (_preview.Selected.Count != sel0 || _preview.Selected.Contains(floorT))
                return "3D: in Lights mode a click on the floor selected it";
            // the context menu there: the lights' tools (Place Light Here), not the surfaces' or sprites'
            var menuCtx = ToolContext();
            var menuNames = Chosen.Forge.Tools.ContextTools.All.Where(t => t.Applies(menuCtx)).Select(t => t.Name).ToList();
            if (!menuNames.Contains("Place Light Here") || menuNames.Contains("Place Sprite Here") || menuNames.Contains("Copy Texture"))
                return "3D: Lights mode's context menu: " + string.Join(", ", menuNames);
            PreviewKey(new InputEventKey { Keycode = Godot.Key.V, CtrlPressed = true, Pressed = true });
            await Frames(2);
            if (Doc.Lights.Count != n1 + 1)
                return $"3D: Ctrl+C, Ctrl+V made {Doc.Lights.Count - n1} lights (\"{_status.Text}\")";
            int h1 = Blood.LightDialog.Height(Doc, n1);
            var hit = _preview.HitPoint;
            var pl = Doc.LightPlace(n1).Value;
            if (Math.Abs(h1 - h0) > 2 || Math.Abs(pl.x - hit.x) > 2 || Math.Abs(pl.y - hit.y) > 2)
                return $"3D paste: at {pl}, {h1} over the floor (aimed at {hit}, {h0} wanted)";
            return "ok";
        }
        finally
        {
            SendKey(Godot.Key.Q, true);
            SendKey(Godot.Key.Q, false);
            await Frames(2);
        }
    }

    async Task Frames(int n)
    {
        for (int i = 0; i < n; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    Vector2 Screen(Vector2 map) => View.GetGlobalTransformWithCanvas() * View.MapToScreen(map);

    /// <summary>The mouse to a point of the view. Handed to the view as the GUI hands it: a window that never had the
    /// mouse enter it (headless, off the screen) gets no hover motion from Godot, only motion with a button held.</summary>
    async Task MoveScreen(Vector2 local)
    {
        if (_held)
        {
            var g = View.GetGlobalTransformWithCanvas() * local;
            Input.ParseInputEvent(new InputEventMouseMotion { Position = g, GlobalPosition = g, ButtonMask = _mask });
        }
        else
            View._GuiInput(new InputEventMouseMotion { Position = local, GlobalPosition = local });
        await Frames(1);
    }

    bool _held;
    MouseButtonMask _mask;

    async Task Move(Vector2 map) => await MoveScreen(View.MapToScreen(map));

    /// <summary>A button pressed or let go at map, with the modifiers in mods held ("s" Shift, "a" Alt, "c" Ctrl: on the
    /// event, as recorded, and as the modes ask ForgeInput).</summary>
    async Task Press(MouseButton b, Vector2 map, bool down, string mods = "", bool dbl = false)
    {
        var g = Screen(map);
        _held = down;
        _mask = down ? (b == MouseButton.Left ? MouseButtonMask.Left : MouseButtonMask.Right) : 0;
        bool held = mods.Length > 0;
        if (held)
            (ForgeInput.ShiftOverride, ForgeInput.AltOverride, ForgeInput.CtrlOverride) = (mods.Contains('s'), mods.Contains('a'), mods.Contains('c'));
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = b, Pressed = down, Position = g, GlobalPosition = g,
            ButtonMask = down ? (b == MouseButton.Left ? MouseButtonMask.Left : MouseButtonMask.Right) : 0,
            ShiftPressed = mods.Contains('s'), AltPressed = mods.Contains('a'), CtrlPressed = mods.Contains('c'), DoubleClick = dbl && down });
        await Frames(1);
        if (held)
            ForgeInput.ShiftOverride = ForgeInput.AltOverride = ForgeInput.CtrlOverride = null;
    }

    async Task Click(MouseButton b, Vector2 map, string mods = "")
    {
        await Move(map);
        await Press(b, map, true, mods);
        await Press(b, map, false, mods);
    }

    /// <summary>A double-click: a click, then a press Godot marks as the double-click's, and its release.</summary>
    async Task DoubleClick(Vector2 map, string mods = "")
    {
        await Click(MouseButton.Left, map, mods);
        await Press(MouseButton.Left, map, true, mods, dbl: true);
        await Press(MouseButton.Left, map, false, mods);
    }

    static void SendKey(Key k, bool down) => Input.ParseInputEvent(new InputEventKey { Keycode = k, PhysicalKeycode = k, Pressed = down });
}
