# Chosen: notes for Claude

Blood (Build engine) ported to Godot 4.7 C# in `godot/`: levels load from Blood's own .MAP files (the Build data,
`godot/src/Sim/Build`, drawn by `godot/src/Render/Native`; README "Levels from Blood's .MAP"). The reference is
**NBlood** (`third_party/nblood`, patched); never use the retired DarkPlaces version (`Deprecated/`: local only, not in the repository; its QuakeC and `godot/tools/gen_*_cs.py` too) as a reference.
If NBlood's source doesn't settle a question, ask the user to compare in the game. The TrenchBroom/brush conversion
that came before is only in the old (Gitea) repository's history, its `trenchbroom` branch. The port was called Blood3D. The map editor is
Chosen Forge (`forge/`, `forge/README.md`), a separate app in the manner of UltimateDoomBuilder (`third_party/udb`),
sharing the map code with the game (`PLAN.md`); after changing it, `forge\run.bat --roundtrip <dir>`,
`--selftest`, `--uitest` and `--regress` must pass (after changing drawing, joining or the dialogs also `--rebuild=<DM2, DM3
and DM1's absolute paths>`: DM2 and DM3 identical, DM1 19 known differences; forge/README.md "Running"; Godot run by hand for these gets `--log-file <scratch file>` before `--`, else test runs push the user's Forge logs out of the five kept); Q is a 3D preview; xmapedit (`third_party/xmapedit`, NoOne's; `build_xmapedit.bat`) is a reference for Blood's editing rules.
Forge records every session; the user's **Help > Report a Problem** puts one in `bugs/forge/<date-time>/` (`note.txt`
says what went wrong, a `mark` line in `events.log` where): replay it (`forge\run.bat --replay=bugs/forge/<date-time>`,
headless; `--until=N`) to see the problem before fixing, then make it a regression replay (`--addtest=... --name=...`,
`forge/tests/replays/README.md`) once the replay gives the right result (`--accept`). Forge's sessions folder (`%APPDATA%/Godot/app_userdata/Chosen Forge/sessions`, `last`) holds the user's recent sessions too.

## Tools

- Godot: `tools/godot-4.7-mono/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe`;
  build with `dotnet build -v q` in `godot/`. It runs on Direct3D 12 (`project.godot`: Vulkan has no HDR output on Windows) unless
  the user picks Vulkan (`r_driver 1`, `user://driver.cfg`, the next start; `--rendering-driver` overrides it for one run).
- Local paths on the user's computer (Blood's folder, mods, the old repository): `CLAUDE.local.md` (git-ignored).
  Scripts find Blood with `pipeline/blood_install.py` (`BLOOD`, else Steam's and GOG's folders), as ChosenLoader does.
  Never commit Blood's files or pictures of the game (`.gitignore` keeps `bugs/`' pictures out), nor local paths.
- Versions (godot/README.md "Versions", CONTRIBUTING.md): `VERSION` for the game, Forge and the loader; raise
  `Extractor.DataVersion` when the extraction's output changes, `MapXFile.FormatVersion` only when an older reader
  would misread a .MAPX. Licence: everything GPL v2, Build's engine ports also under BUILDLIC (`LICENSE.md`).
- Python: `$LOCALAPPDATA/Python/pythoncore-3.14-64/python.exe`. Write edit scripts to files (the shell mangles
  backslashes and long heredocs).
