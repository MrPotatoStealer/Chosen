# 20261023-233120: small hole/ missing geo when door is open

Status: fixed
Regression: stacked-rooms, geometry

## History

- 2026-10-23 23:31:20 reported in maps/e1m3.map
- 2026-10-23 fixed. Cause: The stacked-room strips on live sectors' walls were left out where the wall line touched the live region, but the game draws a live wall only where the line a quarter unit behind it lies in the region (LiveRenderer.AddWall): where a door panel rests against the wall (doors 135, 159) neither drew the band over the room below, a hole by the open door
- 2026-10-23 Fix: convert_map tests the same quarter-unit line behind the wall against the region
