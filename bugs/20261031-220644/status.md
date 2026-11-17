# 20261031-220644: would be interesting if we could somehow make the fog feel more pixelated to fit better with the style of the game.

Status: fixed

## History

- 2026-10-31 22:06:44 reported in maps/e1m1.map
- 2026-10-31 fixed. Cause: Requested look: Godot's volumetric fog is sampled through a smooth grid, so the mist could not be made blocky through its shader.
- 2026-10-31 Fix: r_groundmist 1 draws the mist by its own shader in the coarse screen grid with an ordered dither, lit by the dynamic lights; 2 keeps the smooth one. The depth fade (r_fog) and its light glow stay smooth.
- 2026-10-31 fixed. Cause: Requested look: Godot's volumetric fog is sampled through a smooth grid, so the mist could not be made blocky through its shader.
- 2026-10-31 Fix: r_groundmist 1 draws the mist by its own shader in a coarse screen grid (r_mistpixels, Mist Pixel Size) with an ordered dither, lit by the dynamic lights; 2 keeps the smooth one. r_useindexedcolortextures 2 takes the whole view to Blood's palette at the end, the depth fade and fog glow included.
