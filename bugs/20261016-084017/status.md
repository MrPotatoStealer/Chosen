# 20261016-084017: jumping next to the gate (sprite) here and you are teleported to other side of the sprite in the air for a few frames

Status: fixed
Regression: room-over-room, collision
Regression check: jump beside the gate: the view stays on this side

## History

- 2026-10-16 08:40:17 reported in maps/e1m1.bsp
- 2026-10-16 fixed. Cause: The client moved the eye through any link plane it was just beyond: standing under sector 90's floor link (in sector 65) applied that link's offset, putting the view on the other side of the gate during a jump.
- 2026-10-16 Fix: linkViewOrigin only takes links of the body's own room (body inside the footprint, on the near side), like CheckLink on the camera's sector in viewDrawScreen.
