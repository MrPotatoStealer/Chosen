# 20261023-084101: the moving sheet does not look correct when it moves to the side. In NBlood it looks like it's moving in sync with the tilting?

Status: fixed
Regression: movers
Regression setup: send s0 on | 2
Regression check: mid-rise: the floe is one tilted floor with side walls; the water strip opens beside it in step, no flat sheet

## History

- 2026-10-23 08:41:01 reported in maps/e2m6.map
- 2026-10-23 fixed. Cause: The floe's floor over the water strip at OFF is a lid skin sliding with the strip; it kept its flat height while the floe rose and tilted, it was the strip's tapered shape, and the floe's align point stayed at the strip's ON edge instead of sliding with it (kCmdLink keeps both in step).
- 2026-10-23 Fix: planeWith: the skin takes the floe's rise and slope (shear about the hinge, less its own slide), is the floe's full area with side walls and no collision; alignMoveWith/alignMove slide the floe's align point with the strip.
