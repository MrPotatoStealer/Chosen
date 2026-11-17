# 20261026-143338: sky looks strange

Status: fixed
Regression: geometry, stacked-rooms
Regression setup: nomonsters | 0

## History

- 2026-10-26 14:33:38 reported in maps/e3m2.map
- 2026-10-26 fixed. Cause: Sky sector 293 (floor and ceiling at 284, both sky) lies in room 283's space: the overlay built it there, a patch of sky over a thin slab at 283.5 (the nodraw the aim hit)
- 2026-10-26 Fix: overlay_world: a sector shut for good (no height, nothing opening it) holds no space in a cell with an open room
