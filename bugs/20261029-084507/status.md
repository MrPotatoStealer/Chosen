# 20261029-084507: missing fog/mist here

Status: fixed

## History

- 2026-10-29 08:45:07 reported in maps/dwe3m1.map
- 2026-10-29 fixed. Cause: palette 1 (SATURATE) shades toward white; NBlood's Polymost fogs it to white (palookupfog[1]), the game darkened it
- 2026-10-29 Fix: palette-1 sectors' faces, sprites and sky whiten with shade and distance (build_saturate); the converter writes sector palettes (fpal, cpal)
