# 20261029-125840: shore is white when riding boat

Status: fixed
Regression: movers, stacked-rooms

## History

- 2026-10-29 12:58:40 reported in maps/dwe3m1.map
- 2026-10-29 fixed. Cause: the stacked-rooms walk (walls as stored) started from the boat's sector while it was away from its stored place: walked from the stored outline back at the dock, everything outside what lay through it was hidden
- 2026-10-29 Fix: the walk starts from the room around the camera as stored (SectorMap.StoredAt) while a moving sector's stored outline doesn't hold it
