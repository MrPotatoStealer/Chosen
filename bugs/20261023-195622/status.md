# 20261023-195622: here the coffin texture is not following the lid

Status: fixed
Regression: movers
Regression setup: send s28 on | 2
Regression check: the lid's emblem slides with the lid, whole, not cut by a fixed frame

## History

- 2026-10-23 19:56:22 reported in maps/e1m1.map
- 2026-10-23 fixed. Cause: Live sectors' floors and ceilings kept the texture axes they were built with (world-fixed); the lid (Build sector 27) is relatively aligned (floorstat 64), its picture follows its first wall as that slides
- 2026-10-23 Fix: blood_live floorRelative/ceilRelative (converter); LiveRenderer maps a relative plane's corners back through the first wall's move and turn before its axes
