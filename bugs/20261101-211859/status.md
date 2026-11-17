# 20261101-211859: I wonder if there is some way to make the sky blend better when using fog?

Status: fixed

## History

- 2026-11-01 21:18:59 reported in maps/e1m1.map
- 2026-11-01 fixed. Cause: The depth fog and the volumetric veil left the sky alone: far walls faded, the sky behind them did not.
- 2026-11-01 Fix: r_fogsky (Enhanced): part of the volumetric veil over the sky, the fog's colour along the horizon (build_sky_haze). A level's sky colour (BuildSky.SkyTint) tints the light from the sky, the haze and the ground mist: E3M1, E3M5, E6M1 red; grey skies as they were.
