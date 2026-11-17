# 20261015-230458: strange collision here, I can crouch and walk thru the gap

Status: fixed
Regression: collision
Regression check: the vent openings block when crouching

## History

- 2026-10-15 23:04:58 reported in maps/e1m1.bsp
- 2026-10-15 fixed. Cause: the vent's openings are two-sided walls with cstat 1 (blocking): Build's clipmove stops there, the converter ignored the flag
- 2026-10-15 Fix: converter puts a tools/wallclip slab (playerclip + monsterclip) over every blocking two-sided wall; floor/ceiling checks ignore clip contents
