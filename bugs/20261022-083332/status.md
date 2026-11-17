# 20261022-083332: missing swimming volume here

Status: fixed
Regression: water
Regression check: the view is under water (blue tint): the player swims here

## History

- 2026-10-22 08:33:32 reported in maps/e2m9.map
- 2026-10-22 fixed. Cause: Sector 151 (and 23 more flooded channels in E2M9, a few in E2M1/E2M3/E2M6/E2M7/E3M2/E3M3/E3M6) is an underwater sliding/turning sector: its entity holds only the moving parts, and the water test skipped movers without a z range, so it had no water volume
- 2026-10-22 Fix: The converter gives underwater sliding/turning sectors sOpen (z range over their corners); World.InUnderwaterSector and SectorAtPoint take their area from the sector outline
