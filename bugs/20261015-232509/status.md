# 20261015-232509: incorrect texture offset, looks like the offset is from when the secret door is open

Status: fixed
Regression: movers

## History

- 2026-10-15 23:25:09 reported in maps/e1m1.bsp
- 2026-10-15 fixed. Cause: z-mover brushes are built at OFF but wall textures were pegged to the heights the map stores (ON)
- 2026-10-15 Fix: converter uses the OFF heights of z-moving sectors
