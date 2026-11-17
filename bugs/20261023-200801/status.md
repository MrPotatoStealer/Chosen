# 20261023-200801: some incorrect geo at top of closet door

Status: fixed
Regression: geometry, movers, stacked-rooms
Regression check: no beams across the ceiling or wooden wall beside the louvered closet door

## History

- 2026-10-23 20:08:01 reported in maps/e1m3.map
- 2026-10-23 fixed. Cause: Room 125's turning panels (122-124, whole movers): their area wasn't given to the room because the upper corridor 140 overlaps 125's height by 8 units (counted as a room beside it), so it stayed static solid at the map place; and the band strips of walls facing them were static
- 2026-10-23 Fix: convert_map: overlaps only in a band don't keep a whole mover's area solid; blockers skip walls whose other side is a whole mover
