# 20261019-143458: small strange geo in the sky here

Status: fixed
Regression: geometry

## History

- 2026-10-19 14:34:58 reported in maps/e1m1.map
- 2026-10-19 fixed. Cause: looking steeply up, the sky shader ran past the panorama's top and its columns fanned into a pole (NBlood can't look that high)
- 2026-10-19 Fix: the panorama's cloud rows continue overhead as a flat, tiled cloud layer
