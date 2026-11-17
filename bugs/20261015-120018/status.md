# 20261015-120018: walltexture is darker then the nearby walls.

Status: fixed

## History

- 2026-10-15 12:00:18 reported in maps/e1m1.bsp
- 2026-10-15 note: Probably as in Blood: this wall (wall 4, sector 0's upper wall to the secret crawlspace) uses the darker stone tile 110 and Build shade 28, while the crypt walls around it are shade ~12. Converter shading matches the Build values (1 - shade/64). Left open for confirmation in game.
- 2026-10-15 fixed. Cause: Not Blood's data: the wall's own shade is 28 like its neighbours. bsp_shade.py computed each triangle's facing from the vertex winding, which in Q3 BSPs is clockwise, so the normal pointed backwards: this face took the shade of the crawlspace wall behind it (50), floors took ceiling shades, and many one-sided walls matched nothing and got a default (about 1860 triangles).
- 2026-10-15 Fix: bsp_shade.py uses q3map2's stored vertex normals. Unmatched triangles 1860 -> 276; panel measures shade ~26 (Build 28), same as the walls beside it.
