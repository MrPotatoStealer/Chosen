# 20261101-151653: hole in geo here now

Status: fixed
Regression: room-over-room
Regression setup: send s30 on | 4
Regression check: the chapel's ceiling whole from under the coffin's link, no triangle of sky at the crosshair

## History

- 2026-11-01 15:16:53 reported in maps/e1m1.map
- 2026-11-01 fixed. Cause: With r_skyfaces 0 a portal copy is cut by the walk, and the walk through a link measured the far room's walls from the real eye, not the eye as the far room sees it, so it stopped short (sector 0 from under link 10).
- 2026-11-01 Fix: SectorVisibility: each sector reached through links keeps the offset to the eye and its walls are measured from there. Hole gone at the report.
