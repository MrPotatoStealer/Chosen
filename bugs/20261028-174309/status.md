# 20261028-174309: missing wall

Status: fixed
Regression: geometry

## History

- 2026-10-28 17:43:09 reported in maps/mrw01.map
- 2026-10-28 fixed. Cause: Sector 59 has only two walls (on one line, facing each way, no area): the converter skipped sectors under three walls, so its one-sided wall 589, which Build draws through the portal of wall 635, was never built (collision had it)
- 2026-10-28 Fix: The converter keeps two-wall sectors in the sectors data (wallnum < 2 skipped): their walls draw; MRW01, MRW03/05/06/08/09, MRW2M5, DWE1M6, E6M7 reconverted
