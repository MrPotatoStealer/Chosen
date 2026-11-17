# 20261031-143108: make rain be affected by wind strength, increase leafes with wind amount

Status: fixed

## History

- 2026-10-31 14:31:08 reported in maps/e1m1.map
- 2026-10-31 fixed. Cause: Asked for: rain hardly moved with the wind (its share 0.55 at its fall speed), leaves didn't depend on it.
- 2026-10-31 Fix: Rain's wind share 3: a slant of about 17 degrees at the level's wind, about 40 at r_wind 3; it starts upwind so the rain keeps falling round the camera. Leaves' count scales with wind strength (0.1 to 4 times).
