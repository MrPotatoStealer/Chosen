# 20261019-210045: can see things thru the ceiling

Status: fixed
Regression: stacked-rooms, movers

## History

- 2026-10-19 21:00:45 reported in maps/e1m3.map
- 2026-10-19 fixed. Cause: Stacked rooms overlapping in height (31 under the corridors 155-161): the upper corridor's sprites and its sliding door (sector 159, z 32-128) reached below the lower room's ceiling (56) and showed through it.
- 2026-10-19 Fix: New StackedRooms: the camera's side is the room nearer (in steps through two-sided walls) to its sector. Faces of the shared heights are tagged NNNN_v<own>_<other> by the converter and hidden from the other side; sprites in the overlap belong to the upper room from its floor up; movers of stacked sectors are hidden from the other side too.
