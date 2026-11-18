# Chosen Forge

A map editor for Blood's `.MAP` and Chosen's `.MAPX`, in the manner of UltimateDoomBuilder: the same window, modes,
mouse and keys where Build's maps allow. A separate Godot 4.7 C# app; it shares the map code with the game
(`shared/Chosen.Engine`, `shared/Chosen.Blood`) and needs neither the game nor its extracted data, only a Blood install
for the tile previews (without one it still edits, showing tile numbers).

## Running

- `forge\run.bat [map file]`: builds and starts it (a file given: opened).
- `forge\run.bat --roundtrip <out dir> [maps]`: headless; every map (default: the repository's base and add-on maps)
  opened through the editor's document, saved as .MAP into `<out dir>` and through .MAPX, compared with the original
  byte for byte (141 of 141).
- `forge\run.bat --selftest [map]`: headless; a scripted editing session on E1M1 (points and sprites moved, sprites
  added and deleted, first walls set, x-walls edited) and a geometry fuzz on E1M1, E2M1 and DWE1M1 (200 random
  operations each: points inserted and deleted, rooms drawn and joined back, convex sectors split, sectors deleted), the
  map rules checked after every step, everything undone: byte for byte as opened. The fuzzed maps are saved in
  `tools/forge_fuzz` (for `maptool check` and the game: `--file=<path>.mapx`).
- `forge\run.bat --uitest`: headless; the modes driven by mouse and key events through the window (`src/UiTest.cs`): a
  rectangle drawn (a sector), one against it (joined by a red wall), one inside it (a room), the sector dragged with
  its room, a line across (a split), a rectangle partly over a sector (cut, the rest a new sector), right-click auto-draw, a sprite placed by right-drag and one by
  Insert, Space panning, game units, everything undone; then the session it recorded is replayed and must give the
  same map after every edit. Settings are put back.
- `forge\run.bat --replay=<session folder|last> [--accept] [--until=N] [--save=<file.map>]`, `--regress`,
  `--addtest=...`: see "Sessions" below. run.bat gives the headless runs their own log (`%TEMP%\chosen-forge-test.log`):
  Godot keeps five logs in `user://logs`, and test runs would push the user's out (run Godot yourself with
  `--log-file <file>` before `--` for the same).
- `Godot_..._console.exe --headless --path forge -- --rebuild=<absolute map path>`: a map made again from nothing by
  input only (`src/Testing/RebuildTest.cs`), as a mapper makes it: each sector's outline clicked in Draw Lines mode (the
  level first, islands before the sector round them, rooms over rooms with Alt, a hole's filler as a room in it, an
  empty hole drawn as a room and deleted), wrong joins detached and the right ones made (Backspace, J), extra points
  deleted, first walls set (Shift+F), every sector, wall and sprite dialog filled, markers moved, sprites placed from
  the browser (Insert), the header through Map Options; then compared with the original field by field (report and the
  rebuilt map in `tools/forge_rebuild/`). DM2 and DM3 come out identical; DM1 has 19 differences left: a hole lying
  along its own sector's wall (degenerate in Blood's map: no drawing makes it) and markers' sectors (Blood doesn't use
  them). Run it after changing drawing, joining or the dialogs; its session (in `test-sessions`) replays exactly.
- Testing the window: `Godot_..._console.exe --position 20000,20000 --path forge -- <absolute map path>
  [--mode=sectorsmode] [--do=<action>] [--hover=x,y] [--tab=<docker tab>] [--spritecat=<sprite browser category>] [--edit=<point|line|sector|sprite>:<index>[:<dialog tab>]|atmosphere:<index>]
  [--picktile=<tile>[,<kind>]] [--preview=x,y[,angle[,pitch[,height]]]] [--doafter=<action>] [--select3d=f12,c12,w40u] [--dolater=<action>] [--shotdelay=<seconds>] --shot=<png>` opens the map off the screen, saves a picture of the
  window (dialogs drawn inside it) and quits (`--dolater`: an action after the hover and the preview's first pick,
  `contextmenu` for its menu; `--shotdelay`: the picture later than 1.2 s, for what moves). A test picture draws the
  popups inside the window (their positions the window's); the editor shows them as windows of their own (positions
  on the screen: `MainWindow.PopupPosition`). A test run (`--position`, `--noinput`, `--shot`) never takes the focus or
  the mouse (`src/Editing/ForgeInput.cs`: the window opens without focus, project.godot's `no_focus`, and the 3D
  preview doesn't capture the cursor), and leaves the recent files as they were.

The first start looks for Blood in Steam's library folders and GOG's usual places; Edit > Preferences sets the folder.
Settings live in `%APPDATA%/Godot/app_userdata/Chosen Forge/forge.cfg`.

## Sessions: recorded, replayed, regression tests

Every session is recorded (`src/Recording/SessionRecorder.cs`) into
`%APPDATA%/Godot/app_userdata/Chosen Forge/sessions/<date-time>/` (the newest 30 kept; Help > Open the Sessions
Folder): a map opened is copied first as it was (`map-NN-original.*`, with its `.patch` when applied), then every
mouse and key event on the map (in map units, with the modifiers and buttons), every command from a menu, the toolbar
or a mode button, the view (centre, zoom: the snap ranges are pixels), the settings input depends on (grid, snap,
units, highlight range, sprite type, hidden sprite categories), each dialog's fields changed when it is applied (a
replay builds the same dialog unseen, fills them and applies it), the key bindings, the status bar, the map's
fingerprint after every edit, and every error or warning Godot or a script reported (`error` lines: a C# exception in a
callback too; `events.log`, one line an event). The tests' own sessions (`--uitest`, `--rebuild`) go to
`test-sessions` beside it; a folder still open (another Forge recording into it) is never removed.

**Help > Report a Problem...** (F12) asks what went wrong, marks the moment in the log and copies the session into the
repository's `bugs/forge/<date-time>/` with a `note.txt` (from the exported Forge: it stays in the sessions folder; a
second report from the same session goes into `<date-time>-<hhmmss>/`, the first's note and screenshot kept). A
level of the data folder is stored there as `<copy>.ref` naming it (Blood's maps aren't ours to commit); the mapper's
own maps are copied. The window as it was before the dialog opened goes with it as `screenshot.png` (shown in the
dialog; a box leaves it out), and `report.txt` says where the views were, in the game's report keys: the map, the
view and what was shown (Edit, Off, On, Play and its clock), the 3D preview's camera (`camera`, `angles`: Quake units
as the game's reports; `build_camera`: Build's, with its sector), the 2D view's middle and zoom, and from Play what the
player set off (`play_log`) with `nblood_send` / `nblood_seconds` for NBlood in the same state (the sectors entered
or used, the channels of things broken, that long after the first). `python pipeline/bugs.py nblood forge/<id>`
shows NBlood there (`compare.jpg`: the screenshot beside it). The key isn't PrtScr: Windows 11 opens its Snipping
Tool on that (bound to Print in Preferences > Keys it works where Windows leaves it alone: Windows sends only its
release, which counts as the press). A dialog opened from the 3D preview lets the mouse go and takes it back after.

`forge\run.bat --replay=<folder|last>` plays a session back headless through the same code (the view's input, the
window's key handling, the commands, the dialogs filled as recorded), checks the map's
fingerprint at every recorded check and prints the first that differ with what the status bar said then and now
(`--until=N` stops before line N; `--save=<file.map>` writes the map where it stopped, for MapTool's `dump`; the
recording's errors are printed where they happened; without `--headless` through Godot's own exe, it plays in a
window). Regression
replays live in `forge/tests/replays/<name>/` (`tests/replays/README.md`): `--regress` runs them all; `--addtest=
<session|last> --name=<name> [--note=...]` makes one; `--replay=<test> --accept` takes a corrected replay's results.

## The window

As UDB's: the menus, the main toolbar, the mode buttons on the left, the 2D view, the docker on the right (Stats,
Sprites: the type browser, Filter, Errors; its width is a divider's), the info panel (its height a divider's) at the bottom (what the cursor is over: a vertex, both sides of a wall with their
pictures, a sector's floor and ceiling, a sprite with its type and data captions) and the status bar (cursor
coordinates, grid, zoom, the map and its format). The view keeps Build's orientation (x right, y down).

**Units** (Preferences > Editing): positions, heights and the grid are shown and typed in **game units** by default,
Chosen's own (16 Build units across, 256 up and down; heights go up: a floor at 32 is 32 units above one at 0), or in
**Build units** as the .MAP stores them and NBlood and xmapedit show them (z growing downward). The map keeps Build's
integers either way. A number field takes a relative value as UDB's do: `++1` raises each selected floor by one unit,
`--8` moves down 8. Repeats, panning, slopes, angles and shades are Build's numbers in both.

## View modes (the View menu)

As UDB's: **View Wireframe** (lines only), **View Brightness Levels** (each sector filled with its floor's shade),
**View Floor Textures**, **View Ceiling Textures** (each sector filled with its floor's or ceiling's picture, mapped as
Build maps it: a texel 16 Build units, relative to the first wall, swapped, flipped, panned; through its palette, by its
shade; a room in a room or over a room drawn over it), **Next / Previous View Mode** (by default View Floor Textures); **Sprites as Pictures** (on by default) draws
each sprite's picture under its marker (one facing or on a wall standing at its full size, a floor sprite lying turned
by its angle; an invisible sprite keeps its marker alone). `src/View/SectorFill2D.cs`; kept in forge.cfg; no keys by
default (UDB has none: Preferences > Keys). **View > Grid Over Floors / Ceilings (2D)** draws the grid over the filled
sectors (light lines, every 64 units stronger) instead of under them (`MapView2D.GridOverFill`, forge.cfg `gridoverfill`).
In Sectors mode the selected sectors are tinted red under their outlines (`MapView2D.TintSector`).

**View > Full Brightness** (UDB's Toggle Full Brightness, `togglebrightness`, **B**, in the 2D view and the 3D preview;
kept in forge.cfg `fullbright`) leaves the lighting out, to work in a dark map: the 3D preview draws every face, sprite
and the sky as bright as shade 0 up close, nothing darkened with distance (the faces' shader's `full_bright`,
`Preview3D.FullBright`), and the 2D view's floor and ceiling pictures and sprites' pictures by no shade
(`MapView2D.FullBright`; View Brightness Levels still shows the shades, being what it is for). The view only: the map
keeps its shades, and Play's lighting waves change them unseen. The dynamic lights go off with it too (Dynamic lights, below).

**View > Show Sector Effects (2D)** and **(3D)** (on by default, kept in forge.cfg, no keys) tell what each sector does,
as xmapedit's captions (`src/Blood/SectorCaption.cs`): its number, type, channels ("rx 5", "tx 12"), "On" (its state)
and "locked", and under them its effects: panning ("pan CF 40 @ 90°", the planes, speed, angle; "always": without
being switched on), lighting ("light flicker 2 +4": NBlood's wave, the amplitude added to the shade, so less is brighter), damage (NBlood's
type: a Damage sector's as stored, another's less one), underwater, depth, wind, bob (ProcessMotion), crush, key, what triggers it
("on enter/exit/push/wall push/hit", "once") and "no enemies" (not alone: most doors have it). A sector that does none
of it keeps its plain label. In the 2D view a box at the sector's label (its effects from 8% zoom); in the 3D preview a
label floating in the room a third down from its ceiling, hidden behind walls and where its sector isn't seen
(as a sprite), faded out far off. `--selftest` checks E1M1's.

