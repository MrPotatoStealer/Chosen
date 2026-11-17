# 20261015-212742: portals have small gap on don't align perfectly

Status: fixed
Regression: room-over-room

## History

- 2026-10-15 21:27:42 reported in maps/e1m1.bsp
- 2026-10-15 fixed. Cause: the portal face sat 0.25 units above the link plane, so at grazing angles the slit between the floor edge and the portal showed the void under it
- 2026-10-15 Fix: portal face now lies exactly on the plane; a 1x1 invisible stub keeps the entity's bounds inside the room for culling
