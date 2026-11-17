# 20261015-232356: Not sure what this gray thing is but it should not be here

Status: fixed
Regression: stacked-rooms, movers

## History

- 2026-10-15 23:23:56 reported in maps/e1m1.bsp
- 2026-10-15 fixed. Cause: a sliding panel of sector 87 (upper floor) was built from the bottom to the top of the map and stood in sector 54 below
- 2026-10-15 Fix: moving solids end at the heights of the rooms along their walls (walls_range)
