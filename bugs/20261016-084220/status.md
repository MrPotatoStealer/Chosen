# 20261016-084220: small gap in geo

Status: fixed
Regression: geometry

## History

- 2026-10-16 08:42:20 reported in maps/e1m1.bsp
- 2026-10-16 fixed. Cause: Light-effect skins sat 0.05 in front of each wall; at outside corners they didn't meet and the caulked world face behind showed the void.
- 2026-10-16 Fix: Skins (walls, floors, ceilings) lie behind the surface with their face exactly on it.
