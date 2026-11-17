# 20261027-202304: tiny strip of geo hanging here right where crosshair is

Status: fixed
Regression: stacked-rooms

## History

- 2026-10-27 20:23:04 reported in maps/e4m2.map
- 2026-10-28 fixed. Cause: Sector 8 (one of the rooms sharing space, layer 0) has its wall corner on the shaft 170's edge; sector-drawn faces had no layer in stack_discard, and the pixel's step back landed in 170, which shares no ground with 8, so the corner stayed: a strip. The round window is the shaft's own ceiling, seen because the camera really pitches (Build shears)
- 2026-10-28 Fix: StackedRooms: stack_walls' layer texels carry whether a sector is layered; a sector face takes its own sector's layer and is dropped where the walk arrives in another layer
