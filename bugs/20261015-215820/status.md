# 20261015-215820: Duplicate door bug is back

Status: fixed
Regression: stacked-rooms, movers

## History

- 2026-10-15 21:58:20 reported in maps/e1m1.bsp
- 2026-10-15 fixed. Cause: the previous fix subtracted every other sector's footprint from the freed door area, including sector 59 stacked far above, so the door 51 area was removed again
- 2026-10-15 Fix: only subtract sectors whose height range overlaps the room's
