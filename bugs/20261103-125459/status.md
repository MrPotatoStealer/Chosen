# 20261103-125459: strange 1 frame here when I move between sectors and the visibility seems to be incorrect causing a fast flicker for 1 frame.

Status: fixed

## History

- 2026-11-03 12:54:59 reported in maps/test.map
- 2026-11-03 fixed. Cause: The eye's sector between game frames was found from the player's sector by the first neighbour holding the eye: where two sectors share the place at the same heights (test.map's 0 and 1) stepping back over the wall from 1 into 2 walked one frame from 0
- 2026-11-03 Fix: World.SectorFrom follows the red walls the line from the player to the eye crosses (then the old lookup); the burst over the recording now goes 1 to 2 directly
