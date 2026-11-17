# 20261026-180824: missing geo

Status: fixed
Regression: geometry, masked-walls

## History

- 2026-10-26 18:08:24 reported in maps/e3m3.map
- 2026-10-26 fixed. Cause: Scrolling walls (x-wall panXVel/panYVel) were left by the sector drawing to the converter's skins; this face's skin was missing (a swapped lower wall), so the room behind showed (E3M3 water wall between 120 and 157).
- 2026-10-26 Fix: The sectors draw scrolling walls themselves, scrolling (converter wpk* keys: the panning wall's scroll; one material per Build wall); the converter's wall skins are hidden with r_sectors 1