- Data folder (`game/blood3d`): `dotnet run --project shared/Chosen.MapTool -- extract "<Blood folder>" game/blood3d
  --addons all` (the C# extraction ChosenLoader runs; godot/README.md "Data folder and distribution"); the game draws
  tiles from its `art/` ART files. Releases: `build_release.bat` (CONTRIBUTING.md "Versions"; never publish a build whose packs `pipeline/pck_list.py --check` hasn't passed). Distribution: `build_dist.bat` (Chosen.exe + ChosenLoader.exe into
  `Chosen` beside the repository's folder, ChosenForge.exe into `ChosenForge` beside that).
- Maps: `python -I pipeline/export_maps.py` copies every level's .MAP (BLOOD.RFF, PATCH01.RFF's fixes as `<NAME>.patch`,
  each installed add-on's) into `game/blood3d/maps` and `game/blood3d/addons/<id>/maps`. An add-on of Refreshed Supply
  (Death Wish, Cryptic Passage, MARROW): `install_addon.bat "Death Wish"` (its data folder, then the maps; README
  "Add-ons"); its levels load by name like any other (`--map=dwe1m1`) and show in the menu's ADD-ONS.
- Mods made for NBlood (`-game_dir`; Eviction's folder: `CLAUDE.local.md`): the extract with `--mod "<folder>"` added installs
  one as an add-on (its .RFS resources, custom dudes; godot/README.md "Add-ons"). Their maps are modern maps (NoOne's
  extensions, `godot/src/Sim/Modern/`, README "Modern features"; reference `third_party/nblood/source/blood/src/nnext*.cpp`,
  `aicdud.cpp`, `aipatrol.cpp`); console `cdudes`, `patrols`.
- Shared libraries (`shared/`, godot/README.md "Shared libraries"): `Chosen.Engine` (the clean core: write it fresh,
  never from Build/NBlood/xmapedit/UDB code) and `Chosen.Blood` (GPL: .MAP reader/writer, validator, types, RFF/ART).
  `dotnet run --project shared/Chosen.MapTool -- roundtrip|tomapx|tomap|check|dump|diff|tiles ...` checks them without Godot (`tileuse <maps>[@<add-on art dir>]...` remakes
  `forge/data/tileuse.txt`, the tile picker's tabs: Blood's maps and each add-on's with `@game/blood3d/addons/<id>/art`);
  after changing the map format code, `roundtrip "game/blood3d/maps/*.map" "game/blood3d/addons/*/maps/*.map"` must
  stay 141/141 identical. Game console: `geomcheck all` (core geometry vs Board), `bmapcheck all`.
- Test spots: when the user should play-test a place, add a line to `godot/spots.txt` (`name map x y z yaw pitch
  sector [console cmds]`, z the feet, sector -1 unless rooms share space there) and tell them
  `godot\playat.bat [nblood|both] [nomonsters] <name>`, rather than starting the game for them.
- Walk tests (collision needing play, headless): `python pipeline/walktest.py <map> "<warp x y z yaw [pitch [sector]]>[; send ...]"
  <frames:forward:strafe[:yaw[:jump[:fire[:crouch]]]],...> [--trace a-b]` prints where the player ends up with Build's
  collision; `send` after the warp opens a door first.
- NBlood: `build_nblood.bat` builds it with `pipeline/nblood_b3dview.patch` (`-b3dview`, `-b3dshot`,
  `-b3dviews <file>`; starts without intros or the start window, `-setup` brings that back).

## Bug reports

The user files reports in game (`reportbug <text>`). Then:
`python pipeline/bugs.py import` → `show <id>` → `nblood <id>` (NBlood at the report's camera, `compare.jpg`)
→ `repro <id>` (Godot at the spot) → fix → `fix <id> "<cause>" "<fix>"`. `gotobug <id>` in the game console goes
to the spot.

## Testing rules

- Test runs are **windowed** (`--windowed --resolution 1280x720`; `--shot`, `--views` and `--bugs` runs force it)
  and **off the desktop**, so they don't cover the user's work: give Godot `--position 20000,20000` (before `--`; the
  game and Forge then move their window off the screen and never take the focus or the mouse: `--noinput` does the same without moving it, `godot/src/Client/Mouse.cs`, `forge/src/Editing/ForgeInput.cs`; a Forge `--shot` run too; bugs.py and compare.py do it, `OFFSCREEN`), and
  NBlood runs through bugs.py / compare.py (`NBLOOD_ENV`: the test config's window position, kept by `B3D_OFFSCREEN`).
- A run without `--shot` (or `--views`/`--bugs`) never quits: always give one.
- Test runs that change settings (monsters off, HUD off) put them back in a **separate** run afterwards, never
  inside `--atshot`. The user's settings: `b3d_monsters 1`, `b3d_hudsize 2`, `b3d_crosshair 1`,
  `r_dynamiclights 1`, `r_shadows 1`, `b3d_voxels 1`, `r_occlusion 1`, `vid_vsync 1`, `r_perfoverlay 0`
  (the user may toggle lights/shadows and the overlay themselves; leave those as found: regress and compare.py turn the overlay, ambient occlusion (`r_ao`) and the enhanced features (`ENHANCED_CVARS` in `pipeline/bugs.py`) off for their shots and put the user's values back). Check them after testing
  (`%APPDATA%/Godot/app_userdata/Chosen/config.cfg`). Any saved cvar set in `--exec` (vid_vsync, r_shadows...)
  is written to that file: restore it in a separate run straight after.
- Performance: `r_hitchlog 0.01` over a replay at `--speed=1` logs every frame with section times and counters;
  `dotnet-trace` (installed) profiles a running game; Godot's `--gpu-profile` (before `--`, at the resolution played at:
  `--resolution 3440x1440`) prints the GPU time of each render pass. See godot/README.md "Checking against the recordings".
- Shot run: `--map=<m> --exec="<console cmds>" --wait=<s> --atshot="<cmds>" --shot=<png>`.
- `moonstat`: the light from the sky at the camera.
- Console helpers: `send <channel>|s<sector> [on|off|toggle|n]` (trigger movers), `stacks`, `sprat <x> <y>`,
  `links`, `lights`, `perf`, `portals`, `bsec <sector>|near [r]` (Build sectors and their x-state), `cam x y z yaw pitch`,
  `b3d_monsters 0; restart` (no enemies), `timescale 0.1` (slow motion), `later <s> <cmd>` (run a command that many seconds from now:
  settings changed while playing), `lerphold 0.5` (stop and draw that far between
  two game frames), `shotburst <n> <prefix> [quit]` (save every drawn frame: a one-frame glitch, README "Checking
  against the recordings"), `hidemesh <name part> [x]` (hide meshes by node or parent name, e.g. `World_`, `Moving_`,
  `PortalCopy`, `Sprites`; `x`: all others; in `--atshot`, or `--exec` after `restart`, to see what draws something).

## Regression tests (`bugs/regression/`)

Tricky bugs are tagged by the code they exercise (`Regression: <cats>` in `bugs/<id>/status.md`; categories
and what they cover: `python pipeline/bugs.py categories`, or `bugs/regression/README.md`).

- **After changing code a category covers, run** `python pipeline/bugs.py regress <category>` (or `all`, or
  `<id>,<id>`): one Godot run visits every bug of the category (`--bugs` batch mode: restart, `gotobug`, optional
  setup commands, wait, screenshot; dynamic lights and shadows pinned per bug, on for `dynamic-lights` and off for
  the rest), restores the settings (the user's own light values as found), and compares each shot with the bug's accepted
  `baseline.jpg` (no baseline yet: the shot becomes it).
  It writes `bugs/regression/<cat>/README.md` and `sheet.jpg` (report | NBlood | baseline | now) and prints
  CHANGED for views that differ. Look at the changed ones on the sheet; if a change is intended and correct,
  `bugs.py accept <id>`.
- **When fixing a tricky bug**, tag it: `bugs.py tag <id> <cat>[,<cat>] ["<setup cmds>" <seconds> "<check>"]`.
  Setup puts the level in the bug's state (`send s136 on`, 4 s); check says what to look for when the bug needs
  play (collision, performance, triggered events). Then `regress <id>` once to record its baseline, and look at
  it: the level restarts for each bug, so a door or lid the reporter had opened is closed unless the setup opens
  it (the E1M1 coffin stairwell bugs use `send s30 on`).
- Old DarkPlaces-era reports (`.bsp` maps) can point at spots that moved since; leave those untagged.
- `collision` and `performance` screenshots only find the spot: retest those by playing there.

## Motion tests (`bugs/regression/motion/`)

The regression shots see a stopped frame and replays check the game frames; `python pipeline/bugs.py motion [<id>,..]` checks
the rendering steps between game frames. Each case is held at several blends and the view as drawn is compared with every
moving sector built exactly for that blend (BROKEN) and with its accepted baseline (CHANGED).

- **After changing anything that draws things in motion** (moving sectors, the world mesher or shaders, interpolation, portals,
  or an optimization of them), run it: `regress` won't catch these. `--selftest` must show the boat, the yard door and E2M1 BROKEN (the test still
  catches a wrong blend); `motion accept [<id>,..]` takes new baselines after looking at `sheet.jpg`.
- A new moving thing that went wrong: add a line to `cases.txt`. A case reported as not moving tests nothing: give it another
  moment.

## Map comparison (`pipeline/compare.py`)

`python pipeline/compare.py <MAP> [--max N] [--only ids] [--skip-nblood] [--skip-godot] [--newviews]`: views at the
tricky places (stacked rooms, links, movers), shot in NBlood (`-nodudes 1 -b3dviews`) and Godot (`--views`), scored
(lower is closer) into `tools/compare/<map>/index.html` with pair images; a map compared before keeps its cameras and
NBlood's shots. Many maps at once: `python pipeline/compare_batch.py <map|addon id>... [--max N]` (each map's mean now
and in the run before, its worst views and those that got worse, in `tools/compare/batch.txt`; restores the user's
settings at the end). Use it after changing the renderer or the game's sector code; review the worst and the worse
views. Known leftover differences: NBlood's palette shading reddens dark areas, sky horizontal offset, fire and water
animation phase; NBlood plays BLOOD.RFF's maps and the game PATCH01.RFF's (Refreshed Supply's fixes: wall panning, a
few sector heights: E6M6's door 370 is stored open there); a voxel model right by a view's camera is drawn in Godot
and not in NBlood.

## Working with the user

- Commit and push only when asked.
- `godot/README.md` documents the systems; update it with new ones.
