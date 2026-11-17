# Licence

Chosen (the game, Chosen Forge, ChosenLoader, the shared libraries, the tools, the documentation and the test data)
is free software under the **GNU General Public License, version 2** ([`licenses/GPL-2.0.txt`](licenses/GPL-2.0.txt)),
like [NBlood](https://github.com/nukeykt/NBlood), whose port of Blood's code the game follows. Copyright © 2026 Hakan C
and Chosen's contributors.

The Blood module is written from NBlood's and [xmapedit](https://github.com/NoOneBlood/xmapedit)'s sources (GPL;
xmapedit's type table, `OBJTYPES.INI`, is embedded in `shared/Chosen.Blood`). The clean core (`shared/Chosen.Engine/`
and the files headed `// Chosen core: original code.`) holds only code written for Chosen, nothing copied or
translated from Build, NBlood, xmapedit or UltimateDoomBuilder (`CONTRIBUTING.md`).

## Build engine code

The game's exact ports of Ken Silverman's Build engine as NBlood has it (`engine.cpp`, `clip.cpp`), in
`godot/src/Sim/Build/Board.cs`, `godot/src/Sim/World.BuildClip.cs` and `godot/src/Sim/World.NativeTrace.cs`, are
derived from Build's source and are also under its licence, [`licenses/BUILDLIC.TXT`](licenses/BUILDLIC.TXT):
free of charge only, no commercial use. As in NBlood and EDuke32, the game is distributed under both.

    // "Build Engine & Tools" Copyright (c) 1993-1997 Ken Silverman
    // Ken Silverman's official web site: "http://www.advsys.net/ken"
    // See the included license file "BUILDLIC.TXT" for license info.

## Blood

Blood was made by Monolith Productions (1997); its name and its files belong to their owners. Chosen is an unofficial
fan project, not affiliated with or endorsed by Atari, Monolith Productions or Nightdive Studios. This repository holds
none of Blood's files (art, sounds, music, levels, voxels): the game reads them from your own copy of Blood.
