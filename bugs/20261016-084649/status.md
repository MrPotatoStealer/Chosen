# 20261016-084649: also can't jump thru the window

Status: fixed
Regression: masked-walls, collision
Regression check: break the window and jump through it

## History

- 2026-10-16 08:46:49 reported in maps/e1m1.bsp
- 2026-10-16 fixed. Cause: Same as 084631: the blocking-wall clip slab over the broken glass stopped the player.
- 2026-10-16 Fix: Blocking-wall slabs skip breakable glass.
