# 20261015-161045: gates are closed but has no collision

Status: fixed
Regression: sprites, collision
Regression check: the closed gates block

## History

- 2026-10-15 16:10:45 reported in maps/e1m1.bsp
- 2026-10-15 fixed. Cause: Every blocking sprite got a square axis-aligned box of clipdist size. Build clips wall sprites as a line across their full width and floor sprites as a flat rectangle, so wide/diagonal wall sprites (the cemetery gates, tile 1044 at angle 1792) could be walked through.
- 2026-10-15 Fix: Engine: new .boxangle field gives SOLID_BBOX/SLIDEBOX/CORPSE entities a yaw-oriented box (traces rotate into the box frame, box-vs-box uses a brush trace; area links cover the rotated box). QC spriteUpdateBox: wall sprites get a thin box across the drawn width (tile width * xrepeat, x offset, x flip), floor sprites a flat rectangle; axis-aligned ones stay plain AABBs. Applies to all wall/floor sprites on every map. Dev commands: sprinfo <picnum>, where.
