# 20261105-095023: I fell from wall and ended up in the wrong sector room-over-room issue

Status: fixed

## History

- 2026-11-05 09:50:23 reported in maps/test.map
- 2026-11-05 note: Replayed (inputcheck, sect now traced): at frame 328 the player steps off the rotated sector 8 (rotate type 617, on) while its centre is over 8 but the clip move had only sector 0 in its list (the red wall's clip line was already behind the player standing on 8's edge); clipmove_compat then searches every sector from the last and takes 16, which overlaps sector 0 at the same heights (test.mapx: the room 2304..9216 x -5888..3072 inside the yard). NBlood's clipmove_compat does the same (clip.cpp): a map with two rooms over the same ground at the same heights. Waiting on the user: a Forge warning and/or a b3d_fixblood deviation.
- 2026-11-05 fixed. Cause: Standing on a turning platform's edge from the yard, the platform turned under the player's middle: its walls then face away from the move and are never looked at, so clipmove_compat found no sector it looked at holding the player and searched every sector from the last, taking room 16, which shares the yard's ground at the same heights
- 2026-11-05 Fix: b3d_fixblood: clipmove_compat's fallback searches in Blood's FindSector order (the start sector, its neighbours, then all: World.BuildClip ClipMoveCompat), so the platform beside the yard wins; both recordings keep the player on it; 91 recorded collision, mover, water and carried-sprite bugs replay the same with and without it
