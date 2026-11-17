# 20261016-100306: can jump up this ledge, there is almost like a ledge grab happening pushing the player up. I guess this is because player is using a capsule collider where original is just a position?

Status: fixed
Regression: collision
Regression check: jumping at the ledge does not pull the player up

## History

- 2026-10-16 10:03:06 reported in maps/E1M1.bsp
- 2026-10-16 fixed. Cause: Jumping at the ledge, the GetZRange probe box reached into the ledge while the origin was below its top; the 'origin inside geometry' upward search then put the player on the ledge. Build only takes a neighbouring floor when the sprite's z is above it.
- 2026-10-16 Fix: When the origin is free and only the box overlaps, the probe narrows until it clears (floor and ceiling); the upward search is kept for origins inside solid.
