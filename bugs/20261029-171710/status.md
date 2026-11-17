# 20261029-171710: moving door has some small white dots like the cutting mesh is not perfectly aligned to angle of door

Status: fixed

## History

- 2026-10-29 17:17:10 reported in maps/dwe3m1.map
- 2026-10-29 fixed. Cause: the courtyard's floor is filled in bands whose corners lie along the turning door's foot, not at its ends, and bands split by a corner didn't share theirs: hairline seams showed the white mist
- 2026-10-29 Fix: the moving walls reach a quarter unit past the floor or ceiling they meet (SeamSkirt), the walls of filled sectors too, and each band takes its neighbours' corners along its edges (PolyFill)
