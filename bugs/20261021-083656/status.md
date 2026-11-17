# 20261021-083656: look over secondary fire of the staff weapon, it seems to also don't do anythign

Status: fixed

## History

- 2026-10-21 08:36:56 reported in maps/e1m4.map
- 2026-10-21 note: Replay (exact): alt fire at frame 683 drops the life leech (kThingDroppedLifeLeech, 431) at (1573, -1595); by 812 the zombies around it lost 113-675 health from its shots and attacked it (2176 -> 1714), as NBlood's proximity turret (512 range, fires at a dude it picks). Asked the user what they expected.
- 2026-10-21 fixed. Cause: The dropped life leech fired (every 0.3 s at a dude within reach), but each bolt spawns against the leech (16 units out: the leech's clip radius 12 plus the bolt's 4) and MoveMissile's point-blank rule took a box overlapping a sprite at the start as a hit: every bolt burst on the leech. Build's clipmove only stops a move into a sprite's clip box.
- 2026-10-21 Fix: MoveMissile: a missile starting in a sprite's box is a hit only when it moves into it; moving away it traces on without that sprite.
