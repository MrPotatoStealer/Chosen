# 20261019-102445: portal rendering is strange when getting close to the portal when jumping for a frame or so, just as camera is between I guess?

Status: fixed
Regression: room-over-room
Regression setup: send s30 on | 4
Regression check: jump under the stairwell portal: no frame drawn from inside the ceiling

## History

- 2026-10-19 10:24:45 reported in maps/e1m1.map
- 2026-10-19 fixed. Cause: the Godot camera lacked the DarkPlaces client's link handling: jumping through the stairwell's ceiling portal put the eye above the plane before the body crossed (drawn from inside the solid ceiling), the near plane clipped the portal face just before, and interpolation ran between the two rooms (a frame drawn halfway between them)
- 2026-10-19 Fix: PlayerView ports viewClampHeight, linkViewOrigin (an eye beyond a link plane views from the other room) and linkViewNear (near clip 0.03 close to a link plane); sprites and the camera interpolate across a link in the new room (CollisionWorld.Interpolate, the client's linkCrossOffset)
