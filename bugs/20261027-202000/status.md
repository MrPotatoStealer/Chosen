# 20261027-202000: broken window but collision is still there

Status: fixed
Regression: collision
Regression check: break the window: then you can walk through (Build's collision clears its flags)

## History

- 2026-10-27 20:20:00 reported in maps/e4m1.map
- 2026-10-27 fixed. Cause: breaking a gib wall hid its brushes, but Build's collision (phys_sectors 1) reads the wall flags from the sector map, which kept the blocking bit
- 2026-10-27 Fix: OperateWall runs SetupGibWallState as NBlood: broken clears cstat 1/64 (and 16) on both sides, whole sets them; the map's flags are reset on level load
