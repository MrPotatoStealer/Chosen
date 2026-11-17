# 20261015-120214: wall have incorrect texture or missing, Its solid but should the the same as the walls next to it.

Status: fixed
Regression: geometry

## History

- 2026-10-15 12:02:14 reported in maps/e1m1.bsp
- 2026-10-15 fixed. Cause: Two converter bugs: (1) walls_on() accepted a wall that only touched the face midpoint with its end (the stair riser, tile 6); (2) collinear vertices were nudged 0.05 outward, giving nearly coplanar brush planes that q3map2 snaps together and then silently drops the brush, so faces went missing or spanned several walls.
- 2026-10-15 Fix: convert_map.py: walls_on() requires parallel walls; convex_pieces() nudges only the triangulation input (inward) and builds brushes from exact coordinates, never merging across straight corners. The opening now visible north of the stair landing is Blood's two-sided wall 263 into the graveyard (sector 133).
