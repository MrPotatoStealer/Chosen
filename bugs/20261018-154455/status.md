# 20261018-154455: part of train sector is not moving

Status: fixed
Regression: movers
Regression check: call the train: the whole middle car arrives

## History

- 2026-10-18 15:44:55 reported in maps/E1M2.bsp
- 2026-10-18 fixed. Cause: The middle station car's outline is partly sector 33's marked walls, partly the walls of the car's own moving sectors (7, 8, 9), which Build drags along (DragPoint). The converter only counted marked walls, so the car body stayed static at the start position (and its front block became a separate panel).
- 2026-10-18 Fix: Walls shared with a whole moving sector on the same markers count as moving; moving outlines of different sectors match by travel (pivot only for rotations). The car body is one moving part with the car. Verified: the arriving car has its walls.
