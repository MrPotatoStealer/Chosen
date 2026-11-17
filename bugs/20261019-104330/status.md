# 20261019-104330: secondary fire on weapon causing massive stutter

Status: fixed
Regression: performance
Regression check: alternate fire: no stutter (perf)

## History

- 2026-10-19 10:43:30 reported in maps/e1m1.map
- 2026-10-19 fixed. Cause: same as 20261019-104238: alternate fire sprays many 16384-unit hitscans per frame, each scanning every grid cell of its bounding box in all 56 brush entities
- 2026-10-19 Fix: see 20261019-104238
