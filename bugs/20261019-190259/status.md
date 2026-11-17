# 20261019-190259: looks incorrect

Status: fixed
Regression: stacked-rooms, geometry

## History

- 2026-10-19 19:02:59 reported in maps/e1m3.map
- 2026-10-19 fixed. Cause: a full-height solid beside stacked rooms took one wall texture for its whole side: the upper corridor's (a red picture), seen from the room below
- 2026-10-19 Fix: converter: such solids are sliced at the stacked rooms' floor/ceiling heights, each slice with its room's wall (stack_splits)
