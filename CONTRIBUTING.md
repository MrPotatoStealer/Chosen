# Contributing

Thanks for wanting to help. Chosen is GPL v2 (`LICENSE.md`): what you send is taken under the same licence.

## Signing off

Add a `Signed-off-by:` line to each commit (`git commit -s`). It says you wrote the change, or otherwise have the right
to send it under the GPL v2, as the [Developer Certificate of Origin](https://developercertificate.org/) puts it.

## What may go where

- **The clean core** (`shared/Chosen.Engine/`, and the files in `forge/` and `shared/` that start with
  `// Chosen core: original code.`) holds only code written for Chosen: nothing copied or translated from Build
  (`engine.cpp`, `clip.cpp`: Ken Silverman's licence), NBlood, xmapedit or UltimateDoomBuilder (GPL v3, which can't be
  mixed with Chosen's GPL v2). Studying how those behave is fine; transcribing their code is not. Write it from the
  maths and the data's meaning.
- **The Blood module and the game** (`shared/Chosen.Blood/`, `loader/`, `godot/`, the files that start with
  `// Chosen Blood module (GPL v2): ...`): code following NBlood or xmapedit belongs here. Code ported from Build's
  engine needs Ken Silverman's notice at its top (see `godot/src/Sim/World.BuildClip.cs`) and a line in `LICENSE.md`.
- A new file in `shared/` or `forge/` starts with one of those two lines, the Blood one saying what it derives from.
- Never commit any of Blood's files, or anything made from them: art, sounds, music, levels (.MAP), voxels, palettes,
  and screenshots of the game (the repository's `.gitignore` keeps the bug reports' pictures out).

## Versions

`VERSION` holds Chosen's version, shared by the game, Forge and ChosenLoader (`Directory.Build.props`; builds from the
repository say `0.1.0-dev+<commit>`). Making a release:

1. Raise the version: `VERSION`, the three `export_presets.cfg`' file and product versions, the three
   `project.godot`'s `config/version` (`build_dist.bat` checks they agree). Commit.
2. `git tag v0.1.0`, then `build_release.bat`: from that tagged commit only (nothing uncommitted), it builds the
   programs, checks each one's pack holds nothing of Blood's (`pipeline/pck_list.py --check`: no pictures, sounds,
   maps or test files), and writes `release/Chosen-0.1.0-windows-x64.zip`, `ChosenForge-0.1.0-windows-x64.zip` and
   their SHA-256 sums, each README.txt naming the source at the tag. `build_release.bat --test` tries it on the
   working tree.
3. `git push origin main v0.1.0`, and on GitHub a release from the tag with the two zips and the sums.

Two
formats have versions of their own: the data folder's (`Extractor.DataVersion`: raise it when the extraction's output
changes so the game asks for a new one) and the .MAPX's (`MapXFile.FormatVersion`: godot/README.md ".MAPX" says
when; raising it means a migration step in `MapXFile.Migrations` so older maps still open).

## Building and testing

You need your own copy of Blood (Blood: Refreshed Supply on Steam or GOG). See `README.md` for setting up, and
`godot/README.md` and `forge/README.md` for the tests to run after changing the game or the editor:

- the game: `python pipeline/bugs.py regress <category>` for the code a category covers (`bugs/regression/README.md`),
  and `python pipeline/bugs.py motion` after changing anything drawn in motion. Your first run makes your own
  baselines (the repository has none: they show Blood's art); later runs compare against them, so take them before
  making your change.
- Forge: `forge\run.bat --selftest`, `--uitest`, `--regress` and `--roundtrip <maps folder>`.
- the map format code: `dotnet run --project shared/Chosen.MapTool -- roundtrip "game/blood3d/maps/*.map"` stays
  identical.
