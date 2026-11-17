# 20261023-114350: falling down the grave on start, can't get out

Status: fixed

## History

- 2026-10-23 11:43:50 reported in maps/e1m1.map
- 2026-10-23 fixed. Cause: A sprite placed where its sector has no area yet (the start in the grave that opens as the level begins) was in a live group's void and fell through to the crypt below.
- 2026-10-23 Fix: World.HoldLiveSprites: such a sprite stays where it was placed until a live sector opens there, then stands on its floor (released when warped or pushed).
