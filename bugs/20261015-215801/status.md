# 20261015-215801: Another grap in geo

Status: fixed
Regression: geometry

## History

- 2026-10-15 21:58:01 reported in maps/e1m1.bsp
- 2026-10-15 fixed. Cause: a step face beside a sloped floor picked its wall from the floor height at the edge midpoint only; the slope cleared the face at one end, so it stayed caulk (crack)
- 2026-10-15 Fix: overlay_side_tex takes the open range along the whole edge (both ends and midpoint)
