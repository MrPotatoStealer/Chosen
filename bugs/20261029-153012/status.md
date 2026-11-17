# 20261029-153012: missing ground when opening door

Status: fixed
Regression: movers

## History

- 2026-10-29 15:30:12 reported in maps/dwe3m1.map
- 2026-10-29 fixed. Cause: the courtyard's floor (251) is filled from its loops as the door turns; its corners were moved by vertex, and the door's slab rests on the courtyard's outline, so the outline's corners there turned with the door and cut a wedge off the floor
- 2026-10-29 Fix: each loop's corners move with a carried wall beside them in that loop (CarriedFlat; Carried by edge), as Build's dragpoint follows only two-sided walls
