# 20261016-095646: zombie spawns on top of grave, should be on the ground

Status: fixed

## History

- 2026-10-16 09:56:46 reported in maps/E1M1.bsp
- 2026-10-16 fixed. Cause: GetZRange's upward search (for sprites sunk in the floor) counted the tombstone beside the buried zombie as solid at each height and put the zombie on top of it; rising, it then crossed the ceiling link.
- 2026-10-16 Fix: The upward search leaves out any sprite or mover the probe touches at each height, as Build only takes a sprite as floor when the actor is above it.
