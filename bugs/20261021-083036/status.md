# 20261021-083036: this does not look correct? Compare to NBlood

Status: fixed
Regression: movers, sector-lighting
Regression check: the corridor strips pinch in with slanted walls for the first seconds, then open; walls pulse with the light like floor and ceiling

## History

- 2026-10-21 08:30:36 reported in maps/e1m8.map
- 2026-10-21 fixed. Cause: E1M8's pulsing corridor: slide-marked strips (614) pinch in 16 units at OFF and open on level start (toggle switches on channel 7). TranslateSector's DragPoint also drags the neighbour strips' walls that share the moving vertices, but the converter's sweep panels only stretched the sector's own one-sided walls, so the pinch showed as rectangular notches; the stretch walls and the panels' fronts took no wall light (shadeWalls), so the corridor stayed dark while NBlood's pulses; the panel fronts took the side wall's texture across them (streaks).
- 2026-10-21 Fix: convert_map.py: sweeps run lid_stretch for the walls of other sectors at their moving vertices; sweep panel fronts take their own wall's texture at OFF; stretch walls carry swFxSector (their room's wall light, StretchRenderer adds FxShade); sweep sectors with shadeWalls get fxWalls and MoverRenderer lights their own side faces.