## Links (UDB's event lines)

The highlighted element and the selected ones show what they are tied to, in the links colour over everything
(`src/View/MapView2D.Links.cs`; what is tied to what: `src/Blood/MapLinks.cs`):
- **channels**: a sector, wall or sprite sending a channel (its TX id, 100 and up: below are Blood's own) to every
  sector, wall and sprite receiving it (that RX id), an arrow at the receiver, the channel at the middle; a receiver
  highlighted shows its senders the same way;
- **links**: a room-over-room link's upper and lower markers (the same data 1), arrows both ways, "link n"; also
  shown when either room is highlighted;
- **markers**: a sector to the markers it owns ("off", "on", "axis", a teleporter's "destination").

**View > Follow Link Chains** (on by default) follows the focus's channels on: what each receiver sends in turn, to the
end (a sector entered, the switch it toggles, the two sounds the switch plays: Blood's logic is often such a run of
switches), and back to what sends to the focus; the chain's further links a little lighter. **View > Show All Links** draws every link, faint, under the focus's (kept in forge.cfg; no key by default). An end in a
hidden layer isn't drawn; both ends in a locked layer: dimmed.

## Layers (rooms over rooms)

Each sector is in one layer (`src/Document/Layers.cs`; its walls with it), and each sprite: its own (placed while that
layer was current, or moved into it), else its sector's; kept in the .MAPX's
`LAYR` chunk (the game ignores it; a .MAP can't hold layers, so saving one lists them among what is lost unless they
are the ones Forge made itself). The docker's **Layers** tab: a row a layer, its box showing or hiding it, its name
(typed into: renamed), **edit** making it the current layer, its sectors' count (and sprites', its tooltip), **<**
moving the selected sectors (every sprite in them and their markers with them, and the point lights in them) and the
selected sprites into it; in Lights mode (2D or the 3D preview) the selected lights (`MapDocument.MoveLightsToLayer`: a
point light into that layer's room at its place, as high over its floor as before if outside that room's heights; an
attached one with its sprite; one with no room of that layer there stays, the status says so); **New**, **Delete** (its sectors go to the first layer), **From Overlaps** (made again from the map).

- Only the **current** layer is edited: picked, selected, dragged, deleted, drawn on. New sectors go into it.
- Another layer that **shows** is drawn dimmed under the current one and is **locked**: not picked, and drawing goes
  over it as if it weren't there (not crossed, cut, split or made an island of: a sector drawn over it is a room over
  that room), except that a new sector may still join its walls where it lies against them (snapped to its points).
- A **hidden** layer is out of the way entirely: not drawn (2D, the filled view modes, the 3D preview), not picked, not
  snapped to or joined.
- Showing and the current layer are the view (undo leaves them); new, renamed, deleted layers and sectors moved are
  edits (one undo step each). Sprites placed where layers overlap go into the current layer's sector.

A plain **.MAP** gets layers when it opens (`src/Blood/AutoLayers.cs`): the sectors Blood links (an upper link marker,
types 7 9 11 13, in one, a lower one, 6 10 12 14, with the same data1 in the other: a room over a room seen through a
plane) form a layer each group ("Link 2: 65, 90"; its ends lying over each other: "... above"); of the rest, those lying
over others go into **Above 1**, **Above 2**... by floor height, the lowest staying in **Level**. No layer then holds two
sectors sharing a place. E1M1: Level 141, two link layers (three layers), Above 1 10; DM1: Level 260, Above 1 24, Above
2 2 and a link; E2M1 has fifteen links (mostly water).

## 3D preview (Q)

