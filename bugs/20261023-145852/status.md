# 20261023-145852: fell out of bounds

Status: fixed

## History

- 2026-10-23 14:58:52 reported in maps/e2m6.map
- 2026-10-23 fixed. Cause: The water surface's link plane bobs with the strip's floor and moved (ProcessMotion) before the player did: it rose past the player's origin, who then dropped through it without the link seeing a crossing (above the plane before, below it now), into the void under the surface. Also: a floor search through the link started from where the trace stopped (the live floor a little off the bobbing plane, above the room below) or inside the room's floor tilting up to its ceiling at the edge, so it found no floor (the bottom of the level): the player fell in at the surface and ping-ponged between the two links.
- 2026-10-23 Fix: Link crossings compare with the plane as it was at the last check (Link.PrevPlane). The search through a link continues from the plane and narrows its box when it starts in a solid, as GetZRange's own search does: the player stands on the strip's bottom with the head above the surface. Found with the recording's strict replay (check <id>).
