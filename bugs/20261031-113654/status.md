# 20261031-113654: hole in geo

Status: fixed
Regression: geometry, stacked-rooms

## History

- 2026-10-31 11:36:54 reported in maps/e1m1.map
- 2026-10-31 fixed. Cause: E1M1's chapel 55 and its neighbour 45 share the ground (SectorMap.Overlaps). The low wall from 55's floor up to 45's counted as 45's for the stacked rooms' walk (own = the next sector), so the walk arriving in 55, a partner of 45, discarded it: the sky showed through. Build draws it whenever it walks 55.
- 2026-10-31 Fix: WorldMesher.EmitWall: an opening's walls count as this sector's own where the two sectors share the ground.
