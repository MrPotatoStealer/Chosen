# 20261016-084242: small gap in geo, vertices needs to be merged?

Status: fixed
Regression: geometry

## History

- 2026-10-16 08:42:42 reported in maps/e1m1.bsp
- 2026-10-16 fixed. Cause: Same as 084220: light-effect wall skins offset in front of the wall left a slit at the outside corner.
- 2026-10-16 Fix: Skins lie behind the surface with their face on it.
