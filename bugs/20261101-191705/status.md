# 20261101-191705: starting e1m6 and you die instantly, think you are spawning in the wrong sectors since its a room in room

Status: fixed
Regression: collision
Regression check: start E1M6: in the tunnel, not dropped among cultists; level start, not this report's spot

## History

- 2026-11-01 19:17:05 reported in maps/e1m6.map
- 2026-11-01 fixed. Cause: A start marker kept only its place, and the player's sector was looked up from it: E1M6's start room lies among overlapping rooms, and the lookup took another, dropping the player among its cultists.
- 2026-11-01 Fix: StartSpot keeps the marker's Build sector; PlayerStart puts the player in it (SetSpriteBSector), as Blood does. E1M6 starts in sector 198, health 100 after 5 s with monsters on.
