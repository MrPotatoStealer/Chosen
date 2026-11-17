# Chosen: Blood in Godot

Blood rewritten for Godot 4.7 in C#. The game (`src/Sim`) is a hand port of the DarkPlaces version's QuakeC server (Chosen was called Blood3D then), which was itself
a port of Blood's C, now running on Blood's own level data: each level loads from its Blood .MAP (`src/Sim/Build`) and
is drawn from it (`src/Render/Native`; "Levels from Blood's .MAP" below). NBlood is the reference for how things should
look and play. The DarkPlaces version is retired (not in this repository; the game's generated tables,
`GenTables.cs`, `GenConst.cs` and `GenAiStates.cs`, were made from its QuakeC), and so is the conversion of Blood's levels to TrenchBroom brush maps that came before. Bug recordings
(`bugs/<id>/recording.rec`) remain a regression test set for the simulation (see Checking below).

## Setup

* Godot 4.7.2 **mono** is in `tools/godot-4.7-mono/Godot_v4.7.2-stable_mono_win64/`, and the .NET 8 SDK is needed
  to build (`dotnet build` in `godot/`). Both are git-ignored.
* Blood's data (its tiles as ART files, palettes, tables, sounds, music, movies, voxel models, the levels' .MAP files
  with Refreshed Supply's `.patch` beside them, the add-ons') is read at runtime from the data folder: the cvar
  `fs_blooddata`, else `--data=<folder>`, else `data\` beside the exe in an exported game, else the repository's
  `game/blood3d`. It is made from a Blood install by the C# extraction (`shared/Chosen.Blood/Files/Extractor.cs`;
  "Data folder and distribution" below): `dotnet run --project shared/Chosen.MapTool -- extract "<Blood folder>"
  game/blood3d --addons all`, or ChosenLoader. (The Python pipeline, `build_assets.bat` and `install_addon.bat`, made
  the same files and more for the DarkPlaces version; the C# extraction's files are identical to its, but for the voxel
  models: `Chosen.Blood`'s `VoxelModels.Md3` merges each layer's faces of one colour into rectangles, a vertex wherever
  another face's corner lies on an outline so no T-junction leaves a crack, about four fifths of the triangles of a quad
  per face; the game reads the face normals for the dynamic lights, `Render/Voxels.cs`.)
* Refreshed Supply's add-ons (the install's `addons\<folder>`: Death Wish, Cryptic Passage, MARROW) go into
  `<data>/addons/<id>` with the rest (`--addons all|none|dw,cryptic,marrow`). See "Add-ons" below.
* The map editor is Chosen Forge (`forge/`, `forge\run.bat`; `forge/README.md`), a separate app in the manner of
  UltimateDoomBuilder. xmapedit
  (`third_party/xmapedit`, NoOne's Blood editor, cloned from https://github.com/NoOneBlood/xmapedit) is a reference for
  Blood's editing rules: `build_xmapedit.bat` builds it with MSVC's nmake into `third_party/xmapedit/mapedit-data` and
  copies the Blood files it needs next to it.

## Data folder and distribution

The game reads only what it needs from Blood, extracted once from the player's own install by
`shared/Chosen.Blood/Files/Extractor.cs` (a port of the runtime parts of the Python pipeline, each output byte for byte
as the pipeline wrote it: `maptool compare game/blood3d <extract>` checks it), in about three seconds:

| Path | What | From |
|---|---|---|
| `art/tilesNNN.art` | the tiles, copied: `Data/TileArt.cs` makes their pictures (opaque, masked, recoloured by a sprite's pal through the palette lookups as Blood draws it; voxel palette strips; the console background, tile 2046) | TILES*.ART |
| `gfx/` | palette.lmp, water/beast/sewer/invuln1.lmp, normal.plu, palookup.bin | BLOOD.RFF |
| `data/` | rt_tiles/seq/qav/sfx.txt (and the plain tables), qav.txt, blood.ini, loc_english.txt, rfire.clu, voxels.txt | BLOOD.RFF, SOUNDS.RFF, the DATs, BloodEX.kpf |
| `sound/` | blood/*.wav, midi/ (.mid, gmtimbre.tmb), cdtracks/, sc55/ | SOUNDS.RFF, bloodNN.ogg, SC-55 |
| `movie/` | the DOS .smk/.wav, remastered/ .ogv and .srt | DOS/BLOOD121, movie/ |
| `models/voxels/` | the voxel models as MD3 (`VoxelModels.cs`: Blood's and Refreshed Supply's) | BLOOD.RFF, BloodEX.kpf |
| `maps/` | every level's .MAP and Refreshed Supply's .patch | PATCH01.RFF, BLOOD.RFF, BloodEX.kpf |
| `addons/<id>/` | an add-on's art, tile tables, blood.ini, addon.txt, music, cutscenes, maps | addons/<folder> |

**ChosenLoader** (`loader/`, a small Godot app: `loader/src/LoaderWindow.cs`) is that extraction for players: it
finds the install (Steam's library folders, GOG's), offers the add-ons, extracts into `data\` beside Chosen.exe with a
progress bar (`--auto --blood=<folder> --out=<folder> --addons=... --mod=<folder>` runs it unattended; **Add a Mod
Folder...** takes a mod made for NBlood from anywhere on disk, its folder or the one it was unpacked into
(`Addon.ModsIn`), and lists it with the add-ons; the data folder keeps where it came from (`addon.txt`, `source`), so
the loader run again after an update lists it again (`Addon.InstalledMods`); "Add-ons"). An exported Chosen.exe with no
data folder starts ChosenLoader instead.

`build_dist.bat [folder]` makes the distribution: `Chosen` beside the repository's folder with Chosen.exe, ChosenLoader.exe and
README.txt (`dist/`), and `ChosenForge` beside it with ChosenForge.exe, each a Godot release export
(`export_presets.cfg` in `godot/`, `loader/`, `forge/`; .NET included); each export takes a few minutes. Players run
ChosenLoader once, then Chosen; no Python, no .bat files. `build_release.bat` makes a GitHub release of them from the
commit tagged v<VERSION>: their packs checked by `pipeline/pck_list.py --check` (a Godot export packs every file of its
project folder that no filter excludes, so the presets exclude `tests/*`: Forge's replay screenshots, pictures of
Blood's art, were in ChosenForge.exe), zipped with their SHA-256 sums into `release/` (CONTRIBUTING.md "Versions").

**Versions.** `VERSION` (0.1.0) is the game's, Forge's and ChosenLoader's (`Directory.Build.props`: the assemblies'
version, and `Shared.Engine.ChosenVersion.Full`, `0.1.0-dev+<commit>` from the repository and `0.1.0+<commit>` from
`build_dist.bat`, which checks the export presets' and `project.godot`'s versions agree with it). The title screen shows
it, the console's `version` prints it, and every report carries it (`version:` in `report.txt`, the game's and Forge's
alike); Forge's title bar and About and ChosenLoader's window show it, and a .MAPX saved records it (`INFO` saved_by).
The data folder has its own: `data/extracted.txt` says which `Extractor.DataVersion` made it and with which Chosen;
a folder made by a lower one (or with no `data` line: before 0.1.0) gets a red line on the title screen and the
console saying to extract it again (`BloodData.Outdated`). Raise `DataVersion` whenever the extraction's output
changes in a way the game needs.

## Shared libraries (`shared/`)

The game and Chosen Forge (`forge/`) share two plain .NET 8 libraries, referenced from
`Chosen.csproj` (Godot builds and copies them with the game):

* **`shared/Chosen.Engine`** (namespace `Chosen.Shared.Engine`): the clean core, code written here only, nothing
  copied or translated from Build, NBlood, xmapedit or UDB (`PLAN.md` "Clean core"; like everything here, GPL v2). Geometry on any level of sectors and wall loops (`Geometry/IBuildData.cs`, `BuildGeom`: point in sector,
  slope heights, loops and winding, `GeomValidator`).
* **`shared/Chosen.Blood`** (namespace `Chosen.Shared.Blood`, GPL v2 like NBlood): Blood's .MAP reader
  (`Map/BloodMapFile.cs`: `Read` as stored, `Load` for the game: Refreshed Supply's patch and marker propagation) and
  writer (`BloodMapWriter`: a map read and written unchanged comes out byte for byte), Chosen's .MAPX (`MapXFile`,
  `MapConvert`, the `.patch` as `MapPatch`; below), the x-structures
  (`Map/XStructs.cs`, generated by `shared/tools/gen_xstructs.py` from `pipeline/blood_map.py`'s field tables), the map
  rules (`MapValidator`, `Limits`), the type table (`Types/TypeTable.cs`, xmapedit's OBJTYPES.INI embedded), and Blood's
  files (`Files/`: RFF archives, ART tiles, the palette, finding the install), the start of the pipeline's port to C#.

Every file there starts with its provenance (`// Chosen core: original code.` or `// Chosen Blood module (GPL v2): ...`).
The game keeps its own exact ports of Blood's engine (`Sim/Build/Board.cs`, `World.BuildClip`, `World.NativeTrace`):
the console command `geomcheck <map|all>` compares Board with the core's `BuildGeom` (all maps: inside differs at 54 of
8.9 million points, slope heights over 2 z at 5 of 713,000 wall points, worst 9 z), and `bmapcheck` runs the map rules.

`maptool` (`shared/Chosen.MapTool`, no Godot: `dotnet run --project shared/Chosen.MapTool -- <command>`):
`roundtrip <globs>` (every base and add-on map comes back byte for byte, directly and through .MAPX: 141 of 141),
`tomapx <in.map> <out.mapx> [--no-patch] [--lights]`, `tomap <in.mapx> <out.map> [--patch] [--limits ...]`, `check [--limits
vanilla|nblood|extended] <globs>` (no errors on the shipped maps with NBlood's limits; MARROW's levels exceed
vanilla's), `dump`, `diff <a> <b>`, `stats`, `types`, `install` (the Blood install found), `tiles <blood dir>
game/blood3d [all|n,..]` (every tile decoded through the readers equals the pipeline's PNGs: 10,376 pictures).
After changing `shared/`, if Godot runs an old copy, delete `godot/.godot/mono/temp` once.

### .MAPX

Chosen's own map format (`Map/MapXFile.cs`), for what a Blood .MAP can't hold. Little-endian: `MAPX`, u16 version (1),
u16 flags, then chunks to the end (fourcc, u32 length, payload); a reader keeps chunks it doesn't know and writes them
back. The version changes only when an older reader would misread a file: a new chunk, or new fields at the end of a
chunk (read when bytes are left, as `LITE`'s and `ATMO`'s sizes allow), keep version 1; a field changed or moved raises it,
the reader still reading every older version, and a file newer than its reader is refused (`MapXFile.FormatVersion`).
Reading an older file: a chunk whose layout changed reads by its `Reader.Version`, and `MapXFile.Migrations` (one step
per version, v to v + 1, run in order after the chunks; `Read` refuses to run with a step missing) puts right what needs
the whole map; `MapXExtras.FileVersion` keeps the file's version (Forge says when it opened an older one), and saving
writes the current version. `--selftest`'s `mapx version` checks the version written, read and refused.

| Chunk | Payload |
|---|---|
| `INFO` | UTF-8 `key=value` lines: generator, created, modified, saved_by (the Chosen Forge and its version that saved it), source_crc (the .MAP it came from), title, author, comment |
| `HEAD` | the .MAP header (start, sky bits, visibility, song, parallax type, MapRev), the .MAP's version and header encryption, header2 (128 bytes, if any), the x-structure sizes, the sky offsets, the bytes after the .MAP's CRC |
| `SECT` / `WALL` / `SPRT` | u32 count; each Build record unencrypted (40 / 32 / 44 bytes, `Map/MapRecords.cs`), u8 has-x, the x-structure as the .MAP stores it |
| `SPRX` | sprite extras from Refreshed Supply's `.patch` (voxel model turn, offsets, none): u32 count; u16 sprite, key, value |
| `NAME` | names per object (u8 kind: 0 sector 1 wall 2 sprite, u16 index, string): the editor's |
| `LAYR` | the editor's layers: never read by the game |
| `ARTS` | the map's own art (Forge's Map Options > Art): u16 count; each u8 flags (1 override), the path of an ART file or a folder of them, relative to the map's folder (or absolute). Loaded over Blood's tiles and the add-on's in order (`Shared.Blood.Files.MapArt.Compose`): an override set's files replace those of the same name whole, an added set's tiles go over one by one (its empty tiles leave the ones under them). Its pictures may be numbered up to 30463 (NBlood's user tiles), a .MAP's below 6144 |
| `LITE` | the map's own dynamic lights (`Shared.Blood.Map.LightRules`, `MapLights.cs`; Forge's Lights mode): u32 count, u16 record size (newer fields past this reader's are skipped), each u8 kind (0 point, 1 flame, 2 fixture, 3 glow), u8 flags (1 shadows, 2 follows its sector's light), i32 sprite (-1: a light of its own), i32 x, y, z (Build units: a point light's place, an attached one's offset from its spot on the sprite), u8 r, g, b, i32 reach (Build units), f32 energy, flicker, up (a fixture's place on its picture), i32 tile (a fixture's: dark when its sprite shows another), i32 sector (a point light's room where rooms share its place, -1: the one holding it; records without it are still read) |
| `ATMO` | the map's atmospheres (`Shared.Blood.Map.AtmosphereRules`, `MapAtmosphere.cs`; Forge's Atmosphere mode): i32 count, each a u16 size and its fields (a reader takes what its size holds): the name (u8 length, UTF-8), i32 fog distance (Build units to where the distance fog is complete; 0: the map's, from its visibility), u8 flags (1 the fog's own colour, 2 the mist's), u8 fog r, g, b, i32 mist height (Build's heights over the floor; 0: none), f32 mist density, u8 mist r, g, b, f32 rain, snow, leaves (0 none, 1 the game's amount), i32 wind angle (a Build angle), f32 wind speed (Build units a second), gusts, fade (seconds); then i32 sectors and a u8 a sector, its atmosphere (0: the first, the map's own) |
| `EDIT` | the editor's view (centre, zoom, grid, mode): never read by the game |
| `CRC ` | CRC-32 of everything before it; the last chunk |

Strings are u16 length + UTF-8. A .MAPX is self-contained: converting (`maptool tomapx`, `Map/MapConvert.cs`) applies
the `.patch` beside the .MAP (sector heights into the data, sprite keys into `SPRX`), and the game applies none to it.
Back to .MAP (`tomap`) is lossless for the Blood data; it lists what the .MAP drops (`SPRX`, which `--patch` writes as
`<name>.patch` again, `NAME`, `EDIT`, `ARTS`, `LITE`, `ATMO`, unknown chunks), counts over the target's limits and pictures numbered
6144 and up (kept, but Blood has no such tiles).

A level loaded from a .MAPX with `ARTS` uses that art (`Data/LevelArt.cs`, set by `LevelLoader` before anything reads a
tile: `TileArt` composes the sets over the base and add-on art, `Tiles` adds a row for each of the sets' tiles from the
tile itself, keeping SURFACE.DAT's surface, SHADE.DAT's shade and the voxel model of a tile it replaces; the textures
made again when the sets or their files' times change, so Forge packing again shows on the next reload); a missing set
is said in the console and left out.

The game reads either (`BloodMapFile.Read` tells them by their first bytes): a level's `.map`, else a `.mapx` beside it
(`Data.Addons.BuildMapPath`); `--file=<path>` and the `map` console command take a `.map` or `.mapx` file anywhere (the
level is named after the file: bug reports and recordings still say `res://maps/<name>.map`; `--addon=<id>` plays a level
no add-on lists with that add-on's art, sounds and tables: Forge's Test Map of a map in a mod's folder), `reload` loads it again,
and hot reload watches the file loaded (a file that can't be read yet is kept as it was). `fs_mapx <folder>` (unsaved)
plays `<folder>/<NAME>.mapx` instead of each level's .map: `python pipeline/bugs.py regress all --mapx` converts the
levels into `tools/mapx` and checks every regression view through them.

## Enhanced features

Options > Enhanced: additions that aren't Blood's own, each a saved cvar, all off by default (`Render/Enhanced.cs`).

* **Transparent water** (`r_waterportals`, `r_wateralpha` its opacity): a water surface linked to the room under it
  (Blood's kMarkerUpWater / LowWater links: the river over its underwater room) is drawn as a portal, the other room
  seen through it (from under water: the room above), the surface blended over it. `Game.SetupNativeWorld` makes the
  water links portals (`Link.Portal`), the native world draws their floors and ceilings as the `Water` kind (blended
  at `water_alpha`, writing depth for the fog's blur) and `PortalRenderer.SetupNative` puts a portal on each (its slot's
  w 2: a water portal hides nothing of the level, under a water surface lies the void or a boat's hull) with a black
  face a hair under it for when it isn't drawn (more than `Slots` in view). A water plane that moves (E2M1's bobbing
  lake) only moves its face; a water sector that slides (E2M1's ice floes) has its face and mask made again
  (`MaskRows`, rasterised). Sprites are copied through a water portal only where they can be seen through it, within
  1024 units, and only those of its far room's own sectors (`FarRoom`). The option takes effect at once (the native
  world, the portals, the dynamic lights' regions and the sprites' portal links made again). `fly` crosses links too
  (`FlyCamera`, `LinkPathCross`). Water already drawn with Blood's room-over-room picture (tile 504: E2M5's pools) is a
  portal anyway.
* **Fog** (`r_fog`; `r_fogdensity`, `r_fogglow`, `r_fogblur`; `Render/EnhancedFog.cs`): in place of Build's distance
  fades (the shaders' `build_visibility` is 0 with it on: the sectors' own shades stay, the fog makes the fade). The
  fade is Godot's depth fog, per pixel on every material as it is drawn, the translucent ones too (the see-through
  water fogs as the walls behind it), on Build's curve (`FullAt`): to black, complete at 4 / visibility, its power 0.53
  (Build's brightness (1 - shade / 64)^2.2 halves a quarter of the way); a level whose sky has palette 1 (Death Wish's
  mist) to white, complete at 1.97 / visibility, power 1.4 to 0.82 (Build whitens in the palette's colours, the fog
  blends in linear light: linear, near things were washed out). One colour for the level: Build whitens only its
  palette-1 faces (DWE3M1's dock stays clear there), and loses the sectors' own visibility rates. The dynamic lights
  glow in a thin volumetric fog (a sixth of the light gone where the fade is complete, `r_fogglow` their volumetric
  fog energy, 6 at 1). The blur is the camera's far depth of field, from a third of the way to nine tenths; the
  see-through water writes its depth for it (else it took the room's under it, far down). The sky faces take no fog
  (`fog_disabled`: the environment's sky affect only reaches the background, and the fade and the lights' glow lay
  over the sky).
* **Water** (Options > Enhanced > Water; `Render/WaterFx.cs`). Fog in the water (`r_waterfog`) and moving light on what lies under it (`r_caustics`): a
  fragment works out how much of its line of sight is under the surface (`water_path`): all of it with the eye under water, from a copy through a water portal
  (its `portal_data` row's w 1) the part past the plane, so it shows looking down from above too; through a link joining two rooms under water (w 2,
  `PortalRenderer` `Submerged`: E4M4's, Forge's water under water) all of it with the eye under water, one body of water across the link; the colour is the commonest surface's picture at its shade.
  The view under water moves as Quake's (`r_waterwarp`: the Hud's first pass, under the weapon, and the field of view breathing). On the surface: ripples
  (`r_waterripple`) in steps of the picture's own pixels, the sky at a low angle (`r_watershine`), the level reflected by a march over the depth buffer (`r_waterreflect`:
  its own shader, reading the screen costs even unused; what it misses shows the sky), mist lying on it (`r_watermist`: `GroundMist`'s volumes over `NativeWaterGroups`).
* **A level's materials and pictures** hold the graphics driver's bindings (D3D12's descriptor heap, `rendering_device/d3d12/max_resource_descriptors`, raised to 262144; the largest level uses about 32000). Run out and whatever comes next isn't drawn, a level with holes and missing walls until a restart; the log then fills with "not enough room in the RESOURCES descriptor heap". So they go with their level: `SpriteRenderer.Setup` clears its material caches, `Game.StartLevel` collects at once (a dropped C# wrapper holds its material until the collector runs). `bugs.py regress` warns if the heap ran out.
* **F10** (`r_compare`, `Atmosphere.ToggleComparison`): all the enhanced options off to show the difference, and back as they were. Held, not set (`Cvars.Hold`): quit with them off and the config still has the real values. One changed by hand meanwhile keeps that value.
* **The map's lights** (`DynamicLights.SetupSpriteLights`): a .MAP's come from its sprites by the game's rules
  (`Shared.Blood.Map.LightRules`: torches and candles, light fixtures by their tile, decorations drawn fullbright), as
  they always have. A .MAPX with lights of its own (a `LITE` chunk, placed in Forge's Lights mode or made there by
  Tools > Generate Lights, or `maptool tomapx --lights`) gets those alone, none made from its sprites, even when the
  list is empty (`SetupPlacedLights`): one attached to a sprite follows it and goes out with it as a .MAP's does (a torch
  put out, a lamp shot or switched off), shining from its kind's spot moved by its offset; a point light shines from
  its place, flickering, casting shadows and following its sector's light as it says. A .MAPX without a `LITE` chunk
  (made before, or Forge's Test Map copy of a map whose lights weren't made there) gets the sprites' lights as a .MAP.
  The console says which on loading ("N map lights (from its sprites)" / "(the map's own, of N)").
* **Lights kept to their room** where rooms share a place in plan (Build's impossible space, rooms stacked in height, a
  .MAPX's layers drawn over each other: `SectorMap.OverlapsOf`): the rooms are coloured so that no two sharing a place
  have the same colour (`LightRules.RoomColours`, spreading through the two-sided walls), each colour a render layer from
  10 (`DynamicLights.RoomLayer`: the native world's chunks split by colour, the moving sectors', the portal copies', the
  sprites' and voxel models' by their Build sector), and a light's cull and shadow masks leave out the colours of the
  rooms sharing its own room's place (`RoomsOf`: its sprite's sector; a .MAPX point light's as the map gives it, else the
  sector holding it). A light then neither lights nor is shadowed by the other room there, whichever room the camera is
  in; and a .MAPX light on a sprite of a stacked room hidden from the camera is left out as a .MAP's is.
* **Lights and what gives them**: a picture that is itself a light (fullbright, shade -64 or brighter) and a halo take no dynamic light (`light_lit`): its own light right beside it burned it out off centre. Torches' and candles' lights sit at the flame (`DynamicLights.FlameSpot`); `r_lightflicker` scales how much flames' lights flicker, in strength only (a light moving as it flickered had its shadow map redrawn every frame); `r_lightrange` (Options > Enhanced > Lighting, LIGHT RANGE; 1 as placed, to 3) scales every light's reach, the map's and the game's own.
* **A level's sky colour** (`BuildSky.SkyTint`, measured from its panorama, bright parts counting most): grey skies keep the pale moon; a coloured one (E4M1's red) colours the light from the sky, the fog's haze and the ground mist.
* **The sky in the fog** (`r_fogsky`): the volumetric fog's veil goes over the sky too, and the fog's colour lies along the horizon (`build_sky_haze`). The depth fog itself still leaves the sky alone.
* **Atmosphere** (`Render/Atmosphere.cs`; Lighting, Weather and Atmosphere pages; `r_fxall 0|1` or the menu's ALL EFFECTS ON/OFF for before and after). The light from the sky
  (`r_moonlight`; `r_lightning`, also the console's `lightning`): one directional light with shadows, from the moon where `BuildSky.FindMoon` finds one in the panorama
  (a compact bright blob), to 64 Godot units and faded out before that (`BuildShading.LightFunction`), or every room past them was lit. A sector more than two openings from any under the sky gets none at all (`World.NativeSkyFlags` bit 4, doors counted open; `light_sky`): Build's walls and ceilings have no thickness, and what the shadows let through in there is only leaks. By where the camera is (`Atmosphere.SkyAt`: all of it under the sky, 0.8 and 0.6 one and two openings in, none further) the light moves over 1.5 s (`MoonFade`), so it doesn't pop on at a doorway and casts no shadows deep indoors. `r_moonfog`: the same share in the fog, a haze in the dark depth fog (`EnhancedFog.SetMoon`), brighter towards the moon, and more of it in the volumetric fog, where its shadows show. A room standing over open sky casts no
  shadow from it (`World.NativeSkyFlags`, E1M1's rooftops over their yard: the shaders drop those faces under its orthogonal projection, the only one). Thunder is
  Blood's title sample; the lightning's random numbers are the drawing's own. Glowing pixels (`r_glow`, `build_glow`, `palette_bright`): only on what gives light (a torch lit, a light fixture's or a flame's picture, whatever is fullbright; in the level `BuildShading.GlowTile`: lava, fire, fixtures), as by colour alone any bright speck of any picture glowed. A light fixture's sprite or model (glows 2, `build_glow_lamp`) also glows in its pale warm and near white colours (the lantern's glass, `palette_bright` 128) and brighter: 2.2 times its colour at r_glow 0.25 against a flame's 1.35. Within those, Blood's shade table darkens every
  colour alike, so which glow is chosen by their looks (bright oranges and yellows, reds only at their hottest: 29 of 256), with Godot's glow over white only. Embers and smoke (`r_embers`, `r_smoke`): one
  particle system each starting from the nearest 24 fires seen (`decoration_torch`, tile 2101's), the same few at each however many are in view (`fire_share`), the smoke then the embers by render priority (both nodes sit at the camera: by distance they flickered over each other). The view's colour by what the area is made of (`r_areacolor`: the camera sector's surfaces, `AreaOf`; the Hud's first layer; the dark fog's with it). An
  explosion's aftermath (`r_aftermath`; `boom` on the console): a slight round ripple going out in the Hud's first pass, for explosions in a room seen; translucent sprites fade where they meet a wall.
  Under Weather: rings where rain lands (`r_rainripples`, kind `splash`, from a map of the floors open to the sky), falling ash
  (`r_ash`: levels with lava or fire floors, 2 all), dust seen only where a light falls on it (`r_dust`). All are off for regress, motion and compare (`ENHANCED_CVARS`).
* **Ground mist** (`r_groundmist`, `r_groundmistdensity`; `Render/GroundMist.cs`): seen through a portal it is the far room's, at the far room's place, as the level's copy is (E1M1's yard from its rooftops);
  volumetric fog over the open
  sectors' floors, 24 units high. `World.NativeMistGroups` picks them (the old converter's rules): a mist sector is
  open to the sky (parallax ceiling, or a link up to a room open to it: E1M1's yard by the gate under its rooftops'
  link), not a parallax floor or a link down (the rooftops seen through it: their mist hung in the air at the link),
  not under 32 x 32 units, not raised 32 over all its open neighbours (roofs, ledges), not moving; adjacent mist sectors
  at about one height (steps of 32 at most) are one area; an area raised 32 over all the open ground round it (flat roofs
  side by side) has none. Each area becomes a fog volume fixed over its bounds with a map made as the level loads
  (`SetupNative`: an `Rgf` image of 16-unit cells, at most 256 each way, the floor there and 24 units over it); its
  shader puts the density between them, thickest at the bottom, the four nearest cells' layers blended, two drifting
  noises making it patchy, and none where an active portal shows another room (the portals' clip test; a fog shader
  knows no camera: the global `camera_world`, Game). It gives its own light (white 0.9 in a misty level, a blue grey
  0.42 0.45 0.54 elsewhere, by eye) and scatters the dynamic lights a fifth as much as the fog. It needs the
  environment's volumetric fog (on with it, density 0 when the fog is off). A moving floor's mist stays where it was
  at load.
* **Atmospheres by area** (`Render/AreaAtmosphere.cs`): the fog, the ground mist, the weather and the wind are each
  sector's atmosphere's (`Shared.Blood.Map.MapAtmosphere`): a .MAPX's own (its `ATMO` chunk, given by sector in Forge's
  Atmosphere mode), else the game's guesses in the same form (`AtmosphereRules.Guess`: "Level" with the weather by episode
  and the fog from the map's visibility, "Open ground", the ground mist over the open ground `World.NativeMistGroups` picks, as before).
  The distance fog is the camera's sector's atmosphere's (`EnhancedFog.SetArea`: its end and colour, faded into over the
  atmosphere's seconds along the end's logarithm; at once on a new level). An area of its own fog (adjacent sectors of
  one atmosphere, `AreaAtmosphere.FogAreas`) is seen from outside as a bank of it: a fog volume from its floors to its
  ceilings (`GroundMist.AddFog`, the mist's shader `solid`: even, no patches; three times its end's worth gone at its end;
  its colour in linear light), hidden with the camera in it (its fog is the camera's then). The ground mist is each
  area's (`AreaAtmosphere.MistAreas`: adjacent sectors of one atmosphere with mist at about one height; its height,
  density and colour). A fog volume knows no render layers: an area's mist or fog is hidden when a room sharing its place
  is nearer the camera than its own (`GroundMist.CameraRooms`, `LightRules.RoomsByNearness` / `RoomSeen`: the dynamic
  lights' room colours, through the two-sided walls from the camera's sector; a sector sharing no place sees into the
  room beside it), and a light kept to its room glows in no
  fog (it lit the other room's mist). The weather's grid holds each cell's atmosphere, and a table (a column an
  atmosphere) its share of rain, snow and leaves (its amount over the most any has: a layer's particles are the most)
  and its wind now (gusting as much as it says): rain here, snow there, none in a sheltered yard, a strong wind on a
  cliff. Lightning comes with any atmosphere's rain.
* **Weather** (Options > Enhanced > Weather; `r_weather` 1 the level's, 2 rain, 3 snow; `r_weatheramount`, `r_rainamount` (0.25 by default), `r_snowamount` and `r_leavesamount` each kind alone, `r_wind`; `Render/Weather.cs`): GPU
  particles over the open parts of the level, one system a layer (rain 18000 living 1.4 s at 450 units a second; snow 14000, 9 s; leaves 1600,
  12 s, each turning at its own slow rate from what it was given at its start), on the camera, the particles in world space. The particle shader reads a grid of the space round the camera
  (a 64 x 64 x 32 `ImageTexture3D` of 32-unit cells, 2048 x 2048 x 1024 units): solid, open under a roof, open under
  the sky, the sky over a sky ceiling, from the sectors as they are now (`InSectorNow`, `SectorFloorZ` /
  `SectorCeilZ`, `CeilSky`; the sectors over the window found once). A particle starting anywhere but the sky or the
  open air under it, or moving into a solid cell, is gone: rain stops at roofs and walls, and the wind blows it in
  through a window or a door. The grid is made again 4 slices a frame once the camera has gone a quarter of the way to
  its edge, the last kept until then. Each cell's atmosphere's wind (the table's: two slow gusts, as much as it says,
  turning a little) blows what is in it (`weather_wind`, a global, is the camera's place's: what starts upwind of it). Rain is a streak along its velocity (the particle transform's y) turned to the camera, faded
  right by the camera; snow a soft flake; leaves a tumbling leaf (drawn at load). Shaded by the level's sky shade, lit
  by the dynamic lights, fogged. The weather is each place's atmosphere's ("Atmospheres by area"); a map without its
  own gets the game's guess (`AtmosphereRules.GuessWeather`, the old converter's: Blood's second episode snow, the first
  rain and leaves, the rest rain, none without open sky; wind 30 60 for snow, else 20 90). Not yet: copies through
  room-over-room portals, splashes.

## Add-ons

An add-on is a folder of the install like NBlood's `-game_dir`: its episode file (`dw.ini`, the same format as
`blood.ini`), its maps as loose `.MAP` files, art files that replace the base game's of the same name whole
(`tiles007.art`: tiles 1792-2047), music (`bloodNN.ogg` CD tracks, `.mid`, `SC-55/`) and cutscenes (`.ogv` + `.srt`).
`pipeline/addons.py` finds them (id: the `.ini`'s name, `dw`); `install_addon.bat` runs:

* `pipeline/export_addon.py`: the add-on's data folder `game/blood3d/addons/<id>` with only what it brings: the images
  of the tiles its art replaces, the tile table with its art in place (`data/rt_tiles.txt`), its `.ini` as
  `data/blood.ini`, `data/addon.txt` (title, maps), its music and cutscenes. Refreshed Supply's English strings
  (`data/loc_english.txt` from `BloodEX.kpf`: `$dw_ep1` = "Cold Reception") go in the base data folder.
* `pipeline/export_maps.py`: every level's .MAP, the add-on's into `game/blood3d/addons/<id>/maps`.

The game (`Data/Addons`): loading a level activates the add-on whose `.ini` lists it (or none), and
`BloodData.Find` returns the active add-on's file before the base game's for the tile table, tile images,
`blood.ini` (episodes, music), music and cutscenes; `Addons.BuildMapPath` finds a level's .MAP. The main menu's ADD-ONS
lists the exported add-ons; one picked, its episodes are the game's (NEW GAME is Blood's own). `compare.py` and
`bugs.py nblood` run NBlood with the add-on's folder and `.ini` (`addons.nblood_map_args`). Refreshed Supply's per-map
fixes (`defs/levelHacks/<map>.txt` in `BloodEX.kpf` and an add-on's `.kpf`: E1M4, E2M5, Cryptic Passage) go into the
map's `<NAME>.patch` when their first line, the CRC-32 Blood stores at the end of the map file, is this map's
(`addons.level_patch`; CP02's is for another version), and the game applies them as it reads the map
(`BloodMapFile.ApplyPatch`: `sector N ceilingz`, a sprite's voxel model turn, offset and no-model flag). A line that
takes away Blood's own content is left out (`addons.SKIP_PATCH`: E1M4's hanging Duke Nukem, sprite 952, which
Refreshed Supply draws flat). A sector's slide and turn markers are the marker sprites it owns, not what its xsector
stores (`BloodMapFile.PropagateMarkers`, NBlood's `PropagateMarkerReferences` at load): many maps store stale ones
(MARROW's MRW2M3: slide 1112's marker0 named another sector's warp marker; 14 maps in all, Blood's E2M3 and E6M5
among them).

**Mods made for NBlood** (a folder run there with `-game_dir`; Eviction is the one it was made with) install the same
way: `maptool extract <blood dir> <out dir> --mod <folder>` (any number of them, anywhere on disk; a mod folder inside
the Blood folder is found by itself), ChosenLoader's **Add a Mod Folder...** or its `--mod=<folder>`. Beside an
add-on's files, the extraction (`Extractor.ModFiles`) takes what a mod's resource scripts name: its `.RFS` files
(`Files/Rfs.cs`: `resource "EVICTION\SEQ\X.SEQ" as 6700;`, `data "ZDIE1.SFX" as 12146: ...;`, what BARFC would pack
into the mod's RFFs) give its sequences and sounds, added to the base game's in the add-on's own `data/rt_seq.txt` and
`rt_sfx.txt` (the sounds as WAVs in the add-on's `sound/blood/`), its palette lookups from 15 up (`gfx/palookup.bin`),
its voxels (`models/voxels/mod_v<id>.md3`), its own SURFACE.DAT, SHADE.DAT and VOXEL.DAT, and its custom dude
descriptors (`data/cdud/cdudNNNN.cdu`). Its loose maps not named in the `.ini` are listed too. Activating the add-on
reloads the tables that can differ (`Addons.Activate`: tiles, palette lookups, voxels, sequences, QAVs, sounds). A
sequence newer than version 3.0 has tile numbers above 4095 and NoOne's frame fields (the sound picked from a range,
the floor's step sound), kept in `rt_seq.txt` (`SeqData`). A mod's maps are usually modern maps: see "Modern
features" below. The add-on's `data/addon.txt` names the mod's folder (`source`): the loader lists the mod again from
it, and the pipeline starts NBlood with it (`pipeline/addons.py`, `mod_folders`: `bugs.py nblood`, `compare.py` and
`playat.bat nblood` work on a mod's levels).

## Running

    godot\play.bat [map] [options]       (builds the C#, starts the game; no map: the main menu)

Without a map it opens Blood's title screen (no level loaded until New Game picks one); with a map
(`godot\play.bat e1m1`, `--map=`) it starts that level directly. The level loads again when its .MAP changes on
disk (hot reload: save in the editor, see it in the running game).

A test run (Godot's `--position`, as the pipeline's launchers give it, or `--noinput`) never takes the focus or the
mouse from whoever works while it runs (`src/Client/Mouse.cs`): the window opens without focus (project.godot's
`no_focus`; a played game takes it once it starts), and the mouse isn't captured (Godot's capture on Windows clips and
moves the system's cursor even without focus); the game still plays as captured.

    godot\playat.bat [nblood|both] [nomonsters] <spot>       (from a named test spot in godot\spots.txt)
    godot\playat.bat [nblood|both] [nomonsters] <map> <x> <y> <z> [yaw] [pitch] [sector]   (from anywhere; no arguments: the spots; nomonsters: no enemies, b3d_monsters untouched)

Chosen by default (play.bat, then `warp`), `nblood` for NBlood, `both` for both to compare. A spot line is
`name map x y z yaw pitch sector [console commands]`: Quake units, z the feet; sector is the Build sector where rooms
share space (-1: found from the position); the console commands run in Chosen after the warp (`send <channel> on`).
NBlood (patched: `-b3dwarp x,y,z,yaw,pitch,sector` puts the player there once the level starts, swimming in an
underwater sector) runs from `tools\nblood_play` with its own `nblood.cfg` (windowed 1920x1080 at first; change it
in NBlood's menu), at Chosen's `b3d_skill`.

Controls, like the DarkPlaces version's: mouse look, WASD (Shift toggles run, cvar `cl_alwaysrun`), Space jump, C/Ctrl crouch,
E use, left/right mouse fire/alt fire, 1-0 weapons, mouse wheel next/previous weapon, Enter use item, [ ] item,
Q/X and M K B J inventory keys, Backspace turn around, = (+ on Nordic keyboards) and - the view size (HUD). Escape
opens the menu, F12 saves a screenshot, F7/F8 toggle dynamic lights/shadows, F9 steps ambient occlusion (off, SSAO, GTAO), the key left of 1 (backtick, § on Nordic keyboards) or F1 opens the console
(DarkPlaces' look: half the screen over Blood's logo, `gfx/conback.png`). The game pauses while the console is open
(the mouse stays captured); `pause` pauses it too.

Menus (`Client/Menu.cs`), drawn like Blood's in its tile fonts: the game starts on Blood's title screen with no level
loaded (Escape stays on it). New Game picks an episode (those whose first level's map is exported) and a difficulty
(`b3d_skill`); Options set the HUD, crosshair, bob, always run, fullscreen (`vid_fullscreen`), vsync (`vid_vsync`),
field of view, brightness (`brightness`, a gamma curve over the whole picture), sector contrast
(`b3d_shadecontrast`: scales Blood's shades, 1 as Blood), dynamic lights (`r_dynamiclights`) and shadows (`r_shadows`),
voxels, sound and music volume, and the mouse.

Shading: Blood darkens the displayed colour by its shade (linear, 64 is black); Godot's 3D shading is in linear light,
so the shaders apply the factor in display space (`BuildShading.ShadeFunction`). Distance darkening is the map's
visibility / 1048576 shades per Quake unit and sector rate, measured against NBlood's Polymost; `r_ambientlight`
divides it like NBlood's Visibility option. Each surface takes its Build shade and its sector's visibility rate from
the map (Render.Native.WorldMesher's vertex colours), its light effect's shade from the game each frame (`b3d_sector`).
Palette 1 (Blood's SATURATE) shades toward white instead of black (`build_color_fog`: a blend toward white in the
palette's colours, 0.867 of the way at shade 63, fitted to SATURATE.PLU), the sprites of sectors whose floor has it
too (`viewApplyFloorPal`; `BuildShading.FogFloor`), and the sky where most sky ceilings have it (`sky_fog`): Death
Wish's mist. The other palettes are Build's palookups in the shader (`Render/Native/BuildPalette.cs`). The whitened
share is taken off ambient occlusion (`fog_roughness`). The weapon takes its sector's floor shade with its light effect
(Blood's view.cpp). Distance shading goes by depth along the view, as Build's and Polymost's.

Music (`Client/MusicPlayer.cs`): Refreshed Supply's CD tracks (`sound/cdtracks/trackNNN.ogg`), each level's track and
the menu's from Blood's `blood.ini` (copied into the data folder by `pipeline/export.py`); `bgmvolume` sets the volume.

Sounds (`Client/SoundSystem.cs`): `World.Sound3D` as sfxPlay3DSound, a sound on an object's channel replacing the one
playing there, unless its flags keep that: 2 the same sound, 4 any (1: on anyone's channel). Sounds played every frame
rely on it (standing in lava: the sizzle, 352, and the burn's pain sound would restart each frame).
Loudness is Blood's on the audiolib's 0..255: Calc3DValues' relVol * 80 / max(3/8 distance, 64) (relVol 80 is 100 up
close), sndStartSample's relVol * 80 for a sound heard everywhere, at most 255; `volume` 1 is NBlood's FXVolume 255. The
headroom matters where many sounds start at once: E4M1's thunder is 84 sound sprites on channel 100 playing
thunder2.wav together (a sound at relVol / 80 summed past ten times full scale and crackled). A limiter on the master bus
catches what still goes past full scale, where the audiolib clamps.

The mix is Blood's audiolib's, made here (one `AudioStreamGenerator` on the `Sfx` bus, about 60 ms filled ahead each
frame), not Godot's players: a sound in the world is two voices, an ear each, as sfxPlay3DSound starts them with
gStereo. Each ear is 43 Build units to the player's side (kEarDist), its voice its own volume (Vol3d: the distance's
volume by 1 - (1 - cos) / 8 of the angle from the side the ear hears best, 15 degrees off ahead: a mild 2.5 dB at most
between the ears), started that many samples in as its ear is nearer the sound (mulscale16r(approxDist, 4114 at 11025 Hz,
else 8228): about half a millisecond for a sound to one side, the time between the ears that places it) and its own
Doppler pitch (sfxUpdate3DSounds each game frame: the ear's and the source's speeds along the line between them, Build
units a game frame, against 5853; a jump through a link or a teleport is no speed). A sound heard everywhere, the
title's thunder and the ambient sprites are one voice on both sides. Each voice plays its sample as MV_MixMono does,
`source[position >> 16]`, each sample held (no interpolation: the steps are the bright edge Blood sounds with;
interpolated, about 8 dB less above 4 kHz, dull), its pan volumes eased to their goals (MV_VolumeSmoothFactor);
`snd_smooth 1` (Options > Sound > Sound Quality: Smooth) interpolates between the samples. (Godot's 3D players had also
filtered the high end off every sound quieter than full: `attenuation_filter_db` -24 above 5 kHz as far as the volume
is below full, Blood's distance falloff being that volume.) `sndrec <sound> <prefix>` records a sound through the mixer
alone (the level's other sounds silent) off the `Sfx` bus: crisp and smooth heard everywhere, then 256 units to the
player's right (louder and earlier on the right), into `<prefix>_crisp.wav`, `_smooth.wav`, `_right.wav` (run with
`--audio-driver Dummy`: nothing is heard).

Command line options (after `--`): `--map=<name>`, `--play=<bug id>` (a recording's inputs
drive the player), `--replay=<bug id>` (the recorded path as a camera), `--speed=N`, `--frame=N` (where a replay
stops), `--cam=x,y,z,yaw,pitch`, `--shot=<file.png>` (save a screenshot where the replay stops, or once the level
is up, and quit; `--wait=<seconds>` waits that long first), `--report=<text>` (with `--play`: file a bug report
where the replay stops, and quit), `--atend="cmd; cmd"` (with `--play`: console commands where the replay stops),
`--exec="cmd; cmd"`, and the checks below.

Console: `reportbug`, `gotobug`, `map`, `reload`, `restart`, `play`, `replay`, `stop`, `fly`, `cam`, `where`,
`screenshot`, `quit`, `check`, `inputcheck`, `sizeup`/`sizedown`, `toggleconsole`, `togglemenu`, `pause`, the cheats of the DarkPlaces version
(`give [all|weapons|ammo|armor|keys|items]`, `god`, `mpkfa`, `testkit`, `noclip`/`cousteau`, `idaho`/`lara`, `griswold`,
`montana`, `kill`/`kevorkian`, `nomonsters`, `trigger <channel>`, `warp`, `sprinfo`, `sprnear`), the debugging commands `sprites` (the sprites near the camera), `fx` (light-effect sectors), `node` (show or hide
a level node), `debugdraw` (Godot's wireframe/overdraw views), `perf`, `hitscan [up] [fire]`, `portals`, `bsec`,
`menu_page`, `lights`, and cvars such as `volume`, `bgmvolume`,
`fov`, `sensitivity`, `m_invert`, `cl_alwaysrun`, `b3d_hudsize` (0-3), `b3d_crosshair`, `b3d_levelstats`, `b3d_vbob`,
`b3d_hbob`, `b3d_slopelook`, `b3d_monsters 0` (levels start without monsters; `nomonsters` clears the running level),
`r_horizcenter` (0, as Blood and NBlood's default: the horizon 10 of Blood's 200 rows above the view's middle, which
NBlood's polymost shows as a level look tipped atan(10 / 160) down; here the camera's frustum is shifted by as much,
the camera still turning to look up and down as before; the comparison views take it the same way, `--views`; 1: in
the middle). `fov` is across at 4:3 as Blood's 90, wider screens seeing more to the
sides, as NBlood. Names are case-insensitive and DarkPlaces' `cmd` prefix works (`cmd mpkfa`).

## Bug reports

`reportbug <what is wrong>` works as in the DarkPlaces version: it writes `<id>.txt` (where you are, what you aim
at), `<id>.png` (the view without the console) and `<id>.rec` (the level's input recording from its start, in the
DarkPlaces recordings' format, cheats included) to `game/blood3d/data/bugs`. `python pipeline/bugs.py import` files
them under `bugs/<id>/` like the DarkPlaces reports (report.txt says `engine: godot`, the map is a `.map`).
`gotobug <id>` goes to any report's spot, DarkPlaces or Godot; `bugs.py repro <id>` and `bugs.py replay <id> [frame]`
run Godot reports in Godot (add `godot` to run a DarkPlaces report there); `bugs.py repro <id> dp` shows a Godot
report's spot in DarkPlaces (`repro-dp.jpg`; DarkPlaces' warp keeps the pitch at 0). A Godot recording replays exactly:
`--play=<id>` reproduces the session, and `check`/`inputcheck` on it match 100%.

What `bugs/<id>/` keeps in git: `report.txt`, `status.md`, `screenshot.jpg`, `recording.rec`, NBlood's view
(`nblood.jpg`) and the accepted regression `baseline.jpg`. The pictures the tools make again whenever they're needed
stay local (`.gitignore`): `compare.jpg` (the report beside NBlood), `repro.jpg`, `replay.jpg`, `regress.jpg` and the
regression `sheet.jpg`s. `tools/compare/` (the map comparisons, not in git) holds only each map's latest run.

## Comparing with NBlood

`python pipeline/bugs.py nblood <id>` shows a report's spot in NBlood, the reference: NBlood built from
`third_party/nblood` with `pipeline/nblood_b3dview.patch` (`build_nblood.bat`; `-b3dview x,y,z,yaw,pitch` draws the view
from a Chosen camera, `-b3dshot seconds` saves a screenshot and quits, no intros). It writes `bugs/<id>/nblood.jpg`
and `compare.jpg` (the report beside NBlood). NBlood starts the level fresh, so changes made in the session are not
there; `nblood <id> <seconds> <s,s>` turns those Build sectors on at the start (`-b3dsend`, as walking in or a switch
would) to see movers mid-motion (E2M6's ice floe: `nblood <id> 3 0`); `c<n>` toggles channel n instead, as a switch
sending it would (E1M2's train: `nblood <id> 16 c111`).

`python pipeline/compare.py <MAP>` compares a whole level with NBlood where Build's tricks are hard to draw: it picks
cameras at rooms sharing space (from each), rooms stacked so their heights overlap (from both rooms), room-over-room
links (from each side) and moving sectors (from the rooms beside them), standing in a sector at eye height with a
clear line in the plan to what they look at. NBlood draws them all in one run (`-b3dviews <file>`: a camera per line,
a screenshot each, no weapon or status bar, no screen palette: the player sprite stays at the start, in E6M5's
water, which tinted every view, `-nodudes`) and Godot too (`--views=<file> --viewshots=<dir>`, monsters
off, no HUD; the settings are restored afterwards). Each line ends with the camera's Build sector, which both engines
keep where the camera lies in it (rooms sharing space or stacked: the position alone doesn't tell) and otherwise look
around from it: at the view's moment a moving sector can stand there (E1M2's train car over platform 255), and without
it NBlood's search from nowhere can miss (E3M8's shaft 511). Both take every view at the same game
time (`compare.py`'s `VIEW_SECONDS`, 3): NBlood runs the level that long (`-b3dshot`) and holds its game logic from the
first view on, Godot runs its world to that game frame (`--viewtime=<seconds>`) and pauses it, so timed movers stand
in the same place (E1M2's train: before, each engine's clock ran on as its views were saved, and the two drifted
apart). Blood's look is converted as NBlood's polymost turns the view (its horizon line is 90 of 200,
so a level look tips down 3.6 degrees); steeper than 25 degrees polymost also stretches the view, so the cameras
stay within that. Each pair gets a difference score with brightness normalised, and `tools/compare/<map>/index.html`
shows them worst first (NBlood | Godot | where they differ). Animated fire, sky clouds and Godot's dynamic lights
score too (flickering and pulsing sector light effects are frozen at their base shades, steady ones stay as the game
shows them, and dynamic lights are off, in both engines for these runs), so the list is a place to look, not a verdict.
A map compared before keeps its cameras (`views.json`, from the map's sectors: `compare.Level`) and NBlood's shots of
them; `--newviews` picks them again. `python pipeline/compare_batch.py <map|add-on id>... [--max N]` runs it for many
maps (an add-on's id: all its maps) and lists every map's mean now and in the run before, its worst views and those
that got worse in `tools/compare/batch.txt`, the map that got worse most first; it reads the user's settings once and
puts them back at the end (a run that times out skips compare.py's own restore, and the next map took the test
settings for the user's). `--skip-nblood` reuses NBlood's screenshots, `--only
stack,link,mover` (view kinds, not ids: it clears the other views' Godot shots) and `--max N` narrow the views. Debugging a view: `sprat`, `bsec`, `portals`, `stacks` and `send` (below).

## Regression categories (bugs/regression)

Tricky fixed bugs are tagged by the code they exercise (`python pipeline/bugs.py tag <id> <category>`; a
`Regression:` line in their status.md, `bugs.py categories` lists the categories and what each covers). After
changing that code, `python pipeline/bugs.py regress <category>` (or `all`, or bug ids) takes each tagged bug's view in one Godot
run (`--bugs=<file> --bugshots=<dir>`: per bug a restarted level, `gotobug`, the bug's optional setup commands such as
`send s136 on` and a wait, then a screenshot; monsters off, no HUD, light effects frozen, settings restored in a
second run; the game held for the shot and drawn at its last game frame, `GameClock.AtFrame`: frames drawn slowly, a
window in the background, ran more game time, and a player put back over E1M1's opened coffin fell further into it,
so the same view scored 0.14 one run and 0.88 the next) and scores it against the bug's accepted `baseline.jpg`. `bugs/regression/<category>/README.md` and
`sheet.jpg` (report | NBlood | baseline | now) show the result, CHANGED where the view moved away from the baseline;
`bugs.py accept <id>|<category>` makes a checked new look the baseline. Bugs that need play (collision, performance,
triggered events) carry a `Regression check:` line saying what to try at the spot.

## Checking against the recordings (regression)

    Godot_v4.7.2-stable_mono_win64_console.exe --headless --path godot -- --check="<bug id> [probe frames] [dump=f,f] [rngtrace]"
    Godot_v4.7.2-stable_mono_win64_console.exe --headless --path godot -- --inputcheck=<bug id>

* **check** (strict): each frame the player is put in the recorded state, given the recorded input, the C# world
  runs one frame, and the result is compared with the next recorded state. Reports the share of matching frames
  and the first mismatches. Probe frames print what moved the player. `dump=` writes the world's sprites at those
  frames to `tools/dumps/<id>-<frame>-cs.txt`; `rngtrace` prints every random draw with its callers.
* **inputcheck**: the recorded inputs alone drive the player from the level start; reports when the player first
  drifts more than a unit (`inputcheck <id> from-to` in the console prints the frames in between) and the slowest game
  frame (`inputcheck <id> slow` lists every game frame over 8 ms), and where the player ends (`end:`). A recording
  made before the game's collision or sector rules changed drifts early by nature (recordings from the converted
  levels: their brushes' box traces slid along walls differently, and the AI's probes were theirs).
* **walk tests** (`pipeline/walktest.py`): a made-up recording (a `warp`, then walking segments) run through
  inputcheck with Build's collision, printing where the player ends (`--trace a-b`: each frame):
  for collision reports that need play (does the player get through, stay on the floor, stop at the wall).
* `r_hitchlog <ms>` (cvar) prints every frame slower than that while playing: the time per section (game, movers,
  portals, sprites, HUD, sound; inside the game tick `t.*` and the actor passes `a.*`), counters (`Perf.Add`: time
  and calls of GetZRange, ClipMove, MoveThing, and `mirror`: portal copies of moving meshes rebuilt), disk loads and
  garbage collections in it. `r_hitchlog 0.01` logs
  every frame, e.g. over a replay at normal speed (`--play=<id> --speed=1`).
* Where the GPU's time goes: Godot's own `--gpu-profile` (before `--`) prints each pass's time every second or so
  (the depth pre-pass, "Process Pre Opaque Compositor Effects": the ambient occlusion, the opaque pass, fog volumes,
  depth of field: the fog's blur, glow). Run it at the resolution played at (`--resolution 3440x1440`, off the
  desktop as any test run): the passes are mostly per pixel. `r_stackwalk 0` leaves the stacked rooms' walk out, to
  see what it costs in the depth pre-pass.
* `r_perfoverlay` (F11 cycles it; Options > Video > Performance): 1 shows the frame rate, the frame time (average,
  worst, 1% low), the game's CPU time (Blood ticks, and in all), the main view's render time on the CPU and GPU, draw
  calls and primitives; 2 adds a graph of the last 300 frames (bars as tall as the frame time, the game's CPU time
  in them, the GPU time as a line, the refresh time and 30 fps as dashed lines): a lone tall bar is a hitch, uneven
  bars jitter.
* A glitch of one drawn frame: `--play=<id> --frame=<n> --atend="timescale 0.01; shotburst 100 <dir>/b quit"` saves
  every drawn frame after the replay stops (`b_000.png`..., each printed as `burst <n> frame <game frame> lerp <blend> sector <the walk's camera sector> player <the player's
  sector>`: a frame walked from the wrong room shows as a lone sector).
  `lerphold <0..1>` instead stops the game and draws everything that far between the last two game frames (`off`
  resumes): only for what a blend gets wrong, since every frame drawn after the first sees the caches already refreshed
  (E1M1's napalm ball over the sky link, wrong on the first draw after its crossing only, needed the burst).
* Profiling: `dotnet tool install -g dotnet-trace`, then `dotnet-trace ps` and `dotnet-trace collect -p <pid>
  --duration 00:00:00:25 --format Speedscope` while the game runs (samples are wall-clock time: a frame waiting for
  vsync or the render thread shows up in whatever Godot call it waits in).

HDR output (Options > Video > HDR; `Render/HdrOutput.cs`): `r_hdr` asks Godot for HDR on the window, live, and it falls back to
SDR by itself. Windows needs Direct3D 12: Godot's Vulkan driver has no HDR output here, so `project.godot` picks `d3d12`. Options >
Video > Renderer (`r_driver`, `Render/RenderDriver.cs`) picks Vulkan instead for its frame rate, with no HDR: Godot reads the driver
before the game runs, so the choice is `user://driver.cfg`, which it reads over `project.godot`, and needs a restart. `r_hdrwhite` is the world's ordinary white in nits (paper white: at the screen's
peak everything would be blinding); what is brighter (dynamic lights, fog glow, and `r_hdrhighlight` times white: fullbright
sprites, flame tile 2101 whatever its shade, a weapon's fullbright animation layers) goes up to `r_hdrpeak`, whose slider ends at the peak the system reports for the screen. `r_hdrui` is the UI's: its items (status bar, messages, menus, console,
overlay, movies; `HdrOutput.Ui`) are modulated by the ratio; the weapon and the tints are the world's. With HDR the root's 2D
is linear: a canvas shader reading the screen sees linear values (`hdr_linear_screen`; `ViewPalette`'s matrices are fitted in
sRGB), and a viewport's picture is floats (`HdrOutput.Picture` for any screenshot). Test runs (`--shot`, `--views`, `--bugs`,
`--scripted`) stay in SDR whatever `r_hdr` says; `hdr` prints the state (`hdr on`: this test run too).

The rendering steps between game frames (`pipeline/bugs.py motion`): the regression shots see a stopped frame, and a replay
checks the game frames; neither sees what is drawn between two of them. Each case in `bugs/regression/motion/cases.txt` is
brought to a moment things move (a report's recording, or commands at its spot and some seconds) and held at several blends
(`motionshots`, on `_clock.Hold`). At each, the view as drawn is compared with the same view with every moving sector built
exactly for that blend (`r_meshblend 0`): a colour that isn't around the pixel in the other picture is a gap (BROKEN; an edge
landing a pixel over doesn't count). The drawn view is also compared with its accepted baseline (CHANGED: sprites and the rest
of what blends), and a case whose first and last blends match says it no longer moves there. `--selftest` draws with
`r_meshblend 2`, wrong on purpose, and must show the cases whose floors don't move rigidly BROKEN. It plays with water portals on and writes `sheet.jpg` (drawn, exact,
where they differ).

Performance notes (native world, DWE3M1 and E2M1 at 3440x1440): a sector moving rigidly is built once a game frame, not
each drawn frame: every corner carries how far it moved since the last game frame (`WorldMesher.Blend`, CUSTOM1.w, a flat's
in CUSTOM2.xy and its picture's in CUSTOM1.xy) and the vertex shader blends it by `tick_lerp` as it blends the planes
(`WorldShader.VertexBlend`). A sector moving otherwise (`MovesRigidly`: the floor round a moving hole, beside a door) is
still built each drawn frame at the blend (blended, its floor opened long thin slits). One the camera's walk doesn't reach
isn't built at all until it does. The builds of a drawn frame are jobs (`NativeWorld._jobs`, `r_meshjobs`): each on a worker
with its own mesher (building only reads the level), the slowest last time first, packed there into the bytes of Godot's
surface layout (`WorldMesher.Prepare`); on the main thread (`Commit`) a mesh whose surfaces keep their vertex counts gets
them written into the buffers it has (`ArrayMesh.SurfaceUpdate*Region`, `r_meshupdate`), else is built anew. The packing
(positions; normals octahedral, each with the tangent Godot makes up for a mesh without; colour bytes; float UVs and
custom channels; offsets asked of Godot once a surface) is checked byte for byte against Godot's own on the first 64 full
uploads and turned off at the first difference; a normal unchanged since the last update isn't encoded again. DWE2M1
(51 moving sectors, 30 built a drawn frame): 6.0 ms of moving meshes a frame to 1.7 (new surfaces were two thirds of it);
`movingmesh` lists each one's state and last build's times, the hitch log `mv.build` and `mv.commit`. Dynamic lights: a
map light too far from the eye, directly and through any link it reaches, to be among the nearest (`_farCull`, from the
last frame's order) is rejected on its distance alone; each keeps its light-effect sector while it stays (not before the
first game frame, when none are known yet: a light switched off stayed on), and the pool's lights compare against what they
were last given rather than ask Godot. A water portal's copies are only the meshes holding sectors joined to the room under
it (`PortalRenderer.WaterRoom`: all of them, not stopping at other links' rooms, or the sky shows through a hole). The
regression shots have water portals off: check that last one with them on. A floor or ceiling link's sprite copies are
only those seen through its face (`SpriteRenderer.SeenThrough`: a box round the sprite, its picture's size at least,
projected from the eye onto the link's plane must meet the face; through water also within 1024 units): a link's copy
region can be most of the level (Eviction's floor link 5, a 128 by 448 hole: a thousand sprites copied a frame, 7.8 ms,
report 20261117-111050). The procedural fire's texture is filled as bytes and handed over at once (a `SetPixel` per
texel was 2 ms every 25 ms). Runs from the Godot editor's binary (every test run, `run.bat`, the editor) load Godot's
Debug `GodotSharp.dll`, built with the JIT optimiser off: Godot's own types there (`Vector2` arithmetic, `Color.R8`,
every call into the engine) cost several times what they do in the exported build, so a hot loop is best kept to plain
floats and arrays; the game's own assemblies are optimised in both (`<Optimize>` in the .csproj files).
Shadow maps: Godot redraws a shadowed light's map whenever a caster in its reach changes (its mesh or transform) or has
an animated material, a shader that reads `TIME` and can `discard`: no world, sprite or voxel shader reads `TIME` (the
water's clock is the global `water_time`, `WaterFx`). A moving sector whose moves change no shadow (every wall that moved
flush: two-sided, nothing drawn on it, the same level unsloped floor and ceiling either side; its planes still; its moving
neighbours across those walls the same) casts from a shadows-only copy (`NativeWorld.UpdateShadowStill`, `r_stillshadows`,
`movingmesh` says `shadow still`), its drawn mesh none; the copies are built together whenever that set changes, so their
outlines meet. Eviction's turning sector 420 and the room round it made the street's eight shadowed lights redraw every
frame: 8700 draw calls to 3600 (report 20261117-113226).

A sprite keeps its cstat as the map stores it (`World.Native.SpawnNativeSprite`): every sprite of Blood's own maps
is centred on its z (cstat 128), a mod's map made with xmapedit can have one standing on its z instead, and a face or
wall sprite is then drawn with its bottom there (Eviction's fan, drawn half its height low in its vent box: report
20261115-161220); the game's own sprite sizes (Blood's GetSpriteExtents) take the centre either way, as NBlood's do.

Performance notes (E1M2's train station fight, 20261020-101750): every sprite is placed every frame, so the sprite
renderer sends Godot only what changed (`SpriteRenderer.Sent`; a getter such as `MaterialOverride` costs a wrapper,
every setter a marshal), face sprites turn to the camera in the shader and flat sprites take their depth lifted off their surface there
(only the depth: moved, a fake ceiling sprite left a slit beside the next ceiling; their nodes keep still while the
camera moves), and each sprite's light-effect sector is cached. As the level loads, every picture its sprites may show (their views round, animation frames, a lit torch's next tile) is read in parallel (`SpriteRenderer.Preload`, `TileTextures.Preload`; DWE3M1's replay loaded 57 textures over its first 29 frames, none now). Not their materials: made in another order, coplanar sprites drew differently (DWE3M1's ceiling lamp lost to the ceiling). Godot arrays are
freed at once (`GetFrustum`, mesh arrays): left to the finalizer they contend with the main thread. Collision: a
trace skips the bodies its swept box misses (`MayTouch`) and the floor and ceiling searches try 256 units before 8192
(`TraceZ`). Together: the game tick in that fight from 12-17 ms to about 5.5 ms, the sprite pass from 2.4 to 0.6
ms a frame, and no finalizer stalls; replays still match exactly.

## What's there

### Levels from Blood's .MAP (native)

The game and its drawing run on Build's own data: a level's `.map` in `game/blood3d/maps/<NAME>.map` (an add-on's in
`game/blood3d/addons/<id>/maps/`; `pipeline/export_maps.py` writes them from BLOOD.RFF, with `<NAME>.patch`, the
`levelHacks` lines where Refreshed Supply's PATCH01.RFF fixes the map). `Map/LevelLoader.cs` reads it and loads it
again when it changes on disk.

* **The map** (`Sim/Build/BloodMapFile.cs`): Blood's .MAP read as NBlood's dbLoadMap does (version 7 decryption, the
  CRC, the x-objects' bit fields from `XStructs.cs`, generated), markers propagated, the patch applied. `bmapcheck`
  compares it with the Python reader.
* **The board** (`Sim/Build/Board.cs`): the sectors and walls as the game changes them, with what Build keeps beside
  them (the base positions and planes TranslateSector and ZTranslateSector start from, the last game frame's for the
  drawing's blend), each sector's wall loops and neighbours, versions of its shape and planes, and Build's helpers
  (inside, getzsofslope, dragpoint, updatesector, rotatepoint). `bsec <n>` / `bsec near [r]` prints sectors and their
  x-state.
* **The game on it** (`Sim/World.Native*.cs`): the level spawned from the map's sectors, walls and sprites
  (`LoadNative`); NBlood's trInit, the busy procedures that move sectors (VCrush, VSprite, VDoor, HDoor, RDoor,
  StepRotate, Path; TranslateSector with vanilla's reversed-sprite pivot kept), ProcessMotion, AlignSlopes and the
  panning (`World.NativeSectors.cs`); warpInit's links and CheckLink (`World.NativeLinks.cs`); hitscan, vectorscan
  (masked texels from the tiles' alpha), cansee, the explosion's sectors and the use key's scan
  (`World.NativeTrace.cs`), all ported from NBlood in its ENGINE_19960925 mode. The game code calls them directly
  (`World.Engine.cs`, `World.SectorTrack.cs`, the AI's probes, `World.Physics.cs`); the box traces of `CollisionWorld`
  only meet the sprites' bodies. Drawing never changes the game: the view code asks a sprite's sector with
  `PeekSpriteBSector`. A sprite's sector follows it by updatesectorz (`World.SectorTrack.UpdateSector`), but one still
  within its sector's outline and past that sector's linked floor or ceiling stays in it, as Blood keeps it, until
  CheckLink moves it across (a room right under the link, as Forge's Add Floor makes, took it first by height: water
  never entered; E1M1's falling things under sector 90 now cross its link as in NBlood).
* **The world mesh** (`Render/Native/WorldMesher.cs`, `NativeWorld.cs`, `WorldShader.cs`): the sectors' floors,
  ceilings and walls in squares of 8192 Build units, one surface per (tile, kind: opaque, masked, translucent, sky,
  portal face, water). A vertex names the planes it lies on (CUSTOM0: 1 + 2 sector + ceiling, clamped between two),
  and a wall the plane its picture is pegged to: the shader takes the heights from `b3d_planes` (this game frame's and
  the last's, blended by `tick_lerp`), so a moving floor or ceiling needs no rebuild. A sector whose walls move
  (sliding, turning, dragpoint) gets a mesh of its own, built again each drawn frame while it moves. The light effects'
  shades and the panning come from `b3d_sector` and `b3d_wallpan` (written from the game each frame), the palettes from
  Build's palookups in the shader (`BuildPalette.cs`, `gfx/palookup.bin` from `pipeline/export.py`).
* **The sky** (`Render/Native/NativeSky.cs`): the panorama made from the sky tiles as Blood lays them (its tile and the
  next 15, or one wide tile), registered with `BuildSky` (`NativeCode`: seen from below only, on the sectors' planes).
* **Portals** (`PortalRenderer.SetupNative`): a portal on each link side Build draws the far room through (a floor of
  tile 504 and the ceiling under it, forced to 504 as InitMirrors does); its face is the sector's floor or ceiling as
  built (it follows the sector when it moves), the copies are instances of the native world's meshes marked by the
  instance uniform `portal_copy`, the copy's stacked-rooms walk starting in the sector just beyond the face. A copied
  portal face lying on the plane of the portal it is seen through is left out (a link with no height between its
  rooms, E2M7's 221-223). Links don't nest: another link's face seen in a copy is black, but for a link in place (its
  offset nothing: rooms really over each other, as Forge's Add Floor makes them, `NativeWorld.IsPortalInPlace`, the
  face's material `in_place`) it is left out, the room beyond it being in the copy where it really is; the sectors such
  links join (a chain of them too) don't share the ground for the stacked-rooms walk (`StackedRooms.Setup`), or from
  the middle of three floors the walk hid the top one (report 20261105-143410). With see-through water (`r_waterportals`) the water links are portals too and their flats
  the `Water` kind, blended at `r_wateralpha`.
* **Wall portals** (`PortalRenderer.WallPortals`, found as NBlood's InitMirrors finds them): a wall stack (NBlood's
  type 501 "Wall Stack": a red wall, overpicnum 504 on its one-way side, an x-wall whose data another such wall has;
  Forge's Make Portal on two walls) is a window showing the room behind the other wall, the copy shifted by that wall's
  end less this wall's start (DoWallMirrors: not turned or scaled); a white wall of Blood's mirror picture 504 a mirror,
  its copy its own room reflected in the wall's plane (a node of negative scale). A wall portal's row in `portal_data`
  has w 3 (stack) or 4 (mirror) and a frame (`FrameColumn`, the slots' `portal_frame_N`: a point of the wall and the
  way along it): in it (along, out into the room, height) the plane is level, so the clip test, the face mask and the
  triangles are a floor portal's (`portal_local`); the face is the opening (a stack: between the higher floor and the
  lower ceiling of its two sectors at each end, again when they move; a mirror: floor to ceiling), drawn in the
  `WallPortal` kind (`WorldMesher.IsWallPortal`): open where its portal is drawn and the eye looks through, else black.
  The sector walk goes on into a stack's far room (`SectorVisibility.WallLinks`); a mirror's copy isn't culled by it.
  The frame comes from the wall's own winding (Build keeps a sector's inside on the left of its walls): a point probed a
  few units out missed the 32-unit slivers Blood's mirrors stand in (E1M3's, test spot `e1m3-mirror`). Sprites cross a
  stack (those of its far room). A mirror's sprites (`SpriteRenderer.DrawMirrored`) are those in front of it near it,
  the player's own body too (Blood draws the mirrors before hiding the view sprite): each placed as seen from the eye
  reflected behind the mirror (its rotation frame, one-sided tests), then reflected (`SetTransform` under
  `_mirroring`); a face sprite, which the shader turns to the real camera, gets its picture flipped and its x offset
  negated, a wall, floor or voxel one the reflection itself; always clipped to the mirror. A sprite of a room sharing
  ground with another is hidden by the walk Build makes into the mirror: from where the line from the reflected eye to
  the sprite crosses the mirror (`MirrorEntry`), not from that eye (behind the white mirror wall: the walk ended there
  unknown), and whether or not the camera's own walk sees the sprite's sector (report 20261105-220910). Where rooms share space the
  copy's stacked-rooms walk (`StackedRooms.Shader`, `stack_discard`) starts in the mirror's own sector (its row's far
  sector) with the pixel and the eye reflected back (DoWallMirrors walks in from mirrorsector behind the wall): from
  E1M3's hall a room sharing its ground showed in the mirror. A link seen in a mirror is black as in any copy.
* **Stacked rooms**: `SectorMap.FromBoard` makes the shading data from the board (refreshed as it changes), and the
  sectors sharing ground are found after trInit has put the movers where they start (the movers moving whole left
  out: a door panel stored open lies over the next room). The walk's wall data follows moved walls
  (`StackedRooms.RefreshWalls`).
* **Lights and atmosphere**: the dynamic lights from the sprites by the old converter's rules (torches, candles, the
  fixture tiles, decorations drawn fullbright: `DynamicLights.SetupSpriteLights`); ground mist over the open sectors
  and the weather by episode (the old converter's guesses, `World.NativeAtmosphere.cs`, `GroundMist.SetupNative`); the HUD
  weapon in the view sector's floor shade with its light effect (viewDrawScreen).
* **Checking**: `python pipeline/compare.py <MAP>` and `compare_batch.py` (Comparing with NBlood above). Old
  recordings (from the converted levels) don't replay exactly (the AI's probes and sight are Build's now).
* Not yet: occlusion culling of the native world.

Sprite shadows (`SpriteRenderer.DrawShadow`, viewProcessSprites' kViewEffectShadow; `b3d_spriteshadows`, default
on): monsters (not the player's own body) and things physics moves while in the air (corpses always) get the frame
they show as a face sprite a quarter as tall, black and a third opaque, standing on the floor under them (their
Build sector's floor there, or the blocking floor sprite they stand on), 2 units further from the camera so the
sprite stays in front; only where that floor is below the camera. Over a floor drawn through (a portal link side, tile 504: stacked rooms, see-through water, NBlood's IsRorSector) it lies on the floor of the room below, moved back by the link offset, and is drawn as that portal's copy (hidden while the portal isn't drawn). Like Blood's, it takes only a rotation frame's
mirroring, not the sprite's own flips or offset.

Ambient occlusion (`Render/AmbientOcclusion.cs`; `r_ao` 0 off, 1 SSAO, 2 GTAO, `r_ao_radius` in Quake units,
`r_ao_intensity`, `r_ao_debug 1` shows the term alone; F9 steps through the modes, Enhanced > Lighting has AMBIENT OCCLUSION and AO INTENSITY): a compositor pass on the
level's WorldEnvironment. Godot's own SSAO darkens only ambient light, and the level's Build shading is emission, so
it shows nothing here; this one runs before the opaque pass (after the depth prepass, which writes depth, normals and
roughness while the pass is on) into `ao_texture` (a `Texture2Drd`, a global shader parameter: white while there is
none, set back to white before the pass frees its textures), and the opaque Build shaders (the level, sprites, voxels)
multiply only their emission, Build's shading, by it (`AmbientOcclusion.ShaderApply`): the dynamic lights and the fog
Godot shades over them aren't darkened (darkening the finished image, it put dark corners over the enhanced fog's
glow); translucent faces and the see-through water, whose pixel's occlusion is what lies behind them, don't take it. Positions come from the depth buffer through the scene uniform buffer's inverse
projection (its first members), normals from neighbouring depths. SSAO: 16 samples over the hemisphere about the
normal, each occluded where the surface in front of it is nearer. GTAO (Jimenez et al., after XeGTAO): three slice
directions, six steps each way, the visible arc between the highest horizons integrated with the cosine weight.
Both take per-pixel noise (interleaved gradient, moved each frame), then a 5x5 blur over pixels at about the same depth.
`r_ao_half` (1, the default; Enhanced > Lighting, AO RESOLUTION): the occlusion is found for every second pixel each
way and the second pass spreads it over the picture as it blurs, each pixel from the three or four found each way
around it at about its own depth, weighted as 3x3 boxes about the nearest (over such a box the noise averages out;
weights falling off with distance left its hatching). At 3440x1440 the pass takes 0.8 ms instead of 2.0 (report
20261115-161600: Eviction's station hall), the finished picture all but the same. The sky drawn the faces' way is level geometry (Build's parallax ceilings and floors), so its material writes roughness 0 and the pass, reading Godot's normal-roughness buffer while it is on, leaves those pixels alone; drawn Build's way it comes after the pass.

Sky faces (`BuildSky`, `Render/Native/NativeSky.cs`): the sky is shaded by the level's sky shade, the commonest shade of
its sky ceilings (Build shades a parallax sky by its sector's ceiling shade, and a map's are nearly always alike:
MARROW's night skies at 35 to 45, Death Wish's at about 21; Blood's own at 0), the background sky too. Build's way:
Polymost's sky covers the walls and floors its walk reaches behind a parallax ceiling, but never a sprite (it lies at
the far end of the depth range). The sky faces draw before anything else in the translucent pass (lowest render
priority), without depth of their own, over the level behind them, except where opaque sprites and voxels marked their
pixels in the stencil buffer (3, `SpriteMark`); the sky marks its own 2, which blob shadows leave alone (`UnderSky`).
So a tree rising past a sky ceiling stays whole (CP09, DWE1M1). Only the sprites of sectors the walk reaches without a
link mark their pixels (`SectorVisibility.DirectlySeen`); the others take shaders without the mark, so the sky hides
them as it hides the level there.

The default (`r_skyfaces 0`, not saved, `BuildSky.Faces`; 1 is the old way, the faces painting the sky over what lies behind them): the sky faces draw nothing, the background sky shows, and the
visibility walk hides the rest. `SectorVisibility` keeps for each sector reached without a link what lines of sight reach it: the
angles across, and their slopes up (height over distance), which stay the same all along a line, narrowed at each opening
(`OpeningSlopes`; between two skies it runs on upward). A sector stacked over another (`map.Stacks`) takes its window as
seen directly; one that only overlaps it in plan counts as seen (moving sectors rebuilt) but not directly, as Build reaches
a sector only through walls or links (DWE3M1's black backstage rooms overlapping the lake drew a wall over the trees;
`seen <sector>...` prints each one's window). The shaders drop a face of a sector not reached in the vertex
(`b3d_seen`) and the fragments outside its window (`b3d_window`, `b3d_outside`), so this is a coarse form of Build's columns; a
sprite in a sector not reached isn't drawn. Compared with NBlood on 504 views under open sky: closer on E1M1's and E3M1's, where
the painted sky covers floors and houses. Not done: a leak left on CP09 (spot `sky-cp09`), the fog lies over the background and not the faces. The faces' `SkyOver` stays for `r_skyfaces 1`. The culling is only in the camera's own view (`b3d_eye`): shadow passes see the whole level. A room reached through a link is walked from the eye as it sees it (the link's offset). Animated pictures on the level (`AnimatedPicture`) update one texture's pixels: a new texture on the material each step made Godot rebuild all that uses it, a steady stutter wherever water portals are on.

Water under a surface that moves (`NativeWorld.FollowWater`, with `r_waterportals`): the ceiling of the room under a level water
surface is drawn at the surface, its plane in `b3d_planes` and its portal's (`PortalRenderer.PlaneYOf`), the wall pictures still pegged
to its own height, the game's heights untouched. E2M1's lake bobs while the ceiling under it stays, and the room's walls stopped short
of the surface: a dark sliver along the waterline, and a boat's reflection of nothing.

Sprite bodies (`World.UpdateBox`): a sprite blocking movement and hitscans (cstat 1 and 256: dudes, most things) is a
`Body`, one only blocking movement (cstat 1: E1M4's window grates, which bullets pass) a `Blocker`, one only hit by
hitscans (256) a `Corpse`. `MaskShot` (CLIPMASK1: vectors, the aim) takes Body and Corpse, `MaskMove` and `MaskZRange`
Body and Blocker; `HitScan` takes a mask, and its CLIPMASK0 callers (a missile's spawn check, a thrown thing's, a
dude's CanMove) pass `MaskBlock` (Body and Blocker). A missile starting inside a sprite's box hits it only when it moves
into it (point-blank); moving away it goes free, as Build's clipmove (the dropped life leech's bolts start against
the leech).

Classic palette, post process (`r_useindexedcolortextures 2`, Video: Classic Palette, Post Process): the textures' palette and a last pass over the 3D view and the
weapon, under the tints and the status bar, takes every pixel to its nearest palette colour, so fog, mist, rain, AO and the lights keep to the 256 too.

Classic palette, textures (`Render/ClassicPalette.cs`, `r_useindexedcolortextures 1`, Video: Classic Palette, Textures): NBlood's Polymost "Texture
mode: Classic". Each texel is looked up as its BLOOD.PAL index (`palette_lut`, 64^3: the textures hold the palette's
6-bit colours, one per cell; others take the nearest) and shaded through Blood's shade table (`gfx/normal.plu`, from
export.py; `palette_shade` 256 x 64), blending the two rows around the shade and taking the distance part half a
shade lower, as NBlood's shader. Off: the shade is a brightness factor on the true colour. The shaders' `build_color`
does both (world, sprites, voxels; the HUD and weapon stay true colour).

Music (`Client/MusicPlayer.cs`): each level's and the menu's from blood.ini, Track (a CD track) or Song (a MIDI song).
CD music (`b3d_cdmusic`, Sound options; NBlood's Redbook audio) plays Refreshed Supply's CD tracks; off, or on an
install without them (DOS Blood), the MIDI song plays on the MIDI device (`b3d_mididevice`): Refreshed Supply's SC-55
recordings, or an emulated OPL3 FM card as NBlood's OPL3 driver plays it (`Client/MidiFm.cs`: audiolib's MIDI player
with EMIDI for the AdLib card and the AdLib General MIDI driver with Blood's GMTIMBRE bank, the FM piano fix on;
`Client/Opl3.cs`: Nuked OPL3 with its stereo extension), streamed through an AudioStreamGenerator (about 3% of a core).
A missing recording falls back to FM. pipeline/export.py's `export_music` extracts the songs and the timbre bank from
SOUNDS.RFF (`sound/midi/`) and copies the SC-55 recordings (`sound/sc55/`). `fmrender <song> <seconds> <file.wav>`
renders a song on the FM synth.

Intro and cutscenes (`Client/MoviePlayer.cs`, NBlood's credits.cpp and levels.cpp): the game starts with Monolith's
and GT Interactive's logos (LOGO.SMK, GTI.SMK with SOUNDS.RFF's LOGO and GT sounds), then the title screen with
thunder (`b3d_skipintro 1`, Game options: straight to the title screen). An episode's cutscene A plays on entering its
CutSceneALevel from New Game, cutscene B when it ends (blood.ini's EndingA / EndingB: none means the end), then the
credits (QAV CREDITS looping, centred on black, as NBlood's menuCredits; also Main menu: Credits);
the cutscenes' subtitles come from Refreshed Supply's English .srt, the same for both versions (`b3d_subtitles`); `b3d_cutscenes` (Game options) picks the original Smacker videos or Refreshed Supply's remastered
Theora ones (the original when one is missing). Smacker videos decode in `Client/Smacker.cs`, ported from NBlood's
libsmackerdec (video only: NBlood plays the WAV beside them, never their own audio), drawn in their palette at their
frame rate, 320x200 ones filling the 4:3 screen, larger ones 1:1 in a 640x480 view. Escape skips a video, any key
after its first second. pipeline/export.py copies them into `movie/` (`export_movies`: DOS/BLOOD121's .SMK and
CSnn822M.WAV, the logos' sounds, `remastered/` Refreshed Supply's csN.ogv). `movie <name> [wav]` plays one.

V1.0x weapons balance (`b3d_weaponsv10x`, Game options; NBlood's option, on by default: Refreshed Supply's voodoo doll
and life leech feel like it): the voodoo doll's alt fire hits every target in view with the full charge's damage for
20 charges and the doll stays; the life leech heals its user by a quarter of the damage; a flare that sets its target
burning hits it too; the napalm launcher's alt fire shoots like its primary. Recordings carry it (`setweaponsv10x`
when changed in a level; older recordings: off).

Fix Blood's bugs (`b3d_fixblood`, Game options, on by default; `World.FixBlood`): changes to Blood's own behaviour where
it is plainly a bug. Blood changes a dude's sector only when it moves, so a sector moving away from under
one standing still (E2M1's iceberg 114, which has no Drag) left it held up by that sector's floor plane until its first
step; `MoveDude` now looks up the sector holding it when it stands still too, and it falls at once. A missile isn't solid
(napalm's SEQ frames aren't blockable) and Build's clip lines stop only what crosses them from outside, so a dude
stepping onto one between two game frames left it inside the dude's box and it flew on through (DWE3M1's gill beasts);
a missile starting its move inside a dude's box now hits it (`ClipMoveB`, `_insideDude`). Recordings carry it
(`setfixblood`; older recordings: off, Blood as it was). Walk tests pin it on (`; setfixblood 0` after the warp: off).

Scrolling textures (NBlood's `DoSectorPanning`, `World.NativeSectorPanning`): floors, ceilings and walls scroll by
their x-sector's and x-wall's panning, written to `b3d_sector` / `b3d_wallpan` each game frame and added in the shader.
A panning floor also pushes what stands on it (`World.ActApplyPan`, NBlood's actProcessSprites).

Dynamic lights (`Render/DynamicLights.cs`, off by default; Blood has none): point lights from explosions, burning
things, fire projectiles and the muzzle flash at run time, and the level's own (`SetupSpriteLights`): one for each lit
torch and candle, light fixture (lamp posts, lamps, candelabras, fluorescent tubes and panels: `FixtureLights`, by tile)
and decoration Blood draws fullbright, linked to its sprite: the light follows the sprite and goes out with it (a torch put out, a shot or
broken fixture no longer showing the light's `tile`, a switch showing its off tile), and a fixture Blood draws in its
sector's light follows the sector's light effect, so it is dark while the room's light switch is off and flickers with
a flickering room. A sprite's light (and an explosion's, a projectile's, a burning thing's) is left out wherever the stacked-rooms walk
hides the sprite from the camera (`StackedRooms.SpriteHidden`): E2M5's torch in the closed-off niche 136, whose floor
lies below the ceiling of corridor 694 under it, lit that corridor's ceiling.
They're added on top of Blood's shading (the
shaders are lit, Blood's shade is their emission, with no ambient light, so the picture is unchanged without lights).
Flames and candles flicker smoothly (value noise). `r_shadows` lets them cast shadows (world and voxel models; a
fixture's voxel model, on render layer 3, neither shadows nor takes fixture lights, which sit inside it: the lantern's pane facing its light burned bright otherwise).
Only the `r_shadowlights` (8) most important cast shadows (`PickShadows`: energy times reach squared over the squared
distance to the eye, no nearer than half its reach; one casting now counts 1.3 times, so two alike don't trade places), a
light joining or leaving fading its shadow over 0.35 s (`ShadowOpacity`), and none past `r_shadowdist` (1500, Godot's
distance fade over a further 400; the light itself stays). Each shadowed light redraws its six faces with everything in
reach whenever something there moves: DWE2M1's turning fans with 24 lamps 1400 to 4000 units off made 9000 of its 10000
draw calls (now about 4000; Options > Enhanced > Lighting: SHADOW CASTERS, SHADOW DISTANCE). With more sources than
the 24 lights, the farthest in use fade out so lights don't pop as they change places, and a source keeps its light
while it stays in, so a still light's shadow map isn't redrawn. Godot keeps a light's shadow map until the light or a
shadow caster near it changes, so lights and voxel models are only touched when something actually changed (setting a
light's range or a node's transform, even to the same value, forces a redraw), spinning voxel models (pickups) cast no
shadow, and moving lights use dual-paraboloid shadows (two passes instead of a cube's six). A light near a room-over-room portal also shines into
the linked room as a shadowless copy moved by the link offset; the shaders know each portal side's linked room (its
footprint beyond its plane: the `light_region_*` globals) and the copy lights only inside it, the light itself only
outside (a light names its region by its specular amount, which the lit shaders don't use otherwise), so the copy
can't shine through the walls around it. F7 toggles dynamic lights and F8 their shadows. `light` places a white test
light at the camera (`light clear` removes them); `lights` lists the lights in use, `links` the portal sides.

Debugging commands for map work: `sprat <x> <y>` prints the sprites and voxel models drawn under a screen pixel (and
their Build sectors), `bsec <n>` / `bsec near [r]` Build sectors and their x-state; `stacks` shows the camera's sector and which side each stacked pair is seen
from; `send <channel> [on|off|toggle]` sends a command as a switch would, `send s<sector>` to one Build sector's movers
(a lift without a channel).

A wall picture whose height isn't a power of two repeats as classic Build draws it (NBlood's polymost npot emulation,
the world shader's `npot_wall`): every power-of-two height, the band past the picture showing the next column's
texels (E1M8's poster pillar).

Blood's view effects that change what is drawn (`viewProcessSprites`): a lit torch shows its next tile with a flame
(tile 2101, fullbright) on its top, a lit candle is fullbright with a halo standing on its top (`kViewEffectPhase`:
tile 626 at repeat 24, translucent, shade 26; NBlood's GL renderer darkens the halo more, as it does fullbright colours), a regular tesla bolt has a halo behind it
(`kViewEffectTesla`: tile 2135 at the bolt's repeat, translucent, shade -128, 2 units further from the camera). A torch or candle without an x-sprite in the map
(`noX`) burns. Sprites a z-motion sector carries (flagged 8192/16384, or decorations and items standing on its
floor) start at its OFF height and follow its floor or ceiling mover (`moveZPart`, the mover's `zPart`).

Blood's procedural fire (tile 2342, `Render/ProceduralFire.cs`) runs live as NBlood's `fire.cpp` does, with the colour
lookup `data/rfire.clu` from the asset export. 

A masked wall is drawn as Build draws it: from each side whose own wall is masked (cstat 16), with that wall's picture,
mapping, palette, shade and translucency (cstat 128, 512: a third or two thirds), across the opening and pegged as
Build pegs it (`WorldMesher.EmitWall`); a one-way wall (cstat 32) from its own side only.

The whole simulation (player, weapons, all AI, actors, physics, triggers, movers, links, sector light effects,
level messages, secrets, end of level with the inventory carried to the next map), Build shading, sprites, voxel
models (`b3d_voxels`), the first-person weapon (QAV), the gear drawn over the view (the diving suit's visor, the asbestos
armor's hood: `Hud.DrawGear`; a hand holding on, QAV 518 over the status bar too: `Hud.DrawChoke`, choking the player
each second as `gChoke`, `World.Tick`), the status bar in Blood's three sizes, screen tints, messages,
sounds (3D, with DarkPlaces' falloff, and the ambient loops), music, the menus and the level stats screen.

Teleporters (sector type 604, `NativeOperateTeleport`): the sector's marker0 is its destination (kMarkerWarpDest).

Water: Blood's water is its underwater sectors (xsector Underwater), entered and left through the water links
(`NativeCheckLink`; `PointIsWater`: an underwater sector, or one whose lower link is a water or goo one): inside, the
player swims (jump up, crouch down). The view is underwater (tint, banned weapons) while swimming unless the eye is
over the ceiling of a sector whose lower link is a water or goo one, as Blood's playerProcess tests (E4M4's water 117
under water 118 is joined by a stack link). Buoyancy (MoveDude) is Blood's: a sector is deep where it has Depth or a
water or goo link on its floor or ceiling. `warp` (and `gotobug`) put the player swimming in an underwater sector, as
NBlood's `-b3dwarp`. Under water, in goo or with the death mask, Blood draws the whole screen through another palette
(WATER, SEWER, BEAST, INVULN1; build_assets exports them to `gfx/*.lmp`). `Render/ViewPalette` fits a colour matrix
mapping BLOOD.PAL's colours onto each one (within about 2 of 255 on average) and the HUD applies it over everything in
one pass. `where` prints the player's posture, medium and whether it is under water.

### Modern features (NoOne's extensions; `Sim/Modern/`)

A map made with xmapedit for NBlood's "modern features" (map version 7.1 or 7.2, or an object sending command 100 on
channel 60 or 61 to itself: `World.ModernRevision`, the console says "modern map (revision N)") gets NoOne's additions
to Blood's game code; `World.IsModern` gates every one of them, so Blood's own maps play as before. The port follows
NBlood's `nnexts.cpp`, `nnextsif.cpp`, `nnextcdud.cpp`, `aicdud.cpp` and `aipatrol.cpp`:

* **The x-sprite's extra fields** (`Sprite.Unused1..4`, the dude flags, `SysData1..4`, `PhysAttr`) and the lists a
  sprite joins for its triggers (`World.Modern.cs`: Proximity, Sight, Screen and Aim, Impact on sprites that are no
  dudes or things; `NnExtProcessSuperSprites` each game frame). Events carry their causer; an object of a causer
  sequence loses only that causer's pending events; channels 30-36 are the players, 50 the event's causer.
* **The modern types** (`World.ModernTypes.cs`): random and sequential TX, the changers (properties, pictures, sizes,
  data fields, sector light effects, slopes, velocities), the damager, effect, sequence, sound, missile and wind
  generators, dude and custom dude spawners, the teleport target, gib and explode objects, the player control
  (`World.ModernPlayer.cs`), sector motion paused and continued. Sequences on walls, masked walls, floors and ceilings
  are `SurfSeq`. A changed picture, palette, shade or flag of a sector or wall counts the sector's look up
  (`Board.TouchLook`), and `NativeWorld.GameFrame` builds its mesh again.
* **Conditions** (`World.ModernCond.cs`, types 501/502): the whole of `nnextsif.cpp`'s checks on sectors, walls,
  sprites, dudes, custom dudes and players, with pushed objects and tracking.
* **Debris physics** (`World.ModernDebris.cs`): sprites given mass that fall, float, are pushed by explosions and shots.
* **Custom dudes** (sprite type 254; `CustomDude*.cs`, `World.Cdude.cs`, `World.CdudeAi.cs`): a monster described by a
  descriptor file `CDUDnnnn.CDU` (its data1; version 2: groups General, Velocity, Animation, Sound, WeaponN, Damage,
  Dodge, Recoil, Crouch, Knockout, FXEffectN, DropItem, MoveSetup, Flight, Morphing, Sleeping, Slaves, Randomness,
  LastStand, TargetChange, Tweaks; `CustomDudeSetup` reads it, the first dude of a kind is the template the others
  copy) or, in older maps, by its data fields alone (version 1). It has its own states by posture (standing, crouching,
  swimming, flying), up to twelve weapons picked by distance, angle, height, health and chance (a vector, missile,
  thrown thing, summoned dude, blowing itself up, the beast's stomp, ramming, teleporting, morphing, damage or healing
  from afar), effects by state and frame, and its own damage scales, dodging, knockouts, last stand, sleeping and
  drops. `cdudes [near <radius>]` lists them.
* **Patrols** (`World.AiPatrol.cs`): a dude with the patrol flag walks from path marker to path marker, waits, turns,
  guards, sends the markers' commands, and notices players by touch, hearing (the sounds played lately by something of
  the player's: `PatrolNoteSound`) and sight, in stealth mode by a spot value the patrol regions change; an alarmed
  dude calls the others in sight. While one in stealth mode notices a player, a bar over its head shows how far
  (`SpriteRenderer.DrawSpotProgress`: NBlood's kViewEffectSpotProgress). `patrols` lists them and what each senses
  of the player.
* **Sloped floor sprites** (cstat 48, the slope in the offsets; `Sprite.Slope`, `ZOfSlope`): drawn tilted
  (`SpriteRenderer`), and solid as the engine has them in clipmove, getzrange and hitscan (`World.BuildClip.cs`,
  `World.NativeTrace.cs`). Maps build ramps and tanks out of them.
* Smaller rules: markers flagged to move with their sector, the sprites just outside a moving sector's walls moving
  with it (`SprNearSectInit`), an item's own pickup message, a statue breaking out (data1 1), gargoyles, phantasms
  and hounds attacking other dudes, pods on ceilings, sounds with their own pitch and volume (`Sound3DCP`).

Not there yet: the target changer (AI fight), QAV scenes, custom items, lasers, the path-sprite sectors, the
conditions on seen tiles and sectors, the underwater palette a link names, saving. `World.ModernMissing` says so once in the console when a map asks for one.

### Build's sectors in the game (sector tracking, the visibility walk, Build's collision)

The level's Build sectors are the game's (`Sim/Build/Board`, as `Sim/SectorMap` for the lookups: every sector's
loops, floor and ceiling planes, every wall with the sector and wall beyond, the sectors sharing ground in plan,
`overlaps`, found on the map as stored before trInit). Three things answer from them as Build does:

- **Which sector** (`Sim/World.SectorTrack.cs`): every sprite keeps its Build sector the way Build keeps `sectnum`: the
  same while its outline holds it (whatever else shares the ground there, but a sector moving whole that holds it
  first), else the sector beyond the two-sided walls
  its move crossed, else a neighbour, else any whose column holds it (`updatesector`, Blood's `FindSector`); a sprite
  below or above its sector's column where a sector sharing the ground holds it changes to that one (a noclip flight
  down into the room below). A link moves it to the sector beyond (`MoveThroughLink`), a teleport or `warp` looks it
  up afresh (`warp`'s sector argument puts it there). Heights are the board's as the game has moved them
  (`SectorFloorZ`). The game's sector effects (enter / exit, water, damage, wind, panning floors, the AI's probe) take
  the sector's x-object (`XSectorOf`). Sprites are shaded as Blood's `viewProcessSprites`: their own shade plus their
  Build sector's (its ceiling's under a sky, else its floor's, with the tile's) and its light effect now
  (`SectorSpriteShade`; the renderer only peeks at the sector, `PeekSpriteBSector`, never moving the game's).
- **What's seen** (`Render/StackedRooms`): the camera's sector is the player's (Blood's view: the player sprite's
  sector, then the one beyond a link the eye is past, `PlayerView.EyeSector`); a flying camera follows its own. Every
  face of the native world carries its Build sector (UV2.x - 1: a floor, ceiling or one-sided wall its sector's, a
  lower or upper wall the next sector's), and the world shader walks Build's way from the camera's sector to just in
  front of the pixel through the two-sided walls; a face of a sector sharing the ground with others is dropped where
  the walk arrives in one of them (`stack_discard`), so of rooms over rooms and rooms sharing space the one Build reaches
  shows; a portal copy walks from the sector beyond its face, starting where the eye's line comes through the link's
  plane (the eye moved by the link's offset is seldom in that sector: on Eviction's balcony over its street, a link 32
  units off, it stood over the street's next sector, the walk left the far sector by the wall the line came in
  through, and the balcony's own floor in the copy stayed, a black sheet over the street: report 20261115-161414; a
  link over several sectors walks first along the plane from a place known to be in the far sector, `FarStart`). A
  pixel outside the bounds of the sectors sharing the ground with its own is in none of them and isn't walked for.
  Too far to walk (32 sectors): a sector sharing the ground
  fewer steps from the camera's shows instead, if the pixel lies within its floor and ceiling (`stack_nearer`,
  `b3d_holds`: DWE1M1's cellar 358 under the hills hid them). Sprites the same, by their tracked sector, and clipped to
  their own sector's floor and ceiling (`zclip`: Build's classic renderer does; a z-motion sprite sector keeps its risers
  under the floor, E1M1's fire 268).
- **What collides** (`Sim/World.BuildClip.cs`): moving actors and things use Build's `clipmove` (Blood's 1996 engine:
  walls and blocking sprites of the sectors around the move, a two-sided wall blocking where the next floor or ceiling
  is beyond the sprite's clip band; the board's walls as TranslateSector and DragPoint have moved them; a gib wall
  broken clears its blocking flags on both sides, SetupGibWallState, and a restart puts the stored ones back), `pushmove` in a moving
  sector and on the player every frame (playerProcess: E3M5's turning block under link 30, beside which a fall through
  the link arrived, had its top taken for the floor and threw the player back up), and `getzrange`
  (the highest floor and lowest ceiling within reach, blocking sprites) with Blood's `GetZRange` over it (sky, `Depth`,
  through a link the room beyond); moved out of every sector, a sprite keeps its own (Blood ignores updatesector's
  -1), and one put outside them all takes the nearest. Shots, sight and the AI's probes are Build's hitscan,
  vectorscan and cansee (`World.NativeTrace.cs`).

Not yet: save/load, multiplayer, Blood's credits and loading screens.
