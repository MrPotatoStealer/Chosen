# 20261101-122121: moving sky looks mostly buggy

Status: fixed

## History

- 2026-11-01 12:21:21 reported in maps/e1m1.map
- 2026-11-01 fixed. Cause: r_skydrift mixed in a moved copy of the panorama by how grey both pixels were, so grey and dark parts of the trees moved and the outlines were eaten.
- 2026-11-01 Fix: BuildSky.BuildClouds: a mask of what stands (big patches of colour, dark parts touching them, the moon, under the horizon) and a cloud picture continuing behind it; only clear sky moves, behind them. Checked at 2, 12, 30 and 60 s in stills.
