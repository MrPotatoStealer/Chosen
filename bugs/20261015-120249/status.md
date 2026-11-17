# 20261015-120249: wall is missing texture, I can see through the wall. Collision seems to be correct

Status: fixed
Regression: geometry

## History

- 2026-10-15 12:02:49 reported in maps/e1m1.bsp
- 2026-10-15 fixed. Cause: Brush 2184 (solid next to the graveyard hedge) had nearly coplanar faces from the 0.05 collinear-vertex nudge; q3map2 dropped it, leaving a hole showing the sky.
- 2026-10-15 Fix: Same fix as 20261015-120214 (exact brush coordinates, no near-coplanar planes).
