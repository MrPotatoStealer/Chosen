# 20261023-095006: more strange geo buga when iceblock is on the move

Status: fixed

## History

- 2026-10-23 09:50:06 reported in maps/e2m6.map
- 2026-10-23 fixed. Cause: The ice floe, its water strip and the underwater copy were rigid movers and skins approximating a sector Build reshapes every frame (seams, textures not stretching, holes as it moved).
- 2026-10-23 Fix: They are live sectors now (World.Live): shape, collision and mesh rebuilt from Build's walls as the floe rises and the strip opens, walls stretching their pictures.
