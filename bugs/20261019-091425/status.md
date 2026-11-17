# 20261019-091425: another portal bug where it does not align correctly

Status: fixed
Regression: stacked-rooms, carried-sprites

## History

- 2026-10-19 09:14:25 reported in maps/e1m1.map
- 2026-10-19 fixed. Cause: not a portal: the funeral home fire sprite waiting under its sector's floor was drawn (homeLo/homeHi not ported), and voxels (skull spikes, gate) were invisible
- 2026-10-19 Fix: Sprite.OutsideHome hides sprites wholly outside their sector's open range like net.qc; the MD3 reader read the surface header 4 bytes off
