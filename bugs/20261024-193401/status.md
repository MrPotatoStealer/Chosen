# 20261024-193401: this room is not correct, collision in the way so can't even enter

Status: fixed
Regression: geometry, collision
Regression setup: send s210 on | 3
Regression check: walk through the double door into the hall: floor, ceiling and pillars as in NBlood, nothing blocking

## History

- 2026-10-24 19:34:01 reported in maps/e1m5.map
- 2026-10-24 fixed. Cause: Hall 44's outer wall loop is not contiguous in wall order (walls 209-213 and 229-246, its three pillar loops 214-228 between). The converter's loops() stepped by loop length, so it walked the outer loop twice and never the pillars: the sector got no floor/ceiling pieces (not drawn) and its footprint and collision were wrong.
- 2026-10-24 Fix: loops() walks each loop from the first wall not yet visited (Build only follows point2). Affected 13 sectors on E1M3, E1M4, E1M5, E1M6, E2M5, E2M9; all reconverted.
