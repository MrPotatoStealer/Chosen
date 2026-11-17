# 20261031-220507: try make rain look more pixelated and fit better with the style of the game. Slow down the speed of the rain.

Status: fixed

## History

- 2026-10-31 22:05:07 reported in maps/e1m1.map
- 2026-10-31 fixed. Cause: Requested look: rain was a thin soft streak at 896 units a second.
- 2026-10-31 Fix: Rain drawn as lines of big square pixels in a coarse screen grid (r_pixelrows, default 200), at half the speed, same slant and density.
- 2026-10-31 fixed. Cause: Requested: rain's look and speed. The square-pixel rain I tried wasn't liked, so the look stays as it was.
- 2026-10-31 Fix: Rain falls at half the speed (450 units a second) with the same slant and density; drawn as before.
