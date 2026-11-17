# 20261031-093142: incorrect geo a piece is seen that should not be visible

Status: fixed
Regression: stacked-rooms, room-over-room

## History

- 2026-10-31 09:31:42 reported in maps/e3m1.map
- 2026-10-31 fixed. Cause: E3M1's shaft 87 lies over the bar 381 (shared space). The floor link of 87 was drawn from the bar, showing the pit 85 under it through the bar's railing: the grey piece by the wall.
- 2026-10-31 Fix: PortalRenderer.StackHidden: a portal on a sector sharing the ground is neither drawn nor black where the walk from the camera's sector hides its face (StackedRooms.SectorFaceHidden).
