# 20261024-223841: hole in geo

Status: fixed
Regression: movers, stacked-rooms, geometry

## History

- 2026-10-24 22:38:41 reported in maps/e1m5.map
- 2026-10-24 fixed. Cause: Sector 205 is a sloped hatch in the barrel room's wall whose floor drops 23 units when opened; room 131 overlaps it in height, so the converter kept its moving floor to a 0.5-unit slab. The sectors leave a moving floor's lower walls to its mover, so nothing drew the wall under the closed hatch: a hole into room 131.
- 2026-10-24 Fix: convert_map: a moving floor kept to a thin slab gets the walls under it (its neighbours' lower walls, down to their lowest floor) as a brush without collision hanging from the slab and moving with it.
