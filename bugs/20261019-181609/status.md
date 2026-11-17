# 20261019-181609: lights should not be active until the sprite itself is active, and destroyed if the sprite is destroyed

Status: fixed
Regression: room-over-room, dynamic-lights

## History

- 2026-10-19 18:16:09 reported in maps/e1m1.map
- 2026-10-19 fixed. Cause: the floor spot was the yard-portal copy of the fire behind the wall (x 80): the copy landed at the wall (x 48) without shadows and lit this room's floor
- 2026-10-19 Fix: copies only light inside their linked room (as 181718); the fire itself was active, it is in the next room
