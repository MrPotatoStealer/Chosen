# 20261019-205912: performance issue looking this way

Status: fixed
Regression: room-over-room, performance
Regression check: perf looking this way (portal behind walls not drawn)

## History

- 2026-10-19 20:59:12 reported in maps/e1m3.map
- 2026-10-19 fixed. Cause: A room-over-room portal far behind walls (the cellar ceiling portal, rooms away) was rendered whenever it was inside the view cone: a second full view of the level, without culling (1.35M primitives here). Nothing hidden behind walls was culled either.
- 2026-10-19 Fix: Portals are drawn only when a line from the eye reaches their face (static world traces, 9 points; always when near). Godot occlusion culling added with the level's opaque static faces as occluder (not in portal cameras). Report view: 1525 -> 196 draw calls, 1.35M -> 17K primitives.
