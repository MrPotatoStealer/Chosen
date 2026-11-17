# 20261018-161128: can't blow up this wall

Status: fixed
Regression: movers
Regression check: explosives at the wall crack open the wall

## History

- 2026-10-18 16:11:28 reported in maps/E1M2.bsp
- 2026-10-18 fixed. Cause: The wall is a closed z-motion door (sector 191, channel 146) opened by destroying the wall crack decal (thing 408) on it, which only explosions damage. Radius damage checks line of sight with cansee; the crack lies exactly on the wall face, so the trace from the explosion stopped on the face just short of it and the crack was never damaged. Build's cansee only blocks on walls the segment crosses, so an end on a wall is seen.
- 2026-10-18 Fix: qcansee accepts a trace that stops within a unit of either end (traced both ways). Verified with the recording: the replay now breaks the crack and the wall opens.
