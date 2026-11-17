# 20261101-221212: not following the iceberg, check with NBlood if we should be when standing on it? Also didn't fall down once the iceberg was gone from under me.

Status: fixed
Regression: collision
Regression setup: b3d_fixblood 1 | 3
Regression check: from the spot the player falls straight into the water (b3d_fixblood), not left standing in the air where the iceberg was

## History

- 2026-11-01 22:12:12 reported in maps/e2m1.map
- 2026-11-01 fixed. Cause: Not a bug, as NBlood's source reads: the level gives this iceberg (sector 114, E2M1, no patch) no Drag flag, unlike its neighbours 91-93, and TranslateSector moves a sprite on a sector only with Drag. MoveDude changes a sprite's sector only when it moves, and its floor is its sector's, so a player standing still stays up at that height until moving.
- 2026-11-01 Fix: No change. Walk test: stands where it was with no velocity while the iceberg leaves, falls on the first step.
- 2026-11-02 fixed. Cause: Blood's own bug (NBlood's MoveDude the same): a dude's sector changes only as it moves, so the iceberg (114, no Drag: it never carries you, as in Blood) moving out from under a player standing still left them in its sector, held up by its floor plane extended, until their first step. The recording: a jump onto it as it glided past, then 260 frames in the air.
- 2026-11-02 Fix: b3d_fixblood (Game options, FIX BLOOD'S BUGS, on): MoveDude looks up the sector holding a dude standing still too, so it falls at once. Recordings carry it (older: off). Not carrying you is left as Blood has it.
