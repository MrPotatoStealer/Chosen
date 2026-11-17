# 20261023-090601: in NBlood the textures strech to keep the textures in sync and no visible seams when it moves out of the way

Status: fixed

## History

- 2026-10-23 09:06:01 reported in maps/e2m6.map
- 2026-10-23 fixed. Cause: The ice floe, its water strip and the underwater copy were rigid movers and skins approximating a sector Build reshapes every frame (seams, textures not stretching, holes as it moved).
- 2026-10-23 Fix: They are live sectors now (World.Live): shape, collision and mesh rebuilt from Build's walls as the floe rises and the strip opens, walls stretching their pictures.
