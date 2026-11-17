# 20261028-123952: performance here is not great, look into it

Status: fixed
Regression: performance
Regression check: walk the start area of DWE1M3 with the overlay on: no drops below 140 fps from the looping raft floors

## History

- 2026-10-28 12:39:52 reported in maps/dwe1m3.map
- 2026-10-28 fixed. Cause: A dozen looping floors with the Drag flag (z-motion, origin at the world's 0,0) gathered riders from the world origin out to their outline: most of the level's sprites every frame, de-duplicated with List.Contains; busy sectors 30 ms a frame
- 2026-10-28 Fix: MoverPlace: a dragging sector that doesn't turn gathers around its outline box, de-duplicated with a set; the replay's game time per frame 34.7 -> 3.6 ms
