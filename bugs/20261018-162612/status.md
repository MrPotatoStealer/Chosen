# 20261018-162612: sprite looks to dark

Status: fixed
Regression: sector-lighting

## History

- 2026-10-18 16:26:12 reported in maps/E1M2.bsp
- 2026-10-18 fixed. Cause: Sprites took their sector shade from DarkPlaces' light grid (64x64x128 cells); a decal on a wall sampled cells in solid, which default to dark.
- 2026-10-18 Fix: The converter stores Blood's sprite shade for each sprite's sector (secShade); the server sends it while the sprite is where it started or carried by its sector, and the client uses it instead of the light grid.
