# Chosen

Blood (Monolith, 1997) rebuilt in Godot 4.7 and C#. Levels load from Blood's own .MAP files and play as they do in
Blood, drawn in 3D with dynamic lights, shadows, fog and weather; [NBlood](https://github.com/nukeykt/NBlood) is the
reference for how things should look and play. With it comes **Chosen Forge**, a map editor for Blood levels in the
manner of UltimateDoomBuilder.

Chosen is an unofficial fan project. **It needs your own copy of Blood** (Blood: Refreshed Supply on Steam or GOG): it
reads Blood's art, sounds, music and levels from your install, and this repository holds none of them.

## Playing

Download the latest release from the [Releases](../../releases) page (Windows, 64-bit): `Chosen-<version>-windows-x64.zip`
for the game, `ChosenForge-<version>-windows-x64.zip` for the map editor (unpack the two side by side: Forge's Test Map finds the game). Unpack the game and run ChosenLoader.exe
once: it finds your Blood install, lets you pick the add-ons (Death Wish, Cryptic Passage, MARROW, mods made for
NBlood) and extracts what the game needs into `data\` beside Chosen.exe. Then run Chosen.exe. After an update, run
ChosenLoader again when the title screen asks for it.

The programs aren't signed, so Windows may warn the first time you start one (More info > Run anyway).

## Building from source (Windows)

1. **Godot 4.7.2 .NET** (`Godot_v4.7.2-stable_mono_win64.zip` from godotengine.org), unpacked into
   `tools/godot-4.7-mono/` so that `tools/godot-4.7-mono/Godot_v4.7.2-stable_mono_win64/` holds its exe files, and
   the **.NET 8 SDK**.
2. **The data folder**, extracted from your Blood install:
   `dotnet run --project shared/Chosen.MapTool -- extract "<Blood folder>" game/blood3d --addons all`.
3. `godot\play.bat` builds the game and starts it (`godot\play.bat e1m1` goes straight into a level);
   `forge\run.bat` starts Chosen Forge. `build_dist.bat` makes the release folders, `build_release.bat` the release
   zips (`CONTRIBUTING.md` "Versions").

The development tools (the bug tracker, the regression tests, the comparisons with NBlood) also need **Python 3** with
Pillow (`pip install pillow`). The scripts find your Blood install as ChosenLoader does (Steam's and GOG's folders), or take the `BLOOD`
environment variable. The
references go in `third_party/` (git-ignored), each from its own repository:

| Folder | From | For |
|---|---|---|
| `third_party/nblood` | https://github.com/nukeykt/NBlood (`build_nblood.bat` applies `pipeline/nblood_b3dview.patch` and builds it; Visual Studio 2022) | the reference; `pipeline/bugs.py nblood`, `pipeline/compare.py`, `godot\playat.bat nblood` |
| `third_party/xmapedit` | https://github.com/NoOneBlood/xmapedit (`build_xmapedit.bat`) | Blood's editing rules, for Forge |
| `third_party/udb` | https://github.com/UltimateDoomBuilder/UltimateDoomBuilder | how Forge's editing works |

## In this repository

| Folder | What |
|---|---|
| `godot/` | The game (Godot C# project, `Chosen.csproj`); how it works: **[godot/README.md](godot/README.md)** |
| `forge/` | Chosen Forge, the map editor: **[forge/README.md](forge/README.md)**; its plan is `PLAN.md` |
| `loader/` | ChosenLoader, which sets up a player's data folder from their Blood install |
| `shared/` | The libraries the game, Forge and the loader share: `Chosen.Engine` (the clean core), `Chosen.Blood` (Blood's files and map format), `Chosen.MapTool` (their command line) |
| `pipeline/` | Python tools: the bug tracker and regression tests (`bugs.py`), map comparison with NBlood (`compare.py`), walk tests, the older Python extraction |
| `bugs/` | Bug reports (their text, setup and input recordings: the pictures stay with whoever made them) and the regression sets, `bugs/regression/` |
| `dist/` | The release folders' README files |

`CLAUDE.md` holds the working rules for Claude Code sessions on this repository (the testing rules hold for anyone).

## Licence

Chosen is GPL v2, like NBlood; the game's ports of Build's engine code are also under Ken Silverman's Build licence.
See [LICENSE.md](LICENSE.md), and [CONTRIBUTING.md](CONTRIBUTING.md) before sending changes.

## History

Chosen was called Blood3D. It began as a DarkPlaces (Quake engine) game with Blood's levels converted to brush maps,
then moved to Godot, at first on those brush maps and then on Blood's own .MAP files.
