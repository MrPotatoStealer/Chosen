# 20261029-202850: missing geo when train comes in, only when moving in some areas

Status: fixed
Regression: movers
Regression setup: send 111 | 8.2
Regression check: train mid-arrival: the car's side whole between the pillars, no stone strip beside the cart

## History

- 2026-10-29 20:28:50 reported in maps/e1m2.map
- 2026-10-29 fixed. Cause: a live tile was rebuilt only when its pieces (the group's edges cut to it) or planes changed, but it draws the walls whose middle lies in it whole: E1M2's train car is all solid across one tile, so its pieces stayed the same while the car's side wall slid through, and the tile kept the wall where it had been; beside the cart a stretch of the car's side was missing (the far stone wall showed)
- 2026-10-29 Fix: DrawKey adds the ends (at the game frame) of the walls the tile draws; GroupSeen counts a seen sector's walls drawn by the tile, for a tile with no piece of its own
