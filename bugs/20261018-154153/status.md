# 20261018-154153: sprites looks to dark

Status: fixed
Regression: sector-lighting

## History

- 2026-10-18 15:41:53 reported in maps/E1M2.bsp
- 2026-10-18 fixed. Cause: The light grid shaded sprites with the sector's floor shade. Blood (viewProcessSprites) uses the ceiling's shade under a parallax sky: the trees stand on a dark floor (shade 34) under a sky of shade 0.
- 2026-10-18 Fix: Converter writes Blood's sprite shade per sector (sky ceiling shade, else floor shade, plus the surface's tile shade) and bsp_shade fills the light grid with it.
