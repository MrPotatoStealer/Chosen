# 20261028-123008: minor missing hole in geo herwe

Status: fixed
Regression: stacked-rooms

## History

- 2026-10-28 12:30:08 reported in maps/dwe1m1.map
- 2026-10-28 fixed. Cause: Sector 236 is one of the rooms sharing space (layered); the floor's pixels within a unit of its edge with 239 (another layer's sector, beside it, not over it) stepped back toward the camera into 239, and the layer test for sector faces (added for E4M2's shaft strip) hid them: a strip seen through to the sky
- 2026-10-28 Fix: StackedRooms: a sector face is hidden for the layer only when the walk to the pixel itself also ends in another layer
