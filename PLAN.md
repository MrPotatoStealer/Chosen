# Chosen Forge, phase one, and the rename of the port

## Goal

Rename the port to **Chosen**, split Blood's map code into a shared library, add a .MAP writer and the .MAPX format, and
build **Chosen Forge** (`forge/`, a separate Godot 4.7 C# app) far enough that a mapper opens a Blood .MAP, edits it in 2D
in UDB's manner (vertices / walls / sectors / sprites / draw modes, undo, properties dialogs) and saves a .MAP that NBlood
loads or a .MAPX that Chosen loads. We know it works when every base and add-on map round-trips byte for byte through the
writer, `bugs.py regress all --mapx` shows no CHANGED view with the game reading .MAPX, and the hand-tests listed per
stage pass.

## Assumptions

- Forge is for people who only want the editor: it never needs the game or its extracted data. It reads the Blood
  install itself (the folder is a preference, found automatically in Steam's library folders when possible) through
  `Chosen.Blood.Files` (RFF, ART, palette: stage 1), the same readers ChosenLoader's port of the pipeline builds on.
  Without a Blood folder Forge still opens and saves maps, showing tile numbers instead of previews.
- "UDB's model" means its classic 2D modes and main window; UDB's 3D/visual mode, script editor, nodebuilders, WAD
  resources and plugins have no counterpart here.
- The 2D view keeps Build's orientation (x right, y down, as mapedit/xmapedit and every number the game prints); see
  Open questions 1.

## Constraints

- The game's behaviour and output must not change: `bugs.py regress all` (and `motion`) unchanged after every stage;
  recordings still replay exactly (`bugs.py replay <id>` prints no differing frame on a Godot-era report).
- `.MAP` files written in compatibility mode are plain Blood maps (version 0x700, encrypted, CRC): NBlood and Blood 1.21
  load them. Nothing Chosen-only goes into a .MAP.
- Byte identity: load → save of an unedited .MAP reproduces the input exactly (the test of stage 1).
- Public names kept in phase one: the `b3d_*` cvars (saved in every user's config.cfg, in spots.txt, bugs.py,
  regression setups, recordings' `setfixblood`), the `game/blood3d` folder and `fs_blooddata` default, NBlood's `-b3d*`
  flags (`pipeline/nblood_b3dview.patch`), the `b3d_*` shader globals, `res://maps/<name>.map` as the level name in bug
  reports and recordings.
- **Clean core** (the user may later build a commercial, non-Blood game on the renderer and the editor): the generic
  core, `shared/Chosen.Engine/` and Forge's editor code outside its Blood folder, contains only code written here.
  Nothing copied or translated from Build (`third_party/nblood/source/build`, xmapedit's `src`: Ken Silverman's
  non-commercial license), NBlood's game code (GPL), xmapedit's `src_blood` (GPL) or UDB (GPL). Algorithms are written
  from the maths and the data format's meaning (point in polygon, a slope's plane from heinum, wall loops), never by
  translating engine.cpp / clip.cpp; studying how Build or UDB behaves is fine, transcribing them is not. Everything
  Blood-specific or derived from those sources (the Blood .MAP reader/writer written from NBlood's db.cpp, the Blood
  type tables from xmapedit's OBJTYPES.INI, Blood's file formats, the game's exact Build ports in `Board.cs`,
  `World.BuildClip`, `World.NativeTrace`) lives in the Blood module (`shared/Chosen.Blood/`, the game, Forge's
  `Blood/` folder), GPL v2 like NBlood. Each file in `shared/` and `forge/` starts with a one-line provenance header:
  `// Chosen core: original code.` or `// Chosen Blood module (GPL v2): <what it derives from>.` All of Chosen
  is GPL v2 (`LICENSE.md`); the rule keeps the user's own core code free of anyone else's, so it can be used
  elsewhere too (contributions come under the GPL v2 with a sign-off, `CONTRIBUTING.md`).
- Off limits: `third_party/*` (read only; `third_party/xmapedit/src` and NBlood's `source/build` are Build-licensed:
  reimplement from the rules, never copy), `Deprecated/`, `bugs/*/baseline.jpg`, the old repository's `trenchbroom` branch.
- Test runs of the game follow CLAUDE.md: `--position 20000,20000` before `--`, `--windowed --resolution 1280x720`,
  always `--shot`/`--views`/`--bugs` so the run quits, settings changed by `--exec` put back in a separate run (bugs.py
  does this), never inside `--atshot`. New cvars this plan adds (`fs_mapx`) are unsaved, so they need no restoring.

## Decisions

### Rename
- **User-visible now (stage 0):** `config/name="Chosen"` (window title, and the user folder becomes
  `%APPDATA%/Godot/app_userdata/Chosen`), assembly `Chosen` (the exported exe will be `Chosen.exe`; there is no export
  preset yet, so nothing else to rename there), docs, bat comments, bugs.py's config path. The old user folder's
  `config.cfg` and `driver.cfg` are copied on first start (one-time migration).
- **Internal now (stage 0, same commit series):** C# namespace `Blood3D` → `Chosen`, `Blood3D.csproj` → `Chosen.csproj`,
  the two generators. Reason: stage 1 moves files and changes their namespaces anyway; renaming first means one churn.
- **Later / never:** `b3d_` cvars (would need a config migration and an alias layer; no user value), `game/blood3d` and
  `fs_blooddata` (14 pipeline references, .gitignore, install scripts; ChosenLoader's plan decides the distributed data
  folder and may give `fs_blooddata` a "data folder beside the exe" default then), `-b3d*` NBlood flags and `b3d_*` shader
  globals (test tooling and internals). Blood's own menu title "BLOOD" stays: it is the game's title screen.

### Shared code
- **Two plain .NET 8 class libraries** (Clean core, Constraints; the core is `Chosen.Engine`, not `Chosen.Core`, which the game's
  own `godot/src/Core` namespace already is; their namespaces are `Chosen.Shared.Engine.*` / `Chosen.Shared.Blood.*`, since
  `Chosen.Engine` / `Chosen.Blood` hid the game's `Blood` class and Godot's `Engine` inside `Chosen.*`): `shared/Chosen.Engine/` (namespace `Chosen.Engine.*`:
  the map data model Forge edits, geometry written fresh, .MAPX, the editor-neutral validator rules) and
  `shared/Chosen.Blood/` (namespace `Chosen.Blood.*`, references Core: the Blood .MAP reader/writer, x-structures,
  Blood type tables, Blood's file formats, Blood-specific validator rules). Referenced by `godot/Chosen.csproj`,
  `forge/ChosenForge.csproj` and later ChosenLoader with `<ProjectReference>`. Godot 4 builds and copies referenced
  projects itself. Rejected: linked sources (two compilations of the same types, no shared test tool) and a NuGet
  package (overhead for one repo). Where this plan says `Chosen.Blood.Geometry` or `.MapX` read `Chosen.Engine.*`.
- Forge likewise: `forge/src/` is core (window, view, modes, undo, dialogs' framework), `forge/src/Blood/` holds what
  knows Blood (type names and data captions, the .MAP open/save, sprite categories), the way UDB keeps games in
  configurations; a later non-Blood game supplies its own folder.
- **The renderer** (`godot/src/Render/Native` and what it uses) stays in the game in phase one. Before it moves into the
  core for Forge's 3D preview or another game, a provenance audit lists every part derived from Build or NBlood (e.g.
  Build's texture mapping and shading formulas if transcribed); those are rewritten from the maths first. The audit is
  the first step of the 3D-preview phase.
- Layout by concern, so ChosenLoader's port of the Python pipeline (`pipeline/rff.py`, `blood_assets.py`, `export*.py`,
  `voxels.py`, `gen_qcdata.py`, `make_menu_gfx.py`) slots in beside the map code: `Chosen.Blood.Map` (.MAP, .MAPX, the
  patch, validator), `Chosen.Blood.Geometry` (Build's point-in-sector etc. on an interface), `Chosen.Blood.Types`
  (type tables), `Chosen.Blood.Files` (RFF, ART, palette, PLU, DAT readers and the conversions: empty in phase one
  except a README line; the loader is a sibling consumer of this library, not planned here).
- `Board` stays in the game (it holds game-frame state) with its own exact Build ports (Blood module); Forge's
  `MapDocument` uses the engine core's fresh `BuildGeom`; the `geomcheck` command keeps the two in agreement (stage 1).
- A console tool `shared/Chosen.MapTool/` does every automated check without Godot (round trip, convert, validate, dump,
  diff). Reason: the format tests must run in seconds in a loop, and Godot headless runs cost 10 s each.
- `XStructs.cs` becomes generated from `pipeline/blood_map.py`'s field tables by `shared/tools/gen_xstructs.py`, with
  `Write` beside `Read`. Reason: three copies of the bit layout (Python, C# read, C# write) must stay identical.

### .MAPX v1
- **Binary, chunked, little-endian.** File: magic `MAPX`, u16 version (1), u16 flags (0), then chunks to the end; chunk =
  fourcc, u32 payload length, payload. Readers skip unknown chunks; Forge keeps them and writes them back (forward
  compatibility). Rejected: a text format like UDMF (diff-friendly, but the x-structures would have to be spelled out
  field by field, and a lossless .MAP export gets harder; `maptool dump` gives the diffable text instead).
- **Core chunks carry Build's records verbatim (unencrypted):** this is what makes compatibility export lossless and
  the game's loader trivial. Extensions are separate chunks, which is where the room for later features is.

| Chunk | Payload |
|---|---|
| `INFO` | UTF-8 `key=value` lines: `generator`, `created`, `modified`, `source_crc` (the .MAP it came from), `title`, `author`, `comment`. Free-form, never versioned. |
| `HEAD` | startX/Y/Z i32, startAng i16, startSect i16, pskyBits i16, visibility i32, songId i32, parallaxType u8, mapRev i32, mapVersion u16 (0x700/0x603 as loaded; used by the export), headerEncrypted u8, header2 raw 128 bytes (as decrypted), skyCount u16, skyOfs i16[]. |
| `SECT` | u32 count; per sector the 40-byte Build record, u8 hasX, the 60-byte x-sector blob if hasX. |
| `WALL` | u32 count; 32-byte record, u8 hasX, 24-byte x-wall blob. |
| `SPRT` | u32 count; 44-byte record, u8 hasX, 56-byte x-sprite blob (its last 4 bytes are bits NBlood never reads; kept). |
| `SPRX` | Sprite extras Chosen reads from Refreshed Supply's `.patch` (voxel turn/offset/no-model): u32 count; u16 sprite, string key, string value. Chosen-only. |
| `NAME` | Optional names/comments per object: u32 count; u8 kind (0 sector 1 wall 2 sprite), u16 index, UTF-8 string. Editor-only. |
| `EDIT` | Editor view state (centre x,y i32, zoom f32, grid i32, mode u8). Never read by the game. |
| `CRC ` | u32 CRC-32 of everything before this chunk. Last chunk. |

- **Lossy export (.MAPX → .MAP)** writes the core chunks as a .MAP and lists what it drops in one dialog: `SPRX` (offered
  as a `<NAME>.patch` beside the .MAP, which only Chosen reads), `NAME`, `EDIT`, unknown chunks; and warns on counts over
  Build's limits (`BuildLimits`: MAXSECTORSV7 1024, MAXWALLSV7 8192, MAXSPRITESV7 4096 from NBlood's build.h; vanilla's
  x-object limits from xmapedit's `db.h`) and on NoOne modern types when the target is "vanilla".
- **Patches:** a `.patch` belongs to a `.MAP` (keyed by its CRC). Opening a .MAP with a `.patch` beside it, Forge offers
  "apply Refreshed Supply's fixes": sector heights go into the data, sprite model keys into `SPRX`. A .MAPX is
  self-contained: the game applies no `.patch` to it. Converting with `maptool tomapx` applies the patch the same way
  (flag `--no-patch` to skip), so a converted level plays as its .MAP did.
- **The game loads .MAPX** through `BloodMapFile.Load`, dispatching on the magic; `Addons.BuildMapPath` finds `.mapx`
  when there is no `.map`; `--file=<path>` and the `map` command accept a path with either extension; hot reload watches
  whichever file was loaded; `fs_mapx <dir>` (unsaved cvar) makes the game take `<dir>/<NAME>.mapx` over the .map for the
  regression check.
- Saving increments `MapRev` like mapedit (NBlood's `dbSaveMap` does); `maptool roundtrip` keeps it (the identity test).

### Build's data in UDB's model

| UDB | Forge (Build) | Rule the operation keeps |
|---|---|---|
| Vertex | A **point**: the set of walls starting at one (x,y) that Build's `dragpoint` chain connects (walls joined through `nextwall`; `Board.DragPoint` already does it). Two stacked sectors with a corner at the same coordinates are two points, never merged. | Moving a point moves every wall of its chain; the walls ending there follow through `Point2`. |
| Linedef with two sidedefs | A **wall pair**: a red wall and its `NextWall` shown as one line with a front and a back side (front = the lower wall index); a white wall is a one-sided line. Each side keeps its own picnum, overpicnum, shade, pal, repeats, pannings, cstat, type, x-wall. | `NextWall`/`NextSector` mutual, endpoints reversed, `NextWall == -1 ⇔ NextSector == -1`. Blocking/hitscan cstat bits are edited on both walls by default ("apply to both sides" checkbox, as xmapedit's `wallCstat*` with `nextWall`). |
| Linedef front/back | The side whose own sector the normal tick points into. | The tick is drawn at the midpoint toward the front wall's sector. |
| Sector | A Build sector: contiguous wall range, one outer loop, inner loops. Overlapping sectors (stacked rooms, movers stored open) are allowed: highlight takes the smallest sector under the cursor; Tab cycles the candidates; the status bar says "2 sectors here". | Walls contiguous (`WallPtr`, `WallNum`); every loop closed within the range; outer loop in Build's winding, inner loops the other way (`BuildGeom.LoopIsOuter`). |
| Sector label (index, effect, tag) | Sector index, type name (`TypeTable`), RX/TX ids. | Label position: an interior point from the outer loop (midpoint of the widest horizontal span through the bounding-box centre row; fall back to the loop's average point). |
| Slope | Floor and ceiling heinum relative to the sector's **first wall** (`WallPtr`); the first wall is marked in sectors and walls mode; "Set first wall" action (xmapedit `setFirstWall`). | An operation that would change which wall is first (deleting or splitting it, rotating a loop) re-picks the first wall so the plane stays; if it cannot, it warns and keeps heinum. |
| Thing | A sprite: marker drawn by alignment (circle for face, a bar of the sprite's width for wall-aligned, a square for floor-aligned), angle arrow, colour by category (dude, item, weapon, ammo, marker, decoration, switch, trap, generator, sound, player start) from `TypeTable`. Markers (statnum 10) draw an event line to their owner sector's label. | `SectNum` is the sector holding the sprite (`BuildGeom.UpdateSector` after every move; outside every sector keeps its own and the validator warns, as Blood tolerates). |
| Thing properties dialog (type, flags, args) | Sprite dialog: type (list from `TypeTable` with names), picnum with preview, cstat flags, data1-4 with the type's captions, RX/TX/command/trigger flags, skill/game-mode flags, the rest of the x-sprite in a "more" grid. | `Extra > 0 ⇔ x-sprite present`; `Reference` = own index; the x-struct is created on first edit of an x field. |
| Draw lines mode | Draw geometry: a closed loop outside any sector makes a sector of white walls; a loop inside a sector makes an inner loop plus a new sector joined by red walls (xmapedit `insertLoop` + `redSectorMake`); a path from one wall of a sector to another wall of the same sector splits it (`sectSplit`); a drawn point landing on a wall inserts a point there (`insertPoint`); a drawn edge lying on a white wall joins it as a red pair. | All of the above, checked by `MapValidator` after every operation in debug builds. |
| Join / merge sectors (J / Shift+J) | Join = `redSectorMerge`: remove the shared red walls, relink the loops, the first sector's properties win. Merge (keep as separate sectors but one loop) has no Build meaning: the key is unbound. | Contiguity, mutual red walls. |
| Flip linedef / sidedefs | None: a Build wall's direction is its loop's. Unbound. | |
| Insert (vertices mode) | Insert a point on the highlighted wall (and its `NextWall`). | Endpoints reversed on the pair. |
| Delete | Vertices: `deletePoint` (joins the two walls, both sides). Walls: not deletable alone (a loop stays closed): Delete on a wall deletes nothing and says so; Ctrl+Del makes a red wall white (detach). Sectors: delete the sector (`loopDelete`), red walls facing it turn white, its sprites move to the sector now under them or are deleted. Sprites: delete. | Index renumbering below. |
| Tags | Blood's RX/TX channels: shown in the labels and the dialogs; "new channel" picks the lowest unused (xmapedit `findUnusedChannel`). | |

Index renumbering on insert/delete (one routine in `MapDocument`, used by every operation): wall indices in
`BSector.WallPtr`, `BWall.Point2`, `BWall.NextWall`, `XWall.Reference`; sector indices in `BWall.NextSector`,
`BSprite.SectNum`, `BSprite.Owner` of markers (statnum 10), `XSector.Reference`, the header's `StartSect`; sprite indices
in `XSector.Marker0/Marker1`, `XSprite.Reference` (not `Target`/`BurnSource`: runtime fields, kept as stored).

### Editor architecture (Forge)
- Godot Control nodes for the whole UI (MenuBar, toolbars as HBoxContainers of Buttons, a `TabContainer` docker, Window
  dialogs); the 2D view is one `Control` drawing in `_Draw` with batched `DrawMultiline`/`DrawRect` calls (8192 walls
  are a few batches). Reason: no third UI toolkit, and the 3D preview later is a `SubViewport` in the same window reusing
  `godot/src/Render/Native` (the shared library keeps the map data Godot-free, the renderer stays in the game project and
  will be moved or referenced then).
- Undo/redo by **whole-document snapshots** (the .MAPX core chunks serialised in memory, Deflate-compressed, 100 levels,
  ~100-200 KB each compressed). Rejected: UDB's per-element snapshots: Build's data is index-based and every insert
  shifts indices, so element diffs are the bug-prone path; a snapshot of a 8192-wall map serialises in a millisecond.
- Modes as classes with UDB's hooks (`OnEngage/OnDisengage`, `OnMouseMove`, `OnSelectBegin/End`, `OnEditBegin/End`,
  `OnDragStart/Stop`, `OnUpdateMultiSelection`, `OnInsert`, `OnDelete`, `OnRedraw`), one `EditingManager` switching them,
  keys from `forge/data/keys.cfg` (UDB action names, so UDB's documentation applies).
- Mouse as UDB's `ClassicMode`: LMB press = select (click toggles the highlighted item; press on empty and move =
  rectangle selection); RMB press = edit (release without moving = properties dialog of the selection or the highlighted
  item; move past 2 px with it held = drag the highlighted/selected items, into the mode's Drag sub-mode); wheel zooms
  at the cursor; MMB drag or arrow keys pan; Shift while dragging disables grid snap; Ctrl with rectangle = subtract.
- Default keys: UDB's where its Actions.cfg states them (Insert, Delete, Ctrl+Del dissolve, Ctrl+Z / Ctrl+Y, Alt+G grid,
  Ctrl+Shift+G go to coordinates, Ctrl+Shift+D rectangle draw, Ctrl+Shift+C / Ctrl+Alt+V copy / paste properties,
  Shift+L point sprite at cursor, Ctrl+Shift+wheel rotate), V/L/S/T for the modes, D draw lines, J join, E edit
  selection (later), Shift+] / Shift+[ grid size (verify the rest against a UDB install's Preferences > Controls; a wrong
  default costs one line of keys.cfg).

## Stages

Each stage leaves the game working; implement in this order.

### Stage 0: the rename

Goal: the port is Chosen to users and in its identifiers; the user's settings survive.

Files:
- `godot/project.godot`: `config/name="Chosen"`, `dotnet/project/assembly_name="Chosen"`, header comment.
- `godot/Blood3D.csproj` → `godot/Chosen.csproj` (git mv); `RootNamespace` `Chosen`.
- All `godot/src/**/*.cs`: `namespace Blood3D` → `namespace Chosen`, `using Blood3D.` → `using Chosen.`
  (search-and-replace; 123 files). `godot/tools/gen_tables_cs.py`, `gen_aistates_cs.py`: the namespace they emit.
- `godot/src/Game.cs` (where `Cvars.LoadConfig()` is called, before it): new `Core/UserDataMigration.cs` with
  `public static void MigrateFromBlood3D()`: if `user://config.cfg` does not exist and
  `<OS.GetUserDataDir() with its last segment replaced by "Blood3D">/config.cfg` does, copy `config.cfg` and `driver.cfg`,
  print one console line. Godot reads `project_settings_override` (`user://driver.cfg`) before any C# runs, so the first
  start after the rename uses D3D12 even for a Vulkan user; the copied `driver.cfg` takes effect on the second start:
  print that too.
- `pipeline/bugs.py` (`CONFIG` path, the three `dotnet build ... Blood3D.csproj`), `godot/play.bat`, `godot/playat.bat`
  (line 88 config path, comments), `build_nblood.bat`, `build_assets.bat`, `build_xmapedit.bat` comments, `godot/spots.txt`
  comments, `README.md`, `godot/README.md`, `CLAUDE.md` (the word Blood3D where it means the game; the folder
  `game/blood3d` and `b3d_` stay).
- `godot/Blood3D.sln` → `godot/Chosen.sln` (Godot's C# export looks for `<assembly name>.sln`), its project entry;
  `godot/export_presets.cfg` (already names `Chosen.exe`; the data folder Godot writes becomes `data_Chosen_*` by itself).
- **TrenchBroom remnants** (the brush-map path is gone; it lives on the `trenchbroom` git branch only):
  - comments (done in stage 0); the code itself is stage 0b below;
  - `build_assets.bat` (the entities/TrenchBroom step and the `xcopy`), `pipeline/export.py` comments, the
    TrenchBroom-made test spot in `godot/spots.txt`, `README.md` (the TrenchBroom sections, tools and folder tables);
  - kept: one line in `README.md` and `CLAUDE.md` saying the old brush conversion is on the `trenchbroom` branch.
  - Done when: `grep -ri trench` over `godot/src`, `pipeline`, `*.bat`, `README.md`, `godot/README.md`, `godot/spots.txt`
    finds only those branch pointers.

Steps: rename csproj and namespaces, build (`dotnet build -v q` in `godot/`), then project.godot and the migration, then
the TrenchBroom remnants, then the scripts and docs. Keep the DarkPlaces-era text in `README.md` as history ("Blood3D,
the old name").

Done when: `dotnet build -v q` passes; `godot\play.bat e1m1` opens a window titled Chosen; a fresh
`app_userdata/Chosen/config.cfg` equals the old `app_userdata/Blood3D/config.cfg` after one start; `python
pipeline/bugs.py regress all` shows no CHANGED (it also proves bugs.py's paths); `python pipeline/bugs.py replay <a
Godot-era id>` prints no differing frame; `grep -r Blood3D godot/src pipeline *.bat` finds only the migration code and
history notes.

### Stage 0b: the brush-level code

Goal: the code that only served TrenchBroom/brush levels is gone; behaviour unchanged. Movement, hitscan and sight on a
Build level are Build's own (`World.BuildClip`, `World.NativeTrace`); a Build level's `CollisionWorld` holds no brushes
(`World.Native.cs`), only the links Blood's markers make (`World.NativeLinks`) and the sprites' bodies.

- Delete: brushes and brush models (`Sim/Collision/Brush.cs`, `BrushModel.cs`, `LevelCollision.cs`) and the brush
  traces in `CollisionWorld` (its trace against `World`/`Models`); entity spawning from brush entities
  (`World.SpawnSector`, `SpawnWall`, `SpawnMapSprite`, `EntityShape`, `EntityBounds` and their callers); the Quake `.map`
  reader (`Map/MapFile.cs`'s `MapFile`; `MapEntity` stays as the worldspawn record or becomes a small settings class)
  and `Map/LinkKeys.cs`; the "no Build sectors" fallbacks (`World.SectorTrack`, `World.Triggers` brush outlines,
  `World.Physics`' brush path when `UseSectorClip` is false, `Sector.Entity`), `Layers.cs` if only brushes use it; the
  worldspawn-entity readers in `DynamicLights.SetupMapLights`, `GroundMist.Setup`, `EnhancedFog`, `Weather` where
  only a brush map set those keys (keep what a Build level's generated worldspawn feeds, e.g. `bloodVisibility`).
- Keep: links and bodies (rename `CollisionWorld` to what it is then, e.g. `LinkSet` + bodies, if it reads better);
  the sprite-outside-every-sector case must keep its current result (today: a trace that only sees bodies).
- Each deletion only after grepping its callers; `dotnet build` after each group.

Done when: `dotnet build -v q`; `bugs.py regress all` no CHANGED; `bugs.py replay` on several Godot-era reports
(at least five, including ones with movers and water) with no differing frame; `bugs.py motion` no BROKEN/CHANGED; the
lines removed counted in the commit message.

### Stage 0c: the old converter's lookups (after 0b; its own careful pass)

What 0b left because it is tied together: the brush world in `CollisionWorld` (`World`, `Models`, the brush halves
of `Trace`/`LineBlocked`/`PointContents`/`MayReport`, `Brush.cs`, `BrushModel.cs`, `Sector.Model`/`Shape`,
`Wall.Model`, `Link.Model`); the `.sec` outlines (`LoadOutlines`, `_outlines`, `PointInSector`, `InColumn`, the
volume lookups `VolumeBox`/`SectorZRange` where only brush levels needed them); the world-level layers
(`World.Layers.cs`, `Collision/Layers.cs` gates), which only `.sec` files filled. All of these are inert for a Build level
(empty, null or a constant answer). Careful because the `layer` parameters they share are threaded through 21 files
and are **live** for `SectorMap`'s own layers (Build's overlapping rooms, `SectorMap.HasLayers`): fold the world-layer
constants without touching SectorMap's path. Same gates as 0b.

### Stage 1: the shared library, the .MAP writer, the round trip

Goal: `Chosen.Blood` exists, the game uses it, and every map round-trips byte for byte through `maptool`.

Files (create):
- `shared/Chosen.Blood/Chosen.Blood.csproj` (net8.0 classlib, `Nullable` disable, `Optimize` true, no Godot).
- `shared/Chosen.Blood/Map/BloodMapFile.cs` (moved from `godot/src/Sim/Build/`, namespace `Chosen.Blood.Map`):
  - new fields: `int Version` (the u16 at offset 4), `bool HeaderEncrypted`, `byte[] Header2` (128 bytes decrypted, null
    for v6), `int XSpriteSize, XWallSize, XSectorSize`; `BSprite.FileIndex` (the stored `index` at offset 32, which the
    reader currently overwrites with the position: NBlood does the same at load, so files hold stale values that byte
    identity must keep); `XSprite.Tail` (`byte[4]`, the 32 bits NBlood skips).
  - `public static BloodMapFile Parse(byte[] data)`: raw, no marker propagation, no patch (it still fills `BWall.Sector`,
    a derived field). Throws on size fields larger than the full x-struct sizes (none of the shipped maps).
  - `public void PrepareForGame(string patchPath)`: `ApplyPatch` (if the file exists) then `PropagateMarkers`. Moved out
    of `Parse` so the writer sees the stored data.
  - `public static BloodMapFile Read(string path)`: raw by magic (`BLM\x1a` → Parse, `MAPX` → `MapXFile.Read`, stage 2).
  - `public static BloodMapFile Load(string path)`: `Read` + `PrepareForGame` (for .mapx: no patch). Same name and
    meaning as today, so `LevelLoader`, `Game.LoadHeadless` and `BuildDebug` only change their `using`.
- `shared/Chosen.Blood/Map/XStructs.cs`: generated; `XSector/XWall/XSprite.Read(byte[], int)` as now plus
  `void Write(BitWriter w)`; `public struct BitWriter` (lowest bit first, mirror of `BitReader`).
- `shared/tools/gen_xstructs.py`: reads `XSECTOR_FIELDS`/`XWALL_FIELDS`/`XSPRITE_FIELDS` from `pipeline/blood_map.py`
  and writes `XStructs.cs` (header comment "generated by shared/tools/gen_xstructs.py; do not edit").
- `shared/Chosen.Blood/Map/BloodMapWriter.cs`: `public static byte[] Write(BloodMapFile m, bool bumpRevision)`: the
  inverse of `Parse` as NBlood's `dbSaveMap` writes it: signature + `Version`; 37-byte header (encrypted with
  `kMapHeaderNew` iff `HeaderEncrypted`); for v7 `Header2` (x sizes patched in at 64/68/72, encrypted with numWalls);
  sky offsets (key `nsky*2`); sectors (key `MapRev*40`), walls (`(MapRev*40)|kMapHeaderNew`), sprites
  (`(MapRev*44)|kMapHeaderNew`), each x-struct as its blob (`XSprite.Tail` appended); CRC-32 trailer. Writes sprites in
  list order with `FileIndex` as stored (Forge renumbers `FileIndex = position` when it edits).
- `shared/Chosen.Blood/Geometry/IBuildData.cs`: `IReadOnlyList<BSector> Sectors`, `IReadOnlyList<BWall> Walls`.
- `shared/Chosen.Blood/Geometry/BuildGeom.cs`: `Inside(IBuildData, int sector, double x, double y)`,
  `GetZsOfSlope(IBuildData, int sector, double x, double y, out double ceil, out double floor)`,
  `UpdateSector(IBuildData, double x, double y, int from)`, `RotatePoint`, `WallEnds`, `Loops(IBuildData, int sector)`,
  `LoopIsOuter(IBuildData, int firstWall)` (signed area with Build's winding), `LoopArea`, `SectorBounds`.
  **Written fresh in `Chosen.Engine.Geometry`** (Clean core): `Board.cs`'s versions are ports of Build's engine.cpp, so
  they are not moved. `Board` keeps its own exact ports (the game must match Blood to the bit, and replays check it) and
  does **not** delegate to `BuildGeom`; instead the game's console command `geomcheck` (run headless over every base
  and add-on map, extending `bmapcheck` in `BuildDebug.cs`) compares the two on every sector (inside for a grid of
  points over its bounds, slope heights at its walls' points) and lists any difference, so the editor and the game
  agree where it matters.
- `shared/Chosen.Blood/Types/TypeTable.cs` + `Types/objtypes.cfg` (embedded resource): sector/wall/sprite type names,
  captions, data1-4 captions and availability (vanilla / modern) ported from
  `third_party/xmapedit/tools/mkxmprff/INI/OBJTYPES.INI` (NoOne's, GPL; format `TypeNNNN = (Name=..., Caption=...,
  Data1=..., Availability=2)`; a `*` prefix means modern-only). `public static TypeTable Load()`,
  `Name(kind, type)`, `DataCaption(kind, type, n)`, `Category(type)` (the sprite categories above, by type ranges from
  NBlood's `common_game.h`).
- `shared/Chosen.Blood/Files/`: Blood's archive and art formats, the start of the C# port of `pipeline/rff.py` and
  `blood_assets.py` (ChosenLoader builds its conversions on these later; Forge reads its previews through them now):
  - `Rff.cs`: `Rff.Open(path)`, entries by name/type and by id, `Read(entry)` (the index XOR for version 0x3xx; the
    first 256 bytes XORed when flag 16 is set), as `pipeline/rff.py`.
  - `Art.cs`: `ArtSet.Load(folder)` (every `TILES0NN.ART`, names matched case-insensitively), per tile size, offsets,
    animation, and its pixels (column-major as stored, a `Rows()` helper transposing), as `blood_assets.Tile`.
  - `Palette.cs`: `BLOOD.PAL` from BLOOD.RFF (768 bytes, 6-bit values scaled as `export.export_palette` does) and the
    PLU lookups; index 255 is transparent.
  - `BloodInstall.cs`: `Find()` (Steam's `libraryfolders.vdf` for "Blood - Refreshed Supply", then GOG's registry key, then
    null) and `IsValid(folder)` (BLOOD.RFF and TILES000.ART present).
  - Checked by `maptool tiles <blood dir> <out dir> <ids>`: writes those tiles as PNG (through a tiny PNG writer in the
    tool) and compares the decoded pixels with the pipeline's `game/blood3d/textures/blood_masked/NNNN.png` (pixels, not
    file bytes: Pillow's compression differs).
- `shared/Chosen.MapTool/Chosen.MapTool.csproj` + `Program.cs`: commands `roundtrip <globs>` (Parse → Write with
  `bumpRevision=false` → compare; on a difference print the first offset and which region: header, header2, sky, sector
  n, wall n, sprite n, x-struct, crc), `check <files>` (validator, stage 1 has the structural rules; prints issues, exit
  code 1 on errors), `dump <file>` (text: header, every record and x-field, one line each), `diff <a> <b>` (semantic diff
  of two maps by object), `stats <files>`. Stage 2 adds `tomapx`/`tomap`. Exit code non-zero on any failure.
- `shared/Chosen.Blood/Map/MapValidator.cs`: `public static List<Issue> Check(BloodMapFile m, Limits limits)`,
  `Issue { Severity, Kind, Index, Message }`; the rules listed under "Build's data in UDB's model" (contiguity, closed
  loops, mutual red walls with reversed endpoints, winding, `Extra ⇔ X`, `Reference`, sprite sectors, markers, start
  sector, limits). Shipped maps are the fixture: every rule must pass on all of them or be downgraded to a warning with
  the offending map named in a comment.
- `shared/Chosen.Blood/Map/BuildLimits.cs`: the constants above as `Limits.Vanilla`, `Limits.NBlood`, `Limits.Extended`.

Files (change):
- `godot/Chosen.csproj`: `<ProjectReference Include="..\shared\Chosen.Blood\Chosen.Blood.csproj" />`.
- `godot/src/Sim/Build/Board.cs`: unchanged apart from the `using` of the moved map types. Delete
  `godot/src/Sim/Build/BloodMapFile.cs`, `XStructs.cs`.
- The ten files with `Sim.Build.`/`using Blood3D.Sim.Build` (`LevelLoader`, `Game`, `BuildDebug`, `PortalRenderer`,
  `NativeWorld`, `WorldMesher`, `NativeSky`, `WaterFx`, ...): `using Chosen.Blood.Map;`.
- `godot/src/Sim/Build/BuildDebug.cs` `bmapcheck`: also runs `MapValidator` and prints its issues.
- `godot/README.md`: a "Shared library" paragraph (what lives in `shared/`, how to build, `maptool`).

Done when: `dotnet build -v q` in `godot/` and `shared/Chosen.MapTool/` pass; `dotnet run --project
shared/Chosen.MapTool -- roundtrip "game/blood3d/maps/*.map" "game/blood3d/addons/*/maps/*.map"` reports every map
identical (list the count: it must equal the number of files); `maptool check` on the same reports no error; `bugs.py
regress all` no CHANGED; `bugs.py replay <id>` matches (the moved reader changed nothing); `geomcheck` lists no difference; `python -I
pipeline/blood_map.py <blood dir> E1M1` and `maptool dump` agree on counts (spot check). Note for the record why NBlood's
own save would not be identical: it bumps `MapRev` (new encryption keys), writes `AppProperName` into header2, forces
the x sizes, zeroes `fogpal` and `blend`, stores `busy = state << 16`, drops statnum ≥ 1024 sprites and rewrites
`index`. Our writer keeps all of those as stored.

### Stage 2: .MAPX, and the game reading it

Goal: the format exists, converts both ways, and the game plays a .MAPX exactly as its .MAP.

Files (create):
- `shared/Chosen.Blood/Map/MapXFile.cs`: `public sealed class MapXExtras { Dictionary<string,string> Info;
  List<(int sprite, string key, string value)> SpriteExtras; Dictionary<(kind,index), string> Names; EditState Edit;
  List<(string fourcc, byte[] payload)> Unknown; }`; `public static (BloodMapFile map, MapXExtras extras)
  Read(byte[] data)`; `public static byte[] Write(BloodMapFile m, MapXExtras x)`; chunk table as in Decisions.
  `Read` fills `BSprite.Patch` from `SPRX` so the game's renderer sees the same keys as from a `.patch`.
- `shared/Chosen.Blood/Map/MapPatch.cs`: the `.patch` line parser (moved from `BloodMapFile.ApplyPatch`) with
  `Apply(BloodMapFile)` (sector heights) and `SpriteExtras(IEnumerable<string>)` (the model keys), used by both
  `PrepareForGame` and `maptool tomapx`.
- `shared/Chosen.Blood/Map/MapConvert.cs`: `ToMapX(BloodMapFile, patchLines) → (map, extras)`; `ToMap(BloodMapFile,
  MapXExtras, Limits target) → (bytes, List<string> dropped)` with the warnings list of Decisions.
- `shared/Chosen.MapTool`: `tomapx <in.map> <out.mapx> [--no-patch]`, `tomap <in.mapx> <out.map> [--patch]` (writes the
  `.patch` from `SPRX`), `roundtrip` extended: .MAP → .MAPX → .MAP identical, and .MAPX → bytes → .MAPX identical.

Files (change):
- `BloodMapFile.Read/Load`: dispatch on magic. `Load` of a `.mapx`: no `.patch`, `PropagateMarkers` only.
- `godot/src/Data/Addons.cs` `BuildMapPath(name)`: a `fs_mapx` folder's `<NAME>.mapx` first when the cvar is set
  (`Cvars.Register("fs_mapx", "", "...", saved: false)`), else `.map`, else `.mapx` beside it.
- `godot/src/Map/LevelLoader.cs` `Load(path)`: a path with a directory and a `.map`/`.mapx` extension that exists is
  loaded as is (`LoadBuild(path)`), the add-on activated from its name; `MapPath` stays `res://maps/<name>.map`.
  `Game.LoadMap` already passes `--file` through; the `map` console command gets the same treatment.
- `pipeline/bugs.py`: `regress ... --mapx`: converts each needed map with `maptool tomapx` into `tools/mapx/`, adds
  `fs_mapx <that dir>` to the run's `--exec`, everything else as now (the restore run is unchanged: `fs_mapx` is unsaved).
- `godot/README.md`: ".MAPX" section (the chunk table, the patch relation, `fs_mapx`, `--file`).

Done when: `maptool roundtrip` passes for both directions on all maps; `maptool tomap` of a converted E1M4 (it has a
`.patch`) prints the sprite-extras warning and `--patch` writes a `.patch` equal to the original's model lines;
`python pipeline/bugs.py regress all --mapx` shows no CHANGED (and no "heap ran out" warning); one direct run as a
smoke test, off-screen and windowed per CLAUDE.md:
`Godot_console.exe --position 20000,20000 --windowed --resolution 1280x720 --path godot -- --file=<abs>/tools/mapx/E1M1.mapx --cam=-938.9,-2699.5,312.8,-104.9,0.6 --wait=2 --shot=<abs>/tools/mapx/e1m1_mapx.png`
against the same with `--map=e1m1`: the two PNGs identical (no `--exec`, so nothing to restore). Hot reload: with that
run kept open (no `--shot`, so quit it by hand afterwards) touch the .mapx and see "level changed on disk".

### Stage 3: Forge skeleton: open, show, save (milestone 1)

Goal: Chosen Forge opens a .MAP or .MAPX, shows it in 2D like UDB's window, and saves it back unchanged.

Files (create), all under `forge/`:
- `forge/project.godot` (`config/name="Chosen Forge"`, assembly `ChosenForge`, main scene `res://main.tscn`, Forward+
  not needed: use `gl_compatibility`? No: keep Forward+ so the 3D preview can reuse the game's shaders later; window
  1600x900, `window/size/initial_position_type` centred), `forge/ChosenForge.csproj` (net8.0, Godot.NET.Sdk/4.7.2,
  `ProjectReference` to `shared/Chosen.Blood`), `forge/main.tscn` (root `MainWindow`), `forge/run.bat` (build + start,
  like `godot/play.bat`; `forge\run.bat <map>` opens a file).
- `forge/src/MainWindow.cs`: the layout as UDB's `MainForm`: `MenuBar` (File: New, Open, Open Recent, Save, Save As,
  Save As Extended (.MAPX), Export compatibility .MAP..., Map Options, Exit; Edit: Undo, Redo, Cut/Copy/Paste (stage 4),
  Select All, Clear Selection, Preferences; View: zoom items, Toggle Grid, Grid size, Go To Coordinates, Toggle Info
  Panel, Snap to Grid, Centre on Selection; Mode: Vertices, Walls, Sectors, Sprites, Draw Lines, Draw Rectangle; Tools:
  Map Analysis (stage 6), Test Map (stage 6); Help: About), the main toolbar (new/open/save, undo/redo, cut/copy/paste,
  snap, grid, zoom), the **mode toolbar** on the left (UDB's `modestoolbar`: one toggle button per mode), the display in
  the middle, the docker on the right (`TabContainer`: Statistics tab in this stage; Sprite Filter, Errors later), the
  **info panel** at the bottom (one panel per element kind, swapped by what is highlighted, UDB's `panelinfo`), the
  **status bar** (status text, cursor coordinates in Build units, grid size, zoom %, map name, format mode "MAP" /
  "MAPX"). `DisplayStatus(StatusType, string)`, `UpdateCoordinates(Vector2)`, `ShowSectorInfo/ShowWallInfo/
  ShowSpriteInfo/ShowVertexInfo/HideInfo` mirror UDB's `IMainForm`.
- `forge/src/Document/MapDocument.cs`: `List<BSector> Sectors; List<BWall> Walls; List<BSprite> Sprites;` the header
  fields; `MapXExtras Extras`; `string Path; MapFormat Format (Map, MapX); bool Dirty`; `IBuildData`;
  `static MapDocument Open(string path)` (keeps `.patch` handling as a question to the user: apply / ignore);
  `byte[] SaveBytes(MapFormat)`; `BloodMapFile ToMapFile()`; derived caches rebuilt on `Changed`: `PointIndex`
  (point → wall chain, by `dragpoint` chains), `WallPairs`, `SectorLabelPos`, bounds. Stage 3 only reads.
- `forge/src/View/MapView2D.cs` (Control): camera (centre, scale), `MapToScreen/ScreenToMap`, `_Draw`: background
  (UDB's `BACKGROUND`), grid (`GRID`, every 8th line `GRID64` brighter; sizes 16..4096 Build units, hidden below 4 px
  spacing), walls (white walls `LINEDEFS` bright; red pairs dimmer as UDB draws two-sided lines; blocking (cstat 1)
  brighter; masked (16) and one-way (32) in their own colours from `ColorSettings`; normal tick at the midpoint toward
  the front wall's sector, 4 px), the first wall of every sector marked by a short cross-tick at its start, points as
  3-5 px squares (`VERTICES`), sprites as the markers of Decisions (angle arrow length = radius), sector labels at
  `SectorLabelPos` with index and type name when zoomed in enough, highlight (`HIGHLIGHT`) and selection (`SELECTION`)
  colours, the drag rectangle. `RedrawDisplay()`. Colours in `forge/src/Config/ColorSettings.cs` seeded with UDB's
  defaults (names as `ColorCollection`).
- `forge/src/Editing/EditingManager.cs`, `EditMode.cs` (hooks as in Decisions), `ClassicMode.cs` (the mouse state
  machine of UDB's `ClassicMode`: select/edit pressed, drag threshold, multi-selection rectangle, panning),
  and read-only `VerticesMode`, `WallsMode`, `SectorsMode`, `SpritesMode` that highlight the nearest point / wall pair /
  containing sector (Tab cycles overlaps) / sprite within UDB's highlight range (scaled by zoom) and fill the info panel.
- `forge/src/Ui/InfoPanels/*.cs`: `VertexInfoPanel` (x, y, number of walls at the point, sectors), `WallInfoPanel`
  (index/pair, length, angle, both sides: picnum with preview, shade, pal, repeats, pannings, cstat as named flags,
  type name, RX/TX), `SectorInfoPanel` (index, type name, floor/ceiling z (Build and in Quake units), heights, picnums
  with previews, shades, pals, slopes, visibility, RX/TX, first wall), `SpriteInfoPanel` (index, type name, picnum
  preview, position, angle, sector, cstat flags, data1-4 with captions). Previews from `forge/src/Data/TileImages.cs`
  (a tile's pixels from `Chosen.Blood.Files.ArtSet` through the palette into a cached `ImageTexture`, made on first
  use; a grey box with the number when there is no Blood folder).
- `forge/src/Data/ForgeData.cs`: the Blood folder (preference; on first start `BloodInstall.Find()`, else the
  preferences dialog asks for it, with "skip" allowed), the `ArtSet` and palette loaded from it, tile sizes from the
  ART headers. An add-on's own art (a map from `addons/<folder>`) is out of scope for phase one: base tiles are shown.
- `forge/src/Config/ForgeConfig.cs`: `user://forge.cfg` key/value (Blood folder, recent files, window placement, colours,
  grid default, snap), `forge/data/keys.cfg` (action → key, UDB names), `forge/src/Ui/PreferencesDialog.cs` (data
  folder, colours, keys list), `forge/src/Ui/MapOptionsDialog.cs` (UDB's Map Options: the header fields: start
  position/angle/sector (read-only here), visibility, sky bits/offsets, song id, parallax type, map revision, format,
  the `INFO` fields title/author/comment).
- File dialogs: Godot `FileDialog` in native mode (`use_native_dialog`) with filters `*.map;*.mapx`.

Steps: project + csproj + build; MainWindow layout with empty display; MapDocument.Open + MapView2D drawing; pan/zoom;
modes with highlight + info panels; Save / Save As / Save As Extended; Map Options; preferences.

Done when (automated): `dotnet build -v q` in `forge/`; `forge\run.bat --roundtrip <out dir>` (a headless command-line
mode of Forge: open each base map through `MapDocument`, save as .MAP into `<out dir>`, quit) followed by `maptool
roundtrip` comparing originals with the saved files: identical. Done when (by hand; the user): open `E1M1.map`; the
window looks like UDB's (menus, main toolbar, mode buttons left, docker right, info panel bottom, status bar); the grid
and the level draw; wheel zooms at the cursor, MMB pans; V/L/S/T switch modes and the toolbar buttons follow; hovering
shows the right element and its panel (check a red wall shows two sides; a stacked place such as E1M2's hole into the
room below cycles with Tab); Alt+G toggles the grid; File > Save writes the .MAP (the game, running `play.bat e1m1`,
reloads it on save: hot reload), Save As Extended writes `.mapx` that `play.bat` loads with `--file=`.

### Stage 4: editing properties, selection, dragging, sprites, undo

Goal: everything that changes values without changing the wall graph: UDB's selection and drag feel, the four
properties dialogs, inserting/deleting/moving sprites, undo/redo.

Files (create):
- `forge/src/Editing/UndoManager.cs`: `int CreateUndo(string description)` (snapshot before the change),
  `WithdrawUndo()`, `PerformUndo()`, `PerformRedo()`, lists for the Edit menu; snapshots = `MapXFile.Write` of the
  document's core chunks, Deflate; restore re-reads and raises `Changed`.
- `forge/src/Document/MapDocument.cs` (change): the mutation API every mode uses, each validating in debug builds
  (`MapValidator` over the document, assert no error): `MovePoint(point, x, y)` (the dragpoint chain),
  `MoveSprite(i, x, y)` (+ `UpdateSector`), `SetSpriteZ`, `AddSprite(sector, x, y, z, type)` (z on the floor,
  `UpdateSector`), `DeleteSprite(i)` (renumbering), `EnsureXSector/XWall/XSprite(i)` (lowest unused `Extra`,
  `Reference`), `RenumberIndices(...)` helper, `SetFirstWall(sector, wall)` (xmapedit `setFirstWall`: rotate the loop's
  storage so the wall is at `WallPtr`, fixing `Point2`/`NextWall`/`WallPtr` of the sector only).
- Modes gain: selection (click toggle, rectangle, Ctrl subtract, Select All, Clear Selection), Drag sub-modes
  (`DragVerticesMode`, `DragSectorsMode` (moves the sector's points and the sprites inside), `DragSpritesMode`; snap to
  grid unless Shift; Esc cancels and withdraws the undo), Edit (RMB release) opening the dialog for the highlighted or
  selected items with multi-edit (UDB style: fields left blank apply nothing), Insert (sprites mode: new sprite at the
  cursor, type picked in the dialog that opens at once; vertices mode: stage 5), Delete (sprites), rotate sprites with
  Ctrl+Shift+wheel, Shift+L point at cursor, copy/paste properties (Ctrl+Shift+C / Ctrl+Alt+V).
- `forge/src/Ui/Dialogs/VertexEditDialog.cs` (x, y), `WallEditDialog.cs` (Front / Back tabs, each a Build wall: picnum
  with preview and a number field, overpicnum, shade, pal, xrepeat/yrepeat, xpanning/ypanning, cstat as named
  checkboxes with "apply blocking flags to both sides", type (list), RX/TX/command/trigger flags, the x-wall's other
  fields in a grid), `SectorEditDialog.cs` (Floor / Ceiling / Effects tabs: z, picnum, heinum (slope) with "relative to
  first wall n", shade, pal, pannings, stat flags, visibility, type (list), the x-sector: RX/TX, busy/wait times A/B,
  waves, triggers, light effect, panning, wind, depth, underwater, damage, key, locked, the rest in a grid),
  `SpriteEditDialog.cs` (as in Decisions). All dialogs: Apply writes through `MapDocument` inside one undo step; Cancel
  withdraws it. `TypeTable` supplies names and data captions; unknown types show "Unnamed <n>".
- `forge/src/Ui/Dialogs/TilePickerDialog.cs`: a minimal picker (number entry + the preview + a scrollable grid of the
  tiles in use in the map); the full texture browser is out of scope.
- Docker: `SpriteFilterPanel` (UDB's things filter: by category checkboxes; filtered sprites draw dim and don't
  highlight).

Done when (automated): `forge\run.bat --selftest`: opens E1M1, performs a scripted sequence through `MapDocument`
(move 50 random points by a grid step, move 20 sprites, add and delete 10 sprites, set the first wall of 10 sectors,
edit x-fields of 10 walls), validates after each, undoes all, and checks the document serialises byte-identical to the
opened file; exit code 0. Done when (by hand): drag a vertex in E1M1 with RMB and watch both sectors of a red wall
follow; rectangle-select sprites and drag them; RMB on a sector opens the dialog with the right values and Apply +
Ctrl+Z restores; multi-select two walls, edit, blank fields untouched; insert a sprite in sprites mode, pick a type,
see its name; save, `play.bat e1m1` reloads the edit live.

### Stage 5: geometry

Goal: draw and reshape sectors with Build's rules kept: draw mode, insert/delete points, delete sectors, join sectors,
red/white wall changes.

Files:
- `forge/src/Document/GeometryOps.cs` (partial `MapDocument`), each op one undo step, validator-asserted, written
  from the rules and xmapedit's GPL implementations in `third_party/xmapedit/src_blood/xmpmaped.cpp` (study, do not
  transliterate where a routine mirrors Build's mapedit): `InsertPoint(wall, x, y)` (`insertPoint`: on both walls of a
  pair), `DeletePoint(wall)` (`deletePoint`), `SplitSector(sector, wallA, wallB, points)` (`sectSplit`),
  `MakeSector(points)` / `InsertInnerLoop(parent, points)` (`insertLoop`, `redSectorMake`), `JoinSectors(a, b)`
  (`redSectorMerge`), `DeleteSector(s)` (`loopDelete` for every loop; neighbours' walls go white; sprites re-sectored or
  deleted), `DetachWall(w)` (red → white both sides), `AttachWalls(w)` (white walls lying exactly on each other, in
  opposite directions → red pair; `findNextWall`), `RepairFirstWall(s, oldFirstPoints)` (after an op moved the first
  wall: pick the wall giving the same plane).
- `forge/src/Editing/DrawGeometryMode.cs`: UDB's: click adds a point (snap to grid, snap to nearby points and walls with
  the highlight range), Backspace removes the last, Enter / clicking the first point finishes, Esc cancels; while
  drawing, lines preview with the length labels; finishing classifies the path as in Decisions and calls the ops;
  `DrawRectangleMode` (Ctrl+Shift+D) draws four points. Status text names the target sector ("drawing in sector 12";
  Tab cycles overlaps).
- Vertices mode: Insert = `InsertPoint` on the highlighted wall at the cursor; Delete = `DeletePoint`; dragging a point
  onto another point of the same loop is refused (status message) rather than merged.
- Walls mode: Delete says "a wall can't be deleted alone; delete the sector or the point"; Ctrl+Del = `DetachWall`;
  "Set first wall" (menu + key from keys.cfg); "Split wall" (Insert) same as vertices mode.
- Sectors mode: Delete = `DeleteSector`; J = `JoinSectors` on two selected sectors sharing red walls.
- Sprites touched by geometry ops get `UpdateSector`.

Done when (automated): `forge\run.bat --selftest` extended with a geometry fuzz: on E1M1, E2M1 and DWE1M1 (overlaps),
200 random ops (insert point on a random wall, delete that point, draw a random small rectangle inside a random sector
and join it back, split a random convex sector along a chord, delete a random leaf sector), validator after each op,
undo everything, byte-identical to the opened file; then save each fuzzed map before undoing and check `maptool check`
passes and the game loads it headless (`Godot_console.exe --headless --path godot -- --file=<fuzzed.mapx>
--shot=<png>` exits 0 and the log has no "CRC" or exception). Done when (by hand): in E1M1's yard draw a rectangle
inside the sector: a new sector appears with red walls, dialogs show the parent as the back side; draw a line across a
sector from wall to wall: it splits, both halves keep the floor picture and slope; delete the new sector: the yard is
whole again; join two neighbouring rooms; save as .MAP and open the result in NBlood (`bugs.py nblood`-style run or
xmapedit) to see the same geometry; save as .MAPX and play it in Chosen.

### Stage 6: mapper polish (small, each item optional)

Goal: the things a mapper expects before calling it usable.

- `forge/src/Editing/ErrorCheckMode.cs` + docker "Errors": `MapValidator` results plus xmapedit's checks worth
  porting (sprites outside sectors, missing player start, RX with no TX and the reverse, markers without owners,
  overlapping points on a loop, zero-length walls), click to centre on the item.
- Test Map (F9 as UDB): saves to a temp .mapx and starts `Chosen` (`godot/play.bat`'s command in development: the Godot
  binary with `--path godot -- --file=<tmp>`; later the installed `Chosen.exe`), windowed; the running game reloads on
  every save (hot reload), so the button is mostly "start the game here".
- Preferences: colours, keys, highlight range, grid defaults; Go To Coordinates (Ctrl+Shift+G).
- `godot/README.md` and a new `forge/README.md`: what Forge does, keys, the formats, the data folder.

Done when: the user runs through a 10-minute session (open, change a room, add enemies, test in the game, save both
formats) with no crash and the errors panel clean on the shipped maps except the known warnings.

## Edge cases and risks

- **Round-trip identity traps** (stage 1): the sprite `index` field holds stale values (keep `FileIndex`); the
  x-sprite's last 4 bytes; header2's text and padding; `fogpal`/`blend` bytes; the header's encryption only told by
  `songId`; v6 maps (0x603, unencrypted, 56/24/60 x sizes without header2; `lT = lB` is a load-time fix, not a file
  fact); sky offsets count = `1 << pskyBits` even when no sky sector exists. Miss one and the test says where.
- **Propagation leaking into the writer**: `Load` (game) vs `Read` (editor, tool). A map saved from a propagated model
  would drop orphan markers (statnum 1024) and rewrite `Marker0/1`, silently changing shipped maps. The round-trip test
  catches it on MRW2M3 and the 13 other maps with stale markers.
- **Index renumbering**: every op that inserts or removes a wall/sector/sprite must go through `RenumberIndices`;
  a missed reference (marker `Owner`, `StartSect`) shows up as the validator's "Reference mismatch" or a sprite in the
  wrong sector; the fuzz test exists for this.
- **Overlapping sectors**: point-in-sector is ambiguous there (stacked rooms, movers stored open: E1M2's train,
  coffin lids). Every "which sector" query takes the user's chosen candidate (Tab) and never auto-merges points of
  different chains; the validator must not treat overlap as an error (DWE1M1, E1M2 are fixtures).
- **Two geometry implementations** (stage 1): the game's exact Build ports and the engine core's fresh ones can disagree on
  edge cases (points on a wall, rounding in slopes); `geomcheck` finds where, and the core's version is fixed to match
  Build's behaviour from the rules, never by copying the port.
- **Godot project reference**: Godot's `dotnet build` must pick up `shared/` changes; if the editor build caches the
  old DLL, `.godot/mono/temp` needs clearing once. Note it in the README.
- **User-data migration**: Godot reads `driver.cfg` before C#; first start after the rename ignores a Vulkan choice
  (one restart fixes it, the console says so). A user with no old folder sees nothing.
- **Hot reload while Forge writes**: the game may read a half-written file. Forge writes to `<name>.tmp` and renames;
  the game already retries on the next 0.5 s tick if the parse fails (check `LevelLoader._Process` catches the
  exception; add the catch if it does not).
- **Platform**: paths with backslashes from Windows dialogs into `BloodMapFile.Load` (the game normalises with
  `Replace('\\','/')` in `Addons`); Forge should pass forward slashes too.
- **Performance**: a snapshot per edit (~1 ms, ~150 KB compressed) and a full redraw per mouse move (8192 walls as
  batched lines) are fine; the derived caches (`PointIndex`, labels) rebuild on `Changed`, not per frame. If a 60 Hz
  drag stutters on the largest add-on map, rebuild only the dragged chain's entries.
- **Licensing**: port from `src_blood` (NoOne, GPL) and NBlood (GPL) by reading; where `src_blood` carries Build's
  `insertpoint`/`deletepoint` logic unchanged, write ours from the rules in this plan. UDB is GPL too; we copy the
  interaction design, not code.

## Verification

After each stage, in this order: `dotnet build -v q` in every project touched; `maptool roundtrip` + `check` on all maps
(stages 1+); `python pipeline/bugs.py regress all` (stages 0-2; `--mapx` in 2) with no CHANGED and the user's settings
intact afterwards (`%APPDATA%/Godot/app_userdata/Chosen/config.cfg`: `b3d_monsters 1`, `b3d_hudsize 2`, `b3d_crosshair
1`, `r_dynamiclights 1`, `r_shadows 1`, `b3d_voxels 1`, `r_occlusion 1`, `vid_vsync 1`, `r_perfoverlay 0`); `bugs.py
replay <id>` on one Godot-era report (stage 1); `forge\run.bat --roundtrip` / `--selftest` (stages 3-5); the hand-tests
listed per stage, which the user does. Any game run started by hand is off-screen, windowed and ends with `--shot`.

## Out of scope for phase one

3D preview / visual mode (and moving the renderer into a shared place for it); the texture browser (only a number
field with preview and a tiles-in-use grid); ART, SEQ, QAV, sound editing; prefabs, copy/paste between maps, curve and
ellipse drawing, edit-selection mode (rotate/scale), find/replace, comments beyond `INFO`/`NAME`; editing NoOne's modern
features beyond their raw fields and names; Build (Duke) .MAP support; add-ons' own art in Forge; the loader itself and
the pipeline's conversions (beyond the readers in `Chosen.Blood.Files`); distribution builds (a separate piece of work:
`Chosen` beside the repository's folder with Chosen.exe, ChosenLoader.exe, and Forge as its own download); renaming `b3d_` cvars or
`game/blood3d`.

## Next: ChosenLoader and the distribution (after stage 1; to be planned in detail then)

Decided with the user so far:
- **ChosenLoader.exe**, a separate small app: finds the Blood install (Steam/GOG) or lets the player pick it, ticks
  add-ons, extracts with a progress bar into the data folder beside `Chosen.exe`. Players get no .bat files and no
  Python: the runtime parts of the pipeline are ported to C# in `Chosen.Blood.Files`, each step checked against the
  Python output (decoded pixels, not PNG bytes). The inventory of what the game reads and which step makes it: 29
  runtime file kinds, ~1,500 lines of Python; DarkPlaces-only outputs are not ported.
- **Palette-recoloured sprites (`_pP` textures) are made by the game at load time**, not extracted: the recolour goes
  through the PLU tables on palette indices, so it works from the tile's index data (ART through `Chosen.Blood.Files`,
  or an indexed form the loader stores), never from the RGB PNGs. Removes the 5,364 files no current script can make,
  and fixes the add-on lookup bug (a tile an add-on replaces shows the base game's `_pP` file).
- **Distribution** into `Chosen` beside the repository's folder: Chosen.exe (export preset `godot/export_presets.cfg`, Godot's .NET
  export templates installed), ChosenLoader.exe; Chosen Forge as its own download. `Chosen.exe` finds its data folder
  beside itself (today `fs_blooddata` defaults to the repository's `game/blood3d` through `res://`, which an exported
  game doesn't have). A headless export takes over 10 minutes: the build script runs it in the background.

## Open questions (resolved: the user accepted the recommendations)

1. **2D orientation.** Build's (y down, like mapedit/xmapedit; every coordinate equals what `bsec`, `sprat`, `warp` and
   spots.txt print) or UDB's (y up)? Recommend Build's; a View > Flip Y toggle can come later if Doom mappers ask.
2. **Save keeps the file's format?** Recommend yes: Save on a .MAP writes a .MAP (and warns once if Chosen-only data
   exists), Save on a .MAPX writes a .MAPX; "Save As Extended" / "Export compatibility .MAP" switch explicitly.
3. **Opening a .MAP that has a `.patch` beside it:** apply Refreshed Supply's fixes by default (the map then saves with
   fixed heights, which NBlood would also show) or keep the original and only remember the sprite keys? Recommend ask
   once per file with "apply" as the default: the fixes are real geometry corrections.
4. **MapRev on save:** increment like mapedit (other tools see a new revision; the encryption keys change) or keep?
   Recommend increment.
5. **Repository layout:** `shared/` and `forge/` as new top-level folders beside `godot/` (recommended: the loader joins
   as a third consumer at the top level), or everything under `godot/`?

## Implementation notes (where the result differs from the wording above)

- Stage 0c also removed the old QuakeC-era mover helpers left without callers (MoverRider, MoverPushSprite, ...) and
  the volume lookups only levels without Build sectors used (SectorAtPoint, VolumeBox, SectorZRange); the `layer`
  console command went with the world-level layers. `StackedRooms` gets layer 0 for sprites where SectorMap has layers,
  as before (World.SpriteLayer always returned 0).
- .MAPX `HEAD` also stores the x-structure sizes (u16 x3) and the bytes after the .MAP's CRC, and header2 behind a
  has-header2 byte (v6 maps have none): needed for .MAP -> .MAPX -> .MAP byte identity. Strings are u16 + UTF-8.
- Stage 2's smoke test: the `--file=E1M1.mapx` and `--map=e1m1` shots are not pixel-identical, but two `--map` runs
  aren't either (real-time `--wait`): a second .mapx run differs from the .map runs by 525-574 pixels, about as two
  .map runs (157) and less than two .mapx runs (2850). `regress all --mapx` (240/240 same, every level read from
  .mapx) is the check. The hot-reload hand test is the user's.
- Forge: `--shot=<png>` (with `--mode=`, `--do=`, `--hover=`) for looking at the window from a test run; the self-test
  uses a deeper undo than the window's 100 levels (it makes 110 steps). The geometry operations go through one graph
  commit (GeometryOps) instead of a RenumberIndices helper per operation.
- After the review: the geometry commit keeps untouched walls' links as stored (one-way links, walls in no sector);
  point deletion and rooms repair the first wall like splits do (a slope that can't be kept is reported); drawings
  that cross walls are refused; J keeps the highlighted sector's properties (else the lower-numbered); a cancelled new
  sprite is taken back and its insert and dialog are one undo step; hot reload retries an unreadable file for 5 s.
- Stage 5's game load of the fuzzed maps runs windowed off-screen, not headless: the game's `--shot` can't save a
  picture headless and then never quits.
