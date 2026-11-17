# 20261015-230252: strange doors that should not be here?

Status: fixed
Regression: stacked-rooms, movers

## History

- 2026-10-15 23:02:52 reported in maps/e1m1.bsp
- 2026-10-15 fixed. Cause: whole moving door sectors were built as columns from the bottom to the top of the map, so the funeral home doors also stood in the room stacked above them
- 2026-10-15 Fix: door solids end at the heights of the rooms around them (whole_mover_range)
