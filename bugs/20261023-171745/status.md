# 20261023-171745: ignore previous bug report issues is with collision on the train I cant get on it

Status: fixed
Regression: collision
Regression setup: send 111 | 16
Regression check: step from the platform onto the train car and walk across it

## History

- 2026-10-23 17:17:45 reported in maps/e1m2.map
- 2026-10-23 fixed. Cause: The train cars (whole sliding sectors 7-10) move through the live region of the track sector they sit in (33, live as their frame drags its walls); the region's area no live sector covers was all void (solid, floor to ceiling), the cars' spots included, and a flat car's floor (level with the track's) was never in its mover entity: the static world under the track had it, and the region took that away.
- 2026-10-23 Fix: The sweep leaves each rigid mover's area now (its .sec outline where it is) out of the void (LiveCluster.Rigid); the converter runs a rigid mover's floor and ceiling solids to the map's bottom and top where it passes through a live region.
