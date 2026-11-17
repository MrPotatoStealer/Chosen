# 20261028-085657: collision on falling teleport you up on the platform, in NBlood you hit the platform side and fall down into the lave. Here you somehow teleport up on the platform instead

Status: fixed
Regression: collision
Regression check: fall through the hole in the floor in 211 beside the turning block: you land in the lava below (210/335), never thrown back up onto the block

## History

- 2026-10-28 08:56:57 reported in maps/e3m5.map
- 2026-10-28 fixed. Cause: Falling through link 30 put the player beside the turning block 213, which fills the room below up to its ceiling; without Blood's per-frame pushmove (playerProcess) getzrange took the block's top for the floor and the player went back up through the link
- 2026-10-28 Fix: World.PlayerProcess: pushmove on Build's sectors every frame before the input, as Blood (damage when it fails or loses the sector)
