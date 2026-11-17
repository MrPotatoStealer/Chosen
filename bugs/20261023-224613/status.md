# 20261023-224613: why is there two keys here?

Status: fixed
Regression: room-over-room, sprites
Regression setup: nomonsters | 2
Regression check: one skull key and one chest on the walkway

## History

- 2026-10-23 22:46:13 reported in maps/e1m8.map
- 2026-10-23 fixed. Cause: Sprites near an active portal were copied for the linked room whichever side of the link they were on: E1M8's walkway floor is linked to the room 32 units over and a unit down, so the skull key (and the chest at the end) standing on the walkway got a copy beside itself, above the plane where the portal clip doesn't reach
- 2026-10-23 Fix: SpriteRenderer copies only sprites beyond the portal's linked plane (in the linked room)
