# 20261018-154403: I opened the secret but the collision is preventing me from getting the healthpack

Status: fixed
Regression: movers, collision
Regression check: open the secret: nothing blocks the health pack

## History

- 2026-10-18 15:44:03 reported in maps/E1M2.bsp
- 2026-10-18 fixed. Cause: The push trigger of the secret (wall 2393, the face of the raised block) was a static slab sized at the block's raised height: after the block lowered it stayed in the opening.
- 2026-10-18 Fix: The part of an x-wall that a z-motion neighbour's floor/ceiling shows is a wall proxy the mover forwards pushes to (pushwall), like door panels. Verified: the player reaches the health pack.
