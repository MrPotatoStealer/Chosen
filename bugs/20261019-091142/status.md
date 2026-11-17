# 20261019-091142: portal is not rendering correctly

Status: fixed
Regression: room-over-room
Regression setup: send s30 on | 4

## History

- 2026-10-19 09:11:42 reported in maps/e1m1.map
- 2026-10-19 fixed. Cause: the Godot port drew no room-over-room portals: the level shading dropped every tools/ face, tools/portal included
- 2026-10-19 Fix: PortalRenderer: up to 4 portal cameras (eye + link offset) render into SubViewports the portal faces sample at their screen position; the world, sky, sprite and voxel shaders clip at the linked plane for the camera whose render-layer bit they see (Godot cameras have no clip plane)
