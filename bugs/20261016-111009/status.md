# 20261016-111009: sprite is in wrong location when seen thru a portal, you can see other sprites liek spiderwebs also shifting location

Status: fixed
Regression: room-over-room, sprites
Regression setup: send s30 on | 4

## History

- 2026-10-16 11:10:09 reported in maps/E1M1.bsp
- 2026-10-16 fixed. Cause: Sprites were oriented (rotation frame, wall-sprite side) from the real camera; seen through a portal they are rendered from the virtual camera (eye + link offset), so wall sprites were lifted into their wall and frames were wrong.
- 2026-10-16 Fix: Each sprite uses the camera that sees it: the real one or the virtual camera of a portal in the eye's room, whichever is nearer.
