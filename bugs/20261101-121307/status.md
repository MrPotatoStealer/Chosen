# 20261101-121307: missing hud and weapons under water

Status: fixed

## History

- 2026-11-01 12:13:07 reported in maps/e2m1.map
- 2026-11-01 fixed. Cause: With the palette off, the under water palette pass read the screen copy the warp pass had already made, taken before the weapon and status bar were drawn.
- 2026-11-01 Fix: Hud: _paletteCopy also shows when the warp pass ran. Reproduced with the classic palette off; weapon and bar drawn after the fix.
