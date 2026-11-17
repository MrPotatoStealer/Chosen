# 20261018-154642: collision still exist after blowing up the ground here

Status: fixed
Regression: room-over-room, collision
Regression check: blow up the ground: no invisible floor left

## History

- 2026-10-18 15:46:42 reported in maps/E1M2.bsp
- 2026-10-18 fixed. Cause: Sectors 189 and 184 share the same space (link offset 4 -4 0). The ceiling side's slab (z 396-397) lies just above the floor side's plane, so the floor search from the upper room stopped on it; the continuation trace below the floor link also started inside the floor side's own slab.
- 2026-10-18 Fix: Floor searches only see floor link sides and ceiling searches only ceiling sides; the side just passed is left out of the continuation. Verified: the player drops into the room below.
