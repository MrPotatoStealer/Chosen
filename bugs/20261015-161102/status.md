# 20261015-161102: window should be a bit transparent

Status: fixed
Regression: masked-walls

## History

- 2026-10-15 16:11:02 reported in maps/e1m1.bsp
- 2026-10-15 fixed. Cause: Masked walls ignored Build's translucency bits (wall cstat 128, +512 = more see-through); the window (wall 180, overpicnum 263, cstat 733) was drawn opaque. Also all masked-wall slabs used nonsolid nodraw faces, so blocking masked walls (cstat 1/64) could be walked and shot through.
- 2026-10-15 Fix: pal_textures now also makes translucent masked variants (blood_masked/NNNN[_pP]_tT, opacity 0.66 / 0.33 baked into alpha, alpha-blend shader); converter picks the level from cstat 128/512. Masked walls with cstat 1 or 64 get solid caulk hidden faces. check_map skips masked slabs (their front face is two-sided). Generic for all maps.
