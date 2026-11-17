# 20261015-152927: Missing texture, can see to the other side, should be the same as the walls next to it.

Status: fixed
Regression: geometry

## History

- 2026-10-15 15:29:27 reported in maps/e1m1.bsp
- 2026-10-15 fixed. Cause: earcut drew a brush edge straight through a corner lying exactly on the line between two other vertices (stair landing at x=-208, y=-2336), so one brush face covered both a wall of sector 36 and solid space and got the wrong texture or none.
- 2026-10-15 Fix: convert_map.py: triangulation is now GEOS constrained Delaunay (shapely.constrained_delaunay_triangles), which never draws edges through vertices; earcut is only a fallback.
