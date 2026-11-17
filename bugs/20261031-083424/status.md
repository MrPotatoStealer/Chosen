# 20261031-083424: moving boat is very jittery

Status: fixed

## History

- 2026-10-31 08:34:24 reported in maps/e2m1.map
- 2026-10-31 fixed. Cause: A moving sector's floor textured relative to its first wall (stat 64) took that wall from the latest game frame while its corners were blended between the last two: the texture slid up to a frame's motion off and snapped back each tick.
- 2026-10-31 Fix: WorldMesher.FlatAxes uses the blended wall (Point). Also: b3d_planes_prev from the board's per-tick snapshot (Board.PrevPlane), not the last frame sent.
