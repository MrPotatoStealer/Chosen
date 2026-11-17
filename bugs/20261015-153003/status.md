# 20261015-153003: Incorrect texture, should be the hedge texture, same as next to it

Status: fixed
Regression: movers

## History

- 2026-10-15 15:30:03 reported in maps/e1m1.bsp
- 2026-10-15 fixed. Cause: Sector 6 is a secret slab door (floor lowers when pushed). Blood draws its face with the graveyard wall's picture (wall 862, tile 329 hedge); the converter picked the slab's own wall (tile 421 grey brick) because, with movers counting their full travel, walls on both sides qualified.
- 2026-10-15 Fix: overlay_side_tex only accepts walls whose sector lies on the side the face points into.
