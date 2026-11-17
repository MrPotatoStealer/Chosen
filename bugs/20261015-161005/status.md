# 20261015-161005: coffing lid is not closed at start

Status: fixed
Regression: movers

## History

- 2026-10-15 16:10:05 reported in maps/e1m1.bsp
- 2026-10-15 fixed. Cause: Slide-marked sectors (type 614) are stored in their ON shape; Blood's trInit moves the marked edge back to OFF, so at level start the neighbour sector covers the area (the coffin lid). The converter kept these deforming edges static/open.
- 2026-10-15 Fix: Converter: a marked edge shared with a static neighbour becomes a lid: the neighbour's solid (higher floor / lower ceiling) over the swept area is built at OFF as a mover part that slides with the edge (textures: neighbour's floor/ceiling, step walls, linked room's lid neighbour for faces seen through room-over-room). Slide/rotate-marked sectors also get their floor/ceiling Z motion as extra parts (HDoorBusy calls ZTranslateSector). Generic for all maps.
