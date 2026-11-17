# 20261101-191348: when flying and outside any sector then show all sectors to make it easier to navigate when flying around

Status: fixed

## History

- 2026-11-01 19:13:48 reported in maps/e3m5.map
- 2026-11-01 fixed. Cause: Flying, the camera outside every sector was walked from the last sector it was in, so only what that sector reaches was shown.
- 2026-11-01 Fix: Game: flying with no sector holding the camera, the walk has no start and shows all of the level (SectorVisibility.All).