Q (the View menu, the toolbar's 3D) shows the map in 3D from where the cursor was in 2D, at eye height over the floor,
as UDB's visual mode: `src/View/Preview3D.cs` and `PreviewMesher.cs` (Forge's own: floors and ceilings filled over
their loops with slopes, walls in their upper, lower and masked parts pegged and repeated as Build maps them, shades,
sprites facing the camera, wall- and floor-aligned ones as their angle lies, placed as Build places them (z the
bottom, real-centred the middle; moved by the tile's and the sprite's offsets: two halves of a gate side by side; a
wall or floor sprite lifted a quarter of a game unit off the surface it lies on, to its sector's side, as the game lifts
its depth: no flicker against the wall); every surface and sprite drawn through its palette (Blood's PLU lookups by
pal: a wall recoloured, a secret door's face a shade apart from the walls beside it); walls mapped as Build maps them
(yrepeat / 8 texels a game unit up, xrepeat * 8 across; an x-flip by xrepeat * 8 + 2 * xpanning,
swapped bottoms with the wall behind's picture, panning, alignment and y flip, a picture whose height isn't a power of
two repeating at the next one, the rows past it read from the next column as classic Build and the game do); a masked
or one-way wall's overpicnum seen from its own side only (the wall behind draws its own);
Blood's linked planes (tile 504, a room over a room) left open so the room beyond shows; the parallax sky round the
view as Build lays it out (the map's 1 << sky bits segments, segment i the sky tile plus the map's offset i, the
horizon where the sky ceilings' y panning puts it); Blood's view of 90 degrees at 4:3, wider on a wide window). It
draws what the game draws, as Build does (`src/View/Preview3DPortals.cs`, every frame): from the camera's sector (the
one holding it whose floor and ceiling hold its height) through each red wall facing the camera whose opening shows
within the opening it was reached through, into the sector behind; through a Blood link's plane (504) into the linked
room. Each sector seen is drawn only inside the screen rectangles of the openings it was seen through (up to four;
past four the last grows round the rest, its plane dropped, and the openings beyond are looked through in all of it, as
it is drawn) and behind their planes (its faces' shader discards the rest), a sprite only where its place is: a room over a room or in a
room shows where the game shows it (E1M3's lower floor under its upper one). Outside every sector (flying through a
wall) everything is drawn. The moving sectors as Edit, Off or On shows them, or played (below). The mouse looks
(captured), W A S D move along the view (look up or
down to rise or sink), Shift faster; Q or Esc goes back, the 2D view then centred where the camera
was. Undo, save, F9, F3, F4, P and the dialogs' keys work in it; it is rebuilt when the map changes. F9 (Test Map)
still shows exactly what the game draws.

## Editing in the 3D preview (UDB's visual mode)

`src/View/Preview3DEdit.cs`. What the crosshair (the view's middle) is on is outlined in the highlight colour, over
everything: a sector's floor or ceiling (seen from inside it), a wall's upper, lower or whole part (from its sector's
side; a masked wall's middle), or a sprite (its picture's rectangle, as drawn); the info panel shows its sector,
wall or sprite. A click selects it (tinted red),
again: no longer; C clears the selection. The wheel raises (up) or lowers (down) the selected floors and ceilings, else the one aimed at, by
Build's PgUp step (1024 Build z, 4 game units; Shift 256, Ctrl 4096), and sprites up and down (in Edit); on a wall it
does nothing (UDB's default):
- in **Edit** the heights as stored;
- in **Off** / **On** a moving sector's heights for that state (its x-sector's Off / On floor or ceiling, as
  xmapedit's Alt+F3 / Alt+F4 capture, by moving the plane itself; a plane not moving yet starts both from its
  stored height); a sector that doesn't move keeps its heights there (the status says so);
- not while playing.

A **right click** opens **Texture Settings** (T, below) for the selection (when what the crosshair is on is selected;
else for that alone): its pictures are picked from there. The tile picker alone (`MainWindow.PickTexture`: the first
target's picture, a lower part with swapped bottoms the other side's as Build draws it, a masked middle its mask;
`visualtile <tile> <targets>`, one undo step, the tile looked at shown on the surfaces straight away, the picker at the
window's right) opens for a sprite just placed.
**Ctrl+C** copies the picture of what the crosshair is on and all its settings (`visualcopy f12`: panning, repeats,
shade, palette, flags; `TextureOps.Clip`), **Ctrl+V** puts them on the selection (else on what the crosshair is on;
`visualtex paste <clip> <targets>`): a surface of the same kind (a wall, a floor or ceiling, a sprite) takes the
settings too, a wall its x repeat scaled to its length (the picture keeps its size), another kind the picture alone.
An opening's Masked wall settings (below) stay with its wall: a clip leaves them out. The crosshair is on a masked or a
one-way picture (the latter from its own side). **Space** opens the context menu (below). The mouse is let go for a menu or a dialog and
taken back when it closes.

### Textures in the preview

`src/MainWindow.Textures.cs`, `src/Tools/TextureOps.cs`. On the selection (as the wheel), else what the crosshair is
on; each key a command (`visualtex <op> ... <targets>`), a run of the same key on the same surfaces one undo step:
- **arrows** move the texture (Shift 8, Ctrl the grid size; UDB's): a wall's panning (Build's xpanning in pixels,
  ypanning in 256ths of the picture's height; an x-flipped picture the other way along the wall, a y flip changes
  nothing), a floor's or ceiling's;
- **keypad 6 / 4, 8 / 5 (2), 9 / 7**: X, Y, both repeats up / down (Shift or Ctrl 8; a sprite's size);
- **arrows on sprites** move them (UDB's), by the grid size (Shift an eighth of it): a sprite across the map along
  the map axis nearest to where the camera looks, a wall sprite along its wall (left and right as seen) and up and
  down it; each into the sector now holding it (`visualmove dx,dy,dz:<sprite> ...`; in Edit; the rest of the
  selection pans);
- **keypad + / -**: brighter / darker (shade; Shift or Ctrl 8), walls, floors, ceilings and sprites, from -64 to 63:
  Blood keeps -128..127, but 63 is black and below 0 is as bright as 0 up close (a negative shade only keeps a
  surface bright farther off, against the distance's darkening: the preview draws it, below; a map of visibility 0
  has none); a value already beyond is kept or brought back;
- **F** flips (none, x, x and y, y in turn); **O** a wall's align bottom, a floor's or ceiling's relative alignment;
  **Shift+R** resets (panning 0, Build's repeats for the wall's length, no flips); **Shift+C / Shift+V** copy and
  paste the offsets (panning and repeats);
- **auto-align** (UDB's, by Build's rules): **Ctrl+A** X and Y, **Alt+A** X, **Alt+Shift+A** Y. From the wall aimed at
  along the walls joined at its corners, both ways and into the neighbouring sectors' walls at a corner, each wall
  part with the same picture: X carries the panning on where the wall before ends (its x repeat kept at the start's
  pixels per unit of length, the start's x flip), Y puts a picture row at the same height (the start's y repeat and
  y flip; each part measured from its own z: a white wall's ceiling or, aligned to the bottom, floor; an upper part
  the next ceiling; a lower part the next floor; a masked middle the lower ceiling). A masked or one-way picture
  aligns along the masked openings next to it only, a wall's picture along wall parts only. With several selected, only
  those. A masked picture's offsets, repeats, shade, palette and flips are its wall's own (Blood keeps one set a wall:
  the engine draws the mask with them), so the keys and auto-align on one move the wall's picture above and below the
  opening on that side too, and the status line says so; each side of an opening is its own wall (its own offsets),
  and a lower part with swapped bottoms (cstat 2) takes the wall behind's. `--selftest` measures it on Blood's maps (pairs of walls their designers aligned: y 99% the same, x 67%);
- **floors and ceilings**: **Ctrl+A** on one carries its picture on into the sectors joined to it by red walls whose
  floor (or ceiling) has the same picture, each in turn from the ones it touches, so no seam shows: each keeps its
  own relative-to-the-first-wall setting (a moving sector's stays) and takes the start's double size, the swap and
  flips that turn its picture the way the start's runs, and the panning that lays the picture where the start's lies
  (`TextureOps.AlignPlanes`; with several selected, only those). One relative to a first wall that no quarter turn
  or flip brings in line with the start's picture is left (Build turns a floor's picture only with its first wall).
  **Align Floor / Ceiling to This Wall** (the Space menu on a wall, or one wall in 2D) makes the wall its sector's
  first wall and the plane relative to it, panning 0: the picture's edge along the wall from its start (a sloped
  sector refuses unless the wall lies along its first wall: its slope turns about that wall). In 2D the Space menu's
  **Auto Align Floors / Ceilings** does Ctrl+A over the selected sectors from the one under the mouse.
- **T** (or the menu's **Texture Settings...**, or a right click): a tab for each kind of surface among the targets
  (**Walls (3)**, **Floors (1)**, **Ceilings**, **Sprites**; no tabs for one kind), each with its pictures, shown
  with Pick... beside them (a wall's, and a two-sided wall's **Masked picture**: `TextureOps.PictureKeys`), then the
  panning, repeats, shade, palette (Pick...) and flags, a two-sided wall's **Masked wall** rows (`TextureOps.MaskKeys`:
  **Opening** open, masked on both sides (xmapedit's: the other side's masked picture the same, x-flipped against this
  side's, so it reads alike) or one-way (a solid picture seen from this side; with no masked picture yet the wall's own),
  **See-through** solid, translucent or reversed, **Blocks the player**, **Stops shots**; both sides), and Blood's **Scrolling** (`TextureOps.Scrolling`: a wall's speeds across and up and
  always, its x-wall's; a floor's or ceiling's on / off, speed, angle and always, and a floor's drag, its x-sector's,
  the speed and angle shared by the floor and ceiling; an x-structure made only when a value changes), each as the
  tab's first surface has it: a field its surfaces differ in says **Multiple** (a flag "(multiple)") and is written
  only when changed, then onto all of them. What is changed goes onto that kind only (`visualtex set
  w.panx=8,f.tilefloor=21,...`: a key prefixed with its kind, `TextureOps.ForKind`; with the picture too, `w275.panx`,
  only onto those showing it), shown in the preview as it is changed.

### Slope mode (UDB's slope handles, as Build's slopes are)

**H** (or the context menu's **Slope Edges On / Off**; `visualslopeedges`) turns slope mode on (on any plane shown,
whichever layer is edited: the 3D preview edits what it shows). Build slopes a plane
only across its sector's first wall (z at that wall's line, heinum the rise across it), and the mode works that way:
aimed at a floor or ceiling, it draws that **plane** (`Preview3DEdit.SlopeFrame`): a rectangle from the sector's
**first wall** (green) to the **far edge** (cyan: parallel to the first wall through the sector's farthest corner,
`SlopeOps.FarLine`), as wide as the sector reaches along the wall, filled where it slices the room (depth tested), its
outline, lines across it at even heights and an arrow from the first wall to the far edge over everything. Aimed
anywhere on it, through the walls too (the far edge mostly lies outside the room), the **half under the crosshair**
is the handle (drawn brighter); a strip along either edge keeps the aim on the plane shown over whatever lies in front,
elsewhere a floor or ceiling under the crosshair is aimed at instead (Forge report 20261105-181409-182821: the handles
were thin lines to aim at, the far one mostly behind the walls). A **click locks onto that half** (it and its edge turn red, apart from the half only aimed at; kept wherever the
crosshair goes; clicked again, or a click on no plane: unlocked; clicks select nothing in this mode) and the wheel
moves it (Build's steps: a notch 1024 z, Shift one game unit, Ctrl four notches; nothing locked: the wheel does
nothing; the status gives the slope in degrees): the **far half tilts the plane about the first wall**
(`visualslope <dz> <f12|c12> <x1> <y1> <x2> <y2> tilt`, the first wall named by its ends, `SlopeOps.Tilt`), the
**first wall's half raises that side** with the far edge kept (`... ` without `tilt`,
`SlopeOps.RaiseEdge` on the first wall). Neither changes which wall is first: only **Shift+click** does, on the wall
nearest the crosshair (drawn white, brighter while Shift is held; `visualslope 0 ... first`: the plane kept through that wall's middle and the far
corner, the far edge then locked), so set it first, while the plane is flat, then tilt. (A floor or ceiling drawn
relative to the first wall turns with it, as Shift+F's does.) A run of notches on one handle is one undo step; brought
back level, the slope bit goes off. Only that sector's plane moves (the sector beside the edge keeps its own: a step
where they part). H again: the wheel raises and lowers floors and ceilings as before.

### Placing sprites in the preview

**I** (or the menu's **Place Sprite Here**; `src/MainWindow.Place.cs`) puts a sprite of the Sprites tab's type
where the crosshair is: on a floor standing on it (turned to the camera: an enemy facing whoever placed it; aimed at a
sprite: the floor under it), on a ceiling hanging from it, on a wall flat against it (wall aligned, facing into the
wall's sector, its middle at the crosshair, kept between floor and ceiling): decals. A type with no picture of its
own (a decoration, a gib object) takes the Sprites tab's decoration picture (with its usual size and lie), else the
picture Ctrl+C copied, else the tile picker opens on it (the picture shown
on it as it is picked); otherwise "edit new sprites" opens its properties. One undo step (`visualplace <type>
floor|ceiling|wall x y z sector angle [tile]`); in Edit. Its height is Blood's (`src/Blood/SpriteExtents.cs`, db.h's
GetSpriteExtents): centred (cstat 128, as every sprite of Blood's maps), its top and bottom from the picture's height
and the tile's y offset, the bottom on the floor (where a thing settles in the game), the top at the ceiling, the
middle at the crosshair on a wall; the 2D view's new sprites stand the same way, and a sprite's picture changed in the
preview keeps its bottom (a wall sprite its middle). **Delete** deletes the selected sprites (else the one aimed at;
`visualdelete <sprites>`). **Stand on Floor** (the menu, 2D or 3D) makes sprites placed otherwise stand that way
(`standonfloor <sprites>`: a thing at the floor's height but not centred settles half its height off it in the game).

The keys are keys.cfg's `visual...` actions (they count in the preview only, before a 2D binding of the same key);
the arrows and the keypad are built in; held, those repeat (one undo step), the other keys don't.

### Shade and panning as the game shows them

A shade darkens as in the game (`PreviewMesher.Shade`, the game's `build_shade`): (1 - shade / 64) to the power 2.2
in linear light, as Blood's shade table darkens the palette's colours; black from 64 on (Build keeps no shade past
63), no brighter than 0 below it. The 3D preview adds Build's darkening with distance first, as the game does: the
depth times the map's visibility / 65536 shades (a sector's own visibility v scaling it by (v + 16) / 16), so a shade
below 0 keeps a face bright that far further off (faces: the shade carried in the vertex colour, `PreviewMesher.ShadeCode`,
the faces' shader in `Preview3DPortals.cs`; sprites each frame they are seen). A new map has Blood's visibility
(800; its maps have 752 to 800); visibility 0 darkens nothing with distance, so shades below 0 look as 0 (Map
Analysis warns, its fix sets 800). Blood's panning floors, ceilings and walls move (`src/Blood/PanMotion.cs`, as
NBlood's `DoSectorPanning`: 30 game frames a second, an x-sector's pan velocity along its pan angle, an x-wall's x and
y velocities, over the picture's size): those that pan always, or are on (their stored state, or everything in
**On**); only their sectors are made again as they move.

### Live previews

Every dialog that changes the map shows the result before Apply: the **Properties** dialogs (a wall's repeats, a
sector's heights or pictures, a sprite's angle...), **Texture Settings**, the **tile picker** and the tools' option
dialogs (**Curve Walls**). The 3D preview (and the 2D view) show a copy of the map with the dialog's values, made
again a moment after each change (`MapDocument.TryOn`: the fields applied the way Apply will, the map put back at
once, not marked changed; `MainWindow.ShowToolResult`; with the moving sectors shown Off or On, the copy posed the
same way); a tool's Cancel leaves the map as it was.

**Settings dialogs apply as they close** (`MainWindow.ApplyOnClose`): the Properties dialogs, Texture Settings, a light's
dialog and Map Options have one button, **Close**, and closing them in any way (Close, the window's X, Esc) applies
what was changed, one undo step (Undo takes it back if it was wrong). The tools that build (Make Door, Door Properties,
Curve Walls, Add Floor, Make Portal, the gradient) keep their Apply and Cancel. The session records a closed dialog as
applied ("dlg"); an older session's "dlgcancel" still replays as a cancel.

**Sync Selection** (View; on by default) keeps the 2D view's selection and the preview's in step: a sector
selected in 2D is its floor and ceiling in the preview, a line its walls' parts, a sprite itself; entering the preview takes the 2D
selection, a click or C there gives the 2D view the sectors of the floors and ceilings selected and the lines of the
walls (its points and sprites kept). A click is a command (`visualselect f12`, `c12`, `w40u` / `l` / `m`), recorded
and replayed.

A run of wheel notches on the same floors and ceilings in the same state is one undo step (another change, an undo,
other planes or another state start a new one); each notch is a command (`visualz <dz> f<sector> c<sector> ...`),
recorded and replayed.

## The context menu (Space) and its tools

**Space** tapped in the 2D view (let go soon, without dragging the view; held, it pans) or pressed in the 3D preview
opens a menu at the mouse cursor (in the preview the mouse is let go at the crosshair) with the tools for what is
selected: the 2D view's selection of the mode's
kind, else the highlighted item; the preview's selection when what the crosshair is on is in it, else that alone.
`contextmenu` binds it to another key too; `tool <name>` runs one tool by its name (`tool curve walls`; on the
selection first). Tools that change the map's shape are left out while the moving sectors are shown Off, On or
playing. The tools:
- **Change / Copy / Paste Texture** (3D): as the right click, Ctrl+C and Ctrl+V.
- **Split Walls**: each wall split at its middle (`splitlines <lines>`).
- **Add Floor...** (the context menu over sectors: in 2D the selected ones, in the 3D preview the selected floors' and
  ceilings'; `src/Blood/RoomLinks.cs`, `addfloor <above|below> <same|beside> <layer> <plain|water|goo> <height>
  <sectors...>`, one undo step): an exact copy of each sector (its walls and settings, not its sprites) **Above** (its
  floor the sector's ceiling plane, its ceiling that raised by the height: 0 the sector's own; the ceiling's look kept,
  a sky stays a sky) or **Below** (its ceiling the sector's floor plane), at **the same place** on a layer (a new one,
  or one chosen apart from the sectors' own: on theirs it is refused; the other layers hidden while it is made, so it
  joins none of their walls) or **beside the map** (clear
  of it to the east), each linked to its sector as Blood links rooms over rooms (NBlood's warp.cpp, mirrors.cpp): an
  upper marker in the upper sector at its floor, a lower one in the lower at its ceiling, one new number (data1) on both,
  the upper floor and the lower ceiling tile 504 (no parallax): the game draws and moves across that plane by the
  markers' offset. **Link**: plain (stack markers 11 / 12, as E1M1's), water (9 / 10, as Blood's: the surface water
  picture 2915 both sides instead of 504, the lower room's x-sector Underwater, which every lower water marker's sector
  in Blood's maps has) or goo (13 / 14, the lower room underwater); water or goo under a room underwater already is
  linked as Blood links two rooms under water (E4M4's links 13 and 25): stack markers and 504, no surface between them,
  the lower room underwater too. The dialog remembers nothing: each time Above, the same place, a new layer, plain, the
  same height (a water or goo link left from the last time flooded the selected room under a floor added above it,
  water entered sideways from its neighbour: floating in the air). Refused where a sector's floor or ceiling is linked
  already (Blood keeps one link each way). The copy's layer is then the one edited. A linked room deleted takes its
  link markers and their partners in the other room with it, that room's linked plane given back a picture (the
  deleted room's other plane's: an Add Floor copy has the original's) and, under water, its Underwater flag cleared
  (`RoomLinks.Unlink`, from `DeleteSector`).
- **Make Portal...** (the context menu over two sectors; `makeportal <kind> <a> <b>`): the two linked, the one whose
  floor is higher above the other (its floor and the other's ceiling made 504), the markers placed so their offset lays
  one room's middle over the other's.
- **Make Portal...** over two walls (Walls mode; `makewallportal <line> <line> <front|back>`, `RoomLinks.MakeWallPortal`):
  NBlood's wall stack, a window onto another place: two red walls of the same length, parallel, the first's side chosen
  in the dialog (the bigger room's at first) and the second's running the other way, both type 501 with one stack number
  (x-wall data, shared with the link markers' numbers: `NextLinkId`), the one-way picture 504, blocking moves and shots;
  each shows the room behind the other, shifted by the walls' offset, not turned (the game draws it; not yet the 3D
  preview). The dialog lists the rules (`RoomLinks.WallPortalRules`: two-sided, the same length, parallel, not a portal
  already), each met or not with what to change, and Link stays off until all are met. **Make Mirror** (white walls; `makemirror <lines>`): Blood's mirror picture 504.
- **Make Door...** (the context menu over one sector; `makedoor <slide|swing|single|up|down|split|pivot> <side> <thickness> <lip> <inset> <picture>
  <busy> <wait> <sounds> <sector>` and then the rest named, `edge=<tile> sounds=<a,b,c,d> by=push|channel channel=<n>
  key=<0-6> once=0|1 hinge=0|1 carve=0|1`, a recording without them E1M1's sounds or none and one picture for faces and edges;
  `src/Blood/Doors.cs`): a doorway (four walls in a rectangle, two opposite red ones to
  the rooms, two white jambs; anything else is refused with why) made a door as E1M1's, stored open as Blood stores
  doors (trInit closes it; the toolbar's **Off** shows it closed, **P** plays it), the 3D preview showing the result
  while the dialog is open. A **sliding pair** (sector type 614, E1M1's sector 4): each jamb notched for a leaf (out
  into the wall along a channel of no width, back to the leaf's edge the **lip** out of the wall, across it, out and
  back), the leaves' walls moving (cstat 16384 one, 32768 the other), marker 0 where they meet, marker 1 a leaf's
  travel off; refused when the wall beside the doorway isn't solid that deep in the doorway's own layer (the wall measured: the lip that would fit, Forge report 20261107-105619; a room on another layer there is a room over a room, elsewhere in the game, and doesn't count). The swinging pair works in whichever layer its room is, whatever layer is being edited. A **swinging pair** (617, E1M1's sectors 50
  and 51): two leaf sectors in the room on the chosen side (**Swings into** names each room with the way it lies from the doorway, north, east...; **Reverse** swings them into the other), hinged at the doorway's corners the **inset** into the room (moved in from the corners, the leaves that much shorter, where the room's walls run through them: a corridor as wide as the doorway),
  floor and ceiling at that room's ceiling (solid), closed across the opening and turned a quarter into the room to
  open (each axis marker's angle), the first linked to the second (kCmdLink on a second channel); refused when the room
  can't hold their swing. A **swinging leaf**: one leaf as long as the doorway is wide, **hinged at** the left or right
  jamb (seen from the doorway, looking into the room it swings into), no link. A door that **lifts up**, **lowers down**
  or **splits** (the doorway itself, type 600, as Blood's levels have about 600, 170 and 130 of them): closing to its
  floor, its ceiling or its middle, open at the heights it has, stored closed as Blood stores them (On in the toolbar
  shows it open); its faces are the rooms' walls facing it (the picture once across and once up them, a split once on
  each half, moving with it: cstat 4 off), its jambs (the doorway's own walls) held still (Build pegs a white wall to its sector's ceiling, to its floor with cstat 4: aligned on the bottom lifting up or parting, to the ceiling lowering; Door Properties sets them on a lift made before), its edge its bottom (the doorway's ceiling) or top (its floor: bars, 211,
  over dark stone, 379, by default lowering, as Blood's have most); pushed through those walls (the sector's Wallpush,
  its key and once there, as Blood's are); refused when sloped or under 2048 z high. A **pivot** (615, kSectorRotateMarked):
  a leaf in the doorway (a pillar of white walls, all marked to turn, Document.AddHole) across the opening less 16 at
  each jamb, turning a quarter about its middle (the axis marker), stored open along the way through; it turns inside
  the doorway, so the doorway must be about as deep as it is wide: draw it square, seen from above (refused with the depth to draw it: its leaf, as wide as the opening, sweeps a circle that would reach into both rooms of a thin doorway, and Build moves a sector's walls only within it; Blood's own 615 doors stand in sectors as big as their sweep); or **Carve the room a pivot turns in**: from each threshold a strip as wide as the doorway and as deep as the turn needs is cut out of the room (Document.SplitSector along the threshold's corners, out and across) and joined to the doorway (JoinSectors: the doorway's floor and ceiling, so a doorway lower than its rooms makes a low room for it); refused where a room doesn't hold the strip. A carved pivot's shape stays (Door Properties changes its settings; undo takes the carving back). Both: the **opening time** and an optional **close by itself after** (Blood's tenths of a
  second). The dialog's tabs:
  - **Look**: the leaves' **faces** and **edges** (their ends), each a tile with its Pick... and a picture of it
    (-1: E1M1's for the kind).
  - **Sounds**: a set as Blood's levels have them on their doors (E1M1's for the kind, stone slabs, sliding doors,
    creaks, wooden doors, machinery: the most used on Blood's 614/617/600 sectors), none, or your own. The four of a
    sector sound sprite (709): opening starts, opening stops, closing starts, closing stops (data1..4: SectorStartSound
    plays data1 going on, data3 going off; SectorEndSound data2 and data4), each with the sound picker; a sound changed
    by hand makes the set "your own".
  - **Opening**: **opened by** the use key on the door (its faces push it: toggle on its channel; each wall's own
    state flips and sends on both changes) or **only its channel** (no wall of it pushes: a switch or a trigger
    elsewhere sends On, Off or Toggle). The **channel** (0: the first free from 100; Pick...: the map's channels; not
    remembered, so the next door gets its own), the **key** the use key needs (on the pushed walls, as player.cpp asks
    the wall: "That requires a key."), and **used once**: the walls send On and are spent (triggerOnce), so the door
    opens and stays open (no closing by itself).

  The dialog says why it can't as the options change (a tool dialog's line under its form, from its preview). Multipart doors (each part its own way) are still to come; a revolving door (a cross of leaves) isn't made: Blood's turning sectors have two places, and a cross a quarter turned blocks the way as before.
- **Door Properties...** (the context menu over a door, in place of Make Door: a sliding sector, 614; a leaf of a
  swinging pair, 617, or the doorway beside a pair Make Door made; `adjustdoor` with makedoor's words,
  `src/Blood/Doors.Edit.cs`): Make Door's dialog filled in from the door as it is (Doors.Find reads it back: its
  timing and channel from the sector, the faces and edges, the sound sprite, the walls that push it, their key and
  once; nothing remembered), Apply one undo step. Its settings change where they are (its pictures only when changed:
  a door of Blood's own may have faces of more than one; Opened by: only its channel takes the push off its walls,
  the use key puts it on its faces, a pair's room side). A changed kind, thickness, lip, inset, side or hinge takes the door
  back to its doorway (a sliding door's notches gone, each jamb one wall again; a pair's leaves dissolved into their
  room; a lift at its open heights; a pivot's leaf gone; their markers, sounds and x-sectors gone) and makes it again with the settings kept; only for Make Door's
  shape (each jamb notched as it notches them, the pair hinged as it hinges them): the shape fields of any other door
  are greyed with why, its settings still change. The door's sprites (markers, sound) are in its sector's layer, not
  the one being edited.
- **Gradient...** (Tools > Gradient..., the context menu over two sectors or more: in 2D the selected sectors, in the
  3D preview the selected floors and ceilings; `src/Tools/SectorGradient.cs`): from the first sector (in the order
  selected) to the last, **What** goes from **From** (the first's value) to **To** (the last's; **Reverse** swaps
  them): the floor's height, the ceiling's, the floor and ceiling together (the room's height kept: stairs with their
  headroom), or the brightness (shade) of their floors, ceilings and walls (each a check box). **Spacing**: equal
  steps (stairs: in the order they lie on the way from the first sector's middle to the last's) or by distance (how
  far along that way each middle lies); heights rounded to whole game units if asked. One undo step
  (`sectorgradient <floor|ceiling|both|shade> <from> <to> <equal> <round> <shade parts> <sectors...>`); the 3D preview
  shows it as the values change.
- **Curve Walls...** (UDB's Curve Linedefs): **Points** (new points per wall), **Shape** (an arc: part of a circle;
  parabola; sine bump; S-curve; zigzag), **Depth** (at the deepest, in the units shown), **Reverse** (by default the
  curve bends out of the first wall's sector: Build's walls have their sector on the right) and **Bend connected
  walls as one**: a run of selected walls joined end to end becomes one curve from its first point to its last, its
  corners moved onto the curve and the points spread along all of it (a run that closes or branches: each wall on
  its own). The curve is drawn over the 2D view while the dialog is open, and the 3D preview shows the map as it
would be (the dialog stands in the view's corner); the settings are remembered
  (`tool.curve.*`); Apply is one undo step (`curvelines <points> <shape> <depth> <reverse> <as one> <lines>`). A red
  wall's other side gets the same points.
- **Snap to Grid** (2D, **Shift+G**, UDB's): the selection's points (a wall's two ends, a sector's corners) or
  sprites to the nearest crossing of the grid shown (`snapgrid <grid> <kind> <items>`, one undo step; another
  layer's points stay; the map rules checked after: points snapped onto each other are reported).
- **Texture Settings...**, **Auto Align Textures / X / Y**, **Flip**, **Reset Texture**, **Copy / Paste Offsets**
  (3D): as their keys; **Auto Align Floors / Ceilings**, **Align Floor / Ceiling to This Wall** (3D and 2D).
- **Join / Merge Sectors** (two or more sectors selected), **Properties...** (recorded as `properties sector 3,4`, so a
  replay opens the dialog its changes were made in), **Delete**.

Each tool is a few lines in `src/Tools/BuiltinTools.cs` registered with `ContextTools` (`src/Tools/ContextTools.cs`:
its group, when it applies, what it runs; the menu shows its key from keys.cfg); a tool with options builds them with
`ToolDialog` (`src/Tools/ToolDialog.cs`: numbers, choices, checks, lengths in the units shown, remembered in
forge.cfg; `Preview`: the paths it would make, drawn in the 2D view; `Change`: its change done to a copy of the map,
which the 3D preview shows, `MainWindow.ShowToolResult`) and ends in a window command, so sessions record
and replay it. The geometry is in the document (`src/Document/CurveOps.cs`: `PlanCurve`, `ApplyCurve`, and
`InsertPoints`, a wall split into a chain of points on both sides).

## Moving sectors: Edit, Off, On, Play

The toolbar's **Edit / Off / On** (View > Moving Sectors; F3 Off, F4 On, again: back to Edit, as xmapedit's show
off / show on) choose where the 2D view and the 3D preview show Blood's moving sectors (`src/Blood/SectorMotion.cs`,
NBlood's trInit, TranslateSector, ZTranslateSector):
- **Edit**: the map as stored, the one edited. A sliding or turning sector is stored at its ON place (Blood takes it
  back to OFF when the level starts); a door or lift at whatever heights it was left.
- **Off** / **On**: every moving sector at its off or on place: z-motion sectors (600, 602) at their Off / On floor
  and ceiling heights; sliding ones (614 the marked walls, 616 all) moved and turned from the off marker to the on
  marker; turning ones (615, 617) turned about the axis marker by its angle (and their heights too); the sprites that
  ride with them (8192, 16384, standing on the floor, Drag) moved along. A copy of the map is shown, the map itself
  is untouched; its shape isn't edited there (the mouse on the map and the shape commands say so in the status).
- **Play** (P; in the 2D view and the 3D preview, whichever shows, going on across Q): the level run from its start
  by Blood's events (`src/Blood/EventSim.cs`, NBlood's eventq.cpp and triggers.cpp; `src/View/MoverPlay.cs`) in a fresh
  copy: the level-start channel (7) sent On, then everything as the game does it: commands sent on channels and
  posted after wait times, toggle / one-way / combination / padlock switches, generators repeating, sectors' busy
  times and waves (z motion, sliding, turning, step turning, paths along their markers), trigger on / off sends,
  retrigger, stop, lock, trigger once, decoupled, links (a sector sending its busy each frame: what follows it moves
  with it). Each game frame also the sector lighting (sectorfx.cpp's waves added to the floor's, ceiling's, walls'
  shades, always or while busy), bob (ProcessMotion) and slopes (AlignSlopes: a sector's filler byte, the map's FogPal,
  naming the wall whose next sector its floor and ceiling meet); panning follows the sectors' states. The player is
  the 3D preview's camera: flying into a sector fires its enter trigger, out of one its exit; **E** uses what the
  crosshair is on as Space does in the game (a wall to push, the sector behind a wall pushed through, a sprite to
  push, a floor or ceiling's sector to push, else the camera's own sector; keys not asked for), and breaks a thing as
  an explosion does (those NBlood's thingInfo lets an explosion hurt: wall cracks, crate faces, barrels, glass,
  webs, grates, gib and explode objects...): its trigger sent Off as actDamageSprite sends it (E1M1's crack, sprite
  87, sends 111 and the wall behind it opens), the crack, crate face, gib object or explosive gone, an explosive
  breaking the things in its reach (explodeInfo's radius; walls between not asked). In the 2D view **E** uses what
  the mouse is on (the mode's highlight, else the sector under it; a sector nothing pushes but entering sets off is
  entered). Every thing starts on, as actInit sets it. `playenter <sector>` enters one, `playuse s87|w12|<sector>`
  uses one (tests, shots). Nothing else of the game runs: no enemies, damage over time, sounds, teleports or counters.
  E1M8's corridor (switches started by the level's start, each 0.4 s after the one before, their sliding strips
  going back and forth for ever) and E2M6's ice block (rising when entered, linking 152 and 175, tilting to meet
  152's floor, staying up), E1M1's crack broken (and the barrel beside it blown up, the crack with it) are
  `--selftest`'s. P again or Edit / Off / On stops it; only what changed is made again
  each frame. Off and On align the slopes too (the game does from its first frame).

Away from Edit, both views say so: a frame round them and a label at their top in the state's colour (Off blue, On
red, Play green, xmapedit's), the toolbar's button lit in it, the moving sectors' walls drawn in it in 2D.

## Modes and mouse

The mouse and keys are UDB's defaults. **Left button**: click to select (toggle), press on empty space and drag for a
rectangle (Ctrl: deselect; C clears the selection); in Sectors mode a **double-click** selects the sector and every
sector inside it (its rooms, theirs: as Ctrl+C and dragging take them; `ClassicMode.Within`), the rest of the selection
kept (Shift or not), **Ctrl+double-click** takes them all out; in Walls mode a double-click selects the wall and the
walls going on from it in a line, both ways (at each end the straightest, while it turns 45 degrees at most: a curve of
short walls followed, a room's corner not; `WallsMode.InLine`); press on an item and drag to move it (with the selection if it is
selected; grid snap unless Shift; Esc puts it back). **Right button**: click for the properties dialog of the selection
or the highlighted item (several at once: a field they disagree on is blank and left alone), drag to move; on empty
space it starts drawing lines there (Vertices, Walls, Sectors), or places a sprite (Sprites). **Wheel** zooms at the
cursor; **Space held** (or the middle button) pans, **Space tapped** opens the context menu (below); the arrow keys
scroll; Home fits the map.

**Tab** goes through the items lying at one place under the cursor (the status bar says how many): points, walls and
sprites as sectors (rooms over rooms have walls and corners on each other; a marker lies on another sprite).

- **Vertices** (V): a point is every wall starting at one place that moves together (both rooms of a red wall). With
  no point under the cursor, the place on the nearest wall where one would go shows (an outlined square, snapped: the
  grid's place projected onto the wall); a right press there puts the point in (the wall split) and, held and moved,
  drags it (UDB's; one undo step for both). Insert puts a point on the wall nearest the cursor; Delete (or Backspace) removes the selected points,
  else the highlighted one, the walls at it joining into one (refused where the walls face different rooms or more than
  two rooms meet; the status bar says which couldn't go). A triangle with that point as a corner closes up and goes
  (UDB's): the room on its other side then runs straight across, facing what the triangle's third wall faced. A point dragged onto another of its own loop is put back.
- **Walls** (L): a white wall, or a red wall and the wall behind it as one line with two sides. Walls that do something
  when used are drawn green (UDB's lines with an action, colour `actions`): a push, shot or touch trigger of their own,
  or a wall of a sector opened by pushing its walls (Blood's wall push: doors, secret doors). Insert starts drawing
  lines at the cursor; Delete deletes the selected walls, else the highlighted one: a red wall's two rooms are joined
  into one (the larger keeps its properties), a white wall collapses to one point at its middle (a loop stays closed:
  the walls before and after it meet there); Backspace makes a red wall white on both sides; J joins the two selected white walls lying on
  each other the other way into a red pair (rooms over rooms leave a choice of which to join); Shift+F makes the
  highlighted wall its sector's first wall (slopes are relative to it): of a red wall, the side the mouse is on; a
  hole's wall can be first too (Blood's maps have them).
- **Sectors** (S): the smallest sector under the cursor; **Tab** goes to the next sharing the place (stacked rooms,
  movers stored open). Dragging a sector moves what lies inside its outline with it (UDB's): the rooms in it, rooms in
  those, a room over it within it, the sprites in them all and their markers; the sectors against it stretch. A move
  kept (a drag, G) joins the white walls of the sectors that moved lying along another sector's white walls the other
  way into red walls, their points merged (UDB's merge on drop; a wall partly along the other split where the other's
  end falls; sectors of the editable layer only: `GeometryOps.JoinTouching`). **Separate Sector** (the context menu,
  Mode > Separate Sectors) does the opposite: the selected sectors' red walls on their outer loops to sectors not
  selected made white on both sides, so they move on their own (the rooms in them stay joined and move with them; a
  room separated from the sector round it leaves its hole; `GeometryOps.SeparateSectors`). Insert starts drawing lines; Delete deletes the sector (walls facing it turn white, its sprites
  go to the sector under them); J (or Shift+J) joins the two selected sectors, the walls between them going (UDB's
  merge; the one under the cursor keeps its properties, else the lower-numbered). **Backspace** (the context menu's
  Dissolve Sector) dissolves the selected sectors (else the one under the cursor) into the sector next to them, the one
  each shares the most wall with (a room: the sector round it), which keeps its properties: a room taken away leaves
  no hole (Delete leaves one).
- **Sprites** (T): drawn by alignment (circle facing, bar wall-aligned, square floor-aligned) and coloured by category.
  **Placing**: pick a type in the docker's **Sprites** tab (`src/Blood/SpriteBrowser.cs`: pictures under category
  buttons, All, Enemies, Weapons, Ammo, Items, Keys, Decorations, Things, Traps, Switches, Player starts, Markers,
  Sounds, Generators, Other; or search, a name or a number; its Size slider or Ctrl+wheel sizes the pictures;
  double-click goes to Sprites mode). **Decorations** holds
  the pictures Blood's maps use as plain sprites (type 0: `Blood.TileUse`): one picked places a decoration showing it
  with the size Blood's maps give it most and, where they mostly lay it flat on floors, flat (`SpriteTypes.Decorate`;
  `spritetile` in a session's state, `visualplace ... <tile> d` in the preview),
  then right-click empty space: the sprite appears there on the floor, follows the mouse while the button is held, and
  its properties open when it lands ("Edit new" in the browser turns that off). Insert places one at the cursor. A new
  sprite gets its type's usual picture, size, palette and flags (xmapedit's auto-adjust table in OBJTYPES.INI) and the
  status list Blood keeps that type in (enemies 6, items 3, things 4...: without it an enemy wouldn't act); changing the
  type in the dialog changes the picture and status list with it. Delete; Ctrl+Shift+wheel turns them; Shift+L points
  them at the cursor.
- **Draw Lines** (Ctrl+D, a right click on empty space, Insert in Walls or Sectors mode): click to add points (snapped
  to points and walls near the cursor, else the grid; Shift: no grid), Backspace takes one back, click the first point
  to close, Enter or a right click finishes a line, Esc cancels. A closed shape outside the sectors makes a sector; it
  may lie against them: where its corners meet a wall the wall gets a point, and the walls lying on each other become
    red pairs (free walls only: a red wall's both sides are taken). Drawn round sectors (islands; they may touch the
  outline at a point) it gets a hole round each group of them, joined to them. **Alt on the closing click** draws the
  shape as a sector over what lies there (a room over a room: Blood links them with markers): no islands, no splitting,
  it joins only white walls lying exactly on its sides the other way. A line drawn outside from a room's wall (a point
  or anywhere along a wall) back onto its walls, finished with a right click or Enter, extends the room: with the
  stretch of wall between its ends it makes a new sector there, joined by red walls (J joins the two into one room).
  Inside a sector (Tab: which, where rooms share the place), touching its walls at their points at most: a room in it.
  **Over sectors** (partly over them, across red walls, inside one along its walls, or crossing a wall of one poking
  into it), as UDB's drawing does
  (`src/Document/DrawAcross.cs`): the walls and the drawing get points where they cross, each sector the drawing runs
  through is cut along it (the part inside the drawing a sector of its own with that sector's properties; the larger
  part outside keeps the sector, its type and triggers), the drawing's part outside every sector becomes a new sector
  joined to the walls it lies against, sectors inside it its islands, sprites go to the part holding them; refused
  where rooms over rooms share the place (Alt). A line from one point (or wall) of a sector's outer walls to another,
  through it, splits it; a single point on a wall inserts a point. A drawing Forge can't make a sector of is refused;
  why stays in the status bar. **Draw Rectangle** (Ctrl+Shift+D): two clicks.

The properties dialogs: two fields a row; a type list showing the current type; a sprite's type sets its usual picture
and status list; a **Has an x-sector / x-wall / x-sprite** box adds or removes the x-structure (triggers, data); a
sector type that works with markers (slide, rotate, teleport) gets them, owned by it, in the sector (move them where
they go), and loses them with another type; sprites' Blood flags (pushable, moves with its sector, moves reversed),
offsets and hitag; a sprite's sector is the one of those sharing its place whose floor and ceiling hold its height.
Map Options sets the parallax sky's tile offsets too; a new map has Blood's sky, as every one of its maps does (sky
bits 4, offsets 0 to 15: the 16 tiles of a sky in a row from the sky ceilings' picture, 3678, 2500, 3491...;
parallax type 2: `MapDocument.BloodSky`).

**The map's own art** (Map Options > **Art**; `src/MainWindow.Art.cs`, `shared/Chosen.Blood/Files/MapArt.cs`): a list
of ART files, or folders of them, loaded over Blood's tiles in order (later over earlier), kept in the .MAPX (`ARTS`,
each path relative to the map's folder: a folder of ART files beside the maps can serve several of them; saved
elsewhere, the paths are rebased; Test Map's copy gets them absolute). Each set is **Added** (its tiles go over
Blood's one by one, its empty tiles leaving Blood's: new textures) or **Override** (its files replace Blood's files of
the same names whole, as an add-on's do: Death Wish's art folder). **Pack Pictures...** (`src/Blood/ArtPacker.cs`)
turns a folder of PNG, JPEG, BMP, TGA or WebP pictures into ART files: each pixel the nearest of Blood's palette's
colours 0..254, one less than half opaque see-through (255); the numbers free tiles, all in one run when one is long
enough: **Classic** in Blood's range (below 6144, not the tiles the games make while playing: 504, 4077..4095; for a
.MAP, which Blood, Refreshed Supply and NBlood play) or **Extended** (6144 to 30463, NBlood's user tiles; for a .MAPX,
NBlood and Chosen); a file a run of numbers (`tiles024.art` on: Build reads a file's whole range, so none spans a tile
it doesn't mean to replace); the output folder's `tiles.txt` keeps each picture's number (`tile name [xoffset
yoffset]`: edit the offsets for a sprite's picture), so packing again after a picture changed keeps every number (the
walls showing it keep it), and the output folder is added as a set. The buttons are recorded commands (`mapart
add|remove|kind|move|pack|reload ...`), each an undo step, done at once (not on Apply). A .MAP can't keep the list
(saving one says so, and counts pictures numbered 6144 and up): ship the ART files with it. The game loads a .MAPX's
sets the same way (`godot/README.md` ".MAPX"). A tile field's **Pick...** opens the tile picker (UDB's texture
browser): the Blood folder's tiles as pictures under tabs: **All** (every tile), **Walls**, **Floors**, **Ceilings**,
**Masked**, **Skies** (Blood's three sky sets), **Decorations**, **Unused**, **In this map** and **Map's art** (the
tiles of the map's own art, below). Blood's tiles have no
names, so a tab holds the tiles 141 maps use that way: Blood's 55 and its add-ons' (Cryptic Passage, Death Wish,
MARROW; but for the tiles an add-on's own ART files replace, another picture there: `src/Blood/TileUse.cs` reading
`data/tileuse.txt`, made by `maptool tileuse <maps glob>[@<add-on art dir>]...`; a decoration's picture one the maps use
as a plain sprite at least half as often as on walls, floors and ceilings). **Unused**: tiles no map uses that look
like textures (both sides a power of two from 16, no sprite views, not an animation frame or another tile's angle:
Refreshed Supply's extra art from 4608 up, mostly); the rest of the art (sprite angles, animation frames, the pictures
of enemies and items) is in All only; a field opens on its kind's tab (a wall's picture on Walls, a parallax
ceiling's on Skies; the 3D preview's and Texture Settings' pickers too), else the last one. **Material** narrows it to
a surface type (SURFACE.DAT: stone, metal, wood...), **Order** sorts by number (a set side by side) or most used first
(that tab's way), **Size** (or Ctrl+wheel over the pictures) makes them bigger or smaller (kept; the selected tile stays
in view, centred, else the first in view stays put; the sprite browser has its own: `src/Ui/ThumbGrid.cs`, each picture
made as it comes into view); a tooltip says a tile's size, material and how Blood's maps use it. Those the map uses are marked;
found by number, part of one or a range (`100-200`); double click or Use takes one. The tile picker opens where it was closed and as big (`picker.rect`, the 3D preview's own `picker.rect3d`; a place
no longer on a screen: centred; not saved in test runs). No dialog opens bigger than the screen it is on (`MainWindow.ScreenRoom`: its usable part, or the window when dialogs
are drawn inside it). A palette field's **Pick...** (walls, floors,
ceilings, sprites, Texture Settings) opens the palette picker (`PalettePickerDialog`): Blood's palette lookups, pal 0
NORMAL to 14 P4, each shown on the field's picture, those the map uses marked.

**R** turns the selection (or what the mouse is on) about its middle as the mouse goes round it (UDB's Edit
Selection mode's rotation), in 15° steps (Shift: free): what dragging it would move (a sector's walls, the rooms
inside it and their sprites; lines' and vertices' points; sprites) and the sprites' angles with it; a single sprite
turns where it stands (a single vertex can't: select two or more). One undo step: a click or Enter keeps it, Esc or the right button puts it back. **Drawing** (the left bar's section; `src/Editing/DrawModes.cs`): **Draw Circle**: two clicks give a box (its
corners snapped as drawing snaps), the circle an ellipse of that many sides in it (Left / Right or - / +: 3..64), its
points where they fall; **Draw Grid**: two clicks give a box cut into columns and rows of sectors sharing their walls
(A / D: columns, W / S: rows), all in one undo step (inside a sector each cell after the first lies along the one
before: cut out beside it, not a room, which `FitsAsRoom` refuses for a side along a wall); both drawn as any closed shape is (a new sector, a
room in one, or cut across). In Draw Rectangle, Circle and Grid the second click's keys shape the box: Shift draws it
from the first click as its middle, Alt makes it square, both together a square round it; Ctrl puts the corner off
the grid. **Align Grid** (the toolbar's button, on while it's the mode): a click on a vertex puts the grid's origin there, on a wall turns the grid
along it from its start, on empty space puts it back; snapping, the drawing boxes (Draw Rectangle's too: a turned
rectangle on a turned grid), G's X / Y and the scale box go by it.

**Shift+S** scales the selection (or what the mouse is on): a box round it along the grid's axes, a corner handle
dragged scales it evenly from the opposite corner, a side's across from the opposite side (the handle on the grid,
Shift: free); points and sprites' places scale. Dragged past the opposite side (a negative scale) it **mirrors**: the
sprites' angles mirrored as it goes, and when it is kept mirrored one way each sector wholly in it has its loops
turned round (Build's sectors wind one way: `GeometryOps.ReverseLoops`, the walls x-flipped so their pictures don't read
backwards; a red wall to a sector left out is cut); mirrored both ways is a half turn. A click off the handles or Enter keeps it,
Esc or the right button puts it back. In R, **P** puts the pivot at the mouse (on the grid, Shift: free). In G, **O**
turns the falloff on: the points joined by walls to what moves, within its radius (the wheel: bigger or smaller; a
ring shows it), move part of the way by the falloff's curve (Shift+wheel: smooth, sphere, root, linear, sharp, constant).

**Copy and paste** (Ctrl+C, Ctrl+X, Ctrl+V; `src/Blood/MapPaste.cs`): in Sectors mode the selected sectors with the
rooms inside them, their sprites and markers; in Sprites mode the selected sprites; in Walls mode the selected walls,
a drawing made beforehand (`CopyWalls`: chained where they meet, open lines and loops; `PasteWalls`): laid down as
drawing them makes them with what lies there now, the parts outside every sector cut off: an open line splits each
sector it runs through from wall to wall (`MapDocument.DrawLineAcross`; a stretch ending inside a room short of its
walls is left out, the status says so, and the points it put on walls go again), a loop all within the sectors a room
or a shape cut out of them, one reaching out of them a line round it; the new walls take the copied walls' look
(pictures, shade, palette, repeats for the length, panning, flags; a white wall's blocking, masking and one-way flags
dropped; both sides the front wall's, the side going the other way a red wall's back's) and are selected. Paste shows the outlines on the
mouse (its first point on the grid, Shift: free); a click puts them there as one undo step: each sector made again
(inside a pasted one or a sector of the map: a room in it, as a shape drawn there; within the map's sectors but
against their walls, a coffin by its room's wall: cut out of them as a drawn shape is, `MakeSectorAcross`, the parts
inside it joined into one; else joined to what it touches, or, only where none of that can be, laid over what lies
there, joined to nothing: the game and the 3D preview reach it from nowhere), its properties and its
walls' copied, its sprites with their x-sprites, markers owned by the new sectors and linked (a marker landing outside
every sector, as one beside its room pasted near the map's edge, keeps its own sector: Blood needs one); what was
pasted is then selected. A copied sector's holes that weren't copied themselves come back filled. The walls round the
pasted sectors (the sector each went into or joined: its walls, not the copy's) take the look of the walls that faced
the copied sectors where they were copied (a coffin's sides, seen from its room, are the room's walls): pictures,
shade, palette, repeats (x for the length), panning and flags, not their type, triggers or x-wall
(`MapPaste.OutsideLooks`). A plane with the slope bit but no slope (heinum 0) doesn't count as sloped when a first
wall moves (no "its slope turned" warning for a flat one).

**G** moves the selection (or what the mouse is on) with the mouse
(Blender's grab): what dragging it would move, its grabbed item's place on the grid (Shift: free); **X** or **Y**
locks it to the grid's X or Y axis through where it started (a red or green line shows it; the same key again frees
it). The axes and snapping go by the grid's frame (`MapView2D.GridOrigin`, `GridAngle`: 0 for now), so a grid moved
or turned later is followed. A click or Enter keeps it, Esc or the right button puts it back; one undo step.
Angles in
the dialogs are Build's (2048 a circle, 512 a quarter turn) with the degrees and the compass beside them; typing `90°`
(or `90d`, `++45d`) gives degrees.

**X-fields** (`src/Blood/XFields.cs`): the x-sector, x-wall and x-sprite tabs show every field of the x-structure
(none left out), grouped (Data, Triggers, Triggered by, Timing, Motion, Lighting, Panning, Effects, Where it appears,
Enemy, and last what the game sets as it runs), each with a plain label and its help (what it does, its valid range
from the bits Blood stores it in, modern-only): one-bit fields are check boxes (On, Send when on, Once, Locked,
Entered, Used, Shot...); fields with named values have a list beside the number (Command, Key, busy and light waves,
depth, damage type, respawn, palettes, explosions); times show in seconds, angles in degrees and the compass,
heights in the units shown, the difficulties a sprite isn't on as a check box each; RX and TX have **Pick...** (the
channel picker: the map's channels with what listens and sends on each, the game's own below 100, Next free) and say
who uses the channel; a sprite's data fields carry their meaning for its type (an ambient sound's radii in game
units and volume in percent, an enemy spawner's enemies and a drop's item as type lists, gibs, explosions, players),
a sector's and wall's Data theirs (a Damage sector's 0..1000). A value out of range shows red beside it.

**What it needs** (`src/Blood/Requirements.cs`): a single sector's, wall's or sprite's x-tab lists what its type needs
to work in Blood, each **ok**, **missing** or **?** (advice: it works without, likely not as meant), and colours the
fields they are about (green met, red missing, yellow advice): a turning sector its axis marker and an angle on it (how
far it turns: 512 a quarter turn; Fix sets 90°), a sliding one its off
and on markers apart and walls or sprites marked to move, a teleporter its destination; a moving sector something to set
it off (an RX channel, or Entered / Left / Used / Wall used / Shot), its off and on heights apart, a busy time; a path
sector a marker with its path id; lighting, panning and bob what they act on; a switch a TX and a way to use it, a
combination switch's goal below its max; an ambient sound a sound, Radius1 below Radius2 (else Blood skips it), a
volume and On or an RX (it plays only while on); a Sector SFX a sector with an x-sector; a generator a start (On
unlike Rests on, or an RX); a link its pair with the same id; an RX channel something sending it, a TX something
listening. **Fix** (markers, an x-structure) applies the dialog, makes it as an undo step of its own (`fixneeds`) and
opens the dialog again. Map Analysis (F11) lists every need missing in the map. Over Blood's own levels the rules find
107 things missing (dead generators and ambient sounds, half-set lighting): `--selftest` prints the most common.

**Sounds** (`src/Blood/SoundLibrary.cs`, `src/Ui/SoundPicker.cs`): a sprite's data field that holds a sound (the type
table's captions: a switch's On Snd / Off Snd, Sound, SFX; all four of a Sector SFX: off to on, stopping, on to off,
stopping) shows the sound's sample, length and loop beside it, **Play** (again: stop) and **Pick...**; the info panel
names the sample too. Pick... (and **Tools > Sounds...**, to listen) opens the sound browser: every sound of the Blood
folder's SOUNDS.RFF with its sample, length and what Blood's own levels use it for (BLOOD.RFF's maps read in the
background: "Sector SFX: Off->On x49 (E1M2, E1M3, E1M5 +10)", and this map's uses, marked), found by number, range,
sample name or use ("door", "switch", "E1M1"), shown all, this map's or the levels'; choosing one plays it (Play on
select), double click or Use takes it. Sounds play as Blood plays them: the format's rate times the sound's pitch,
looping from its loop start.

Every change is one undo step (Ctrl+Z, Ctrl+Y; whole-map snapshots, 100 levels). Copy and paste properties:
Ctrl+Shift+C, Ctrl+Alt+V. Map Analysis: F11. Keys are in `forge/data/keys.cfg` (UDB's action names and default keys);
Preferences > Keys changes them.

## Dynamic lights (Lights mode, K)

The map's dynamic lights, as the game adds them over Blood's shading (`godot/README.md` "The map's lights"; kept in
a `.MAPX`'s `LITE` chunk: `Document/Lights.cs`, `shared/Chosen.Blood/Map/MapLights.cs`). A `.MAP` has none of its own:
the game makes its lights from its sprites as it loads (torches and candles, lamps and other fixtures by their tile,
decorations drawn fullbright: `LightRules`). Opening a `.MAP` whose sprites give lights asks whether to make them here
(**Make the lights**: `generatelights`, one undo step; **Not now**: Tools > Generate Lights later), so they can be seen,
moved and changed, and a `.MAPX` saved from it keeps them. A map with lights of its own gets those alone in the game,
even all deleted; Generate Lights again adds only the ones for sprites without one (the moved and changed ones stay).
Saving as `.MAP` says it drops them.

- **Lights mode** (`LightsMode`, the mode bar's Lights, key K): each light a bulb in its colour where it shines from,
  its reach a faint circle, a line to the sprite it is attached to. Left click selects (a rectangle too), drag moves
  (grid snap unless Shift; an attached light moves off its spot and still follows its sprite), **right click on empty
  space** places a point light half way up the room (held and moved it follows; released, its dialog opens), right
  click on a light its properties, Insert places one at the cursor, Delete takes the selected (else the highlighted)
  away. Lights show and are edited with their sprite's layer (a point light: its sector's; where rooms of two layers
  lie over each other at its height, the one of the layer edited: `MapDocument.LightSector`).
- **Copy and paste** (`Blood/MapPaste.cs`, `MapClip.Lights`): Ctrl+C in Lights mode copies the lights (Ctrl+X cuts), and
  a copied sprite brings its attached light, a copied sector its point lights. Ctrl+V places them as anything else
  pasted (their bulbs shown under the cursor): a light whose sprite was pasted is attached to the new sprite (its offset
  kept); any other, a point light, or an attached one copied without its sprite (a point light where it shone from),
  lands as high over the floor there as it was over its own. The lights pasted are selected in Lights mode.
- **In the 3D preview** (Q from Lights mode, or **K in the preview**: Lights mode on there, K again back to the mode
  before, the preview kept: `visuallightsmode`, its help line saying which; `View/Preview3DLights.cs`,
  `MainWindow.Lights.cs`): each of the map's lights is a light-bulb icon in its colour with a black outline, the same
  size on the screen near and far (`BulbIcon`, dimmed when dark, tinted the selection's red when selected, as the 2D
  view's bulb is), drawn over the sprites (a lamp's in its glass; hidden only behind a wall, floor or ceiling,
  `Occluded`), and only the bulbs are clicked there (a click, right click or the
  wheel on a wall, floor or sprite does nothing; they are aimed at still for I and Ctrl+V). The crosshair picks a bulb (a little round it; the info panel shows it). A click selects it (C clears; the
  2D view's selection synced); **held**, it moves with the crosshair, kept as far ahead as it was (the selection with
  it when it is in it; the wheel while held pushes it away or pulls it near, Shift a little, Ctrl more; let go:
  `lightmove dx dy dz <lights>`, one undo step). The wheel raises and lowers the selected lights (else the one aimed
  at), the arrows move them along the map's axes as they move sprites (a run one undo step), right click opens their
  dialog, Delete deletes them, Insert places a point light at the crosshair (`lightadd x y z`: on a floor or ceiling
  half way up the room, else a little off what it is on), Ctrl+C copies them (`copylights`) and Ctrl+V with only lights
  copied puts them where the crosshair is. Its context menu (Space) has the lights' tools in place of the surfaces' and
  sprites': Place Light Here, Light Properties..., Copy Lights, Paste Lights Here, Delete Lights (2D Lights mode's: Place
  Light Here, Light Properties..., Delete).
- **The light's dialog** (`Blood/LightDialog.cs`; closing it sends the fields changed as `lightset <lights> name=value...`,
  one undo step, recorded and replayed): Kind (Point; Flame: lit while its torch or candle burns; Light fixture: dark
  when shot or switched off, the tile it shines with and where on its picture; Glow: the middle of a fullbright
  picture), Attached to sprite (-1 none; Nearest picks the sprite nearest it: attached, it sits on its kind's spot
  there, let go it stays where it shone), Colour, Reach, Brightness (percent of a lamp's), Flicker (a torch's 40),
  Height (a point light's above the floor, an attached one's above its spot), Casts shadows, Follows its sector's
  light (dark when the room's lights are off). Both views show each change as it is made (the lights alone made again
  from a changed copy of their list, `PreviewLights`: not the whole map), the map's own again if it is cancelled.
- **The 3D preview** lights the faces as the game does (`View/Preview3DLights.cs`: Godot's omni lights, the game's
  colours, reach, brightness and falloff; the faces' picture lit over its shade). A map without lights of its own shows
  the ones the game would make, so a `.MAP` looks as it plays; a light dark as the level starts (a torch not lit, a lamp
  not showing its tile, a hidden sprite) isn't drawn. The nearest 96 shine, the nearest 12 that cast shadows do
  (walls and floors both sides). In Lights mode each light has a bulb there too (dimmed when dark). A change to the
  lights alone (`MapDocument.NotifyLightsChanged`) makes only them again, the meshes kept. Sprites are lit as the game
  lights them (`LitSprite`, `SpriteShader`: the picture lit over its shade; invisible ones stay faint and unlit). A light
  is kept to its room where rooms share a place (layers drawn over each other, rooms stacked in height): the rooms
  coloured as the game's (`AutoLayers.Rooms`, `LightRules.RoomColours`; a room over only a corner of another shares
  its place too, `OutlinesOverlap` as the game's `SectorMap`), each sector's mesh and sprites on its colour's
  render layer, a light's masks leaving out the rooms sharing its room's place. A point light keeps the room it was
  placed or moved in (`MapDocument.SettleLight`: the edited layer's where rooms share the place; saved, `MapLight.Sector`,
  so the game knows it; one saved before gets the edited layer's as the map opens). **View >
  Dynamic Lights (3D)** turns them off (Build's shading alone, as `r_dynamiclights 0`), **View > Dynamic Light Shadows
  (3D)** their shadows (faster); **Full Brightness** (B) leaves all lighting out. Kept in forge.cfg (`showlights3d`,
  `lightshadows`).

## Atmospheres (Atmosphere mode, M)

A map's fog, ground mist, weather and wind are given by sector (`src/Document/Atmospheres.cs`; Chosen.Blood's
`MapAtmosphere.cs`, the .MAPX's `ATMO` chunk; the game: godot/README.md "Atmospheres by area"). An atmosphere is a named
set of settings; the first is the map's own, every sector given none has it; any number of others, each given to as many
sectors as wanted (changing one changes all its sectors). Each sector keeps its atmosphere through every change
(`Commit`); a new one takes its neighbours' (a room split, drawn in or beside one). Being sectors, they follow the
layers: each room of a room over a room has its own.

- A map without atmospheres of its own gets the game's guesses (`AtmosphereRules.Guess`: "Level", its weather by
  episode, its fog from the map's visibility; "Open ground", the ground mist over the open ground), shown in the 3D preview as the game
  shows them. **Tools > Generate Atmosphere** makes those the map's own; so does any first edit of them, so nothing
  changes but the edit. A .MAP keeps none (saving one says so).
- **Atmosphere mode** (`Editing/AtmosphereMode.cs`, the mode bar's Atmosphere, key M): sectors picked and selected as in
  Sectors mode (click, drag a box, Tab through rooms over rooms), each tinted with its atmosphere's colour
  (`MapView2D.SectorTint`; the map's own untinted); nothing moved, joined or deleted. A right click (2D, or the 3D
  preview's on a floor, ceiling, wall or sprite; the pan key tapped in 2D) opens the atmosphere menu for the selected
  sectors, else the one under the mouse: **Settings for These Sectors Only** (theirs when no other sector uses their
  atmosphere, else a copy of it named after it, "Ground mist 2", given to them alone and its settings opened: a green mist
  here, a pink one there), **Settings of X (All N Sectors Using It)** (the shared one), **Same as** another atmosphere
  (given to them: they change with it); Delete gives them the map's own. The settings dialog says when its settings are
  shared.
- **The Atmosphere tab** (`Ui/AtmospherePanel.cs`): a row an atmosphere, its tint, name, sectors, **<** giving it to the
  selected sectors (2D, or the preview's floors and ceilings), **...** its settings, **x** deleting it (its sectors get the
  map's own); **New**, **Generate**.
- **The settings** (`Blood/AtmosphereDialog.cs`, applied as it closes, shown live in the 3D preview): the name; the fog
  (where it is complete, 0 the map's; its own colour; the seconds the camera's fog takes to become it); the ground mist
  (its height over the floor, 0 none; density; colour); the weather (rain, snow, leaves: 0 none, 1 the game's amount);
  the wind (the way it blows, a Build angle; speed; gusts). Lengths in the units shown (`Units`).
- **Commands** (recorded, one undo step each): `atmoassign a 3,4,5`, `atmonew a [3,4,5 [name]]`, `atmodelete a`,
  `atmoset a name=value...` (`name` escaped: `%20`), `generateatmosphere`; sectors of a locked layer are left out.
- **The 3D preview** (`View/Preview3DAtmosphere.cs`, the game's at the preview's scale: a unit a game unit) shows them:
  the distance fog of the camera's sector's atmosphere (faded into) in place of Build's darkening with distance, an area
  of its own fog a bank of it seen from outside, the ground mist over each area's floors, rain, snow and leaves where the
  sky is open (each place's share, blown by its wind; a grid of 32-unit cells round the camera). An area's mist or fog
  is hidden when a room sharing its place is nearer the camera than its own (`Rooms()`, `LightRules.RoomSeen`: from a
  sector sharing no place, the room beside it). **View > Atmosphere (3D)** turns it off (`showatmosphere3d`);
  Full Brightness leaves it out.

## Mods and modern maps

A map opened from a **mod's or an add-on's folder** (one run with NBlood's `-game_dir`: its `.ini`, its maps, ART files
beside them, perhaps `.RFS` resource scripts and custom dude descriptors; Eviction is the one this was made with) is
shown with that folder's own data, as the game plays it (`Blood/ForgeData.cs`, `ModFolder`): its ART files over Blood's
(a file of one of Blood's names replaces it whole, new files add tiles: the picker's Map's Art tab lists them), its
palette lookups (the `.RFS` scripts' PLU resources, by number), its SHADE.DAT and SURFACE.DAT. The Stats tab names the
folder and how many tiles it brought. Nothing of it is stored in the map: a `.MAP` stays a plain `.MAP`.

**Modern maps** (NoOne's extensions, NBlood's and Chosen's: `godot/README.md` "Modern features"):

- A map is modern by its version, 7.1 (xmapedit's way; an object sending command 100 on channel 60 or 61 to itself
  counts too: `Blood/ModernTypes.Revision`). The Stats tab says so and shows NBlood's limits for it; **Map Options >
  Modern map** turns it on or off for a map (the version 7.1 or 7.0). Maps of NBlood's size (Eviction: 2384 sectors,
  16328 walls, 9161 sprites) open, edit and save byte for byte (`--roundtrip`).
- The modern sprite types have their names and captions from xmapedit's type table (`shared/Chosen.Blood/Types`); the
  dude flags (deaf, ambush, guard, patrol) are in the x-sprite tab. A **custom dude** (type 254) and its spawner show
  what their data fields are: data 1 the descriptor, with the name its `CDUDnnnn.CDU` gives in the map's folder ("The
  Heckler"), data 2 its flags in words (awake, crouching, no last stand, cruel, innocent).
- A **random or sequential TX switch** (types 25, 26) sends on the channels in its data fields (data 1 and data 4
  alone: every channel from the one to the other): Map Analysis, the link lines, the needs and the channel picker
  count them (`ModernTypes.DataChannels`), so a door opened only by such a switch isn't called unsent.
- A **sloped floor sprite** (wall aligned and floor aligned both; the engine keeps its slope in the offsets) is drawn
  tilted in the 3D preview and shortened on the map, its marker a square with its rising edge doubled; the sprite
  dialog's **Slope** sets it (4096: 45 degrees up the way it faces; 0: a plain floor sprite again).
- **Wall sprites in one plane** (at one point, turned alike: a picture on its backing, Eviction's fan on the black
  board in its vent box) are drawn in the 3D preview as Build orders them, the lower numbered over the others
  (`Preview3D.CoplanarAbove`: it stands an eighth of a game unit further out the way it faces); the fan was hidden
  behind its board. A sprite not centred on its z (cstat 128 off) stands on it, as in the game.
- An x-structure's reference that wrapped in the file's 14 bits (a wall or sprite past 8191: the games set them as
  they load) is no finding.
- **Test Map** (F9) starts the game with the mod (`--addon=<id>`: its art, sounds, custom dudes), for a new map in the
  mod's folder too; the mod must be installed in the game (ChosenLoader's Add a Mod Folder, or `maptool extract --mod`).

A field added to a dialog later is marked `SessionRecorder.LateField`: the recorder numbers it after the others, so
sessions recorded before it replay as they were. `--selftest`'s modern case checks the above.

## Files

- Save keeps the file's format. A `.MAP` is a plain Blood map (NBlood and Blood load it); saving one lists what it
  can't hold (sprite extras, names, the editor's view) and offers `.MAPX` instead; sprite extras are written as a
  `.patch` beside it, which Chosen reads. File > Save As Extended writes a `.MAPX` (`godot/README.md` ".MAPX"), File >
  Export Compatibility .MAP a `.MAP`. Saving bumps the map's revision, as Build's editors do, and writes through a
  temporary file so a game watching the map never reads half of it.
- Opening a `.MAP` with Refreshed Supply's `.patch` beside it asks whether to apply its fixes (the heights go into the
  map; the voxel model fixes are kept in a `.MAPX`).
- Tools > Test Map (F9): the map as it is into a temporary `.MAPX`, played in Chosen (`Chosen.exe` beside Forge, else
  the repository's game); with that game still open, F9 again sends it the changes (hot reload).
- Tools > Map Analysis: the map rules (`shared/Chosen.Blood/Map/MapValidator.cs`) and Blood checks (no player start,
  channels nobody sends or receives, markers without an owner, walls of no length, two points of a loop at one place,
sky ceilings under a sky of one piece: its Fix gives the map Blood's sky);
  click one to go there. Findings marked [Fix] have one (their tooltip says what): **Fix** makes the selected one's,
  **Fix All** the repairs, the fixes that only mend the map's data (a sprite of no sector, which NBlood stops on, put
  in the one it lies in, a marker its owner's, else the nearest; a sprite stored in the wrong sector; x-structure
  references; markers nobody owns deleted, as Blood does; a wall of no length taken out with its start point, the walls round it
  joined), one undo step. What a type needs is the mapper's choice:
  its Fix only one at a time. **Saving** a map the rules find anything in asks first (Save anyway, Fix and save: the
  repairs, Show them, Cancel); warnings alone can be let through for good (the dialog's check box, `savewarnings` in
  forge.cfg). A turning or sliding sector whose floor or ceiling is aligned to a short first wall is advised a long
  one: Blood rounds the turned points each frame, and a short first wall tips the picture (a 400-unit end of a
  3376-unit block: half a texel of wobble at the far end); its Fix makes the longest wall first.
- The map header's start position (where Blood puts every player unless a Player Start sprite says otherwise): a new
  map has none (sector -1); after an edit, a save or Map Analysis, a start in no sector becomes Player 1's start
  (type 1, data 1 = 0), else a spot in a sector. A start already in a sector is left alone.

## The code

`src/` is the clean core (window, view, modes, undo, dialogs' framework, the geometry operations written from Build's
map rules); `src/Blood/` is what knows Blood (type names and captions, the properties dialogs' fields, Map Analysis),
GPL v2 like NBlood. Each file starts with its provenance line (`PLAN.md` "Clean core"). The geometry operations
(`src/Document/GeometryOps.cs`) work on the map as a graph (sectors of loops of wall objects, each red wall's partner)
and write it back in one place, which renumbers every stored index (Point2, NextWall, NextSector, first walls,
sprites' sectors, markers' owners, x-references, the start sector). Walls no operation touched keep their links as
stored, renumbered (the maps' one-way links and mismatched NextSectors stay), and walls in no sector's range are kept;
`--selftest`'s preserve check holds it to that on E3M5, DWE4M1 and DWE1M9.
