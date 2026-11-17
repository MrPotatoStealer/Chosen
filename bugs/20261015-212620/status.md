# 20261015-212620: duplicate doors, seems to be overlapping when doors are open.

Status: fixed
Regression: movers

## History

- 2026-10-15 21:26:20 reported in maps/e1m1.bsp
- 2026-10-15 fixed. Cause: Blood stores rotating/sliding whole-sector doors open and closes them at level start; the converter left the open position solid in the static world, so a copy of the door stayed there
- 2026-10-15 Fix: overlay_world adds the area a whole moving sector leaves to the room it opens into (also in the shade data); generic for all 616/617 sectors
