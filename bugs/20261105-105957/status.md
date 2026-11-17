# 20261105-105957: fell into other sector again

Status: fixed

## History

- 2026-11-05 10:59:57 reported in maps/test.map
- 2026-11-05 fixed. Cause: Standing on a turning platform's edge from the yard, the platform turned under the player's middle: its walls then face away from the move and are never looked at, so clipmove_compat found no sector it looked at holding the player and searched every sector from the last, taking room 16, which shares the yard's ground at the same heights
- 2026-11-05 Fix: b3d_fixblood: clipmove_compat's fallback searches in Blood's FindSector order (the start sector, its neighbours, then all: World.BuildClip ClipMoveCompat), so the platform beside the yard wins; both recordings keep the player on it; 91 recorded collision, mover, water and carried-sprite bugs replay the same with and without it
