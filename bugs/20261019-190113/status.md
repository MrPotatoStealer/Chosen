# 20261019-190113: opening this vent caude massive stutter

Status: fixed
Regression: movers, performance
Regression check: open the vent: no stutter (perf)

## History

- 2026-10-19 19:01:13 reported in maps/e1m3.map
- 2026-10-19 fixed. Cause: the vent is a hinged grill (615, rotOn -90) whose brush entity has only a nodraw driver brush and an origin brush; collision skipped entities without solid brushes, so the mover had no model and its pivot was the world origin: while turned, its bounds reached across the map and every game frame tested ~700 sprites as riders (38 ms)
- 2026-10-19 Fix: collision keeps a model with a pivot even when nothing is solid (3 ms while moving)
