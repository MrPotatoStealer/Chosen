# 20261031-143341: hole in geo

Status: fixed
Regression: geometry, stacked-rooms

## History

- 2026-10-31 14:33:41 reported in maps/e1m1.map
- 2026-10-31 fixed. Cause: The same pair as 20261031-113654: ledge 45 beside chapel 55, which share the ground. The stacked rooms' walk takes a step of one unit back toward the camera from each pixel: along the ledge's edge that step lands in 55, a partner of 45, so a thin line of the ledge's floor was discarded and the sky showed.
- 2026-10-31 Fix: StackedRooms stack_discard: before dropping a face because the walk arrived in a partner, walk to the pixel itself: in its own sector it stays.
