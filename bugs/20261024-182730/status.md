# 20261024-182730: slight gap

Status: fixed
Regression: sprites, geometry
Regression setup: warp 2237.1 -2271.5 -32.0 87.7 -27.5 43; nomonsters | 2
Regression check: no dotted line across the doorway's ceiling

## History

- 2026-10-24 18:27:30 reported in maps/e2m3.map
- 2026-10-24 fixed. Cause: The ceiling over the doorway's far half is a floor sprite (tile 140, z 80) edge to edge with sector 2's ceiling at 80; sector 5 above it has a sky ceiling. The sprite shader lifted wall/floor sprites toward the camera (about half a unit there), opening a slit at the seam through which the sky showed.
- 2026-10-24 Fix: The sprite shader keeps the sprite where Build puts it and takes only its depth from the lifted position (DEPTH from the lifted point), so it still wins against its own surface without moving off it.
