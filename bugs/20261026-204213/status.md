# 20261026-204213: still a push at the start of bridge but not on bridge check recording for exact position

Status: fixed

## History

- 2026-10-26 20:42:13 reported in maps/e3m6.map
- 2026-10-26 fixed. Cause: The pan push skipped only a player standing on a sprite; at the bridge's start the player is in the river sector 192 but held up by the bank's floor beside it (Build's getzrange takes neighbouring floors), so the current pushed them into the water
- 2026-10-26 Fix: As Blood (actor.cpp): a panning floor pushes only a sprite whose bottom reaches its own sector's floor at its x, y (getflorzofslope <= bottom), from Build's sector heights
