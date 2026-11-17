# 20261016-153928: strange shading on the curtains

Status: fixed
Regression: sprites

## History

- 2026-10-16 15:39:28 reported in maps/E1M1.bsp
- 2026-10-16 fixed. Cause: Each curtain is a pair of back-to-back one-sided wall sprites (tile 147, shades 2 and 16). The client ignored the one-sided flag (cstat 64), so both drew in the same plane and z-fought along the quad diagonal.
- 2026-10-16 Fix: The client culls one-sided wall sprites seen from behind their facing and one-sided floor sprites seen from the wrong side (y-flip swaps it), as Polymost does.
