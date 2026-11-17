# 20261031-230439: check if there is something we can do about the seams between water an portal when transparent water is enabled. If its to tricky just skip it

Status: fixed

## History

- 2026-10-31 23:04:39 reported in maps/e2m1.map
- 2026-11-01 fixed. Cause: E2M1's lake bobs (its floor, sector 125 and the others) while the ceiling of the room under it stays: the room's walls stopped short of the surface when it rose, and through see-through water a dark sliver showed along the waterline. The reporter's reading.
- 2026-11-01 Fix: NativeWorld.FollowWater: the ceiling under a level water surface is drawn at the surface (its plane and its portal's), the game's heights untouched. Before and after at the report's camera: the black lines are gone.
